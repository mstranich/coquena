using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coquena.Contracts;

namespace Coquena.Models;

public sealed class ModelSourceException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record RepositoryRevision(string Repository, string RequestedRevision, string Commit);
// Preserve different identity domains; a Git pointer or Xet hash is not a weights SHA-256.
public sealed record RepositoryFile(string Path, long Size, string GitOid,
    string? LfsSha256, string? XetHash)
{
    public RemoteFile ToRemoteFile() => new(Path, Size, LfsSha256 is not null ?
        new("sha256", "file-bytes", LfsSha256) : XetHash is not null ?
        new("xet", "xet-file", XetHash) : new("sha1", "git-blob", GitOid));
}
public sealed record RepositoryInventory(RepositoryRevision Revision, RepositoryFile[] Files);

/// <summary>Public Hub repository access. No clone, history, credentials or disk state.</summary>
public sealed class HuggingFaceStore(HttpClient client)
{
    public const int MaxMetadataBytes = 4 * 1024 * 1024;
    public const int MaxReadmeBytes = 512 * 1024;
    public const int MaxPages = 128;
    public const int MaxEntries = 100_000;
    private const string Host = "https://huggingface.co";

    public static HttpClient CreateClient() => new(new HttpClientHandler
    { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None })
    { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<RepositoryRevision> ResolveAsync(string repository, string revision = "main",
        CancellationToken cancellation = default)
    {
        ValidateRepository(repository);
        if (string.IsNullOrWhiteSpace(revision) || revision.Length > 256 || revision.Any(char.IsControl))
            throw Error("invalid-revision", "Referencia remota inválida.");
        var (bytes, _) = await ReadAsync(new($"{Host}/api/models/{repository}/revision/{Uri.EscapeDataString(revision)}"),
            MaxMetadataBytes, cancellation);
        try
        {
            using var json = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
            var root = json.RootElement;
            Unique(root);
            var id = root.GetProperty("id").GetString() ?? throw new JsonException();
            ValidateRepository(id);
            var commit = root.GetProperty("sha").GetString() ?? throw new JsonException();
            ValidateCommit(commit);
            return new(id, revision, commit);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw Error("invalid-metadata", "Respuesta de revisión inválida."); }
    }

    public async Task<RepositoryInventory> InventoryAsync(RepositoryRevision revision,
        CancellationToken cancellation = default)
    {
        Validate(revision);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        budget.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            return await ReadInventoryAsync(revision, budget.Token);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw Error("remote-timeout", "Tiempo de inventario HF agotado; no se declara completo."); }
    }

    private async Task<RepositoryInventory> ReadInventoryAsync(RepositoryRevision revision, CancellationToken cancellation)
    {
        var prefix = $"/api/models/{revision.Repository}/tree/{revision.Commit}";
        Uri? next = new($"{Host}{prefix}?recursive=true&expand=false&limit=1000");
        HashSet<string> visited = new(StringComparer.Ordinal);
        HashSet<string> paths = new(StringComparer.Ordinal);
        List<RepositoryFile> files = [];
        var entries = 0;
        for (var page = 0; next is not null; page++)
        {
            if (page >= MaxPages || !visited.Add(next.AbsoluteUri))
                throw Error("inventory-incomplete", "Inventario incompleto: límite o ciclo de paginación.");
            var (bytes, link) = await ReadAsync(next, MaxMetadataBytes, cancellation);
            try
            {
                using var json = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
                if (json.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException();
                foreach (var item in json.RootElement.EnumerateArray())
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++entries > MaxEntries) throw Error("inventory-incomplete", "Inventario excede el límite de entradas.");
                    Unique(item);
                    var path = item.GetProperty("path").GetString() ?? throw new JsonException();
                    ValidatePath(path);
                    if (!paths.Add(path)) throw new JsonException();
                    var type = item.GetProperty("type").GetString();
                    if (type == "directory") continue;
                    if (type != "file") throw new JsonException();
                    var size = item.GetProperty("size").GetInt64();
                    var oid = item.GetProperty("oid").GetString() ?? throw new JsonException();
                    if (size < 0 || !Hex(oid, 40)) throw new JsonException();
                    string? sha = null;
                    if (item.TryGetProperty("lfs", out var lfs) && lfs.ValueKind != JsonValueKind.Null)
                    {
                        Unique(lfs);
                        sha = lfs.GetProperty("oid").GetString();
                        if (!Hex(sha, 64) || lfs.GetProperty("size").GetInt64() != size) throw new JsonException();
                    }
                    var xet = item.TryGetProperty("xetHash", out var x) ? x.GetString() : null;
                    if (xet is not null && !Hex(xet, 64)) throw new JsonException();
                    files.Add(new(path, size, oid, sha, xet));
                }
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            { throw Error("invalid-metadata", "Inventario remoto inválido; no se declara ausencia de archivos."); }
            next = Next(link, prefix);
        }
        return new(revision, files.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray());
    }

    public RepositoryFile File(RepositoryInventory inventory, string path)
    {
        ValidatePath(path);
        return inventory.Files.SingleOrDefault(f => f.Path == path)
            ?? throw Error("file-not-found", "Archivo ausente del inventario completo fijado.");
    }

    // Stable address, not a signed redirect. Future transfer consumer owns byte streaming.
    public Uri FileAddress(RepositoryRevision revision, string path)
    {
        Validate(revision); ValidatePath(path);
        return new($"{Host}/{revision.Repository}/resolve/{revision.Commit}/{EscapePath(path)}");
    }

    public async Task<string> ReadmeAsync(RepositoryRevision revision, CancellationToken cancellation = default)
    {
        Validate(revision);
        var (bytes, _) = await ReadAsync(new($"{Host}/{revision.Repository}/raw/{revision.Commit}/README.md"),
            MaxReadmeBytes, cancellation);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw Error("invalid-readme", "README no es UTF-8 válido."); }
    }

    private async Task<(byte[] Bytes, string? Link)> ReadAsync(Uri uri, int limit, CancellationToken cancellation)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            // At most three attempts. Never wait less than server guidance; long waits are reported.
            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("Coquena/0.1");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
                if (response.RequestMessage?.RequestUri is { } final && final != uri)
                    throw Error("unexpected-redirect", "El cliente no debe seguir redirecciones automáticamente.");
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or
                    HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError)
                {
                    var wait = response.Headers.RetryAfter?.Delta ??
                        (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(attempt + 1));
                    if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.TryGetValues("RateLimit", out var rates))
                    {
                        var resets = Regex.Matches(string.Join(",", rates), "(?:^|;)\\s*t=([0-9]+)(?=;|,|$)", RegexOptions.CultureInvariant);
                        if (resets.Count == 0) throw Error("remote-rate-limit", "RateLimit no reconocido; no se reintenta antes del reset.");
                        foreach (Match reset in resets)
                        {
                            if (!int.TryParse(reset.Groups[1].Value, out var seconds))
                                throw Error("remote-rate-limit", "Espera RateLimit fuera de límites; reintente más tarde.");
                            if (TimeSpan.FromSeconds(seconds) > wait) wait = TimeSpan.FromSeconds(seconds);
                        }
                    }
                    if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
                    if (attempt >= 2 || wait > TimeSpan.FromSeconds(5))
                        throw Error(response.StatusCode == HttpStatusCode.TooManyRequests ? "remote-rate-limit" : "remote-unavailable",
                            $"Consulta no completada; reintente más tarde (espera indicada: {Math.Ceiling(wait.TotalSeconds)} s).");
                    await Task.Delay(wait, budget.Token); continue;
                }
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var code = response.Headers.TryGetValues("X-Error-Code", out var values) ? values.FirstOrDefault() : null;
                    throw Error(code == "RevisionNotFound" ? "revision-not-found" :
                        code == "EntryNotFound" ? "file-not-found" : "repository-unavailable",
                        "Repositorio, revisión o archivo no accesible; no se presume borrado remoto.");
                }
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw Error("remote-access", "Sólo repositorios públicos sin autenticación; acceso rechazado.");
                if (!response.IsSuccessStatusCode)
                    throw Error("remote-status", $"Consulta HF fallida: HTTP {(int)response.StatusCode}.");
                if (response.Content.Headers.ContentLength > limit) throw Error("metadata-limit", "Respuesta excede el límite de metadatos.");
                await using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
                using var buffer = new MemoryStream();
                var block = new byte[16 * 1024];
                int read;
                while ((read = await stream.ReadAsync(block, budget.Token)) > 0)
                {
                    if (buffer.Length + read > limit) throw Error("metadata-limit", "Respuesta excede el límite de metadatos.");
                    buffer.Write(block, 0, read);
                }
                var link = response.Headers.TryGetValues("Link", out var links) ? string.Join(",", links) : null;
                return (buffer.ToArray(), link);
            }
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        { throw Error("remote-unavailable", "No se pudo leer Hugging Face."); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw Error("remote-timeout", "Tiempo de consulta HF agotado."); }
    }

    private static Uri? Next(string? link, string prefix)
    {
        if (link is null) return null;
        Uri? next = null;
        foreach (var part in link.Split(','))
        {
            var match = Regex.Match(part.Trim(), "^<([^>]+)>;\\s*rel=\"([^\"]+)\"$", RegexOptions.CultureInvariant);
            if (!match.Success) throw Error("invalid-pagination", "Encabezado de paginación no reconocido.");
            if (match.Groups[2].Value != "next") continue;
            if (next is not null || !Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out next) ||
                next.Scheme != "https" || next.Host != "huggingface.co" || !next.IsDefaultPort ||
                next.UserInfo.Length != 0 || next.Fragment.Length != 0 || next.AbsolutePath != prefix)
                throw Error("invalid-pagination", "Paginación fuera del repositorio/commit fijado.");
            var query = next.Query.TrimStart('?').Split('&');
            if (!query.Contains("recursive=true") || !query.Contains("expand=false") ||
                query.Select(q => q.Split('=')[0]).Distinct(StringComparer.Ordinal).Count() != query.Length ||
                query.Any(q => q.Split('=')[0] is not ("recursive" or "expand" or "limit" or "cursor")))
                throw Error("invalid-pagination", "Paginación cambia la forma del inventario.");
        }
        return next;
    }

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    internal static bool Hex(string? value, int length) => value is not null && value.Length == length && value.All(Uri.IsHexDigit);
    public static void ValidateRepository(string repository)
    {
        if (!Regex.IsMatch(repository, "^[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant) || repository.Length > 256)
            throw Error("invalid-repository", "Se requiere usuario/repositorio público.");
    }
    public static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.Contains('\\') || path.Any(char.IsControl) ||
            path.Split('/').Any(s => s is "" or "." or ".." || s.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 || s.EndsWith('.') || s.EndsWith(' ') ||
                Regex.IsMatch(s.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw Error("unsafe-path", "Ruta remota insegura; no se selecciona.");
    }
    private static void ValidateCommit(string commit)
    { if (!Hex(commit, 40)) throw Error("invalid-commit", "Commit HF inválido."); }
    private static void Validate(RepositoryRevision revision)
    { ValidateRepository(revision.Repository); ValidateCommit(revision.Commit); }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count())
            throw new JsonException();
    }
    private static ModelSourceException Error(string code, string message) => new(code, message);
}
