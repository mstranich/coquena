using System.Text.Json.Nodes;
using Coquena.Storage;
using Coquena.Contracts;

namespace Coquena.Projects;

public sealed record CleanupItem(string ResourceId, string InstallationId, string Kind, long Bytes, string Name = "");
// A kept or failed installation: its name for people, its ID and the reason as a code.
public sealed record CleanupNote(string Name, string InstallationId, string Code, UsageReference? Reference = null);
public sealed record CleanupPlan(IReadOnlyList<CleanupItem> Items, long Bytes,
    IReadOnlyList<ScopeStore> Stores, IReadOnlyList<ScopeSnapshot> Snapshots, IReadOnlyList<CleanupNote> Diagnostics,
    Guid? BackendInstallation = null, CleanupSelection? Selection = null,
    IReadOnlyList<CleanupDownload>? Downloads = null);
public sealed record CleanupDownload(Guid Id, string Name, string Kind, long Bytes, string Checkpoint);
public sealed record CleanupResult(long FreedBytes, IReadOnlyList<CleanupNote> Diagnostics);

// Only inventories prove ownership. Navigation and publication records identify
// the roots whose current evidence must be consulted, never absence of use.
public sealed class ResourceCleanup(ScopeContext context, Home home)
{
    private const string Inventory = "local/inventory.json";

    public CleanupPlan Preview(Guid? backendInstallation = null, CleanupSelection? selection = null)
    {
        // Previewing never creates folders: a scope without a root owns nothing.
        if (!Directory.Exists(context.Root)) return new([], 0, [], [], [], backendInstallation);
        var stores = Discover();
        return ScopeStore.Coordinated<CleanupPlan>(stores, states =>
        {
            var selected = Array.FindIndex(stores, s => s.Context.PhysicalRoot == context.PhysicalRoot);
            var diagnostics = new List<CleanupNote>();
            var evaluated = Evaluate(stores[selected].Context, states[selected], Collect(stores, states), selection ?? CleanupSelection.All, diagnostics);
            foreach (var resource in evaluated.Usage.Resources.Where(r => r.Status == UsageStatus.InUse))
                foreach (var reference in resource.References ?? []) diagnostics.Add(new(resource.Name, "", "resource-in-use", reference));
            var items = Select(evaluated.Items, backendInstallation);
            var downloads = backendInstallation is null ? evaluated.Usage.CleanupDownloads! : [];
            return new(items, items.Sum(i => i.Bytes) + downloads.Sum(d => d.Bytes), stores, states, diagnostics, backendInstallation, selection, downloads);
        });
    }

    public CleanupResult Apply(CleanupPlan plan)
    {
        if (plan.Items.Count + (plan.Downloads?.Count ?? 0) == 0) return new(0, []);
        var currentStores = Discover();
        if (!currentStores.Select(s => s.Context.PhysicalRoot).ToHashSet().SetEquals(plan.Stores.Select(s => s.Context.PhysicalRoot)))
            throw new StorageException("cleanup-conflict", Messages.CleanupScopesChanged);
        var selected = plan.Stores.ToList().FindIndex(s => s.Context.PhysicalRoot == context.PhysicalRoot);
        if (selected < 0) throw new ArgumentException(Messages.CleanupWrongScope);
        var documents = plan.Snapshots.Select(RecipeRegistry.Next).ToArray();
        var diagnostics = new List<CleanupNote>(); long freed = 0;
        var changes = Enumerable.Range(0, plan.Stores.Count).Where(i => i != selected).Append(selected)
            .Select(i => new ScopeChange(plan.Stores[i], plan.Snapshots[i].Revision, documents[i])).ToArray();
        // CommitTogether checks every expected revision before this final
        // participant's callback. All writer leases remain held during deletion.
        changes[^1] = changes[^1] with { Validate = () =>
        {
            var again = Filter(Select(Eligible(plan.Stores.ToArray(), plan.Snapshots.ToArray(), selected), plan.BackendInstallation),
                plan.Snapshots[selected], plan.Selection ?? CleanupSelection.All);
            if (!again.SequenceEqual(plan.Items)) throw new StorageException("cleanup-conflict", Messages.CleanupFilesChanged);
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
                        diagnostics.Add(new(item.Name, item.InstallationId, code));
                    }
                }
                if (errors.Count == 0)
                {
                    installations.Remove(installation);
                    if (installations.Count == 0) resources.Remove(resource);
                }
                else installation["cleanup-diagnostics"] = errors;
            }
        }};
        ScopeStore.CommitTogether(changes);
        foreach (var download in plan.Downloads ?? [])
        {
            try { freed += new Coquena.Downloads.ScopeTransfers(context).Clean(download); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or StorageException or Coquena.Downloads.DownloadException)
            {
                if (e is Coquena.Downloads.DownloadCleanupException partial) freed += partial.FreedBytes;
                diagnostics.Add(new(download.Name, download.Id.ToString("D"), e switch {
                    StorageException s => s.Code, Coquena.Downloads.DownloadException d => d.Code, _ => "delete-failed" }));
            }
        }
        return new(freed, diagnostics);
    }

    private static IReadOnlyList<CleanupItem> Filter(IReadOnlyList<CleanupItem> items, ScopeSnapshot state, CleanupSelection selection)
    {
        selection.Validate(state);
        if (!selection.IncludeInstallations) return [];
        var inventory = state.Documents.GetValueOrDefault(Inventory)?["data"]?["resources"]?.AsArray() ?? [];
        return items.Where(item => {
            var resource = inventory.Single(r => r!["resource-id"]!.ToString() == item.ResourceId)!;
            var installation = resource["installations"]!.AsArray().Single(i => i!["installation-id"]!.ToString() == item.InstallationId);
            return selection.Matches(item.Kind, item.Name, resource, installation, state);
        }).ToArray();
    }

    private static IReadOnlyList<CleanupDownload> Downloads(ScopeContext scope, ScopeSnapshot state, CleanupSelection selection)
    {
        if (!selection.Downloads || !Directory.Exists(scope.PathFor("downloads"))) return [];
        var results = new List<CleanupDownload>();
        foreach (var operation in Operations(scope, state.ScopeId))
        {
            var installation = new JsonObject { ["selection"] = new JsonObject {
                ["backendSelection"] = operation.BackendSelection is null ? null : System.Text.Json.JsonSerializer.SerializeToNode(operation.BackendSelection, ContractJsonContext.Default.BackendDownloadSelection) } };
            if (!selection.Matches(operation.ResourceKind, operation.Name, null, installation, state)) continue;
            // Never stop a live executor or remove a physical publication through maintenance.
            var destination = operation.ScopePublication is { } publication ? scope.PathFor(
                $"{(operation.ResourceKind == "backend" ? "backends" : "models")}/installations/{publication.InstallationId:D}") : null;
            if (operation.ScopePublication is null || operation.Phase is "publishing" or "cancelling-publication") continue;
            var published = state.Documents.GetValueOrDefault(Inventory)?["data"]?["resources"]?.AsArray()
                .Where(r => r!["resource-id"]!.ToString() == operation.ScopePublication.ResourceId.ToString("D"))
                .SelectMany(r => r!["installations"]!.AsArray()).Any(i => i!["installation-id"]!.ToString() == operation.ScopePublication.InstallationId.ToString("D") &&
                    i["operation-id"]?.ToString() == operation.Id.ToString("D")) == true;
            if (destination is not null && Directory.Exists(destination) && !published) continue;
            if (!Coquena.Downloads.DownloadManager.Idle(scope, operation.Id)) continue;
            var checkpoint = System.Text.Json.JsonSerializer.Serialize(operation, ContractJsonContext.Default.OperationDocument);
            results.Add(new(operation.Id, operation.Name, operation.ResourceKind, DiskUsage.FolderSize(scope, $"downloads/{operation.Id:D}"), checkpoint));
        }
        return results;
    }

    private static IReadOnlyList<CleanupItem> Select(IReadOnlyList<CleanupItem> items, Guid? backendInstallation) =>
        backendInstallation is null ? items :
        items.Where(item => item.Kind == "backend" && Guid.Parse(item.InstallationId) == backendInstallation).ToArray();

    private ScopeStore[] Discover(List<string>? skipped = null)
    {
        skipped ??= [];
        var stores = new List<ScopeStore>();
        void Add(ScopeContext scope)
        {
            if (!Directory.Exists(scope.Root)) return;
            scope.Ensure();
            if (!stores.Any(s => s.Context.PhysicalRoot == scope.PhysicalRoot)) stores.Add(new(scope));
        }
        Add(context);
        if (Directory.Exists(home.Root)) Add(ScopeContext.At(home.Root, Path.Combine(home.Root, "global"), true));
        var indexPath = home.PathFor("projects.json");
        if (File.Exists(indexPath))
            foreach (var entry in ProjectDocuments.Read(SharedFile.ReadAllBytes(indexPath), "project-index")["data"]!["projects"]!.AsArray())
            try
            {
                var scope = ScopeContext.Project(Path.Combine(entry!["base"]!.GetValue<string>(), "coquena.yaml"));
                if (!Directory.Exists(scope.Root) || scope.PhysicalBase.Volume != entry["volume"]!.GetValue<string>() ||
                    scope.PhysicalBase.FileId != entry["file-id"]!.GetValue<string>()) Uncertain();
                var state = new ScopeStore(scope).ReadVerified();
                if (state.ScopeId != entry["scope-id"]!.GetValue<string>()) Uncertain();
                Add(scope);
            }
            // The navigation index does not prove a resource reference.
            catch (Exception e) when (e is StorageException or IOException or UnauthorizedAccessException)
            { skipped.Add(entry!["base"]!.GetValue<string>()); }
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
                            try
                            {
                                var scope = ScopeContext.At(b.GetValue<string>(), r.GetValue<string>(), global);
                                if (!Directory.Exists(scope.Root)) Uncertain();
                                var other = new ScopeStore(scope).ReadVerified();
                                var expected = obj[prefix == "source" ? "scope-id" : "target-scope-id"]?.GetValue<string>();
                                if (expected is null || other.ScopeId != expected) Uncertain();
                                Add(scope);
                            }
                            catch (Exception e) when (e is StorageException or IOException or UnauthorizedAccessException)
                            {
                                // Keep concrete recorded references in Collect; unrelated files remain reclaimable.
                                skipped.Add(b.GetValue<string>());
                            }
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


    // Every installation Coquena knows, in every reachable scope, with its size and
    // whether prune may free it: not protected and not referenced by any recipe,
    // preset, service, plan or unfinished transfer.
    public UsageSurvey Survey(CleanupSelection? selection = null)
    {
        var skipped = new List<string>();
        var stores = Discover(skipped);
        return ScopeStore.Coordinated<UsageSurvey>(stores, states =>
        {
            var evidence = Collect(stores, states);
            var scopes = stores.Select((store, i) => Evaluate(store.Context, states[i], evidence,
                selection ?? CleanupSelection.All).Usage).ToArray();
            return new(scopes, skipped);
        });
    }

    private static (IReadOnlyList<CleanupItem> Items, ScopeUsage Usage) Evaluate(ScopeContext scope, ScopeSnapshot state,
        Evidence evidence, CleanupSelection filter, List<CleanupNote>? diagnostics = null)
    {
        var items = Filter(Owned(scope, state, evidence.References, evidence.External, evidence.ExternalPaths, diagnostics), state, filter);
        var free = items.Select(x => x.InstallationId).ToHashSet();
        var resources = new List<UsageResource>();
        foreach (var resource in state.Documents.GetValueOrDefault(Inventory)?["data"]?["resources"]?.AsArray() ?? [])
        {
            var kind = resource!["kind"]!.GetValue<string>();
            var name = resource["logical-key"]?.GetValue<string>() ?? kind;
            var externalOwned = resource["ownership"]!.GetValue<string>() == "external";
            foreach (var installation in resource["installations"]!.AsArray())
            {
                if (!filter.IncludeInstallations || !filter.Matches(kind, name, resource, installation, state)) continue;
                var id = installation!["installation-id"]!.GetValue<string>();
                long bytes = 0;
                var paths = installation["files"]?.AsArray().Select(f => externalOwned
                    ? Path.GetFullPath(f!["path"]!.GetValue<string>(), scope.Base) : SafeOwnedPath(scope, installation, f!)).ToList() ?? [];
                if (externalOwned && installation["executable"] is JsonValue executable) paths.Add(Path.GetFullPath(executable.GetValue<string>(), scope.Base));
                foreach (var path in paths.OfType<string>())
                    try { if (File.Exists(path)) bytes += new FileInfo(path).Length; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                var status = externalOwned ? UsageStatus.External : free.Contains(id) ? UsageStatus.Free :
                    installation["protected"]?.GetValue<bool>() ?? installation["origin"]?.GetValue<string>() == "explicit" ? UsageStatus.Protected :
                    Has(evidence.References, id) || HasResourceOnly(evidence.References, resource["resource-id"]!.GetValue<string>()) ? UsageStatus.InUse : UsageStatus.Kept;
                var references = evidence.Sources.Where(s => Has(s.Data, id) || HasResourceOnly(s.Data, resource["resource-id"]!.ToString()))
                    .Select(s => new UsageReference(s.Scope, s.Kind)).Distinct().ToArray();
                resources.Add(new(kind, name, bytes, status, references, id));
            }
        }
        var downloads = Downloads(scope, state, filter);
        var downloadBytes = filter.IsUnfiltered ? DiskUsage.FolderSize(scope, "downloads") :
            filter.Downloads && Directory.Exists(scope.PathFor("downloads")) ? Operations(scope, state.ScopeId).Where(o => filter.Matches(o.ResourceKind, o.Name, null,
                new JsonObject { ["selection"] = new JsonObject { ["backendSelection"] = o.BackendSelection is null ? null :
                    System.Text.Json.JsonSerializer.SerializeToNode(o.BackendSelection, ContractJsonContext.Default.BackendDownloadSelection) } }, state))
                .Sum(o => DiskUsage.FolderSize(scope, $"downloads/{o.Id:D}")) : 0;
        return (items, new(scope, resources, downloadBytes,
            filter.IsUnfiltered ? DiskUsage.FolderSize(scope, "logs") : 0,
            downloads));
    }

    private static string? SafeOwnedPath(ScopeContext scope, JsonNode installation, JsonNode file)
    {
        try { return OwnedPath(scope, installation, file); }
        catch (StorageException) { return null; }
    }
    private sealed record ReferenceEvidence(JsonNode Data, string Scope, string Kind);
    private sealed record Evidence(JsonArray References, HashSet<DirectoryIdentity> External, HashSet<string> ExternalPaths, IReadOnlyList<ReferenceEvidence> Sources);

    private List<CleanupItem> Eligible(ScopeStore[] stores, ScopeSnapshot[] states, int selected, List<CleanupNote>? diagnostics = null)
    {
        var evidence = Collect(stores, states);
        return Owned(stores[selected].Context, states[selected], evidence.References, evidence.External, evidence.ExternalPaths, diagnostics);
    }

    private static Evidence Collect(ScopeStore[] stores, ScopeSnapshot[] states)
    {
        var evidence = new JsonArray();
        var external = new HashSet<DirectoryIdentity>();
        var externalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<ReferenceEvidence>();
        for (var i = 0; i < stores.Length; i++)
        {
            void Add(JsonNode node, string kind)
            {
                evidence.Add(node);
                sources.Add(new(node, stores[i].Context.Base, kind));
            }
            foreach (var document in states[i].Documents.Values)
            {
                var kind = document["kind"]!.GetValue<string>();
                if (kind == "combination-index") Add(document.DeepClone(), kind);
                if (kind == "update-plan" && document["data"]?["upgrades"] is JsonNode upgrades) Add(upgrades.DeepClone(), kind);
                if (kind == "active" && document["data"]?["terminated"]?.GetValue<bool>() != true || kind == "last-good" && document["data"]?["forgotten"]?.GetValue<bool>() != true)
                    Add(document.DeepClone(), kind);
                if (kind == "recipe-registry") Add(RegistryEvidence(document["data"]!, stores, states), kind);
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
                        Add(new JsonArray(JsonValue.Create(publication.ResourceId.ToString("D")), JsonValue.Create(publication.InstallationId.ToString("D")),
                            publication.ExternalPrevious?.DeepClone()), "download");
                    }
        }
        return new(evidence, external, externalPaths, sources);
    }

    private static List<CleanupItem> Owned(ScopeContext scope, ScopeSnapshot state, JsonArray evidence,
        HashSet<DirectoryIdentity> external, HashSet<string> externalPaths, List<CleanupNote>? diagnostics)
    {
        var result = new List<CleanupItem>();
        foreach (var resource in state.Documents.GetValueOrDefault(Inventory)?["data"]?["resources"]?.AsArray() ?? [])
        {
            if (resource!["ownership"]!.GetValue<string>() != "managed") continue;
            var resourceId = resource["resource-id"]!.GetValue<string>();
            var name = resource["logical-key"]?.GetValue<string>() ?? resource["kind"]!.GetValue<string>();
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
                        var path = OwnedPath(scope, installation, file!);
                        if (!Exists(path)) continue;
                        var identity = PhysicalDirectory.Read(path);
                        if (externalPaths.Contains(path) || external.Contains(identity) || identity.Volume != file!["volume"]?.GetValue<string>() ||
                            identity.FileId != file["file-id"]?.GetValue<string>())
                        { diagnostics?.Add(new(name, id, "file-ownership-uncertain-or-external")); own = false; break; }
                        bytes = checked(bytes + new FileInfo(path).Length);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or StorageException)
                    { diagnostics?.Add(new(name, id, e is StorageException s ? s.Code : "file-inaccessible")); own = false; break; }
                }
                if (own) result.Add(new(resourceId, id, resource["kind"]!.GetValue<string>(), bytes, name));
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
                throw new StorageException("invalid-operation-directory", Messages.CleanupUnknownOperation);
            var path = scope.PathFor($"downloads/{name}/operation.json");
            var data = ProjectDocuments.Read(SharedFile.ReadAllBytes(path), "operation")["data"]!;
            if (scopeId is null || data["scope-id"]?.GetValue<string>() != scopeId || data["operation-id"]?.GetValue<string>() != name ||
                data["transfer"] is not JsonObject transfer)
                throw new StorageException("transfer-scope-conflict", Messages.CleanupTransferIdentityUnknown);
            var operation = ContractJson.Read(transfer.ToJsonString(), ContractJsonContext.Default.OperationDocument, ContractValidation.Operation);
            if (operation.Id != id) throw new StorageException("operation-id-mismatch", Messages.CleanupOperationDirectoryMismatch);
            yield return operation;
        }
    }

    private string OwnedPath(JsonNode installation, JsonNode file) => OwnedPath(context, installation, file);
    private static string OwnedPath(ScopeContext context, JsonNode installation, JsonNode file)
    {
        var location = installation["location"]!.GetValue<string>();
        var id = installation["installation-id"]!.GetValue<string>();
        if (location != "models/installations/" + id && location != "backends/installations/" + id)
            throw new StorageException("file-ownership-uncertain", Messages.CleanupInstallationLocationUnknown);
        return context.PathFor(location + "/" + file["path"]!.GetValue<string>());
    }
    private static bool Exists(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new StorageException("file-ownership-uncertain", Messages.CleanupNotRegularFile);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    private static JsonNode RegistryEvidence(JsonNode data, ScopeStore[]? stores = null, ScopeSnapshot[]? states = null)
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
        bool MissingEndpoint(JsonObject node, string prefix)
        {
            if (node[prefix + "-root"] is not JsonValue value) return false;
            var root = value.GetValue<string>();
            try { _ = File.GetAttributes(root); }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
            // A publication to a service explicitly removed from a verified scope is obsolete.
            if (prefix == "target" && stores is not null && states is not null && node["service-id"] is JsonValue service)
            {
                var index = Array.FindIndex(stores, s => string.Equals(s.Context.Root, root, StringComparison.OrdinalIgnoreCase));
                if (index >= 0 && node["target-scope-id"]?.ToString() == states[index].ScopeId)
                    return !(states[index].Documents.GetValueOrDefault("local/recipes.json")?["data"]?["recipes"]?.AsArray() ?? [])
                        .SelectMany(r => r!["entities"]?.AsArray() ?? []).Any(e => e?["kind"]?.ToString() == "service" && e["id"]?.ToString() == service.ToString());
            }
            return false;
        }
        bool PruneDead(JsonNode? node, bool stoppedService = false)
        {
            if (node is JsonObject obj)
            {
                if (MissingEndpoint(obj, "target") || stoppedService && MissingEndpoint(obj, "source")) return true;
                if (obj["kind"]?.ToString() == "service")
                    stoppedService = obj["instance"] is null && obj["publication-operation"] is null;
                foreach (var key in obj.Select(p => p.Key).ToArray())
                    if (PruneDead(obj[key], stoppedService)) obj.Remove(key);
            }
            else if (node is JsonArray array)
                foreach (var child in array.ToArray()) if (PruneDead(child, stoppedService)) array.Remove(child);
            return false;
        }
        PruneDead(copy);
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
    private static void Uncertain() => throw new StorageException("cleanup-reference-uncertain", Messages.CleanupProjectUnreadable);
}
