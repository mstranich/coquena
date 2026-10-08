using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Coquena.Models;

public sealed class GgufException(string code, string message) : Exception(message)
{ public string Code { get; } = code; }
public sealed record GgufHeader(uint Version, ulong TensorCount, int? SplitIndex, int? SplitCount,
    long? SplitTensorCount, IReadOnlyDictionary<string, string> Hints, uint? FileTypeId = null);
public sealed record GgufPart(string Path, GgufHeader? Header, string? Diagnostic);
public sealed record GgufUnit(string Status, string[] Expected, string[] Missing, GgufPart[] Parts, string[] Diagnostics);

public static class GgufInspection
{
    // Only the metadata/header area is inspected: never allocate or read tensor weights.
    public const long MaxHeaderBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static GgufHeader ReadHeader(Stream stream, CancellationToken cancellation = default) => ReadHeaderAsync(stream, cancellation).GetAwaiter().GetResult();

    public static async Task<GgufHeader> ReadHeaderAsync(Stream stream, CancellationToken cancellation = default)
    {
        var reader = new HeaderReader(stream, cancellation);
        if (!(await reader.Read(4)).Span.SequenceEqual("GGUF"u8)) throw new GgufException("invalid", "Magic GGUF ausente.");
        var version = await reader.Number(4);
        if (version is not (2 or 3)) throw new GgufException("unsupported", "Sólo GGUF v2/v3 little-endian; no declarar inválida una versión/endian no soportada.");
        var tensors = await reader.Number(8);
        var count = await reader.Number(8);
        if (count > 1000000) throw new GgufException("limit", "Demasiadas claves GGUF para inspección acotada.");
        HashSet<string> keys = new(StringComparer.Ordinal);
        Dictionary<string, string> hints = new(StringComparer.Ordinal);
        int? index = null, total = null; long? tensorTotal = null; uint? fileType = null;
        for (ulong i = 0; i < count; i++)
        {
            var key = await reader.Text(1024);
            if (key.Length == 0 || !keys.Add(key)) throw new GgufException("invalid", "Clave GGUF vacía/duplicada.");
            var type = await reader.Number(4);
            if (key is "split.no" or "split.count")
            {
                if (type != 2) throw new GgufException("invalid", "split.no/count debe ser UINT16.");
                var value = (int)await reader.Number(2);
                if (key == "split.no") index = value; else total = value;
            }
            else if (key == "general.file_type")
            {
                if (type != 4) throw new GgufException("invalid", "general.file_type debe ser UINT32.");
                fileType = (uint)await reader.Number(4);
            }
            else if (key == "split.tensors.count")
            {
                if (type != 5) throw new GgufException("invalid", "split.tensors.count debe ser INT32.");
                var value = unchecked((int)await reader.Number(4));
                if (value < 0) throw new GgufException("invalid", "Cantidad global de tensores negativa.");
                tensorTotal = value;
            }
            else if (key is "general.architecture" or "general.name" or "general.basename" or "general.uuid")
            {
                if (type != 8) throw new GgufException("invalid", "Pista general GGUF debe ser STRING.");
                hints.Add(key, await reader.Text(65536));
            }
            else await reader.SkipValue(type, 0);
        }
        // gguf-split --merge preserves split metadata with split.count=0 to disable splitting.
        if ((index.HasValue && !total.HasValue) ||
            (index.HasValue && total is > 0 && index.Value >= total.Value))
            throw new GgufException("invalid", "Metadatos de división contradictorios.");
        return new((uint)version, tensors, index, total, tensorTotal, hints, fileType);
    }

    // First shard only; locate exact siblings in this directory, never recursively or by similarity.
    public static GgufUnit Inspect(string firstPath, CancellationToken cancellation = default)
    {
        var first = Path.GetFullPath(firstPath);
        var name = Path.GetFileName(first);
        var match = Regex.Match(name, "^(.*)-([0-9]{5})-of-([0-9]{5})\\.gguf$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        string[] expected;
        if (match.Success)
        {
            var n = int.Parse(match.Groups[3].Value);
            if (int.Parse(match.Groups[2].Value) != 1 || n < 1 || n > 65535)
                return new("invalid", [first], [], [], ["Indicar el primer shard de un total 1..65535."]);
            var dir = Path.GetDirectoryName(first)!;
            expected = Enumerable.Range(1, n).Select(i => Path.Combine(dir, $"{match.Groups[1].Value}-{i:00000}-of-{n:00000}.gguf")).ToArray();
        }
        else expected = [first];
        return InspectPaths(expected, cancellation);
    }

    // Explicit complete selection (e.g. 009); duplicates and mixed units are diagnosed, not merged.
    public static GgufUnit InspectPaths(IReadOnlyList<string> paths, CancellationToken cancellation = default,
        Func<string, string>? resolvePath = null)
    {
        if (paths.Count == 0 || paths.Count > 65535) throw new ArgumentException("Unidad vacía o excesiva.");
        var expected = paths.Select(Path.GetFullPath).ToArray();
        List<string> missing = []; List<string> diagnostics = []; List<GgufPart> parts = [];
        var status = "complete";
        if (expected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Length)
        { status = "invalid"; diagnostics.Add("Rutas duplicadas en la unidad."); }
        // Expand a conventional partial selection before disk access, retaining every expected
        // sibling. Missing input entries are not evidence of invalid bytes or an invented unit.
        var names = expected.Select(path => (Path: path, Match: Regex.Match(Path.GetFileName(path),
            "^(.*)-([0-9]{5})-of-([0-9]{5})\\.gguf$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))).ToArray();
        if (names.All(p => p.Match.Success))
        {
            var first = names[0];
            var prefix = Path.Combine(Path.GetDirectoryName(first.Path)!, first.Match.Groups[1].Value);
            var total = int.Parse(first.Match.Groups[3].Value);
            if (total < 1 || total > 65535 || names.Any(p =>
                Path.Combine(Path.GetDirectoryName(p.Path)!, p.Match.Groups[1].Value) != prefix ||
                int.Parse(p.Match.Groups[3].Value) != total || int.Parse(p.Match.Groups[2].Value) < 1 ||
                int.Parse(p.Match.Groups[2].Value) > total))
                Invalid("Selección de shards contradictoria.");
            else if (status != "invalid")
                expected = Enumerable.Range(1, total).Select(i => $"{prefix}-{i:00000}-of-{total:00000}.gguf").ToArray();
        }
        else if (names.Any(p => p.Match.Success)) Invalid("Selección mezcla archivo único y shards.");
        string? group = null; int? namedTotal = null;
        HashSet<int> indices = [];
        foreach (var path in expected)
        {
            cancellation.ThrowIfCancellationRequested();
            GgufHeader? header = null;
            try
            {
                using var stream = new FileStream(resolvePath?.Invoke(path) ?? path, FileMode.Open, FileAccess.Read, FileShare.Read);
                header = ReadHeader(stream, cancellation);
                parts.Add(new(path, header, null));
            }
            catch (FileNotFoundException) { missing.Add(path); parts.Add(new(path, null, "missing")); continue; }
            catch (DirectoryNotFoundException) { missing.Add(path); parts.Add(new(path, null, "missing")); continue; }
            catch (GgufException e)
            {
                if (e.Code == "invalid" || status == "invalid") status = "invalid";
                else if (status == "complete") status = "unsupported";
                diagnostics.Add($"{path}: {e.Message}"); parts.Add(new(path, null, e.Code)); continue;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { if (status == "complete") status = "read-error"; diagnostics.Add($"{path}: {e.Message}"); parts.Add(new(path, null, "read-error")); continue; }
            var m = Regex.Match(Path.GetFileName(path), "^(.*)-([0-9]{5})-of-([0-9]{5})\\.gguf$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var key = Path.Combine(Path.GetDirectoryName(path)!, m.Groups[1].Value);
                var i = int.Parse(m.Groups[2].Value); var n = int.Parse(m.Groups[3].Value);
                group ??= key; namedTotal ??= n;
                if (key != group || n != namedTotal || n < 1 || i < 1 || i > n || !indices.Add(i) ||
                    header.SplitIndex != i - 1 || header.SplitCount != n || header.SplitTensorCount is null)
                    Invalid("Nombres y metadatos split no forman una única unidad coherente.");
            }
            else if (expected.Length > 1 || header.SplitCount > 1)
            { if (status == "complete") status = "unsupported"; diagnostics.Add("División no convencional: no se inventa conjunto ni orden."); }
        }
        var headers = parts.Where(p => p.Header is not null).Select(p => p.Header!).ToArray();
        if (headers.Select(h => h.Version).Distinct().Count() > 1) Invalid("Versiones GGUF contradictorias.");
        foreach (var key in new[] { "general.architecture", "general.name", "general.basename", "general.uuid" })
            if (headers.Where(h => h.Hints.ContainsKey(key)).Select(h => h.Hints[key]).Distinct(StringComparer.Ordinal).Count() > 1)
                Invalid($"Metadato compartido contradictorio: {key}.");
        if (headers.Where(h => h.SplitTensorCount.HasValue).Select(h => h.SplitTensorCount).Distinct().Count() > 1)
            Invalid("Cantidad global de tensores contradictoria.");
        if (missing.Count == 0 && headers.Length == expected.Length && namedTotal.HasValue)
        {
            if (namedTotal != expected.Length || !Enumerable.Range(1, expected.Length).All(indices.Contains))
                Invalid("Selección omite partes esperadas o repite índices.");
            ulong sum = 0;
            foreach (var h in headers)
            {
                if (ulong.MaxValue - sum < h.TensorCount) { Invalid("Overflow de cantidad de tensores."); break; }
                sum += h.TensorCount;
            }
            if (headers[0].SplitTensorCount is long n && sum != (ulong)n) Invalid("Suma de tensores no coincide con split.tensors.count.");
        }
        if (status == "complete" && missing.Count > 0) status = "incomplete";
        return new(status, expected, missing.ToArray(), parts.ToArray(), diagnostics.ToArray());
        void Invalid(string message) { status = "invalid"; diagnostics.Add(message); }
    }

    private sealed class HeaderReader(Stream stream, CancellationToken cancellation)
    {
        private long consumed;
        private readonly byte[] scratch = new byte[65536];
        public async ValueTask<ReadOnlyMemory<byte>> Read(int n)
        {
            cancellation.ThrowIfCancellationRequested();
            if (n < 0 || consumed > MaxHeaderBytes - n) throw new GgufException("limit", "Encabezado supera 64 MiB.");
            try { await stream.ReadExactlyAsync(scratch.AsMemory(0, n), cancellation); }
            catch (EndOfStreamException) { throw new GgufException("invalid", "Encabezado GGUF truncado."); }
            consumed += n; return scratch.AsMemory(0, n);
        }
        public async ValueTask<ulong> Number(int width) => width switch
        { 1 => (await Read(1)).Span[0], 2 => BinaryPrimitives.ReadUInt16LittleEndian((await Read(2)).Span),
            4 => BinaryPrimitives.ReadUInt32LittleEndian((await Read(4)).Span), 8 => BinaryPrimitives.ReadUInt64LittleEndian((await Read(8)).Span), _ => throw new ArgumentException() };
        public async ValueTask<string> Text(int max)
        {
            var n = await Number(8);
            if (n > (ulong)max) throw new GgufException("limit", "Texto GGUF excede límite de inspección.");
            try { return Utf8.GetString((await Read((int)n)).Span); }
            catch (DecoderFallbackException) { throw new GgufException("invalid", "Texto GGUF no es UTF-8 válido."); }
        }
        private async ValueTask Skip(ulong n)
        {
            if (n > (ulong)(MaxHeaderBytes - consumed)) throw new GgufException("limit", "Encabezado supera 64 MiB.");
            while (n > 0) { var size = (int)Math.Min(n, (ulong)scratch.Length); await Read(size); n -= (ulong)size; }
        }
        public async ValueTask SkipValue(ulong type, int depth)
        {
            if (depth > 8) throw new GgufException("limit", "Array GGUF demasiado anidado.");
            var width = type switch { 0 or 1 => 1, 2 or 3 => 2, 4 or 5 or 6 => 4, 10 or 11 or 12 => 8, _ => 0 };
            if (width != 0) { await Skip((ulong)width); return; }
            if (type == 7) { if (await Number(1) > 1) throw new GgufException("invalid", "Boolean GGUF inválido."); return; }
            if (type == 8) { await Skip(await Number(8)); return; }
            if (type != 9) throw new GgufException("unsupported", "Tipo de metadato GGUF no soportado.");
            var element = await Number(4); var count = await Number(8);
            if (element > 12) throw new GgufException("unsupported", "Tipo de array GGUF no soportado.");
            if (count > 1000000) throw new GgufException("limit", "Array GGUF excesivo.");
            for (ulong i = 0; i < count; i++) await SkipValue(element, depth + 1);
        }
    }
}
