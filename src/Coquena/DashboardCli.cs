using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
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
        public string Label => (Store.Context.IsGlobal ? "◎ " : "◇ ") + (IsDefault ? "★ " : "") + (ProjectName ?? Service?.Name);
    }

    private sealed record ServicePanel(ServiceSnapshot Snapshot);

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

    // Zones: tabs (row 0), navigation (left), page content (right), service log (bottom of En vivo)
    // and the hardware bar (last two rows). Each zone is positioned by Arrange(); visibility never
    // leaves gaps because rows are assigned explicitly.
    internal static int RunDashboard(ISystemMetrics? metrics = null, HardwareProbe.AdapterEvidence? adapters = null)
    {
        const int NavigationWidth = 24;
        const int Live = 0, Definitions = 1, Settings = 2, Hardware = 3;
        var targets = Discover(out var diagnostic);
        var preferences = new ConfigurationManager(Home.Resolve());
        var hardware = metrics ?? SystemMetrics.Create();
        using var stop = new CancellationTokenSource();
        using var app = Application.Create().Init();
        var useColor = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        Palette.Ansi16 = app.Driver?.Force16Colors == true;
        using var window = new Window { BorderStyle = LineStyle.None };

        // Tabs.
        var tabs = new View { Width = Dim.Fill(), Height = 1 };
        var tabButtons = new List<Button>();
        foreach (var caption in new[] { " ● En vivo ", " ◇ Definiciones ", " ≡ Configuración " })
        {
            var button = new Button { Text = caption, X = tabButtons.Count == 0 ? 0 : Pos.Right(tabButtons[^1]),
                NoDecorations = true, ShadowStyle = ShadowStyles.None };
            tabButtons.Add(button);
            tabs.Add(button);
        }
        var indicators = new Label { X = Pos.AnchorEnd(10), Width = 10, Text = "⇣ —  ▲ — " };
        tabs.Add(indicators);

        // Navigation.
        var navigation = new View { Y = 1, Width = NavigationWidth };
        var navigationList = new ListView { Width = Dim.Fill(), Height = Dim.Fill() };
        navigation.Add(navigationList);

        // En vivo: service row, recipe row, entry detail.
        var live = new View { X = NavigationWidth, Y = 1, Width = Dim.Fill() };
        var serviceName = new Label { Height = 1 };
        var serviceState = new Label { X = Pos.Right(serviceName) + 1, Height = 1 };
        var serviceProcess = new Label { X = Pos.Right(serviceState) + 1, Width = Dim.Fill(), Height = 1 };
        var serviceRow = new View { Width = Dim.Fill(), Height = 1 };
        serviceRow.Add(serviceName, serviceState, serviceProcess);
        var controls = new View { Width = Dim.Fill(), Height = 1 };
        var startButton = Action(" Iniciar ");
        var stopButton = Action(" Detener ");
        stopButton.X = Pos.Right(startButton) + 1;
        var controlNote = new Label { X = Pos.Right(stopButton) + 2, Width = Dim.Fill(), Height = 1 };
        controls.Add(startButton, stopButton, controlNote);
        var recipeRow = new View { Width = Dim.Fill(), Height = 1 };
        var upButton = Action(" Up ");
        var downButton = Action(" Down ");
        downButton.X = Pos.Right(upButton) + 1;
        var privatePreparation = new CheckBox { X = Pos.Right(downButton) + 2, Text = "Preparación privada" };
        var recipeTarget = new Label { X = Pos.Right(privatePreparation) + 2, Width = Dim.Fill(), Height = 1 };
        recipeRow.Add(upButton, downButton, privatePreparation, recipeTarget);
        var entryHeader = new Label { Width = Dim.Fill(), Height = 1 };
        var promptLine = new RateLine("prompt", useColor, Palette.Information);
        var outputLine = new RateLine("gen", useColor, Palette.Gold);
        var entryFacts = new Label { Width = Dim.Fill(), Height = 1 };
        var entryMeans = new Label { Width = Dim.Fill(), Height = 1 };
        var signal = new Label { Width = Dim.Fill(), Height = 1 };
        var tasksTitle = new Label { Width = Dim.Fill(), Height = 1, Text = " Tareas" };
        var taskRows = new ListView { Width = Dim.Fill() };
        live.Add(serviceRow, controls, recipeRow, entryHeader, promptLine, outputLine, entryFacts, entryMeans, signal, tasksTitle, taskRows);

        // Other pages share one content zone.
        var pageView = new View { X = NavigationWidth, Y = 1, Width = Dim.Fill() };
        var pageTitle = new Label { Width = Dim.Fill(), Height = 1 };
        var pageList = new ListView { Y = 1, Width = Dim.Fill(), Height = Dim.Fill(),
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar | ViewportSettingsFlags.HasHorizontalScrollBar };
        var filterLogs = new CheckBox
        {
            X = 1, Y = 2, Text = "Filtrar logging de monitoreo",
            Value = preferences.FilterMonitoringLogs() ? CheckState.Checked : CheckState.UnChecked
        };
        var preferenceError = new Label { X = 1, Y = 3, Width = Dim.Fill() };
        var hardwareCharts = new View { Y = 1, Width = Dim.Fill(), Height = 0 };
        pageView.Add(pageTitle, hardwareCharts, pageList, filterLogs, preferenceError);

        // Service log.
        var log = new View { Width = Dim.Fill() };
        var logHeader = new View { Width = Dim.Fill(), Height = 1 };
        var logHistory = Action(" Histórico ");
        logHistory.X = Pos.AnchorEnd(11);
        var logFollow = Action(" ⤓ Seguir ");
        logFollow.X = Pos.AnchorEnd(22);
        var logPause = Action(" ❚❚ Pausar ");
        logPause.X = Pos.AnchorEnd(34);
        var logTitle = new Label { Width = Dim.Fill(35), Height = 1 };
        logHeader.Add(logTitle, logPause, logFollow, logHistory);
        var lines = new ListView { Y = 1, Width = Dim.Fill(), Height = Dim.Fill() };
        log.Add(logHeader, lines);

        // Hardware bar: two rows, always visible; click or Enter opens the hardware detail.
        var bar = new View { Y = Pos.AnchorEnd(2), Width = Dim.Fill(), Height = 2, CanFocus = true };
        var cpuMeter = new Meter("CPU ", useColor, 6, 10);
        var ramMeter = new Meter("RAM ", useColor, 14, 10) { X = Pos.Right(cpuMeter) + 3 };
        var gpuNone = new Label { Y = 1, Width = Dim.Fill(), Text = "GPU · esperando lecturas" };
        bar.Add(cpuMeter, ramMeter, gpuNone);

        window.Add(tabs, navigation, live, pageView, log, bar);

        if (useColor)
        {
            Paint(window, Palette.Text, Palette.Base);
            Paint(tabs, Palette.Muted, Palette.Surface);
            Paint(indicators, Palette.Muted, Palette.Surface);
            Paint(navigation, Palette.Text, Palette.Surface);
            Paint(navigationList, Palette.Text, Palette.Surface);
            Paint(live, Palette.Text, Palette.Base);
            Paint(serviceRow, Palette.Text, Palette.Card);
            Paint(serviceName, Palette.Text, Palette.Card);
            Paint(serviceProcess, Palette.Muted, Palette.Card);
            Paint(controls, Palette.Muted, Palette.Base);
            Paint(controlNote, Palette.Muted, Palette.Base);
            Paint(recipeRow, Palette.Muted, Palette.Base);
            Paint(privatePreparation, Palette.Text, Palette.Base);
            Paint(recipeTarget, Palette.Muted, Palette.Base);
            Paint(entryHeader, Palette.Text, Palette.Base);
            Paint(entryFacts, Palette.Muted, Palette.Base);
            Paint(entryMeans, Palette.Muted, Palette.Base);
            Paint(signal, Palette.Muted, Palette.Base);
            Paint(tasksTitle, Palette.Muted, Palette.Card);
            Paint(taskRows, Palette.Text, Palette.Base);
            Paint(pageView, Palette.Text, Palette.Base);
            Paint(pageTitle, Palette.Text, Palette.Card);
            Paint(pageList, Palette.Text, Palette.Base);
            Paint(hardwareCharts, Palette.Muted, Palette.Surface);
            Paint(filterLogs, Palette.Text, Palette.Base);
            Paint(preferenceError, Palette.Danger, Palette.Base);
            Paint(log, Palette.Text, Palette.Surface);
            Paint(logHeader, Palette.Text, Palette.Card);
            Paint(logTitle, Palette.Text, Palette.Card);
            Paint(lines, Palette.Text, Palette.Surface);
            Paint(bar, Palette.Muted, Palette.Surface);
            Paint(gpuNone, Palette.Muted, Palette.Surface);
        }

        // State.
        var page = Live;
        var settingsCategory = 0;
        var hardwareSelection = 0;
        var index = Math.Max(0, targets.FindIndex(target => target.Recipe is not null));
        var catalogues = new Dictionary<int, IReadOnlyList<EntrySnapshot>>();
        IReadOnlyList<DashboardPresentation.Node> navigationNodes = [];
        var updatingNavigation = false;
        var logSessions = new Dictionary<int, ServiceLogSession>();
        var retiredLogs = new List<Task>();
        var projectClock = Stopwatch.StartNew();
        var layoutSize = (-1, -1);
        bool logHeld = false, paintingLog = false;
        string? lastLogText = null, shownTasks = null, shownPage = null, shownNavigation = null;
        var selectionEpoch = 0;
        var probe = adapters is null ? Task.Run(HardwareProbe.InspectAdapters) : Task.FromResult(adapters);
        HardwareProbe.AdapterEvidence? evidence = null;
        HardwareSnapshot? lastHardware = null;
        TimeSpan? hardwareCadence = null;
        string? hardwareProblem = null;
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
        var refreshingTasks = false;
        var showRates = false;
        // Hardware series: one curve per metric, drawn in the bar and in the detail.
        var devices = new Dictionary<string, (int Number, Meter Bar, Meter Use, Meter Dedicated, Meter Shared)>(StringComparer.OrdinalIgnoreCase);
        var cpuDetail = new Meter("cpu ", useColor, 22);
        var ramDetail = new Meter("ram ", useColor, 22) { Y = 1 };
        hardwareCharts.Add(cpuDetail, ramDetail);

        // Events.
        for (var i = 0; i < tabButtons.Count; i++)
        {
            var selection = i;
            tabButtons[i].Accepted += (_, _) => Open(selection);
        }
        bar.MouseEvent += (_, mouse) =>
        {
            if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked)) return;
            mouse.Handled = true;
            Open(Hardware);
        };
        bar.KeyDown += (_, key) => { if (key == Key.Enter) { key.Handled = true; Open(Hardware); } };
        navigationList.ValueChanged += (_, _) =>
        {
            if (updatingNavigation || navigationList.SelectedItem is not int row) return;
            switch (page)
            {
                case Settings: settingsCategory = row; ShowPage(); return;
                case Hardware: hardwareSelection = row; ShowHardwareDetail(); return;
            }
            if (row < 0 || row >= navigationNodes.Count) return;
            var node = navigationNodes[row];
            preferredEntry = node.Entry;
            preferredTask = null;
            if (index != node.Target) { index = node.Target; selectionEpoch++; Select(); }
            else if (selectedSnapshot is not null) ShowInference(selectedSnapshot.Entries);
            ShowPage();
        };
        navigationList.KeyDown += (_, key) =>
        {
            if (key != Key.Enter || page != Live || navigationList.SelectedItem is not int row || row >= navigationNodes.Count) return;
            if (navigationNodes[row].Entry is not { } name || selectedSnapshot?.Entries.FirstOrDefault(entry => entry.Name == name) is not { } entry) return;
            key.Handled = true;
            ShowDetail("Entrada · " + entry.Name, EntryDetail(entry));
        };
        taskRows.ValueChanged += (_, _) =>
        {
            if (refreshingTasks || taskRows.SelectedItem is not int row || row < 0 || row >= visibleTasks.Count) return;
            preferredTask = visibleTasks[row].Identity;
            if (selectedSnapshot is not null) ShowInference(selectedSnapshot.Entries);
        };
        taskRows.KeyDown += (_, key) =>
        {
            if (key != Key.Enter || taskRows.SelectedItem is not int row || row < 0 || row >= visibleTasks.Count) return;
            key.Handled = true;
            ShowDetail("Tarea · último valor observado", InferencePresentation.Detail(visibleTasks[row]));
        };
        startButton.Accepted += (_, _) => BeginServiceAction(stopService: false);
        stopButton.Accepted += (_, _) => BeginServiceAction(stopService: true);
        upButton.Accepted += (_, _) => BeginPublication("up");
        downButton.Accepted += (_, _) => BeginPublication("down");
        logPause.Accepted += (_, _) => { logHeld = !logHeld; if (!logHeld) lastLogText = null; ShowLogHeader(); };
        logFollow.Accepted += (_, _) =>
        {
            if (logSessions.TryGetValue(index, out var session)) session.Current();
            logHeld = false; lastLogText = null; ShowLogHeader();
        };
        lines.ViewportChanged += (_, _) =>
        {
            if (!paintingLog && lines.HasFocus && lines.Viewport.Y < lines.GetContentSize().Height - lines.Viewport.Height)
            { logHeld = true; ShowLogHeader(); }
        };
        logHistory.Accepted += (_, action) =>
        {
            action.Handled = true;
            if (!logSessions.TryGetValue(index, out var session)) return;
            var files = session.Files.Reverse().ToArray();
            var choice = Choose(app, "Log histórico · " + targets[index].Label, files.Length == 0
                ? ["Sin archivos registrados disponibles."]
                : files.Select(file => $"{file.StartedAt} · {file.Id} · {Path.GetFileName(file.Path)}{(file.Active ? " · activo" : "")}").ToArray(),
                files.Length > 0 ? "Abrir" : null);
            if (choice is int row && row < files.Length)
            { session.SelectHistory(files[row].Id); logHeld = false; lastLogText = null; ShowLogHeader(); }
        };
        filterLogs.ValueChanging += (_, change) =>
        {
            try
            {
                preferences.SetFilterMonitoringLogs(change.NewValue == CheckState.Checked);
                preferenceError.Text = "";
                lastLogText = null;
            }
            catch (Exception e) when (ServiceObservation.ObservationError(e))
            {
                change.Handled = true;
                preferenceError.Text = "No se pudo guardar la preferencia: " + e.Message;
            }
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
            page = selected;
            shownPage = null;
            RebuildNavigation();
            Arrange();
            ShowPage();
        }

        void Tick()
        {
            if (operation is { IsCompleted: true } completed)
            {
                operation = null;
                selectedSnapshot = null;
                RefreshProject();
                Select();
                var problem = completed.IsCompletedSuccessfully ? completed.Result : completed.Exception?.GetBaseException().Message;
                if (!string.IsNullOrWhiteSpace(problem)) Query(app, "Resultado de la operación", problem, "Aceptar");
            }
            if (operation is null && projectClock.Elapsed >= CaptureInterval)
            {
                projectClock.Restart();
                if (RefreshProject()) Select();
                retiredLogs.RemoveAll(task => task.IsCompletedSuccessfully);
            }
            if (evidence is null && probe.IsCompletedSuccessfully) evidence = probe.Result;
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
                    startButton.Enabled = stopButton.Enabled = false;
                    ShowState("Observación no disponible · " + pending.Task.Exception!.GetBaseException().Message, Palette.Warning);
                    inference.Suspend();
                    ShowInference([]);
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
            if (page != Live) ShowPage();
            ReadLog();
        }

        // Explicit rows per zone; the minimum is 80×24 and extra height goes to tasks and log.
        void Arrange()
        {
            layoutSize = (window.Viewport.Width, window.Viewport.Height);
            var height = window.Viewport.Height;
            var content = Math.Max(4, height - 3);
            var showLog = page == Live;
            var logHeight = showLog ? Math.Max(5, height < 30 ? 6 : content * 2 / 5) : 0;
            var top = content - logHeight;
            navigation.Height = top;
            live.Height = pageView.Height = top;
            live.Visible = page == Live;
            pageView.Visible = page != Live;
            log.Visible = showLog;
            log.Y = 1 + top;
            log.Height = logHeight;
            for (var i = 0; i < tabButtons.Count; i++)
                if (useColor) Paint(tabButtons[i], page == i ? Palette.Text : Palette.Muted, page == i ? Palette.Primary : Palette.Surface);

            var target = targets.ElementAtOrDefault(index);
            var y = 0;
            serviceRow.Y = y++;
            controls.Visible = target is not null && (target.Service is not null || target.DeclaresService);
            if (controls.Visible) controls.Y = y++;
            recipeRow.Visible = target?.Recipe is not null;
            if (recipeRow.Visible) recipeRow.Y = y++;
            entryHeader.Visible = target?.Service is not null;
            if (entryHeader.Visible) entryHeader.Y = y++;
            promptLine.Visible = outputLine.Visible = entryHeader.Visible && showRates;
            if (promptLine.Visible) { promptLine.Y = y++; outputLine.Y = y++; }
            entryFacts.Visible = entryHeader.Visible && entryFacts.Text.Length > 0;
            if (entryFacts.Visible) entryFacts.Y = y++;
            entryMeans.Visible = entryHeader.Visible && entryMeans.Text.Length > 0;
            if (entryMeans.Visible) entryMeans.Y = y++;
            signal.Visible = entryHeader.Visible && signal.Text.Length > 0;
            if (signal.Visible) signal.Y = y++;
            tasksTitle.Visible = taskRows.Visible = entryHeader.Visible;
            if (tasksTitle.Visible) { tasksTitle.Y = y++; taskRows.Y = y; taskRows.Height = Math.Max(1, top - y); }

            var detailRows = page == Hardware ? (hardwareSelection == 0 ? 2 : 3) : 0;
            hardwareCharts.Visible = page == Hardware;
            hardwareCharts.Height = detailRows;
            pageList.Y = 1 + detailRows + (page == Hardware ? 1 : 0);
            filterLogs.Visible = preferenceError.Visible = page == Settings && settingsCategory == 0;
            pageList.Visible = !(page == Settings && settingsCategory == 0);
            // Moving a view does not repaint the rows it leaves; repaint every zone.
            foreach (var zone in new View[] { navigation, live, pageView, log, bar })
            {
                zone.SetNeedsLayout();
                zone.SetNeedsDraw();
                foreach (var child in zone.SubViews) child.SetNeedsDraw();
            }
        }

        void RebuildNavigation()
        {
            updatingNavigation = true;
            string[] rows;
            if (page == Settings) rows = ["TUI", "Sistema"];
            else if (page == Hardware) rows = ["CPU / RAM", .. devices.Values.OrderBy(device => device.Number).Select(device => "GPU" + device.Number)];
            else
            {
                navigationNodes = DashboardPresentation.Tree(targets, catalogues, NavigationWidth - 1);
                rows = navigationNodes.Count == 0 ? [diagnostic] : navigationNodes.Select(node => node.Text).ToArray();
            }
            var text = string.Join('\n', rows);
            if (text != shownNavigation)
            {
                shownNavigation = text;
                navigationList.SetSource(new ObservableCollection<string>(rows));
            }
            if (page is Live or Definitions)
            {
                var row = navigationNodes.ToList().FindIndex(node => node.Target == index && node.Entry == preferredEntry);
                if (row >= 0) navigationList.SelectedItem = row;
            }
            else navigationList.SelectedItem = page == Settings ? settingsCategory : hardwareSelection;
            updatingNavigation = false;
        }

        void Select()
        {
            var target = targets.ElementAtOrDefault(index);
            if (target?.Service is { } service && !logSessions.ContainsKey(index))
                logSessions[index] = new ServiceLogSession(new ServiceLogReader(target.Store, service.ServiceId), stop.Token);
            logHeld = false; lastLogText = null;
            selectedSnapshot = null;
            inference.Suspend();
            preferredTask = null;
            inferenceEntry = null;
            promptLine.Clear(); outputLine.Clear();
            startButton.Enabled = stopButton.Enabled = false;
            startButton.Text = " Iniciar ";
            upButton.Enabled = downButton.Enabled = operation is null;
            controlNote.Text = "";
            if (target is null)
            {
                serviceName.Text = " " + diagnostic;
                ShowState("", Palette.Muted);
                serviceProcess.Text = "";
            }
            else
            {
                serviceName.Text = " " + target.Label;
                serviceProcess.Text = "";
                if (target.Recipe is not null)
                    recipeTarget.Text = target.RecipeProblem is { } problem ? "receta no disponible: " + problem : "→ " + target.Destination;
                if (target.Service is null)
                {
                    startButton.Enabled = target.DeclaresService && operation is null;
                    ShowState(target.DeclaresService ? "○ sin iniciar" : "sin servicio propio", Palette.Muted);
                }
                else ShowState("consultando…", Palette.Information);
            }
            ShowInference([]);
            SetRows(lines, ref lastLogText, target?.Service is null ? "Sin servicio que registre logs." : "Consultando últimas líneas…");
            lastLogText = null;
            ShowLogHeader();
            RebuildNavigation();
            Arrange();
        }

        void ShowState(string text, Color color)
        {
            serviceState.Text = text;
            if (useColor) Paint(serviceState, Palette.Ansi16 ? color : color, Palette.Card);
        }

        void Show(ServiceSnapshot snapshot)
        {
            snapshot = snapshot with { Entries = snapshot.Entries.OrderByDescending(entry => entry.Inference?.Active.Count > 0)
                .ThenByDescending(entry => entry.State == "loaded").ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray() };
            selectedSnapshot = snapshot;
            var running = snapshot.ProcessState == "alive" && snapshot.InstanceId is not null;
            startButton.Text = running ? " ⟳ Reiniciar " : " Iniciar ";
            startButton.Enabled = running || snapshot.ProcessState == "stopped";
            stopButton.Enabled = running;
            var port = DashboardPresentation.Port(snapshot);
            serviceName.Text = " " + targets[index].Label + (port.Length > 0 ? " " + port : "");
            var healthy = snapshot.ApiState == "available" && snapshot.Health.StartsWith("HTTP 2", StringComparison.Ordinal);
            var (glyph, color) = snapshot.ProcessState == "stopped" ? ("○", Palette.Muted)
                : snapshot.Health.StartsWith("HTTP 5", StringComparison.Ordinal) ? ("✕", Palette.Danger)
                : healthy ? ("●", Palette.Success) : ("▲", Palette.Warning);
            var state = glyph + " " + DashboardPresentation.ProcessState(snapshot.ProcessState);
            if (!healthy && snapshot.ProcessState != "stopped") state += " · API " + snapshot.ApiState + " · " + snapshot.Health;
            ShowState(state, color);
            serviceProcess.Text = DashboardPresentation.Process(snapshot, DateTimeOffset.UtcNow);
            controlNote.Text = snapshot.CompositionCurrent ? "" : "≠ receta";
            inference.Observe(snapshot);
            catalogues[index] = snapshot.Entries;
            RebuildNavigation();
            ShowInference(snapshot.Entries);
            if (page == Definitions) ShowPage();
        }

        void ShowInference(IReadOnlyList<EntrySnapshot> observed)
        {
            var target = targets.ElementAtOrDefault(index);
            visibleTasks = target?.Service is { } service ? InferencePresentation.Rows(inference,
                target.Store.Context.IsGlobal ? "global" : target.Store.Context.Base, service.ServiceId)
                .Where(task => preferredEntry is null || task.Identity.Entry == preferredEntry).ToArray() : [];
            var width = Math.Max(20, live.Viewport.Width);
            refreshingTasks = true;
            SetRows(taskRows, ref shownTasks, visibleTasks.Count == 0 ? " Sin tareas observadas" :
                string.Join('\n', visibleTasks.Select(task => " " + DashboardPresentation.Fit(InferencePresentation.Row(task, width), width - 1))));
            var selectedTask = visibleTasks.FirstOrDefault(task => task.Identity == preferredTask)
                ?? (preferredEntry is null ? null : visibleTasks.FirstOrDefault(task => task.Identity.Entry == preferredEntry));
            if (selectedTask is not null) taskRows.SelectedItem = visibleTasks.ToList().IndexOf(selectedTask);
            refreshingTasks = false;

            var entry = observed.FirstOrDefault(item => item.Name == selectedTask?.Identity.Entry)
                ?? observed.FirstOrDefault(item => item.Name == preferredEntry)
                ?? observed.FirstOrDefault(item => item.Inference?.Active.Count > 0)
                ?? observed.FirstOrDefault(item => item.Name == inferenceEntry)
                ?? observed.FirstOrDefault(item => item.Inference is not null)
                ?? observed.FirstOrDefault();
            inferenceEntry = selectedTask?.Identity.Entry ?? entry?.Name ?? inferenceEntry;
            var active = inference.Active.Where(task => task.Identity.Entry == inferenceEntry).ToArray();
            var latest = selectedSnapshot is { } current ? inference.History.LastOrDefault(task =>
                task.Identity.Scope == current.Scope && task.Identity.Service == current.ServiceId &&
                task.Identity.Entry == inferenceEntry) : null;
            var display = selectedTask ?? active.FirstOrDefault() ?? latest;
            var focused = display?.End is null ? display : null;

            entryHeader.Text = selectedSnapshot is null ? "" : " " + DashboardPresentation.Fit(DashboardPresentation.EntryHeader(entry, entry?.Inference?.Model ?? display?.Model), width - 1);
            entryFacts.Text = DashboardPresentation.EntryFacts(entry) is { } facts ? " " + DashboardPresentation.Fit(facts, width - 1) : "";
            var hadMeans = entryMeans.Text.Length > 0;
            entryMeans.Text = DashboardPresentation.EntryMeans(entry) is { } means ? " " + DashboardPresentation.Fit(means, width - 1) : "";
            var hadRates = showRates;
            showRates = display is not null;
            promptLine.Clear(); outputLine.Clear();
            if (display is not null)
            {
                foreach (var point in display.Points)
                {
                    promptLine.Add(point.At, point.Prompt, display.PromptPeak);
                    outputLine.Add(point.At, point.Output, display.OutputPeak);
                }
                promptLine.Show(DashboardPresentation.Rate(focused?.PromptRate, display.PromptMean, display.PromptPeak));
                outputLine.Show(DashboardPresentation.Rate(focused?.OutputRate, display.OutputMean, display.OutputPeak));
            }
            signal.Text = entry is null ? (selectedSnapshot is null ? "" : " Sin entradas publicadas")
                : entry.Inference is null ? " Sin señal /slots" + (entry.InferenceDiagnostic is { } reason ? " · " + DashboardPresentation.Fit(reason, width - 20) : "")
                : display is null ? " Sin tarea observada en esta entrada" : "";
            if (hadRates != showRates || hadMeans != entryMeans.Text.Length > 0 || entryFacts.Visible != entryFacts.Text.Length > 0 || signal.Visible != signal.Text.Length > 0) Arrange();
        }

        void ReadLog()
        {
            if (!logSessions.TryGetValue(index, out var session)) return;
            var frame = session.Read(filterLogs.Value == CheckState.Checked);
            ShowLogHeader(session, frame.Discarded);
            if (logHeld) return;
            var text = string.Join('\n', frame.Rows);
            if (text.Length == 0) text = "Sin líneas visibles.";
            if (text == lastLogText) return;
            paintingLog = true;
            SetRows(lines, ref lastLogText, text);
            var rows = text.Split('\n');
            lines.SetContentSize(new System.Drawing.Size(rows.Max(row => row.Length), rows.Length));
            lines.SelectedItem = session.Historical ? 0 : rows.Length - 1;
            lines.ScrollVertical((session.Historical ? 0 : Math.Max(0, rows.Length - lines.Viewport.Height)) - lines.Viewport.Y);
            paintingLog = false;
        }

        void ShowLogHeader(ServiceLogSession? session = null, long discarded = 0)
        {
            session ??= logSessions.GetValueOrDefault(index);
            var name = targets.ElementAtOrDefault(index)?.Service?.Name ?? "";
            var state = session is null ? "" : logHeld ? "❚❚ en pausa" : session.Historical ? "histórico" : "● siguiendo";
            var text = $" Log {name}  {state}" + (discarded > 0 ? $" · {discarded} descartadas" : "");
            logTitle.Text = DashboardPresentation.Fit(text, Math.Max(10, logTitle.Viewport.Width));
            logPause.Text = logHeld ? " ▸ Reanudar " : " ❚❚ Pausar ";
            logPause.Enabled = logFollow.Enabled = logHistory.Enabled = session is not null;
        }

        void ShowPage()
        {
            if (page == Live) return;
            var rows = new List<string>();
            string title;
            if (page == Definitions)
            {
                var target = targets.ElementAtOrDefault(index);
                title = " " + (target?.Label ?? diagnostic) + " · consulta";
                if (target?.Recipe is not null)
                {
                    rows.Add("Destino de publicación  " + target.Destination);
                    if (target.RecipeProblem is not null) rows.Add("Receta no disponible  " + target.RecipeProblem);
                    rows.Add("");
                }
                if (catalogues.TryGetValue(index, out var catalogue))
                {
                    rows.Add($"Catálogo observado · {catalogue.Count} entradas");
                    foreach (var entry in catalogue.Where(entry => preferredEntry is null || entry.Name == preferredEntry))
                    {
                        rows.Add($"  {DashboardPresentation.StateGlyph(entry.State)} {entry.Name} · {entry.State} · {entry.ObservedAt.ToLocalTime():HH:mm:ss}");
                        if (DashboardPresentation.EntryFacts(entry) is { } facts) rows.Add("    " + facts);
                        if (DashboardPresentation.EntryMeans(entry) is { } means) rows.Add("    " + means);
                    }
                }
                else rows.Add("Sin catálogo observado.");
            }
            else if (page == Settings)
            {
                title = settingsCategory == 0 ? " TUI" : " Sistema";
                if (settingsCategory == 1) rows.AddRange(SystemRows(lastHardware));
            }
            else
            {
                title = hardwareSelection == 0 ? " CPU / RAM · máquina" : " GPU" + (hardwareSelection - 1) + " · uso · vram · compartida";
                rows.AddRange(HardwareRows());
            }
            pageTitle.Text = title;
            var text = string.Join('\n', rows);
            if (text == shownPage) return;
            var selected = pageList.SelectedItem;
            SetRows(pageList, ref shownPage, text);
            pageList.SetContentSize(new System.Drawing.Size(Math.Max(1, rows.DefaultIfEmpty("").Max(row => row.Length)), rows.Count));
            if (selected is >= 0 && selected < rows.Count) pageList.SelectedItem = selected;
        }

        void ShowHardwareDetail()
        {
            foreach (var device in devices.Values)
                device.Use.Visible = device.Dedicated.Visible = device.Shared.Visible = hardwareSelection == device.Number + 1;
            cpuDetail.Visible = ramDetail.Visible = hardwareSelection == 0;
            Arrange();
            ShowPage();
        }

        void SampleHardware()
        {
            if (machineSample is not null && !machineSample.IsCompleted) return;
            var captured = machineSample is { IsCompletedSuccessfully: true };
            if (captured)
            {
                if (lastHardware is not null) hardwareCadence = machineSample!.Result.StartedAt - lastHardware.StartedAt;
                lastHardware = machineSample!.Result;
                hardwareProblem = null;
            }
            else if (machineSample is { IsFaulted: true }) hardwareProblem = machineSample.Exception!.GetBaseException().Message;
            machineSample = null;
            if (!machineClock.IsRunning || machineClock.Elapsed >= CaptureInterval)
                machineSample = Task.Run(() => { machineClock.Restart(); return hardware.CaptureAsync(stop.Token); });
            ShowHardware(lastHardware, captured);
        }

        void ShowHardware(HardwareSnapshot? sample, bool captured)
        {
            var now = DateTimeOffset.UtcNow;
            var samples = sample?.Samples ?? [];
            var cpu = Fresh(samples, "cpu.utilization");
            var ramUsed = Fresh(samples, "ram.used");
            var ramTotal = Fresh(samples, "ram.total");
            Feed([cpuMeter, cpuDetail], sample, samples, "cpu.utilization", cpu / 100,
                cpu is null ? "—" : Percent(cpu), cpu is null ? null : $"{Percent(cpu)}");
            var ram = Fraction(ramUsed, ramTotal);
            var ramText = ramUsed is null ? "—" : $"{DashboardPresentation.Compact(ramUsed.Value)}/{(ramTotal is null ? "—" : DashboardPresentation.Compact(ramTotal.Value))}";
            Feed([ramMeter, ramDetail], sample, samples, "ram.used", ram, ramText, ramText);

            var added = false;
            foreach (var device in sample?.Devices ?? [])
            {
                if (ComputeLabel(device.Id) is null || devices.ContainsKey(device.Id)) continue;
                var number = devices.Count;
                var barMeter = new Meter($"GPU{number} ", useColor, 22, 6) { Y = 1 };
                barMeter.X = number == 0 ? 0 : Pos.Right(devices.Values.Single(other => other.Number == number - 1).Bar) + 2;
                var use = new Meter("uso  ", useColor, 22) { Visible = false };
                var dedicated = new Meter("vram ", useColor, 22) { Y = 1, Visible = false };
                var shared = new Meter("+ram ", useColor, 22) { Y = 2, Visible = false };
                devices.Add(device.Id, (number, barMeter, use, dedicated, shared));
                bar.Add(barMeter);
                hardwareCharts.Add(use, dedicated, shared);
                added = true;
            }
            gpuNone.Visible = devices.Count == 0;
            if (devices.Count == 0) gpuNone.Text = evidence is null ? "GPU · esperando lecturas" : "GPU · sin cómputo confirmado";
            foreach (var (id, views) in devices)
            {
                var device = sample?.Devices.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Id, id));
                var deviceSamples = device?.Samples ?? [];
                var usage = Fresh(deviceSamples, "gpu.utilization");
                var used = Fresh(deviceSamples, "vram.used");
                var total = Fresh(deviceSamples, "vram.total");
                var sharedUsed = Fresh(deviceSamples, "gpu.shared");
                var memory = used is null ? "—" : $"{DashboardPresentation.Compact(used.Value)}/{(total is null ? "—" : DashboardPresentation.Compact(total.Value))}";
                var summary = $"{(usage is null ? "—" : usage.Value.ToString("0", CultureInfo.CurrentCulture) + "%")} {memory}" +
                    (sharedUsed is null ? "" : " +" + DashboardPresentation.Compact(sharedUsed.Value));
                Feed([views.Bar, views.Use], sample, deviceSamples, "gpu.utilization", usage / 100, summary,
                    usage is null ? "—" : Percent(usage));
                Feed([views.Dedicated], sample, deviceSamples, "vram.used", Fraction(used, total), memory, memory);
                Feed([views.Shared], sample, deviceSamples, "gpu.shared", sharedUsed is null || ramTotal is null ? null : sharedUsed / ramTotal,
                    "", sharedUsed is null ? "—" : DashboardPresentation.Compact(sharedUsed.Value));
            }
            if (added) { ShowHardwareDetail(); if (page == Hardware) RebuildNavigation(); }

            double? Fresh(IReadOnlyList<MetricSample> list, string name) =>
                list.FirstOrDefault(metric => metric.Name == name) is { Value: { } value } metric && metric.IsFresh(now, MetricLifetime) ? value : null;

            void Feed(Meter[] meters, HardwareSnapshot? source, IReadOnlyList<MetricSample> list, string name, double? ratio, string barText, string? detailText)
            {
                var metric = list.FirstOrDefault(item => item.Name == name);
                var color = UsageColor(ratio, Palette.Ansi16);
                for (var i = 0; i < meters.Length; i++)
                {
                    if (captured) meters[i].Graph.AddTimedSample(metric?.ObservedAt ?? source?.StartedAt ?? now, ratio);
                    meters[i].Show(i == 0 ? barText : detailText ?? barText, ratio is null ? Palette.Muted : color);
                }
            }
        }

        IEnumerable<string> HardwareRows()
        {
            var now = DateTimeOffset.UtcNow;
            var rows = new List<string>();
            if (hardwareProblem is not null) rows.Add("Captura no disponible · " + hardwareProblem);
            if (lastHardware is not { } sample) { rows.Add("Esperando la primera captura."); return rows; }
            rows.Add($"Captura {sample.StartedAt.ToLocalTime():HH:mm:ss} · {(sample.CompletedAt - sample.StartedAt).TotalMilliseconds:0} ms · " +
                (hardwareCadence is { } cadence ? $"cada {cadence.TotalSeconds:0.#} s" : "primera captura") + " · curvas 2 s/col");
            if (hardwareSelection == 0)
            {
                rows.Add(sample.Environment ?? "");
                rows.AddRange(sample.Samples.SelectMany(metric => FormatMetric(metric, now).Split('\n')));
                rows.Add("");
                rows.Add("Servicio seleccionado · instancia y procesos verificados");
                if (selectedSnapshot is { InstanceId: not null } snapshot && snapshot.Metrics.Count > 0)
                    rows.AddRange(snapshot.Metrics.SelectMany(metric => FormatMetric(metric, now).Split('\n')));
                else rows.Add("  sin muestras atribuibles");
            }
            else if (devices.FirstOrDefault(pair => pair.Value.Number == hardwareSelection - 1) is { Key: not null } selected)
            {
                var device = sample.Devices.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Id, selected.Key));
                rows.Add($"{device?.Name ?? selected.Key} · {ComputeLabel(selected.Key)}");
                if (device is null) rows.Add("Sin lectura en la última captura.");
                else rows.AddRange(device.Samples.SelectMany(metric => FormatMetric(metric, now).Split('\n')));
            }
            rows.AddRange(sample.Diagnostics);
            return rows;
        }

        IEnumerable<string> SystemRows(HardwareSnapshot? sample)
        {
            var context = evidence?.Context;
            var rows = new List<string> { "Coquena " + typeof(DashboardCli).Assembly.GetName().Version, "" };
            if (context is not null)
            {
                rows.Add($"{context.Platform} · {context.Cpu} · RAM {Memory(context.MemoryBytes)}");
                rows.Add($"CUDA · {context.Cuda.State} · {context.Cuda.Diagnostic}");
                rows.AddRange(context.CudaDevices.Select(device => $"  {device.Name} · capacidad {device.ComputeCapability}"));
                rows.Add($"Vulkan · {context.Vulkan.State} · {context.Vulkan.Diagnostic}");
                rows.Add("Adaptadores de pantalla");
                rows.AddRange(context.DisplayAdapters.Select(name => $"  {name}"));
            }
            else rows.Add(probe.IsFaulted ? "Sondeo no disponible." : "Consultando CUDA y Vulkan…");
            rows.Add("Adaptadores con telemetría");
            if (sample is not null)
            {
                foreach (var device in sample.Devices)
                {
                    var name = devices.TryGetValue(device.Id, out var views) ? $"GPU{views.Number} · {device.Name}" : device.Name;
                    rows.Add($"  {name} · {ComputeLabel(device.Id) ?? "cómputo sin confirmar"}");
                }
                rows.AddRange(sample.Diagnostics);
            }
            return rows;
        }

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
                $"{entry.Name} · {entry.State} · modelo {source?.Model ?? "n/d"}",
                $"Instancia {selectedSnapshot?.InstanceId ?? "n/d"} · hijo {source?.Pid.ToString() ?? "n/d"}/{source?.Creation.ToString() ?? "n/d"}",
                source is null ? entry.InferenceDiagnostic ?? "Tareas /slots: hijo/build/endpoint sin verificar" : "Tareas /slots: hijo y build verificados",
                entry.Diagnostic ?? "Métricas disponibles según la fuente",
                "Acumulados y medias del backend: período no informado; cada hijo abre otro período.",
                ""
            };
            rows.AddRange(entry.Metrics.SelectMany(metric => FormatMetric(metric, DateTimeOffset.UtcNow).Split('\n')));
            return string.Join('\n', rows);
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
            var value = metric.Value is null ? $"n/d · {metric.UnavailableReason ?? "lectura sin valor"}" :
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

            startButton.Enabled = stopButton.Enabled = upButton.Enabled = downButton.Enabled = false;
            ShowState($"{verb.ToLowerInvariant()}…", Palette.Information);
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
            startButton.Enabled = stopButton.Enabled = upButton.Enabled = downButton.Enabled = false;
            ShowState($"{action}…", Palette.Information);
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
