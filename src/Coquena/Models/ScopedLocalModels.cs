using System.Text.Json.Nodes;
using Coquena.Contracts;
using Coquena.Projects;
using Coquena.Recipes;

namespace Coquena.Models;

public sealed partial class ScopedModels
{
    private void EnsureInactive(JsonNode resource)
    {
        var snapshot = store.Read();
        var ids = new[] { resource["resource-id"]!.GetValue<string>() }.Concat(resource["installations"]!.AsArray().Select(i => i!["installation-id"]!.GetValue<string>())).ToArray();
        foreach (var active in snapshot.ProtectedRuntimeDocuments())
            if (ids.Any(id => Contains(active, id))) UsageFailure(active);
        if (snapshot.Documents.TryGetValue("local/recipes.json", out var registry) && ids.Any(id => Contains(registry, id)) &&
            registry["data"]!["recipes"]!.AsArray().SelectMany(r => r!["entities"]!.AsArray()).Any(e => e!["instance"] is not null)) UsageFailure(registry);
    }
    private string[] ExternalPaths(JsonNode installation)
    {
        if (installation["external"]?.GetValue<bool>() != true || installation["files"] is not JsonArray { Count: > 0 })
            ProjectDocuments.Invalid();
        return installation["files"]!.AsArray().Select(f => Path.GetFullPath(f!["path"]!.GetValue<string>(), context.Base)).ToArray();
    }

    private static string? ExternalType(string name, bool companion, string? type)
    {
        if (name.Contains('/') || name.Contains(':') || name.Contains('#'))
        {
            var request = ModelRequest.Parse(name);
            if (request.Quantization is null || companion != (request.CompanionType is not null) || type is not null && type != request.CompanionType)
                Fail("invalid-external-name");
            return request.CompanionType;
        }
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => char.IsControl(c) || c is '\\' or ':' or '/' or '#') ||
            companion && type is not ("mmproj" or "mtp") || !companion && type is not null) Fail("invalid-external-name");
        return type;
    }

    public PreparedModel AddExternal(string path, string name, bool companion = false, string? originBase = null, string? type = null, string? projectName = null)
    {
        type = ExternalType(name, companion, type);
        if (!path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) Fail("invalid-external-path");
        originBase = Path.GetFullPath(originBase ?? context.Base);
        var unit = GgufInspection.Inspect(Path.GetFullPath(path, originBase));
        if (unit.Status is "invalid" or "unsupported") Fail("external-gguf-" + unit.Status);
        return RegisterExternal(unit.Expected, name, companion, relative: !context.IsGlobal && !Path.IsPathRooted(path) &&
            string.Equals(Path.TrimEndingDirectorySeparator(originBase), context.Base, StringComparison.OrdinalIgnoreCase), type, projectName);
    }

    public PreparedModel PrepareLocal(ModelIntent intent, string? originBase = null, bool allowNewRevision = false)
    {
        if (intent.Request is not null || intent.Files.Length == 0 || intent.Kind is not ("model" or "companion")) Fail("invalid-local-intent");
        var type = ExternalType(intent.Key, intent.Kind == "companion", intent.LocalType);
        originBase = Path.GetFullPath(originBase ?? context.Base);
        var paths = intent.Files.Select(p => Path.GetFullPath(p, originBase)).ToArray();
        if (paths.Any(p => !p.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))) Fail("invalid-external-path");
        var unit = paths.Length == 1 ? GgufInspection.Inspect(paths[0]) : GgufInspection.InspectPaths(paths);
        if (unit.Status is "invalid" or "unsupported") Fail("external-gguf-" + unit.Status);
        var existing = List(intent.Kind == "companion").SingleOrDefault(r => r!["logical-key"]!.GetValue<string>().Equals(intent.Key, StringComparison.OrdinalIgnoreCase));
        PreparedModel prepared;
        if (existing is not null)
        {
            if (existing["removal-pending"]?.GetValue<bool>() == true) Fail("removal-pending");
            if (existing["ownership"]!.GetValue<string>() != "external") Fail("model-collision");
            var installation = existing["installations"]!.AsArray().LastOrDefault(i => ExternalPaths(i!).SequenceEqual(unit.Expected, StringComparer.OrdinalIgnoreCase) &&
                (i!["companion-type"]?.GetValue<string>() ?? existing["companion-type"]?.GetValue<string>()) == type);
            if (installation is null)
            {
                if (!allowNewRevision) Fail("model-collision");
                var snapshot = store.Read(); var docs = RecipeRegistry.Next(snapshot);
                if (docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Any(r =>
                    r!["kind"]!.GetValue<string>() is "model" or "companion" &&
                    r["resource-id"]!.GetValue<string>() != existing["resource-id"]!.GetValue<string>() &&
                    r["installations"]!.AsArray().Any(i => Paths(r, i!).Intersect(unit.Expected, StringComparer.OrdinalIgnoreCase).Any()))) Fail("model-collision");
                if (Directory.Exists(context.PathFor("downloads")) && Transfers.Downloads.List().Any(o =>
                    o.ScopePublication?.ResourceId.ToString("D") == existing["resource-id"]!.GetValue<string>())) Fail("resource-operation-pending");
                var current = docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["resource-id"]!.GetValue<string>() == existing["resource-id"]!.GetValue<string>())!;
                if (!JsonNode.DeepEquals(current, existing)) Fail("model-changed");
                installation = new JsonObject { ["installation-id"] = ProjectDocuments.Id(), ["external"] = true,
                    ["origin"] = "explicit", ["protected"] = true, ["integrity-status"] = "no-baseline",
                    ["files"] = new JsonArray(unit.Expected.Select(p => (JsonNode?)new JsonObject { ["path"] = p,
                        ["size"] = new FileInfo(p).Length, ["last-write-utc"] = File.GetLastWriteTimeUtc(p).Ticks }).ToArray()) };
                if (type is not null)
                {
                    foreach (var previous in current["installations"]!.AsArray())
                        if (previous!["companion-type"] is null) previous["companion-type"] = current["companion-type"]?.DeepClone();
                    installation["companion-type"] = type; current["companion-type"] = type;
                }
                current["installations"]!.AsArray().Add(installation.DeepClone());
                store.Commit(snapshot.Revision, docs);
            }
            else if (existing["companion-type"]?.GetValue<string>() != type)
            {
                if (!allowNewRevision) Fail("model-collision");
                var snapshot = store.Read(); var docs = RecipeRegistry.Next(snapshot);
                var current = docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["resource-id"]!.GetValue<string>() == existing["resource-id"]!.GetValue<string>())!;
                if (!JsonNode.DeepEquals(current, existing)) Fail("model-changed");
                current["companion-type"] = type;
                store.Commit(snapshot.Revision, docs);
            }
            prepared = new(existing["resource-id"]!.GetValue<string>(), installation["installation-id"]!.GetValue<string>(), unit.Expected[0]);
        }
        else prepared = RegisterExternal(unit.Expected, intent.Key, intent.Kind == "companion",
            !context.IsGlobal && intent.Files.All(p => !Path.IsPathRooted(p)) &&
            string.Equals(Path.TrimEndingDirectorySeparator(originBase), context.Base, StringComparison.OrdinalIgnoreCase), type, intent.ProjectName);
        var selected = List(intent.Kind == "companion").Single(r => r!["resource-id"]!.GetValue<string>() == prepared.ResourceId)!
            ["installations"]!.AsArray().Single(i => i!["installation-id"]!.GetValue<string>() == prepared.InstallationId)!;
        if (selected["integrity-status"]?.GetValue<string>() is "invalid" or "mismatch") Fail("model-integrity-invalid");
        BindExternal(intent.Key, intent.Kind, prepared);
        return prepared;
    }

    private PreparedModel RegisterExternal(string[] paths, string name, bool companion, bool relative, string? type, string? projectName = null)
    {
        var snapshot = store.Read();
        var resources = List().Concat(List(true)).ToArray();
        if (resources.Any(r => r!["logical-key"]!.GetValue<string>().Equals(name, StringComparison.OrdinalIgnoreCase) &&
            r["kind"]!.GetValue<string>() == (companion ? "companion" : "model") || r!["installations"]!.AsArray().Any(i =>
                Paths(r, i!).Intersect(paths, StringComparer.OrdinalIgnoreCase).Any()))) Fail("model-collision");
        if (Directory.Exists(context.PathFor("downloads")) && Transfers.Downloads.List().Any(o =>
            o.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) Fail("model-reserved");
        var docs = RecipeRegistry.Next(snapshot);
        if (!docs.ContainsKey("local/inventory.json")) docs["local/inventory.json"] = ProjectDocuments.Create("inventory", 1,
            new JsonObject { ["resources"] = new JsonArray() });
        var resourceId = ProjectDocuments.Id(); var installationId = ProjectDocuments.Id();
        var files = new JsonArray(paths.Select(p =>
        {
            var info = new FileInfo(p);
            return (JsonNode?)new JsonObject { ["path"] = relative ? Path.GetRelativePath(context.Base, p) : p,
                ["size"] = info.Exists ? info.Length : 0, ["last-write-utc"] = info.Exists ? info.LastWriteTimeUtc.Ticks : 0 };
        }).ToArray());
        var resource = new JsonObject
        {
            ["resource-id"] = resourceId, ["logical-key"] = name, ["kind"] = companion ? "companion" : "model",
            ["ownership"] = "external", ["installations"] = new JsonArray((JsonNode)new JsonObject
            {
                ["installation-id"] = installationId, ["external"] = true, ["origin"] = "explicit", ["protected"] = true,
                ["integrity-status"] = "no-baseline", ["files"] = files
            })
        };
        if (type is not null) resource["companion-type"] = type;
        if (!context.IsGlobal && !name.Contains('/'))
            resource["project-name"] = new RecipeRegistry(store).Recipes().FirstOrDefault()?.RegisteredName ?? projectName ??
                (File.Exists(Path.Combine(context.Base, "coquena.yaml")) ? RecipeReader.ProjectName(new(context.Base)) : Path.GetFileName(context.Base));
        docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Add((JsonNode)resource);
        store.Commit(snapshot.Revision, docs);
        return new(resourceId, installationId, paths[0]);
    }

    private string[] Paths(JsonNode resource, JsonNode installation) => resource["ownership"]!.GetValue<string>() == "external"
        ? ExternalPaths(installation) : installation["files"]!.AsArray().Select(f => context.PathFor(
            installation["location"]!.GetValue<string>() + "/" + f!["path"]!.GetValue<string>())).ToArray();

    public void BindExternal(string key, string kind, PreparedModel prepared)
    {
        if (Directory.Exists(context.PathFor("downloads")) && Transfers.Downloads.List().Any(o => o.ScopePublication?.ResourceId.ToString("D") == prepared.ResourceId))
            Fail("resource-operation-pending");
        var snapshot = store.Read(); var docs = RecipeRegistry.Next(snapshot);
        var data = docs["local/inventory.json"]["data"]!;
        var resource = data["resources"]!.AsArray().SingleOrDefault(r => r!["resource-id"]!.GetValue<string>() == prepared.ResourceId &&
            r["kind"]!.GetValue<string>() == kind && r["ownership"]!.GetValue<string>() == "external" &&
            r["removal-pending"]?.GetValue<bool>() != true && r["installations"]!.AsArray().Any(i => i!["installation-id"]!.GetValue<string>() == prepared.InstallationId));
        if (resource is null) Fail("stale-model-binding");
        if (data["model-aliases"] is not JsonArray) data["model-aliases"] = new JsonArray();
        var aliases = data["model-aliases"]!.AsArray();
        var old = aliases.SingleOrDefault(a => a!["kind"]!.GetValue<string>() == kind && a["key"]!.GetValue<string>().Equals(key, StringComparison.OrdinalIgnoreCase));
        if (old is not null) aliases.Remove(old);
        aliases.Add((JsonNode)new JsonObject { ["key"] = key, ["kind"] = kind, ["resource-id"] = prepared.ResourceId, ["installation-id"] = prepared.InstallationId });
        store.Commit(snapshot.Revision, docs);
    }

    public string ResolveReference(ArgumentReference reference)
    {
        var companion = reference.Kind == "companion";
        var snapshot = store.Read();
        var alias = reference.Canonical ? null : snapshot.Documents.GetValueOrDefault("local/inventory.json")?["data"]?["model-aliases"]?.AsArray()
            .SingleOrDefault(a => a!["kind"]!.GetValue<string>() == reference.Kind && a["key"]!.GetValue<string>().Equals(reference.Name, StringComparison.OrdinalIgnoreCase));
        var resourceId = alias?["resource-id"]!.GetValue<string>();
        var resource = List(snapshot, companion).SingleOrDefault(r => reference.Canonical
            ? r!["logical-key"]!.GetValue<string>().Equals(reference.Name, StringComparison.OrdinalIgnoreCase)
            : resourceId is not null ? r!["resource-id"]!.GetValue<string>() == resourceId :
                r!["ownership"]!.GetValue<string>() == "external" && r["logical-key"]!.GetValue<string>().Equals(reference.Name, StringComparison.OrdinalIgnoreCase));
        if (resource is null) Fail("unknown-resource");
        if (resource!["removal-pending"]?.GetValue<bool>() == true) Fail("removal-pending");
        var installationId = alias?["installation-id"]!.GetValue<string>();
        var installation = resource["installations"]!.AsArray().LastOrDefault(i => installationId is null || i!["installation-id"]!.GetValue<string>() == installationId);
        if (installation is null) Fail("unknown-installation");
        if (ObserveInstallation(installation!)["observed-availability"]!.GetValue<string>() != "available") Fail("model-unavailable");
        if (installation!["integrity-status"]?.GetValue<string>() is "invalid" or "mismatch") Fail("model-integrity-invalid");
        if (Directory.Exists(context.PathFor("downloads")) && Transfers.Downloads.List().Any(o =>
            o.ScopePublication?.ResourceId.ToString("D") == resource["resource-id"]!.GetValue<string>())) Fail("resource-operation-pending");
        return Paths(resource, installation)[0];
    }

    public async Task<PreparedModel> PrepareReferenceAsync(EffectiveRecipe recipe, ArgumentReference reference,
        HuggingFaceStore source, Coquena.Downloads.IDownloadTransport transport, IModelSelectionInteraction interaction,
        IRemoteGgufInspector? inspector = null, CancellationToken cancellation = default)
    {
        if (!reference.Canonical)
        {
            var intent = ModelIntent.FromRecipe(recipe).SingleOrDefault(i => i.Kind == reference.Kind && i.Key.Equals(reference.Name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ModelSourceException("unknown-resource-reference", "La referencia no existe en la receta.");
            if (intent.Request is null) { cancellation.ThrowIfCancellationRequested(); return PrepareLocal(intent, recipe.Selection.Base, allowNewRevision: true); }
            return await PrepareAsync(intent, source, transport, interaction, inspector, cancellation);
        }
        var request = ModelRequest.Parse(reference.Name);
        if (request.Quantization is null || (reference.Kind == "companion") != (request.CompanionType is not null)) Fail("invalid-selector");
        var existing = List(reference.Kind == "companion").SingleOrDefault(r => r!["logical-key"]!.GetValue<string>().Equals(reference.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var entry = ResolveReference(reference);
            return new(existing["resource-id"]!.GetValue<string>(), existing["installations"]!.AsArray().Last()!["installation-id"]!.GetValue<string>(), entry);
        }
        var selected = await SelectAsync(new(reference.Name, reference.Kind, request, "main", []), source, interaction, inspector, cancellation)
            ?? throw new OperationCanceledException(cancellation);
        return await AcquireAsync(selected, transport, explicitlyRequested: false, cancellation: cancellation);
    }

    public async Task TryMatchAsync(PreparedModel prepared, HuggingFaceStore source, IModelSelectionInteraction interaction,
        CancellationToken cancellation = default)
    {
        var resource = List().Concat(List(true)).Single(r => r!["resource-id"]!.GetValue<string>() == prepared.ResourceId)!;
        var installation = resource["installations"]!.AsArray().Single(i => i!["installation-id"]!.GetValue<string>() == prepared.InstallationId)!;
        var paths = ExternalPaths(installation);
        ModelRequest request;
        try { request = ModelRequest.Parse(resource["logical-key"]!.GetValue<string>()); }
        catch (ModelSourceException) { interaction.Show("Registro local sin repositorio declarado; se conserva sin emparejamiento."); return; }
        var record = new ModelRecord(Guid.Parse(prepared.ResourceId), resource["logical-key"]!.GetValue<string>(), null,
            paths.Select((p, i) => new InstalledFile("", p, installation["files"]![i]!["size"]!.GetValue<long>())).ToArray(), [],
            Ownership: "external", CompanionType: request.CompanionType);
        var match = await ExternalMatching.SelectAsync(record, source, interaction, cancellation);
        if (match is null) return;
        var snapshot = store.Read(); var docs = RecipeRegistry.Next(snapshot);
        var current = docs["local/inventory.json"]["data"]!["resources"]!.AsArray().SingleOrDefault(r => r!["resource-id"]!.GetValue<string>() == prepared.ResourceId);
        if (!JsonNode.DeepEquals(current, resource)) Fail("model-changed");
        if (Directory.Exists(context.PathFor("downloads")) && Transfers.Downloads.List().Any(o => o.ScopePublication?.ResourceId.ToString("D") == prepared.ResourceId)) Fail("resource-operation-pending");
        current!["informative-match"] = JsonNode.Parse(ContractJson.Write(match, ContractJsonContext.Default.InformativeMatch));
        store.Commit(snapshot.Revision, docs);
    }
}
