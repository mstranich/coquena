using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Coquena.Recipes;

namespace Coquena.Tests;

// Opt-in integration evidence. All raw output stays in the caller's local evidence directory.
// No discovery/download, no attachment to an existing router, and no product lifecycle policy.
internal static class RouterEvidenceTests
{
    public static async Task Run(string executable, string output, string? firstModel = null, string? secondModel = null)
    {
        executable = Path.GetFullPath(executable);
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        if (Directory.EnumerateFileSystemEntries(output).Any()) throw new Exception("Evidence directory must be empty.");
        if ((firstModel is null) != (secondModel is null)) throw new Exception("Supply both model paths or neither.");
        foreach (var model in new[] { firstModel, secondModel }.OfType<string>())
            if (!File.Exists(model)) throw new FileNotFoundException("Model fixture is missing.", model);
        using var version = Process.Start(Start(executable, ["--version"]))!;
        var versionOut = version.StandardOutput.ReadToEndAsync();
        var versionErr = version.StandardError.ReadToEndAsync();
        await version.WaitForExitAsync();
        var versionText = await versionOut + await versionErr;
        File.WriteAllText(Path.Combine(output, "version.txt"), versionText);
        if (version.ExitCode != 0 || !versionText.Contains("build 11899, commit c6fb3b5be", StringComparison.Ordinal))
            throw new Exception("This evidence protocol is fixed to official b11899 c6fb3b5be.");
        var checks = new List<string>();
        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            checks.Add(description);
            File.WriteAllLines(Path.Combine(output, "checks.txt"), checks);
            Console.WriteLine("OK: " + description);
        }
        var missing = Path.Combine(output, "missing model.gguf");
        var a = firstModel ?? missing;
        var b = secondModel ?? missing;
        var ini = Path.Combine(output, "presets.ini");
        var codec = new LlamaB11482ArgumentAdapter();
        var origin = new RecipeOrigin("evidence", 1, 1, "$");
        string Preset(string name, string model, int context = 1024, bool startup = false) =>
            codec.Section(name, origin) + codec.Entry("model", Path.GetFullPath(model), origin)
            + codec.Entry("ctx-size", context.ToString(System.Globalization.CultureInfo.InvariantCulture), origin)
            + codec.Entry("load-on-startup", startup ? "true" : "false", origin);
        const string common = "version=1\n[*]\nctx-size=768\nparallel=1\nn-gpu-layers=0\nthreads=2\nthreads-batch=2\nmetrics=true\nstop-timeout=1\n";
        void Write(string text)
        {
            File.WriteAllText(ini + ".new", text, new UTF8Encoding(false));
            File.Move(ini + ".new", ini, true);
        }
        Write("version=1\n");
        using var router = new Router(executable, ini, output, "control", ["--no-models-autoload"]);
        await router.Ready();
        Check((await router.Models()).Count == 0, "empty router starts healthy without model inference");
        Write(common + Preset("consumer-a.chat", a) + Preset("consumer-b.chat", b));
        var entries = await router.Models(true);
        Check(Names(entries).SequenceEqual(new[] { "consumer-a.chat", "consumer-b.chat" }), "reload adds two independent consumer entries");
        Check(Unloaded(entries), "publishing lists presets without loading weights");
        foreach (var endpoint in new[] { "metrics", "slots", "props" })
        {
            var response = await router.Request(HttpMethod.Get, "/" + endpoint + "?model=consumer-a.chat&autoload=false");
            Check(response.Status != 200, endpoint + " on unloaded entry with autoload=false reports unavailable");
        }
        Check(Unloaded(await router.Models()), "observation does not load either entry");
        const string special = "literal \\ path = value 'single' \"double\" ñ 模型";
        Write(common + Preset("consumer-a.chat", a) + codec.Entry("chat-template", special, origin) + Preset("consumer-b.chat", b));
        entries = await router.Models(true);
        var emitted = entries.First(x => x?["id"]?.GetValue<string>() == "consumer-a.chat")!["status"]!["args"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        Check(emitted.Contains(special) && emitted.Contains(Path.GetFullPath(a)), "INI preserves internal spaces, equals, quotes, backslash and Unicode as literal tokens");
        Check(emitted.Contains("1024") && emitted.Contains("--threads") && emitted.Contains("2"), "preset context overrides global context and inherits global thread count");
        var rejected = 0;
        foreach (var unsafeText in new[] { "a;b", "a#b", " padded ", "line\nbreak", "nul\0value" })
        {
            try { codec.Entry("chat-template", unsafeText, origin); }
            catch (RecipeException) { rejected++; }
        }
        Check(rejected == 5, "codec rejects comments, outer whitespace, line breaks and NUL before publication");
        Write(common + Preset("consumer-a.chat", a, 1280) + Preset("consumer-b.chat", b));
        await router.UnobservedReload();
        entries = await router.Models();
        Check(entries.First(x => x?["id"]?.GetValue<string>() == "consumer-a.chat")?["status"]?["args"]?.AsArray().Any(x => x?.GetValue<string>() == "1280") == true,
            "discarded reload response is reconciled by nonmutating query without retrying mutation");
        Write(common + Preset("consumer-a.chat", a, 1536) + Preset("consumer-b.chat", b));
        entries = await router.Models(true);
        Check(Names(entries).Count() == 2 && Unloaded(entries), "reload updates unloaded entry and retains other consumer");
        Write(common + Preset("consumer-b.chat", b));
        Check(Names(await router.Models(true)).SequenceEqual(new[] { "consumer-b.chat" }), "withdraw removes only selected consumer");
        Write(common + Preset("consumer-a.chat", missing) + Preset("consumer-b.chat", b));
        await router.Models(true);
        var failedLoad = await router.Request(HttpMethod.Post, "/models/load", new JsonObject { ["model"] = "consumer-a.chat" });
        await router.WaitState("consumer-a.chat", false);
        entries = await router.Models();
        Check(failedLoad.Status == 200 && entries.Any(x => x?["id"]?.GetValue<string>() == "consumer-b.chat"), "accepted load later fails while preserving unrelated consumer definition");
        Write(common + Preset("consumer-a.chat", a) + Preset("consumer-b.chat", b));
        entries = await router.Models(true);
        Check(Names(entries).Count() == 2 && Unloaded(entries), "bounded definition restoration succeeds without router restart");
        if (firstModel is null)
        {
            Check(true, "control-only run: real inference, telemetry and restart identity remain unaccredited");
            return;
        }
        await router.Stop();
        Write(common + Preset("consumer-a.chat", a) + Preset("consumer-b.chat", b));
        using var live = new Router(executable, ini, output, "live", ["--models-max", "2"]);
        await live.Ready();
        using var sseCancel = new CancellationTokenSource();
        var sseTask = live.Events(sseCancel.Token);
        try
        {
            await live.EventsReady;
            await live.Load("consumer-a.chat");
            await live.Load("consumer-b.chat");
            var props = await live.Request(HttpMethod.Get, "/props?model=consumer-a.chat&autoload=false");
            Check(props.Status == 200, "declared warmup load observed ready before inference");
            var first = live.Complete("consumer-a.chat", 96);
            var second = live.Complete("consumer-b.chat", 96);
            await live.ObserveUntil(Task.WhenAll(first, second), ["consumer-a.chat", "consumer-b.chat"]);
            Check((await first).Status == 200 && (await second).Status == 200, "two different presets execute concurrent synthetic generation");
            var cached = await live.Complete("consumer-a.chat", 16);
            Check(cached.Status == 200, "repeated synthetic prompt exercises cache evidence");
            // One slot: concurrent requests create real contention, without imposing a Coquena policy.
            var queue = Enumerable.Range(0, 3).Select(_ => live.Complete("consumer-a.chat", 96)).ToArray();
            await live.ObserveUntil(Task.WhenAll(queue), ["consumer-a.chat"]);
            Check(queue.All(x => x.Result.Status == 200), "three requests on one slot complete under backend queue policy");
            var ongoing = live.Complete("consumer-a.chat", 512);
            await live.WaitProcessing("consumer-a.chat");
            Write(common + Preset("consumer-a.chat", a, 1536) + Preset("consumer-b.chat", b));
            entries = await live.Models(true);
            var interrupted = await ongoing;
            Check(entries.Any(x => x?["id"]?.GetValue<string>() == "consumer-b.chat" && x?["status"]?["value"]?.GetValue<string>() == "loaded"), "changed entry reload preserves unrelated loaded consumer");
            File.WriteAllText(Path.Combine(output, "in-flight-status.txt"), interrupted.Status.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await live.Load("consumer-a.chat");
            var baseline = await live.Complete("consumer-a.chat", 16);
            Check(baseline.Status == 200, "changed preset works after explicit load");
            Write(common + Preset("consumer-a.chat", missing) + Preset("consumer-b.chat", b));
            await live.Models(true);
            var failure = await live.Request(HttpMethod.Post, "/models/load", new JsonObject { ["model"] = "consumer-a.chat" });
            await live.WaitState("consumer-a.chat", false);
            Check(failure.Status == 200, "accepted invalid warmup later fails independently of publication");
            Write(common + Preset("consumer-a.chat", a, 1536) + Preset("consumer-b.chat", b));
            await live.Models(true);
            await live.Load("consumer-a.chat");
            Check((await live.Complete("consumer-a.chat", 16)).Status == 200, "bounded restoration returns affected entry to inference without restart");
            // Adding startup=true during reload must not substitute for explicit declarative warmup.
            Write(common + Preset("consumer-a.chat", a, 1536) + Preset("consumer-b.chat", b) + Preset("consumer-c.chat", a, startup: true));
            entries = await live.Models(true);
            Check(entries.Any(x => x?["id"]?.GetValue<string>() == "consumer-c.chat" && x?["status"]?["value"]?.GetValue<string>() == "unloaded"), "load-on-startup does not warm newly reloaded entry");
        }
        finally { sseCancel.Cancel(); await sseTask; }
        await live.Stop();
        // CLI overrides per-preset ctx-size; model IDs/task IDs are scoped to this new process.
        Write(common + Preset("consumer-a.chat", a));
        using var restarted = new Router(executable, ini, output, "restart", ["--ctx-size", "2048"]);
        await restarted.Ready();
        await restarted.Load("consumer-a.chat");
        var slots = await restarted.Request(HttpMethod.Get, "/slots?model=consumer-a.chat&autoload=false");
        Check(slots.Status == 200 && JsonNode.Parse(slots.Body)?[0]?["n_ctx"]?.GetValue<int>() == 2048, "CLI overrides preset and global INI context on explicit new instance");
        var afterRestart = restarted.Complete("consumer-a.chat", 32);
        await restarted.ObserveUntil(afterRestart, ["consumer-a.chat"]);
        Check((await afterRestart).Status == 200, "new instance executes with independently scoped slot and task IDs");
        Write(common + Preset("consumer-a.chat", a, 3072));
        await restarted.Models(true);
        await restarted.Load("consumer-a.chat");
        slots = await restarted.Request(HttpMethod.Get, "/slots?model=consumer-a.chat&autoload=false");
        Check(slots.Status == 200 && JsonNode.Parse(slots.Body)?[0]?["n_ctx"]?.GetValue<int>() == 2048,
            "process CLI context cannot be changed by INI reload: explicit restart required, none performed");
        Write(common + Preset("consumer-a.chat", a) + codec.Entry("metrics", "false", origin) + codec.Entry("slots", "false", origin));
        await restarted.Models(true);
        await restarted.Load("consumer-a.chat");
        var noMetrics = await restarted.Request(HttpMethod.Get, "/metrics?model=consumer-a.chat&autoload=false");
        var noSlots = await restarted.Request(HttpMethod.Get, "/slots?model=consumer-a.chat&autoload=false");
        Check(noMetrics.Status != 200 && noSlots.Status != 200 && (await restarted.Request(HttpMethod.Get, "/health")).Status == 200,
            "disabled metrics and slots are unavailable while router remains healthy");
        Console.WriteLine("Router evidence saved locally; inspect telemetry fields and logs before publishing conclusions.");
    }

    private static IEnumerable<string?> Names(JsonArray entries) => entries.Select(x => x?["id"]?.GetValue<string>()).Order(StringComparer.Ordinal);
    private static bool Unloaded(JsonArray entries) => entries.All(x => x?["status"]?["value"]?.GetValue<string>() == "unloaded");
    private static ProcessStartInfo Start(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        // Avoid case-duplicate environment keys on Windows child-process launch.
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in start.Environment) environment[pair.Key] = pair.Value;
        start.Environment.Clear();
        foreach (var pair in environment.Where(x => !x.Key.StartsWith("LLAMA_", StringComparison.OrdinalIgnoreCase))) start.Environment[pair.Key] = pair.Value;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private sealed class Router : IDisposable
    {
        private readonly Process process;
        private readonly HttpClient client;
        private readonly string directory;
        private readonly Task stdout, stderr;
        private readonly object gate = new();
        private readonly TaskCompletionSource eventsReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task EventsReady => eventsReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        private int sequence;
        public Router(string executable, string ini, string output, string instance, string[] extra)
        {
            directory = Path.Combine(output, instance);
            Directory.CreateDirectory(directory);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var start = Start(executable, new[] { "--host", "127.0.0.1", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--models-preset", ini }.Concat(extra));
            process = Process.Start(start)!;
            stdout = Drain(process.StandardOutput, Path.Combine(directory, "stdout.log"));
            stderr = Drain(process.StandardError, Path.Combine(directory, "stderr.log"));
            File.WriteAllText(Path.Combine(directory, "identity.json"), new JsonObject { ["instance"] = Guid.NewGuid().ToString(), ["pid"] = process.Id, ["started"] = process.StartTime.ToUniversalTime().ToString("O") }.ToJsonString());
            client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(120) };
        }
        private static async Task Drain(StreamReader reader, string path)
        {
            await using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
            while (await reader.ReadLineAsync() is { } line) await writer.WriteLineAsync(DateTimeOffset.UtcNow.ToString("O") + " " + line);
        }
        public async Task Ready()
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(120) && !process.HasExited)
            {
                var result = await Request(HttpMethod.Get, "/health");
                if (result.Status == 200) return;
                await Task.Delay(200);
            }
            throw new Exception("Isolated router failed health; inspect local stderr.");
        }
        public async Task<(int Status, string Body)> Request(HttpMethod method, string path, JsonObject? body = null)
        {
            var started = DateTimeOffset.UtcNow;
            int status;
            string text;
            try
            {
                using var request = new HttpRequestMessage(method, path);
                if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(request);
                status = (int)response.StatusCode;
                text = await response.Content.ReadAsStringAsync();
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            { status = 0; text = error.GetType().Name; }
            lock (gate)
            {
                File.AppendAllText(Path.Combine(directory, "http.jsonl"), new JsonObject { ["sequence"] = ++sequence, ["started"] = started.ToString("O"), ["finished"] = DateTimeOffset.UtcNow.ToString("O"), ["method"] = method.Method, ["path"] = path, ["status"] = status, ["body"] = text }.ToJsonString() + "\n");
            }
            return (status, text);
        }
        public async Task<JsonArray> Models(bool reload = false)
        {
            var result = await Request(HttpMethod.Get, reload ? "/models?reload=1" : "/models");
            if (result.Status != 200) throw new Exception("Model listing/reload failed: " + result.Status);
            return JsonNode.Parse(result.Body)?["data"]?.AsArray() ?? throw new Exception("Missing model data.");
        }
        public async Task UnobservedReload()
        {
            using var socket = new TcpClient();
            await socket.ConnectAsync(IPAddress.Loopback, client.BaseAddress!.Port);
            await socket.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /models?reload=1 HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"));
            // Deliberately do not read the response. This is a controlled lost-result fixture,
            // not a claim that every network failure has the same server-side outcome.
            await Task.Delay(200);
            File.WriteAllText(Path.Combine(directory, "unobserved-reload.txt"), "Reload sent; response deliberately not read. Outcome must be reconciled by /models, never a repeated reload.");
        }
        public async Task Load(string model)
        {
            // Reload compares effective options: an INI value masked by CLI may leave
            // the child loaded, and /models/load rejects an already loaded entry.
            if ((await Models()).Any(x => x?["id"]?.GetValue<string>() == model && x?["status"]?["value"]?.GetValue<string>() == "loaded")) return;
            var result = await Request(HttpMethod.Post, "/models/load", new JsonObject { ["model"] = model });
            if (result.Status != 200) throw new Exception("Explicit load failed: " + result.Status);
            await WaitState(model, true);
        }
        public async Task WaitState(string model, bool loaded)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(120))
            {
                var entry = (await Models()).FirstOrDefault(x => x?["id"]?.GetValue<string>() == model);
                if (loaded && entry?["status"]?["value"]?.GetValue<string>() == "loaded") return;
                if (!loaded && entry?["status"]?["failed"]?.GetValue<bool>() == true) return;
                if (loaded && entry?["status"]?["failed"]?.GetValue<bool>() == true) throw new Exception("Accepted load failed; inspect local log.");
                await Task.Delay(200);
            }
            throw new Exception("Accepted load outcome remains unconfirmed at deadline.");
        }
        public Task<(int Status, string Body)> Complete(string model, int tokens) => Request(HttpMethod.Post, "/completion", new JsonObject
        {
            ["model"] = model, ["prompt"] = string.Concat(Enumerable.Repeat("Synthetic router evidence: red green blue one two three. ", 40)) + "Continue:",
            ["n_predict"] = tokens, ["temperature"] = 0, ["seed"] = 52, ["ignore_eos"] = true, ["cache_prompt"] = true
        });
        public async Task WaitProcessing(string model)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(30))
            {
                var result = await Request(HttpMethod.Get, "/slots?model=" + Uri.EscapeDataString(model) + "&autoload=false");
                if (result.Status == 200 && JsonNode.Parse(result.Body)!.AsArray().Any(x => x?["is_processing"]?.GetValue<bool>() == true)) return;
                await Task.Delay(100);
            }
            throw new Exception("Did not observe in-flight request.");
        }
        public async Task ObserveUntil(Task work, string[] models)
        {
            var watch = Stopwatch.StartNew();
            while (!work.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(115))
            {
                foreach (var model in models)
                foreach (var endpoint in new[] { "slots", "metrics" })
                    await Request(HttpMethod.Get, "/" + endpoint + "?model=" + Uri.EscapeDataString(model) + "&autoload=false");
                await Task.Delay(200);
            }
            await work;
        }
        public async Task Events(CancellationToken cancel)
        {
            try
            {
                using var response = await client.GetAsync("/models/sse", HttpCompletionOption.ResponseHeadersRead, cancel);
                response.EnsureSuccessStatusCode();
                eventsReady.TrySetResult();
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancel));
                await using var writer = new StreamWriter(Path.Combine(directory, "events.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
                while (await reader.ReadLineAsync(cancel) is { } line)
                    await writer.WriteLineAsync(new JsonObject { ["at"] = DateTimeOffset.UtcNow.ToString("O"), ["line"] = line }.ToJsonString());
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch (Exception error) { eventsReady.TrySetException(error); throw; }
        }
        public async Task Stop()
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            await Task.WhenAll(stdout, stderr);
        }
        public void Dispose()
        {
            Stop().GetAwaiter().GetResult();
            client.Dispose(); process.Dispose();
        }
    }
}
