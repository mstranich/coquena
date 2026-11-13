using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Recipes;
using Coquena.Storage;
using System.Net;
using System.Net.Http.Headers;

namespace Coquena.Projects;

// Transfers publish immutable candidates locally; only the final generation makes
// the selected set visible in inventory and combination resolutions together.
public sealed class RecipeUpgrade(ScopeContext context, Action? transferReserved = null)
{
    private const string Plans = "local/update-plan.json";
    private readonly ScopeStore store = new(context);

    internal static JsonObject? Pending(IReadOnlyDictionary<string, JsonObject> docs, Guid operation)
        => docs.GetValueOrDefault(Plans)?["data"]?["upgrades"]?.AsObject().SelectMany(p => p.Value!["units"]!.AsArray())
            .FirstOrDefault(u => u!["operation-id"]?.GetValue<string>() == operation.ToString("D"))?.AsObject();

    public async Task<JsonObject> ApplyAsync(IReadOnlyList<RecipeSelection> selections, IReadOnlySet<string> selectors,
        HardwareContext hardware, IDownloadTransport models, IDownloadTransport backends,
        Func<string, BackendDownloadSelection, CancellationToken, Task> validateBackend,
        Action<string>? progress = null, Action<ScopeWriteStage>? fault = null, CancellationToken cancellation = default)
    {
        var plans = new RecipeUpdatePlans(context);
        var plan = plans.ReadValid(selections);
        var guard = plans.IntentGuard(selections);
        var recipes = selections.Select(s => RecipeReader.Read(s)).ToDictionary(r => CombinationIdentity.FromSelection(r.Selection).Id);
        var initial = store.Read();
        var filters = selectors.Select(ResourceSelector.Parse).ToArray();
        var observations = plan["resources"]!.AsArray().Select(n => n!.AsObject()).ToArray();
        PreparationRequest Request(JsonObject o) => plans.Requests(recipes[o["combination-id"]!.GetValue<string>()])
            .Single(r => r.Kind == o["kind"]!.GetValue<string>() && r.Key == o["key"]!.GetValue<string>());
        JsonNode? Resource(JsonObject o) => initial.Documents["local/inventory.json"]["data"]!["resources"]!.AsArray()
            .SingleOrDefault(r => r!["resource-id"]!.GetValue<string>() == o["resource-id"]?.GetValue<string>());
        bool Matches(ResourceSelector f, JsonObject o) => f.Matches(o["kind"]!.GetValue<string>(), o["key"]!.GetValue<string>(),
            Resource(o), new JsonObject { ["selection"] = new JsonObject { ["backendSelection"] = o["current"]?.DeepClone() } }, Request(o));
        foreach (var filter in filters)
        {
            var matched = observations.Where(o => Matches(filter, o)).ToArray();
            if (matched.Length == 0) throw new ArgumentException("El selector no pertenece al plan vigente; ejecutar update.");
            filter.CheckAmbiguity(matched.Select(o => Request(o).ProjectName));
        }
        var chosen = observations.Where(o => filters.Length == 0 || filters.Any(f => Matches(f, o))).ToArray();
        bool Allowed(JsonObject o) => o["status"]!.GetValue<string>() == "update-available" && o["proposed"] is JsonObject &&
            o["upgrade-allowed"]?.GetValue<bool>() != false;
        var applicable = chosen.Where(Allowed).ToArray();
        var report = RecipeUpdatePlans.Report(new JsonObject { ["created-at"] = plan["created-at"]!.DeepClone(),
            ["resources"] = new JsonArray(chosen.Select(o => (JsonNode)o.DeepClone()).ToArray()) });
        foreach (var o in report["resources"]!.AsArray())
            o!["upgrade-status"] = o["status"]!.GetValue<string>() is "unchanged" or "not-applicable" ? "skipped" :
                o["upgrade-allowed"]?.GetValue<bool>() == false || o["proposed"] is null ? "blocked" : "ready";
        report["applied"] = 0;
        if (applicable.Length == 0) return report;
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plan.ToJsonString() +
            string.Join("\n", applicable.Select(o => o.ToJsonString())))));
        Directory.CreateDirectory(context.PathFor("local/upgrade-leases"));
        using var lease = Lease(context.PathFor("local/upgrade-leases/" + id + ".lease"));
        // Revalidate after acquiring the executor; another upgrade may have committed.
        if (!JsonNode.DeepEquals(plans.ReadValid(selections), plan)) Stale();
        var current = store.Read();
        if (Batch(current, id) is null)
        {
            var docs = RecipeRegistry.Next(current);
            var data = docs[Plans]["data"]!;
            if (data["upgrades"] is null) data["upgrades"] = new JsonObject();
            data["upgrades"]![id] = new JsonObject { ["plan"] = plan.DeepClone(), ["units"] = new JsonArray() };
            ScopeStore.CommitTogether([new(store, current.Revision, docs, Validate: guard)]);
        }
        var transfers = new ScopeTransfers(context, validateBackend: validateBackend);
        foreach (var observation in applicable)
        {
            cancellation.ThrowIfCancellationRequested();
            current = store.Read();
            var units = Batch(current, id)!["units"]!.AsArray();
            var unit = units.SingleOrDefault(u => JsonNode.DeepEquals(u!["observation"], observation));
            var shared = units.FirstOrDefault(u => u!["candidate"] is not null &&
                u["observation"]!["kind"]!.GetValue<string>() == observation["kind"]!.GetValue<string>() &&
                JsonNode.DeepEquals(u["observation"]!["proposed"], observation["proposed"]) &&
                (observation["kind"]!.GetValue<string>() == "backend" || u["observation"]!["resource-id"]!.GetValue<string>() == observation["resource-id"]!.GetValue<string>()));
            if (unit is null && shared is not null)
            {
                unit = shared.DeepClone(); unit["observation"] = observation.DeepClone();
                var docs = RecipeRegistry.Next(current);
                docs[Plans]["data"]!["upgrades"]![id]!["units"]!.AsArray().Add(unit);
                ScopeStore.CommitTogether([new(store, current.Revision, docs, Validate: guard)]);
            }
            if (unit is null)
            {
                var backend = observation["kind"]!.GetValue<string>() == "backend";
                var target = observation["proposed"]!.ToJsonString();
                var bs = backend ? ContractJson.Read(target, ContractJsonContext.Default.BackendDownloadSelection,
                    s => ScopedBackends.CheckSelection(s, hardware, false, progress)) : null;
                var ms = backend ? null : ContractJson.Read(target, ContractJsonContext.Default.RemoteSelection, ContractValidation.Remote);
                var name = backend ? $"ggml/{bs!.Variant}:{bs.Release}" : Resource(observation)!["logical-key"]!.GetValue<string>();
                var guid = Guid.NewGuid();
                var files = ms?.Files ?? bs!.Artifacts;
                transfers.Create(new(1, guid, observation["kind"]!.GetValue<string>(), name, ms, bs,
                    files.Select((f, i) => new TransferRecord(f.Path, $"downloads/{guid:D}/files/{i}.part", "unused/" + f.Path,
                        0, f.Size, f.Identity, null, null, "pending")).ToArray(), "pending", "transfer", DateTimeOffset.UtcNow, null),
                    "automatic", backend ? ["llama-server.exe"] : null, beforeCreate: op =>
                    {
                        current = store.Read(); var docs = RecipeRegistry.Next(current);
                        var batch = docs[Plans]["data"]!["upgrades"]![id]!;
                        unit = new JsonObject { ["operation-id"] = guid.ToString("D"), ["resource-id"] = op.ScopePublication!.ResourceId.ToString("D"), ["logical-key"] = name,
                            ["observation"] = observation.DeepClone(),
                            ["transfer"] = JsonNode.Parse(ContractJson.Write(op, ContractJsonContext.Default.OperationDocument)) };
                        batch["units"]!.AsArray().Add(unit);
                        ScopeStore.CommitTogether([new(store, current.Revision, docs, Validate: guard)]);
                        transferReserved?.Invoke();
                    });
            }
            var operation = Guid.Parse(unit!["operation-id"]!.GetValue<string>());
            progress?.Invoke("Operación de upgrade: " + operation.ToString("D"));
            if (unit["candidate"] is null)
            {
                if (!File.Exists(context.PathFor($"downloads/{operation:D}/operation.json")))
                    transfers.Downloads.Create(ContractJson.Read(unit["transfer"]!.ToJsonString(), ContractJsonContext.Default.OperationDocument, ContractValidation.Operation));
                var pending = await transfers.RunAsync(operation, observation["kind"]!.GetValue<string>() == "backend" ? backends :
                    new ReuseTransport(context, observation, Resource(observation)!, models),
                    cancellation: cancellation);
                if (pending is not null) throw new DownloadException(pending.Diagnostic ?? (pending.State == "paused" ? "transfer-paused" : "upgrade-incomplete"), "Preparación incompleta; repetir upgrade con el mismo plan.");
            }
        }
        // Candidates resumed by download are still checked as a complete set here.
        current = store.Read();
        var completed = Batch(current, id)!["units"]!.AsArray();
        var guards = new List<FileStream>();
        try
        {
            foreach (var unit in completed)
            {
                var candidate = unit!["candidate"] ?? throw new StorageException("upgrade-incomplete", "Faltan candidatas verificadas.");
                var observation = unit["observation"]!;
                var selected = candidate["selection"]?[observation["kind"]!.GetValue<string>() == "backend" ? "backendSelection" : "modelSelection"];
                var expectedSelection = observation["kind"]!.GetValue<string>() == "backend" ? JsonNode.Parse(ContractJson.Write(
                    ContractJson.Read(observation["proposed"]!.ToJsonString(), ContractJsonContext.Default.BackendDownloadSelection, _ => { }), ContractJsonContext.Default.BackendDownloadSelection)) : observation["proposed"];
                if (!JsonNode.DeepEquals(selected, expectedSelection) || candidate["operation-id"]?.GetValue<string>() != unit["operation-id"]!.GetValue<string>())
                    throw new StorageException("upgrade-candidate-conflict", "La candidata no corresponde al plan exacto.");
                if (candidate["selection"]?["backendSelection"] is JsonObject backendSelection)
                    _ = ContractJson.Read(backendSelection.ToJsonString(), ContractJsonContext.Default.BackendDownloadSelection,
                        s => ScopedBackends.CheckSelection(s, hardware, false, progress));
                foreach (var file in candidate["files"]!.AsArray())
                {
                    var path = context.PathFor(candidate["location"]!.GetValue<string>() + "/" + file!["path"]!.GetValue<string>());
                    guards.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
                    var expected = file["integrity"]!;
                    var verified = await FileIntegrity.VerifyAsync(path, new(expected["algorithm"]!.GetValue<string>(),
                        expected["domain"]!.GetValue<string>(), expected["value"]!.GetValue<string>()), cancellation);
                    if (verified.Status != "verified") throw new StorageException("upgrade-integrity-failed", "La candidata no conserva su integridad.");
                }
            }
            current = store.Read();
            if (!JsonNode.DeepEquals(plans.ReadValid(selections), plan)) Stale();
            var docs = RecipeRegistry.Next(current);
            foreach (var unit in completed) Publish(docs, unit!.AsObject(), Request(unit["observation"]!.AsObject()), recipes);
            docs[Plans]["data"]!["upgrades"]!.AsObject().Remove(id);
            cancellation.ThrowIfCancellationRequested();
            ScopeStore.CommitTogether([new(store, current.Revision, docs, Validate: guard)], fault);
        }
        finally { foreach (var file in guards) file.Dispose(); }
        report["applied"] = applicable.Length;
        foreach (var o in report["resources"]!.AsArray()) if (o!["upgrade-status"]!.GetValue<string>() == "ready") o["upgrade-status"] = "prepared-for-next-application";
        return report;
    }

    private static FileStream Lease(string path)
    {
        try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new StorageException("upgrade-in-progress", "Otro ejecutor conserva esta actualización.", inner: e); }
    }
    private static void Stale() => throw new StorageException("stale-update-plan", "Cambió el plan; se requiere update nuevo.");
    private static JsonObject? Batch(ScopeSnapshot state, string id) => state.Documents.GetValueOrDefault(Plans)?["data"]?["upgrades"]?[id]?.AsObject();

    private sealed class ReuseTransport(ScopeContext context, JsonObject observation, JsonNode resource, IDownloadTransport remote) : IDownloadTransport
    {
        public async Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int index, long offset, CancellationToken cancellation)
        {
            var file = operation.ModelSelection!.Files[index];
            var action = observation["actions"]?.AsArray().SingleOrDefault(a => a!["target-path"]!.GetValue<string>() == file.Path);
            if (action?["action"]?.GetValue<string>() != "reuse") return await remote.OpenAsync(operation, index, offset, cancellation);
            var installation = resource["installations"]!.AsArray().Single(i => i!["installation-id"]!.GetValue<string>() == observation["installation-id"]!.GetValue<string>())!;
            var sourcePath = action["source-path"]!.GetValue<string>();
            var original = ContractJson.Read(installation["selection"]!["modelSelection"]!.ToJsonString(), ContractJsonContext.Default.RemoteSelection, ContractValidation.Remote)
                .Files.Single(f => f.Path == sourcePath);
            if (!ModelUpdateComparer.Same(original.Identity, file.Identity))
                throw new StorageException("upgrade-reuse-integrity-failed", "La identidad remota no demuestra correspondencia.");
            var reference = installation["files"]!.AsArray().Single(f => f!["path"]!.GetValue<string>() == sourcePath)!["integrity"]!;
            var path = context.PathFor(installation["location"]!.GetValue<string>() + "/" + sourcePath);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                var check = await FileIntegrity.VerifyAsync(path, new(reference["algorithm"]!.GetValue<string>(), reference["domain"]!.GetValue<string>(), reference["value"]!.GetValue<string>()), cancellation);
                if (check.Status != "verified" || stream.Length != file.Size)
                    throw new StorageException("upgrade-reuse-integrity-failed", "No se puede demostrar el contenido reutilizado.");
                stream.Position = offset;
                var response = new HttpResponseMessage(offset == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent) { Content = new StreamContent(stream) };
                response.Content.Headers.ContentLength = stream.Length - offset;
                if (offset > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, stream.Length - 1, stream.Length);
                return response;
            }
            catch { stream.Dispose(); throw; }
        }
    }

    private void Publish(Dictionary<string, JsonObject> docs, JsonObject unit, PreparationRequest request, Dictionary<string, EffectiveRecipe> recipes)
    {
        var o = unit["observation"]!;
        var candidate = unit["candidate"]!.DeepClone().AsObject();
        var resources = docs["local/inventory.json"]["data"]!["resources"]!.AsArray();
        var resource = resources.SingleOrDefault(r => r!["resource-id"]!.GetValue<string>() == unit["resource-id"]!.GetValue<string>());
        if (resource is null)
        {
            if (resources.Any(r => r!["logical-key"]!.GetValue<string>() == unit["logical-key"]!.GetValue<string>() && r["kind"]!.GetValue<string>() == request.Kind))
                throw new StorageException("scope-conflict", "Otra operación publicó el mismo recurso.");
            resource = new JsonObject { ["resource-id"] = unit["resource-id"]!.DeepClone(), ["logical-key"] = unit["logical-key"]!.DeepClone(),
                ["kind"] = request.Kind, ["ownership"] = "managed", ["installations"] = new JsonArray() };
            resources.Add(resource);
        }
        if (resource["ownership"]!.GetValue<string>() != "managed" || resource["removal-pending"]?.GetValue<bool>() == true)
            throw new StorageException("resource-ownership-conflict", "El recurso ya no admite la revisión propuesta.");
        var installations = resource["installations"]!.AsArray();
        if (!installations.Any(i => i!["installation-id"]!.GetValue<string>() == candidate["installation-id"]!.GetValue<string>())) installations.Add(candidate.DeepClone());
        var backend = request.Kind == "backend";
        var entryFile = backend ? "llama-server.exe" : candidate["files"]![0]!["path"]!.GetValue<string>();
        var path = context.PathFor(candidate["location"]!.GetValue<string>() + "/" + entryFile);
        var resolution = new JsonObject { ["origin"] = backend ? "official" : "hugging-face", ["selection"] = o["proposed"]!.DeepClone(),
            ["artifacts"] = new JsonArray(candidate["files"]!.AsArray().Select(f => (JsonNode)new JsonObject {
                ["path"] = f!["path"]!.DeepClone(), ["size"] = f["size"]!.DeepClone(), ["identity"] = f["integrity"]!.DeepClone() }).ToArray()) };
        if (!backend)
        {
            var name = resource["logical-key"]!.GetValue<string>(); resolution["name"] = name;
            if (ModelRequest.Parse(name).CompanionType is string type) resolution["companion-type"] = type;
        }
        var binding = new JsonObject { ["key"] = request.Key, ["kind"] = request.Kind,
            ["resource-id"] = unit["resource-id"]!.DeepClone(), ["installation-id"] = candidate["installation-id"]!.DeepClone(), ["entry-path"] = path };
        var combination = o["combination-id"]!.GetValue<string>();
        var entry = docs.GetValueOrDefault("local/combinations.json")?["data"]?["combinations"]?[combination];
        void Replace(JsonArray? values, JsonObject replacement)
        {
            if (values is null) return;
            var old = values.SingleOrDefault(v => v!["kind"]!.GetValue<string>() == request.Kind && v["key"]!.GetValue<string>() == request.Key);
            if (old is not null) { var index = values.IndexOf(old); values[index] = replacement.DeepClone(); }
        }
        var resolved = resolution.DeepClone().AsObject(); resolved["kind"] = request.Kind; resolved["key"] = request.Key;
        Replace(entry?["bindings"] as JsonArray, binding);
        Replace(entry?["service-preparation"]?["bindings"] as JsonArray, binding);
        Replace(entry?["service-preparation"]?["resolutions"] as JsonArray, resolved);
        if (entry?["resolution-path"]?.GetValue<string>() is string resolutionPath)
            Replace(docs.GetValueOrDefault(resolutionPath)?["data"]?["resources"] as JsonArray, resolved);
        if (entry?["preparation-revision"] is not null) entry["preparation-revision"] = entry["preparation-revision"]!.GetValue<long>() + 1;
        if (entry?["service-preparation"] is JsonObject service) service["revision"] = service["revision"]!.GetValue<long>() + 1;
        if (context.IsGlobal)
        {
            if (!docs.ContainsKey("local/recipes.json")) docs["local/recipes.json"] = ProjectDocuments.Create("recipe-registry", 1, new JsonObject { ["recipes"] = new JsonArray() });
            ImportedResolutions.Record(docs["local/recipes.json"]["data"]!.AsObject(), context, request,
                new(request.Key, request.Kind, unit["resource-id"]!.GetValue<string>(), candidate["installation-id"]!.GetValue<string>(), path, resolution), recipes[combination].Name);
        }
        foreach (var alias in docs["local/inventory.json"]["data"]?["model-aliases"] as JsonArray ?? [])
            if (alias!["kind"]!.GetValue<string>() == request.Kind && alias["key"]!.GetValue<string>() == request.Key && alias["resource-id"]!.GetValue<string>() == unit["resource-id"]!.GetValue<string>())
                alias["installation-id"] = candidate["installation-id"]!.DeepClone();
    }
}
