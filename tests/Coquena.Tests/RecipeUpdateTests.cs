using System.Net;
using System.Text.Json.Nodes;
using Coquena.Contracts;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;
using Coquena.Backends;

namespace Coquena.Tests;

internal static class RecipeUpdateTests
{
    public static async Task Run(string root, string? product = null)
    {
        Directory.CreateDirectory(root);
        var checks = 0;
        void Check(bool value, string detail) { if (!value) throw new Exception(detail); checks++; }
        var remote = new Metadata(); using var client = new HttpClient(remote);
        var probe = new ResourceUpdateProbe(new(client), (_, _) => throw new Exception("Unexpected backend lookup"), new Quiet());
        var selection = new RemoteSelection("fixture/repository", "main", new string('a', 40), [new("old-Q4_0.gguf", 100, new("sha256", "file-bytes", new string('b', 64)))]);
        var resource = new JsonObject { ["resource-id"] = Guid.NewGuid().ToString("D"), ["ownership"] = "managed", ["logical-key"] = "fixture/repository:Q4_0" };
        var installation = new JsonObject { ["installation-id"] = Guid.NewGuid().ToString("D"), ["selection"] = new JsonObject { ["modelSelection"] = JsonNode.Parse(ContractJson.Write(selection, ContractJsonContext.Default.RemoteSelection)) } };
        var yaml = Path.Combine(root, "coquena.yaml");
        const string text = "models:\n  a: fixture/repository:Q4_0\npresets:\n  p:\n    args:\n      model: model://a\n";
        File.WriteAllText(yaml, text);
        var recipe = RecipeReader.Read(new(root));
        Check(ResourceSelector.Parse("model://unsloth/LFM2.5-8B-A1B-GGUF:UD-IQ1_M").Name == "unsloth/LFM2.5-8B-A1B-GGUF:UD-IQ1_M", "remote URI preserves repository and quantization");
        Check(ResourceSelector.Parse("backend://cuda@12").Matches("backend", "backend", null,
            new JsonObject { ["selection"] = new JsonObject { ["backendSelection"] = new JsonObject { ["variant"] = "cuda@12" } } }), "backend URI matches concrete variant");
        Check(ResourceSelector.Parse("companion://fixture/repository#mtp:BF16").Kind == "companion", "companion URI preserves type");
        Check(ResourceSelector.Parse("model://local model").Name == "local model" && ResourceSelector.Parse("model://local#1").Name == "local#1", "local keys retain the existing naming contract");
        foreach (var invalid in new[] { "backend", "model:a", "backend://a/b/c", "backend://cuda@12:b1", "model://fixture/repository", "model://fixture/repository#mtp:BF16", "companion://fixture/repository:Q4_0" })
        {
            try { ResourceSelector.Parse(invalid); throw new Exception("invalid URI accepted: " + invalid); }
            catch (Exception e) when (e is ArgumentException or ModelSourceException) { Check(true, "invalid URI rejected"); }
        }
        var request = new PreparationRequest("a", "model", recipe.Root.Get("models")!.Get("a")!);
        async Task<JsonObject> Observe() => await probe.CheckAsync(request, resource, installation);
        Check((await Observe())["status"]!.GetValue<string>() == "update-available", "changed same path is available");
        remote.Hash = 'b'; Check((await Observe())["status"]!.GetValue<string>() == "unchanged", "equal content is unchanged despite commit change");
        remote.Paths = ["renamed-Q4_0.gguf"];
        Check((await Observe())["actions"]![0]!["action"]!.GetValue<string>() == "reuse", "exact rename reuses content");
        remote.Paths = ["arbitrary-name.gguf"];
        Check((await Observe())["status"]!.GetValue<string>() == "update-available", "content proof confirms rename independently of filename hints");
        remote.Paths = ["one-Q4_0.gguf", "two-Q4_0.gguf"];
        Check((await Observe())["status"]!.GetValue<string>() == "intervention-required", "ambiguous rename does not choose");
        var interactive = new ResourceUpdateProbe(new(client), (_, _) => throw new Exception("Unexpected backend lookup"), new Choice(0));
        var selectedRename = await interactive.CheckAsync(request, resource, installation);
        Check(selectedRename["explicit-choice"]!.GetValue<bool>() && selectedRename["actions"]![0]!["action"]!.GetValue<string>() == "reuse", "interactive choice resolves multiple exact renames without download");
        remote.Hash = 'c'; remote.Paths = ["new-Q4_0.gguf"];
        var selectedSuccessor = await interactive.CheckAsync(request, resource, installation);
        Check(selectedSuccessor["explicit-choice"]!.GetValue<bool>() && selectedSuccessor["actions"]![0]!["action"]!.GetValue<string>() == "download", "explicit different-content candidate is planned without asserting automatic succession");
        var cancelledProbe = new ResourceUpdateProbe(new(client), (_, _) => throw new Exception("Unexpected backend lookup"), new Choice(null));
        Check((await cancelledProbe.CheckAsync(request, resource, installation))["proposed"] is null, "cancelled choice does not authorize a candidate");
        remote.Paths = []; Check((await Observe())["status"]!.GetValue<string>() == "orphaned", "complete empty inventory confirms absence");
        remote.Fail = true; Check((await Observe())["status"]!.GetValue<string>() == "query-failed", "remote failure distinct from absence");
        remote.Fail = false;
        remote.Hash = 'c'; remote.Paths = ["new-Q8_0.gguf"];
        var wrongQuantization = await Observe();
        Check(wrongQuantization["status"]!.GetValue<string>() == "intervention-required" && wrongQuantization["proposed"] is null && wrongQuantization["candidates"] is null,
            "disappeared route never proposes a different quantization");
        remote.Paths = ["shard-Q4_0-00001-of-00002.gguf"];
        Check((await Observe())["proposed"] is null, "incomplete shard set is never proposed");
        remote.Paths = ["old-Q4_0.gguf"];
        var context = ScopeContext.Project(yaml); var store = new ScopeStore(context);
        await new CombinationPreparation(context).PrepareAsync(new(root), new External(context));
        var plans = new RecipeUpdatePlans(context);
        var before = store.Read().Documents.Where(p => p.Value["kind"]!.GetValue<string>() == "resolution").Single().Value["data"]!.ToJsonString();
        var plan = await plans.UpdateAsync([new(root)], new HashSet<string>(), probe);
        Check(plan["resources"]![0]!["status"]!.GetValue<string>() == "not-applicable", "external is never followed");
        Check(plans.ReadValid([new(root)])["created-at"] is not null, "persisted plan can be read without network");
        Check((await plans.UpdateAsync([new(root)], new HashSet<string> { "model://fixture/repository:Q4_0" }, probe))["resources"]!.AsArray().Count == 1, "canonical selector resolves local alias through inventory");
        var revision = store.Read().Revision;
        try { await plans.UpdateAsync([new(root)], new HashSet<string> { "model://missing" }, probe); throw new Exception("expected unknown selector"); }
        catch (ArgumentException) { Check(store.Read().Revision == revision, "unknown selector leaves plan unchanged"); }
        Check(store.Read().Documents.Where(p => p.Value["kind"]!.GetValue<string>() == "resolution").Single().Value["data"]!.ToJsonString() == before, "update does not mutate resolutions");
        File.AppendAllText(yaml, "# changed\n");
        try { plans.ReadValid([new(root)]); throw new Exception("expected stale plan"); }
        catch (StorageException e) { Check(e.Code == "stale-update-plan", "YAML edit invalidates plan including comments"); }
        File.WriteAllText(yaml, text);
        var backendRecipe = Path.Combine(root, "backend.yaml");
        File.WriteAllText(backendRecipe, "service:\n  backend:\n    version: b1\n    variant: cpu\n");
        var backendIntent = RecipeReader.Read(new(root, [backendRecipe])).Root.Get("service")!.Get("backend")!;
        var backendProbe = new ResourceUpdateProbe(new(client), (_, _) => Task.FromResult(new BackendResolution(
            new("b2", "cpu", "win-x64", "compatible", "fixture", [new("llama-b2-bin-win-cpu-x64.zip", 100, "https://example.invalid/fixture")], true), [])), new Quiet());
        var backendInstallation = new JsonObject { ["installation-id"] = Guid.NewGuid().ToString("D"), ["selection"] = new JsonObject { ["backendSelection"] = new JsonObject {
            ["origin"] = "ggml", ["release"] = "b1", ["family"] = "cpu", ["variant"] = "cpu", ["platform"] = "win-x64", ["artifacts"] = new JsonArray() } } };
        var backendResult = await backendProbe.CheckAsync(new("backend", "backend", backendIntent), resource, backendInstallation);
        Check(backendResult["status"]!.GetValue<string>() == "update-available" && !backendResult["upgrade-allowed"]!.GetValue<bool>(), "fixed pin reports newer build without allowing upgrade");
        var oldPlan = store.Read().Documents["local/update-plan.json"]["data"]!.ToJsonString();
        var managed = store.Read(); var changed = managed.Documents.ToDictionary(p => p.Key, p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
        var savedResource = changed["local/inventory.json"]["data"]!["resources"]![0]!;
        savedResource["ownership"] = "managed";
        savedResource["installations"]![0]!["selection"] = installation["selection"]!.DeepClone();
        store.Commit(managed.Revision, changed);
        remote.During = () => File.AppendAllText(yaml, "# concurrent\n");
        try { await plans.UpdateAsync([new(root)], new HashSet<string>(), probe); throw new Exception("expected concurrent edit"); }
        catch (StorageException e) { Check(e.Code == "stale-update-plan" && store.Read().Documents["local/update-plan.json"]["data"]!.ToJsonString() == oldPlan, "concurrent edit rejects publication and preserves previous plan"); }
        remote.During = null;
        File.WriteAllText(yaml, text);
        remote.Paths = ["old-Q4_0.gguf"]; remote.Hash = 'c';
        var available = await plans.UpdateAsync([new(root)], new HashSet<string> { "model://a" }, probe);
        Check(available["resources"]![0]!["proposed"] is not null, "plan persists exact remote destination");
        remote.Fail = true;
        var failed = await plans.UpdateAsync([new(root)], new HashSet<string> { "model://a" }, probe);
        Check(failed["resources"]![0]!["status"]!.GetValue<string>() == "query-failed" && plans.ReadValid([new(root)])["resources"]![0]!["proposed"] is null, "failed new query never authorizes an old proposal");
        remote.Fail = false;
        var intentKey = File.ReadAllBytes(context.PathFor("local/intent.key"));
        File.Delete(context.PathFor("local/intent.key"));
        try { plans.ReadValid([new(root)]); throw new Exception("expected invalid key"); }
        catch (StorageException e) { Check(e.Code == "update-preparation-required", "lost key invalidates plan"); }
        File.WriteAllBytes(context.PathFor("local/intent.key"), intentKey);
        var final = store.Read();
        var finalDocs = final.Documents.ToDictionary(p => p.Key, p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
        finalDocs["local/inventory.json"]["data"]!["resources"]![0]!["ownership"] = "external";
        store.Commit(final.Revision, finalDocs);
        await Backends(root, context, Check);
        await Global(root, probe, Check, product);
        await Privacy(root, probe, Check);
        await BackendAliasTests.Run(root, product, Check);
        Console.WriteLine($"Recipe update: {checks} checks passed.");
    }

    private static async Task Privacy(string root, ResourceUpdateProbe probe, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "privacy"); Directory.CreateDirectory(folder);
        var yaml = Path.Combine(folder, "coquena.yaml"); var env = Path.Combine(folder, "selection.env");
        File.WriteAllText(yaml, "models:\n  a: ${MODEL}\npresets:\n  p:\n    args:\n      model: model://a\n");
        File.WriteAllText(env, "MODEL=fixture/repository:Q4_0\n");
        var selection = new RecipeSelection(folder, envFiles: [env]); var context = ScopeContext.Project(yaml);
        await new CombinationPreparation(context).PrepareAsync(selection, new External(context));
        var plans = new RecipeUpdatePlans(context); var plan = await plans.UpdateAsync([selection], new HashSet<string>(), probe);
        var report = RecipeUpdatePlans.Report(plan).ToJsonString();
        check(plan["resources"]![0]!["sensitive"]!.GetValue<bool>() && report.Contains("redacted") && !report.Contains("hmac"), "environment selections are redacted from the CLI report");
        File.AppendAllText(env, "# edit\n");
        try { plans.ReadValid([selection]); throw new Exception("expected stale env plan"); }
        catch (StorageException e) { check(e.Code == "stale-update-plan", "environment file edit invalidates private plan"); }
    }

    private static async Task Backends(string root, ScopeContext context, Action<bool, string> check)
    {
        var cpu = Path.Combine(root, "cpu.yaml"); var cuda = Path.Combine(root, "cuda.yaml");
        File.WriteAllText(cpu, "name: cpu-project\nservice:\n  backend:\n    name: my-official\n    version: b1\n    variant: cpu\n");
        File.WriteAllText(cuda, "name: cuda-project\nservice:\n  backend:\n    name: my-official\n    version: latest\n    variant: cuda@12\n");
        var cpuSelection = new RecipeSelection(root, [cpu]); var cudaSelection = new RecipeSelection(root, [cuda]);
        var preparation = new CombinationPreparation(context);
        await preparation.PrepareAsync(cpuSelection, new BackendFixture(context));
        await preparation.PrepareAsync(cudaSelection, new BackendFixture(context));
        using var http = new HttpClient(new Metadata());
        var calls = new List<string>();
        var probe = new ResourceUpdateProbe(new(http), (variant, _) =>
        {
            calls.Add(variant);
            return Task.FromResult(new BackendResolution(new("b2", variant, "win-x64", "compatible", "fixture", [new("artifact.zip", 100, "https://example.invalid/fixture")], true), []));
        }, new Quiet());
        var plans = new RecipeUpdatePlans(context);
        var byAlias = await plans.UpdateAsync([cpuSelection], new HashSet<string> { "backend://my-official" }, probe);
        check(byAlias["resources"]![0]!["status"]!.GetValue<string>() == "update-available" && byAlias["resources"]![0]!["current"]!["variant"]!.GetValue<string>() == "cpu", "official backend alias resolves before querying its concrete variant");
        try { await plans.UpdateAsync([cpuSelection, cudaSelection], new HashSet<string> { "backend://my-official" }, probe); throw new Exception("expected ambiguous alias"); }
        catch (StorageException e) { check(e.Code == "ambiguous-resource-selector", "colliding backend aliases require project qualification"); }
        calls.Clear();
        var qualified = await plans.UpdateAsync([cpuSelection, cudaSelection], new HashSet<string> { "backend://cuda-project/my-official" }, probe);
        check(qualified["resources"]!.AsArray().Count == 1 && calls.SequenceEqual(new[] { "cuda@12" }), "qualified alias queries only its own official variant");
        calls.Clear();
        var both = await plans.UpdateAsync([cpuSelection, cudaSelection], new HashSet<string> { "backend://cpu", "backend://cuda@12" }, probe);
        check(both["resources"]!.AsArray().Count == 2 && calls.SequenceEqual(new[] { "cpu", "cuda@12" }), "union of backend URIs queries exactly their variants");
        calls.Clear();
        var partial = await plans.UpdateAsync([cpuSelection, cudaSelection], new HashSet<string> { "backend://cuda@12" }, probe);
        check(calls.SequenceEqual(new[] { "cuda@12" }) && partial["resources"]!.AsArray().Count == 1 && plans.ReadValid([cpuSelection, cudaSelection])["resources"]!.AsArray().Count == 1,
            "partial plan replaces prior proposals and queries no excluded backend");
        check(partial["resources"]![0]!["upgrade-allowed"]!.GetValue<bool>(), "latest intent remains upgradeable");
        var one = await plans.UpdateAsync([cpuSelection], new HashSet<string>(), probe);
        check(!one["resources"]![0]!["upgrade-allowed"]!.GetValue<bool>(), "pin intent remains non-upgradeable in persisted plan");
        var revision = new ScopeStore(context).Read();
        var docs = revision.Documents.ToDictionary(p => p.Key, p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
        var record = docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["kind"]!.GetValue<string>() == "backend" && r["logical-key"]!.GetValue<string>() == "cpu")!;
        var alreadyPresent = record["installations"]![0]!.DeepClone();
        alreadyPresent["installation-id"] = Guid.NewGuid().ToString("D");
        alreadyPresent["selection"]!["backendSelection"]!["release"] = "b2";
        record["installations"]!.AsArray().Add(alreadyPresent);
        new ScopeStore(context).Commit(revision.Revision, docs);
        var withCandidate = await plans.UpdateAsync([cpuSelection], new HashSet<string>(), probe);
        check(withCandidate["resources"]![0]!["current"]!["release"]!.GetValue<string>() == "b1" && withCandidate["resources"]![0]!["proposed"]!["release"]!.GetValue<string>() == "b2",
            "an already inventoried candidate does not silently advance the combination baseline");
        var stale = new ScopeStore(context).Read();
        var staleDocs = stale.Documents.ToDictionary(p => p.Key, p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
        var combination = staleDocs["local/combinations.json"]["data"]!["combinations"]![CombinationIdentity.FromSelection(cpuSelection).Id]!;
        combination["preparation-revision"] = combination["preparation-revision"]!.GetValue<long>() + 1;
        new ScopeStore(context).Commit(stale.Revision, staleDocs);
        try { plans.ReadValid([cpuSelection]); throw new Exception("expected changed baseline"); }
        catch (StorageException e) { check(e.Code == "stale-update-plan", "preparation revision change invalidates exact proposal"); }
        var state = new ScopeStore(context).Read();
        check(!state.Documents.Values.Any(d => d["kind"]!.GetValue<string>() is "active" or "last-good"), "checking updates never starts or promotes runtime");
    }

    private sealed class BackendFixture(ScopeContext context) : ICombinationResources
    {
        public Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exact, CancellationToken cancellation)
        {
            var variant = request.Intent.Get("variant")!.Text!;
            var registry = new RecipeRegistry(new(context)); var resource = registry.RegisterResource(variant, "backend", "managed");
            var store = new ScopeStore(context); var state = store.Read();
            var docs = state.Documents.ToDictionary(p => p.Key, p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
            var selection = new JsonObject { ["origin"] = "ggml", ["release"] = "b1", ["family"] = variant,
                ["variant"] = variant, ["platform"] = "win-x64", ["artifacts"] = new JsonArray((JsonNode)new JsonObject { ["path"] = "artifact.zip", ["size"] = 100 }) };
            var id = Guid.NewGuid().ToString("D");
            docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["resource-id"]!.GetValue<string>() == resource.ResourceId)!["installations"]!.AsArray()
                .Add((JsonNode)new JsonObject { ["installation-id"] = id, ["selection"] = new JsonObject { ["backendSelection"] = selection.DeepClone() } });
            store.Commit(state.Revision, docs);
            return Task.FromResult(new PreparationResource("backend", "backend", resource.ResourceId, id, "fixture.exe", new JsonObject { ["origin"] = "official", ["selection"] = selection.DeepClone() }));
        }
    }

    private static async Task Global(string root, ResourceUpdateProbe probe, Action<bool, string> check, string? product)
    {
        var home = new Home(Path.Combine(root, "home")); var context = ScopeContext.Global(home);
        Directory.CreateDirectory(context.Root);
        var path = context.PathFor("recipe.yaml");
        var text = "models:\n  a: fixture/repository:Q4_0\npresets:\n  p:\n    args:\n      model: model://a\n";
        File.WriteAllText(path, text);
        var selection = new RecipeSelection(context.Base, [path], context.Base);
        await new CombinationPreparation(context).PrepareAsync(selection, new External(context));
        var store = new ScopeStore(context); var state = store.Read();
        var docs = state.Documents.ToDictionary(p => p.Key, p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
        docs["recipe.yaml"] = ProjectDocuments.Create("recipe-intention", 1, new JsonObject { ["text"] = text, ["path-bases"] = new JsonObject() });
        store.Commit(state.Revision, docs);
        var previousHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("COQUENA_HOME", home.Root);
            using var output = new StringWriter(); using var error = new StringWriter();
            check(Cli.Run(["update", "--global", "--select", "model://fixture/repository:Q4_0"], output, error) == 0 && output.ToString().Contains("not-applicable"), "global CLI resolves canonical model without querying external origin");
            check(!output.ToString().Contains("hmac"), "CLI report excludes private plan fingerprints");
            var revision = store.Read().Revision;
            check(Cli.Run(["update", "--global", "--select", "backend://cuda@12"], output, error) == 2 && store.Read().Revision == revision, "unmatched global variant never broadens query");
            check(Cli.Run(["update", "--global", "--private"], output, error) == 2, "global rejects private project context");
            if (product is not null)
            {
                foreach (var args in new[] { new[] { "update", "--global", "--select", "model://fixture/repository:Q4_0" },
                    new[] { "update", "--project-directory", root, "--select", "model://a" } })
                {
                    var start = new System.Diagnostics.ProcessStartInfo(product) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    start.Environment["COQUENA_HOME"] = home.Root;
                    foreach (var arg in args) start.ArgumentList.Add(arg);
                    using var process = System.Diagnostics.Process.Start(start)!;
                    var stdout = await process.StandardOutput.ReadToEndAsync(); var stderr = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
                    check(process.ExitCode == 0 && stdout.Contains("not-applicable") && !stdout.Contains("hmac") && stderr.Length == 0,
                        "published Native AOT persists scoped URI-selected plan without querying external origin");
                }
            }
        }
        finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previousHome); }
    }

    private sealed class Quiet : IModelSelectionInteraction
    {
        public bool IsInteractive => false;
        public void Show(string text) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation) => throw new Exception("Unexpected prompt");
    }
    private sealed class Choice(int? choice) : IModelSelectionInteraction
    {
        public bool IsInteractive => true;
        public void Show(string text) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation) => Task.FromResult(choice);
    }
    private sealed class Metadata : HttpMessageHandler
    {
        public string[] Paths = ["old-Q4_0.gguf"]; public char Hash = 'c'; public bool Fail; public Action? During;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (Fail) throw new HttpRequestException("fixture failure");
            During?.Invoke();
            if (!request.RequestUri!.AbsolutePath.StartsWith("/api/models/", StringComparison.Ordinal)) throw new Exception("Unexpected weights request");
            var body = request.RequestUri.AbsolutePath.Contains("/revision/", StringComparison.Ordinal)
                ? "{\"id\":\"fixture/repository\",\"sha\":\"" + new string('d', 40) + "\"}"
                : "[" + string.Join(',', Paths.Select(p => "{\"type\":\"file\",\"path\":\"" + p + "\",\"size\":100,\"oid\":\"" + new string('e', 40) + "\",\"lfs\":{\"oid\":\"" + new string(Hash, 64) + "\",\"size\":100}}")) + "]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
    private sealed class External(ScopeContext context) : ICombinationResources
    {
        public Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exact, CancellationToken cancellation)
        {
            var registry = new RecipeRegistry(new(context)); var resource = registry.RegisterResource("fixture/repository:Q4_0", "model", "external");
            var store = new ScopeStore(context); var snapshot = store.Read();
            var docs = snapshot.Documents.ToDictionary(p => p.Key, p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
            var id = Guid.NewGuid().ToString("D");
            var record = docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["resource-id"]!.GetValue<string>() == resource.ResourceId)!;
            record["installations"]!.AsArray().Add((JsonNode)new JsonObject { ["installation-id"] = id, ["files"] = new JsonArray((JsonNode)new JsonObject { ["path"] = "fixture.gguf" }) });
            store.Commit(snapshot.Revision, docs);
            return Task.FromResult(new PreparationResource(request.Key, request.Kind, resource.ResourceId, id, "fixture.gguf", new JsonObject { ["origin"] = "external" }));
        }
    }
}
