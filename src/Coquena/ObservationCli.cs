using System.Globalization;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena;

public static class ObservationCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            var ps = Array.IndexOf(args, "ps");
            if (ps < 0 || args.Count(a => a == "ps") != 1 || args.Count(a => a is "-w" or "--watch") > 1)
                throw new ArgumentException("Use ps [-w|--watch] con una selección de servicio válida.");
            var watch = args.Any(a => a is "-w" or "--watch");
            var options = args.Where((a, i) => i != ps && a is not ("-w" or "--watch")).ToArray();
            // Use the same selector and recipe rules as service logs, without
            // its reconciliation, retention or index writes.
            var command = ServiceCommand.Parse([.. options, "logs"]);
            var selected = options.Any(a => a == "--service") || options.Any(a => !a.StartsWith('-')) &&
                command.Selector != "default";
            selected |= options.Any(a => a == "default");
            var explicitRecipe = command.Files.Count != 0 || command.BasePath is not null || command.EnvFiles.Count != 0;
            if (explicitRecipe && !selected) command = command with { Selector = "self" };
            selected |= explicitRecipe;
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                try
                {
                    if (!watch)
                    {
                        var hardware = SystemMetrics.Create().CaptureAsync(cancellation.Token).GetAwaiter().GetResult();
                        WriteCompactHardware(hardware, output);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception e) when (ServiceObservation.ObservationError(e)) { error.WriteLine("Hardware no disponible: " + e.Message); }
                if (!OperatingSystem.IsWindows())
                {
                    if (watch) return WatchAsync([], output, cancellation.Token).GetAwaiter().GetResult();
                    output.WriteLine("Servicios: identidad persistida sólo soportada en Windows; no se consultan PID/API desde Linux.");
                    return 0;
                }
                var home = Home.Resolve(); var targets = new List<(ScopeStore Store, ServiceIdentity Service, RecipeSelection? Selection)>();
                var discovered = new HashSet<DirectoryIdentity>();
                if (selected)
                {
                    var target = ScopedServiceCli.Resolve(command, home);
                    targets.Add((new(target.Context), target.Service, explicitRecipe ? target.Selection : null));
                }
                else RefreshTargets();
                if (watch) return WatchAsync(targets.ToArray(), output, cancellation.Token, selected ? null : () =>
                {
                    RefreshTargets(); return targets.ToArray();
                }).GetAwaiter().GetResult();
                var shown = false;
                foreach (var target in targets)
                {
                    var snapshot = new ServiceObservation(target.Store).CaptureAsync(target.Service, target.Selection, cancellation.Token).GetAwaiter().GetResult();
                    shown = true;
                    output.WriteLine($"Servicio {snapshot.Name}{(snapshot.IsDefault ? " (default)" : "")} | {snapshot.ProcessState} | {snapshot.Health}");
                    var metrics = snapshot.Metrics.Where(m => m.Value is not null && m.Name is "cpu.utilization" or "ram.private-commit").Select(CompactSample).ToArray();
                    if (metrics.Length != 0) output.WriteLine("  " + string.Join(" | ", metrics));
                    foreach (var entry in snapshot.Entries)
                        output.WriteLine($"  {entry.Name} | {entry.State switch { "loaded" => "cargado", "unloaded" => "publicado, sin cargar", _ => entry.State }}");
                    if (snapshot.Entries.Count == 0 && snapshot.ApiState == "available") output.WriteLine("  Sin presets publicados.");
                }
                if (!shown) output.WriteLine("Sin servicios conocidos en la selección.");
                void RefreshTargets()
                {
                    targets.Clear(); discovered.Clear();
                    Discover(new ScopeStore(ScopeContext.Global(home)));
                    if (!command.Global)
                    {
                        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var locations = new List<string>();
                        try { locations.AddRange(new ProjectIndex(home).Locations()); }
                        catch (Exception e) when (ServiceObservation.ObservationError(e)) { error.WriteLine("Índice de proyectos: " + e.Message); }
                        locations.Add(Environment.CurrentDirectory);
                        foreach (var location in locations)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            try
                            {
                                if (!seen.Add(Path.GetFullPath(location)) || !Directory.Exists(Path.Combine(location, ".coquena"))) continue;
                                Discover(new ScopeStore(ScopeContext.Project(Path.Combine(location, "coquena.yaml"))));
                            }
                            catch (Exception e) when (ServiceObservation.ObservationError(e)) { error.WriteLine($"Proyecto {location}: {e.Message}"); }
                        }
                    }
                }
                void Discover(ScopeStore store)
                {
                    try
                    {
                        // Reject copied scopes before using the navigation index.
                        _ = store.ReadVerified();
                        if (!discovered.Add(store.Context.PhysicalBase)) return;
                        foreach (var service in new RecipeRegistry(store).Services()) targets.Add((store, service, null));
                    }
                    catch (Exception e) when (ServiceObservation.ObservationError(e)) { error.WriteLine($"Ámbito {store.Context.Base}: {e.Message}"); }
                }
                return 0;
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        catch (OperationCanceledException) { return 130; }
        catch (Exception e) when (ServiceObservation.ObservationError(e)) { error.WriteLine("Error: " + e.Message + " Consulte coquena --help."); return 2; }
    }

    public static async Task<int> WatchAsync(
        IReadOnlyList<(ScopeStore Store, ServiceIdentity Service, RecipeSelection? Selection)> targets,
        TextWriter output, CancellationToken cancellation,
        Func<IReadOnlyList<(ScopeStore Store, ServiceIdentity Service, RecipeSelection? Selection)>>? discover = null)
    {
        var preferences = new Coquena.Configuration.ConfigurationManager(Home.Resolve()).Observer();
        var latest = new Dictionary<string, WatchSnapshot?>();
        HardwareSnapshot? hardware = null; string? hardwareError = null;
        var sync = new object();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var budget = new SemaphoreSlim(4);
        var jobs = new List<Task>();
        Add(targets);
        jobs.Add(Hardware());
        var discoveryClock = System.Diagnostics.Stopwatch.StartNew();
        var interactive = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected;
        if (interactive) output.Write("\u001b[?1049h\u001b[?25l");
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            do
            {
                cancellation.ThrowIfCancellationRequested();
                if (discover is not null && discoveryClock.Elapsed >= TimeSpan.FromSeconds(5))
                { Add(discover()); discoveryClock.Restart(); }
                using var frame = new StringWriter(CultureInfo.InvariantCulture);
                frame.WriteLine($"Observación | Ctrl+C: cerrar observador | {DateTimeOffset.UtcNow:O}");
                lock (sync)
                {
                    if (hardware is not null)
                    {
                        if (interactive) WriteCompactHardware(hardware, frame);
                        else WriteHardware(hardware, frame);
                    }
                    else frame.WriteLine("Hardware: " + (hardwareError ?? "esperando muestra"));
                    foreach (var sample in latest.Values)
                    {
                        if (sample is null) { frame.WriteLine("Servicio: esperando muestra"); continue; }
                        if (interactive && sample.Snapshot is { } snapshot)
                        {
                            frame.WriteLine($"{snapshot.Name} | {snapshot.CompletedAt:HH:mm:ss} | proceso {snapshot.ProcessState} | API {snapshot.ApiState} | salud {snapshot.Health}");
                            frame.WriteLine($"  Instancia {snapshot.InstanceId ?? "ninguna"}");
                            frame.WriteLine("  Eventos: " + sample.Events);
                            frame.WriteLine("  " + string.Join("; ", snapshot.Metrics.Where(m => m.Name is "cpu.utilization" or "ram.private-commit").Select(CompactSample)));
                            foreach (var entry in snapshot.Entries)
                                frame.WriteLine($"  {entry.Name}: {entry.State} | catálogo {entry.ObservedAt:HH:mm:ss} | " + string.Join("; ", entry.Metrics.Where(m => m.Unit.Contains("tokens/s")).Select(CompactSample)));
                        }
                        else
                        {
                            frame.WriteLine("Eventos: " + sample.Events);
                            if (sample.Snapshot is not null) WriteService(sample.Snapshot, frame);
                        }
                        if (sample.Transition is not null) frame.WriteLine(sample.Transition);
                    }
                }
                if (latest.Count == 0) frame.WriteLine("Sin servicios conocidos en la selección.");
                if (interactive)
                {
                    output.Write("\u001b[H\u001b[2J");
                    // Avoid scrolling the terminal; the full snapshot remains available when redirected.
                    var width = Math.Max(1, Console.WindowWidth - 1); var height = Math.Max(1, Console.WindowHeight - 1);
                    foreach (var line in frame.ToString().Split('\n').Take(height))
                        output.WriteLine(line.TrimEnd('\r') is var text && text.Length > width ? text[..width] : text);
                }
                else { output.Write(frame.ToString()); output.WriteLine(); }
                output.Flush();
                if (jobs.Any(t => t.IsFaulted)) await Task.WhenAll(jobs.Where(t => t.IsFaulted));
            } while (await timer.WaitForNextTickAsync(cancellation));
            return 0;
        }
        finally
        {
            await stop.CancelAsync();
            try { await Task.WhenAll(jobs); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally { if (interactive) { output.Write("\u001b[?25h\u001b[?1049l"); output.Flush(); } }
        }

        void Add(IReadOnlyList<(ScopeStore Store, ServiceIdentity Service, RecipeSelection? Selection)> found)
        {
            foreach (var target in found)
            {
                var key = target.Store.Context.PhysicalBase + "/" + target.Service.ServiceId;
                lock (sync)
                {
                    if (latest.ContainsKey(key)) continue;
                    latest[key] = null;
                }
                jobs.Add(new ServiceWatch(target.Store, target.Service, target.Selection, preferences)
                    .RunAsync(sample =>
                    {
                        lock (sync) latest[key] = sample with { Transition = sample.Transition ?? latest[key]?.Transition };
                    }, stop.Token, budget));
            }
        }

        async Task Hardware()
        {
            var source = SystemMetrics.Create();
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var sample = await source.CaptureAsync(stop.Token);
                    lock (sync) { hardware = sample; hardwareError = null; }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                catch (Exception e) when (ServiceObservation.ObservationError(e))
                { lock (sync) { hardware = null; hardwareError = "no disponible: " + e.GetType().Name; } }
                await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
            }
        }
    }

    private static string CompactSample(MetricSample sample)
    {
        var value = sample.Value is not double number ? "N/D: " + sample.UnavailableReason :
            sample.Unit == "bytes" && number <= long.MaxValue ? ByteSize.Format((long)number) : number.ToString("0.##", CultureInfo.InvariantCulture) + " " + sample.Unit;
        return $"{sample.Name} {value}";
    }
    private static void WriteCompactHardware(HardwareSnapshot hardware, TextWriter output)
    {
        output.WriteLine($"Hardware {hardware.Environment} | {hardware.CompletedAt:HH:mm:ss} | alcance máquina");
        output.WriteLine("  " + string.Join("; ", hardware.Samples.Where(m => m.Value is not null).Select(CompactSample)));
        foreach (var device in hardware.Devices.Where(d => d.Samples.Any(m => m.Value > 0)))
            output.WriteLine($"  GPU {device.Name}: " + string.Join("; ", device.Samples.Where(m => m.Value is not null).Select(CompactSample)));
    }

    public static void WriteHardware(HardwareSnapshot snapshot, TextWriter output)
    {
        output.WriteLine($"Hardware | {snapshot.Environment} | {snapshot.StartedAt:O} — {snapshot.CompletedAt:O}");
        foreach (var sample in snapshot.Samples) WriteSample(sample, output);
        foreach (var device in snapshot.Devices)
        {
            output.WriteLine($"GPU {device.Name} | {device.Id} | compartida por la máquina");
            foreach (var sample in device.Samples) WriteSample(sample, output);
        }
        foreach (var diagnostic in snapshot.Diagnostics) output.WriteLine("  Diagnóstico: " + diagnostic);
    }
    public static void WriteService(ServiceSnapshot snapshot, TextWriter output)
    {
        output.WriteLine($"Servicio {snapshot.Name}{(snapshot.IsDefault ? " (default)" : "")} | {snapshot.ServiceId} | {snapshot.Scope} | instancia {snapshot.InstanceId ?? "ninguna"}");
        output.WriteLine($"  {snapshot.StartedAt:O} — {snapshot.CompletedAt:O} | proceso {snapshot.ProcessState} | API {snapshot.ApiState} | salud {snapshot.Health}");
        output.WriteLine($"  Intención: {snapshot.IntentComparison}; recuperación: {snapshot.Recovery}; composición confirmada en API: {snapshot.CompositionCurrent}");
        if (snapshot.CurrentIntention is { } intention)
            output.WriteLine("  Presets en intención actual de la receta: " + string.Join(", ", (intention["presets"] as System.Text.Json.Nodes.JsonObject ?? []).Select(p => p.Key)));
        if (snapshot.ActiveComposition is { } active)
        {
            output.WriteLine($"  Composición registrada de ejecución: {active["combination"]}; borrador: {active["draft"]}");
            var names = (active["preset-composition"] as System.Text.Json.Nodes.JsonArray ?? [])
                .SelectMany(c => c!["presets"]!.AsArray()).Select(p => p!["name"]!.GetValue<string>());
            output.WriteLine("  Entradas registradas: " + string.Join(", ", names));
        }
        foreach (var sample in snapshot.Metrics) WriteSample(sample, output);
        foreach (var entry in snapshot.Entries)
        {
            output.WriteLine($"  Entrada {entry.Name}: {entry.State} | {entry.ObservedAt:O} | {entry.Diagnostic}");
            foreach (var sample in entry.Metrics) WriteSample(sample, output);
        }
        foreach (var diagnostic in snapshot.Diagnostics) output.WriteLine("  Diagnóstico: " + diagnostic);
    }
    private static void WriteSample(MetricSample sample, TextWriter output)
    {
        var value = sample.Value is not double number ? "no disponible: " + sample.UnavailableReason :
            sample.Unit == "bytes" && number <= long.MaxValue ? ByteSize.Format((long)number) : number.ToString("0.##", CultureInfo.InvariantCulture) + " " + sample.Unit;
        output.WriteLine($"  {sample.Name}: {value} | fuente {sample.Source} | alcance {sample.Scope} | {sample.ObservedAt:O}" +
            (sample.Window is { } window ? $" | ventana {window.TotalMilliseconds:0} ms" : " | ventana no informada/puntual"));
    }
}
