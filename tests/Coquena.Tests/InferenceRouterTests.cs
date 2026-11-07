using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Servers;

namespace Coquena.Tests;

internal static class InferenceRouterTests
{
    public static async Task Run(string backend, string model, string root)
    {
        backend = Path.GetFullPath(backend);
        model = Path.GetFullPath(model).Replace('\\', '/');
        root = Path.GetFullPath(root);
        if (!File.Exists(model)) throw new FileNotFoundException("Missing GGUF fixture.", model);
        Directory.CreateDirectory(root);
        if (Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidOperationException("Use an empty fixture directory.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        var recipe = Path.Combine(root, "coquena.yaml");
        File.WriteAllText(recipe, "name: fixture\nservice:\n  backend:\n    version: latest\n    variant: cpu\n");
        var store = new ScopeStore(ScopeContext.Project(recipe));
        var service = new RecipeRegistry(store).EnsureProjectService(["coquena.yaml"], "fixture");
        var names = new[] { "fixture/a", "fixture/b" };
        var presets = names.Select(name => new PublishedPreset(name,
            $"[{name}]\nmodel={model}\nload-on-startup=true\nctx-size=1024\ncontext-shift=true\nparallel=2\nthreads=1\nthreads-batch=1\nn-gpu-layers=0\n",
            [], [], false)).ToArray();
        var publication = new PresetPublication(store, store, service.ServiceId);
        await publication.PublishAsync(service.RecipeId, presets, "fixture", "fixture", stopped: true,
            interactive: true, confirm: _ => true, router: null, cancellation: token);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var address = new Uri($"http://127.0.0.1:{port}");
        var plan = new ScopedStartPlan(backend, ["--host", "127.0.0.1", "--port", port.ToString(), "--metrics", "--slots"],
            Path.Combine(root, "router.log"), "fixture", new JsonObject(), [],
            new JsonObject { ["host"] = "127.0.0.1", ["port"] = port }, new Uri(address, "/health"),
            TimeSpan.FromSeconds(25), TimeSpan.FromMilliseconds(100), PresetIni: true);
        var lifecycle = new ScopedServiceProcess(store);
        var instance = await lifecycle.StartAsync(service.ServiceId, plan, token);
        try
        {
            using var http = new HttpClient { BaseAddress = address };
            var observer = new ServiceObservation(store);
            var tracker = new InferenceTasks(10);
            async Task Capture() => tracker.Observe(await observer.CaptureAsync(service, cancellation: token, observeInference: true));
            await Capture();
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
            var jobs = new[] { Request(names[0], cancelled.Token), Request(names[0], token), Request(names[1], token) };
            var concurrent = false;
            var measured = false;
            while (!jobs.All(job => job.IsCompleted))
            {
                await Capture();
                concurrent |= tracker.Active.Count(task => task.Identity.Entry == names[0]) == 2 &&
                    tracker.Active.Any(task => task.Identity.Entry == names[1]);
                measured |= tracker.Active.Where(task => task.OutputRate is > 0).Select(task => task.Identity.Entry)
                    .Distinct(StringComparer.Ordinal).Count() == 2;
                if (concurrent && measured && !jobs[0].IsCompleted) await cancelled.CancelAsync();
            }
            await Task.WhenAll(jobs);
            var drainUntil = DateTimeOffset.UtcNow.AddSeconds(5);
            do { await Capture(); } while (tracker.Active.Any() && DateTimeOffset.UtcNow < drainUntil);
            Require(concurrent, "two simultaneous slots in one preset and a third in another preset of the same GGUF");
            Require(measured, "independent generation rates measured in both presets");
            Require(!tracker.Active.Any() && tracker.History.Select(task => task.Identity.Entry).Distinct().Count() == 2,
                "ended requests are removed from active and retain last measurements by preset");
            Require(cancelled.IsCancellationRequested, "controlled client cancellation exercised");
            Console.WriteLine("Real inference checks: 4.");

            async Task Request(string name, CancellationToken cancellation)
            {
                try
                {
                    var request = new JsonObject
                    {
                        ["model"] = name, ["prompt"] = "Once upon a time", ["n_predict"] = 4000, ["ignore_eos"] = true
                    };
                    using var body = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
                    using var response = await http.PostAsync("/completion", body, cancellation);
                    response.EnsureSuccessStatusCode();
                }
                catch (OperationCanceledException) when (cancellation == cancelled.Token && cancelled.IsCancellationRequested && !token.IsCancellationRequested) { }
            }
        }
        finally { lifecycle.Stop(service.ServiceId, () => true, instance); }
    }

    private static void Require(bool condition, string evidence)
    {
        if (!condition) throw new InvalidOperationException(evidence);
        Console.WriteLine("OK: " + evidence);
    }
}
