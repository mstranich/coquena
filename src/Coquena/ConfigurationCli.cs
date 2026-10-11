using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena;

public static class ConfigurationCli
{
    public static int Run(string command, string[] args, TextWriter output, TextWriter error, ConfigurationManager? manager = null,
        Func<string?>? directoryChoice = null, Func<bool>? directoryOverwrite = null)
    {
        try
        {
            if (command == "param") return ParameterCli.Run(args, output, error,
                manager?.Parameters, manager);
            // Check grammar before creating the home or modifying state.
            if (command == "config")
            {
                if (args.Length >= 1 && args[0] == "backends-dir")
                {
                    if (!(args.Length == 2 && args[1] is "show" or "unset" || args.Length == 3 && args[1] == "set")) Invalid();
                    manager ??= new(Home.Resolve());
                    if (args[1] != "show") manager.BackendDirectory.Change(args[1] == "set" ? args[2] : null, output,
                        directoryChoice ?? (() => Console.IsInputRedirected ? null : Console.ReadLine()),
                        directoryOverwrite ?? (() => !Console.IsInputRedirected && Console.ReadLine()?.Trim().ToLowerInvariant() is "s" or "si" or "sí"));
                    var view = manager.BackendDirectory.Show();
                    output.WriteLine($"{view.EffectivePath} | {(view.Own ? "propio" : "default")} | ubicación: {view.Location}");
                    return 0;
                }
                if (args.Length < 2 || args[0] != "log-retention" ||
                    !(args[1] is "show" or "unset" && args.Length == 2 || args[1] == "set" && args.Length == 3)) Invalid();
                long? bytes = args[1] == "set" ? LogRetentionPolicy.Parse(args[2]) : null;
                manager ??= new(Home.Resolve());
                if (args[1] != "show") manager.SetRetention(bytes);
                LogRetentionPolicy.Print(manager.Retention(), output);
                return 0;
            }
            if (command == "preset")
            {
                var valid = args.Length == 3 && args[0] == "create" || args.Length == 1 && args[0] == "list" ||
                    args.Length == 3 && args[0] == "list" && args[1] == "--model" ||
                    args.Length == 2 && args[0] is "show" or "remove" ||
                    args.Length == 4 && args[0] == "model" && args[1] == "set" ||
                    args.Length == 4 && args[0] == "set" || args.Length == 3 && args[0] == "unset";
                if (!valid) Invalid();
                manager ??= new(Home.Resolve());
                switch (args[0])
                {
                    case "set":
                    case "unset":
                        manager.Set(args[1], args[2], args[0] == "set" ? args[3] : null);
                        output.WriteLine("Valores propios del preset actualizados.");
                        break;
                    case "create":
                        var created = manager.Create(args[1], args[2]);
                        output.WriteLine($"Preset {created.Name} | {created.Id:D} | modelId {created.ModelId:D}");
                        break;
                    case "model": manager.ChangeModel(args[2], args[3]); output.WriteLine("Modelo del preset actualizado; valores propios conservados."); break;
                    case "remove": manager.Remove(args[1]); output.WriteLine("Preset retirado; referencias existentes conservan su ID."); break;
                    case "show":
                        var view = manager.View(args[1]);
                        ShowPreset(view, output);
                        ShowValues(view.Effective, view.Own, manager.DefaultBackend(), manager.Parameters.List(), output);
                        break;
                    case "list":
                        var entries = manager.List(args.Length == 3 ? args[2] : null);
                        foreach (var entry in entries) ShowPreset(entry, output);
                        if (entries.Length == 0) output.WriteLine("Sin presets.");
                        break;
                }
                return 0;
            }
            Invalid();
            return 2;
        }
        catch (Exception exception) when (exception is ConfigurationException or ContractException or StorageException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error.WriteLine($"Error: {exception.Message} Consulte coquena --help.");
            return 2;
        }
    }

    private static void Invalid() => throw new ConfigurationException("Argumentos inválidos.");
    private static void ShowPreset(PresetView view, TextWriter output)
    {
        output.WriteLine($"{view.Preset.Id:D} | {view.Preset.Name} | modelo {view.Model?.Name ?? "ausente"} | modelId {view.Preset.ModelId:D}");
        foreach (var diagnostic in view.Diagnostics) output.WriteLine($"  {diagnostic}");
    }
    private static void ShowValues(Dictionary<string, string> effective, Dictionary<string, string> own, BackendRecord? backend, ParameterDefinition[] definitions, TextWriter output)
    {
        foreach (var pair in effective.OrderBy(p => p.Key, StringComparer.Ordinal))
            output.WriteLine($"  {pair.Key} = {pair.Value} | {(own.ContainsKey(pair.Key) ? "propio" : "heredado")} | {ParameterManager.Availability(ParameterCatalog.Find(pair.Key, definitions), backend)}");
        if (effective.Count == 0) output.WriteLine("  Sin valores.");
    }
}
