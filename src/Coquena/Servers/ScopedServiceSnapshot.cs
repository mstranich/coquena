using System.Text.Json.Nodes;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Servers;

public static class ScopedServiceSnapshot
{
    public static async Task<ScopedStartPlan> RestoreAsync(ScopeContext context, JsonObject good,
        ICombinationResources resources, string logPath, CancellationToken cancellation = default)
    {
        var snapshot = good["snapshot"]?.AsObject()
            ?? throw new StorageException("invalid-start-plan", "La última buena no contiene snapshot.");
        var execution = good["execution"]?.AsObject()
            ?? throw new StorageException("invalid-start-plan", "La última buena no contiene ejecución probada.");
        var bindings = good["resources"]!.DeepClone().AsArray();
        var state = new ScopeStore(context).Read();
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Integrity is checked for every selected installation before any repair.
        foreach (var binding in bindings)
        {
            var installation = FindInstallation(state, binding!);
            if (installation?["integrity-status"]?.GetValue<string>() is "invalid" or "mismatch")
                throw new StorageException("resource-integrity-invalid", "La última buena selecciona integridad inválida conocida.");
            var resource = state.Documents.GetValueOrDefault("local/inventory.json")?["data"]?["resources"]?.AsArray()
                .SingleOrDefault(item => item!["resource-id"]!.GetValue<string>() == binding!["resource-id"]!.GetValue<string>());
            if (resource?["removal-pending"]?.GetValue<bool>() == true ||
                installation?["availability"]?.GetValue<string>() is string availability && availability is not ("available" or "missing"))
                throw new StorageException("snapshot-resource-unavailable", "La instalación probada está retirada o no disponible.");
        }
        foreach (var binding in bindings)
        {
            cancellation.ThrowIfCancellationRequested();
            var kind = binding!["kind"]!.GetValue<string>();
            var key = binding["key"]!.GetValue<string>();
            var resolution = binding["resolution"]?.AsObject();
            var installation = FindInstallation(state, binding);
            var path = binding["entry-path"]!.GetValue<string>();
            string FilePath(JsonNode file) => installation?["location"] is JsonNode location
                ? context.PathFor(location.GetValue<string>() + "/" + file["path"]!.GetValue<string>())
                : Path.GetFullPath(file["path"]!.GetValue<string>(), context.Base);
            var files = installation?["files"] as JsonArray ?? [];
            var missing = !File.Exists(path) || installation is null || installation["availability"]?.GetValue<string>() == "missing" ||
                files.Any(file => !File.Exists(FilePath(file!)));
            if (!missing && files.Any(file => file!["size"] is JsonNode size && new FileInfo(FilePath(file)).Length != size.GetValue<long>()))
                throw new StorageException("resource-content-conflict", "Cambió el tamaño de un archivo probado; no se repara automáticamente.");
            // Available model revisions are used by recorded identity, never by a
            // possibly changed alias. Backend preparation also checks --version.
            if (!missing && kind != "backend") continue;
            if (missing && resolution?["origin"]?.GetValue<string>() == "external")
                throw new StorageException("snapshot-resource-missing", "Falta un recurso externo de la última buena; requiere intervención.");
            if (resolution is null || resolution["origin"]?.GetValue<string>() != "external" && resolution["selection"] is null)
                throw new StorageException("snapshot-resource-missing", "Falta una resolución exacta; no se consulta latest.");
            var intent = kind == "backend" ? snapshot["service"]?["backend"] :
                snapshot[kind == "model" ? "models" : "companions"]?.AsObject()
                    .FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
            ArgumentReference? reference = null;
            if (kind != "backend" && intent is null)
            {
                intent = new JsonObject { ["type"] = "String", ["value"] = (kind == "model" ? "model://" : "companion://") + key };
                reference = new(kind, key, true);
            }
            var prepared = await resources.PrepareAsync(new(key, kind, Value(intent), reference), resolution.DeepClone().AsObject(), cancellation);
            if (prepared.Key != key || prepared.Kind != kind ||
                resolution["selection"] is { } selected && !JsonNode.DeepEquals(selected, prepared.Resolution["selection"]) ||
                resolution["artifacts"] is { } artifacts && !JsonNode.DeepEquals(artifacts, prepared.Resolution["artifacts"]))
                throw new StorageException("resource-content-conflict", "La recuperación no coincide con los recursos probados.");
            replacements[path] = prepared.EntryPath;
            binding["resource-id"] = prepared.ResourceId;
            binding["installation-id"] = prepared.InstallationId;
            binding["entry-path"] = prepared.EntryPath;
        }
        string Replace(string value) => replacements.GetValueOrDefault(value, value);
        var health = good["health"]!;
        return new(Replace(execution["executable"]!.GetValue<string>()),
            execution["arguments"]!.AsArray().Select(arg => Replace(arg!.GetValue<string>())).ToArray(), logPath,
            good["combination"]!.GetValue<string>(), snapshot.DeepClone().AsObject(), bindings,
            good["endpoint"]!.DeepClone().AsObject(), new Uri(health["url"]!.GetValue<string>()),
            TimeSpan.FromMilliseconds(health["timeout-ms"]?.GetValue<double>() ?? 120000),
            TimeSpan.FromMilliseconds(health["interval-ms"]?.GetValue<double>() ?? 500),
            IsDraft: good["draft"]?.GetValue<bool>() == true);
    }

    private static JsonNode? FindInstallation(ScopeSnapshot state, JsonNode binding) =>
        state.Documents.GetValueOrDefault("local/inventory.json")?["data"]?["resources"]?.AsArray()
            .SingleOrDefault(resource => resource!["resource-id"]!.GetValue<string>() == binding["resource-id"]!.GetValue<string>())?
            ["installations"]?.AsArray().SingleOrDefault(installation =>
                installation!["installation-id"]!.GetValue<string>() == binding["installation-id"]!.GetValue<string>());

    private static RecipeValue Value(JsonNode? node)
    {
        var origin = new RecipeOrigin("<local-snapshot>", 1, 1, "$");
        if (node is JsonObject map)
        {
            if (map.Count == 2 && map["type"] is JsonValue type && type.TryGetValue<string>(out var name) &&
                Enum.TryParse<RecipeKind>(name, out var kind) && kind is not (RecipeKind.Map or RecipeKind.Sequence))
                return new(kind, origin, map["value"]?.GetValue<string>());
            return new(RecipeKind.Map, origin, fields: map.ToDictionary(pair => pair.Key, pair => Value(pair.Value)));
        }
        if (node is JsonArray sequence) return new(RecipeKind.Sequence, origin, items: sequence.Select(Value));
        if (node is null) return new(RecipeKind.Null, origin);
        if (node.AsValue().TryGetValue<string>(out var text)) return new(RecipeKind.String, origin, text);
        if (node.AsValue().TryGetValue<bool>(out var boolean)) return new(RecipeKind.Boolean, origin, boolean ? "true" : "false");
        return new(RecipeKind.Number, origin, node.ToJsonString());
    }
}
