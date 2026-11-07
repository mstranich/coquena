namespace Coquena.Observation;

// A column is a time interval, including intervals without a reading.
// Each metric owns its samples while every graph uses the same UTC axis.
internal sealed class ObservationTimeline
{
    public const int Capacity = 24;
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private readonly List<(DateTimeOffset At, long Column, double? Value)> samples = [];

    public int Count => samples.Count;

    public void Clear() => samples.Clear();

    public void Add(DateTimeOffset observedAt, double? value)
    {
        if (samples.Count > 0 && observedAt <= samples[^1].At) return;
        var sample = (observedAt, Column(observedAt), value);
        if (samples.Count > 0 && samples[^1].Column == sample.Item2)
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
            if (sample.Column >= start && sample.Column <= end)
                result[(int)(sample.Column - start)] = sample.Value;
        return result;
    }

    private static long Column(DateTimeOffset time) =>
        (long)Math.Floor(time.ToUnixTimeMilliseconds() / Interval.TotalMilliseconds);
}
