using System.Net;
using System.Text.Json;

namespace Coquena.Backends;

public sealed class OfficialBackendSource(HttpClient client)
{
    private const string Api = "https://api.github.com/repos/ggml-org/llama.cpp/releases";
    public const int MaxBuildProbes = 32;
    public const int MaxMetadataBytes = 4 * 1024 * 1024;

    public async Task<OfficialRelease> PinnedAsync(string release, CancellationToken cancellation)
    {
        _ = BackendCatalog.BuildNumber(release);
        var value = await ReadAsync($"/tags/{release}", cancellation)
            ?? throw new BackendException("release-not-found", "Release fijado no disponible.");
        if (value.Tag != release) throw new BackendException("invalid-metadata", "Release distinto del fijado.");
        return value;
    }

    // Official bNNNN builds are prereleases, so /latest is not their discovery API.
    // Establish the newest build from the recent published-build listing, then walk
    // numeric tags (including gaps) rather than mistaking latest for a stable marker.
    public async Task<BackendResolution> ResolveAsync(HardwareContext hardware, string? family,
        string release, CancellationToken cancellation = default)
    {
        if (!BackendCatalog.ValidFamily(family)) throw new BackendException("invalid-family", "Familias: cpu, vulkan, cuda@<mayor>.");
        if (release != "latest") _ = BackendCatalog.BuildNumber(release);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        budget.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var first = release == "latest" ? await LatestBuildAsync(budget.Token)
                : await ReadAsync($"/tags/{release}", budget.Token)
                ?? throw new BackendException("release-not-found", "Release oficial no encontrado.");
            if (release != "latest" && first.Tag != release)
                throw new BackendException("invalid-metadata", "El release recibido no coincide con el solicitado.");
            List<OfficialRelease> releases = [first];
            var result = BackendCatalog.Resolve(releases, hardware, family, release);
            if (release != "latest" || result.Selected is not null) return result;
            var number = BackendCatalog.BuildNumber(first.Tag);
            for (var probe = 1; probe < MaxBuildProbes && number - probe > 0; probe++)
            {
                var tag = $"b{number - probe}";
                var previous = await ReadAsync($"/tags/{tag}", budget.Token);
                if (previous is null) continue; // A numeric tag need not have a published release.
                if (previous.Tag != tag) throw new BackendException("invalid-metadata", "El release recibido no coincide con el solicitado.");
                releases.Add(previous);
                result = BackendCatalog.Resolve(releases, hardware, family);
                if (result.Selected is not null) return result;
            }
            if (number <= MaxBuildProbes) return result;
            throw new BackendException("discovery-limit", $"Sin selección en {MaxBuildProbes} builds; búsqueda incompleta. Consulte un release explícito; no se afirma que no existan candidatos anteriores.");
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        { throw new BackendException("remote-unavailable", "No se pudo consultar GitHub; no se resolvió ninguna selección."); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new BackendException("remote-timeout", "Se agotó el tiempo de consulta; selección no resuelta."); }
    }

    private async Task<OfficialRelease> LatestBuildAsync(CancellationToken cancellation)
    {
        for (var page = 1; page <= 8; page++)
        {
            var bytes = await ReadBytesAsync($"?per_page=30&page={page}", cancellation)
                ?? throw new BackendException("release-not-found", "Listado oficial no encontrado.");
            try
            {
                using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
                if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 30) throw new JsonException();
                OfficialRelease? newest = null;
                var tags = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    Unique(item);
                    var tag = item.GetProperty("tag_name").GetString() ?? throw new JsonException();
                    if (tag.Length < 2 || tag[0] != 'b' || tag.AsSpan(1).IndexOfAnyExceptInRange('0', '9') >= 0 || item.GetProperty("draft").GetBoolean()) continue;
                    if (!tags.Add(tag)) throw new JsonException();
                    var candidate = Parse(System.Text.Encoding.UTF8.GetBytes(item.GetRawText()));
                    if (newest is null || BackendCatalog.BuildNumber(candidate.Tag) > BackendCatalog.BuildNumber(newest.Tag)) newest = candidate;
                }
                if (newest is not null) return newest;
                if (document.RootElement.GetArrayLength() == 0)
                    throw new BackendException("release-not-found", "No hay builds oficiales bNNNN publicados.");
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
            { throw new BackendException("invalid-metadata", "Listado oficial inválido."); }
        }
        throw new BackendException("discovery-limit", "Sin builds bNNNN en las páginas consultadas; búsqueda incompleta.");
    }

    private async Task<OfficialRelease?> ReadAsync(string endpoint, CancellationToken cancellation)
    {
        var bytes = await ReadBytesAsync(endpoint, cancellation);
        return bytes is null ? null : Parse(bytes);
    }

    private async Task<byte[]?> ReadBytesAsync(string endpoint, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Api + endpoint);
        request.Headers.UserAgent.ParseAdd("Coquena/0.1");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new BackendException("remote-rate-limit", "GitHub rechazó la consulta por permisos/límite; reintente más tarde.");
        if (!response.IsSuccessStatusCode)
            throw new BackendException("remote-status", $"GitHub devolvió HTTP {(int)response.StatusCode}; selección no resuelta.");
        if (response.Content.Headers.ContentLength > MaxMetadataBytes)
            throw new BackendException("metadata-limit", "Metadatos demasiado grandes.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var bytes = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) != 0)
        {
            if (bytes.Length + count > MaxMetadataBytes) throw new BackendException("metadata-limit", "Metadatos demasiado grandes.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }

    public static OfficialRelease Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            Unique(root);
            var tag = root.GetProperty("tag_name").GetString() ?? throw new JsonException();
            _ = BackendCatalog.BuildNumber(tag);
            if (root.GetProperty("draft").GetBoolean()) throw new JsonException();
            var array = root.GetProperty("assets");
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 512) throw new JsonException();
            List<ReleaseAsset> assets = [];
            foreach (var value in array.EnumerateArray())
            {
                Unique(value);
                var name = value.GetProperty("name").GetString() ?? throw new JsonException();
                var size = value.GetProperty("size").GetInt64();
                var url = value.GetProperty("browser_download_url").GetString() ?? throw new JsonException();
                if (name.Length is 0 or > 256 || name.Any(c => char.IsControl(c) || c is '/' or '\\') || size < 0 ||
                    url != $"https://github.com/ggml-org/llama.cpp/releases/download/{tag}/{Uri.EscapeDataString(name)}") throw new JsonException();
                if (value.GetProperty("state").GetString() != "uploaded") continue;
                assets.Add(new(name, size, url));
            }
            if (assets.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() != assets.Count) throw new JsonException();
            return new(tag, assets.ToArray());
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new BackendException("invalid-metadata", "Metadatos oficiales inválidos o ambiguos."); }
    }

    private static void Unique(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) if (!names.Add(property.Name)) throw new JsonException();
    }
}
