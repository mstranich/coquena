using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Coquena.Projects;
using Coquena.Backends;
using Coquena.Models;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ServiceRecoveryTests
{
    public static async Task Run(string? product)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var root = Path.Combine("D:/tmp", "coquena-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var context = ScopeContext.Project(Path.Combine(root, "coquena.yaml"));
        var store = new ScopeStore(context);
        var registry = new RecipeRegistry(store);
        var service = registry.EnsureProjectService(["coquena.yaml"], "fixture");
        using var output = new StringWriter();
        var lifecycle = new ScopedServiceProcess(store, output);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var apphost = Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe");
        ScopedStartPlan Plan(bool unhealthy = false, bool draft = false, bool allowDraft = false) => new(apphost,
            unhealthy ? ["--scoped-health-child", "--port", port.ToString(), "--health-unready"] : ["--scoped-health-child", "--port", port.ToString()],
            Path.Combine(root, Guid.NewGuid().ToString("N") + ".log"), "fixture",
            new JsonObject { ["service"] = new JsonObject { ["recovery"] = new JsonObject { ["allow-draft-last-good"] =
                new JsonObject { ["type"] = "Boolean", ["value"] = allowDraft ? "true" : "false" } } },
                ["presets"] = new JsonObject { ["unused"] = "not published" } }, new JsonArray(),
            new JsonObject { ["host"] = "127.0.0.1", ["port"] = port }, new Uri($"http://127.0.0.1:{port}/health"),
            TimeSpan.FromSeconds(unhealthy ? 1 : 10), TimeSpan.FromMilliseconds(100), IsDraft: draft);
        JsonObject? Good() => store.Read().Documents.Values.SingleOrDefault(doc => doc["kind"]!.GetValue<string>() == "last-good" &&
            doc["data"]!["service-id"]!.GetValue<string>() == service.ServiceId)?["data"]?.AsObject();
        JsonNode Recovery() => store.Read().Documents["local/recipes.json"]["data"]!["recipes"]![0]!["entities"]![0]!["recovery"]!;
        Task<ScopedStartPlan> Restore(JsonObject good, CancellationToken token) => ScopedServiceSnapshot.RestoreAsync(context, good,
            new RejectResources(), Path.Combine(root, Guid.NewGuid().ToString("N") + ".log"), token);
        Task<string> Run(Func<CancellationToken, Task<ScopedStartPlan>> prepare, bool restart = false, bool detached = true,
            Func<string, bool, ServiceRecoveryChoice>? decide = null,
            Func<JsonObject, CancellationToken, Task<ScopedStartPlan>>? restore = null) =>
            lifecycle.RunAsync(service.ServiceId, restart, detached, prepare, restore ?? Restore, decide);
        try
        {
            await lifecycle.StartAsync(service.ServiceId, Plan(draft: true));
            Check(Good() is null, "healthy draft without permission must not become last-good");
            lifecycle.Stop(service.ServiceId);
            await lifecycle.StartAsync(service.ServiceId, Plan(draft: true, allowDraft: true));
            Check(Good()?["draft"]?.GetValue<bool>() == true, "explicit draft permission permits promotion after health");
            lifecycle.Stop(service.ServiceId);
            var first = await Run(_ => Task.FromResult(Plan()));
            var goodSnapshot = Good()!["snapshot"]!.DeepClone();
            Check(Good()!["draft"]?.GetValue<bool>() == false, "saved candidate becomes last-good after local health");
            var attempts = 0;
            var retained = await Run(_ => { attempts++; throw new IOException("fixture preparation failure"); }, restart: true);
            Check(retained == first && attempts == 1 && lifecycle.Reconcile(service.ServiceId), "preparation failure keeps the exact active instance");
            lifecycle.Stop(service.ServiceId);

            File.WriteAllText(Path.Combine(root, "coquena.yaml"), "invalid: [");
            var recovered = await Run(_ => { attempts++; throw new IOException("fixture preparation failure"); });
            Check(recovered != first && JsonNode.DeepEquals(goodSnapshot, Good()!["snapshot"]), "fallback uses saved snapshot independently of current YAML");
            Check(Recovery()["candidate-failed"]!.GetValue<bool>() && Recovery()["result"]!.GetValue<string>() == "last-good-succeeded", "recovery does not clear candidate failure");
            lifecycle.Stop(service.ServiceId);
            var retried = 0;
            await Run(_ => { retried++; return Task.FromResult(Plan()); });
            Check(retried == 1 && !Recovery()["candidate-failed"]!.GetValue<bool>(), "new daemon invocation retries known failed candidate and clears failure on success");
            lifecycle.Stop(service.ServiceId);
            var unhealthy = Plan(true);
            var afterHealthFailure = await Run(_ => Task.FromResult(unhealthy));
            Check(lifecycle.Reconcile(service.ServiceId) && store.Read().ActiveDocuments().Count() == 1 &&
                store.Read().ActiveDocuments().Single()["data"]!["execution"]!["arguments"]!.AsArray().All(arg => arg!.GetValue<string>() != "--health-unready"),
                "health failure stops candidate and starts one healthy recovery instance");
            lifecycle.Stop(service.ServiceId);
            var choices = 0; var interactiveAttempts = 0;
            await Run(_ => ++interactiveAttempts == 1 ? Task.FromException<ScopedStartPlan>(new IOException("fixture")) : Task.FromResult(Plan()),
                detached: false, decide: (_, available) => { choices++; Check(available, "interactive recovery availability"); return ServiceRecoveryChoice.Retry; });
            Check(choices == 1 && interactiveAttempts == 2, "interactive retry follows an explicit decision");
            var restartAttempts = 0;
            await Run(_ => Task.FromResult(++restartAttempts == 1 ? Plan(true) : Plan()), restart: true,
                detached: false, decide: (_, _) => ServiceRecoveryChoice.Retry);
            Check(restartAttempts == 2 && lifecycle.Reconcile(service.ServiceId), "interactive restart can retry after original instance was stopped");
            lifecycle.Stop(service.ServiceId);
            await Run(_ => Task.FromException<ScopedStartPlan>(new IOException("fixture")), detached: false,
                decide: (_, _) => ServiceRecoveryChoice.LastGood);
            Check(lifecycle.Reconcile(service.ServiceId), "interactive last-good choice restores service");
            lifecycle.Stop(service.ServiceId);
            try
            {
                await Run(_ => Task.FromException<ScopedStartPlan>(new IOException("candidate")),
                    restore: (_, _) => Task.FromResult(Plan(true)));
                throw new Exception("expected double failure");
            }
            catch (StorageException error) when (error.Code == "service-recovery-failed")
            { Check(!lifecycle.Reconcile(service.ServiceId) && Recovery()["result"]!.GetValue<string>() == "last-good-failed", "double failure ends without a loop or unhealthy instance"); }
            await Run(_ => Task.FromResult(Plan()));
            var activeBefore = store.Read().ActiveDocuments().Single()["data"]!["instance-id"]!.GetValue<string>();
            var restores = 0;
            try
            {
                await Run(_ => Task.FromException<ScopedStartPlan>(new StorageException("resource-integrity-invalid", "fixture")), restart: true,
                    restore: (snapshot, token) => { restores++; return Restore(snapshot, token); });
                throw new Exception("expected fatal integrity");
            }
            catch (StorageException error) when (error.Code == "resource-integrity-invalid")
            { Check(restores == 0 && store.Read().ActiveDocuments().Single()["data"]!["instance-id"]!.GetValue<string>() == activeBefore, "known invalid integrity cannot fall back or stop original"); }
            lifecycle.Stop(service.ServiceId);
            var untouchedGood = Good()!.DeepClone();
            try
            {
                await Run(_ => Task.FromException<ScopedStartPlan>(new StorageException("scope-conflict", "fixture")));
                throw new Exception("expected conflict");
            }
            catch (StorageException error) when (error.Code == "scope-conflict")
            { Check(JsonNode.DeepEquals(untouchedGood, Good()), "coordination conflict preserves recovery evidence"); }
            Check(output.ToString().Contains("Última buena recuperada") && File.ReadAllText(context.PathFor($"local/runtime/{service.ServiceId}/recovery.log")).Contains("Última buena fallida"), "terminal and durable log describe recovery outcomes");
            var noGoodService = registry.EnsureProjectService(["other.yaml"], "no-good");
            try
            {
                await lifecycle.RunAsync(noGoodService.ServiceId, false, true,
                    _ => Task.FromException<ScopedStartPlan>(new IOException("fixture")), Restore);
                throw new Exception("expected no-last-good failure");
            }
            catch (IOException) { Check(!lifecycle.Reconcile(noGoodService.ServiceId), "no last-good gives a finite diagnostic"); }
            using var foreignListener = new TcpListener(IPAddress.Loopback, 0);
            foreignListener.Start(); var foreignPort = ((IPEndPoint)foreignListener.LocalEndpoint).Port; foreignListener.Stop();
            var foreignPlan = Plan() with { Arguments = ["--scoped-health-child", "--port", foreignPort.ToString()],
                Endpoint = new JsonObject { ["host"] = "127.0.0.1", ["port"] = foreignPort }, HealthUrl = new Uri($"http://127.0.0.1:{foreignPort}/health") };
            var foreignInstance = await lifecycle.StartAsync(noGoodService.ServiceId, foreignPlan);
            var foreignBefore = store.Read().ActiveDocuments().Single(document => document["data"]!["instance-id"]!.GetValue<string>() == foreignInstance)["data"]!.DeepClone();
            try
            {
                await Run(_ => Task.FromException<ScopedStartPlan>(new IOException("fixture")));
                Check(lifecycle.Reconcile(noGoodService.ServiceId) && JsonNode.DeepEquals(foreignBefore,
                    store.Read().ActiveDocuments().Single(document => document["data"]!["instance-id"]!.GetValue<string>() == foreignInstance)["data"]),
                    "recovery preserves unrelated service execution, snapshot and resource contributions");
            }
            finally { lifecycle.Stop(noGoodService.ServiceId); }
        }
        finally { lifecycle.Stop(service.ServiceId, () => true); }
        await ExactResources(root, Check);
        if (product is not null) await CliScenarios(product, apphost, root, port, Check);
        Console.WriteLine($"Service recovery: {checks} checks passed.");
    }

    private sealed class RejectResources : ICombinationResources
    {
        public Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exact, CancellationToken cancellation)
            => throw new Exception("No resource should be resolved in this empty snapshot");
    }

    private static async Task ExactResources(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "exact"); Directory.CreateDirectory(folder);
        var yaml = Path.Combine(folder, "coquena.yaml");
        File.WriteAllText(yaml, "name: exact\nservice:\n  backend:\n    version: latest\n    variant: cpu\n");
        var context = ScopeContext.Project(yaml);
        var fixture = new ScopedBackendTests.Fixture(); fixture.Add("b1", "cpu"); fixture.Add("b2", "cpu");
        var hardware = new HardwareContext("win-x64", "fixture", null, [], null, [], new("unknown", "fixture"), new("unknown", "fixture"));
        var versions = new List<string>();
        var backends = new ScopedBackends(context, (h, variant, version, token) =>
        { versions.Add(version); return fixture.Resolve(h, variant, version, token); },
            (path, _) => Task.FromResult($"version: {File.ReadAllText(path)} (abcdef)"));
        using var http = new HttpClient();
        var resources = new ScopedCombinationResources(context, hardware, new HuggingFaceStore(http), fixture,
            new ConsoleModelSelectionInteraction(), backends: backends);
        var selection = new RecipeSelection(folder, [yaml]);
        var prepared = await new CombinationPreparation(context).PrepareServiceAsync(selection, resources);
        check(!new ScopeStore(context).Read().Documents.Values.Any(document => document["kind"]!.GetValue<string>() == "last-good"),
            "preparation and exact imported resolutions cannot certify local execution");
        var plan = ScopedServicePlan.Compose(context, RecipeReader.Read(selection), prepared, Path.Combine(folder, "original.log"));
        var good = new JsonObject { ["snapshot"] = plan.Snapshot.DeepClone(), ["combination"] = plan.Combination,
            ["resources"] = plan.Resources.DeepClone(), ["endpoint"] = plan.Endpoint.DeepClone(),
            ["health"] = new JsonObject { ["url"] = plan.HealthUrl.ToString() },
            ["execution"] = new JsonObject { ["executable"] = plan.Executable,
                ["arguments"] = new JsonArray(plan.Arguments.Select(arg => (JsonNode?)JsonValue.Create(arg)).ToArray()) } };
        File.Delete(Path.Combine(Path.GetDirectoryName(plan.Executable)!, "neighbor.dll"));
        File.WriteAllText(yaml, "invalid: ["); fixture.Build = "b2"; versions.Clear();
        var recovered = await ScopedServiceSnapshot.RestoreAsync(context, good, resources, Path.Combine(folder, "restored.log"));
        check(versions.Count == 1 && versions[0] == "b1" && File.ReadAllText(recovered.Executable) == "1",
            "missing backend dependency restores exact b1 rather than newer latest or current YAML");
        check(recovered.Executable != plan.Executable && File.Exists(plan.Executable) && File.Exists(Path.Combine(Path.GetDirectoryName(recovered.Executable)!, "neighbor.dll")),
            "exact restoration creates a separate complete installation and retains previous files");
        check(JsonNode.DeepEquals(recovered.Snapshot, plan.Snapshot) && JsonNode.DeepEquals(recovered.Resources[0]!["resolution"], plan.Resources[0]!["resolution"]),
            "restoration preserves tested snapshot and exact artifact references");
        var stateStore = new ScopeStore(context); var state = stateStore.Read();
        var docs = state.Documents.ToDictionary(pair => pair.Key, pair => ProjectDocuments.Create(pair.Value["kind"]!.GetValue<string>(),
            pair.Value["revision"]!.GetValue<long>() + 1, pair.Value["data"]!.AsObject()));
        var originalInstallation = docs["local/inventory.json"]["data"]!["resources"]!.AsArray()
            .SelectMany(resource => resource!["installations"]!.AsArray()).Single(installation =>
                installation!["installation-id"]!.GetValue<string>() == plan.Resources[0]!["installation-id"]!.GetValue<string>())!;
        originalInstallation["integrity-status"] = "invalid";
        stateStore.Commit(state.Revision, docs);
        var calls = fixture.Calls;
        try
        {
            await ScopedServiceSnapshot.RestoreAsync(context, good, resources, Path.Combine(folder, "blocked.log"));
            throw new Exception("expected snapshot integrity rejection");
        }
        catch (StorageException error) when (error.Code == "resource-integrity-invalid")
        { check(fixture.Calls == calls, "known invalid snapshot resource cannot trigger exact redownload"); }
        var missingExternal = good.DeepClone().AsObject();
        missingExternal["resources"]![0]!["resource-id"] = Guid.NewGuid().ToString("D");
        missingExternal["resources"]![0]!["entry-path"] = Path.Combine(folder, "missing.exe");
        missingExternal["resources"]![0]!["resolution"] = new JsonObject { ["origin"] = "external" };
        try
        {
            await ScopedServiceSnapshot.RestoreAsync(context, missingExternal, new RejectResources(), Path.Combine(folder, "external.log"));
            throw new Exception("expected missing external intervention");
        }
        catch (StorageException error) when (error.Code == "snapshot-resource-missing")
        { check(true, "missing external requires intervention without acquiring a substitute"); }
    }

    private static async Task CliScenarios(string product, string apphost, string root, int port, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "cli"); Directory.CreateDirectory(folder);
        var yaml = Path.Combine(folder, "coquena.yaml");
        var saved = $"name: fixture\nservice:\n  backend:\n    executable: '{apphost.Replace('\\', '/')}'\n  server:\n    health:\n      url: http://127.0.0.1:{port}/health\n      timeout-seconds: 2\n    args:\n      scoped-health-child: true\n      port: {port}\n";
        File.WriteAllText(yaml, saved);
        var store = new ScopeStore(ScopeContext.Project(yaml));
        async Task<(int Code, string Text)> Command(string action)
        {
            var start = new System.Diagnostics.ProcessStartInfo(product)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["COQUENA_HOME"] = Path.Combine(root, "home");
            foreach (var arg in new[] { "--private", "--base", folder, "--file", yaml, "service", "self", action }.Concat(action == "stop" ? [] : new[] { "-d" })) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(true); throw; }
            return (process.ExitCode, await stdout + await stderr);
        }
        try
        {
            var started = await Command("start"); check(started.Code == 0, "published CLI healthy start: " + started.Text);
            check((await Command("stop")).Code == 0, "published CLI stop");
            File.WriteAllText(yaml, saved.Replace("      scoped-health-child: true", "      scoped-health-child: true\n      health-unready: true"));
            var fallback = await Command("start");
            check(fallback.Code == 0 && fallback.Text.Contains("Última buena recuperada"), "published CLI recovers after health failure: " + fallback.Text);
            check((await Command("stop")).Code == 0, "published CLI stop fallback");
            File.WriteAllText(yaml, "invalid: [");
            var malformed = await Command("restart");
            check(malformed.Code == 0 && malformed.Text.Contains("Última buena recuperada"), "published CLI recovers malformed YAML preparation using previous snapshot: " + malformed.Text);
            check((await Command("stop")).Code == 0, "published CLI stops despite malformed YAML");
            File.WriteAllText(yaml, saved);
            var fixedCandidate = await Command("start");
            check(fixedCandidate.Code == 0 && fixedCandidate.Text.Contains("diagnóstico de fallo eliminado"), "published CLI retries repaired candidate: " + fixedCandidate.Text);
            check(File.ReadAllText(store.Context.PathFor("local/recipes.json")).Contains("candidate-succeeded"), "published CLI persists recovered candidate success");
        }
        finally
        {
            var service = new RecipeRegistry(store).FindProjectService([yaml]);
            new ScopedServiceProcess(store).Stop(service.ServiceId, () => true);
        }
    }
}
