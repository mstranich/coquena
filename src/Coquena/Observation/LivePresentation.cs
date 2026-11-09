using System.Globalization;
using System.Text.RegularExpressions;
using static Coquena.Observation.Markup;

namespace Coquena.Observation;

// Rows of the En vivo tab in the notation of the TUI preview (docs/propuestas/tui-rediseno).
// Every function returns markup; a value without a measurement is left out, never shown as n/d.
internal static class LivePresentation
{
    public const int TreeWidth = 23;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4);
    private const string Levels = "▁▂▃▄▅▆▇█";

    public static string Number(double value, string format = "0.#") => value.ToString(format, CultureInfo.CurrentCulture);

    public static string Grouped(long value)
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberGroupSeparator = " ";
        return value.ToString("#,0", format);
    }

    public static string Tokens(long value) => value < 1000 ? value.ToString(CultureInfo.InvariantCulture) : Number(value / 1000d) + "k";

    public static string Spark(IEnumerable<double?> ratios) =>
        new(ratios.Select(ratio => ratio is double value ? Levels[Math.Clamp((int)(Math.Clamp(value, 0, 1) * Levels.Length), 0, Levels.Length - 1)] : ' ').ToArray());

    // Top bar: three tabs, downloads and notices. The indicators always keep their space.
    public static string Tabs(int active, int downloads, int notices)
    {
        string[] names = ["● En vivo", "◇ Definiciones", "≡ Configuración"];
        var tabs = string.Join(" ", names.Select((name, i) => i == active ? Style("sel", " " + name + " ", "tab" + i) : " " + Style("nav", name, "tab" + i) + " "));
        var download = Style(downloads > 0 ? "nav" : "mut", "⇣ " + downloads, "downloads");
        var notice = Style(notices > 0 ? "warn" : "mut", "▲ " + notices, "notices");
        return tabs + "{pad}" + download + "  " + notice + " ";
    }

    public static (string Glyph, string Class, string Text) ServiceState(ServiceSnapshot? snapshot)
    {
        if (snapshot is null) return ("·", "mut", "consultando");
        if (snapshot.ProcessState == "stopped") return ("○", "mut", "detenido");
        if (snapshot.Health.StartsWith("HTTP 5", StringComparison.Ordinal)) return ("✕", "err", "error · " + snapshot.Health);
        if (snapshot.ApiState == "available" && snapshot.Health.StartsWith("HTTP 2", StringComparison.Ordinal)) return ("●", "ok", "en línea");
        return ("▲", "warn", DashboardPresentation.ProcessState(snapshot.ProcessState) + " · API " + snapshot.ApiState);
    }

    // Title row of the right column: service, port, state and process figures.
    public static string ServiceHeader(string label, ServiceSnapshot? snapshot, string? note, DateTimeOffset now)
    {
        var text = Escape(label);
        if (snapshot is null) return text + (note is null ? "" : "  " + Style("mut", note));
        var port = DashboardPresentation.Port(snapshot);
        if (port.Length > 0) text += "  " + Style("mut", port);
        var (glyph, @class, state) = ServiceState(snapshot);
        text += "  " + Style(@class, glyph + " " + state);
        if (Fresh(snapshot.Metrics, "cpu.utilization", now) is { } cpu) text += "  " + Style("mut", "CPU") + " " + Number(cpu, "0") + "%";
        if (Fresh(snapshot.Metrics, "ram.private-commit", now) is { } ram) text += " " + Style("mut", "RAM") + " " + DashboardPresentation.Compact(ram);
        var queues = snapshot.Entries.Select(entry => Metric(entry, "requests_deferred")).OfType<double>().ToArray();
        if (queues.Length > 0) text += " " + Style("mut", "cola") + " " + Number(queues.Sum());
        return text;
    }

    public static string Controls(bool running, bool stopped, bool enabled)
    {
        if (!enabled) return "";
        if (running) return " " + Style("btn", " ■ Detener ", "stop") + "  " + Style("btn", " ⟳ Reiniciar ", "restart");
        return stopped ? " " + Style("btn", " ▸ Iniciar ", "start") : "";
    }

    internal sealed record TreeRow(string Normal, string Selected, int Target, string? Entry);

    // Service → project → preset. Loaded presets carry no glyph; the rest show their state.
    public static IReadOnlyList<TreeRow> Tree(IReadOnlyList<DashboardCli.Target> targets,
        IReadOnlyDictionary<int, IReadOnlyList<EntrySnapshot>> catalogues,
        IReadOnlyDictionary<int, ServiceSnapshot> snapshots, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        var rows = new List<TreeRow>();
        for (var i = 0; i < targets.Count; i++)
        {
            var (glyph, @class, _) = targets[i].Service is null ? ("○", "mut", "") : ServiceState(snapshots.GetValueOrDefault(i));
            catalogues.TryGetValue(i, out var entries);
            var expanded = entries is { Count: > 0 };
            var name = DashboardPresentation.Fit(targets[i].Name, TreeWidth - 4);
            var stopped = @class == "mut";
            rows.Add(new(" " + Style(expanded ? "ok" : "mut", (expanded ? "▾" : "▸")) + Style(@class, glyph) + " " +
                    Style(stopped ? "mut" : "hi", name),
                Selected(" " + (expanded ? "▾" : "▸") + glyph + " " + name), i, null));
            if (!expanded) continue;
            // The prefix is a published namespace, not proof of client or process ownership.
            foreach (var group in entries!.GroupBy(entry => entry.Name.Contains('/')
                         ? entry.Name[..entry.Name.LastIndexOf('/')] : "", StringComparer.Ordinal))
            {
                var indent = "   ";
                if (group.Key.Length > 0)
                {
                    var project = "   ▾ " + DashboardPresentation.Fit(group.Key, TreeWidth - 5);
                    rows.Add(new(Escape(project), Selected(project), i, null));
                    indent = "      ";
                }
                foreach (var entry in group)
                {
                    var leaf = entry.Name[(entry.Name.LastIndexOf('/') + 1)..];
                    var stale = at - entry.ObservedAt > Lifetime;
                    var state = stale ? "◌ " : entry.State == "loaded" ? "" : DashboardPresentation.StateGlyph(entry.State) + " ";
                    var text = DashboardPresentation.Fit(state + leaf, TreeWidth - indent.Length);
                    var normal = indent + (entry.State == "loaded" && !stale ? Escape(text)
                        : Style(entry.State == "failed" ? "err" : "mut", text));
                    rows.Add(new(normal, Selected(indent + text), i, entry.Name));
                }
            }
        }
        return rows;

        static string Selected(string text) => Style("focb", text.PadRight(TreeWidth));
    }

    public static string EntryHeader(EntrySnapshot? entry, string? model, bool differs)
    {
        if (entry is null) return differs ? " " + Style("warn", "≠ receta") : "";
        var text = " " + Style("hi", entry.Name);
        if (entry.State != "loaded") text += "  " + Style(entry.State == "failed" ? "err" : "mut", DashboardPresentation.StateGlyph(entry.State) + " " + entry.State);
        if (model is not null) text += "  " + Style("mut", ModelName(model));
        if (differs) text += "  " + Style("warn", "≠ receta");
        return text;
    }

    // The backend reports the model path; the row shows only the file name without extension.
    public static string ModelName(string model)
    {
        var name = model.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        return name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? name[..^5] : name;
    }

    // One curve of the task: prompt in blue, generation in gold, with the current value and peak.
    public static string Rate(string caption, string @class, IReadOnlyList<double?> rates, double? current, double? peak, int width, DateTimeOffset? end = null)
    {
        var scale = Math.Max(1, peak ?? rates.Max() ?? 1);
        var columns = rates.TakeLast(width).Select(rate => rate is null ? 0 : rate / scale).ToList();
        while (columns.Count < width) columns.Insert(0, null);
        var text = " " + Style("mut", caption.PadRight(6)) + " " + Style(@class, Spark(columns)) + " ";
        if (current is null && end is { } at) return text + (peak is null ? "" : Style("mut", "pico ") + Style("hi", Number(peak.Value, "0")) + " " + Style("mut", "tk/s · ")) +
            Style("mut", "fin " + at.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        text += current is null ? Style("mut", "—") : Style("hi", Number(current.Value, "0"));
        text += " " + Style("mut", "tk/s" + (peak is null ? "" : " pico " + Number(peak.Value, "0")));
        return text;
    }

    public static string? Facts(EntrySnapshot? entry)
    {
        var parts = new List<string>();
        if (Metric(entry, "prompt_tokens_total") is > 0 and var prompt && Metric(entry, "prompt_tokens_cached_total") is { } cached)
            parts.Add(Style("mut", "caché") + " " + Style("ok", Number(cached / (prompt + cached) * 100, "0") + "%"));
        if (Metric(entry, "spec_decode_num_draft_tokens_total") is > 0 and var drafted && Metric(entry, "spec_decode_num_accepted_tokens_total") is { } accepted)
            parts.Add(Style("mut", "aceptación") + " " + Style("ok", Number(accepted / drafted * 100, "0") + "%"));
        if (Metric(entry, "n_busy_slots_per_decode") is { } busy) parts.Add(Style("mut", "slots/decode") + " " + Number(busy));
        if (Metric(entry, "requests_processing") is { } active) parts.Add(Style("mut", "activas") + " " + Number(active));
        return parts.Count == 0 ? null : " " + string.Join("   ", parts);
    }

    // Context in use per active slot: prompt plus generated tokens over the slot context.
    public static string? Context(InferenceSnapshot? inference, int width)
    {
        if (inference is null) return null;
        var parts = new List<string>();
        var used = 1;
        foreach (var slot in inference.Active.Where(slot => slot.Context is > 0).OrderBy(slot => slot.Slot))
        {
            var tokens = slot.Prompt + slot.Decoded;
            var ratio = Math.Clamp(tokens / (double)slot.Context!.Value, 0, 1);
            var full = (int)(ratio * 10);
            var bar = new string('█', full) + (full < 10 && ratio * 10 - full > 0.05 ? "▒" : "");
            bar = bar.PadRight(10, '░');
            var part = Style("mut", (parts.Count == 0 ? "ctx s" : "s") + slot.Slot) + " " + Style(ratio > 0.9 ? "warn" : "foc", bar) +
                " " + Tokens(tokens) + "/" + Tokens(slot.Context.Value);
            var size = Width(part) + 3;
            if (used + size > width) break;
            used += size;
            parts.Add(part);
        }
        return parts.Count == 0 ? null : " " + string.Join("   ", parts);
    }

    public static string TaskHeader => " " + Style("mut", "TAREA     FASE        PROMPT  SALIDA    TTFT   FIN");

    // TTFT comes from the backend log (prompt eval time of the task); FIN is the last
    // observation of a finished task, the moment the curves left visible belong to.
    public static string Task(InferenceTask task, bool selected, double? ttft = null, DateTimeOffset? end = null)
    {
        var id = task.Identity;
        var (phase, @class) = task.End switch
        {
            InferenceTaskEnd.LeftSlot => ("✓ fin", "ok"),
            InferenceTaskEnd.ChildReplaced => ("▲ reemplazo", "warn"),
            InferenceTaskEnd.ObservationLost => ("▲ sin señal", "warn"),
            _ => task.Phase switch
            {
                InferenceTaskPhase.Generation => ("GENERANDO", "gen"),
                InferenceTaskPhase.Prompt => ("PROMPT", "pr"),
                _ => ("—", "mut")
            }
        };
        var name = $"s{id.Slot} t{id.Task}".PadRight(10);
        var prompt = Grouped(task.Last.Prompt).PadRight(8);
        var output = (task.Last.Decoded == 0 && task.Phase != InferenceTaskPhase.Generation ? "—"
            : Grouped(task.Last.Decoded) + (task.Last.PredictLimit is > 0 and var limit ? "/" + Grouped(limit) : "")).PadRight(10);
        var first = (ttft is { } ms ? Seconds(ms) : "—").PadRight(7);
        var finished = end is { } at ? at.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "";
        var text = name + phase.PadRight(12) + prompt + output + first + finished;
        return selected ? Style("focb", " " + text) : " " + Escape(name) + Style(@class, phase.PadRight(12)) + Escape(prompt + output) +
            Style(ttft is null ? "mut" : "text", first) + Style("mut", finished);
    }

    public static string Seconds(double milliseconds) => milliseconds < 1000
        ? Number(milliseconds, "0") + "ms" : Number(milliseconds / 1000, "0.0") + "s";

    // llama-server: "[port] … print_timing: id  0 | task 41 | prompt eval time =  812.3 ms / …".
    // The router prefix is the child's port, not its process; slot and task identify the row.
    public static (int Slot, int Task, double Milliseconds)? PromptTiming(string row)
    {
        var match = Regex.Match(row, @"print_timing: id\s+(\d+) \| task (\d+) \|\s*prompt eval time =\s*([\d.]+) ms", RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        return (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    public enum LogMode { Following, Paused, Historical, None }

    public static string LogHeader(string name, LogMode mode, long discarded, bool paused)
    {
        var state = mode switch
        {
            LogMode.Following => Style("ok", "● siguiendo"),
            LogMode.Paused => Style("warn", "❚❚ en pausa"),
            LogMode.Historical => Style("mut", "histórico"),
            _ => ""
        };
        var text = " " + Escape("Log " + name) + "  " + state + (discarded > 0 ? " " + Style("mut", "· " + discarded + " descartadas") : "");
        if (mode == LogMode.None) return text;
        return text + "{pad}" + Style("btn", paused ? " ▸ Reanudar " : " ❚❚ Pausar ", "pause") + " " +
            Style("btn", " ⤓ Seguir ", "follow") + " " + Style("btn", " Histórico ", "history");
    }

    // Log lines keep their text; only the severity adds a glyph and color.
    public static string LogRow(string raw)
    {
        var text = " " + raw;
        return Severity(raw) switch
        {
            2 => Style("err", " ✗ " + raw.TrimStart()),
            1 => Style("warn", " ▲ " + raw.TrimStart()),
            _ => Escape(text)
        };
    }

    // 0 information, 1 warning, 2 error. Based on the words the backend writes in the line.
    public static int Severity(string raw)
    {
        if (raw.Contains("error", StringComparison.OrdinalIgnoreCase) || raw.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains(" E ", StringComparison.Ordinal)) return 2;
        if (raw.Contains("warn", StringComparison.OrdinalIgnoreCase) || raw.Contains(" W ", StringComparison.Ordinal)) return 1;
        return 0;
    }

    internal sealed record Gauge(string Name, IReadOnlyList<double?> Ratios, double? Use, string? Memory, string? Extra, double? MemoryRatio);

    public static string UsageClass(double? ratio) => ratio is > 0.9 ? "warn" : "gen";

    // Hardware bar: CPU and RAM on the first row, one block per GPU on the second.
    public static (string First, string Second) HardwareBar(Gauge cpu, Gauge ram, IReadOnlyList<Gauge> gpus, bool waiting)
    {
        var first = " " + Style("mut@cpu", "CPU") + " " + Style(UsageClass(cpu.Use), Spark(Last(cpu.Ratios, 16))) + " " +
            (cpu.Use is null ? "—" : Number(cpu.Use.Value * 100, "0") + "%").PadRight(10) +
            Style("mut@ram", "RAM") + " " + Style(UsageClass(ram.Use), Spark(Last(ram.Ratios, 16))) + " " + (ram.Memory ?? "—");
        string second;
        if (gpus.Count == 0) second = " " + Style("mut@hardware", waiting ? "GPU · esperando lecturas" : "GPU · sin cómputo confirmado");
        else second = string.Concat(gpus.Select((gpu, i) => (i == 0 ? " " : "   ") + Style("mut@gpu" + i, gpu.Name) + " " +
            Style(UsageClass(gpu.Use), Spark(Last(gpu.Ratios, 5))) + " " + (gpu.Use is null ? "—" : Number(gpu.Use.Value * 100, "0") + "%") +
            (gpu.Memory is null ? "" : " " + Style(gpu.MemoryRatio is > 0.9 ? "warn" : "text", gpu.Memory)) +
            (gpu.Extra is null ? "" : " " + gpu.Extra)));
        return (first, second);
    }

    private static IEnumerable<double?> Last(IReadOnlyList<double?> ratios, int width)
    {
        var tail = ratios.TakeLast(width).ToList();
        while (tail.Count < width) tail.Insert(0, null);
        return tail;
    }

    internal static double? Metric(EntrySnapshot? entry, string name) =>
        entry?.Metrics.FirstOrDefault(metric => metric.Name == "llamacpp:" + name)?.Value;

    private static double? Fresh(IReadOnlyList<MetricSample> metrics, string name, DateTimeOffset now) =>
        metrics.FirstOrDefault(metric => metric.Name == name) is { Value: { } value } metric && metric.IsFresh(now, Lifetime)
            ? value : null;
}
