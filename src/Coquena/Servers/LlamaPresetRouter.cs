using System.Text.Json.Nodes;

namespace Coquena.Servers;

public sealed class LlamaPresetRouter(HttpClient client, Uri endpoint) : IPresetRouter
{
    private Uri Url(string path) => new(endpoint, path);
    public async Task<JsonArray> ObserveAsync(CancellationToken cancellation)
    {
        using var response = await client.GetAsync(Url("/models"), cancellation);
        response.EnsureSuccessStatusCode();
        try
        {
            var entries = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation))?["data"]?.AsArray()
                ?? throw new HttpRequestException(Messages.ServerCatalogEmpty.ToString());
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry?["id"]?.GetValue<string>() is not string name || !names.Add(name) ||
                    entry["status"]?["value"]?.GetValue<string>() is null || entry["status"]?["args"] is not JsonArray)
                    throw new HttpRequestException(Messages.ServerCatalogDoesNotAllowEntryAttribution.ToString());
                foreach (var argument in entry["status"]!["args"]!.AsArray()) _ = argument?.GetValue<string>() ?? throw new InvalidOperationException();
                if (entry["status"]!["preset"] is JsonNode preset) _ = preset.GetValue<string>();
                if (entry["status"]!["failed"] is JsonNode failed) _ = failed.GetValue<bool>();
            }
            return entries;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
        { throw new HttpRequestException(Messages.ServerReceivedCatalogIsInvalid.ToString(), e); }
    }
    public async Task<string?> ActivityAsync(string[] names, CancellationToken cancellation)
    {
        var affected = Messages.ServerAffectedPresetsList.Format(string.Join(", ", names));
        var unknown = Messages.ServerCouldNotCheckActivity.Format(affected);
        try
        {
            var entries = await ObserveAsync(cancellation);
            var loaded = entries.Where(e => e?["status"]?["value"]?.GetValue<string>() == "loaded").Select(e => e!["id"]!.GetValue<string>());
            foreach (var name in loaded)
            {
                using var response = await client.GetAsync(Url("/slots?autoload=false&model=" + Uri.EscapeDataString(name)), cancellation);
                if (!response.IsSuccessStatusCode) throw new Storage.StorageException("router-activity-unavailable", unknown);
                var slots = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation)) as JsonArray;
                if (slots is null || slots.Any(s => s?["is_processing"]?.GetValue<bool>() is null)) throw new Storage.StorageException("router-activity-unavailable", unknown);
                if (slots.Any(s => s?["is_processing"]?.GetValue<bool>() == true))
                    return Messages.ServerBusy + affected;
            }
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException || e is OperationCanceledException && !cancellation.IsCancellationRequested || e is InvalidOperationException)
        { throw new Storage.StorageException("router-activity-unavailable", unknown); }
    }
    public async Task<bool> ReloadAsync(CancellationToken cancellation)
    {
        using var response = await client.GetAsync(Url("/models?reload=1"), cancellation);
        return response.IsSuccessStatusCode;
    }
    // Asks the router to load the entry without waiting for it.
    public async Task<bool> RequestLoadAsync(string name, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/models/load"))
        { Content = new StringContent(new JsonObject { ["model"] = name }.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
        using var response = await client.SendAsync(request, cancellation);
        return response.IsSuccessStatusCode;
    }

    // State of one entry in the router catalog: loaded, failed, or still on its way.
    public async Task<(bool Loaded, bool Failed)> LoadStateAsync(string name, CancellationToken cancellation)
    {
        var item = (await ObserveAsync(cancellation)).SingleOrDefault(p => p?["id"]?.GetValue<string>() == name);
        return (item?["status"]?["value"]?.GetValue<string>() == "loaded", item?["status"]?["failed"]?.GetValue<bool>() == true);
    }

    public async Task<bool> WarmupAsync(string name, CancellationToken cancellation)
    {
        try
        {
            if (!await RequestLoadAsync(name, cancellation)) return false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                var item = (await ObserveAsync(cancellation)).SingleOrDefault(p => p?["id"]?.GetValue<string>() == name);
                if (item?["status"]?["failed"]?.GetValue<bool>() == true) return false;
                if (item?["status"]?["value"]?.GetValue<string>() == "loaded") return true;
                await Task.Delay(200, cancellation);
            } while (clock.Elapsed < TimeSpan.FromSeconds(120));
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { }
        return false;
    }
}
