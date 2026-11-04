using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ObservationTests
{
    public static async Task Run(string? product, string? backend = null)
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var root = Path.Combine(OperatingSystem.IsWindows() ? "D:/tmp" : "/tmp", "coquena-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var proc = Path.Combine(root, "proc"); var drm = Path.Combine(root, "drm");
        Directory.CreateDirectory(proc); Directory.CreateDirectory(Path.Combine(drm, "card0/device"));
        File.WriteAllText(Path.Combine(proc, "stat"), "cpu 100 20 30 400 50 0 0 0 10 0\n");
        File.WriteAllText(Path.Combine(proc, "meminfo"), "MemTotal: 8192 kB\nMemAvailable: 2048 kB\n");
        File.WriteAllText(Path.Combine(drm, "card0/device/mem_info_vram_total"), "4096");
        File.WriteAllText(Path.Combine(drm, "card0/device/mem_info_vram_used"), "-1");
        var linux = await new LinuxSystemMetrics(proc, drm).CaptureAsync();
        Check(linux.Samples.Single(s => s.Name == "ram.used").Value == 6144 * 1024, "Linux RAM is available-based bytes");
        Check(linux.Samples.Single(s => s.Name == "cpu.utilization").Value is null, "unchanged CPU counters are unavailable, not zero");
        var card = linux.Devices.Single(d => d.Id == "card0");
        Check(card.Samples.Single(s => s.Name == "vram.total").Value == 4096, "DRM capacity uses driver bytes");
        Check(card.Samples.Single(s => s.Name == "vram.used").Value is null && card.Samples.Single(s => s.Name == "gpu.utilization").UnavailableReason is not null,
            "invalid/absent DRM counters are unavailable with reason");
        var timestamp = linux.Samples[0];
        Check(timestamp.IsFresh(timestamp.ObservedAt, TimeSpan.FromSeconds(1)) && !timestamp.IsFresh(timestamp.ObservedAt.AddSeconds(2), TimeSpan.FromSeconds(1)), "freshness is explicit");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await new LinuxSystemMetrics(proc, drm).CaptureAsync(cancelled.Token); throw new Exception("expected cancellation"); }
            catch (OperationCanceledException) { Check(true, "hardware capture is cancellable"); }
        }
        var hardware = await SystemMetrics.Create().CaptureAsync();
        Check(hardware.Samples.All(s => s.Value is null || double.IsFinite(s.Value.Value) && s.Value >= 0), "real OS samples are finite and nonnegative");
        Check(hardware.Samples.Where(s => s.Name.StartsWith("ram.")).All(s => s.Value > 0), "real OS RAM is available");
        Check(hardware.Devices.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == hardware.Devices.Count, "shared devices are enumerated once");
        if (OperatingSystem.IsWindows()) await Services();
        if (OperatingSystem.IsWindows() && backend is not null) await Router();
        Console.WriteLine($"Observation checks: {checks}; platform {hardware.Environment}. Local fixtures: {root}");

        async Task Services()
        {
            var context = ScopeContext.Project(Path.Combine(root, "coquena.yaml"));
            File.WriteAllText(Path.Combine(root, "coquena.yaml"), "name: edited-name\nservice:\n  backend:\n    version: latest\n    variant: cpu\n");
            var store = new ScopeStore(context); var registry = new RecipeRegistry(store);
            var service = registry.EnsureProjectService(["coquena.yaml"], "fixture");
            var second = registry.RegisterService(registry.Register(["other.yaml"], "other").RecipeId);
            var stopped = await new ServiceObservation(store).CaptureAsync(second);
            Check(stopped.ProcessState == "stopped", "known stopped service remains visible");
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var apphost = Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe");
            var lifecycle = new ScopedServiceProcess(store);
            var plan = new ScopedStartPlan(apphost, ["--scoped-health-child", port.ToString()], Path.Combine(root, "fixture.log"),
                "recorded-combination", new JsonObject(), new JsonArray(), new JsonObject { ["host"] = "127.0.0.1", ["port"] = port },
                new Uri($"http://127.0.0.1:{port}/health"), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
            var instance = await lifecycle.StartAsync(service.ServiceId, plan);
            try
            {
                var handler = new MetricsHandler(); using var client = new HttpClient(handler);
                var observer = new ServiceObservation(store, client);
                var revision = store.Read().Revision;
                var observed = await observer.CaptureAsync(service);
                Check(observed.ProcessState == "alive" && observed.ApiState == "available" && observed.Health == "HTTP 200", "process/API/health are independent");
                Check(observed.IntentComparison.Contains("differs") && observed.Name == "fixture", "edited intention does not rename local service or replace active snapshot");
                Check(observed.ActiveComposition?["combination"]?.GetValue<string>() == "recorded-combination", "execution composition comes from registered instance");
                Check(observed.Metrics.Single(s => s.Name == "ram.private-commit").Value > 0, "verified job memory is measured");
                Check(observed.Metrics.Any(s => s.Name == "ram.working-set" && s.Value > 0 && s.Scope.Contains("/created:")), "resident memory is attributed to verified process generations without summing shared pages");
                Check(observed.Metrics.Single(s => s.Name == "vram.used").Value is null, "device totals are never attributed to service");
                Check(observed.Entries.Single(e => e.Name == "loaded").Metrics.Any(m => m.Unit == "tokens/s (backend mean)"), "entry metrics preserve mean units");
                Check(observed.Entries.Single(e => e.Name == "idle").Metrics.Count == 0 && handler.MetricRequests == 1, "unloaded entry is never loaded for metrics");
                Check(store.Read().Revision == revision, "observing does not promote last-good or rewrite state");
                handler.Broken = true;
                var broken = await observer.CaptureAsync(service);
                Check(broken.ProcessState == "alive" && broken.ApiState == "unavailable", "live process without API is retained");
                handler.Broken = false; handler.WithoutMetrics = true;
                var without = await observer.CaptureAsync(service);
                Check(without.Entries.Single(e => e.Name == "loaded").Metrics.Count == 0 && without.ProcessState == "alive", "backend without telemetry remains visible");
                handler.WithoutMetrics = false;
                var update = store.Read(); var updated = CloneNext(update);
                var updatedActive = updated.Values.Single(d => d["kind"]?.GetValue<string>() == "active")["data"]!;
                updatedActive["snapshot"] = new JsonObject { ["service"] = new JsonObject { ["server"] = new JsonObject
                { ["args"] = new JsonObject { ["metrics"] = new JsonObject { ["type"] = "Boolean", ["value"] = "false" } } } } };
                var updatedEntity = updated["local/recipes.json"]["data"]!["recipes"]!.AsArray().SelectMany(r => r!["entities"]!.AsArray())
                    .Single(e => e!["id"]!.GetValue<string>() == service.ServiceId)!;
                updatedEntity["recovery"] = new JsonObject { ["result"] = "last-good", ["instance-id"] = instance };
                store.Commit(update.Revision, updated);
                var metricRequests = handler.MetricRequests;
                var disabled = await observer.CaptureAsync(service);
                Check(handler.MetricRequests == metricRequests && disabled.Entries.Single(e => e.Name == "loaded").Diagnostic!.Contains("desactivadas"),
                    "disabled metrics do not suppress state and are not requested");
                Check(disabled.Recovery.Contains("last-good") && disabled.ActiveComposition!["combination"]!.GetValue<string>() == "recorded-combination",
                    "recovery evidence is retained with actual execution");
                // Restore the fixture snapshot before the timeout/race checks.
                update = store.Read(); updated = CloneNext(update);
                updated.Values.Single(d => d["kind"]?.GetValue<string>() == "active")["data"]!["snapshot"] = new JsonObject();
                store.Commit(update.Revision, updated);
                handler.DelayMetrics = true;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var timedOut = await observer.CaptureAsync(service);
                Check(clock.Elapsed < TimeSpan.FromSeconds(8) && timedOut.ProcessState == "alive" &&
                    timedOut.Entries.Single(e => e.Name == "loaded").Metrics.Count == 0, "one five-second HTTP budget bounds a slow entry");
                handler.DelayMetrics = false;
                // Deliberately stale creation identity: even a live, matching PID
                // must not authorize an HTTP request or mutate the stale record.
                var before = store.Read(); var docs = CloneNext(before);
                var active = docs.Values.Single(d => d["kind"]?.GetValue<string>() == "active")["data"]!;
                var registered = docs["local/recipes.json"]["data"]!["recipes"]!.AsArray()
                    .SelectMany(r => r!["entities"]!.AsArray()).Single(e => e!["id"]!.GetValue<string>() == service.ServiceId)!;
                var created = active["process"]!["creation-time"]!.GetValue<long>();
                active["process"]!["creation-time"] = created + 1; registered["instance"]!["process"]!["creation-time"] = created + 1;
                store.Commit(before.Revision, docs);
                var requests = handler.Requests;
                var stale = await observer.CaptureAsync(service);
                Check(stale.ProcessState == "ended-or-reused" && handler.Requests == requests, "reused PID cannot authorize API");
                var restore = store.Read(); docs = CloneNext(restore);
                active = docs.Values.Single(d => d["kind"]?.GetValue<string>() == "active")["data"]!;
                registered = docs["local/recipes.json"]["data"]!["recipes"]!.AsArray()
                    .SelectMany(r => r!["entities"]!.AsArray()).Single(e => e!["id"]!.GetValue<string>() == service.ServiceId)!;
                active["process"]!["creation-time"] = created; registered["instance"]!["process"]!["creation-time"] = created;
                store.Commit(restore.Revision, docs);
                if (product is not null)
                {
                    var home = new Home(Path.Combine(root, "home"));
                    new ProjectIndex(home).Refresh(store);
                    var globalStore = new ScopeStore(ScopeContext.Global(home)); var globalRegistry = new RecipeRegistry(globalStore);
                    var globalService = globalRegistry.RegisterService(globalRegistry.Register(["global.yaml"], "global-fixture").RecipeId);
                    globalRegistry.SetDefaultService(globalService.ServiceId);
                    Check((await new ServiceObservation(globalStore).CaptureAsync(globalService)).IsDefault, "default alias is identified on stopped globals");
                    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(product)
                    {
                        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                        ArgumentList = { "ps", "--service", "self", "--file", Path.Combine(root, "coquena.yaml") },
                        Environment = { ["COQUENA_HOME"] = Path.Combine(root, "home") }
                    })!;
                    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));
                    Check(process.ExitCode == 0 && (await stdout).Contains("Servicio fixture | alive | HTTP 200"), "published CLI observes selected scoped service and terminates: " + await stderr);
                    var general = await Product("ps");
                    Check(general.Contains("global-fixture (default)") && general.Contains("Servicio fixture |") && general.Contains("Servicio other |"),
                        "general observation includes global and known project services, including stopped");
                    var onlyGlobal = await Product("ps", "--global");
                    Check(onlyGlobal.Contains("global-fixture") && !onlyGlobal.Contains("Servicio fixture |"), "global filter limits services while retaining machine hardware");
                    var byDefault = await Product("ps", "--service", "default");
                    Check(byDefault.Contains("global-fixture") && !byDefault.Contains("Servicio fixture |"), "explicit default selects only its registered global");
                    async Task<string> Product(params string[] arguments)
                    {
                        var start = new System.Diagnostics.ProcessStartInfo(product) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                        start.Environment["COQUENA_HOME"] = home.Root;
                        foreach (var argument in arguments) start.ArgumentList.Add(argument);
                        using var child = System.Diagnostics.Process.Start(start)!;
                        var text = child.StandardOutput.ReadToEndAsync(); var errors = child.StandardError.ReadToEndAsync();
                        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));
                        Check(child.ExitCode == 0, "native ps selection completes: " + await errors);
                        return await text;
                    }
                }
            }
            finally { lifecycle.Stop(service.ServiceId, () => true, instance); }
        }
        async Task Router()
        {
            var routerRoot = Path.Combine(root, "router"); Directory.CreateDirectory(routerRoot);
            var store = new ScopeStore(ScopeContext.Project(Path.Combine(routerRoot, "coquena.yaml")));
            var service = new RecipeRegistry(store).EnsureProjectService(["coquena.yaml"], "router");
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var ini = Path.Combine(routerRoot, "entries.ini");
            File.WriteAllText(ini, "[unloaded]\nmodel=" + Path.Combine(routerRoot, "absent.gguf").Replace('\\', '/') + "\nload-on-startup=false\n");
            var plan = new ScopedStartPlan(backend!, ["--host", "127.0.0.1", "--port", port.ToString(), "--metrics", "--models-preset", ini],
                Path.Combine(routerRoot, "router.log"), "router-fixture", new JsonObject(), [],
                new JsonObject { ["host"] = "127.0.0.1", ["port"] = port }, new Uri($"http://127.0.0.1:{port}/health"),
                TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(100));
            var lifecycle = new ScopedServiceProcess(store);
            var instance = await lifecycle.StartAsync(service.ServiceId, plan);
            try
            {
                var state = store.Read();
                var observed = await new ServiceObservation(store).CaptureAsync(service);
                Check(observed.ApiState == "available" && observed.Health == "HTTP 200", "real router API and health observed");
                Check(observed.Entries.Count == 1 && observed.Entries[0].State == "unloaded" && observed.Entries[0].Metrics.Count == 0,
                    "real router remains unloaded despite absent model file");
                Check(store.Read().Revision == state.Revision, "real router observation leaves registered composition unchanged");
                Check(!observed.CompositionCurrent, "unregistered router entries are not claimed as the registered composition");
            }
            finally { lifecycle.Stop(service.ServiceId, () => true, instance); }
        }
    }
    private static Dictionary<string, JsonObject> CloneNext(ScopeSnapshot state) => state.Documents.ToDictionary(p => p.Key, p =>
    {
        var doc = p.Value.DeepClone().AsObject(); doc["revision"] = doc["revision"]!.GetValue<long>() + 1; return doc;
    });
    private sealed class MetricsHandler : HttpMessageHandler
    {
        public int Requests, MetricRequests; public bool Broken, WithoutMetrics, DelayMetrics;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.Method != HttpMethod.Get || request.RequestUri!.Query.Contains("reload")) throw new Exception("observation must be read-only");
            if (Broken) throw new HttpRequestException("fixture unavailable");
            if (request.RequestUri!.AbsolutePath == "/health") return new HttpResponseMessage(HttpStatusCode.OK);
            if (request.RequestUri.AbsolutePath == "/models") return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"data\":[{\"id\":\"loaded\",\"status\":{\"value\":\"loaded\",\"args\":[]}},{\"id\":\"idle\",\"status\":{\"value\":\"unloaded\",\"args\":[]}}]}") };
            MetricRequests++;
            if (DelayMetrics) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (!request.RequestUri.Query.Contains("autoload=false") || !request.RequestUri.Query.Contains("model=loaded")) throw new Exception("metrics must never autoload");
            return new HttpResponseMessage(WithoutMetrics ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            { Content = new StringContent("llamacpp:prompt_tokens_seconds 10\nllamacpp:requests_processing 1\n") };
        }
    }
}
