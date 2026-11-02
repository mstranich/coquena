using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class RecipeUpgradeTests
{
    public static async Task CrashWorker(string folder, ScopeWriteStage stage)
    {
        var selection = new RecipeSelection(folder); var context = ScopeContext.Project(selection.Files[0]);
        await new RecipeUpgrade(context).ApplyAsync([selection], new HashSet<string>(), HardwareProbe.Inspect(), new Transport(Bytes(1)), new Transport(Bytes(1)),
            (_, _, _) => Task.CompletedTask, fault: reached => { if (reached == stage) Environment.Exit(73); });
        throw new Exception("Crash stage not reached");
    }
    private static byte[] Bytes(byte marker)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(0ul); writer.Write(marker);
        return stream.ToArray();
    }
    private static ContentIdentity Hash(byte[] bytes) => new("sha256", "file-bytes", Convert.ToHexStringLower(SHA256.HashData(bytes)));
    private static RemoteSelection Selection(byte[] bytes, char commit) => new("fixture/repository", "main", new string(commit, 40), [new("weights-Q4_0.gguf", bytes.Length, Hash(bytes))]);
    private sealed class Transport(byte[] bytes) : IDownloadTransport
    {
        public bool Fail; public bool Interrupt; public Action? During; public int Calls; public List<long> Offsets = [];
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            Calls++; Offsets.Add(offset); During?.Invoke();
            if (Fail || Interrupt && offset > 0) throw new HttpRequestException("fixture");
            var response = new HttpResponseMessage(offset == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent) { Content = Interrupt ? new StreamContent(new BrokenStream(bytes)) : new ByteArrayContent(bytes[(int)offset..]) };
            response.Content.Headers.ContentLength = bytes.Length - offset;
            response.Headers.ETag = new("\"fixture\"");
            if (offset > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
            return Task.FromResult(response);
        }
    }
    private sealed class BrokenStream(byte[] bytes) : Stream
    {
        private bool sent;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (sent) throw new IOException("fixture interruption");
            sent = true; bytes.AsSpan(0, 8).CopyTo(buffer.Span); return ValueTask.FromResult(8);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class Initial(ScopeContext context, byte[] bytes) : ICombinationResources
    {
        public async Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exact, CancellationToken cancellation)
        {
            var models = new ScopedModels(context); var name = request.Intent.Text ?? request.Intent.Get("repository")!.Text + ":" + request.Intent.Get("quantization")!.Text;
            var selected = new SelectedModel(name, null, Selection(bytes, 'a') with { Repository = ModelRequest.Parse(name).Repository });
            var prepared = await models.AcquireAsync(selected, new Transport(bytes), false, cancellation: cancellation);
            models.Bind(request.Key, request.Kind, selected, prepared);
            var installation = models.List().Single(r => r!["resource-id"]!.GetValue<string>() == prepared.ResourceId)!["installations"]![0]!;
            return new(request.Key, request.Kind, prepared.ResourceId, prepared.InstallationId, prepared.EntryPath,
                new JsonObject { ["origin"] = "hugging-face", ["name"] = selected.Name,
                    ["selection"] = JsonNode.Parse(ContractJson.Write(selected.Selection, ContractJsonContext.Default.RemoteSelection)),
                    ["artifacts"] = new JsonArray(installation["files"]!.AsArray().Select(f => (JsonNode)new JsonObject { ["path"] = f!["path"]!.DeepClone(),
                        ["size"] = f["size"]!.DeepClone(), ["identity"] = f["integrity"]!.DeepClone() }).ToArray()) });
        }
    }
    private sealed class Metadata(byte[] bytes) : HttpMessageHandler
    {
        public string PathName = "weights-Q4_0.gguf";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var repository = request.RequestUri!.AbsolutePath.Split("/revision/")[0].Replace("/api/models/", "");
            var body = request.RequestUri!.AbsolutePath.Contains("/revision/") ? "{\"id\":\"" + repository + "\",\"sha\":\"" + new string('b', 40) + "\"}" :
                "[{\"type\":\"file\",\"path\":\"" + PathName + "\",\"oid\":\"" + new string('e', 40) + "\",\"size\":" + bytes.Length + ",\"lfs\":{\"oid\":\"" + Hash(bytes).Value + "\",\"size\":" + bytes.Length + "}}]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
    private sealed class Quiet : IModelSelectionInteraction
    {
        public bool IsInteractive => false;
        public void Show(string value) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> values, CancellationToken token) => throw new Exception("unexpected prompt");
    }
    public static async Task Run(string root, string? product)
    {
        Directory.CreateDirectory(root); int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        var old = Bytes(0); var next = Bytes(1);
        using var client = new HttpClient(new Metadata(next));
        var probe = new ResourceUpdateProbe(new(client), (_, _) => throw new Exception("unexpected resolution"), new Quiet());
        async Task<(ScopeContext Context, RecipeSelection Selection)> Setup(string name)
        {
            var directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "coquena.yaml"), "models:\n  a: fixture/repository:Q4_0\n  b: fixture/second:Q4_0\npresets:\n  p:\n    args:\n      model: model://a\n      model-draft: model://b\n");
            var selection = new RecipeSelection(directory); var context = ScopeContext.Project(selection.Files[0]);
            await new CombinationPreparation(context).PrepareAsync(selection, new Initial(context, old));
            await new RecipeUpdatePlans(context).UpdateAsync([selection], new HashSet<string>(), probe);
            return (context, selection);
        }
        static string Locks(ScopeContext c) => string.Join("\n", new ScopeStore(c).Read().Documents.Where(d => d.Value["kind"]!.GetValue<string>() == "resolution").Select(d => d.Value["data"]!.ToJsonString()));
        var hardware = HardwareProbe.Inspect();
        Task<JsonObject> Apply(ScopeContext c, RecipeSelection s, Transport t, HashSet<string>? filters = null, Action<ScopeWriteStage>? fault = null)
            => new RecipeUpgrade(c).ApplyAsync([s], filters ?? [], hardware, t, t, (_, _, _) => Task.CompletedTask, fault: fault);
        var first = await Setup("failure"); var firstLocks = Locks(first.Context);
        var inventory = new ScopedModels(first.Context).List().ToJsonString();
        var bad = new Transport(next); bad.During = () => { if (bad.Calls == 2) bad.Fail = true; };
        try { await Apply(first.Context, first.Selection, bad); throw new Exception("expected failure"); }
        catch (Exception e) when (e is DownloadException or HttpRequestException) { }
        Check(Locks(first.Context) == firstLocks && new ScopedModels(first.Context).List().ToJsonString() == inventory, "failure preserves complete locks and inventory");
        var good = new Transport(next);
        var applied = await Apply(first.Context, first.Selection, good);
        Check(applied["applied"]!.GetValue<int>() == 2 && good.Calls == 1, "retry retains completed candidate and exact pending operation");
        Check(Locks(first.Context) != firstLocks, "joint resolution publication");
        Check(new ScopedModels(first.Context).List().All(r => r!["installations"]!.AsArray().Count == 2), "each old revision retained with its prepared candidate");
        try { await Apply(first.Context, first.Selection, good); throw new Exception("expected consumed plan"); }
        catch (StorageException e) { Check(e.Code == "stale-update-plan", "consumed plan cannot be reapplied"); }
        var partial = await Setup("partial");
        var state = new ScopeStore(partial.Context).Read(); var docs = state.Documents.ToDictionary(d => d.Key, d => (JsonObject)d.Value.DeepClone());
        foreach (var d in docs.Values) d["revision"] = d["revision"]!.GetValue<long>() + 1;
        var observation = docs["local/update-plan.json"]["data"]!["plans"]!.AsObject().First().Value!["resources"]![1]!;
        observation["status"] = "query-failed"; observation.AsObject().Remove("proposed");
        new ScopeStore(partial.Context).Commit(state.Revision, docs);
        var result = await Apply(partial.Context, partial.Selection, new(next));
        Check(result["applied"]!.GetValue<int>() == 1 && result["resources"]![1]!["upgrade-status"]!.GetValue<string>() == "blocked", "valid proposals apply while blocked units remain reported");
        var stale = await Setup("stale"); var staleLocks = Locks(stale.Context);
        var editing = new Transport(next) { During = () => File.AppendAllText(stale.Selection.Files[0], "# concurrent\n") };
        try { await Apply(stale.Context, stale.Selection, editing); throw new Exception("expected stale intent"); }
        catch (StorageException e) { Check(e.Code == "stale-update-plan" && Locks(stale.Context) == staleLocks, "concurrent YAML edit preserves prior locks"); }
        var concurrent = await Setup("concurrent");
        var nested = new Transport(next); bool rejected = false;
        nested.During = () =>
        {
            try { Apply(concurrent.Context, concurrent.Selection, new(next)).GetAwaiter().GetResult(); }
            catch (StorageException e) { rejected = e.Code == "upgrade-in-progress"; }
        };
        await Apply(concurrent.Context, concurrent.Selection, nested);
        Check(rejected, "OS handle excludes duplicate batch executor");
        var reserved = await Setup("reserved");
        try
        {
            await new RecipeUpgrade(reserved.Context, () => throw new IOException("fixture close after reservation"))
                .ApplyAsync([reserved.Selection], new HashSet<string>(), hardware, new Transport(next), new Transport(next), (_, _, _) => Task.CompletedTask);
            throw new Exception("expected reserved interruption");
        }
        catch (IOException) { }
        Check(new ScopeTransfers(reserved.Context).Downloads.List().Length == 0, "durable batch reservation precedes transfer checkpoint creation");
        var reservedResult = await Apply(reserved.Context, reserved.Selection, new(next));
        Check(reservedResult["applied"]!.GetValue<int>() == 2, "retry reconstructs exactly reserved transfer after close");
        var interrupted = await Setup("interrupted"); var beforeInterruption = Locks(interrupted.Context);
        try { await Apply(interrupted.Context, interrupted.Selection, new(next) { Interrupt = true }); throw new Exception("expected interruption"); }
        catch (Exception e) when (e is DownloadException or IOException) { }
        var pendingId = new ScopeTransfers(interrupted.Context).Downloads.List().Single().Id;
        Check(Locks(interrupted.Context) == beforeInterruption, "partial stream does not publish locks");
        var resumedTransport = new Transport(next);
        await new ScopeTransfers(interrupted.Context).RunAsync(pendingId, resumedTransport);
        Check(resumedTransport.Offsets.Contains(8) && Locks(interrupted.Context) == beforeInterruption, "download resume continues exact prefix but does not publish batch resolutions");
        await Apply(interrupted.Context, interrupted.Selection, resumedTransport);
        Check(Locks(interrupted.Context) != beforeInterruption && resumedTransport.Calls == 2, "upgrade uses externally resumed candidate and completes remaining unit");
        foreach (var stage in new[] { ScopeWriteStage.Prepared, ScopeWriteStage.Decided })
        {
            var crash = await Setup("crash-" + stage); var previous = Locks(crash.Context);
            var worker = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                worker.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.dll"));
            foreach (var argument in new[] { "--upgrade-crash-worker", crash.Selection.Base, stage.ToString() }) worker.ArgumentList.Add(argument);
            using (var child = System.Diagnostics.Process.Start(worker)!)
            {
                var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Check(child.ExitCode == 73, "actual process exits at durable publication stage: " + stage + " " + await stderr);
                await stdout;
            }
            if (stage == ScopeWriteStage.Prepared)
            {
                Check(Locks(crash.Context) == previous, "before durable decision previous set remains");
                if (product is null) await Apply(crash.Context, crash.Selection, new(next));
                else
                {
                    var start = new System.Diagnostics.ProcessStartInfo(product) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    foreach (var arg in new[] { "upgrade", "--project-directory", crash.Selection.Base }) start.ArgumentList.Add(arg);
                    using var process = System.Diagnostics.Process.Start(start)!;
                    var stdout = await process.StandardOutput.ReadToEndAsync(); var stderr = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
                    Check(process.ExitCode == 0 && stdout.Contains("prepared-for-next-application") && stderr.Length == 0, "Native AOT commits recovered exact candidates without network");
                }
                Check(Locks(crash.Context) != previous, "retry publishes complete prepared set");
            }
            else Check(Locks(crash.Context) != previous, "after durable decision reader reconciles complete generation");
        }
        var filtered = await Setup("selector");
        await new RecipeUpdatePlans(filtered.Context).UpdateAsync([filtered.Selection], new HashSet<string> { "model://a" }, probe);
        var noCalls = new Transport(next);
        try { await Apply(filtered.Context, filtered.Selection, noCalls, ["model://b"]); throw new Exception("unknown selector accepted"); }
        catch (ArgumentException) { Check(noCalls.Calls == 0, "partial plan never broadens selection"); }
        var corrupt = await Setup("corrupt"); var priorCorrupt = Locks(corrupt.Context);
        try { await Apply(corrupt.Context, corrupt.Selection, new(next), fault: stage => { if (stage == ScopeWriteStage.Prepared) throw new IOException("fixture stop"); }); }
        catch (IOException) { }
        var pendingCandidate = new ScopeStore(corrupt.Context).Read().Documents["local/update-plan.json"]["data"]!["upgrades"]!.AsObject().First().Value!["units"]![0]!["candidate"]!;
        File.WriteAllBytes(corrupt.Context.PathFor(pendingCandidate["location"]!.GetValue<string>() + "/" + pendingCandidate["files"]![0]!["path"]!.GetValue<string>()), old);
        try { await Apply(corrupt.Context, corrupt.Selection, new(next)); throw new Exception("corrupt candidate accepted"); }
        catch (StorageException e) { Check(e.Code == "upgrade-integrity-failed" && Locks(corrupt.Context) == priorCorrupt, "full rehash rejects changed prepared candidate without partial publication"); }
        var rename = await Setup("rename");
        using var renamedClient = new HttpClient(new Metadata(old) { PathName = "renamed-Q4_0.gguf" });
        var renamedProbe = new ResourceUpdateProbe(new(renamedClient), (_, _) => throw new Exception("unexpected lookup"), new Quiet());
        await new RecipeUpdatePlans(rename.Context).UpdateAsync([rename.Selection], new HashSet<string>(), renamedProbe);
        var unused = new Transport(next) { Fail = true };
        var renamed = await Apply(rename.Context, rename.Selection, unused);
        Check(renamed["applied"]!.GetValue<int>() == 2 && unused.Calls == 0, "exact renamed content reuses verified old bytes without network");
        var aliases = await Setup("aliases");
        File.WriteAllText(aliases.Selection.Files[0], File.ReadAllText(aliases.Selection.Files[0]).Replace("fixture/second", "fixture/repository"));
        await new CombinationPreparation(aliases.Context).PrepareAsync(aliases.Selection, new Initial(aliases.Context, old));
        await new RecipeUpdatePlans(aliases.Context).UpdateAsync([aliases.Selection], new HashSet<string>(), probe);
        var sharedTransport = new Transport(next);
        await Apply(aliases.Context, aliases.Selection, sharedTransport);
        var aliasBindings = new ScopeStore(aliases.Context).Read().Documents["local/combinations.json"]["data"]!["combinations"]![CombinationIdentity.FromSelection(aliases.Selection).Id]!["bindings"]!.AsArray();
        Check(sharedTransport.Calls == 1 && aliasBindings.Select(b => b!["installation-id"]!.GetValue<string>()).Distinct().Count() == 1,
            "aliases of same exact target share transfer and physical installation");
        var backendFolder = Path.Combine(root, "backend"); Directory.CreateDirectory(backendFolder);
        var backendFile = Path.Combine(backendFolder, "coquena.yaml");
        const string backendYaml = "name: fixture\nservice:\n  backend:\n    version: b1\n    variant: cpu\n";
        File.WriteAllText(backendFile, backendYaml);
        var bc = ScopeContext.Project(backendFile); var bs = new RecipeSelection(backendFolder);
        var fixture = new ScopedBackendTests.Fixture(); fixture.Add("b1", "cpu"); fixture.Add("b2", "cpu");
        var backendManager = new ScopedBackends(bc, fixture.Resolve, (p, _) => Task.FromResult($"version: {File.ReadAllText(p)} (abcdef)"));
        var resources = new ScopedCombinationResources(bc, hardware, new(client), fixture, new Quiet(), backends: backendManager);
        await new CombinationPreparation(bc).PrepareAsync(bs, resources);
        fixture.Build = "b2";
        var backendProbe = new ResourceUpdateProbe(new(client), (variant, token) => fixture.Resolve(hardware, variant, "latest", token), new Quiet());
        await new RecipeUpdatePlans(bc).UpdateAsync([bs], new HashSet<string>(), backendProbe);
        var pinned = await new RecipeUpgrade(bc).ApplyAsync([bs], new HashSet<string>(), hardware, fixture, fixture, backendManager.ValidateOfficial);
        Check(pinned["applied"]!.GetValue<int>() == 0 && pinned["resources"]![0]!["upgrade-status"]!.GetValue<string>() == "blocked", "fixed pin reports blocked without transfer");
        File.WriteAllText(backendFile, backendYaml.Replace("version: b1", "version: latest"));
        fixture.Build = "b1";
        await new CombinationPreparation(bc).PrepareAsync(bs, resources);
        fixture.Build = "b2";
        await new RecipeUpdatePlans(bc).UpdateAsync([bs], new HashSet<string>(), backendProbe);
        var runtime = new ScopeStore(bc).Read(); var runtimeDocs = runtime.Documents.ToDictionary(d => d.Key, d => ProjectDocuments.Create(d.Value["kind"]!.GetValue<string>(), d.Value["revision"]!.GetValue<long>() + 1, d.Value["data"]!.AsObject()));
        var active = new JsonObject { ["fixture"] = "active snapshot", ["resources"] = runtimeDocs["local/combinations.json"]["data"]!["combinations"]![CombinationIdentity.FromSelection(bs).Id]!["bindings"]!.DeepClone() };
        runtimeDocs["local/active.json"] = ProjectDocuments.Create("active", 1, active);
        runtimeDocs["local/last-good.json"] = ProjectDocuments.Create("last-good", 1, active);
        new ScopeStore(bc).Commit(runtime.Revision, runtimeDocs);
        var resolutionCalls = fixture.Resolutions;
        var upgradedBackend = await new RecipeUpgrade(bc).ApplyAsync([bs], new HashSet<string>(), hardware, fixture, fixture, backendManager.ValidateOfficial);
        Check(upgradedBackend["applied"]!.GetValue<int>() == 1 && fixture.Resolutions == resolutionCalls, "backend applies exact artifact selection without resolving latest again");
        var backendInventory = backendManager.List();
        Check(backendInventory.Count == 2 && backendInventory[0]!["installations"]![0]!["protected"]!.GetValue<bool>(), "new build retains protected previously pinned canonical resource");
        var afterBackend = new ScopeStore(bc).Read();
        Check(JsonNode.DeepEquals(afterBackend.Documents["local/active.json"]["data"], active) && JsonNode.DeepEquals(afterBackend.Documents["local/last-good.json"]["data"], active), "active and last-good fixture snapshots unchanged");
        await new CombinationPreparation(bc).PrepareAsync(bs, resources);
        var backendBinding = new ScopeStore(bc).Read().Documents["local/combinations.json"]["data"]!["combinations"]![CombinationIdentity.FromSelection(bs).Id]!["bindings"]![0]!;
        Check(backendBinding["resource-id"]!.GetValue<string>() == backendInventory[1]!["resource-id"]!.GetValue<string>(), "following preparation keeps upgraded exact backend");
        var globalContext = ScopeContext.Global(new Home(Path.Combine(root, "global-home")));
        Directory.CreateDirectory(globalContext.Root);
        File.WriteAllText(globalContext.PathFor("recipe.yaml"), "models:\n  a: fixture/repository:Q4_0\npresets:\n  p:\n    args:\n      model: model://a\n");
        var globalSelection = new RecipeSelection(globalContext.Base, [globalContext.PathFor("recipe.yaml")], globalContext.Base);
        await new CombinationPreparation(globalContext).PrepareAsync(globalSelection, new Initial(globalContext, old));
        var globalStore = new ScopeStore(globalContext); var globalState = globalStore.Read();
        var globalDocs = globalState.Documents.ToDictionary(d => d.Key, d => ProjectDocuments.Create(d.Value["kind"]!.GetValue<string>(), d.Value["revision"]!.GetValue<long>() + 1, d.Value["data"]!.AsObject()));
        globalDocs["recipe.yaml"] = ProjectDocuments.Create("recipe-intention", 1, new JsonObject { ["text"] = File.ReadAllText(globalSelection.Files[0]), ["path-bases"] = new JsonObject() });
        globalStore.Commit(globalState.Revision, globalDocs);
        await new RecipeUpdatePlans(globalContext).UpdateAsync([globalSelection], new HashSet<string>(), probe);
        var globalResult = await Apply(globalContext, globalSelection, new(next));
        Check(globalResult["applied"]!.GetValue<int>() == 1 && new ScopeStore(globalContext).Read().Documents["local/recipes.json"]["data"]?["import-resolutions"]?["model:a"] is not null,
            "global upgrade publishes exact entity resolution for subsequent imports");
        var previousHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
        Environment.SetEnvironmentVariable("COQUENA_HOME", globalContext.Base);
        try
        {
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(Cli.Run(["upgrade", "--global"], output, error) == 2 && error.ToString().Contains("stale-update-plan"), "global CLI reads consumed plan without querying network");
        }
        finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previousHome); }
        Console.WriteLine($"Recipe upgrade: {checks} checks passed.");
    }
}
