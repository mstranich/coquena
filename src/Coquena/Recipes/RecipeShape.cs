using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Coquena.Recipes;

// Closed structural vocabulary of a recipe layer, declared once. The reader validates
// with it and the JSON Schema for editors is generated from it, so both accept the same
// keys and value types. Rules across fields (Effective) stay in RecipeStructure.
internal static partial class RecipeShape
{
    internal abstract record Shape(string? Description = null);
    internal enum ScalarKind { Text, Boolean, Integer, Positive, Number, Any }
    internal sealed record Scalar(ScalarKind Kind, string? Description = null) : Shape(Description);
    internal sealed record Field(string Key, Shape Shape);
    internal sealed record Map(IReadOnlyList<Field> Fields, string? Description = null) : Shape(Description);
    // A map with keys chosen by the user: named entries or argument names.
    internal sealed record Entries(Shape Value, bool Arguments, string? Description = null) : Shape(Description);
    internal sealed record List(Shape Item, string? Description = null) : Shape(Description);
    // An argument value: scalar, {literal: scalar} or a list of scalars.
    internal sealed record Argument(string? Description = null) : Shape(Description);
    // A model: "owner/repository:quantization" or a map; companions only take the map.
    internal sealed record Resource(bool Companion, Map Fields, string? Description = null) : Shape(Description);
    internal sealed record Reference(string Name, string? Description = null) : Shape(Description);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]*$", RegexOptions.CultureInvariant)]
    internal static partial Regex ArgumentName();

    private static Scalar Text(string? description = null) => new(ScalarKind.Text, description);
    private static Map Fields(string? description, params Field[] fields) => new(fields, description);
    private static Field F(string key, Shape shape) => new(key, shape);
    private static readonly Scalar Mode = new(ScalarKind.Boolean,
        "Argument validation. true (strict) rejects arguments without a definition in parameters; false (permissive, default) accepts them. " +
        "Both check type, options, scope and rules of defined arguments. The root sets the general value; " +
        "service.server and each preset may override it and inherit it from the root when omitted.");
    private static readonly List Texts = new(Text());
    private static Scalar Bool(string description) => new(ScalarKind.Boolean, description);

    private static Map ResourceFields(bool companion) => Fields(null,
        [F("repository", Text("Remote repository as publisher/repository. Without repository, files lists local paths.")),
         F("revision", Text("Repository revision. When omitted, the default one is used and pinned to a commit on resolution; not allowed with local paths.")),
         F("quantization", Text("Quantization to download from the repository. Alternative to files; they are not combined.")),
         F("files", new List(Text(), "Weight files: repository paths, as an alternative to quantization, or local paths without repository. Do not mix local and remote.")),
         .. companion ? [F("type", Text("Companion role: mmproj or mtp."))] : Array.Empty<Field>()]);

    private static Map EmitTarget(bool cli) => Fields(cli ? "Emission as a server command-line argument." : "Emission as a key of the presets INI file.",
        [F("name", Text(cli ? "Argument name without --. When omitted, the canonical name is used." : "Exact INI key. When omitted, the canonical name is used.")),
         F("mode", Text("Required: value (a scalar), flag (boolean only), repeat or join (array only).")),
         F("separator", Text("Separator between items with mode join.")),
         F("true-value", Text("Text emitted for true in a boolean with mode value.")),
         F("false-value", Text("Text emitted for false in a boolean with mode value.")),
         .. cli ? [F("negative-name", Text("Flag emitted for false in a boolean with mode flag."))] : Array.Empty<Field>()]);

    internal static readonly Map Definition = Fields("Definition of a backend argument: type, constraints, scope, rules and emission.",
        F("type", Text("Value type: string, integer, number, boolean or array. Required unless any-of is used.")),
        F("enum", new List(new Scalar(ScalarKind.Any), "Allowed values, of the declared type.")),
        F("minimum", new Scalar(ScalarKind.Number, "Inclusive minimum; integer or number only.")),
        F("maximum", new Scalar(ScalarKind.Number, "Inclusive maximum; integer or number only.")),
        F("pattern", Text("Regular expression a string must match (.NET, without backreferences or lookaround).")),
        F("items", new Reference("definition-item", "Definition of each item; required for array.")),
        F("any-of", new List(new Reference("definition-item"), "Value alternatives; excludes type and its constraints.")),
        F("description", Text("Text for forms and help.")),
        F("default", new Scalar(ScalarKind.Any, "Value suggested in forms. Not emitted when the argument is omitted.")),
        F("aliases", new List(Text(), "Other names of the same argument; they cannot match existing names or aliases.")),
        F("scope", Text("Where it can be assigned: preset or server. When omitted, both.")),
        F("rules", new List(Fields("Rule: when `when` holds, requires must hold and conflicts must not. At least one of the two.",
            F("when", new Reference("condition", "Condition on this argument. When omitted, it means {present: true}.")),
            F("requires", new Entries(new Reference("condition"), false, "Argument → condition that must hold.")),
            F("conflicts", new Entries(new Reference("condition"), false, "Argument → condition that must not hold."))), "Rules between arguments.")),
        F("applies-to", Fields("Backend builds the definition applies to.",
            F("distribution", Text("Backend distribution.")), F("min-build", Text("Inclusive minimum build, bNNNN.")),
            F("max-build", Text("Inclusive maximum build, bNNNN.")), F("capabilities", new List(Text(), "Capabilities the build requires.")))),
        F("emit", Fields("How the argument is emitted. A missing destination uses the known adapter or free emission.", F("cli", EmitTarget(true)), F("ini", EmitTarget(false)))));

    internal static readonly Map DefinitionItem = new(Definition.Fields.Where(field =>
        field.Key is "type" or "enum" or "minimum" or "maximum" or "pattern" or "items" or "any-of").ToArray(), "Definition of a value, without argument metadata.");

    internal static readonly Map Condition = Fields("Condition: exactly one of present, equals or contains.",
        F("present", Bool("true if the argument is assigned (false counts as assigned); false if it is missing or removed with null.")),
        F("equals", new Scalar(ScalarKind.Any, "The argument is exactly this scalar.")),
        F("contains", new Scalar(ScalarKind.Any, "The argument is an array containing this scalar.")));

    internal static readonly Map Root = Fields("Coquena recipe (coquena.yaml): defines its own inference service, presets to publish, or both.",
        F("name", Text("Recipe name. Identifies its own service and prefixes published presets (name/preset). When omitted, the folder name is used on registration.")),
        F("publish-to", Text("Service where the presets are published: global or default (the default global service; also when the field is omitted), " +
            "self (the service this recipe defines; requires service) or the name of a registered global service. " +
            "An unknown name is an error, with no fallback. Choosing the destination does not start the service and does not depend on the recipe defining service.")),
        F("strict-args", Mode),
        F("service", Fields("The recipe's own inference service; at most one. Presets are published to it with publish-to: self.",
            F("backend", Fields("Backend that runs the service: version and variant of an official backend, or executable; mutually exclusive.",
                F("name", Text("Alias of the downloaded backend.")), F("version", Text("Official backend version.")),
                F("variant", Text("Official backend variant.")), F("executable", Text("Path to an external executable.")))),
            F("server", Fields("Server process configuration.",
                F("strict-args", Mode), F("args", new Entries(new Argument(), true, "Server arguments: name without -- → value.")),
                F("logs", Fields("Server logs.", F("directory", Text("Log folder.")),
                    F("retention", Fields("Log retention.", F("max-bytes", new Scalar(ScalarKind.Positive, "Maximum size kept, in bytes.")))))),
                F("hooks", Fields("Commands run on server events.", F("start-failed", Fields("Runs when the server fails to start.",
                    F("command", Text("Command path.")), F("args", new List(Text(), "Command arguments; empty by default.")))))),
                F("health", Fields("Server health check.", F("url", Text("URL that is queried.")),
                    F("timeout-seconds", new Scalar(ScalarKind.Positive, "Maximum time to become healthy, in seconds.")),
                    F("interval-ms", new Scalar(ScalarKind.Integer, "Interval between checks, from 100 to 10000 ms.")))))),
            F("recovery", Fields("Service recovery.", F("allow-draft-last-good", Bool("Allows promoting an applied, healthy draft as the last good version. Default false.")))))),
        F("parameters", new Entries(new Reference("definition"), true,
            "Parameters available to server and presets: argument name without -- → definition. A later layer replaces the whole definition; null removes it.")),
        F("models", new Entries(new Resource(false, ResourceFields(false)), false, "Models by key. Short form publisher/repository:quantization or a map; presets reference them as model://key.")),
        F("companions", new Entries(new Resource(true, ResourceFields(true)), false, "Auxiliary model files (mmproj, mtp) by key.")),
        F("presets", new Entries(Fields("Preset published as name/key.",
            F("enable", Bool("Publishes the preset. Default true; when disabled it keeps its definition.")),
            F("warmup", Bool("Preloads the model on publication. Default false.")), F("strict-args", Mode),
            F("args", new Entries(new Argument(), true, "Preset arguments: name without -- → value."))), false, "Presets by name.")));

    private static Shape Resolve(Reference reference) => reference.Name switch
    {
        "definition" => Definition,
        "definition-item" => DefinitionItem,
        _ => Condition
    };

    // Throws the first structural problem under `value`. Before interpolation, a plain
    // string with `$` stands for a boolean or number still to expand.
    internal static void Validate(RecipeValue value, Shape shape, bool beforeInterpolation)
    {
        switch (shape)
        {
            case Reference reference: Validate(value, Resolve(reference), beforeInterpolation); break;
            case Map map:
                Object(value, map.Fields.Select(field => field.Key));
                foreach (var field in map.Fields)
                    if (value.Get(field.Key) is { Kind: not RecipeKind.Null } child) Validate(child, field.Shape, beforeInterpolation);
                break;
            case Entries entries:
                Object(value, null);
                foreach (var pair in value.Fields)
                {
                    if (entries.Arguments) { if (!ArgumentName().IsMatch(pair.Key)) throw pair.Value.Error("invalid-argument-name", "structure", "Nombre de argumento inválido."); }
                    else if (entries.Value is not Reference) Name(pair.Key, pair.Value);
                    if (pair.Value.Kind != RecipeKind.Null) Validate(pair.Value, entries.Value, beforeInterpolation);
                }
                break;
            case List list:
                Kind(value, RecipeKind.Sequence, beforeInterpolation);
                if (list.Item is not Scalar { Kind: ScalarKind.Any })
                    foreach (var item in value.Items) Validate(item, list.Item, beforeInterpolation);
                break;
            case Argument:
                if (value.Kind == RecipeKind.Map)
                {
                    Object(value, ["literal"]);
                    if (value.Get("literal") is not { Kind: RecipeKind.String or RecipeKind.Boolean or RecipeKind.Integer or RecipeKind.Number })
                        throw value.Error("invalid-literal", "structure", "Literal requiere un escalar no null.");
                }
                if (value.Kind == RecipeKind.Sequence)
                    foreach (var item in value.Items)
                        if (item.Kind is RecipeKind.Map or RecipeKind.Sequence or RecipeKind.Null)
                            throw item.Error("invalid-argument-item", "structure", "La lista de argumentos requiere escalares no null.");
                break;
            case Resource resource:
                if (!resource.Companion && value.Kind == RecipeKind.String) { if (!beforeInterpolation) RecipeStructure.ShortModel(value); return; }
                Validate(value, resource.Fields, beforeInterpolation);
                break;
            case Scalar scalar: Check(value, scalar.Kind, beforeInterpolation); break;
        }
    }

    private static void Check(RecipeValue value, ScalarKind kind, bool beforeInterpolation)
    {
        switch (kind)
        {
            case ScalarKind.Any: return;
            case ScalarKind.Text: Kind(value, RecipeKind.String, beforeInterpolation); return;
            case ScalarKind.Boolean: Kind(value, RecipeKind.Boolean, beforeInterpolation); return;
            case ScalarKind.Integer: Kind(value, RecipeKind.Integer, beforeInterpolation); return;
            case ScalarKind.Positive:
                Kind(value, RecipeKind.Integer, beforeInterpolation);
                if (value.Kind == RecipeKind.Integer && BigInteger.Parse(value.Text!, CultureInfo.InvariantCulture) <= 0)
                    throw value.Error("positive-required", "structure", "Se requiere entero positivo.");
                return;
            case ScalarKind.Number:
                if (Deferred(value, beforeInterpolation)) return;
                if (value.Kind is not (RecipeKind.Integer or RecipeKind.Number)) throw value.Error("invalid-type", "structure", "Se requiere un número.");
                return;
        }
    }

    private static bool Deferred(RecipeValue value, bool beforeInterpolation) =>
        beforeInterpolation && value.Kind == RecipeKind.String && value.Style == RecipeStyle.Plain && value.Text!.Contains('$');

    private static void Kind(RecipeValue value, RecipeKind kind, bool beforeInterpolation)
    {
        if (kind is RecipeKind.Boolean or RecipeKind.Integer && Deferred(value, beforeInterpolation)) return;
        if (value.Kind != kind) throw value.Error("invalid-type", "structure", "Tipo de campo inválido.");
    }

    private static void Name(string text, RecipeValue value)
    {
        if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(['\0', '\r', '\n', ':', '/', '\\']) >= 0)
            throw value.Error("invalid-name", "structure", "Nombre inválido; se requiere un componente explícito válido.");
    }

    private static void Object(RecipeValue value, IEnumerable<string>? vocabulary)
    {
        if (value.Kind != RecipeKind.Map) throw value.Error("map-required", "structure", "Se requiere un mapa.");
        if (vocabulary is null) return;
        var allowed = vocabulary.ToHashSet(StringComparer.Ordinal);
        foreach (var pair in value.Fields)
            if (!allowed.Contains(pair.Key)) throw pair.Value.Error("unknown-key", "structure", "Clave estructural desconocida.");
    }

    internal const string ManualUrl = "https://github.com/mstranich/coquena/blob/main/docs/user-guide/recipes.md";
    internal const string SchemaId = "https://coquena.dev/schema/recipe.json";

    // JSON Schema (2020-12) of a recipe file, generated from the same shapes.
    internal static JsonObject Schema()
    {
        var schema = (JsonObject)Emit(Root);
        var result = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = SchemaId,
            ["$comment"] = "Generated by Coquena. Do not edit: coquena init replaces it.",
            ["title"] = "Coquena recipe"
        };
        foreach (var pair in schema.ToArray()) { schema.Remove(pair.Key); result[pair.Key] = pair.Value; }
        result["description"] = Root.Description + " Manual: " + ManualUrl;
        var properties = result["properties"]!.AsObject();
        properties["publish-to"]!["examples"] = new JsonArray("global", "self", "secondary-gpu");
        properties["parameters"]!["examples"] = new JsonArray(new JsonObject
        {
            ["ctx-size"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["scope"] = "preset", ["description"] = "Context size." }
        });
        result["$defs"] = new JsonObject
        {
            ["definition"] = Emit(Definition), ["definition-item"] = Emit(DefinitionItem), ["condition"] = Emit(Condition)
        };
        return result;
    }

    private static JsonNode Emit(Shape shape)
    {
        var node = shape switch
        {
            Reference reference => new JsonObject { ["$ref"] = "#/$defs/" + reference.Name },
            Map map => new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(map.Fields.Select(field => KeyValuePair.Create(field.Key, (JsonNode?)Nullable(Emit(field.Shape))))),
                ["additionalProperties"] = false
            },
            Entries entries => new JsonObject
            {
                ["type"] = "object",
                ["propertyNames"] = entries.Arguments ? new JsonObject { ["pattern"] = ArgumentName().ToString() } : new JsonObject { ["pattern"] = @"^[^:/\\\r\n\u0000]*\S[^:/\\\r\n\u0000]*$" },
                ["additionalProperties"] = Nullable(Emit(entries.Value))
            },
            List list => new JsonObject { ["type"] = "array", ["items"] = Emit(list.Item) },
            Argument => new JsonObject
            {
                ["anyOf"] = new JsonArray(
                    new JsonObject { ["type"] = new JsonArray("string", "number", "boolean") },
                    new JsonObject
                    {
                        ["type"] = "object", ["required"] = new JsonArray("literal"), ["additionalProperties"] = false,
                        ["properties"] = new JsonObject { ["literal"] = new JsonObject { ["type"] = new JsonArray("string", "number", "boolean") } }
                    },
                    new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = new JsonArray("string", "number", "boolean") } })
            },
            Resource { Companion: true } resource => Emit(resource.Fields),
            Resource resource => new JsonObject
            {
                ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "string", ["pattern"] = "^[^/:\\s]+/[^/:\\s]+:[^:]+$", ["description"] = "publisher/repository:quantization" }, Emit(resource.Fields))
            },
            Scalar scalar => Emit(scalar.Kind),
            _ => throw new InvalidOperationException()
        };
        if (shape.Description is { } description && node is JsonObject target) target["description"] = description;
        return node;
    }

    private static JsonObject Emit(ScalarKind kind)
    {
        // Booleans and numbers may still be `${VAR|type}` text before interpolation.
        static JsonObject Deferred(JsonObject typed) => new() { ["anyOf"] = new JsonArray(typed, new JsonObject { ["type"] = "string", ["pattern"] = "\\$" }) };
        return kind switch
        {
            ScalarKind.Text => new JsonObject { ["type"] = "string" },
            ScalarKind.Boolean => Deferred(new JsonObject { ["type"] = "boolean" }),
            ScalarKind.Integer => Deferred(new JsonObject { ["type"] = "integer" }),
            ScalarKind.Positive => Deferred(new JsonObject { ["type"] = "integer", ["minimum"] = 1 }),
            ScalarKind.Number => Deferred(new JsonObject { ["type"] = "number" }),
            _ => new JsonObject()
        };
    }

    // Any field or entry may be null: null unassigns what a lower layer set.
    // The description is repeated on the wrapper so editors show it on hover.
    private static JsonObject Nullable(JsonNode node)
    {
        var result = new JsonObject { ["anyOf"] = new JsonArray(node, new JsonObject { ["type"] = "null" }) };
        if (node["description"] is { } description) result["description"] = description.DeepClone();
        return result;
    }
}
