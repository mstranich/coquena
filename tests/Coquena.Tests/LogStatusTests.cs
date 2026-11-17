using Coquena.Observation;

namespace Coquena.Tests;

internal static class LogStatusTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        check(LogStatus.Compose("shared", null, []).StartsWith("shared · ", StringComparison.Ordinal) &&
            LogStatus.Compose("shared", null, []).EndsWith(Messages.LogStatusWaiting, StringComparison.Ordinal),
            "without a snapshot the line names the service and waits for observation");
        ServiceSnapshot Snapshot(string process, params EntrySnapshot[] entries) => new("service", "shared", "global", "instance", now, now,
            process, "available", "HTTP 200", "same", "none", true, null, null,
            [new("cpu.utilization", 12, "%", "fixture", "instance", now), new("ram.private-commit", 1024L * 1024 * 1024, "bytes", "fixture", "instance", now)],
            entries, [], false);
        var idle = LogStatus.Compose("shared", Snapshot("alive", new EntrySnapshot("code/chat", "unloaded", now, [])), []);
        check(idle.Contains("HTTP 200") && idle.Contains("CPU 12%") && idle.Contains("RAM ") &&
            idle.Contains(Messages.LogStatusNoLoaded) && idle.EndsWith(Messages.LogStatusIdle, StringComparison.Ordinal),
            "a live service shows health, CPU, RAM, no loaded presets and no tasks: " + idle);
        var loaded = LogStatus.Compose("shared", Snapshot("alive", new EntrySnapshot("code/chat", "loaded", now, [])), []);
        check(loaded.Contains("code/chat") && !loaded.Contains(Messages.LogStatusNoLoaded), "loaded presets are listed by name");
        var stopped = LogStatus.Compose("shared", Snapshot("stopped"), []);
        check(stopped.Contains("stopped") && !stopped.Contains(Messages.LogStatusIdle), "a stopped service shows its state without presets or tasks");
        var live = Snapshot("alive", new EntrySnapshot("code/chat", "loaded", now, []));
        InferenceTask Task(long decoded, double? prompt, double? output) => new(
            new("global", "service", "instance", "code/chat", 1, 1, 0, 7, 0), new(0, 7, 100, 0, decoded), 0,
            prompt, output, null, null, null, null, 0, 0);
        var fresh = LogStatus.Compose("shared", live, [Task(0, null, null)]);
        check(fresh.EndsWith("s0 PROMPT", StringComparison.Ordinal), "a task without decoded tokens or rate yet shows the prompt phase: " + fresh);
        var generating = LogStatus.Compose("shared", live, [Task(12, 400, 41.2)]);
        check(generating.EndsWith("s0 " + Messages.InferencePhaseGeneration + " " + 41.2.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " tk/s", StringComparison.Ordinal),
            "a generating task shows its output rate: " + generating);
        var hardware = new HardwareSnapshot("windows", now, now, [], [
            new("gpu-a", "RTX", [new("gpu.utilization", 91, "%", "fixture", "system", now), new("vram.used", 20.1 * 1024 * 1024 * 1024, "bytes", "fixture", "system", now),
                new("vram.total", 24d * 1024 * 1024 * 1024, "bytes", "fixture", "system", now)]),
            new("cpu-only", "Other", [new("gpu.utilization", 5, "%", "fixture", "system", now)])], []);
        var withGpu = LogStatus.Compose("shared", live, [], hardware);
        check(withGpu.Contains("GPU0 91% " + DashboardPresentation.Compact(20.1 * 1024 * 1024 * 1024) + "/24G") && !withGpu.Contains("GPU1") &&
            withGpu.IndexOf("GPU0", StringComparison.Ordinal) < withGpu.IndexOf("CPU", StringComparison.Ordinal),
            "GPUs with VRAM come before CPU and RAM, numbered like the TUI bar: " + withGpu);
        check(LogStatus.Fit("abcdefghij", 6) == "abcd…" && LogStatus.Fit("abc", 6) == "abc", "the line is cut to the width with an ellipsis");

        using var raw = new StringWriter();
        var footer = new StatusFooterWriter(raw, () => 40);
        footer.SetStatus("state");
        footer.Write("first line\npart");
        var afterFirst = raw.ToString();
        check(afterFirst.Contains("first line\r\n") && !afterFirst.Contains("part") && afterFirst.TrimEnd().EndsWith("state\u001b[0m\u001b[K", StringComparison.Ordinal),
            "complete lines go above the status row and a partial line waits");
        footer.Write("ial\n");
        check(raw.ToString().Contains("partial\r\n"), "a line split across writes is written whole");
        footer.Write("x E boom\nx W careful\nplain\n");
        var toned = raw.ToString();
        check(toned.Contains("\u001b[38;2;230;57;70m✗ x E boom\u001b[0m") && toned.Contains("\u001b[38;2;224;159;62m▲ x W careful\u001b[0m") &&
            toned.Contains("\u001b[Kplain\r\n"), "errors and warnings take the En vivo glyph and color; other lines stay plain");
        using var plainRaw = new StringWriter();
        var plain = new StatusFooterWriter(plainRaw, () => 40, colors: false);
        plain.Write("x E boom\n");
        check(plainRaw.ToString().Contains("\u001b[Kx E boom\r\n"), "without colors the line is written as is");
        footer.Write("tail");
        footer.End();
        check(raw.ToString().EndsWith("\r\u001b[Ktail" + Environment.NewLine, StringComparison.Ordinal), "ending clears the row and releases the trailing text");
    }
}
