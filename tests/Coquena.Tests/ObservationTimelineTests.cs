using Coquena.Observation;

namespace Coquena.Tests;

internal static class ObservationTimelineTests
{
    public static void Run(Action<bool, string> check)
    {
        var start = DateTimeOffset.UnixEpoch;
        var gpuA = new ObservationTimeline();
        var gpuB = new ObservationTimeline();
        gpuA.Add(start, .40);
        gpuA.Add(start.AddSeconds(2), .45);
        gpuA.Add(start.AddSeconds(4), .42);
        gpuB.Add(start, .30);
        gpuB.Add(start.AddSeconds(4), .35);
        check(gpuA.Read(start.AddSeconds(4), 3).SequenceEqual(new double?[] { .40, .45, .42 }),
            "responding GPU retains the shared time axis");
        check(gpuB.Read(start.AddSeconds(4), 3).SequenceEqual(new double?[] { .30, null, .35 }),
            "missing GPU reading leaves a gap without shifting or interpolating");
        check(gpuB.Read(start.AddSeconds(4), 2).SequenceEqual(new double?[] { null, .35 }) &&
            gpuB.Read(start.AddSeconds(4), 5).SequenceEqual(new double?[] { null, null, .30, null, .35 }),
            "resize preserves observation times and gaps");
        check(gpuB.Read(start.AddSeconds(6), 3).SequenceEqual(new double?[] { null, .35, null }),
            "a redraw advances time without duplicating the last measurement");
        check(gpuB.Read(start.AddSeconds(54), 24).All(value => value is null),
            "a stalled source ages out of the visible time window");

        var phase = new ObservationTimeline();
        phase.Add(start.AddMilliseconds(500), 1);
        phase.Add(start.AddSeconds(4).AddMilliseconds(500), 2);
        check(phase.Read(start.AddSeconds(5), 3).SequenceEqual(new double?[] { 1, null, 2 }),
            "phase and resource readings use the same two-second UTC columns");
        gpuB.Add(start.AddSeconds(6), 0);
        check(gpuB.Read(start.AddSeconds(6), 1)[0] == 0,
            "an observed zero is distinct from an absent measurement");
        gpuB.Add(start.AddSeconds(8), null);
        check(gpuB.Read(start.AddSeconds(8), 1)[0] is null,
            "an explicitly unavailable reading remains a gap");

        var repeated = new ObservationTimeline();
        repeated.Add(start, .1);
        repeated.Add(start.AddMilliseconds(500), .2);
        repeated.Add(start.AddMilliseconds(250), .9);
        repeated.Add(start.AddMilliseconds(500), .9);
        check(repeated.Count == 1 && repeated.Read(start.AddSeconds(1), 1)[0] == .2,
            "a column keeps the most recent reading and rejects older or repeated observations");
        check(repeated.Read(start.AddSeconds(-2), 3).All(value => value is null),
            "future observations are not displayed before their time");
        check(repeated.Read(start, 0).Length == 0 && repeated.Read(start, -1).Length == 0 &&
            repeated.Read(start, 1000).Length == ObservationTimeline.Capacity,
            "hidden and large terminals keep rendering bounded");

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1; i <= 10_000; i++) repeated.Add(start.AddSeconds(i * 2), i);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        check(repeated.Count == ObservationTimeline.Capacity && allocated < 16_384,
            "long sessions retain at most 24 columns with bounded allocation");
        check(repeated.Read(start.AddSeconds(20_000), 24).First() == 9977 &&
            repeated.Read(start.AddSeconds(20_000), 24).Last() == 10_000,
            "eviction preserves the most recent window");
        repeated.Clear();
        check(repeated.Count == 0 && repeated.Read(start.AddSeconds(20_000), 24).All(value => value is null),
            "changing phase identity clears its timeline");
        check(gpuA.Read(start.AddSeconds(4), 3)[0] == .40,
            "clearing the focused task leaves the machine timeline intact");

        var metric = new MetricSample("gpu.utilization", 60, "%", "fixture", "device:fixture", start);
        check(metric.IsFresh(start.AddSeconds(4), TimeSpan.FromSeconds(4)) &&
            !metric.IsFresh(start.AddSeconds(4).AddMilliseconds(1), TimeSpan.FromSeconds(4)),
            "a reading becomes old only after four seconds");
        check(gpuA.Read(start.AddSeconds(6), 4)[0] == .40,
            "current-value freshness does not remove historical readings");
        check(WindowsSystemMetrics.BusiestEngine([30, 70]) == 70 &&
            WindowsSystemMetrics.BusiestEngine([0, 100]) == 100,
            "GPU activity uses the busiest engine within the percentage scale");
        check(WindowsSystemMetrics.BusiestEngine([203.5, 20]) is null &&
            WindowsSystemMetrics.BusiestEngine([double.NaN]) is null &&
            WindowsSystemMetrics.BusiestEngine([]) is null,
            "out-of-range PDH aggregates remain unavailable instead of saturating the graph");
    }
}
