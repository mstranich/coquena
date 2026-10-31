using System.Diagnostics;
using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class RecipeImportRuntimeTests
{
    public static async Task Run(string backend, string product, string root)
    {
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new ArgumentException("Fixture directory must be empty.");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        var home = new Home(Path.Combine(root, "home")); var context = ScopeContext.Global(home); var store = new ScopeStore(context);
        using var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        portProbe.Start(); var port = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        var weight = Path.Combine(source, "fixture.gguf");
        using (var writer = new BinaryWriter(File.Create(weight))) { writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(0ul); }
        var yaml = Path.Combine(source, "coquena.yaml");
        var backendYaml = backend.Replace('\\', '/').Replace("'", "''");
        var content = $"name: imported\nservice:\n  backend:\n    executable: '{backendYaml}'\n  server:\n    args:\n      host: 127.0.0.1\n      port: {port}\n    health:\n      url: http://127.0.0.1:{port}/health\nmodels:\n  local:\n    files: [./fixture.gguf]\npresets:\n  chat:\n    args:\n      model: model://local\n      ctx-size: 512\n";
        File.WriteAllText(yaml, content);
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        async Task<int> Product(params string[] args)
        {
            var start = new ProcessStartInfo(product) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.Environment["COQUENA_HOME"] = home.Root;
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            var text = await output + await error;
            File.AppendAllText(Path.Combine(root, "native-cli.log"), string.Join(' ', args) + "\n" + text + "\n");
            return process.ExitCode;
        }
        Check(await Product("import", yaml, "--as-default") == 0, "Native AOT product imports complete recipe");
        Check(!store.Read().Documents.Keys.Any(p => p.EndsWith("active.json", StringComparison.Ordinal)), "native import does not start backend");
        var serviceId = new RecipeRegistry(store).ResolveGlobalService("default");
        var started = false;
        try
        {
            Check(await Product("service", "start", "-d") == 0, "native product starts imported service independently"); started = true;
            var activePath = $"services/{serviceId}/local/active.json";
            var instance = store.Read().Documents[activePath]["data"]!["instance-id"]!.GetValue<string>();
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
            var router = new LlamaPresetRouter(http, new Uri($"http://127.0.0.1:{port}"));
            Check((await router.ObserveAsync(default)).Any(p => p!["id"]!.GetValue<string>() == "chat"), "official router exposes promoted preset");
            var model = new ScopedModels(context).List().Single()!;
            var bindings = new JsonArray(new JsonObject
            {
                ["resource-id"] = model["resource-id"]!.DeepClone(),
                ["installation-id"] = model["installations"]![0]!["installation-id"]!.DeepClone()
            });
            var registry = new RecipeRegistry(store); var foreign = registry.Register(["foreign.yaml"], "foreign");
            var publication = new PresetPublication(store, store, serviceId);
            var foreignPreset = new PublishedPreset("foreign/p", $"[foreign/p]\nmodel={weight}\nctx-size=256\n", ["--model", weight, "--ctx-size", "256"], bindings, false);
            Check((await publication.PublishAsync(foreign.RecipeId, [foreignPreset], "fixture", "foreign", false, true, _ => true, router)).Confirmed, "foreign consumer confirmed on official router");
            File.WriteAllText(yaml, content.Replace("512", "1024"));
            var intentionBefore = Directory.GetFiles(context.PathFor("consumers"), "recipe.yaml", SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();
            Check(await Product("import", yaml, "--select", "presets", "--replace", "preset:chat") == 3, "headless native active import requires interaction");
            Check(intentionBefore.SequenceEqual(Directory.GetFiles(context.PathFor("consumers"), "recipe.yaml", SearchOption.AllDirectories).Select(File.ReadAllText)), "headless rejection preserves intention");
            using var transport = new OfficialArtifactTransport();
            var provider = new ScopedCombinationResources(context, HardwareProbe.Inspect(), new HuggingFaceStore(http), transport, new NoInteraction());
            var result = await new RecipeImporter(context).ImportAsync(new(source), provider, select: "presets", replace: ["preset:chat"],
                interactive: true, confirm: _ => true, router: _ => router);
            var catalog = await router.ObserveAsync(default);
            Check(result.Confirmed && catalog.Any(p => p!["id"]!.GetValue<string>() == "chat" && p["status"]!["args"]!.AsArray().Any(a => a?.GetValue<string>() == "1024")), "interactive import confirms actual reload on official router");
            Check(catalog.Any(p => p!["id"]!.GetValue<string>() == "foreign/p"), "actual import preserves foreign consumer");
            Check(store.Read().Documents[activePath]["data"]!["instance-id"]!.GetValue<string>() == instance, "actual import does not restart active instance");
            Check(File.ReadAllText(yaml) == content.Replace("512", "1024") && File.Exists(weight) && Directory.GetFiles(context.Root, "*.gguf", SearchOption.AllDirectories).Length == 0, "native roundtrip leaves source and external weights intact");
            var activeBackend = store.Read().Documents[activePath]["data"]!["resources"]!.AsArray().Single(r => r!["kind"]!.GetValue<string>() == "backend")!["resource-id"]!.GetValue<string>();
            var inactiveBackend = Path.Combine(source, "candidate.exe"); File.WriteAllText(inactiveBackend, "must never execute during import");
            File.WriteAllText(yaml, content.Replace("name: imported", "name: renamed").Replace(backendYaml, inactiveBackend.Replace('\\', '/')));
            Check(await Product("import", yaml, "--select", "service", "--replace", "service:imported") == 0, "headless service-only import changes definition without publishing or starting");
            Check(registry.Services().All(s => s.Name != "renamed") && store.Read().Documents[activePath]["data"]!["instance-id"]!.GetValue<string>() == instance &&
                store.Read().Documents[activePath]["data"]!["resources"]!.AsArray().Any(r => r!["resource-id"]!.GetValue<string>() == activeBackend), "registered name and active backend revision remain protected");
        }
        finally { if (started) Check(await Product("service", "stop") == 0, "fixture service stopped"); }
        File.WriteAllText(Path.Combine(root, "checks.txt"), $"{checks} checks passed. Official router; no model loading or inference requested.\n");
        Console.WriteLine($"Import runtime: {checks} checks passed. No inference requested. Local evidence: {root}");
    }
    private sealed class NoInteraction : IModelSelectionInteraction
    {
        public bool IsInteractive => false;
        public Task<int?> ChooseAsync(IReadOnlyList<string> choices, CancellationToken cancellation) => throw new Exception("Unexpected selection");
        public void Show(string message) { }
    }
}
