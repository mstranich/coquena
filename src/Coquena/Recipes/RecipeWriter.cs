using System.Globalization;
using System.Text;

namespace Coquena.Recipes;

// Value written by a point edit. The writer decides block or flow layout from the
// place it lands in the file; callers only describe the content.
internal abstract record EditNode
{
    internal static EditNode Null { get; } = new EditScalar("null");
}

// A scalar already rendered as YAML text.
internal sealed record EditScalar(string Text) : EditNode;

internal sealed record EditMap(IReadOnlyList<KeyValuePair<string, EditNode>> Fields) : EditNode;

internal sealed record EditSequence(IReadOnlyList<EditNode> Items) : EditNode;

// Block text copied from the file, with indentation relative to its first line.
// Fallback renders the same content where the text cannot be copied (flow context).
internal sealed record EditText(IReadOnlyList<string> Lines, EditNode Fallback) : EditNode;

// Removes an entry: deletes the key in its own layer, writes null over an inherited one.
// A map left empty goes too (absent and empty merge alike), except the entity at depth
// `Entity`, whose absence would delete it: unsetting the last argument keeps the preset.
internal sealed record EditRemove(int Entity = 0) : EditNode;

internal static class RecipeWriter
{
    internal static EditMap Map(params (string Key, EditNode Value)[] fields) =>
        new(fields.Select(field => KeyValuePair.Create(field.Key, field.Value)).ToArray());

    internal static EditNode String(string text) => new EditScalar(Scalar(text));

    // Rebuilds a read value keeping its scalar kinds and interpolation semantics.
    internal static EditNode From(RecipeValue value) => value.Kind switch
    {
        RecipeKind.Map => new EditMap(value.Fields.Select(p => KeyValuePair.Create(p.Key, From(p.Value))).ToArray()),
        RecipeKind.Sequence => new EditSequence(value.Items.Select(From).ToArray()),
        RecipeKind.String => new EditScalar(value.Style is RecipeStyle.SingleQuoted or RecipeStyle.Block
            ? Uninterpolated(value.Text!) : Scalar(value.Text!)),
        RecipeKind.Null => EditNode.Null,
        _ => new EditScalar(value.Text!)
    };

    // A string whose `$` must stay literal: single quotes do not interpolate.
    internal static string Uninterpolated(string text) =>
        text.Contains('\n') || text.Contains('\r') ? Quoted(text.Replace("$", "$$", StringComparison.Ordinal)) : "'" + text.Replace("'", "''") + "'";

    // Plain when the text reads back as the same string; double quotes otherwise.
    internal static string Scalar(string text) => Plain(text) ? text : Quoted(text);

    internal static string Key(string key) => Plain(key) ? key : Quoted(key);

    private static bool Plain(string text)
    {
        if (text.Length == 0 || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])) return false;
        if (text is "null" or "Null" or "NULL" or "~" or "true" or "false" or "True" or "False" or "TRUE" or "FALSE" or "<<") return false;
        if (RecipeValue.IsInteger(text) || RecipeValue.IsNumber(text) || RecipeValue.HasNumberSyntax(text)) return false;
        var bare = text.TrimStart('+', '-');
        if (bare.StartsWith('.') && (bare.Equals(".nan", StringComparison.OrdinalIgnoreCase) || bare.Equals(".inf", StringComparison.OrdinalIgnoreCase))) return false;
        if ("-?:,[]{}#&*!|>'\"%@`".Contains(text[0])) return false;
        if (text.EndsWith(':') || text.Contains(": ", StringComparison.Ordinal) || text.Contains(" #", StringComparison.Ordinal)) return false;
        return !text.Any(c => char.IsControl(c) || c is ',' or '[' or ']' or '{' or '}');
    }

    private static string Quoted(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in text)
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ when char.IsControl(c) => "\\x" + ((int)c).ToString("x2", CultureInfo.InvariantCulture),
                _ => c.ToString()
            });
        return builder.Append('"').ToString();
    }

    // Text after "key:" in block layout; children start at `indent`.
    internal static string Block(EditNode node, int indent, int step, string newline) => node switch
    {
        EditScalar scalar => " " + scalar.Text,
        EditMap { Fields.Count: 0 } => " {}",
        EditSequence { Items.Count: 0 } => " []",
        EditMap map => string.Concat(map.Fields.Select(field =>
            newline + new string(' ', indent) + Key(field.Key) + ":" + Block(field.Value, indent + step, step, newline))),
        EditSequence sequence => string.Concat(sequence.Items.Select(item =>
            newline + new string(' ', indent) + "-" + Item(item, indent + 2, step, newline))),
        EditText text => string.Concat(text.Lines.Select(line => newline + (line.Length == 0 ? "" : new string(' ', indent) + line))),
        _ => throw new InvalidOperationException("Valor de edición no representable.")
    };

    // A sequence item: a map starts on the dash line and continues aligned after it.
    private static string Item(EditNode node, int indent, int step, string newline)
    {
        if (node is not EditMap { Fields.Count: > 0 } map) return node is EditText text ? Block(text.Fallback, indent, step, newline) : Block(node, indent, step, newline);
        var lines = Block(map, indent, step, newline);
        return " " + lines[(newline.Length + indent)..];
    }

    // Single-line layout for values that land inside an existing flow collection.
    internal static string Flow(EditNode node) => node switch
    {
        EditScalar scalar => scalar.Text,
        EditMap map => "{" + string.Join(", ", map.Fields.Select(field => Key(field.Key) + ": " + Flow(field.Value))) + "}",
        EditSequence sequence => "[" + string.Join(", ", sequence.Items.Select(Flow)) + "]",
        EditText text => Flow(text.Fallback),
        _ => throw new InvalidOperationException("Valor de edición no representable.")
    };
}
