using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ServiceLogHookTests
{
    public static async Task Run(string? product = null)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var root = Path.Combine("D:/tmp", "coquena-log-hooks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var context = ScopeContext.Project(Path.Combine(root, "coquena.yaml"));
        var store = new ScopeStore(context);
        var registry = new RecipeRegistry(store);
        var service = registry.EnsureProjectService(["coquena.yaml"], "fixture");
        var capture = Path.Combine(root, "notifications.jsonl");
        var app = Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe");
        var hook = new StartFailureHook(app, ["--start-failure-hook", capture, "ok"]);
        using var output = new StringWriter();
        var lifecycle = new ScopedServiceProcess(store, output, output);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var logs = Path.Combine(root, "external-shared-logs");
        Directory.CreateDirectory(logs);
        var foreign = Path.Combine(logs, Guid.NewGuid().ToString("D") + ".log");
        File.WriteAllText(foreign, "foreign file");
        ScopedStartPlan Plan() => new(app, ["--scoped-health-child", "--port", port.ToString()],
            Path.Combine(logs, "unused.log"), "fixture", new JsonObject(), new JsonArray(),
            new JsonObject { ["host"] = "127.0.0.1", ["port"] = port }, new Uri($"http://127.0.0.1:{port}/health"),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100), LogDirectory: logs, LogRetentionBytes: 1);
        Task<ScopedStartPlan> Fail(CancellationToken _) => Task.FromException<ScopedStartPlan>(new IOException("private fixture detail"));
        Task<ScopedStartPlan> Restore(JsonObject _, CancellationToken __) => Task.FromResult(Plan());
        JsonObject[] Payloads() => File.Exists(capture) ? File.ReadAllLines(capture).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray() : [];
        async Task ExpectedFailure(string id, StartFailureHook? configured, bool integrity = false,
            Func<JsonObject, CancellationToken, Task<ScopedStartPlan>>? restore = null)
        {
            try
            {
                await lifecycle.RunAsync(id, false, true,
                    integrity ? _ => Task.FromException<ScopedStartPlan>(new StorageException("resource-integrity-invalid", "private")) : Fail,
                    restore ?? Restore, hook: configured);
                throw new Exception("expected failure");
            }
            catch (IOException) { }
            catch (StorageException e) when (e.Code is "resource-integrity-invalid" or "service-recovery-failed") { }
        }
        try
        {
            await ExpectedFailure(service.ServiceId, null);
            Check(!File.Exists(capture), "no configured hook means no notification");
            await ExpectedFailure(service.ServiceId, hook);
            Check(Payloads().Length == 1 && Payloads()[0]["recovery"]!.GetValue<string>() == "not-attempted" &&
                !Payloads()[0]["available"]!.GetValue<bool>(), "first start reports no recovery and unavailable once");
            await ExpectedFailure(service.ServiceId, hook, integrity: true);
            Check(Payloads().Length == 2 && Payloads()[1]["diagnostic-code"]!.GetValue<string>() == "resource-integrity-invalid", "fatal integrity notifies without recovery");
            var attempts = 0;
            await lifecycle.RunAsync(service.ServiceId, false, false,
                token => ++attempts < 3 ? Fail(token) : Task.FromResult(Plan()), Restore,
                (_, _) => ServiceRecoveryChoice.Retry, hook: hook);
            Check(attempts == 3 && Payloads().Length == 3 && Payloads()[2]["available"]!.GetValue<bool>(), "interactive retries notify once after final successful candidate");
            var firstPath = store.Read().ActiveDocuments().Single()["data"]!["log"]!.GetValue<string>();
            var firstId = store.Read().ActiveDocuments().Single()["data"]!["instance-id"]!.GetValue<string>();
            Check(Path.GetFileName(firstPath) == firstId + ".log", "log filename uses actual instance identity");
            lifecycle.Stop(service.ServiceId);
            File.AppendAllText(firstPath, new string('x', 100));
            lifecycle.Reconcile(service.ServiceId);
            Check(File.Exists(firstPath) && output.ToString().Contains("protegidos"), "last oversized log protected after termination");
            await lifecycle.RunAsync(service.ServiceId, false, true, Fail, Restore, hook: hook);
            Check(Payloads().Length == 4 && Payloads()[3]["recovery"]!.GetValue<string>() == "succeeded" &&
                Payloads()[3]["available"]!.GetValue<bool>(), "recovery success still notifies once");
            Check(!File.Exists(firstPath) && File.Exists(foreign), "retention deletes owned obsolete file and preserves unrelated UUID log");
            lifecycle.Stop(service.ServiceId);
            await ExpectedFailure(service.ServiceId, hook, restore: (_, _) => Task.FromException<ScopedStartPlan>(new IOException("private recovery")));
            Check(Payloads().Length == 5 && Payloads()[4]["recovery"]!.GetValue<string>() == "failed" &&
                !Payloads()[4]["available"]!.GetValue<bool>(), "double failure notifies final unavailable outcome");
            var failedHook = hook with { Arguments = ["--start-failure-hook", capture, "fail"] };
            await lifecycle.RunAsync(service.ServiceId, false, true, Fail, Restore, hook: failedHook);
            Check(lifecycle.Reconcile(service.ServiceId) && output.ToString().Contains("[Hook]"), "hook error cannot revert restored service");
            Check(!output.ToString().Contains("HOOK_PRIVATE_OUTPUT") && !File.ReadAllText(capture).Contains("private"), "hook output and raw failures excluded from diagnostics and payload");
            lifecycle.Stop(service.ServiceId);
            var replacedPath = store.Read().Documents.Values.Single(d => d["kind"]!.GetValue<string>() == "active")["data"]!["log"]!.GetValue<string>();
            File.Move(replacedPath, replacedPath + ".original");
            File.WriteAllText(replacedPath, new string('r', 100));
            await lifecycle.RunAsync(service.ServiceId, false, true, _ => Task.FromResult(Plan()), Restore);
            Check(File.Exists(replacedPath) && output.ToString().Contains("propiedad"), "retention refuses a replacement at a registered log path");
            lifecycle.Stop(service.ServiceId);
            var prior = store.Read().Documents.Values.Single(d => d["kind"]!.GetValue<string>() == "last-good")["data"]!;
            Check(prior["log-directory"]!.GetValue<string>() == logs && prior["log-retention-bytes"]!.GetValue<long>() == 1, "last-good preserves resolved log policy");
            var restored = await ScopedServiceSnapshot.RestoreAsync(context, prior.AsObject(), new NoResources(), Path.Combine(root, "fallback.log"));
            Check(restored.LogDirectory == logs && restored.LogRetentionBytes == 1, "snapshot restores logging without current YAML");
            var yaml = Path.Combine(root, "coquena.yaml");
            File.WriteAllText(yaml, "service:\n  backend: {executable: ./fixture.exe}\n  server:\n    hooks:\n      start-failed:\n        command: ./notify.exe\n        args: [literal]\n");
            var parsed = StartFailureHook.FromRecipe(RecipeReader.Read(new(root)));
            Check(parsed!.Command == Path.GetFullPath(Path.Combine(root, "notify.exe")) && parsed.Arguments.SequenceEqual(new[] { "literal" }), "hook path resolved from recipe base and args separate");
            Environment.SetEnvironmentVariable("COQUENA_HOOK_TEST_SECRET", "private-value");
            File.WriteAllText(yaml, File.ReadAllText(yaml).Replace("[literal]", "[\"${COQUENA_HOOK_TEST_SECRET}\"]"));
            try { StartFailureHook.FromRecipe(RecipeReader.Read(new(root))); throw new Exception("secret accepted"); }
            catch (RecipeException e) when (e.Code == "sensitive-hook-argument") { Check(true, "expanded secrets rejected before launching a hook"); }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            await (hook with { Arguments = ["--start-failure-hook", capture, "hang"] }).NotifyAsync(service.ServiceId, Guid.NewGuid().ToString("D"), "failed", false, "fixture", output);
            Check(timer.Elapsed < TimeSpan.FromSeconds(15) && output.ToString().Contains("No se completó"), "hook deadline is bounded and diagnosed");
        }
        finally { lifecycle.Stop(service.ServiceId, () => true); Environment.SetEnvironmentVariable("COQUENA_HOOK_TEST_SECRET", null); }
        if (product is not null) await Native(product, app, root, port, Check);
        Console.WriteLine($"Service logs/hooks: {checks} checks passed (anonymized fixtures; no inference).");
    }

    private static async Task Native(string product, string app, string root, int port, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "native-source"); Directory.CreateDirectory(folder);
        var home = new Home(Path.Combine(root, "native-home"));
        var yaml = Path.Combine(folder, "coquena.yaml");
        var capture = Path.Combine(root, "native-notifications.jsonl");
        var saved = $"name: logs-fixture\nservice:\n  backend:\n    executable: '{app.Replace('\\', '/')}'\n  server:\n    args:\n      scoped-health-child: true\n      port: {port}\n    health:\n      url: http://127.0.0.1:{port}/health\n      timeout-seconds: 1\n    hooks:\n      start-failed:\n        command: '{app.Replace('\\', '/')}'\n        args: ['--start-failure-hook', '{capture.Replace('\\', '/')}', ok]\n";
        File.WriteAllText(yaml, saved);
        async Task<int> Command(params string[] args)
        {
            var start = new System.Diagnostics.ProcessStartInfo(product) { UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.Environment["COQUENA_HOME"] = home.Root;
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40)); }
            catch { process.Kill(true); throw; }
            File.AppendAllText(Path.Combine(root, "native-cli.log"), await stdout + await stderr);
            return process.ExitCode;
        }
        string[] Local(string action) => ["--private", "--base", folder, "--file", yaml, "service", "self", action, .. action == "stop" ? Array.Empty<string>() : new[] { "-d" }];
        var local = new ScopeStore(ScopeContext.Project(yaml));
        var global = new ScopeStore(ScopeContext.Global(home));
        try
        {
            check(await Command("config", "log-retention", "set", "2MiB") == 0, "native global log preference");
            check(await Command(Local("start")) == 0, "native project start");
            var active = local.Read().ActiveDocuments().Single()["data"]!;
            var log = active["log"]!.GetValue<string>();
            check(Path.GetFileName(log) == active["instance-id"]!.GetValue<string>() + ".log" &&
                log.StartsWith(local.Context.Root, StringComparison.OrdinalIgnoreCase), "native external backend default log belongs to project state");
            check(await Command(Local("stop")) == 0, "native project stop");
            File.AppendAllText(log, new string('x', 100));
            var obsoleteProjectLog = log;
            File.WriteAllText(yaml, saved.Replace("\n    args:\n", "\n    logs:\n      directory: ./shared-logs\n      retention: {max-bytes: 1}\n    args:\n").Replace("      scoped-health-child: true", "      scoped-health-child: true\n      health-unready: true"));
            check(await Command(Local("start")) == 0, "native unhealthy candidate restores service");
            var payload = JsonNode.Parse(File.ReadAllLines(capture).Single())!;
            check(payload["recovery"]!.GetValue<string>() == "succeeded" && payload["available"]!.GetValue<bool>(), "native hook reports restored final availability once");
            check(Directory.GetFiles(Path.Combine(folder, "shared-logs"), "*.log").Length == 1, "native candidate uses relative external log directory");
            check(!File.Exists(obsoleteProjectLog), "native YAML override supersedes larger global retention");
            check(await Command(Local("stop")) == 0, "native restored project stop");
            File.WriteAllText(yaml, saved);
            check(await Command("import", yaml, "--select", "service", "--as-default") == 0, "native import service only");
            check(await Command("service", "start", "-d") == 0, "native imported global start");
            active = global.Read().ActiveDocuments().Single()["data"]!;
            log = active["log"]!.GetValue<string>();
            check(log.StartsWith(global.Context.Root, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(log) == active["instance-id"]!.GetValue<string>() + ".log", "native global default log belongs to service state");
            check(await Command("service", "stop") == 0, "native global stop");
            File.AppendAllText(log, new string('x', 3 * 1024 * 1024));
            check(await Command("service", "start", "-d") == 0 && !File.Exists(log), "native global service inherits configured retention");
            check(await Command("service", "stop") == 0, "native global final stop");
        }
        finally
        {
            foreach (var scope in new[] { local, global })
                foreach (var service in new RecipeRegistry(scope).Services()) new ScopedServiceProcess(scope).Stop(service.ServiceId, () => true);
        }
    }
    private sealed class NoResources : ICombinationResources
    {
        public Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exactResolution, CancellationToken cancellation)
            => throw new Exception("unexpected resource preparation");
    }
}
