using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ModelDirectoryTests
{
    private static int checks;
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private static void Check(bool value, string why)
    { checks++; if (!value) throw new Exception("Model directory: " + why); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ConfigurationException or StorageException or IOException or UnauthorizedAccessException)
        { checks++; return; }
        throw new Exception("Expected directory rejection");
    }
    private static byte[] Gguf(int? shard)
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory, Encoding.UTF8, true);
        writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(shard is null ? 0ul : 3ul);
        if (shard is not null)
        {
            Text("split.no"); writer.Write(2u); writer.Write((ushort)shard);
            Text("split.count"); writer.Write(2u); writer.Write((ushort)2);
            Text("split.tensors.count"); writer.Write(5u); writer.Write(0);
        }
        writer.Write(new byte[1024]); return memory.ToArray();
        void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write((ulong)bytes.Length); writer.Write(bytes); }
    }
    private sealed class Bytes(byte[][] bytes) : IDownloadTransport
    {
        public Task<HttpResponseMessage> OpenAsync(OperationDocument o, int i, long offset, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes[i]) });
    }
    private static async Task<ModelRecord> Install(Home home, string repo, bool shards = false, ModelInstallStage? fault = null)
    {
        byte[][] bytes = shards ? [Gguf(0), Gguf(1)] : [Gguf(null)];
        var selection = new RemoteSelection(repo, "main", new string('a', 40), bytes.Select((b, i) =>
            new RemoteFile(shards ? $"Model-Q4_K_M-{i + 1:00000}-of-00002.gguf" : "Model-Q4_K_M.gguf", b.Length,
                new("sha256", "file-bytes", Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant()))).ToArray());
        var installer = new ModelInstaller(home, fault: stage => { if (stage == fault) throw new IOException("publication interruption"); });
        var op = installer.Create(new(repo + ":Q4_K_M", null, selection));
        var result = await installer.Downloads.RunAsync(op.Id, new Bytes(bytes), finalize: installer.FinalizeAsync, beforeCancel: installer.CancelAsync);
        if (fault is not null)
        {
            Check(result is not null && op.Files.All(f => File.Exists(home.PathFor(f.PartialPath))), "interrupted publication retains complete preparation");
            installer = new ModelInstaller(home);
            Check(await installer.Downloads.RunAsync(op.Id, new Bytes(bytes), finalize: installer.FinalizeAsync) is null, "new installer recovers cross-volume publication");
        }
        return installer.List().Single(m => m.Record.Id == op.ModelInstall!.ModelId).Record;
    }
    private static bool Change(Home home, string? location, string? answer = "s", Func<bool>? overwrite = null,
        Action<ModelDirectoryStage>? fault = null) => new ModelDirectoryManager(home, fault: fault)
        .Change(location, TextWriter.Null, () => answer, overwrite ?? (() => true));
    private static ModelsDocument Catalog(Home home) => new LocalStore(home).Read(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models)!;
    private static void Recover(Home home) => new LocalStore(home).Update(DocumentKind.Config, Json.ConfigDocument,
        ContractValidation.Config, old => old!);

    public static async Task Run(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-model-dir-" + Guid.NewGuid().ToString("N"));
        var volume = Path.GetFullPath("artifacts/task020-volume-" + Guid.NewGuid().ToString("N"));
        var home = new Home(Path.Combine(root, "home")); home.Ensure();
        try
        {
            var paths = new ModelPaths(home);
            Check(!new ModelDirectoryManager(home).Show().Own, "default source");
            var single = await Install(home, "owner/single"); var shards = await Install(home, "owner/shards", true);
            var original = paths.File(single, single.Files[0]);
            var preset = new ConfigurationManager(home).Create(single.Name, "test");
            Check(!Change(home, "../unused", "c") && !Directory.Exists(Path.Combine(root, "unused")), "cancel has no target");
            Check(!Change(home, "../unused", null), "missing input cancels");
            var instanceId = Guid.NewGuid();
            var instance = new InstanceDocument(1, instanceId, Guid.NewGuid(), Guid.NewGuid(), "b1", Environment.ProcessId,
                "detached", "127.0.0.1", 9931, true, [preset.Id], [single.Id], $"run/busy/{instanceId:D}.ini", $"logs/busy/{instanceId:D}.log");
            new LocalStore(home).Update(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, _ => instance, server: "busy");
            Reject(() => Change(home, "../busy"));
            Check(File.Exists(original) && !Directory.Exists(Path.Combine(root, "busy")), "uncertain running identity blocks entire move");
            File.Delete(home.PathFor("run/busy/instance.json"));
            Check(Change(home, "../next", "n") && File.Exists(original), "retain old installation");
            var other = await Install(home, "owner/other");
            Check(other.InstallationDirectory == "../next" && paths.File(other, other.Files[0]).StartsWith(Path.Combine(root, "next")), "new installation uses configured relative base");
            Check(new ModelInstaller(home).List().All(m => m.Availability == "available" && !m.Changed), "mixed locations resolve");
            var file = Path.Combine(root, "file"); File.WriteAllText(file, "keep");
            Reject(() => Change(home, file, "n")); Reject(() => Change(home, file + "/child", "n"));
            Reject(() => Change(home, home.PathFor("catalogs")));
            var readonlySource = paths.File(other, other.Files[0]); File.SetAttributes(readonlySource, FileAttributes.ReadOnly);
            try { Reject(() => Change(home, "../readonly")); Check(File.Exists(original), "access rejection precedes any copies"); }
            finally { File.SetAttributes(readonlySource, FileAttributes.Normal); }
            if (OperatingSystem.IsWindows())
            {
                var denied = new DirectoryInfo(Path.Combine(root, "denied")); denied.Create();
                var originalAcl = denied.GetAccessControl(); var rules = denied.GetAccessControl();
                using var identity = WindowsIdentity.GetCurrent();
                rules.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.Write,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
                try
                {
                    denied.SetAccessControl(rules);
                    Reject(() => Change(home, denied.FullName));
                    Check(File.Exists(original) && !File.Exists(home.PathFor("catalogs/model-directory-transaction.json")), "ACL denial leaves move unstarted");
                }
                finally { denied.SetAccessControl(originalAcl); }
            }
            Directory.CreateDirectory(volume);
            var collision = new ModelPaths(home).Resolve(volume, "models/owner/single/Model-Q4_K_M.gguf");
            Directory.CreateDirectory(Path.GetDirectoryName(collision)!); File.WriteAllText(collision, "previous");
            var external = new BackendRecord(Guid.NewGuid(), "b1", "1", "external", "manual", "cpu", "cpu", "win-x64", collision);
            new LocalStore(home).Update(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends, _ => new(1, [external]));
            Reject(() => Change(home, volume));
            Check(File.ReadAllText(collision) == "previous", "external destination protected before confirmation");
            new LocalStore(home).Update(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends, _ => new(1, []));
            var neighbor = Path.Combine(volume, "keep.txt"); File.WriteAllText(neighbor, "keep");
            Check(!Change(home, volume, overwrite: () => false) && File.ReadAllText(collision) == "previous", "collision cancellation");
            var confirmations = 0;
            Reject(() => Change(home, volume, overwrite: () => { File.WriteAllText(collision, "changed"); return true; }));
            Check(File.Exists(original), "new collision content never authorized");
            Check(Change(home, volume, overwrite: () => { confirmations++; return true; }) && confirmations == 1, "one confirmation");
            Check(!File.Exists(original) && File.ReadAllText(neighbor) == "keep", "cross volume move preserves unrelated files");
            Check(new ModelInstaller(home).List().All(m => m.Availability == "available" && !m.Changed), "all shards and timestamps preserved");
            Check(Catalog(home).Models.Select(m => m.Id).ToHashSet().SetEquals([single.Id, shards.Id, other.Id]), "identities preserved");
            Check(new ConfigurationManager(home).View(preset.Name).Preset.ModelId == single.Id, "preset identity preserved");
            var backend = new BackendRecord(Guid.NewGuid(), "b11482", "1", "ggml", "ggml-org/llama.cpp", "cpu", "cpu", "win-x64", "backends/fixture.exe");
            var currentSingle = Catalog(home).Models.Single(m => m.Id == single.Id);
            Check(new ConfigurationManager(home).Compose(backend, [preset.Id]).Ini.Contains(paths.File(currentSingle, currentSingle.Files[0])), "INI resolves actual relocated model path");
            Check(Change(home, null) && !new ModelDirectoryManager(home).Show().Own, "unset moves to default");
            Check(new ModelInstaller(home).List().All(m => m.Availability == "available"), "default relocation resolves");

            foreach (var stage in Enum.GetValues<ModelDirectoryStage>())
            {
                var target = "../fault-" + stage;
                var oldPath = paths.File(Catalog(home).Models[0], Catalog(home).Models[0].Files[0]);
                Reject(() => Change(home, target, fault: s => { if (s == stage) throw new IOException("injected"); }));
                Check(File.Exists(oldPath), "origin retained after " + stage);
                var committed = stage is ModelDirectoryStage.AfterCommit or ModelDirectoryStage.AfterCheckpoint or ModelDirectoryStage.BeforeSourceRemoval;
                Check(new ModelDirectoryManager(home).Show().Location == (committed ? target : Catalog(home).Models[0].InstallationDirectory), "coherent readers at " + stage);
                Recover(home);
                Check(new ModelDirectoryManager(home).Show().Location == target && !File.Exists(oldPath), "new writer recovers " + stage);
                Check(new ModelInstaller(home).List().All(m => m.Availability == "available"), "recovery keeps complete units");
            }
            Check(Change(home, "../relative"), "relative relocation");
            var relocated = root + "-relocated";
            Directory.Move(root, relocated); root = relocated; home = new Home(Path.Combine(root, "home"));
            Check(new ModelInstaller(home).List().All(m => m.Availability == "available"), "relative catalog survives home relocation");

            var pending = new ModelInstaller(home).Create(new("owner/pending:Q4_K_M", null,
                new("owner/pending", "main", new string('a', 40), [new("Model-Q4_K_M.gguf", 24)])));
            var before = File.ReadAllText(home.PathFor("config.json"));
            Reject(() => Change(home, "../blocked"));
            Check(File.ReadAllText(home.PathFor("config.json")) == before && !Directory.Exists(Path.Combine(root, "blocked")), "pending operation blocks entire move");
            Check(Change(home, volume, "n") && pending.ModelInstall!.InstallationDirectory == "../relative", "pending destination remains pinned after retain choice");
            await new ModelInstaller(home).Downloads.ControlAsync(pending.Id, true, beforeCancel: new ModelInstaller(home).CancelAsync);
            await Install(home, "owner/cross-volume", true);
            await Install(home, "owner/cross-recovery-move", true, ModelInstallStage.AfterMove);
            await Install(home, "owner/cross-recovery-index", true, ModelInstallStage.AfterIndex);
            Check(new ModelInstaller(home).List().All(m => m.Availability == "available"), "download preparation on C publishes on S");
            Check(Change(home, "../relative", "n"), "restore relative configuration for native check");
            if (executable is not null)
            {
                var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetTempPath(), RedirectStandardOutput = true,
                    RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
                start.Environment["COQUENA_HOME"] = home.Root;
                var environment = start.Environment.ToArray(); start.Environment.Clear();
                foreach (var item in environment.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)) start.Environment[item.Key] = item.Last().Value;
                start.ArgumentList.Add("config"); start.ArgumentList.Add("models-dir"); start.ArgumentList.Add("show");
                using var process = Process.Start(start)!; process.StandardInput.Close();
                var stdout = await process.StandardOutput.ReadToEndAsync(); var stderr = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
                Check(process.ExitCode == 0 && stdout.Contains(Path.Combine(root, "relative")) && stderr.Length == 0, "native CLI resolves from another cwd");
                Check(ConfigurationCli.Run("config", ["models-dir", "set"], TextWriter.Null, TextWriter.Null) == 2, "invalid syntax rejected before home access");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(volume)) Directory.Delete(volume, true);
        }
        Console.WriteLine($"OK: {checks} comprobaciones de directorio de modelos.");
    }
}
