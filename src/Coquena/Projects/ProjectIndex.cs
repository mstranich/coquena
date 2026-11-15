using System.Text.Json.Nodes;
using Coquena.Storage;

namespace Coquena.Projects;

public enum KnownProjectState { Available, NoRecipe, Unreachable }

public sealed record KnownProject(string ScopeId, string Base, KnownProjectState State);
public sealed record ProjectForgetPlan(long Revision, IReadOnlyList<KnownProject> Projects, bool InaccessibleOnly);

// Navigation cache only. It never supplies ownership or recipe names to a scope.
public sealed class ProjectIndex(Home home)
{
    public void Refresh(ScopeStore project)
    {
        var snapshot = project.Read();
        if (snapshot.ScopeId is null) return;
        var path = home.PathFor("projects.json");
        using var lease = Lease();
        var old = File.Exists(path) ? ProjectDocuments.Read(SharedFile.ReadAllBytes(path), "project-index") : null;
        var entries = old is null ? new JsonArray() : old["data"]!["projects"]!.DeepClone().AsArray();
        var existing = entries.FirstOrDefault(x => x!["scope-id"]!.GetValue<string>() == snapshot.ScopeId);
        if (existing is not null) entries.Remove(existing);
        entries.Add((JsonNode)new JsonObject
        {
            ["scope-id"] = snapshot.ScopeId, ["base"] = project.Context.Base,
            ["volume"] = project.Context.PhysicalBase.Volume, ["file-id"] = project.Context.PhysicalBase.FileId,
            ["revision"] = snapshot.Revision
        });
        var document = ProjectDocuments.Create("project-index", checked((old?["revision"]?.GetValue<long>() ?? 0) + 1),
            new JsonObject { ["projects"] = entries });
        ScopeStore.Replace(path, ProjectDocuments.Bytes(document));
    }

    public string[] Locations() => Entries().Select(entry => entry.Base).ToArray();

    // Scope ID and folder of each entry, without checking that the folder still resolves.
    public (string ScopeId, string Base)[] Entries()
    {
        var path = home.PathFor("projects.json");
        if (!File.Exists(path)) return [];
        var document = ProjectDocuments.Read(SharedFile.ReadAllBytes(path), "project-index");
        return document["data"]!["projects"]!.AsArray()
            .Select(x => (x!["scope-id"]!.GetValue<string>(), x["base"]!.GetValue<string>())).ToArray();
    }

    // Each entry with what can be checked without changing anything: the folder, its
    // physical identity and the scope identity recorded in its .coquena.
    public KnownProject[] List()
    {
        var path = home.PathFor("projects.json");
        if (!File.Exists(path)) return [];
        var document = ProjectDocuments.Read(SharedFile.ReadAllBytes(path), "project-index");
        return document["data"]!["projects"]!.AsArray().Select(entry => new KnownProject(
            entry!["scope-id"]!.GetValue<string>(), entry["base"]!.GetValue<string>(), State(entry))).ToArray();
    }

    // Removes one entry, chosen by its scope ID or a unique prefix. Files are never touched;
    // opening the project again registers it anew.
    public KnownProject Forget(string selector)
    {
        var plan = PreviewForget([selector]);
        Forget(plan);
        return plan.Projects.Single();
    }

    public ProjectForgetPlan PreviewForget(IReadOnlyList<string> selectors, bool inaccessible = false)
    {
        if (inaccessible == (selectors.Count > 0) || selectors.Any(string.IsNullOrWhiteSpace))
            throw new StorageException("invalid-selection", Messages.ProjectForgetSelectionRequired);
        var path = home.PathFor("projects.json");
        if (!File.Exists(path))
        {
            if (inaccessible) return new(0, [], true);
            throw new StorageException("unknown-project", Messages.ProjectIndexEmpty);
        }
        var old = ProjectDocuments.Read(SharedFile.ReadAllBytes(path), "project-index");
        var entries = old["data"]!["projects"]!.AsArray().Select(entry => new KnownProject(
            entry!["scope-id"]!.ToString(), entry["base"]!.ToString(), State(entry))).ToArray();
        var chosen = new List<KnownProject>();
        if (inaccessible) chosen.AddRange(entries.Where(p => p.State == KnownProjectState.Unreachable));
        foreach (var selector in selectors)
        {
            var matches = entries.Where(p => p.ScopeId.StartsWith(selector, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0) throw new StorageException("unknown-project", Messages.ProjectUnknownId);
            if (matches.Length > 1) throw new StorageException("ambiguous-project", Messages.ProjectAmbiguousId);
            chosen.Add(matches[0]);
        }
        return new(old["revision"]!.GetValue<long>(), chosen.DistinctBy(p => p.ScopeId).ToArray(), inaccessible);
    }

    public void Forget(ProjectForgetPlan plan)
    {
        if (plan.Projects.Count == 0) return;
        using var lease = Lease();
        var path = home.PathFor("projects.json");
        if (!File.Exists(path)) throw new StorageException("project-index-conflict", Messages.ProjectIndexChanged);
        var old = ProjectDocuments.Read(SharedFile.ReadAllBytes(path), "project-index");
        if (old["revision"]!.GetValue<long>() != plan.Revision) throw new StorageException("project-index-conflict", Messages.ProjectIndexChanged);
        var entries = old["data"]!["projects"]!.DeepClone().AsArray();
        foreach (var project in plan.Projects)
        {
            var match = entries.SingleOrDefault(e => e!["scope-id"]!.ToString() == project.ScopeId);
            if (match is null || match["base"]!.ToString() != project.Base || State(match) != project.State)
                throw new StorageException("project-index-conflict", Messages.ProjectChangedBeforeForget);
            entries.Remove(match);
        }
        var document = ProjectDocuments.Create("project-index", checked(old["revision"]!.GetValue<long>() + 1),
            new JsonObject { ["projects"] = entries });
        ScopeStore.Replace(path, ProjectDocuments.Bytes(document));
    }

    private static KnownProjectState State(JsonNode entry)
    {
        try
        {
            var folder = entry["base"]!.GetValue<string>();
            var scope = ScopeContext.Project(Path.Combine(folder, "coquena.yaml"));
            if (!Directory.Exists(scope.Root) || scope.PhysicalBase.Volume != entry["volume"]!.GetValue<string>() ||
                scope.PhysicalBase.FileId != entry["file-id"]!.GetValue<string>()) return KnownProjectState.Unreachable;
            if (new ScopeStore(scope).ReadVerified().ScopeId != entry["scope-id"]!.GetValue<string>()) return KnownProjectState.Unreachable;
            return File.Exists(Path.Combine(folder, "coquena.yaml")) ? KnownProjectState.Available : KnownProjectState.NoRecipe;
        }
        catch (Exception e) when (e is StorageException or IOException or UnauthorizedAccessException) { return KnownProjectState.Unreachable; }
    }

    private FileStream Lease()
    {
        Directory.CreateDirectory(home.Root);
        return new FileStream(home.PathFor("projects.writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
