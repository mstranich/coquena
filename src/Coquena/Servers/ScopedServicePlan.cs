using System.Globalization;
using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Servers;

// Builds a process-only plan from an already published preparation. Preset
// arguments are deliberately excluded from process configuration.
public static class ScopedServicePlan
{
    public static ScopedStartPlan Compose(ScopeContext context, EffectiveRecipe recipe, PreparedCombination prepared, string logPath)
    {
        var intent = BackendIntent.FromRecipe(recipe);
        var backend = prepared.Resources.SingleOrDefault(r => r.Kind == "backend")
            ?? throw new StorageException("backend-required", "La combinación no contiene un backend preparado.");
        var state = new ScopeStore(context).Read();
        var entry = state.Documents.GetValueOrDefault("local/combinations.json")?["data"]?["combinations"]?[prepared.Id];
        var evidence = prepared.ProcessOnly ? entry?["service-preparation"] : entry;
        var revision = prepared.ProcessOnly ? evidence?["revision"] : evidence?["preparation-revision"];
        if (revision?.GetValue<long>() != prepared.Revision || evidence?["snapshot"] is not JsonObject snapshot)
            throw new StorageException("stale-preparation", "La combinación preparada cambió antes de componer el inicio.");
        if (!JsonNode.DeepEquals(snapshot, CombinationPreparation.Expanded(recipe.Root)))
            throw new StorageException("intention-conflict", "La receta efectiva cambió después de la preparación.");
        var paths = recipe.Selection.Files.Concat(recipe.Selection.EnvFiles).Concat(recipe.Layers.SelectMany(layer =>
            (layer.Get("arg-defs")?.Get("imports")?.Items ?? []).Select(import => recipe.Selection.ResolvePath(import.Text!))));
        var inputs = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        void ValidateIntent()
        {
            if (inputs.Any(input => !File.Exists(input.Key) || !File.ReadAllBytes(input.Key).AsSpan().SequenceEqual(input.Value)) ||
                !JsonNode.DeepEquals(snapshot, CombinationPreparation.Expanded(RecipeReader.Read(recipe.Selection).Root)))
                throw new StorageException("intention-conflict", "Cambió la receta o su schema antes de publicar la instancia.");
        }
        ValidateIntent();
        var blocks = ArgumentValidation.Validate(recipe);
        var server = blocks.SingleOrDefault(block => block.Scope == "server");
        var args = server is null ? new List<string>() : ArgumentEmission.Cli(recipe, server,
            intent.Executable is null ? new LlamaB11482ArgumentAdapter() : null,
            reference => prepared.Resources.SingleOrDefault(resource => resource.Kind == reference.Kind &&
                resource.Key.Equals(reference.Name, StringComparison.OrdinalIgnoreCase))?.EntryPath
                ?? throw new StorageException("unknown-resource", "Falta la referencia preparada del proceso.")).ToList();
        var values = server?.Arguments;
        var host = values?.GetValueOrDefault("host")?.Value.Text;
        var portText = values?.GetValueOrDefault("port")?.Value.Text;
        if (intent.Executable is null)
        {
            if (host is null) { host = "127.0.0.1"; args.AddRange(["--host", host]); }
            if (portText is null) { portText = "9931"; args.AddRange(["--port", portText]); }
            if (values?.ContainsKey("metrics") != true) args.Add("--metrics");
        }
        var health = recipe.Root.Get("service")?.Get("server")?.Get("health");
        var url = health?.Get("url")?.Text;
        if (url is null && intent.Executable is not null)
            throw new StorageException("health-required", "Un backend externo requiere URL de salud explícita.");
        if (url is null && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out _) || host is null))
            throw new StorageException("invalid-endpoint", "Host y puerto del servidor son inválidos.");
        if (!Uri.TryCreate(url ?? $"http://{host}:{portText}/health", UriKind.Absolute, out var healthUrl) ||
            healthUrl.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(healthUrl.UserInfo))
            throw new StorageException("invalid-health-url", "URL de salud inválida o con credenciales.");
        host ??= healthUrl.Host;
        if (portText is null) portText = healthUrl.Port.ToString(CultureInfo.InvariantCulture);
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw new StorageException("invalid-endpoint", "Puerto inválido.");
        var timeout = int.Parse(health?.Get("timeout-seconds")?.Text ?? "120", CultureInfo.InvariantCulture);
        var interval = int.Parse(health?.Get("interval-ms")?.Text ?? "500", CultureInfo.InvariantCulture);
        var bindings = new JsonArray(prepared.Resources.Select(r => (JsonNode?)new JsonObject
        { ["key"] = r.Key, ["kind"] = r.Kind, ["resource-id"] = r.ResourceId, ["installation-id"] = r.InstallationId,
            ["entry-path"] = r.EntryPath, ["resolution"] = r.Resolution.DeepClone() }).ToArray());
        return new(backend.EntryPath, args.ToArray(), logPath, prepared.Id, snapshot.DeepClone().AsObject(), bindings,
            new JsonObject { ["host"] = host, ["port"] = port }, healthUrl, TimeSpan.FromSeconds(timeout), TimeSpan.FromMilliseconds(interval), ValidateIntent: ValidateIntent);
    }
}
