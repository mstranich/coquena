using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Servers;

public sealed record ServerStartResult(InstanceDocument Instance, bool Started);

// The console callback only acknowledges input and changes a flag. Process
// operations remain in the command loop, with verified handles and cleanup.
public sealed class ServerInterrupt(TextWriter output)
{
    private int count;
    public int Count => Volatile.Read(ref count);
    public void Receive()
    {
        var presses = Interlocked.Increment(ref count);
        output.WriteLine(presses == 1
            ? "[Ctrl+C recibido] Deteniendo servidor; máximo 10 s. Pulse Ctrl+C otra vez para forzar el cierre inmediato."
            : "[Cierre forzado] Segundo Ctrl+C recibido; cerrando inmediatamente.");
        output.Flush();
    }
}

public sealed partial class ServerRuntime(Home home, LocalStore? storage = null)
{
    private readonly LocalStore store = storage ?? new(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private ServerManager Manager => new(home, store);

    private FileStream Lease(string server)
    {
        var directory = home.PathFor($"run/{server}"); Directory.CreateDirectory(directory);
        try { return new(home.PathFor($"run/{server}/lifecycle.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new ConfigurationException("Otro comando está iniciando o deteniendo este servidor; no se duplica la ejecución."); }
    }
    private InstanceDocument? Read(string server) => store.Read(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, server: server);
    private void Paths(string server, InstanceDocument instance)
    {
        if (instance.IniPath != $"run/{server}/{instance.InstanceId:D}.ini" || instance.LogPath != $"logs/{server}/{instance.InstanceId:D}.log")
            throw new ConfigurationException($"Identidad/rutas inciertas de instancia {instance.InstanceId:D}; no se limpia.");
        _ = home.PathFor(instance.IniPath); _ = home.PathFor(instance.LogPath);
    }
    private void Clean(string server, InstanceDocument instance, TextWriter? output = null)
    {
        Paths(server, instance);
        // Deleting INI first makes an interrupted cleanup retryable from the record.
        RememberLog(server, instance);
        File.Delete(home.PathFor(instance.IniPath));
        File.Delete(home.PathFor($"run/{server}/instance.json"));
        RetainLogs(server, null, output ?? TextWriter.Null);
    }
    internal InstanceDocument? RecoverLocked(string server)
    {
        var instance = Read(server);
        if (instance is null) return null;
        Paths(server, instance);
        using var process = WindowsServerProcess.Verified(instance, out var ended);
        if (ended) { Clean(server, instance); return null; }
        return instance;
    }
    public InstanceDocument? Recover(string server) => store.Exclusive(() => RecoverLocked(server));

    public ServerStartResult Start(string? name, bool detached, TextWriter output)
    {
        var server = store.Exclusive(() => Manager.Named(name));
        using var lease = Lease(server.Name);
        return StartLocked(server.Name, detached, output);
    }
    private ServerStartResult StartLocked(string name, bool detached, TextWriter output) => store.Exclusive(() =>
    {
        var existing = RecoverLocked(name);
        if (existing is not null)
        {
            output.WriteLine($"Servidor ya ejecutándose; instancia {existing.InstanceId:D} (PID {existing.Pid}).");
            return new ServerStartResult(existing, false);
        }
        var plan = Manager.ComposeLocked(name);
        Coquena.Models.ModelInstaller.CheckUpgradeReservations(home, store, plan.Presets.ResourceIds);
        RetainLogs(name, null, output);
        ProbePort(plan.Parameters["host"].GetString()!, plan.Parameters["port"].GetInt32());
        var id = Guid.NewGuid();
        var ini = $"run/{name}/{id:D}.ini"; var log = $"logs/{name}/{id:D}.log";
        Directory.CreateDirectory(Path.GetDirectoryName(home.PathFor(log))!);
        using (var file = new FileStream(home.PathFor(ini), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            file.Write(Encoding.UTF8.GetBytes(plan.Presets.Ini)); file.Flush(true);
        }
        WindowsServerProcess? child = null;
        var published = false;
        try
        {
            var arguments = Arguments(plan, home.PathFor(ini));
            child = WindowsServerProcess.Create(new Coquena.Backends.BackendPaths(home).Server(plan.Backend), arguments, home.PathFor(log), id);
            var instance = new InstanceDocument(1, id, plan.Server.Id, plan.Backend.Id, plan.Backend.Build, child.Pid,
                detached ? "detached" : "foreground", plan.Parameters["host"].GetString()!, plan.Parameters["port"].GetInt32(),
                plan.Parameters["metrics"].GetBoolean(), plan.Server.PresetIds, plan.Presets.ResourceIds, ini, log, child.Created, child.Executable);
            store.UpdateLocked(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, _ => instance, server: name);
            published = true;
            RememberLog(name, instance);
            child.Activate();
            foreach (var diagnostic in plan.Presets.Diagnostics) output.WriteLine($"Diagnóstico: {diagnostic}");
            output.WriteLine($"Instancia {id:D}; servidor {name}; PID {child.Pid}; {instance.Host}:{instance.Port}; {instance.Mode}.");
            output.Flush();
            return new ServerStartResult(instance, true);
        }
        catch
        {
            // Closing the staging job kills the child, including failures after a
            // durable write with incomplete audit. Keep that record for recovery.
            child?.Dispose(); child = null;
            if (!published && Read(name)?.InstanceId != id) File.Delete(home.PathFor(ini));
            throw;
        }
        finally { child?.Dispose(); }
    });
    public static string[] Arguments(ServerStartPlan plan, string ini)
    {
        var arguments = new List<string> { "--models-preset", ini };
        foreach (var pair in plan.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (pair.Key == "metrics") { if (pair.Value.GetBoolean()) arguments.Add("--metrics"); }
            else if (pair.Key == "models-autoload" && ParameterCatalog.Find(pair.Key, plan.Definitions)?.Type == "boolean")
            { if (!pair.Value.GetBoolean()) arguments.Add("--no-models-autoload"); }
            else if (ParameterCatalog.Find(pair.Key, plan.Definitions)?.Type == "flag") arguments.Add("--" + pair.Key);
            else { arguments.Add("--" + pair.Key); arguments.Add(pair.Value.ToString()); }
        }
        arguments.AddRange(plan.Presets.CliArguments ?? []);
        return arguments.ToArray();
    }
    private static void ProbePort(string host, int port)
    {
        IPAddress[] addresses;
        try { addresses = IPAddress.TryParse(host, out var address) ? [address] : Dns.GetHostAddresses(host); }
        catch (SocketException e) { throw new ConfigurationException($"Host inválido: {e.Message}"); }
        if (addresses.Length == 0) throw new ConfigurationException("Host sin direcciones de escucha.");
        var sockets = new List<Socket>();
        try
        {
            foreach (var address in addresses.Distinct())
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
                sockets.Add(socket);
                if (address.AddressFamily == AddressFamily.InterNetworkV6) socket.DualMode = false;
                socket.Bind(new IPEndPoint(address, port));
            }
        }
        catch (SocketException e) { throw new ConfigurationException($"Puerto {port} ocupado o escucha no disponible en {host}: {e.Message}; no se reasigna."); }
        finally { foreach (var socket in sockets) socket.Dispose(); }
    }
    private (string Server, InstanceDocument Instance) Find(Guid id) => store.Exclusive(() =>
    {
        foreach (var directory in Directory.GetDirectories(home.PathFor("run")))
        {
            Home.CheckLinks(directory); var name = Path.GetFileName(directory);
            var instance = Read(name);
            if (instance?.InstanceId == id) { Paths(name, instance); return (name, instance); }
        }
        throw new ConfigurationException($"Instancia ausente: {id:D}.");
    });
    public void Stop(Guid id, TextWriter output, ServerInterrupt? interrupt = null)
    {
        var found = Find(id);
        using var lease = Lease(found.Server);
        StopLocked(found.Server, id, output, interrupt);
    }
    public void StopServer(string? name, TextWriter output, ServerInterrupt? interrupt = null)
    {
        // Do not initialize or recreate a default merely to stop a server. Resolve
        // its current designation, then retain the lifecycle lease through stop.
        var server = store.Exclusive(() => Manager.Named(name));
        using var lease = Lease(server.Name);
        var instance = store.Exclusive(() => RecoverLocked(server.Name));
        if (instance is null)
        {
            store.Exclusive(() => { RetainLogs(server.Name, null, output); return true; });
            output.WriteLine($"[Detenido] Servidor {server.Name}: sin instancia activa.");
            output.Flush(); return;
        }
        StopLocked(server.Name, instance.InstanceId, output, interrupt);
    }
    private void StopLocked(string server, Guid id, TextWriter output, ServerInterrupt? interrupt = null)
    {
        var instance = store.Exclusive(() =>
        {
            var record = Read(server);
            if (record?.InstanceId != id) throw new ConfigurationException("La instancia cambió; no se detiene un sustituto.");
            Paths(server, record); return record;
        });
        using var process = WindowsServerProcess.Verified(instance, out var ended);
        if (!ended)
        {
            output.WriteLine($"[Deteniendo] Instancia {id:D}; espera máxima 10 s."); output.Flush();
            var watch = Stopwatch.StartNew();
            using var signal = interrupt?.Count >= 2 ? null : LaunchSignal(instance);
            var forced = false;
            while (!WindowsServerProcess.Exited(process!))
            {
                if (interrupt?.Count >= 2 || watch.ElapsedMilliseconds >= 10000)
                {
                    output.WriteLine("[Cierre forzado] Terminando el grupo de procesos de la instancia."); output.Flush();
                    WindowsServerProcess.Force(instance, process!); forced = true; break;
                }
                WindowsServerProcess.Exited(process!, 25);
            }
            if (forced && !WindowsServerProcess.Exited(process!, 3000))
                throw new ConfigurationException("El cierre no pudo confirmarse; registro e INI conservados.");
            if (signal is not null)
            {
                if (!signal.HasExited) signal.Kill();
                signal.WaitForExit();
            }
        }
        store.Exclusive(() =>
        {
            if (Read(server)?.InstanceId == id) Clean(server, instance, output);
            return true;
        });
        output.WriteLine($"[Detenido] Instancia {id:D}; log conservado."); output.Flush();
    }
    private Process LaunchSignal(InstanceDocument instance)
    {
        var start = Self("--internal-server-signal", instance.InstanceId.ToString("D"));
        start.Environment["COQUENA_HOME"] = home.Root;
        return Process.Start(start) ?? throw new ConfigurationException("No se pudo solicitar la parada ordenada.");
    }
    public static ProcessStartInfo Self(params string[] args)
    {
        var executable = Environment.ProcessPath ?? throw new ConfigurationException("Ejecutable de Coquena ausente.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "coquena.dll"));
        foreach (var argument in args) start.ArgumentList.Add(argument);
        return start;
    }
    public static int SignalWorker(Home home, Guid id)
    {
        var runtime = new ServerRuntime(home);
        var found = runtime.Find(id);
        return WindowsServerProcess.Signal(found.Instance);
    }
    public InstanceDocument Restart(string? name, TextWriter output, ServerInterrupt? interrupt = null)
    {
        var server = store.Exclusive(() => Manager.Named(name));
        using var lease = Lease(server.Name);
        var previous = store.Exclusive(() => RecoverLocked(server.Name));
        if (previous is not null) StopLocked(server.Name, previous.InstanceId, output, interrupt);
        return StartLocked(server.Name, true, output).Instance;
    }
    public int Foreground(InstanceDocument instance, TextWriter output, ServerInterrupt interrupt)
    {
        var found = Find(instance.InstanceId);
        using var process = WindowsServerProcess.Verified(instance, out var ended);
        using var stream = new FileStream(home.PathFor(instance.LogPath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096);
        var buffer = new char[4096];
        output.WriteLine("[Primer plano] Ctrl+C: parada ordenada (10 s); segundo Ctrl+C: cierre forzado inmediato."); output.Flush();
        while (!ended && !WindowsServerProcess.Exited(process!))
        {
            Drain();
            if (interrupt.Count > 0)
            {
                Stop(instance.InstanceId, output, interrupt); Drain(int.MaxValue); return 0;
            }
            WindowsServerProcess.Exited(process!, 25);
        }
        Drain(int.MaxValue);
        var code = process is null ? 0 : WindowsServerProcess.ExitCode(process);
        store.Exclusive(() => { if (Read(found.Server)?.InstanceId == instance.InstanceId) Clean(found.Server, instance, output); return true; });
        output.WriteLine($"[Finalizado] Instancia {instance.InstanceId:D}; salida {code}; log conservado.");
        return code == 0 ? 0 : 1;

        void Drain(int chunks = 16)
        {
            int read;
            while (chunks-- > 0 && (read = reader.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
            output.Flush();
        }
    }
}
