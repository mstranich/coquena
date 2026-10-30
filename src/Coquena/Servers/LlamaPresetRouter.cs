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
                ?? throw new HttpRequestException("El catálogo no contiene data.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry?["id"]?.GetValue<string>() is not string name || !names.Add(name) ||
                    entry["status"]?["value"]?.GetValue<string>() is null || entry["status"]?["args"] is not JsonArray)
                    throw new HttpRequestException("El catálogo no permite atribuir las entradas.");
                foreach (var argument in entry["status"]!["args"]!.AsArray()) _ = argument?.GetValue<string>() ?? throw new InvalidOperationException();
                if (entry["status"]!["preset"] is JsonNode preset) _ = preset.GetValue<string>();
                if (entry["status"]!["failed"] is JsonNode failed) _ = failed.GetValue<bool>();
            }
            return entries;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
        { throw new HttpRequestException("El catálogo recibido no es válido.", e); }
    }
    public async Task<string> ActivityAsync(string[] names, CancellationToken cancellation)
    {
        try
        {
            var entries = await ObserveAsync(cancellation);
            var loaded = names.Where(name => entries.SingleOrDefault(e => e?["id"]?.GetValue<string>() == name)?["status"]?["value"]?.GetValue<string>() == "loaded").ToArray();
            foreach (var name in loaded)
            {
                using var response = await client.GetAsync(Url("/slots?autoload=false&model=" + Uri.EscapeDataString(name)), cancellation);
                if (!response.IsSuccessStatusCode) return "Actividad desconocida; no se pudo comprobar ocupación. Entradas: " + string.Join(", ", names);
                var slots = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation)) as JsonArray;
                if (slots is null || slots.Any(s => s?["is_processing"]?.GetValue<bool>() == true))
                    return "Peticiones activas o actividad desconocida; publicar puede interrumpirlas. Entradas: " + string.Join(", ", names);
            }
            return "No se observaron slots activos; la cola y peticiones posteriores no están descartadas. Entradas: " + string.Join(", ", names);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        { return "Actividad desconocida; publicar puede interrumpir peticiones. Entradas: " + string.Join(", ", names); }
    }
    public async Task<bool> ReloadAsync(CancellationToken cancellation)
    {
        using var response = await client.GetAsync(Url("/models?reload=1"), cancellation);
        return response.IsSuccessStatusCode;
    }
    public async Task<bool> WarmupAsync(string name, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/models/load"))
        { Content = new StringContent(new JsonObject { ["model"] = name }.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
        try
        {
            using var response = await client.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode) return false;
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
