using Coquena.Backends;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class BackendAliasTests
{
    public static async Task Run(string root, string? product, Action<bool, string> check)
    {
        check(!ResourceSelector.Parse("backend://cuda@13").Matches("backend", "backend", null, null, alias: "cuda@13", projectName: "project") &&
            ResourceSelector.Parse("backend://project/cuda@13").Matches("backend", "backend", null, null, alias: "cuda@13", projectName: "project"), "qualified alias disambiguates names reserved for official variants");
        var folder = Path.Combine(root, "backend-aliases"); Directory.CreateDirectory(folder);
        var home = new Home(Path.Combine(folder, "home")); var global = ScopeContext.Global(home);
        using var http = new HttpClient(new NoNetwork()); using var transport = new OfficialArtifactTransport();
        var hardware = new HardwareContext("win-x64", "fixture", null, [], null, [], new("unknown", ""), new("unknown", ""));
        ScopedCombinationResources Provider(ScopeContext context) => new(context, hardware, new(http), transport, new Quiet());
        string? firstProject = null;
        foreach (var name in new[] { "project-one", "project-two" })
        {
            var project = Path.Combine(folder, name); Directory.CreateDirectory(project);
            var yaml = Path.Combine(project, "coquena.yaml");
            var executable = Path.Combine(project, "backend.exe"); File.WriteAllText(executable, "external fixture, never executed");
            var text = $"name: {name}\ntarget: self\nservice:\n  backend:\n    name: my-downloaded\n    executable: ./backend.exe\n  server:\n    args:\n      port: 29991\n";
            File.WriteAllText(yaml, text);
            var context = ScopeContext.Project(yaml); var preparation = new CombinationPreparation(context);
            using (var unpreparedOutput = new StringWriter())
            using (var unpreparedError = new StringWriter())
                check(Cli.Run(["verify", "--base", project, "--select", "backend://my-downloaded"], unpreparedOutput, unpreparedError) == 6 && unpreparedOutput.ToString().Contains("no-reference"),
                    "unprepared external alias is known but cannot be certified without a baseline");
            var prepared = await preparation.PrepareAsync(new(project), Provider(context));
            var backend = prepared.Resources.Single();
            await new ResourceVerification(context).SetBaselineAsync(backend.ResourceId, backend.InstallationId);
            using var output = new StringWriter(); using var error = new StringWriter();
            check(Cli.Run(["verify", "--base", project, "--select", "backend://my-downloaded"], output, error) == 0, "external backend alias can be verified in its project");
            check(Cli.Run(["verify", "--base", project, "--select", "backend://" + name + "/my-downloaded"], output, error) == 0, "qualified backend alias remains relative to the selected project");
            check(Cli.Run(["verify", "--base", project, "--select", "backend://another-project/my-downloaded"], output, error) == 2, "qualification never discovers a different project");
            output.GetStringBuilder().Clear();
            check(Cli.Run(["update", "--base", project, "--select", "backend://my-downloaded"], output, error) == 0 && output.ToString().Contains("not-applicable"), "external alias is selectable but never updated as managed");
            var counting = new Counting(Provider(context));
            File.WriteAllText(yaml, text.Replace("my-downloaded", "renamed-alias"));
            var renamed = await preparation.PrepareAsync(new(project), counting);
            check(counting.Exact && renamed.Resources.Single().InstallationId == backend.InstallationId, "changing only alias retains the exact physical backend resolution");
            File.WriteAllText(yaml, text.Replace("    name: my-downloaded\n", ""));
            await preparation.PrepareAsync(new(project), Provider(context));
            check(Cli.Run(["verify", "--base", project, "--select", "backend://" + name], output, error) == 0, "recipe name is the backend alias when no independent name is declared");
            File.WriteAllText(yaml, text);
            await preparation.PrepareAsync(new(project), Provider(context));
            var imported = await new RecipeImporter(global).ImportAsync(new(project), Provider(global));
            check(imported.Confirmed, "named external backend imports without running or adopting its executable");
            firstProject ??= project;
        }
        var registry = new RecipeRegistry(new(global)); var importer = new RecipeImporter(global);
        foreach (var service in registry.Services())
        {
            var recipe = RecipeReader.Read(importer.ServiceSelection(service.ServiceId));
            check(recipe.Root.Get("service")!.Get("backend")!.Get("name")!.Text == "my-downloaded" && recipe.Name == service.Name, "import preserves backend alias and recipe qualification");
        }
        foreach (var resource in new ScopedBackends(global).List())
            foreach (var installation in resource!["installations"]!.AsArray())
                await new ResourceVerification(global).SetBaselineAsync(resource["resource-id"]!.GetValue<string>(), installation!["installation-id"]!.GetValue<string>());
        var previous = Environment.GetEnvironmentVariable("COQUENA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("COQUENA_HOME", home.Root);
            using var output = new StringWriter(); using var error = new StringWriter();
            var revision = new ScopeStore(global).Read().Revision;
            check(Cli.Run(["verify", "--global", "--select", "backend://my-downloaded"], output, error) == 2 && error.ToString().Contains("ambiguous-resource-selector") && new ScopeStore(global).Read().Revision == revision,
                "ambiguous global alias is rejected before auditing either backend");
            check(Cli.Run(["update", "--global", "--select", "backend://my-downloaded"], output, error) == 2 && new ScopeStore(global).Read().Revision == revision,
                "ambiguous global alias is rejected before querying or replacing a plan");
            output.GetStringBuilder().Clear();
            check(Cli.Run(["verify", "--global", "--select", "backend://project-one/my-downloaded"], output, error) == 0 && output.ToString().Contains("verified"), "qualified global alias verifies one imported external backend");
            output.GetStringBuilder().Clear();
            check(Cli.Run(["verify", "--base", firstProject!, "--select", "backend://my-downloaded"], output, error) == 0, "global name collisions do not affect project-relative selection");
            if (product is not null)
                foreach (var args in new[] {
                    new[] { "verify", "--base", firstProject!, "--select", "backend://my-downloaded" },
                    new[] { "verify", "--global", "--select", "backend://project-one/my-downloaded" },
                    new[] { "update", "--global", "--select", "backend://project-one/my-downloaded" } })
                {
                    var start = new System.Diagnostics.ProcessStartInfo(product) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    start.Environment["COQUENA_HOME"] = home.Root;
                    foreach (var arg in args) start.ArgumentList.Add(arg);
                    using var process = System.Diagnostics.Process.Start(start)!;
                    var stdout = await process.StandardOutput.ReadToEndAsync(); var stderr = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
                    check(process.ExitCode == 0 && stderr.Length == 0 && stdout.Contains(args[0] == "update" ? "not-applicable" : "verified"), "published product resolves backend aliases in project and imported global scopes");
                }
        }
        finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previous); }
    }
    private sealed class Counting(ICombinationResources inner) : ICombinationResources
    {
        public bool Exact;
        public Task<PreparationResource> PrepareAsync(PreparationRequest request, System.Text.Json.Nodes.JsonObject? exact, CancellationToken cancellation)
        { Exact = exact is not null; return inner.PrepareAsync(request, exact, cancellation); }
    }
    private sealed class Quiet : IModelSelectionInteraction
    {
        public bool IsInteractive => false;
        public void Show(string text) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation) => throw new Exception("Unexpected interaction");
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => throw new Exception("Unexpected network request for external backend");
    }
}
