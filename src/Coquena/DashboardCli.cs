using System.Collections.ObjectModel;
using System.Globalization;
using Coquena.Backends;
using Coquena.Models;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Coquena;

internal static class DashboardCli
{
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

        public void AddSample(string display, double? used, double? total, string suffix = "")
        {
            var ratio = Fraction(used, total);
            value.Text = display;
            totalLabel.Text = suffix;
            if (useColor) Paint(value, UsageColor(ratio, App?.Driver?.Force16Colors == true), Palette.Surface);
            graph.AddSample(ratio);
        }

        public void Clear() => graph.Clear();

        public void AddRate(double? rate, double? mean, double? peak, double seconds)
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
            graph.AddSample(rate, maximum);

            static string Rate(double? number) => number?.ToString("0.#", CultureInfo.CurrentCulture) ?? "—";
        }
    }

    private sealed class UsageGraph(bool useColor) : View
    {
        private const int Capacity = 24;
        private readonly Queue<double?> history = new();
        private double? maximum;

        public void Clear() => history.Clear();

        public void AddSample(double? value, double? scale = null)
        {
            maximum = scale;
            if (history.Count == Capacity) history.Dequeue();
            history.Enqueue(value);
            SetNeedsDraw();
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            const string levels = "▁▂▃▄▅▆▇█";
            var column = 0;
            foreach (var sample in history.TakeLast(Viewport.Width))
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
                AddRune(sample is null ? '·' : levels[Math.Min((int)(ratio * levels.Length), levels.Length - 1)]);
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

    private static int RunDashboard()
    {
        var targets = Discover(out var diagnostic);
        var hardware = SystemMetrics.Create();
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
        var deviceCharts = new Dictionary<string, (Label Name, UsageChart Gpu, UsageChart Vram, View Stats, Label Load, Label Memory, int StatsWidth)>();
        var deviceNumbers = new Dictionary<string, int>();
        var visibleChartRows = 2;
        var charts = new View
        {
            Y = Pos.Bottom(status), Width = Dim.Fill(), CanFocus = true,
            Height = Dim.Func(_ => Math.Min(visibleChartRows, Math.Max(2, window.Viewport.Height / 2))),
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar
        };
        var cpuGraph = new UsageChart("CPU", useColor) { Width = Dim.Percent(50) };
        var ramGraph = new UsageChart("RAM", useColor) { X = Pos.Right(cpuGraph) };
        charts.Add(cpuGraph, ramGraph);
        var inferenceLabel = new Label { Y = Pos.Bottom(charts), Width = Dim.Fill(), Text = "Inferencia · esperando catálogo" };
        var promptGraph = new UsageChart("Prompt", useColor) { Y = Pos.Bottom(inferenceLabel) };
        var outputGraph = new UsageChart("Generación", useColor) { Y = Pos.Bottom(promptGraph) };
        var logsButton = new Button { Y = Pos.Bottom(outputGraph), NoDecorations = true, ShadowStyle = ShadowStyles.None };
        var presetsButton = new Button { X = Pos.Right(logsButton) + 1, Y = Pos.Top(logsButton), NoDecorations = true, ShadowStyle = ShadowStyles.None };
        var lines = new ListView { Y = Pos.Bottom(logsButton), Width = Dim.Fill(), Height = Dim.Fill(Dim.Height(machine)) };
        var entries = new ListView { Y = Pos.Bottom(logsButton), Width = Dim.Fill(), Height = Dim.Fill(Dim.Height(machine)) };
        var system = new ListView { Y = Pos.Bottom(services), Width = Dim.Fill(), Height = Dim.Fill(Dim.Height(machine)), Visible = false };
        window.Add(services, startButton, stopButton, statusMark, status, charts, inferenceLabel, promptGraph, outputGraph,
            logsButton, presetsButton, lines, entries, system, machine);
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
            Paint(lines, Palette.Text, Palette.Surface);
            Paint(system, Palette.Text, Palette.Surface);
        }
        var index = targets.Count == 0 ? targets.Count : 0;
        var showLogsPanel = true;
        SelectPanel(showLogs: true);
        logsButton.Accepted += (_, _) => SelectPanel(showLogs: true);
        presetsButton.Accepted += (_, _) => SelectPanel(showLogs: false);

        var samplingIndex = 0;
        var probe = Task.Run(HardwareProbe.InspectAdapters);
        HardwareProbe.AdapterEvidence? evidence = null;
        HardwareSnapshot? lastHardware = null;
        Task<HardwareSnapshot>? machineSample = null;
        Task<ServicePanel>? serviceSample = null;
        Task<string?>? operation = null;
        ServiceSnapshot? selectedSnapshot = null;
        string? shownPresets = null, shownLogs = null, shownSystem = null;
        var inference = new InferenceSeries();
        string? inferenceEntry = null, preferredEntry = null;
        IReadOnlyList<EntrySnapshot> lastEntries = [];
        bool refreshingEntries = false;
        Select();
        entries.ValueChanged += (_, _) =>
        {
            if (!refreshingEntries && entries.SelectedItem is int selected && selected >= 0 && selected < lastEntries.Count)
                preferredEntry = lastEntries[selected].Name;
        };
        startButton.Accepted += (_, _) => BeginServiceAction(stopService: false);
        stopButton.Accepted += (_, _) => BeginServiceAction(stopService: true);
        for (var i = 0; i < serviceButtons.Count; i++)
        {
            var selection = i;
            serviceButtons[i].Accepted += (_, _) => SelectService(selection);
        }

        app.AddTimeout(TimeSpan.FromMilliseconds(500), () =>
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
                if (machineSample is { IsCompletedSuccessfully: true }) ShowHardware(machineSample.Result);
                else if (machineSample is { IsFaulted: true })
                {
                    _ = machineSample.Exception;
                    ShowHardware(null);
                }
                machineSample = Task.Run(() => hardware.CaptureAsync(stop.Token));
            }
            if (operation is null && index < targets.Count && (serviceSample is null || serviceSample.IsCompleted))
            {
                if (serviceSample is { IsCompletedSuccessfully: true } && samplingIndex == index)
                    Show(serviceSample.Result);
                else if (serviceSample is { IsFaulted: true })
                {
                    var message = serviceSample.Exception!.GetBaseException().Message;
                    if (samplingIndex == index)
                    {
                        selectedSnapshot = null;
                        startButton.Enabled = stopButton.Enabled = false;
                        status.Text = "Observación no disponible · " + message;
                        SetStatusColor(Palette.Warning, ColorName16.BrightYellow);
                        ShowInference([]);
                    }
                }
                samplingIndex = index;
                var target = targets[index];
                serviceSample = Task.Run(() => CaptureAsync(target, stop.Token));
            }
            return true;
        });
        try { app.Run(window); }
        finally
        {
            stop.Cancel();
            foreach (var sample in new Task?[] { machineSample, serviceSample, operation })
            {
                try { sample?.GetAwaiter().GetResult(); }
                catch (Exception e) when (ServiceObservation.ObservationError(e)) { }
            }
        }
        return 0;

        void SelectService(int selected)
        {
            if (index == selected) return;
            index = selected;
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
            samplingIndex = -1;
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

        void SelectPanel(bool showLogs)
        {
            showLogsPanel = showLogs;
            logsButton.Text = showLogs ? "▸ Logs" : "  Logs";
            presetsButton.Text = showLogs ? "  Presets" : "▸ Presets";
            if (useColor)
            {
                Paint(logsButton, showLogs ? Palette.Accent : Palette.Text, Palette.Card);
                Paint(presetsButton, showLogs ? Palette.Text : Palette.Accent, Palette.Card);
            }
            lines.Visible = index < targets.Count && showLogs;
            entries.Visible = index < targets.Count && !showLogs;
        }

        void ShowHardware(HardwareSnapshot? sample)
        {
            if (sample is not null) lastHardware = sample;
            var cpu = Metric(sample?.Samples, "cpu.utilization");
            var ram = Metric(sample?.Samples, "ram.used");
            var ramTotal = Metric(sample?.Samples, "ram.total");
            cpuGraph.AddSample(Percent(cpu), cpu, 100);
            var (ramUsed, ramSuffix) = MemoryPair(ram, ramTotal);
            ramGraph.AddSample(ramUsed, ram, ramTotal, ramSuffix);
            UpdateHardwareValue(cpuValue, $"{Percent(cpu),6}", cpu / 100);
            UpdateHardwareValue(ramValue, StatsMemory(ram).PadLeft(memoryStatsWidth), Fraction(ram, ramTotal));

            if (sample is null)
            {
                foreach (var device in deviceCharts.Values)
                {
                    device.Gpu.AddSample("—", null, 100);
                    device.Vram.AddSample("—", null, null);
                    UpdateHardwareValue(device.Load, $"{Percent(null),6}", null);
                    UpdateHardwareValue(device.Memory, StatsMemory(null).PadLeft(memoryStatsWidth), null);
                }
                return;
            }

            var ids = sample.Devices.Select(device => device.Id).ToHashSet();
            foreach (var id in deviceCharts.Keys.Where(id => !ids.Contains(id)).ToArray())
            {
                var removed = deviceCharts[id];
                charts.Remove(removed.Name);
                charts.Remove(removed.Gpu);
                charts.Remove(removed.Vram);
                removed.Name.Dispose();
                removed.Gpu.Dispose();
                removed.Vram.Dispose();
                machine.Remove(removed.Stats);
                removed.Stats.Dispose();
                deviceCharts.Remove(id);
            }
            var row = 2;
            var statsRow = 0;
            var statsX = hostStatsWidth;
            foreach (var device in sample.Devices)
                if (!deviceNumbers.ContainsKey(device.Id)) deviceNumbers.Add(device.Id, deviceNumbers.Count);
            foreach (var device in sample.Devices.OrderBy(device => deviceNumbers[device.Id]))
            {
                if (!deviceCharts.TryGetValue(device.Id, out var views))
                {
                    var number = deviceNumbers[device.Id];
                    var gpuCaption = $"  GPU{number} ";
                    var vramCaption = $"  VRAM{number} ";
                    var width = gpuCaption.Length + 6 + vramCaption.Length + memoryStatsWidth;
                    var stats = new View { Width = width, Height = 1 };
                    var loadValue = AddHardwareField(stats, gpuCaption, 0, 6);
                    var memoryValue = AddHardwareField(stats, vramCaption, Pos.Right(loadValue), memoryStatsWidth);
                    views = (new Label { Width = Dim.Fill() },
                        new UsageChart($"GPU{number}", useColor) { Width = Dim.Percent(50) }, new UsageChart($"VRAM{number}", useColor),
                        stats, loadValue, memoryValue, width);
                    views.Vram.X = Pos.Right(views.Gpu);
                    deviceCharts.Add(device.Id, views);
                    charts.Add(views.Name, views.Gpu, views.Vram);
                    machine.Add(stats);
                }
                var confirmed = ComputeLabel(device.Id) is not null;
                views.Stats.Visible = confirmed;
                if (confirmed)
                {
                    if (statsX > 0 && statsX + views.StatsWidth > window.Viewport.Width)
                    {
                        statsRow++;
                        statsX = 0;
                    }
                    views.Stats.X = statsX;
                    views.Stats.Y = statsRow;
                    statsX += views.StatsWidth;
                }
                views.Name.Visible = views.Gpu.Visible = views.Vram.Visible = confirmed;
                views.Name.Text = $"GPU{deviceNumbers[device.Id]} · {device.Name}";
                views.Name.Y = row;
                views.Gpu.Y = row + 1;
                views.Vram.Y = row + 1;
                var load = Metric(device.Samples, "gpu.utilization");
                var used = Metric(device.Samples, "vram.used");
                var total = Metric(device.Samples, "vram.total");
                views.Gpu.AddSample(Percent(load), load, 100);
                var (vramUsed, vramSuffix) = MemoryPair(used, total);
                views.Vram.AddSample(vramUsed, used, total, vramSuffix);
                UpdateHardwareValue(views.Load, $"{Percent(load),6}", load / 100);
                UpdateHardwareValue(views.Memory, StatsMemory(used).PadLeft(memoryStatsWidth), Fraction(used, total));
                if (confirmed) row += 3;
            }
            visibleChartRows = row;
            if (machine.Frame.Height != statsRow + 1) machine.Height = statsRow + 1;
            if (charts.GetContentSize().Height != row || charts.GetContentSize().Width != window.Viewport.Width)
                charts.SetContentSize(new System.Drawing.Size(window.Viewport.Width, row));
            if (index == targets.Count) ShowSystem(sample);
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
                foreach (var device in sample.Devices.OrderBy(device => deviceNumbers[device.Id]))
                {
                    var number = deviceNumbers[device.Id];
                    var (used, suffix) = MemoryPair(Metric(device.Samples, "vram.used"), Metric(device.Samples, "vram.total"));
                    rows.Add($"  GPU{number} · {device.Name} · {ComputeLabel(device.Id) ?? "Cómputo sin confirmar"}");
                    rows.Add($"    VRAM{number} {used}{suffix}");
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
                logsButton.Visible = presetsButton.Visible = !systemSelected;
            selectedSnapshot = null;
            startButton.Enabled = stopButton.Enabled = false;
            startButton.Text = "Iniciar";
            lines.Visible = !systemSelected && showLogsPanel;
            entries.Visible = !systemSelected && !showLogsPanel;
            if (systemSelected)
            {
                ShowSystem(lastHardware);
                return;
            }
            status.Text = targets.Count == 0 ? diagnostic : "Consultando estado del servicio…";
            SetStatusColor(Palette.Information, ColorName16.BrightBlue);
            preferredEntry = inferenceEntry = null;
            inference = new();
            promptGraph.Clear();
            outputGraph.Clear();
            lastEntries = [];
            ShowInference([]);
            SetRows(entries, ref shownPresets, targets.Count == 0 ? "Los presets del servicio seleccionado aparecerán aquí." : "Esperando catálogo…");
            SetRows(lines, ref shownLogs, targets.Count == 0 ? "Los logs del servicio seleccionado aparecerán aquí." : "Consultando últimas líneas…");
        }

        void Show(ServicePanel panel)
        {
            var snapshot = panel.Snapshot;
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
            var presets = snapshot.Entries.Count == 0
                ? snapshot.ApiState == "available" ? "Sin presets publicados." : "Catálogo no disponible."
                : string.Join("\n", snapshot.Entries.Select(FormatEntry));
            var previousSelection = entries.SelectedItem;
            lastEntries = snapshot.Entries;
            refreshingEntries = true;
            SetRows(entries, ref shownPresets, presets);
            if (preferredEntry is not null)
            {
                var selected = snapshot.Entries.ToList().FindIndex(entry => entry.Name == preferredEntry);
                if (selected >= 0) entries.SelectedItem = selected;
            }
            else if (previousSelection is >= 0 && previousSelection < snapshot.Entries.Count)
                entries.SelectedItem = previousSelection;
            refreshingEntries = false;
            ShowInference(snapshot.Entries);
            SetRows(lines, ref shownLogs, panel.Logs);
        }

        void ShowInference(IReadOnlyList<EntrySnapshot> observed)
        {
            var entry = preferredEntry is null ? null : observed.FirstOrDefault(item => item.Name == preferredEntry);
            entry ??= observed.FirstOrDefault(item => item.Inference?.Active.Count > 0)
                ?? observed.FirstOrDefault(item => item.Name == inferenceEntry)
                ?? observed.FirstOrDefault(item => item.Inference is not null);
            if (entry is not null && entry.Name != inferenceEntry)
            {
                inferenceEntry = entry.Name;
                inference = new();
                promptGraph.Clear();
                outputGraph.Clear();
            }
            var source = entry?.Inference;
            var (prompt, output, reset) = inference.Observe(source);
            if (reset)
            {
                promptGraph.Clear();
                outputGraph.Clear();
            }
            var phase = source?.Active.Count switch
            {
                null => "señal /slots sin verificar",
                0 when inference.Last is { } last => $"sin tarea activa · último observado: {last.Prompt} tk P / {last.Decoded} tk G",
                0 => "sin tarea activa",
                1 when inference.Last is { Decoded: > 0 } task => $"generación · {task.Decoded} tk",
                1 when inference.Last is { Prompt: > 0 } task => $"prompt · {task.Prompt} tk · caché {task.Cached}",
                1 => "tarea activa · fase sin confirmar",
                _ => "varias tareas activas"
            };
            inferenceLabel.Text = $"Inferencia /slots · {entry?.Name ?? inferenceEntry ?? "—"} · {phase}";
            promptGraph.AddRate(prompt, inference.PromptMean, inference.PromptPeak, inference.PromptSeconds);
            outputGraph.AddRate(output, inference.OutputMean, inference.OutputPeak, inference.OutputSeconds);
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
        return $"{entry.Name} · {entry.State}   Prompt {prompt}   Generación {generation}";

        string Rate(string suffix) => entry.Metrics.FirstOrDefault(sample => sample.Name.EndsWith(suffix, StringComparison.Ordinal))?.Value
            is double rate ? rate.ToString("0.#", CultureInfo.InvariantCulture) + " tk/s" : "—";
    }

    private static double? Metric(IReadOnlyList<MetricSample>? samples, string name) =>
        samples?.FirstOrDefault(item => item.Name == name)?.Value;

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
