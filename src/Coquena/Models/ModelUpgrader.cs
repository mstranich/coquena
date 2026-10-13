using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Models;

public sealed partial class ModelInstaller
{
    private static string Snapshot(ModelRecord record) => ContractJson.Write(record, Json.ModelRecord);
    private static bool Equal(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
    private string Backup(OperationDocument operation, int index) => home.PathFor($"downloads/{operation.Id:D}/previous/{index}.gguf");
    internal bool Published(OperationDocument operation) => operation.ModelInstall?.Previous is null ? InstallPublished(operation) : operation.ModelInstall.VerifiedFiles is not null && Catalog().Models.Any(m => m.Id == operation.ModelInstall!.ModelId &&
        (operation.ModelInstall.Previous is null || m.Selection?.Commit == operation.ModelSelection!.Commit &&
            Snapshot(m) == Snapshot(NextUpgrade(operation))));

    internal static void CheckUpgradeReservations(Home home, LocalStore store, Guid[] ids)
    {
        foreach (var op in new DownloadManager(home, store).SnapshotLocked())
            if ((op.ModelInstall?.Previous ?? op.ModelInstall?.ExternalPrevious) is { } previous && ids.Contains(previous.Id))
                throw new Coquena.Configuration.ConfigurationException($"{(op.ModelInstall!.ExternalPrevious is null ? "Upgrade" : "Transición")} pendiente {op.Id:D} de {previous.Name}; reanude o cancele antes de iniciar.");
    }

    internal static void CheckUpgradeMutation(Home home, LocalStore store, Guid id)
    {
        ResourceRemoval.CheckPending(home, store, [id]);
        if (new DownloadManager(home, store).SnapshotLocked().Any(op => (op.ModelInstall?.Previous ?? op.ModelInstall?.ExternalPrevious)?.Id == id))
            throw new ModelSourceException("upgrade-pending", "Reanude o cancele la operación antes de modificar el registro.");
    }

    private void CheckUsage(ModelRecord previous)
    {
        ResourceRemoval.CheckPending(home, store, [previous.Id]);
        foreach (var area in Directory.GetDirectories(home.PathFor("run")))
        {
            Home.CheckLinks(area);
            var instance = store.Read(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, server: Path.GetFileName(area));
            if (instance is null || !instance.ResourceIds.Contains(previous.Id)) continue;
            try
            {
                using var process = WindowsServerProcess.Verified(instance, out var ended);
                if (ended) continue;
            }
            catch (Coquena.Configuration.ConfigurationException)
            { throw new DownloadException("model-in-use", $"Identidad incierta de instancia {instance.InstanceId:D}; detener antes de upgrade."); }
            throw new DownloadException("model-in-use", $"Unidad utilizada por instancia {instance.InstanceId:D}; detener antes de upgrade.");
        }
    }

    private void CurrentUpgrade(OperationDocument operation, bool allowPublished = false)
    {
        var previous = operation.ModelInstall!.Previous!;
        var actual = Catalog().Models.SingleOrDefault(m => m.Id == previous.Id);
        if (actual is null || Snapshot(actual) != Snapshot(previous) && !(allowPublished && Published(operation))) Fail("stale-upgrade-plan");
        CheckUsage(previous);
    }

    public OperationDocument CreateUpgrade(ModelRecord previous) => store.Exclusive(() =>
    {
        if (previous.Ownership != "managed" || previous.Update is not { State: "update-available", Plan: { } update }) Fail("unresolved-upgrade-plan");
        var plan = previous.Update!.Plan!;
        if (ContractJson.Write(plan.Installed, Json.RemoteSelection) != ContractJson.Write(previous.Selection!, Json.RemoteSelection)) Fail("stale-upgrade-plan");
        var id = Guid.NewGuid();
        var operation = new OperationDocument(1, id, previous.CompanionType is null ? "model" : "companion", plan.TargetName,
            plan.Target, null, plan.Target.Files.Select((f, i) => new TransferRecord(f.Path, $"downloads/{id:D}/files/{i}.part",
                $"models/{plan.Target.Repository}/{f.Path}", 0, f.Size, f.Identity, null, null, "pending")).ToArray(),
            "pending", "transfer", DateTimeOffset.UtcNow, null,
            ModelInstall: new(previous.Id, InstallationDirectory: previous.InstallationDirectory, Previous: previous));
        ValidatePlan(operation);
        CurrentUpgrade(operation);
        foreach (var pending in Downloads.SnapshotLocked())
            if (pending.ModelInstall?.ModelId == previous.Id || Equal(pending.Name, operation.Name) || pending.Files.Any(f =>
                operation.Files.Any(n => Equal(paths.Destination(operation, n), pending.ResourceKind == "backend" ? home.PathFor(f.DestinationPath) : paths.Destination(pending, f)))))
                Fail("upgrade-pending");
        UpgradeCollisions(operation, false);
        CheckUpgradeSpace(operation);
        return store.UpdateLocked(DocumentKind.Operation, Json.OperationDocument, ContractValidation.Operation,
            old => old is null ? operation : throw new DownloadException("already-exists", "GUID ocupado."), operation: id);
    });

    private void CheckUpgradeSpace(OperationDocument operation)
    {
        decimal required = operation.Files.Sum(f => (decimal)Math.Max(0, (f.ExpectedSize ?? 0) - f.ConfirmedBytes)) +
            operation.ModelInstall!.Previous!.Files.Select((f, i) => File.Exists(Backup(operation, i)) ? 0m : f.Size).Sum();
        var targetRequired = operation.Files.Sum(f => (decimal)(f.ExpectedSize ?? 0));
        var root = paths.Root(operation.ModelInstall.InstallationDirectory);
        if (Equal(Path.GetPathRoot(root)!, Path.GetPathRoot(home.Root)!)) required += targetRequired;
        var free = availableBytes?.Invoke() ?? new DriveInfo(Path.GetPathRoot(home.Root)!).AvailableFreeSpace;
        if (required + 1048576 > free) Fail("insufficient-space");
        var targetFree = availableBytes?.Invoke() ?? new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;
        if (targetRequired + 1048576 > targetFree) Fail("insufficient-space");
    }

    private void UpgradeCollisions(OperationDocument operation, bool publishing)
    {
        var previous = operation.ModelInstall!.Previous!;
        foreach (var other in Catalog().Models.Where(m => m.Id != previous.Id))
            if (Equal(other.Name, operation.Name) || other.Files.Any(f => operation.Files.Any(n => Equal(paths.File(other, f), paths.Destination(operation, n))) ||
                previous.Files.Any(p => Equal(paths.File(other, f), paths.File(previous, p))))) Fail("model-collision");
        for (var i = 0; i < operation.Files.Length; i++)
        {
            var target = paths.Destination(operation, operation.Files[i]);
            if (Directory.Exists(target)) Fail("destination-collision");
            if (File.Exists(target) && !previous.Files.Any(f => Equal(paths.File(previous, f), target)) &&
                !(publishing && (i < operation.ModelInstall.MovedFiles || i == operation.ModelInstall.MovingFile))) Fail("destination-collision");
        }
    }

    // The persisted operation reserves the unit across pause/restart. Start checks
    // that reservation under the same writer lease used to create and publish it.
    internal async Task<OperationDocument> PrepareUpgradeAsync(OperationDocument operation, CancellationToken cancellation)
    {
        ValidatePlan(operation);
        store.Exclusive(() => { CurrentUpgrade(operation, operation.ModelInstall!.VerifiedFiles is not null); return 0; });
        if (operation.Phase is "publishing" or "cleanup") return operation;
        CheckUpgradeSpace(operation);
        var previous = operation.ModelInstall!.Previous!;
        for (var i = 0; i < operation.Files.Length; i++)
        {
            var action = previous.Update!.Plan!.Actions[i];
            if (action.Action != "reuse" || operation.Files[i].State == "transferred") continue;
            var original = previous.Files.Single(f => f.RemotePath == action.SourcePath);
            if (!FileIntegrity.Comparable(original.Integrity)) Fail("reuse-integrity-failed");
            var source = paths.File(previous, original);
            var partial = home.PathFor(operation.Files[i].PartialPath);
            await CopyChecked(source, partial, original.Integrity, cancellation);
            var file = operation.Files[i];
            if (file.ExpectedSize is { } size && new FileInfo(partial).Length != size) Fail("reuse-size-mismatch");
            operation = Save(operation with { Files = operation.Files.Select((f, n) => n == i ? f with {
                ConfirmedBytes = new FileInfo(partial).Length, State = "transferred", LocalIntegrity = original.Integrity,
                VerifiedWriteUtc = File.GetLastWriteTimeUtc(partial) } : f).ToArray() });
        }
        return operation;
    }

    private async Task<ContentIdentity> CopyChecked(string source, string target, ContentIdentity? reference, CancellationToken cancellation)
    {
        Home.CheckLinks(source); Home.CheckLinks(target);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        using var hash = new FileHashSession();
        await hash.ReadPrefixAsync(input, input.Length, cancellation);
        var observed = hash.Finish();
        if (reference is not null && FileIntegrity.Compare(observed, reference).Status != "verified") Fail("reuse-integrity-failed");
        if (File.Exists(target))
        {
            if ((await FileIntegrity.VerifyAsync(target, observed, cancellation)).Status != "verified") Fail("preparation-collision");
            return observed;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".tmp";
        // An interrupted copy only owns this temporary preparation file.
        Home.CheckLinks(temporary);
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
        { input.Position = 0; await input.CopyToAsync(output, cancellation); output.Flush(true); }
        if ((await FileIntegrity.VerifyAsync(temporary, observed, cancellation)).Status != "verified") Fail("copy-verification-failed");
        File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(source));
        File.Move(temporary, target);
        return observed;
    }

    private ModelRecord NextUpgrade(OperationDocument operation)
    {
        var plan = operation.ModelInstall!; var previous = plan.Previous!;
        return previous with { Name = operation.Name, Selection = operation.ModelSelection,
            Files = plan.VerifiedFiles!.Select(f => plan.InstallationDirectory is null ? f : f with { LocalPath = f.LocalPath[7..] }).ToArray(),
            IntegrityCheck = null, RefreshedAt = null, Update = previous.Update! with {
                State = "unchanged", Diagnostic = "Plan aplicado; revisión instalada fijada.", LastSuccessfulState = "unchanged", Plan = null,
                Followed = operation.ModelSelection, FollowedName = operation.Name } };
    }

    private async Task<OperationDocument?> FinalizeUpgradeAsync(OperationDocument operation, CancellationToken cancellation)
    {
        ValidatePlan(operation);
        if (operation.Phase == "cleanup") { CleanupUpgrade(operation); return null; }
        var previous = operation.ModelInstall!.Previous!;
        var handles = new List<FileStream>();
        try
        {
            foreach (var f in operation.Files)
            {
                var partial = home.PathFor(f.PartialPath);
                handles.Add(new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read));
                if (f.State != "transferred" || (await FileIntegrity.VerifyAsync(partial, f.LocalIntegrity, cancellation)).Status != "verified") Fail("partial-integrity-failed");
            }
            var logical = operation.Files.Select(f => paths.Destination(operation, f)).ToArray();
            var map = operation.Files.Select((f, i) => (logical[i], home.PathFor(f.PartialPath))).ToDictionary(p => p.Item1, p => p.Item2, StringComparer.Ordinal);
            var unit = GgufInspection.InspectPaths(logical, cancellation, p => map[p]);
            if (unit.Status != "complete" || !unit.Expected.SequenceEqual(logical)) Fail("gguf-" + unit.Status);
            if (operation.Phase != "publishing")
            {
                // Keep every old file until the complete new unit has been indexed.
                var rollback = new InstalledFile[previous.Files.Length];
                for (var i = 0; i < previous.Files.Length; i++)
                {
                    var digest = await CopyChecked(paths.File(previous, previous.Files[i]), Backup(operation, i), null, cancellation);
                    rollback[i] = previous.Files[i] with { Size = new FileInfo(Backup(operation, i)).Length,
                        Integrity = digest, LastWriteUtc = File.GetLastWriteTimeUtc(Backup(operation, i)) };
                }
                cancellation.ThrowIfCancellationRequested();
                operation = Save(operation with { Phase = "publishing", ModelInstall = operation.ModelInstall with {
                    RollbackFiles = rollback, VerifiedFiles = operation.Files.Select(f => new InstalledFile(f.SourcePath, f.DestinationPath, f.ConfirmedBytes,
                        f.LocalIntegrity, File.GetLastWriteTimeUtc(home.PathFor(f.PartialPath)))).ToArray() } });
            }
            operation = store.Exclusive(() =>
            {
                CurrentUpgrade(operation, true);
                UpgradeCollisions(operation, true);
                if (!Published(operation))
                {
                    var plan = operation.ModelInstall!;
                    for (var i = plan.MovedFiles; i < operation.Files.Length; i++)
                    {
                        var f = operation.Files[i]; var target = paths.Destination(operation, f);
                        if (plan.MovingFile != i)
                        {
                            plan = plan with { MovingFile = i };
                            operation = SaveLocked(operation with { ModelInstall = plan });
                        }
                        var old = previous.Files.FirstOrDefault(p => Equal(paths.File(previous, p), target));
                        if (File.Exists(target))
                        {
                            var digest = Coquena.Configuration.ModelDirectoryManager.Hash(target);
                            if (!Equal(digest, f.LocalIntegrity!.Value) && (old is null || !Equal(digest,
                                plan.RollbackFiles![Array.IndexOf(previous.Files, old)].Integrity!.Value))) Fail("destination-collision");
                        }
                        ReplaceChecked(home.PathFor(f.PartialPath), target, f.LocalIntegrity!, operation.Id);
                        fault?.Invoke(ModelInstallStage.AfterMove);
                        plan = plan with { MovedFiles = i + 1, MovingFile = -1 };
                        operation = SaveLocked(operation with { ModelInstall = plan });
                    }
                    // A confirmed move checkpoint does not prove the destination
                    // still exists or has the same bytes after an interruption.
                    foreach (var f in operation.Files)
                    {
                        var target = paths.Destination(operation, f);
                        handles.Add(new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
                        if (!Equal(Coquena.Configuration.ModelDirectoryManager.Hash(target), f.LocalIntegrity!.Value)) Fail("publication-integrity-failed");
                    }
                    var next = NextUpgrade(operation); var catalog = Catalog();
                    store.UpdateLocked(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models,
                        _ => catalog with { Models = catalog.Models.Select(m => m.Id == previous.Id ? next : m).ToArray() });
                    fault?.Invoke(ModelInstallStage.AfterIndex);
                }
                return SaveLocked(operation with { State = "completed", Phase = "cleanup", Diagnostic = "cleanup-pending" });
            });
        }
        finally { foreach (var handle in handles) handle.Dispose(); }
        CleanupUpgrade(operation);
        return null;
    }

    private static string ReplacementTemporary(string target, Guid operation) => target + $".coquena-{operation:D}.tmp";

    private static void ReplaceChecked(string source, string target, ContentIdentity reference, Guid operation)
    {
        Home.CheckLinks(source); Home.CheckLinks(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = ReplacementTemporary(target, operation);
        Home.CheckLinks(temporary);
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { input.CopyTo(output); output.Flush(true); }
        if (!Equal(Coquena.Configuration.ModelDirectoryManager.Hash(temporary), reference.Value)) Fail("copy-verification-failed");
        File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(source));
        File.Move(temporary, target, true);
    }

    private void CleanupUpgrade(OperationDocument operation)
    {
        fault?.Invoke(ModelInstallStage.BeforeCleanup);
        store.Exclusive(() =>
        {
            if (!Published(operation)) Fail("stale-cleanup");
            foreach (var f in operation.Files)
                if (!Equal(Coquena.Configuration.ModelDirectoryManager.Hash(paths.Destination(operation, f)), f.LocalIntegrity!.Value)) Fail("publication-integrity-failed");
            var previous = operation.ModelInstall!.Previous!;
            foreach (var f in previous.Files.Where(f => !operation.Files.Any(n => Equal(paths.File(previous, f), paths.Destination(operation, n)))))
            {
                var path = paths.File(previous, f);
                if (!File.Exists(path)) continue;
                if (Catalog().Models.Where(m => m.Id != previous.Id).Any(m => m.Files.Any(n => Equal(paths.File(m, n), path)))) Fail("model-collision");
                if (!Equal(Coquena.Configuration.ModelDirectoryManager.Hash(path),
                    operation.ModelInstall.RollbackFiles![Array.IndexOf(previous.Files, f)].Integrity!.Value)) Fail("cleanup-integrity-failed");
                File.Delete(path);
            }
            var backups = home.PathFor($"downloads/{operation.Id:D}/previous");
            if (Directory.Exists(backups))
            {
                for (var i = 0; i < previous.Files.Length; i++) File.Delete(Backup(operation, i));
                Directory.Delete(backups);
            }
            foreach (var f in operation.Files) File.Delete(ReplacementTemporary(paths.Destination(operation, f), operation.Id));
            return 0;
        });
        Cleanup(operation);
    }

    private async Task<bool> CancelUpgradeAsync(OperationDocument operation)
    {
        ValidatePlan(operation);
        if (operation.ModelInstall!.VerifiedFiles is not null && Published(operation))
        { await FinalizeUpgradeAsync(operation, CancellationToken.None); return true; }
        var plan = operation.ModelInstall!; var previous = plan.Previous!;
        if (plan.MovedFiles != 0 || plan.MovingFile != -1)
        {
            operation = Save(operation with { State = "failed", Phase = "cancelling-publication" });
            store.Exclusive(() =>
            {
                CurrentUpgrade(operation);
                for (var i = Math.Max(plan.MovedFiles, plan.MovingFile + 1) - 1; i >= 0; i--)
                {
                    var target = paths.Destination(operation, operation.Files[i]);
                    var old = previous.Files.FirstOrDefault(f => Equal(paths.File(previous, f), target));
                    if (File.Exists(target))
                    {
                        var digest = Coquena.Configuration.ModelDirectoryManager.Hash(target);
                        if (!Equal(digest, operation.Files[i].LocalIntegrity!.Value) && (old is null || !Equal(digest,
                            plan.RollbackFiles![Array.IndexOf(previous.Files, old)].Integrity!.Value))) Fail("destination-collision");
                    }
                    if (old is null) File.Delete(target);
                    else ReplaceChecked(Backup(operation, Array.IndexOf(previous.Files, old)), target,
                        plan.RollbackFiles![Array.IndexOf(previous.Files, old)].Integrity!, operation.Id);
                    plan = plan with { MovedFiles = i, MovingFile = -1 };
                    operation = SaveLocked(operation with { ModelInstall = plan });
                }
                return 0;
            });
        }
        foreach (var f in operation.Files)
        {
            var temporary = ReplacementTemporary(paths.Destination(operation, f), operation.Id);
            Home.CheckLinks(temporary); File.Delete(temporary);
        }
        return false;
    }
}
