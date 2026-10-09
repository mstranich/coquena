using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Backends;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena;

public static class ServerCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error, ServerManager? manager = null)
    {
        // Parse the entire grammar before reading/initializing any catalog.
        string? name = null;
        if (args.Length == 0) return Invalid();
        var action = args[0]; var rest = args[1..];
        if (action == "default" && rest.Length > 0 && ServerManager.Actions.Contains(rest[0]))
        { name = "default"; action = rest[0]; rest = rest[1..]; }
        else if (!ServerManager.Reserved.Contains(action))
        {
            name = action;
            if (rest.Length == 0 || !ServerManager.Actions.Contains(rest[0])) return Invalid();
            action = rest[0]; rest = rest[1..];
        }
        var valid = action switch
        {
            "create" => rest.Length == 1 || rest.Length == 3 && rest[1] == "--port" && int.TryParse(rest[2], out _),
            "default" => rest.Length <= 1 && (rest.Length == 0 || !ServerManager.Reserved.Contains(rest[0]) || rest[0] == "default"),
            "list" or "show" or "ps" => rest.Length == 0,
            "logs" => rest.Length == 0 || rest.Length == 1 && rest[0] == "-f",
            "start" => rest.Length == 0 || rest.Length == 1 && rest[0] == "-d",
            "restart" => rest.Length == 0,
            "stop" => rest.Length == 0 || name is null && rest.Length == 2 && rest[0] == "--id" && Guid.TryParse(rest[1], out var id) && id != Guid.Empty,
            "use" or "unset" => rest.Length == 1,
            "set" => rest.Length == 2,
            "preset" => rest.Length >= 1 && (rest[0] == "list" ? rest.Length == 1 : rest[0] is "add" or "remove" && rest.Length >= 2),
            _ => false
        };
        if (!valid) return Invalid();
        try
        {
            var home = Home.Resolve();
            manager ??= new ServerManager(home);
            if (action == "ps") { new ServerRuntime(home).Ps(output); return 0; }
            if (action == "logs")
            {
                var cancelled = 0;
                ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; Interlocked.Exchange(ref cancelled, 1); };
                Console.CancelKeyPress += handler;
                try { return new ServerRuntime(home).Logs(name, rest.Length == 1, output, () => Volatile.Read(ref cancelled) != 0); }
                finally { Console.CancelKeyPress -= handler; }
            }
            if (action is "start" or "stop" or "restart")
            {
                var runtime = new ServerRuntime(home);
                output = TextWriter.Synchronized(output);
                var interrupt = new ServerInterrupt(output);
                ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; interrupt.Receive(); };
                Console.CancelKeyPress += handler;
                try
                {
                    if (action == "stop")
                    {
                        if (rest.Length == 0) runtime.StopServer(name, output, interrupt);
                        else runtime.Stop(Guid.Parse(rest[1]), output, interrupt);
                        return 0;
                    }
                    manager.EnsureInitial();
                    if (action == "restart") { runtime.Restart(name, output, interrupt); return 0; }
                    var result = runtime.Start(name, rest.Length == 1, output);
                    return result.Started && rest.Length == 0 ? runtime.Foreground(result.Instance, output, interrupt) : 0;
                }
                finally { Console.CancelKeyPress -= handler; }
            }
            if (action != "create" || !rest[0].Equals("default", StringComparison.OrdinalIgnoreCase)) manager.EnsureInitial();
            switch (action)
            {
                case "create": Print(manager.Create(rest[0], rest.Length == 3 ? int.Parse(rest[2]) : null)); break;
                case "default":
                    var selected = manager.Default(rest.Length == 0 ? null : rest[0]);
                    output.WriteLine(selected is null ? "Sin servidor predeterminado." : $"Servidor predeterminado: {selected.Name}"); break;
                case "use": Print(manager.Use(name, rest[0])); break;
                case "set": Print(manager.Set(name, rest[0], rest[1])); break;
                case "unset": Print(manager.Set(name, rest[0], null)); break;
                case "preset":
                    if (rest[0] == "list") Show(manager.View(name), presetsOnly: true);
                    else Print(manager.Presets(name, rest[1..], rest[0] == "add"));
                    break;
                case "show": Show(manager.View(name)); break;
                case "list": foreach (var view in manager.List()) Print(view.Server); break;
            }
            return 0;
        }
        catch (Exception e) when (e is ConfigurationException or DownloadException or BackendException or StorageException or ContractException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { error.WriteLine($"Error: {e.Message} Consulte coquena --help."); return 1; }

        int Invalid() { error.WriteLine("Error: comando server o argumentos no reconocidos. Consulte coquena --help."); return 2; }
        void Print(ServerRecord record) => output.WriteLine($"Servidor {record.Name} ({record.Id:D}); puerto {record.Parameters["port"].GetInt32()}.");
        void Show(ServerView view, bool presetsOnly = false)
        {
            if (!presetsOnly)
            {
                Print(view.Server);
                output.WriteLine($"Backend {(view.Server.Backend is null ? "heredado" : "propio")}: {view.Selection?.Kind ?? "ausente"} {view.Selection?.Origin}/{view.Selection?.Variant} {view.Selection?.BackendId}");
                if (view.Backend is { } backend) output.WriteLine($"Build instalado: {backend.Build} ({backend.Id:D})");
                foreach (var pair in view.Effective.OrderBy(p => p.Key)) output.WriteLine($"{pair.Key} = {pair.Value}");
            }
            for (var i = 0; i < view.Presets.Length; i++) output.WriteLine($"Preset: {view.Presets[i]?.Name ?? "ausente"} ({view.Server.PresetIds[i]:D})");
            foreach (var diagnostic in view.Diagnostics) output.WriteLine($"Diagnóstico: {diagnostic}");
        }
    }
}
