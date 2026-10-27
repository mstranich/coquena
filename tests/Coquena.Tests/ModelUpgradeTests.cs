using System.Net;
using System.Security.Cryptography;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Storage;
using Coquena.Configuration;
using Coquena.Servers;

namespace Coquena.Tests;

internal static class ModelUpgradeTests
{
    private static int checks;
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception("Upgrade: " + why); }
    private static byte[] Bytes(int? index = null, byte marker = 1)
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(index is null ? 0ul : 3ul);
        if (index is not null)
        {
            Text("split.no"); writer.Write(2u); writer.Write((ushort)index.Value);
            Text("split.count"); writer.Write(2u); writer.Write((ushort)2);
            Text("split.tensors.count"); writer.Write(5u); writer.Write(0);
        }
        writer.Write(Enumerable.Repeat(marker, 512).ToArray()); return memory.ToArray();
        void Text(string s) { writer.Write((ulong)s.Length); writer.Write(System.Text.Encoding.UTF8.GetBytes(s)); }
    }
    private static ContentIdentity Digest(byte[] bytes) => new("sha256", "file-bytes", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    private sealed class Transport(byte[][] bytes, string commit, Action? before = null) : IDownloadTransport
    {
        public int Calls;
        public Task<HttpResponseMessage> OpenAsync(OperationDocument op, int i, long offset, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++; before?.Invoke();
            Check(op.ModelSelection!.Commit == commit, "fixed commit");
            Check(offset == 0, "fixture full transfer");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes[i]) });
        }
    }
    private static void Fails(Action action, string code)
    {
        try { action(); throw new Exception("Expected " + code); }
        catch (DownloadException e) { Check(e.Code == code, code + " got " + e.Code); }
        catch (ModelSourceException e) { Check(e.Code == code, code + " got " + e.Code); }
    }
    private sealed class Fixture : IDisposable
    {
        public Home Home { get; } = new(Path.Combine(Path.GetTempPath(), "coquena-upgrade-" + Guid.NewGuid()));
        public LocalStore Store;
        public ModelInstaller Installer;
        public Fixture() { Home.Ensure(); Store = new(Home); Installer = new(Home); }
        public ModelRecord Seed(byte[][] bytes, string? companion = null, string? location = null)
        {
            var repo = "owner/repo";
            var files = bytes.Select((b, i) => new RemoteFile(bytes.Length == 1 ? "old.gguf" : $"old-{i + 1:00000}-of-00002.gguf", b.Length, Digest(b))).ToArray();
            var record = new ModelRecord(Guid.NewGuid(), repo + (companion is null ? "" : "#" + companion) + ":Q4_K_M",
                new(repo, "main", new string('a', 40), files), files.Select((f, i) => new InstalledFile(f.Path,
                    (location is null ? "models/" : "") + repo + "/" + f.Path, bytes[i].Length, Digest(bytes[i]))).ToArray(), [], location, CompanionType: companion);
            for (var i = 0; i < bytes.Length; i++)
            {
                var path = new ModelPaths(Home).File(record, record.Files[i]); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes[i]);
            }
            Store.Update(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models, _ => new(1, [record])); return record;
        }
        public ModelRecord Plan(ModelRecord record, byte[][] bytes, bool rename = false, bool samePaths = true)
        {
            var repo = rename ? "owner/renamed" : record.Selection!.Repository;
            var target = new RemoteSelection(repo, "main", new string('b', 40), bytes.Select((b, i) =>
                new RemoteFile(samePaths ? record.Files[i].RemotePath : bytes.Length == 1 ? "new.gguf" : $"new-{i + 1:00000}-of-00002.gguf", b.Length, Digest(b))).ToArray());
            var actions = target.Files.Select(f => { var old = record.Selection!.Files.FirstOrDefault(n => ModelUpdateComparer.Same(n.Identity, f.Identity)); return new ModelUpdateAction(f.Path, old?.Path, old is null ? "download" : "reuse"); }).ToArray();
            var updated = record with { Update = new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "update-available", "fixture", "update-available",
                new(record.Selection!, target, repo + (record.CompanionType is null ? "" : "#" + record.CompanionType) + ":Q4_K_M", actions, rename), target) };
            Store.Update(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models, _ => new(1, [updated])); return updated;
        }
        public ModelRecord Record => Installer.List().Single().Record;
        public byte[] Read(ModelRecord record, int i = 0) => File.ReadAllBytes(new ModelPaths(Home).File(record, record.Files[i]));
        public Task<OperationDocument?> Run(OperationDocument op, IDownloadTransport transport, ModelInstaller? installer = null, CancellationToken token = default)
        { installer ??= Installer; return installer.Downloads.RunAsync(op.Id, transport, cancellation: token, finalize: installer.FinalizeAsync, beforeCancel: installer.CancelAsync); }
        public void Cancel(OperationDocument op) => Installer.Downloads.ControlAsync(op.Id, true, beforeCancel: Installer.CancelAsync).GetAwaiter().GetResult();
        public void Dispose() { if (Directory.Exists(Home.Root)) Directory.Delete(Home.Root, true); }
    }

    public static async Task Run(string? executable)
    {
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes(0), Bytes(1)]; byte[][] next = [old[0], Bytes(1, 2)];
            var baseline = f.Plan(f.Seed(old), next); var op = f.Installer.CreateUpgrade(baseline);
            var witness = f.Read(baseline, 1);
            var transport = new Transport(next, new string('b', 40), () => Check(f.Read(baseline, 1).SequenceEqual(witness), "old shards retained while transferring"));
            Check(await f.Run(op, transport) is null, "complete upgrade");
            Check(transport.Calls == 1, "unchanged shard reused without HTTP");
            Check(f.Record.Id == baseline.Id && f.Record.Selection!.Commit == new string('b', 40), "same identity and target revision");
            Check(f.Read(f.Record).SequenceEqual(next[0]) && f.Read(f.Record, 1).SequenceEqual(next[1]), "no mixed shards");
            Check(f.Record.Update is { Plan: null, State: "unchanged" }, "consumed plan");
            Check(f.Installer.Downloads.List().Length == 0, "operation cleaned");
        }
        using (var f = new Fixture())
        {
            byte[][] bytes = [Bytes()]; var baseline = f.Plan(f.Seed(bytes, "mmproj", "custom-models"), bytes, true, false);
            var op = f.Installer.CreateUpgrade(baseline); var transport = new Transport(bytes, new string('b', 40));
            var oldPath = new ModelPaths(f.Home).File(baseline, baseline.Files[0]);
            Check(await f.Run(op, transport) is null && transport.Calls == 0, "rename companion no download");
            Check(f.Record.Id == baseline.Id && f.Record.Name == "owner/renamed#mmproj:Q4_K_M", "renamed ID");
            Check(f.Record.InstallationDirectory == "custom-models" && !File.Exists(oldPath), "stored base and old removal");
            Check(f.Read(f.Record).SequenceEqual(bytes[0]), "reused bytes");
        }
        foreach (var stage in new[] { ModelInstallStage.AfterMove, ModelInstallStage.AfterIndex, ModelInstallStage.BeforeCleanup })
        {
            using var f = new Fixture(); byte[][] bytes = [Bytes(0), Bytes(1)]; byte[][] next = [Bytes(0, 2), Bytes(1, 2)];
            var baseline = f.Plan(f.Seed(bytes), next); var op = f.Installer.CreateUpgrade(baseline);
            var hit = false;
            var faulty = new ModelInstaller(f.Home, fault: s => { if (!hit && s == stage) { hit = true; throw new IOException("fixture interruption"); } });
            try { await f.Run(op, new Transport(next, new string('b', 40)), faulty); } catch (IOException) { }
            Check(hit, "injected " + stage);
            var recovered = f.Installer.Downloads.Status(op.Id);
            Check(recovered.Phase is "publishing" or "cleanup", "durable publication phase");
            var noNetwork = new Transport([], new string('b', 40));
            Check(await f.Run(recovered, noNetwork) is null && noNetwork.Calls == 0, "recovery no network " + stage);
            Check(f.Record.Id == baseline.Id && f.Read(f.Record, 1).SequenceEqual(next[1]), "recover complete unit " + stage);
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes(0), Bytes(1)]; byte[][] next = [Bytes(0, 2), Bytes(1, 2)];
            var baseline = f.Plan(f.Seed(old), next); var op = f.Installer.CreateUpgrade(baseline);
            var faulty = new ModelInstaller(f.Home, fault: stage => { if (stage == ModelInstallStage.AfterMove) throw new IOException("interrupt"); });
            await f.Run(op, new Transport(next, new string('b', 40)), faulty);
            f.Cancel(f.Installer.Downloads.Status(op.Id));
            Check(f.Record.Selection!.Commit == baseline.Selection!.Commit && f.Read(baseline).SequenceEqual(old[0]) && f.Read(baseline, 1).SequenceEqual(old[1]), "cancel restores overwritten shards");
            Check(f.Installer.Downloads.List().Length == 0, "cancel cleanup");
        }
        using (var f = new Fixture())
        {
            byte[][] bytes = [Bytes()]; var baseline = f.Plan(f.Seed(bytes), bytes, false, false);
            var target = f.Home.PathFor("models/owner/repo/new.gguf"); File.WriteAllText(target, "foreign");
            Fails(() => f.Installer.CreateUpgrade(baseline), "destination-collision");
            Check(File.ReadAllText(target) == "foreign", "foreign file preserved"); File.Delete(target);
            Fails(() => new ModelInstaller(f.Home, availableBytes: () => 0).CreateUpgrade(baseline), "insufficient-space");
            var op = f.Installer.CreateUpgrade(baseline);
            Fails(() => f.Installer.CreateUpgrade(baseline), "upgrade-pending");
            Fails(() => f.Installer.Refresh(baseline.Name), "upgrade-pending");
            File.WriteAllBytes(new ModelPaths(f.Home).File(baseline, baseline.Files[0]), Bytes(marker: 3));
            var noNetwork = new Transport(bytes, new string('b', 40));
            var failed = await f.Run(op, noNetwork);
            Check(failed?.Diagnostic == "reuse-integrity-failed" && noNetwork.Calls == 0, "reuse hashes local bytes");
            Check(f.Record.Selection!.Commit == baseline.Selection!.Commit, "failure preserves installed revision"); f.Cancel(op);
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes()]; byte[][] next = [Bytes(marker: 2)]; var baseline = f.Plan(f.Seed(old), next);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var op = f.Installer.CreateUpgrade(baseline);
            var paused = await f.Run(op, new Transport(next, new string('b', 40)), token: cancelled.Token);
            Check(paused?.State == "paused" && f.Read(baseline).SequenceEqual(old[0]), "cancellation retains old bytes");
            Check(await f.Run(op, new Transport(next, new string('b', 40))) is null, "resume fixed plan");
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes()]; byte[][] next = [Bytes(marker: 2)]; var baseline = f.Plan(f.Seed(old), next);
            var instance = new InstanceDocument(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "fixture", Environment.ProcessId,
                "detached", "127.0.0.1", 9931, false, [Guid.NewGuid()], [baseline.Id], "run/fixture/a.ini", "logs/fixture/a.log");
            f.Store.Update(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, _ => instance, server: "fixture");
            Fails(() => f.Installer.CreateUpgrade(baseline), "model-in-use");
            Check(f.Installer.Downloads.List().Length == 0, "in-use rejected before download reservation");
            File.Delete(f.Home.PathFor("run/fixture/instance.json"));
            var op = f.Installer.CreateUpgrade(baseline);
            var backend = new BackendRecord(Guid.NewGuid(), "b11482", "fixture", "ggml", "ggml-org/llama.cpp", "cpu", "cpu", "win-x64", "backends/fixture/llama-server.exe", true);
            Directory.CreateDirectory(Path.GetDirectoryName(f.Home.PathFor(backend.ServerPath))!); File.WriteAllText(f.Home.PathFor(backend.ServerPath), "fixture");
            f.Store.Update(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends, _ => new(1, [backend]));
            var config = new ConfigurationManager(f.Home); var servers = new ServerManager(f.Home);
            servers.EnsureInitial(); new Coquena.Backends.BackendInstaller(f.Home).Use("cpu:latest");
            config.Create(baseline.Name, "chat"); servers.Presets(null, ["chat"], true);
            try { new ServerRuntime(f.Home).Start(null, true, TextWriter.Null); throw new Exception("start must block"); }
            catch (ConfigurationException e) { Check(e.Message.Contains("Upgrade pendiente"), "start reservation under writer lease"); }
            Check(!File.Exists(f.Home.PathFor("run/default/instance.json")), "no process publication during upgrade");
            var presetBefore = File.ReadAllBytes(f.Home.PathFor("catalogs/presets.json"));
            Check(await f.Run(op, new Transport(next, new string('b', 40))) is null, "upgrade after blocked start");
            Check(presetBefore.SequenceEqual(File.ReadAllBytes(f.Home.PathFor("catalogs/presets.json"))), "references retained");
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes(0), Bytes(1)]; byte[][] next = [Bytes(0, 2), Bytes(1, 2)];
            var baseline = f.Plan(f.Seed(old), next); var op = f.Installer.CreateUpgrade(baseline);
            var count = 0;
            var faulty = new ModelInstaller(f.Home, fault: stage => { if (stage == ModelInstallStage.AfterMove && ++count == 2) throw new IOException("interrupt"); });
            await f.Run(op, new Transport(next, new string('b', 40)), faulty);
            File.Delete(new ModelPaths(f.Home).File(baseline, baseline.Files[0]));
            var result = await f.Run(f.Installer.Downloads.Status(op.Id), new Transport([], new string('b', 40)));
            Check(result?.State == "failed" && f.Record.Selection!.Commit == baseline.Selection!.Commit, "lost confirmed destination cannot be indexed");
            f.Cancel(f.Installer.Downloads.Status(op.Id));
            Check(f.Read(baseline).SequenceEqual(old[0]) && f.Read(baseline, 1).SequenceEqual(old[1]), "cancel restores missing confirmed destination");
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes(0), Bytes(1)]; byte[][] next = [Bytes(0, 2), Bytes(1, 2)];
            var baseline = f.Plan(f.Seed(old), next); var op = f.Installer.CreateUpgrade(baseline);
            var broken = next.Select(b => b.ToArray()).ToArray(); broken[1][^1] ^= 1;
            var result = await f.Run(op, new Transport(broken, new string('b', 40)));
            Check(result?.State == "failed" && f.Record.Selection!.Commit == baseline.Selection!.Commit, "bad download cannot publish revision");
            Check(f.Read(baseline).SequenceEqual(old[0]) && f.Read(baseline, 1).SequenceEqual(old[1]), "bad download retains all old shards"); f.Cancel(op);
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes()]; byte[][] next = [Bytes(marker: 2)]; var baseline = f.Plan(f.Seed(old), next);
            f.Store.Update(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models, _ => new(1, [baseline with { RefreshedAt = DateTimeOffset.UtcNow }]));
            Fails(() => f.Installer.CreateUpgrade(baseline), "stale-upgrade-plan");
            Check(f.Read(baseline).SequenceEqual(old[0]) && f.Installer.Downloads.List().Length == 0, "stale snapshot preserves bytes");
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes()]; byte[][] next = [Bytes(marker: 2)]; var baseline = f.Plan(f.Seed(old), next, true, false);
            var op = f.Installer.CreateUpgrade(baseline);
            var faulty = new ModelInstaller(f.Home, fault: stage => { if (stage == ModelInstallStage.AfterMove) throw new IOException("interrupt"); });
            await f.Run(op, new Transport(next, new string('b', 40)), faulty);
            f.Cancel(f.Installer.Downloads.Status(op.Id));
            Check(f.Record.Id == baseline.Id && f.Read(baseline).SequenceEqual(old[0]), "rename cancellation retains old installation");
            Check(!File.Exists(f.Home.PathFor("models/owner/renamed/new.gguf")), "rename cancellation removes own new file");
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes()]; byte[][] next = [Bytes(marker: 2)]; var baseline = f.Plan(f.Seed(old), next);
            var op = f.Installer.CreateUpgrade(baseline);
            var faulty = new ModelInstaller(f.Home, fault: stage => { if (stage == ModelInstallStage.AfterIndex) throw new IOException("interrupt"); });
            await f.Run(op, new Transport(next, new string('b', 40)), faulty);
            f.Cancel(f.Installer.Downloads.Status(op.Id));
            Check(f.Record.Selection!.Commit == new string('b', 40) && f.Read(f.Record).SequenceEqual(next[0]), "cancel after publication keeps new installation");
            Check(f.Installer.Downloads.List().Length == 0, "cancel after index finishes cleanup");
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes()]; byte[][] next = [Bytes(marker: 2)]; var baseline = f.Plan(f.Seed(old), next);
            var op = f.Installer.CreateUpgrade(baseline);
            var result = await f.Run(op, new Transport(next, new string('b', 40), () => throw new IOException("disk full")));
            Check(result?.State == "failed" && f.Read(baseline).SequenceEqual(old[0]), "IO failure retains installation"); f.Cancel(op);
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes()]; byte[][] next = [Bytes(marker: 2)]; var baseline = f.Plan(f.Seed(old), next);
            var auxiliary = baseline with { Id = Guid.NewGuid(), Name = "owner/auxiliary#mtp:Q4_K_M", CompanionType = "mtp", Update = null,
                Selection = baseline.Selection! with { Repository = "owner/auxiliary" },
                Files = baseline.Files.Select(n => n with { LocalPath = n.LocalPath.Replace("owner/repo", "owner/auxiliary") }).ToArray() };
            var auxPath = new ModelPaths(f.Home).File(auxiliary, auxiliary.Files[0]); Directory.CreateDirectory(Path.GetDirectoryName(auxPath)!); File.WriteAllBytes(auxPath, old[0]);
            auxiliary = f.Plan(auxiliary, next);
            var extPath = Path.Combine(f.Home.Root, "external.gguf"); File.WriteAllBytes(extPath, old[0]);
            var external = new ModelRecord(Guid.NewGuid(), "owner/external:Q4_K_M", null, [new("", extPath, old[0].Length)], [], Ownership: "external");
            f.Store.Update(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models, _ => new(1, [baseline, auxiliary, external]));
            var instance = new InstanceDocument(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "fixture", Environment.ProcessId,
                "detached", "127.0.0.1", 9931, false, [Guid.NewGuid()], [baseline.Id], "run/fixture/a.ini", "logs/fixture/a.log");
            f.Store.Update(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, _ => instance, server: "fixture");
            using var output = new StringWriter(); using var error = new StringWriter(); var transport = new Transport(next, new string('b', 40));
            Check(ModelCli.Run(["upgrade"], output, error, f.Installer, transport: transport) == 1, "global reports individual failure");
            var records = f.Store.Read(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models)!.Models;
            Check(records.Single(m => m.Id == baseline.Id).Selection!.Commit == new string('a', 40), "global blocked principal retained");
            Check(records.Single(m => m.Id == auxiliary.Id).Selection!.Commit == new string('b', 40) && transport.Calls == 1, "global continues companion after failure");
            Check(File.ReadAllBytes(extPath).SequenceEqual(old[0]) && records.Single(m => m.Id == external.Id).Ownership == "external", "global excludes external");
            Check(output.ToString().Contains("no aplica: externo"), "global explains external exclusion");
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes()]; byte[][] next = [Bytes(marker: 2)]; var baseline = f.Plan(f.Seed(old), next);
            File.WriteAllBytes(new ModelPaths(f.Home).File(baseline, baseline.Files[0]), Bytes(marker: 3));
            var damaged = f.Read(baseline); var op = f.Installer.CreateUpgrade(baseline);
            var faulty = new ModelInstaller(f.Home, fault: stage => { if (stage == ModelInstallStage.AfterMove) throw new IOException("interrupt"); });
            await f.Run(op, new Transport(next, new string('b', 40)), faulty);
            f.Cancel(f.Installer.Downloads.Status(op.Id));
            Check(f.Read(baseline).SequenceEqual(damaged) && f.Record.Files[0].Integrity == baseline.Files[0].Integrity, "rollback preserves observed bytes without redefining old integrity");
        }
        using (var f = new Fixture())
        {
            byte[][] old = [Bytes()]; byte[][] next = [Bytes(marker: 2)]; var baseline = f.Plan(f.Seed(old), next);
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(ModelCli.Run(["upgrade"], output, error, f.Installer, transport: new Transport(next, new string('b', 40))) == 0, "global CLI");
            Check(error.ToString().Length == 0 && f.Record.Id == baseline.Id, "global CLI applied");
            Check(ModelCli.Run(["companion", "upgrade"], output, error, f.Installer, transport: new Transport([], "")) == 0, "companion scope empty");
        }
        if (executable is not null)
        {
            using var f = new Fixture();
            var info = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("model"); info.ArgumentList.Add("upgrade"); info.Environment["COQUENA_HOME"] = f.Home.Root;
            using var process = ModelEngineProcess.Start(info)!;
            var output = await process.StandardOutput.ReadToEndAsync(); var error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
            Check(process.ExitCode == 0 && error.Length == 0 && output.Contains("Sin unidades"), "published CLI upgrade");
        }
        Console.WriteLine($"OK: {checks} comprobaciones upgrade modelos.");
    }
}
