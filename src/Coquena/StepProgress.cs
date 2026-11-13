namespace Coquena;

public enum StepState { Running, Done, Failed }

public sealed record StepRow(string Label, StepState State, string? Note);

// Compact step progress in the style of npm or Docker Compose, shared by the CLI and the TUI.
// The steps are the model; the console renderer and the TUI draw the same rows.
// Interactive console: each running step is one line with the spinner; parallel steps form a
// block redrawn together, and a finished step is fixed with ✔ or ✖.
// Redirected: plain lines when a step ends, without animation or control characters.
// Without output (TUI): nothing is written; the view reads Rows().
// Detail lines are shown only when the step fails, or always with verbose.
public sealed class StepProgress : IDisposable
{
    private readonly TextWriter? output;
    private readonly bool interactive;
    private readonly bool verbose;
    private readonly Glyphs glyphs;
    private readonly object gate = new();
    private readonly List<Step> steps = [];
    private readonly Timer? timer;
    private int drawn;

    public StepProgress(TextWriter? output, bool interactive, bool verbose, Glyphs? glyphs = null)
    {
        this.output = output;
        this.interactive = interactive && output is not null;
        this.verbose = verbose;
        this.glyphs = glyphs ?? (this.interactive ? Glyphs.ForConsole() : Glyphs.Text);
        if (this.interactive && this.glyphs.Animated)
            timer = new Timer(_ => { lock (gate) Redraw(); }, null, Spinner.PeriodMilliseconds, Spinner.PeriodMilliseconds);
    }

    // Steps kept for a view that draws them itself.
    public static StepProgress Silent() => new(null, false, false, Glyphs.Unicode);

    public static StepProgress ForConsole(TextWriter output, bool verbose) =>
        new(output, ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected &&
            Environment.GetEnvironmentVariable("TERM") != "dumb", verbose);

    public bool Verbose => verbose;

    public Step Begin(string label)
    {
        var step = new Step(this, label);
        lock (gate)
        {
            steps.Add(step);
            if (interactive) Redraw();
        }
        return step;
    }

    // Runs one step: fixed as done when the action returns, as failed when it throws.
    public async Task<T> Run<T>(string label, Func<Step, Task<T>> action)
    {
        var step = Begin(label);
        try { var result = await action(step); step.Done(); return result; }
        catch { step.Fail(); throw; }
    }

    private int held;

    // Pauses the animation while a question is on screen; the running block is redrawn after it.
    public IDisposable Hold()
    {
        lock (gate) { held++; if (interactive) Clear(); }
        return new Release(this);
    }

    private sealed class Release(StepProgress owner) : IDisposable
    {
        private bool done;
        public void Dispose()
        {
            if (done) return; done = true;
            lock (owner.gate) { owner.held--; owner.Redraw(); }
        }
    }

    // Writes a line above the running block, so ordinary output is not erased by the redraw.
    public void Print(string line, TextWriter? target = null)
    {
        lock (gate)
        {
            var writer = target ?? output;
            if (writer is null) return;
            if (interactive && held == 0) Clear();
            writer.WriteLine(line);
            writer.Flush();
            Redraw();
        }
    }

    public IReadOnlyList<StepRow> Rows()
    {
        lock (gate) return steps.Select(s => new StepRow(s.Label, s.State, s.Note)).ToArray();
    }

    // One row as both renderers show it.
    public static string Format(StepRow row, Glyphs glyphs, long? milliseconds = null) =>
        (row.State switch
        {
            StepState.Done => glyphs.Ok,
            StepState.Failed => glyphs.Fail,
            _ => glyphs.Animated ? Spinner.Frame(milliseconds) : "…"
        }) + " " + row.Label + (row.Note is null ? "" : " — " + row.Note);

    private void Detail(Step step, string line)
    {
        lock (gate)
        {
            if (!verbose || output is null) { step.Details.Add(line); return; }
            if (interactive && held == 0) Clear();
            output.WriteLine("  " + line);
            if (interactive) Redraw();
        }
    }

    private void End(Step step, StepState state, string? note)
    {
        lock (gate)
        {
            if (step.State != StepState.Running) return;
            step.State = state; step.Note = note;
            if (output is null) return;
            if (interactive) Clear();
            output.WriteLine(Format(new(step.Label, state, note), glyphs));
            if (state == StepState.Failed && !verbose) foreach (var line in step.Details) output.WriteLine("  " + line);
            if (interactive) Redraw();
            output.Flush();
        }
    }

    // Moves the cursor back over the running block and erases it.
    private void Clear()
    {
        if (drawn == 0) return;
        output!.Write($"\u001b[{drawn}F\u001b[0J");
        drawn = 0;
    }

    private void Redraw()
    {
        if (!interactive || held > 0) return;
        Clear();
        var running = steps.Where(s => s.State == StepState.Running).ToArray();
        foreach (var step in running) output!.WriteLine(Format(new(step.Label, StepState.Running, null), glyphs));
        drawn = running.Length;
        output!.Flush();
    }

    public void Dispose()
    {
        timer?.Dispose();
        lock (gate)
        {
            // A step left open was interrupted: fix it as failed so the screen is not left animated.
            foreach (var step in steps.Where(s => s.State == StepState.Running).ToArray()) End(step, StepState.Failed, "interrumpido");
        }
    }

    public sealed class Step
    {
        private readonly StepProgress owner;
        internal Step(StepProgress owner, string label) { this.owner = owner; Label = label; }
        public string Label { get; }
        internal StepState State { get; set; } = StepState.Running;
        internal string? Note { get; set; }
        internal List<string> Details { get; } = [];
        public void Log(string line) => owner.Detail(this, line);
        public void Done(string? note = null) => owner.End(this, StepState.Done, note);
        public void Fail(string? note = null) => owner.End(this, StepState.Failed, note);
        // A writer whose lines become details of this step.
        public TextWriter Writer() => new LineWriter(Log);
    }
}

// Forwards each written line to a callback; partial lines wait for their end.
internal sealed class LineWriter(Action<string> line) : TextWriter
{
    private readonly System.Text.StringBuilder pending = new();
    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    public override void Write(char value)
    {
        if (value == '\r') return;
        if (value != '\n') { pending.Append(value); return; }
        line(pending.ToString()); pending.Clear();
    }
    public override void Flush() { }
    protected override void Dispose(bool disposing)
    {
        if (pending.Length > 0) { line(pending.ToString()); pending.Clear(); }
        base.Dispose(disposing);
    }
}

// Model choices asked during a step pause the progress block.
internal sealed class HeldModelSelection(Coquena.Models.IModelSelectionInteraction inner, StepProgress steps) : Coquena.Models.IModelSelectionInteraction
{
    public bool IsInteractive => inner.IsInteractive;
    public void Show(string text) { using (steps.Hold()) inner.Show(text); }
    public async Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation)
    {
        using (steps.Hold()) return await inner.ChooseAsync(options, cancellation);
    }
}
