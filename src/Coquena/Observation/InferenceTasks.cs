using System.Diagnostics;

namespace Coquena.Observation;

internal sealed record InferenceTaskIdentity(
    string Scope, string Service, string Instance, string Entry, int Pid, long Creation, int Slot, int Task, long FirstObserved);

internal enum InferenceTaskPhase { Unknown, Prompt, Generation }

internal enum InferenceTaskEnd { LeftSlot, ChildReplaced, ObservationLost }

internal sealed record InferenceTask(
    InferenceTaskIdentity Identity, InferenceSlot Last, long ObservedAt,
    double? PromptRate, double? OutputRate, double? PromptMean, double? OutputMean,
    double? PromptPeak, double? OutputPeak, double PromptSeconds, double OutputSeconds,
    InferenceTaskEnd? End = null)
{
    public InferenceTaskPhase Phase => Last.Decoded > 0 ? InferenceTaskPhase.Generation :
        PromptRate is not null ? InferenceTaskPhase.Prompt : InferenceTaskPhase.Unknown;
}

// Updated from reconciled snapshots. Disappearance does not reveal the request outcome.
internal sealed class InferenceTasks(int historyLimit)
{
    private readonly Dictionary<(string Entry, int Slot), (InferenceTask Task, InferenceSeries Series)> active = [];
    private readonly Queue<InferenceTask> history = new();
    private (string Scope, string Id)? service;
    private string? instance;
    private DateTimeOffset? lastCapture;

    public IEnumerable<InferenceTask> Active => active.Values.Select(value => value.Task);
    public IReadOnlyCollection<InferenceTask> History => history;

    public void Observe(ServiceSnapshot snapshot)
    {
        var observedService = (snapshot.Scope, snapshot.ServiceId);
        if (service == observedService && lastCapture >= snapshot.StartedAt) return;
        if (service != observedService)
            Suspend();
        else if (instance is not null && snapshot.InstanceId is not null && instance != snapshot.InstanceId)
        {
            foreach (var key in active.Keys.ToArray()) End(key, InferenceTaskEnd.ChildReplaced);
        }
        service = observedService;
        instance = snapshot.InstanceId;
        lastCapture = snapshot.StartedAt;
        var entries = snapshot.Entries.Where(entry => entry.Inference is not null)
            .ToDictionary(entry => entry.Name, entry => entry.Inference!, StringComparer.Ordinal);

        if (instance is null) entries.Clear();
        foreach (var (key, tracked) in active.ToArray())
        {
            var task = tracked.Task;
            if (!entries.TryGetValue(key.Entry, out var source))
                End(key, InferenceTaskEnd.ObservationLost);
            else if (source.Timestamp > task.ObservedAt)
            {
                var identity = task.Identity;
                var slot = source.Active.FirstOrDefault(slot => slot.Slot == key.Slot);
                if (source.Pid != identity.Pid || source.Creation != identity.Creation)
                    End(key, InferenceTaskEnd.ChildReplaced);
                else if (Stopwatch.GetElapsedTime(task.ObservedAt, source.Timestamp).TotalSeconds > 3)
                    End(key, InferenceTaskEnd.ObservationLost);
                else if (slot is null || slot.Task != identity.Task)
                    End(key, InferenceTaskEnd.LeftSlot);
                else if (slot.Prompt < task.Last.Prompt || slot.Decoded < task.Last.Decoded)
                    End(key, InferenceTaskEnd.ObservationLost);
            }
        }

        if (instance is null) return;
        foreach (var (entry, source) in entries)
        {
            foreach (var slot in source.Active)
            {
                var key = (entry, slot.Slot);
                var (prior, series) = active.GetValueOrDefault(key);
                if (prior is not null && source.Timestamp <= prior.ObservedAt) continue;

                series ??= new();
                var identity = prior?.Identity ?? new(snapshot.Scope, snapshot.ServiceId, instance, entry, source.Pid,
                    source.Creation, slot.Slot, slot.Task, source.Timestamp);
                var (prompt, output) = series.Observe(slot, source.Timestamp);
                active[key] = (new(identity, slot, source.Timestamp, prompt, output,
                    series.PromptMean, series.OutputMean, series.PromptPeak, series.OutputPeak,
                    series.PromptSeconds, series.OutputSeconds), series);
            }
        }
    }

    public void Suspend()
    {
        foreach (var key in active.Keys.ToArray()) End(key, InferenceTaskEnd.ObservationLost);
        service = null;
        instance = null;
        lastCapture = null;
    }

    private void End((string Entry, int Slot) key, InferenceTaskEnd reason)
    {
        var task = active[key].Task;
        active.Remove(key);
        history.Enqueue(task with { End = reason });
        while (history.Count > historyLimit) history.Dequeue();
    }
}
