using Coquena.Configuration;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena;

internal static class RecipeEditCli
{
    internal static int Run(string[] args, int command, TextWriter output, TextWriter error)
    {
        try
        {
            var family = args[command] == "runtime" ? "backend" : args[command];
            var files = new List<string>();
            var env = new List<string>();
            string? directory = null, definitionFile = null;
            var positional = new List<string>();
            var end = false;
            var global = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (i == command) continue;
                var arg = args[i];
                if (!end && arg == "--") { end = true; continue; }
                if (!end && arg is "--global" or "-g") { global = true; continue; }
                if (!end && family == "param" && positional.FirstOrDefault() == "define" && arg == "--file" && positional.Count == 2 && definitionFile is null)
                {
                    if (++i == args.Length) throw Invalid();
                    definitionFile = args[i];
                    continue;
                }
                if (!end && family == "model" && arg == "--file" &&
                    (positional.FirstOrDefault() == "add" || positional.Count >= 2 && positional[0] == "companion" && positional[1] == "add"))
                {
                    if (++i == args.Length) throw Invalid();
                    positional.Add("--weight");
                    positional.Add(args[i]);
                    continue;
                }
                if (!end && arg is "-f" or "--file" or "--env-file" or "--base" or "--project-directory")
                {
                    if (++i == args.Length || i == command) throw Invalid();
                    if (arg is "-f" or "--file") files.Add(args[i]);
                    else if (arg == "--env-file") env.Add(args[i]);
                    else if (directory is null) directory = args[i];
                    else throw Invalid();
                }
                else positional.Add(arg);
            }
            if (positional.Count == 0)
            {
                output.WriteLine(family == "preset" ? "preset list|show|set|bind|unset|clone|remove"
                    : "param list|show|define|set|unset|remove|reset");
                return 0;
            }
            var action = positional[0];
            if (family == "param" && action == "reset")
            {
                if (files.Count != 0 || env.Count != 0 || directory is not null ||
                    positional.Count > 2 || positional.Count == 2 && positional[1] != "--yes") throw Invalid();
                var confirmed = new ParameterManager(Home.Resolve()).ResetCatalog(output, () =>
                {
                    if (positional.Count == 2) return true;
                    if (Console.IsInputRedirected) return false;
                    output.WriteLine("¿Restablecer el catálogo global? [s/N]");
                    return Console.ReadLine()?.Trim().Equals("s", StringComparison.OrdinalIgnoreCase) == true;
                });
                return confirmed ? 0 : 3;
            }
            if (global)
            {
                if (family == "param" && action is "list" or "show" && files.Count == 0 && env.Count == 0 && directory is null)
                    return ParameterCli.Run(positional.ToArray(), output, error);
                // Aliases and fields of a global definition are edited in the global catalog.
                if (family == "param" && action == "edit" && files.Count == 0 && env.Count == 0 && directory is null)
                    return ParameterCli.Run(positional.ToArray(), output, error);
                throw Invalid();
            }
            if (family == "param" && (action is not ("list" or "show" or "define" or "set" or "unset" or "remove") ||
                action == "define" && definitionFile is null)) throw Invalid();
            var selection = new RecipeSelection(Environment.CurrentDirectory, files, directory, env);
            var inputs = selection.Files.Concat(selection.EnvFiles).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(path => path, RecipeYaml.ReadBytes, StringComparer.OrdinalIgnoreCase);
            selection.CapturedFiles = inputs;
            var recipe = RecipeReader.Read(selection);
            var schema = ArgumentSchema.Load(recipe);
            var presets = recipe.Root.Get("presets")?.Fields;
            var entities = family == "preset" ? presets : schema.Definitions;
            var name = positional.ElementAtOrDefault(1);
            if (family == "param" && name is not null) name = schema.Canonical(name);
            if (action == "list" && positional.Count == 1)
            {
                foreach (var key in entities?.Keys ?? []) output.WriteLine(key);
                return 0;
            }
            if (action == "show" && positional.Count == 2)
            {
                output.WriteLine(entities?.GetValueOrDefault(name!) ?? throw Invalid());
                return 0;
            }
            if (family is "preset" or "param" && name is null) throw Invalid();
            var file = selection.Files[^1];
            string[] path;
            EditNode value;
            if (family == "backend")
            {
                var options = Options(positional, 1, null, "--version", "--variant", "--executable");
                path = ["service", "backend"];
                var alias = recipe.SourceRoot.Get("service")?.Get("backend")?.Get("name");
                if (action != "set" || !(options.Keys.ToHashSet().SetEquals(["--version", "--variant"]) ||
                    options.Keys.ToHashSet().SetEquals(["--executable"]))) throw Invalid();
                var fields = options.Select(p => KeyValuePair.Create(p.Key[2..], RecipeWriter.String(p.Value))).ToList();
                if (alias is not null) fields.Add(KeyValuePair.Create("name", RecipeWriter.From(alias)));
                value = new EditMap(fields);
            }
            else if (family == "model")
            {
                var companion = action == "companion";
                var verb = companion ? 1 : 0;
                var adding = positional.ElementAtOrDefault(verb) == "add";
                var optionStart = verb + (adding ? 1 : 2);
                var weights = new List<string>();
                var fields = adding
                    ? Options(positional, optionStart, weights, "--name", "--type")
                    : Options(positional, optionStart, weights, "--repository", "--revision", "--quantization", "--type");
                var key = adding ? fields.GetValueOrDefault("--name") : positional.ElementAtOrDefault(verb + 1);
                if (key is null || adding && weights.Count == 0 || !adding && fields.Count + weights.Count == 0) throw Invalid();
                fields.Remove("--name");
                path = [companion ? "companions" : "models", key];
                var current = recipe.SourceRoot.Get(path[0])?.Get(key);
                if (adding && current is not null)
                {
                    var effective = recipe.Root.Get(path[0])?.Get(key);
                    if (effective?.Get("repository") is not null || effective?.Get("files") is not { } existingFiles ||
                        !existingFiles.Items.Select(item => item.Text).SequenceEqual(weights) ||
                        effective.Get("type")?.Text != fields.GetValueOrDefault("--type")) throw Invalid();
                    output.WriteLine("La definición ya está presente.");
                    return 0;
                }
                if (!companion && fields.ContainsKey("--type")) throw Invalid();
                var definition = current?.Fields.ToDictionary(p => p.Key, p => RecipeWriter.From(p.Value), StringComparer.Ordinal)
                    ?? new Dictionary<string, EditNode>(StringComparer.Ordinal);
                if (current is { Kind: RecipeKind.String } && weights.Count == 0 &&
                    !(fields.ContainsKey("--repository") && fields.ContainsKey("--quantization")))
                {
                    var shorthand = RecipeStructure.ShortModel(current);
                    EditNode Part(string text) => RecipeWriter.From(new(RecipeKind.String, current.Origin, text, current.Style));
                    definition["repository"] = Part(shorthand.Repository);
                    definition["quantization"] = Part(shorthand.Quantization);
                }
                if (weights.Count != 0 && !fields.ContainsKey("--repository"))
                    foreach (var field in new[] { "repository", "revision", "quantization" }) definition[field] = EditNode.Null;
                if (fields.ContainsKey("--repository") && weights.Count == 0) definition["files"] = EditNode.Null;
                if (fields.ContainsKey("--quantization") && weights.Count != 0) throw Invalid();
                if (weights.Count != 0) { definition["files"] = new EditSequence(weights.Select(RecipeWriter.String).ToArray()); definition["quantization"] = EditNode.Null; }
                foreach (var field in fields) definition[field.Key[2..]] = RecipeWriter.String(field.Value);
                if (definition.Count == 0) throw Invalid();
                value = new EditMap(definition.ToArray());
            }
            else if (family == "preset")
            {
                switch (action)
                {
                    case "set" when positional.Count == 4:
                        path = ["presets", name!, "args", PresetArgument(positional[2])];
                        var literal = ParseValue(positional[3]);
                        if (literal.Kind is RecipeKind.Map or RecipeKind.Null ||
                            literal.Items.Any(item => item.Kind is RecipeKind.Map or RecipeKind.Sequence or RecipeKind.Null))
                            throw Invalid();
                        if (literal.Items.Any(item => ArgumentValidation.Reference(recipe, item, false, true) is not null))
                            throw literal.Error("literal-list-reference", "edit", "El formato no admite referencias escritas como texto literal dentro de una lista.");
                        value = literal.Kind == RecipeKind.String ? RecipeWriter.Map(("literal", Literal(literal))) : Literal(literal);
                        break;
                    case "bind" when positional.Count == 4:
                        if (!positional[3].StartsWith("model://", StringComparison.Ordinal) &&
                            !positional[3].StartsWith("companion://", StringComparison.Ordinal)) throw Invalid();
                        path = ["presets", name!, "args", PresetArgument(positional[2])];
                        value = RecipeWriter.String(positional[3]);
                        break;
                    case "unset" when positional.Count == 3 && presets?.ContainsKey(name!) == true:
                        path = ["presets", name!, "args", PresetArgument(positional[2])];
                        value = new EditRemove(2);
                        break;
                    case "remove" when positional.Count == 2 && presets?.ContainsKey(name!) == true:
                        path = ["presets", name!];
                        value = new EditRemove();
                        break;
                    case "clone" when positional.Count == 3 && presets?.ContainsKey(positional[2]) != true:
                        path = ["presets", positional[2]];
                        if (recipe.SourceRoot.Get("presets")?.Get(name!) is null) throw Invalid();
                        value = RecipeEdit.Copy(selection, inputs, ["presets", name!]);
                        break;
                    default: throw Invalid();
                }
            }
            else
            {
                for (var i = recipe.Layers.Count - 1; i >= 0; i--)
                    if (recipe.Layers[i].Get("arg-defs") is not null) { file = selection.Files[i]; break; }
                path = ["arg-defs", "definitions", name!];
                var existing = recipe.SourceRoot.Get("arg-defs")?.Get("definitions")?.Get(name!);
                switch (action)
                {
                    case "define" when positional.Count == 2 && definitionFile is not null:
                        var definition = new RecipeYaml(definitionFile).Read();
                        value = RecipeWriter.From(definition);
                        break;
                    case "remove" when positional.Count == 2 && existing is not null:
                        value = new EditRemove();
                        break;
                    case "set" when positional.Count == 4 && existing is not null:
                    case "unset" when positional.Count == 3 && existing is not null:
                        if (existing!.Origin.File.Equals(file, StringComparison.OrdinalIgnoreCase))
                        {
                            path = [..path, positional[2]];
                            value = action == "unset" ? new EditRemove(3) : RecipeWriter.From(ParseValue(positional[3]));
                            break;
                        }
                        var fields = existing.Fields.ToDictionary(p => p.Key, p => RecipeWriter.From(p.Value), StringComparer.Ordinal);
                        if (action == "unset") fields.Remove(positional[2]);
                        else fields[positional[2]] = RecipeWriter.From(ParseValue(positional[3]));
                        value = new EditMap(fields.ToArray());
                        break;
                    default: throw Invalid();
                }
            }
            if (selection.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selection.Files.Count)
                throw recipe.Root.Error("ambiguous-edit", "edit", "Una capa YAML está seleccionada más de una vez.");
            RecipeEdit.Save(selection, inputs, file, path, value);

            string PresetArgument(string argument)
            {
                var canonical = schema.Canonical(argument);
                return presets?.GetValueOrDefault(name!)?.Get("args")?.Fields.Keys
                    .FirstOrDefault(key => schema.Canonical(key) == canonical) ?? argument;
            }
            output.WriteLine($"Guardado en {file}.");
            return 0;
        }
        catch (RecipeException e)
        {
            error.WriteLine(RecipeValidation.Message(e));
            return e.Code == "edit-conflict" ? 3 : 2;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or ConfigurationException or StorageException or Backends.BackendException or Models.ModelSourceException)
        {
            error.WriteLine(e.Message);
            if (e is ArgumentException) error.WriteLine("Consulte coquena --help para ver la sintaxis.");
            return e is ArgumentException or Backends.BackendException or Models.ModelSourceException ? 2 : 7;
        }
    }

    private static Dictionary<string, string> Options(List<string> args, int start, List<string>? weights, params string[] allowed)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = start; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count) throw Invalid();
            if (args[i] == "--weight" && weights is not null) { weights.Add(args[i + 1]); continue; }
            if (!allowed.Contains(args[i]) || !result.TryAdd(args[i], args[i + 1])) throw Invalid();
        }
        return result;
    }

    private static ArgumentException Invalid() => new("Sintaxis o selección de edición inválida.");

    private static RecipeValue ParseValue(string text)
    {
        var root = new RecipeYaml("<valor>", System.Text.Encoding.UTF8.GetBytes("value: " + text)).Read();
        if (root.Fields.Count != 1 || root.Get("value") is not { } value) throw Invalid();
        return value;
    }

    // A literal keeps `$` as text: lists are written item by item with that protection.
    private static EditNode Literal(RecipeValue value) => value.Kind switch
    {
        RecipeKind.String => RecipeWriter.String(value.Text!),
        RecipeKind.Sequence => new EditSequence(value.Items.Select(item => item.Kind == RecipeKind.String
            ? new EditScalar(RecipeWriter.Uninterpolated(item.Text!)) : Literal(item)).ToArray()),
        _ => RecipeWriter.From(value)
    };
}
