using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

// Interactive acceptance fixture. Its isolated scopes contain no user services.
internal static class DashboardFixture
{
    public static async Task Run(string root, bool synthetic)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new InvalidOperationException("Use a new fixture directory.");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("COQUENA_HOME", Path.Combine(root, "home"));
        var recipe = Path.Combine(root, "coquena.yaml");
        File.WriteAllText(recipe, "name: project-fixture\nservice:\n  backend:\n    variant: cpu\n    version: latest\n");
        Directory.SetCurrentDirectory(root);
        var project = new ScopeStore(ScopeContext.Project(recipe));
        var projectService = new RecipeRegistry(project).EnsureProjectService(["coquena.yaml"], "project-fixture");
        var global = new ScopeStore(ScopeContext.Global(Home.Resolve()));
        var registry = new RecipeRegistry(global);
        var globalService = registry.RegisterService(registry.Register(["global.yaml"], "global-fixture").RecipeId);
        registry.SetDefaultService(globalService.ServiceId);
        var slowService = registry.RegisterService(registry.Register(["slow.yaml"], "slow-fixture").RecipeId);
        var started = new List<(ScopedServiceProcess Lifecycle, string Id, string Instance)>();
        try
        {
            await Start(global, globalService);
            await Start(global, slowService, slow: true);
            await Start(project, projectService);
            if (synthetic)
            {
                var context = new HardwareContext("fixture", "CPU simulada", 8192UL * 1024 * 1024, [], null, [],
                    new("available", "Sondeo simulado"), new("available", "Sondeo simulado"));
                var adapters = new HardwareProbe.AdapterEvidence(context, ["fixture-a"], ["fixture-b"]);
                var metrics = new FixtureMetrics();
                DashboardCli.RunDashboard(metrics, adapters);
                Console.WriteLine($"Fixture captures {metrics.Captures}; concurrent {metrics.MaximumConcurrent}; " +
                    $"longest {metrics.LongestCapture.TotalMilliseconds:0} ms; peak RAM {Process.GetCurrentProcess().PeakWorkingSet64} bytes.");
            }
            else DashboardCli.RunDashboard();
        }
        finally
        {
            foreach (var (lifecycle, id, instance) in started) lifecycle.Stop(id, () => true, instance);
        }

        async Task Start(ScopeStore store, ServiceIdentity service, bool slow = false)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var lifecycle = new ScopedServiceProcess(store);
            var executable = Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe");
            var arguments = new List<string> { "--scoped-health-child", "--port", port.ToString() };
            if (slow) arguments.Add("--slow-after-first-request");
            var plan = new ScopedStartPlan(executable, arguments.ToArray(), Path.Combine(root, service.Name + ".log"),
                "fixture", new JsonObject(), [], new JsonObject { ["host"] = "127.0.0.1", ["port"] = port },
                new Uri($"http://127.0.0.1:{port}/health"), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
            var instance = await lifecycle.StartAsync(service.ServiceId, plan);
            started.Add((lifecycle, service.ServiceId, instance));
        }
    }

    private sealed class FixtureMetrics : ISystemMetrics
    {
        private int active;
        private DateTimeOffset deviceDate;
        public int Captures { get; private set; }
        public int MaximumConcurrent { get; private set; }
        public TimeSpan LongestCapture { get; private set; }

        public async Task<HardwareSnapshot> CaptureAsync(CancellationToken cancellation = default)
        {
            var start = DateTimeOffset.UtcNow;
            var clock = Stopwatch.StartNew();
            Captures++;
            MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref active));
            try
            {
                // Exercise slow and failed captures, then recovery, without overlapping.
                await Task.Delay(Captures % 12 == 0 ? 6000 : 250, cancellation);
                if (Captures % 12 == 9) throw new UnauthorizedAccessException("Permiso insuficiente simulado.");
                var now = DateTimeOffset.UtcNow;
                if (Captures % 6 == 1) deviceDate = now;
                var memory = 1024d * 1024 * 1024;
                return new("fixture · valores simulados", start, now,
                    [new("cpu.utilization", 25, "%", "fixture", "machine", now, now - start),
                     new("ram.used", 2 * memory, "bytes", "fixture", "machine", now),
                     new("ram.total", 8 * memory, "bytes", "fixture", "machine", now)],
                    [Device("fixture-a", now, 40, memory), Device("fixture-b", deviceDate, 60, 2 * memory),
                     Device("unconfirmed", now, 90, memory)], []);

                DeviceMetrics Device(string id, DateTimeOffset at, double load, double used) => new(id, id,
                    [new("gpu.utilization", load, "%", "fixture", "device:" + id, at),
                     new("vram.used", used, "bytes", "fixture", "device:" + id, at),
                     new("vram.total", 4 * memory, "bytes", "fixture", "device:" + id, at),
                     new("gpu.shared", null, "bytes", "fixture", "device:" + id, at,
                         UnavailableReason: "Permiso insuficiente simulado.")]);
            }
            finally
            {
                if (clock.Elapsed > LongestCapture) LongestCapture = clock.Elapsed;
                Interlocked.Decrement(ref active);
            }
        }
    }
}
