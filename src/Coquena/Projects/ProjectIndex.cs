using System.Text.Json.Nodes;
using Coquena.Storage;

namespace Coquena.Projects;

// Navigation cache only. It never supplies ownership or recipe names to a scope.
public sealed class ProjectIndex(Home home)
{
    public void Refresh(ScopeStore project)
    {
        var snapshot = project.Read();
        if (snapshot.ScopeId is null) return;
        Directory.CreateDirectory(home.Root);
        var path = home.PathFor("projects.json");
        using var lease = new FileStream(home.PathFor("projects.writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var old = File.Exists(path) ? ProjectDocuments.Read(File.ReadAllBytes(path), "project-index") : null;
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

    public string[] Locations()
    {
        var path = home.PathFor("projects.json");
        if (!File.Exists(path)) return [];
        var document = ProjectDocuments.Read(File.ReadAllBytes(path), "project-index");
        return document["data"]!["projects"]!.AsArray().Select(x => x!["base"]!.GetValue<string>()).ToArray();
    }
}
