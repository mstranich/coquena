using System.Text;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Coquena.Recipes;

internal static class RecipeEdit
{
    internal static byte[] Propose(RecipeSelection selection, Dictionary<string, byte[]> inputs, string file,
        string[] keys, string value)
    {
        var original = inputs[file];
        var text = RecipeYaml.ReadText(file, false, original);
        var parser = new RecipeYaml(file, original) { Spans = new(), FirstKeys = new() };
        var root = parser.Read();
        var node = root;
        var path = "$";
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        RecipeException Ambiguous() => node.Error("ambiguous-edit", "edit", "Edite manualmente la asignación indicada.");

        for (var i = 0; i < keys.Length; i++)
        {
            var childPath = path + "[" + Quote(keys[i]) + "]";
            var child = node.Get(keys[i]);
            if (child is null)
            {
                for (var j = keys.Length - 1; j > i; j--)
                    value = "{" + Quote(keys[j]) + ": " + value + "}";
                var assignment = Quote(keys[i]) + ": " + value;
                if (parser.FirstKeys!.TryGetValue(path, out var first))
                {
                    var index = checked((int)first.Start.Index);
                    if (parser.Spans![path].Start is MappingStart { Style: MappingStyle.Flow })
                        text = text.Insert(index, assignment + ", ");
                    else
                    {
                        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
                        var indent = text[lineStart..index];
                        if (indent.Any(c => c != ' ')) throw Ambiguous();
                        text = text.Insert(lineStart, indent + assignment + newline);
                    }
                }
                else if (parser.Spans!.TryGetValue(path, out var empty))
                {
                    if (empty.Start is not (Scalar or MappingStart { Style: MappingStyle.Flow })) throw Ambiguous();
                    text = Replace(empty.Start, EndOf(node), "{" + assignment + "}");
                }
                else if (path == "$")
                    text += (text.Length == 0 || text.EndsWith('\n') ? "" : newline) + assignment + newline;
                else throw Ambiguous();
                break;
            }
            node = child;
            path = childPath;
            if (i == keys.Length - 1)
            {
                var span = parser.Spans![path];
                var last = EndOf(node);
                var range = text[checked((int)span.Start.Start.Index)..last];
                if (range.Contains('\n') && range.Contains('#')) throw Ambiguous();
                text = Replace(span.Start, last, value + (range.EndsWith('\n') ? newline : ""));
            }
            else if (node.Kind != RecipeKind.Map) throw Ambiguous();
        }

        var (encoding, offset) = RecipeYaml.TextEncoding(original, false);
        var candidate = original.Take(offset).Concat(encoding.GetBytes(text)).ToArray();
        var changed = new Dictionary<string, byte[]>(inputs, inputs.Comparer) { [file] = candidate };
        var candidateSelection = new RecipeSelection(selection.Base, selection.Files, selection.Base, selection.EnvFiles)
        {
            CapturedFiles = changed, ImportedScope = selection.ImportedScope,
            ImportedPathBases = selection.ImportedPathBases, FilterUnusedModels = selection.FilterUnusedModels
        };
        var recipe = RecipeReader.Read(candidateSelection);
        if (recipe.Root.Get("service")?.Get("backend") is not null) _ = Backends.BackendIntent.FromRecipe(recipe);
        _ = Models.ModelIntent.FromRecipe(recipe);
        var impact = ArgumentValidation.Impact(recipe, ArgumentSchema.Load(recipe));
        if (impact.Count != 0)
            throw new RecipeException(impact[0].Code, "edit", impact[0].Origin, string.Join(Environment.NewLine, impact.Select(d => d.Detail)));

        return candidate;

        int EndOf(RecipeValue item)
        {
            var span = parser.Spans![item.Origin.Path];
            return span.Start switch
            {
                MappingStart { Style: MappingStyle.Block } when item.Fields.Count != 0 => EndOf(item.Fields.Values.Last()),
                SequenceStart { Style: SequenceStyle.Block } when item.Items.Count != 0 => EndOf(item.Items[^1]),
                // Flow end events point to the closing delimiter.
                MappingStart { Style: MappingStyle.Flow } or SequenceStart { Style: SequenceStyle.Flow } => checked((int)span.End.End.Index + 1),
                _ => checked((int)span.End.End.Index)
            };
        }

        string Replace(ParsingEvent start, int end, string replacement)
        {
            var index = checked((int)start.Start.Index);
            if (start is Scalar { Style: ScalarStyle.Plain, Value.Length: 0 } && index > 0 && text[index - 1] == ':')
                replacement = " " + replacement;
            return text[..index] + replacement + text[end..];
        }
    }

    internal static void Save(RecipeSelection selection, Dictionary<string, byte[]> inputs, string file, string[] keys, string value)
    {
        var candidate = Propose(selection, inputs, file, keys, value);
        var locks = new Stack<Mutex>();
        var temporary = Path.Combine(Path.GetDirectoryName(file)!, "." + Guid.NewGuid().ToString("N") + ".tmp");
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

    internal static string Quote(string key) => JsonValue.Create(key)!.ToJsonString();
}
