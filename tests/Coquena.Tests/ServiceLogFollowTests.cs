using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Coquena.Projects;
using Coquena.Observation;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ServiceLogFollowTests
{
    public static async Task Run(string? product)
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        const string proxy = "0.01 I srv  proxy_reques: proxying request to model fixture on port 12345";
        var noisyTail = string.Join("\r\n", new[] { "carga terminada", "error de prueba" }
            .Concat(Enumerable.Repeat(proxy, 35)));
        var buffer = new LogBuffer(); buffer.Write(noisyTail);
        Check(buffer.Read(true).Rows.Where(row => row.Length > 0).SequenceEqual(new[] { "carga terminada", "error de prueba" }),
            "filter keeps useful rows before presenting the bounded log buffer");
        Check(buffer.Read(false).Rows.Count(row => row == proxy) == 35,
            "disabling the filter restores forwarded requests from the captured buffer");
        await DashboardLogTests.Run(Check);
        var root = Path.Combine("D:/tmp", "coquena-follow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new ScopeStore(ScopeContext.Project(Path.Combine(root, "coquena.yaml")));
        var registry = new RecipeRegistry(store);
        var service = registry.EnsureProjectService(["coquena.yaml"], "follow-fixture");
        var logs = new ServiceLogReader(store, service.ServiceId);
        string Register(string text)
        {
            var id = Guid.NewGuid().ToString("D"); var path = Path.Combine(root, id + ".log");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            var state = store.Read(); var docs = state.Documents.ToDictionary(p => p.Key, p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()), StringComparer.Ordinal);
            var entity = docs["local/recipes.json"]["data"]!["recipes"]!.AsArray().SelectMany(r => r!["entities"]!.AsArray()).Single(e => e!["id"]!.GetValue<string>() == service.ServiceId)!;
            var history = entity["log-history"] as JsonArray;
            if (history is null) entity["log-history"] = history = new JsonArray();
            history.Add((JsonNode)new JsonObject { ["instance-id"] = id, ["path"] = path, ["volume"] = "fixture", ["file-id"] = id, ["started-at"] = DateTimeOffset.UtcNow.ToString("O") });
            store.Commit(state.Revision, docs);
            return path;
        }
        var capture = new Capture();
        using var cancellation = new CancellationTokenSource();
        var follow = logs.FollowAsync(capture, cancellation.Token);
        async Task Wait(string text)
        {
            var timer = Stopwatch.StartNew();
            while (!capture.Text.Contains(text) && timer.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
            Check(capture.Text.Contains(text), "follow receives " + text);
        }
        try
        {
            await Wait("Esperando");
            var first = Register("FIRST-á\n"); await Wait("FIRST-á");
            using (var writer = new FileStream(first, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                writer.Write(new byte[] { 0xe2 }); writer.Flush();
                await Task.Delay(350);
                writer.Write(new byte[] { 0x82, 0xac }); writer.Flush();
            }
            await Wait("€");
            Check(!capture.Text.Contains('\ufffd'), "split UTF-8 write survives EOF polling");
            File.AppendAllText(first, "OLD-FINAL\n");
            var second = Register("SECOND\n"); var third = Register("THIRD\n");
            await Wait("THIRD");
            Check(capture.Text.IndexOf("OLD-FINAL", StringComparison.Ordinal) < capture.Text.IndexOf("SECOND", StringComparison.Ordinal) &&
                capture.Text.IndexOf("SECOND", StringComparison.Ordinal) < capture.Text.IndexOf("THIRD", StringComparison.Ordinal), "drains old file and visits both restarts in ledger order");
            for (var i = 0; i < 30; i++) { File.AppendAllText(third, $"ROW-{i}\n"); await Task.Delay(2); }
            await Wait("ROW-29");
            Check(capture.Text.Split("ROW-29").Length == 2, "concurrent writes emitted once");
            using var history = new StringWriter();
            logs.Recent(history, Path.GetFileNameWithoutExtension(first));
            Check(history.ToString() == "FIRST-á\n€OLD-FINAL\n", "show remains on selected historical file");
            Check(logs.List().Length == 3, "list exposes every retained instance ID");
            File.WriteAllText(third, "TRUNCATED\n"); await Wait("TRUNCATED");
            File.Delete(third); var fourth = Register("AFTER-MISSING\n"); await Wait("AFTER-MISSING");
            File.AppendAllText(fourth, new string('x', 100000) + "TAIL-END");
            using var recent = new StringWriter(); logs.Recent(recent);
            Check(recent.ToString().Length <= 65536 && recent.ToString().EndsWith("TAIL-END"), "recent query bounded to tail");
            using var full = new StringWriter(); logs.Recent(full, Path.GetFileNameWithoutExtension(fourth));
            Check(full.ToString().Length > 100000, "historical show streams full fixed extent");
            try { logs.Recent(TextWriter.Null, "../unregistered"); throw new Exception("arbitrary log accepted"); }
            catch (StorageException e) when (e.Code == "unknown-log") { checks++; }
            registry.RemoveStoppedService(service.ServiceId);
            registry.EnsureProjectService(["coquena.yaml"], "follow-fixture");
            try { await follow.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("follow adopted replacement"); }
            catch (StorageException e) when (e.Code == "unknown-service") { checks++; }
        }
        finally
        {
            cancellation.Cancel();
            try { await follow; } catch (OperationCanceledException) { } catch (StorageException) { }
        }
        service = registry.Services().Single();
        logs = new ServiceLogReader(store, service.ServiceId);
        capture = new Capture();
        using (var waitingCancellation = new CancellationTokenSource())
        {
            var waiting = logs.FollowAsync(capture, waitingCancellation.Token);
            await Wait("Esperando");
            waitingCancellation.Cancel();
            try { await waiting; } catch (OperationCanceledException) { checks++; }
            Check(logs.List().Length == 0, "cancelling an empty observer creates no log");
        }
        var blockedPath = Register("ACCESS-RESTORED");
        capture = new Capture();
        using (var accessCancellation = new CancellationTokenSource())
        {
            Task access;
            using (var exclusive = new FileStream(blockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                access = logs.FollowAsync(capture, accessCancellation.Token);
                await Task.Delay(700);
                Check(capture.Text.Split("Archivo no disponible").Length == 2, "access failure is diagnosed once with bounded retries");
            }
            await Wait("ACCESS-RESTORED");
            accessCancellation.Cancel();
            try { await access; } catch (OperationCanceledException) { checks++; }
        }
        Check(File.Exists(blockedPath), "cancelling observer preserves log");
        if (product is not null) await Native(product, root, Check);
        Console.WriteLine($"Service log follow: {checks} checks passed (anonymized fixtures; no inference).");
    }

    private static async Task Native(string product, string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "native"); Directory.CreateDirectory(folder);
        var home = Path.Combine(root, "home");
        var app = Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe");
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var yaml = Path.Combine(folder, "coquena.yaml");
        File.WriteAllText(yaml, $"name: follow-fixture\nservice:\n  backend: {{executable: '{app.Replace('\\', '/')}'}}\n  server:\n    args:\n      scoped-health-child: true\n      log-marker: NATIVE-0\n      port: {port}\n    health:\n      url: http://127.0.0.1:{port}/health\n");
        Process Start(params string[] args)
        {
            var start = new ProcessStartInfo(product) { UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true, WorkingDirectory = folder };
            start.Environment["COQUENA_HOME"] = home;
            foreach (var arg in args) start.ArgumentList.Add(arg);
            var process = Process.Start(start)!; process.StandardInput.Close(); return process;
        }
        async Task<string> Command(params string[] args)
        {
            using var process = Start(args);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
            var result = await stdout; var error = await stderr;
            check(process.ExitCode == 0, "native command: " + string.Join(" ", args) + ": " + error);
            return result;
        }
        var store = new ScopeStore(ScopeContext.Project(yaml));
        var ids = new List<string>();
        await Command("--private", "service", "self", "start", "-d");
        using var observer = Start("logs", "follow", "--service", "self");
        var captured = new Capture();
        var pumping = Task.Run(async () => { var chars = new char[1024]; int count; while ((count = await observer.StandardOutput.ReadAsync(chars)) > 0) captured.Write(new string(chars, 0, count)); });
        var errors = observer.StandardError.ReadToEndAsync();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                if (i > 0) File.WriteAllText(yaml, File.ReadAllText(yaml).Replace($"NATIVE-{i - 1}", $"NATIVE-{i}"));
                if (i > 0) await Command("--private", "server", "self", "restart", "-d");
                var active = store.Read().ActiveDocuments().Single()["data"]!;
                ids.Add(active["instance-id"]!.GetValue<string>());
                var timer = Stopwatch.StartNew();
                while (!captured.Text.Contains($"NATIVE-{i}") && timer.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(25);
                check(captured.Text.Contains($"NATIVE-{i}"), "native follow continues across restart " + i);
            }
            var list = await Command("service", "self", "logs", "list");
            check(ids.All(list.Contains), "native list exposes all three IDs");
            var historical = await Command("server", "self", "logs", "show", ids[0]);
            check(historical.Contains("NATIVE-0") && !historical.Contains("NATIVE-2"), "native show fixes historical ID");
            check((await Command("--service", "self", "logs")).Contains("NATIVE-2"), "native root shortcut recent selection");
            observer.Kill(); await observer.WaitForExitAsync(); await pumping;
            check(new ScopedServiceProcess(store).Reconcile(new RecipeRegistry(store).Services().Single().ServiceId), "closing observer leaves service running");
            await Command("service", "self", "stop");
            File.WriteAllText(yaml, File.ReadAllText(yaml).Replace("name: follow-fixture", "name: logs"));
            await Command("import", yaml, "--select", "service", "--as-default");
            await Command("service", "start", "-d");
            var defaultList = await Command("logs", "list");
            check(defaultList == await Command("service", "default", "logs", "list"), "root and service select same default");
            check(defaultList == await Command("--service", "logs", "server", "logs", "list"), "reserved service name works through explicit selector");
            check((await Command("logs")).Contains("NATIVE-2"), "default recent output");
            await Command("service", "stop");
        }
        finally
        {
            if (!observer.HasExited) { observer.Kill(); await observer.WaitForExitAsync(); }
            await pumping; await errors;
            foreach (var scope in new[] { store, new ScopeStore(ScopeContext.Global(new Home(home))) })
                foreach (var service in new RecipeRegistry(scope).Services()) new ScopedServiceProcess(scope).Stop(service.ServiceId, () => true);
        }
    }

    private sealed class Capture : TextWriter
    {
        private readonly StringBuilder text = new();
        public override Encoding Encoding => Encoding.UTF8;
        public string Text { get { lock (text) return text.ToString(); } }
        public override void Write(string? value) { lock (text) text.Append(value); }
        public override void Write(char[] buffer, int index, int count) { lock (text) text.Append(buffer, index, count); }
        public override void WriteLine(string? value) { lock (text) text.AppendLine(value); }
    }
}
