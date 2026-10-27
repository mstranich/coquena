using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Coquena.Recipes;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena.Projects;

public sealed record PreparationRequest(string Key, string Kind, RecipeValue Intent, ArgumentReference? Reference = null, bool SensitiveSelection = false);
public sealed record PreparationResource(string Key, string Kind, string ResourceId, string InstallationId, string EntryPath, JsonObject Resolution);
public sealed record PreparationTarget(ScopeStore Store, string ServiceId);
public sealed record PreparedCombination(string Id, bool Private, long Revision, IReadOnlyList<PreparationResource> Resources);

// The resource preparer operates outside writer leases. Exact is the previous
// portable resolution, never a request to look for newer resources.
public interface ICombinationResources
{
    Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exact, CancellationToken cancellation);
}

public sealed class CombinationPreparation(ScopeContext context)
{
    private readonly ScopeStore store = new(context);
    private const string LocalIndex = "local/combinations.json";
    private const string SharedIndex = "combinations.json";

    public JsonObject List()
    {
        var snapshot = store.Read();
        var result = new JsonObject();
        foreach (var path in new[] { SharedIndex, LocalIndex })
            foreach (var item in snapshot.Documents.GetValueOrDefault(path)?["data"]?["combinations"]?.AsObject() ?? new JsonObject())
                result[item.Key] = new JsonObject { ["files"] = item.Value!["files"]!.DeepClone(),
                    ["env-files"] = item.Value["env-files"]!.DeepClone(), ["private"] = item.Value["private"]?.DeepClone(),
                    ["preparation-revision"] = item.Value["preparation-revision"]?.DeepClone() };
        return result;
    }

    public async Task<PreparedCombination> PrepareAsync(RecipeSelection selection, ICombinationResources resources,
        bool privateSelection = false, IReadOnlyDictionary<string, string?>? environment = null,
        PreparationTarget? target = null, Action<ScopeWriteStage>? fault = null, CancellationToken cancellation = default)
    {
        if (!string.Equals(selection.Base, context.Base, StringComparison.OrdinalIgnoreCase))
            throw new StorageException("preparation-base-conflict", "La selección debe pertenecer a la base del ámbito.");
        var identity = CombinationIdentity.FromSelection(selection, privateSelection);
        var inputs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in selection.Files.Concat(selection.EnvFiles)) inputs[path] = File.ReadAllBytes(path);
        var recipe = RecipeReader.Read(selection, environment);
        foreach (var import in recipe.Layers.SelectMany(l => l.Get("arg-defs")?.Get("imports")?.Items ?? []))
        {
            var path = selection.ResolvePath(import.Text!);
            inputs[path] = File.ReadAllBytes(path);
        }
        var blocks = ArgumentValidation.Validate(recipe);
        _ = ModelIntent.FromRecipe(recipe);
        CheckInputs(inputs);
        var requests = Requests(recipe, blocks);
        // Outside references require an explicit private context. Environment
        // selections and cross-volume selections are private by definition.
        if (!identity.Private)
        {
            foreach (var path in inputs.Keys.Concat(LocalReferences(recipe, requests)))
                if (Outside(selection.Base, path))
                    throw new StorageException("private-reference", "Una referencia exterior requiere --private antes de publicar resoluciones.");
        }
        // Concrete resource origins derived from environment values stay local too.
        if (requests.Any(r => r.SensitiveSelection || Sensitive(r.Intent))) identity = identity with { Private = true };
        EnsureIgnore(identity);
        var registered = Register(identity, inputs);
        var previousEntry = registered.Documents[LocalIndex]["data"]!["combinations"]![identity.Id]!.AsObject();
        var version = previousEntry["preparation-revision"]?.GetValue<long>() ?? 0;
        var targetSnapshot = target?.Store.Read();
        if (target is not null) ValidateTarget(targetSnapshot!, target.ServiceId);
        var key = Key();
        var priorKey = previousEntry["key-id"]?.GetValue<string>() == KeyId(key);
        var fingerprints = previousEntry["intent-hmacs"] as JsonObject;
        var resolutionPath = ResolutionPath(identity);
        var previousPath = previousEntry["resolution-path"]?.GetValue<string>() ?? resolutionPath;
        var previousDocument = registered.Documents.GetValueOrDefault(previousPath);
        if (previousDocument is null && !identity.Private && File.Exists(context.PathFor(resolutionPath)))
        {
            var path = context.PathFor(resolutionPath);
            var bytes = File.ReadAllBytes(path);
            inputs[path] = bytes;
            previousDocument = ProjectDocuments.Read(bytes, "resolution");
            if (previousDocument["data"]!["combination-id"]!.GetValue<string>() != identity.Id) ProjectDocuments.Invalid();
        }
        var previousResolution = previousDocument?["data"]?["resources"] as JsonArray;
        var prepared = new List<PreparationResource>();
        var nextFingerprints = new JsonObject();
        foreach (var request in requests)
        {
            cancellation.ThrowIfCancellationRequested();
            var name = request.Kind + ":" + request.Key;
            var hmac = Hmac(key, Expanded(request.Intent).ToJsonString());
            nextFingerprints[name] = hmac;
            var raw = SourceIntent(recipe, request);
            var unchanged = priorKey && fingerprints?[name]?.GetValue<string>() == hmac ||
                fingerprints is null && !Sensitive(request.Intent) && JsonNode.DeepEquals(previousDocument?["data"]?["selections"]?[name], raw);
            var exact = unchanged
                ? previousResolution?.SingleOrDefault(r => r!["key"]!.GetValue<string>() == request.Key && r["kind"]!.GetValue<string>() == request.Kind)?.AsObject()
                : null;
            var result = await resources.PrepareAsync(request, exact?.DeepClone().AsObject(), cancellation);
            if (exact?["artifacts"] is { } expected && !JsonNode.DeepEquals(expected, result.Resolution["artifacts"]))
                throw new StorageException("resource-content-conflict", "Los artefactos preparados no coinciden con la referencia fijada.");
            if (result.Key != request.Key || result.Kind != request.Kind) throw new StorageException("invalid-preparation-result", "El recurso preparado no corresponde a la selección.");
            ProjectDocuments.Uuid(JsonValue.Create(result.ResourceId)); ProjectDocuments.Uuid(JsonValue.Create(result.InstallationId));
            prepared.Add(result);
        }
        cancellation.ThrowIfCancellationRequested();
        var current = store.Read();
        var entry = current.Documents[LocalIndex]["data"]!["combinations"]![identity.Id]!.AsObject();
        if ((entry["preparation-revision"]?.GetValue<long>() ?? 0) != version ||
            entry["attempt-id"]?.GetValue<string>() != previousEntry["attempt-id"]?.GetValue<string>())
            throw new StorageException("scope-conflict", "Otra preparación cambió la combinación; se conserva su conjunto completo.");
        var docs = RecipeRegistry.Next(current);
        var nextEntry = docs[LocalIndex]["data"]!["combinations"]![identity.Id]!;
        nextEntry["preparation-revision"] = checked(version + 1);
        nextEntry["resolution-path"] = resolutionPath;
        nextEntry["key-id"] = KeyId(key);
        nextEntry["intent-hmacs"] = nextFingerprints;
        nextEntry["effective-hmac"] = Hmac(key, Expanded(recipe.Root).ToJsonString());
        nextEntry["snapshot"] = Expanded(recipe.Root);
        var bindings = new JsonArray();
        foreach (var item in prepared)
        {
            ValidateBinding(current, item);
            bindings.Add((JsonNode)new JsonObject { ["key"] = item.Key, ["kind"] = item.Kind,
                ["resource-id"] = item.ResourceId, ["installation-id"] = item.InstallationId, ["entry-path"] = item.EntryPath });
        }
        nextEntry["bindings"] = bindings;
        var portableResources = new JsonArray(prepared.Select(p =>
        {
            var node = p.Resolution.DeepClone().AsObject(); node["key"] = p.Key; node["kind"] = p.Kind;
            return (JsonNode?)node;
        }).ToArray());
        var selections = new JsonObject();
        foreach (var request in requests) selections[request.Kind + ":" + request.Key] = SourceIntent(recipe, request);
        var resolution = new JsonObject { ["combination-id"] = identity.Id, ["selections"] = selections, ["resources"] = portableResources };
        docs[resolutionPath] = ProjectDocuments.Create("resolution", docs.GetValueOrDefault(resolutionPath)?["revision"]?.GetValue<long>() ?? 1, resolution);
        var changes = new List<ScopeChange>();
        if (target is not null)
        {
            var same = target.Store.Context.PhysicalRoot == context.PhysicalRoot;
            var other = same ? current : targetSnapshot!;
            var otherDocs = same ? docs : RecipeRegistry.Next(other);
            var targetEntry = Service(otherDocs, target.ServiceId);
            var references = targetEntry["preparations"] as JsonArray ?? new JsonArray();
            var previous = references.SingleOrDefault(r => r!["scope-id"]!.GetValue<string>() == current.ScopeId && r["combination-id"]!.GetValue<string>() == identity.Id);
            if (previous is not null) references.Remove(previous);
            references.Add((JsonNode)new JsonObject { ["scope-id"] = current.ScopeId, ["combination-id"] = identity.Id,
                ["resources"] = bindings.DeepClone() });
            targetEntry["preparations"] = references;
            nextEntry["target"] = new JsonObject { ["scope-id"] = other.ScopeId, ["service-id"] = target.ServiceId };
            if (!same) changes.Add(new(target.Store, other.Revision, otherDocs));
        }
        changes.Insert(0, new(store, current.Revision, docs, Validate: () =>
        {
            CheckInputs(inputs);
            if (!File.ReadAllBytes(context.PathFor("local/intent.key")).AsSpan().SequenceEqual(key))
                throw new StorageException("scope-conflict", "Cambió la clave local durante la preparación.");
        }));
        ScopeStore.CommitTogether(changes, fault);
        return new(identity.Id, identity.Private, checked(version + 1), prepared.AsReadOnly());
    }

    private ScopeSnapshot Register(CombinationIdentity identity, Dictionary<string, byte[]> inputs)
    {
        var snapshot = store.Read();
        var docs = RecipeRegistry.Next(snapshot);
        if (!docs.ContainsKey("local/inventory.json")) docs["local/inventory.json"] = ProjectDocuments.Create("inventory", 1,
            new JsonObject { ["resources"] = new JsonArray() });
        JsonObject Index(string path)
        {
            if (!docs.ContainsKey(path)) docs[path] = ProjectDocuments.Create("combination-index", 1, new JsonObject { ["combinations"] = new JsonObject() });
            return docs[path]["data"]!["combinations"]!.AsObject();
        }
        var local = Index(LocalIndex);
        var previous = local[identity.Id] as JsonObject;
        var entry = previous?.DeepClone().AsObject() ?? Selection(identity);
        entry["private"] = identity.Private;
        entry["attempt-id"] = ProjectDocuments.Id();
        local[identity.Id] = entry;
        if (!identity.Private) Index(SharedIndex)[identity.Id] = Selection(identity);
        else if (docs.GetValueOrDefault(SharedIndex)?["data"]?["combinations"] is JsonObject shared) shared.Remove(identity.Id);
        ScopeStore.CommitTogether([new(store, snapshot.Revision, docs, Validate: () => CheckInputs(inputs))]);
        return store.Read();
    }

    private static List<PreparationRequest> Requests(EffectiveRecipe recipe, IReadOnlyList<ValidatedArgumentBlock> blocks)
    {
        var result = new Dictionary<string, PreparationRequest>(StringComparer.OrdinalIgnoreCase);
        if (recipe.Root.Get("service")?.Get("backend") is { } backend) result["backend:backend"] = new("backend", "backend", backend);
        foreach (var block in blocks.Where(b => b.Scope != "preset" || recipe.PresetEnabled(b.Key)))
            foreach (var argument in block.Arguments.Values)
            {
                if (argument.Literal) continue;
                foreach (var value in argument.Value.Kind == RecipeKind.Sequence ? argument.Value.Items : [argument.Value])
                {
                    var reference = ArgumentValidation.Reference(recipe, value, false, block.Scope == "preset");
                    if (reference is null) continue;
                    var intent = reference.Canonical ? value : recipe.Root.Get(reference.Kind == "model" ? "models" : "companions")?.Fields
                        .SingleOrDefault(p => p.Key.Equals(reference.Name, StringComparison.OrdinalIgnoreCase)).Value
                        ?? throw value.Error("unknown-resource", "preparation", "Referencia sin declaración.");
                    result[reference.Kind + ":" + reference.Name] = new(reference.Name, reference.Kind, intent, reference, value.Sensitive);
                }
            }
        return result.Values.ToList();
    }

    private static IEnumerable<string> LocalReferences(EffectiveRecipe recipe, IEnumerable<PreparationRequest> requests)
    {
        foreach (var request in requests)
        {
            var paths = request.Kind == "backend" ? request.Intent.Get("executable") is { } executable ? new[] { executable } : []
                : request.Intent.Get("repository") is null ? request.Intent.Get("files")?.Items ?? [] : [];
            foreach (var path in paths)
            {
                if (Path.IsPathRooted(path.Text!))
                    throw new StorageException("private-reference", "Una ruta física absoluta requiere --private antes de publicar resoluciones.");
                yield return recipe.Selection.ResolvePath(path.Text!);
            }
        }
    }
    private static bool Outside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal);
    }
    private static bool Sensitive(RecipeValue value) => value.Sensitive || value.Fields.Values.Any(Sensitive) || value.Items.Any(Sensitive);
    private static JsonNode Expanded(RecipeValue value) => value.Kind switch
    {
        RecipeKind.Map => new JsonObject(value.Fields.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, Expanded(p.Value)))),
        RecipeKind.Sequence => new JsonArray(value.Items.Select(v => (JsonNode?)Expanded(v)).ToArray()),
        _ => new JsonObject { ["type"] = value.Kind.ToString(), ["value"] = value.Text }
    };
    private byte[] Key() => store.Locked(() =>
    {
        context.Ensure(); var path = context.PathFor("local/intent.key");
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var key = RandomNumberGenerator.GetBytes(32); file.Write(key); file.Flush(true); return key;
        }
        catch (IOException) when (File.Exists(path))
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length != 32) throw new StorageException("invalid-intent-key", "La clave local no es válida.");
            return bytes;
        }
    });
    private void EnsureIgnore(CombinationIdentity identity) => store.Locked(() =>
    {
        context.Ensure();
        var path = context.PathFor(".gitignore");
        var existing = File.Exists(path) ? File.ReadAllText(path) : "";
        var missing = new[] { "/local/", "/combinations/*/local/", "/models/", "/backends/", "/downloads/", "/tmp/", "/logs/" }
            .Concat(identity.Private ? ["/combinations/" + identity.Id + "/resolved.json"] : Array.Empty<string>())
            .Where(rule => !existing.Split('\n').Any(line => line.TrimEnd('\r') == rule)).ToArray();
        if (missing.Length != 0) ScopeStore.Replace(path, Encoding.UTF8.GetBytes(existing + (existing.Length != 0 && !existing.EndsWith('\n') ? "\n" : "") + string.Join('\n', missing) + "\n"));
        return true;
    });
    private static JsonNode? SourceIntent(EffectiveRecipe recipe, PreparationRequest request)
    {
        if (request.Kind == "backend") return recipe.SourceRoot.Get("service")?.Get("backend")?.Redacted();
        if (request.Reference?.Canonical == true)
        {
            // The unexpanded argument is recorded through its source location.
            RecipeValue? Find(RecipeValue value)
            {
                if (value.Origin == request.Intent.Origin) return value;
                foreach (var child in value.Fields.Values.Concat(value.Items)) if (Find(child) is { } found) return found;
                return null;
            }
            return Find(recipe.SourceRoot)?.Redacted();
        }
        return recipe.SourceRoot.Get(request.Kind == "model" ? "models" : "companions")?.Fields
            .SingleOrDefault(p => p.Key.Equals(request.Key, StringComparison.OrdinalIgnoreCase)).Value?.Redacted();
    }
    private static string Hmac(byte[] key, string text) => Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text)));
    private static string KeyId(byte[] key) => Hmac(key, "coquena-intent-key");
    private static void CheckInputs(Dictionary<string, byte[]> inputs)
    {
        foreach (var input in inputs)
            if (!File.Exists(input.Key) || !File.ReadAllBytes(input.Key).AsSpan().SequenceEqual(input.Value))
                throw new StorageException("intention-conflict", "Cambió un YAML, env o schema durante la preparación.");
    }
    private static JsonObject Selection(CombinationIdentity identity) => new()
    {
        ["files"] = new JsonArray(identity.Files.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()),
        ["env-files"] = new JsonArray(identity.EnvFiles.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray())
    };
    private static string ResolutionPath(CombinationIdentity identity) => "combinations/" + identity.Id + (identity.Private ? "/local" : "") + "/resolved.json";
    private static void ValidateBinding(ScopeSnapshot snapshot, PreparationResource resource)
    {
        var found = snapshot.Documents.GetValueOrDefault("local/inventory.json")?["data"]?["resources"]?.AsArray()
            .SingleOrDefault(r => r!["resource-id"]!.GetValue<string>() == resource.ResourceId && r["kind"]!.GetValue<string>() == resource.Kind);
        if (found is null || found["removal-pending"]?.GetValue<bool>() == true ||
            !found["installations"]!.AsArray().Any(i => i!["installation-id"]!.GetValue<string>() == resource.InstallationId))
            throw new StorageException("stale-resource", "La instalación preparada ya no está disponible en el inventario.");
    }
    private static void ValidateTarget(ScopeSnapshot snapshot, string id) => _ = Service(snapshot.Documents, id);
    private static JsonNode Service(IReadOnlyDictionary<string, JsonObject> docs, string id) =>
        docs.GetValueOrDefault("local/recipes.json")?["data"]?["recipes"]?.AsArray().SelectMany(r => r!["entities"]!.AsArray())
            .SingleOrDefault(e => e!["kind"]!.GetValue<string>() == "service" && e["id"]!.GetValue<string>() == id)
        ?? throw new StorageException("unknown-service", "El servicio destino no está registrado en ese ámbito.");
}
