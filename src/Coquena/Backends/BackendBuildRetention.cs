using Coquena.Contracts;
using Coquena.Storage;
using Coquena.Downloads;

namespace Coquena.Backends;

// The durable deletion queue precedes catalog removal. Failed physical cleanup is
// retried after another successful upgrade, without exposing a partly deleted build.
public sealed class BackendBuildRetention(Home home, LocalStore? storage = null)
{
    private readonly LocalStore store = storage ?? new(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private readonly BackendPaths paths = new(home);
    private BackendRecord[] Records() => store.Read(DocumentKind.Backends, Json.BackendsDocument,
        ContractValidation.Backends)?.Backends ?? [];
    private static void Validate(BackendRetentionJournal journal)
    {
        ContractValidation.Backends(new(journal.SchemaVersion, journal.Pending));
        if (journal.Pending.Any(b => b.Ownership != "managed" || b.Origin != "ggml"))
            throw new StorageException("invalid-retention-journal", "Cola de limpieza inválida.");
    }
    private void Save(BackendRecord[] pending) => store.UpdateLocked(DocumentKind.BackendRetention,
        Json.BackendRetentionJournal, Validate, _ => new(1, pending));
    private bool Protected(BackendRecord record, BackendRecord[] records)
    {
        if (record.RemovalPending || record.TracksLatest || record.Pinned) return true;
        var config = store.Read(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config);
        if (config?.DefaultBackend?.BackendId == record.Id) return true;
        if ((store.Read(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers)?.Servers ?? [])
            .Any(s => s.Backend?.BackendId == record.Id)) return true;
        var slot = paths.Slot(record);
        if (records.Any(b => b.Id != record.Id && BackendPaths.Within(paths.Server(b), slot))) return true;
        var run = home.PathFor("run");
        if (Directory.Exists(run))
            foreach (var area in Directory.GetDirectories(run))
            {
                Home.CheckLinks(area);
                var name = Path.GetFileName(area);
                var instance = store.Read(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, server: name);
                if (instance?.BackendId != record.Id) continue;
                try { if (new Coquena.Servers.ServerRuntime(home, store).RecoverLocked(name) is not null) return true; }
                catch (Coquena.Configuration.ConfigurationException) { return true; }
            }
        // A paused installation still owns its previous slots and IDs.
        return new DownloadManager(home, store).SnapshotLocked().Any(o =>
            o.BackendInstall is { } plan && (plan.BackendId == record.Id || plan.PreviousLatest?.Id == record.Id) && o.State is not ("cancelled" or "completed"));
    }
    private void CheckOwned(BackendRecord record)
    {
        var slot = paths.Slot(record);
        if (!Directory.Exists(slot)) return;
        BackendPaths.CheckTree(slot);
        var marker = Path.Combine(slot, ".coquena-owner");
        if (!File.Exists(marker) || File.ReadAllText(marker) != Path.GetFileName(slot))
            throw new StorageException("unsafe-retention-slot", "Directorio sin identidad de instalación válida.");
        DirectoryAccess.Check(slot, false);
        foreach (var entry in Directory.EnumerateFileSystemEntries(slot, "*", SearchOption.AllDirectories)) DirectoryAccess.Check(entry, false);
    }
    public void Apply(string repository, string family, string platform, Action<string>? report = null) => store.Exclusive(() =>
    {
        var pending = store.Read(DocumentKind.BackendRetention, Json.BackendRetentionJournal, Validate)?.Pending ?? [];
        // Finish previously committed deletions. Still indexed entries are re-evaluated below.
        foreach (var record in pending.ToArray())
        {
            var records = Records();
            if (records.Any(b => b.Id == record.Id)) continue;
            var slot = paths.Slot(record);
            if (Protected(record, records))
                throw new StorageException("retention-reference-conflict", "Limpieza pendiente referenciada por otro backend.");
            // The identity was verified before catalog removal; partial deletion can remove its marker.
            if (Directory.Exists(slot))
            {
                var marker = Path.Combine(slot, ".coquena-owner");
                if (File.Exists(marker) && File.ReadAllText(marker) != Path.GetFileName(slot))
                    throw new StorageException("unsafe-retention-slot", "Identidad de limpieza pendiente modificada.");
                BackendPaths.RemoveOwnedTree(slot);
            }
            pending = pending.Where(b => b.Id != record.Id).ToArray(); Save(pending);
        }
        var all = Records();
        var group = all.Where(b => b.Ownership == "managed" && b.Origin == "ggml" && b.Repository == repository &&
            b.Family == family && b.Platform == platform).ToArray();
        var limit = store.Read(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config)?.BackendRetention.MaxBuilds ?? 5;
        var builds = group.GroupBy(b => b.Build).ToArray();
        var protectedBuilds = builds.Where(g => g.Any(b => Protected(b, all))).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        if (protectedBuilds.Count > limit) report?.Invoke($"Retención: {protectedBuilds.Count} builds protegidos superan el límite {limit}; se conservan.");
        var remove = builds.Where(g => !protectedBuilds.Contains(g.Key))
            .OrderBy(g => BackendCatalog.BuildNumber(g.Key))
            .Take(Math.Max(0, builds.Length - Math.Max(limit, protectedBuilds.Count))).SelectMany(g => g).ToArray();
        foreach (var record in remove)
        {
            CheckOwned(record);
            if (!pending.Any(b => b.Id == record.Id)) { pending = [.. pending, record]; Save(pending); }
            store.UpdateLocked(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends,
                old => old! with { Backends = old.Backends.Where(b => b.Id != record.Id).ToArray() });
            var slot = paths.Slot(record);
            if (Directory.Exists(slot)) BackendPaths.RemoveOwnedTree(slot);
            pending = pending.Where(b => b.Id != record.Id).ToArray(); Save(pending);
            report?.Invoke($"Retención: retirado {record.Origin}/{record.Variant}:{record.Build}.");
        }
        // Drop abandoned queue entries still indexed and now protected or within the limit.
        pending = pending.Where(b => !Records().Any(r => r.Id == b.Id)).ToArray();
        if (pending.Length != 0 || File.Exists(home.PathFor("catalogs/backend-retention.json"))) Save(pending);
        return true;
    });
}
