using System.Diagnostics;
using Coquena.Observation;
using Coquena.Servers;

namespace Coquena.Tests;

internal static class InferencePresentationTests
{
    public static void Run(Action<bool, string> check)
    {
        var start = DateTimeOffset.UtcNow;
        var clock = Stopwatch.GetTimestamp();
        var tasks = new InferenceTasks(2);
        ServiceSnapshot Capture(int second, string instance = "router", bool empty = false) =>
            new("service", "fixture", "global", instance, start.AddSeconds(second), start.AddSeconds(second),
                "alive", "available", "HTTP 200", "", "", true, null, null, [],
                empty ? [] : [new("a", "loaded", start.AddSeconds(second), [])
                {
                    Inference = new(10, 10, clock + second * Stopwatch.Frequency,
                        [new(0, 1, 10, 2, second), new(1, 2, 20, 0, second * 2)]) { Model = "fixture.gguf" }
                }, new("b", "loaded", start.AddSeconds(second), [])
                {
                    Inference = new(20, 20, clock + second * Stopwatch.Frequency, [new(0, 1, 10, 0, second * 3)])
                }], [], false);
        for (var second = 1; second < 100; second++) tasks.Observe(Capture(second));
        var rows = InferencePresentation.Rows(tasks, "global", "service");
        check(rows.Count == 3 && rows.All(task => task.End is null), "all concurrent tasks are available for presentation");
        check(rows.All(task => task.Points.Count == ObservationTimeline.Capacity), "curves retain bounded history for every task, including unfocused tasks");
        var selected = rows[1].Identity;
        tasks.Observe(Capture(100));
        check(InferencePresentation.Rows(tasks, "global", "service").Any(task => task.Identity == selected), "stable selection uses full task identity through refresh");
        var detail = InferencePresentation.Detail(rows[0]);
        check(detail.Contains("fixture.gguf") && detail.Contains("sin denominador") && detail.Contains("no latencia completa"), "task detail preserves model and explicitly limits progress and timing claims");
        check(InferencePresentation.Row(rows[0], 40).Contains("GENERACIÓN") && InferencePresentation.Row(rows[0], 40).Contains("G 99"), "compact rows retain entry, phase and observed tokens");
        tasks.Observe(Capture(101, empty: true));
        rows = InferencePresentation.Rows(tasks, "global", "service");
        check(rows.Count == 2 && rows.All(task => task.End is not null && task.Points.Count == 24), "completed retention bounds rows and curves together");
        check(InferencePresentation.Detail(rows[0]).Contains("resultado de la petición sin confirmar"), "ended observations never assert request success");
        tasks.Observe(Capture(102, instance: "replacement"));
        rows = InferencePresentation.Rows(tasks, "global", "service");
        check(rows.Count == 5 && rows.Take(3).All(task => task.Identity.Instance == "replacement"), "active tasks precede history across router replacement");
        check(rows[0].Points.Count == 1 && rows[0].OutputMean is null, "new router cannot inherit curves or statistics");
        check(InferencePresentation.Rows(tasks, "other", "service").Count == 0, "history does not leak across service scopes");
        var metrics = ServerRuntime.ParseMetrics("llamacpp:spec_decode_num_draft_tokens_total 10\n" +
            "llamacpp:spec_decode_num_accepted_tokens_total 4\nllamacpp:n_tokens_max 100\n" +
            "llamacpp:spec_decode_num_accepted_tokens_per_pos_total{position=\"0\"} 3\n" +
            "llamacpp:spec_decode_num_accepted_tokens_per_pos_total{position=\"1\"} 1\n");
        check(metrics.Count == 5 && metrics["llamacpp:spec_decode_num_accepted_tokens_per_pos_total{position=\"1\"}"] == 1,
            "speculative totals and labeled positions retain separate identities");
        try
        {
            ServerRuntime.ParseMetrics("llamacpp:spec_decode_num_draft_tokens_total NaN");
            check(false, "nonfinite speculation rejected");
        }
        catch (FormatException) { check(true, "nonfinite speculation rejected"); }
    }
}
