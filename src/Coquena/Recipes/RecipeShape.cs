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
    internal enum ScalarKind { Text, Boolean, Integer, Positive, Number, Mode, Any }
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
    private static readonly Scalar Mode = new(ScalarKind.Mode, "strict rechaza argumentos sin definición; permissive los admite.");
    private static readonly List Texts = new(Text());

    private static Map ResourceFields(bool companion) => Fields(null,
        [F("repository", Text("Repositorio publicador/nombre.")), F("revision", Text("Revisión del repositorio.")),
         F("quantization", Text("Cuantización a descargar.")), F("files", new List(Text(), "Archivos de pesos.")),
         .. companion ? [F("type", Text("mmproj o mtp."))] : Array.Empty<Field>()]);

    internal static readonly Map Definition = Fields(null,
        F("type", Text("Tipo del valor.")), F("enum", new List(new Scalar(ScalarKind.Any))), F("minimum", new Scalar(ScalarKind.Number)),
        F("maximum", new Scalar(ScalarKind.Number)), F("pattern", Text()), F("items", new Reference("definition-item")),
        F("any-of", new List(new Reference("definition-item"))), F("description", Text()), F("default", new Scalar(ScalarKind.Any)),
        F("aliases", Texts), F("scope", Text("preset o server.")),
        F("rules", new List(Fields(null, F("when", new Reference("condition")),
            F("requires", new Entries(new Reference("condition"), false)), F("conflicts", new Entries(new Reference("condition"), false))))),
        F("applies-to", Fields(null, F("distribution", Text()), F("min-build", Text()), F("max-build", Text()), F("capabilities", Texts))),
        F("emit", Fields(null,
            F("cli", Fields(null, F("name", Text()), F("mode", Text()), F("separator", Text()), F("true-value", Text()), F("false-value", Text()), F("negative-name", Text()))),
            F("ini", Fields(null, F("name", Text()), F("mode", Text()), F("separator", Text()), F("true-value", Text()), F("false-value", Text()))))));

    internal static readonly Map DefinitionItem = new(Definition.Fields.Where(field =>
        field.Key is "type" or "enum" or "minimum" or "maximum" or "pattern" or "items" or "any-of").ToArray());

    internal static readonly Map Condition = Fields(null, F("present", new Scalar(ScalarKind.Boolean)), F("equals", new Scalar(ScalarKind.Any)), F("contains", new Scalar(ScalarKind.Any)));

    internal static readonly Map Root = Fields("Receta de Coquena.",
        F("name", Text("Nombre del proyecto.")),
        F("target", Text("Servicio donde se publican los presets: global o self.")),
        F("args-mode", Mode),
        F("service", Fields("Servicio propio del proyecto.",
            F("backend", Fields("Backend del servicio.", F("name", Text("Alias del backend.")), F("version", Text()), F("variant", Text()), F("executable", Text("Ejecutable externo.")))),
            F("server", Fields(null, F("args-mode", Mode), F("args", new Entries(new Argument(), true, "Argumentos del servidor.")),
                F("logs", Fields(null, F("directory", Text()), F("retention", Fields(null, F("max-bytes", new Scalar(ScalarKind.Positive)))))),
                F("hooks", Fields(null, F("start-failed", Fields(null, F("command", Text()), F("args", Texts))))),
                F("health", Fields(null, F("url", Text()), F("timeout-seconds", new Scalar(ScalarKind.Positive)), F("interval-ms", new Scalar(ScalarKind.Integer)))))),
            F("recovery", Fields(null, F("allow-draft-last-good", new Scalar(ScalarKind.Boolean)))))),
        F("arg-defs", Fields("Parámetros disponibles para los presets.", F("definitions", new Entries(new Reference("definition"), true)))),
        F("models", new Entries(new Resource(false, ResourceFields(false)), false, "Modelos por clave.")),
        F("companions", new Entries(new Resource(true, ResourceFields(true)), false, "Companions por clave.")),
        F("presets", new Entries(Fields(null,
            F("enable", new Scalar(ScalarKind.Boolean)), F("warmup", new Scalar(ScalarKind.Boolean)), F("args-mode", Mode),
            F("args", new Entries(new Argument(), true, "Argumentos del preset."))), false, "Presets por nombre.")));

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
            case ScalarKind.Mode:
                Kind(value, RecipeKind.String, beforeInterpolation);
                if (beforeInterpolation && value.Style != RecipeStyle.SingleQuoted && value.Text!.Contains('$')) return;
                if (value.Text is not ("strict" or "permissive")) throw value.Error("invalid-args-mode", "structure", "Modo de argumentos inválido.");
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

    internal const string SchemaId = "https://coquena.dev/schema/recipe.json";

    // JSON Schema (2020-12) of a recipe file, generated from the same shapes.
    internal static JsonObject Schema()
    {
        var schema = (JsonObject)Emit(Root);
        var result = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = SchemaId,
            ["$comment"] = "Generado por Coquena. No editar: se reemplaza al ejecutar coquena init."
        };
        foreach (var pair in schema.ToArray()) { schema.Remove(pair.Key); result[pair.Key] = pair.Value; }
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
                ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "string", ["pattern"] = "^[^/:\\s]+/[^/:\\s]+:[^:]+$", ["description"] = "publicador/repositorio:cuantización" }, Emit(resource.Fields))
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
            ScalarKind.Mode => new JsonObject { ["anyOf"] = new JsonArray(new JsonObject { ["enum"] = new JsonArray("strict", "permissive") }, new JsonObject { ["type"] = "string", ["pattern"] = "\\$" }) },
            _ => new JsonObject()
        };
    }

    // Any field or entry may be null: null unassigns what a lower layer set.
    private static JsonObject Nullable(JsonNode node) => new() { ["anyOf"] = new JsonArray(node, new JsonObject { ["type"] = "null" }) };
}
