using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Coquena.Downloads;

namespace Coquena.Models;

public sealed record InspectionLimits(int BlockBytes = 1048576, long HeaderBytes = 67108864,
    long UnitBytes = 134217728, long SessionBytes = 268435456, int Requests = 8192,
    int RequestSeconds = 20, int SessionSeconds = 120);
public sealed record InspectionStatistics(long Bytes, long DeclaredBytes, int Requests, int Files, int PeakBufferBytes, TimeSpan Elapsed);
public interface IRemoteGgufInspector
{
    Task<GgufHeader> InspectAsync(RepositoryRevision revision, RepositoryFile file, CancellationToken cancellation);
}

// One invocation, ephemeral buffers and memoization. No home, downloads, GUID or weight files.
public sealed class RemoteGgufInspector : IRemoteGgufInspector, IDisposable
{
    private readonly HttpClient client;
    private readonly InspectionLimits limits;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Dictionary<string, GgufHeader> headers = new(StringComparer.Ordinal);
    private long bytes, unitStart, declaredBytes;
    private int peakBufferBytes;
    private int requests, files;
    public InspectionStatistics Statistics => new(bytes, declaredBytes, requests, files, peakBufferBytes, clock.Elapsed);
    internal void BeginUnit() => unitStart = bytes;
    public RemoteGgufInspector(HttpMessageHandler? handler = null, InspectionLimits? limits = null)
    {
        this.limits = limits ?? new();
        if (this.limits.BlockBytes is < 1 or > 2097152 || this.limits.HeaderBytes < 1 ||
            this.limits.SessionBytes < 1 || this.limits.UnitBytes < 1 || this.limits.Requests < 1 ||
            this.limits.RequestSeconds < 1 || this.limits.SessionSeconds < 1) throw new ArgumentException("Presupuesto inválido.");
        client = new(new HuggingFaceTransport.Redirects(handler, () =>
        {
            if (++requests > this.limits.Requests) throw new ModelSourceException("inspection-limit", "Límite de peticiones agotado; exploración parcial.");
        })) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public async Task<GgufHeader> InspectAsync(RepositoryRevision revision, RepositoryFile file, CancellationToken cancellation)
    {
        HuggingFaceStore.ValidateRepository(revision.Repository);
        HuggingFaceStore.ValidatePath(file.Path);
        if (!HuggingFaceStore.Hex(revision.Commit, 40) || file.Size < 24)
            throw new ModelSourceException("invalid-metadata", "Commit/tamaño insuficiente para cabecera.");
        var key = revision.Repository + "/" + revision.Commit + "/" + file.Path;
        cancellation.ThrowIfCancellationRequested();
        if (headers.TryGetValue(key, out var cached)) return cached;
        files++;
        var remaining = TimeSpan.FromSeconds(limits.SessionSeconds) - clock.Elapsed;
        if (remaining <= TimeSpan.Zero) throw new ModelSourceException("inspection-limit", "Tiempo de exploración agotado; exploración parcial.");
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        session.CancelAfter(remaining);
        using var stream = new RangeStream(this, revision, file);
        try
        {
            var header = await GgufInspection.ReadHeaderAsync(stream, session.Token);
            headers.Add(key, header);
            return header;
        }
        catch (GgufException e) { throw new ModelSourceException("gguf-" + e.Code, e.Message); }
        catch (DownloadException e) { throw new ModelSourceException(e.Code, e.Message); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new ModelSourceException("inspection-timeout", "Tiempo de inspección agotado; no implica ausencia de variante."); }
        catch (Exception e) when (e is IOException or HttpRequestException)
        { throw new ModelSourceException("inspection-network", "Lectura remota interrumpida."); }
    }
    public void Dispose() => client.Dispose();

    private sealed class RangeStream(RemoteGgufInspector owner, RepositoryRevision revision, RepositoryFile file) : Stream
    {
        private byte[] block = [];
        private int index;
        private long offset;
        private string? validator;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (buffer.Length == 0) return 0;
            if (index == block.Length)
            {
                if (offset >= file.Size) return 0;
                block = await Fetch(cancellation); index = 0;
            }
            var n = Math.Min(buffer.Length, block.Length - index);
            block.AsMemory(index, n).CopyTo(buffer); index += n; return n;
        }
        private async Task<byte[]> Fetch(CancellationToken cancellation)
        {
            var size = (int)Math.Min(Math.Min(offset == 0 ? Math.Min(65536, owner.limits.BlockBytes) : owner.limits.BlockBytes, file.Size - offset), owner.limits.HeaderBytes - offset);
            if (size <= 0) throw new ModelSourceException("inspection-limit", "Presupuesto de cabecera agotado.");
            if (offset > owner.limits.HeaderBytes - size || owner.bytes > owner.limits.SessionBytes - size - 1 ||
                owner.bytes - owner.unitStart > owner.limits.UnitBytes - size - 1)
                throw new ModelSourceException("inspection-limit", "Presupuesto de bytes agotado; exploración parcial.");
            var end = offset + size - 1;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (owner.bytes > owner.limits.SessionBytes - size - 1 || owner.bytes - owner.unitStart > owner.limits.UnitBytes - size - 1)
                    throw new ModelSourceException("inspection-limit", "Presupuesto de bytes agotado.");
                using var timed = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timed.CancelAfter(TimeSpan.FromSeconds(owner.limits.RequestSeconds));
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://huggingface.co/{revision.Repository}/resolve/{revision.Commit}/{string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString))}");
                request.Headers.Range = new RangeHeaderValue(offset, end);
                request.Headers.AcceptEncoding.ParseAdd("identity");
                using var response = await owner.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timed.Token);
                if (response.Content.Headers.ContentLength is { } contentLength)
                    owner.declaredBytes += Math.Min(contentLength, long.MaxValue - owner.declaredBytes);
                if ((int)response.StatusCode is 429 or 500 or 502 or 503 or 504)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromMilliseconds(200));
                    if (attempt == 2 || wait > TimeSpan.FromSeconds(5))
                        throw new ModelSourceException(response.StatusCode == HttpStatusCode.TooManyRequests ? "remote-rate-limit" : "inspection-http", "Espera/reintentos excedidos; reintentar más tarde.");
                    // Close without draining error bodies. The next request still consumes the session budget.
                    response.Dispose();
                    await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, cancellation); continue;
                }
                if (response.StatusCode != HttpStatusCode.PartialContent)
                    throw new ModelSourceException(response.StatusCode == HttpStatusCode.OK ? "range-unsupported" : "inspection-http", "Se requiere 206 para inspección acotada; respuesta cerrada sin drenar.");
                var range = response.Content.Headers.ContentRange;
                if (range?.Unit != "bytes" || range.From != offset || range.To != end || range.Length != file.Size ||
                    response.Content.Headers.ContentLength is { } declared && declared != size || response.Content.Headers.ContentEncoding.Count != 0)
                    throw new ModelSourceException("invalid-range", "Rango, total, longitud o codificación incoherente.");
                var etag = response.Headers.ETag?.ToString();
                if (file.LfsSha256 is { } sha && response.Headers.TryGetValues("X-Linked-Etag", out var linked) &&
                    linked.Any(value => HuggingFaceStore.Hex(value.Trim('"'), 64) && !value.Trim('"').Equals(sha, StringComparison.OrdinalIgnoreCase)))
                    throw new ModelSourceException("content-changed", "Identidad LFS de respuesta no corresponde al inventario.");
                if (validator is not null && etag is not null && validator != etag)
                    throw new ModelSourceException("content-changed", "Identidad cambió entre rangos.");
                validator ??= etag;
                var result = new byte[size];
                owner.peakBufferBytes = Math.Max(owner.peakBufferBytes, size + block.Length + 65537);
                using var body = await response.Content.ReadAsStreamAsync(timed.Token);
                var read = 0;
                while (read < size)
                {
                    var n = await body.ReadAsync(result.AsMemory(read), timed.Token);
                    owner.bytes += n;
                    if (n == 0) throw new ModelSourceException("invalid-range", "Cuerpo de rango truncado.");
                    read += n;
                }
                var extra = new byte[1]; var excessive = await body.ReadAsync(extra, timed.Token); owner.bytes += excessive;
                if (excessive != 0) throw new ModelSourceException("invalid-range", "Cuerpo excede rango solicitado.");
                offset += size; return result;
            }
            throw new ModelSourceException("inspection-http", "Reintentos agotados.");
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => file.Size;
        public override long Position { get => offset - block.Length + index; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Usar lector async.");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
