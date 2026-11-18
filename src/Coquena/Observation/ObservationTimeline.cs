namespace Coquena.Observation;

// A column is a time interval, including intervals without a reading.
// Each metric owns its samples while every graph uses the same UTC axis.
internal sealed class ObservationTimeline
{
    public const int Capacity = 24;
    // Reading pace of En vivo; columns are computed on read, so a change re-buckets the history.
    public static TimeSpan Interval { get; set; } = ObserverInterval.Default;
    private readonly List<(DateTimeOffset At, double? Value)> samples = [];

    public int Count => samples.Count;

    public void Clear() => samples.Clear();

    public void Add(DateTimeOffset observedAt, double? value)
    {
        if (samples.Count > 0 && observedAt <= samples[^1].At) return;
        var sample = (observedAt, value);
        if (samples.Count > 0 && Column(samples[^1].At) == Column(observedAt))
        {
            samples[^1] = sample;
            return;
        }
        if (samples.Count == Capacity) samples.RemoveAt(0);
        samples.Add(sample);
    }

    public double?[] Read(DateTimeOffset now, int width)
    {
        var count = Math.Clamp(width, 0, Capacity);
        var result = new double?[count];
        var end = Column(now);
        var start = end - count + 1;
        foreach (var sample in samples)
            if (Column(sample.At) is var column && column >= start && column <= end)
                result[(int)(column - start)] = sample.Value;
        return result;
    }

    public static long ColumnOf(DateTimeOffset time) => Column(time);

    private static long Column(DateTimeOffset time) =>
        (long)Math.Floor(time.ToUnixTimeMilliseconds() / Interval.TotalMilliseconds);
}
