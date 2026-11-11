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

    // Retry repeats a failed operation; Updates names the scope root whose Actualizaciones it opens.
    private sealed record Notice(DateTimeOffset At, string Glyph, string Class, string Text, int Target,
        (string Key, Func<Task<string>> Run)? Retry = null, string? Updates = null);

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
    // Configuración is drawn as markup rows; management actions capture their selected scope.
    internal static int RunDashboard(ISystemMetrics? metrics = null, HardwareProbe.AdapterEvidence? adapters = null)
    {
        const int Left = LivePresentation.TreeWidth, Right = Left + 1;
        const int Live = 0, Definitions = 1, Settings = 2, Hardware = 3, History = 4, Notices = 5, Editing = 6, DownloadsPage = 7;
        const string Finished = "ya había terminado o fue retirada; no hubo cambios.";
        var targets = Discover(out var diagnostic);
        var preferences = new ConfigurationManager(Home.Resolve());
        var hardware = metrics ?? SystemMetrics.Create();
        using var stop = new CancellationTokenSource();
        using var app = Application.Create().Init();
        var consoleOut = Console.Out; // Notices ring the terminal bell through it.
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
        var definitionDetail = new Canvas(useColor) { X = Right, Y = 2, Interactive = true, Scrollable = true };
        // N · Descargas: full width, one title row and the grouped operations.
        var downloadTitle = new Canvas(useColor) { Y = 1, Height = 1, ZoneName = "hd" };
        var downloadDetail = new Canvas(useColor) { Y = 2, Interactive = true, Scrollable = true };
        rightTitle.Interactive = true;

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
        var recipeEditor = new RecipeEditorPage(useColor);
        var systemDetail = new Canvas(useColor) { Interactive = true };
        settingsView.Add(settings.Coquena, settings.Tui, systemDetail);

        window.Add(tabs, leftTitle, rightTitle, tree, detail, tasks, logTitle, logLines, hardwareDetail,
            historyTitle, historyControls, regex, historyLines, historyFooter, noticeTitle, noticeList, definitionDetail, downloadTitle, downloadDetail, pageView, settingsView, bar);
        window.Add(recipeEditor.Views.ToArray());
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
        int? publicationExit = null;
        string? operationLabel = null;
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
        Notice? focusedNotice = null;
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
        var sessionCombinations = new Dictionary<string, RecipeCombination>();
        var combinationNewFiles = new Dictionary<string, IReadOnlySet<string>>();
        var authorSelections = new Dictionary<string, RecipeSelection[]>();
        var updateViews = new Dictionary<string, UpdateView>();
        IReadOnlyList<DefinitionsPresentation.Node> definitionNodes = [];
        DefinitionsPresentation.Page? definitionPage = null;
        var definitionClock = new Stopwatch();
        var managedSelection = new Dictionary<string, string>();
        var resourceFilters = new Dictionary<string, string>();
        var resourceReads = new Dictionary<string, Task<InventoryRow[]>>();
        var resourceCache = new Dictionary<string, InventoryRow[]>();
        var resourceProblems = new Dictionary<string, string>();
        var hubViews = new Dictionary<string, HubView>();
        var maintenanceViews = new Dictionary<string, MaintenanceView>();
        var managementJobs = new Dictionary<string, Task<string>>();
        var managementRuns = new Dictionary<string, Func<Task<string>>>();
        var transferClock = new Stopwatch();
        // Descargas: Global and the current project only; other known projects are not listed.
        var transferGroups = new List<(DefinitionsSource.Place Place, ScopeContext Context, DownloadsPresentation.Group Group)>();
        var transferSamples = new Dictionary<Guid, (long Done, DateTimeOffset At, double? Speed)>();
        var transferProblems = new List<string>();
        var transferCount = 0;
        string? transferIndicator = null;
        string? transferProblem = null;

        rightTitle.Pressed += DefinitionAction;
        recipeEditor.Changed += () => { if (page == Editing) Render(); };
        recipeEditor.Tabs.Pressed += action =>
        {
            if (action.StartsWith("tab:", StringComparison.Ordinal) && int.TryParse(action[4..], CultureInfo.InvariantCulture, out var tab)) recipeEditor.Show(tab);
            Render();
        };
        recipeEditor.Tools.Pressed += EditorAction;
        definitionDetail.Pressed += DefinitionAction;
        downloadDetail.Pressed += DownloadAction;

        // Events.
        tabs.Pressed += action =>
        {
            switch (action)
            {
                case "tab0": Open(Live); break;
                case "tab1": Open(Definitions); break;
                case "tab2": Open(Settings); break;
                case "notices": Open(Notices); break;
                case "downloads": Open(DownloadsPage); break;
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
            Render();
        };
        noticeList.Pressed += action =>
        {
            if (!action.StartsWith('n') || !int.TryParse(action[1..], CultureInfo.InvariantCulture, out var row) || row >= notices.Count) return;
            NoticeAction(notices[notices.Count - 1 - row]);
        };
        noticeList.Chosen += row => { if (row < notices.Count) OpenNotice(notices[notices.Count - 1 - row]); };
        // The selection follows the notice, not the row: new notices are added above it.
        noticeList.SelectionChanged += row => focusedNotice = row >= 0 && row < notices.Count ? notices[notices.Count - 1 - row] : null;
        upButton.Accepted += (_, _) => BeginPublication("up");
        downButton.Accepted += (_, _) => BeginPublication("down");
        window.KeyDown += (_, key) =>
        {
            if (page == Editing && key == Key.S.WithCtrl) { key.Handled = true; SaveDrafts(); Render(); return; }
            // Tabs have no «Volver»: Esc only leaves subpages and the Editor.
            if (key != Key.Esc || page is Live or Definitions or Settings or Notices or DownloadsPage) return;
            key.Handled = true;
            if (page == Editing) { CloseEditor(); return; }
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
        // The status spinner of a Hub query animates only while it is visible.
        app.AddTimeout(TimeSpan.FromMilliseconds(120), () =>
        {
            if (page == Definitions && definitionView == "Modelos" && managementJobs.Keys.Any(key => key.StartsWith("hf:", StringComparison.Ordinal)))
            { definitionPage = null; Render(); }
            return true;
        });
        app.AddTimeout(TimeSpan.FromSeconds(1), () =>
        {
            Tick();
            return true;
        });
        var consoleError = Console.Error;
        Console.SetError(TextWriter.Null);
        try { app.Run(window); }
        finally
        {
            Console.SetError(consoleError);
            stop.Cancel();
            try { Task.WhenAll(managementJobs.Values).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
            catch (Exception error) when (ManagementError(error) || error is TimeoutException) { }
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
            // Leaving the Editor asks for its drafts like Cerrar does.
            if (page == Editing && selected != Editing && !ReleaseEditor()) return;
            if (selected == Definitions)
            {
                places = definitionSource.Places();
                focusedPlace = Math.Clamp(focusedPlace, 0, places.Count - 1);
                definitionPage = null;
            }
            page = selected;
            Arrange();
            Render();
            if (page == Editing) { recipeEditor.Show(recipeEditor.Current); return; }
            (page switch { Hardware or Settings or Live or Definitions => tree, Notices => noticeList, DownloadsPage => downloadDetail, _ => (View)historyLines }).SetFocus();
        }

        void Tick()
        {
            foreach (var read in resourceReads.Where(read => read.Value.IsCompleted).ToArray())
            {
                resourceReads.Remove(read.Key);
                if (read.Value.IsCompletedSuccessfully) { resourceCache[read.Key] = read.Value.Result; resourceProblems.Remove(read.Key); }
                else resourceProblems[read.Key] = read.Value.Exception?.GetBaseException().Message ?? "Consulta cancelada.";
                definitionPage = null;
            }
            foreach (var job in managementJobs.Where(job => job.Value.IsCompleted).ToArray())
            {
                managementJobs.Remove(job.Key);
                managementRuns.Remove(job.Key, out var run);
                resourceCache.Clear(); resourceProblems.Clear();
                definitionPage = null;
                transferClock.Reset();
                if (job.Key.StartsWith("service:", StringComparison.Ordinal)) RefreshTargets();
                var message = job.Value.IsCompletedSuccessfully ? job.Value.Result : job.Value.Exception?.GetBaseException().Message ?? "Operación cancelada.";
                // Hub queries are navigation: their result is the page itself, only failures are notices.
                // A preparation whose transfer was cancelled ends without installing anything; it is not an error.
                if (job.Value.Exception?.GetBaseException() is Coquena.Downloads.DownloadException { Code: "transfer-cancelled" } cancelled)
                    Notify("▲", "warn", cancelled.Message, -1);
                else if (!(job.Key.StartsWith("hf:", StringComparison.Ordinal) && job.Value.IsCompletedSuccessfully))
                {
                    // Comprobar and Actualizar lead to their view; other failures can be repeated,
                    // except removals, whose confirmed plan may no longer hold.
                    var updates = job.Key.StartsWith("update:", StringComparison.Ordinal) ? job.Key["update:".Length..] : null;
                    var retry = !job.Value.IsCompletedSuccessfully && updates is null && run is not null &&
                        !job.Key.StartsWith("remove:", StringComparison.Ordinal) && !job.Key.StartsWith("forget:", StringComparison.Ordinal)
                        ? (job.Key, run) : ((string, Func<Task<string>>)?)null;
                    Notify(job.Value.IsCompletedSuccessfully ? "✓" : "✗", job.Value.IsCompletedSuccessfully ? "ok" : "err", message, -1, retry, updates);
                }
                Render();
            }
            if (!transferClock.IsRunning || transferClock.Elapsed >= CaptureInterval)
            {
                transferClock.Restart();
                ReadTransfers();
            }
            if (operation is { IsCompleted: true } completed)
            {
                operation = null;
                selectedSnapshot = null;
                serviceNote = null;
                RefreshTargets();
                var problem = completed.IsCompletedSuccessfully ? completed.Result : completed.Exception?.GetBaseException().Message;
                if (!string.IsNullOrWhiteSpace(problem))
                {
                    var tone = completed.IsCompletedSuccessfully && publicationExit == 0 ? "ok" : publicationExit == 130 ? "warn" : "err";
                    Notify(tone == "ok" ? "✓" : tone == "warn" ? "▲" : "✗", tone, operationLabel + ": " + problem, -1);
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
            downloadTitle.Visible = downloadDetail.Visible = page == DownloadsPage;
            downloadDetail.Height = content - 1;
            noticeList.Height = content - 1;
            recipeRow.Visible = false;
            settingsView.Visible = settingsPage;
            settingsView.Height = content - 1;
            settings.Coquena.Visible = settingsCategory == 0;
            settings.Tui.Visible = settingsCategory == 1;
            systemDetail.Visible = settingsCategory == 2;
            systemDetail.Width = Dim.Fill();
            systemDetail.Height = Dim.Fill();
            // H · Editor: file tabs, actions, the text and the last validation over the hardware bar.
            foreach (var view in recipeEditor.Views) view.Visible = page == Editing;
            recipeEditor.Host.Height = Math.Max(1, content - 4);
            recipeEditor.ValidationTitle.Y = content - 1;
            recipeEditor.Validation.Y = content;
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

        void Notify(string glyph, string @class, string text, int target, (string Key, Func<Task<string>> Run)? retry = null, string? updates = null)
        {
            notices.Add(new(DateTimeOffset.Now, glyph, @class, text, target, retry, updates));
            if (notices.Count > 100) notices.RemoveAt(0);
            unread++;
            // Terminal bell: the terminal decides whether it sounds or flashes.
            try { consoleOut.Write(''); consoleOut.Flush(); } catch (IOException) { }
        }

        // The row button acts; Enter on the row shows the notice, as Ver detalle or Ver log do.
        void NoticeAction(Notice notice)
        {
            if (notice.Updates is { } root)
            {
                places = definitionSource.Places();
                var place = places.FindIndex(candidate => candidate.Context?.Root == root);
                if (place < 0) { Query(app, "Actualizaciones", "El ámbito de este aviso ya no está disponible.", "Aceptar"); return; }
                focusedPlace = place;
                definitionView = "Actualizaciones";
                Open(Definitions);
                return;
            }
            if (notice.Retry is { } retry)
            {
                var row = notices.FindLastIndex(candidate => ReferenceEquals(candidate, notice));
                if (managementJobs.ContainsKey(retry.Key)) { Query(app, "Operación en curso", "Ya hay una operación de este tipo sobre el destino elegido.", "Aceptar"); return; }
                if (row >= 0)
                {
                    notices[row] = notice with { Retry = null };
                    if (ReferenceEquals(focusedNotice, notice)) focusedNotice = notices[row];
                }
                ManagementJob(retry.Key, retry.Run);
                Render();
                return;
            }
            OpenNotice(notice);
        }

        void OpenNotice(Notice notice)
        {
            if (notice.Target < 0) { Query(app, "Detalle del aviso", notice.Text, "Aceptar"); return; }
            if (notice.Target != index && notice.Target < targets.Count) { index = notice.Target; preferredEntry = null; selectionEpoch++; Select(); }
            Open(Live);
        }

        void Render()
        {
            var now = DateTimeOffset.UtcNow;
            tabs.Rows = [LivePresentation.Tabs(page is Definitions or Editing ? 1 : page is Settings ? 2 : page is DownloadsPage ? 3 : page is Notices ? 4 : 0,
                transferCount, unread, transferIndicator)];
            RenderBar(now);
            switch (page)
            {
                case Live: RenderLive(now); break;
                case Hardware: RenderHardware(now); break;
                case History: RenderHistory(); break;
                case Notices: RenderNotices(); break;
                case DownloadsPage: RenderDownloads(); break;
                case Definitions: RenderDefinitions(); break;
                case Editing: recipeEditor.Render(window.Viewport.Width); break;
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
            var level = historyLevel switch { 1 => "avisos y errores", 2 => "sólo errores", _ => "todos" };
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

        // Reads Global and the current project; a scope that cannot be read is reported without hiding the other.
        void ReadTransfers()
        {
            var groups = new List<(DefinitionsSource.Place, ScopeContext, DownloadsPresentation.Group)>();
            var problems = new List<string>();
            string? problem = null;
            var now = DateTimeOffset.UtcNow;
            var seen = new HashSet<Guid>();
            foreach (var place in definitionSource.Places().Where(place => place.Scope.Global || place.Scope.Here))
            {
                if (!place.Scope.Accessible) { problems.Add("No se puede abrir la carpeta de " + place.Scope.Name + "."); continue; }
                // A folder without recipe can already hold a local scope created by the CLI.
                var context = place.Context ?? (place.Scope.Here && place.Folder is { } folder && Directory.Exists(Path.Combine(folder, ".coquena"))
                    ? ScopeContext.Project(Path.Combine(folder, "coquena.yaml")) : null);
                if (context is null) continue;
                try
                {
                    var items = TransferList(context).Select(operation => TransferItem(context, operation, now)).ToArray();
                    seen.UnionWith(items.Select(item => item.Id));
                    groups.Add((place, context, new(place.Scope.Name, place.Scope.Here && !place.Scope.Global, items)));
                }
                catch (Exception error) when (ManagementError(error))
                {
                    problems.Add("No se pudieron leer las descargas de " + place.Scope.Name + ".");
                    problem = place.Scope.Name + ": " + error.Message;
                }
            }
            foreach (var gone in transferSamples.Keys.Where(id => !seen.Contains(id)).ToArray()) transferSamples.Remove(gone);
            transferGroups = groups;
            transferProblems = problems;
            var all = groups.SelectMany(group => group.Item3.Items).ToArray();
            transferCount = all.Count(item => DownloadsPresentation.Visible(item.State) && DownloadsPresentation.Rank(item.State) == 0);
            transferIndicator = DownloadsPresentation.Indicator(all);
            if (problem is not null && problem != transferProblem) Notify("✗", "err", "Descargas · " + problem, -1);
            transferProblem = problem;
        }

        // Speed comes from the confirmed bytes between two reads; the start is the operation folder's creation.
        DownloadsPresentation.Item TransferItem(ScopeContext context, Coquena.Contracts.OperationDocument operation, DateTimeOffset now)
        {
            var row = TransferRow(operation);
            double? speed = null;
            if (transferSamples.TryGetValue(operation.Id, out var last) && (now - last.At).TotalSeconds > 0 && row.Done >= last.Done)
                speed = row.Done > last.Done ? (row.Done - last.Done) / (now - last.At).TotalSeconds : last.Speed;
            transferSamples[operation.Id] = (row.Done, now, speed);
            // Operations created before CreatedAt existed fall back to their folder's creation time.
            var started = operation.CreatedAt ?? operation.UpdatedAt;
            if (operation.CreatedAt is null)
                try
                {
                    var folder = context.PathFor("downloads/" + operation.Id.ToString("D"));
                    if (Directory.Exists(folder)) started = Directory.GetCreationTimeUtc(folder);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or StorageException) { }
            return new(operation.Id, row.Name, row.Kind, row.State, row.Phase, row.Done, row.Total, row.Diagnostic, started,
                operation.State == "downloading" ? speed : null);
        }

        void RenderDownloads()
        {
            var groups = transferGroups.Select(item => item.Group).ToArray();
            downloadTitle.Rows = [" " + DownloadsPresentation.Title(groups) + " "];
            downloadDetail.Rows = DownloadsPresentation.Rows(groups, transferProblems);
        }

        // Pausar, Reanudar, Cancelar and Reintentar act on the operation of the row's own scope.
        void DownloadAction(string action)
        {
            if (page != DownloadsPage) return;
            var split = action.Split(':', 3);
            if (split.Length != 3 || split[0] is not ("pause" or "resume" or "cancel") ||
                !int.TryParse(split[1], CultureInfo.InvariantCulture, out var group) || group < 0 || group >= transferGroups.Count ||
                !Guid.TryParse(split[2], out var operationId)) return;
            var (place, context, _) = transferGroups[group];
            try
            {
                if (split[0] == "cancel" && Query(app, "Cancelar descarga", "Se borrará lo descargado hasta ahora.", "No cancelar", "Cancelar descarga") != 1) return;
                var label = "Descarga " + Transfers(context).Downloads.Status(operationId).Name + " · " + place.Scope.Name;
                ManagementJob(split[0] + ":" + context.Root + operationId, async () =>
                {
                    try
                    {
                        if (split[0] == "resume") return label + ": " + await ResumeTransfer(context, operationId, app, stop.Token);
                        await Transfers(context).ControlAsync(operationId, split[0] == "cancel", stop.Token);
                    }
                    // The transfer finished or was removed while the confirmation was open.
                    catch (Coquena.Downloads.DownloadException error) when (error.Code == "not-found") { return label + ": " + Finished; }
                    return label + ": " + (split[0] == "cancel" ? "cancelada." : "pausada.");
                });
            }
            catch (Coquena.Downloads.DownloadException error) when (error.Code == "not-found")
            {
                transferClock.Reset();
                Query(app, "Descarga", "La descarga " + Finished, "Aceptar");
            }
            catch (Exception error) when (ManagementError(error)) { Query(app, "No se completó la operación", error.Message, "Aceptar"); }
        }

        void RenderNotices()
        {
            noticeTitle.Rows = [" Notificaciones" + (unread > 0 ? "  " + Markup.Style("mut", unread + " sin leer") : "") + "{pad}" +
                (unread > 0 ? Markup.Style("btn", " Marcar leídas ", "read") + " " : "")];
            noticeList.RowZone = y => noticeList.HasFocus && noticeList.Top + y == noticeList.Selected ? "chg" : null;
            if (notices.Count == 0) { noticeList.Show(1, (_, _) => " " + Markup.Style("mut", "Todavía no hay avisos en esta sesión.")); return; }
            var focused = focusedNotice is null ? -1 : notices.FindLastIndex(notice => ReferenceEquals(notice, focusedNotice));
            noticeList.Show(notices.Count, (row, selected) =>
            {
                var notice = notices[notices.Count - 1 - row];
                var text = " " + Markup.Style(notice.Class, notice.Glyph) + " " + Markup.Style("mut", notice.At.ToString("HH:mm", CultureInfo.InvariantCulture)) +
                    " " + Markup.Escape(notice.Text.Split('\n')[0]) + "{pad}" + Markup.Style("btn", " " + LivePresentation.NoticeAction(notice.Updates is not null, notice.Retry is not null, notice.Target >= 0) + " ", "n" + row) + " ";
                return text;
            }, focused < 0 ? null : notices.Count - 1 - focused);
            if (focused >= 0) noticeList.ScrollTo(notices.Count - 1 - focused);
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
                try
                {
                    if (definitionView is "Servicios" or "Servicio" && place.Context is not null)
                    {
                        var services = ServiceDefinitions(place, targets, snapshots);
                        var selected = managedSelection.GetValueOrDefault(place.Context.Root);
                        if (selected is null || !services.Any(service => service.Key == selected)) selected = services.FirstOrDefault()?.Key;
                        if (selected is not null) managedSelection[place.Context.Root] = selected;
                        definitionPage = ManagementPresentation.Services(place.Scope.Name, place.Scope.Global, services, selected,
                            Math.Max(20, window.Viewport.Width - Right));
                    }
                    else if (definitionView is "Backends" or "Modelos" && place.Context is not null)
                    {
                        var backend = definitionView == "Backends";
                        var key = place.Context.Root + definitionView;
                        // Read once per invalidation; finished jobs clear the cache.
                        if (!resourceReads.ContainsKey(key) && !resourceCache.ContainsKey(key) && !resourceProblems.ContainsKey(key))
                        {
                            var captured = place.Context;
                            resourceReads[key] = Task.Run(() => Inventory(captured, backend), stop.Token);
                        }
                        var items = resourceCache.GetValueOrDefault(key) ?? [];
                        var filter = resourceFilters.GetValueOrDefault(key) ?? "";
                        items = items.Where(item => item.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
                        var selected = managedSelection.GetValueOrDefault(key);
                        if (!items.Any(item => item.Id == selected)) selected = items.FirstOrDefault()?.Id;
                        if (selected is not null) managedSelection[key] = selected;
                        definitionPage = ResourcePage(place.Scope.Name, backend, items, selected, filter, Math.Max(20, window.Viewport.Width - Right), definitionDetail.Viewport.Height, evidence?.Context);
                        if (resourceProblems.TryGetValue(key, out var problem))
                            definitionPage = DefinitionsPresentation.Problem((backend ? "Backends" : "Modelos") + " de " + place.Scope.Name, problem);
                        else if (!resourceCache.ContainsKey(key))
                            definitionPage = definitionPage with { Rows = [" " + Markup.Style("mut", "Consultando inventario…")] };
                        if (!backend && hubViews.TryGetValue(place.Context.Root, out var hub) && hub.Active)
                            definitionPage = HubPage(place.Scope.Name, hub, Math.Max(20, window.Viewport.Width - Right),
                                managementJobs.ContainsKey("hf:" + place.Context.Root));
                    }
                    else if (definitionView == "Mantenimiento" && place.Context is not null)
                    {
                        if (!maintenanceViews.TryGetValue(place.Context.Root, out var state)) maintenanceViews[place.Context.Root] = state = new();
                        definitionPage = MaintenancePage(place.Scope.Name, state);
                    }
                    else if (definitionView == "Recetas" && place.Context is not null)
                    {
                        var combinations = AvailableCombinations(place);
                        var key = place.Context.Root + "Recetas";
                        var selected = managedSelection.GetValueOrDefault(key);
                        if (!combinations.Any(item => item.Key == selected)) selected = combinations.FirstOrDefault(item => item.Current)?.Key ?? combinations.FirstOrDefault()?.Key;
                        if (selected is not null) managedSelection[key] = selected;
                        string? destination = null;
                        try { destination = place.Recipe is null ? null : RecipeReader.Read(place.Recipe).Target; }
                        catch (RecipeException) { }
                        definitionPage = RecipesPage(place.Scope.Name, destination, combinations, selected, Math.Max(20, window.Viewport.Width - Right));
                    }
                    else if (definitionView == "Presets" && place.Context is not null)
                    {
                        var presets = PresetRows(place.Context, AuthorSelections(place), snapshots);
                        var key = place.Context.Root + "Presets";
                        var selected = managedSelection.GetValueOrDefault(key);
                        if (!presets.Any(preset => preset.Id == selected)) selected = presets.FirstOrDefault()?.Id;
                        if (selected is not null) managedSelection[key] = selected;
                        definitionPage = PresetsPage(place.Scope.Name, presets, selected, Math.Max(20, window.Viewport.Width - Right));
                    }
                    else if (definitionView == "Actualizaciones" && place.Context is not null)
                    {
                        if (!updateViews.TryGetValue(place.Context.Root, out var state)) updateViews[place.Context.Root] = state = new();
                        ReadUpdates(place.Context, AuthorSelections(place), state);
                        var ids = (state.Plan?["resources"] as System.Text.Json.Nodes.JsonArray ?? []).Select(item => item?["installation-id"]?.ToString()).OfType<string>().ToArray();
                        var impact = snapshots.Values.Where(snapshot => snapshot.ProcessState == "alive" && ids.Any(id => ContainsIdentity(snapshot.ActiveComposition, id)))
                            .Select(snapshot => snapshot.Name + ": conserva su revisión hasta aplicar/reiniciar explícitamente").ToArray();
                        definitionPage = UpdatesPage(place.Scope.Name, state, impact, managementJobs.ContainsKey("update:" + place.Context.Root));
                    }
                    else if (definitionView == "Parámetros" && place.Context is not null)
                    {
                        var key = place.Context.Root + "Parámetros";
                        var filter = resourceFilters.GetValueOrDefault(key) ?? "";
                        var definitions = DefinitionRows(DefinitionSelections(place)).Where(row => row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
                        var selected = managedSelection.GetValueOrDefault(key);
                        if (!definitions.Any(row => row.Id == selected)) selected = definitions.FirstOrDefault()?.Id;
                        if (selected is not null) managedSelection[key] = selected;
                        definitionPage = ParametersPage(place.Scope.Name, definitions, selected, filter);
                    }
                }
                catch (Exception error) when (ManagementError(error)) { definitionPage = DefinitionsPresentation.Problem(definitionView ?? "Definiciones", error.Message); }
            }
            leftTitle.Rows = [""];
            rightTitle.Rows = [" " + definitionPage.Title];
            definitionDetail.Rows = definitionPage.Rows;
        }

        void RefreshTargets()
        {
            var previous = targets.ElementAtOrDefault(index);
            selectionEpoch++;
            foreach (var session in logSessions.Values) retiredLogs.Add(session.CloseAsync());
            logSessions.Clear(); snapshots.Clear(); catalogues.Clear(); serviceCaptures.Clear();
            // Old captures must finish before their numeric slots can be reused.
            foreach (var sample in serviceSamples.Values) _ = sample.Task.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            serviceSamples.Clear();
            var previousTargets = targets;
            targets = Discover(out diagnostic);
            foreach (var previousTarget in previousTargets.Where(target => !target.Store.Context.IsGlobal && target.Service is not null))
            {
                if (targets.Any(target => target.Service?.ServiceId == previousTarget.Service!.ServiceId && SameScope(target.Store.Context, previousTarget.Store.Context))) continue;
                try
                {
                    if (new RecipeRegistry(previousTarget.Store).Services().Any(service => service.ServiceId == previousTarget.Service!.ServiceId)) targets.Add(previousTarget);
                }
                catch (Exception error) when (ManagementError(error)) { diagnostic = error.Message; }
            }
            for (var row = 0; row < notices.Count; row++)
            {
                var target = previousTargets.ElementAtOrDefault(notices[row].Target);
                var focused = ReferenceEquals(notices[row], focusedNotice);
                notices[row] = notices[row] with { Target = target is null ? -1 : targets.FindIndex(candidate =>
                    candidate.Service?.ServiceId == target.Service?.ServiceId && SameScope(candidate.Store.Context, target.Store.Context)) };
                if (focused) focusedNotice = notices[row];
            }
            index = Math.Max(0, targets.FindIndex(target => previous is not null && target.Service?.ServiceId == previous.Service?.ServiceId &&
                SameScope(target.Store.Context, previous.Store.Context)));
            Select();
        }

        void ManagementJob(string key, Func<Task<string>> run)
        {
            if (managementJobs.ContainsKey(key)) { Query(app, "Operación en curso", "Ya hay una operación de este tipo sobre el destino elegido.", "Aceptar"); return; }
            managementJobs[key] = Task.Run(run, stop.Token);
            managementRuns[key] = run;
        }

        void DefinitionAction(string action)
        {
            if (page != Definitions) return;
            var place = places[focusedPlace];
            if (action == "init" && place.Context is null && place.Folder is { } folder)
            {
                using var output = new StringWriter();
                using var failure = new StringWriter();
                var code = InitCli.Run([folder], output, failure);
                Notify(code == 0 ? "✓" : "✗", code == 0 ? "ok" : "err", (code == 0 ? output : failure).ToString().Trim(), -1);
                places = definitionSource.Places();
                focusedPlace = Math.Max(0, places.FindIndex(candidate => candidate.Context is not null &&
                    candidate.Context.Base.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase)));
                definitionView = DefinitionsPresentation.Views(places[focusedPlace].Scope).FirstOrDefault();
                definitionPage = null; Render(); return;
            }
            if (place.Context is not { } context) return;
            try
            {
                if (action == "recipe-import")
                {
                    var combination = SelectedCombination(place) ?? throw new ArgumentException("Seleccioná una combinación.");
                    var recipe = RecipeReader.Read(combination.Selection);
                    var global = ScopeContext.Global(Home.Resolve());
                    var form = ImportForm(app, recipe, global);
                    if (form is null) return;
                    var importSelection = combination.Selection;
                    var args = new List<string> { "import", importSelection.Files[0], "--project-directory", importSelection.Base, "--select", form.Categories };
                    foreach (var file in importSelection.Files.Skip(1)) args.AddRange(["-f", file]);
                    foreach (var file in importSelection.EnvFiles) args.AddRange(["--env-file", file]);
                    foreach (var entity in form.Replacements) args.AddRange(["--replace", entity]);
                    if (form.Default) args.Add("--as-default");
                    ManagementJob("import:" + global.Root, async () =>
                    {
                        var output = new StringWriter(); var error = new StringWriter();
                        var code = await ImportCli.RunAsync(args.ToArray(), output, error, new DashboardModelSelection(app, error),
                            message => AskOnUi(app, "Importar · confirmar publicación", message, "Aplicar", stop.Token).GetAwaiter().GetResult(), stop.Token);
                        var text = output + "\n" + error;
                        if (code != 0) throw new IOException(text);
                        return text;
                    });
                    return;
                }
                if (action.StartsWith("update-", StringComparison.Ordinal))
                {
                    if (!updateViews.TryGetValue(context.Root, out var state)) updateViews[context.Root] = state = new();
                    var selections = AuthorSelections(place);
                    ReadUpdates(context, selections, state);
                    if (action.StartsWith("update-toggle:", StringComparison.Ordinal))
                    {
                        var selector = System.Text.Encoding.UTF8.GetString(Convert.FromHexString(action[14..]));
                        if (!state.Chosen.Add(selector)) state.Chosen.Remove(selector);
                        definitionPage = null; Render(); return;
                    }
                    if (action == "update-check")
                    {
                        if (state.Unprepared) throw new ArgumentException(state.Notice);
                        var fields = ResourceForm(app, "Comprobar novedades · filtros opcionales", [("referencias", "")], "Comprobar");
                        if (fields is null) return;
                        var filters = fields[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach (var filter in filters) _ = ResourceSelector.Parse(filter);
                        ManagementJob("update:" + context.Root, async () =>
                        {
                            using var client = HuggingFaceStore.CreateClient();
                            using var github = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(60) };
                            var source = new OfficialBackendSource(github); var hardware = HardwareProbe.Inspect();
                            var output = new StringWriter();
                            var probe = new ResourceUpdateProbe(new(client), (variant, token) => source.ResolveAsync(hardware, variant, "latest", token), new DashboardModelSelection(app, output));
                            var plan = await new RecipeUpdatePlans(context).UpdateAsync(selections, filters, probe, stop.Token);
                            var report = RecipeUpdatePlans.Report(plan);
                            var text = "Comprobación completada en " + place.Scope.Name + "\n" + UpdateReportText(report);
                            if (report["resources"]!.AsArray().Any(item => item?["status"]?.ToString() is not ("unchanged" or "update-available" or "not-applicable"))) throw new IOException(text);
                            return text;
                        });
                    }
                    else if (action == "update-apply")
                    {
                        if (state.Problem is not null) throw new ArgumentException(state.Problem);
                        var filters = state.Chosen.ToHashSet(StringComparer.Ordinal);
                        if (filters.Count == 0) return;
                        if (Query(app, "Actualizar · " + place.Scope.Name, string.Join("\n", filters) + "\nSe aplica el plan persistido como conjunto; no se modifica YAML ni se reinician servicios.", "Cancelar", "Actualizar") != 1) return;
                        ManagementJob("update:" + context.Root, async () =>
                        {
                            using var models = new HuggingFaceTransport(); using var backends = new OfficialArtifactTransport();
                            var report = await new RecipeUpgrade(context).ApplyAsync(selections, filters, HardwareProbe.Inspect(), models, backends,
                                new ScopedBackends(context).ValidateOfficial, _ => { }, cancellation: stop.Token);
                            if (report["resources"]!.AsArray().Any(item => item?["upgrade-status"]?.ToString() == "blocked")) throw new IOException(UpdateReportText(report));
                            return "Actualización de " + place.Scope.Name + "\n" + UpdateReportText(report);
                        });
                    }
                    return;
                }
                if (action == "author-selection")
                {
                    var options = place.Scope.Global ? UpdateCli.GlobalSelections(context) : AvailableCombinations(place).Select(item => item.Selection).ToArray();
                    var row = Choose(app, "Combinación · " + place.Scope.Name,
                        (place.Scope.Global ? new[] { "Todas las intenciones de Global" } : Array.Empty<string>()).Concat(options.Select(selection => string.Join(" + ", selection.Files.Select(file => Path.GetRelativePath(context.Base, file))))).ToArray(), "Elegir");
                    if (row is int choice)
                    {
                        authorSelections[context.Root] = place.Scope.Global && choice == 0 ? options : [options[choice - (place.Scope.Global ? 1 : 0)]];
                        if (!place.Scope.Global)
                            managedSelection[context.Root + "Recetas"] = AvailableCombinations(place).Single(item => CombinationIdentity.FromSelection(item.Selection).Id == CombinationIdentity.FromSelection(options[choice]).Id).Key;
                    }
                    definitionPage = null; Render(); return;
                }
                if (action.StartsWith("preset:", StringComparison.Ordinal) || action.StartsWith("param:", StringComparison.Ordinal))
                { managedSelection[context.Root + definitionView] = action[(action.IndexOf(':') + 1)..]; definitionPage = null; Render(); return; }
                if (action.StartsWith("preset-", StringComparison.Ordinal))
                {
                    var items = PresetRows(context, AuthorSelections(place), snapshots);
                    var chosen = items.FirstOrDefault(item => item.Id == managedSelection.GetValueOrDefault(context.Root + "Presets")) ?? items.FirstOrDefault()
                        ?? throw new ArgumentException("Seleccioná un preset.");
                    if (action is "preset-next" or "preset-prev")
                    { managedSelection[context.Root + "Presets"] = items[Math.Clamp(Array.IndexOf(items, chosen) + (action == "preset-next" ? 4 : -4), 0, items.Length - 1)].Id; }
                    else if (action == "preset-edit") { OpenEditor(chosen.Recipe.Selection, place.Scope.Name, AuthorPrivate(place, chosen.Recipe.Selection)); FocusOrigin(chosen.Value.Origin); return; }
                    else if (action == "preset-detail") Choose(app, chosen.Name, ["Receta: " + chosen.Recipe.Name, "Archivo: " + chosen.Value.Origin.File,
                        chosen.Value.Redacted()?.ToJsonString() ?? "—", "Publicación: " + (chosen.Published is null ? "sin evidencia en la instancia observada" : "snapshot de la instancia; no prueba carga")], null);
                    else if (action == "preset-clone")
                    {
                        // An invalid name says why and reopens the form with what was typed.
                        var name = "";
                        while (true)
                        {
                            var fields = ResourceForm(app, "Clonar preset · " + chosen.Name, [("nuevo nombre", name)], "Abrir borrador");
                            if (fields is null) return;
                            name = fields[0].Trim();
                            var reason = name.Length == 0 ? "Escribí un nombre para el preset nuevo."
                                : chosen.Recipe.Root.Get("presets")?.Get(name) is not null ? "Ya existe un preset «" + name + "» en esta combinación." : null;
                            if (reason is null) break;
                            Query(app, "Clonar preset · nombre no válido", reason, "Aceptar");
                        }
                        StageEdit(chosen.Recipe.Selection, place.Scope.Name, chosen.Recipe.Selection.Files[^1], ["presets", name], RecipeEditCli.Render(chosen.Recipe.SourceRoot.Get("presets")!.Get(chosen.Name)!));
                        return;
                    }
                    else if (action == "preset-remove")
                    {
                        if (Query(app, "Eliminar definición · " + chosen.Name, "Se prepara una desasignación en YAML. Guardar no borra pesos ni retira publicaciones; Up aplicará el cambio explícitamente.", "Cancelar", "Abrir borrador") != 1) return;
                        StageEdit(chosen.Recipe.Selection, place.Scope.Name, chosen.Recipe.Selection.Files[^1], ["presets", chosen.Name], "null"); return;
                    }
                    else if (action == "preset-live" && chosen.Target is int target)
                    { index = target; preferredEntry = context.IsGlobal ? chosen.Name : chosen.Recipe.Name + "/" + chosen.Name; selectionEpoch++; Select(); Open(Live); return; }
                    else if (action.StartsWith("preset-resource:", StringComparison.Ordinal))
                    {
                        var argument = System.Text.Encoding.UTF8.GetString(Convert.FromHexString(action[16..]));
                        var value = chosen.Value.Get("args")?.Get(argument);
                        if (value is null || value.Sensitive) throw new ArgumentException("Sin referencia de inventario visible.");
                        var reference = ArgumentValidation.Reference(chosen.Recipe, value, false, true);
                        if (reference is null) throw new ArgumentException("El valor es literal; no identifica un recurso del inventario.");
                        var requests = CombinationPreparation.Requests(chosen.Recipe, ArgumentValidation.Validate(chosen.Recipe));
                        var request = requests.SingleOrDefault(item => item.Kind == reference.Kind && item.Key.Equals(reference.Name, StringComparison.OrdinalIgnoreCase));
                        var binding = request is null ? null : CombinationPreparation.VerificationBinding(context, chosen.Recipe, request, chosen.Recipe.Selection.EnvFiles.Count > 0);
                        definitionView = "Modelos";
                        resourceFilters[context.Root + definitionView] = binding?.InstallationId is null ? reference.Name : "";
                        if (binding?.InstallationId is { } installation) managedSelection[context.Root + definitionView] = installation;
                        resourceCache.Remove(context.Root + definitionView);
                    }
                    definitionPage = null; Render(); return;
                }
                if (action.StartsWith("param-", StringComparison.Ordinal))
                {
                    var selections = DefinitionSelections(place);
                    if (selections.Length == 0) throw new ArgumentException("Seleccioná una intención existente.");
                    var key = context.Root + "Parámetros";
                    if (action == "param-filter")
                    {
                        var fields = ResourceForm(app, "Filtrar parámetros", [("nombre", resourceFilters.GetValueOrDefault(key) ?? "")], "Filtrar");
                        if (fields is not null) resourceFilters[key] = fields[0];
                    }
                    else
                    {
                        var items = DefinitionRows(selections).Where(row => row.Name.Contains(resourceFilters.GetValueOrDefault(key) ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
                        var chosen = items.FirstOrDefault(item => item.Id == managedSelection.GetValueOrDefault(key)) ?? items.FirstOrDefault();
                        if (action is "param-prev" or "param-next" && chosen is not null)
                            managedSelection[key] = items[Math.Clamp(Array.IndexOf(items, chosen) + (action == "param-next" ? 4 : -4), 0, items.Length - 1)].Id;
                        else if (action == "param-define")
                        {
                            var fields = ResourceForm(app, "Definir parámetro · completar metadatos en YAML", [("nombre", ""), ("tipo", "string"), ("ámbito", "preset")], "Abrir borrador");
                            if (fields is null) return;
                            var recipe = RecipeReader.Read(selections[0]);
                            if (ArgumentSchema.Load(recipe).Definitions.ContainsKey(fields[0])) throw new ArgumentException("Ya existe esa definición.");
                            StageEdit(selections[0], place.Scope.Name, ParameterFile(recipe), ["arg-defs", "definitions", fields[0]],
                                "{type: " + RecipeEdit.Quote(fields[1]) + ", scope: " + RecipeEdit.Quote(fields[2]) + "}", true, context.IsGlobal ? context : null); return;
                        }
                        else if (chosen is not null)
                        {
                            if (action == "param-edit") { OpenEditor(chosen.Recipe.Selection, place.Scope.Name, AuthorPrivate(place, chosen.Recipe.Selection)); recipeEditor.RequireValidSave = true; FocusOrigin(chosen.Value.Origin); return; }
                            if (action == "param-remove")
                            {
                                if (Query(app, "Quitar definición · " + chosen.Name, "Asignaciones en la selección: " + string.Join(", ", chosen.Uses) + "\nSe conservan valores y recursos. Se validará el modo estricto y las reglas antes de permitir guardar.", "Cancelar", "Abrir borrador") != 1) return;
                                StageEdit(chosen.Recipe.Selection, place.Scope.Name, ParameterFile(chosen.Recipe), ["arg-defs", "definitions", chosen.Name], "null", true); return;
                            }
                            if (action == "param-detail") Choose(app, chosen.Name + " · definición efectiva", ["Origen: " + chosen.Value.Origin.File, chosen.Value.Redacted()?.ToJsonString() ?? "—", "Usado por: " + string.Join(", ", chosen.Uses)], null);
                        }
                    }
                    definitionPage = null; Render(); return;
                }
                if (action == "recipe-new")
                {
                    var combination = SelectedCombination(place);
                    var current = combination?.Selection ?? place.Recipe;
                    if (current is null) return;
                    var form = CombinationForm(app, current, combination?.Private == true);
                    if (form is null) return;
                    var key = CombinationIdentity.FromSelection(form.Selection).Id;
                    sessionCombinations[context.Root] = new(key, null, form.Selection, form.Private, false);
                    authorSelections[context.Root] = [form.Selection];
                    combinationNewFiles[context.Root] = form.NewFiles;
                    managedSelection[context.Root + "Recetas"] = key;
                    definitionPage = null;
                    if (form.NewFiles.Count > 0) OpenEditor(form.Selection, place.Scope.Name, form.Private, form.NewFiles);
                    else Render();
                    return;
                }
                if (action == "downloads") { Open(DownloadsPage); return; }
                if (action.StartsWith("maintenance-", StringComparison.Ordinal))
                {
                    if (!maintenanceViews.TryGetValue(context.Root, out var state)) maintenanceViews[context.Root] = state = new();
                    if (action == "maintenance-preview")
                        ManagementJob("maintenance:" + context.Root, () =>
                        {
                            var plan = new ResourceCleanup(context, Home.Resolve()).Preview();
                            app.Invoke(() => { state.Plan = plan; definitionPage = null; Render(); });
                            return Task.FromResult("Plan de limpieza de " + place.Scope.Name + ": " + ByteSize.Format(plan.Bytes) + " recuperables.");
                        });
                    else if (action == "maintenance-clean" && state.Plan is { } plan)
                    {
                        if (managementJobs.ContainsKey("maintenance:" + context.Root)) return;
                        if (Query(app, "Limpiar · " + place.Scope.Name, "Eliminar el conjunto completo mostrado: " + plan.Items.Count + " instalaciones.\nRecuperable: " + ByteSize.Format(plan.Bytes) +
                            ".\nSe revalidan referencias y protecciones antes de borrar.", "Cancelar", "Eliminar conjunto") != 1) return;
                        ManagementJob("maintenance:" + context.Root, () =>
                        {
                            var result = new ResourceCleanup(context, Home.Resolve()).Apply(plan);
                            var text = "Última limpieza de esta sesión · " + DateTimeOffset.Now.ToString("dd/MM HH:mm") + "\n" + CleanupText(result);
                            app.Invoke(() => { state.Plan = null; state.Cleanup = text; definitionPage = null; Render(); });
                            if (result.Diagnostics.Count > 0) throw new IOException(text);
                            return Task.FromResult(text);
                        });
                    }
                    else if (action == "maintenance-verify")
                    {
                        var mode = Query(app, "Verificar · " + place.Scope.Name, "Auditar definición y recursos; no descarga ni repara.", "Cancelar", "Auditoría rápida", "SHA-256");
                        if (mode <= 0) return;
                        var args = new List<string>();
                        if (context.IsGlobal)
                        {
                            args.Add("--global");
                            var include = Query(app, "Alcance de verificación", "Incluir instalaciones del inventario que no están referenciadas?", "Cancelar", "Sólo referencias", "Todo el inventario");
                            if (include <= 0) return;
                            if (include == 2) args.Add("--include-unused");
                        }
                        else
                        {
                            var combinations = new CombinationPreparation(context).List().ToArray();
                            var selections = combinations.Select(item => new RecipeSelection(context.Base,
                                item.Value!["files"]!.AsArray().Select(file => file!.GetValue<string>()), context.Base,
                                item.Value["env-files"]!.AsArray().Select(file => file!.GetValue<string>()))).ToList();
                            if (place.Recipe is not null) selections.Insert(0, place.Recipe);
                            var choice = Choose(app, "Combinación a verificar", selections.Select(selection => string.Join(" + ", selection.Files.Select(Path.GetFileName))).ToArray(), "Verificar");
                            if (choice is not int row) return;
                            var selection = selections[row];
                            args.AddRange(["--project-directory", context.Base]);
                            foreach (var file in selection.Files) args.AddRange(["-f", file]);
                            foreach (var file in selection.EnvFiles) args.AddRange(["--env-file", file]);
                            var combinationIndex = row - (place.Recipe is null ? 0 : 1);
                            if (combinationIndex >= 0 && combinations[combinationIndex].Value?["private"]?.GetValue<bool>() == true) args.Add("--private");
                        }
                        var filters = ResourceForm(app, "Filtrar verificación · " + place.Scope.Name, [("referencias", "")], "Verificar todo / filtro");
                        if (filters is null) return;
                        foreach (var selector in filters[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        { _ = ResourceSelector.Parse(selector); args.AddRange(["--select", selector]); }
                        if (mode == 2) args.Add("--checksum");
                        ManagementJob("maintenance:" + context.Root, async () =>
                        {
                            var output = new StringWriter();
                            var code = await VerifyCli.RunAsync(args.ToArray(), output, stop.Token);
                            var text = DateTimeOffset.Now.ToString("dd/MM HH:mm") + " · " + (code == 0 ? "✓" : "▲") + "\n" + output.ToString().Trim();
                            app.Invoke(() => { state.Verification = text; definitionPage = null; Render(); });
                            if (code != 0) throw new IOException(text);
                            return text;
                        });
                    }
                    return;
                }
                if (action == "inventory" || action == "hf" || action.StartsWith("hf-", StringComparison.Ordinal))
                {
                    // Network actions are disabled while a query runs; the status line says which one.
                    if (managementJobs.ContainsKey("hf:" + context.Root) && (action is "hf-search" or "hf-more-results" or
                        "hf-repository" or "hf-revision" || action.StartsWith("hf-result:", StringComparison.Ordinal))) return;
                    if (!hubViews.TryGetValue(context.Root, out var hub)) hubViews[context.Root] = hub = new();
                    hub.Active = action != "inventory";
                    if (action is "inventory" or "hf") { definitionPage = null; Render(); return; }
                    // One continuous list: › past the loaded results asks the Hub for the next page and appends it.
                    void SearchHub(Uri? start)
                    {
                        var query = hub.Query;
                        hub.Status = start is null ? "Buscando «" + (query.Length == 0 ? "GGUF" : query) + "»…" : "Cargando más resultados…";
                        ManagementJob("hf:" + context.Root, async () =>
                        {
                            using var client = HuggingFaceStore.CreateClient();
                            var result = await new HuggingFaceStore(client).SearchAsync(query, start, stop.Token);
                            app.Invoke(() =>
                            {
                                if (start is null) { hub.Results.Clear(); hub.ResultOffset = 0; }
                                else if (result.Repositories.Length > 0) hub.ResultOffset = hub.Results.Count;
                                hub.Results.AddRange(result.Repositories.Where(repository => !hub.Results.Contains(repository)));
                                hub.Next = result.Next; hub.Searched = true;
                                definitionPage = null; Render();
                            });
                            return "Búsqueda HF: " + result.Repositories.Length + " resultados en la página.";
                        });
                    }
                    void ReadRepository(string repository, string revision)
                    {
                        hub.Status = "Consultando " + repository + " @ " + revision + "…";
                        ManagementJob("hf:" + context.Root, async () =>
                        {
                            using var client = HuggingFaceStore.CreateClient();
                            var source = new HuggingFaceStore(client);
                            var resolved = await source.ResolveAsync(repository, revision, stop.Token);
                            var inventory = await source.InventoryAsync(resolved, stop.Token);
                            using var inspector = new RemoteGgufInspector();
                            var diagnostics = new List<string>();
                            var candidates = await new ModelResolver(inspector).DiscoverAsync(inventory, diagnostics.Add, stop.Token);
                            string? readme = null;
                            try { readme = await source.ReadmeAsync(resolved, stop.Token); }
                            catch (ModelSourceException error) { diagnostics.Add("README: " + error.Message); }
                            app.Invoke(() =>
                            {
                                hub.Repository = repository; hub.Revision = revision; hub.Resolved = resolved; hub.Selected = 0;
                                hub.Variants = candidates.Variants; hub.Readme = readme;
                                hub.Diagnostics = diagnostics.Concat(candidates.Diagnostics).ToArray(); definitionPage = null; Render();
                            });
                            return "Repositorio HF consultado: " + repository + "; " + candidates.Variants.Length + " unidades seleccionables.";
                        });
                    }
                    if (action == "hf-search")
                    {
                        var fields = ResourceForm(app, "Buscar en Hugging Face · GGUF", [("buscar", hub.Query)], "Buscar");
                        if (fields is not null) { hub.Query = fields[0]; SearchHub(null); }
                    }
                    else if (action == "hf-previous-results") hub.ResultOffset = Math.Max(0, hub.ResultOffset - HubStep);
                    else if (action == "hf-more-results")
                    {
                        if (hub.ResultOffset + HubStep < hub.Results.Count) hub.ResultOffset += HubStep;
                        else if (hub.Next is { } next) SearchHub(next);
                    }
                    else if (action is "hf-repository" or "hf-revision")
                    {
                        var fields = ResourceForm(app, "Consultar repositorio GGUF", [("repositorio", hub.Repository ?? ""), ("revisión", hub.Revision)], "Consultar");
                        if (fields is not null) { HuggingFaceStore.ValidateRepository(fields[0]); ReadRepository(fields[0], fields[1]); }
                    }
                    else if (action.StartsWith("hf-result:", StringComparison.Ordinal) && int.TryParse(action[10..], out var result) && result >= 0 && result < hub.Results.Count)
                        ReadRepository(hub.Results[result], "main");
                    else if (action == "hf-readme") Choose(app, "README · " + hub.Repository, (hub.Readme ?? "README no disponible.").Split('\n'), null);
                    else if (action == "hf-files" && hub.Variants.ElementAtOrDefault(hub.Selected) is { } fileVariant)
                        Choose(app, "Archivos de " + fileVariant.Label, fileVariant.Files.Select(file => file.Path + " · " + ByteSize.Format(file.Size)).ToArray(), null);
                    else if (action == "hf-variant")
                    {
                        var variantChoice = Choose(app, "Unidad GGUF · modelos y companions explícitos", hub.Variants.Select(variant => variant.Label + " · " + (variant.CompanionType ?? "modelo")).ToArray(), "Elegir");
                        if (variantChoice is int row) hub.Selected = row;
                    }
                    else if (action == "hf-download" && hub.Resolved is { } resolved && hub.Variants.ElementAtOrDefault(hub.Selected) is { } variant)
                    {
                        var models = new ScopedModels(context);
                        var name = variant.Name(resolved.Repository);
                        var external = models.List(variant.CompanionType is not null).Any(resource => resource!["logical-key"]!.GetValue<string>().Equals(name, StringComparison.OrdinalIgnoreCase) && resource["ownership"]?.GetValue<string>() == "external");
                        if (Query(app, "Descargar en " + place.Scope.Name, name + "\n" + string.Join("\n", variant.Files.Select(file => file.Path)) +
                            (external ? "\nReemplazar registro externo al publicar; conservar sus archivos." : "\nPreparar inventario sin modificar YAML."), "Cancelar", "Descargar") != 1) return;
                        ManagementJob("model-download:" + context.Root + name, async () =>
                        {
                            using var inspector = new RemoteGgufInspector();
                            var diagnostics = new List<string>();
                            var evidence = await new ModelResolver(inspector).ValidateAsync(resolved, new(variant.Files), diagnostics.Add, stop.Token);
                            var checkedVariant = ModelSelection.Variant(new(variant.Files), evidence);
                            if (checkedVariant is null || checkedVariant.Quantization != variant.Quantization || checkedVariant.CompanionType != variant.CompanionType)
                                throw new ModelSourceException("selection-conflict", "La selección no pudo validarse.");
                            var selected = new SelectedModel(name, variant.CompanionType, new(resolved.Repository, resolved.RequestedRevision, resolved.Commit, variant.Files.Select(file => file.ToRemoteFile()).ToArray()));
                            using var transport = new HuggingFaceTransport();
                            await models.AcquireAsync(selected, transport, progress: _ => { }, cancellation: stop.Token, replaceExternal: external);
                            return "Descarga publicada en " + place.Scope.Name + ": " + name;
                        });
                    }
                    definitionPage = null; Render(); return;
                }
                if (action.StartsWith("resource:", StringComparison.Ordinal))
                { managedSelection[context.Root + definitionView] = action[9..]; definitionPage = null; Render(); return; }
                if (action == "model-filter")
                {
                    var values = ResourceForm(app, "Filtrar inventario", [("nombre", resourceFilters.GetValueOrDefault(context.Root + definitionView) ?? "")], "Aplicar");
                    if (values is not null) resourceFilters[context.Root + definitionView] = values[0];
                    definitionPage = null; Render(); return;
                }
                if (action == "model-add")
                {
                    var form = AddLocalModelForm(app, place.Scope.Name);
                    if (form is null) return;
                    ManagementJob("model-add:" + context.Root, async () =>
                    {
                        var models = new ScopedModels(context);
                        var added = models.AddExternal(form.Path, form.Name, form.Type is not null, type: form.Type,
                            projectName: place.Scope.Global ? null : place.Scope.Name);
                        var output = new StringWriter();
                        output.WriteLine("Registrado en " + place.Scope.Name + ": " + form.Name + "; archivos externos conservados.");
                        if (form.Match)
                        {
                            using var client = HuggingFaceStore.CreateClient();
                            try { await models.TryMatchAsync(added, new(client), new DashboardModelSelection(app, output), stop.Token); }
                            catch (Exception error) when (ManagementError(error) || error is HttpRequestException or OperationCanceledException)
                            { output.WriteLine("▲ Matching sin confirmar; alta conservada. " + error.Message); }
                        }
                        return output.ToString();
                    });
                    return;
                }
                if (action.StartsWith("resource-", StringComparison.Ordinal))
                {
                    var backend = definitionView == "Backends";
                    var cached = resourceCache.GetValueOrDefault(context.Root + definitionView) ?? [];
                    var item = cached.SingleOrDefault(item => item.Id == managedSelection.GetValueOrDefault(context.Root + definitionView))
                        ?? throw new ArgumentException("Seleccioná nuevamente el recurso.");
                    if (action is "resource-prev" or "resource-next")
                    {
                        var filter = resourceFilters.GetValueOrDefault(context.Root + definitionView) ?? "";
                        var items = cached.Where(row => row.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
                        var position = Array.FindIndex(items, row => row.Id == item.Id);
                        var step = Math.Max(1, Math.Min(5, definitionDetail.Viewport.Height - 12));
                        managedSelection[context.Root + definitionView] = items[Math.Clamp(position + (action == "resource-next" ? step : -step), 0, items.Length - 1)].Id;
                        definitionPage = null; Render(); return;
                    }
                    if (action == "resource-detail")
                    {
                        Choose(app, item.Label + " · " + place.Scope.Name, ResourceDetails(item, context), null);
                        return;
                    }
                    if (action == "resource-protect")
                    {
                        ManagementJob("protect:" + context.Root + item.Id, () =>
                        {
                            if (backend) new ScopedBackends(context).Protect(item.Id, !item.Protected);
                            else new ScopedModels(context).Protect(item.Id, !item.Protected);
                            return Task.FromResult(item.Label + (item.Protected ? ": protección explícita retirada; otras referencias se conservan." : ": protegido."));
                        });
                        return;
                    }
                    if (action == "resource-verify")
                    {
                        var mode = Query(app, "Verificar · " + place.Scope.Name, item.Label, "Cancelar", "Auditoría rápida", "SHA-256");
                        if (mode > 0) ManagementJob("verify:" + context.Root + item.Id, async () =>
                        {
                            var report = await new ResourceVerification(context).VerifyAsync([new(item.Name, item.Kind, item.ResourceId, item.Id)], checksum: mode == 2, cancellation: stop.Token);
                            if (!report.Verified) throw new IOException(VerificationText(report));
                            return VerificationText(report);
                        });
                        return;
                    }
                    if (action == "resource-remove")
                    {
                        if (backend)
                        {
                            var cleanup = new ResourceCleanup(context, Home.Resolve());
                            var plan = cleanup.Preview(Guid.Parse(item.Id));
                            if (plan.Items.Count == 0) throw new ArgumentException("El backend no es eliminable: protegido, externo, referenciado o con uso incierto.");
                            if (Query(app, "Eliminar · " + place.Scope.Name, item.Label + "\nRecuperable: " + ByteSize.Format(plan.Bytes), "Cancelar", "Eliminar") == 1)
                                ManagementJob("remove:" + context.Root + item.Id, () => Task.FromResult(CleanupText(cleanup.Apply(plan))));
                        }
                        else if (Query(app, "Eliminar · " + place.Scope.Name, item.Label + "\nSe retira el registro y sus instalaciones. Las referencias guardadas pueden quedar rotas. Los archivos externos se conservan.", "Cancelar", "Eliminar") == 1)
                            ManagementJob("remove:" + context.Root + item.Id, () =>
                            {
                                var current = Inventory(context, false).SingleOrDefault(row => row.Id == item.Id);
                                if (current is null || !System.Text.Json.Nodes.JsonNode.DeepEquals(current.Resource, item.Resource))
                                    throw new IOException("Cambió el recurso; revisá sus instalaciones y repetí la eliminación.");
                                var result = new ScopedModels(context).Remove(item.ResourceId, item.Kind == "companion");
                                if (!result.Complete) throw new IOException("Retirada pendiente: " + string.Join(", ", result.Pending));
                                return Task.FromResult("Recurso retirado; liberado " + ByteSize.Format(result.FreedBytes));
                            });
                        return;
                    }
                }
                if (action.StartsWith("service:", StringComparison.Ordinal))
                { managedSelection[context.Root] = action[8..]; definitionPage = null; Render(); return; }
                if (action.StartsWith("combination:", StringComparison.Ordinal))
                {
                    managedSelection[context.Root + "Recetas"] = action[12..];
                    var chosen = AvailableCombinations(place).SingleOrDefault(item => item.Key == action[12..]);
                    if (chosen is not null) authorSelections[context.Root] = [chosen.Selection];
                    definitionPage = null; Render(); return;
                }
                if (action is "recipe-edit" or "recipe-forget")
                {
                    var chosen = AvailableCombinations(place).SingleOrDefault(item => item.Key == managedSelection.GetValueOrDefault(context.Root + "Recetas"))
                        ?? throw new ArgumentException("Seleccioná nuevamente la combinación.");
                    if (action == "recipe-edit") { OpenEditor(chosen.Selection, place.Scope.Name, chosen.Private, combinationNewFiles.GetValueOrDefault(context.Root)); return; }
                    if (chosen.Id is not { } id) return;
                    if (Query(app, "Olvidar combinación · " + place.Scope.Name, chosen.Files + " · " + id[..6] +
                        "\nSe quita del registro de .coquena. Los YAML, los pesos y lo publicado se conservan; una instancia activa sigue igual.",
                        "Cancelar", "Olvidar") != 1) return;
                    ManagementJob("forget:" + context.Root + id, () =>
                    {
                        new CombinationCleanup(context).Forget(id);
                        return Task.FromResult("Combinación olvidada en " + place.Scope.Name + ": " + chosen.Files + "; YAML, pesos y publicaciones conservados.");
                    });
                    return;
                }
                if (action == "create")
                {
                    // A service only takes a backend already installed in its scope; Backends prepares them.
                    var form = ServiceForm(app, place.Scope.Name, null, InstalledBackends(context), out var goToBackends);
                    if (goToBackends) { definitionView = "Backends"; definitionPage = null; Render(); return; }
                    if (form is null) return;
                    ManagementJob("service:" + context.Root, async () =>
                    {
                        var output = new StringWriter();
                        await PublicationCli.EnsureGlobalAsync(context, output, serviceName: form.Name, backend: form.Intent,
                            cancellation: stop.Token, options: form.Options, installed: true);
                        return output.ToString().Trim();
                    });
                    return;
                }
                if (action == "backend")
                {
                    var form = BackendForm(app, place.Scope.Name, evidence?.Context);
                    if (form is null) return;
                    ManagementJob("backend:" + context.Root, async () =>
                    {
                        var output = new StringWriter();
                        using var transport = new OfficialArtifactTransport();
                        var backends = new ScopedBackends(context);
                        // Progress is read from the operation in Descargas, never written to the console.
                        var prepared = form.Intent.Executable is not null
                            ? await backends.PrepareCombinationAsync(form.Intent, HardwareProbe.Inspect(), transport, null, false, output.WriteLine, stop.Token)
                            : await backends.DownloadAsync(form.Intent, HardwareProbe.Inspect(), transport, warning: output.WriteLine,
                                cancellation: stop.Token, progress: _ => { });
                        backends.Protect(prepared.InstallationId, true);
                        output.WriteLine("Backend preparado en " + place.Scope.Name + "; no se asignó a ningún servicio.");
                        return output.ToString().Trim();
                    });
                    return;
                }
                if (action == "up")
                {
                    var chosen = SelectedCombination(place);
                    if (chosen is null) return;
                    if (chosen.Selection.Files.Any(file => !File.Exists(file)))
                    { OpenEditor(chosen.Selection, place.Scope.Name, chosen.Private, combinationNewFiles.GetValueOrDefault(context.Root)); return; }
                    BeginPublication("up", chosen.Selection, place.Scope.Name, chosen.Private);
                    return;
                }
                var selected = managedSelection.GetValueOrDefault(context.Root);
                if (action == "live" && selected == "recipe" && place.Recipe is { } localRecipe)
                {
                    var targetIndex = targets.FindIndex(target => SameScope(target.Store.Context, context) && target.Recipe is not null);
                    if (targetIndex < 0) { targets.Add(new(new ScopeStore(context), null, false, localRecipe, place.Scope.Name, true)); targetIndex = targets.Count - 1; }
                    index = targetIndex; preferredEntry = null; selectionEpoch++; Select(); Open(Live); return;
                }
                var registry = new RecipeRegistry(new ScopeStore(context));
                var service = registry.Services().SingleOrDefault(service => service.ServiceId == selected)
                    ?? throw new StorageException("service-conflict", "El servicio seleccionado ya no existe; volvé a seleccionarlo.");
                if (action == "live")
                {
                    var targetIndex = targets.FindIndex(target => target.Service?.ServiceId == service.ServiceId && SameScope(target.Store.Context, context));
                    if (targetIndex < 0)
                    {
                        var selection = ServiceSelection(place, service);
                        targets.Add(new(new ScopeStore(context), service, false, selection, place.Scope.Name, true));
                        targetIndex = targets.Count - 1;
                    }
                    index = targetIndex; preferredEntry = null; selectionEpoch++; Select(); Open(Live); return;
                }
                if (action == "default" && context.IsGlobal)
                {
                    registry.SetDefaultService(service.ServiceId);
                    RefreshTargets(); definitionPage = null; Render(); return;
                }
                if (action == "remove" && Query(app, "Eliminar servicio", "Eliminar «" + service.Name + "». Se conservan archivos y logs; se comprobarán las protecciones de uso.", "Cancelar", "Eliminar") == 1)
                    ManagementJob("service:" + context.Root, () =>
                    {
                        new ScopedServiceProcess(new ScopeStore(context)).Remove(service.ServiceId);
                        return Task.FromResult("Servicio «" + service.Name + "» retirado; archivos y logs conservados.");
                    });
            }
            catch (Exception error) when (ManagementError(error)) { Query(app, "No se completó la operación", error.Message, "Aceptar"); }
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
            publicationExit = null;
            operationLabel = target.Label;
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

        RecipeCombination[] AvailableCombinations(DefinitionsSource.Place place)
        {
            var items = RecipeCombinations(place).ToList();
            if (place.Context is { } context && sessionCombinations.TryGetValue(context.Root, out var pending))
            {
                var existing = items.FindIndex(item => CombinationIdentity.FromSelection(item.Selection).Id == pending.Key);
                if (existing < 0) items.Insert(0, pending);
                else items[existing] = items[existing] with { Selection = pending.Selection, Private = pending.Private };
            }
            return items.ToArray();
        }

        RecipeSelection[] AuthorSelections(DefinitionsSource.Place place) => authorSelections.GetValueOrDefault(place.Context!.Root)
            ?? (place.Scope.Global ? UpdateCli.GlobalSelections(place.Context) : SelectedCombination(place) is { } selected ? [selected.Selection] : []);

        bool AuthorPrivate(DefinitionsSource.Place place, RecipeSelection selection) => selection.ImportedScope is not null ||
            AvailableCombinations(place).Any(item => item.Private && CombinationIdentity.FromSelection(item.Selection).Id == CombinationIdentity.FromSelection(selection).Id) ||
            CombinationIdentity.FromSelection(selection).Private;

        RecipeSelection[] DefinitionSelections(DefinitionsSource.Place place)
        {
            var selections = AuthorSelections(place);
            if (selections.Length > 0 || !place.Scope.Global) return selections;
            var path = place.Context!.PathFor("recipe.yaml");
            if (File.Exists(path)) throw new StorageException("unregistered-global-intention", "Existe un YAML global sin intención registrada; se conserva sin adoptarlo ni sobrescribirlo.");
            return [new RecipeSelection(place.Context.Base, [path], place.Context.Base)
                { CapturedFiles = new Dictionary<string, byte[]> { [path] = [] } }];
        }

        void FocusOrigin(RecipeOrigin origin)
        {
            var at = recipeEditor.Files.FindIndex(file => file.Path.Equals(origin.File, StringComparison.OrdinalIgnoreCase));
            if (at < 0) return;
            recipeEditor.Show(at);
            var document = recipeEditor.Files[at].View.Document!;
            recipeEditor.Files[at].View.CaretOffset = document.GetLineByNumber(Math.Clamp(origin.Line, 1, document.LineCount)).Offset;
        }

        void StageEdit(RecipeSelection selection, string scope, string file, string[] path, string value, bool strict = false, ScopeContext? importedContext = null)
        {
            var newFiles = importedContext is not null ? selection.Files.Where(path => !File.Exists(path) && selection.CapturedFiles?.ContainsKey(path) == true).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
            OpenEditor(selection, scope, AuthorPrivate(places[focusedPlace], selection), newFiles);
            if (page != Editing) return;
            recipeEditor.ImportedContext = importedContext ?? selection.ImportedScope;
            var inputs = recipeEditor.Drafts().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var candidate = RecipeEdit.Propose(selection, inputs, file, path, value);
            recipeEditor.RequireValidSave = strict;
            recipeEditor.Stage(file, candidate);
            recipeEditor.Status = "borrador; Guardar no aplica a ejecución";
            Render();
        }

        RecipeCombination? SelectedCombination(DefinitionsSource.Place place)
        {
            var items = AvailableCombinations(place);
            return items.FirstOrDefault(item => item.Key == managedSelection.GetValueOrDefault(place.Context!.Root + "Recetas")) ?? items.FirstOrDefault();
        }

        void OpenEditor(RecipeSelection selection, string scope, bool isPrivate, IReadOnlySet<string>? newFiles = null)
        {
            try { recipeEditor.Open(selection, scope, isPrivate, newFiles); }
            catch (Exception error) when (ManagementError(error) || error is RecipeException)
            { recipeEditor.Close(); Query(app, "Editor", "No se pudo abrir la combinación: " + error.Message, "Aceptar"); return; }
            Open(Editing);
        }

        // Cerrar, Esc or another tab: the drafts are saved, discarded or kept open.
        bool ReleaseEditor()
        {
            var modified = recipeEditor.Files.Where(file => file.Modified).ToArray();
            if (modified.Length > 0)
            {
                var choice = Query(app, "Cambios sin guardar", string.Join(", ", modified.Select(file => file.Name)) + " tiene cambios sin guardar.",
                    "Guardar", "Descartar", "Cancelar");
                if (choice is not (0 or 1)) return false;
                if (choice == 0 && !SaveDrafts()) return false;
            }
            recipeEditor.Close();
            definitionPage = null;
            return true;
        }

        void CloseEditor() => Open(Definitions);

        // Saves every modified file. A file changed or removed on disk asks first (Y).
        bool SaveDrafts()
        {
            var modified = recipeEditor.Files.Where(file => file.Modified).ToArray();
            if (modified.Length == 0) { recipeEditor.Status = "sin cambios"; return true; }
            if (recipeEditor.ImportedContext is { } importedScope)
            {
                var opened = recipeEditor.Files.ToDictionary(file => file.Path, file => file.Opened, StringComparer.OrdinalIgnoreCase);
                var drafts = modified.ToDictionary(file => file.Path, file => RecipeDrafts.Encode(file.View.Text, file.Opened), StringComparer.OrdinalIgnoreCase);
                var overwrite = false;
                foreach (var file in recipeEditor.Files)
                    if (file.Existed && !File.Exists(file.Path) || File.Exists(file.Path) && !RecipeYaml.ReadBytes(file.Path).AsSpan().SequenceEqual(file.Opened))
                    {
                        var choice = Query(app, "Cambios externos · " + file.Name, "La intención global cambió. Sí sobrescribe con tu borrador; No recarga; Cancelar conserva la edición.", "Sí", "No", "Cancelar");
                        if (choice == 1) { OpenEditor(recipeEditor.Selection!, recipeEditor.Scope, recipeEditor.Private); return false; }
                        if (choice != 0) return false;
                        overwrite = true;
                    }
                ImportedRecipeEdits.Save(importedScope, opened, drafts, overwrite);
                foreach (var file in modified) { RecipeEditorPage.Load(file, drafts[file.Path]); file.Existed = true; }
                recipeEditor.Status = "intención global guardada; ejecución sin cambios";
                definitionPage = null;
                Notify("✓", "ok", recipeEditor.Status, -1);
                return true;
            }
            if (recipeEditor.Validate() is { } problem)
            {
                if (recipeEditor.RequireValidSave) { Query(app, "Cambios inválidos; se conserva lo guardado", problem.Message, "Aceptar"); return false; }
                if (Query(app, "La combinación no es válida", problem.Message + "\nUp no la publicará hasta corregirla. ¿Guardar igualmente?", "Cancelar", "Guardar igualmente") != 1) return false;
            }
            foreach (var file in modified)
            {
                var draft = RecipeDrafts.Encode(file.View.Text, file.Opened);
                var result = RecipeDrafts.Save(file.Path, file.Opened, draft, !file.Existed && !File.Exists(file.Path));
                if (result is RecipeDrafts.SaveResult.Changed or RecipeDrafts.SaveResult.Missing)
                {
                    var missing = result == RecipeDrafts.SaveResult.Missing;
                    var choice = Query(app, file.Name + (missing ? " ya no está en disco" : " cambió en disco"),
                        (missing ? "Se borró o se renombró después de abrirlo en el editor.\n¿Crearlo de nuevo con tu versión?"
                            : "Se modificó después de abrirlo en el editor (" + File.GetLastWriteTime(file.Path).ToString("HH:mm", CultureInfo.InvariantCulture) + ").\n¿Sobrescribir con tu versión?") +
                        "\n\nSí        guarda tu versión sobre la del disco\n" +
                        (missing ? "No       cierra el editor sin guardar\n" : "No        recarga el disco y descarta tu borrador\n") +
                        "Cancelar  vuelve al editor sin guardar", "Sí", "No", "Cancelar");
                    if (choice == 1)
                    {
                        if (missing) { recipeEditor.Close(); Open(Definitions); return false; }
                        RecipeEditorPage.Load(file, RecipeYaml.ReadBytes(file.Path));
                        recipeEditor.Status = file.Name + " recargado";
                        continue;
                    }
                    if (choice != 0) return false;
                    result = RecipeDrafts.Save(file.Path, file.Opened, draft, true);
                }
                file.Opened = draft;
                file.Existed = true;
                file.OpenedLines = file.View.Text.Split('\n');
                file.Changed = null;
                file.View.Document!.UndoStack.MarkAsOriginalFile();
                file.View.SetNeedsDraw();
                Notify("✓", "ok", file.Name + " guardado en " + recipeEditor.Scope + ".", -1);
            }
            recipeEditor.Status = "guardado " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
            return true;
        }

        void EditorAction(string action)
        {
            if (page != Editing || recipeEditor.File is not { } file) return;
            try
            {
                switch (action)
                {
                    case "save": SaveDrafts(); break;
                    case "validate":
                        recipeEditor.Validate();
                        if (recipeEditor.Problem is { Line: { } line } problem &&
                            recipeEditor.Files.FindIndex(item => item.Path.Equals(problem.File, StringComparison.OrdinalIgnoreCase)) is var at and >= 0)
                        {
                            recipeEditor.Show(at);
                            var document = recipeEditor.Files[at].View.Document!;
                            recipeEditor.Files[at].View.CaretOffset = document.GetLineByNumber(Math.Min(line, document.LineCount)).Offset;
                        }
                        break;
                    case "up" or "down":
                        if (recipeEditor.ImportedContext is not null) break;
                        // Up and Down use the files on disk: drafts are saved first or the action is cancelled.
                        if (recipeEditor.Files.Any(item => item.Modified))
                        {
                            if (Query(app, (action == "up" ? "Up" : "Down") + " · " + recipeEditor.Scope, "Hay cambios sin guardar; " +
                                (action == "up" ? "Up" : "Down") + " usa los archivos guardados.", "Cancelar", "Guardar y continuar") != 1 || !SaveDrafts()) break;
                        }
                        if (action == "up" && recipeEditor.Validate() is not null) break;
                        BeginPublication(action, recipeEditor.Selection, recipeEditor.Scope, recipeEditor.Private);
                        recipeEditor.Status = action + "…";
                        break;
                    case "copy": file.View.InvokeCommand(Command.Copy); break;
                    case "cut": file.View.InvokeCommand(Command.Cut); break;
                    case "paste": file.View.InvokeCommand(Command.Paste); break;
                    case "close": CloseEditor(); return;
                }
            }
            catch (Exception error) when (ManagementError(error) || error is RecipeException)
            {
                Query(app, "Editor", error.Message, "Aceptar");
            }
            if (page == Editing) file.View.SetFocus();
            Render();
        }

        void BeginPublication(string action, RecipeSelection? requested = null, string? requestedLabel = null, bool requestedPrivate = false)
        {
            if (operation is not null) return;
            var selection = requested ?? targets.ElementAtOrDefault(index)?.Recipe;
            if (selection is null) return;
            var label = requestedLabel ?? targets[index].Label;
            var usePrivateResources = requested is not null ? requestedPrivate : privatePreparation.Value == CheckState.Checked;
            upButton.Enabled = downButton.Enabled = false;
            serviceNote = $"{action}…";
            publicationExit = null;
            operationLabel = label;
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
                foreach (var file in selection.EnvFiles) args.AddRange(["--env-file", file]);
                var result = PublicationCli.Execute(args.ToArray(), output, error,
                    new DashboardPublicationInteraction(app, label, stop.Token), frontend);
                publicationExit = result;
                return $"{action} · {label} · código {result}\n{output}\n{error}".Trim();
            });
        }
    }

    // Modal message: title, wrapped text and actions on the right. Returns the chosen action index.
    private static int Query(IApplication app, string title, string message, params string[] actions)
    {
        var width = Math.Min(70, app.Screen.Width - 6);
        var wrapped = DashboardPresentation.Wrap(message, width - 4);
        // Title, blank row, message and the button row with its margin: the message keeps all its rows
        // (the previous height left the button row over the last message row).
        var height = Math.Min(app.Screen.Height - 4, wrapped.Count + 6);
        using var dialog = new Dialog { Width = width, Height = height, BorderStyle = LineStyle.None, ShadowStyle = ShadowStyles.None };
        var heading = new Label { X = 1, Text = title, Width = Dim.Fill(1) };
        var color = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        var content = new Canvas(color) { X = 1, Y = 2, Width = Dim.Fill(1), Height = Math.Max(1, height - 5),
            ZoneName = "dlg", Interactive = true, Scrollable = true, Rows = wrapped.Select(Markup.Escape).ToArray() };
        dialog.Add(heading, content);
        if (color) { Paint(dialog, Palette.Text, Palette.Card); Paint(heading, Palette.Text, Palette.Card); }
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
        public bool Confirm(string diagnostic) => Ask("Publicar en destino · " + project, diagnostic, "Publicar igual");
        public bool Confirm(string diagnostic, string destination) => Ask("Publicar en " + destination, project + "\n" + diagnostic, "Publicar igual");
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
        diagnostic = "No hay servicios en Global ni en esta carpeta.";
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
