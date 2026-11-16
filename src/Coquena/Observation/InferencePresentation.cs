using System.Diagnostics;
using System.Globalization;

namespace Coquena.Observation;

// Formatting shared by the live list and detail; never substitutes entry means for task rates.
internal static class InferencePresentation
{
    public static IReadOnlyList<InferenceTask> Rows(InferenceTasks tasks, string scope, string service) =>
        tasks.Active.Where(task => task.Identity.Scope == scope && task.Identity.Service == service).OrderBy(task => task.Identity.Entry, StringComparer.Ordinal)
            .ThenBy(task => task.Identity.Slot)
            .Concat(tasks.History.Where(task => task.Identity.Scope == scope && task.Identity.Service == service).Reverse()).ToArray();

    // Scope and service remain part of the row identity across router replacements.
    private static string Number(double? value) => value?.ToString("0.#", CultureInfo.CurrentCulture) ?? Messages.DisplayNotAvailable.ToString();

    public static string Phase(InferenceTask task) => task.End switch
    {
        InferenceTaskEnd.LeftSlot => Messages.InferenceEndLeftSlot.ToString(),
        InferenceTaskEnd.ChildReplaced => Messages.InferenceEndChildReplaced.ToString(),
        InferenceTaskEnd.ObservationLost => Messages.InferenceEndSignalLost.ToString(),
        _ => task.Phase switch
        {
            InferenceTaskPhase.Prompt => "PROMPT",
            InferenceTaskPhase.Generation => Messages.InferencePhaseGeneration.ToString(),
            _ => Messages.InferencePhaseUncertain.ToString()
        }
    };

    public static string Row(InferenceTask task, int width)
    {
        var id = task.Identity;
        var rate = task.Phase == InferenceTaskPhase.Generation ? task.OutputRate : task.PromptRate;
        var tokens = task.Phase == InferenceTaskPhase.Generation ? $"G {task.Last.Decoded}" : $"P {task.Last.Prompt}";
        var text = $"{id.Entry} · s{id.Slot}/t{id.Task} · {Phase(task)} · {tokens} tk";
        if (width >= 75) text += $" · {Number(rate)} tk/s";
        if (width >= 115) text += Messages.InferenceRowCacheChild.Format(task.Last.Cached, id.Pid);
        return text;
    }

    public static string Detail(InferenceTask task)
    {
        var id = task.Identity;
        var at = DateTimeOffset.UtcNow - Stopwatch.GetElapsedTime(task.ObservedAt);
        return Messages.InferenceDetailEntryModel.Format(id.Entry, task.Model ?? Messages.DisplayNotAvailable.ToString()) +
            Messages.InferenceDetailScopeServiceInstance.Format(id.Scope, id.Service, id.Instance) +
            Messages.InferenceDetailChildSlotTaskSegment.Format(id.Pid, id.Creation, id.Slot, id.Task, id.FirstObserved) +
            Messages.InferenceDetailLastReading.Format(Phase(task), at.ToLocalTime()) +
            (task.End is null ? Messages.InferenceDetailTaskActive.ToString() : Messages.InferenceLastObservedValues.Format(Messages.RequestUnconfirmed)) + "\n" +
            Messages.InferenceDetailTokens.Format(task.Last.Prompt, task.Last.Cached, task.Last.Decoded) +
            Messages.InferenceDetailSlotContext.Format(task.Last.Context?.ToString() ?? Messages.DisplayNotAvailable.ToString(), task.Last.Speculative?.ToString() ?? Messages.DisplayNotAvailable.ToString()) +
            Messages.InferenceDetailPredictionLimit.Format(task.Last.PredictLimit?.ToString() ?? Messages.DisplayNotAvailable.ToString()) +
            Messages.InferenceDetailPromptRates.Format(Number(task.PromptRate), Number(task.PromptMean), Number(task.PromptPeak), task.PromptSeconds) +
            Messages.InferenceDetailGenerationRates.Format(Number(task.OutputRate), Number(task.OutputMean), Number(task.OutputPeak), task.OutputSeconds) +
            Messages.InferenceDetailSourceSlots.ToString() +
            Messages.InferenceDetailNoteRate.ToString() +
            Messages.InferenceDetailNoteMean.ToString() +
            Messages.InferenceDetailNotePeak.ToString() +
            Messages.InferenceDetailNoteTime.ToString() +
            Messages.InferenceProgressUnavailable.Format(Messages.NoProgressDenominator) +
            Messages.InferenceDetailNoteRemaining.ToString() +
            Messages.InferenceDetailNoteCharts.ToString();
    }
}
