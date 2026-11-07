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
        var requestTokens = int.TryParse(Environment.GetEnvironmentVariable("COQUENA_FIXTURE_TOKENS"), out var requested)
            ? Math.Clamp(requested, 4000, 40000) : 4000;
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
        string? instance = null;
        try
        {
            instance = await lifecycle.StartAsync(service.ServiceId, plan, token);
            using var http = new HttpClient { BaseAddress = address };
            var observer = new ServiceObservation(store);
            var tracker = new InferenceTasks(10);
            ServiceSnapshot? latest = null;
            async Task Capture()
            {
                latest = await observer.CaptureAsync(service, cancellation: token, observeInference: true);
                tracker.Observe(latest);
            }
            await Capture();
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
            var jobs = new[] { Request(names[0], cancelled.Token), Request(names[0], token), Request(names[1], token) };
            var concurrent = false;
            var measured = false;
            var metadata = false;
            while (!jobs.All(job => job.IsCompleted))
            {
                await Capture();
                concurrent |= tracker.Active.Count(task => task.Identity.Entry == names[0]) == 2 &&
                    tracker.Active.Any(task => task.Identity.Entry == names[1]);
                measured |= tracker.Active.Where(task => task.OutputRate is > 0).Select(task => task.Identity.Entry)
                    .Distinct(StringComparer.Ordinal).Count() == 2;
                metadata |= tracker.Active.Any(task => task.Model is not null && task.Last.Context > 0 && task.Last.Speculative is not null);
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
            Require(metadata, "verified model identity, slot context and speculation flag exposed without conversation text");
            Require(latest!.Entries.All(entry => entry.Metrics.Any(metric => metric.Name == "llamacpp:n_decode_total") &&
                entry.Metrics.Any(metric => metric.Name == "llamacpp:spec_decode_num_draft_tokens_total")),
                "decode and speculative metrics survive the observer parser in both entries");
            var beforeTokens = latest.Entries.Single(entry => entry.Name == names[0]).Metrics
                .Single(metric => metric.Name == "llamacpp:tokens_predicted_total").Value;
            var progressRequest = new JsonObject
            {
                ["model"] = names[0], ["prompt"] = "Once upon a time there was a little bird who lived in a tree.",
                ["n_predict"] = 64, ["ignore_eos"] = true, ["cache_prompt"] = false,
                ["stream"] = true, ["return_progress"] = true, ["timings_per_token"] = true
            };
            using var progressBody = new StringContent(progressRequest.ToJsonString(), Encoding.UTF8, "application/json");
            using var progressMessage = new HttpRequestMessage(HttpMethod.Post, "/completion") { Content = progressBody };
            using var progressResponse = await http.SendAsync(progressMessage, HttpCompletionOption.ResponseHeadersRead, token);
            progressResponse.EnsureSuccessStatusCode();
            using var progressReader = new StreamReader(await progressResponse.Content.ReadAsStreamAsync(token));
            var promptSeen = false; var generationSeen = false; var endSeen = false;
            while (await progressReader.ReadLineAsync(token) is { } line)
            {
                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                var frame = JsonNode.Parse(line[6..])!;
                promptSeen |= frame["prompt_progress"]?["processed"]?.GetValue<long>() > 0;
                generationSeen |= frame["timings"]?["predicted_n"]?.GetValue<long>() > 0;
                endSeen |= frame["stop"]?.GetValue<bool>() == true;
            }
            Require(promptSeen && generationSeen && endSeen, "controlled stream confirms prompt, generation and completion without attributing that private stream to third-party tasks");
            await Capture();
            var afterTokens = latest!.Entries.Single(entry => entry.Name == names[0]).Metrics
                .Single(metric => metric.Name == "llamacpp:tokens_predicted_total").Value;
            Require(afterTokens - beforeTokens == 64, "entry generation counter agrees with the controlled 64-token response");
            var oldTasks = tracker.History.Select(task => task.Identity).ToArray();
            var backendYaml = backend.Replace('\\', '/').Replace("'", "''");
            var modelYaml = model.Replace("'", "''");
            File.WriteAllText(recipe, $"name: fixture\ntarget: self\nservice:\n  backend:\n    executable: '{backendYaml}'\n  server:\n    health:\n      url: http://127.0.0.1:{port}/health\n    args:\n      host: 127.0.0.1\n      port: {port}\n      models-preset: '{publication.IniPath.Replace('\\', '/').Replace("'", "''")}'\n      metrics: true\n      slots: true\nmodels:\n  local:\n    files: ['{modelYaml}']\npresets:\n  a:\n    args:\n      model: model://local\n      ctx-size: 2048\n      parallel: 2\n      threads: 1\n      threads-batch: 1\n      n-gpu-layers: 0\n      context-shift: true\n      load-on-startup: true\n");
            Command("up");
            Command("down");
            await Capture();
            Require(latest!.Entries.Count == 0, "down removes entries while the observer remains open");
            File.WriteAllText(recipe, File.ReadAllText(recipe).Replace("ctx-size: 2048", "ctx-size: 4096"));
            Command("up");
            var next = Request(names[0], token);
            var replaced = false;
            while (!next.IsCompleted)
            {
                await Capture();
                replaced |= tracker.Active.Any(task => task.Last.Context > 0 &&
                    oldTasks.All(old => old.Pid != task.Identity.Pid || old.Creation != task.Identity.Creation));
            }
            await next;
            await Capture();
            Require(replaced, "YAML edit and up replace the child and expose its actual slot context without inheriting task identity");
            lifecycle.Stop(service.ServiceId, () => true, instance);
            listener.Start();
            var replacementPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            instance = await lifecycle.StartAsync(service.ServiceId, plan with
            {
                Arguments = ["--host", "127.0.0.1", "--port", replacementPort.ToString(), "--metrics", "--slots"],
                Endpoint = new JsonObject { ["host"] = "127.0.0.1", ["port"] = replacementPort },
                HealthUrl = new Uri($"http://127.0.0.1:{replacementPort}/health"),
                LogPath = Path.Combine(root, "router-restarted.log")
            }, token);
            await Capture();
            Require(latest!.InstanceId == instance && oldTasks.All(old => old.Instance != instance),
                "router replacement opens a distinct instance in the same observer");
            Console.WriteLine("Real inference checks: 14.");

            void Command(string action)
            {
                using var output = new StringWriter();
                using var error = new StringWriter();
                var result = PublicationCli.Run([action, "--project-directory", root, "--file", recipe, "--service", "self", "--private"],
                    output, error, new FixtureInteraction());
                File.AppendAllText(Path.Combine(root, "commands.log"), action + "\n" + output + "\n" + error + "\n");
                Require(result == 0, "CLI " + action + " completes against the isolated fixture");
            }

            async Task Request(string name, CancellationToken cancellation)
            {
                try
                {
                    var request = new JsonObject
                    {
                        ["model"] = name, ["prompt"] = "Once upon a time", ["n_predict"] = requestTokens, ["ignore_eos"] = true
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

    private sealed class FixtureInteraction : IPublicationInteraction
    {
        public bool Available => true;
        public bool Confirm(string diagnostic) => true;
        public bool StartService() => true;
    }

    private static void Require(bool condition, string evidence)
    {
        if (!condition) throw new InvalidOperationException(evidence);
        Console.WriteLine("OK: " + evidence);
    }
}
