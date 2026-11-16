using Coquena.Chat;
using Coquena.Observation;
using Terminal.Gui.App;
using Terminal.Gui.Editor;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Coquena;

internal static partial class DashboardCli
{
    // CH · Herramientas › Chat: history (takes the extra height), two metric rows, the
    // service › preset selector, a two-row message box and the actions, over the hardware bar.
    private sealed class ChatPage
    {
        private readonly IApplication app;
        private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        private CancellationTokenSource? answer;
        private bool following = true;

        public Lines History { get; }
        public Canvas Panel { get; }
        public Editor Input { get; }
        public Canvas Buttons { get; }
        public ChatSession? Session { get; private set; }
        public Task? Sending { get; private set; }
        public int? Target { get; private set; }
        public string? Service { get; private set; }
        public string? Entry { get; private set; }
        public string? Model { get; set; }
        public string? State { get; set; }
        public bool Answering => Sending is { IsCompleted: false };
        public IEnumerable<View> Views => [History, Panel, Input, Buttons];
        public event Action? Changed;

        public ChatPage(IApplication app, bool useColor, int left)
        {
            this.app = app;
            History = new Lines(useColor) { X = left, Y = 2, Selectable = false };
            Panel = new Canvas(useColor) { X = left, Height = 3, Interactive = true };
            Input = new Editor { X = left + 1, Height = 2, Width = Dim.Fill(1), WordWrap = true, GutterOptions = GutterOptions.None };
            Buttons = new Canvas(useColor) { X = left, Height = 1, Interactive = true };
            History.Scrolled += _ => following = History.AtEnd;
            if (useColor) Paint(Input, Palette.Accent, Palette.Card);
        }

        // Height left for the history: what the other zones do not use.
        public void Arrange(int content)
        {
            var history = Math.Max(1, content - 1 - 6);
            History.Height = history;
            Panel.Y = 2 + history;
            Input.Y = Panel.Y + 3;
            Buttons.Y = Input.Y + 2;
        }

        // Choosing another target starts a new session: one model's context is not sent to another.
        public void Choose(int target, string service, string? entry, string? model)
        {
            if (Target == target && Service == service && Entry == entry) return;
            Cancel();
            Target = target; Service = service; Entry = entry; Model = model;
            Session = null;
            following = true;
        }

        public void Clear()
        {
            Cancel();
            Session = null;
            following = true;
        }

        public void Cancel() => answer?.Cancel();

        public bool Send(Uri address)
        {
            var text = Input.Text.TrimEnd();
            if (text.Length == 0 || Entry is null || Answering) return false;
            if (Session is null || Session.Address != address) Session = new ChatSession(client, address, Entry);
            Input.Text = "";
            answer?.Dispose();
            answer = new CancellationTokenSource();
            following = true;
            var token = answer.Token;
            var session = Session;
            Sending = Task.Run(() => session.SendAsync(text, () => app.Invoke(() => Changed?.Invoke()), token));
            _ = Sending.ContinueWith(_ => app.Invoke(() => Changed?.Invoke()), TaskScheduler.Default);
            return true;
        }

        public void Render(int width)
        {
            List<string> rows;
            IReadOnlyList<string> metrics;
            // The answer is written on a worker thread: rows are built under the session's lock.
            lock (Session?.Gate ?? this)
            {
                var turns = Session?.Turns ?? [];
                rows = [.. ChatPresentation.History(turns, width)];
                metrics = ChatPresentation.Metrics(turns.LastOrDefault(turn => turn.Role == "assistant"));
            }
            if (rows.Count == 0)
                rows = [" " + Markup.Style("mut", Entry is null
                    ? "Elegir servicio › preset para empezar una prueba."
                    : "Escribir un mensaje. Enter envía · Shift+Enter nueva línea.")];
            History.Show(rows.Count, (row, _) => Markup.Fit(rows[row], width));
            if (following) History.ScrollToEnd();
            Panel.Rows = [.. metrics.Select(row => Markup.Fit(row, width)),
                Markup.Fit(ChatPresentation.Selector(Service, Entry, Model, State), width)];
            Buttons.Rows = [Markup.Fit(ChatPresentation.Actions(Answering, Entry is not null, Target is not null), width)];
        }

        public void Dispose()
        {
            Cancel();
            try { Sending?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
            client.Dispose();
        }
    }
}
