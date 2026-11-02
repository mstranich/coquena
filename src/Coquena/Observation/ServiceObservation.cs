using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;

namespace Coquena.Observation;

public sealed record EntrySnapshot(string Name, string State, DateTimeOffset ObservedAt,
    IReadOnlyList<MetricSample> Metrics, string? Diagnostic = null);
public sealed record ServiceSnapshot(string ServiceId, string Name, string Scope, string? InstanceId,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, string ProcessState, string ApiState, string Health,
    string IntentComparison, string Recovery, bool CompositionCurrent, JsonObject? ActiveComposition, JsonObject? CurrentIntention,
    IReadOnlyList<MetricSample> Metrics, IReadOnlyList<EntrySnapshot> Entries, IReadOnlyList<string> Diagnostics, bool IsDefault);

public sealed class ServiceObservation(ScopeStore store, HttpClient? client = null, IInstanceProbe? instanceProbe = null)
{
    private JsonArray? catalog;
    private string? catalogInstance;
    private JsonNode? catalogComposition;
    private DateTimeOffset catalogAt;
    public async Task<ServiceSnapshot> CaptureAsync(ServiceIdentity service, RecipeSelection? selection = null,
        CancellationToken cancellation = default, bool reconcileCatalog = true)
    {
        var start = DateTimeOffset.UtcNow;
        var diagnostics = new List<string>(); var samples = new List<MetricSample>(); var entries = new List<EntrySnapshot>();
        string? instanceId = null; JsonObject? active = null; JsonObject? currentIntention = null;
        var processState = "unknown"; var api = "not-queried"; var health = "not-queried";
        var intent = "unavailable"; var recovery = "not-recorded"; var current = false;
        var isDefault = false;
        var path = store.RuntimeDocumentPath(service.ServiceId, "active");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            // ReadVerified rejects copies rather than silently adopting a new scope.
            var state = store.ReadVerified();
            isDefault = store.Context.IsGlobal && state.State?["data"]?["aliases"]?["default"]?.GetValue<string>() == service.ServiceId;
            var registered = state.Documents.GetValueOrDefault("local/recipes.json")?["data"]?["recipes"]?.AsArray()
                .Single(r => r!["recipe-id"]!.GetValue<string>() == service.RecipeId);
            var entity = registered?["entities"]?.AsArray().Single(e => e!["id"]!.GetValue<string>() == service.ServiceId);
            active = state.Documents.GetValueOrDefault(path)?["data"]?.DeepClone().AsObject();
            instanceId = active?["instance-id"]?.GetValue<string>();
            recovery = entity?["recovery"] is { } recordedRecovery ? recordedRecovery.ToJsonString() : "not-recorded";
            if (active is null || active["terminated"]?.GetValue<bool>() == true)
            {
                processState = entity?["instance"] is null ? "stopped" : "uncertain";
                return Result();
            }
            if (entity?["instance"]?["instance-id"]?.GetValue<string>() != instanceId ||
                !JsonNode.DeepEquals(entity?["instance"]?["process"], active["process"]))
                throw new ConfigurationException("Registro e instancia no coinciden; no se consulta su API.");
            var identity = Identity(service.ServiceId, active);
            var check = (instanceProbe ?? InstanceProbe.Create()).Open(identity);
            processState = check.State;
            if (check.Diagnostic is { } message) diagnostics.Add(message);
            using var process = check.Instance;
            if (process is null) return Result();
            intent = CompareIntent();
            try { samples.AddRange(await process.CaptureAsync(cancellation)); }
            catch (Exception e) when (ObservationError(e)) { diagnostics.Add("Recursos del proceso no disponibles: " + e.GetType().Name); }

            var endpoint = active["endpoint"]!;
            var host = endpoint["host"]!.GetValue<string>(); var port = endpoint["port"]!.GetValue<int>();
            if (!process.OwnsEndpoint(host, port))
            { api = "unverified-listener"; diagnostics.Add("Proceso vivo; escucha no comprobada como propia. No se consulta otro proceso."); return Result(); }
            using var owned = client is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) : null;
            var http = client ?? owned!;
            var configuredHealth = active["snapshot"]?["service"]?["server"]?["health"]?["url"]?["value"]?.GetValue<string>();
            var scheme = Uri.TryCreate(configuredHealth, UriKind.Absolute, out var configuredUrl) ? configuredUrl.Scheme : "http";
            if (scheme is not ("http" or "https")) throw new FormatException("Protocolo de observación inválido.");
            var address = new UriBuilder(scheme, host == "0.0.0.0" ? "127.0.0.1" : host == "::" ? "::1" : host, port).Uri;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            // A single request budget spans health, models and every entry.
            try
            {
                var healthText = configuredHealth;
                var healthUrl = healthText is null ? new Uri(address, "/health") : new Uri(healthText);
                if (healthUrl.Port != port || healthUrl.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(healthUrl.UserInfo) ||
                    healthText is not null && !string.Equals(healthUrl.Host, host, StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("URL de salud no atribuible al endpoint verificado.");
                using var response = await http.GetAsync(healthUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                health = "HTTP " + (int)response.StatusCode;
            }
            catch (Exception e) when (ObservationError(e)) { health = "unavailable"; diagnostics.Add("Salud: " + e.GetType().Name); }
            try
            {
                var freshCatalog = reconcileCatalog || catalog is null || catalogInstance != instanceId ||
                    DateTimeOffset.UtcNow - catalogAt >= TimeSpan.FromSeconds(5) ||
                    !JsonNode.DeepEquals(catalogComposition, active["preset-composition"]);
                var models = freshCatalog ? JsonNode.Parse(await Fetch(http, new(address, "/models"), deadline.Token))?["data"]?.AsArray()
                    ?? throw new FormatException("Catálogo sin data.") : catalog!;
                var observedCatalogAt = freshCatalog ? DateTimeOffset.UtcNow : catalogAt;
                if (models.Count > 128) throw new FormatException("Catálogo excede 128 entradas por snapshot.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                var composition = active["preset-composition"] as JsonArray ?? [];
                var expected = composition.SelectMany(c => c!["presets"]!.AsArray()).Select(p => p!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                api = freshCatalog ? "available" : "cached-catalog";
                current = freshCatalog && PresetPublication.Matches(composition, models);
                if (!freshCatalog) diagnostics.Add($"Catálogo conservado desde {catalogAt:O}; esperando reconciliación, sin confirmar composición actual.");
                var disabled = active["snapshot"]?["service"]?["server"]?["args"]?["metrics"]?["value"]?.GetValue<string>() == "false" ||
                    active["execution"]?["arguments"]?.AsArray().Any(a => a?.GetValue<string>() == "--no-metrics") == true;
                foreach (var model in models)
                {
                    var name = model!["id"]!.GetValue<string>();
                    if (!names.Add(name)) throw new FormatException("Catálogo con entradas duplicadas.");
                    var status = model["status"]?["value"]?.GetValue<string>() ?? "unknown";
                    var entrySamples = new List<MetricSample>(); string? diagnostic = null;
                    if (!expected.Contains(name)) diagnostic = "Entrada observada ajena a la composición activa registrada.";
                    if (status != "loaded") diagnostic = "Modelo no cargado; métricas no consultadas.";
                    else if (disabled) diagnostic = "Métricas desactivadas en la ejecución.";
                    else
                    {
                        try
                        {
                            var metrics = ServerRuntime.ParseMetrics(await Fetch(http, new(address,
                                "/metrics?model=" + Uri.EscapeDataString(name) + "&autoload=false"), deadline.Token));
                            foreach (var (metric, value) in metrics)
                                entrySamples.Add(new(metric, value, Unit(metric), "/metrics?model; backend mean/cumulative; period not reported",
                                    "instance:" + instanceId + "/entry:" + name, DateTimeOffset.UtcNow));
                        }
                        catch (Exception e) when (ObservationError(e)) { diagnostic = "Métricas no disponibles: " + e.GetType().Name; }
                    }
                    entries.Add(new(name, status, observedCatalogAt, entrySamples, diagnostic));
                }
                foreach (var missing in expected.Except(names)) entries.Add(new(missing, "absent", observedCatalogAt, [], "Ausente del catálogo del router."));
                if (freshCatalog)
                {
                    catalog = models; catalogInstance = instanceId; catalogAt = observedCatalogAt;
                    catalogComposition = active["preset-composition"]?.DeepClone();
                }
            }
            catch (Exception e) when (ObservationError(e)) { catalog = null; api = "unavailable"; current = false; diagnostics.Add("API: " + e.GetType().Name); }
            if (!process.IsAlive || !process.OwnsEndpoint(host, port) || !JsonNode.DeepEquals(store.ReadVerified().Documents.GetValueOrDefault(path)?["data"], active))
            { current = false; diagnostics.Add("Instancia/composición cambió durante la muestra; conservarla como histórica, sin enlazar con el reemplazo."); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception e) when (ObservationError(e)) { current = false; diagnostics.Add(e.Message); }
        return Result();

        ServiceSnapshot Result()
        {
            cancellation.ThrowIfCancellationRequested();
            return new(service.ServiceId, service.Name, store.Context.IsGlobal ? "global" : store.Context.Base,
                instanceId, start, DateTimeOffset.UtcNow, processState, api, health, intent, recovery, current,
                active, currentIntention, samples, entries, diagnostics, isDefault);
        }
        string CompareIntent()
        {
            try
            {
                var chosen = selection ?? (store.Context.IsGlobal ? new RecipeImporter(store.Context).ServiceSelection(service.ServiceId) :
                    new RecipeSelection(store.Context.Base, registeredFiles(), store.Context.Base));
                var recipe = RecipeReader.Read(chosen);
                var expanded = CombinationPreparation.Expanded(recipe.Root);
                currentIntention = expanded.AsObject();
                // Publications from other projects are compared against /models,
                // not against the owning service recipe's presets.
                var same = JsonNode.DeepEquals(expanded, active!["snapshot"]);
                var declared = recipe.Root.Get("name")?.Text;
                return (same ? "recipe-matches" : "recipe-differs") + (declared is not null && declared != service.Name ? "; declared-name-differs-from-local" : "");
            }
            catch (Exception e) when (ObservationError(e)) { diagnostics.Add("Intención actual no comparable: " + e.GetType().Name); return "unavailable"; }
        }
        string[] registeredFiles() => new RecipeRegistry(store).Get(service.RecipeId).Files;
    }

    internal static InstanceDocument Identity(string serviceId, JsonObject active)
    {
        var process = active["process"]!;
        return new(1, Guid.ParseExact(active["instance-id"]!.GetValue<string>(), "D"), Guid.ParseExact(serviceId, "D"), Guid.Empty,
            "", process["pid"]!.GetValue<int>(), "", "", 0, false, [], [], "", "",
            process["creation-time"]!.GetValue<long>(), process["executable"]!.GetValue<string>());
    }
    internal static bool ObservationError(Exception e) => e is Coquena.Storage.StorageException or ContractException or ConfigurationException or RecipeException or
        IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or JsonException or
        InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or NullReferenceException or OverflowException or System.ComponentModel.Win32Exception;
    private static string Unit(string name) => name.EndsWith("tokens_seconds", StringComparison.Ordinal) ? "tokens/s (backend mean)" :
        name.Contains("seconds_total", StringComparison.Ordinal) ? "seconds (cumulative)" :
        name.Contains("requests_", StringComparison.Ordinal) ? "requests" : "tokens (cumulative)";
    private static async Task<string> Fetch(HttpClient client, Uri url, CancellationToken cancellation)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        using var body = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream(); var bytes = new byte[4096]; int count;
        while ((count = await body.ReadAsync(bytes, cancellation)) > 0)
        {
            if (buffer.Length + count > 1024 * 1024) throw new FormatException("Respuesta excede 1 MiB.");
            buffer.Write(bytes, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
