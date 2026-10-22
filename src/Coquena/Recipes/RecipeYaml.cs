using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Coquena.Recipes;

internal sealed class RecipeYaml(string file)
{
    internal const int MaxBytes = 16 * 1024 * 1024;
    private int nodes;
    private IParser parser = null!;

    public RecipeValue Read()
    {
        try
        {
            parser = new Parser(new StringReader(ReadText(file, false)));
            parser.Consume<StreamStart>();
            if (parser.Accept<StreamEnd>(out _)) return RecipeValue.Map(new(file, 1, 1, "$"));
            var document = parser.Consume<DocumentStart>();
            if (document.Version is not null && (document.Version.Version.Major != 1 || document.Version.Version.Minor != 2))
                throw Fail("yaml-version", document, "$", "Sólo se acepta YAML 1.2.");
            var value = Node("$", 1);
            parser.Consume<DocumentEnd>();
            if (!parser.Accept<StreamEnd>(out _)) throw Fail("multiple-documents", parser.Current!, "$", "Se requiere un único documento.");
            if (value.Kind == RecipeKind.Null && value.Text == "") return RecipeValue.Map(value.Origin);
            if (value.Kind != RecipeKind.Map) throw value.Error("root-map-required", "yaml", "La raíz debe ser un mapa.");
            return value;
        }
        catch (YamlException e) { throw new RecipeException("yaml-syntax", "yaml", new(file, checked((int)e.Start.Line), checked((int)e.Start.Column), "$"), "YAML mal formado."); }
    }

    private RecipeValue Node(string path, int depth)
    {
        var current = parser.Current!;
        if (depth > 64 || ++nodes > 100_000) throw Fail("yaml-limit", current, path, "Se excede profundidad o cantidad de nodos.");
        var origin = new RecipeOrigin(file, checked((int)current.Start.Line), checked((int)current.Start.Column), path);
        if (current is AnchorAlias) throw Fail("yaml-alias", current, path, "Aliases no admitidos.");
        if (current is NodeEvent node && !node.Anchor.IsEmpty) throw Fail("yaml-anchor", current, path, "Anchors no admitidos.");
        if (current is Scalar scalar)
        {
            parser.MoveNext();
            var style = scalar.Style switch
            {
                ScalarStyle.SingleQuoted => RecipeStyle.SingleQuoted,
                ScalarStyle.DoubleQuoted => RecipeStyle.Quoted,
                ScalarStyle.Literal or ScalarStyle.Folded => RecipeStyle.Block,
                _ => RecipeStyle.Plain
            };
            var kind = RecipeKind.String;
            if (style == RecipeStyle.Plain)
            {
                if (scalar.Value is "" or "null" or "Null" or "NULL" or "~") kind = RecipeKind.Null;
                else if (scalar.Value is "true" or "false") kind = RecipeKind.Boolean;
                else if (RecipeValue.IsInteger(scalar.Value)) kind = RecipeKind.Integer;
                else if (RecipeValue.IsNumber(scalar.Value)) kind = RecipeKind.Number;
                else if (RecipeValue.HasNumberSyntax(scalar.Value) || scalar.Value.Equals(".nan", StringComparison.OrdinalIgnoreCase) ||
                    scalar.Value.TrimStart('+', '-').Equals(".inf", StringComparison.OrdinalIgnoreCase))
                    throw Fail("non-finite-number", scalar, path, "NaN e infinito no son válidos.");
            }
            if (!scalar.Tag.IsEmpty)
            {
                var tagged = scalar.Tag.Value switch
                {
                    "tag:yaml.org,2002:str" => RecipeKind.String,
                    "tag:yaml.org,2002:int" when RecipeValue.IsInteger(scalar.Value) => RecipeKind.Integer,
                    "tag:yaml.org,2002:float" when RecipeValue.IsNumber(scalar.Value) => RecipeKind.Number,
                    "tag:yaml.org,2002:bool" when scalar.Value is "true" or "false" => RecipeKind.Boolean,
                    "tag:yaml.org,2002:null" when kind == RecipeKind.Null => RecipeKind.Null,
                    _ => throw Fail("yaml-tag", scalar, path, "Tag personalizado o tipo explícito inválido.")
                };
                kind = tagged;
                // Explicit string tags must also survive subsequent validation.
                if (kind == RecipeKind.String && style == RecipeStyle.Plain) style = RecipeStyle.Quoted;
            }
            return new(kind, origin, scalar.Value, style);
        }
        if (current is MappingStart mapping)
        {
            Tag(mapping, "tag:yaml.org,2002:map", path);
            parser.MoveNext();
            var fields = new Dictionary<string, RecipeValue>(StringComparer.Ordinal);
            while (!parser.Accept<MappingEnd>(out _))
            {
                if (parser.Current is not Scalar key) throw Fail("yaml-complex-key", parser.Current!, path, "Las claves deben ser strings simples.");
                var keyNode = Node(path, depth + 1);
                if (keyNode.Kind != RecipeKind.String) throw keyNode.Error("yaml-key-type", "yaml", "La clave debe ser string.");
                if (key.Value == "<<") throw Fail("yaml-merge-key", key, path, "Merge keys no admitidas.");
                if (fields.ContainsKey(key.Value)) throw Fail("duplicate-key", key, path, "Clave duplicada.");
                fields.Add(key.Value, Node(path + "[" + System.Text.Json.Nodes.JsonValue.Create(key.Value)!.ToJsonString() + "]", depth + 1));
            }
            parser.MoveNext();
            return RecipeValue.Map(origin, fields);
        }
        if (current is SequenceStart sequence)
        {
            Tag(sequence, "tag:yaml.org,2002:seq", path);
            parser.MoveNext();
            var items = new List<RecipeValue>();
            while (!parser.Accept<SequenceEnd>(out _)) items.Add(Node(path + "[" + items.Count + "]", depth + 1));
            parser.MoveNext();
            return new(RecipeKind.Sequence, origin, items: items);
        }
        throw Fail("yaml-node", current, path, "Nodo YAML no admitido.");
    }

    private void Tag(NodeEvent node, string expected, string path)
    {
        if (!node.Tag.IsEmpty && node.Tag.Value != expected) throw Fail("yaml-tag", node, path, "Tag personalizado no admitido.");
    }
    private RecipeException Fail(string code, ParsingEvent item, string path, string detail) => new(code, "yaml", new(file, checked((int)item.Start.Line), checked((int)item.Start.Column), path), detail);

    internal static string ReadText(string path, bool utf8Only = true)
    {
        var origin = new RecipeOrigin(path, 1, 1, "$");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxBytes) throw new RecipeException("file-limit", "read", origin, "El archivo supera 16 MiB.");
            // Read through the limit even if a concurrently growing file had a smaller length.
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = stream.Read(buffer)) > 0)
            {
                if (bytes.Length + count > MaxBytes) throw new RecipeException("file-limit", "read", origin, "El archivo supera 16 MiB.");
                bytes.Write(buffer, 0, count);
            }
            var data = bytes.ToArray();
            var offset = data.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            Encoding encoding = new UTF8Encoding(false, true);
            if (!utf8Only && offset == 0)
            {
                if (data.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false, true); offset = 4; }
                else if (data.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, false, true); offset = 4; }
                else if (data.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
                else if (data.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
                else if (data.Length >= 4)
                {
                    if (data[0] == 0 && data[1] == 0 && data[2] == 0) encoding = new UTF32Encoding(true, false, true);
                    else if (data[1] == 0 && data[2] == 0 && data[3] == 0) encoding = new UTF32Encoding(false, false, true);
                    else if (data[0] == 0 && data[2] == 0) encoding = new UnicodeEncoding(true, false, true);
                    else if (data[1] == 0 && data[3] == 0) encoding = new UnicodeEncoding(false, false, true);
                }
            }
            return encoding.GetString(data, offset, data.Length - offset);
        }
        catch (DecoderFallbackException) { throw new RecipeException(utf8Only ? "invalid-utf8" : "invalid-encoding", "read", origin, "Codificación de texto inválida."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new RecipeException("file-unreadable", "read", origin, "No se pudo leer el archivo seleccionado."); }
    }
}
