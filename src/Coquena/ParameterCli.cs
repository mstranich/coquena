using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena;

public static class ParameterCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error, ParameterManager? manager = null,
        ConfigurationManager? configuration = null, Func<bool>? confirmation = null)
    {
        try
        {
            if (args.Length == 0) Invalid();
            var action = args[0];
            if (action is "list" or "show")
            {
                var start = action == "show" ? 2 : 1;
                if (args.Length < start) Invalid();
                var options = Parse(args, start, ["--backend", "--version", "--distribution"], []);
                var backendText = One(options, "--backend"); var release = One(options, "--version"); var origin = One(options, "--distribution");
                if (backendText is not null && (release is not null || origin is not null) || origin is not null && release is null) Invalid();
                if (origin is not null && !ParameterManager.Official(origin)) Invalid();
                if (release is not null) _ = ParameterManager.Build(release);
                BackendRecord? backend = null;
                if (backendText is not null)
                {
                    var id = Id(backendText); configuration ??= new(Home.Resolve()); backend = configuration.Backend(id);
                    if (!ParameterManager.OfficialBackend(backend)) Invalid();
                }
                manager ??= new(Home.Resolve());
                var definitions = manager.List();
                var selected = action == "list" ? definitions : ParameterCatalog.Find(args[1], definitions) is { } found ? [found] : [];
                foreach (var d in selected)
                {
                    output.WriteLine($"{d.Name} | {d.Type} | {d.Application}/{d.Destination} | {ParameterManager.Availability(d, backend, origin ?? "ggml", release)} | {d.Description} | alias: {string.Join(", ", d.Aliases)} | default: {d.Default ?? "sin default"} | values: {string.Join(", ", d.Choices ?? [])}");
                    foreach (var s in d.Support ?? [])
                    {
                        output.WriteLine($"  soporte: {(s.BackendId is { } local ? "backend " + local : s.Variant is null ? "general oficial" : "ggml/" + s.Variant)} | desde {s.From ?? "abierto"} | hasta {s.Until ?? "abierto"}{(s.EvidenceOnly ? " | evidencia inicial; otros builds desconocidos" : "")}");
                        if ((backend is not null || release is not null) && s == ParameterManager.EffectiveSupport(d, backend))
                            output.WriteLine("  asociación efectiva para la consulta.");
                        if (s.BackendId is { } referenced)
                        {
                            configuration ??= new(Home.Resolve());
                            try { _ = configuration.Backend(referenced); }
                            catch (ConfigurationException) { output.WriteLine($"  referencia local ausente: {referenced:D}; no se reasigna por nombre."); }
                        }
                    }
                }
                if (selected.Length == 0) output.WriteLine(action == "list" ? "Sin definiciones." : $"{ParameterCatalog.Canonical(args[1], definitions)} | desconocido; sin definición local, no implica rechazo.");
                return 0;
            }
            if (action == "support")
            {
                if (args.Length < 3 || args[1] is not ("set" or "remove")) Invalid();
                var start = 3; string? variant = null;
                if (args.Length > 3 && !args[3].StartsWith("--", StringComparison.Ordinal))
                {
                    var selector = args[3].Split('/');
                    if (selector.Length != 2 || selector[0] is not ("ggml" or "oficial")) Invalid();
                    variant = selector[1]; start++;
                }
                var options = Parse(args, start, args[1] == "set" ? ["--backend", "--from", "--until"] : ["--backend"], ["--dry-run"]);
                var local = One(options, "--backend");
                if (variant is not null && local is not null) Invalid();
                var support = new ParameterSupport(variant, local is null ? null : Id(local), One(options, "--from"), One(options, "--until"));
                ParameterManager.ValidateSupport(support);
                manager ??= new(Home.Resolve());
                manager.Support(args[2], support, args[1] == "remove", options.ContainsKey("--dry-run"), output, confirmation ?? (() => Confirm(output)));
                return 0;
            }
            if (action is not ("define" or "edit" or "remove") || args.Length < 2) Invalid();
            var valueOptions = action == "remove" ? Array.Empty<string>() : new[]
                { "--type", "--description", "--default", "--alias", "--values", "--scope", "--target" };
            var switches = action == "define" ? Array.Empty<string>() : action == "remove" ? ["--dry-run"] : new[]
                { "--dry-run", "--clear-default", "--clear-description", "--clear-aliases" };
            var metadata = Parse(args, 2, valueOptions, switches);
            foreach (var pair in new[] { ("--default", "--clear-default"), ("--description", "--clear-description"), ("--alias", "--clear-aliases") })
                if (metadata.ContainsKey(pair.Item1) && metadata.ContainsKey(pair.Item2)) Invalid();
            if (action == "define" && new[] { "--type", "--scope", "--target" }.Any(k => !metadata.ContainsKey(k))) Invalid();
            if (One(metadata, "--type") is { } type && type is not ("integer" or "decimal" or "string" or "boolean" or "flag" or "enum")) Invalid();
            if (One(metadata, "--scope") is { } scope && scope is not ("model" or "server")) Invalid();
            if (One(metadata, "--target") is { } target && target is not ("cli" or "ini")) Invalid();
            ParameterDefinition Edit(ParameterDefinition d)
            {
                var nextType = One(metadata, "--type") ?? d.Type;
                return d with
                {
                    Type = nextType, Application = One(metadata, "--scope") ?? d.Application,
                    Destination = One(metadata, "--target")?.ToUpperInvariant() ?? d.Destination,
                    Description = metadata.ContainsKey("--clear-description") ? "" : One(metadata, "--description") ?? d.Description,
                    Default = metadata.ContainsKey("--clear-default") ? null : One(metadata, "--default") ?? d.Default,
                    Aliases = metadata.ContainsKey("--clear-aliases") ? [] : metadata.TryGetValue("--alias", out var aliases) ? aliases.ToArray() : d.Aliases,
                    Choices = metadata.TryGetValue("--values", out var values) ? values.ToArray() : nextType == "enum" ? d.Choices : []
                };
            }
            manager ??= new(Home.Resolve());
            if (action == "define")
            {
                var definition = Edit(new(args[1], "", "", "", "", [], Choices: [], Support: []));
                manager.Define(definition); output.WriteLine("Definición creada; no se asignan valores.");
            }
            else
            {
                var summary = action == "remove" ? "eliminar definición" : "editar " + string.Join(" ", args[2..].Where(a => a != "--dry-run"));
                manager.ChangeDefinition(args[1], action == "remove" ? _ => null : Edit, summary,
                    metadata.ContainsKey("--dry-run"), output, confirmation ?? (() => Confirm(output)));
            }
            return 0;
        }
        catch (Exception e) when (e is ConfigurationException or ContractException or StorageException or IOException or UnauthorizedAccessException)
        { error.WriteLine($"Error: {e.Message} Consulte coquena --help."); return 2; }
    }
    private static Dictionary<string, List<string>> Parse(string[] args, int start, string[] values, string[] switches)
    {
        var result = new Dictionary<string, List<string>>();
        for (var i = start; i < args.Length; i++)
        {
            var split = args[i].Split('=', 2); var key = split[0];
            if (switches.Contains(key))
            { if (split.Length != 1 || !result.TryAdd(key, [])) Invalid(); continue; }
            if (!values.Contains(key)) Invalid();
            var value = split.Length == 2 ? split[1] : ++i < args.Length ? args[i] : throw new ConfigurationException("Falta valor de opción.");
            if (split.Length == 1 && (value.StartsWith("--", StringComparison.Ordinal) || key == "--alias" && value.StartsWith('-'))) Invalid();
            if (!result.TryGetValue(key, out var list)) result[key] = list = [];
            else if (key is not ("--alias" or "--values")) Invalid();
            list.Add(value);
        }
        return result;
    }
    private static string? One(Dictionary<string, List<string>> values, string key) => values.TryGetValue(key, out var list) ? list.Single() : null;
    private static Guid Id(string text) => Guid.TryParse(text, out var id) && id != Guid.Empty ? id : throw new ConfigurationException("ID de backend inválido.");
    internal static bool Confirm(TextWriter output)
    {
        if (Console.IsInputRedirected) throw new ConfigurationException("Se requiere entrada interactiva para confirmar la operación; estado conservado.");
        output.Write("¿Aplicar la operación? [s/N]: "); output.Flush();
        return Console.ReadLine()?.Trim().ToLowerInvariant() is "s" or "si" or "sí";
    }
    private static void Invalid() => throw new ConfigurationException("Argumentos inválidos.");
}
