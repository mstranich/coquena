using System.Globalization;
using System.Text;

namespace Coquena.Observation;

// One line with the live state of a service, drawn below followed logs.
internal static class LogStatus
{
    // GPU and VRAM come first: they are system-wide, like the TUI bar, because the OS gives no
    // verified per-instance reading. CPU and RAM belong to the service process.
    public static string Compose(string service, ServiceSnapshot? snapshot, IEnumerable<InferenceTask> active,
        HardwareSnapshot? hardware = null)
    {
        var parts = new List<string> { service };
        if (snapshot is null) { parts.Add(Messages.LogStatusWaiting); return string.Join(" · ", parts); }
        parts.Add(snapshot.ProcessState == "alive" ? snapshot.Health : snapshot.ProcessState);
        var number = 0;
        foreach (var device in hardware?.Devices ?? [])
        {
            double? Value(string name) => device.Samples.FirstOrDefault(sample => sample.Name == name)?.Value;
            var total = Value("vram.total");
            if (total is null) continue;
            var use = Value("gpu.utilization"); var used = Value("vram.used");
            parts.Add($"GPU{number++} " + (use is null ? "—" : use.Value.ToString("0", CultureInfo.CurrentCulture) + "%") + " " +
                (used is null ? "—" : DashboardPresentation.Compact(used.Value)) + "/" + DashboardPresentation.Compact(total.Value));
        }
        foreach (var metric in snapshot.Metrics)
        {
            if (metric.Value is not double value) continue;
            if (metric.Name == "cpu.utilization") parts.Add("CPU " + value.ToString("0", CultureInfo.CurrentCulture) + "%");
            else if (metric.Name == "ram.private-commit" && value <= long.MaxValue) parts.Add("RAM " + ByteSize.Format((long)value));
        }
        if (snapshot.ProcessState != "alive") return string.Join(" · ", parts);
        var loaded = snapshot.Entries.Where(entry => entry.State == "loaded").Select(entry => entry.Name).ToArray();
        parts.Add(loaded.Length == 0 ? Messages.LogStatusNoLoaded : string.Join(", ", loaded));
        var tasks = active.Where(task => task.End is null).OrderBy(task => task.Identity.Entry, StringComparer.Ordinal)
            .ThenBy(task => task.Identity.Slot).ToArray();
        if (tasks.Length == 0) parts.Add(Messages.LogStatusIdle);
        foreach (var task in tasks)
        {
            var rate = task.Phase == InferenceTaskPhase.Generation ? task.OutputRate : task.PromptRate;
            // A task still in its slot without decoded tokens is processing the prompt; the rate may come later.
            var phase = task.Last.Decoded > 0 ? Messages.InferencePhaseGeneration.ToString() : "PROMPT";
            var text = $"s{task.Identity.Slot} {phase}";
            if (rate is double r) text += " " + r.ToString("0.#", CultureInfo.CurrentCulture) + " tk/s";
            parts.Add(text);
        }
        return string.Join(" · ", parts);
    }

    // Cuts to the terminal width, leaving the last column free so the cursor never wraps.
    public static string Fit(string text, int width)
    {
        width = Math.Max(2, width);
        return text.Length >= width ? text[..(width - 2)] + "…" : text;
    }
}

// Keeps a status row at the foot of the terminal; complete lines are written above it.
// Partial lines wait for their end so the row never splits a log line. Warnings and
// errors take the glyph and color of the En vivo log unless colors are off (NO_COLOR).
internal sealed class StatusFooterWriter(TextWriter inner, Func<int> width, bool colors = true) : TextWriter
{
    private readonly object sync = new();
    private readonly StringBuilder pending = new();
    private string status = "";
    private bool closed;

    public override Encoding Encoding => inner.Encoding;

    public void SetStatus(string text)
    {
        lock (sync)
        {
            if (closed) return;
            status = text;
            inner.Write("\r" + Row() + "\u001b[K");
            inner.Flush();
        }
    }

    public override void Write(char value) => Write([value], 0, 1);
    public override void Write(string? value) { if (value is not null) Write(value.ToCharArray(), 0, value.Length); }

    public override void Write(char[] buffer, int index, int count)
    {
        lock (sync)
        {
            var output = new StringBuilder();
            for (var i = index; i < index + count; i++)
            {
                if (buffer[i] != '\n') { pending.Append(buffer[i]); continue; }
                var line = pending.ToString().TrimEnd('\r'); pending.Clear();
                output.Append("\r\u001b[K").Append(Tone(line)).Append("\r\n");
            }
            if (output.Length == 0 || closed) { if (closed && output.Length != 0) inner.Write(output.ToString()); return; }
            inner.Write(output.Append(Row()).Append("\u001b[K").ToString());
        }
    }

    public override void Flush() { lock (sync) inner.Flush(); }

    // Clears the row and releases a trailing partial line.
    public void End()
    {
        lock (sync)
        {
            if (closed) return;
            closed = true;
            inner.Write("\r\u001b[K" + (pending.Length == 0 ? "" : pending + Environment.NewLine));
            pending.Clear();
            inner.Flush();
        }
    }

    // Same palette as the TUI: Warning #E09F3E and Danger #E63946.
    private string Tone(string line) => !colors ? line : LivePresentation.Severity(line) switch
    {
        2 => "\u001b[38;2;230;57;70m✗ " + line.TrimStart() + "\u001b[0m",
        1 => "\u001b[38;2;224;159;62m▲ " + line.TrimStart() + "\u001b[0m",
        _ => line
    };

    private string Row() => "\u001b[2m" + LogStatus.Fit(status, width()) + "\u001b[0m";
}
