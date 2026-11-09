using Coquena.Observation;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class DashboardPresentationTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var store = new ScopeStore(ScopeContext.Global(new Home(Path.Combine(Path.GetTempPath(), "coquena-readonly-tree"))));
        var targets = new[] { new DashboardCli.Target(store, null, false, ProjectName: "shared") };
        var entries = new[]
        {
            new EntrySnapshot("news/summary", "loaded", now, []),
            new EntrySnapshot("code/assistant", "unloaded", now, []),
            new EntrySnapshot("external", "loaded", now, [])
        };
        var tree = DashboardPresentation.Tree(targets, new Dictionary<int, IReadOnlyList<EntrySnapshot>> { [0] = entries });
        check(tree.Count(node => node.Entry is not null) == 3 && tree.All(node => node.Target == 0),
            "navigation preserves every observed entry and its target");
        check(tree.Single(node => node.Entry == "news/summary").Text.Contains("summary") && tree.Any(node => node.Text == "  news"),
            "published namespaces group entries while full names remain selection identities");
        check(tree.Any(node => node.Text == "  Sin prefijo") && tree.Single(node => node.Entry == "external").Entry == "external",
            "external names without namespaces remain observable without inventing an owner");
        check(DashboardPresentation.Tree(targets, new Dictionary<int, IReadOnlyList<EntrySnapshot>>()).Count == 1,
            "a recipe without an observed catalogue remains navigable");
        check(DashboardPresentation.Tree([], new Dictionary<int, IReadOnlyList<EntrySnapshot>>()).Count == 0,
            "empty scopes do not invent service nodes");
        check(DashboardPresentation.EntryFacts(null) is null && DashboardPresentation.EntryFacts(entries[0]) is null,
            "unmeasured entry counters are omitted instead of shown as n/d");
        var entry = entries[0] with { Metrics = [new("llamacpp:requests_deferred", 0, "requests", "fixture", "entry", now),
            new("llamacpp:prompt_tokens_seconds", 12, "tokens/second", "fixture", "entry", now)] };
        check(DashboardPresentation.EntryFacts(entry) is { } facts && facts.Contains("cola 0") && DashboardPresentation.EntryMeans(entry) == "medias del backend · P 12 tk/s",
            "backend means and measured zero queue retain their meaning");
        check(DashboardPresentation.Fit("Ornith-1.5-35B-A3B-balanced", 18) is { Length: 18 } fitted && fitted.EndsWith("balanced") && fitted.Contains('…'),
            "truncated names keep the distinguishing suffix of sibling presets");
        check(tree.Single(node => node.Entry == "news/summary").Text.Contains("● summary") &&
            tree.Single(node => node.Entry == "code/assistant").Text.Contains("○ assistant"),
            "tree nodes show the entry state as a glyph");
        var snapshot = new ServiceSnapshot("service", "shared", "global", "instance", now, now, "alive", "available", "HTTP 200",
            "same", "none", true, null, null,
            [new("cpu.utilization", 12, "%", "fixture", "instance", now.AddSeconds(-5)),
             new("ram.private-commit", 1024, "bytes", "fixture", "instance", now)], [], [], false);
        check(DashboardPresentation.Process(snapshot, now) == "RAM 0M",
            "process summary expires each sample independently and omits the stale one");
    }
}
