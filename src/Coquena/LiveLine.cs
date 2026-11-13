namespace Coquena;

// One live progress line shared by every progress report of the process.
// Interactive terminal: the line is rewritten in place and animates the shared spinner,
// so a long step never looks frozen. Any ordinary output first closes the live line,
// which stays on screen with its last text. Redirected output: one plain line per update.
public static class LiveLine
{
    private static readonly object Gate = new();
    private static TextWriter? target;
    private static bool live;
    private static string? text;
    private static Timer? timer;

    // Called once at startup with the raw error stream; Console.Out and Console.Error
    // are then wrapped so that writing anything closes the live line first.
    public static void Install()
    {
        lock (Gate)
        {
            if (target is not null) return;
            target = Console.Error;
            live = !Console.IsErrorRedirected && Environment.GetEnvironmentVariable("TERM") != "dumb" &&
                Console.OutputEncoding.CodePage == 65001;
            Console.SetOut(new Closing(Console.Out));
            Console.SetError(new Closing(Console.Error));
        }
    }

    public static void Update(string line)
    {
        lock (Gate)
        {
            var writer = target ?? Console.Error;
            if (!live) { writer.WriteLine(line); return; }
            text = line;
            Draw(writer);
            timer ??= new Timer(_ => { lock (Gate) if (text is not null) Draw(target!); }, null, Spinner.PeriodMilliseconds, Spinner.PeriodMilliseconds);
        }
    }

    // Leaves the last text on screen and moves to a new line. Not named Close: inside the
    // writer below that name would resolve to TextWriter.Close and dispose the stream.
    public static void Finish()
    {
        lock (Gate)
        {
            if (text is null) return;
            text = null;
            timer?.Dispose(); timer = null;
            target!.WriteLine();
            target.Flush();
        }
    }

    private static void Draw(TextWriter writer)
    {
        writer.Write("\r\u001b[2K" + Spinner.Frame() + " " + text);
        writer.Flush();
    }

    private sealed class Closing(TextWriter inner) : TextWriter
    {
        public override System.Text.Encoding Encoding => inner.Encoding;
        public override void Write(char value) { Finish(); inner.Write(value); }
        public override void Write(string? value) { Finish(); inner.Write(value); }
        public override void Write(char[] buffer, int index, int count) { Finish(); inner.Write(buffer, index, count); }
        public override void WriteLine() { Finish(); inner.WriteLine(); }
        public override void WriteLine(string? value) { Finish(); inner.WriteLine(value); }
        public override void Flush() => inner.Flush();
    }
}
