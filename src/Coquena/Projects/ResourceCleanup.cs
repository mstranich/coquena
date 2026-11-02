using System.Text.Json.Nodes;
using Coquena.Storage;
using Coquena.Contracts;

namespace Coquena.Projects;

public sealed record CleanupItem(string ResourceId, string InstallationId, string Kind, long Bytes);
public sealed record CleanupPlan(IReadOnlyList<CleanupItem> Items, long Bytes,
    IReadOnlyList<ScopeStore> Stores, IReadOnlyList<ScopeSnapshot> Snapshots, IReadOnlyList<string> Diagnostics);
public sealed record CleanupResult(long FreedBytes, IReadOnlyList<string> Diagnostics);

// Only inventories prove ownership. Navigation and publication records identify
// the roots whose current evidence must be consulted, never absence of use.
public sealed class ResourceCleanup(ScopeContext context, Home home)
{
    private const string Inventory = "local/inventory.json";

    public CleanupPlan Preview()
    {
        var stores = Discover();
        return ScopeStore.Coordinated<CleanupPlan>(stores, states =>
        {
            var selected = Array.FindIndex(stores, s => s.Context.PhysicalRoot == context.PhysicalRoot);
            var diagnostics = new List<string>();
            var items = Eligible(stores, states, selected, diagnostics);
            return new(items, items.Sum(i => i.Bytes), stores, states, diagnostics);
        });
    }

    public CleanupResult Apply(CleanupPlan plan)
    {
        var currentStores = Discover();
        if (!currentStores.Select(s => s.Context.PhysicalRoot).ToHashSet().SetEquals(plan.Stores.Select(s => s.Context.PhysicalRoot)))
            throw new StorageException("cleanup-conflict", "Cambió el conjunto de ámbitos; repita clean.");
        var selected = plan.Stores.ToList().FindIndex(s => s.Context.PhysicalRoot == context.PhysicalRoot);
        if (selected < 0) throw new ArgumentException("El plan pertenece a otro ámbito.");
        var documents = plan.Snapshots.Select(RecipeRegistry.Next).ToArray();
        var diagnostics = new List<string>(); long freed = 0;
        var changes = Enumerable.Range(0, plan.Stores.Count).Where(i => i != selected).Append(selected)
            .Select(i => new ScopeChange(plan.Stores[i], plan.Snapshots[i].Revision, documents[i])).ToArray();
        // CommitTogether checks every expected revision before this final
        // participant's callback. All writer leases remain held during deletion.
        changes[^1] = changes[^1] with { Validate = () =>
        {
            var again = Eligible(plan.Stores.ToArray(), plan.Snapshots.ToArray(), selected);
            if (!again.SequenceEqual(plan.Items)) throw new StorageException("cleanup-conflict", "Cambió la elegibilidad o el tamaño; repita clean.");
            var resources = documents[selected].GetValueOrDefault(Inventory)?["data"]?["resources"]?.AsArray() ?? [];
            foreach (var item in plan.Items)
            {
                var resource = resources.Single(r => r!["resource-id"]!.GetValue<string>() == item.ResourceId)!;
                var installations = resource["installations"]!.AsArray();
                var installation = installations.Single(i => i!["installation-id"]!.GetValue<string>() == item.InstallationId)!;
                var errors = new JsonArray();
                foreach (var file in installation["files"]!.AsArray())
                {
                    try
                    {
                        var path = OwnedPath(installation, file!);
                        if (!Exists(path)) continue;
                        freed += PhysicalDirectory.DeleteOwnedFile(path, file!["volume"]!.GetValue<string>(), file["file-id"]!.GetValue<string>());
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or StorageException)
                    {
                        var code = e is StorageException storage ? storage.Code : "delete-failed";
                        errors.Add((JsonNode?)JsonValue.Create(file!["path"]!.GetValue<string>() + ": " + code));
                        diagnostics.Add(item.InstallationId + ": " + code);
                    }
                }
                if (errors.Count == 0) installations.Remove(installation);
                else installation["cleanup-diagnostics"] = errors;
            }
        }};
        ScopeStore.CommitTogether(changes);
        return new(freed, diagnostics);
    }

    private ScopeStore[] Discover()
    {
        var stores = new List<ScopeStore>();
        void Add(ScopeContext scope)
        {
            scope.Ensure();
            if (!stores.Any(s => s.Context.PhysicalRoot == scope.PhysicalRoot)) stores.Add(new(scope));
        }
        Add(context); Add(ScopeContext.Global(home));
        var indexPath = home.PathFor("projects.json");
        if (File.Exists(indexPath))
            foreach (var entry in ProjectDocuments.Read(File.ReadAllBytes(indexPath), "project-index")["data"]!["projects"]!.AsArray())
            {
                var scope = ScopeContext.Project(Path.Combine(entry!["base"]!.GetValue<string>(), "coquena.yaml"));
                if (!Directory.Exists(scope.Root) || scope.PhysicalBase.Volume != entry["volume"]!.GetValue<string>() ||
                    scope.PhysicalBase.FileId != entry["file-id"]!.GetValue<string>()) Uncertain();
                var state = new ScopeStore(scope).ReadVerified();
                if (state.ScopeId != entry["scope-id"]!.GetValue<string>()) Uncertain();
                Add(scope);
            }
        for (var i = 0; i < stores.Count; i++)
        {
            var snapshot = stores[i].ReadVerified();
            void Visit(JsonNode? node)
            {
                if (node is JsonObject obj)
                {
                    foreach (var prefix in new[] { "source", "target" })
                        if (obj[prefix + "-base"] is JsonValue b && obj[prefix + "-root"] is JsonValue r)
                        {
                            var global = obj[prefix == "source" ? "source-global" : "global"]?.GetValue<bool>() ?? false;
                            var scope = ScopeContext.At(b.GetValue<string>(), r.GetValue<string>(), global);
                            if (!Directory.Exists(scope.Root)) Uncertain();
                            var other = new ScopeStore(scope).ReadVerified();
                            var expected = obj[prefix == "source" ? "scope-id" : "target-scope-id"]?.GetValue<string>();
                            if (expected is null || other.ScopeId != expected) Uncertain();
                            Add(scope);
                        }
                    foreach (var pair in obj) Visit(pair.Value);
                }
                else if (node is JsonArray array) foreach (var item in array) Visit(item);
            }
            foreach (var document in snapshot.Documents.Values)
                Visit(document["kind"]!.GetValue<string>() == "recipe-registry" ? RegistryEvidence(document["data"]!) : document["data"]);
        }
        return stores.ToArray();
    }

    private List<CleanupItem> Eligible(ScopeStore[] stores, ScopeSnapshot[] states, int selected, List<string>? diagnostics = null)
    {
        var evidence = new JsonArray();
        var external = new HashSet<DirectoryIdentity>();
        var externalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < stores.Length; i++)
        {
            foreach (var document in states[i].Documents.Values)
            {
                var kind = document["kind"]!.GetValue<string>();
                if (kind == "combination-index") evidence.Add(document.DeepClone());
                if (kind == "update-plan" && document["data"]?["upgrades"] is JsonNode upgrades) evidence.Add(upgrades.DeepClone());
                if (kind == "active" && document["data"]?["terminated"]?.GetValue<bool>() != true || kind == "last-good" && document["data"]?["forgotten"]?.GetValue<bool>() != true)
                    evidence.Add(document.DeepClone());
                if (kind == "recipe-registry") evidence.Add(RegistryEvidence(document["data"]!));
            }
            foreach (var resource in states[i].Documents.GetValueOrDefault(Inventory)?["data"]?["resources"]?.AsArray() ?? [])
                if (resource!["ownership"]!.GetValue<string>() == "external")
                    foreach (var installation in resource["installations"]!.AsArray())
                    {
                        var paths = installation!["files"]?.AsArray().Select(f => f!["path"]!.GetValue<string>()).ToList() ?? [];
                        if (installation["executable"] is JsonValue executable) paths.Add(executable.GetValue<string>());
                        foreach (var path in paths)
                        {
                            var absolute = Path.GetFullPath(path, stores[i].Context.Base);
                            externalPaths.Add(absolute);
                            if (Exists(absolute)) external.Add(PhysicalDirectory.Read(absolute));
                        }
                    }
            // Only unfinished transfers protect their source/replacement resource;
            // completed checkpoints are historical evidence, not eternal roots.
            if (Directory.Exists(stores[i].Context.PathFor("downloads")))
                foreach (var operation in Operations(stores[i].Context, states[i].ScopeId))
                    if (operation.State is not ("completed" or "cancelled") && operation.ScopePublication is { } publication)
                    {
                        evidence.Add((JsonNode?)JsonValue.Create(publication.ResourceId.ToString("D")));
                        evidence.Add((JsonNode?)JsonValue.Create(publication.InstallationId.ToString("D")));
                        if (publication.ExternalPrevious is not null) evidence.Add(publication.ExternalPrevious.DeepClone());
                    }
        }
        var result = new List<CleanupItem>();
        foreach (var resource in states[selected].Documents.GetValueOrDefault(Inventory)?["data"]?["resources"]?.AsArray() ?? [])
        {
            if (resource!["ownership"]!.GetValue<string>() != "managed") continue;
            var resourceId = resource["resource-id"]!.GetValue<string>();
            foreach (var installation in resource["installations"]!.AsArray())
            {
                var id = installation!["installation-id"]!.GetValue<string>();
                if (installation["protected"]?.GetValue<bool>() ?? installation["origin"]?.GetValue<string>() == "explicit") continue;
                if (Has(evidence, id) || HasResourceOnly(evidence, resourceId)) continue;
                long bytes = 0; var own = true;
                foreach (var file in installation["files"]!.AsArray())
                {
                    try
                    {
                        var path = OwnedPath(installation, file!);
                        if (!Exists(path)) continue;
                        var identity = PhysicalDirectory.Read(path);
                        if (externalPaths.Contains(path) || external.Contains(identity) || identity.Volume != file!["volume"]?.GetValue<string>() ||
                            identity.FileId != file["file-id"]?.GetValue<string>())
                        { diagnostics?.Add(id + ": file-ownership-uncertain-or-external"); own = false; break; }
                        bytes = checked(bytes + new FileInfo(path).Length);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or StorageException)
                    { diagnostics?.Add(id + ": " + (e is StorageException s ? s.Code : "file-inaccessible")); own = false; break; }
                }
                if (own) result.Add(new(resourceId, id, resource["kind"]!.GetValue<string>(), bytes));
            }
        }
        return result;
    }

    // Checkpoints are atomic envelopes. Read them against the already locked
    // scope snapshot; constructing the transfer engine would reacquire that lease.
    private static IEnumerable<OperationDocument> Operations(ScopeContext scope, string? scopeId)
    {
        foreach (var directory in Directory.EnumerateDirectories(scope.PathFor("downloads")))
        {
            var name = Path.GetFileName(directory);
            if (!Guid.TryParseExact(name, "D", out var id) || id == Guid.Empty || id.ToString("D") != name)
                throw new StorageException("invalid-operation-directory", "No se infiere ausencia de una operación desconocida.");
            var path = scope.PathFor($"downloads/{name}/operation.json");
            var data = ProjectDocuments.Read(File.ReadAllBytes(path), "operation")["data"]!;
            if (scopeId is null || data["scope-id"]?.GetValue<string>() != scopeId || data["operation-id"]?.GetValue<string>() != name ||
                data["transfer"] is not JsonObject transfer)
                throw new StorageException("transfer-scope-conflict", "Checkpoint de identidad incierta; se conserva el ámbito.");
            var operation = ContractJson.Read(transfer.ToJsonString(), ContractJsonContext.Default.OperationDocument, ContractValidation.Operation);
            if (operation.Id != id) throw new StorageException("operation-id-mismatch", "La operación no corresponde a su directorio.");
            yield return operation;
        }
    }

    private string OwnedPath(JsonNode installation, JsonNode file)
    {
        var location = installation["location"]!.GetValue<string>();
        var id = installation["installation-id"]!.GetValue<string>();
        if (location != "models/installations/" + id && location != "backends/installations/" + id)
            throw new StorageException("file-ownership-uncertain", "Ubicación de instalación no comprobada.");
        return context.PathFor(location + "/" + file["path"]!.GetValue<string>());
    }
    private static bool Exists(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new StorageException("file-ownership-uncertain", "Se conserva la ruta que no es un archivo regular.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    private static JsonNode RegistryEvidence(JsonNode data)
    {
        var copy = data.DeepClone();
        foreach (var recipe in copy["recipes"]!.AsArray())
        {
            recipe!.AsObject().Remove("last-publication-operation");
            foreach (var key in new[] { "publication", "publications" })
            {
                if (recipe[key] is JsonObject publication && publication["status"]?.GetValue<string>() == "withdrawn") recipe.AsObject().Remove(key);
                if (recipe[key] is JsonArray publications)
                    foreach (var p in publications.Where(p => p!["status"]?.GetValue<string>() == "withdrawn").ToArray()) publications.Remove(p);
            }
        }
        return copy;
    }
    internal static bool Has(JsonNode? node, string id) => node switch
    {
        JsonValue value => value.TryGetValue<string>(out var text) && text == id,
        JsonObject obj => obj.Any(p => Has(p.Value, id)), JsonArray array => array.Any(n => Has(n, id)), _ => false
    };
    private static bool HasResourceOnly(JsonNode? node, string id) => node switch
    {
        JsonObject obj => obj["resource-id"]?.GetValue<string>() == id && obj["installation-id"] is null || obj.Any(p => p.Key != "resource-id" && HasResourceOnly(p.Value, id)),
        JsonArray array => array.Any(n => HasResourceOnly(n, id)), JsonValue value => value.TryGetValue<string>(out var text) && text == id,
        _ => false
    };
    private static void Uncertain() => throw new StorageException("cleanup-reference-uncertain", "No se pudo comprobar un ámbito conocido; se conserva el inventario.");
}
