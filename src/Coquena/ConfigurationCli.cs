using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena;

public static class ConfigurationCli
{
    public static int Run(string command, string[] args, TextWriter output, TextWriter error, ConfigurationManager? manager = null,
        Func<string?>? directoryChoice = null, Func<bool>? directoryOverwrite = null, Func<bool>? parameterConfirmation = null)
    {
        try
        {
            if (command == "param") return ParameterCli.Run(args, output, error,
                manager?.Parameters, manager);
            // Check grammar before creating the home or modifying state.
            if (command == "config")
            {
                if (args is ["language", ..]) return Language(args, output, error, manager);
                if (args is ["inference-history", ..])
                {
                    int? limit = null;
                    if (args is ["inference-history", "set", var count])
                    {
                        if (!int.TryParse(count, out var parsed) || parsed is < 0 or > 10000) Invalid();
                        limit = parsed;
                    }
                    else if (args is not (["inference-history", "show"] or ["inference-history", "unset"])) Invalid();
                    manager ??= new(Home.Resolve());
                    if (args[1] != "show") manager.SetInferenceHistory(limit);
                    output.WriteLine(Messages.ConfigInferenceHistory.Format(manager.InferenceHistory()));
                    return 0;
                }
                if (args.Length >= 1 && args[0] == "observer-retry")
                {
                    if (!(args.Length == 2 && args[1] is "show" or "unset" || args.Length == 4 && args[1] == "set")) Invalid();
                    ObserverPreferences? preferences = null;
                    if (args[1] == "set")
                    {
                        if (!int.TryParse(args[2], out var initial) || !int.TryParse(args[3], out var maximum) || initial < 1 || maximum < initial || maximum > 3600) Invalid();
                        preferences = new(int.Parse(args[2]), int.Parse(args[3]));
                    }
                    manager ??= new(Home.Resolve());
                    if (args[1] != "show") manager.SetObserver(preferences);
                    var retry = manager.Observer();
                    output.WriteLine(Messages.ConfigReconnection.Format(retry.RetryInitialSeconds, retry.RetryMaxSeconds));
                    return 0;
                }
                if (args.Length >= 1 && args[0] == "param-reset")
                {
                    if (!(args.Length == 1 || args.Length == 2 && args[1] == "--dry-run")) Invalid();
                    manager ??= new(Home.Resolve());
                    manager.Parameters.Reset(args.Length == 2, output, parameterConfirmation ?? (() => ParameterCli.Confirm(output)));
                    return 0;
                }
                if (args.Length >= 1 && args[0] is "backends-dir" or "models-dir")
                {
                    if (!(args.Length == 2 && args[1] is "show" or "unset" || args.Length == 3 && args[1] == "set")) Invalid();
                    manager ??= new(Home.Resolve());
                    if (args[1] != "show" && args[0] == "models-dir") manager.ModelDirectory.Change(args[1] == "set" ? args[2] : null, output,
                        directoryChoice ?? (() => Console.IsInputRedirected ? null : Console.ReadLine()),
                        directoryOverwrite ?? (() => !Console.IsInputRedirected && Messages.IsYes(Console.ReadLine())));
                    if (args[1] != "show" && args[0] == "backends-dir") manager.BackendDirectory.Change(args[1] == "set" ? args[2] : null, output,
                        directoryChoice ?? (() => Console.IsInputRedirected ? null : Console.ReadLine()),
                        directoryOverwrite ?? (() => !Console.IsInputRedirected && Messages.IsYes(Console.ReadLine())));
                    var view = args[0] == "models-dir" ? manager.ModelDirectory.Show() : manager.BackendDirectory.Show();
                    output.WriteLine(Messages.ConfigDirectory.Format(view.EffectivePath, Messages.OwnOrDefault(view.Own), view.Location));
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
                    args.Length == 4 && args[0] is "set" or "bind" && args[3] != "--ref" || args.Length == 3 && args[0] == "unset" ||
                    (args.Length == 3 || args.Length == 5 && args[3] == "--model") && args[0] == "clone";
                if (!valid) Invalid();
                manager ??= new(Home.Resolve());
                switch (args[0])
                {
                    case "bind":
                        manager.Bind(args[1], args[2], args[3]);
                        output.WriteLine(Messages.PresetBindingUpdated);
                        break;
                    case "set":
                    case "unset":
                        manager.Set(args[1], args[2], args[0] == "set" ? args[3] : null);
                        output.WriteLine(Messages.PresetValuesUpdated);
                        break;
                    case "create":
                        var created = manager.Create(args[1], args[2]);
                        output.WriteLine(Messages.PresetCreated.Format(created.Name, created.Id.ToString("D"), created.ModelId?.ToString("D")));
                        break;
                    case "clone":
                        var clone = manager.Clone(args[1], args[2], args.Length == 5 ? args[4] : null);
                        output.WriteLine(Messages.PresetCloned.Format(clone.Name, clone.Id.ToString("D"), clone.ModelId?.ToString("D")));
                        break;
                    case "model": manager.ChangeModel(args[2], args[3]); output.WriteLine(Messages.PresetModelUpdated); break;
                    case "remove": manager.Remove(args[1]); output.WriteLine(Messages.PresetRemoved); break;
                    case "show":
                        var view = manager.View(args[1]);
                        ShowPreset(view, output);
                        ShowValues(view.Effective, view.Own, manager.DefaultBackend(), manager.Parameters.List(), output);
                        break;
                    case "list":
                        var entries = manager.List(args.Length == 3 ? args[2] : null);
                        foreach (var entry in entries) ShowPreset(entry, output);
                        if (entries.Length == 0) output.WriteLine(Messages.PresetNone);
                        break;
                }
                return 0;
            }
            Invalid();
            return 2;
        }
        catch (Exception exception) when (exception is ConfigurationException or ContractException or StorageException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error.WriteLine(Messages.ErrorSeeHelp.Format(exception.Message));
            return 2;
        }
    }

    // config language show | set <auto|code> | unset | list | export <code>
    private static int Language(string[] args, TextWriter output, TextWriter error, ConfigurationManager? manager)
    {
        if (args is not (["language", "show" or "unset" or "list"] or ["language", "set" or "export", _])) Invalid();
        var home = Home.Resolve();
        var available = Localization.Available(home);
        if (args[1] == "list")
        {
            foreach (var language in available)
                output.WriteLine($"{language.Code,-8} {Messages.LanguageOrigin(language.Origin)}{(language.Path is { } path ? "  " + path : "")}");
            return 0;
        }
        if (args[1] == "export")
        {
            if (!available.Any(l => string.Equals(l.Code, args[2], StringComparison.OrdinalIgnoreCase)))
                throw new ConfigurationException(Messages.LanguageUnavailable.Format(args[2]));
            output.WriteLine(Localization.Export(args[2], home));
            return 0;
        }
        string? code = null;
        if (args[1] == "set" && !string.Equals(args[2], Localization.Auto, StringComparison.OrdinalIgnoreCase))
            code = available.FirstOrDefault(l => string.Equals(l.Code, args[2], StringComparison.OrdinalIgnoreCase))?.Code
                ?? throw new ConfigurationException(Messages.LanguageUnavailable.Format(args[2]));
        manager ??= new(home);
        if (args[1] != "show") manager.SetLanguage(code);
        var setting = manager.Language();
        var used = Localization.Resolve(setting, System.Globalization.CultureInfo.CurrentUICulture, available);
        output.WriteLine(Messages.LanguageShow.Format(setting ?? Localization.Auto, Localization.Current));
        if (args[1] != "show" && used != Localization.Current) output.WriteLine(Messages.LanguageRestart);
        return 0;
    }

    private static void Invalid() => throw new ConfigurationException(Messages.InvalidArguments);
    private static void ShowPreset(PresetView view, TextWriter output)
    {
        output.WriteLine(Messages.PresetRow.Format(view.Preset.Id.ToString("D"), view.Preset.Name, view.Model?.Name ?? Messages.Missing, view.Preset.ModelId?.ToString("D")));
        foreach (var diagnostic in view.Diagnostics) output.WriteLine($"  {diagnostic}");
        foreach (var binding in view.Preset.Bindings ?? [])
            output.WriteLine(Messages.PresetBindingRow.Format(binding.Key, view.BoundResources?.GetValueOrDefault(binding.Key) ?? Messages.Missing, binding.Value.ToString("D")));
    }
    private static void ShowValues(Dictionary<string, string> effective, Dictionary<string, string> own, BackendRecord? backend, ParameterDefinition[] definitions, TextWriter output)
    {
        foreach (var pair in effective.OrderBy(p => p.Key, StringComparer.Ordinal))
            output.WriteLine(Messages.PresetValueRow.Format(pair.Key, pair.Value, Messages.OwnOrInherited(own.ContainsKey(pair.Key)), Messages.Availability(ParameterManager.Availability(ParameterCatalog.Find(pair.Key, definitions), backend))));
        if (effective.Count == 0) output.WriteLine(Messages.PresetNoValues);
    }
}
