using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Coquena.Recipes;

// Closed structural vocabulary. Cross-argument constraints, imports and emission
// are owned by 046; a schema-shaped mapping here does not certify those semantics.
internal static partial class RecipeStructure
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentName();

    internal static RecipeValue Layer(RecipeValue root, bool beforeInterpolation)
    {
        Object(root, "name target args-mode service arg-defs models companions presets");
        Field(root, "name", Text); Field(root, "target", Text); Field(root, "args-mode", Mode);
        Field(root, "service", Service);
        Field(root, "arg-defs", ArgDefs);
        foreach (var group in new[] { "models", "companions", "presets" })
        {
            var collection = root.Get(group);
            if (collection is null || collection.Kind == RecipeKind.Null) continue;
            Object(collection);
            foreach (var pair in collection.Fields)
            {
                Name(pair.Key, pair.Value);
                if (pair.Value.Kind == RecipeKind.Null) continue;
                if (group == "presets") Preset(pair.Value);
                else Resource(pair.Value, group == "companions");
            }
        }
        return root;

        void Field(RecipeValue value, string key, Action<RecipeValue> check)
        {
            if (value.Get(key) is { Kind: not RecipeKind.Null } child) check(child);
        }
        void Kind(RecipeValue value, RecipeKind kind)
        {
            if (beforeInterpolation && kind is RecipeKind.Boolean or RecipeKind.Integer &&
                value.Kind == RecipeKind.String && value.Style == RecipeStyle.Plain && value.Text!.Contains('$')) return;
            if (value.Kind != kind) throw value.Error("invalid-type", "structure", "Tipo de campo inválido.");
        }
        void Text(RecipeValue value) => Kind(value, RecipeKind.String);
        void Boolean(RecipeValue value) => Kind(value, RecipeKind.Boolean);
        void Integer(RecipeValue value) => Kind(value, RecipeKind.Integer);
        void Number(RecipeValue value)
        {
            if (beforeInterpolation && value.Kind == RecipeKind.String && value.Style == RecipeStyle.Plain && value.Text!.Contains('$')) return;
            if (value.CanConvert) { value.ConvertInterpolatedScalar("number"); return; }
            if (value.Kind is not (RecipeKind.Integer or RecipeKind.Number)) throw value.Error("invalid-type", "structure", "Se requiere un número.");
        }
        void Positive(RecipeValue value)
        {
            Integer(value);
            if (value.Kind == RecipeKind.Integer && BigInteger.Parse(value.Text!, CultureInfo.InvariantCulture) <= 0)
                throw value.Error("positive-required", "structure", "Se requiere entero positivo.");
        }
        void Mode(RecipeValue value)
        {
            Text(value);
            if (beforeInterpolation && value.Style != RecipeStyle.SingleQuoted && value.Text!.Contains('$')) return;
            if (value.Text is not ("strict" or "permissive")) throw value.Error("invalid-args-mode", "structure", "Modo de argumentos inválido.");
        }
        void Texts(RecipeValue value)
        {
            Kind(value, RecipeKind.Sequence);
            foreach (var item in value.Items) Text(item);
        }
        void Service(RecipeValue value)
        {
            Object(value, "backend server recovery");
            Field(value, "backend", backend =>
            {
                Object(backend, "version variant executable");
                Field(backend, "version", Text); Field(backend, "variant", Text); Field(backend, "executable", Text);
            });
            Field(value, "server", server =>
            {
                Object(server, "args-mode args logs hooks health");
                Field(server, "args-mode", Mode); Field(server, "args", Args);
                Field(server, "logs", logs =>
                {
                    Object(logs, "directory retention"); Field(logs, "directory", Text);
                    Field(logs, "retention", retention => { Object(retention, "max-bytes"); Field(retention, "max-bytes", Positive); });
                });
                Field(server, "hooks", hooks =>
                {
                    Object(hooks, "start-failed");
                    Field(hooks, "start-failed", hook =>
                    {
                        Object(hook, "command args"); Field(hook, "command", Text); Field(hook, "args", Texts);
                    });
                });
                Field(server, "health", health =>
                {
                    Object(health, "url timeout-seconds interval-ms"); Field(health, "url", Text);
                    Field(health, "timeout-seconds", Positive); Field(health, "interval-ms", Integer);
                });
            });
            Field(value, "recovery", recovery =>
            {
                Object(recovery, "allow-draft-last-good"); Field(recovery, "allow-draft-last-good", Boolean);
            });
        }
        void Preset(RecipeValue value)
        {
            Object(value, "enable warmup args-mode args"); Field(value, "enable", Boolean); Field(value, "warmup", Boolean);
            Field(value, "args-mode", Mode); Field(value, "args", Args);
        }
        void Args(RecipeValue value)
        {
            Object(value);
            foreach (var pair in value.Fields)
            {
                if (!ArgumentName().IsMatch(pair.Key)) throw pair.Value.Error("invalid-argument-name", "structure", "Nombre de argumento inválido.");
                var argument = pair.Value;
                if (argument.Kind == RecipeKind.Map)
                {
                    Object(argument, "literal");
                    if (argument.Get("literal") is not { Kind: RecipeKind.String or RecipeKind.Boolean or RecipeKind.Integer or RecipeKind.Number })
                        throw argument.Error("invalid-literal", "structure", "Literal requiere un escalar no null.");
                }
                if (argument.Kind == RecipeKind.Sequence)
                    foreach (var item in argument.Items)
                        if (item.Kind is RecipeKind.Map or RecipeKind.Sequence or RecipeKind.Null)
                            throw item.Error("invalid-argument-item", "structure", "La lista de argumentos requiere escalares no null.");
            }
        }
        void Resource(RecipeValue value, bool companion)
        {
            if (!companion && value.Kind == RecipeKind.String) { if (!beforeInterpolation) ShortModel(value); return; }
            Object(value, companion ? "repository revision quantization files type" : "repository revision quantization files");
            foreach (var key in new[] { "repository", "revision", "quantization", "type" }) Field(value, key, Text);
            Field(value, "files", Texts);
        }
        void ArgDefs(RecipeValue value)
        {
            Object(value, "imports definitions"); Field(value, "imports", Texts);
            Field(value, "definitions", definitions =>
            {
                Object(definitions);
                foreach (var pair in definitions.Fields)
                {
                    if (!ArgumentName().IsMatch(pair.Key)) throw pair.Value.Error("invalid-argument-name", "structure", "Nombre de argumento inválido.");
                    if (pair.Value.Kind != RecipeKind.Null) Definition(pair.Value, false);
                }
            });
        }
        void Definition(RecipeValue value, bool item)
        {
            Object(value, item ? "type enum minimum maximum pattern items any-of" :
                "type enum minimum maximum pattern items any-of description default aliases scope rules applies-to emit");
            foreach (var key in new[] { "type", "pattern", "description", "scope" }) Field(value, key, Text);
            Field(value, "minimum", Number); Field(value, "maximum", Number);
            Field(value, "aliases", Texts);
            foreach (var key in new[] { "enum", "any-of", "rules" })
                Field(value, key, list => Kind(list, RecipeKind.Sequence));
            Field(value, "items", child => Definition(child, true));
            Field(value, "any-of", list => { foreach (var child in list.Items) Definition(child, true); });
            Field(value, "applies-to", applies =>
            {
                Object(applies, "distribution min-build max-build capabilities");
                foreach (var key in new[] { "distribution", "min-build", "max-build" }) Field(applies, key, Text);
                Field(applies, "capabilities", Texts);
            });
            Field(value, "emit", emit =>
            {
                Object(emit, "cli ini");
                foreach (var destination in new[] { "cli", "ini" }) Field(emit, destination, format =>
                {
                    Object(format, destination == "cli" ? "name mode separator true-value false-value negative-name" : "name mode separator true-value false-value");
                    foreach (var child in format.Fields.Values) if (child.Kind != RecipeKind.Null) Text(child);
                });
            });
            Field(value, "rules", rules =>
            {
                foreach (var rule in rules.Items)
                {
                    Object(rule, "when requires conflicts"); Field(rule, "when", Condition);
                    foreach (var key in new[] { "requires", "conflicts" }) Field(rule, key, conditions =>
                    {
                        Object(conditions);
                        foreach (var condition in conditions.Fields.Values) if (condition.Kind != RecipeKind.Null) Condition(condition);
                    });
                }
            });
        }
        void Condition(RecipeValue value)
        {
            Object(value, "present equals contains"); Field(value, "present", Boolean);
        }
    }

    internal static void Effective(RecipeValue root)
    {
        if (root.Get("name") is { } name) Name(name.Text!, name);
        if (root.Get("target") is { } target) Name(target.Text!, target);
        if (root.Get("target")?.Text == "self" && root.Get("service") is null)
            throw root.Get("target")!.Error("self-requires-service", "validation", "Target self requiere service.");
        if (root.Get("service") is { } service)
        {
            var backend = service.Get("backend") ?? throw service.Error("backend-required", "validation", "Service requiere un backend efectivo.");
            var external = backend.Get("executable");
            var version = backend.Get("version"); var variant = backend.Get("variant");
            if (external is null ? version is null || variant is null : version is not null || variant is not null)
                throw backend.Error("invalid-backend-selection", "validation", "Usar version y variant, o executable, excluyentes.");
            foreach (var child in backend.Fields.Values) Nonempty(child);
            if (service.Get("server")?.Get("health")?.Get("interval-ms") is { } interval &&
                (BigInteger.Parse(interval.Text!, CultureInfo.InvariantCulture) < 100 || BigInteger.Parse(interval.Text!, CultureInfo.InvariantCulture) > 10000))
                throw interval.Error("invalid-health-interval", "validation", "Interval-ms debe estar entre 100 y 10000.");
            if (service.Get("server")?.Get("hooks")?.Get("start-failed") is { } hook)
                Nonempty(hook.Get("command") ?? throw hook.Error("hook-command-required", "validation", "Hook requiere command."));
        }
        foreach (var group in new[] { "models", "companions" })
            if (root.Get(group) is { } resources)
                foreach (var resource in resources.Fields.Values)
                {
                    var repository = resource.Get("repository"); var files = resource.Get("files"); var quantization = resource.Get("quantization");
                    if (repository is not null)
                    {
                        if (!ValidRepository(repository.Text!)) throw repository.Error("invalid-repository", "validation", "Se requiere publicador/repositorio.");
                        if ((quantization is null) == (files is null)) throw resource.Error("invalid-resource-selection", "validation", "Repository requiere quantization o files, excluyentes.");
                    }
                    else if (files is null || quantization is not null || resource.Get("revision") is not null)
                        throw resource.Error("invalid-local-selection", "validation", "Recurso local requiere files y no admite revision ni quantization.");
                    if (files is not null)
                    {
                        if (files.Items.Count == 0) throw files.Error("empty-files", "validation", "Files requiere una lista no vacía.");
                        foreach (var file in files.Items)
                        {
                            Nonempty(file);
                            if (repository is not null && (Path.IsPathRooted(file.Text!) || Uri.TryCreate(file.Text, UriKind.Absolute, out _)))
                                throw file.Error("mixed-resource-paths", "validation", "Files con repository requiere rutas del repositorio, no rutas locales o URLs.");
                        }
                    }
                    if (quantization is not null) Nonempty(quantization);
                    if (resource.Get("revision") is { } revision) Nonempty(revision);
                    if (group == "companions" && resource.Get("type")?.Text is not ("mmproj" or "mtp"))
                        throw resource.Error("invalid-companion-type", "validation", "Companion requiere type mmproj o mtp.");
                }
    }

    internal static RecipeValue NormalizeModels(RecipeValue root)
    {
        if (root.Get("models") is not { Kind: RecipeKind.Map } models) return root;
        var fields = models.Fields.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        foreach (var pair in fields.ToArray())
            if (pair.Value.Kind == RecipeKind.String)
            {
                var (repository, quantization) = ShortModel(pair.Value);
                fields[pair.Key] = RecipeValue.Map(pair.Value.Origin, new Dictionary<string, RecipeValue>
                {
                    ["repository"] = new(RecipeKind.String, pair.Value.Origin, repository, pair.Value.Style, pair.Value.Sensitive),
                    ["quantization"] = new(RecipeKind.String, pair.Value.Origin, quantization, pair.Value.Style, pair.Value.Sensitive)
                });
            }
        var result = root.Fields.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        result["models"] = models.WithFields(fields);
        return root.WithFields(result);
    }

    private static (string Repository, string Quantization) ShortModel(RecipeValue value)
    {
        // Prior to expansion this is checked only as a string, not as a selector.
        var separator = value.Text!.IndexOf(':');
        if (separator <= 0 || separator != value.Text.LastIndexOf(':') || separator == value.Text.Length - 1 || !ValidRepository(value.Text[..separator]))
            throw value.Error("invalid-model-shorthand", "validation", "Modelo escalar requiere publicador/repositorio:cuantización.");
        return (value.Text[..separator], value.Text[(separator + 1)..]);
    }
    private static bool ValidRepository(string text) => text.Split('/') is [var owner, var repo] &&
        owner.Length > 0 && repo.Length > 0 && !text.Any(c => char.IsWhiteSpace(c) || c is ':' or '\\' or '#' or '\0');
    private static void Nonempty(RecipeValue value)
    { if (string.IsNullOrWhiteSpace(value.Text)) throw value.Error("empty-value", "validation", "Se requiere texto no vacío."); }
    private static void Name(string text, RecipeValue value)
    {
        if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(['\0', '\r', '\n', ':', '/', '\\']) >= 0)
            throw value.Error("invalid-name", "structure", "Nombre inválido; se requiere un componente explícito válido.");
    }
    private static void Object(RecipeValue value, string? vocabulary = null)
    {
        if (value.Kind != RecipeKind.Map) throw value.Error("map-required", "structure", "Se requiere un mapa.");
        if (vocabulary is null) return;
        var allowed = vocabulary.Split(' ').ToHashSet(StringComparer.Ordinal);
        foreach (var pair in value.Fields)
            if (!allowed.Contains(pair.Key)) throw pair.Value.Error("unknown-key", "structure", "Clave estructural desconocida.");
    }
}
