using System.Diagnostics;
using System.Text.Json.Nodes;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class DashboardLogTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var buffer = new LogBuffer();
        buffer.Write("Unicode á € 漢 😀\n\u001b[31mCONTROL\r\npartial");
        var frozen = buffer.Read(false);
        buffer.Write("-end\n");
        check(frozen.Rows[^1] == "partial" && buffer.Read(false).Rows[^2] == "partial-end", "immutable reading snapshot while capture continues");
        check(frozen.Rows[0] == "Unicode á € 漢 😀" && !string.Join("", frozen.Rows).Contains('\u001b'), "Unicode preserved and terminal controls removed");
        buffer.Write(new string('x', LogBuffer.MaximumCharacters * 3) + "😀\n");
        var frame = buffer.Read(false);
        check(frame.Rows.Sum(row => row.Length) <= LogBuffer.MaximumCharacters + LogBuffer.MaximumLine && frame.Discarded > 0,
            "long physical lines are segmented and byte-independent character storage bounded");
        for (var i = 0; i < 10000; i++) buffer.WriteLine("row-" + i);
        frame = buffer.Read(false);
        check(frame.Rows.Length <= LogBuffer.MaximumRows + 1 && frame.Rows[^2] == "row-9999", "burst retains newest rows and reports loss");

        var malformed = new LogBuffer(); malformed.Write(new string('\ud800', LogBuffer.MaximumCharacters * 2));
        check(malformed.Read(false).Rows.Sum(row => row.Length) <= LogBuffer.MaximumCharacters + LogBuffer.MaximumLine + 1,
            "even malformed surrogate input cannot grow the partial line without a bound");

        var root = Path.Combine("D:/tmp", "coquena-tui-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new ScopeStore(ScopeContext.Project(Path.Combine(root, "one.yaml")));
        var registry = new RecipeRegistry(store);
        var one = registry.EnsureProjectService(["one.yaml"], "one");
        var two = registry.EnsureProjectService(["two.yaml"], "two");
        string Register(ServiceIdentity service, string text)
        {
            var id = Guid.NewGuid().ToString("D");
            var path = Path.Combine(root, id + ".log");
            File.WriteAllText(path, text);
            var state = store.Read();
            var docs = state.Documents.ToDictionary(p => p.Key, p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(),
                p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()), StringComparer.Ordinal);
            var entity = docs["local/recipes.json"]["data"]!["recipes"]!.AsArray().SelectMany(r => r!["entities"]!.AsArray())
                .Single(e => e!["id"]!.GetValue<string>() == service.ServiceId)!;
            if (entity["log-history"] is not JsonArray) entity["log-history"] = new JsonArray();
            entity["log-history"]!.AsArray().Add((JsonNode)new JsonObject { ["instance-id"] = id, ["path"] = path,
                ["volume"] = "fixture", ["file-id"] = id, ["started-at"] = DateTimeOffset.UtcNow.ToString("O") });
            store.Commit(state.Revision, docs);
            return path;
        }
        async Task Wait(Func<bool> ready, string message)
        {
            var clock = Stopwatch.StartNew();
            while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(6)) await Task.Delay(25);
            check(ready(), message);
        }
        var first = new ServiceLogSession(new(store, one.ServiceId), CancellationToken.None);
        var second = new ServiceLogSession(new(store, two.ServiceId), CancellationToken.None);
        bool Has(ServiceLogSession session, string text) => session.Read(false).Rows.Any(row => row.Contains(text, StringComparison.Ordinal));
        try
        {
            await Wait(() => first.Status.Contains("Espera"), "waiting service is explicit");
            var a = Register(one, "ONE-α\n" ); var b = Register(two, "TWO-β\n");
            await Wait(() => Has(first, "ONE-α") && Has(second, "TWO-β"), "two service streams captured independently");
            check(!Has(first, "TWO-β") && !Has(second, "ONE-α"), "service identity never mixes log destinations");
            var old = first.Read(false);
            File.AppendAllText(a, "CAPTURE-CONTINUES\n");
            await Wait(() => Has(first, "CAPTURE-CONTINUES"), "capture proceeds while view holds an immutable snapshot");
            check(!old.Rows.Any(row => row.Contains("CAPTURE-CONTINUES")), "held reading remains stable");
            var next = Register(one, "NEW-INSTANCE\n");
            await Wait(() => Has(first, "NEW-INSTANCE"), "live session transitions to next registered instance");
            first.SelectHistory(Path.GetFileNameWithoutExtension(a));
            await Wait(() => first.Historical && first.Status.Contains("completada") && Has(first, "ONE-α"), "historical file selected by instance");
            check(!Has(first, "NEW-INSTANCE"), "historical and current executions stay separate");
            File.AppendAllText(next, "LIVE-WHILE-HISTORY\n");
            await Task.Delay(400);
            first.Current();
            await Wait(() => !first.Historical && Has(first, "LIVE-WHILE-HISTORY"), "return to current includes lines captured during history");
            File.Delete(a);
            first.SelectHistory(Path.GetFileNameWithoutExtension(a));
            await Wait(() => first.Status.Contains("No disponible"), "retained registry entry with removed file shows unavailable");
            first.Current();
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try { new ServiceLogReader(store, one.ServiceId).Recent(TextWriter.Null, cancellation: cancelled.Token); check(false, "cancelled history accepted"); }
                catch (OperationCanceledException) { check(true, "historical reader supports cooperative cancellation"); }
            }
            File.WriteAllText(next, "TRUNC\n");
            await Wait(() => Has(first, "Archivo truncado") && Has(first, "TRUNC"), "truncation visible and capture resumes");
            var unavailable = Register(one, "RESTORED-ACCESS\n");
            File.Delete(unavailable);
            await Wait(() => first.Status.Contains("Archivo no disponible"), "unavailable live file is reported without blocking capture");
            File.WriteAllText(unavailable, "RESTORED-ACCESS\n");
            await Wait(() => Has(first, "RESTORED-ACCESS"), "missing live file recovers on bounded retry");
            for (var i = 0; i < 20; i++) first.SelectHistory(Path.GetFileNameWithoutExtension(b));
            await Wait(() => first.Status.Contains("No disponible"), "foreign historical instance rejected under rapid selection");
            first.Current();
            registry.RemoveStoppedService(one.ServiceId);
            registry.EnsureProjectService(["one.yaml"], "one");
            await Wait(() => first.Status.Contains("Desconectado"), "recreated name disconnects original service identity");
            check(!Has(first, "TWO-β"), "disconnection cannot redirect stream");
        }
        finally { await Task.WhenAll(first.CloseAsync(), second.CloseAsync()); }
    }
}
