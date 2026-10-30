using System.Collections;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Recipes;

public sealed class RecipeSelection
{
    public string Base { get; }
    public IReadOnlyList<string> Files { get; }
    public IReadOnlyList<string> EnvFiles { get; }

    public RecipeSelection(string workingDirectory, IEnumerable<string>? files = null,
        string? projectDirectory = null, IEnumerable<string>? envFiles = null)
    {
        var cwd = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory));
        var selected = files?.ToArray() ?? [];
        var environment = envFiles?.ToArray() ?? [];
        var origin = new RecipeOrigin(cwd, 1, 1, "$selection");
        if (selected.Length + environment.Length + (selected.Length == 0 ? 1 : 0) > 64)
            throw new RecipeException("layer-limit", "selection", origin, "Se admiten como máximo 64 capas YAML o env.");
        if (selected.Concat(environment).Any(x => string.IsNullOrWhiteSpace(x) || x == "-"))
            throw new RecipeException("invalid-file-selection", "selection", origin, "Se requiere una ruta de archivo; stdin no está admitido.");
        var explicitBase = projectDirectory is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory, cwd));
        var first = selected.Length == 0 ? Path.Combine(explicitBase ?? cwd, "coquena.yaml") : Path.GetFullPath(selected[0], explicitBase ?? cwd);
        Base = explicitBase ?? Path.GetDirectoryName(first)!;
        Files = Array.AsReadOnly(new[] { first }.Concat(selected.Skip(1).Select(x => Path.GetFullPath(x, Base))).ToArray());
        EnvFiles = Array.AsReadOnly(environment.Select(x => Path.GetFullPath(x, Base)).ToArray());
    }

    public string ResolvePath(string path) => Path.GetFullPath(path, Base);
}

public sealed record RecipeTarget(string Selector, bool IsSelf, string? ServiceId);

public sealed class EffectiveRecipe
{
    public RecipeSelection Selection { get; }
    public RecipeValue Root { get; }
    public RecipeValue SourceRoot { get; }
    public IReadOnlyList<RecipeValue> Layers { get; }
    public string Name { get; }
    public string Target => Root.Get("target")?.Text ?? "global";
    public string ArgsMode => Root.Get("args-mode")?.Text ?? "permissive";

    internal EffectiveRecipe(RecipeSelection selection, RecipeValue root, RecipeValue[] layers, RecipeIdentity? identity, RecipeValue sourceRoot)
    {
        Selection = selection; Root = root; SourceRoot = sourceRoot; Layers = Array.AsReadOnly(layers);
        Name = identity?.RegisteredName ?? root.Get("name")?.Text ?? Path.GetFileName(selection.Base);
    }

    // Resolving selection is explicit, so editable/consumer recipes can be inspected
    // without a home or a registered target. No registration or process start occurs.
    public RecipeTarget ResolveTarget(Func<string, string> globalResolver, string? serviceOverride = null,
        ServiceIdentity? ownService = null)
    {
        var selector = serviceOverride ?? Target;
        if (selector == "self")
        {
            if (Root.Get("service") is null) throw Root.Error("self-requires-service", "target", "Target self requiere service.");
            return new(selector, true, ownService?.ServiceId);
        }
        try
        {
            var id = globalResolver(selector == "global" ? "default" : selector);
            if (string.IsNullOrEmpty(id)) throw new StorageException("unknown-service", "No resuelve el destino.");
            return new(selector, false, id);
        }
        catch (StorageException error)
        {
            throw (Root.Get("target") ?? Root).Error(error.Code, "target", error.Code == "unknown-service"
                ? "No resuelve el servicio seleccionado; no se elige sustituto."
                : "No se pudo consultar el estado del servicio seleccionado.");
        }
    }

    public bool PresetEnabled(string key) => Preset(key).Get("enable")?.Text != "false";
    public bool PresetWarmup(string key) => Preset(key).Get("warmup")?.Text == "true";
    public string PresetArgsMode(string key) => Preset(key).Get("args-mode")?.Text ?? ArgsMode;
    private RecipeValue Preset(string key) => Root.Get("presets")?.Get(key) ?? throw Root.Error("unknown-preset", "query", "El preset no existe.");
    public override string ToString() => Root.ToString();
}

public static class RecipeReader
{
    public static EffectiveRecipe Read(RecipeSelection selection, IReadOnlyDictionary<string, string?>? environment = null,
        RecipeIdentity? registeredIdentity = null)
    {
        environment ??= Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(x => (string)x.Key, x => (string?)x.Value, StringComparer.Ordinal);
        var variables = new RecipeVariables(environment);
        foreach (var file in selection.EnvFiles) variables.Load(file);
        var layers = new List<RecipeValue>();
        var effective = RecipeValue.Map(new(selection.Files[0], 1, 1, "$"));
        var source = effective;
        foreach (var file in selection.Files)
        {
            var layer = new RecipeYaml(file).Read();
            RecipeStructure.Layer(layer, true);
            source = Merge(source, layer, "root");
            layer = Expand(layer, variables, "root");
            RecipeStructure.Layer(layer, false);
            layer = RecipeStructure.NormalizeModels(layer);
            layers.Add(layer);
            effective = Merge(effective, layer, "root");
        }
        RecipeStructure.Effective(effective);
        return new(selection, effective, layers.ToArray(), registeredIdentity, source);
    }

    private static RecipeValue Expand(RecipeValue value, RecipeVariables variables, string context)
    {
        if (value.Kind == RecipeKind.Map)
        {
            // Only argument wrappers suppress expansion; a random map with this key
            // has already failed the closed structural vocabulary check.
            if (context.EndsWith(".args.*", StringComparison.Ordinal) && value.Get("literal") is not null) return value;
            return value.WithFields(value.Fields.ToDictionary(x => x.Key,
                x => Expand(x.Value, variables, Child(context, x.Key)), StringComparer.Ordinal));
        }
        if (value.Kind == RecipeKind.Sequence)
            return new(RecipeKind.Sequence, value.Origin, items: value.Items.Select(x => Expand(x, variables, context + "[]")));
        if (value.Kind != RecipeKind.String || value.Style is RecipeStyle.SingleQuoted or RecipeStyle.Block) return value;
        var expansion = variables.Expand(value.Text!, value.Origin);
        var kind = expansion.Type switch
        {
            "integer" => RecipeKind.Integer, "number" => RecipeKind.Number,
            "boolean" => RecipeKind.Boolean, _ => RecipeKind.String
        };
        return new(kind, value.Origin, expansion.Text, value.Style, expansion.Sensitive);
    }

    private static string Child(string context, string key) => context is "root.presets" or "root.models" or "root.companions" or "root.arg-defs.definitions" || context.EndsWith(".args", StringComparison.Ordinal)
        ? context + ".*" : context + "." + key;

    private static RecipeValue Merge(RecipeValue previous, RecipeValue next, string context)
    {
        if (next.Kind != RecipeKind.Map) return next;
        var atomic = context == "root.service.backend" || context.StartsWith("root.arg-defs.definitions.", StringComparison.Ordinal) && context.Count(c => c == '.') == 3;
        var fields = !atomic && previous.Kind == RecipeKind.Map
            ? previous.Fields.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
            : new Dictionary<string, RecipeValue>(StringComparer.Ordinal);
        foreach (var pair in next.Fields)
        {
            if (pair.Value.Kind == RecipeKind.Null) { fields.Remove(pair.Key); continue; }
            // Definitions are replaced as complete values, while other maps merge.
            var childContext = context + "." + pair.Key;
            fields[pair.Key] = Merge(fields.GetValueOrDefault(pair.Key) ?? RecipeValue.Map(pair.Value.Origin), pair.Value, childContext);
        }
        return next.WithFields(fields);
    }
}
