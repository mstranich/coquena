using System.Diagnostics;

namespace Coquena.Observation;

internal sealed record InferenceSlot(int Slot, int Task, long Prompt, long Cached, long Decoded)
{
    public long? Context { get; init; }
    public bool? Speculative { get; init; }
    public long? PredictLimit { get; init; }
}
internal sealed record InferenceSnapshot(int Pid, long Creation, long Timestamp, IReadOnlyList<InferenceSlot> Active)
{
    public string? Model { get; init; }
}

// One contiguous task. Rates are deltas over observed wall time within a phase.
internal sealed class InferenceSeries
{
    private InferenceSlot? previous;
    private long timestamp;
    private long promptTokens, outputTokens;

    public double? PromptPeak { get; private set; }
    public double? OutputPeak { get; private set; }
    public double? PromptMean => PromptSeconds > 0 ? promptTokens / PromptSeconds : null;
    public double? OutputMean => OutputSeconds > 0 ? outputTokens / OutputSeconds : null;
    public double PromptSeconds { get; private set; }
    public double OutputSeconds { get; private set; }

    public (double? Prompt, double? Output) Observe(InferenceSlot slot, long observedAt)
    {
        double? prompt = null, output = null;
        if (previous is { } prior)
        {
            var seconds = Stopwatch.GetElapsedTime(timestamp, observedAt).TotalSeconds;
            if (seconds is > 0 and <= 3)
            {
                if (prior.Decoded == 0 && slot.Decoded == 0 && slot.Prompt > prior.Prompt)
                {
                    var delta = slot.Prompt - prior.Prompt;
                    prompt = delta / seconds;
                    promptTokens += delta;
                    PromptSeconds += seconds;
                    PromptPeak = Math.Max(PromptPeak ?? 0, prompt.Value);
                }
                else if (prior.Decoded > 0 && slot.Decoded > prior.Decoded && slot.Prompt == prior.Prompt)
                {
                    var delta = slot.Decoded - prior.Decoded;
                    output = delta / seconds;
                    outputTokens += delta;
                    OutputSeconds += seconds;
                    OutputPeak = Math.Max(OutputPeak ?? 0, output.Value);
                }
            }
        }
        previous = slot;
        timestamp = observedAt;
        return (prompt, output);
    }
}
