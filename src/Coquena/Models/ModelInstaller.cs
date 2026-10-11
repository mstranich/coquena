using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Storage;

namespace Coquena.Models;

public enum ModelInstallStage { AfterMove, AfterIndex, BeforeCleanup }
public sealed record LocalModel(ModelRecord Record, string Availability, bool Changed);

// Initial acquisition only. No replacement, external transition or update/upgrade.
public sealed class ModelInstaller(Home home, LocalStore? storage = null,
    Func<long>? availableBytes = null, Action<ModelInstallStage>? fault = null)
{
    private readonly LocalStore store = storage ?? new LocalStore(home);
    private readonly ModelPaths paths = new(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    public DownloadManager Downloads => new(home, store);
    private ModelsDocument Catalog() => store.Read(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models) ?? new(1, []);
    internal bool IsInstalled(Guid id) => Catalog().Models.Any(m => m.Id == id);
    private OperationDocument Save(OperationDocument value) => store.Update(DocumentKind.Operation,
        Json.OperationDocument, ContractValidation.Operation, _ => value, operation: value.Id);
    private OperationDocument SaveLocked(OperationDocument value) => store.UpdateLocked(DocumentKind.Operation,
        Json.OperationDocument, ContractValidation.Operation, _ => value, operation: value.Id);
    private static void Fail(string code) => throw new DownloadException(code, "Modelo no confirmado; no se sobrescriben archivos ni registros.");

    public OperationDocument Create(SelectedModel selected)
    {
        var request = ModelRequest.Parse(selected.Name);
        var selection = selected.Selection;
        if (selected.CompanionType is not null || request.CompanionType is not null || request.Quantization is null ||
            request.Repository != selection.Repository || !HuggingFaceStore.Hex(selection.Commit, 40)) Fail("invalid-selection");
        ContractValidation.Remote(selection);
        var id = Guid.NewGuid();
        var files = selection.Files.Select((f, i) => new TransferRecord(f.Path, $"downloads/{id:D}/files/{i}.part",
            $"models/{selection.Repository}/{f.Path}", 0, f.Size, f.Identity, null, null, "pending")).ToArray();
        return store.Exclusive(() =>
        {
            var operation = new OperationDocument(1, id, "model", selected.Name, selection, null, files,
                "pending", "transfer", DateTimeOffset.UtcNow, null, ModelInstall: new(Guid.NewGuid(), InstallationDirectory: store.Read(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config)?.ModelsDirectory));
            ValidatePlan(operation);
            CheckCollision(operation, Catalog(), allowOwn: false);
            // A persisted plan reserves its name and paths, not a long-lived catalog lease.
            var area = home.PathFor("downloads");
            foreach (var directory in Directory.GetDirectories(area))
            {
                Home.CheckLinks(directory);
                if (!Guid.TryParseExact(Path.GetFileName(directory), "D", out var other)) Fail("invalid-operation-directory");
                var pending = store.Read(DocumentKind.Operation, Json.OperationDocument, ContractValidation.Operation, operation: other);
                if (pending is null) Fail("orphan-operation");
                if (pending!.ResourceKind == "model" && (pending.Name.Equals(operation.Name, StringComparison.OrdinalIgnoreCase) ||
                    pending.Files.Any(f => operation.Files.Any(n => n.DestinationPath.Equals(f.DestinationPath, StringComparison.OrdinalIgnoreCase)))))
                    Fail("model-reserved");
            }
            CheckSpace(operation);
            return store.UpdateLocked(DocumentKind.Operation, Json.OperationDocument, ContractValidation.Operation,
                old => old is null ? operation : throw new DownloadException("already-exists", "GUID ocupado."), operation: id);
        });
    }

    public void CheckSpace(OperationDocument operation)
    {
        if (operation.ModelInstall is null || operation.Phase is "publishing" or "cleanup") return;
        decimal required = 0;
        foreach (var f in operation.Files)
            if (f.ExpectedSize is long size) required += Math.Max(0, size - f.ConfirmedBytes);
        // Advisory preflight; unknown totals and competing processes still require handling disk-full.
        var free = availableBytes?.Invoke() ?? new DriveInfo(Path.GetPathRoot(home.Root)!).AvailableFreeSpace;
        if (required + 1048576 > free) Fail("insufficient-space");
    }

    private void ValidatePlan(OperationDocument operation)
    {
        ContractValidation.Operation(operation);
        var plan = operation.ModelInstall;
        var selection = operation.ModelSelection;
        if (plan is null || selection is null || operation.ResourceKind != "model" || !HuggingFaceStore.Hex(selection.Commit, 40)) Fail("invalid-install-plan");
        var request = ModelRequest.Parse(operation.Name);
        if (request.CompanionType is not null || request.Quantization is null || request.Repository != selection!.Repository) Fail("invalid-install-plan");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < operation.Files.Length; i++)
        {
            var f = operation.Files[i];
            HuggingFaceStore.ValidatePath(f.SourcePath);
            if (!f.SourcePath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ||
                f.PartialPath != $"downloads/{operation.Id:D}/files/{i}.part" ||
                f.DestinationPath != $"models/{selection!.Repository}/{f.SourcePath}" ||
                !paths.Add(this.paths.Destination(operation, f))) Fail("unsafe-install-path");
            _ = home.PathFor(f.PartialPath);
        }
    }

    private void CheckCollision(OperationDocument operation, ModelsDocument catalog, bool allowOwn)
    {
        var plan = operation.ModelInstall!;
        foreach (var record in catalog.Models)
        {
            if (allowOwn && record.Id == plan.ModelId) continue;
            if (record.Id == plan.ModelId || record.Name.Equals(operation.Name, StringComparison.OrdinalIgnoreCase) ||
                record.Files.Any(f => operation.Files.Any(n => paths.Destination(operation, n).Equals(paths.File(record, f), StringComparison.OrdinalIgnoreCase))))
                Fail("model-collision");
        }
        for (var i = 0; i < operation.Files.Length; i++)
        {
            var target = paths.Destination(operation, operation.Files[i]);
            if ((File.Exists(target) || Directory.Exists(target)) &&
                !(allowOwn && (i < plan.MovedFiles || i == plan.MovingFile))) Fail("destination-collision");
        }
    }

    public LocalModel[] List()
    {
        return Catalog().Models.Select(record =>
        {
            var paths = record.Files.Select(f => this.paths.File(record, f)).ToArray();
            var unit = GgufInspection.InspectPaths(paths);
            var availability = unit.Status switch { "complete" => "available", "incomplete" when paths.Length > 1 => "incomplete",
                "incomplete" or "read-error" => "inaccessible", _ => "invalid" };
            var changed = record.Files.Select((f, i) =>
            {
                var info = new FileInfo(paths[i]);
                return info.Exists && (info.Length != f.Size || f.LastWriteUtc is { } date && info.LastWriteTimeUtc != date.UtcDateTime);
            }).Any(x => x);
            return new LocalModel(record, availability, changed);
        }).ToArray();
    }

    public async Task<OperationDocument?> FinalizeAsync(OperationDocument operation, CancellationToken cancellation)
    {
        ValidatePlan(operation);
        if (operation.Phase == "cleanup") { Cleanup(operation); return null; }
        var recovering = operation.Phase == "publishing";
        var plan = operation.ModelInstall!;
        var handles = new List<FileStream>();
        try
        {
            // Hold non-writing, delete-shared handles through moves/index publication on Windows.
            // No hash of multi-GB files is performed while holding the home writer lease.
            var actual = new string[operation.Files.Length];
            for (var i = 0; i < operation.Files.Length; i++)
            {
                var f = operation.Files[i];
                var partial = home.PathFor(f.PartialPath); var final = paths.Destination(operation, f);
                var moved = i < plan.MovedFiles || i == plan.MovingFile && File.Exists(final);
                if (File.Exists(final) && File.Exists(partial) && !moved) Fail("destination-collision");
                actual[i] = moved ? final : partial;
                var stream = new FileStream(actual[i], FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                handles.Add(stream);
                if (f.State != "transferred" || stream.Length != f.ConfirmedBytes || f.ExpectedSize is { } size && size != stream.Length ||
                    !FileIntegrity.Comparable(f.LocalIntegrity)) Fail("incomplete-model");
                if (recovering)
                {
                    using var hash = new FileHashSession();
                    await hash.ReadPrefixAsync(stream, stream.Length, cancellation);
                    if (FileIntegrity.Compare(hash.Finish(), f.LocalIntegrity).Status != "verified") Fail("publication-integrity-failed");
                }
                else if (f.VerifiedWriteUtc is null || File.GetLastWriteTimeUtc(actual[i]) != f.VerifiedWriteUtc.Value.UtcDateTime)
                    Fail("partial-changed");
            }
            var logical = operation.Files.Select(f => paths.Destination(operation, f)).ToArray();
            var map = logical.Select((p, i) => (p, actual[i])).ToDictionary(x => x.p, x => x.Item2, StringComparer.Ordinal);
            var unit = GgufInspection.InspectPaths(logical, cancellation, path => map.TryGetValue(path, out var mapped) ? mapped : path);
            if (unit.Status != "complete" || !unit.Expected.SequenceEqual(logical, StringComparer.Ordinal)) Fail("gguf-" + unit.Status);
            cancellation.ThrowIfCancellationRequested();
            if (!recovering)
            {
                var verified = operation.Files.Select((f, i) => new InstalledFile(f.SourcePath, f.DestinationPath,
                    f.ConfirmedBytes, f.LocalIntegrity, File.GetLastWriteTimeUtc(actual[i]))).ToArray();
                operation = Save(operation with { Phase = "publishing", ModelInstall = plan with { VerifiedFiles = verified } });
            }
            // Short non-cancellable commit region. Persist each move intent before touching disk.
            operation = store.Exclusive(() =>
            {
                var catalog = Catalog(); plan = operation.ModelInstall!;
                CheckCollision(operation, catalog, allowOwn: true);
                var existing = catalog.Models.SingleOrDefault(m => m.Id == plan.ModelId);
                var next = new ModelRecord(plan.ModelId, operation.Name, operation.ModelSelection!, plan.VerifiedFiles!.Select(f => plan.InstallationDirectory is null ? f : f with { LocalPath = f.LocalPath[7..] }).ToArray(), [], plan.InstallationDirectory);
                if (existing is not null)
                {
                    if (ContractJson.Write(existing, Json.ModelRecord) != ContractJson.Write(next, Json.ModelRecord)) Fail("stale-install-plan");
                }
                else
                {
                    for (var i = plan.MovedFiles; i < operation.Files.Length; i++)
                    {
                        var f = operation.Files[i]; var source = home.PathFor(f.PartialPath); var target = paths.Destination(operation, f);
                        if (plan.MovingFile != i)
                        {
                            if (File.Exists(target) || Directory.Exists(target)) Fail("destination-collision");
                            plan = plan with { MovingFile = i };
                            operation = SaveLocked(operation with { ModelInstall = plan });
                        }
                        if (File.Exists(source))
                        {
                            if (Directory.Exists(target)) Fail("destination-collision");
                            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                            PublishFile(source, target);
                            fault?.Invoke(ModelInstallStage.AfterMove);
                        }
                        else if (!File.Exists(target)) Fail("publication-file-missing");
                        plan = plan with { MovedFiles = i + 1, MovingFile = -1 };
                        operation = SaveLocked(operation with { ModelInstall = plan });
                    }
                    store.UpdateLocked(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models,
                        _ => new(1, catalog.Models.Append(next).ToArray()));
                    fault?.Invoke(ModelInstallStage.AfterIndex);
                }
                return SaveLocked(operation with { State = "completed", Phase = "cleanup", Diagnostic = "cleanup-pending" });
            });
        }
        finally { foreach (var handle in handles) handle.Dispose(); }
        Cleanup(operation);
        return null;
    }

    // Indexed units survive cancel. For an interrupted unindexed publication, return only
    // operation-owned, hash-checked files to preparation before generic cleanup can run.
    public async Task<bool> CancelAsync(OperationDocument operation)
    {
        ValidatePlan(operation);
        if (Catalog().Models.Any(m => m.Id == operation.ModelInstall!.ModelId))
        { await FinalizeAsync(operation, CancellationToken.None); return true; }
        var plan = operation.ModelInstall!;
        if (plan.MovedFiles != 0 || plan.MovingFile != -1)
        {
            operation = Save(operation with { State = "failed", Phase = "cancelling-publication", Diagnostic = "cancel-cleanup-pending" });
            var count = Math.Max(plan.MovedFiles, plan.MovingFile + 1);
            for (var i = count - 1; i >= 0; i--)
            {
                var f = operation.Files[i]; var partial = home.PathFor(f.PartialPath); var target = paths.Destination(operation, f);
                var path = File.Exists(target) ? target : partial;
                using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                using var hash = new FileHashSession();
                await hash.ReadPrefixAsync(held, held.Length);
                if (FileIntegrity.Compare(hash.Finish(), f.LocalIntegrity).Status != "verified") Fail("publication-integrity-failed");
                operation = store.Exclusive(() =>
                {
                    if (IsInstalled(plan.ModelId)) Fail("stale-cancellation");
                    if (File.Exists(target))
                    {
                        if (File.Exists(partial) && Coquena.Configuration.ModelDirectoryManager.Hash(partial) != Coquena.Configuration.ModelDirectoryManager.Hash(target)) Fail("destination-collision");
                        Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
                        if (!File.Exists(partial)) PublishFile(target, partial);
                        File.Delete(target);
                    }
                    plan = plan with { MovedFiles = i, MovingFile = -1 };
                    return SaveLocked(operation with { ModelInstall = plan });
                });
            }
            Save(operation with { State = "paused", Phase = "transfer", Diagnostic = null,
                ModelInstall = plan with { VerifiedFiles = null } });
        }
        return false;
    }

    private static void PublishFile(string source, string target)
    {
        Home.CheckLinks(source); Home.CheckLinks(target);
        if (File.Exists(target))
        {
            if (Coquena.Configuration.ModelDirectoryManager.Hash(source) != Coquena.Configuration.ModelDirectoryManager.Hash(target)) Fail("destination-collision");
            return;
        }
        var temporary = target + ".coquena-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(source));
            if (Coquena.Configuration.ModelDirectoryManager.Hash(source) != Coquena.Configuration.ModelDirectoryManager.Hash(temporary)) Fail("copy-verification-failed");
            File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void Cleanup(OperationDocument operation)
    {
        try
        {
            fault?.Invoke(ModelInstallStage.BeforeCleanup);
            store.Exclusive(() =>
            {
                if (!Catalog().Models.Any(m => m.Id == operation.ModelInstall!.ModelId)) Fail("stale-cleanup");
                var relative = $"downloads/{operation.Id:D}"; var area = home.PathFor(relative);
                foreach (var entry in Directory.GetFileSystemEntries(area, "*", SearchOption.AllDirectories)) Home.CheckLinks(entry);
                var files = home.PathFor(relative + "/files");
                if (Directory.Exists(files)) Directory.Delete(files, recursive: true);
                foreach (var request in new[] { "pause.request", "cancel.request" })
                    File.Delete(home.PathFor(relative + "/" + request));
                // Unknown payloads are not silently removed: operation stays cleanup-pending.
                if (Directory.GetFileSystemEntries(area).Any(p => Path.GetFileName(p) is not ("operation.json" or "executor.lock"))) Fail("unknown-preparation-payload");
                store.RecordInstallation(operation.Id);
                File.Delete(home.PathFor(relative + "/operation.json"));
                File.Delete(home.PathFor(relative + "/executor.lock"));
                Directory.Delete(area);
                return 0;
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or StorageException or DownloadException)
        {
            Save(operation with { State = "completed", Phase = "cleanup", Diagnostic = "cleanup-pending" });
            throw new DownloadException("cleanup-pending", "Modelo publicado; limpieza pendiente. Reanude el GUID.");
        }
    }
}
