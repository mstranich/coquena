using System.Diagnostics;

namespace Coquena.Observation;

internal sealed record InferenceSlot(int Slot, int Task, long Prompt, long Cached, long Decoded);
internal sealed record InferenceSnapshot(int Pid, long Creation, long Timestamp, IReadOnlyList<InferenceSlot> Active);

// Rates are deltas over observed wall time for one task and phase. Missing or
// ambiguous observations break the baseline instead of spanning unknown work.
internal sealed class InferenceSeries
{
    private (int Pid, long Creation, int Slot, int Task)? identity;
    private InferenceSlot? previous;
    private long timestamp;
    private bool idle;
    private long promptTokens, outputTokens;
    private double promptSeconds, outputSeconds;

    public InferenceSlot? Last { get; private set; }
    public double? PromptPeak { get; private set; }
    public double? OutputPeak { get; private set; }
    public double? PromptMean => promptSeconds > 0 ? promptTokens / promptSeconds : null;
    public double? OutputMean => outputSeconds > 0 ? outputTokens / outputSeconds : null;
    public double PromptSeconds => promptSeconds;
    public double OutputSeconds => outputSeconds;

    public (double? Prompt, double? Output, bool Reset) Observe(InferenceSnapshot? sample)
    {
        var changedChild = sample is not null && identity is { } key &&
            (key.Pid != sample.Pid || key.Creation != sample.Creation);
        if (changedChild) Reset();
        if (sample is null || sample.Active.Count != 1)
        {
            previous = null;
            idle |= sample?.Active.Count == 0;
            return (null, null, changedChild);
        }

        var slot = sample.Active[0];
        var current = (sample.Pid, sample.Creation, slot.Slot, slot.Task);
        var reset = changedChild || identity != current || idle ||
            (Last is { } last && (slot.Prompt < last.Prompt || slot.Decoded < last.Decoded));
        if (reset) Reset();
        idle = false;
        identity = current;
        Last = slot;
        double? prompt = null, output = null;
        if (previous is { } prior)
        {
            var seconds = Stopwatch.GetElapsedTime(timestamp, sample.Timestamp).TotalSeconds;
            if (seconds is > 0 and <= 3)
            {
                if (prior.Decoded == 0 && slot.Decoded == 0 && slot.Prompt > prior.Prompt)
                {
                    var delta = slot.Prompt - prior.Prompt;
                    prompt = delta / seconds;
                    promptTokens += delta;
                    promptSeconds += seconds;
                    PromptPeak = Math.Max(PromptPeak ?? 0, prompt.Value);
                }
                else if (prior.Decoded > 0 && slot.Decoded > prior.Decoded && slot.Prompt == prior.Prompt)
                {
                    var delta = slot.Decoded - prior.Decoded;
                    output = delta / seconds;
                    outputTokens += delta;
                    outputSeconds += seconds;
                    OutputPeak = Math.Max(OutputPeak ?? 0, output.Value);
                }
            }
        }
        previous = slot;
        timestamp = sample.Timestamp;
        return (prompt, output, reset);
    }

    private void Reset()
    {
        identity = null;
        previous = Last = null;
        PromptPeak = OutputPeak = null;
        promptTokens = outputTokens = 0;
        promptSeconds = outputSeconds = 0;
    }
}
