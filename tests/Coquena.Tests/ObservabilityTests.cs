using System.Net;
using Coquena.Contracts;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ObservabilityTests
{
    public static void Run(Home home, ServerRuntime runtime, InstanceDocument instance)
    {
        var checks = 0; var calls = new List<string>();
        var mode = "normal";
        using var client = new HttpClient(new Handler(async (request, token) =>
        {
            calls.Add(request.RequestUri!.PathAndQuery);
            if (mode == "timeout") { await Task.Delay(Timeout.Infinite, token); }
            if (mode == "http") return new(HttpStatusCode.ServiceUnavailable);
            var models = request.RequestUri.AbsolutePath == "/models";
            if (mode == "multi") return new(HttpStatusCode.OK) { Content = new StringContent(models ? "{\"data\":[{\"id\":\"chat\",\"status\":{\"value\":\"loaded\"}},{\"id\":\"chat+two\",\"status\":{\"value\":\"loaded\"}}]}" :
                request.RequestUri.Query.Contains("chat%2Btwo") ? "llamacpp:predicted_tokens_seconds 99" : "llamacpp:predicted_tokens_seconds 7") };
            var text = models ? mode == "bad-json" ? "{}" : mode == "unloaded" ? "{\"data\":[{\"id\":\"chat\",\"status\":{\"value\":\"unloaded\"}}]}" : "{\"data\":[{\"id\":\"chat\",\"status\":{\"value\":\"loaded\"}}]}" :
                mode == "invalid" ? "llamacpp:prompt_tokens_seconds NaN" : "# TYPE llamacpp:prompt_tokens_seconds gauge\nllamacpp:prompt_tokens_seconds 12.5\nllamacpp:predicted_tokens_seconds 7\n";
            return new(HttpStatusCode.OK) { Content = new StringContent(text) };
        }));
        using var output = new StringWriter(); runtime.Ps(output, client);
        Check(output.ToString().Contains(instance.InstanceId.ToString()) && output.ToString().Contains("12.5") && output.ToString().Contains("=7"), "verified instance and throughput");
        Check(calls.SequenceEqual(new[] { "/models", "/metrics?model=chat&autoload=false" }), "router model and autoload disabled");
        var iniPath = home.PathFor(instance.IniPath); var ini = File.ReadAllText(iniPath);
        try
        {
            File.AppendAllText(iniPath, "\n[chat+two]\n");
            mode = "multi"; calls.Clear(); output.GetStringBuilder().Clear(); runtime.Ps(output, client);
            Check(output.ToString().Contains("Modelo chat: llamacpp:predicted_tokens_seconds=7") && output.ToString().Contains("Modelo chat+two: llamacpp:predicted_tokens_seconds=99"), "rates isolated by model");
            Check(calls.Contains("/metrics?model=chat%2Btwo&autoload=false"), "model URL escaped");
        }
        finally { File.WriteAllText(iniPath, ini); }
        foreach (var failure in new[] { "invalid", "bad-json", "http", "timeout" })
        {
            mode = failure; output.GetStringBuilder().Clear(); runtime.Ps(output, client);
            Check(output.ToString().Contains(instance.InstanceId.ToString()) && output.ToString().Contains("no disponibles") && !output.ToString().Contains("=0"), "failure retains instance without numbers: " + failure);
        }
        mode = "unloaded"; calls.Clear(); output.GetStringBuilder().Clear(); runtime.Ps(output, client);
        Check(calls.Count == 1 && output.ToString().Contains("no cargado") && !output.ToString().Contains("=0"), "unloaded does not query metrics");
        var store = new LocalStore(home); var json = ContractJsonContext.Default;
        store.Update(DocumentKind.Instance, json.InstanceDocument, ContractValidation.Instance, _ => instance with { Metrics = false }, server: "default");
        calls.Clear(); output.GetStringBuilder().Clear(); runtime.Ps(output, client);
        Check(calls.Count == 0 && output.ToString().Contains("desactivadas"), "disabled avoids HTTP");
        store.Update(DocumentKind.Instance, json.InstanceDocument, ContractValidation.Instance, _ => instance, server: "default");
        store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config, old => old! with { LogRetention = new(1) });
        var obsolete = home.PathFor($"logs/default/{Guid.NewGuid():D}.log"); File.WriteAllText(obsolete, "obsolete");
        output.GetStringBuilder().Clear(); runtime.Logs(null, false, output);
        Check(!File.Exists(obsolete) && File.Exists(home.PathFor(instance.LogPath)) && output.ToString().Contains("protegidos") && output.ToString().Contains("ready"), "retention removes old log and protects active oversized log");
        var polls = 0; runtime.Logs(null, true, TextWriter.Null, () => ++polls > 2);
        Check(runtime.Recover("default")?.InstanceId == instance.InstanceId, "cancel follow keeps server alive");
        store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config, old => old! with { LogRetention = null });
        try
        {
            store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config, old => old! with { LogRetention = new(0) });
            throw new Exception("expected invalid retention");
        }
        catch (ContractException) { checks++; }
        Check(ServerRuntime.ParseMetrics("llamacpp:prompt_tokens_seconds{model=\"chat two\"} 2.5 123")["llamacpp:prompt_tokens_seconds"] == 2.5, "valid labels and timestamp");
        foreach (var bad in new[] { "bogus", "llamacpp:prompt_tokens_seconds Infinity", "llamacpp:prompt_tokens_seconds -1", "llamacpp:prompt_tokens_seconds 1\nllamacpp:prompt_tokens_seconds 2", "llamacpp:prompt_tokens_seconds{broken 1", "llamacpp:prompt_tokens_seconds 1 invalid-timestamp" })
        {
            try { ServerRuntime.ParseMetrics(bad); throw new Exception("expected invalid metrics"); }
            catch (FormatException) { checks++; }
        }
        Console.WriteLine($"OK: {checks} comprobaciones de métricas y retención.");
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Observability test: " + message); }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
