using System.Text;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Coquena.Recipes;

// Point edits over the YAML text: only the touched entry changes; comments, BOM,
// line endings and the rest of the file stay as written.
internal static class RecipeEdit
{
    internal static byte[] Propose(RecipeSelection selection, Dictionary<string, byte[]> inputs, string file,
        string[] keys, EditNode value)
    {
        var original = inputs[file];
        var text = RecipeYaml.ReadText(file, false, original);
        if (value is EditRemove && Inherited(selection, inputs, file, keys)) value = EditNode.Null;
        text = value is EditRemove remove ? Remove(file, original, text, keys, remove.Entity) : Set(file, original, text, keys, value);

        var (encoding, offset) = RecipeYaml.TextEncoding(original, false);
        var candidate = original.Take(offset).Concat(encoding.GetBytes(text)).ToArray();
        var changed = new Dictionary<string, byte[]>(inputs, inputs.Comparer) { [file] = candidate };
        var candidateSelection = new RecipeSelection(selection.Base, selection.Files, selection.Base, selection.EnvFiles)
        {
            CapturedFiles = changed, ImportedScope = selection.ImportedScope,
            ImportedPathBases = selection.ImportedPathBases, FilterUnusedModels = selection.FilterUnusedModels
        };
        // The edit keeps assignments that a schema change would invalidate as impact, not as
        // a failed full validation; every problem found is reported together.
        RecipeValidation.Require(candidateSelection, stage: RecipeValidation.Edit);
        return candidate;
    }

    // The value to write for a cloned entry; the combination must be valid first. An entry written in block style in a single
    // layer is copied as text, comments included; one assembled from several layers is
    // rebuilt from the combined source values.
    internal static EditNode Copy(RecipeSelection selection, IReadOnlyDictionary<string, byte[]> inputs, string[] keys)
    {
        // Precondition: the recipe the user sees, drafts included, must be valid.
        var (recipe, _) = RecipeValidation.Require(selection, inputs);
        var captured = selection.Files.ToDictionary(path => path, path => inputs.TryGetValue(path, out var draft) ? draft : RecipeYaml.ReadBytes(path), StringComparer.OrdinalIgnoreCase);
        RecipeValue? merged = recipe.SourceRoot;
        foreach (var key in keys) merged = merged?.Get(key);
        if (merged is null) throw recipe.Root.Error("unknown-entry", "edit", "No existe la entrada que se quiere copiar.");
        var fallback = RecipeWriter.From(merged);
        var owners = selection.Files.Where(path => Lookup(path, captured[path], keys) is not null).ToArray();
        if (owners.Length != 1) return fallback;

        var owner = owners[0];
        var text = RecipeYaml.ReadText(owner, false, captured[owner]);
        var parser = Parser(owner, captured[owner]);
        var node = Walk(parser.Root, keys)!;
        var path = Path(keys);
        if (parser.Yaml.Spans![path].Start is not MappingStart { Style: MappingStyle.Block } || node.Fields.Count == 0) return fallback;
        var start = LineStart(text, checked((int)parser.Yaml.Spans[path].Start.Start.Index));
        var end = LineEnd(text, EndOf(parser.Yaml, node));
        var lines = text[start..end].Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        var indent = lines.Where(line => line.Trim().Length != 0).Min(line => line.Length - line.TrimStart(' ').Length);
        if (lines.Any(line => line.Trim().Length != 0 && line.Length - line.TrimStart(' ').Length < indent || line.Contains('\t'))) return fallback;
        return new EditText(lines.Select(line => line.Trim().Length == 0 ? "" : line[indent..]).ToArray(), fallback);
    }

    private static string Set(string file, byte[] original, string text, string[] keys, EditNode value)
    {
        var (yaml, root) = Parser(file, original);
        var newline = Newline(text);
        var step = Step(text);
        var node = root;
        var path = "$";
        RecipeException Ambiguous() => node.Error("ambiguous-edit", "edit", "Edite manualmente la asignación indicada.");

        for (var i = 0; i < keys.Length; i++)
        {
            var child = node.Get(keys[i]);
            if (child is null)
            {
                for (var j = keys.Length - 1; j > i; j--) value = RecipeWriter.Map((keys[j], value));
                var key = RecipeWriter.Key(keys[i]);
                if (yaml.FirstKeys!.TryGetValue(path, out var first))
                {
                    var index = checked((int)first.Start.Index);
                    if (yaml.Spans![path].Start is MappingStart { Style: MappingStyle.Flow })
                        return text.Insert(index, key + ": " + RecipeWriter.Flow(value) + ", ");
                    var lineStart = LineStart(text, index);
                    var indent = text[lineStart..index];
                    if (indent.Any(c => c != ' ')) throw Ambiguous();
                    // New entries go after the last one, so a clone follows its siblings.
                    var after = LineEnd(text, EndOf(yaml, node));
                    return text.Insert(after, newline + indent + key + ":" + RecipeWriter.Block(value, indent.Length + step, step, newline));
                }
                if (yaml.Spans!.TryGetValue(path, out var empty))
                {
                    if (empty.Start is not (Scalar or MappingStart { Style: MappingStyle.Flow })) throw Ambiguous();
                    var entry = RecipeWriter.Map((keys[i], value));
                    if (path == "$")
                        return Replace(text, checked((int)empty.Start.Start.Index), EndOf(yaml, node),
                            RecipeWriter.Block(entry, 0, step, newline)[newline.Length..]);
                    if (InFlow(yaml, path)) return Replace(text, checked((int)empty.Start.Start.Index), EndOf(yaml, node), RecipeWriter.Flow(entry));
                    return ReplaceAfterKey(yaml, text, path, node, entry, step, newline) ?? throw Ambiguous();
                }
                if (path == "$")
                    return text + (text.Length == 0 || text.EndsWith('\n') ? "" : newline) +
                        RecipeWriter.Block(RecipeWriter.Map((keys[i], value)), 0, step, newline)[newline.Length..] + newline;
                throw Ambiguous();
            }
            node = child;
            path += "[" + Quote(keys[i]) + "]";
            if (i == keys.Length - 1)
            {
                var span = yaml.Spans![path];
                var last = EndOf(yaml, node);
                var range = text[checked((int)span.Start.Start.Index)..last];
                if (range.Contains('\n') && range.Contains('#')) throw Ambiguous();
                if (InFlow(yaml, path)) return Replace(text, checked((int)span.Start.Start.Index), last, RecipeWriter.Flow(value));
                return ReplaceAfterKey(yaml, text, path, node, value, step, newline) ?? throw Ambiguous();
            }
            if (node.Kind != RecipeKind.Map) throw Ambiguous();
        }
        throw Ambiguous();
    }

    // Deletes the entry; an entry left alone in its map takes its parent with it, since an
    // absent map inherits while an empty one would read as null and unassign.
    // With keepLines the removed text leaves its line breaks, so later positions do not move.
    private static string Remove(string file, byte[] original, string text, string[] keys, int entity, bool keepLines = false)
    {
        string Cut(int from, int to) => text[..from] + (keepLines ? string.Concat(text[from..to].Where(c => c is '\n' or '\r')) : "") + text[to..];
        var (yaml, root) = Parser(file, original);
        var depth = keys.Length;
        while (depth > 1 && depth - 1 != entity && Walk(root, keys[..(depth - 1)]) is { Fields.Count: 1 }) depth--;
        // The entity stays: emptied, it is written as `{}` because a bare key reads as null.
        if (depth - 1 == entity && entity > 0 && Walk(root, keys[..entity]) is { Fields.Count: 1 })
            return Set(file, original, text, keys[..entity], new EditMap([]));
        var target = keys[..depth];
        var node = Walk(root, target) ?? throw root.Error("ambiguous-edit", "edit", "La entrada no está en el archivo editado.");
        var path = Path(target);
        var key = yaml.KeySpans![path];
        var start = checked((int)key.Start.Index);
        var end = EndOf(yaml, node);
        if (InFlow(yaml, path))
        {
            var after = end;
            while (after < text.Length && text[after] == ' ') after++;
            if (after < text.Length && text[after] == ',')
            {
                after++;
                while (after < text.Length && text[after] == ' ') after++;
                end = after;
            }
            else
            {
                var before = start;
                while (before > 0 && char.IsWhiteSpace(text[before - 1])) before--;
                if (before > 0 && text[before - 1] == ',') start = before - 1;
            }
            return Cut(start, end);
        }
        var lineStart = LineStart(text, start);
        if (text[lineStart..start].Any(c => c != ' ')) throw node.Error("ambiguous-edit", "edit", "Edite manualmente la asignación indicada.");
        end = LineEnd(text, end);
        if (end < text.Length && text[end] == '\r') end++;
        if (end < text.Length && text[end] == '\n') end++;
        return Cut(lineStart, end);
    }

    // The file without the entry at `keys`, so validation can go on past a located
    // problem; null when the entry cannot be isolated.
    internal static byte[]? Prune(string file, byte[] bytes, string[] keys)
    {
        try
        {
            var text = RecipeYaml.ReadText(file, false, bytes);
            // Keep the line count, so later problems point at the lines the user sees.
            var pruned = Remove(file, bytes, text, keys, 0, keepLines: true);
            if (pruned == text) return null;
            var (encoding, offset) = RecipeYaml.TextEncoding(bytes, false);
            return bytes.Take(offset).Concat(encoding.GetBytes(pruned)).ToArray();
        }
        catch (RecipeException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    // Rewrites "key: value" from the end of the key, so a block value never leaves
    // the key alone on its line.
    private static string? ReplaceAfterKey(RecipeYaml yaml, string text, string path, RecipeValue node, EditNode value, int step, string newline)
    {
        if (!yaml.KeySpans!.TryGetValue(path, out var key)) return null;
        var keyStart = checked((int)key.Start.Index);
        var keyEnd = checked((int)key.End.Index);
        var colon = text.IndexOf(':', keyEnd);
        if (colon < 0) return null;
        // An empty value has no text of its own: write right after the colon.
        var end = yaml.Spans![path].Start is Scalar { Value.Length: 0, Style: ScalarStyle.Plain } ? colon + 1 : EndOf(yaml, node);
        if (colon >= end) return null;
        var indent = keyStart - LineStart(text, keyStart);
        return text[..colon] + ":" + RecipeWriter.Block(value, indent + step, step, newline) + text[end..];
    }

    private static bool Inherited(RecipeSelection selection, Dictionary<string, byte[]> inputs, string file, string[] keys)
    {
        var below = selection.Files.TakeWhile(path => !path.Equals(file, StringComparison.OrdinalIgnoreCase));
        return below.Any(path => Lookup(path, inputs.TryGetValue(path, out var bytes) ? bytes : RecipeYaml.ReadBytes(path), keys) is not null);
    }

    private static RecipeValue? Lookup(string file, byte[] bytes, string[] keys) => Walk(new RecipeYaml(file, bytes).Read(), keys);

    private static RecipeValue? Walk(RecipeValue? node, string[] keys)
    {
        foreach (var key in keys) node = node?.Kind == RecipeKind.Map ? node.Get(key) : null;
        return node;
    }

    private static (RecipeYaml Yaml, RecipeValue Root) Parser(string file, byte[] bytes)
    {
        var yaml = new RecipeYaml(file, bytes) { Spans = new(), FirstKeys = new(), KeySpans = new() };
        return (yaml, yaml.Read());
    }

    private static string Path(string[] keys) => "$" + string.Concat(keys.Select(key => "[" + Quote(key) + "]"));

    private static bool InFlow(RecipeYaml yaml, string path)
    {
        var parent = path == "$" ? null : path[..path.LastIndexOf('[')];
        return parent is not null && yaml.Spans!.TryGetValue(parent, out var span) && span.Start is MappingStart { Style: MappingStyle.Flow };
    }

    private static int EndOf(RecipeYaml yaml, RecipeValue item)
    {
        var span = yaml.Spans![item.Origin.Path];
        return span.Start switch
        {
            MappingStart { Style: MappingStyle.Block } when item.Fields.Count != 0 => EndOf(yaml, item.Fields.Values.Last()),
            SequenceStart { Style: SequenceStyle.Block } when item.Items.Count != 0 => EndOf(yaml, item.Items[^1]),
            // Flow end events point to the closing delimiter.
            MappingStart { Style: MappingStyle.Flow } or SequenceStart { Style: SequenceStyle.Flow } => checked((int)span.End.End.Index + 1),
            _ => checked((int)span.End.End.Index)
        };
    }

    private static string Replace(string text, int start, int end, string replacement)
    {
        if (start > 0 && text[start - 1] == ':') replacement = " " + replacement;
        return text[..start] + replacement + text[end..];
    }

    private static int LineStart(string text, int index) => index == 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;

    // End of the line holding `index`, before its line break; a trailing comment stays with the entry.
    private static int LineEnd(string text, int index)
    {
        // A block scalar ends after its own line break.
        if (index > 0 && index <= text.Length && text[index - 1] == '\n') index--;
        var next = text.IndexOf('\n', Math.Min(index, text.Length));
        if (next < 0) return text.Length;
        return next > 0 && text[next - 1] == '\r' ? next - 1 : next;
    }

    private static string Newline(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    // Indentation step used by the file: the smallest indentation of a content line.
    private static int Step(string text)
    {
        var step = text.Split('\n').Select(line => line.TrimEnd('\r'))
            .Where(line => line.Trim().Length != 0 && !line.TrimStart().StartsWith('#'))
            .Select(line => line.Length - line.TrimStart(' ').Length).Where(width => width > 0).DefaultIfEmpty(2).Min();
        return Math.Clamp(step, 1, 8);
    }

    internal static void Save(RecipeSelection selection, Dictionary<string, byte[]> inputs, string file, string[] keys, EditNode value)
    {
        var candidate = Propose(selection, inputs, file, keys, value);
        var locks = new Stack<Mutex>();
        var temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(candidate); stream.Flush(true); }
            foreach (var input in inputs.Keys.Order(StringComparer.OrdinalIgnoreCase))
            {
                var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input.ToUpperInvariant())));
                var mutex = new Mutex(false, "CoquenaRecipeEdit-" + id);
                try { try { mutex.WaitOne(); } catch (AbandonedMutexException) { } locks.Push(mutex); }
                catch { mutex.Dispose(); throw; }
            }
            foreach (var input in inputs)
                if (!RecipeYaml.ReadBytes(input.Key).AsSpan().SequenceEqual(input.Value))
                    throw new RecipeException("edit-conflict", "edit", new(file, 1, 1, "$"), "Cambió una capa seleccionada; vuelva a revisar la edición.");
            if (!candidate.AsSpan().SequenceEqual(inputs[file])) File.Replace(temporary, file, null);
        }
        finally
        {
            try { File.Delete(temporary); }
            finally { while (locks.TryPop(out var mutex)) { mutex.ReleaseMutex(); mutex.Dispose(); } }
        }
    }

    // Path segment quoting used by the reader's origin paths.
    internal static string Quote(string key) => JsonValue.Create(key)!.ToJsonString();
}
