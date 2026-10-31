using System.Text.Json.Nodes;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Models;

public sealed record ModelIntent(string Key, string Kind, ModelRequest? Request, string Revision, string[] Files, string? LocalType = null, string? ProjectName = null)
{
    public static ModelIntent[] FromRecipe(EffectiveRecipe recipe)
    {
        var result = new List<ModelIntent>();
        foreach (var group in new[] { "models", "companions" })
        {
            var entries = recipe.Root.Get(group);
            if (entries is null) continue;
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in entries.Fields)
            {
                if (!keys.Add(key)) throw new ModelSourceException("alias-collision", "Aliases que sólo difieren en capitalización.");
                ModelRequest request;
                if (value.Kind == RecipeKind.String) request = ModelRequest.Parse(value.Text!);
                else
                {
                    if (value.Get("repository")?.Text is not string repository)
                    {
                        var localFiles = value.Get("files")!.Items.Select(f => f.Text!).ToArray();
                        result.Add(new(key, group == "models" ? "model" : "companion", null, "", localFiles, value.Get("type")?.Text, recipe.Name));
                        continue;
                    }
                    var type = value.Get("type")?.Text;
                    var quant = value.Get("quantization")?.Text;
                    request = ModelRequest.Parse(repository + (type is null ? "" : "#" + type) + (quant is null ? "" : ":" + quant));
                }
                if ((group == "companions") != (request.CompanionType is not null))
                    throw new ModelSourceException("invalid-role", "El selector no corresponde al grupo de recursos.");
                var files = value.Get("files")?.Items.Select(f => f.Text!).ToArray() ?? [];
                foreach (var file in files) HuggingFaceStore.ValidatePath(file);
                result.Add(new(key, group == "models" ? "model" : "companion", request, value.Get("revision")?.Text ?? "main", files));
            }
        }
        return result.ToArray();
    }
}

public sealed record PreparedModel(string ResourceId, string InstallationId, string EntryPath);
public sealed record ModelRemoval(bool Complete, long FreedBytes, string[] Pending);

// Canonical inventory and immutable physical revisions are independent of recipe aliases.
public sealed partial class ScopedModels(ScopeContext context)
{
    private readonly ScopeStore store = new(context);
    private ScopeTransfers? transfers;
    public ScopeTransfers Transfers => transfers ??= new(context);
    private static void Fail(string code) => throw new ModelSourceException(code, "Operación de modelo no confirmada; se conserva el estado del ámbito.");
    public JsonArray List(bool companion = false)
        => List(store.Read(), companion);
    private JsonArray List(ScopeSnapshot snapshot, bool companion)
    {
        var resources = new JsonArray((snapshot.Documents.GetValueOrDefault("local/inventory.json")?["data"]?["resources"]?.AsArray() ?? [])
            .Where(r => r!["kind"]!.GetValue<string>() == (companion ? "companion" : "model")).Select(r => r!.DeepClone()).ToArray());
        try
        {
            if (resources.Select(r => r!["logical-key"]!.GetValue<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != resources.Count)
                ProjectDocuments.Invalid();
            foreach (var resource in resources)
                foreach (var installation in resource!["installations"]!.AsArray())
                {
                    ProjectDocuments.Uuid(installation!["installation-id"]);
                    if (resource["ownership"]!.GetValue<string>() != "managed")
                    { _ = ExternalPaths(installation); continue; }
                    _ = context.PathFor(installation["location"]!.GetValue<string>());
                    var selection = ReadSelection(installation);
                    ContractValidation.Remote(selection);
                    if (installation["files"] is not JsonArray { Count: > 0 }) ProjectDocuments.Invalid();
                    foreach (var file in installation["files"]!.AsArray())
                    {
                        _ = context.PathFor(installation["location"]!.GetValue<string>() + "/" + file!["path"]!.GetValue<string>());
                        if (file["size"]!.GetValue<long>() < 0) ProjectDocuments.Invalid();
                    }
                }
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException or FormatException or ArgumentException)
        { throw new StorageException("invalid-project-document", "Inventario de modelos inválido; se conserva sin sobrescribir.", inner: e); }
        return resources;
    }
    public JsonArray Query(bool companion = false)
    {
        var resources = new JsonArray(List(companion).Select(r => (JsonNode?)Observe(r!.AsObject())).ToArray());
        // Observation never constructs a transfer engine for a pristine scope.
        if (!Directory.Exists(context.PathFor("downloads"))) return resources;
        if (!File.Exists(context.PathFor("local/state.json")))
        {
            if (Directory.GetFileSystemEntries(context.PathFor("downloads")).Length != 0)
                throw new StorageException("missing-state-pointer", "Hay transferencias sin identidad de ámbito.");
            return resources;
        }
        foreach (var operation in Transfers.Downloads.List(companion ? "companion" : "model"))
        {
            var resource = resources.FirstOrDefault(r => r!["logical-key"]!.GetValue<string>().Equals(operation.Name, StringComparison.OrdinalIgnoreCase));
            if (resource is null)
            {
                resource = new JsonObject { ["logical-key"] = operation.Name, ["kind"] = operation.ResourceKind, ["installations"] = new JsonArray() };
                resources.Add(resource);
            }
            if (resource["operations"] is not JsonArray) resource["operations"] = new JsonArray();
            resource["operations"]!.AsArray().Add(JsonNode.Parse(ContractJson.Write(operation, ContractJsonContext.Default.OperationDocument)));
        }
        return resources;
    }
    public string ResolveAlias(string key, bool companion)
    {
        var aliases = store.Read().Documents.GetValueOrDefault("local/inventory.json")?["data"]?["model-aliases"]?.AsArray() ?? [];
        return aliases.SingleOrDefault(a => a!["kind"]!.GetValue<string>() == (companion ? "companion" : "model") &&
            a["key"]!.GetValue<string>().Equals(key, StringComparison.OrdinalIgnoreCase))?["resource-id"]?.GetValue<string>()
            ?? throw new ModelSourceException("unknown-alias", "Alias no preparado en el ámbito seleccionado.");
    }
    public async Task<SelectedModel?> SelectAsync(ModelIntent intent, HuggingFaceStore source, IModelSelectionInteraction interaction,
        IRemoteGgufInspector? inspector = null, CancellationToken cancellation = default)
    {
        if (intent.Request is null) throw new ModelSourceException("local-resource-required", "La selección local se registra sin consultar el origen remoto.");
        if (intent.Files.Length == 0)
            return await new ModelSelectionMenu(source, inspector).SelectAsync(intent.Request, interaction, cancellation, intent.Revision);
        var revision = await source.ResolveAsync(intent.Request.Repository, intent.Revision, cancellation);
        var inventory = await source.InventoryAsync(revision, cancellation);
        if (intent.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != intent.Files.Length) Fail("selection-conflict");
        var files = intent.Files.Select(path => inventory.Files.SingleOrDefault(f => f.Path == path)
            ?? throw new ModelSourceException("remote-file-missing", "Archivo seleccionado ausente en el commit.")).ToArray();
        using var remote = inspector is null ? new RemoteGgufInspector() : null;
        var evidence = await new ModelResolver(inspector ?? remote!).ValidateAsync(revision, new(files), interaction.Show, cancellation);
        var variant = ModelSelection.Variant(new(files), evidence);
        if (variant is null || variant.CompanionType != intent.Request.CompanionType ||
            intent.Request.Quantization is not null && !variant.Quantization.Equals(intent.Request.Quantization, StringComparison.OrdinalIgnoreCase))
            Fail("selection-conflict");
        return new(variant!.Name(revision.Repository), variant.CompanionType,
            new(revision.Repository, revision.RequestedRevision, revision.Commit, files.Select(f => f.ToRemoteFile()).ToArray()));
    }
    public async Task<PreparedModel> PrepareAsync(ModelIntent intent, HuggingFaceStore source, IDownloadTransport transport,
        IModelSelectionInteraction interaction, IRemoteGgufInspector? inspector = null, CancellationToken cancellation = default)
    {
        if (intent.Request is null) { cancellation.ThrowIfCancellationRequested(); return PrepareLocal(intent, allowNewRevision: true); }
        var selected = await SelectAsync(intent, source, interaction, inspector, cancellation)
            ?? throw new OperationCanceledException(cancellation);
        var prepared = await AcquireAsync(selected, transport, false, cancellation: cancellation);
        Bind(intent.Key, intent.Kind, selected, prepared);
        return prepared;
    }
    public void Bind(string key, string kind, SelectedModel selected, PreparedModel prepared)
    {
        var snapshot = store.Read(); var docs = RecipeRegistry.Next(snapshot);
        var inventory = docs["local/inventory.json"]["data"]!;
        var resource = inventory["resources"]!.AsArray().SingleOrDefault(r => r!["resource-id"]!.GetValue<string>() == prepared.ResourceId);
        if (resource is null || resource["removal-pending"]?.GetValue<bool>() == true ||
            resource["kind"]!.GetValue<string>() != kind || !resource["logical-key"]!.GetValue<string>().Equals(selected.Name, StringComparison.OrdinalIgnoreCase) ||
            !resource["installations"]!.AsArray().Any(i => i!["installation-id"]!.GetValue<string>() == prepared.InstallationId && Same(ReadSelection(i), selected.Selection))) Fail("stale-model-binding");
        var aliases = inventory["model-aliases"] as JsonArray;
        if (aliases is null) { aliases = new JsonArray(); inventory["model-aliases"] = aliases; }
        var existing = aliases.SingleOrDefault(a => a!["kind"]!.GetValue<string>() == kind && a["key"]!.GetValue<string>().Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) aliases.Remove(existing);
        aliases.Add((JsonNode)new JsonObject { ["key"] = key, ["kind"] = kind, ["resource-id"] = prepared.ResourceId,
            ["installation-id"] = prepared.InstallationId });
        store.Commit(snapshot.Revision, docs);
    }
    public async Task<PreparedModel> AcquireAsync(SelectedModel selected, IDownloadTransport transport,
        bool explicitlyRequested = true, Action<OperationDocument>? progress = null, CancellationToken cancellation = default,
        bool replaceExternal = false, bool restoreMissing = false)
    {
        var request = ModelRequest.Parse(selected.Name);
        if (request.Quantization is null || request.CompanionType != selected.CompanionType || request.Repository != selected.Selection.Repository) Fail("invalid-selection");
        ContractValidation.Remote(selected.Selection);
        var resources = List(selected.CompanionType is not null);
        var resource = resources.SingleOrDefault(r => r!["logical-key"]!.GetValue<string>().Equals(selected.Name, StringComparison.OrdinalIgnoreCase));
        if (resource is not null)
        {
            if (resource["removal-pending"]?.GetValue<bool>() == true) Fail("removal-pending");
            if (resource["ownership"]!.GetValue<string>() != "managed" && !replaceExternal) Fail("resource-ownership-conflict");
            if (resource["ownership"]!.GetValue<string>() == "external") EnsureInactive(resource);
            foreach (var installation in resource["ownership"]!.GetValue<string>() == "managed" ? resource["installations"]!.AsArray().Reverse() : [])
            {
                var prior = ReadSelection(installation!);
                if (prior.Commit == selected.Selection.Commit && !Same(prior, selected.Selection)) Fail("canonical-selection-collision");
                if (!Same(prior, selected.Selection)) continue;
                if (installation!["integrity-status"]?.GetValue<string>() is "invalid" or "mismatch")
                {
                    if (explicitlyRequested) continue;
                    Fail("model-integrity-invalid");
                }
                var observed = ObserveInstallation(installation!);
                if (observed["observed-availability"]!.GetValue<string>() != "available")
                {
                    if (restoreMissing && Paths(resource, installation!).Any(p => !File.Exists(p))) continue;
                    Fail("model-unavailable");
                }
                var prepared = Prepared(resource, installation!);
                if (explicitlyRequested) Protect(prepared.InstallationId, true);
                return prepared;
            }
        }
        // A pending selection reserves its canonical name; resume it through download.
        if (Directory.Exists(context.PathFor("downloads")) && Transfers.Downloads.List().Any(o => o.Name.Equals(selected.Name, StringComparison.OrdinalIgnoreCase)))
            Fail("model-reserved");
        var id = Guid.NewGuid();
        var name = resource?["logical-key"]!.GetValue<string>() ?? selected.Name;
        var operation = Transfers.Create(new(1, id, selected.CompanionType is null ? "model" : "companion", name,
            selected.Selection, null, selected.Selection.Files.Select((f, i) => new TransferRecord(f.Path,
                $"downloads/{id:D}/files/{i}.part", "models/" + f.Path, 0, f.Size, f.Identity, null, null, "pending")).ToArray(),
            "pending", "transfer", DateTimeOffset.UtcNow, null), explicitlyRequested ? "explicit" : "automatic",
            externalPrevious: resource?["ownership"]?.GetValue<string>() == "external" ? resource.AsObject() : null);
        progress?.Invoke(operation);
        var result = await Transfers.RunAsync(operation.Id, transport, progress, cancellation);
        if (result is not null) throw new DownloadException(result.State == "failed" ? "model-transfer-failed" : "model-transfer-interrupted", "Continuar con download resume en el mismo ámbito.");
        resource = List(selected.CompanionType is not null).Single(r => r!["resource-id"]!.GetValue<string>() == operation.ScopePublication!.ResourceId.ToString("D"));
        return Prepared(resource!, resource!["installations"]!.AsArray().Single(i => i!["installation-id"]!.GetValue<string>() == operation.ScopePublication!.InstallationId.ToString("D"))!);
    }
    private static bool Same(RemoteSelection a, RemoteSelection b) => a.Repository == b.Repository && a.Commit == b.Commit &&
        a.Files.SequenceEqual(b.Files);
    private static RemoteSelection ReadSelection(JsonNode installation)
    {
        var selection = installation["selection"]!["modelSelection"]!;
        var files = selection["files"]!.AsArray().Select(f => new RemoteFile(f!["path"]!.GetValue<string>(),
            f["size"]?.GetValue<long>(), f["identity"] is not JsonObject hash ? null : new ContentIdentity(
                hash["algorithm"]!.GetValue<string>(), hash["domain"]!.GetValue<string>(), hash["value"]!.GetValue<string>()))).ToArray();
        return new(selection["repository"]!.GetValue<string>(), selection["requestedRevision"]!.GetValue<string>(),
            selection["commit"]!.GetValue<string>(), files);
    }
    private PreparedModel Prepared(JsonNode resource, JsonNode installation) => new(resource["resource-id"]!.GetValue<string>(),
        installation["installation-id"]!.GetValue<string>(), context.PathFor(installation["location"]!.GetValue<string>() + "/" + ReadSelection(installation).Files[0].Path));
    public JsonObject Observe(JsonObject resource)
    {
        var result = (JsonObject)resource.DeepClone();
        result["identity"] = Identity(result);
        foreach (var installation in result["installations"]!.AsArray())
        {
            var observed = ObserveInstallation(installation!);
            foreach (var pair in observed) installation![pair.Key] = pair.Value?.DeepClone();
            if (result["removal-pending"]?.GetValue<bool>() == true) installation!["observed-availability"] = "removal-pending";
        }
        return result;
    }
    public string ResolveKey(string selector, bool companion = false) => List(companion)
        .SingleOrDefault(r => Identity(r!).Equals(selector, StringComparison.OrdinalIgnoreCase))?["logical-key"]?.GetValue<string>() ?? selector;
    private string Identity(JsonNode resource)
    {
        var key = resource["logical-key"]!.GetValue<string>();
        return context.IsGlobal || key.Contains('/') ? key :
            (resource["project-name"]?.GetValue<string>() ?? Path.GetFileName(context.Base)) + "/" + key;
    }
    private JsonObject ObserveInstallation(JsonNode installation)
    {
        var paths = installation["external"]?.GetValue<bool>() == true ? ExternalPaths(installation) :
            installation["files"]!.AsArray().Select(f => context.PathFor(installation["location"]!.GetValue<string>() + "/" + f!["path"]!.GetValue<string>())).ToArray();
        var unit = GgufInspection.InspectPaths(paths);
        var status = unit.Status switch { "complete" => "available", "incomplete" => paths.Length > 1 ? "incomplete" : "inaccessible", "missing" => "inaccessible", "read-error" => "inaccessible", _ => "invalid" };
        var changed = installation["files"]!.AsArray().Select((f, i) => (File: f!, Path: paths[i])).Any(p => File.Exists(p.Path) &&
            (new FileInfo(p.Path).Length != p.File["size"]!.GetValue<long>() || p.File["last-write-utc"] is not null &&
            File.GetLastWriteTimeUtc(p.Path).Ticks != p.File["last-write-utc"]!.GetValue<long>()));
        return new() { ["observed-availability"] = status, ["changed"] = changed };
    }
    public void Protect(string installationId, bool protect)
    {
        var snapshot = store.Read(); var docs = RecipeRegistry.Next(snapshot);
        if (Directory.Exists(context.PathFor("downloads")) && Transfers.Downloads.List().Any(o =>
            o.ScopePublication?.ExternalPrevious?["installations"]?.AsArray().Any(i => i!["installation-id"]!.GetValue<string>() == installationId) == true))
            Fail("resource-operation-pending");
        var matches = docs.GetValueOrDefault("local/inventory.json")?["data"]?["resources"]?.AsArray()
            .Where(r => r!["kind"]!.GetValue<string>() is "model" or "companion").SelectMany(r => r!["installations"]!.AsArray())
            .Where(i => i!["installation-id"]!.GetValue<string>() == installationId).ToArray() ?? [];
        if (matches.Length != 1) Fail("unknown-installation");
        matches[0]!["protected"] = protect;
        store.Commit(snapshot.Revision, docs);
    }
    public ModelRemoval Remove(string nameOrId, bool companion)
    {
        nameOrId = ResolveKey(nameOrId, companion);
        _ = List(companion); // Validate recorded paths and selections before persisting withdrawal.
        var snapshot = store.Read(); var docs = RecipeRegistry.Next(snapshot);
        var inventory = docs.GetValueOrDefault("local/inventory.json")?["data"]?["resources"]?.AsArray();
        var aliasId = docs.GetValueOrDefault("local/inventory.json")?["data"]?["model-aliases"]?.AsArray()
            .SingleOrDefault(a => a!["kind"]!.GetValue<string>() == (companion ? "companion" : "model") &&
                a["key"]!.GetValue<string>().Equals(nameOrId, StringComparison.OrdinalIgnoreCase))?["resource-id"]?.GetValue<string>();
        var resource = inventory?.SingleOrDefault(r => r!["kind"]!.GetValue<string>() == (companion ? "companion" : "model") &&
            (r["resource-id"]!.GetValue<string>() == (aliasId ?? nameOrId) || aliasId is null && r["logical-key"]!.GetValue<string>().Equals(nameOrId, StringComparison.OrdinalIgnoreCase)));
        if (resource is null) Fail("unknown-resource");
        var resourceId = resource!["resource-id"]!.GetValue<string>();
        // Active evidence blocks withdrawal even when process liveness is uncertain.
        var identities = new[] { resourceId }.Concat(resource["installations"]!.AsArray().Select(i => i!["installation-id"]!.GetValue<string>())).ToArray();
        foreach (var active in snapshot.ProtectedRuntimeDocuments())
            if (identities.Any(id => Contains(active, id))) UsageFailure(active);
        if (snapshot.Documents.TryGetValue("local/combinations.json", out var combinations) && identities.Any(id => Contains(combinations, id))) UsageFailure(combinations);
        if (snapshot.Documents.TryGetValue("local/recipes.json", out var publicationRegistry))
            foreach (var recipe in publicationRegistry["data"]!["recipes"]!.AsArray())
                foreach (var key in new[] { "publication", "publications", "publication-operation", "publication-previous" })
                    if (recipe![key] is JsonNode publication && identities.Any(id => Contains(publication, id))) UsageFailure(publication);
        if (snapshot.Documents.TryGetValue("local/recipes.json", out var registry) && Contains(registry, resourceId) &&
            registry["data"]!["recipes"]!.AsArray().SelectMany(r => r!["entities"]!.AsArray()).Any(e => e!["instance"] is not null)) UsageFailure(registry);
        if (Directory.Exists(context.PathFor("downloads")) && Transfers.Downloads.List().Any(o =>
            o.ScopePublication?.ResourceId.ToString("D") == resourceId)) Fail("resource-operation-pending");
        resource["removal-pending"] = true;
        store.Commit(snapshot.Revision, docs); // Persist ownership and every remaining file before deletion.
        long freed = 0; var pending = new List<string>();
        if (resource["ownership"]!.GetValue<string>() == "managed")
            foreach (var installation in resource["installations"]!.AsArray())
                foreach (var file in installation!["files"]!.AsArray())
                {
                    var relative = installation["location"]!.GetValue<string>() + "/" + file!["path"]!.GetValue<string>();
                    try
                    {
                        var path = context.PathFor(relative);
                        FileAttributes attributes;
                        try { attributes = File.GetAttributes(path); }
                        catch (FileNotFoundException) { continue; }
                        catch (DirectoryNotFoundException) { continue; }
                        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                            throw new StorageException("file-ownership-uncertain", "La ruta ya no identifica un archivo propio regular.");
                        if (file["volume"]?.GetValue<string>() is not string volume || file["file-id"]?.GetValue<string>() is not string fileId)
                            throw new StorageException("file-ownership-uncertain", "La identidad del archivo cambió.");
                        freed += PhysicalDirectory.DeleteOwnedFile(path, volume, fileId);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or StorageException)
                    { pending.Add(relative + ": " + (e is StorageException s ? s.Code : "delete-failed")); }
                }
        if (pending.Count == 0)
        {
            snapshot = store.Read(); docs = RecipeRegistry.Next(snapshot);
            inventory = docs["local/inventory.json"]["data"]!["resources"]!.AsArray();
            var current = inventory.Single(r => r!["resource-id"]!.GetValue<string>() == resourceId);
            inventory.Remove(current); // Alias IDs deliberately remain broken; never reconnect by canonical name.
            store.Commit(snapshot.Revision, docs);
        }
        else
        {
            snapshot = store.Read(); docs = RecipeRegistry.Next(snapshot);
            var current = docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["resource-id"]!.GetValue<string>() == resourceId)!;
            current["removal-diagnostics"] = new JsonArray(pending.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
            store.Commit(snapshot.Revision, docs);
        }
        return new(pending.Count == 0, freed, pending.ToArray());
    }
    private static bool Contains(JsonNode? node, string id) => node switch
    {
        JsonObject obj => obj.Any(p => Contains(p.Value, id)), JsonArray array => array.Any(p => Contains(p, id)),
        JsonValue value => value.TryGetValue<string>(out var text) && text == id, _ => false
    };
    private static void UsageFailure(JsonNode evidence)
    {
        static IEnumerable<string> Instances(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var pair in obj)
                {
                    if (pair.Key == "instance-id" && pair.Value is JsonValue value && value.TryGetValue<string>(out var id) && Guid.TryParseExact(id, "D", out _)) yield return id;
                    foreach (var child in Instances(pair.Value)) yield return child;
                }
            else if (node is JsonArray array)
                foreach (var item in array) foreach (var child in Instances(item)) yield return child;
        }
        var ids = Instances(evidence).Distinct().ToArray();
        throw new ModelSourceException("resource-in-use", ids.Length == 0 ? "Uso activo de identidad incierta; se conserva la unidad." :
            "Instancias que requieren reconciliación: " + string.Join(", ", ids));
    }
}
