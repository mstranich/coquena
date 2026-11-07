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

internal static class DashboardCli
{
    private static readonly TimeSpan CaptureInterval = ObservationTimeline.Interval;
    private static readonly TimeSpan MetricLifetime = TimeSpan.FromSeconds(4);

    // Coquena web/showcase tokens. Keep semantic roles distinct even when they share a color.
    private static class Palette
    {
        public static readonly Color Base = new("#14171A");
        public static readonly Color Surface = new("#1E2328");
        public static readonly Color Card = new("#252B32");
        public static readonly Color Border = new("#3E4651");
        public static readonly Color Text = new("#EAEBED");
        public static readonly Color Muted = new("#88929A");
        public static readonly Color Primary = new("#C85A32");
        public static readonly Color Accent = new("#2EC4B6");
        public static readonly Color Gold = new("#DDA15E");
        public static readonly Color Success = Accent;
        public static readonly Color Warning = new("#E09F3E");
        public static readonly Color Danger = new("#E63946");
        public static readonly Color Information = new("#457B9D");
    }

    private sealed class UsageChart : View
    {
        private readonly Label value;
        private readonly Label totalLabel;
        private readonly UsageGraph graph;
        private readonly bool useColor;

        public UsageChart(string caption, bool useColor)
        {
            this.useColor = useColor;
            Width = Dim.Fill();
            Height = 2;
            var name = new Label { Text = caption };
            value = new Label { X = Pos.Right(name) + 2, Text = "—" };
            totalLabel = new Label { X = Pos.Right(value), Width = Dim.Fill() };
            graph = new UsageGraph(useColor) { Y = 1, Width = Dim.Fill(), Height = 1 };
            Add(name, value, totalLabel, graph);
            if (useColor) Paint(this, Palette.Muted, Palette.Surface);
        }

        public void ShowResource(string display, double? ratio, string suffix,
            DateTimeOffset? observedAt, double? observedRatio)
        {
            value.Text = display;
            totalLabel.Text = suffix;
            if (useColor) Paint(value, UsageColor(ratio, App?.Driver?.Force16Colors == true), Palette.Surface);
            if (observedAt is { } date) graph.AddTimedSample(date, observedRatio);
            graph.SetNeedsDraw();
        }

        public void Clear() => graph.Clear();

        public void AddRate(double? rate, double? mean, double? peak, double seconds, DateTimeOffset? observedAt)
        {
            var maximum = 1d;
            if (peak is > 1)
            {
                var step = Math.Max(1, Math.Pow(10, Math.Floor(Math.Log10(peak.Value)) - 1));
                maximum = Math.Ceiling(peak.Value / step) * step;
            }
            value.Text = Rate(rate);
            totalLabel.Text = $" tk/s · media {Rate(mean)} · pico {Rate(peak)} · {seconds:0.0}s · escala 0–{maximum:0}";
            if (useColor) Paint(value, rate is null ? Palette.Muted : Palette.Accent, Palette.Surface);
            if (observedAt is { } date) graph.AddTimedSample(date, rate, maximum);
            graph.SetNeedsDraw();

            static string Rate(double? number) => number?.ToString("0.#", CultureInfo.CurrentCulture) ?? "—";
        }
    }

    private sealed class UsageGraph(bool useColor, string? symbols = null) : View
    {
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
                        : sample is null ? Palette.Muted : Palette.Accent;
                    SetAttribute(new Terminal.Gui.Drawing.Attribute(color, Palette.Surface));
                }
                Move(column++, 0);
                AddRune(sample is null ? '·' : symbols is not null ? symbols[(int)sample.Value] :
                    levels[Math.Min((int)(ratio * levels.Length), levels.Length - 1)]);
            }
            return true;
        }
    }

    private sealed record Target(ScopeStore Store, ServiceIdentity Service, bool IsDefault)
    {
        public string Label => (Store.Context.IsGlobal ? "◎ " : "◇ ") + (IsDefault ? "★ " : "") + Service.Name;
    }

    private sealed record ServicePanel(ServiceSnapshot Snapshot, string Logs);

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

    internal static int RunDashboard(ISystemMetrics? metrics = null, HardwareProbe.AdapterEvidence? adapters = null)
    {
        var targets = Discover(out var diagnostic);
        var hardware = metrics ?? SystemMetrics.Create();
        using var stop = new CancellationTokenSource();
        using var app = Application.Create().Init();
        var useColor = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        using var window = new Window { Title = "COQUENA · Servicios", BorderStyle = LineStyle.None };
        const int memoryStatsWidth = 9;
        const int hostStatsWidth = 23 + memoryStatsWidth;
        var machine = new View { Y = Pos.AnchorEnd(), Width = Dim.Fill(), Height = 1 };
        var cpuValue = AddHardwareField(machine, "Host · CPU ", 0, 6);
        var ramValue = AddHardwareField(machine, "  RAM ", Pos.Right(cpuValue), memoryStatsWidth);
        var services = new View { Width = Dim.Fill(), Height = 1, CanFocus = true };
        var serviceButtons = new List<Button>();
        for (var i = 0; i <= targets.Count; i++)
        {
            var button = new Button
            {
                X = i == 0 ? 0 : Pos.Right(serviceButtons[i - 1]) + 1,
                NoDecorations = true,
                ShadowStyle = ShadowStyles.None
            };
            button.HasFocusChanged += (_, _) =>
            {
                if (!button.HasFocus) return;
                if (button.Frame.Left < services.Viewport.Left)
                    services.ScrollHorizontal(button.Frame.Left - services.Viewport.Left);
                else if (button.Frame.Right > services.Viewport.Right)
                    services.ScrollHorizontal(button.Frame.Right - services.Viewport.Right);
            };
            serviceButtons.Add(button);
            services.Add(button);
        }
        var startButton = new Button { Y = Pos.Bottom(services), Width = 13, Text = "Iniciar", ShadowStyle = ShadowStyles.None, Enabled = false };
        var stopButton = new Button { X = Pos.Right(startButton), Y = Pos.Top(startButton), Width = 12, Text = "Detener", ShadowStyle = ShadowStyles.None, Enabled = false };
        var statusMark = new Label { X = Pos.Right(stopButton) + 1, Y = Pos.Top(startButton), Text = "●" };
        var status = new Label { X = Pos.Right(statusMark) + 1, Y = Pos.Top(statusMark), Width = Dim.Fill() };
        var deviceCharts = new Dictionary<string, (int Number, Label Name, UsageChart Gpu, UsageChart Vram, View Stats, Label Load, Label Memory, int StatsWidth)>(StringComparer.OrdinalIgnoreCase);
        var visibleChartRows = 4;
        var charts = new View
        {
            Width = Dim.Fill(), CanFocus = true,
            Height = Dim.Func(_ => Math.Min(visibleChartRows, Math.Max(2, window.Viewport.Height / 2 - 3))),
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar
        };
        var phaseGraph = new UsageGraph(useColor, "?PG") { Y = 1, Width = Dim.Percent(50), Height = 1 };
        var axis = new Label { Width = Dim.Fill(), Text = "Fase /slots · 2 s/col · P prompt · G generación · ? incierta · · sin muestra" };
        var cpuGraph = new UsageChart("CPU", useColor) { Y = 2, Width = Dim.Percent(50) };
        var ramGraph = new UsageChart("RAM", useColor) { Y = 2, X = Pos.Right(cpuGraph), Width = Dim.Percent(50) };
        charts.Add(axis, phaseGraph, cpuGraph, ramGraph);
        var inferenceLabel = new Label { Y = Pos.Bottom(status), Width = Dim.Fill(), Text = "Inferencia · esperando catálogo" };
        var promptGraph = new UsageChart("Prompt", useColor) { Y = Pos.Bottom(inferenceLabel) };
        var outputGraph = new UsageChart("Generación", useColor) { Y = Pos.Bottom(promptGraph) };
        var summary = new ListView
        {
            Y = Pos.Bottom(status), Width = Dim.Fill(), Height = 2,
            ViewportSettings = ViewportSettingsFlags.HasHorizontalScrollBar | ViewportSettingsFlags.HasVerticalScrollBar
        };
        var taskRows = new ListView
        {
            Y = Pos.Bottom(summary), Width = Dim.Fill(),
            Height = Dim.Func(_ => window.Viewport.Height < 28 ? 2 : 4),
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar
        };
        inferenceLabel.Y = Pos.Bottom(taskRows);
        charts.Y = Pos.Bottom(outputGraph);
        var logsButton = new Button { Y = Pos.Bottom(charts), NoDecorations = true, ShadowStyle = ShadowStyles.None };
        var presetsButton = new Button { X = Pos.Right(logsButton) + 1, Y = Pos.Top(logsButton), NoDecorations = true, ShadowStyle = ShadowStyles.None };
        var resourcesButton = new Button { X = Pos.Right(presetsButton) + 1, Y = Pos.Top(logsButton), NoDecorations = true, ShadowStyle = ShadowStyles.None, Text = "Recursos" };
        var resources = new ListView
        {
            Y = Pos.Bottom(logsButton), Width = Dim.Fill(), Height = Dim.Fill(Dim.Height(machine)), Visible = false,
            ViewportSettings = ViewportSettingsFlags.HasHorizontalScrollBar | ViewportSettingsFlags.HasVerticalScrollBar
        };
        var lines = new ListView { Y = Pos.Bottom(logsButton), Width = Dim.Fill(), Height = Dim.Fill(Dim.Height(machine)) };
        var entries = new ListView { Y = Pos.Bottom(logsButton), Width = Dim.Fill(), Height = Dim.Fill(Dim.Height(machine)) };
        var system = new ListView { Y = Pos.Bottom(services), Width = Dim.Fill(), Height = Dim.Fill(Dim.Height(machine)), Visible = false };
        window.Add(summary, taskRows, services, startButton, stopButton, statusMark, status, charts, inferenceLabel, promptGraph, outputGraph,
            logsButton, presetsButton, resourcesButton, lines, entries, resources, system, machine);
        if (useColor)
        {
            Paint(window, Palette.Text, Palette.Base);
            Paint(machine, Palette.Muted, Palette.Surface);
            Paint(services, Palette.Text, Palette.Base);
            Paint(status, Palette.Muted, Palette.Base);
            Paint(startButton, Palette.Accent, Palette.Card);
            Paint(stopButton, Palette.Text, Palette.Card);
            Paint(charts, Palette.Muted, Palette.Surface);
            Paint(inferenceLabel, Palette.Muted, Palette.Surface);
            Paint(entries, Palette.Text, Palette.Surface);
            Paint(taskRows, Palette.Text, Palette.Surface);
            Paint(summary, Palette.Text, Palette.Surface);
            Paint(lines, Palette.Text, Palette.Surface);
            Paint(resources, Palette.Text, Palette.Surface);
            Paint(system, Palette.Text, Palette.Surface);
        }
        var index = targets.Count == 0 ? targets.Count : 0;
        var showLogsPanel = true;
        var showResources = false;
        string? shownResources = null;
        bool expandedLogs = false;
        var expandButton = new Button { X = Pos.Right(resourcesButton) + 1, Y = Pos.Top(logsButton),
            NoDecorations = true, ShadowStyle = ShadowStyles.None, Text = "Ampliar" };
        var pauseButton = new Button { X = Pos.Right(resourcesButton) + 1, Y = Pos.Top(logsButton),
            NoDecorations = true, ShadowStyle = ShadowStyles.None, Text = "Pausar" };
        pauseButton.X = Pos.Right(expandButton) + 1;
        window.Add(expandButton, pauseButton);
        logsButton.Accepted += (_, _) => SelectPanel(showLogs: true);
        presetsButton.Accepted += (_, _) => SelectPanel(showLogs: false);
        resourcesButton.Accepted += (_, _) => SelectPanel(showLogs: false, resourcePanel: true);

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
        string? shownPresets = null, shownLogs = null, shownSystem = null;
        var inference = new InferenceTasks(new ConfigurationManager(Home.Resolve()).InferenceHistory());
        InferenceTaskIdentity? graphTask = null;
        string? inferenceEntry = null, preferredEntry = null;
        IReadOnlyList<EntrySnapshot> lastEntries = [];
        bool refreshingEntries = false, refreshingTasks = false, paused = false;
        IReadOnlyList<InferenceTask> visibleTasks = [];
        InferenceTaskIdentity? preferredTask = null;
        string? shownTasks = null, shownSummary = null;
        expandButton.Accepted += (_, _) =>
        {
            expandedLogs = !expandedLogs;
            expandButton.Text = expandedLogs ? "Monitor" : "Ampliar";
            SelectPanel(showLogs: true);
        };
        pauseButton.Accepted += (_, _) =>
        {
            paused = !paused;
            pauseButton.Text = paused ? "Reanudar" : "Pausar";
            if (!paused && selectedSnapshot is not null) ShowInference(selectedSnapshot.Entries);
        };
        taskRows.ValueChanged += (_, _) =>
        {
            if (refreshingTasks || taskRows.SelectedItem is not int row || row < 0 || row >= visibleTasks.Count) return;
            preferredTask = visibleTasks[row].Identity;
            if (selectedSnapshot is not null) ShowInference(selectedSnapshot.Entries);
        };
        taskRows.KeyDown += (_, key) =>
        {
            if (key != Key.Enter) return;
            key.Handled = true;
            if (taskRows.SelectedItem is int row && row >= 0 && row < visibleTasks.Count)
                ShowDetail("Tarea · último valor observado", InferencePresentation.Detail(visibleTasks[row]));
        };
        entries.KeyDown += (_, key) =>
        {
            if (key != Key.Enter) return;
            key.Handled = true;
            if (entries.SelectedItem is int row && row >= 0 && row < lastEntries.Count)
                ShowDetail("Entrada · métricas del backend", EntryDetail(lastEntries[row]));
        };
        summary.KeyDown += (_, key) =>
        {
            if (key != Key.Enter) return;
            key.Handled = true;
            if (summary.SelectedItem is int row && row >= 0 && row < lastEntries.Count)
                ShowDetail("Entrada · métricas del backend", EntryDetail(lastEntries[row]));
        };
        SelectPanel(showLogs: true);
        Select();
        entries.ValueChanged += (_, _) =>
        {
            if (!refreshingEntries && entries.SelectedItem is int selected && selected >= 0 && selected < lastEntries.Count)
            {
                preferredEntry = lastEntries[selected].Name;
                preferredTask = null;
                ShowInference(lastEntries);
            }
        };
        summary.ValueChanged += (_, _) =>
        {
            if (!refreshingEntries && summary.SelectedItem is int selected && selected >= 0 && selected < lastEntries.Count)
            {
                preferredEntry = lastEntries[selected].Name;
                preferredTask = null;
                ShowInference(lastEntries);
            }
        };
        startButton.Accepted += (_, _) => BeginServiceAction(stopService: false);
        stopButton.Accepted += (_, _) => BeginServiceAction(stopService: true);
        for (var i = 0; i < serviceButtons.Count; i++)
        {
            var selection = i;
            serviceButtons[i].Accepted += (_, _) => SelectService(selection);
        }

        app.AddTimeout(TimeSpan.FromSeconds(1), () =>
        {
            if (operation is { IsCompleted: true } completed)
            {
                operation = null;
                selectedSnapshot = null;
                var problem = completed.IsCompletedSuccessfully ? completed.Result : completed.Exception?.GetBaseException().Message;
                if (!string.IsNullOrWhiteSpace(problem))
                    MessageBox.Query(app, "Operación de servicio", problem, "Cerrar");
            }
            if (evidence is null && probe.IsCompletedSuccessfully) evidence = probe.Result;
            if (machineSample is null || machineSample.IsCompleted)
            {
                var captured = machineSample is { IsCompletedSuccessfully: true };
                if (captured)
                {
                    if (lastHardware is not null) hardwareCadence = machineSample!.Result.StartedAt - lastHardware.StartedAt;
                    lastHardware = machineSample!.Result;
                    hardwareProblem = null;
                }
                else if (machineSample is { IsFaulted: true })
                {
                    hardwareProblem = machineSample.Exception!.GetBaseException().Message;
                }
                machineSample = null;
                if (!machineClock.IsRunning || machineClock.Elapsed >= CaptureInterval)
                {
                    machineSample = Task.Run(() =>
                    {
                        machineClock.Restart();
                        return hardware.CaptureAsync(stop.Token);
                    });
                }
                ShowHardware(lastHardware, captured);
            }
            else ShowHardware(lastHardware, false);
            phaseGraph.SetNeedsDraw();
            promptGraph.SetNeedsDraw();
            outputGraph.SetNeedsDraw();
            foreach (var (targetIndex, pending) in serviceSamples.Where(item => item.Value.Task.IsCompleted).ToArray())
            {
                serviceSamples.Remove(targetIndex);
                var selected = operation is null && targetIndex == index && pending.Epoch == selectionEpoch;
                if (pending.Task.IsCompletedSuccessfully && selected)
                    Show(pending.Task.Result);
                else if (pending.Task.IsFaulted)
                {
                    var message = pending.Task.Exception!.GetBaseException().Message;
                    if (selected)
                    {
                        selectedSnapshot = null;
                        startButton.Enabled = stopButton.Enabled = false;
                        status.Text = "Observación no disponible · " + message;
                        SetStatusColor(Palette.Warning, ColorName16.BrightYellow);
                        inference.Suspend();
                        ShowInference([]);
                    }
                }
            }
            if (operation is null && index < targets.Count && serviceSamples.Count < 4 && !serviceSamples.ContainsKey(index) &&
                (!serviceCaptures.TryGetValue(index, out var lastCapture) || lastCapture.Elapsed >= CaptureInterval))
            {
                var target = targets[index];
                var clock = new Stopwatch();
                serviceCaptures[index] = clock;
                serviceSamples.Add(index, (selectionEpoch, Task.Run(() =>
                {
                    clock.Start();
                    return CaptureAsync(target, stop.Token);
                })));
            }
            if (showResources && index < targets.Count) ShowResourceDetails();
            return true;
        });
        try { app.Run(window); }
        finally
        {
            stop.Cancel();
            var observations = Task.WhenAll(serviceSamples.Values.Select(value => (Task)value.Task)
                .Concat(machineSample is null ? [] : new Task[] { machineSample }));
            _ = observations.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            try { observations.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            catch (Exception e) when (ServiceObservation.ObservationError(e) || e is TimeoutException) { }
            try { operation?.GetAwaiter().GetResult(); }
            catch (Exception e) when (ServiceObservation.ObservationError(e)) { }
        }
        return 0;

        void SelectService(int selected)
        {
            if (index == selected) return;
            index = selected;
            selectionEpoch++;
            Select();
        }

        void BeginServiceAction(bool stopService)
        {
            if (operation is not null || index >= targets.Count || selectedSnapshot is not { } snapshot) return;
            var running = snapshot.ProcessState == "alive" && snapshot.InstanceId is not null;
            if (!running && (stopService || snapshot.ProcessState != "stopped")) return;
            var target = targets[index];
            var verb = stopService ? "Detener" : running ? "Reiniciar" : "Iniciar";
            if (running && MessageBox.Query(app, verb + " servicio",
                    $"{verb} «{target.Service.Name}» ({(target.Store.Context.IsGlobal ? "global" : "proyecto")}) puede interrumpir peticiones activas.",
                    verb, "Cancelar") != 0) return;

            startButton.Enabled = stopButton.Enabled = false;
            status.Text = $"{verb} · {target.Service.Name}…";
            selectionEpoch++;
            operation = Task.Run(async () =>
            {
                using var diagnostics = new StringWriter();
                var lifecycle = new ScopedServiceProcess(target.Store, TextWriter.Null, diagnostics);
                try
                {
                    if (stopService)
                    {
                        lifecycle.Stop(target.Service.ServiceId, expectedInstanceId: snapshot.InstanceId);
                        return null;
                    }
                    var context = target.Store.Context;
                    var selection = context.IsGlobal
                        ? new RecipeImporter(context).ServiceSelection(target.Service.ServiceId)
                        : new RecipeSelection(context.Base, new RecipeRegistry(target.Store).Get(target.Service.RecipeId).Files, context.Base);
                    var instance = await ScopedServiceCli.RunCandidateAsync(lifecycle, new(context, selection, target.Service),
                        running, detached: true, privateResources: false, TextWriter.Null, diagnostics,
                        new DashboardModelSelection(app, diagnostics), stop.Token,
                        expectedInstanceId: running ? snapshot.InstanceId : null);
                    if (running && instance == snapshot.InstanceId)
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

        Label AddHardwareField(View parent, string caption, Pos x, int width)
        {
            var label = new Label { X = x, Text = caption };
            var value = new Label { X = Pos.Right(label), Width = width, Text = "—".PadLeft(width) };
            parent.Add(label, value);
            return value;
        }

        void SelectPanel(bool showLogs, bool resourcePanel = false)
        {
            showLogsPanel = showLogs;
            showResources = resourcePanel;
            logsButton.Text = showLogs ? "▸ Logs" : "  Logs";
            presetsButton.Text = showLogs || resourcePanel ? "  Presets" : "▸ Presets";
            resourcesButton.Text = resourcePanel ? "▸ Recursos" : "  Recursos";
            if (useColor)
            {
                Paint(logsButton, showLogs ? Palette.Accent : Palette.Text, Palette.Card);
                Paint(presetsButton, showLogs || resourcePanel ? Palette.Text : Palette.Accent, Palette.Card);
                Paint(resourcesButton, resourcePanel ? Palette.Accent : Palette.Text, Palette.Card);
            }
            lines.Visible = index < targets.Count && showLogs;
            entries.Visible = index < targets.Count && !showLogs && !resourcePanel;
            resources.Visible = index < targets.Count && resourcePanel;
            if (!showLogs) expandedLogs = false;
            expandButton.Text = expandedLogs ? "Monitor" : "Ampliar";
            summary.Visible = taskRows.Visible = inferenceLabel.Visible = promptGraph.Visible = outputGraph.Visible =
                charts.Visible = index < targets.Count && !expandedLogs;
            logsButton.Y = expandedLogs ? Pos.Bottom(status) : Pos.Bottom(charts);
        }

        void ShowHardware(HardwareSnapshot? sample, bool captured)
        {
            var now = DateTimeOffset.UtcNow;
            var columnEnd = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / 2 * 2).ToLocalTime();
            axis.Text = $"← 2 s/col · {columnEnd:HH:mm:ss} → · P prompt · G generación · ? incierta · · sin muestra";
            ShowUsage(cpuGraph, cpuValue, sample?.Samples, "cpu.utilization");
            ShowUsage(ramGraph, ramValue, sample?.Samples, "ram.used", "ram.total");

            foreach (var device in sample?.Devices ?? [])
            {
                if (ComputeLabel(device.Id) is null || deviceCharts.ContainsKey(device.Id)) continue;
                var number = deviceCharts.Count;
                var gpuCaption = $"  GPU{number} ";
                var vramCaption = $"  VRAM{number} ";
                var width = gpuCaption.Length + 6 + vramCaption.Length + memoryStatsWidth;
                var stats = new View { Width = width, Height = 1 };
                var loadValue = AddHardwareField(stats, gpuCaption, 0, 6);
                var memoryValue = AddHardwareField(stats, vramCaption, Pos.Right(loadValue), memoryStatsWidth);
                var gpu = new UsageChart($"GPU{number}", useColor) { Width = Dim.Percent(50) };
                var vram = new UsageChart($"VRAM{number}", useColor) { X = Pos.Right(gpu), Width = Dim.Percent(50) };
                var name = new Label { Width = Dim.Fill(), Text = $"GPU{number} · {device.Name} · {ComputeLabel(device.Id)}" };
                deviceCharts.Add(device.Id, (number, name, gpu, vram, stats, loadValue, memoryValue, width));
                charts.Add(name, gpu, vram);
                machine.Add(stats);
            }

            var row = 4;
            var statsRow = 0;
            var statsX = hostStatsWidth;
            foreach (var (id, views) in deviceCharts)
            {
                if (statsX > 0 && statsX + views.StatsWidth > window.Viewport.Width)
                {
                    statsRow++;
                    statsX = 0;
                }
                views.Stats.X = statsX;
                views.Stats.Y = statsRow;
                statsX += views.StatsWidth;
                views.Name.Y = row;
                views.Gpu.Y = views.Vram.Y = row + 1;
                var device = sample?.Devices.FirstOrDefault(device => StringComparer.OrdinalIgnoreCase.Equals(device.Id, id));
                ShowUsage(views.Gpu, views.Load, device?.Samples, "gpu.utilization");
                ShowUsage(views.Vram, views.Memory, device?.Samples, "vram.used", "vram.total");
                row += 3;
            }
            visibleChartRows = row;
            if (machine.Frame.Height != statsRow + 1) machine.Height = statsRow + 1;
            if (charts.GetContentSize().Height != row || charts.GetContentSize().Width != window.Viewport.Width)
                charts.SetContentSize(new System.Drawing.Size(window.Viewport.Width, row));
            if (index == targets.Count) ShowSystem(sample);

            void ShowUsage(UsageChart chart, Label field, IReadOnlyList<MetricSample>? samples, string name,
                string? capacityName = null)
            {
                var metric = samples?.FirstOrDefault(item => item.Name == name);
                var capacity = capacityName is null ? null : samples?.FirstOrDefault(item => item.Name == capacityName);
                var fresh = metric?.IsFresh(now, MetricLifetime) == true;
                var used = fresh ? metric?.Value : null;
                var total = capacityName is null ? 100 : capacity?.IsFresh(now, MetricLifetime) == true ? capacity.Value : null;
                var (display, suffix) = capacityName is null ? (Percent(metric?.Value), "") : MemoryPair(metric?.Value, capacity?.Value);
                var status = metric?.Value is null ? "n/d" : fresh ? null : "ant.";
                var ratio = Fraction(used, total);
                chart.ShowResource(metric?.Value is null ? "n/d" : display, ratio,
                    suffix + (status == "ant." ? " ant." : "") + (metric is null ? "" : $" · {metric.ObservedAt.ToLocalTime():HH:mm:ss}"),
                    captured ? metric?.ObservedAt ?? sample?.StartedAt ?? now : null,
                    Fraction(metric?.Value, capacityName is null ? 100 : capacity?.Value));
                var compact = status ?? (capacityName is null ? display : StatsMemory(used));
                UpdateHardwareValue(field, compact.PadLeft(capacityName is null ? 6 : memoryStatsWidth), ratio);
            }
        }

        void ShowResourceDetails()
        {
            var now = DateTimeOffset.UtcNow;
            var rows = new List<string>
            {
                "2 s mín. entre capturas por fuente · refresco 1 s · ant. > 4 s",
                "Escala 0–100% · memoria respecto de la capacidad observada",
                "Histórico de sesión · hasta 24 columnas de 2 s · · sin lectura",
                "Coincidencia temporal · hardware compartido · fase de la tarea enfocada",
                $"Máquina · {lastHardware?.Environment ?? "n/d"}"
            };
            if (hardwareProblem is not null) rows.Add("Captura no disponible · " + hardwareProblem);
            if (lastHardware is { } sample)
            {
                rows.Add($"Captura {sample.StartedAt.ToLocalTime():HH:mm:ss} · {(sample.CompletedAt - sample.StartedAt).TotalMilliseconds:0} ms");
                rows.Add("Intervalo efectivo entre inicios · " +
                    (hardwareCadence is { } cadence ? $"{cadence.TotalSeconds:0.###} s" : "n/d · primera captura"));
                rows.AddRange(sample.Samples.Select(metric => FormatMetric(metric, now)));
                foreach (var (id, views) in deviceCharts)
                {
                    rows.Add(views.Name.Text);
                    var device = sample.Devices.FirstOrDefault(device => StringComparer.OrdinalIgnoreCase.Equals(device.Id, id));
                    if (device is null) rows.Add("  n/d · dispositivo sin lectura en la última captura");
                    else rows.AddRange(device.Samples.Select(metric => FormatMetric(metric, now)));
                }
                rows.AddRange(sample.Diagnostics);
            }
            rows.Add("Servicio seleccionado · instancia y procesos verificados");
            if (selectedSnapshot is { InstanceId: not null } snapshot && snapshot.Metrics.Count > 0)
                rows.AddRange(snapshot.Metrics.Select(metric => FormatMetric(metric, now)));
            else rows.Add("n/d · sin muestras atribuibles");
            var selected = resources.SelectedItem;
            var text = string.Join('\n', rows);
            SetRows(resources, ref shownResources, text);
            var detailRows = text.Split('\n');
            if (selected is >= 0 && selected < detailRows.Length) resources.SelectedItem = selected;
            resources.SetContentSize(new System.Drawing.Size(detailRows.Max(row => row.Length), detailRows.Length));
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
            var time = metric.ObservedAt.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var window = metric.Window is { } span ? $"ventana {span.TotalMilliseconds:0} ms" : "sin ventana informada";
            return $"{name} {value}\n  {metric.Scope}\n  {time} · {window}\n  {metric.Source}";
        }

        string? ComputeLabel(string id)
        {
            if (evidence is null) return null;
            var cuda = evidence.Cuda.Contains(id);
            var vulkan = evidence.Vulkan.Contains(id);
            return cuda ? vulkan ? "CUDA / Vulkan" : "CUDA" : vulkan ? "Vulkan" : null;
        }

        void ShowSystem(HardwareSnapshot? sample)
        {
            var context = evidence?.Context;
            var rows = new List<string> { "Sistema · inventario y sondeo" };
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
                    var confirmed = deviceCharts.TryGetValue(device.Id, out var views);
                    var name = confirmed ? $"GPU{views.Number} · {device.Name}" : device.Name;
                    rows.Add($"  {name} · {ComputeLabel(device.Id) ?? "Cómputo sin confirmar"}");
                }
                rows.AddRange(sample.Diagnostics);
            }
            var selected = system.SelectedItem;
            SetRows(system, ref shownSystem, string.Join('\n', rows));
            if (selected is >= 0 && selected < rows.Count) system.SelectedItem = selected;
        }

        void UpdateHardwareValue(Label label, string text, double? ratio)
        {
            label.Text = text;
            if (useColor) Paint(label, UsageColor(ratio, app.Driver?.Force16Colors == true), Palette.Surface);
        }

        void Select()
        {
            for (var i = 0; i < serviceButtons.Count; i++)
            {
                var button = serviceButtons[i];
                button.Text = (i == index ? "▸ " : "  ") + (i == targets.Count ? "Sistema" : targets[i].Label);
                if (useColor) Paint(button, i == index ? Palette.Accent : Palette.Muted, Palette.Card);
            }
            if (serviceButtons.Count > 0)
                services.SetContentSize(new System.Drawing.Size(serviceButtons.Sum(button => button.Text.Length + 3), 1));
            var systemSelected = index == targets.Count;
            system.Visible = systemSelected;
            statusMark.Visible = status.Visible = startButton.Visible = stopButton.Visible = charts.Visible =
                inferenceLabel.Visible = promptGraph.Visible = outputGraph.Visible =
                logsButton.Visible = presetsButton.Visible = resourcesButton.Visible = taskRows.Visible = summary.Visible =
                expandButton.Visible = pauseButton.Visible = !systemSelected;
            selectedSnapshot = null;
            startButton.Enabled = stopButton.Enabled = false;
            startButton.Text = "Iniciar";
            lines.Visible = !systemSelected && showLogsPanel;
            entries.Visible = !systemSelected && !showLogsPanel && !showResources;
            resources.Visible = !systemSelected && showResources;
            phaseGraph.Clear();
            inference.Suspend();
            if (resources.Visible) ShowResourceDetails();
            if (systemSelected)
            {
                ShowSystem(lastHardware);
                return;
            }
            status.Text = targets.Count == 0 ? diagnostic : "Consultando estado del servicio…";
            SetStatusColor(Palette.Information, ColorName16.BrightBlue);
            preferredTask = null;
            paused = false;
            pauseButton.Text = "Pausar";
            preferredEntry = inferenceEntry = null;
            graphTask = null;
            promptGraph.Clear();
            outputGraph.Clear();
            lastEntries = [];
            SetRows(summary, ref shownSummary, "Esperando catálogo…");
            SelectPanel(showLogsPanel, showResources);
            ShowInference([]);
            SetRows(entries, ref shownPresets, targets.Count == 0 ? "Los presets del servicio seleccionado aparecerán aquí." : "Esperando catálogo…");
            SetRows(lines, ref shownLogs, targets.Count == 0 ? "Los logs del servicio seleccionado aparecerán aquí." : "Consultando últimas líneas…");
        }

        void Show(ServicePanel panel)
        {
            var snapshot = panel.Snapshot;
            snapshot = snapshot with { Entries = snapshot.Entries.OrderByDescending(entry => entry.Inference?.Active.Count > 0)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray() };
            selectedSnapshot = snapshot;
            var running = snapshot.ProcessState == "alive" && snapshot.InstanceId is not null;
            startButton.Text = running ? "Reiniciar" : "Iniciar";
            startButton.Enabled = running || snapshot.ProcessState == "stopped";
            stopButton.Enabled = running;
            status.Text = $"{snapshot.ProcessState} · API {snapshot.ApiState} · {snapshot.Health} · {snapshot.CompletedAt.ToLocalTime():HH:mm:ss}";
            if (snapshot.ProcessState == "stopped") SetStatusColor(Palette.Muted, ColorName16.Gray);
            else if (snapshot.Health.StartsWith("HTTP 5", StringComparison.Ordinal))
                SetStatusColor(Palette.Danger, ColorName16.BrightRed);
            else if (snapshot.ApiState == "available" && snapshot.Health.StartsWith("HTTP 2", StringComparison.Ordinal))
                SetStatusColor(Palette.Success, ColorName16.BrightCyan);
            else SetStatusColor(Palette.Warning, ColorName16.BrightYellow);
            inference.Observe(snapshot);
            if (paused) return;
            var presets = snapshot.Entries.Count == 0
                ? snapshot.ApiState == "available" ? "Sin presets publicados." : "Catálogo no disponible."
                : string.Join("\n", snapshot.Entries.Select(FormatEntry));
            var previousSelection = entries.SelectedItem;
            lastEntries = snapshot.Entries;
            refreshingEntries = true;
            SetRows(entries, ref shownPresets, presets);
            if (!paused) SetRows(summary, ref shownSummary, presets);
            if (preferredEntry is not null)
            {
                var selected = snapshot.Entries.ToList().FindIndex(entry => entry.Name == preferredEntry);
                if (selected >= 0) entries.SelectedItem = summary.SelectedItem = selected;
            }
            else if (previousSelection is >= 0 && previousSelection < snapshot.Entries.Count)
                entries.SelectedItem = previousSelection;
            refreshingEntries = false;
            ShowInference(snapshot.Entries);
            if (!paused) SetRows(lines, ref shownLogs, panel.Logs);
        }

        void ShowInference(IReadOnlyList<EntrySnapshot> observed)
        {
            if (paused) return;
            visibleTasks = index < targets.Count ? InferencePresentation.Rows(inference,
                targets[index].Store.Context.IsGlobal ? "global" : targets[index].Store.Context.Base,
                targets[index].Service.ServiceId) : [];
            refreshingTasks = true;
            SetRows(taskRows, ref shownTasks, visibleTasks.Count == 0 ? "Sin tareas observables · Enter en Presets: fuentes y métricas" :
                string.Join('\n', visibleTasks.Select(task => InferencePresentation.Row(task, window.Viewport.Width))));
            var selectedTask = visibleTasks.FirstOrDefault(task => task.Identity == preferredTask)
                ?? (preferredEntry is null ? null : visibleTasks.FirstOrDefault(task => task.Identity.Entry == preferredEntry));
            if (selectedTask is not null) taskRows.SelectedItem = visibleTasks.ToList().IndexOf(selectedTask);
            else if (visibleTasks.Count > 0) taskRows.SelectedItem = 0;
            refreshingTasks = false;
            var entry = observed.FirstOrDefault(item => item.Name == selectedTask?.Identity.Entry)
                ?? observed.FirstOrDefault(item => item.Name == preferredEntry)
                ?? observed.FirstOrDefault(item => item.Inference?.Active.Count > 0)
                ?? observed.FirstOrDefault(item => item.Name == inferenceEntry)
                ?? observed.FirstOrDefault(item => item.Inference is not null);
            inferenceEntry = selectedTask?.Identity.Entry ?? entry?.Name ?? inferenceEntry;
            var active = inference.Active.Where(task => task.Identity.Entry == inferenceEntry).ToArray();
            var latest = selectedSnapshot is { } current ? inference.History.LastOrDefault(task =>
                task.Identity.Scope == current.Scope && task.Identity.Service == current.ServiceId &&
                task.Identity.Entry == inferenceEntry) : null;
            var display = selectedTask ?? active.FirstOrDefault(task => task.Identity == graphTask) ?? active.FirstOrDefault() ?? latest;
            var focused = display?.End is null ? display : null;
            graphTask = display?.Identity;
            phaseGraph.Clear();
            promptGraph.Clear();
            outputGraph.Clear();
            foreach (var point in display?.Points ?? [])
            {
                var date = point.At;
                phaseGraph.AddTimedSample(date, point.Phase switch
                {
                    InferenceTaskPhase.Prompt => 1,
                    InferenceTaskPhase.Generation => 2,
                    _ => 0
                });
                promptGraph.AddRate(point.Prompt, display?.PromptMean, display?.PromptPeak, display?.PromptSeconds ?? 0, date);
                outputGraph.AddRate(point.Output, display?.OutputMean, display?.OutputPeak, display?.OutputSeconds ?? 0, date);
            }

            var phase = focused?.Phase switch
            {
                InferenceTaskPhase.Generation => $"generación · {focused.Last.Decoded} tk",
                InferenceTaskPhase.Prompt => $"prompt · {focused.Last.Prompt} tk · caché {focused.Last.Cached}",
                not null => "fase sin confirmar",
                _ when entry?.Inference is null => "señal /slots sin verificar",
                _ => "sin tarea activa"
            };
            if (focused is not null)
                phase += $" · slot {focused.Identity.Slot} / tarea {focused.Identity.Task} · {active.Length}/{inference.Active.Count()} activas";
            else if (display is not null)
                phase = $"{InferencePresentation.Phase(display)} · P {display.Last.Prompt} / G {display.Last.Decoded} tk · resultado sin confirmar";
            inferenceLabel.Text = $"/slots · {inferenceEntry ?? "—"} · {phase}";
            promptGraph.AddRate(focused?.PromptRate, display?.PromptMean, display?.PromptPeak, display?.PromptSeconds ?? 0, null);
            outputGraph.AddRate(focused?.OutputRate, display?.OutputMean, display?.OutputPeak, display?.OutputSeconds ?? 0, null);
        }

        void ShowDetail(string title, string text)
        {
            using var dialog = new Dialog { Title = title, Width = Dim.Percent(95), Height = Dim.Percent(90) };
            var detail = new ListView { Width = Dim.Fill(), Height = Dim.Fill(),
                ViewportSettings = ViewportSettingsFlags.HasHorizontalScrollBar | ViewportSettingsFlags.HasVerticalScrollBar };
            var rows = text.Split('\n');
            detail.SetSource(new ObservableCollection<string>(rows));
            detail.SetContentSize(new System.Drawing.Size(rows.Max(row => row.Length), rows.Length));
            dialog.Add(detail);
            app.Run(dialog);
        }

        string EntryDetail(EntrySnapshot entry)
        {
            var source = entry.Inference;
            var rows = new List<string>
            {
                $"{entry.Name} · {entry.State} · modelo {source?.Model ?? "n/d"}",
                $"Instancia {selectedSnapshot?.InstanceId ?? "n/d"} · hijo {source?.Pid.ToString() ?? "n/d"}/{source?.Creation.ToString() ?? "n/d"}",
                "Acumulados y medias del backend: período no informado.",
                "Cada hijo abre otro período; sin total acumulado entre intentos.",
                "Las medias de entradas no se suman como tasa instantánea del servicio.",
                "Instantánea de detalle; la captura continúa mientras se consulta.",
                entry.Diagnostic ?? "Métricas disponibles según la fuente",
                source is null ? entry.InferenceDiagnostic ?? "Tareas /slots: n/d · hijo/build/endpoint sin verificar" : "Tareas /slots: hijo y build verificados"
            };
            rows.AddRange(entry.Metrics.Select(metric => FormatMetric(metric, DateTimeOffset.UtcNow)));
            rows.Add("Especulación: contadores propuestos/aceptados sólo cuando /metrics los expone.");
            rows.Add("TTFT, ETA, resultado y razonamiento por tarea: n/d · sin fuente atribuible.");
            return string.Join('\n', rows);
        }

        void SetStatusColor(Color color, ColorName16 fallback)
        {
            if (useColor) Paint(statusMark, app.Driver?.Force16Colors == true ? new Color(fallback) : color, Palette.Base);
        }
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
                    using var dialog = new Dialog { Title = "Seleccionar recurso", Width = Dim.Percent(85), Height = Dim.Percent(70) };
                    var list = new ListView { Width = Dim.Fill(), Height = Dim.Fill() };
                    list.SetSource(new ObservableCollection<string>(options));
                    list.SelectedItem = 0;
                    var accept = new Button { Text = "Elegir", IsDefault = true };
                    var cancel = new Button { Text = "Cancelar" };
                    int? selected = null;
                    accept.Accepted += (_, _) => { selected = list.SelectedItem; app.RequestStop(dialog); };
                    cancel.Accepted += (_, _) => app.RequestStop(dialog);
                    dialog.Add(list);
                    dialog.AddButton(accept);
                    dialog.AddButton(cancel);
                    app.Run(dialog);
                    choice.TrySetResult(selected);
                }
                catch (Exception error) { choice.TrySetException(error); }
            });
            return await choice.Task.WaitAsync(cancellation);
        }
    }

    private static List<Target> Discover(out string diagnostic)
    {
        var targets = new List<Target>();
        diagnostic = "Sin servicios registrados en el ámbito global o en este proyecto.";
        if (!OperatingSystem.IsWindows())
        {
            diagnostic = "La observación de servicios registrados está disponible en Windows.";
            return targets;
        }
        var errors = new List<string>();
        var home = Home.Resolve();
        if (Directory.Exists(Path.Combine(home.Root, "global"))) Add(() => ScopeContext.Global(home));
        if (Directory.Exists(".coquena")) Add(() => ScopeContext.Project("coquena.yaml"));
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
        var snapshot = await new ServiceObservation(target.Store).CaptureAsync(target.Service, cancellation: cancellation, observeInference: true);
        cancellation.ThrowIfCancellationRequested();
        using var output = new StringWriter();
        try
        {
            var reader = new ServiceLogReader(target.Store, target.Service.ServiceId);
            // Recent without an ID bounds the read to the tail of the latest log.
            reader.Recent(output);
        }
        catch (Exception e) when (ServiceObservation.ObservationError(e)) { output.Write("Log no disponible: " + e.Message); }
        var tail = output.ToString().Replace("\r", "").TrimEnd().Split('\n').TakeLast(30).Reverse();
        var text = string.Join('\n', tail.Select(line => new string(line.Where(c => c == '\t' || !char.IsControl(c)).ToArray())));
        return new(snapshot, text.Length == 0 ? "Log vacío." : text);
    }

    private static string FormatEntry(EntrySnapshot entry)
    {
        var prompt = Rate("prompt_tokens_seconds");
        var generation = Rate("predicted_tokens_seconds");
        return $"{entry.Name} · {entry.State} · activas {Value("requests_processing")} / cola {Value("requests_deferred")} · medias P {prompt} / G {generation}" +
            $" · acumulados P {Value("prompt_tokens_total")} / caché {Value("prompt_tokens_cached_total")} / G {Value("tokens_predicted_total")} tk · Enter: detalle";

        string Value(string suffix) => entry.Metrics.FirstOrDefault(sample => sample.Name == "llamacpp:" + suffix)?.Value
            is double value ? value.ToString("0.#", CultureInfo.InvariantCulture) : "n/d";

        string Rate(string suffix) => entry.Metrics.FirstOrDefault(sample => sample.Name.EndsWith(suffix, StringComparison.Ordinal))?.Value
            is double rate ? rate.ToString("0.#", CultureInfo.InvariantCulture) + " tk/s" : "—";
    }

    private static double? Fraction(double? used, double? total) => total is > 0 ? used / total : null;

    private static Color UsageColor(double? usage, bool ansi16)
    {
        if (usage is not double value) return Palette.Muted;
        var ratio = Math.Clamp(value, 0, 1);
        if (ratio > 0.9) return ansi16 ? new Color(ColorName16.BrightRed) : Palette.Primary;
        if (ansi16) return new Color(ratio < 0.5 ? ColorName16.BrightCyan : ColorName16.BrightYellow);
        return new Color(Blend(Palette.Accent.R, Palette.Gold.R),
            Blend(Palette.Accent.G, Palette.Gold.G), Blend(Palette.Accent.B, Palette.Gold.B));

        int Blend(int start, int end) => (int)Math.Round(start + (end - start) * ratio);
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
            Focus = new Terminal.Gui.Drawing.Attribute(foreground, Palette.Border)
        });
}
