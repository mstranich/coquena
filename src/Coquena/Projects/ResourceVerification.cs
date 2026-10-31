using System.Text.Json.Nodes;
using Coquena.Contracts;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena.Projects;

public sealed record VerificationSelection(string Key, string Kind, string? ResourceId, string? InstallationId, string[]? Paths = null);
public sealed record VerificationReport(JsonArray Resources, bool Verified);

// Observations belong to the local inventory, never to portable resolutions.
public sealed class ResourceVerification(ScopeContext context, Action<int>? afterRead = null)
{
    private readonly ScopeStore store = new(context);

    internal static void CheckPublishedResources(ScopeStore target, string serviceId)
    {
        var state = target.Read();
        var service = Coquena.Servers.PresetPublication.Service(state, serviceId);
        foreach (var contribution in service["consumers"] as JsonArray ?? [])
        {
            var bindings = new JsonArray((contribution!["presets"] as JsonArray ?? []).SelectMany(p => p!["resources"]!.AsArray()).Select(r => r!.DeepClone()).ToArray());
            if (bindings.Count == 0) continue;
            var scopeId = contribution["scope-id"]!.GetValue<string>();
            var source = state;
            if (scopeId != state.ScopeId)
            {
                ScopeContext? owner = null;
                if (contribution["source-base"] is JsonNode baseNode && contribution["source-root"] is JsonNode rootNode && Directory.Exists(baseNode.GetValue<string>()))
                {
                    var recorded = ScopeContext.At(baseNode.GetValue<string>(), rootNode.GetValue<string>(), contribution["source-global"]!.GetValue<bool>());
                    if (new ScopeStore(recorded).Read().ScopeId == scopeId) owner = recorded;
                }
                if (owner is null)
                    foreach (var location in new ProjectIndex(Home.Resolve()).Locations())
                    {
                        if (!Directory.Exists(location)) continue;
                        var candidate = ScopeContext.Project(Path.Combine(location, "coquena.yaml"));
                        if (new ScopeStore(candidate).Read().ScopeId == scopeId) { owner = candidate; break; }
                    }
                if (owner is null) throw new StorageException("resource-integrity-unknown", "No se localizó el ámbito propietario de una contribución; no se inicia sin comprobar sus fallos conocidos.");
                source = new ScopeStore(owner).Read();
                if (source.ScopeId != scopeId) throw new StorageException("resource-integrity-unknown", "La identidad del propietario cambió; no se inicia la composición.");
            }
            RecipeRegistry.ValidateRuntimeBindings(source, bindings);
        }
    }

    public async Task<JsonObject> SetBaselineAsync(string resourceId, string installationId, CancellationToken cancellation = default)
    {
        var snapshot = store.Read();
        var resource = snapshot.Documents.GetValueOrDefault("local/inventory.json")?["data"]?["resources"]?.AsArray()
            .SingleOrDefault(r => r!["resource-id"]!.GetValue<string>() == resourceId)
            ?? throw new ArgumentException("Recurso inexistente.");
        if (resource["ownership"]!.GetValue<string>() != "external") throw new ArgumentException("Sólo se establece baseline explícita en externos.");
        var installation = resource["installations"]!.AsArray().Single(i => i!["installation-id"]!.GetValue<string>() == installationId)!;
        var files = installation["files"]!.DeepClone().AsArray();
        if (resource["kind"]!.GetValue<string>() == "backend") files = new JsonArray(new JsonObject { ["path"] = installation["executable"]!.DeepClone() });
        var observed = new List<ContentIdentity>(); var stamps = new List<(string Path, long Size, long Date)>();
        foreach (var file in files)
        {
            var path = Path.GetFullPath(file!["path"]!.GetValue<string>(), context.Base);
            var info = new FileInfo(path); stamps.Add((path, info.Length, info.LastWriteTimeUtc.Ticks));
            var result = await FileIntegrity.VerifyAsync(path, null, cancellation);
            if (result.Observed is null) throw new StorageException("baseline-read-failed", "No se estableció la base de la unidad; referencias anteriores conservadas.");
            observed.Add(result.Observed);
        }
        void Validate()
        {
            foreach (var stamp in stamps)
            {
                var info = new FileInfo(stamp.Path);
                if (!info.Exists || info.Length != stamp.Size || info.LastWriteTimeUtc.Ticks != stamp.Date)
                    throw new StorageException("baseline-read-failed", "La unidad cambió; referencias anteriores conservadas.");
            }
        }
        var docs = RecipeRegistry.Next(snapshot);
        var current = docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["resource-id"]!.GetValue<string>() == resourceId)!
            ["installations"]!.AsArray().Single(i => i!["installation-id"]!.GetValue<string>() == installationId)!;
        if (resource["kind"]!.GetValue<string>() == "backend") current["external-baseline"] = Identity(observed.Single());
        else for (var i = 0; i < observed.Count; i++) current["files"]![i]!["integrity"] = Identity(observed[i]);
        var evidence = new JsonObject { ["status"] = "baseline-set", ["checked-at"] = DateTimeOffset.UtcNow.ToString("O"), ["provenance-certified"] = false };
        current["baseline"] = evidence.DeepClone();
        cancellation.ThrowIfCancellationRequested();
        EnsureLocalIgnore();
        ScopeStore.CommitTogether([new(store, snapshot.Revision, docs, Validate: Validate)]);
        return evidence;
    }
    public async Task<VerificationReport> VerifyAsync(IReadOnlyList<VerificationSelection> selection,
        bool includeUnused = false, CancellationToken cancellation = default)
    {
        var snapshot = store.Read();
        var inventory = snapshot.Documents.GetValueOrDefault("local/inventory.json")?["data"]?["resources"] as JsonArray ?? [];
        var requested = selection.ToList();
        if (includeUnused)
            foreach (var resource in inventory)
                foreach (var installation in resource!["installations"]!.AsArray())
                    if (!requested.Any(s => s.ResourceId == resource["resource-id"]!.GetValue<string>() && s.InstallationId == installation!["installation-id"]!.GetValue<string>()))
                        requested.Add(new(resource["logical-key"]!.GetValue<string>(), resource["kind"]!.GetValue<string>(),
                            resource["resource-id"]!.GetValue<string>(), installation!["installation-id"]!.GetValue<string>()));
        var docs = RecipeRegistry.Next(snapshot);
        var results = new JsonArray();
        var completed = new Dictionary<(string, string), JsonObject>();
        foreach (var item in requested.Distinct())
        {
            cancellation.ThrowIfCancellationRequested();
            if (item.ResourceId is not null && item.InstallationId is not null && completed.TryGetValue((item.ResourceId, item.InstallationId), out var earlier))
            {
                var alias = earlier.DeepClone(); alias["key"] = item.Key; alias["kind"] = item.Kind; results.Add(alias); continue;
            }
            var resource = inventory.SingleOrDefault(r => r!["resource-id"]!.GetValue<string>() == item.ResourceId);
            var installation = resource?["installations"]?.AsArray().SingleOrDefault(i => i!["installation-id"]!.GetValue<string>() == item.InstallationId);
            var result = new JsonObject { ["key"] = item.Key, ["kind"] = item.Kind,
                ["resource-id"] = item.ResourceId, ["installation-id"] = item.InstallationId,
                ["checked-at"] = DateTimeOffset.UtcNow.ToString("O"), ["files"] = new JsonArray() };
            results.Add((JsonNode)result);
            var unregistered = installation is null;
            if (unregistered && item.Paths is not { Length: > 0 }) { result["status"] = "unavailable"; continue; }
            installation ??= new JsonObject { ["files"] = new JsonArray(item.Paths!.Select(p => (JsonNode?)new JsonObject { ["path"] = p }).ToArray()) };
            var files = installation["files"]!.DeepClone().AsArray();
            if (item.Kind == "backend" && resource?["ownership"]?.GetValue<string>() == "external")
                files = new JsonArray(new JsonObject { ["path"] = installation["executable"]!.DeepClone(),
                    ["integrity"] = installation["external-baseline"]?.DeepClone() });
            var checks = result["files"]!.AsArray();
            var stamps = new List<(string Path, long Size, long Date)>();
            foreach (var file in files)
            {
                var path = installation["location"] is JsonNode location
                    ? context.PathFor(location.GetValue<string>() + "/" + file!["path"]!.GetValue<string>())
                    : Path.GetFullPath(file!["path"]!.GetValue<string>(), context.Base);
                var check = new JsonObject { ["path"] = file!["path"]!.DeepClone() }; checks.Add((JsonNode)check);
                try
                {
                    _ = File.GetAttributes(path);
                    var info = new FileInfo(path); stamps.Add((path, info.Length, info.LastWriteTimeUtc.Ticks));
                    var reference = Identity(file["integrity"]);
                    var read = await FileIntegrity.VerifyAsync(path, reference, cancellation);
                    check["status"] = read.Status;
                    if (read.Observed is { } observed) check["observed"] = Identity(observed);
                }
                catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
                { stamps.Add((path, -1, 0)); check["status"] = "missing-file"; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { stamps.Add((path, -1, 0)); check["status"] = "read-error"; }
                afterRead?.Invoke(checks.Count - 1);
            }
            for (var i = 0; i < stamps.Count; i++)
            {
                if (stamps[i].Size < 0) continue;
                try
                {
                    var info = new FileInfo(stamps[i].Path);
                    if (!info.Exists || info.Length != stamps[i].Size || info.LastWriteTimeUtc.Ticks != stamps[i].Date)
                        checks[i]!["status"] = "changed-during-read";
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { checks[i]!["status"] = "read-error"; }
            }
            var status = new[] { "mismatch", "missing-file", "read-error", "changed-during-read", "no-reference" }
                .FirstOrDefault(s => checks.Any(c => c!["status"]!.GetValue<string>() == s)) ?? (checks.Count == 0 ? "no-reference" : "verified");
            var structure = item.Kind == "backend" ? "complete" : GgufInspection.InspectPaths(stamps.Select(s => s.Path).ToArray()).Status;
            if (structure != "complete" && status is "verified" or "no-reference") status = "invalid-structure";
            if (resource?["removal-pending"]?.GetValue<bool>() == true) status = "unavailable";
            result["status"] = status;
            if (unregistered) continue;
            var current = docs["local/inventory.json"]["data"]!["resources"]!.AsArray()
                .Single(r => r!["resource-id"]!.GetValue<string>() == item.ResourceId)!["installations"]!.AsArray()
                .Single(i => i!["installation-id"]!.GetValue<string>() == item.InstallationId)!;
            // Keep each known failure until that same file is successfully compared.
            var failures = current["verification-failures"]?.DeepClone().AsObject() ?? new JsonObject();
            if (structure == "complete") failures.Remove("$unit");
            else failures["$unit"] = new JsonObject { ["status"] = structure };
            foreach (var check in checks)
            {
                var path = check!["path"]!.GetValue<string>(); var state = check["status"]!.GetValue<string>();
                var original = Identity(failures[path]?["reference"]);
                var observed = Identity(check["observed"]);
                if ((state == "verified" || state == "no-reference" && original is null) &&
                    (original is null || observed is not null && FileIntegrity.Compare(observed, original).Status == "verified")) failures.Remove(path);
                else if (state is "mismatch" or "missing-file" or "read-error" or "changed-during-read")
                {
                    var failure = check.DeepClone();
                    failure["reference"] = failures[path]?["reference"]?.DeepClone() ?? files.Single(f => f!["path"]!.GetValue<string>() == path)?["integrity"]?.DeepClone();
                    failures[path] = failure;
                }
            }
            current["verification-failures"] = failures;
            current["verification"] = result.DeepClone();
            var unresolvedPrevious = current["integrity-status"]?.GetValue<string>() is "invalid" or "mismatch" && failures.Count == 0 && status != "verified";
            current["integrity-status"] = failures.Count > 0 || unresolvedPrevious ? "invalid" : status;
            if (failures.Count > 0) result["known-failures"] = failures.DeepClone();
            completed[(item.ResourceId!, item.InstallationId!)] = result;
        }
        cancellation.ThrowIfCancellationRequested();
        if (requested.Count > 0)
        {
            if (!docs.ContainsKey("local/inventory.json")) docs["local/inventory.json"] = ProjectDocuments.Create("inventory", 1, new JsonObject { ["resources"] = new JsonArray() });
            var data = docs["local/inventory.json"]["data"]!;
            var unresolved = data["unresolved-verifications"]?.DeepClone().AsObject() ?? new JsonObject();
            foreach (var result in results.Where(r => r!["installation-id"] is null))
                unresolved[result!["kind"]!.GetValue<string>() + ":" + result["key"]!.GetValue<string>()] = result.DeepClone();
            data["unresolved-verifications"] = unresolved;
            EnsureLocalIgnore();
            store.Commit(snapshot.Revision, docs);
        }
        return new(results, results.All(r => r!["status"]!.GetValue<string>() == "verified" && r["known-failures"] is null));
    }

    private void EnsureLocalIgnore() => store.Locked(() =>
    {
        context.Ensure();
        var path = context.PathFor(".gitignore");
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        if (!text.Split('\n').Any(line => line.TrimEnd('\r') == "/local/"))
            ScopeStore.Replace(path, System.Text.Encoding.UTF8.GetBytes(text + (text.Length > 0 && !text.EndsWith('\n') ? "\n" : "") + "/local/\n"));
        return true;
    });

    internal static ContentIdentity? Identity(JsonNode? node) => node is JsonObject value &&
        value["algorithm"]?.GetValue<string>() is string algorithm && value["domain"]?.GetValue<string>() is string domain && value["value"]?.GetValue<string>() is string hash
        ? new(algorithm, domain, hash) : null;
    internal static JsonObject Identity(ContentIdentity identity) => new()
    { ["algorithm"] = identity.Algorithm, ["domain"] = identity.Domain, ["value"] = identity.Value };
}
