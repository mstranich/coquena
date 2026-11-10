using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Coquena.Backends;
using Coquena.Configuration;
using Coquena.Models;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Coquena;

internal static partial class DashboardCli
{
    private static readonly TimeSpan CaptureInterval = ObservationTimeline.Interval;
    private static readonly TimeSpan MetricLifetime = TimeSpan.FromSeconds(4);

    internal sealed record Target(ScopeStore Store, ServiceIdentity? Service, bool IsDefault,
        RecipeSelection? Recipe = null, string? ProjectName = null, bool DeclaresService = false, string? Destination = null, string? RecipeProblem = null)
    {
        public string Name => (IsDefault ? "★ " : "") + (ProjectName ?? Service?.Name);
        public string Label => (Store.Context.IsGlobal ? "◎ " : "◇ ") + (IsDefault ? "★ " : "") + (ProjectName ?? Service?.Name);
    }

    private sealed record ServicePanel(ServiceSnapshot Snapshot);

    private sealed record Notice(DateTimeOffset At, string Glyph, string Class, string Text, int Target);

    private sealed record Device(int Number, string Name, Gauge Use, Gauge Memory, Gauge Shared);

    // One hardware value: a 2 s timeline for the bar, five minutes for the hardware view.
    private sealed class Gauge
    {
        public ObservationTimeline Bar { get; } = new();
        public HardwareHistory History { get; } = new();
        public double? Ratio { get; private set; }
        public double? Value { get; private set; }
        public double? Peak { get; private set; }
        public double? First { get; private set; }
        public double? Total { get; set; }
        public string? Text { get; private set; }
        public DateTimeOffset? FirstRise { get; private set; }

        public void Add(DateTimeOffset at, double? ratio, double? value, string? text)
        {
            Bar.Add(at, ratio);
            History.Add(at, ratio);
            (Ratio, Value, Text) = (ratio, value, text);
            if (value is not { } current) return;
            First ??= current;
            Peak = Math.Max(Peak ?? current, current);
            if (FirstRise is null && current - First > 64d * 1024 * 1024) FirstRise = at;
        }
    }

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is ["--help"] or ["-h"])
        {
            output.WriteLine("Uso: coquena tui");
            return 0;
        }
        if (args.Length != 0 || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            error.WriteLine("Use coquena tui sin argumentos en una terminal interactiva.");
            return 2;
        }

        try { return RunDashboard(); }
        catch (Exception e) when (ServiceObservation.ObservationError(e))
        {
            error.WriteLine("TUI: " + e.Message);
            return 2;
        }
    }

    // Zones of the preview (docs/design/tui): tabs (row 0), a 23-column tree, the page on the
    // right, the service log under En vivo and the hardware bar (last two rows). Every page but
    // Configuración is drawn as markup rows; Definiciones is a read-only layer.
    internal static int RunDashboard(ISystemMetrics? metrics = null, HardwareProbe.AdapterEvidence? adapters = null)
    {
        const int Left = LivePresentation.TreeWidth, Right = Left + 1;
        const int Live = 0, Definitions = 1, Settings = 2, Hardware = 3, History = 4, Notices = 5;
        var targets = Discover(out var diagnostic);
        var preferences = new ConfigurationManager(Home.Resolve());
        var hardware = metrics ?? SystemMetrics.Create();
        using var stop = new CancellationTokenSource();
        using var app = Application.Create().Init();
        var useColor = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        Palette.Ansi16 = app.Driver?.Force16Colors == true;
        using var window = new Window { BorderStyle = LineStyle.None };

        var tabs = new Canvas(useColor) { Height = 1, ZoneName = "z1", Interactive = true };
        var leftTitle = new Canvas(useColor) { Y = 1, Width = Left, Height = 1, ZoneName = "hd" };
        var rightTitle = new Canvas(useColor) { X = Right, Y = 1, Height = 1, ZoneName = "hd" };
        var tree = new Lines(useColor) { Y = 2, Width = Left, ZoneName = "z1" };
        var detail = new Canvas(useColor) { X = Right, Y = 2, Interactive = true };
        var tasks = new Lines(useColor) { X = Right, ShowSelectionWithoutFocus = false };
        var logTitle = new Canvas(useColor) { Height = 1, ZoneName = "hd", Interactive = true };
        var logLines = new Lines(useColor) { Selectable = false };
        var bar = new Canvas(useColor) { Y = Pos.AnchorEnd(2), Height = 2, ZoneName = "bar", Interactive = true, DefaultAction = "hardware" };
        var hardwareDetail = new Canvas(useColor) { X = Right, Y = 2 };
        var historyTitle = new Canvas(useColor) { Y = 1, Height = 1, ZoneName = "hd", Interactive = true };
        var historyControls = new Canvas(useColor) { Y = 2, Height = 2, ZoneName = "z1", Interactive = true };
        var regex = new TextField { X = 53, Y = 2, Width = Dim.Fill(1) };
        var historyLines = new Lines(useColor) { Y = 4, Selectable = false };
        var historyFooter = new Canvas(useColor) { Height = 1, ZoneName = "z1" };
        var noticeTitle = new Canvas(useColor) { Y = 1, Height = 1, ZoneName = "hd", Interactive = true };
        var noticeList = new Lines(useColor) { Y = 2, ShowSelectionWithoutFocus = false };
        var definitionDetail = new Canvas(useColor) { X = Right, Y = 2 };

        // Publication controls kept for the recipe actions; Definiciones shows them disabled.
        var pageView = new View { X = Right, Y = 1, Width = Dim.Fill(), Visible = false };
        var recipeRow = new View { Y = 1, Width = Dim.Fill(), Height = 1 };
        var upButton = Action(" ▲ Up ");
        var downButton = Action(" ▼ Down ");
        downButton.X = Pos.Right(upButton) + 1;
        var privatePreparation = new CheckBox { X = Pos.Right(downButton) + 2, Text = "Preparación privada" };
        var recipeTarget = new Label { X = Pos.Right(privatePreparation) + 2, Width = Dim.Fill(), Height = 1 };
        recipeRow.Add(upButton, downButton, privatePreparation, recipeTarget);
        pageView.Add(recipeRow);
        // Configuración: Coquena and TUI are input forms; Sistema is drawn as markup rows.
        var settings = new SettingsForm(app, preferences, Home.Resolve(), useColor);
        var settingsView = new View { X = Right, Y = 2, Width = Dim.Fill(), CanFocus = true };
        var systemDetail = new Canvas(useColor) { Interactive = true };
        settingsView.Add(settings.Coquena, settings.Tui, systemDetail);

        window.Add(tabs, leftTitle, rightTitle, tree, detail, tasks, logTitle, logLines, hardwareDetail,
            historyTitle, historyControls, regex, historyLines, historyFooter, noticeTitle, noticeList, definitionDetail, pageView, settingsView, bar);
        if (useColor)
        {
            Paint(window, Palette.Text, Palette.Base);
            Paint(pageView, Palette.Text, Palette.Base);
            Paint(settingsView, Palette.Text, Palette.Base);
            Paint(recipeRow, Palette.Muted, Palette.Base);
            Paint(privatePreparation, Palette.Text, Palette.Base);
            Paint(recipeTarget, Palette.Muted, Palette.Base);
            Paint(regex, Palette.Accent, Palette.Card);
        }

        // State.
        var page = Live;
        var settingsCategory = 0;
        var hardwareSelection = 0;
        var index = Math.Max(0, targets.FindIndex(target => target.Recipe is not null));
        var catalogues = new Dictionary<int, IReadOnlyList<EntrySnapshot>>();
        var snapshots = new Dictionary<int, ServiceSnapshot>();
        IReadOnlyList<LivePresentation.TreeRow> treeRows = [];
        var logSessions = new Dictionary<int, ServiceLogSession>();
        var retiredLogs = new List<Task>();
        var projectClock = Stopwatch.StartNew();
        var layoutSize = (-1, -1);
        var upperRows = 11;
        var logHeld = false;
        long logDiscarded = 0;
        string[] logRows = [];
        string? serviceNote = null;
        var selectionEpoch = 0;
        var probe = adapters is null ? Task.Run(HardwareProbe.InspectAdapters) : Task.FromResult(adapters);
        HardwareProbe.AdapterEvidence? evidence = null;
        DateTimeOffset? probedAt = null;
        HardwareSnapshot? lastHardware = null;
        Task<HardwareSnapshot>? machineSample = null;
        var machineClock = new Stopwatch();
        var serviceSamples = new Dictionary<int, (int Epoch, Task<ServicePanel> Task)>();
        var serviceCaptures = new Dictionary<int, Stopwatch>();
        Task<string?>? operation = null;
        ServiceSnapshot? selectedSnapshot = null;
        var inference = new InferenceTasks(preferences.InferenceHistory());
        string? preferredEntry = null, inferenceEntry = null;
        IReadOnlyList<InferenceTask> visibleTasks = [];
        InferenceTaskIdentity? preferredTask = null;
        // Hardware: a 2 s timeline for the bar and five minutes for the hardware view.
        var cpuGauge = new Gauge();
        var ramGauge = new Gauge();
        var devices = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);
        // Log histórico.
        ServiceLog? historyFile = null;
        var historyLevel = 0;
        var historyMatch = -1;
        int[] historyMatches = [];
        string[] historyRows = [];
        string? historyProblem = null;
        // Notificaciones: what this session observed; nothing is persisted.
        var notices = new List<Notice>();
        var unread = 0;
        var lastStates = new Dictionary<int, string>();
        // Last slots seen with context per entry: they stay visible until the next generation.
        // Prompt processing time reported by the backend log, per slot and task.
        var promptTimes = new Dictionary<(int Slot, int Task), double>();
        var lastContext = new Dictionary<string, InferenceSnapshot>(StringComparer.Ordinal);
        // Definiciones: scopes, the focused one and its view; pages are reread every capture interval.
        var definitionSource = new DefinitionsSource(Home.Resolve(), Environment.CurrentDirectory);
        var places = definitionSource.Places();
        var focusedPlace = Math.Max(0, places.FindIndex(place => place.Scope.Here));
        var definitionView = DefinitionsPresentation.Views(places[focusedPlace].Scope).FirstOrDefault();
        IReadOnlyList<DefinitionsPresentation.Node> definitionNodes = [];
        DefinitionsPresentation.Page? definitionPage = null;
        var definitionClock = new Stopwatch();

        // Events.
        tabs.Pressed += action =>
        {
            switch (action)
            {
                case "tab0": Open(Live); break;
                case "tab1": Open(Definitions); break;
                case "tab2": Open(Settings); break;
                case "notices": Open(Notices); break;
                case "downloads": break;
            }
        };
        bar.Pressed += action =>
        {
            var gpus = devices.Count;
            hardwareSelection = action switch
            {
                "cpu" => gpus,
                "ram" => gpus + 1,
                _ when action.StartsWith("gpu", StringComparison.Ordinal) => int.Parse(action[3..], CultureInfo.InvariantCulture),
                _ => hardwareSelection
            };
            Open(Hardware);
        };
        tree.SelectionChanged += row =>
        {
            switch (page)
            {
                case Settings: settingsCategory = row; Render(); return;
                case Hardware: hardwareSelection = row; Render(); return;
                case Definitions:
                    if (row < 0 || row >= definitionNodes.Count) return;
                    var chosen = definitionNodes[row];
                    if (chosen.Scope != focusedPlace || chosen.View is null)
                    {
                        focusedPlace = chosen.Scope;
                        definitionView = chosen.View ?? DefinitionsPresentation.Views(places[focusedPlace].Scope).FirstOrDefault();
                    }
                    else definitionView = chosen.View;
                    definitionPage = null;
                    Render();
                    return;
            }
            if (row < 0 || row >= treeRows.Count) return;
            var node = treeRows[row];
            preferredEntry = node.Entry;
            preferredTask = null;
            if (index != node.Target) { index = node.Target; selectionEpoch++; Select(); }
            Render();
        };
        tree.Chosen += row =>
        {
            if (page != Live || row >= treeRows.Count || treeRows[row].Entry is not { } name ||
                selectedSnapshot?.Entries.FirstOrDefault(entry => entry.Name == name) is not { } entry) return;
            ShowDetail("Entrada · " + entry.Name, EntryDetail(entry));
        };
        tasks.SelectionChanged += row =>
        {
            if (row < 0 || row >= visibleTasks.Count) return;
            preferredTask = visibleTasks[row].Identity;
            Render();
        };
        tasks.Chosen += row =>
        {
            if (row < 0 || row >= visibleTasks.Count) return;
            ShowDetail("Tarea · último valor observado", InferencePresentation.Detail(visibleTasks[row]));
        };
        detail.Pressed += action =>
        {
            if (action == "stop") BeginServiceAction(stopService: true);
            else if (action is "start" or "restart") BeginServiceAction(stopService: false);
        };
        logTitle.Pressed += action =>
        {
            switch (action)
            {
                case "pause": logHeld = !logHeld; break;
                case "follow":
                    if (logSessions.TryGetValue(index, out var session)) session.Current();
                    logHeld = false; logLines.ScrollToEnd(); break;
                case "history": OpenHistory(); return;
            }
            ReadLog(force: true);
        };
        logLines.Scrolled += _ => { if (!logLines.AtEnd && !logHeld) { logHeld = true; Render(); } };
        historyTitle.Pressed += action => { if (action == "back") CloseHistory(); };
        historyControls.Pressed += action =>
        {
            switch (action)
            {
                case "instance":
                    if (!logSessions.TryGetValue(index, out var session)) return;
                    var files = session.Files;
                    var choice = Choose(app, "Instancia · " + targets[index].Label, files.Length == 0 ? ["Sin archivos registrados."]
                        : files.Select((file, i) => $"i{i + 1} · {file.StartedAt}{(file.Active ? " → ahora" : "")}").ToArray(),
                        files.Length > 0 ? "Abrir" : null);
                    if (choice is int row && row < files.Length) ShowHistoryFile(files[row]);
                    return;
                case "level": historyLevel = (historyLevel + 1) % 3; historyMatch = -1; break;
                case "previous": MoveMatch(-1); break;
                case "next": MoveMatch(1); break;
            }
            RenderHistory();
        };
        regex.TextChanged += (_, _) => { historyMatch = -1; RenderHistory(); };
        regex.KeyDown += (_, key) => { if (key == Key.Enter) { key.Handled = true; MoveMatch(1); RenderHistory(); } };
        noticeTitle.Pressed += action =>
        {
            if (action == "read") unread = 0;
            else if (action == "back") Open(Live);
            Render();
        };
        noticeList.Pressed += action =>
        {
            if (!action.StartsWith('n') || !int.TryParse(action[1..], CultureInfo.InvariantCulture, out var row) || row >= notices.Count) return;
            OpenNotice(notices[notices.Count - 1 - row]);
        };
        noticeList.Chosen += row => { if (row < notices.Count) OpenNotice(notices[notices.Count - 1 - row]); };
        upButton.Accepted += (_, _) => BeginPublication("up");
        downButton.Accepted += (_, _) => BeginPublication("down");
        window.KeyDown += (_, key) =>
        {
            if (key != Key.Esc || page is Live or Definitions or Settings) return;
            key.Handled = true;
            if (page == History) CloseHistory(); else Open(Live);
        };
        settings.FilterLogs.ValueChanged += (_, _) => ReadLog(force: true);
        settings.Changed += () => { inference.HistoryLimit = settings.HistoryLimit; RenderBar(DateTimeOffset.UtcNow); };
        systemDetail.Pressed += action =>
        {
            if (action != "probe" || !probe.IsCompleted) return;
            probe = Task.Run(HardwareProbe.InspectAdapters);
            Render();
        };

        Select();
        app.AddTimeout(TimeSpan.FromSeconds(1), () =>
        {
            Tick();
            return true;
        });
        try { app.Run(window); }
        finally
        {
            stop.Cancel();
            Task.WhenAll(logSessions.Values.Select(session => session.CloseAsync()).Concat(retiredLogs)).GetAwaiter().GetResult();
            var observations = Task.WhenAll(serviceSamples.Values.Select(value => (Task)value.Task)
                .Concat(machineSample is null ? [] : new Task[] { machineSample }));
            _ = observations.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            try { observations.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            catch (Exception e) when (ServiceObservation.ObservationError(e) || e is TimeoutException) { }
            try { operation?.GetAwaiter().GetResult(); }
            catch (Exception e) when (ServiceObservation.ObservationError(e)) { }
        }
        return 0;

        Button Action(string text)
        {
            var button = new Button { Text = text, NoDecorations = true, ShadowStyle = ShadowStyles.None };
            if (useColor) Paint(button, Palette.Text, Palette.Border);
            return button;
        }

        void Open(int selected)
        {
            if (page == History && selected != History) LeaveHistory();
            if (selected == Notices) unread = 0;
            if (selected == Definitions)
            {
                places = definitionSource.Places();
                focusedPlace = Math.Clamp(focusedPlace, 0, places.Count - 1);
                definitionPage = null;
            }
            page = selected;
            Arrange();
            Render();
            (page switch { Hardware or Settings or Live or Definitions => tree, Notices => noticeList, _ => (View)historyLines }).SetFocus();
        }

        void Tick()
        {
            if (operation is { IsCompleted: true } completed)
            {
                operation = null;
                selectedSnapshot = null;
                serviceNote = null;
                RefreshProject();
                Select();
                var problem = completed.IsCompletedSuccessfully ? completed.Result : completed.Exception?.GetBaseException().Message;
                if (!string.IsNullOrWhiteSpace(problem))
                {
                    Notify("✗", "err", targets.ElementAtOrDefault(index)?.Label + ": " + problem.Split('\n')[0], index);
                    Query(app, "Resultado de la operación", problem, "Aceptar");
                }
            }
            if (operation is null && projectClock.Elapsed >= CaptureInterval)
            {
                projectClock.Restart();
                if (RefreshProject()) Select();
                retiredLogs.RemoveAll(task => task.IsCompletedSuccessfully);
            }
            if (probe.IsCompletedSuccessfully && evidence != probe.Result) { evidence = probe.Result; probedAt = DateTimeOffset.Now; }
            SampleHardware();
            foreach (var (targetIndex, pending) in serviceSamples.Where(item => item.Value.Task.IsCompleted).ToArray())
            {
                serviceSamples.Remove(targetIndex);
                var selected = operation is null && targetIndex == index && pending.Epoch == selectionEpoch;
                if (!selected) continue;
                if (pending.Task.IsCompletedSuccessfully) Show(pending.Task.Result.Snapshot);
                else if (pending.Task.IsFaulted)
                {
                    selectedSnapshot = null;
                    snapshots.Remove(index);
                    serviceNote = "observación no disponible · " + pending.Task.Exception!.GetBaseException().Message;
                    inference.Suspend();
                }
            }
            if (operation is null && index < targets.Count && targets[index].Service is not null && serviceSamples.Count < 4 &&
                !serviceSamples.ContainsKey(index) &&
                (!serviceCaptures.TryGetValue(index, out var lastCapture) || lastCapture.Elapsed >= CaptureInterval))
            {
                var target = targets[index];
                var clock = new Stopwatch();
                serviceCaptures[index] = clock;
                serviceSamples.Add(index, (selectionEpoch, Task.Run(() => { clock.Start(); return CaptureAsync(target, stop.Token); })));
            }
            if ((window.Viewport.Width, window.Viewport.Height) != layoutSize) Arrange();
            ReadLog();
            Render();
        }

        // Rows per zone. 80×24 is the minimum; extra height is split between the upper zone
        // (tasks) and the log, extra width goes to the right column.
        void Arrange()
        {
            layoutSize = (window.Viewport.Width, window.Viewport.Height);
            var height = window.Viewport.Height;
            var content = Math.Max(6, height - 3);
            var upper = upperRows = Math.Min(content - 3, 11 + Math.Max(0, height - 24) / 2);
            bool live = page == Live, hardwareView = page == Hardware, history = page == History, notice = page == Notices;
            var settingsPage = page == Settings;
            var definitions = page == Definitions;

            // Configuración has no title over its categories, so the tree starts one row higher.
            leftTitle.Visible = live || hardwareView || definitions;
            rightTitle.Visible = live || hardwareView || definitions || settingsPage;
            tree.Visible = live || hardwareView || settingsPage || definitions;
            tree.Y = settingsPage ? 1 : 2;
            tree.Height = live ? upper - 1 : content - (settingsPage ? 0 : 1);
            detail.Visible = tasks.Visible = logTitle.Visible = logLines.Visible = live;
            detail.Height = upper - 1;
            tasks.Height = 0;
            logTitle.Y = 1 + upper;
            logLines.Y = 2 + upper;
            logLines.Height = content - upper - 1;
            hardwareDetail.Visible = hardwareView;
            hardwareDetail.Height = content - 1;
            definitionDetail.Visible = definitions;
            definitionDetail.Height = content - 1;
            historyTitle.Visible = historyControls.Visible = regex.Visible = historyLines.Visible = historyFooter.Visible = history;
            historyLines.Height = content - 4;
            historyFooter.Y = content;
            noticeTitle.Visible = noticeList.Visible = notice;
            noticeList.Height = content - 1;
            recipeRow.Visible = false;
            settingsView.Visible = settingsPage;
            settingsView.Height = content - 1;
            settings.Coquena.Visible = settingsCategory == 0;
            settings.Tui.Visible = settingsCategory == 1;
            systemDetail.Visible = settingsCategory == 2;
            systemDetail.Width = Dim.Fill();
            systemDetail.Height = Dim.Fill();
            foreach (var view in window.SubViews) { view.SetNeedsLayout(); view.SetNeedsDraw(); }
        }

        void Select()
        {
            var target = targets.ElementAtOrDefault(index);
            if (target?.Service is { } service && !logSessions.ContainsKey(index))
                logSessions[index] = new ServiceLogSession(new ServiceLogReader(target.Store, service.ServiceId), stop.Token);
            logHeld = false;
            logRows = [];
            selectedSnapshot = null;
            inference.Suspend();
            preferredTask = null;
            inferenceEntry = null;
            serviceNote = target is null ? null : target.Service is null
                ? target.DeclaresService ? "○ sin iniciar" : "sin servicio propio" : "consultando…";
            if (target?.Recipe is not null)
                recipeTarget.Text = target.RecipeProblem is { } problem ? "receta no disponible: " + problem : "→ " + target.Destination;
            upButton.Enabled = downButton.Enabled = operation is null;
            ReadLog(force: true);
            Arrange();
            Render();
        }

        void Show(ServiceSnapshot snapshot)
        {
            snapshot = snapshot with { Entries = snapshot.Entries.OrderByDescending(entry => entry.Inference?.Active.Count > 0)
                .ThenByDescending(entry => entry.State == "loaded").ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray() };
            selectedSnapshot = snapshot;
            snapshots[index] = snapshot;
            serviceNote = null;
            var (glyph, @class, state) = LivePresentation.ServiceState(snapshot);
            if (lastStates.TryGetValue(index, out var previous) && previous != @class)
                Notify(glyph, @class, $"{targets[index].Label}: {state}", index);
            lastStates[index] = @class;
            inference.Observe(snapshot);
            catalogues[index] = snapshot.Entries;
        }

        void Notify(string glyph, string @class, string text, int target)
        {
            notices.Add(new(DateTimeOffset.Now, glyph, @class, text, target));
            if (notices.Count > 100) notices.RemoveAt(0);
            if (page != Notices) unread++;
        }

        void OpenNotice(Notice notice)
        {
            if (notice.Target != index && notice.Target < targets.Count) { index = notice.Target; preferredEntry = null; selectionEpoch++; Select(); }
            Open(Live);
        }

        void Render()
        {
            var now = DateTimeOffset.UtcNow;
            tabs.Rows = [LivePresentation.Tabs(page is Definitions ? 1 : page is Settings ? 2 : 0, 0, unread)];
            RenderBar(now);
            switch (page)
            {
                case Live: RenderLive(now); break;
                case Hardware: RenderHardware(now); break;
                case History: RenderHistory(); break;
                case Notices: RenderNotices(); break;
                case Definitions: RenderDefinitions(); break;
                default: RenderSettings(); break;
            }
        }

        void RenderNavigation()
        {
            treeRows = LivePresentation.Tree(targets, catalogues, snapshots);
            var current = treeRows.ToList().FindIndex(node => node.Target == index && node.Entry == preferredEntry);
            if (current < 0) current = treeRows.ToList().FindIndex(node => node.Target == index && node.Entry is null);
            if (treeRows.Count == 0) tree.Show(1, (_, _) => " " + Markup.Escape(diagnostic));
            else tree.Show(treeRows.Count, (row, selected) => selected ? treeRows[row].Selected : treeRows[row].Normal, Math.Max(0, current));
        }

        void RenderLive(DateTimeOffset now)
        {
            RenderNavigation();
            var target = targets.ElementAtOrDefault(index);
            var width = Math.Max(20, window.Viewport.Width - Right);
            leftTitle.Rows = [""];
            rightTitle.Rows = [" " + LivePresentation.ServiceHeader(target is null ? diagnostic : target.Name, selectedSnapshot,
                operation is not null ? serviceNote : selectedSnapshot is null ? serviceNote : null, now)];

            var rows = new List<string>();
            var running = selectedSnapshot is { ProcessState: "alive", InstanceId: not null };
            var stopped = selectedSnapshot?.ProcessState == "stopped" || target is { Service: null, DeclaresService: true };
            var controls = LivePresentation.Controls(running, stopped, operation is null && target is not null);
            if (controls.Length > 0) rows.Add(controls);

            // Entry and task in focus.
            visibleTasks = target?.Service is { } service ? InferencePresentation.Rows(inference,
                target.Store.Context.IsGlobal ? "global" : target.Store.Context.Base, service.ServiceId)
                .Where(task => preferredEntry is null || task.Identity.Entry == preferredEntry).ToArray() : [];
            var observed = selectedSnapshot?.Entries ?? [];
            var selectedTask = visibleTasks.FirstOrDefault(task => task.Identity == preferredTask);
            var entry = observed.FirstOrDefault(item => item.Name == selectedTask?.Identity.Entry)
                ?? observed.FirstOrDefault(item => item.Name == preferredEntry)
                ?? observed.FirstOrDefault(item => item.Inference?.Active.Count > 0)
                ?? observed.FirstOrDefault(item => item.Name == inferenceEntry)
                ?? observed.FirstOrDefault(item => item.Inference is not null)
                ?? observed.FirstOrDefault();
            inferenceEntry = selectedTask?.Identity.Entry ?? entry?.Name ?? inferenceEntry;
            var display = selectedTask
                ?? inference.Active.FirstOrDefault(task => task.Identity.Entry == inferenceEntry)
                ?? visibleTasks.FirstOrDefault(task => task.Identity.Entry == inferenceEntry);
            var differs = selectedSnapshot is { CompositionCurrent: false };
            if (selectedSnapshot is not null)
            {
                var header = LivePresentation.EntryHeader(entry, entry?.Inference?.Model ?? display?.Model, differs);
                if (header.Length > 0) rows.Add(header);
                if (display is not null)
                {
                    var current = display.End is null;
                    var spark = Math.Max(8, Math.Min(ObservationTimeline.Capacity, width - 30));
                    rows.Add(LivePresentation.Rate("prompt", "pr", display.Points.Select(point => point.Prompt).ToArray(),
                        current ? display.PromptRate : null, display.PromptPeak, spark, current ? null : Ended(display)));
                    rows.Add(LivePresentation.Rate("gen", "gen", display.Points.Select(point => point.Output).ToArray(),
                        current ? display.OutputRate : null, display.OutputPeak, spark, current ? null : Ended(display)));
                }
                if (LivePresentation.Facts(entry) is { } facts) rows.Add(facts);
                if (entry?.Inference is { } slots && slots.Active.Any(slot => slot.Context is > 0)) lastContext[index + "/" + entry.Name] = slots;
                if (LivePresentation.Context(entry is null ? null : lastContext.GetValueOrDefault(index + "/" + entry.Name), width - 1) is { } context) rows.Add(context);
                if (entry is null) rows.Add(" " + Markup.Style("mut", "Sin entradas publicadas"));
                else if (entry.Inference is null) rows.Add(" " + Markup.Style("mut", "Sin señal /slots" +
                    (entry.InferenceDiagnostic is { } reason ? " · " + DashboardPresentation.Fit(reason, width - 20) : "")));
                if (visibleTasks.Count > 0) rows.Add(LivePresentation.TaskHeader);
            }
            var available = Math.Max(1, upperRows - 1);
            var detailRows = Math.Min(rows.Count, available);
            detail.Height = detailRows;
            detail.Rows = rows.Take(detailRows).ToArray();
            tasks.Y = 2 + detailRows;
            tasks.Height = Math.Max(0, available - detailRows);
            var focus = visibleTasks.ToList().FindIndex(task => task.Identity == preferredTask);
            tasks.Show(visibleTasks.Count, (row, selected) => LivePresentation.Task(visibleTasks[row], selected,
                promptTimes.TryGetValue((visibleTasks[row].Identity.Slot, visibleTasks[row].Identity.Task), out var ttft) ? ttft : null,
                visibleTasks[row].End is null ? null : Ended(visibleTasks[row])), focus < 0 ? null : focus);

            RenderLog();
        }

        void ReadLog(bool force = false)
        {
            if (!logSessions.TryGetValue(index, out var session))
            {
                logRows = [targets.ElementAtOrDefault(index)?.Service is null ? "Sin servicio que registre logs." : "Consultando últimas líneas…"];
                logDiscarded = 0;
                return;
            }
            if (logHeld && !force) return;
            var frame = session.Read(settings.FilterLogs.Value == CheckState.Checked);
            logDiscarded = frame.Discarded;
            foreach (var row in frame.Rows)
                if (LivePresentation.PromptTiming(row) is { } timing) promptTimes[(timing.Slot, timing.Task)] = timing.Milliseconds;
            if (promptTimes.Count > 2000) promptTimes.Clear();
            logRows = frame.Rows.Length == 0 || frame.Rows is [""] ? ["Sin líneas visibles."] : frame.Rows;
        }

        void RenderLog()
        {
            var session = logSessions.GetValueOrDefault(index);
            var mode = session is null ? LivePresentation.LogMode.None : logHeld ? LivePresentation.LogMode.Paused
                : session.Historical ? LivePresentation.LogMode.Historical : LivePresentation.LogMode.Following;
            logTitle.Rows = [LivePresentation.LogHeader(targets.ElementAtOrDefault(index)?.Service?.Name ?? "", mode, logDiscarded, logHeld)];
            var rows = logRows;
            logLines.Show(rows.Length, (row, _) => LivePresentation.LogRow(rows[row]));
            if (!logHeld) logLines.ScrollToEnd();
        }

        void OpenHistory()
        {
            if (!logSessions.TryGetValue(index, out var session)) return;
            historyFile = session.Files.LastOrDefault(file => file.Active) ?? session.Files.LastOrDefault();
            historyMatch = -1;
            Open(History);
            regex.SetFocus();
        }

        void ShowHistoryFile(ServiceLog file)
        {
            if (!logSessions.TryGetValue(index, out var session)) return;
            historyFile = file;
            historyMatch = -1;
            if (file.Active) session.Current(); else session.SelectHistory(file.Id);
        }

        void LeaveHistory()
        {
            if (logSessions.TryGetValue(index, out var session)) session.Current();
            historyFile = null;
            logHeld = false;
        }

        void CloseHistory() { LeaveHistory(); Open(Live); }

        void MoveMatch(int step)
        {
            if (historyMatches.Length == 0) return;
            historyMatch = historyMatch < 0 ? (step > 0 ? 0 : historyMatches.Length - 1)
                : (historyMatch + step + historyMatches.Length) % historyMatches.Length;
            historyLines.ScrollTo(historyMatches[historyMatch]);
        }

        void RenderHistory()
        {
            if (page != History) return;
            var session = logSessions.GetValueOrDefault(index);
            if (session is not null) historyRows = session.Read(settings.FilterLogs.Value == CheckState.Checked).Rows;
            var files = session?.Files ?? [];
            var name = targets.ElementAtOrDefault(index)?.Service?.Name ?? "";
            historyTitle.Rows = [" " + Markup.Escape("Log " + name + " · histórico") + "{pad}" + Markup.Style("btn", " Volver ", "back") + " "];
            var number = historyFile is null ? 0 : Array.FindIndex(files, file => file.Id == historyFile.Id) + 1;
            var instance = historyFile is null ? " sin archivos ▾ "
                : $" i{number} · {historyFile.StartedAt}{(historyFile.Active ? " → ahora" : "")} ▾ ";
            Regex? pattern = null;
            historyProblem = null;
            if (regex.Text.Length > 0)
            {
                try { pattern = new Regex(regex.Text, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)); }
                catch (ArgumentException) { historyProblem = "regex inválida"; }
            }
            var rows = historyRows.Where(row => LivePresentation.Severity(row) >= historyLevel).ToArray();
            try { historyMatches = pattern is null ? [] : rows.Select((row, i) => (row, i)).Where(item => pattern.IsMatch(item.row)).Select(item => item.i).ToArray(); }
            catch (RegexMatchTimeoutException) { historyMatches = []; historyProblem = "regex demasiado costosa"; }
            if (historyMatch >= historyMatches.Length) historyMatch = -1;
            var level = historyLevel switch { 1 => "▲ y ✗", 2 => "✗", _ => "todos" };
            var count = historyProblem ?? (pattern is null ? "" : historyMatches.Length + " coincidencias");
            historyControls.Rows =
            [
                Markup.Fit(" " + Markup.Style("mut", "instancia") + " " + Markup.Style("focb", DashboardPresentation.Fit(instance, 40), "instance"), 47) + Markup.Style("mut", "regex "),
                " " + Markup.Style("mut", "nivel") + " " + Markup.Style("nav", level + " ▾", "level") + "   " + Markup.Style(historyProblem is null ? "mut" : "warn", count) +
                    "{pad}" + Markup.Style("btn", " ◂ Anterior ", "previous") + " " + Markup.Style("btn", " Siguiente ▸ ", "next") + " "
            ];
            var current = historyMatch >= 0 ? historyMatches[historyMatch] : -1;
            historyLines.RowZone = y => historyLines.Top + y == current ? "chg" : null;
            historyLines.Show(rows.Length, (row, _) => Highlight(rows[row], pattern));
            historyFooter.Rows = [" " + Markup.Style("mut", "instancias") + "  " + string.Join("  ", files.Select((file, i) =>
                file.Id == historyFile?.Id ? Markup.Style("sel", $" i{i + 1} {file.StartedAt}{(file.Active ? " → " : "")} ")
                    : Markup.Escape($"i{i + 1} {file.StartedAt}")))];
        }

        static string Highlight(string row, Regex? pattern)
        {
            if (pattern is null) return LivePresentation.LogRow(row);
            var text = " ";
            var at = 0;
            try
            {
                foreach (Match match in pattern.Matches(row))
                {
                    if (match.Length == 0) continue;
                    text += Markup.Escape(row[at..match.Index]) + Markup.Style("sel", match.Value);
                    at = match.Index + match.Length;
                }
            }
            catch (RegexMatchTimeoutException) { return LivePresentation.LogRow(row); }
            return at == 0 ? LivePresentation.LogRow(row) : text + Markup.Escape(row[at..]);
        }

        void RenderNotices()
        {
            noticeTitle.Rows = [" Notificaciones{pad}" + Markup.Style("btn", " Marcar leídas ", "read") + "  " + Markup.Style("btn", " Volver ", "back") + " "];
            noticeList.RowZone = y => noticeList.HasFocus && noticeList.Top + y == noticeList.Selected ? "chg" : null;
            if (notices.Count == 0) { noticeList.Show(1, (_, _) => " " + Markup.Style("mut", "Sin avisos en esta sesión.")); return; }
            noticeList.Show(notices.Count, (row, selected) =>
            {
                var notice = notices[notices.Count - 1 - row];
                var text = " " + Markup.Style(notice.Class, notice.Glyph) + " " + Markup.Style("mut", notice.At.ToString("HH:mm", CultureInfo.InvariantCulture)) +
                    " " + Markup.Escape(notice.Text) + "{pad}" + Markup.Style("btn", " Ver log ", "n" + row) + " ";
                return text;
            });
        }

        void RenderDefinitions()
        {
            definitionNodes = DefinitionsPresentation.Tree(places.Select(place => place.Scope).ToArray(), focusedPlace);
            var scopes = places.Select(place => place.Scope).ToArray();
            var current = definitionNodes.ToList().FindIndex(node => node.Scope == focusedPlace && node.View == definitionView);
            if (current < 0) current = definitionNodes.ToList().FindIndex(node => node.Scope == focusedPlace);
            tree.Show(definitionNodes.Count, (row, selected) =>
                DefinitionsPresentation.TreeRow(definitionNodes[row], scopes, focusedPlace, selected), Math.Max(0, current));
            if (definitionPage is null || definitionClock.Elapsed >= CaptureInterval)
            {
                definitionClock.Restart();
                var place = places[focusedPlace];
                definitionPage = definitionView is null ? DefinitionsPresentation.Inaccessible(place.Scope.Name)
                    : definitionSource.Read(place, definitionView, targets, snapshots, definitionDetail.Viewport.Height,
                        Math.Max(20, window.Viewport.Width - Right));
            }
            leftTitle.Rows = [""];
            rightTitle.Rows = [" " + definitionPage.Title];
            definitionDetail.Rows = definitionPage.Rows;
        }

        // E, F and G: the category tree, the title with its source and the selected form.
        void RenderSettings()
        {
            var categories = SettingsPresentation.Categories;
            tree.Show(categories.Length, (row, selected) =>
                selected ? Markup.Style("focb", (" " + categories[row]).PadRight(Left)) : Markup.Escape(" " + categories[row]), settingsCategory);
            rightTitle.Rows = [" " + categories[settingsCategory] + "  " + Markup.Style("mut", settingsCategory switch
            {
                0 => Home.Resolve().PathFor("config.json"),
                1 => "preferencias de esta interfaz",
                _ => probe.IsCompleted ? "sondeo local" + (probedAt is { } at ? " · " + at.ToString("HH:mm", CultureInfo.InvariantCulture) : "") : "sondeando…"
            })];
            if (settings.Coquena.Visible != (settingsCategory == 0) || settings.Tui.Visible != (settingsCategory == 1)) Arrange();
            if (settingsCategory == 1) settings.ShowResources(devices.Count);
            if (settingsCategory == 2)
            {
                var adapters = new List<string>();
                var notes = new List<string>();
                if (lastHardware is not null)
                {
                    foreach (var device in lastHardware.Devices)
                        adapters.Add((devices.TryGetValue(device.Id, out var known) ? $"GPU{known.Number}  " : "      ") +
                            device.Name + " · " + (ComputeLabel(device.Id) ?? "cómputo sin confirmar"));
                    notes.AddRange(lastHardware.Diagnostics);
                }
                systemDetail.Rows = SettingsPresentation.System(typeof(DashboardCli).Assembly.GetName().Version?.ToString(3) ?? "—",
                    evidence?.Context, probe.IsFaulted, adapters, notes, Math.Max(20, window.Viewport.Width - Right));
            }
        }

        void SampleHardware()
        {
            if (machineSample is not null && !machineSample.IsCompleted) return;
            if (machineSample is { IsCompletedSuccessfully: true })
            {
                lastHardware = machineSample.Result;
                Record(lastHardware);
            }
            machineSample = null;
            if (!machineClock.IsRunning || machineClock.Elapsed >= CaptureInterval)
                machineSample = Task.Run(() => { machineClock.Restart(); return hardware.CaptureAsync(stop.Token); });
        }

        void Record(HardwareSnapshot sample)
        {
            var now = DateTimeOffset.UtcNow;
            var cpu = Fresh(sample.Samples, "cpu.utilization", now);
            var ramUsed = Fresh(sample.Samples, "ram.used", now);
            var ramTotal = Fresh(sample.Samples, "ram.total", now);
            cpuGauge.Add(sample.StartedAt, cpu / 100, cpu, cpu is null ? null : Number(cpu.Value, "0") + "%");
            ramGauge.Add(sample.StartedAt, Fraction(ramUsed, ramTotal), ramUsed, ramUsed is null ? null :
                DashboardPresentation.Compact(ramUsed.Value) + "/" + (ramTotal is null ? "—" : DashboardPresentation.Compact(ramTotal.Value)));
            ramGauge.Total = ramTotal;
            foreach (var device in sample.Devices)
            {
                if (ComputeLabel(device.Id) is null) continue;
                if (!devices.TryGetValue(device.Id, out var known))
                    devices[device.Id] = known = new Device(devices.Count, device.Name, new(), new(), new());
                var usage = Fresh(device.Samples, "gpu.utilization", now);
                var used = Fresh(device.Samples, "vram.used", now);
                var total = Fresh(device.Samples, "vram.total", now);
                var shared = Fresh(device.Samples, "gpu.shared", now);
                known.Use.Add(sample.StartedAt, usage / 100, usage, usage is null ? null : Number(usage.Value, "0") + "%");
                known.Memory.Add(sample.StartedAt, Fraction(used, total), used, used is null ? null :
                    DashboardPresentation.Compact(used.Value) + "/" + (total is null ? "—" : DashboardPresentation.Compact(total.Value)));
                known.Memory.Total = total;
                known.Shared.Add(sample.StartedAt, shared is null || ramTotal is null ? null : shared / ramTotal, shared,
                    shared is null ? null : "+" + DashboardPresentation.Compact(shared.Value));
            }
        }

        void RenderBar(DateTimeOffset now)
        {
            var gpus = devices.Values.OrderBy(device => device.Number).Select(device => new LivePresentation.Gauge("GPU" + device.Number,
                device.Use.Bar.Read(now, 5), device.Use.Ratio, device.Memory.Text, device.Shared.Text, device.Memory.Ratio)).ToArray();
            var (first, second) = LivePresentation.HardwareBar(
                new("CPU", cpuGauge.Bar.Read(now, 16), cpuGauge.Ratio, cpuGauge.Text, null, null),
                new("RAM", ramGauge.Bar.Read(now, 16), ramGauge.Ratio, ramGauge.Text, null, null), gpus, evidence is null, settings.Hidden);
            bar.Rows = [first, second];
        }

        void RenderHardware(DateTimeOffset now)
        {
            var ordered = devices.Values.OrderBy(device => device.Number).ToArray();
            var names = ordered.Select(device => ("GPU" + device.Number, device.Use.Text ?? "—")).Append(("CPU", cpuGauge.Text ?? "—"))
                .Append(("RAM", ramGauge.Text ?? "—")).ToArray();
            hardwareSelection = Math.Clamp(hardwareSelection, 0, names.Length - 1);
            tree.Show(names.Length, (row, selected) =>
            {
                var text = " " + names[row].Item1.PadRight(Left - 2 - names[row].Item2.Length) + names[row].Item2 + " ";
                return selected ? Markup.Style("focb", text) : Markup.Escape(text);
            }, hardwareSelection);
            leftTitle.Rows = [" Hardware · " + (int)HardwareHistory.Span.TotalMinutes + " min"];
            var width = Math.Max(10, window.Viewport.Width - Right - 7);
            var rows = new List<string>();
            if (hardwareSelection < ordered.Length)
            {
                var device = ordered[hardwareSelection];
                rightTitle.Rows = [" " + names[hardwareSelection].Item1 + "  " + Markup.Style("mut", "uso · vram · compartida")];
                rows.Add(Curve(device.Use, "uso", width));
                rows.Add(Curve(device.Memory, "vram", width));
                rows.Add(Curve(device.Shared, "+ram", width, "pr"));
                rows.Add("");
                rows.Add(Summary("uso", device.Use, value => Number(value, "0") + "%"));
                if (device.Memory.Value is { } used)
                {
                    rows.Add(" " + Markup.Style("mut", "vram") + " " + DashboardPresentation.Compact(used) +
                        (device.Memory.Total is { } total ? " / " + DashboardPresentation.Compact(total) : "") +
                        " " + Markup.Style("mut", "· pico") + " " + DashboardPresentation.Compact(device.Memory.Peak ?? used));
                }
                if (device.Shared.Value is { } shared)
                {
                    var rise = device.Shared.Peak is { } top && device.Shared.First is { } start && top - start > 64d * 1024 * 1024 ? device.Shared.FirstRise : null;
                    rows.Add(rise is { } at
                        ? " " + Markup.Style("warn", "▲ compartida") + " " + DashboardPresentation.Compact(device.Shared.First!.Value) + " → " +
                          DashboardPresentation.Compact(shared) + " " + Markup.Style("mut", "desde " + at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) +
                          (device.Memory.Ratio is > 0.9 ? ", VRAM en pico" : ""))
                        : " " + Markup.Style("mut", "compartida") + " " + DashboardPresentation.Compact(shared));
                }
                rows.Add("");
                rows.Add(" " + Markup.Style("mut", "adaptador") + " " + Markup.Escape(device.Name + " · " + ComputeName(device)));
            }
            else
            {
                var gauge = hardwareSelection == ordered.Length ? cpuGauge : ramGauge;
                rightTitle.Rows = [" " + names[hardwareSelection].Item1 + "  " + Markup.Style("mut", "uso")];
                rows.Add(Curve(gauge, "uso", width));
                rows.Add("");
                rows.Add(gauge == cpuGauge ? Summary("uso", gauge, value => Number(value, "0") + "%")
                    : " " + Markup.Style("mut", "ram") + " " + (gauge.Text ?? "—") + " " + Markup.Style("mut", "· pico") + " " +
                      (gauge.Peak is { } peak ? DashboardPresentation.Compact(peak) : "—"));
                if (lastHardware?.Environment is { Length: > 0 } environment) { rows.Add(""); rows.Add(" " + Markup.Style("mut", Markup.Escape(environment))); }
            }
            hardwareDetail.Rows = rows;

            string Curve(Gauge gauge, string caption, int columns, string? tone = null)
            {
                var values = gauge.History.Columns(now, columns);
                var peak = values.Max();
                return " " + Markup.Style(tone ?? LivePresentation.UsageClass(peak), LivePresentation.Spark(values)) + " " + Markup.Style("mut", caption);
            }

            string Summary(string caption, Gauge gauge, Func<double, string> format)
            {
                var values = gauge.History.Values.ToArray();
                if (gauge.Value is not { } value) return " " + Markup.Style("mut", caption) + " —";
                return " " + Markup.Style("mut", caption) + " " + format(value) +
                    (values.Length > 0 ? " " + Markup.Style("mut", "· media") + " " + format(values.Average() * 100) +
                        " " + Markup.Style("mut", "· pico") + " " + format(values.Max() * 100) : "");
            }
        }

        static DateTimeOffset Ended(InferenceTask task) => DateTimeOffset.UtcNow - Stopwatch.GetElapsedTime(task.ObservedAt);

        static double? Fresh(IReadOnlyList<MetricSample> list, string name, DateTimeOffset now) =>
            list.FirstOrDefault(metric => metric.Name == name) is { Value: { } value } metric && metric.IsFresh(now, MetricLifetime) ? value : null;

        static string Number(double value, string format) => LivePresentation.Number(value, format);

        string ComputeName(Device device) =>
            devices.FirstOrDefault(pair => pair.Value == device).Key is { } id ? ComputeLabel(id) ?? "" : "";

        string? ComputeLabel(string id)
        {
            if (evidence is null) return null;
            var cuda = evidence.Cuda.Contains(id);
            var vulkan = evidence.Vulkan.Contains(id);
            return cuda ? vulkan ? "CUDA / Vulkan" : "CUDA" : vulkan ? "Vulkan" : null;
        }

        void ShowDetail(string title, string text) => Choose(app, title, text.Split('\n'), null);

        string EntryDetail(EntrySnapshot entry)
        {
            var source = entry.Inference;
            var rows = new List<string>
            {
                $"{entry.Name} · {entry.State}" + (source?.Model is { } model ? " · modelo " + model : ""),
                selectedSnapshot?.InstanceId is { } instance ? "Instancia " + instance + (source is null ? "" : $" · hijo {source.Pid}/{source.Creation}") : "",
                source is null ? entry.InferenceDiagnostic ?? "Tareas /slots: hijo/build/endpoint sin verificar" : "Tareas /slots: hijo verificado",
                entry.Diagnostic ?? "Métricas disponibles según la fuente",
                "Acumulados y medias del backend: período no informado; cada hijo abre otro período.",
                ""
            };
            rows.AddRange(entry.Metrics.SelectMany(metric => FormatMetric(metric, DateTimeOffset.UtcNow).Split('\n')));
            return string.Join('\n', rows.Where(row => row.Length > 0 || rows.IndexOf(row) == 5));
        }

        static string FormatMetric(MetricSample metric, DateTimeOffset now)
        {
            var name = metric.Name switch
            {
                "ram.private-commit" => "RAM privada comprometida",
                "ram.working-set" => "RAM residente por proceso",
                "gpu.shared" => "RAM compartida GPU",
                "vram.used" => "VRAM dedicada usada",
                "vram.total" => "VRAM dedicada total",
                _ => metric.Name
            };
            var value = metric.Value is null ? $"sin valor · {metric.UnavailableReason ?? "lectura sin valor"}" :
                (metric.Unit == "bytes" ? Memory(metric.Value) : metric.Value.Value.ToString("0.##", CultureInfo.CurrentCulture) + " " + metric.Unit) +
                (metric.IsFresh(now, MetricLifetime) ? "" : " · ant.");
            var time = metric.ObservedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var window = metric.Window is { } span ? $" · ventana {span.TotalMilliseconds:0} ms" : "";
            return $"{name}  {value}\n    {time}{window} · {metric.Scope} · {metric.Source}";
        }

        void BeginServiceAction(bool stopService)
        {
            if (operation is not null || index >= targets.Count) return;
            var target = targets[index];
            var snapshot = selectedSnapshot;
            if (target.Service is not null && snapshot is null) return;
            var running = snapshot?.ProcessState == "alive" && snapshot.InstanceId is not null;
            if (!running && (stopService || snapshot is not null && snapshot.ProcessState != "stopped")) return;
            if (target.Service is null && !target.DeclaresService) return;
            var verb = stopService ? "Detener" : running ? "Reiniciar" : "Iniciar";
            var usePrivateResources = privatePreparation.Value == CheckState.Checked;
            if (running && Query(app, verb + " servicio",
                    $"{verb} «{target.Service!.Name}» ({(target.Store.Context.IsGlobal ? "global" : "proyecto")}) puede interrumpir peticiones activas.",
                    "Cancelar", verb) != 1) return;

            upButton.Enabled = downButton.Enabled = false;
            serviceNote = $"{verb.ToLowerInvariant()}…";
            selectionEpoch++;
            operation = Task.Run(async () =>
            {
                using var diagnostics = new StringWriter();
                var lifecycle = new ScopedServiceProcess(target.Store, TextWriter.Null, diagnostics);
                try
                {
                    var service = target.Service ?? new RecipeRegistry(target.Store).EnsureProjectService(
                        target.Recipe!.Files, RecipeReader.Read(target.Recipe).Root.Get("name")?.Text);
                    if (stopService)
                    {
                        lifecycle.Stop(service.ServiceId, expectedInstanceId: snapshot!.InstanceId);
                        return null;
                    }
                    var context = target.Store.Context;
                    var selection = context.IsGlobal
                        ? new RecipeImporter(context).ServiceSelection(service.ServiceId)
                        : target.Recipe ?? new RecipeSelection(context.Base, new RecipeRegistry(target.Store).Get(service.RecipeId).Files, context.Base);
                    var instance = await ScopedServiceCli.RunCandidateAsync(lifecycle, new(context, selection, service),
                        running, detached: true, privateResources: usePrivateResources, TextWriter.Null, diagnostics,
                        new DashboardModelSelection(app, diagnostics), stop.Token,
                        expectedInstanceId: running ? snapshot!.InstanceId : null);
                    if (running && instance == snapshot!.InstanceId)
                        return "El reinicio conservó la instancia anterior. " + diagnostics.ToString().Trim();
                    return null;
                }
                catch (Exception error) when (ServiceObservation.ObservationError(error) ||
                    error is BackendException or Coquena.Downloads.DownloadException or ModelSourceException)
                {
                    return error.Message;
                }
            });
        }

        bool RefreshProject()
        {
            var changed = false;
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.Recipe is null) continue;
                try
                {
                    var recipe = RecipeReader.Read(target.Recipe);
                    ServiceIdentity? service = null;
                    try { service = new RecipeRegistry(target.Store).FindProjectService(target.Recipe.Files); }
                    catch (StorageException error) when (error.Code == "unknown-service") { }
                    var next = target with { Service = service, ProjectName = recipe.Name,
                        DeclaresService = recipe.Root.Get("service") is not null, Destination = recipe.Target, RecipeProblem = null };
                    if (next == target) continue;
                    targets[i] = next;
                    changed = true;
                    if (service?.ServiceId == target.Service?.ServiceId) continue;
                    catalogues.Remove(i);
                    if (logSessions.Remove(i, out var previous)) retiredLogs.Add(previous.CloseAsync());
                }
                catch (Exception error) when (ServiceObservation.ObservationError(error))
                {
                    if (target.RecipeProblem != error.Message)
                    { targets[i] = target with { RecipeProblem = error.Message }; changed = true; }
                }
            }
            if (changed) selectionEpoch++;
            return changed;
        }

        void BeginPublication(string action)
        {
            if (operation is not null || index >= targets.Count || targets[index].Recipe is not { } selection) return;
            var label = targets[index].Label;
            var usePrivateResources = privatePreparation.Value == CheckState.Checked;
            upButton.Enabled = downButton.Enabled = false;
            serviceNote = $"{action}…";
            selectionEpoch++;
            operation = Task.Run<string?>(() =>
            {
                using var output = new StringWriter();
                using var error = new StringWriter();
                var models = new DashboardModelSelection(app, error);
                var frontend = new PublicationCli.Frontend(models, async (args, stdout, stderr) =>
                {
                    var target = ScopedServiceCli.Resolve(ServiceCommand.Parse(args), Home.Resolve());
                    var lifecycle = new ScopedServiceProcess(new ScopeStore(target.Context), stdout, stderr);
                    await ScopedServiceCli.RunCandidateAsync(lifecycle, target, false, true, args.Contains("--private"),
                        stdout, stderr, models, stop.Token);
                    return 0;
                }, stop.Token);
                var args = new List<string> { action, "--project-directory", selection.Base };
                if (usePrivateResources) args.Add("--private");
                foreach (var file in selection.Files) args.AddRange(["--file", file]);
                var result = PublicationCli.Execute(args.ToArray(), output, error,
                    new DashboardPublicationInteraction(app, label, stop.Token), frontend);
                return $"{action} · {label} · código {result}\n{output}\n{error}".Trim();
            });
        }
    }

    // Modal message: title, wrapped text and actions on the right. Returns the chosen action index.
    private static int Query(IApplication app, string title, string message, params string[] actions)
    {
        var width = Math.Min(70, app.Screen.Width - 6);
        var wrapped = DashboardPresentation.Wrap(message, width - 4);
        var height = Math.Min(app.Screen.Height - 4, wrapped.Count + 5);
        using var dialog = new Dialog { Width = width, Height = height, BorderStyle = LineStyle.None, ShadowStyle = ShadowStyles.None };
        var heading = new Label { X = 1, Text = title, Width = Dim.Fill(1) };
        var content = new ListView { X = 1, Y = 2, Width = Dim.Fill(1), Height = Dim.Fill(1),
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar };
        content.SetSource(new ObservableCollection<string>(wrapped));
        dialog.Add(heading, content);
        var color = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        if (color) { Paint(dialog, Palette.Text, Palette.Card); Paint(heading, Palette.Text, Palette.Card); Paint(content, Palette.Text, Palette.Card); }
        foreach (var text in actions)
        {
            var button = new Button { Text = " " + text + " ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
            if (color) Paint(button, Palette.Text, Palette.Border);
            dialog.AddButton(button);
        }
        app.Run(dialog);
        return dialog.Result is int result ? result : -1;
    }

    // Modal list: returns the selected row when the accept action is chosen; without one it only informs.
    private static int? Choose(IApplication app, string title, IReadOnlyList<string> rows, string? accept)
    {
        using var dialog = new Dialog { Width = Dim.Percent(90), Height = Dim.Percent(80), BorderStyle = LineStyle.None, ShadowStyle = ShadowStyles.None };
        var heading = new Label { X = 1, Text = title, Width = Dim.Fill(1) };
        var list = new ListView { X = 1, Y = 2, Width = Dim.Fill(1), Height = Dim.Fill(1),
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar | ViewportSettingsFlags.HasHorizontalScrollBar };
        list.SetSource(new ObservableCollection<string>(rows));
        list.SetContentSize(new System.Drawing.Size(Math.Max(1, rows.DefaultIfEmpty("").Max(row => row.Length)), rows.Count));
        list.SelectedItem = 0;
        dialog.Add(heading, list);
        var color = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        if (color) { Paint(dialog, Palette.Text, Palette.Card); Paint(heading, Palette.Text, Palette.Card); Paint(list, Palette.Text, Palette.Base); }
        var actions = accept is null ? new[] { " Aceptar " } : [" Cancelar ", " " + accept + " "];
        foreach (var text in actions)
        {
            var button = new Button { Text = text, NoDecorations = true, ShadowStyle = ShadowStyles.None };
            if (color) Paint(button, Palette.Text, Palette.Border);
            dialog.AddButton(button);
        }
        app.Run(dialog);
        return accept is not null && dialog.Result == 1 ? list.SelectedItem : null;
    }

    private sealed class DashboardModelSelection(IApplication app, TextWriter diagnostics) : IModelSelectionInteraction
    {
        public bool IsInteractive => true;
        public void Show(string text) => diagnostics.WriteLine(text);

        public async Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation)
        {
            var choice = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            app.Invoke(() =>
            {
                if (cancellation.IsCancellationRequested) return;
                try
                {
                    using var dialog = new Dialog { BorderStyle = LineStyle.None, ShadowStyle = ShadowStyles.None, Title = "Seleccionar recurso", Width = Dim.Percent(85), Height = Dim.Percent(70) };
                    var list = new ListView { Y = 1, Width = Dim.Fill(), Height = Dim.Fill() };
                    dialog.Add(new Label { Text = "Seleccionar recurso", Width = Dim.Fill() });
                    list.SetSource(new ObservableCollection<string>(options));
                    list.SelectedItem = 0;
                    var accept = new Button { Text = "Elegir", IsDefault = true, ShadowStyle = ShadowStyles.None };
                    var cancel = new Button { Text = "Cancelar", ShadowStyle = ShadowStyles.None };
                    dialog.Add(list);
                    dialog.AddButton(accept);
                    dialog.AddButton(cancel);
                    accept.NoDecorations = cancel.NoDecorations = true;
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
                    { Paint(dialog, Palette.Text, Palette.Card); Paint(accept, Palette.Text, Palette.Border); Paint(cancel, Palette.Text, Palette.Border); }
                    accept.IsDefault = true; cancel.IsDefault = false;
                    app.Run(dialog);
                    choice.TrySetResult(dialog.Result == 0 ? list.SelectedItem : null);
                }
                catch (Exception error) { choice.TrySetException(error); }
            });
            return await choice.Task.WaitAsync(cancellation);
        }
    }

    private sealed class DashboardPublicationInteraction(IApplication app, string project, CancellationToken cancellation) : IPublicationInteraction
    {
        public bool Available => !cancellation.IsCancellationRequested;
        public bool Confirm(string diagnostic) => Ask("Publicar / retirar · " + project, diagnostic, "Continuar");
        public bool StartService() => Ask("Destino detenido · " + project,
            "La publicación requiere iniciar el servicio destino. ¿Iniciar ahora?", "Iniciar");
        public bool StartService(string diagnostic) => Ask("Destino detenido · " + project, diagnostic, "Iniciar");

        private bool Ask(string title, string text, string accept)
        {
            var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            app.Invoke(() =>
            {
                if (cancellation.IsCancellationRequested) { answer.TrySetCanceled(cancellation); return; }
                try { answer.TrySetResult(Query(app, title, text, "Cancelar", accept) == 1); }
                catch (Exception error) { answer.TrySetException(error); }
            });
            return answer.Task.WaitAsync(cancellation).GetAwaiter().GetResult();
        }
    }

    internal static List<Target> Discover(out string diagnostic, string? directory = null, Home? selectedHome = null)
    {
        var targets = new List<Target>();
        diagnostic = "Sin servicios registrados en el ámbito global o en este proyecto.";
        if (!OperatingSystem.IsWindows())
        {
            diagnostic = "La observación de servicios registrados está disponible en Windows.";
            return targets;
        }
        var errors = new List<string>();
        var home = selectedHome ?? Home.Resolve();
        directory ??= Environment.CurrentDirectory;
        if (Directory.Exists(Path.Combine(home.Root, "global"))) Add(() => ScopeContext.Global(home));
        var file = Path.Combine(directory, "coquena.yaml");
        if (File.Exists(file))
        {
            try
            {
                var selection = new RecipeSelection(directory);
                var store = new ScopeStore(ScopeContext.Project(file));
                ServiceIdentity? service = null;
                try { service = new RecipeRegistry(store).FindProjectService(selection.Files); }
                catch (StorageException error) when (error.Code == "unknown-service") { }
                try
                {
                    var recipe = RecipeReader.Read(selection);
                    targets.Add(new(store, service, false, selection, recipe.Name,
                        recipe.Root.Get("service") is not null, recipe.Target));
                }
                catch (RecipeException error)
                {
                    targets.Add(new(store, service, false, selection, service?.Name ?? Path.GetFileName(directory),
                        RecipeProblem: error.Message));
                }
            }
            catch (Exception error) when (ServiceObservation.ObservationError(error)) { errors.Add(error.Message); }
        }
        else if (Directory.Exists(Path.Combine(directory, ".coquena"))) Add(() => ScopeContext.Project(file));
        if (targets.Count == 0 && errors.Count != 0) diagnostic = string.Join(" · ", errors);
        return targets;

        void Add(Func<ScopeContext> resolve)
        {
            try
            {
                var store = new ScopeStore(resolve());
                var state = store.ReadVerified();
                var defaultId = store.Context.IsGlobal ? state.State?["data"]?["aliases"]?["default"]?.GetValue<string>() : null;
                targets.AddRange(new RecipeRegistry(store).Services().Select(service => new Target(store, service, service.ServiceId == defaultId)));
            }
            catch (Exception e) when (ServiceObservation.ObservationError(e)) { errors.Add(e.Message); }
        }
    }

    private static async Task<ServicePanel> CaptureAsync(Target target, CancellationToken cancellation)
    {
        var snapshot = await new ServiceObservation(target.Store).CaptureAsync(target.Service!, cancellation: cancellation, observeInference: true);
        cancellation.ThrowIfCancellationRequested();
        return new(snapshot);
    }

}
