using Coquena.Observation;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Coquena;

internal static partial class DashboardCli
{
    // Classes of the preview notation. A null background keeps the row's zone.
    private static (Color Foreground, Color? Background, bool Bold) Style(string? @class) => @class switch
    {
        "mut" => (Palette.Muted, null, false),
        "hi" => (Palette.Text, null, true),
        "ok" => (Palette.Success, null, false),
        "warn" => (Palette.Warning, null, false),
        "err" => (Palette.Danger, null, false),
        "gen" => (Palette.Gold, null, false),
        "pr" => (Palette.Information, null, false),
        "foc" => (Palette.Accent, null, false),
        "focb" => (Palette.Accent, Palette.Card, false),
        "sel" => (Palette.Text, Palette.Primary, true),
        "selt" => (Palette.SelectionText, null, true),
        "btn" => (Palette.Text, Palette.Border, false),
        "btnf" => (Palette.Base, Palette.Accent, true),
        "chg" => (Palette.Text, Palette.Card, false),
        _ => (Palette.Text, null, false)
    };

    private static Color Zone(string zone) => zone switch
    {
        "z1" or "bar" => Palette.Surface,
        "hd" or "chg" or "dlg" => Palette.Card,
        _ => Palette.Base
    };

    // Rows of markup drawn as-is. Spans with an action are buttons: click, or focus + Enter.
    private class Canvas : View
    {
        private IReadOnlyList<string> rows = [];
        private int focusedAction;
        protected readonly bool UseColor;
        public string ZoneName { get; set; } = "z0";
        public Func<int, string?>? RowZone { get; set; }
        public event Action<string>? Pressed;
        public string? DefaultAction { get; set; }

        public Canvas(bool useColor) { UseColor = useColor; Width = Dim.Fill(); }

        public IReadOnlyList<string> Rows
        {
            get => rows;
            set
            {
                if (rows.SequenceEqual(value)) return;
                rows = value.ToArray();
                SetNeedsDraw();
            }
        }

        protected IReadOnlyList<string> Actions => rows.SelectMany(row => Markup.Parse(row))
            .Where(span => span.Action is not null).Select(span => span.Action!).Distinct().ToArray();

        public bool Interactive { set => CanFocus = value; }

        protected virtual string RowAt(int y) => y < rows.Count ? rows[y] : "";
        protected virtual string? ZoneAt(int y) => RowZone?.Invoke(y);

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var actions = Actions;
            var focused = HasFocus && actions.Count > 0 ? actions[Math.Clamp(focusedAction, 0, actions.Count - 1)] : null;
            for (var y = 0; y < Viewport.Height; y++)
            {
                var zone = ZoneAt(y) ?? ZoneName;
                var background = Zone(zone);
                var bold = zone == "hd";
                Move(0, y);
                var used = 0;
                foreach (var span in Markup.Parse(Markup.Fit(RowAt(y), Viewport.Width)))
                {
                    var style = Style(span.Action is not null && span.Action == focused ? "btnf" : span.Class);
                    Paint(style.Foreground, style.Background ?? background, style.Bold || bold);
                    AddStr(span.Text);
                    used += span.Text.Length;
                }
            }
            return true;
        }

        private void Paint(Color foreground, Color background, bool bold)
        {
            if (!UseColor) { SetAttribute(new Attribute(Color.White, Color.Black)); return; }
            SetAttribute(new Attribute(foreground, background, bold ? TextStyle.Bold : TextStyle.None));
        }

        protected string? ActionAt(int x, int y)
        {
            var column = 0;
            foreach (var span in Markup.Parse(Markup.Fit(RowAt(y), Viewport.Width)))
            {
                if (x >= column && x < column + span.Text.Length) return span.Action;
                column += span.Text.Length;
            }
            return null;
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && !mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked) ||
                mouse.Position is not { } at) return base.OnMouseEvent(mouse);
            if (ActionAt(at.X, at.Y) is not { } action)
            {
                if (OnRowClicked(at.Y, mouse)) return true;
                if (DefaultAction is null) return base.OnMouseEvent(mouse);
                action = DefaultAction;
            }
            var index = Actions.ToList().IndexOf(action);
            if (index >= 0) focusedAction = index;
            mouse.Handled = true;
            Pressed?.Invoke(action);
            return true;
        }

        protected virtual bool OnRowClicked(int y, Mouse mouse) => false;

        protected override bool OnKeyDown(Key key)
        {
            var actions = Actions;
            if (actions.Count == 0) return base.OnKeyDown(key);
            if (key == Key.CursorRight || key == Key.CursorLeft)
            {
                focusedAction = (focusedAction + (key == Key.CursorRight ? 1 : actions.Count - 1)) % actions.Count;
                SetNeedsDraw();
                return true;
            }
            if (key == Key.Enter || key == Key.Space)
            {
                Pressed?.Invoke(actions[Math.Clamp(focusedAction, 0, actions.Count - 1)]);
                return true;
            }
            return base.OnKeyDown(key);
        }

        protected override void OnHasFocusChanged(bool newHasFocus, View? previousFocusedView, View? focusedView) => SetNeedsDraw();
    }

    // A scrollable list of markup rows with one selected row. Rows are produced on demand
    // so the selected row can use another style.
    private sealed class Lines : Canvas
    {
        private int count, top;
        private Func<int, bool, string> item = (_, _) => "";
        public int Selected { get; private set; } = -1;
        public bool Selectable { get; set; } = true;
        public bool ShowSelectionWithoutFocus { get; set; } = true;
        public event Action<int>? SelectionChanged;
        public event Action<int>? Chosen;
        public event Action<int>? Scrolled;

        public Lines(bool useColor) : base(useColor) { CanFocus = true; }

        public int Top => top;
        public int Count => count;
        public bool AtEnd => top >= Math.Max(0, count - Viewport.Height);

        public void Show(int rowCount, Func<int, bool, string> render, int? select = null)
        {
            count = rowCount;
            item = render;
            if (select is int row) Selected = row;
            Selected = Selectable && count > 0 ? Math.Clamp(Selected, 0, count - 1) : -1;
            Clamp();
            SetNeedsDraw();
        }

        public void ScrollToEnd() { top = Math.Max(0, count - Viewport.Height); SetNeedsDraw(); }

        public void ScrollTo(int row)
        {
            if (row < top) top = row;
            else if (row >= top + Viewport.Height) top = row - Viewport.Height + 1;
            Clamp();
            SetNeedsDraw();
        }

        public void Select(int row, bool notify = true)
        {
            if (!Selectable || count == 0) return;
            row = Math.Clamp(row, 0, count - 1);
            if (row == Selected) return;
            Selected = row;
            ScrollTo(row);
            if (notify) SelectionChanged?.Invoke(row);
        }

        private void Clamp() => top = Math.Clamp(top, 0, Math.Max(0, count - Math.Max(1, Viewport.Height)));

        protected override string RowAt(int y)
        {
            var row = top + y;
            return row < count ? item(row, row == Selected && (HasFocus || ShowSelectionWithoutFocus)) : "";
        }

        private void Scroll(int delta)
        {
            var before = top;
            top += delta;
            Clamp();
            if (top != before) { SetNeedsDraw(); Scrolled?.Invoke(top); }
        }

        protected override bool OnRowClicked(int y, Mouse mouse)
        {
            if (top + y >= count) return false;
            Select(top + y);
            if (Selectable && mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked)) Chosen?.Invoke(top + y);
            mouse.Handled = true;
            return true;
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (mouse.Flags.HasFlag(MouseFlags.WheeledDown)) { Scroll(3); return mouse.Handled = true; }
            if (mouse.Flags.HasFlag(MouseFlags.WheeledUp)) { Scroll(-3); return mouse.Handled = true; }
            return base.OnMouseEvent(mouse);
        }

        protected override bool OnKeyDown(Key key)
        {
            var page = Math.Max(1, Viewport.Height - 1);
            if (!Selectable)
            {
                if (key == Key.CursorDown) { Scroll(1); return true; }
                if (key == Key.CursorUp) { Scroll(-1); return true; }
                if (key == Key.PageDown) { Scroll(page); return true; }
                if (key == Key.PageUp) { Scroll(-page); return true; }
                if (key == Key.End) { Scroll(count); return true; }
                if (key == Key.Home) { Scroll(-count); return true; }
                return base.OnKeyDown(key);
            }
            if (key == Key.CursorDown) { Select(Selected + 1); return true; }
            if (key == Key.CursorUp) { Select(Selected - 1); return true; }
            if (key == Key.PageDown) { Select(Selected + page); return true; }
            if (key == Key.PageUp) { Select(Selected - page); return true; }
            if (key == Key.Home) { Select(0); return true; }
            if (key == Key.End) { Select(count - 1); return true; }
            if (key == Key.Enter && Selected >= 0) { Chosen?.Invoke(Selected); return true; }
            return base.OnKeyDown(key);
        }
    }
}
