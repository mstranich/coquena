using System.Text;

namespace Coquena.Observation;

// Styled terminal text with the notation of the TUI preview: {class|text} or {class@action|text}.
// Plain text must pass through Escape; {pad} expands to the spaces that complete a row.
internal static class Markup
{
    private const char Open = '', Close = '', Bar = '';

    internal sealed record Span(string? Class, string Text, string? Action);

    public static string Escape(string text) =>
        text.Replace('{', Open).Replace('}', Close).Replace('|', Bar);

    public static string Unescape(string text) =>
        text.Replace(Open, '{').Replace(Close, '}').Replace(Bar, '|');

    public static string Style(string @class, string text, string? action = null) =>
        "{" + @class + (action is null ? "" : "@" + action) + "|" + Escape(text) + "}";

    public static IReadOnlyList<Span> Parse(string line)
    {
        var spans = new List<Span>();
        var plain = new StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '{')
            {
                var bar = line.IndexOf('|', i);
                var end = bar < 0 ? -1 : line.IndexOf('}', bar);
                if (end > 0)
                {
                    if (plain.Length > 0) { spans.Add(new(null, Unescape(plain.ToString()), null)); plain.Clear(); }
                    var head = line[(i + 1)..bar];
                    var at = head.IndexOf('@');
                    spans.Add(new(at < 0 ? head : head[..at], Unescape(line[(bar + 1)..end]), at < 0 ? null : head[(at + 1)..]));
                    i = end;
                    continue;
                }
            }
            plain.Append(line[i]);
        }
        if (plain.Length > 0) spans.Add(new(null, Unescape(plain.ToString()), null));
        return spans;
    }

    public static int Width(string line) => Parse(line).Sum(span => span.Text.Length);

    public static string Plain(string line) => string.Concat(Parse(line).Select(span => span.Text));

    // Expands {pad} and fits the row to exactly `width` visible columns.
    public static string Fit(string line, int width)
    {
        var split = line.IndexOf("{pad}", StringComparison.Ordinal);
        if (split >= 0)
        {
            var left = line[..split];
            var right = line[(split + 5)..];
            var room = width - Width(right);
            left = Cut(left, Math.Max(0, room - 1));
            return left + new string(' ', Math.Max(0, room - Width(left))) + Cut(right, width);
        }
        var cut = Cut(line, width);
        return cut + new string(' ', Math.Max(0, width - Width(cut)));
    }

    private static string Cut(string line, int width)
    {
        var result = new StringBuilder();
        var used = 0;
        foreach (var span in Parse(line))
        {
            if (used >= width) break;
            var text = span.Text.Length + used > width ? span.Text[..(width - used)] : span.Text;
            used += text.Length;
            result.Append(span.Class is null ? Escape(text) : "{" + span.Class + (span.Action is null ? "" : "@" + span.Action) + "|" + Escape(text) + "}");
        }
        return result.ToString();
    }
}
