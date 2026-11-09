using Coquena.Models;
using Coquena.Projects;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class DashboardProjectTests
{
    // Prepares an isolated interactive recipe without starting a backend. The
    // explicit INI argument opts this known external build into router mode.
    public static void PrepareFixture(string root, string backend, string model)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new ArgumentException("Use a new fixture directory.");
        if (!File.Exists(backend) || !File.Exists(model)) throw new FileNotFoundException("Local fixture resources are required.");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "coquena.yaml");
        File.WriteAllText(file, "name: news\n");
        var store = new ScopeStore(ScopeContext.Project(file));
        var service = new RecipeRegistry(store).EnsureProjectService([file], "news");
        var ini = new PresetPublication(store, store, service.ServiceId).IniPath;
        using var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        socket.Start();
        var port = ((System.Net.IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        string Quote(string path) => "'" + Path.GetFullPath(path).Replace('\\', '/').Replace("'", "''") + "'";
        File.WriteAllText(file, $"""
            name: news
            target: self
            service:
              backend:
                executable: {Quote(backend)}
              server:
                args:
                  host: 127.0.0.1
                  port: {port}
                  models-preset: {Quote(ini)}
                  metrics: true
                  slots: true
                health:
                  url: http://127.0.0.1:{port}/health
                  timeout-seconds: 20
            models:
              tiny:
                files: [{Quote(model)}]
            presets:
              summary:
                args:
                  model: model://tiny
                  n-gpu-layers: 0
                  ctx-size: 1024
                  threads: 1
                  threads-batch: 1
                  parallel: 2
            """);
        Console.WriteLine("Project fixture prepared; no process started. Port: " + port);
    }

    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine("D:/tmp", "coquena-dashboard-project-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "folder-name");
        Directory.CreateDirectory(directory);
        var home = new Home(Path.Combine(root, "home"));
        var file = Path.Combine(directory, "coquena.yaml");
        const string definition = "name: news\ntarget: self\nservice:\n  backend:\n    executable: ./absent.exe\n";
        File.WriteAllText(file, definition);
        var target = DashboardCli.Discover(out _, directory, home).Single();
        check(target.Label == "◇ news" && target.DeclaresService && target.Service is null && target.Destination == "self",
            "fresh recipe has a local project tab and a declared, not yet registered own service");
        check(!Directory.Exists(Path.Combine(directory, ".coquena")), "discovering a recipe does not prepare or register runtime state");
        File.WriteAllText(file, "presets: {}\n");
        target = DashboardCli.Discover(out _, directory, home).Single();
        check(target.Label == "◇ folder-name" && !target.DeclaresService && target.Service is null && target.Destination == "global",
            "consumer-only recipe uses folder fallback and keeps its own tab without inventing a service");
        var globals = new RecipeRegistry(new ScopeStore(ScopeContext.Global(home)));
        var globalService = globals.RegisterService(globals.Register(["global.yaml"], "shared").RecipeId);
        globals.SetDefaultService(globalService.ServiceId);
        var tabs = DashboardCli.Discover(out _, directory, home);
        check(tabs.Count == 2 && tabs[0].Label == "◎ ★ shared" && tabs[1].Label == "◇ folder-name",
            "global default precedes a consumer-only project tab");
        File.WriteAllText(file, definition);
        var previousHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
        Environment.SetEnvironmentVariable("COQUENA_HOME", home.Root);
        try
        {
            var decisions = new Decisions();
            var starts = 0;
            var frontend = new PublicationCli.Frontend(new NoModels(), (args, _, _) =>
            {
                starts++;
                check(args.Contains("self") && args.Contains(Path.GetFullPath(directory)), "visual start receives the own service and explicit project directory");
                return Task.FromResult(93);
            }, CancellationToken.None);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var arguments = new[] { "up", "--project-directory", directory, "--file", file };
            check(PublicationCli.Execute(arguments, output, error, decisions, frontend) == 8 && starts == 0,
                "cancelling a stopped target confirmation does not invoke a visual start");
            decisions.AllowStart = true;
            check(PublicationCli.Execute(arguments, output, error, decisions, frontend) == 93 && starts == 1,
                "up dispatches to the visual service operation without falling back to console input");
            check(decisions.LastStart?.Contains("news") == true && decisions.LastStart.Contains("proyecto"),
                "start confirmation identifies the actual target and scope");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            check(PublicationCli.Execute(arguments, output, error, decisions, frontend with { Cancellation = cancelled.Token }) == 130 && starts == 1,
                "closing a visual operation cancels before another start");

            var own = new ScopeStore(ScopeContext.Project(file));
            var registry = new RecipeRegistry(own);
            var consumer = registry.EnsureProjectConsumer([file], "news");
            var service = registry.FindProjectService([file]);
            var publication = new PresetPublication(own, own, service.ServiceId);
            await publication.PublishAsync(consumer.RecipeId, [new("news/summary", "[news/summary]\nmodel=absent.gguf\n", [], [], false)],
                "fixture", "news", true, true, _ => true, null);
            var discovered = DashboardCli.Discover(out _, directory, home).Last();
            check(discovered.Service?.ServiceId == service.ServiceId && discovered.Label == "◇ news",
                "project tab attaches to its registered own service after an explicit action");
            File.WriteAllText(file, "invalid: edited\n");
            check(DashboardCli.Discover(out _, directory, home).Last() is { Recipe: not null, RecipeProblem: not null },
                "invalid edited YAML leaves the project tab available for recorded withdrawal");
            var withdrawn = PublicationCli.Execute(["down", "--project-directory", directory], output, error, decisions, frontend);
            check(withdrawn == 0 &&
                !File.ReadAllText(publication.IniPath).Contains("[news/summary]"),
                "visual down withdraws the recorded composition with an edited YAML and no console or start: " + withdrawn + " · " + error);
        }
        finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previousHome); }
    }

    private sealed class Decisions : IPublicationInteraction
    {
        public bool Available => true;
        public bool AllowStart { get; set; }
        public string? LastStart { get; private set; }
        public bool Confirm(string _) => true;
        public bool StartService() => AllowStart;
        public bool StartService(string diagnostic) { LastStart = diagnostic; return AllowStart; }
    }

    private sealed class NoModels : IModelSelectionInteraction
    {
        public bool IsInteractive => true;
        public void Show(string _) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> _, CancellationToken cancellation) =>
            throw new Exception("This fixture must not select or download models.");
    }
}
