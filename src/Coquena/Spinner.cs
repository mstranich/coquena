namespace Coquena;

// Braille spinner shared by the CLI and the TUI: one frame each 120 ms.
public static class Spinner
{
    public const int PeriodMilliseconds = 120;
    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public static string Frame(long? milliseconds = null) =>
        Frames[(milliseconds ?? Environment.TickCount64) / PeriodMilliseconds % Frames.Length];
}

// Terminal glyphs with a plain-text fallback when the terminal cannot show them.
public sealed record Glyphs(string Ok, string Fail, string Warn, string Arrow, bool Animated)
{
    public static readonly Glyphs Unicode = new("✔", "✖", "⚠", "→", true);
    public static readonly Glyphs Text = new("[ok]", "[error]", "[!]", "->", false);

    // Unicode needs a UTF-8 console and a capable terminal; TERM=dumb keeps plain text.
    public static Glyphs ForConsole() =>
        Environment.GetEnvironmentVariable("TERM") == "dumb" || Console.OutputEncoding.CodePage != 65001 ? Text : Unicode;
}
