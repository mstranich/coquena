using System.Diagnostics;
using Coquena.Contracts;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class LocalIntegrityTests
{
    public static async Task Run(string? executable = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-integrity-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var home = new Home(Path.Combine(root, "home"));
        var installer = new ModelInstaller(home);
        var maintenance = new LocalModelMaintenance(home);
        var store = new LocalStore(home);
        var json = ContractJsonContext.Default;
        var checks = 0;
        void Check(bool value, string why) { checks++; if (!value) throw new Exception("Local integrity: " + why); }
        string Catalog() => File.ReadAllText(home.PathFor("catalogs/models.json"));
        try
        {
            var path = Path.Combine(root, "single.gguf");
            File.WriteAllBytes(path, ExternalModelTests.Fixture());
            var model = installer.AddExternal(path, "user/local:BF16");
            var noReference = await maintenance.VerifyAsync(model.Name);
            Check(noReference.IntegrityCheck is { Status: "no-reference", SetBaseline: false } && noReference.Files[0].Integrity is null, "no implicit base");
            var baseline = await maintenance.VerifyAsync(model.Name, setBaseline: true);
            Check(baseline.IntegrityCheck is { Status: "baseline-set", SetBaseline: true } && FileIntegrity.Comparable(baseline.Files[0].Integrity), "explicit baseline distinct from verified");
            var expected = baseline.Files[0].Integrity;
            var verified = await maintenance.VerifyAsync(model.Name);
            Check(verified.IntegrityCheck is { Status: "verified", SetBaseline: false } && verified.IntegrityCheck.CheckedAt >= baseline.IntegrityCheck!.CheckedAt &&
                verified.BaselineAt == baseline.IntegrityCheck.CheckedAt, "comparison dated and original baseline date retained");
            using (var stream = new FileStream(path, FileMode.Append)) stream.WriteByte(123);
            var mismatch = await maintenance.VerifyAsync(model.Name);
            Check(mismatch.IntegrityCheck!.Status == "mismatch" && mismatch.Files[0].Integrity == expected, "changed full bytes never rebase");
            var refresh = maintenance.Refresh(model.Name);
            Check(!refresh.Changed && refresh.Record.Files[0].Size == new FileInfo(path).Length && refresh.Record.Files[0].Integrity == expected && refresh.Record.IntegrityCheck!.Status == mismatch.IntegrityCheck!.Status && refresh.Record.IntegrityCheck.CheckedAt == mismatch.IntegrityCheck.CheckedAt && refresh.Record.RefreshedAt is not null, "refresh observes without declaring integrity");
            var replacement = await maintenance.VerifyAsync(model.Name, setBaseline: true);
            Check(replacement.Files[0].Integrity != expected && replacement.IntegrityCheck!.Status == "baseline-set", "explicit replacement");
            var before = Catalog();
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await maintenance.VerifyAsync(model.Name, cancellation: cancelled.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { Check(Catalog() == before, "cancel preserves catalog"); }
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var failed = await maintenance.VerifyAsync(model.Name, setBaseline: true);
                Check(failed.IntegrityCheck!.Status == "read-error" && failed.Files[0].Integrity == replacement.Files[0].Integrity, "locked read preserves base");
            }
            var first = Path.Combine(root, "split-00001-of-00002.gguf");
            var second = Path.Combine(root, "split-00002-of-00002.gguf");
            File.WriteAllBytes(first, ExternalModelTests.Fixture(0, 2));
            var split = installer.AddExternal(first, "user/split:BF16");
            var missing = await maintenance.VerifyAsync(split.Name, setBaseline: true);
            Check(missing.IntegrityCheck!.Status == "missing-file" && missing.IntegrityCheck.Files[0].Status == "no-reference" && missing.IntegrityCheck.Files[1].Status == "missing-file" && missing.Files.All(f => f.Integrity is null), "no partial baseline on missing shard");
            Check(maintenance.Refresh(split.Name).Availability == "incomplete", "refresh retains expected missing shards");
            File.WriteAllBytes(second, ExternalModelTests.Fixture(1, 2));
            var splitBase = await maintenance.VerifyAsync(split.Name, setBaseline: true);
            Check(splitBase.Files.All(f => FileIntegrity.Comparable(f.Integrity)) && (await maintenance.VerifyAsync(split.Name)).IntegrityCheck!.Status == "verified", "whole set comparison");
            File.Delete(second);
            var splitFailed = await maintenance.VerifyAsync(split.Name, setBaseline: true);
            Check(splitFailed.Files.Select(f => f.Integrity).SequenceEqual(splitBase.Files.Select(f => f.Integrity)), "failed replacement preserves entire previous base");
            File.WriteAllBytes(second, ExternalModelTests.Fixture(1, 2));
            var changing = new LocalModelMaintenance(home, i =>
            {
                if (i == 1) { using var stream = new FileStream(first, FileMode.Append); stream.WriteByte(111); }
            });
            var unstable = await changing.VerifyAsync(split.Name, setBaseline: true);
            Check(unstable.IntegrityCheck!.Status == "changed-during-read" && unstable.IntegrityCheck.Files[0].Observed is null &&
                unstable.Files.Select(f => f.Integrity).SequenceEqual(splitBase.Files.Select(f => f.Integrity)), "changed set cannot replace any base");
            var concurrent = new LocalModelMaintenance(home, _ => store.Update(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models,
                old => old! with { Models = old.Models.Select(m => m.Id == model.Id ? m with { Parameters = new() { ["ctx-size"] = "123" } } : m).ToArray() }));
            try { await concurrent.VerifyAsync(model.Name); throw new Exception("Concurrent change overwritten"); }
            catch (ModelSourceException e)
            {
                Check(e.Code == "model-changed" && installer.List().Single(m => m.Record.Id == model.Id).Record.Parameters["ctx-size"] == "123",
                    "concurrent catalog changes preserved");
            }
            var auxiliary = Path.Combine(root, "aux.gguf"); File.WriteAllBytes(auxiliary, ExternalModelTests.Fixture());
            var companion = installer.AddExternal(auxiliary, "user/local#mtp:BF16", true);
            Check((await maintenance.VerifyAsync(companion.Name, true, true)).IntegrityCheck!.Status == "baseline-set", "companion baseline");
            try { maintenance.Refresh(companion.Name); throw new Exception("Wrong catalog accepted"); }
            catch (ModelSourceException e) { Check(e.Code == "model-not-found", "principal does not select companion"); }
            var managedPath = home.PathFor("models/user/managed/m.gguf"); Directory.CreateDirectory(Path.GetDirectoryName(managedPath)!);
            File.WriteAllBytes(managedPath, ExternalModelTests.Fixture());
            var digest = (await FileIntegrity.VerifyAsync(managedPath, null)).Observed;
            var managed = new ModelRecord(Guid.NewGuid(), "user/managed:BF16", new("user/managed", "main", new string('a', 40), [new("m.gguf", new FileInfo(managedPath).Length)]),
                [new("m.gguf", "models/user/managed/m.gguf", new FileInfo(managedPath).Length, digest)], []);
            store.Update(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models, old => old! with { Models = old.Models.Append(managed).ToArray() });
            Check((await maintenance.VerifyAsync(managed.Name)).IntegrityCheck!.Status == "verified", "managed download reference");
            before = Catalog();
            try { await maintenance.VerifyAsync(managed.Name, setBaseline: true); throw new Exception("Managed rebase accepted"); }
            catch (ModelSourceException e) { Check(e.Code == "managed-baseline-rejected" && before == Catalog(), "managed base flag rejected without changes"); }
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(ModelCli.Run(["verify", model.Name], output, error, installer) == 0 && output.ToString().Contains("verified"), "CLI compare");
            Check(ModelCli.Run(["companion", "refresh", companion.Name], output, error, installer) == 0, "CLI companion refresh");
            Check(ModelCli.Run(["verify", model.Name, "--invalid"], output, error, installer) == 2, "invalid flag rejected");
            if (executable is not null)
            {
                (int Code, string Out) Native(params string[] args)
                {
                    var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    var env = start.Environment.ToArray(); start.Environment.Clear();
                    foreach (var group in env.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)) start.Environment[group.Key] = group.Last().Value;
                    start.Environment["COQUENA_HOME"] = home.Root;
                    foreach (var arg in args) start.ArgumentList.Add(arg);
                    using var process = ModelEngineProcess.Start(start)!;
                    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(20000)) { process.Kill(true); throw new Exception("Native timeout"); }
                    return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
                }
                Check(Native("model", "verify", model.Name).Code == 0, "native verify persisted reference");
                var nativeBase = Native("model", "companion", "verify", companion.Name, "--set-baseline");
                Check(nativeBase.Code == 0 && nativeBase.Out.Contains("no certifica") && nativeBase.Out.Contains("baseline-set"), "native companion baseline warning");
                Check(Native("model", "refresh", model.Name).Code == 0, "native refresh");
                Check(Native("model", "verify", managed.Name, "--set-baseline").Code == 1, "native managed rejection");
                File.Delete(second);
                Check(Native("model", "verify", split.Name).Out.Contains("missing-file"), "native missing shard");
                using (var stream = new FileStream(path, FileMode.Append)) stream.WriteByte(234);
                Check(Native("model", "verify", model.Name).Code == 1, "native mismatch");
                Check(Native("model", "refresh", model.Name).Code == 0 && Native("model", "verify", model.Name).Code == 1, "native refresh never rebases");
            }
            Console.WriteLine($"OK: {checks} comprobaciones de refresh/integridad local.");
        }
        finally { Directory.Delete(root, true); }
    }
}
