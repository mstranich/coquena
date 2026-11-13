using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class WatchTests
{
    public static async Task Run(string? product = null, string? backend = null)
    {
        var checks = 0;
        void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); checks++; }
        var preferences = new ObserverPreferences();
        Check(new[] { 0, 1, 2, 5, 31 }.Select(i => ServiceWatch.RetryDelay(preferences, i).TotalSeconds).SequenceEqual([1d, 2, 4, 30, 30]), "bounded exponential retry");
        var frames = ": comment\r\n\r\nevent: unknown\ndata: not-json\n\nid: ignored\ndata: {}\ndata: another\n\n";
        var events = 0;
        try { await ModelEvents.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(frames)), () => events++, default); }
        catch (IOException) { Check(events == 2, "unknown events invalidate without interpreting payload; multiline dispatches once; EOF disconnects"); }
        var raw = new List<ModelEvent>();
        try { await ModelEvents.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(frames)), raw.Add, default); }
        catch (IOException) { Check(raw[0].Name == "unknown" && raw[0].Data == "not-json" && raw[1].Data == "{}\nanother", "reusable transport retains event names and multiline data for build-specific adapters"); }
        try { await ModelEvents.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes("data: " + new string('x', 1024 * 1024))), () => { }, default); throw new Exception("unbounded frame"); }
        catch (FormatException) { Check(true, "oversized unterminated SSE line bounded"); }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await ModelEvents.ReadAsync(new MemoryStream([1]), () => { }, canceled.Token); throw new Exception("not canceled"); }
            catch (OperationCanceledException) { Check(true, "SSE read cancellation"); }
        }
        var root = Path.Combine("D:/tmp", "coquena-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var manager = new ConfigurationManager(new Home(Path.Combine(root, "home")));
        Check(manager.Observer() == preferences, "missing preference keeps defaults");
        manager.SetObserver(new(2, 8));
        Check(new ConfigurationManager(new Home(Path.Combine(root, "home"))).Observer() == new ObserverPreferences(2, 8), "global preferences roundtrip in AOT");
        manager.SetObserver(null); Check(manager.Observer() == preferences, "unset restores defaults");
        try { manager.SetObserver(new(5, 2)); throw new Exception("invalid retry accepted"); }
        catch (ContractException) { Check(true, "invalid persisted retry rejected"); }

        File.WriteAllText(Path.Combine(root, "coquena.yaml"), "name: fixture\nservice:\n  backend:\n    version: latest\n    variant: cpu\n");
        var store = new ScopeStore(ScopeContext.Project(Path.Combine(root, "coquena.yaml")));
        var service = new RecipeRegistry(store).EnsureProjectService(["coquena.yaml"], "fixture");
        var lifecycle = new ScopedServiceProcess(store);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var plan = new ScopedStartPlan(Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe"), ["--scoped-health-child", port.ToString()],
            Path.Combine(root, "fixture.log"), "fixture", new JsonObject(), new JsonArray(),
            new JsonObject { ["host"] = "127.0.0.1", ["port"] = port }, new Uri($"http://127.0.0.1:{port}/health"),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
        var first = await lifecycle.StartAsync(service.ServiceId, plan);
        var handler = new Handler(); using var client = new HttpClient(handler);
        var observer = new ServiceObservation(store, client);
        var original = await observer.CaptureAsync(service);
        var catalogs = handler.Catalogs;
        var cached = await observer.CaptureAsync(service, reconcileCatalog: false);
        Check(handler.Catalogs == catalogs && cached.Entries[0].ObservedAt == original.Entries[0].ObservedAt && !cached.CompositionCurrent,
            "catalog reuse preserves timestamp without confirming current composition");
        await observer.CaptureAsync(service);
        Check(handler.Catalogs == catalogs + 1, "explicit invalidation reconciles catalog");
        var samples = new ConcurrentQueue<WatchSnapshot>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var revision = store.Read().Revision;
        var watch = new ServiceWatch(store, service, null, preferences, client: client).RunAsync(samples.Enqueue, stop.Token);
        try
        {
            await Until(() => handler.Streams >= 2 && samples.Count >= 2);
            Check(samples.Any(s => s.Events.Contains("desconectado")), "EOF disconnect is visible; reconnects while process remains alive");
            Check(store.Read().Revision == revision, "watch does not mutate persisted intent/runtime");
            handler.State = "external-change";
            await Until(() => samples.Any(s => s.Snapshot!.Entries.Any(e => e.State == "external-change")));
            Check(true, "periodic snapshots reconcile external changes without inference");
            handler.Unavailable = true;
            await Until(() => samples.Any(s => s.Snapshot!.ApiState == "unavailable"));
            Check(samples.Last().Snapshot!.ProcessState == "alive", "API unavailable is distinct from process termination");
            handler.Unavailable = false;
            handler.NoEvents = true; var attempts = handler.Streams;
            await Until(() => handler.Streams > attempts && samples.Last().Snapshot!.ApiState == "available" && samples.Last().Events.Contains("desconectado"));
            Check(samples.Last().Snapshot!.ProcessState == "alive", "unsupported SSE preserves periodic observation of live API");
            handler.NoEvents = false;
            lifecycle.Stop(service.ServiceId);
            await Until(() => samples.Any(s => s.Snapshot!.ProcessState != "alive"));
            var second = await lifecycle.StartAsync(service.ServiceId, plan with { LogPath = Path.Combine(root, "fixture-second.log") });
            await Until(() => samples.Any(s => s.Snapshot!.InstanceId == second));
            Check(second != first && samples.Any(s => s.Snapshot!.InstanceId == second && s.Transition?.StartsWith(Messages.NewInstanceSpan) == true), "restart separates instance identity and reconnects to same service");
            Check(handler.Requests.All(path => path == "/health" || path == "/models" || path == "/models/sse"), "only observation GET endpoints; no loads or conversations");
            if (product is not null)
            {
                foreach (var flag in new[] { "-w", "--watch" })
                {
                    var start = new System.Diagnostics.ProcessStartInfo(product)
                    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                    start.Environment["COQUENA_HOME"] = Path.Combine(root, "cli-home");
                    foreach (var argument in new[] { "ps", flag, "--service", "self", "--file", Path.Combine(root, "coquena.yaml") }) start.ArgumentList.Add(argument);
                    using var child = System.Diagnostics.Process.Start(start)!;
                    var text = child.StandardOutput.ReadToEndAsync(); var errors = child.StandardError.ReadToEndAsync();
                    try { await Task.Delay(3500); Check(!child.HasExited, "native " + flag + " remains an observer"); }
                    finally { if (!child.HasExited) child.Kill(); await child.WaitForExitAsync(); }
                    var output = await text;
                    Check(output.Split(Messages.ObservationFrame + " |").Length >= 3 && output.Contains("Servicio fixture") && !output.Contains('\u001b'), "native redirected " + flag + " emits multiple snapshots without escapes: " + await errors);
                }
            }
            await stop.CancelAsync();
            try { await watch; } catch (OperationCanceledException) { }
            Check((await new ServiceObservation(store, client).CaptureAsync(service)).ProcessState == "alive", "cancel closes observer and preserves backend");
        }
        finally
        {
            await stop.CancelAsync();
            try { await watch; } catch (OperationCanceledException) { }
            lifecycle.Stop(service.ServiceId);
        }
        if (backend is not null) await Router(backend);
        Console.WriteLine($"Watch checks: {checks}. Local fixtures: {root}");

        async Task Router(string executable)
        {
            var folder = Path.Combine(root, "router"); Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(root, "coquena.yaml"), Path.Combine(folder, "coquena.yaml"));
            var routerStore = new ScopeStore(ScopeContext.Project(Path.Combine(folder, "coquena.yaml")));
            var routerService = new RecipeRegistry(routerStore).EnsureProjectService(["coquena.yaml"], "router");
            var routerLifecycle = new ScopedServiceProcess(routerStore);
            var ini = Path.Combine(folder, "entries.ini");
            var entry = "[idle]\nmodel=" + Path.Combine(folder, "absent.gguf").Replace('\\', '/') + "\nload-on-startup=false\n";
            File.WriteAllText(ini, entry);
            var instance = await routerLifecycle.StartAsync(routerService.ServiceId, plan with
            {
                Executable = executable, Arguments = ["--host", "127.0.0.1", "--port", port.ToString(), "--metrics", "--models-preset", ini],
                LogPath = Path.Combine(folder, "router.log")
            });
            var seen = new ConcurrentQueue<WatchSnapshot>();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var follow = new ServiceWatch(routerStore, routerService, null, preferences).RunAsync(seen.Enqueue, cancellation.Token);
            try
            {
                await Wait(() => seen.Any(s => s.Events.StartsWith("conectado") && s.Snapshot!.Entries.Any(e => e.Name == "idle")));
                Check(true, "real official router SSE connects and reconciles initial catalog");
                File.WriteAllText(ini, entry + entry.Replace("[idle]", "[external]"));
                using var externalClient = new HttpClient();
                using var response = await externalClient.GetAsync($"http://127.0.0.1:{port}/models?reload=1", cancellation.Token);
                response.EnsureSuccessStatusCode();
                await Wait(() => seen.Any(s => s.Snapshot!.Entries.Any(e => e.Name == "external")));
                Check(seen.Last().Snapshot!.Entries.All(e => e.State == "unloaded" && e.Metrics.Count == 0), "external publication appears without observation loading missing weights");
                await cancellation.CancelAsync();
                try { await follow; } catch (OperationCanceledException) { }
                Check((await new ServiceObservation(routerStore).CaptureAsync(routerService)).InstanceId == instance, "cancel of real SSE retains same router instance");
            }
            finally
            {
                await cancellation.CancelAsync();
                try { await follow; } catch (OperationCanceledException) { }
                routerLifecycle.Stop(routerService.ServiceId, () => true, instance);
            }
            async Task Wait(Func<bool> condition)
            {
                while (!condition())
                {
                    if (follow.IsFaulted) await follow;
                    await Task.Delay(100, cancellation.Token);
                }
            }
        }
        async Task Until(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!condition())
            {
                if (watch.IsFaulted) await watch;
                if (DateTime.UtcNow > deadline) throw new Exception("Watch condition timed out.");
                await Task.Delay(100, stop.Token);
            }
        }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Streams, Catalogs; public volatile bool Unavailable, NoEvents; public volatile string State = "unloaded";
        public ConcurrentQueue<string> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; Requests.Enqueue(path);
            if (Unavailable) throw new HttpRequestException("fixture disconnected");
            var text = "{}";
            var type = "application/json";
            if (path == "/models")
            {
                Interlocked.Increment(ref Catalogs);
                text = new JsonObject { ["data"] = new JsonArray((JsonNode)new JsonObject
                    { ["id"] = "entry", ["status"] = new JsonObject { ["value"] = State, ["args"] = new JsonArray() } }) }.ToJsonString();
            }
            if (path == "/models/sse")
            {
                Interlocked.Increment(ref Streams);
                if (NoEvents) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                text = "event: unknown\ndata: {}\n\n"; type = "text/event-stream";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, type) });
        }
    }
}
