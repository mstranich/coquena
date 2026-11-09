namespace Coquena.Observation;

// Last minutes of one device metric. A column of the hardware view keeps the maximum
// of its interval: the question is whether use or memory saturated, not its average.
internal sealed class HardwareHistory
{
    public static readonly TimeSpan Span = TimeSpan.FromMinutes(5);
    private readonly List<(DateTimeOffset At, double? Value)> samples = [];

    public void Add(DateTimeOffset at, double? value)
    {
        if (samples.Count > 0 && at <= samples[^1].At) return;
        samples.Add((at, value));
        while (samples.Count > 0 && at - samples[0].At > Span) samples.RemoveAt(0);
    }

    public IEnumerable<double> Values => samples.Where(sample => sample.Value is not null).Select(sample => sample.Value!.Value);

    public double?[] Columns(DateTimeOffset now, int width)
    {
        var result = new double?[Math.Max(0, width)];
        if (width <= 0) return result;
        var start = now - Span;
        foreach (var (at, value) in samples)
        {
            if (value is null || at < start) continue;
            var column = Math.Clamp((int)((at - start).Ticks * width / Span.Ticks), 0, width - 1);
            result[column] = Math.Max(result[column] ?? double.MinValue, value.Value);
        }
        return result;
    }

    // First moment, within the span, when the value rose above `threshold`.
    public (DateTimeOffset At, double From)? RiseAbove(double threshold)
    {
        double? from = null;
        foreach (var (at, value) in samples)
        {
            if (value is null) continue;
            from ??= value;
            if (value > threshold && from <= threshold) return (at, from.Value);
        }
        return null;
    }
}
