using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Coquena.Recipes;

public enum RecipeKind { Null, String, Integer, Number, Boolean, Map, Sequence }
public enum RecipeStyle { Plain, Quoted, SingleQuoted, Block }
public sealed record RecipeOrigin(string File, int Line, int Column, string Path);

public sealed class RecipeException(string code, string phase, RecipeOrigin origin, string detail)
    : Exception($"{origin.File}:{origin.Line}:{origin.Column} {origin.Path} [{phase}/{code}] {detail}")
{
    public string Code { get; } = code;
    public string Phase { get; } = phase;
    public RecipeOrigin Origin { get; } = origin;
}

// Values remain typed and carry their source through composition. Rendering is
// explicitly redacted; callers must not serialize scalar contents into shared locks.
public sealed class RecipeValue
{
    public RecipeKind Kind { get; }
    public string? Text { get; }
    public RecipeOrigin Origin { get; }
    public RecipeStyle Style { get; }
    public bool Sensitive { get; }
    // Schema validation (046) can convert only this kind of interpolated scalar.
    public bool CanConvert { get; }
    public IReadOnlyDictionary<string, RecipeValue> Fields { get; }
    public IReadOnlyList<RecipeValue> Items { get; }

    internal RecipeValue(RecipeKind kind, RecipeOrigin origin, string? text = null,
        RecipeStyle style = RecipeStyle.Plain, bool sensitive = false, bool canConvert = false,
        IDictionary<string, RecipeValue>? fields = null, IEnumerable<RecipeValue>? items = null)
    {
        Kind = kind; Origin = origin; Text = text; Style = style;
        Sensitive = sensitive; CanConvert = canConvert;
        Fields = new ReadOnlyDictionary<string, RecipeValue>(fields is null ? new Dictionary<string, RecipeValue>(StringComparer.Ordinal) : new Dictionary<string, RecipeValue>(fields, StringComparer.Ordinal));
        Items = Array.AsReadOnly(items?.ToArray() ?? []);
    }

    public RecipeValue? Get(string key) => Fields.GetValueOrDefault(key);

    public RecipeValue ConvertInterpolatedScalar(string type)
    {
        if (!CanConvert || type == "string") return this;
        RecipeKind kind;
        if (type == "boolean" && Text is "true" or "false") kind = RecipeKind.Boolean;
        else if (type == "integer" && IsInteger(Text!)) kind = RecipeKind.Integer;
        else if (type == "number" && IsNumber(Text!)) kind = IsInteger(Text!) ? RecipeKind.Integer : RecipeKind.Number;
        else throw Error("invalid-type", "validation", "La expansión no tiene el tipo escalar declarado.");
        return new(kind, Origin, Text, Style, Sensitive);
    }

    public JsonNode? Redacted()
    {
        if (Sensitive && Kind is not (RecipeKind.Map or RecipeKind.Sequence)) return JsonValue.Create("<redacted>");
        if (Kind == RecipeKind.Map)
        {
            var map = new JsonObject();
            foreach (var field in Fields) map[field.Key] = field.Value.Redacted();
            return map;
        }
        if (Kind == RecipeKind.Sequence) return new JsonArray(Items.Select(x => x.Redacted()).ToArray());
        return Kind switch
        {
            RecipeKind.Null => null,
            RecipeKind.Boolean => JsonValue.Create(Text == "true"),
            RecipeKind.Integer or RecipeKind.Number => JsonNode.Parse(NormalizeNumber(Text!)),
            _ => JsonValue.Create(Text)
        };
    }

    public override string ToString() => Redacted()?.ToJsonString() ?? "null";
    internal RecipeException Error(string code, string phase, string detail) => new(code, phase, Origin, detail);
    internal RecipeValue WithFields(IDictionary<string, RecipeValue> fields) => new(RecipeKind.Map, Origin, fields: fields);
    internal static RecipeValue Map(RecipeOrigin origin, IDictionary<string, RecipeValue>? fields = null) => new(RecipeKind.Map, origin, fields: fields);
    internal static bool IsInteger(string text) => System.Text.RegularExpressions.Regex.IsMatch(text, @"^[+-]?[0-9]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    internal static bool HasNumberSyntax(string text) => System.Text.RegularExpressions.Regex.IsMatch(text, @"^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    internal static bool IsNumber(string text) => HasNumberSyntax(text)
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number);
    private static string NormalizeNumber(string text)
    {
        if (IsInteger(text)) return BigInteger.Parse(text, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        var sign = text.StartsWith('-') ? "-" : "";
        text = text.TrimStart('+', '-');
        var exponentAt = text.IndexOfAny(['e', 'E']);
        var exponent = exponentAt < 0 ? "" : text[exponentAt..];
        var mantissa = exponentAt < 0 ? text : text[..exponentAt];
        var point = mantissa.IndexOf('.');
        var integer = (point < 0 ? mantissa : mantissa[..point]).TrimStart('0');
        if (integer.Length == 0) integer = "0";
        var fraction = point < 0 ? "" : mantissa[point..];
        if (fraction == ".") fraction = ".0";
        return sign + integer + fraction + exponent;
    }
}
