using System.Globalization;

namespace Coquena.Observation;

// Text for each dashboard zone. Values without a measurement are omitted instead of shown as n/d;
// navigation is built from observed catalogue names, never from an edited recipe.
internal static class DashboardPresentation
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4);
    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    // Braille spinner shared by every wait: one frame each 120 ms, the period of the animation timer.
    public static string Spinner(long? milliseconds = null) =>
        SpinnerFrames[(milliseconds ?? Environment.TickCount64) / 120 % SpinnerFrames.Length];

    public static IReadOnlyList<string> Wrap(string message, int width)
    {
        var rows = new List<string>();
        foreach (var line in message.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var row = "";
            foreach (var word in line.Split(' '))
            {
                if (row.Length > 0 && row.Length + word.Length + 1 > width)
                { rows.Add(row); row = ""; }
                row += (row.Length > 0 ? " " : "") + word;
            }
            rows.Add(row);
        }
        return rows;
    }

    // Keeps head and tail: sibling presets often differ only in their suffix.
    public static string Fit(string text, int width)
    {
        if (width <= 0) return "";
        if (text.Length <= width) return text;
        if (width < 4) return text[..width];
        var tail = (width - 1) / 2;
        return text[..(width - 1 - tail)] + "…" + text[^tail..];
    }

    public static string StateGlyph(string state) => state switch
    {
        "loaded" => "●",
        "loading" => "◐",
        "unloaded" => "○",
        "failed" => "✕",
        _ => "·"
    };

    public static string ProcessState(string state) => state switch
    {
        "alive" => "en línea",
        "stopped" => "detenido",
        "starting" => "iniciando",
        "unknown" => "estado desconocido",
        _ => state
    };

    internal sealed record Node(string Text, int Target, string? Entry = null);

    public static IReadOnlyList<Node> Tree(IReadOnlyList<DashboardCli.Target> targets,
        IReadOnlyDictionary<int, IReadOnlyList<EntrySnapshot>> catalogues, int width = 24, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        var rows = new List<Node>();
        for (var i = 0; i < targets.Count; i++)
        {
            rows.Add(new(Fit(targets[i].Label, width), i));
            if (!catalogues.TryGetValue(i, out var entries)) continue;
            // The prefix is a published namespace, not proof of client or process ownership.
            foreach (var group in entries.GroupBy(entry => entry.Name.Contains('/')
                         ? entry.Name[..entry.Name.LastIndexOf('/')] : "Sin prefijo", StringComparer.Ordinal))
            {
                rows.Add(new(Fit("  " + group.Key, width), i));
                foreach (var entry in group)
                {
                    var glyph = at - entry.ObservedAt > Lifetime ? "◌" : StateGlyph(entry.State);
                    rows.Add(new("    " + glyph + " " + Fit(entry.Name[(entry.Name.LastIndexOf('/') + 1)..], width - 6), i, entry.Name));
                }
            }
        }
        return rows;
    }

    // Process figures for the service row; each sample expires on its own.
    public static string Process(ServiceSnapshot snapshot, DateTimeOffset now)
    {
        var parts = new List<string>();
        if (Fresh(snapshot.Metrics, "cpu.utilization", now) is { } cpu)
            parts.Add("CPU " + cpu.ToString("0", CultureInfo.CurrentCulture) + "%");
        if (Fresh(snapshot.Metrics, "ram.private-commit", now) is { } ram)
            parts.Add("RAM " + Compact(ram));
        return string.Join(" · ", parts);
    }

    public static string Port(ServiceSnapshot snapshot) =>
        snapshot.ActiveComposition?["endpoint"]?["port"]?.ToString() is { } port ? ":" + port : "";

    public static string EntryHeader(EntrySnapshot? entry, string? model)
    {
        if (entry is null) return "Sin entrada observada";
        var parts = new List<string> { entry.Name, StateGlyph(entry.State) + " " + entry.State };
        if (model is not null) parts.Add(model);
        return string.Join(" · ", parts);
    }

    // Backend counters that the entry actually exposes; null when none was measured.
    public static string? EntryFacts(EntrySnapshot? entry)
    {
        double? Value(string name) => entry?.Metrics.FirstOrDefault(metric => metric.Name == "llamacpp:" + name)?.Value;
        string N(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);
        var parts = new List<string>();
        if (Value("requests_processing") is { } active) parts.Add("activas " + N(active));
        if (Value("requests_deferred") is { } queue) parts.Add("cola " + N(queue));
        if (Value("prompt_tokens_total") is { } prompt && Value("prompt_tokens_cached_total") is { } cached && prompt > 0)
            parts.Add("caché " + (cached / (prompt + cached) * 100).ToString("0", CultureInfo.CurrentCulture) + "%");
        if (Value("n_busy_slots_per_decode") is { } busy) parts.Add("slots/decode " + N(busy));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    // Backend means over an unreported period; never presented as the rate of a task.
    public static string? EntryMeans(EntrySnapshot? entry)
    {
        double? Value(string name) => entry?.Metrics.FirstOrDefault(metric => metric.Name == "llamacpp:" + name)?.Value;
        string N(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);
        var parts = new List<string>();
        if (Value("prompt_tokens_seconds") is { } p) parts.Add("P " + N(p));
        if (Value("predicted_tokens_seconds") is { } g) parts.Add("G " + N(g));
        return parts.Count == 0 ? null : "medias del backend · " + string.Join(" · ", parts) + " tk/s";
    }

    public static string Rate(double? current, double? mean, double? peak)
    {
        static string N(double? value) => value?.ToString("0.#", CultureInfo.CurrentCulture) ?? "—";
        var text = N(current) + " tk/s";
        if (mean is not null) text += " · media " + N(mean);
        if (peak is not null) text += " · pico " + N(peak);
        return text;
    }

    public static string Compact(double bytes) => bytes < 1024d * 1024 * 1024
        ? (bytes / (1024d * 1024)).ToString("0", CultureInfo.CurrentCulture) + "M"
        : (bytes / (1024d * 1024 * 1024)).ToString("0.#", CultureInfo.CurrentCulture) + "G";

    private static double? Fresh(IReadOnlyList<MetricSample> metrics, string name, DateTimeOffset now) =>
        metrics.FirstOrDefault(metric => metric.Name == name) is { Value: { } value } metric && metric.IsFresh(now, Lifetime)
            ? value : null;
}
