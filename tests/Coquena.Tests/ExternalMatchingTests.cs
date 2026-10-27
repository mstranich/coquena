using System.Net;
using Coquena.Contracts;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ExternalMatchingTests
{
    private sealed class Interaction(Func<IReadOnlyList<string>, int?> choose, bool interactive = true) : IModelSelectionInteraction
    {
        public bool IsInteractive => interactive;
        public void Show(string text) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation) => Task.FromResult(choose(options));
    }
    private sealed class Remote(long size) : HttpMessageHandler
    {
        public int Calls; public string Mode = "normal"; public string? InventoryText;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Calls++;
            var path = request.RequestUri!.AbsolutePath;
            if (Mode == "fail") throw new HttpRequestException("fixture remote failure");
            if (!path.StartsWith("/api/models/owner/repo/")) throw new Exception("Unexpected repository or weights request");
            var text = path.EndsWith("/revision/main") ? "{\"id\":\"owner/repo\",\"sha\":\"" + new string('a', 40) + "\"}" :
                InventoryText ?? (Mode == "empty" ? "[]" : "[" + string.Join(',', new[] { "A-BF16.gguf", "B-BF16.gguf", "mmproj-BF16.gguf", "mtp-BF16.gguf", "wrong-Q4_0.gguf" }
                    .Select(p => $"{{\"type\":\"file\",\"path\":\"{p}\",\"size\":{size},\"oid\":\"{new string('b', 40)}\"}}")) + "]");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        }
    }
    public static void Run(string? executable = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-match-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var checks = 0;
        void Check(bool value, string why) { checks++; if (!value) throw new Exception("Matching: " + why); }
        try
        {
            var bytes = ExternalModelTests.Fixture();
            using var remote = new Remote(bytes.Length); using var client = new HttpClient(remote);
            var source = new HuggingFaceStore(client);
            var home = new Home(Path.Combine(root, "home")); var installer = new ModelInstaller(home);
            int Add(string key, bool companion, bool flag, IModelSelectionInteraction interaction)
            {
                var path = Path.Combine(root, key + (companion ? "-companion" : "") + ".gguf"); File.WriteAllBytes(path, bytes);
                using var output = new StringWriter(); using var error = new StringWriter();
                var args = new List<string>(); if (companion) args.Add("companion");
                args.AddRange(["add", path, "--name", "owner/repo" + (companion ? "#mmproj" : "") + ":" + key]);
                if (flag) args.Add("--try-match");
                return ModelCli.Run(args.ToArray(), output, error, installer, source, interaction: interaction);
            }
            Check(Add("BF16", false, true, new Interaction(options =>
            {
                Check(installer.List().Any(m => m.Record.Name == "owner/repo:BF16" && m.Record.InformativeMatch is null), "persisted before menu");
                Check(options.Count == 3 && options[0].Contains("A-BF16") && options[1].Contains("B-BF16") && options[2].Contains("Cancelar"), "ambiguous explicit choices and cancel");
                return 1;
            })) == 0, "successful add");
            var record = installer.List().Single().Record;
            Check(record.InformativeMatch?.Candidate.Files[0].Path == "B-BF16.gguf" && record.Selection is null && record.Ownership == "external" && record.Files[0].RemotePath == "", "informative only and persisted");
            Check(File.ReadAllBytes(record.Files[0].LocalPath).SequenceEqual(bytes) && record.Files[0].Integrity is null, "no byte changes or integrity certificate");
            Check(remote.Calls == 2, "only bounded revision and inventory requests");
            Check(Add("BF16", true, true, new Interaction(options => { Check(options.Count == 2 && options[0].Contains("mmproj"), "companion role filtered"); return null; })) == 0, "companion cancellation preserves add");
            Check(installer.List().Single(m => m.Record.CompanionType == "mmproj").Record.InformativeMatch is null, "cancel leaves no match");
            var companionRecord = installer.List().Single(m => m.Record.CompanionType == "mmproj").Record;
            var companionMatch = ExternalMatching.SelectAsync(companionRecord, source, new Interaction(_ => 0), default).GetAwaiter().GetResult();
            installer.SaveInformativeMatch(companionRecord, companionMatch!);
            Check(installer.List().Single(m => m.Record.Id == companionRecord.Id).Record.InformativeMatch!.Candidate.Files[0].Path == "mmproj-BF16.gguf", "companion selected match persists independently");
            try { ExternalMatching.SelectAsync(record, source, new Interaction(_ => -1), default).GetAwaiter().GetResult(); throw new Exception("Expected invalid choice"); }
            catch (ModelSourceException e) { Check(e.Code == "invalid-choice", "invalid choice rejected without catalog changes"); }
            var mismatchedSize = record with { Files = [record.Files[0] with { Size = bytes.Length + 1 }] };
            Check(ExternalMatching.SelectAsync(mismatchedSize, source, new Interaction(_ => throw new Exception("No menu")), default).GetAwaiter().GetResult() is null, "size disagreement no candidate");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try { ExternalMatching.SelectAsync(record, source, new Interaction(_ => 0), cancelled.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
                catch (OperationCanceledException) { Check(installer.List().Single(m => m.Record.Id == record.Id).Record.InformativeMatch!.Candidate.Files[0].Path == "B-BF16.gguf", "cancellation preserves catalog"); }
            }
            var calls = remote.Calls;
            Check(Add("F16", false, false, new Interaction(_ => throw new Exception("No menu"))) == 0 && calls == remote.Calls, "no flag no network");
            Check(Add("Q8_0", false, true, new Interaction(_ => throw new Exception("No menu"), false)) == 0 && calls == remote.Calls, "noninteractive preserves add no network");
            remote.Mode = "empty";
            Check(Add("Q4_K_M", false, true, new Interaction(_ => throw new Exception("No menu"))) == 0, "no candidates preserves add");
            remote.Mode = "fail";
            Check(Add("Q5_K_M", false, true, new Interaction(_ => throw new Exception("No menu"))) == 0 && installer.List().Length == 6, "remote failure preserves add");
            var store = new LocalStore(home);
            store.Update(DocumentKind.Models, ContractJsonContext.Default.ModelsDocument, ContractValidation.Models,
                d => d! with { Models = d.Models.Select(m => m.Id == record.Id ? m with { RefreshedAt = DateTimeOffset.UtcNow } : m).ToArray() });
            try { installer.SaveInformativeMatch(record, record.InformativeMatch!); throw new Exception("Expected concurrency rejection"); }
            catch (ModelSourceException e) { Check(e.Code == "model-changed", "concurrent record update preserved"); }
            remote.Mode = "normal";
            var mtpPath = Path.Combine(root, "mtp.gguf"); File.WriteAllBytes(mtpPath, bytes);
            var mtp = installer.AddExternal(mtpPath, "owner/repo#mtp:BF16", true);
            Check(ExternalMatching.SelectAsync(mtp, source, new Interaction(options => { Check(options.Count == 2 && options[0].Contains("mtp-BF16"), "mtp role distinct"); return 0; }), default).GetAwaiter().GetResult() is not null, "mtp candidate");
            var first = Path.Combine(root, "split-00001-of-00002.gguf");
            var second = Path.Combine(root, "split-00002-of-00002.gguf");
            File.WriteAllBytes(first, ExternalModelTests.Fixture(0, 2)); File.WriteAllBytes(second, ExternalModelTests.Fixture(1, 2));
            var split = installer.AddExternal(first, "owner/repo:Q4_0");
            remote.InventoryText = "[" + string.Join(',', new[] { "remote-Q4_0-00001-of-00002.gguf", "remote-Q4_0-00002-of-00002.gguf" }.Select(p =>
                $"{{\"type\":\"file\",\"path\":\"{p}\",\"size\":{new FileInfo(first).Length},\"oid\":\"{new string('b', 40)}\"}}")) + "]";
            Check(ExternalMatching.SelectAsync(split, source, new Interaction(_ => 0), default).GetAwaiter().GetResult()?.Candidate.Files.Length == 2, "whole shard unit candidate");
            File.Delete(second);
            Check(ExternalMatching.SelectAsync(split, source, new Interaction(_ => throw new Exception("No menu")), default).GetAwaiter().GetResult() is null, "incomplete local unit preserved without match");
            if (executable is not null)
            {
                var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                var env = start.Environment.ToArray(); start.Environment.Clear();
                foreach (var g in env.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)) start.Environment[g.Key] = g.Last().Value;
                start.Environment["COQUENA_HOME"] = Path.Combine(root, "native");
                foreach (var arg in new[] { "model", "add", record.Files[0].LocalPath, "--name", record.Name, "--try-match" }) start.ArgumentList.Add(arg);
                using var process = ModelEngineProcess.Start(start)!; process.StandardInput.Close();
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(20000)) { process.Kill(true); throw new Exception("Native match timeout"); }
                Check(process.ExitCode == 0 && stdout.Result.Contains("sin emparejamiento") && stderr.Result.Length == 0, "native noninteractive add");
                Check(new ModelInstaller(new Home(start.Environment["COQUENA_HOME"]!)).List().Single().Record.Selection is null, "native external persisted");
            }
            Console.WriteLine($"OK: {checks} comprobaciones de emparejamiento externo.");
        }
        finally { Directory.Delete(root, true); }
    }
}
