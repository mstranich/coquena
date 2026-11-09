using Coquena.Observation;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Tests;

// Rows of the En vivo tab against the 80×24 preview: widths, glyphs and omitted values.
internal static class LivePresentationTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        check(Markup.Parse("a {ok|b} {btn@stop|c}") is [{ Class: null, Text: "a " }, { Class: "ok", Text: "b" }, { Text: " " }, { Class: "btn", Action: "stop", Text: "c" }],
            "markup keeps classes and actions of each span");
        check(Markup.Plain(Markup.Escape("{\"a\":1} | x")) == "{\"a\":1} | x" && Markup.Parse(Markup.Escape("{x|y}")).Count == 1,
            "escaped text with braces and bars is drawn literally");
        var tabs = Markup.Fit(LivePresentation.Tabs(0, 0, 2), 80);
        check(Markup.Width(tabs) == 80 && Markup.Plain(tabs).StartsWith(" ● En vivo ") && Markup.Plain(tabs).EndsWith("⇣ 0  ▲ 2 "),
            "top bar fits 80 columns with tabs and both indicators");
        check(Markup.Width(Markup.Fit("x{pad}" + new string('y', 90), 80)) == 80, "rows never exceed their width");

        var store = new ScopeStore(ScopeContext.Global(new Home(Path.Combine(Path.GetTempPath(), "coquena-readonly-live"))));
        var targets = new[] { new DashboardCli.Target(store, null, false, ProjectName: "principal") };
        var entries = new[]
        {
            new EntrySnapshot("código/asistente", "loaded", now, []),
            new EntrySnapshot("código/editor", "unloaded", now, [])
        };
        var tree = LivePresentation.Tree(targets, new Dictionary<int, IReadOnlyList<EntrySnapshot>> { [0] = entries }, new Dictionary<int, ServiceSnapshot>(), now);
        check(tree.Select(row => Markup.Plain(row.Normal)).SequenceEqual([" ▾○ principal", "   ▾ código", "      asistente", "      ○ editor"]),
            "tree shows service, project and presets with glyphs only for presets not loaded");
        check(tree.All(row => Markup.Width(row.Selected) == LivePresentation.TreeWidth), "the selected tree row fills the column");

        var entry = entries[0] with { Metrics = [
            new("llamacpp:prompt_tokens_total", 38, "tokens", "fixture", "entry", now),
            new("llamacpp:prompt_tokens_cached_total", 62, "tokens", "fixture", "entry", now),
            new("llamacpp:spec_decode_num_draft_tokens_total", 50, "tokens", "fixture", "entry", now),
            new("llamacpp:spec_decode_num_accepted_tokens_total", 34, "tokens", "fixture", "entry", now),
            new("llamacpp:n_busy_slots_per_decode", 1.6, "slots", "fixture", "entry", now)] };
        var facts = Markup.Plain(LivePresentation.Facts(entry)!);
        check(facts.Contains("caché 62%") && facts.Contains("aceptación 68%") && facts.Contains("slots/decode 1"), "entry facts match the preview");
        check(LivePresentation.Facts(entries[1]) is null && LivePresentation.Context(null, 56) is null, "unmeasured rows are omitted");
        var inference = new InferenceSnapshot(1, 1, 1, [new InferenceSlot(0, 41, 5012, 0, 128) { Context = 8192 }, new InferenceSlot(1, 42, 900, 0, 0) { Context = 8192 }]);
        var context = Markup.Plain(LivePresentation.Context(inference, 56)!);
        check(context.StartsWith(" ctx s0 ██████▒░░░ 5,1k/8,2k") || context.StartsWith(" ctx s0 ██████▒░░░ 5.1k/8.2k"), "context bar per slot: " + context);
        check(Markup.Width(LivePresentation.Context(inference, 56)!) <= 56, "context row fits the right column");

        var task = new InferenceTask(new("global", "s", "i", "código/asistente", 1, 1, 0, 41, 1), new InferenceSlot(0, 41, 5012, 0, 128) { PredictLimit = 512 },
            1, null, 20, null, null, 96, 24, 1, 1);
        var row = LivePresentation.Task(task, false, 1234, null);
        check(Markup.Plain(row).StartsWith(" s0 t41    GENERANDO   5 012   128/512   ") && Markup.Plain(row).Contains("1,2s") || Markup.Plain(row).Contains("1.2s"), "task row: " + Markup.Plain(row));
        check(Markup.Width(LivePresentation.Task(task, false, 1234, now)) <= 56, "task row with TTFT and end fits the right column");
        check(LivePresentation.PromptTiming("[58760] 7.06.175.382 I slot print_timing: id  0 | task 3248 | prompt eval time =     812.30 ms /   630 tokens") == (0, 3248, 812.3),
            "prompt eval time is read per slot and task from the backend log");
        check(LivePresentation.PromptTiming("[58760] slot print_timing: id  0 | task 3248 |        eval time =  10 ms") is null, "generation time is not taken as TTFT");
        var rate = LivePresentation.Rate("gen", "gen", [10, 20, null], 20, 24, 22);
        check(Markup.Plain(rate).StartsWith(" gen    ") && Markup.Plain(rate).EndsWith(" 20 tk/s pico 24") && Markup.Width(rate) <= 56, "rate row: curve, current and peak");

        var log = LivePresentation.LogHeader("principal", LivePresentation.LogMode.Following, 0, false);
        var header = Markup.Plain(Markup.Fit(log, 80));
        check(header.StartsWith(" Log principal  ● siguiendo") && header.EndsWith(" ❚❚ Pausar   ⤓ Seguir   Histórico "), "log header: " + header);
        check(LivePresentation.LogRow("{\"error\":1}").Contains("err|") && Markup.Plain(LivePresentation.LogRow("ok {x}")) == " ok {x}", "log rows keep their text");

        var (first, second) = LivePresentation.HardwareBar(new("CPU", [0.4], 0.42, null, null, null), new("RAM", [0.5], 0.57, "18,2G/32G", null, null),
            [new("GPU0", [0.9], 0.91, "20,1G/24G", "+412M", 0.95), new("GPU1", [0.1], 0.12, "0,1G/0,5G", "+1,1G", 0.2)], false);
        check(Markup.Width(first) <= 80 && Markup.Width(second) <= 80, "hardware bar fits 80 columns");
        check(Markup.Parse(second).Any(span => span.Action == "gpu1") && Markup.Parse(first).Any(span => span.Action == "ram"), "bar captions open the hardware view");

        var history = new HardwareHistory();
        history.Add(now.AddMinutes(-4), 0.2);
        history.Add(now.AddMinutes(-4).AddSeconds(1), 0.9);
        var columns = history.Columns(now, 50);
        check(columns.Count(value => value is not null) == 1 && columns.Max() == 0.9, "hardware columns keep the maximum of their interval");
    }
}
