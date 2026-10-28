using System.Security.Cryptography;
using System.Text;
using Coquena.Projects;
using Coquena.Recipes;
using System.Text.Json.Nodes;
using Coquena.Storage;
using Coquena.Models;
using Coquena.Backends;
using Coquena.Downloads;
using Coquena.Contracts;

namespace Coquena.Tests;

internal static class CombinationTests
{
    public static async Task Run()
    {
        var checks = 0;
        void Check(bool condition, string detail)
        { if (!condition) throw new Exception(detail); checks++; }
        var input = "{\"files\":[\"coquena.yaml\",\"coquena.cpu.yaml\"],\"env-files\":[]}";
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        var first = CombinationIdentity.FromSelection(new("D:/fixture/project", ["coquena.yaml", "./sub/../coquena.cpu.yaml"]));
        Check(first.Id == expected, "canonical document example hashes exact compact bytes");
        Check(CombinationIdentity.FromSelection(new("D:/elsewhere", ["coquena.yaml", "coquena.cpu.yaml"])).Id == first.Id,
            "moving base preserves portable identity");
        Check(CombinationIdentity.FromSelection(new("D:/fixture/project", ["coquena.cpu.yaml", "coquena.yaml"])).Id != first.Id,
            "ordered selection changes identity");
        Check(CombinationIdentity.FromSelection(new("D:/fixture/project", ["Coquena.yaml", "coquena.cpu.yaml"])).Id != first.Id,
            "selection retains case");
        Check(CombinationIdentity.FromSelection(new("D:/fixture/project", envFiles: ["settings.env"])).Private,
            "explicit env selection is private");
        Check(CombinationIdentity.FromSelection(new("D:/fixture/project"), true).Private, "explicit private selection");
        var unicode = "{\"files\":[\"á/😀<&>.yaml\"],\"env-files\":[]}";
        Check(CombinationIdentity.Hash(["á/😀<&>.yaml"], []) == Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(unicode))),
            "unicode and HTML characters remain unescaped");
        var cross = CombinationIdentity.FromSelection(new("D:/fixture/project", ["coquena.yaml", "E:/fixture/extra.yaml"]));
        Check(cross.Private && cross.Files[1] == "E:/fixture/extra.yaml", "cross-volume identity is local with normalized absolute selection");
        var root = Path.Combine("D:/tmp", "coquena-combinations-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var yaml = Path.Combine(root, "coquena.yaml");
        const string content = "models:\n  a: fixture/one:Q4_0\n  b: fixture/two:Q4_0\n  unused: fixture/unused:Q4_0\npresets:\n  p:\n    args:\n      model: model://a\n      draft: model://b\n      token: ${TOKEN}\n  off:\n    enable: false\n    args:\n      model: model://unused\n";
        File.WriteAllText(yaml, content);
        var selection = new RecipeSelection(root);
        var context = ScopeContext.Project(yaml);
        var preparation = new CombinationPreparation(context);
        var fixture = new Resources(context);
        var env = new Dictionary<string, string?> { ["TOKEN"] = "private-value" };
        var prepared = await preparation.PrepareAsync(selection, fixture, environment: env);
        Check(fixture.Fresh == 2 && fixture.Exact == 0 && prepared.Resources.Count == 2, "only enabled references prepare");
        var store = new ScopeStore(context);
        var snapshot = store.Read();
        var resolutionPath = $"combinations/{prepared.Id}/resolved.json";
        var resolution = snapshot.Documents[resolutionPath];
        Check(!resolution.ToJsonString().Contains("private-value") && !resolution.ToJsonString().Contains("hmac"), "portable lock has neither expanded secrets nor secret hashes");
        Check(!snapshot.Documents.ContainsKey("local/active.json") && !snapshot.Documents.ContainsKey("local/last-good.json"), "preparation never promotes active or last good");
        Check(preparation.List().Count == 1 && File.ReadAllText(context.PathFor(".gitignore")).Contains("/local/"), "explicit selection indexed with local exclusions");
        Check(File.ReadAllText(context.PathFor(".gitignore")).Contains("/services/*/local/") &&
            File.ReadAllText(context.PathFor(".gitignore")).Contains("/services/*/logs/"), "service runtime and logs remain excluded from Git");
        Check(!preparation.List().ToJsonString().Contains("private-value") && !preparation.List().ToJsonString().Contains("hmac"), "listing known combinations does not expose effective snapshots or HMACs");
        var again = await preparation.PrepareAsync(selection, fixture, environment: env);
        Check(again.Id == prepared.Id && fixture.Exact == 2 && fixture.Fresh == 2, "unchanged selections reuse exact locks");
        File.WriteAllText(yaml, content.Replace("fixture/one", "fixture/new"));
        await preparation.PrepareAsync(selection, fixture, environment: env);
        Check(fixture.Exact == 3 && fixture.Fresh == 3, "editing one resource leaves unrelated revision fixed");
        env["TOKEN"] = "changed-private-value";
        await preparation.PrepareAsync(selection, fixture, environment: env);
        Check(fixture.Exact == 5 && fixture.Fresh == 3, "argument secret change does not refresh model selections");
        var failedBefore = Resolution(store.Read(), resolutionPath);
        fixture.Fail = true;
        try { await preparation.PrepareAsync(selection, fixture, environment: env); throw new Exception("expected failure"); }
        catch (InvalidOperationException) { Check(Resolution(store.Read(), resolutionPath) == failedBefore, "failed preparation keeps previous entire resolution"); }
        fixture.Fail = false;
        fixture.During = () => File.AppendAllText(yaml, "# concurrent edit\n");
        try { await preparation.PrepareAsync(selection, fixture, environment: env); throw new Exception("expected source conflict"); }
        catch (StorageException e) { Check(e.Code == "intention-conflict" && Resolution(store.Read(), resolutionPath) == failedBefore, "source bytes checked at commit"); }
        fixture.During = null;
        File.Delete(context.PathFor("local/intent.key"));
        var freshBefore = fixture.Fresh;
        await preparation.PrepareAsync(selection, fixture, environment: env);
        Check(fixture.Fresh == freshBefore + 2, "missing HMAC key invalidates local equality");
        // Import only the portable files into a new machine/project state.
        var clone = Path.Combine(root, "clone"); Directory.CreateDirectory(clone);
        File.WriteAllText(Path.Combine(clone, "coquena.yaml"), File.ReadAllText(yaml));
        var cloneContext = ScopeContext.Project(Path.Combine(clone, "coquena.yaml"));
        Directory.CreateDirectory(Path.GetDirectoryName(cloneContext.PathFor(resolutionPath))!);
        File.Copy(context.PathFor(resolutionPath), cloneContext.PathFor(resolutionPath));
        var cloneFixture = new Resources(cloneContext);
        await new CombinationPreparation(cloneContext).PrepareAsync(new(clone), cloneFixture, environment: env);
        Check(cloneFixture.Exact == 2 && cloneFixture.Fresh == 0, "portable lock pins resources without local state or previous home");
        // A concurrent complete preparation wins; no last writer wins overwrite.
        var intervened = false;
        fixture.During = () =>
        {
            if (intervened) return; intervened = true;
            new CombinationPreparation(context).PrepareAsync(selection, new Resources(context), environment: env).GetAwaiter().GetResult();
        };
        try { await preparation.PrepareAsync(selection, fixture, environment: env); throw new Exception("expected concurrent preparation conflict"); }
        catch (StorageException e) { Check(e.Code == "scope-conflict", "concurrent preparation cannot overwrite newer set"); }
        fixture.During = null;
        // Fault injection at the durable transaction boundaries exercises the same
        // recovery path used after process termination, with no network replay.
        foreach (var stage in new[] { ScopeWriteStage.Prepared, ScopeWriteStage.Decided, ScopeWriteStage.ParticipantPublished, ScopeWriteStage.ProjectionsPublished })
        {
            var oldVersion = Version(store.Read(), prepared.Id);
            try
            {
                await preparation.PrepareAsync(selection, fixture, environment: env, fault: point => { if (point == stage) throw new Interrupted(); });
                throw new Exception("fault not reached");
            }
            catch (Interrupted) { }
            catch (StorageException e) when (e.InnerException is Interrupted) { }
            var recovered = store.Read();
            Check(Version(recovered, prepared.Id) == oldVersion + (stage == ScopeWriteStage.Prepared ? 0 : 1), "transaction decision recovers " + stage);
            Check(Resolution(recovered, resolutionPath) == Resolution(store.Read(), resolutionPath), "recovery idempotent " + stage);
        }
        // Private env combinations never write their paths into the shared index.
        File.WriteAllText(Path.Combine(root, "private.env"), "TOKEN=env-secret\n");
        var envPrepared = await preparation.PrepareAsync(new(root, envFiles: ["private.env"]), fixture);
        Check(envPrepared.Private && store.Read().Documents.ContainsKey($"combinations/{envPrepared.Id}/local/resolved.json"), "private resolution stays in ignored local subtree");
        Check(!store.Read().Documents["combinations.json"].ToJsonString().Contains("private.env"), "env path excluded from portable index");
        // Privacy follows the explicit invocation; env selections remain private.
        var localPrivate = await preparation.PrepareAsync(selection, fixture, true, env);
        Check(localPrivate.Private && store.Read().Documents["combinations.json"]["data"]!["combinations"]![localPrivate.Id] is null, "private conversion retires portable index entry");
        await preparation.PrepareAsync(selection, fixture, environment: env);
        Check(store.Read().Documents["combinations.json"]["data"]!["combinations"]![localPrivate.Id] is not null, "explicit public selection restores portable index");
        await LocalAndTarget(root, Check);
        await Remote(root, Check);
        await ServicePlan(root, Check);
        foreach (var stage in new[] { ScopeWriteStage.Prepared, ScopeWriteStage.Decided, ScopeWriteStage.ParticipantPublished })
        {
            var folder = Path.Combine(root, "crash-" + stage); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "coquena.yaml"), "{}\n");
            var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.dll"));
            foreach (var arg in new[] { "--combination-crash-worker", folder, stage.ToString() }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(true); }
            Check(process.ExitCode == 73, "process terminated at " + stage);
            var recovered = new ScopeStore(ScopeContext.Project(Path.Combine(folder, "coquena.yaml"))).Read();
            var id = CombinationIdentity.FromSelection(new(folder)).Id;
            Check(recovered.Documents.ContainsKey($"combinations/{id}/resolved.json") == (stage != ScopeWriteStage.Prepared), "process death respects commit at " + stage);
        }
        Console.WriteLine($"Combinations: {checks} checks passed.");
    }

    public static async Task CrashWorker(string folder, ScopeWriteStage stage)
    {
        var context = ScopeContext.Project(Path.Combine(folder, "coquena.yaml"));
        await new CombinationPreparation(context).PrepareAsync(new(folder), new Resources(context), fault: point =>
        { if (point == stage) Environment.Exit(73); });
    }

    private static async Task ServicePlan(string parent, Action<bool, string> check)
    {
        var folder = Path.Combine(parent, "service-plan"); Directory.CreateDirectory(folder);
        var yaml = Path.Combine(folder, "coquena.yaml");
        File.WriteAllText(yaml, "service:\n  backend:\n    executable: ./server.exe\n  server:\n    health:\n      url: http://127.0.0.1:23456/health\n    args:\n      port: 23456\nmodels:\n  unused:\n    files: [./missing.gguf]\npresets:\n  demo:\n    args:\n      model: model://unused\n      ctx-size: 4096\n");
        var selection = new RecipeSelection(folder); var context = ScopeContext.Project(yaml);
        var preparation = new CombinationPreparation(context);
        var prepared = await preparation.PrepareAsync(selection, new Resources(context));
        var resolutionPath = $"combinations/{prepared.Id}/resolved.json";
        var completeBefore = new ScopeStore(context).Read().Documents[resolutionPath]["data"]!.ToJsonString();
        var clone = Path.Combine(parent, "service-lock-clone");
        Directory.CreateDirectory(clone);
        File.Copy(yaml, Path.Combine(clone, "coquena.yaml"));
        var cloneContext = ScopeContext.Project(Path.Combine(clone, "coquena.yaml"));
        var cloneLock = cloneContext.PathFor(resolutionPath);
        Directory.CreateDirectory(Path.GetDirectoryName(cloneLock)!);
        File.Copy(context.PathFor(resolutionPath), cloneLock);
        var cloneResources = new Resources(cloneContext);
        await new CombinationPreparation(cloneContext).PrepareServiceAsync(new RecipeSelection(clone), cloneResources);
        check(cloneResources.Exact == 1 && cloneResources.Fresh == 0, "process preparation honors received portable lock without local state");
        var processResources = new Resources(context);
        var processPrepared = await preparation.PrepareServiceAsync(selection, processResources);
        check(processResources.Fresh == 0 && processResources.Exact == 1 && processPrepared.Resources.Count == 1 && processPrepared.Resources[0].Kind == "backend",
            "service preparation does not acquire preset models");
        check(new ScopeStore(context).Read().Documents["local/combinations.json"]["data"]!["combinations"]![processPrepared.Id]!["service-preparation"]!["bindings"]!.ToJsonString().Contains(processPrepared.Resources[0].InstallationId),
            "service preparation protects the exact installation before activation");
        check(new ScopeStore(context).Read().Documents[resolutionPath]["data"]!.ToJsonString() == completeBefore,
            "service preparation preserves complete resolution");
        var processAgain = await preparation.PrepareServiceAsync(selection, processResources);
        check(processResources.Exact == 2 && processAgain.Id == processPrepared.Id, "service preparation reuses exact backend selection");
        processResources.During = () => File.AppendAllText(yaml, "# concurrent edit\n");
        try { await preparation.PrepareServiceAsync(selection, processResources); throw new Exception("expected source conflict"); }
        catch (StorageException e) { check(e.Code == "intention-conflict", "service preparation rejects concurrent source changes"); }
        processResources.During = null;
        var recipe = RecipeReader.Read(selection);
        var plan = Coquena.Servers.ScopedServicePlan.Compose(context, recipe, processAgain, Path.Combine(folder, "test.log"));
        check(plan.Arguments.Contains("--port") && !plan.Arguments.Contains("--ctx-size") && !plan.Arguments.Contains("--metrics"),
            "external service emits only process arguments and adds no official defaults");
        check(plan.HealthUrl.Port == 23456 && plan.Resources.Count == 1, "plan binds prepared backend and configured health");
        File.AppendAllText(yaml, "    enable: false\n");
        try { Coquena.Servers.ScopedServicePlan.Compose(context, RecipeReader.Read(selection), processAgain, Path.Combine(folder, "test.log")); throw new Exception("expected intention conflict"); }
        catch (StorageException e) { check(e.Code == "intention-conflict", "changed effective recipe cannot reuse prepared snapshot"); }
    }

    private static async Task Remote(string parent, Action<bool, string> check)
    {
        var folder = Path.Combine(parent, "remote"); Directory.CreateDirectory(folder);
        var yaml = Path.Combine(folder, "coquena.yaml");
        File.WriteAllText(yaml, "models:\n  a: fixture/repository:Q4_0\npresets:\n  p:\n    args:\n      model: model://a\n");
        var context = ScopeContext.Project(yaml);
        var bytes = ScopedModelTests.Gguf();
        var metadata = new Metadata(bytes.Length);
        using var client = new HttpClient(metadata);
        var transport = new ScopedModelTests.Transport(bytes);
        var provider = new ScopedCombinationResources(context, new("win-x64", "fixture", null, [], null, [], new("unknown", ""), new("unknown", "")),
            new HuggingFaceStore(client), transport, new NoInteraction(), new ResolutionTests.NamedInspector());
        var preparation = new CombinationPreparation(context);
        var first = await preparation.PrepareAsync(new(folder), provider);
        check(metadata.Calls > 0 && transport.Calls == 1, "real remote pipeline selects and transfers one GGUF");
        metadata.Forbid = true;
        var next = await preparation.PrepareAsync(new(folder), provider);
        check(next.Resources[0].InstallationId == first.Resources[0].InstallationId && transport.Calls == 1, "unchanged remote lock reuses physical installation without metadata or transfer");
        var portable = new ScopeStore(context).Read().Documents[$"combinations/{first.Id}/resolved.json"].ToJsonString();
        check(portable.Contains(new string('a', 40)) && !portable.Contains(context.Base) && !portable.Contains(first.Resources[0].ResourceId), "portable resolution includes commit without physical paths or local UUIDs");
        File.AppendAllText(yaml, "service:\n  backend:\n    version: latest\n    variant: cpu\n");
        var hardware = new HardwareContext("win-x64", "fixture", null, [], null, [], new("unknown", ""), new("unknown", ""));
        var artifacts = new ScopedBackendTests.Fixture(); artifacts.Add("b1", "cpu");
        var backends = new ScopedBackends(context, artifacts.Resolve, (path, _) => Task.FromResult("version: " + File.ReadAllText(path) + " (abcdef)"));
        var combinedProvider = new ScopedCombinationResources(context, hardware, new HuggingFaceStore(client), artifacts, new NoInteraction(),
            new ResolutionTests.NamedInspector(), backends: backends);
        var combined = await preparation.PrepareAsync(new(folder), combinedProvider);
        check(combined.Resources.Count == 2 && artifacts.Resolutions == 1 && transport.Calls == 1, "adding backend prepares only new resource while retaining model selection");
        var combinedAgain = await preparation.PrepareAsync(new(folder), combinedProvider);
        check(artifacts.Resolutions == 1 && artifacts.Calls == 1 && combinedAgain.Resources[0].InstallationId == combined.Resources[0].InstallationId,
            "unchanged official latest backend reuses exact build without release lookup");
        check(backends.List()[0]!["installations"]![0]!["protected"]?.GetValue<bool>() != true, "resolved latest is not reclassified as explicit pin");
        var firstModel = combined.Resources.Single(r => r.Kind == "model");
        File.WriteAllText(yaml, File.ReadAllText(yaml).Replace("version: latest", "version: b1"));
        await preparation.PrepareAsync(new(folder), combinedProvider);
        check(backends.List()[0]!["installations"]![0]!["protected"]!.GetValue<bool>(), "explicit backend pin retains protection");
        File.WriteAllText(yaml, "models:\n  a: fixture/repository:Q4_0\npresets:\n  p:\n    args:\n      model: model://a\n");
        File.Delete(firstModel.EntryPath);
        var restored = await preparation.PrepareAsync(new(folder), provider);
        check(transport.Calls == 2 && restored.Resources[0].InstallationId != firstModel.InstallationId,
            "missing managed file restores exact locked unit to a new immutable installation without metadata lookup");
    }
    private sealed class Metadata(long size) : HttpMessageHandler
    {
        public int Calls; public bool Forbid;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (Forbid) throw new Exception("metadata lookup forbidden after resolution"); Calls++;
            var text = request.RequestUri!.AbsolutePath.EndsWith("/revision/main", StringComparison.Ordinal)
                ? "{\"id\":\"fixture/repository\",\"sha\":\"" + new string('a', 40) + "\"}"
                : "[{\"type\":\"file\",\"path\":\"model-Q4_0.gguf\",\"size\":" + size + ",\"oid\":\"" + new string('b', 40) + "\"}]";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(text) });
        }
    }

    private static long Version(ScopeSnapshot snapshot, string id) => snapshot.Documents["local/combinations.json"]["data"]!["combinations"]![id]!["preparation-revision"]!.GetValue<long>();
    private static string Resolution(ScopeSnapshot snapshot, string path) => snapshot.Documents[path]["data"]!.ToJsonString();
    private sealed class Interrupted : Exception;
    private sealed class Resources(ScopeContext context) : ICombinationResources
    {
        public int Fresh, Exact; public bool Fail; public Action? During;
        public Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exact, CancellationToken cancellation)
        {
            if (Fail) throw new InvalidOperationException("fixture resource failure");
            if (exact is null) Fresh++; else Exact++;
            var registry = new RecipeRegistry(new(context));
            var resource = registry.Resources().SingleOrDefault(r => r.LogicalKey == request.Key) ?? registry.RegisterResource(request.Key, request.Kind, "external");
            var store = new ScopeStore(context); var snapshot = store.Read(); var docs = snapshot.Documents.ToDictionary(p => p.Key,
                p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
            var record = docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["resource-id"]!.GetValue<string>() == resource.ResourceId)!;
            var installations = record["installations"]!.AsArray();
            if (installations.Count == 0) installations.Add((JsonNode)new JsonObject { ["installation-id"] = Guid.NewGuid().ToString("D"), ["external"] = true,
                ["files"] = new JsonArray((JsonNode)new JsonObject { ["path"] = "fixture.gguf" }) });
            var installationId = installations[0]!["installation-id"]!.GetValue<string>();
            store.Commit(snapshot.Revision, docs);
            During?.Invoke();
            return Task.FromResult(new PreparationResource(request.Key, request.Kind, resource.ResourceId, installationId, "fixture.gguf",
                exact ?? new JsonObject { ["commit"] = "fixture-" + Fresh }));
        }
    }

    private static async Task LocalAndTarget(string parent, Action<bool, string> check)
    {
        var folder = Path.Combine(parent, "local"); Directory.CreateDirectory(folder);
        var yaml = Path.Combine(folder, "coquena.yaml");
        File.WriteAllText(yaml, "models:\n  local:\n    files: [./weights.gguf]\npresets:\n  p:\n    args:\n      model: model://local\n");
        using (var writer = new BinaryWriter(File.Create(Path.Combine(folder, "weights.gguf"))))
        { writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(0ul); }
        var context = ScopeContext.Project(yaml);
        var globalRoot = Path.Combine(parent, "home"); Directory.CreateDirectory(globalRoot);
        var global = ScopeContext.Global(Home.Resolve(globalRoot, null));
        var targetStore = new ScopeStore(global);
        var registry = new RecipeRegistry(targetStore);
        var recipe = registry.Register(["recipe.yaml"], "fixture-service");
        var service = registry.RegisterService(recipe.RecipeId);
        using var http = new HttpClient(new NoNetwork());
        var provider = new ScopedCombinationResources(context, new("win-x64", "fixture", null, [], null, [], new("unknown", ""), new("unknown", "")),
            new HuggingFaceStore(http), new NoTransport(), new NoInteraction());
        var preparation = new CombinationPreparation(context);
        var prepared = await preparation.PrepareAsync(new(folder), provider, target: new(targetStore, service.ServiceId));
        check(prepared.Resources.Count == 1 && File.Exists(prepared.Resources[0].EntryPath), "real local GGUF reused without network");
        check(registry.Resources().Length == 0 && new RecipeRegistry(new(context)).Resources().Length == 1, "consumer resources stay in owning scope");
        check(targetStore.Read().Documents["local/recipes.json"].ToJsonString().Contains(prepared.Resources[0].ResourceId), "cross-scope preparation records protection at target");
        foreach (var stage in new[] { ScopeWriteStage.Prepared, ScopeWriteStage.Decided, ScopeWriteStage.ParticipantPublished })
        {
            var version = Version(new ScopeStore(context).Read(), prepared.Id);
            var targetRevision = targetStore.Read().Revision;
            try
            {
                await preparation.PrepareAsync(new(folder), provider, target: new(targetStore, service.ServiceId),
                    fault: point => { if (point == stage) throw new Interrupted(); });
                throw new Exception("fault not reached");
            }
            catch (Interrupted) { }
            catch (StorageException e) when (e.InnerException is Interrupted) { }
            var recoveredTarget = targetStore.Read();
            var recoveredOwn = new ScopeStore(context).Read();
            check(Version(recoveredOwn, prepared.Id) == version + (stage == ScopeWriteStage.Prepared ? 0 : 1) &&
                recoveredTarget.Revision == targetRevision + (stage == ScopeWriteStage.Prepared ? 0 : 1), "consumer and target recover one joint decision at " + stage);
        }
        var conflicting = new Resources(context);
        conflicting.During = () => registry.RegisterResource("concurrent", "model", "external");
        try
        {
            await preparation.PrepareAsync(new(folder), conflicting, target: new(targetStore, service.ServiceId));
            throw new Exception("expected target conflict");
        }
        catch (StorageException e) { check(e.Code == "scope-conflict", "target revision changes prevent joint publication"); }
        try { new ScopedModels(context).Remove(prepared.Resources[0].ResourceId, false); throw new Exception("expected resource-in-use"); }
        catch (ModelSourceException) { check(true, "known combination blocks resource removal"); }
        var old = new ScopeStore(context).Read();
        var moved = folder + "-moved"; Directory.Move(folder, moved);
        var movedContext = ScopeContext.Project(Path.Combine(moved, "coquena.yaml"));
        var movedStore = new ScopeStore(movedContext);
        check(movedStore.Read().ScopeId == old.ScopeId, "physical move with state retains scope without home lookup");
        check(new ScopedModels(movedContext).ResolveReference(new("model", "local")) == Path.GetFullPath(Path.Combine(moved, "weights.gguf")), "local relative paths survive move");
        var outsideFolder = Path.Combine(parent, "outside"); Directory.CreateDirectory(outsideFolder);
        var outsideYaml = Path.Combine(outsideFolder, "coquena.yaml");
        File.WriteAllText(outsideYaml, "models:\n  a:\n    files: ['" + Path.GetFullPath(Path.Combine(moved, "weights.gguf")).Replace('\\', '/') + "']\npresets:\n  p:\n    args:\n      model: model://a\n");
        var outsideContext = ScopeContext.Project(outsideYaml);
        var outsidePreparation = new CombinationPreparation(outsideContext);
        try { await outsidePreparation.PrepareAsync(new(outsideFolder), new Resources(outsideContext)); throw new Exception("expected private reference"); }
        catch (StorageException e) { check(e.Code == "private-reference" && !Directory.Exists(outsideContext.Root), "absolute physical reference requires explicit private selection before writes"); }
        var privatePrepared = await outsidePreparation.PrepareAsync(new(outsideFolder), new Resources(outsideContext), true);
        check(privatePrepared.Private, "explicit private selection accepts absolute reference");
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => throw new Exception("network forbidden");
    }
    private sealed class NoTransport : IDownloadTransport
    {
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation) => throw new Exception("download forbidden");
    }
    private sealed class NoInteraction : IModelSelectionInteraction
    {
        public bool IsInteractive => false;
        public void Show(string text) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation) => throw new Exception("interaction forbidden");
    }
}
