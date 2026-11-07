using System.Net;
using System.Text.Json.Nodes;
using Coquena.Contracts;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Servers;

namespace Coquena.Tests;

internal static class InferenceObservationTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-inference-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var recipe = Path.Combine(root, "coquena.yaml");
        File.WriteAllText(recipe, "name: fixture\nservice:\n  backend:\n    executable: ./fixture.exe\n");
        var store = new ScopeStore(ScopeContext.Project(recipe));
        var registry = new RecipeRegistry(store);
        var service = registry.EnsureProjectService(["coquena.yaml"], "fixture");
        registry.PublishActive(service.ServiceId, new JsonObject
        {
            ["service-id"] = service.ServiceId, ["instance-id"] = Guid.NewGuid().ToString("D"),
            ["process"] = new JsonObject { ["pid"] = 100, ["creation-time"] = 1L, ["executable"] = Path.Combine(root, "fixture.exe") },
            ["resources"] = new JsonArray(), ["snapshot"] = new JsonObject(), ["combination"] = "fixture",
            ["endpoint"] = new JsonObject { ["host"] = "127.0.0.1", ["port"] = 9000 }, ["log"] = Path.Combine(root, "fixture.log"),
            ["preset-composition"] = new JsonArray(new JsonObject { ["presets"] = new JsonArray(new JsonObject { ["name"] = "a", ["expected-arguments"] = new JsonArray() }) })
        });
        var probe = new Probe();
        using var handler = new Handler(probe);
        using var client = new HttpClient(handler);
        var observer = new ServiceObservation(store, client, probe);
        var sample = await observer.CaptureAsync(service, observeInference: true);
        var entry = sample.Entries.Single();
        check(entry.Metrics.Count == 1 && entry.Metrics[0].Scope.EndsWith("/child:10/1", StringComparison.Ordinal),
            "entry aggregates identify the verified child generation");
        check(entry.Inference is { Model: "fixture.gguf" } && entry.Inference.Active.Single() is { Context: 128, Speculative: false, PredictLimit: 100 },
            "slot metadata preserves the observed context, speculation flag and prediction limit");
        handler.ReplaceDuringMetrics = true;
        sample = await observer.CaptureAsync(service, observeInference: true);
        entry = sample.Entries.Single();
        check(entry.Metrics.Count == 0 && entry.Inference is null && entry.InferenceDiagnostic?.Contains("hijo cambió") == true,
            "replacement during the metrics query discards mixed-generation metrics and tasks");
        check(handler.ReadOnly, "all inference queries use GET without reload or autoload");
    }

    private sealed class Probe : IInstanceProbe, IObservedInstance
    {
        public long Creation = 1;
        public InstanceCheck Open(InstanceDocument identity) => new("alive", this);
        public bool IsAlive => true;
        public bool OwnsEndpoint(string host, int port) => true;
        public (int Pid, long Creation)? ObserveListener(string host, int port) => (10, Creation);
        public Task<IReadOnlyList<MetricSample>> CaptureAsync(CancellationToken cancellation) => Task.FromResult<IReadOnlyList<MetricSample>>([]);
        public void Dispose() { }
    }

    private sealed class Handler(Probe probe) : HttpMessageHandler
    {
        public bool ReplaceDuringMetrics;
        public bool ReadOnly = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            ReadOnly &= request.Method == HttpMethod.Get && !uri.Query.Contains("reload") &&
                (uri.AbsolutePath is "/health" or "/models" || uri.Query.Contains("autoload=false"));
            var body = uri.AbsolutePath switch
            {
                "/models" => "{\"data\":[{\"id\":\"a\",\"status\":{\"value\":\"loaded\",\"args\":[\"--host\",\"127.0.0.1\",\"--port\",\"9001\"]}}]}",
                "/props" => "{\"build_info\":\"b12058-3f8af15a7\",\"model_path\":\"fixture.gguf\"}",
                "/slots" => "[{\"id\":0,\"is_processing\":true,\"id_task\":1,\"n_prompt_tokens_processed\":10,\"n_prompt_tokens_cache\":2,\"next_token\":[{\"n_decoded\":3}],\"n_ctx\":128,\"speculative\":false,\"params\":{\"n_predict\":100}}]",
                "/metrics" => "llamacpp:tokens_predicted_total 100\n",
                _ => "{}"
            };
            if (uri.AbsolutePath == "/metrics" && ReplaceDuringMetrics) probe.Creation++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
