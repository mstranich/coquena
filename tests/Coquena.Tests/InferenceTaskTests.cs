using System.Diagnostics;
using Coquena.Observation;

namespace Coquena.Tests;

internal static class InferenceTaskTests
{
    public static void Run(Action<bool, string> check)
    {
        var tasks = new InferenceTasks(historyLimit: 2);
        var start = DateTimeOffset.UnixEpoch;
        EntrySnapshot Entry(string name, int second, InferenceSlot[] slots, int child = 10) =>
            new(name, "loaded", start.AddSeconds(second), []) { Inference = new(child, child, second * Stopwatch.Frequency, slots) };
        ServiceSnapshot Sample(int second, EntrySnapshot[] entries, string instance = "instance") =>
            new("service", "fixture", "global", instance, start.AddSeconds(second), start.AddSeconds(second),
                "alive", "available", "HTTP 200", "", "", true, null, null, [], entries, [], false);

        tasks.Observe(Sample(1, [Entry("a", 1, [new(0, 1, 10, 2, 0), new(1, 2, 20, 0, 4)]),
            Entry("b", 1, [new(0, 1, 50, 5, 0)], child: 20)]));
        var first = tasks.Active.Single(task => task.Identity.Entry == "a" && task.Identity.Slot == 0).Identity;
        var second = Sample(2, [Entry("a", 2, [new(0, 1, 25, 2, 0), new(1, 2, 20, 0, 11)]),
            Entry("b", 2, [new(0, 1, 80, 5, 0)], child: 20)]);
        tasks.Observe(second);
        check(tasks.Active.Count() == 3, "concurrent slots and entries preserve distinct tasks");
        check(tasks.Active.Single(task => task.Identity == first) is { PromptRate: 15, Phase: InferenceTaskPhase.Prompt }, "prompt delta belongs to its slot");
        check(tasks.Active.Single(task => task.Identity.Slot == 1) is { OutputRate: 7, Phase: InferenceTaskPhase.Generation }, "output delta belongs to its slot");
        check(tasks.Active.Single(task => task.Identity.Entry == "b").PromptRate == 30, "shared slot and task numbers do not cross entries");
        tasks.Observe(second);
        tasks.Observe(Sample(1, []));
        check(tasks.Active.Count() == 3 && tasks.History.Count == 0, "duplicate and out-of-order captures do not change observations");

        tasks.Observe(Sample(3, [Entry("a", 3, [new(0, 1, 25, 2, 2)]), Entry("b", 3, [], child: 20)]));
        check(tasks.Active.Single() is { OutputRate: null, PromptMean: 15, PromptPeak: 15, PromptSeconds: 1 }, "phase transition preserves prompt statistics without a cross-phase delta");
        check(tasks.History.Count == 2 && tasks.History.All(task => task.End == InferenceTaskEnd.LeftSlot), "a task leaving its slot keeps last values and an unconfirmed outcome");
        tasks.Observe(Sample(4, []));
        check(tasks.History.Count == 2 && tasks.History.Last() is { End: InferenceTaskEnd.ObservationLost, Last.Decoded: 2 }, "loss bounds history and preserves the last observed count");
        tasks.Observe(Sample(5, [Entry("a", 5, [new(0, 1, 25, 2, 9)])]));
        check(tasks.Active.Single().Identity != first && tasks.Active.Single().OutputMean is null, "a gap starts a new observation even with reused IDs");
        tasks.Observe(Sample(6, [Entry("a", 6, [new(0, 1, 25, 2, 10)], child: 11)]));
        check(tasks.History.Last().End == InferenceTaskEnd.ChildReplaced && tasks.Active.Single().OutputMean is null, "child replacement prevents continuity");
        tasks.Observe(Sample(7, [Entry("a", 7, [new(0, 1, 25, 2, 1)], child: 11)]));
        check(tasks.History.Last().Last.Decoded == 10 && tasks.Active.Single().OutputMean is null, "counter rollback separates reused task numbers");
        tasks.Observe(Sample(11, [Entry("a", 11, [new(0, 1, 25, 2, 8)], child: 11)]));
        check(tasks.Active.Single().OutputMean is null && tasks.History.Last().End == InferenceTaskEnd.ObservationLost, "a long sampling gap breaks continuity");
        tasks.Observe(Sample(12, [Entry("a", 12, [new(0, 1, 25, 2, 9)], child: 11)], instance: "replacement"));
        check(tasks.Active.Single().Identity.Instance == "replacement" && tasks.Active.Single().OutputMean is null, "router replacement cannot inherit a task");
        tasks.Observe(Sample(13, [Entry("a", 13, [new(0, 1, 25, 2, 15)], child: 11)], instance: "replacement")
            with { Scope = "other-scope" });
        check(tasks.Active.Single() is { Identity.Scope: "other-scope", OutputMean: null }, "matching service IDs cannot cross scopes");
        var noHistory = new InferenceTasks(historyLimit: 0);
        noHistory.Observe(second);
        check(noHistory.Active.Count() == 3, "retention never limits active tasks");
        noHistory.Suspend();
        check(!noHistory.Active.Any() && noHistory.History.Count == 0, "zero retention disables history");
    }
}
