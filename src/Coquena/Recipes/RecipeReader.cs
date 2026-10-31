using System.Collections;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Recipes;

public sealed class RecipeSelection
{
    public string Base { get; }
    public IReadOnlyList<string> Files { get; }
    public IReadOnlyList<string> EnvFiles { get; }
    internal ScopeContext? ImportedScope { get; set; }
    internal System.Text.Json.Nodes.JsonObject? ImportedPathBases { get; set; }
    internal bool FilterUnusedModels { get; set; }
    internal IReadOnlySet<string>? ImportCategories { get; set; }
    internal IReadOnlyDictionary<string, byte[]>? CapturedFiles { get; set; }

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
    internal static string ProjectName(RecipeSelection selection)
    {
        var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(x => (string)x.Key, x => (string?)x.Value, StringComparer.Ordinal);
        var variables = new RecipeVariables(environment);
        foreach (var file in selection.EnvFiles) variables.Load(file, selection.CapturedFiles?.GetValueOrDefault(file));
        RecipeValue? name = null;
        foreach (var file in selection.Files)
        {
            var root = new RecipeYaml(file, selection.CapturedFiles?.GetValueOrDefault(file)).Read();
            if (root.Get("name") is { } selected) name = selected.Kind == RecipeKind.Null ? null : Expand(selected, variables, "root.name");
        }
        name ??= new(RecipeKind.String, new(selection.Files[0], 1, 1, "$.name"), Path.GetFileName(selection.Base));
        var namedRoot = RecipeValue.Map(name.Origin, new Dictionary<string, RecipeValue> { ["name"] = name });
        RecipeStructure.Layer(namedRoot, false); RecipeStructure.Effective(namedRoot);
        return name.Text!;
    }

    public static EffectiveRecipe Read(RecipeSelection selection, IReadOnlyDictionary<string, string?>? environment = null,
        RecipeIdentity? registeredIdentity = null)
    {
        environment ??= Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(x => (string)x.Key, x => (string?)x.Value, StringComparer.Ordinal);
        var variables = new RecipeVariables(environment);
        foreach (var file in selection.EnvFiles) variables.Load(file, selection.CapturedFiles?.GetValueOrDefault(file));
        var layers = new List<RecipeValue>();
        var effective = RecipeValue.Map(new(selection.Files[0], 1, 1, "$"));
        var source = effective;
        var rawLayers = selection.Files.Select(file => new RecipeYaml(file, selection.CapturedFiles?.GetValueOrDefault(file)).Read()).ToArray();
        var selectedLayers = rawLayers.Select(raw =>
        {
            RecipeStructure.Layer(raw, true);
            if (selection.ImportCategories is not { } categories) return raw;
            return raw.WithFields(raw.Fields.Where(p => p.Key switch
            {
                "service" => categories.Contains("service"),
                "presets" => categories.Contains("presets"),
                "models" or "companions" => categories.Contains("model"),
                "arg-defs" => categories.Contains("params"),
                "args-mode" => categories.Contains("service") || categories.Contains("presets"),
                _ => true
            }).ToDictionary(StringComparer.Ordinal));
        }).ToArray();
        HashSet<string>? needed = null;
        if (selection.FilterUnusedModels)
        {
            needed = new(StringComparer.OrdinalIgnoreCase);
            var lightLayers = selectedLayers.Select(raw => Expand(raw.WithFields(raw.Fields.Where(p => p.Key is not ("models" or "companions")).ToDictionary(StringComparer.Ordinal)), variables, "root")).ToArray();
            var lightRoot = lightLayers.Aggregate(RecipeValue.Map(effective.Origin), (previous, next) => Merge(previous, next, "root"));
            if (lightRoot.Get("service")?.Get("server")?.Get("args") is { } serverArgs) CollectReferences(serverArgs, needed);
            foreach (var preset in lightRoot.Get("presets")?.Fields.Values ?? [])
                if (preset.Get("args") is { } args) CollectReferences(args, needed);
            var lightRecipe = new EffectiveRecipe(selection, lightRoot, lightLayers, registeredIdentity, lightRoot);
            if (selection.CapturedFiles is Dictionary<string, byte[]> captured) CaptureSchemas(lightRecipe, captured);
            var scopes = new HashSet<string>(StringComparer.Ordinal);
            if (lightRoot.Get("service")?.Get("server") is not null) scopes.Add("server");
            if ((lightRoot.Get("presets")?.Fields.Count ?? 0) != 0) scopes.Add("preset");
            foreach (var definition in ArgumentSchema.Load(lightRecipe).Definitions.Values)
                if (scopes.Count != 0 && (definition.Get("scope") is not { } argumentScope || scopes.Contains(argumentScope.Text!)))
                    if (definition.Get("rules") is { } rules) CollectReferences(rules, needed);
        }
        foreach (var selected in selectedLayers)
        {
            var layer = needed is null ? selected : FilterModels(selected, needed);
            source = Merge(source, layer, "root");
            layer = Expand(layer, variables, "root");
            RecipeStructure.Layer(layer, false);
            layer = RecipeStructure.NormalizeModels(layer);
            layers.Add(layer);
            effective = Merge(effective, layer, "root");
        }
        if (selection.ImportCategories is { } importedCategories && effective.Get("target")?.Text == "self" && !importedCategories.Contains("service"))
            throw effective.Error("import-self-requires-service", "import", "Target self requiere importar service.");
        RecipeStructure.Effective(effective);
        if (selection.ImportedPathBases is { } selectedBases) effective = Rebase(effective, selectedBases);
        if (selection.ImportedScope is { } scope)
        {
            // Read a single immutable generation, also when the caller already
            // holds the scope writer lease during ValidateIntent. Taking another
            // OS writer lease here would deadlock with that caller.
            var statePath = scope.PathFor("local/state.json");
            var state = ProjectDocuments.Read(File.ReadAllBytes(statePath), "state");
            var generation = ProjectDocuments.Uuid(state["data"]!["generation"]);
            foreach (var file in selection.Files)
            {
                var relative = Path.GetRelativePath(scope.Root, file).Replace('\\', '/');
                var entry = state["data"]!["documents"]?[relative];
                if (entry?["kind"]?.GetValue<string>() != "recipe-intention") continue;
                var bytes = File.ReadAllBytes(scope.PathFor($"local/generations/{generation}/{relative}"));
                if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != entry["sha256"]!.GetValue<string>())
                    throw new StorageException("invalid-generation", "La intención importada no coincide con la generación.");
                var document = ProjectDocuments.Read(bytes, "recipe-intention");
                effective = Rebase(effective, document["data"]!["path-bases"]!.AsObject());
            }
        }
        return new(selection, effective, layers.ToArray(), registeredIdentity, source);
    }

    internal static void CollectReferences(RecipeValue value, HashSet<string> names)
    {
        if (value.Kind == RecipeKind.Map && value.Get("literal") is not null) return;
        if (value.Kind == RecipeKind.String)
            foreach (var kind in new[] { "model", "companion" })
                if (value.Text!.StartsWith(kind + "://", StringComparison.Ordinal)) names.Add(kind + ":" + value.Text[(kind.Length + 3)..]);
        foreach (var child in value.Fields.Values.Concat(value.Items)) CollectReferences(child, names);
    }
    internal static void CaptureSchemas(EffectiveRecipe recipe, Dictionary<string, byte[]> inputs)
    {
        foreach (var import in recipe.Layers.SelectMany(l => l.Get("arg-defs")?.Get("imports")?.Items ?? []))
        {
            var path = recipe.Selection.ResolvePath(import.Text!);
            if (inputs.ContainsKey(path)) continue;
            try { inputs[path] = RecipeYaml.ReadBytes(path); }
            catch (RecipeException e) when (import.Sensitive)
            { throw new RecipeException(e.Code, "schema", import.Origin, "No se pudo leer el schema seleccionado mediante una variable."); }
        }
    }
    internal static RecipeValue FilterModels(RecipeValue root, HashSet<string> needed)
    {
        var fields = root.Fields.ToDictionary(StringComparer.Ordinal);
        foreach (var pair in new[] { ("models", "model"), ("companions", "companion") })
            if (fields.GetValueOrDefault(pair.Item1) is { Kind: RecipeKind.Map } entries)
                fields[pair.Item1] = entries.WithFields(entries.Fields.Where(p => needed.Contains(pair.Item2 + ":" + p.Key)).ToDictionary(StringComparer.Ordinal));
        return root.WithFields(fields);
    }

    private static RecipeValue Rebase(RecipeValue root, System.Text.Json.Nodes.JsonObject bases)
    {
        return Walk(root, "");
        RecipeValue Walk(RecipeValue value, string path)
        {
            if (bases[path]?.GetValue<string>() is string origin && value.Kind == RecipeKind.String)
                return new(RecipeKind.String, value.Origin, Path.GetFullPath(value.Text!, origin), value.Style, value.Sensitive);
            if (value.Kind == RecipeKind.Map)
                return value.WithFields(value.Fields.ToDictionary(p => p.Key, p => Walk(p.Value, path.Length == 0 ? p.Key : path + "/" + p.Key), StringComparer.Ordinal));
            if (value.Kind == RecipeKind.Sequence)
                return new(value.Kind, value.Origin, items: value.Items.Select((v, i) => Walk(v, path + "/" + i)));
            return value;
        }
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
            "integer" => RecipeKind.Integer,
            "number" => RecipeKind.Number,
            "boolean" => RecipeKind.Boolean,
            _ => RecipeKind.String
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
