using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Storage;

namespace Coquena.Backends;

public enum InstallStage { AfterMove, AfterIndex, AfterCopy }

// Separate slots allow a failed reinstall to leave the previous executable untouched.
public sealed partial class BackendInstaller(Home home, LocalStore? storage = null,
    Func<string, CancellationToken, Task<string>>? probe = null, Action<InstallStage>? fault = null,
    Action<string>? retentionReport = null)
{
    private readonly LocalStore store = storage ?? new LocalStore(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private readonly BackendPaths paths = new(home);
    public const long MaxExtractedBytes = 8L * 1024 * 1024 * 1024;
    public const int MaxEntries = 4096;
    public DownloadManager Downloads => new(home, store);
    private BackendsDocument Catalog() => store.Read(DocumentKind.Backends, Json.BackendsDocument,
        ContractValidation.Backends) ?? new(1, []);
    public BackendRecord[] List() => Catalog().Backends;
    public static bool ValidExternalName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name == name.Trim() && !name.Any(char.IsControl) && !name.Contains('/') && !name.Contains(':') &&
        !name.Equals("default", StringComparison.OrdinalIgnoreCase);
    public static bool ValidExternalFamily(string? family) => family is not null && family != "cuda" &&
        (BackendCatalog.ValidFamily(family) || Regex.IsMatch(family, @"^[a-z][a-z0-9-]*(?:@[1-9][0-9]*)?$", RegexOptions.CultureInvariant));

    public async Task<BackendRecord> RegisterAsync(string server, string name, string family,
        string? distribution = null, CancellationToken cancellation = default)
    {
        if (!ValidExternalName(name)) throw new BackendException("invalid-name", "Nombre externo inválido.");
        if (!ValidExternalFamily(family)) throw new BackendException("invalid-family", "Familia inválida; CUDA requiere cuda@<mayor>.");
        var repository = distribution ?? "ggml-org/llama.cpp";
        if (Coquena.Configuration.ParameterManager.Official(repository)) repository = "ggml-org/llama.cpp";
        if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant) ||
            repository.Split('/').Any(p => p is "." or ".."))
            throw new BackendException("invalid-distribution", "Use un repositorio propietario/nombre.");
        var path = Path.GetFullPath(server);
        if (!File.Exists(path)) throw new BackendException("backend-absent", "Ejecutable ausente.");
        var version = await (probe ?? ProbeAsync)(path, cancellation);
        var matches = VersionPattern().Matches(version);
        if (version.Length > 65536 || matches.Count != 1)
            throw new BackendException("invalid-server-version", "Sondeo --version inválido.");
        var build = "b" + matches[0].Groups["build"].Value;
        _ = BackendCatalog.BuildNumber(build);
        cancellation.ThrowIfCancellationRequested();
        var record = new BackendRecord(Guid.NewGuid(), build, version,
            repository == "ggml-org/llama.cpp" ? "ggml" : repository, repository, family,
            family.Split('@')[0], "win-x64", path, Name: name, Ownership: "external");
        return store.Exclusive(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (!File.Exists(path)) throw new BackendException("backend-absent", "Ejecutable ausente.");
            store.UpdateLocked(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends,
                old => (old ?? new(1, [])) with { Backends = (old?.Backends ?? []).Append(record).ToArray() });
            return record;
        });
    }
    private static string FinalPath(BackendInstallPlan plan) =>
        (plan.InstallationDirectory is null ? plan.Slot : plan.Slot[9..]) + "/" + plan.ServerRelativePath;
    public bool Published(OperationDocument operation) => operation.BackendInstall is { } plan &&
        List().Any(b => b.Id == plan.BackendId && b.InstallationDirectory == plan.InstallationDirectory &&
            (plan.ServerRelativePath is null ? b.ServerPath.StartsWith(FinalPath(plan), StringComparison.Ordinal) : b.ServerPath == FinalPath(plan)));
    private OperationDocument Save(OperationDocument value) => store.Update(DocumentKind.Operation,
        Json.OperationDocument, ContractValidation.Operation, _ => value, operation: value.Id);
    private OperationDocument SaveLocked(OperationDocument value) => store.UpdateLocked(DocumentKind.Operation,
        Json.OperationDocument, ContractValidation.Operation, _ => value, operation: value.Id);

    public OperationDocument Create(BackendCandidate candidate, string requestedRelease)
        => store.Exclusive(() => CreateLocked(candidate, requestedRelease));

    internal OperationDocument CreateLocked(BackendCandidate candidate, string requestedRelease, BackendUpdateObservation? upgrade = null)
    {
        if (!candidate.Complete || candidate.Platform != "win-x64" || candidate.Artifacts.Length == 0 ||
            !BackendCatalog.ValidFamily(candidate.Variant)) Fail("invalid-selection");
        _ = BackendCatalog.BuildNumber(candidate.Release);
        if (requestedRelease != "latest" && requestedRelease != candidate.Release) Fail("invalid-selection");
        var records = List();
        var matches = records.Where(b => b.Ownership == "managed" && b.Origin == "ggml" && b.Variant == candidate.Variant &&
            b.Platform == candidate.Platform && b.Build == candidate.Release).ToArray();
        if (matches.Length > 1) Fail("ambiguous-installation");
        var previous = matches.SingleOrDefault();
        if (previous is not null) NotInUse(previous.Id);
        var latest = Current(records, candidate.Variant);
        var id = Guid.NewGuid();
        var backend = previous?.Id ?? Guid.NewGuid();
        var slot = $"backends/{backend:D}/{id:D}";
        var location = store.Read(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config)?.BackendsDirectory;
        var artifacts = candidate.Artifacts.Select(a => new RemoteFile(a.Name, a.Size)).ToArray();
        var files = artifacts.Select((a, i) => new TransferRecord(a.Path, $"downloads/{id:D}/files/{i}.part",
            $"{slot}/archives/{i}.zip", 0, a.Size, null, null, null, "pending")).ToArray();
        var operation = new OperationDocument(1, id, "backend", $"ggml/{candidate.Variant}:{candidate.Release}", null,
            new("ggml", candidate.Release, candidate.Variant.Split('@')[0], candidate.Variant, candidate.Platform, artifacts),
            files, "pending", "transfer", DateTimeOffset.UtcNow, null,
            new(requestedRelease, backend, previous, latest, slot, InstallationDirectory: location, Upgrade: upgrade));
        return Downloads.CreateLocked(operation);
    }

    private static BackendRecord? Current(BackendRecord[] records, string variant)
    {
        var matches = records.Where(b => b.Ownership == "managed" && b.Origin == "ggml" && b.Variant == variant &&
            b.Platform == "win-x64" && b.TracksLatest).ToArray();
        if (matches.Length > 1) Fail("ambiguous-latest");
        return matches.SingleOrDefault();
    }

    public BackendSelection? Use(string? selector)
    {
        if (selector is null) return store.Read(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config)?.DefaultBackend;
        return store.Exclusive(() =>
        {
            var selection = Resolve(selector);
            if (selection.Kind == "fixed" && List().Any(b => b.Id == selection.BackendId && b.Ownership == "managed"))
                store.UpdateLocked(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends,
                    old => old! with { Backends = old.Backends.Select(b => b.Id == selection.BackendId ? b with { Pinned = true } : b).ToArray() });
            store.UpdateLocked(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config,
                old => (old ?? new(1, null, null, new(5))) with { DefaultBackend = selection });
            return selection;
        });
    }

    public BackendSelection Resolve(string selector)
    {
        if (!selector.Contains(':') && !selector.Contains('/'))
        {
            var external = List().SingleOrDefault(b => b.Ownership == "external" &&
                string.Equals(b.Name, selector, StringComparison.OrdinalIgnoreCase));
            if (external is null || !File.Exists(paths.Server(external))) Fail("backend-absent");
            return new("fixed", external!.Id);
        }
        if (selector.StartsWith("oficial/", StringComparison.Ordinal)) selector = "ggml/" + selector[8..];
        if (selector.StartsWith("ggml/", StringComparison.Ordinal)) selector = selector[5..];
        var parts = selector.Split(':');
        if (parts.Length != 2 || !BackendCatalog.ValidFamily(parts[0])) Fail("invalid-selector");
        var records = List();
        BackendRecord? record;
        if (parts[1] == "latest") record = Current(records, parts[0]);
        else
        {
            _ = BackendCatalog.BuildNumber(parts[1]);
            var matches = records.Where(b => b.Ownership == "managed" && b.Origin == "ggml" && b.Variant == parts[0] &&
                b.Build == parts[1] && b.Platform == "win-x64").ToArray();
            if (matches.Length > 1) Fail("ambiguous-selector");
            record = matches.SingleOrDefault();
        }
        if (record is null || !File.Exists(paths.Server(record))) Fail("backend-absent");
        return parts[1] == "latest" ? new("latest", Origin: "ggml", Variant: parts[0]) : new("fixed", record!.Id);
    }

    // Recovery and publication share the home writer lease. Verified ended instances
    // release the build; active or unverifiable identities keep it protected.
    private void NotInUse(Guid id)
    {
        var run = home.PathFor("run");
        if (!Directory.Exists(run)) return;
        foreach (var area in Directory.GetDirectories(run))
        {
            Home.CheckLinks(area);
            var instance = store.Read(DocumentKind.Instance, Json.InstanceDocument,
                ContractValidation.Instance, server: Path.GetFileName(area));
            if (instance?.BackendId == id)
            {
                try
                {
                    if (new Coquena.Servers.ServerRuntime(home, store).RecoverLocked(Path.GetFileName(area)) is not null)
                        Fail("backend-in-use-or-uncertain");
                }
                catch (Coquena.Configuration.ConfigurationException) { Fail("backend-in-use-or-uncertain"); }
            }
        }
    }

    public async Task<OperationDocument?> FinalizeAsync(OperationDocument operation, CancellationToken cancellation)
    {
        var plan = operation.BackendInstall ?? throw new DownloadException("missing-install-plan", "Operación sin plan de instalación.");
        var selection = operation.BackendSelection ?? throw new DownloadException("invalid-selection", "Backend requerido.");
        ValidatePlan(operation);
        if (operation.Phase != "cleanup")
        {
            if (operation.Phase != "publishing")
            {
                operation = Save(operation with { Phase = "extraction" });
                var relative = $"downloads/{operation.Id:D}/extracted";
                RemoveTree(relative);
                Directory.CreateDirectory(home.PathFor(relative));
                await ExtractAsync(operation, relative, cancellation);
                var servers = Directory.GetFiles(home.PathFor(relative), "llama-server.exe", SearchOption.AllDirectories);
                if (servers.Length != 1) Fail("server-missing-or-ambiguous");
                var serverRelative = Path.GetRelativePath(home.PathFor(relative), servers[0]).Replace('\\', '/');
                operation = Save(operation with { Phase = "version-probe" });
                var version = await (probe ?? ProbeAsync)(servers[0], cancellation);
                CheckVersion(version, selection.Release);
                plan = plan with { ServerRelativePath = serverRelative, Version = version };
                // Ownership marker travels with the directory, before any final slot exists.
                File.WriteAllText(home.PathFor(relative + "/.coquena-owner"), operation.Id.ToString("D"));
                if (Directory.Exists(paths.Resolve(plan.InstallationDirectory, plan.Slot))) Fail("destination-collision");
                operation = Save(operation with { BackendInstall = plan, Phase = "publishing" });
            }
            plan = operation.BackendInstall!;
            var prepared = $"downloads/{operation.Id:D}/extracted";
            var slot = paths.Resolve(plan.InstallationDirectory, plan.Slot);
            // A recovered moved slot is not installed by existence alone.
            var checkArea = Directory.Exists(slot) ? slot : home.PathFor(prepared);
            if (File.ReadAllText(Path.Combine(checkArea, ".coquena-owner")) != operation.Id.ToString("D")) Fail("destination-collision");
            var observed = await (probe ?? ProbeAsync)(Path.Combine(checkArea, plan.ServerRelativePath!), cancellation);
            CheckVersion(observed, selection.Release);
            if (observed != plan.Version) Fail("version-changed");
            cancellation.ThrowIfCancellationRequested();
            // Local publication, serialized against configuration and process starts.
            // A cross-volume copy retains preparation; no network I/O occurs here.
            operation = store.Exclusive(() =>
            {
                var catalog = Catalog();
                var previous = catalog.Backends.SingleOrDefault(b => b.Id == plan.BackendId);
                var finalPath = FinalPath(plan);
                var next = new BackendRecord(plan.BackendId, selection.Release, plan.Version!, "ggml", "ggml-org/llama.cpp",
                    selection.Variant, selection.Family, selection.Platform, finalPath,
                    plan.RequestedRelease == "latest" || plan.Previous?.TracksLatest == true,
                    plan.RequestedRelease != "latest" || plan.Previous?.Pinned == true, plan.InstallationDirectory);
                var alreadyPublished = previous is not null && previous.ServerPath == finalPath && previous.InstallationDirectory == plan.InstallationDirectory &&
                    previous with { TracksLatest = next.TracksLatest, Pinned = next.Pinned } == next;
                if (!alreadyPublished)
                {
                    if (previous != plan.Previous) Fail("stale-install-plan");
                    if (plan.RequestedRelease == "latest" && Current(catalog.Backends, selection.Variant) != plan.PreviousLatest)
                        Fail("stale-latest-plan");
                    if (plan.Upgrade is { } expected) BackendUpgrader.CheckObservation(store, expected);
                    if (previous is not null) NotInUse(previous.Id);
                    if (!Directory.Exists(slot))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(slot)!);
                        PublishDirectory(home.PathFor(prepared), slot, operation.Id, cancellation);
                    }
                    else if (Directory.Exists(home.PathFor(prepared))) VerifyCopy(home.PathFor(prepared), slot);
                    fault?.Invoke(InstallStage.AfterMove);
                    var values = catalog.Backends.Where(b => b.Id != plan.BackendId).Select(b =>
                        b.Ownership == "managed" && plan.RequestedRelease == "latest" && b.Variant == selection.Variant && b.Platform == selection.Platform
                        ? b with { TracksLatest = false } : b).Append(next).ToArray();
                    store.UpdateLocked(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends, _ => new(1, values));
                    fault?.Invoke(InstallStage.AfterIndex);
                }
                // Default decision belongs to publication and is serialized against backend use.
                store.UpdateLocked(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config, old =>
                {
                    var config = old ?? new(1, null, null, new(5));
                    // Only the first installation autoassigns; later removal never repairs default.
                    return config.DefaultBackend is null && catalog.Backends.All(b => b.Id == next.Id)
                        ? config with { DefaultBackend = plan.RequestedRelease == "latest"
                            ? new("latest", Origin: "ggml", Variant: selection.Variant) : new("fixed", next.Id) }
                        : config;
                });
                return SaveLocked(operation with { State = "completed", Phase = "cleanup", Diagnostic = "cleanup-pending" });
            });
        }
        // Index is active. Cleanup failure must not revert or claim a failed installation.
        Cleanup(operation);
        return null;
    }

    public async Task<bool> CancelAsync(OperationDocument operation)
    {
        ValidatePlan(operation);
        var plan = operation.BackendInstall!;
        var installed = List().SingleOrDefault(b => b.Id == plan.BackendId);
        if (installed?.ServerPath == FinalPath(plan) && installed.InstallationDirectory == plan.InstallationDirectory)
        {
            // Commit already happened. Finish publication/cleanup rather than undo an active index.
            await FinalizeAsync(operation, CancellationToken.None);
            return true;
        }
        var slot = paths.Resolve(plan.InstallationDirectory, plan.Slot);
        if (Directory.Exists(slot))
        {
            if (File.ReadAllText(Path.Combine(slot, ".coquena-owner")) != operation.Id.ToString("D")) Fail("destination-collision");
            BackendPaths.RemoveOwnedTree(slot);
        }
        RemoveCopy(slot, operation.Id);
        return false;
    }

    private void ValidatePlan(OperationDocument operation)
    {
        var plan = operation.BackendInstall!;
        if (plan.BackendId == Guid.Empty || plan.Slot != $"backends/{plan.BackendId:D}/{operation.Id:D}" ||
            (plan.RequestedRelease != "latest" && plan.RequestedRelease != operation.BackendSelection!.Release) ||
            plan.Previous is not null && plan.Previous.Id != plan.BackendId) Fail("invalid-install-plan");
        if (plan.ServerRelativePath is not null) _ = paths.Resolve(plan.InstallationDirectory, plan.Slot + "/" + plan.ServerRelativePath);
        if ((operation.Phase == "publishing" || operation.Phase == "cleanup" && operation.State != "cancelled") && (plan.Version is null || plan.ServerRelativePath is null)) Fail("invalid-install-plan");
    }

    private void Cleanup(OperationDocument operation)
    {
        try
        {
            var plan = operation.BackendInstall!;
            if (plan.Upgrade is not null)
                new BackendBuildRetention(home, store).Apply("ggml-org/llama.cpp", operation.BackendSelection!.Family,
                    operation.BackendSelection.Platform, retentionReport);
            store.Exclusive(() =>
            {
                var installed = Catalog().Backends.SingleOrDefault(b => b.Id == plan.BackendId);
                if (installed?.ServerPath != FinalPath(plan) || installed.InstallationDirectory != plan.InstallationDirectory) Fail("stale-cleanup");
                if (plan.Previous is { } old && old.ServerPath != installed!.ServerPath)
                {
                    NotInUse(old.Id);
                    // Old slots only; never recursively remove an arbitrary catalog path.
                    var oldSlot = paths.Slot(old);
                    if (Directory.Exists(oldSlot)) BackendPaths.RemoveOwnedTree(oldSlot);
                }
                RemovePayload($"downloads/{operation.Id:D}");
                store.RecordInstallation(operation.Id);
                File.Delete(home.PathFor($"downloads/{operation.Id:D}/operation.json"));
                File.Delete(home.PathFor($"downloads/{operation.Id:D}/executor.lock"));
                Directory.Delete(home.PathFor($"downloads/{operation.Id:D}"));
                return 0;
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or StorageException or DownloadException)
        {
            Save(operation with { State = "completed", Phase = "cleanup", Diagnostic = "cleanup-pending" });
            throw new DownloadException("cleanup-pending", "Backend publicado; limpieza pendiente. Reanude el GUID.");
        }
    }

    private void RemovePayload(string relative)
    {
        var path = home.PathFor(relative);
        foreach (var entry in Directory.GetFileSystemEntries(path))
        {
            Home.CheckLinks(entry);
            if (Path.GetFileName(entry) is "operation.json" or "executor.lock") continue;
            if (Directory.Exists(entry)) RemoveTree(Path.GetRelativePath(home.Root, entry).Replace('\\', '/'));
            else File.Delete(entry);
        }
    }
    private void RemoveCopy(string slot, Guid operation)
    {
        var stage = slot + ".copy";
        Home.CheckLinks(stage);
        if (!Directory.Exists(stage)) return;
        if (File.ReadAllText(Path.Combine(stage, ".coquena-owner")) != operation.ToString("D")) Fail("destination-collision");
        BackendPaths.RemoveOwnedTree(stage);
    }
    private void PublishDirectory(string prepared, string slot, Guid operation, CancellationToken cancellation)
    {
        Home.CheckLinks(slot);
        if (Path.GetPathRoot(prepared)!.Equals(Path.GetPathRoot(slot), StringComparison.OrdinalIgnoreCase))
        {
            try { Directory.Move(prepared, slot); return; }
            // Mounted volumes and virtual drives can differ even under one root.
            catch (IOException e) when ((e.HResult & 0xffff) == 17) { }
        }
        RemoveCopy(slot, operation);
        var stage = slot + ".copy";
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, ".coquena-owner"), operation.ToString("D"));
        BackendPaths.CheckTree(prepared);
        foreach (var dir in Directory.GetDirectories(prepared, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(stage, Path.GetRelativePath(prepared, dir)));
        foreach (var source in Directory.GetFiles(prepared, "*", SearchOption.AllDirectories))
        {
            cancellation.ThrowIfCancellationRequested();
            var destination = Path.Combine(stage, Path.GetRelativePath(prepared, source));
            if (Path.GetFileName(source) == ".coquena-owner" && Path.GetDirectoryName(source) == prepared) continue;
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            if (Coquena.Configuration.BackendDirectoryManager.Hash(source) != Coquena.Configuration.BackendDirectoryManager.Hash(destination)) Fail("copy-verification-failed");
        }
        fault?.Invoke(InstallStage.AfterCopy);
        VerifyCopy(prepared, stage);
        Home.CheckLinks(slot);
        Directory.Move(stage, slot);
    }
    private static void VerifyCopy(string source, string destination)
    {
        BackendPaths.CheckTree(source); BackendPaths.CheckTree(destination);
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        if (files.Length != Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length ||
            files.Any(p => !File.Exists(Path.Combine(destination, Path.GetRelativePath(source, p))) ||
                Coquena.Configuration.BackendDirectoryManager.Hash(p) != Coquena.Configuration.BackendDirectoryManager.Hash(Path.Combine(destination, Path.GetRelativePath(source, p)))))
            Fail("copy-verification-failed");
        var directories = Directory.GetDirectories(source, "*", SearchOption.AllDirectories);
        if (directories.Length != Directory.GetDirectories(destination, "*", SearchOption.AllDirectories).Length ||
            directories.Any(p => !Directory.Exists(Path.Combine(destination, Path.GetRelativePath(source, p))))) Fail("copy-verification-failed");
    }
    private void RemoveTree(string relative)
    {
        var path = home.PathFor(relative);
        if (!Directory.Exists(path)) return;
        foreach (var entry in Directory.GetFileSystemEntries(path, "*", SearchOption.AllDirectories)) Home.CheckLinks(entry);
        Directory.Delete(path, recursive: true);
    }

    private async Task ExtractAsync(OperationDocument operation, string area, CancellationToken cancellation)
    {
        long total = 0; int count = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in operation.Files)
        {
            var path = home.PathFor(file.PartialPath);
            if (file.State != "transferred" || new FileInfo(path).Length != file.ConfirmedBytes ||
                file.ExpectedSize is long size && size != file.ConfirmedBytes) Fail("incomplete-archive");
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                if (++count > MaxEntries || entry.Length > MaxExtractedBytes - total) Fail("extraction-limit");
                total += entry.Length;
                // Unix symlinks and Windows reparse-point attributes are never extracted.
                var mode = (entry.ExternalAttributes >> 16) & 0xf000;
                if (mode is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & 0x400) != 0) Fail("unsafe-archive-entry");
                var name = entry.FullName.Replace('\\', '/');
                var directory = name.EndsWith('/');
                if (directory) name = name[..^1];
                if (directory && paths.Contains(name) && Directory.Exists(home.PathFor(area + "/" + name))) continue;
                if (name.Equals(".coquena-owner", StringComparison.OrdinalIgnoreCase) || !paths.Add(name)) Fail("archive-collision");
                string destination;
                try { destination = home.PathFor(area + "/" + name); }
                catch (StorageException e) when (e.Code == "unsafe-path") { throw new DownloadException("unsafe-archive-entry", "Ruta de archivo comprimido no permitida."); }
                if (directory) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = entry.Open();
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[65536]; long written = 0;
                while (true)
                {
                    var bytes = await input.ReadAsync(buffer, cancellation);
                    if (bytes == 0) break;
                    written += bytes;
                    if (written > entry.Length) Fail("invalid-archive-length");
                    await output.WriteAsync(buffer.AsMemory(0, bytes), cancellation);
                }
                if (written != entry.Length) Fail("invalid-archive-length");
                output.Flush(true);
            }
        }
    }

    [GeneratedRegex(@"(?m)^version:[ \t]*(?:[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?[ \t]+\(build (?<build>[1-9][0-9]*), commit [0-9a-fA-F]+\)|(?<build>[1-9][0-9]*)[ \t]+\([0-9a-fA-F]+\))[ \t\r]*$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
    private static void CheckVersion(string version, string release)
    {
        if (version.Length > 65536 || VersionPattern().Matches(version).Count != 1 ||
            VersionPattern().Match(version).Groups["build"].Value != release[1..]) Fail("invalid-server-version");
    }
    internal async Task ValidateInstalledAsync(BackendRecord record, CancellationToken cancellation)
    {
        var slot = paths.Slot(record);
        BackendPaths.CheckTree(slot);
        if (File.ReadAllText(Path.Combine(slot, ".coquena-owner")) != Path.GetFileName(slot)) Fail("unsafe-installation");
        var version = await (probe ?? ProbeAsync)(paths.Server(record), cancellation);
        CheckVersion(version, record.Build);
        if (version != record.Version) Fail("version-changed");
    }
    public static async Task<string> ProbeAsync(string server, CancellationToken cancellation)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        var start = new ProcessStartInfo(server) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(server)! };
        start.ArgumentList.Add("--version");
        Process started;
        try { started = Process.Start(start) ?? throw new DownloadException("version-start-failed", "No inició llama-server."); }
        catch (System.ComponentModel.Win32Exception) { throw new DownloadException("version-start-failed", "No inició llama-server."); }
        using var process = started;
        try
        {
            var stdout = ReadBounded(process.StandardOutput, budget.Token);
            var stderr = ReadBounded(process.StandardError, budget.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(budget.Token));
            if (process.ExitCode != 0) Fail("version-exit-failed");
            return ((await stdout) + "\n" + (await stderr)).Trim();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new DownloadException("version-timeout", "Sondeo --version excedió 15 segundos."); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
    }
    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken cancellation)
    {
        var text = new System.Text.StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer, cancellation);
            if (count == 0) return text.ToString();
            if (text.Length + count > 32768) Fail("version-output-limit");
            text.Append(buffer, 0, count);
        }
    }
    private static void Fail(string code) => throw new DownloadException(code, "Instalación no confirmada; preparación conservada para diagnóstico/reanudación.");
}
