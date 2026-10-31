using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Coquena.Recipes;

// Local, closed data vocabulary. It never loads a validator, $ref or executable.
public sealed class ArgumentSchema
{
    public IReadOnlyDictionary<string, RecipeValue> Definitions { get; }
    private readonly Dictionary<string, string> names = new(StringComparer.Ordinal);
    internal ArgumentSchema(IDictionary<string, RecipeValue> definitions)
    {
        Definitions = new ReadOnlyDictionary<string, RecipeValue>(new Dictionary<string, RecipeValue>(definitions, StringComparer.Ordinal));
        foreach (var pair in Definitions)
        {
            Name(pair.Key, pair.Value);
            Definition(pair.Value, false, false);
            AddName(pair.Key, pair.Key, pair.Value);
        }
        foreach (var pair in Definitions)
            foreach (var alias in pair.Value.Get("aliases")?.Items ?? [])
            { Name(alias.Text!, alias); AddName(alias.Text!, pair.Key, alias); }
        foreach (var pair in Definitions)
            foreach (var rule in pair.Value.Get("rules")?.Items ?? [])
            {
                Object(rule, "when requires conflicts");
                if (rule.Get("requires") is null && rule.Get("conflicts") is null) Fail(rule, "invalid-rule");
                if (rule.Get("when") is { } when) Condition(when, pair.Value);
                foreach (var key in new[] { "requires", "conflicts" })
                    if (rule.Get(key) is { } conditions)
                    {
                        Object(conditions);
                        foreach (var target in conditions.Fields)
                        {
                            if (!Definitions.TryGetValue(target.Key, out var definition)) Fail(target.Value, "unknown-rule-argument");
                            if (pair.Value.Get("scope") is { } ownerScope && definition!.Get("scope") is { } targetScope && ownerScope.Text != targetScope.Text)
                                Fail(target.Value, "rule-scope-mismatch");
                            Condition(target.Value, definition!);
                        }
                    }
            }
    }

    public string Canonical(string name) => names.GetValueOrDefault(name) ?? name;

    public static ArgumentSchema Load(EffectiveRecipe recipe)
    {
        var definitions = new Dictionary<string, RecipeValue>(StringComparer.Ordinal);
        foreach (var layer in recipe.Layers)
        {
            // A null arg-defs/definitions removes the inherited catalog as well.
            if (layer.Get("arg-defs") is not { } catalog) continue;
            if (catalog.Kind == RecipeKind.Null) { definitions.Clear(); continue; }
            foreach (var import in catalog.Get("imports")?.Items ?? [])
            {
                string path;
                try { path = recipe.Selection.ResolvePath(import.Text!); }
                catch (Exception e) when (e is ArgumentException or NotSupportedException)
                { throw import.Error("invalid-schema-path", "schema", "Ruta de catálogo local inválida."); }
                RecipeValue document;
                try
                {
                    using var json = JsonDocument.Parse(RecipeYaml.ReadText(path, captured: recipe.Selection.CapturedFiles?.GetValueOrDefault(path)), new JsonDocumentOptions { MaxDepth = 64 });
                    var count = 0;
                    document = Json(json.RootElement, import.Sensitive ? import.Origin with { Path = import.Origin.Path + ".schema" } : new(path, 1, 1, "$"), ref count);
                }
                catch (RecipeException e) when (import.Sensitive)
                { throw new RecipeException(e.Code, "schema", import.Origin, "No se pudo leer el schema seleccionado mediante una variable."); }
                catch (JsonException) { throw import.Error("invalid-schema-json", "schema", "El catálogo importado no es JSON válido."); }
                Object(document, "definitions reference-build");
                if (document.Get("reference-build") is { } build) Text(build);
                var entries = document.Get("definitions") ?? throw document.Error("definitions-required", "schema", "El catálogo requiere definitions.");
                Apply(entries);
            }
            if (catalog.Get("definitions") is { } inline)
            {
                if (inline.Kind == RecipeKind.Null) definitions.Clear();
                else Apply(inline);
            }
        }
        return new(definitions);

        void Apply(RecipeValue entries)
        {
            Object(entries);
            foreach (var entry in entries.Fields)
            {
                Name(entry.Key, entry.Value);
                if (entry.Value.Kind == RecipeKind.Null) definitions.Remove(entry.Key);
                else definitions[entry.Key] = entry.Value;
            }
        }
    }

    // Changes are immutable candidates. Callers inspect impact before publishing.
    public ArgumentSchema Define(string name, RecipeValue definition)
    {
        var candidate = Definitions.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        candidate[name] = definition;
        return new(candidate);
    }
    public ArgumentSchema Remove(string name)
    {
        var candidate = Definitions.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        candidate.Remove(Canonical(name));
        return new(candidate);
    }

    private void AddName(string name, string canonical, RecipeValue at)
    { if (!names.TryAdd(name, canonical)) Fail(at, "alias-collision"); }

    private static void Definition(RecipeValue d, bool valueOnly, bool insideArray)
    {
        Object(d, valueOnly ? "type enum minimum maximum pattern items any-of" :
            "type enum minimum maximum pattern items any-of description default aliases scope rules applies-to emit");
        if (d.Get("description") is { } description) Text(description);
        if (d.Get("any-of") is { } alternatives)
        {
            Sequence(alternatives, true);
            if (new[] { "type", "enum", "minimum", "maximum", "pattern", "items" }.Any(k => d.Get(k) is not null)) Fail(d, "invalid-any-of");
            foreach (var alternative in alternatives.Items) Definition(alternative, true, insideArray);
        }
        else
        {
            var type = d.Get("type") ?? throw d.Error("type-required", "schema", "Se requiere type o any-of.");
            Text(type);
            if (type.Text is not ("string" or "integer" or "number" or "boolean" or "array")) Fail(type, "invalid-schema-type");
            if (type.Text == "array")
            {
                if (insideArray) Fail(d, "nested-array");
                Definition(d.Get("items") ?? throw d.Error("items-required", "schema", "Array requiere items."), true, true);
            }
            else if (d.Get("items") is not null) Fail(d, "inapplicable-items");
            foreach (var key in new[] { "minimum", "maximum" })
                if (d.Get(key) is { } limit)
                {
                    if (type.Text is not ("integer" or "number") || limit.Kind is not (RecipeKind.Integer or RecipeKind.Number)) Fail(limit, "invalid-bound");
                }
            if (d.Get("minimum") is { } min && d.Get("maximum") is { } max && CompareNumber(min.Text!, max.Text!) > 0) Fail(d, "invalid-range");
            if (d.Get("pattern") is { } pattern)
            {
                Text(pattern);
                if (type.Text != "string") Fail(pattern, "inapplicable-pattern");
                Pattern(pattern);
            }
            if (d.Get("enum") is { } choices)
            {
                Sequence(choices, true);
                foreach (var choice in choices.Items)
                    if (choice.Kind is RecipeKind.Map or RecipeKind.Sequence or RecipeKind.Null || !Matches(d, choice, false)) Fail(choice, "invalid-enum");
                for (var i = 0; i < choices.Items.Count; i++)
                    if (choices.Items.Take(i).Any(x => Equal(x, choices.Items[i]))) Fail(choices.Items[i], "duplicate-enum");
            }
        }
        if (d.Get("default") is { } fallback && !Matches(d, fallback)) Fail(fallback, "invalid-default");
        if (d.Get("aliases") is { } aliases)
        { Sequence(aliases); foreach (var alias in aliases.Items) Text(alias); }
        if (d.Get("scope") is { } scope)
        { Text(scope); if (scope.Text is not ("server" or "preset")) Fail(scope, "invalid-scope"); }
        if (d.Get("rules") is { } rules) Sequence(rules);
        if (d.Get("applies-to") is { } applies)
        {
            Object(applies, "distribution min-build max-build capabilities");
            if (applies.Get("distribution") is { } distribution) Text(distribution);
            foreach (var key in new[] { "min-build", "max-build" })
                if (applies.Get(key) is { } build) { Text(build); Build(build); }
            if (applies.Get("min-build") is { } low && applies.Get("max-build") is { } high && Build(low) > Build(high)) Fail(applies, "invalid-build-range");
            if (applies.Get("capabilities") is { } capabilities)
            { Sequence(capabilities); foreach (var capability in capabilities.Items) Text(capability); }
        }
        if (d.Get("emit") is { } emit)
        {
            Object(emit, "cli ini");
            foreach (var pair in emit.Fields) Emission(pair.Value, pair.Key, Types(d));
        }
    }

    private static void Emission(RecipeValue emit, string destination, HashSet<string> types)
    {
        Object(emit, destination == "cli" ? "name mode separator true-value false-value negative-name" : "name mode separator true-value false-value");
        foreach (var field in emit.Fields.Values) Text(field);
        if (emit.Get("name") is { } name)
        { if (destination == "cli") Name(name.Text!, name); else SafeIniName(name.Text!, name); }
        if (emit.Get("negative-name") is { } negative) Name(negative.Text!, negative);
        var mode = emit.Get("mode")?.Text;
        if (mode is null) Fail(emit, "emission-mode-required");
        if (mode is not ("value" or "flag" or "repeat" or "join")) Fail(emit, "invalid-emission-mode");
        if (mode == "flag" && !types.SetEquals(["boolean"]) || mode is "repeat" or "join" && !types.SetEquals(["array"]) || mode == "value" && types.Contains("array")) Fail(emit, "inapplicable-emission-mode");
        if (mode == "join")
        { if (emit.Get("separator") is not { Text.Length: > 0 } separator || separator.Text.Contains('\0')) Fail(emit, "separator-required"); }
        else if (emit.Get("separator") is not null) Fail(emit, "inapplicable-separator");
        if (emit.Get("negative-name") is not null && mode != "flag") Fail(emit, "inapplicable-negative-name");
        if (emit.Get("true-value") is not null || emit.Get("false-value") is not null)
        {
            if (mode != "value" || !types.Contains("boolean") || emit.Get("true-value") is null || emit.Get("false-value") is null) Fail(emit, "inapplicable-boolean-values");
            if (emit.Get("true-value")!.Text == emit.Get("false-value")!.Text) Fail(emit, "indistinguishable-booleans");
        }
    }

    internal static HashSet<string> Types(RecipeValue d) => d.Get("any-of") is { } any
        ? any.Items.SelectMany(x => Types(x)).ToHashSet(StringComparer.Ordinal) : new([d.Get("type")!.Text!], StringComparer.Ordinal);
    private static void Condition(RecipeValue c, RecipeValue definition)
    {
        Object(c, "present equals contains");
        if (c.Fields.Count != 1) Fail(c, "invalid-condition");
        if (c.Get("present") is { } present)
        { if (present.Kind != RecipeKind.Boolean) Fail(present, "invalid-condition"); return; }
        var operand = c.Get("equals") ?? c.Get("contains")!;
        if (operand.Kind is RecipeKind.Null or RecipeKind.Map or RecipeKind.Sequence) Fail(operand, "invalid-condition");
        var alternatives = ValueAlternatives(definition);
        if (c.Get("contains") is not null) alternatives = alternatives.Where(x => x.Get("type")?.Text == "array").SelectMany(x => ValueAlternatives(x.Get("items")!));
        if (!alternatives.Any(x => TypeMatches(x.Get("type")!.Text!, operand))) Fail(c, "condition-type-mismatch");
    }
    private static IEnumerable<RecipeValue> ValueAlternatives(RecipeValue d) => d.Get("any-of") is { } any ? any.Items.SelectMany(ValueAlternatives) : [d];

    internal static bool Matches(RecipeValue d, RecipeValue value, bool checkEnum = true)
    {
        if (d.Get("any-of") is { } any) return any.Items.Any(x => Matches(x, value));
        if (!TypeMatches(d.Get("type")!.Text!, value)) return false;
        if (value.Kind == RecipeKind.Sequence && !value.Items.All(x => Matches(d.Get("items")!, x))) return false;
        if (checkEnum && d.Get("enum") is { } choices && !choices.Items.Any(x => Equal(x, value))) return false;
        if (d.Get("minimum") is { } min && CompareNumber(value.Text!, min.Text!) < 0 ||
            d.Get("maximum") is { } max && CompareNumber(value.Text!, max.Text!) > 0) return false;
        if (d.Get("pattern") is { } pattern)
        {
            try { if (!Pattern(pattern).IsMatch(value.Text!)) return false; }
            catch (RegexMatchTimeoutException) { throw value.Error("pattern-inconclusive", "validation", "La validación regex agotó su plazo."); }
        }
        return true;
    }
    private static bool TypeMatches(string type, RecipeValue value) => type switch
    {
        "string" => value.Kind == RecipeKind.String, "boolean" => value.Kind == RecipeKind.Boolean,
        "integer" => value.Kind == RecipeKind.Integer, "number" => value.Kind is RecipeKind.Integer or RecipeKind.Number,
        "array" => value.Kind == RecipeKind.Sequence, _ => false
    };
    internal static bool Equal(RecipeValue a, RecipeValue b) => a.Kind is RecipeKind.Integer or RecipeKind.Number && b.Kind is RecipeKind.Integer or RecipeKind.Number
        ? CompareNumber(a.Text!, b.Text!) == 0 : a.Kind == b.Kind && (a.Kind == RecipeKind.Sequence
            ? a.Items.Count == b.Items.Count && a.Items.Zip(b.Items).All(x => Equal(x.First, x.Second)) : a.Text == b.Text);

    // Compare decimal/scientific numbers exactly, including integers beyond 2^53.
    internal static int CompareNumber(string a, string b)
    {
        static (int Sign, string Digits, BigInteger Position) Parts(string text)
        {
            var sign = text.StartsWith('-') ? -1 : 1;
            text = text.TrimStart('+', '-');
            var at = text.IndexOfAny(['e', 'E']);
            var exponent = at < 0 ? BigInteger.Zero : BigInteger.Parse(text[(at + 1)..], CultureInfo.InvariantCulture);
            var mantissa = at < 0 ? text : text[..at];
            var point = mantissa.IndexOf('.');
            var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
            if (digits.Length == 0) return (0, "", 0);
            return (sign, digits.TrimEnd('0'), exponent - (point < 0 ? 0 : mantissa.Length - point - 1) + digits.Length);
        }
        var x = Parts(a); var y = Parts(b);
        if (x.Sign != y.Sign) return x.Sign.CompareTo(y.Sign);
        if (x.Sign == 0) return 0;
        var order = x.Position.CompareTo(y.Position);
        if (order == 0)
            for (var i = 0; i < Math.Max(x.Digits.Length, y.Digits.Length) && order == 0; i++)
                order = (i < x.Digits.Length ? x.Digits[i] : '0').CompareTo(i < y.Digits.Length ? y.Digits[i] : '0');
        return order * x.Sign;
    }

    internal static BigInteger Build(RecipeValue build)
    {
        if (!Regex.IsMatch(build.Text!, "^b[0-9]+$", RegexOptions.CultureInvariant)) Fail(build, "invalid-build");
        return BigInteger.Parse(build.Text![1..], CultureInfo.InvariantCulture);
    }
    private static Regex Pattern(RecipeValue pattern)
    {
        var text = pattern.Text!;
        var whitespace = false;
        var groups = new Stack<bool>();
        for (var i = 0; i < text.Length; i++)
        {
            if (whitespace && text[i] == '#')
            { while (i < text.Length && text[i] != '\n') i++; continue; }
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                var next = text[++i];
                if (next is >= '1' and <= '9' || next == 'k') Fail(pattern, "invalid-pattern");
                continue;
            }
            if (text[i] == '[')
            {
                var depth = 1;
                i++;
                if (i < text.Length && text[i] == '^') i++;
                if (i < text.Length && text[i] == ']') i++;
                for (; i < text.Length; i++)
                {
                    if (text[i] == '\\') { i++; continue; }
                    if (text[i] == '[' && i > 0 && text[i - 1] == '-') depth++;
                    if (text[i] == ']' && --depth == 0) break;
                }
                continue;
            }
            if (text[i] == ')' && groups.Count > 0) { whitespace = groups.Pop(); continue; }
            if (text[i] != '(') continue;
            if (i + 2 < text.Length && text[i + 1] == '?')
            {
                var next = text[i + 2];
                if (next is '=' or '!' || next == '<' && i + 3 < text.Length && text[i + 3] is '=' or '!') Fail(pattern, "invalid-pattern");
                if (next == '#') { i = text.IndexOf(')', i + 3); if (i < 0) break; continue; }
                var cursor = i + 2;
                var disabled = false;
                var optionWhitespace = whitespace;
                while (cursor < text.Length && "imnsx-".Contains(text[cursor]))
                {
                    if (text[cursor] == '-') disabled = true;
                    if (text[cursor] == 'x') optionWhitespace = !disabled;
                    cursor++;
                }
                if (cursor > i + 2 && cursor < text.Length && text[cursor] is ')' or ':')
                {
                    if (text[cursor] == ':') groups.Push(whitespace);
                    whitespace = optionWhitespace; i = cursor; continue;
                }
            }
            groups.Push(whitespace);
        }
        try { return new(text, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { throw pattern.Error("invalid-pattern", "schema", "Regex inválida o construcción no permitida."); }
    }
    internal static void Name(string name, RecipeValue at)
    { if (!Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9-]*$", RegexOptions.CultureInvariant)) Fail(at, "invalid-argument-name"); }
    internal static void SafeIniName(string name, RecipeValue at)
    { if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Any(char.IsControl) || name.IndexOfAny(['[', ']', '=', ';', '#']) >= 0) Fail(at, "invalid-ini-name"); }
    internal static void Object(RecipeValue value, string? keys = null)
    {
        if (value.Kind != RecipeKind.Map) Fail(value, "map-required");
        if (keys is not null && value.Fields.Keys.Except(keys.Split(' '), StringComparer.Ordinal).Any()) Fail(value, "unknown-key");
    }
    private static void Sequence(RecipeValue value, bool nonempty = false)
    { if (value.Kind != RecipeKind.Sequence || nonempty && value.Items.Count == 0) Fail(value, "invalid-schema-list"); }
    private static void Text(RecipeValue value)
    { if (value.Kind != RecipeKind.String) Fail(value, "invalid-schema-text"); }
    internal static void Fail(RecipeValue at, string code) => throw at.Error(code, "schema", "Definición de argumentos inválida.");

    private static RecipeValue Json(JsonElement element, RecipeOrigin origin, ref int count)
    {
        if (++count > 100_000) throw new RecipeException("schema-limit", "schema", origin, "Catálogo demasiado grande.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var map = new Dictionary<string, RecipeValue>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                var child = Json(property.Value, origin with { Path = origin.Path + "[" + System.Text.Json.Nodes.JsonValue.Create(property.Name)!.ToJsonString() + "]" }, ref count);
                if (!map.TryAdd(property.Name, child)) Fail(child, "duplicate-key");
            }
            return RecipeValue.Map(origin, map);
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            var items = new List<RecipeValue>();
            foreach (var item in element.EnumerateArray()) items.Add(Json(item, origin with { Path = origin.Path + "[" + items.Count + "]" }, ref count));
            return new(RecipeKind.Sequence, origin, items: items);
        }
        var raw = element.GetRawText();
        var kind = element.ValueKind switch
        {
            JsonValueKind.String => RecipeKind.String, JsonValueKind.Null => RecipeKind.Null,
            JsonValueKind.True or JsonValueKind.False => RecipeKind.Boolean,
            _ => RecipeValue.IsInteger(raw) ? RecipeKind.Integer : RecipeKind.Number
        };
        if (kind == RecipeKind.Number && !RecipeValue.IsNumber(raw)) throw new RecipeException("non-finite-number", "schema", origin, "Número no finito.");
        return new(kind, origin, kind == RecipeKind.String ? element.GetString() : raw,
            kind == RecipeKind.String ? RecipeStyle.SingleQuoted : RecipeStyle.Plain);
    }
}
