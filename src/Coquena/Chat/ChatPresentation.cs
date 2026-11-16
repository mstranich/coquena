using System.Globalization;
using Coquena.Observation;

namespace Coquena.Chat;

// Rows of CH · Herramientas › Chat (docs/design/tui): history, two metric rows, the target
// selector and the actions. Metrics come from the server response of the last request.
internal static class ChatPresentation
{
    private const int SparkWidth = 10;

    // History rows wrapped to `width`: a header per turn and its text indented by two columns.
    public static IReadOnlyList<string> History(IReadOnlyList<ChatTurn> turns, int width)
    {
        var rows = new List<string>();
        var text = Math.Max(8, width - 3);
        foreach (var turn in turns)
        {
            rows.Add(" " + Header(turn));
            var body = turn.Text.ToString().TrimEnd();
            if (turn.Role == "assistant" && body.Length == 0 && turn.Reasoning.Length > 0)
                body = "(razonando) " + turn.Reasoning.ToString().Trim();
            var lines = body.Length == 0 ? [] : body.Replace("\r", "").Split('\n').SelectMany(line => DashboardPresentation.Wrap(line, text)).ToList();
            var live = turn.Phase == ChatPhase.Generating;
            for (var i = 0; i < lines.Count; i++)
                rows.Add("  " + (turn.Role == "assistant" && body.StartsWith("(razonando)", StringComparison.Ordinal) ? Markup.Style("mut", lines[i]) : Markup.Escape(lines[i])) +
                    (live && i == lines.Count - 1 ? "▌" : ""));
            if (turn.Problem is { } problem)
                foreach (var line in DashboardPresentation.Wrap(problem, text))
                    rows.Add("  " + Markup.Style(turn.Phase == ChatPhase.Cancelled ? "warn" : "err", line));
        }
        return rows;
    }

    public static string Header(ChatTurn turn)
    {
        var time = turn.At.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        if (turn.Role == "user") return Markup.Style("selt", "tú") + " " + Markup.Style("mut", time);
        var facts = new List<string> { time };
        if (turn.FirstToken is { } first) facts.Add("1.º " + Seconds(first.TotalSeconds));
        if (turn.Timings is { Predicted: > 0 } t) { facts.Add(t.Predicted.ToString(CultureInfo.InvariantCulture) + " tk"); facts.Add(ChatSession.Rate(t.PredictedRate) + " tk/s"); }
        var state = turn.Phase switch
        {
            ChatPhase.Waiting => DashboardPresentation.Spinner() + " esperando",
            ChatPhase.Prompt => DashboardPresentation.Spinner() + " procesando prompt",
            ChatPhase.Generating => DashboardPresentation.Spinner() + " generando",
            ChatPhase.Cancelled => "✗ interrumpida",
            ChatPhase.Failed => "✗ error",
            _ => ChatSession.FinishLabel(turn.Finish)
        };
        if (state.Length > 0) facts.Add(state);
        return Markup.Style("hi", "asistente") + " " + Markup.Style("mut", string.Join(" · ", facts));
    }

    // Two rows: phase, prompt rate and cache reuse; generated tokens, generation rate and
    // speculative acceptance. Values the server did not report show «—».
    public static string[] Metrics(ChatTurn? turn)
    {
        var t = turn?.Timings;
        var phase = turn is null ? Markup.Style("mut", "○ SIN PETICIÓN") : turn.Phase switch
        {
            ChatPhase.Waiting => Markup.Style("mut", DashboardPresentation.Spinner() + " ESPERANDO"),
            ChatPhase.Prompt => Markup.Style("pr", "● PROMPT " + Percent(turn.Progress?.Ratio)),
            ChatPhase.Generating => Markup.Style("gen", "● GENERANDO"),
            ChatPhase.Done => Markup.Style("ok", "✓ FIN"),
            ChatPhase.Cancelled => Markup.Style("warn", "✗ INTERRUMPIDA"),
            _ => Markup.Style("err", "✗ ERROR")
        };
        var cache = t is null || t.Cached + t.Prompt == 0 ? null : (double?)t.Cached / (t.Cached + t.Prompt);
        var acceptance = t?.Drafted is > 0 ? (double?)t.Accepted!.Value / t.Drafted.Value : null;
        var tokens = t is null ? "" : t.Predicted.ToString(CultureInfo.InvariantCulture) + " tk";
        return
        [
            " " + Pad(phase, 13) + Markup.Style("mut", "prompt ") + Markup.Style("pr", Spark(turn?.PromptRates)) + " " +
                Markup.Style("hi", Right(ChatSession.Rate(t?.PromptRate), 4)) + " " + Markup.Style("mut", "tk/s") + "  " +
                Markup.Style("mut", "caché ") + Value(cache),
            " " + Pad(Markup.Style("mut", tokens), 13) + Markup.Style("mut", "gen    ") + Markup.Style("gen", Spark(turn?.GenerationRates)) + " " +
                Markup.Style("hi", Right(ChatSession.Rate(t?.PredictedRate), 4)) + " " + Markup.Style("mut", "tk/s") + "  " +
                Markup.Style("mut", "acept. ") + (t?.Drafted is null ? Markup.Style("mut", "—") : Value(acceptance))
        ];
    }

    public static string Selector(string? service, string? entry, string? model, string? state)
    {
        var label = service is null ? "elegir servicio › preset" : service + " › " + (entry ?? "elegir preset");
        // The load state comes first, then the logical model name of the preset.
        return " " + Markup.Style("btn", " " + label + " ▾ ", "target") + (state is null ? "" : " " + state) +
            (model is null ? "" : " " + Markup.Style("mut", model));
    }

    public static string Actions(bool answering, bool canSend, bool hasDefinition) =>
        " " + (hasDefinition ? Markup.Style("btn", " Ir a la definición ", "definition") : "") + "{pad}" +
        (answering ? Markup.Style("btnf", " ■ Detener ", "send") : Markup.Style(canSend ? "btnf" : "btn", " Enviar ", "send"));

    // Normalized to the highest rate of the request so the curve shows its shape.
    public static string Spark(IReadOnlyList<double>? rates)
    {
        if (rates is not { Count: > 0 }) return new string('·', SparkWidth);
        var tail = rates.Skip(Math.Max(0, rates.Count - SparkWidth)).ToArray();
        var peak = tail.Max();
        var spark = LivePresentation.Spark(tail.Select(rate => peak <= 0 ? (double?)0 : rate / peak * 0.999));
        return spark.PadLeft(SparkWidth, ' ');
    }

    private static string Value(double? ratio) => ratio is { } r ? Markup.Style("ok", Percent(r)) : Markup.Style("mut", "—");
    private static string Percent(double? ratio) => ratio is { } r ? Math.Round(r * 100).ToString(CultureInfo.InvariantCulture) + "%" : "—";
    private static string Seconds(double value) => value.ToString(value >= 10 ? "0" : "0.0", CultureInfo.CurrentCulture) + " s";
    private static string Right(string text, int width) => text.PadLeft(width);
    private static string Pad(string markup, int width) => markup + new string(' ', Math.Max(1, width - Markup.Width(markup)));
}
