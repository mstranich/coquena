using System.Text.Json.Nodes;
using Coquena.Configuration;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Servers;

public sealed partial class ScopedServiceProcess
{
    public void RetainLogs(string serviceId)
    {
        using var lease = Lease(serviceId);
        RetainLogsLocked(serviceId);
    }

    // Caller holds the service lifecycle lease. Only successfully created files enter this ledger.
    private void RememberLog(string serviceId, Guid instanceId, string path, long? limit)
    {
        var identity = PhysicalDirectory.Read(path);
        var state = store.Read(); var docs = RecipeRegistry.Next(state);
        var service = PresetPublication.Service(new ScopeSnapshot(state.Revision, state.ScopeId, state.State, docs), serviceId);
        var history = service["log-history"] as JsonArray;
        if (history is null) service["log-history"] = history = new JsonArray();
        service["log-retention-bytes"] = limit;
        history.Add((JsonNode)new JsonObject { ["instance-id"] = instanceId.ToString("D"), ["path"] = path,
            ["volume"] = identity.Volume, ["file-id"] = identity.FileId, ["started-at"] = DateTimeOffset.UtcNow.ToString("O") });
        store.Commit(state.Revision, docs);
    }

    private void RetainLogsLocked(string serviceId, long? ownLimit = null)
    {
        var state = store.Read();
        var docs = RecipeRegistry.Next(state);
        var service = PresetPublication.Service(new ScopeSnapshot(state.Revision, state.ScopeId, state.State, docs), serviceId);
        if (service["log-history"] is not JsonArray history || history.Count == 0) return;
        var limit = ownLimit ?? service["log-retention-bytes"]?.GetValue<long>() ??
            new ConfigurationManager(Home.Resolve()).Retention().MaxBytes;
        var active = state.Documents.GetValueOrDefault(store.RuntimeDocumentPath(serviceId, "active"))?["data"];
        var activeId = active?["terminated"]?.GetValue<bool>() != true ? active?["instance-id"]?.GetValue<string>() : null;
        var lastId = history[^1]!["instance-id"]!.GetValue<string>();
        long total = 0;
        var files = new List<(JsonNode Entry, long Size)>();
        var removed = new List<JsonNode>();
        foreach (var entry in history)
        {
            var path = entry!["path"]!.GetValue<string>();
            if (!File.Exists(path)) { if (entry["instance-id"]!.GetValue<string>() != lastId) removed.Add(entry); continue; }
            var size = new FileInfo(path).Length;
            total = checked(total + size); files.Add((entry, size));
        }
        foreach (var (entry, _) in files)
        {
            if (total <= limit) break;
            var id = entry["instance-id"]!.GetValue<string>();
            if (id == activeId || id == lastId) continue;
            try
            {
                total -= PhysicalDirectory.DeleteOwnedFile(entry["path"]!.GetValue<string>(),
                    entry["volume"]!.GetValue<string>(), entry["file-id"]!.GetValue<string>());
                removed.Add(entry);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or StorageException)
            { (diagnostics ?? output)?.WriteLine("[Logs] No se retiró un log: propiedad o acceso no comprobados."); }
        }
        if (removed.Count != 0)
        {
            foreach (var entry in removed) history.Remove(entry);
            store.Commit(state.Revision, docs);
        }
        if (total > limit) (diagnostics ?? output)?.WriteLine("[Logs] Se supera la retención; se conservan logs protegidos o no retirables.");
    }

}
