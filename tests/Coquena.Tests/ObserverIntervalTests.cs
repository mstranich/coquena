using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Observation;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ObserverIntervalTests
{
    public static void Run(Action<bool, string> check)
    {
        check(ObserverInterval.Parse("0.5") == 0.5 && ObserverInterval.Parse("0,5") == 0.5 && ObserverInterval.Parse("60") == 60,
            "the interval accepts a decimal point or comma within range");
        foreach (var invalid in new[] { "0.1", "61", "-1", "abc", "" })
            check(Throws(() => ObserverInterval.Parse(invalid)), $"the interval rejects {invalid}");

        var args = new List<string> { "logs", "--interval", "1.5", "-f" };
        check(ObserverInterval.Take(args) == TimeSpan.FromSeconds(1.5) && args.SequenceEqual(["logs", "-f"]),
            "--interval is removed from the arguments");
        check(ObserverInterval.Take(["ps", "-w"]) is null, "without --interval the caller keeps its pace");
        check(Throws(() => ObserverInterval.Take(["ps", "--interval"])) && Throws(() => ObserverInterval.Take(["--interval", "1", "--interval", "2"])),
            "--interval needs one value and appears once");

        check(new ObserverPreferences().Interval(ObserverInterval.Follow) == ObserverInterval.Follow &&
            new ObserverPreferences(IntervalSeconds: 3).Interval(ObserverInterval.Follow) == TimeSpan.FromSeconds(3),
            "the preference replaces each observer's default");

        var root = Path.Combine(Path.GetTempPath(), "coquena-interval-" + Guid.NewGuid().ToString("N"));
        try
        {
            var home = new Home(root);
            home.Ensure();
            var manager = new ConfigurationManager(home);
            using var output = new StringWriter();
            check(ConfigurationCli.Run("config", ["observer-interval", "set", "0.75"], output, TextWriter.Null, manager) == 0 &&
                manager.Observer().IntervalSeconds == 0.75, "config observer-interval set stores the interval");
            check(ConfigurationCli.Run("config", ["observer-retry", "set", "2", "10"], output, TextWriter.Null, manager) == 0 &&
                manager.Observer() == new ObserverPreferences(2, 10, 0.75), "changing the reconnection keeps the interval");
            check(ConfigurationCli.Run("config", ["observer-retry", "unset"], output, TextWriter.Null, manager) == 0 &&
                manager.Observer() == new ObserverPreferences(IntervalSeconds: 0.75), "resetting the reconnection keeps the interval");
            check(ConfigurationCli.Run("config", ["observer-interval", "set", "0.1"], TextWriter.Null, TextWriter.Null, manager) == 2 &&
                manager.Observer().IntervalSeconds == 0.75, "an interval out of range is rejected without changes");
            check(ConfigurationCli.Run("config", ["observer-interval", "unset"], output, TextWriter.Null, manager) == 0 &&
                manager.Observer() == new ObserverPreferences(), "unset returns to the defaults");
            check(!File.ReadAllText(home.PathFor("config.json")).Contains("observer", StringComparison.OrdinalIgnoreCase),
                "default preferences are not written");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }

        var original = ObservationTimeline.Interval;
        try
        {
            var start = DateTimeOffset.UnixEpoch;
            var timeline = new ObservationTimeline();
            timeline.Add(start, 1);
            timeline.Add(start.AddSeconds(1), 2);
            timeline.Add(start.AddSeconds(2), 3);
            check(timeline.Read(start.AddSeconds(2), 2).SequenceEqual(new double?[] { 2, 3 }), "two-second columns keep the last reading");
            ObservationTimeline.Interval = TimeSpan.FromSeconds(1);
            timeline.Add(start.AddSeconds(3), 4);
            check(timeline.Read(start.AddSeconds(3), 3).SequenceEqual(new double?[] { 2, 3, 4 }),
                "a new interval re-buckets the kept history");
        }
        finally { ObservationTimeline.Interval = original; }
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }
}
