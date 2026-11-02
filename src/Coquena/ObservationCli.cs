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
            if (ps < 0 || args.Count(a => a == "ps") != 1 || args.Any(a => a is "-w" or "--watch"))
                throw new ArgumentException("ps muestra una instantánea finita; seguimiento aún no disponible.");
            var options = args.Where((_, i) => i != ps).ToArray();
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
                    var hardware = SystemMetrics.Create().CaptureAsync(cancellation.Token).GetAwaiter().GetResult();
                    WriteHardware(hardware, output);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception e) when (ServiceObservation.ObservationError(e)) { error.WriteLine("Hardware no disponible: " + e.Message); }
                if (!OperatingSystem.IsWindows())
                {
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
                else
                {
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
                foreach (var target in targets)
                {
                    var snapshot = new ServiceObservation(target.Store).CaptureAsync(target.Service, target.Selection, cancellation.Token).GetAwaiter().GetResult();
                    WriteService(snapshot, output);
                }
                if (targets.Count == 0) output.WriteLine("Sin servicios conocidos en la selección.");
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
        catch (Exception e) when (ServiceObservation.ObservationError(e)) { error.WriteLine("Error: " + e.Message); return 2; }
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
