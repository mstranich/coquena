using System.Text;
using System.Text.RegularExpressions;

namespace Coquena.Recipes;

internal sealed partial class RecipeVariables(IReadOnlyDictionary<string, string?> process)
{
    private readonly Dictionary<string, string?> values = new(StringComparer.Ordinal);
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
    private string? Lookup(string name) => process.TryGetValue(name, out var value) ? value : values.GetValueOrDefault(name);

    internal (string Text, bool Sensitive, bool Whole) Expand(string input, RecipeOrigin origin)
    {
        var sensitive = false;
        var whole = false;
        var text = Evaluate(input, origin, 0, ref sensitive, ref whole);
        return (text, sensitive, whole);
    }

    private string Evaluate(string input, RecipeOrigin origin, int depth, ref bool sensitive, ref bool whole)
    {
        if (depth > 32) throw Fail("interpolation-limit", origin, "La interpolación supera profundidad 32.");
        var result = new StringBuilder();
        for (var i = 0; i < input.Length;)
        {
            var start = i;
            if (input[i] != '$') { Append(result, input[i++].ToString(), origin); continue; }
            if (i + 1 < input.Length && input[i + 1] == '$') { Append(result, "$", origin); i += 2; continue; }
            if (i + 1 < input.Length && input[i + 1] == '(') { Append(result, "$", origin); i++; continue; }
            string name, op = "", argument = "";
            if (i + 1 < input.Length && input[i + 1] == '{')
            {
                var from = i + 2;
                var cursor = from;
                while (cursor < input.Length && (char.IsAsciiLetterOrDigit(input[cursor]) || input[cursor] == '_')) cursor++;
                name = input[from..cursor];
                if (!NamePattern().IsMatch(name)) throw Fail("interpolation-syntax", origin, "Nombre de variable inválido.");
                if (cursor >= input.Length) throw Fail("interpolation-syntax", origin, "Expansión sin cierre.");
                if (input[cursor] != '}')
                {
                    if (input[cursor] == ':') { op = ":"; cursor++; }
                    if (cursor >= input.Length || input[cursor] is not ('-' or '+' or '?'))
                        throw Fail("interpolation-syntax", origin, "Operador de interpolación inválido.");
                    op += input[cursor++];
                    var argumentStart = cursor;
                    var nested = 0;
                    for (; cursor < input.Length; cursor++)
                    {
                        if (input[cursor] == '$' && cursor + 1 < input.Length && input[cursor + 1] == '$') { cursor++; continue; }
                        if (input[cursor] == '$' && cursor + 1 < input.Length && input[cursor + 1] == '{')
                        {
                            nested++; cursor++;
                            if (nested + depth >= 32) throw Fail("interpolation-limit", origin, "La interpolación supera profundidad 32.");
                        }
                        else if (input[cursor] == '}' && nested-- == 0) break;
                    }
                    if (cursor >= input.Length) throw Fail("interpolation-syntax", origin, "Expansión sin cierre.");
                    argument = input[argumentStart..cursor];
                }
                i = cursor + 1;
            }
            else
            {
                var cursor = i + 1;
                if (cursor >= input.Length || !(char.IsAsciiLetter(input[cursor]) || input[cursor] == '_'))
                    throw Fail("interpolation-syntax", origin, "Expansión de variable inválida.");
                cursor++;
                while (cursor < input.Length && (char.IsAsciiLetterOrDigit(input[cursor]) || input[cursor] == '_')) cursor++;
                name = input[(i + 1)..cursor]; i = cursor;
            }
            sensitive = true;
            if (start == 0 && i == input.Length) whole = true;
            var value = Lookup(name);
            var missing = value is null || (op.StartsWith(':') && value.Length == 0);
            string replacement;
            if (op is "?" or ":?" && missing || op == "" && value is null)
                throw Fail("variable-required", origin, "Variable requerida: " + name + ".");
            if (op is "-" or ":-" && missing || op is "+" or ":+" && !missing)
            {
                var ignoredWhole = false;
                replacement = Evaluate(argument, origin, depth + 1, ref sensitive, ref ignoredWhole);
            }
            else if (op is "+" or ":+") replacement = "";
            else replacement = value ?? "";
            Append(result, replacement, origin);
        }
        return result.ToString();
    }

    internal void Load(string file)
    {
        var text = RecipeYaml.ReadText(file).Replace("\r\n", "\n");
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimStart();
            if (line.Length == 0 || line[0] == '#') continue;
            var origin = new RecipeOrigin(file, index + 1, lines[index].Length - line.Length + 1, "$env");
            var equal = line.IndexOf('=');
            var name = (equal < 0 ? line : line[..equal]).Trim();
            if (!NamePattern().IsMatch(name)) throw Fail("env-syntax", origin, "Nombre de variable env inválido.");
            origin = origin with { Path = "$env[" + name + "]" };
            if (equal < 0) { values[name] = null; continue; }
            var assigned = line[(equal + 1)..];
            var raw = assigned.TrimStart();
            var leadingSpace = raw.Length != assigned.Length;
            var single = raw.StartsWith('\'');
            var quoted = single || raw.StartsWith('"');
            string value;
            if (quoted)
            {
                var quote = raw[0];
                var output = new StringBuilder();
                var cursor = 1;
                while (true)
                {
                    if (cursor >= raw.Length)
                    {
                        if (!single || ++index >= lines.Length) throw Fail("env-syntax", origin, "String env sin cierre.");
                        Append(output, "\n", origin); raw = lines[index]; cursor = 0; continue;
                    }
                    var character = raw[cursor++];
                    if (character == quote) break;
                    if (!single && character == '\\')
                    {
                        if (cursor >= raw.Length) throw Fail("env-syntax", origin, "Escape env incompleto.");
                        character = raw[cursor++] switch
                        {
                            'n' => '\n', 'r' => '\r', 't' => '\t', '\\' => '\\', '"' => '"',
                            _ => throw Fail("env-syntax", origin, "Escape env inválido.")
                        };
                    }
                    Append(output, character.ToString(), origin);
                }
                var tail = raw[cursor..];
                if (tail.Length > 0 && (!char.IsWhiteSpace(tail[0]) || tail.TrimStart() is var rest && rest.Length > 0 && !rest.StartsWith('#')))
                    throw Fail("env-syntax", origin, "Contenido tras string env.");
                value = output.ToString();
            }
            else
            {
                var comment = -1;
                for (var cursor = 0; cursor < raw.Length; cursor++)
                    if (raw[cursor] == '#' && (cursor == 0 ? leadingSpace : char.IsWhiteSpace(raw[cursor - 1]))) { comment = cursor; break; }
                value = (comment < 0 ? raw : raw[..comment]).TrimEnd();
            }
            values[name] = single ? value : Expand(value, origin).Text;
        }
    }

    private static void Append(StringBuilder output, string text, RecipeOrigin origin)
    {
        if ((long)output.Length + text.Length > RecipeYaml.MaxBytes)
            throw Fail("interpolation-size", origin, "La expansión supera el límite de lectura.");
        output.Append(text);
    }
    private static RecipeException Fail(string code, RecipeOrigin origin, string detail) => new(code, "interpolation", origin, detail);
}
