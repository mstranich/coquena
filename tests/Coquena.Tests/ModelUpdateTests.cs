using System.Diagnostics;
using System.Net;
using Coquena.Contracts;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ModelUpdateTests
{
    private static readonly string Commit = new('a', 40);
    private static RepositoryFile File(string path, char c = 'b') => new(path, 100, new('d', 40), new(c, 64), null);
    private static ModelRecord Record(string name = "owner/repo:Q4_0", string path = "old-Q4_0.gguf", string? type = null) =>
        new(Guid.NewGuid(), name, new("owner/repo", "main", new('e', 40), [File(path).ToRemoteFile()]),
            [new(path, "models/owner/repo/" + path, 100)], [], CompanionType: type);
    private sealed class Interaction(Func<IReadOnlyList<string>, int?> choose, bool interactive = true) : IModelSelectionInteraction
    {
        public bool IsInteractive => interactive;
        public void Show(string text) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation) => Task.FromResult(choose(options));
    }
    private sealed class Remote : HttpMessageHandler
    {
        public string Mode = "changed";
        public int Calls;
        public int Failures;
        public bool OriginalOld;
        public string[] Paths = ["old-Q4_0.gguf", "mmproj-BF16.gguf", "mtp-BF16.gguf"];
        public Action? DuringInventory;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Calls++;
            if (Failures > 0) { Failures--; throw new HttpRequestException("one unit failed"); }
            var path = request.RequestUri!.AbsolutePath;
            if (!path.StartsWith("/api/models/owner/repo/")) throw new Exception("Weights or unexpected endpoint requested");
            if (Mode == "fail") throw new HttpRequestException("fixture network failure");
            if (Mode == "access") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            if (Mode == "missing") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var revision = path.EndsWith("/revision/main");
            if (!revision) DuringInventory?.Invoke();
            var text = revision ? "{\"id\":\"owner/repo\",\"sha\":\"" + Commit + "\"}" :
                "[" + string.Join(',', (Mode == "empty" ? [] : Paths).Select(p =>
                    $"{{\"type\":\"file\",\"path\":\"{p}\",\"size\":100,\"oid\":\"{new string('d', 40)}\",\"lfs\":{{\"oid\":\"{new string(Mode == "same" || OriginalOld && p == "old-Q4_0.gguf" ? 'b' : 'c', 64)}\",\"size\":100}}}}")) + "]";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) };
            if (Mode == "partial" && !revision) response.Headers.TryAddWithoutValidation("Link", "<https://evil.example/tree>; rel=\"next\"");
            return Task.FromResult(response);
        }
    }
    private sealed class Headers(bool fail = false, uint fileType = 2) : IRemoteGgufInspector
    {
        public int Calls;
        public Task<GgufHeader> InspectAsync(RepositoryRevision revision, RepositoryFile file, CancellationToken cancellation)
        {
            Calls++;
            if (fail) throw new ModelSourceException("inspection-limit", "fixture bounded inspection failure");
            return Task.FromResult(new GgufHeader(3, 1, null, null, null, new Dictionary<string, string>(), fileType));
        }
    }
    public static async Task Run(string? executable = null)
    {
        var checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Model update: " + why); }
        var original = Record();
        RepositoryInventory Inventory(params RepositoryFile[] files) => new(new("owner/repo", "main", Commit), files);
        Check(ModelUpdateComparer.Compare(original, Inventory(File("old-Q4_0.gguf"), new("README.md", 1, new('f', 40), null, null))).State == "unchanged", "README/commit alone not an update");
        var changed = ModelUpdateComparer.Compare(original, Inventory(File("old-Q4_0.gguf", 'c')));
        Check(changed.State == "update-available" && changed.Plan!.Target.Commit == Commit && changed.Plan.Actions[0].Action == "download", "changed weights fixed plan");
        var moved = ModelUpdateComparer.Compare(original, Inventory(File("new-Q4_0.gguf")));
        Check(moved.Plan!.Actions[0].Action == "reuse" && moved.Plan.Actions[0].SourcePath == "old-Q4_0.gguf", "unique exact relocation");
        Check(ModelUpdateComparer.Compare(original, Inventory(File("new-Q8_0.gguf"))).Plan!.TargetName == original.Name, "rename never automatically changes quantization");
        Check(ModelUpdateComparer.Compare(original, Inventory(File("a-Q4_0.gguf"), File("b-Q4_0.gguf"))).Candidates.Length == 2, "ambiguous exact relocations");
        Check(ModelUpdateComparer.Compare(original, Inventory(File("next-Q8_0.gguf", 'c'))).State == "intervention-required", "successor not automatic");
        Check(ModelUpdateComparer.Compare(original, Inventory()).State == "orphaned", "complete empty inventory confirms absence");
        Check(ModelUpdateComparer.Compare(original, Inventory(), false).State == "query-failed", "partial absence not orphan");
        var noIdentity = original with { Selection = original.Selection! with { Files = [new("old-Q4_0.gguf", 100)] } };
        Check(ModelUpdateComparer.Compare(noIdentity, Inventory(File("old-Q4_0.gguf"))).State == "intervention-required", "absent identity cannot use size");
        var xet = original with { Selection = original.Selection! with { Files = [new("old-Q4_0.gguf", 100, new("xet", "xet-file", new('b', 64)))] } };
        Check(ModelUpdateComparer.Compare(xet, Inventory(File("old-Q4_0.gguf"))).State == "intervention-required", "Xet vs SHA distinct domains");
        Check(ModelUpdateComparer.Same(new("sha1", "git-blob", new('a', 40)), new("sha1", "git-blob", new('a', 40))), "git blob identity supported");
        Check(!ModelUpdateComparer.Same(new("sha256", "git-blob", new('b', 64)), new("sha256", "file-bytes", new('b', 64))), "domain must match");
        var splitPaths = new[] { "split-Q4_0-00001-of-00002.gguf", "split-Q4_0-00002-of-00002.gguf" };
        var split = original with { Selection = original.Selection! with { Files = splitPaths.Select(p => File(p).ToRemoteFile()).ToArray() },
            Files = splitPaths.Select(p => new InstalledFile(p, "models/owner/repo/" + p, 100)).ToArray() };
        Check(ModelUpdateComparer.Compare(split, Inventory(File(splitPaths[0]))).Plan is null, "one shard insufficient");
        Check(ModelUpdateComparer.Compare(split, Inventory(splitPaths.Select(p => File("folder/" + p)).ToArray())).Plan!.Actions.Length == 2, "whole set relocated");
        var root = Path.Combine(Path.GetTempPath(), "coquena-update-" + Guid.NewGuid());
        var home = new Home(root); var store = new LocalStore(home); var updater = new ModelUpdater(home);
        var companion = Record("owner/repo#mmproj:BF16", "mmproj-BF16.gguf", "mmproj");
        var mtp = Record("owner/repo#mtp:BF16", "mtp-BF16.gguf", "mtp");
        var external = original with { Id = Guid.NewGuid(), Name = "owner/external:Q4_0", Ownership = "external", Selection = null,
            Files = [new("", Path.Combine(root, "external.gguf"), 100)] };
        using var handler = new Remote(); using var client = new HttpClient(handler); var source = new HuggingFaceStore(client);
        var noMenu = new Interaction(_ => throw new Exception("Unexpected menu"), false);
        void Save(params ModelRecord[] records) => store.Update(DocumentKind.Models, ContractJsonContext.Default.ModelsDocument, ContractValidation.Models, _ => new(1, records));
        ModelRecord Read() => updater.Scope(original.Name, false).Single();
        try
        {
            Save(original, companion, mtp, external);
            Check(updater.Scope(null, false).Length == 4 && updater.Scope(null, true).Length == 2, "global and companion scope");
            Check((await updater.CheckAsync(external, source, noMenu)).Update is null && handler.Calls == 0, "external excluded no network");
            var result = await updater.CheckAsync(original, source, noMenu);
            Check(result.Selection!.Commit == original.Selection!.Commit && result.Files.SequenceEqual(original.Files) && result.Name == original.Name, "installed revision and paths unchanged");
            Check(result.Update!.Plan!.Target.Commit == Commit && result.Update.LastSuccess is not null, "plan persisted and dates");
            Check(Read().Update!.Plan!.Actions[0].Action == "download", "source-generated roundtrip");
            foreach (var mode in new[] { "fail", "access", "missing", "partial" })
            {
                handler.Mode = mode;
                var before = Read(); result = await updater.CheckAsync(before, source, noMenu);
                Check(result.Update!.State == "query-failed" && result.Update.LastSuccess == before.Update!.LastSuccess &&
                    result.Update.Plan!.Target.Commit == Commit && result.Update.LastSuccessfulState == "update-available", "failure retains prior success/plan: " + mode);
                Check(result.Update.LastSuccessfulDiagnostic == before.Update!.LastSuccessfulDiagnostic, "retains previous observation detail");
            }
            handler.Mode = "empty";
            result = await updater.CheckAsync(Read(), source, noMenu);
            Check(result.Update!.State == "orphaned" && result.Update.Plan is null, "confirmed selection loss clears candidate only");
            handler.Mode = "same"; handler.Paths = ["a-Q4_0.gguf", "b-Q4_0.gguf"];
            result = await updater.CheckAsync(Read(), source, new Interaction(options =>
            { Check(options.Count == 3 && options[0].Contains("reuse") && options[^1].Contains("Cancelar"), "menu differences and cancel"); return 1; }));
            Check(result.Update!.Plan!.ExplicitChoice && result.Update.Followed!.Files[0].Path == "b-Q4_0.gguf" && result.Files[0].RemotePath == "old-Q4_0.gguf", "explicit follow separate from installed");
            result = await updater.CheckAsync(Read(), source, noMenu);
            Check(result.Update!.State == "update-available" && result.Update.Plan!.Target.Files[0].Path == "b-Q4_0.gguf", "repeat follows chosen relocation");
            Save(original, companion, mtp, external); handler.Mode = "changed"; handler.Paths = ["next-Q8_0.gguf", "mmproj-BF16.gguf"];
            result = await updater.CheckAsync(Read(), source, new Interaction(_ => null));
            Check(result.Update!.State == "intervention-required" && result.Update.Followed is null && result.Update.Plan is null, "cancel preserves unresolved tracking");
            result = await updater.CheckAsync(Read(), source, new Interaction(options => { Check(options.Count == 2 && options[0].Contains("Q8_0"), "roles excluded from successor menu"); return 0; }));
            Check(result.Update!.Plan!.TargetName == "owner/repo:Q8_0" && result.Name == original.Name && result.Update.Plan.Actions[0].Action == "download", "explicit successor plan only");
            handler.Paths = ["relocated-Q8_0.gguf", "old-Q4_0.gguf"];
            handler.OriginalOld = true;
            handler.Mode = "changed";
            result = await updater.CheckAsync(Read(), source, noMenu);
            Check(result.Update!.Plan!.TargetName == "owner/repo:Q8_0" && result.Update.Plan.Target.Files[0].Path == "relocated-Q8_0.gguf", "successor relocation follows chosen content rather than installed origin");
            handler.Paths = ["next-Q8_0.gguf", "other-Q8_0.gguf"];
            result = await updater.CheckAsync(Read(), source, new Interaction(_ => null));
            Check(result.Update!.FollowedName == "owner/repo:Q8_0" && result.Update.Plan is null, "cancel keeps explicit tracking after earlier choice");
            handler.Paths = ["relocated-Q8_0.gguf"];
            result = await updater.CheckAsync(Read(), source, noMenu);
            Check(result.Update!.Plan!.TargetName == "owner/repo:Q8_0", "new query keeps tracking name after unresolved query");
            Save(original); handler.Paths = ["unlabelled.gguf"];
            var headers = new Headers();
            result = await updater.CheckAsync(Read(), source, new Interaction(_ => 0), inspector: headers);
            Check(headers.Calls == 1 && result.Update!.Plan!.TargetName == original.Name, "nonconventional successor through bounded header resolver");
            Save(original);
            result = await updater.CheckAsync(Read(), source, new Interaction(_ => 0), inspector: new Headers(fileType: 7));
            Check(result.Update!.Plan!.TargetName == "owner/repo:Q8_0", "header-resolved successor retains chosen quantization");
            Save(original);
            result = await updater.CheckAsync(Read(), source, noMenu, inspector: new Headers(true));
            Check(result.Update!.State == "intervention-required" && result.Update.Plan is null, "incomplete header evidence never orphaned");
            var installedPath = home.PathFor(original.Files[0].LocalPath);
            Directory.CreateDirectory(Path.GetDirectoryName(installedPath)!); System.IO.File.WriteAllText(installedPath, "fixture untouched bytes");
            Save(original, companion, mtp, external); handler.OriginalOld = false; handler.Paths = ["old-Q4_0.gguf", "mmproj-BF16.gguf", "mtp-BF16.gguf"];
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(ModelCli.Run(["update"], output, error, new ModelInstaller(home), source, interaction: noMenu) == 0 &&
                output.ToString().Contains("no aplica") && updater.Scope(null, true).All(m => m.Update!.State == "update-available"), "global CLI includes companions and external skipped");
            Check(System.IO.File.ReadAllText(installedPath) == "fixture untouched bytes" && Directory.GetDirectories(home.PathFor("downloads")).Length == 0, "no byte changes or transfer operations");
            var invalid = Read() with { Update = Read().Update! with { Plan = Read().Update!.Plan! with { Target = Read().Update!.Plan!.Target with { Commit = "main" } } } };
            try { ContractValidation.Model(invalid); throw new Exception("Expected mutable commit rejection"); }
            catch (ContractException) { Check(true, "mutable revision plan rejected"); }
            Save(original, companion, mtp, external);
            Check(ModelCli.Run(["companion", "update"], output, error, new ModelInstaller(home), source, interaction: noMenu) == 0 && Read().Update is null, "companion CLI limits scope");
            Save(original, companion, mtp, external); handler.Failures = 1;
            Check(ModelCli.Run(["update"], output, error, new ModelInstaller(home), source, interaction: noMenu) == 1 && Read().Update!.State == "query-failed" &&
                updater.Scope(null, true).All(m => m.Update!.State == "update-available"), "failed unit does not block remaining companions");
            Save(original); handler.Mode = "same";
            Check((await updater.CheckAsync(Read(), source, noMenu)).Update!.State == "unchanged", "README-only successful observation");
            Save(original); handler.DuringInventory = () => Save(original with { RefreshedAt = DateTimeOffset.UtcNow });
            try { await updater.CheckAsync(original, source, noMenu); throw new Exception("Expected stale write rejection"); }
            catch (ModelSourceException e) { Check(e.Code == "model-changed" && Read().Update is null, "concurrent record not overwritten"); }
            handler.DuringInventory = null; Save(original);
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); result = await updater.CheckAsync(original, source, noMenu, cancelled.Token);
                Check(result.Update!.State == "query-failed" && result.Update.LastSuccess is null, "cancelled query not success");
            }
            if (executable is not null)
            {
                Save(external);
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("model"); start.ArgumentList.Add("update");
                var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()) env[(string)entry.Key] = (string?)entry.Value;
                start.Environment.Clear(); foreach (var entry in env) start.Environment[entry.Key] = entry.Value;
                start.Environment["COQUENA_HOME"] = root;
                using var process = ModelEngineProcess.Start(start)!;
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                Check(process.ExitCode == 0 && (await stdout).Contains("no aplica") && (await stderr).Length == 0, "published CLI external scope");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine($"OK: {checks} comprobaciones de model update.");
    }
}
