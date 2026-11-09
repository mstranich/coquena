using System.Collections.ObjectModel;
using System.Globalization;
using Coquena.Observation;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Coquena;

internal static partial class DashboardCli
{
    // Coquena web/showcase tokens. Keep semantic roles distinct even when they share a color.
    private static class Palette
    {
        public static bool Ansi16 { get; set; }
        public static Color Base => Ansi16 ? new(ColorName16.Black) : new("#14171A");
        public static Color Surface => Ansi16 ? new(ColorName16.Black) : new("#1E2328");
        public static Color Card => Ansi16 ? new(ColorName16.DarkGray) : new("#252B32");
        public static Color Border => Ansi16 ? new(ColorName16.DarkGray) : new("#3E4651");
        public static Color Text => Ansi16 ? new(ColorName16.White) : new("#EAEBED");
        public static Color Muted => Ansi16 ? new(ColorName16.Gray) : new("#88929A");
        public static Color Primary => Ansi16 ? new(ColorName16.Red) : new("#C85A32");
        public static Color Accent => Ansi16 ? new(ColorName16.BrightCyan) : new("#2EC4B6");
        public static Color Gold => Ansi16 ? new(ColorName16.BrightYellow) : new("#DDA15E");
        public static Color Success => Ansi16 ? new(ColorName16.BrightGreen) : new("#8FBF6A");
        public static Color Warning => Ansi16 ? new(ColorName16.BrightYellow) : new("#E09F3E");
        public static Color Danger => Ansi16 ? new(ColorName16.BrightRed) : new("#E63946");
        public static Color Information => Ansi16 ? new(ColorName16.BrightBlue) : new("#457B9D");
    }

    // One line: caption, session curve and current value. Used by the hardware bar and detail.
    private sealed class Meter : View
    {
        private readonly Label value;
        private readonly bool useColor;
        public UsageGraph Graph { get; }

        public Meter(string caption, bool useColor, int valueWidth, int? graphWidth = null, Color? tone = null)
        {
            Height = 1;
            this.useColor = useColor;
            var name = new Label { Text = caption, Width = caption.Length };
            Graph = new UsageGraph(useColor, tone: tone) { X = caption.Length, Height = 1 };
            value = new Label { Height = 1, Width = valueWidth, Text = "—" };
            if (graphWidth is int columns)
            {
                Graph.Width = columns;
                value.X = caption.Length + columns + 1;
                Width = caption.Length + columns + 1 + valueWidth;
            }
            else
            {
                Graph.Width = Dim.Fill(valueWidth + 1);
                value.X = Pos.AnchorEnd(valueWidth);
                Width = Dim.Fill();
            }
            Add(name, Graph, value);
            if (useColor) { Paint(this, Palette.Muted, Palette.Surface); Paint(value, Palette.Text, Palette.Surface); }
        }

        public void Show(string text, Color? color = null)
        {
            value.Text = text;
            if (useColor) Paint(value, color ?? Palette.Text, Palette.Surface);
            Graph.SetNeedsDraw();
        }
    }

    // Rate of the focused task: prompt or generation curve with its current, mean and peak.
    private sealed class RateLine : View
    {
        private readonly Label value;
        private readonly UsageGraph graph;

        public RateLine(string caption, bool useColor, Color tone)
        {
            Height = 1; Width = Dim.Fill();
            var name = new Label { Text = caption, Width = 6 };
            graph = new UsageGraph(useColor, tone: tone) { X = 6, Width = 24, Height = 1, Background = Palette.Base };
            value = new Label { X = 31, Width = Dim.Fill(), Height = 1 };
            Add(name, graph, value);
            if (useColor) { Paint(this, Palette.Muted, Palette.Base); Paint(value, tone, Palette.Base); }
        }

        public void Clear() => graph.Clear();

        public void Add(DateTimeOffset at, double? rate, double? peak)
        {
            var maximum = 1d;
            if (peak is > 1)
            {
                var step = Math.Max(1, Math.Pow(10, Math.Floor(Math.Log10(peak.Value)) - 1));
                maximum = Math.Ceiling(peak.Value / step) * step;
            }
            graph.AddTimedSample(at, rate, maximum);
        }

        public void Show(string text) { value.Text = text; graph.SetNeedsDraw(); }
    }

    private sealed class UsageGraph(bool useColor, string? symbols = null, Color? tone = null) : View
    {
        public Color Background { get; set; } = Palette.Surface;
        public Color Tone => tone ?? Palette.Gold;
        private readonly ObservationTimeline timedHistory = new();
        private double? maximum;

        public void Clear()
        {
            timedHistory.Clear();
            SetNeedsDraw();
        }

        public void AddTimedSample(DateTimeOffset observedAt, double? value, double? scale = null)
        {
            maximum = scale;
            timedHistory.Add(observedAt, value);
            SetNeedsDraw();
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            const string levels = "▁▂▃▄▅▆▇█";
            var column = 0;
            foreach (var sample in timedHistory.Read(DateTimeOffset.UtcNow, Viewport.Width))
            {
                var normalized = sample / (maximum ?? 1);
                var ratio = normalized is double value ? Math.Clamp(value, 0, 1) : 0;
                if (useColor)
                {
                    var color = maximum is null ? UsageColor(normalized, App?.Driver?.Force16Colors == true)
                        : sample is null ? Palette.Muted : Tone;
                    SetAttribute(new Terminal.Gui.Drawing.Attribute(color, Background));
                }
                Move(column++, 0);
                AddRune(sample is null ? '·' : symbols is not null ? symbols[(int)sample.Value] :
                    levels[Math.Min((int)(ratio * levels.Length), levels.Length - 1)]);
            }
            return true;
        }
    }

    private static double? Fraction(double? used, double? total) => total is > 0 ? used / total : null;

    private static Color UsageColor(double? usage, bool ansi16)
    {
        if (usage is not double value) return Palette.Muted;
        var ratio = Math.Clamp(value, 0, 1);
        if (ratio > 0.9) return ansi16 ? new Color(ColorName16.BrightRed) : Palette.Danger;
        return Palette.Gold;
    }

    private static string Percent(double? value) =>
        value is double number ? number.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "—";

    private static string Memory(double? value) =>
        value is double number ? ByteSize.Format((decimal)number) : "—";

    private static string StatsMemory(double? used) => Memory(used).Replace(" GiB", "G", StringComparison.Ordinal);

    private static (string Used, string Suffix) MemoryPair(double? used, double? total)
    {
        if (total is not > 0) return (Memory(used), "/—");
        var capacity = total.Value;
        var formatted = Memory(capacity);
        var split = formatted.LastIndexOf(' ');
        var unit = formatted[(split + 1)..];
        var power = Array.IndexOf(new[] { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" }, unit);
        var amount = used is double bytes ? (bytes / Math.Pow(1024, power)).ToString("0.##", CultureInfo.CurrentCulture) : "—";
        return (amount, "/" + formatted.Remove(split, 1));
    }

    private static void SetRows(ListView list, ref string? previous, string text)
    {
        if (previous == text) return;
        previous = text;
        list.SetSource(new ObservableCollection<string>(text.Split('\n')));
    }

    private static void Paint(View view, Color foreground, Color background) =>
        view.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(foreground, background))
        {
            Focus = new Terminal.Gui.Drawing.Attribute(Palette.Base, Palette.Accent)
        });
}
