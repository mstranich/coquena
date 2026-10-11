using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using Coquena.Backends;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class BackendDirectoryTests
{
    private static int checks;
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private static void Check(bool condition, string description)
    { checks++; if (!condition) throw new Exception($"Backend directory {checks}: {description}"); }
    private static void Reject(Action action, string message)
    {
        try { action(); throw new Exception("Se aceptó: " + message); }
        catch (Exception e) when (e is ConfigurationException or StorageException or IOException or UnauthorizedAccessException)
        { Check(true, message); }
    }
    private static BackendRecord Add(Home home, string build, string? location = null)
    {
        var id = Guid.NewGuid(); var slot = $"{id:D}/{Guid.NewGuid():D}";
        var record = new BackendRecord(id, "b" + build, $"version: {build} (abcdef)", "ggml", "ggml-org/llama.cpp",
            "cpu", "cpu", "win-x64", (location is null ? "backends/" : "") + slot + "/bin/llama-server.exe", InstallationDirectory: location);
        var paths = new BackendPaths(home); var directory = paths.Slot(record);
        Directory.CreateDirectory(Path.Combine(directory, "bin", "empty"));
        Directory.CreateDirectory(Path.Combine(directory, "licenses"));
        File.WriteAllText(paths.Server(record), record.Version);
        File.WriteAllText(Path.Combine(directory, "bin", "ggml.dll"), "library " + build);
        File.WriteAllText(Path.Combine(directory, "licenses", "COPYING"), "license " + build);
        File.WriteAllText(Path.Combine(directory, ".coquena-owner"), "fixture owner");
        new LocalStore(home).Update(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends,
            old => new(1, (old?.Backends ?? []).Append(record).ToArray()));
        return record;
    }
    private static bool Change(BackendDirectoryManager manager, string? path, string? choice = "s", bool overwrite = true) =>
        manager.Change(path, TextWriter.Null, () => choice, () => overwrite);
    private static BackendRecord[] List(Home h) => new BackendInstaller(h).List();
    private static string State(Home h) => File.ReadAllText(h.PathFor("catalogs/backends.json")) +
        (File.Exists(h.PathFor("config.json")) ? File.ReadAllText(h.PathFor("config.json")) : "absent");

    public static async Task Run(string? executable = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-directories-" + Guid.NewGuid().ToString("N"));
        var volume = Path.GetFullPath(Path.Combine("artifacts", "task019-volume-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var home = new Home(Path.Combine(root, "home")); var paths = new BackendPaths(home);
            var manager = new BackendDirectoryManager(home); var store = new LocalStore(home);
            Check(!manager.Show().Own && !Directory.Exists(home.Root), "show default does not initialize home");
            var a = Add(home, "1"); var oldA = paths.Slot(a);
            File.WriteAllText(home.PathFor("backends/unregistered.txt"), "unrelated");
            store.Update(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config, _ => new(1, null, new("fixed", a.Id), new(5)));
            var before = State(home);
            Check(!Change(manager, "../configured", "c") && State(home) == before && !Directory.Exists(Path.Combine(root, "configured")), "cancel preserves state without creating destination");
            Check(!Change(manager, "../configured", null) && State(home) == before, "missing interaction cancels");
            Check(Change(manager, "../configured", "n") && paths.Server(List(home).Single()) == paths.Server(a), "no move retains old installation path");
            Check(manager.Show().Location == "../configured" && manager.Show().Own, "relative configuration kept verbatim");
            var b = Add(home, "2", "../configured"); var oldB = paths.Slot(b);
            var destination = Path.Combine(root, "combined");
            var bDest = Path.Combine(destination, b.Id.ToString("D"), b.ServerPath.Split('/')[1]);
            Directory.CreateDirectory(Path.Combine(bDest, "licenses"));
            Directory.CreateDirectory(Path.Combine(bDest, "bin"));
            File.WriteAllText(Path.Combine(bDest, "licenses", "COPYING"), "previous license");
            File.WriteAllText(Path.Combine(bDest, "bin", "ggml.dll"), "previous library");
            File.WriteAllText(Path.Combine(bDest, "keep.txt"), "keep");
            var collideState = State(home);
            using var report = new StringWriter();
            Check(!manager.Change("../combined", report, () => "s", () => false), "one overwrite confirmation cancels entire transfer");
            Check(report.ToString().Contains("COPYING") && report.ToString().Contains("ggml.dll") && report.ToString().Contains("destruirá") &&
                State(home) == collideState && File.ReadAllText(Path.Combine(bDest, "licenses", "COPYING")) == "previous license", "complete collision report without mutation");
            var confirmations = 0;
            Check(manager.Change("../combined", TextWriter.Null, () => "s", () => { confirmations++; return true; }), "merge all managed installations from multiple roots");
            Check(confirmations == 1 && !Directory.Exists(oldA) && !Directory.Exists(oldB), "single authorization then source directories removed");
            Check(File.ReadAllText(Path.Combine(bDest, "keep.txt")) == "keep" && File.ReadAllText(Path.Combine(bDest, "licenses", "COPYING")) == "license 2", "noncollision preserved and authorized collision overwritten");
            Check(File.Exists(home.PathFor("backends/unregistered.txt")) && List(home).Select(r => r.Id).Order().SequenceEqual(new[] { a.Id, b.Id }.Order()), "unregistered file outside installation and opaque IDs preserved");
            foreach (var record in List(home))
            {
                Check(record.InstallationDirectory == "../combined" && !record.ServerPath.StartsWith("backends/"), "file path relative to stored installation location");
                Check(File.Exists(paths.Server(record)) && Directory.Exists(Path.Combine(paths.Slot(record), "bin", "empty")), "whole tree including empty directories preserved");
            }
            Check(new BackendInstaller(home).Use(null)?.BackendId == a.Id, "default reference preserved");
            Check(new BackendInstaller(home).Resolve("cpu:b1").BackendId == a.Id, "old backend still resolves after transfer");
            before = State(home);
            Reject(() => manager.Change("../race", TextWriter.Null, () =>
            {
                store.Update(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config, c => c! with { LogRetention = new(1234) });
                return "s";
            }, () => true), "configuration changed while choosing must be reviewed again");
            Check(manager.Show().Location == "../combined" && File.Exists(paths.Server(List(home).First())), "stale choice did not transfer");

            // The entire home and sibling directories can be relocated together.
            var movedBundle = Path.Combine(root, "relocated"); Directory.CreateDirectory(movedBundle);
            Directory.Move(home.Root, Path.Combine(movedBundle, "home"));
            Directory.Move(destination, Path.Combine(movedBundle, "combined"));
            home = new Home(Path.Combine(movedBundle, "home")); paths = new(home); manager = new(home); store = new(home);
            var previousCwd = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = Path.GetPathRoot(root)!;
                Check(manager.Show().EffectivePath == Path.Combine(movedBundle, "combined") && File.Exists(paths.Server(List(home).First())), "relative config and catalog resolve from relocated home, independent of cwd");
            }
            finally { Environment.CurrentDirectory = previousCwd; }
            Check(Change(manager, null) && !manager.Show().Own && List(home).All(r => File.Exists(paths.Server(r))), "unset transfers to default and clears override");
            Check(Change(manager, "../configured-again", "n"), "subsequent no-move config");
            Check(Change(manager, null, "n") && !manager.Show().Own, "unset without move retains per-installation locations");
            var invalid = Path.Combine(root, "file-destination"); File.WriteAllText(invalid, "file"); before = State(home);
            Reject(() => Change(manager, invalid), "file destination rejected");
            Reject(() => Change(manager, invalid + "/child"), "file ancestor rejected");
            Reject(() => Change(manager, "C:relative"), "drive-relative ambiguity rejected");
            Reject(() => Change(manager, "downloads"), "destination cannot overwrite downloads area");
            Check(State(home) == before, "invalid destinations leave config/catalog unchanged");
            if (OperatingSystem.IsWindows())
            {
                // A Windows device is a deterministic unwritable target without
                // changing host ACLs or requiring elevation.
                Reject(() => Change(manager, "CON"), "unusable device destination rejected before publication");
                Check(State(home) == before, "unwritable target preserves catalog and settings");
            }

            await PendingDownloads(root);
            AccessFailures(root);
            Protection(root);
            Race(root);
            foreach (var stage in Enum.GetValues<BackendDirectoryStage>()) Recovery(root, stage);
            await CrossVolume(root, volume);
            if (executable is not null) Native(executable, root);
            Console.WriteLine($"OK: {checks} comprobaciones de directorios de backends (traslado, fusión, recuperación y volúmenes).");
        }
        finally
        {
            // Both absolute roots were created exclusively by this fixture.
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(volume)) Directory.Delete(volume, true);
        }
    }

    private static async Task PendingDownloads(string root)
    {
        var home = new Home(Path.Combine(root, "pending")); var a = Add(home, "1"); var manager = new BackendDirectoryManager(home);
        var installer = new BackendInstaller(home);
        var op = installer.Create(Candidate(Archive()), "b2"); var before = State(home);
        using var report = new StringWriter();
        Reject(() => manager.Change("../pending-dest", report, () => "s", () => true), "pending backend download blocks all transfer");
        Check(State(home) == before && File.Exists(new BackendPaths(home).Server(a)), "blocked download retains all original state");
        Check(Change(manager, "../pending-dest", "n"), "no-transfer change allowed with download pending");
        Check(op.BackendInstall!.InstallationDirectory is null, "download retains its original pinned destination");
        await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);
        Check(Change(manager, "../pending-dest"), "transfer allowed after cancelling download");
    }

    private static void Protection(string root)
    {
        var home = new Home(Path.Combine(root, "protection")); var a = Add(home, "1"); var paths = new BackendPaths(home); var store = new LocalStore(home);
        var manager = new BackendDirectoryManager(home); var server = "busy"; var instanceId = Guid.NewGuid();
        var instance = new InstanceDocument(1, instanceId, Guid.NewGuid(), a.Id, "b1", Environment.ProcessId,
            "detached", "127.0.0.1", 9931, true, [Guid.NewGuid()], [Guid.NewGuid()], $"run/{server}/{instanceId:D}.ini", $"logs/{server}/{instanceId:D}.log");
        store.Update(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, _ => instance, server: server);
        var before = State(home);
        try { Change(manager, "../protected-target"); throw new Exception("busy transfer accepted"); }
        catch (ConfigurationException e) { Check(e.Message.Contains(instanceId.ToString("D")), "blocker identifies uncertain active instance"); }
        Check(State(home) == before && File.Exists(paths.Server(a)), "busy transfer preserves state");
        File.Delete(home.PathFor($"run/{server}/instance.json"));
        var target = Path.Combine(root, "protected-target");
        var targetSlot = Path.Combine(target, a.Id.ToString("D"), a.ServerPath.Split('/')[2]);
        Directory.CreateDirectory(Path.Combine(targetSlot, "bin"));
        var externalFile = Path.Combine(targetSlot, "bin", "llama-server.exe"); File.WriteAllText(externalFile, "external");
        var external = a with { Id = Guid.NewGuid(), Origin = "external", ServerPath = externalFile, TracksLatest = false };
        store.Update(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends, c => c! with { Backends = c.Backends.Append(external).ToArray() });
        before = State(home);
        Reject(() => Change(manager, target), "external destination protected even with overwrite confirmation");
        Check(State(home) == before && File.ReadAllText(externalFile) == "external", "external path/data preserved");
        var other = Path.Combine(root, "external-outside.exe"); File.WriteAllText(other, "outside");
        store.Update(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends, c => c! with { Backends = c.Backends.Select(b => b.Id == external.Id ? b with { ServerPath = other } : b).ToArray() });
        Check(Change(manager, "../safe-target") && List(home).Single(b => b.Id == external.Id).ServerPath == other && File.ReadAllText(other) == "outside", "external references elsewhere remain unchanged");
    }

    private static void AccessFailures(string root)
    {
        var home = new Home(Path.Combine(root, "access")); var a = Add(home, "1"); var manager = new BackendDirectoryManager(home);
        var destination = Path.Combine(root, "access-target");
        var slot = Path.Combine(destination, a.Id.ToString("D"), a.ServerPath.Split('/')[2]);
        Directory.CreateDirectory(Path.Combine(slot, "bin"));
        var collision = Path.Combine(slot, "bin", "llama-server.exe"); File.WriteAllText(collision, "locked destination");
        var before = State(home);
        using (var locked = new FileStream(collision, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Reject(() => Change(manager, destination), "unreadable collision rejects before publishing plan");
        Check(State(home) == before && !File.Exists(home.PathFor("catalogs/backend-directory-transaction.json")), "access failure leaves settings, catalog and transfer journal untouched");
        File.SetAttributes(collision, FileAttributes.ReadOnly);
        try
        {
            Reject(() => Change(manager, destination), "read-only collision rejected before any file replacement");
            Check(State(home) == before && File.ReadAllText(collision) == "locked destination" &&
                !File.Exists(home.PathFor("catalogs/backend-directory-transaction.json")), "read-only target leaves entire transfer unstarted");
        }
        finally { File.SetAttributes(collision, FileAttributes.Normal); }
        if (!OperatingSystem.IsWindows()) return;
        var denied = new DirectoryInfo(Path.Combine(root, "denied-target")); denied.Create();
        var original = denied.GetAccessControl();
        var rules = denied.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        rules.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.Write | FileSystemRights.Delete,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
        try
        {
            denied.SetAccessControl(rules);
            Reject(() => Change(manager, denied.FullName), "destination without write permission rejected before transfer");
            Check(State(home) == before && File.Exists(new BackendPaths(home).Server(a)) &&
                !File.Exists(home.PathFor("catalogs/backend-directory-transaction.json")), "permission denial conserves all resources and configuration");
        }
        finally
        {
            original.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            denied.SetAccessControl(original);
        }
        var sourceDirectory = new DirectoryInfo(new BackendPaths(home).Slot(a));
        original = sourceDirectory.GetAccessControl(); rules = sourceDirectory.GetAccessControl();
        rules.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
        try
        {
            sourceDirectory.SetAccessControl(rules);
            Reject(() => Change(manager, destination), "source deletion permission checked before copying");
            Check(State(home) == before && File.ReadAllText(collision) == "locked destination" &&
                !File.Exists(home.PathFor("catalogs/backend-directory-transaction.json")), "source ACL failure leaves destination collision and all metadata unchanged");
        }
        finally
        {
            original.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            sourceDirectory.SetAccessControl(original);
        }
    }

    private static void Race(string root)
    {
        var home = new Home(Path.Combine(root, "race")); var a = Add(home, "1"); var manager = new BackendDirectoryManager(home);
        var destination = Path.Combine(root, "race-target");
        var slot = Path.Combine(destination, a.Id.ToString("D"), a.ServerPath.Split('/')[2]);
        Directory.CreateDirectory(Path.Combine(slot, "bin"));
        var collision = Path.Combine(slot, "bin", "llama-server.exe"); File.WriteAllText(collision, "prior");
        var next = Path.Combine(slot, "bin", "ggml.dll"); var before = State(home);
        Reject(() => manager.Change(destination, TextWriter.Null, () => "s", () =>
        { File.WriteAllText(next, "new collision"); return true; }), "new collision appearing during confirmation must be reviewed");
        Check(State(home) == before && File.ReadAllText(collision) == "prior" && File.ReadAllText(next) == "new collision", "no old or new collision overwritten from stale authorization");
        File.Delete(next);
        Reject(() => manager.Change(destination, TextWriter.Null, () => "s", () =>
        { File.WriteAllText(collision, "changed collision"); return true; }), "changed destination contents invalidate consent");
        Check(State(home) == before && File.ReadAllText(collision) == "changed collision", "modified collision preserved");
    }

    private static void Recovery(string root, BackendDirectoryStage fail)
    {
        var home = new Home(Path.Combine(root, "recovery-" + fail)); var a = Add(home, "1"); var paths = new BackendPaths(home); var before = State(home);
        var thrown = false;
        var manager = new BackendDirectoryManager(home, fault: stage =>
        {
            if (stage != fail || thrown) return;
            thrown = true;
            if (stage == BackendDirectoryStage.BeforeSourceRemoval) File.Delete(Path.Combine(paths.Slot(a), "licenses", "COPYING"));
            throw new IOException("simulated interruption");
        });
        try { Change(manager, "../recovered-" + fail); throw new Exception("fault not injected"); }
        catch (StorageException e)
        {
            var committed = fail is BackendDirectoryStage.AfterCommit or BackendDirectoryStage.AfterCheckpoint or BackendDirectoryStage.BeforeSourceRemoval;
            Check(e.Published == committed, "publication diagnostic corresponds to transaction commit point: " + fail);
            Check(Directory.Exists(paths.Slot(a)), "source retained until committed recovery: " + fail);
            Check(new BackendDirectoryManager(home).Show().Own == committed, "readers see coherent committed snapshot: " + fail);
            if (!committed) Check(State(home) == before, "precommit catalogs remain intact: " + fail);
        }
        var installer = new BackendInstaller(home); installer.Use("cpu:b1"); // next writer recovers automatically
        Check(new BackendDirectoryManager(home).Show().Own && !Directory.Exists(paths.Slot(a)) &&
            File.Exists(paths.Server(installer.List().Single())), "new store recovers interrupted transfer before next mutation: " + fail);
        Check(!File.Exists(home.PathFor("catalogs/backend-directory-transaction.json")), "recovery journal cleared: " + fail);
    }

    private static byte[] Archive()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("bin/llama-server.exe").Open())) writer.Write("version: 2 (abcdef)");
            using (var writer = new StreamWriter(zip.CreateEntry("bin/ggml.dll").Open())) writer.Write("library");
        }
        return bytes.ToArray();
    }
    private static BackendCandidate Candidate(byte[] bytes) => new("b2", "cpu", "win-x64", "compatible", "fixture",
        [new("fixture.zip", bytes.Length, "https://github.com/ggml-org/llama.cpp/releases/download/b2/fixture.zip")], true);
    private sealed class Transport(byte[] bytes) : IDownloadTransport
    {
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
    private static Task<string> Probe(string path, CancellationToken cancellation) => Task.FromResult(File.ReadAllText(path));
    private static async Task CrossVolume(string root, string volume)
    {
        var home = new Home(Path.Combine(root, "cross-volume")); var manager = new BackendDirectoryManager(home);
        Check(!Path.GetPathRoot(home.Root)!.Equals(Path.GetPathRoot(volume), StringComparison.OrdinalIgnoreCase), "fixture uses distinct drive roots");
        Check(Change(manager, volume, "n"), "nonexistent absolute destination created");
        var archive = Archive(); var injected = false;
        var installer = new BackendInstaller(home, probe: Probe, fault: stage =>
        { if (stage == InstallStage.AfterCopy && !injected) { injected = true; throw new IOException("copy interruption"); } });
        var operation = installer.Create(Candidate(archive), "b2");
        var result = await installer.Downloads.RunAsync(operation.Id, new Transport(archive), finalize: installer.FinalizeAsync);
        Check(injected && result is not null && installer.List().Length == 0, "cross-volume publication failure retains preparation and no catalog entry");
        Check(Directory.Exists(home.PathFor($"downloads/{operation.Id:D}/extracted")), "cross-volume copy keeps original preparation until publication");
        installer = new(home, probe: Probe);
        result = await installer.Downloads.RunAsync(operation.Id, new Transport(archive), finalize: installer.FinalizeAsync);
        var installed = installer.List().Single(); var paths = new BackendPaths(home);
        Check(result is null && installed.InstallationDirectory == volume && File.Exists(paths.Server(installed)), "cross-volume publication recovers verified staged copy");
        Check(!Directory.Exists(home.PathFor($"downloads/{operation.Id:D}")), "successful copied publication cleans download preparation");
        Check(installer.Resolve("cpu:b2").BackendId == installed.Id, "custom absolute backend resolves");
        foreach (var stage in new[] { InstallStage.AfterMove, InstallStage.AfterIndex })
        {
            var previous = installer.List().Single();
            var failing = new BackendInstaller(home, probe: Probe, fault: seen => { if (seen == stage) throw new IOException("publication interruption"); });
            var pending = failing.Create(Candidate(archive), "b2");
            result = await failing.Downloads.RunAsync(pending.Id, new Transport(archive), finalize: failing.FinalizeAsync);
            Check(result is not null && Directory.Exists(home.PathFor($"downloads/{pending.Id:D}/extracted")), "cross-volume recovery retains preparation after " + stage);
            Check(await installer.Downloads.RunAsync(pending.Id, new Transport(archive), finalize: installer.FinalizeAsync) is null,
                "cross-volume recovery completes after " + stage);
            Check(installer.List().Single().Id == previous.Id && !File.Exists(paths.Server(previous)), "recovered publication retains ID and cleans previous slot after " + stage);
        }
        installed = installer.List().Single();
        var interruptedCopy = new BackendInstaller(home, probe: Probe, fault: stage => { if (stage == InstallStage.AfterCopy) throw new IOException("cancel copy"); });
        var toCancel = interruptedCopy.Create(Candidate(archive), "b2");
        _ = await interruptedCopy.Downloads.RunAsync(toCancel.Id, new Transport(archive), finalize: interruptedCopy.FinalizeAsync);
        await installer.Downloads.ControlAsync(toCancel.Id, true, beforeCancel: installer.CancelAsync);
        Check(File.Exists(paths.Server(installed)) && !Directory.Exists(Path.Combine(volume, installed.Id.ToString("D"), toCancel.Id.ToString("D") + ".copy")),
            "cancelling interrupted cross-volume publication removes only owned staged directory");
        operation = installer.Create(Candidate(archive), "b2");
        await installer.Downloads.RunAsync(operation.Id, new Transport(archive), finalize: installer.FinalizeAsync);
        Check(!File.Exists(paths.Server(installed)) && installer.List().Single().Id == installed.Id, "reinstall in custom volume preserves ID and cleans old slot");
        Check(Change(manager, "../cross-return") && !File.Exists(paths.Server(installer.List().Single() with { InstallationDirectory = volume })), "transfer returns installation across volumes");
        Check(File.Exists(paths.Server(installer.List().Single())), "returned installation bytes available");
    }

    private static void Native(string executable, string root)
    {
        var home = new Home(Path.Combine(root, "native-cli")); Add(home, "1");
        var config = new ConfigurationManager(home); using var output = new StringWriter(); using var error = new StringWriter();
        Check(ConfigurationCli.Run("config", ["backends-dir", "set", "../native-location"], output, error, config, () => "n") == 0, "CLI injected choice configures native fixture");
        (int Code, string Out, string Error) Run(params string[] args)
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true, WorkingDirectory = root };
            var env = start.Environment.ToArray(); start.Environment.Clear();
            foreach (var item in env.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)) start.Environment[item.Key] = item.Last().Value;
            start.Environment["COQUENA_HOME"] = home.Root;
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20000)) { process.Kill(true); throw new Exception("native CLI timeout"); }
            return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        var show = Run("config", "backends-dir", "show");
        Check(show.Code == 0 && show.Out.Contains("propio") && show.Out.Contains(Path.Combine(root, "native-location")) && show.Error.Length == 0, "native process restart reads persisted relative setting");
        var state = State(home); var unset = Run("config", "backends-dir", "unset");
        Check(unset.Code == 0 && unset.Out.Contains("Cancelado") && State(home) == state, "native redirected input cancels unset");
        Check(Run("config", "backends-dir", "set").Code == 2, "native CLI rejects invalid syntax");
        Check(Run("backend", "list").Out.Contains(new BackendPaths(home).Server(List(home).Single())), "native list shows actual retained installation path");
        Check(Run("backend", "use", "cpu:b1").Code == 0, "native resolution uses per-resource location");
        Check(Run("--help").Out.Contains("config backends-dir"), "native help advertises directory command");
    }
}
