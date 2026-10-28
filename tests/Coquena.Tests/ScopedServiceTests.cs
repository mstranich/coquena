using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Coquena.Projects;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ScopedServiceTests
{
    public static async Task Run(string? product = null)
    {
        var checks = 0;
        void Check(bool condition, string detail)
        {
            if (!condition) throw new Exception(detail);
            checks++;
        }
        var root = Path.Combine("D:/tmp", "coquena-services-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var context = ScopeContext.Project(Path.Combine(root, "coquena.yaml"));
        var store = new ScopeStore(context);
        var registry = new RecipeRegistry(store);
        var service = registry.EnsureProjectService(["coquena.yaml"], "fixture");
        var lifecycle = new ScopedServiceProcess(store, Console.Out);
        var apphost = Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var plan = new ScopedStartPlan(apphost, ["--scoped-health-child", port.ToString()],
            Path.Combine(root, "first.log"), "fixture", new JsonObject(), new JsonArray(),
            new JsonObject { ["host"] = "127.0.0.1", ["port"] = port }, new Uri($"http://127.0.0.1:{port}/health"),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await lifecycle.StartAsync(service.ServiceId, plan, cancelled.Token); throw new Exception("expected cancellation"); }
            catch (OperationCanceledException) { Check(!lifecycle.Reconcile(service.ServiceId) && !File.Exists(plan.LogPath), "cancel before spawn leaves no process or log"); }
        }
        var instance = await lifecycle.StartAsync(service.ServiceId, plan);
        try
        {
            try { lifecycle.Stop(service.ServiceId, () => true, Guid.NewGuid().ToString("D")); throw new Exception("expected identity conflict"); }
            catch (StorageException e) when (e.Code == "instance-conflict") { Check(lifecycle.Reconcile(service.ServiceId), "stale observer cannot stop replacement"); }
            try
            {
                await lifecycle.RestartAsync(service.ServiceId, _ => Task.FromException<ScopedStartPlan>(new IOException("preparation failed")));
                throw new Exception("expected preparation failure");
            }
            catch (IOException) { Check(lifecycle.Reconcile(service.ServiceId), "preparation failure keeps original instance"); }
            var invalid = plan with
            {
                LogPath = Path.Combine(root, "invalid.log"),
                Resources = new JsonArray(new JsonObject { ["resource-id"] = Guid.NewGuid().ToString("D"), ["installation-id"] = Guid.NewGuid().ToString("D") })
            };
            try { await lifecycle.RestartAsync(service.ServiceId, _ => Task.FromResult(invalid)); throw new Exception("expected invalid binding"); }
            catch (StorageException e) when (e.Code == "unknown-resource") { Check(lifecycle.Reconcile(service.ServiceId), "invalid binding detected before stop"); }
            var resource = registry.RegisterResource("invalid-backend", "backend", "external");
            var before = store.Read();
            var documents = before.Documents.ToDictionary(p => p.Key, p => ProjectDocuments.Create(
                p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
            var installationId = Guid.NewGuid().ToString("D");
            var resourceEntry = documents["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["resource-id"]!.GetValue<string>() == resource.ResourceId)!;
            resourceEntry["installations"]!.AsArray().Add((JsonNode)new JsonObject
            {
                ["installation-id"] = installationId, ["availability"] = "available", ["integrity-status"] = "mismatch", ["files"] = new JsonArray()
            });
            store.Commit(before.Revision, documents);
            var invalidIntegrity = plan with { Resources = new JsonArray(new JsonObject { ["resource-id"] = resource.ResourceId, ["installation-id"] = installationId }) };
            try { await lifecycle.RestartAsync(service.ServiceId, _ => Task.FromResult(invalidIntegrity)); throw new Exception("expected integrity rejection"); }
            catch (StorageException e) when (e.Code == "resource-integrity-invalid") { Check(lifecycle.Reconcile(service.ServiceId), "known invalid integrity cannot stop the active instance"); }

            var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var restart = lifecycle.RestartAsync(service.ServiceId, async _ =>
            {
                preparing.SetResult();
                await release.Task;
                throw new IOException("preparation interrupted");
            });
            await preparing.Task;
            try
            {
                try { await lifecycle.StartAsync(service.ServiceId, plan); throw new Exception("expected lifecycle contention"); }
                catch (StorageException e) when (e.Code == "service-busy") { Check(true, "lifecycle lease prevents concurrent start during preparation"); }
            }
            finally { release.SetResult(); }
            try { await restart; throw new Exception("expected interrupted preparation"); }
            catch (IOException) { Check(lifecycle.Reconcile(service.ServiceId), "contended preparation leaves original running"); }
            try { lifecycle.Remove(service.ServiceId); throw new Exception("expected live removal rejection"); }
            catch (StorageException e) when (e.Code == "service-in-use") { Check(lifecycle.Reconcile(service.ServiceId), "remove cannot hide live instance"); }
        }
        finally { lifecycle.Stop(service.ServiceId, () => true, instance); }

        var marker = Path.Combine(root, "child.pid");
        var withChild = plan with
        {
            Arguments = ["--scoped-health-child", "--port", port.ToString(), "--spawn-stubborn-child", marker],
            LogPath = Path.Combine(root, "group.log")
        };
        var groupInstance = await lifecycle.StartAsync(service.ServiceId, withChild);
        try
        {
            using var descendant = System.Diagnostics.Process.GetProcessById(int.Parse(File.ReadAllText(marker)));
            lifecycle.Stop(service.ServiceId, expectedInstanceId: groupInstance);
            Check(descendant.WaitForExit(3000) && !lifecycle.Reconcile(service.ServiceId), "ordered stop escalates remaining descendants after root exits");
        }
        finally
        {
            KillRecordedChild(marker);
        }

        var orphaned = withChild with { Arguments = [.. withChild.Arguments, "--exit-with-child"], LogPath = Path.Combine(root, "orphaned.log") };
        try
        {
            try { await lifecycle.StartAsync(service.ServiceId, orphaned); throw new Exception("expected surviving group"); }
            catch (Coquena.Configuration.ConfigurationException)
            {
                Check(store.Read().ActiveDocuments().Any(), "root exit preserves evidence when descendants remain");
            }
            try { lifecycle.Reconcile(service.ServiceId); throw new Exception("expected uncertain group"); }
            catch (Coquena.Configuration.ConfigurationException) { Check(true, "reconciliation cannot release a surviving group"); }
        }
        finally
        {
            KillRecordedChild(marker);
        }
        Check(!lifecycle.Reconcile(service.ServiceId), "reconciliation releases group only after all descendants exit");

        // Wait until publication before cancelling, to exercise cleanup of a real
        // child that has started but cannot pass health.
        using (var cancelled = new CancellationTokenSource())
        {
            var unready = plan with { Arguments = ["--scoped-health-child", port.ToString(), "--health-unready"], LogPath = Path.Combine(root, "cancelled.log") };
            var starting = lifecycle.StartAsync(service.ServiceId, unready, cancelled.Token, () => true);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!store.Read().ActiveDocuments().Any())
            {
                if (starting.IsCompleted || DateTime.UtcNow > deadline) throw new Exception("child never published");
                await Task.Delay(20);
            }
            cancelled.Cancel();
            try { await starting; throw new Exception("expected health cancellation"); }
            catch (OperationCanceledException) { Check(!lifecycle.Reconcile(service.ServiceId), "cancelled health wait stops and reconciles child"); }
        }
        lifecycle.Remove(service.ServiceId);
        Check(registry.Services().Length == 0 && File.Exists(plan.LogPath), "removal preserves log evidence");

        var previousHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
        var home = Path.Combine(root, "syntax-home");
        Environment.SetEnvironmentVariable("COQUENA_HOME", home);
        try
        {
            string[][] invalid = [
                ["service", "self", "stop", "-g"], ["service", "self", "stop", "-d"],
                ["service", "self", "start", "--service", "self"], ["service", "list", "--service", "self"],
                ["service", "default", "set", "self"], ["service", "default", "set", "default"],
                ["service", "start", "remove"], ["service", "start", "-d", "-d"],
                ["service", "start", "--base", root, "--project-directory", root],
                ["service", "start", "-g", "--file", "anything.yaml"], ["service", "--service"],
                ["--service", "self", "service", "start", "--service", "self"]
            ];
            foreach (var args in invalid)
            {
                using var output = new StringWriter();
                using var error = new StringWriter();
                var exit = product is null ? Cli.Run(args, output, error) : Invoke(product, args);
                Check(exit == 2 && !Directory.Exists(home), "invalid syntax has no scope effects: " + string.Join(' ', args));
            }
        }
        finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previousHome); }

        // Exercise the actual command boundary as well as the lifecycle API.
        var cliRoot = Path.Combine(root, "cli");
        Directory.CreateDirectory(cliRoot);
        File.WriteAllText(Path.Combine(cliRoot, "coquena.yaml"),
            $"name: prueba\nservice:\n  backend:\n    executable: '{apphost.Replace('\\', '/').Replace("'", "''")}'\n  server:\n    health:\n      url: http://127.0.0.1:{port}/health\n      timeout-seconds: 10\n    args:\n      port: {port}\n      scoped-health-child: true\nmodels:\n  unused:\n    files: [./absent.gguf]\npresets:\n  unused:\n    args:\n      model: model://unused\n");
        Environment.SetEnvironmentVariable("COQUENA_HOME", home);
        try
        {
            int Command(params string[] args)
            {
                using var output = new StringWriter();
                using var error = new StringWriter();
                var code = product is null ? Cli.Run(args, output, error) : Invoke(product, args);
                Check(code == 0, "CLI lifecycle: " + string.Join(' ', args) + " " + error);
                return code;
            }
            string[] options = ["--private", "--project-directory", cliRoot];
            Command([.. options, "service", "self", "start", "-d"]);
            var cliStore = new ScopeStore(ScopeContext.Project(Path.Combine(cliRoot, "coquena.yaml")));
            var cliService = new RecipeRegistry(cliStore).FindProjectService([Path.Combine(cliRoot, "coquena.yaml")]);
            try
            {
                Command([.. options, "service", "self", "start", "-d"]);
                Command(["server", "self", "restart", "-d", .. options]);
                Command(["service", "list", .. options]);
            }
            finally { Command(["service", "self", "stop", .. options]); }
            Check(!new ScopedServiceProcess(cliStore).Reconcile(cliService.ServiceId), "CLI confirms termination");
            Command(["service", "self", "remove", .. options]);
            if (product is not null)
            {
                var yaml = Path.Combine(cliRoot, "coquena.yaml");
                File.WriteAllText(yaml, File.ReadAllText(yaml).Replace("      scoped-health-child: true", "      scoped-health-child: true\n      health-unready: true"));
                var id = Guid.NewGuid();
                using var child = WindowsServerProcess.Create(product, [.. options, "service", "self", "start", "-d"], Path.Combine(root, "console.log"), id);
                var identity = new Coquena.Contracts.InstanceDocument(1, id, Guid.Empty, Guid.Empty, "", child.Pid,
                    "detached", "", 0, false, [], [], "", "", child.Created, child.Executable);
                child.Activate();
                using var process = System.Diagnostics.Process.GetProcessById(child.Pid);
                var processHandle = process.Handle;
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    while (!cliStore.Read().ActiveDocuments().Any())
                    {
                        if (process.HasExited || DateTime.UtcNow > deadline) throw new Exception("native CLI never published child");
                        await Task.Delay(50);
                    }
                    Check(Invoke(apphost, ["--internal-scoped-signal", id.ToString("D"), child.Pid.ToString(), child.Created.ToString(), child.Executable]) == 0,
                        "console Ctrl+C delivered to native CLI during health wait");
                    Check(process.WaitForExit(15000) && GetExitCodeProcess(processHandle, out var exitCode) && exitCode == 130 && !cliStore.Read().ActiveDocuments().Any(),
                        "native CLI cancels health, stops child and returns 130");
                }
                finally { if (!process.HasExited) WindowsServerProcess.Force(identity); }
            }
        }
        finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previousHome); }
        Console.WriteLine($"Scoped services: {checks} checks passed.");
    }

    private static void KillRecordedChild(string marker)
    {
        if (!File.Exists(marker)) return;
        try
        {
            using var child = System.Diagnostics.Process.GetProcessById(int.Parse(File.ReadAllText(marker)));
            if (!child.HasExited) { child.Kill(); child.WaitForExit(); }
        }
        catch (ArgumentException) { }
    }

    private static int Invoke(string executable, string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(true); throw new Exception("CLI timeout"); }
        Task.WaitAll(output, error);
        return process.ExitCode;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(nint process, out uint code);
}
