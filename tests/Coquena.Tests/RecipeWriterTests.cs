using System.Text;
using Coquena.Recipes;

namespace Coquena.Tests;

// Point edits write block YAML, keep the rest of the file and choose between deleting
// a key and writing null by layer.
internal static class RecipeWriterTests
{
    private const string Schema = "arg-defs:\n  definitions:\n    context: {type: integer, scope: preset}\n    label: {type: string, scope: preset}\n";

    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "coquena.yaml");
            string Edit(string yaml, string[] keys, Func<RecipeSelection, Dictionary<string, byte[]>, EditNode> value, string? overlay = null, byte[]? raw = null)
            {
                var bytes = raw ?? Encoding.UTF8.GetBytes(yaml);
                File.WriteAllBytes(file, bytes);
                var files = new List<string> { file };
                var inputs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [file] = bytes };
                if (overlay is not null)
                {
                    var extra = Path.Combine(root, "extra.yaml");
                    File.WriteAllText(extra, overlay);
                    files.Add(extra);
                    inputs[extra] = Encoding.UTF8.GetBytes(overlay);
                }
                var selection = new RecipeSelection(root, files, root);
                var target = files[^1];
                var proposed = RecipeEdit.Propose(selection, inputs, target, keys, value(selection, inputs));
                var (encoding, offset) = RecipeYaml.TextEncoding(proposed, false);
                return encoding.GetString(proposed, offset, proposed.Length - offset);
            }

            // A recipe created by init has only comments: the first edit is a block entry.
            var fromInit = Edit("# yaml-language-server: $schema=x\n" + Schema, ["presets", "chat"],
                (_, _) => RecipeWriter.Map(("args", RecipeWriter.Map(("context", new EditScalar("8"))))));
            check(fromInit.EndsWith("presets:\n  chat:\n    args:\n      context: 8\n", StringComparison.Ordinal) && !fromInit.Contains("{\"", StringComparison.Ordinal),
                "missing intermediate maps are written in block");
            var fromBraces = Edit("{}\n", ["name"], (_, _) => RecipeWriter.String("demo"));
            check(fromBraces == "name: demo\n", "an empty root map becomes a block entry");

            const string recipe = "# header\nname: demo\n" + Schema + "presets:\n  chat:  # main\n    args:\n      context: 12   # tokens\n      label: hi\n  other:\n    args:\n      context: 4\n";
            var cloned = Edit(recipe, ["presets", "copy"], (selection, inputs) => RecipeEdit.Copy(selection, inputs, ["presets", "chat"]));
            check(cloned == recipe + "  copy:\n    args:\n      context: 12   # tokens\n      label: hi\n",
                "clone copies the block text with its comments after the last sibling");

            var flow = "name: demo\n" + Schema + "presets: {chat: {args: {context: 12}}}\n";
            var flowClone = Edit(flow, ["presets", "copy"], (selection, inputs) => RecipeEdit.Copy(selection, inputs, ["presets", "chat"]));
            check(flowClone.Contains("presets: {copy: {args: {context: 12}}, chat:", StringComparison.Ordinal), "a flow map keeps flow style for its new entry");

            var removed = Edit(recipe, ["presets", "chat"], (_, _) => new EditRemove());
            check(removed == "# header\nname: demo\n" + Schema + "presets:\n  other:\n    args:\n      context: 4\n", "removing a preset of the same layer deletes its lines");
            var last = Edit("name: demo\n" + Schema + "presets:\n  chat:\n    args:\n      context: 1\n", ["presets", "chat"], (_, _) => new EditRemove());
            check(last == "name: demo\n" + Schema, "removing the last preset leaves no empty presets map");
            var unset = Edit(recipe, ["presets", "other", "args", "context"], (_, _) => new EditRemove(2));
            // An empty preset needs `{}`: a key without value would read as null and unassign it.
            check(unset.EndsWith("  other: {}\n", StringComparison.Ordinal), "unsetting the last argument keeps the preset");

            var inherited = Edit(recipe, ["presets", "chat"], (_, _) => new EditRemove(), "presets:\n  chat:\n    args:\n      context: 24\n");
            check(inherited == "presets:\n  chat: null\n", "removing an inherited preset writes null on the key's line");
            var inheritedMissing = Edit(recipe, ["presets", "other"], (_, _) => new EditRemove(), "name: overlay\n");
            check(inheritedMissing == "name: overlay\npresets:\n  other: null\n", "an inherited preset absent from the layer gets a null entry");

            var crlf = Edit(recipe.Replace("\n", "\r\n", StringComparison.Ordinal), ["presets", "chat", "args", "label"], (_, _) => RecipeWriter.String("a: b"),
                raw: [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(recipe.Replace("\n", "\r\n", StringComparison.Ordinal))]);
            check(crlf.Contains("      label: \"a: b\"\r\n", StringComparison.Ordinal) && crlf.StartsWith("# header\r\n", StringComparison.Ordinal),
                "CRLF and BOM files keep their line endings; ambiguous text is quoted");

            // One validation reports every located problem; a syntax error stops its file only.
            const string twoErrors = "name: demo\n" + Schema + "presets:\n  a:\n    args:\n      context: uno\n  b:\n    args:\n      context: dos\n  c:\n    args:\n      context: 3\n";
            File.WriteAllText(file, twoErrors);
            var single = new RecipeSelection(root, [file], root);
            var problems = RecipeValidation.Check(single).Problems;
            check(problems.Count == 2 && problems.Select(p => p.Line).Distinct().Count() == 2 && problems.All(p => p.File == file),
                "validation reports every invalid preset with its line: " + RecipeValidation.Describe(problems));
            // After setting an entry aside, the next problems keep their own lines and paths.
            var nested = RecipeValidation.Check(single, new Dictionary<string, byte[]> { [file] = Encoding.UTF8.GetBytes(
                "name: demo\nextra: 1\npresets:\n  a:\n    args:\n    - mal\n  b:\n    enable: quizas\n") }).Problems;
            check(nested.Select(p => (p.Line, p.Code)).SequenceEqual([(2, "unknown-key"), (6, "map-required"), (8, "invalid-type")]),
                "problems after a removed block keep their lines: " + RecipeValidation.Describe(nested));
            // References to an entry set aside are consequences: counted on its problem, not listed.
            var derived = RecipeValidation.Check(single, new Dictionary<string, byte[]> { [file] = Encoding.UTF8.GetBytes(
                "name: demo\nargs-mode: strict\narg-defs:\n  definitions:\n    context: 5\npresets:\n  a:\n    args:\n      context: 1\n  b:\n    args:\n      context: 2\n") }).Problems;
            check(derived.Count == 1 && derived[0].Line == 5 && derived[0].Message.EndsWith("(y 2 referencias a esta entrada)", StringComparison.Ordinal),
                "references to a removed definition are not reported as new problems: " + RecipeValidation.Describe(derived));

            // A point edit reports every problem of the result, not only the first.
            File.WriteAllText(file, "name: demo\n" + Schema + "presets:\n  a:\n    args:\n      context: uno\n  b:\n    args:\n      context: dos\n");
            try { RecipeEdit.Propose(single, new Dictionary<string, byte[]> { [file] = File.ReadAllBytes(file) }, file, ["name"], RecipeWriter.String("otro")); check(false, "invalid edit accepted"); }
            catch (RecipeException error) { check(error.Phase == "validate" && error.Detail.Split('\n').Length == 2, "an edit lists every problem: " + error.Detail); }

            // Init brings an existing recipe's schema reference to the current one, without asking.
            var project = Path.Combine(root, "project");
            Directory.CreateDirectory(project);
            var projectRecipe = Path.Combine(project, "coquena.yaml");
            File.WriteAllBytes(projectRecipe, [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("name: demo\r\n# yaml-language-server: $schema=./old.json\r\n")]);
            var initOutput = new StringWriter();
            check(InitCli.Run([project], initOutput, TextWriter.Null) == 0 && initOutput.ToString().Contains("Referencia al schema actualizada", StringComparison.Ordinal) &&
                  File.ReadAllBytes(projectRecipe).SequenceEqual((byte[])[0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("name: demo\r\n" + InitCli.SchemaComment + "\r\n")]),
                "init replaces an old schema reference keeping BOM and line endings");
            File.WriteAllText(projectRecipe, "name: demo\n");
            initOutput.GetStringBuilder().Clear();
            check(InitCli.Run([project], initOutput, TextWriter.Null) == 0 && File.ReadAllText(projectRecipe) == InitCli.SchemaComment + "\nname: demo\n" &&
                  initOutput.ToString().Contains("Referencia al schema agregada", StringComparison.Ordinal) && !initOutput.ToString().Contains("Schema actualizado", StringComparison.Ordinal),
                "init adds a missing reference and leaves an identical schema alone");
            initOutput.GetStringBuilder().Clear();
            check(InitCli.Run([project], initOutput, TextWriter.Null) == 0 && !initOutput.ToString().Contains("Referencia", StringComparison.Ordinal), "a current project reports no changes");

            // verify --json reports an invalid recipe as data: every problem with file, line and code.
            File.WriteAllText(projectRecipe, "name: demo\nextra: 1\npresets:\n  b:\n    enable: quizas\n");
            var json = new StringWriter();
            var jsonCode = Cli.Run(["verify", "--base", project, "--json"], json, TextWriter.Null);
            var report = System.Text.Json.Nodes.JsonNode.Parse(json.ToString())!;
            check(jsonCode == 2 && report["valid"]!.GetValue<bool>() == false && report["problems"]!.AsArray().Count == 2 &&
                  report["problems"]![0]!["line"]!.GetValue<int>() == 2 && report["problems"]![1]!["code"]!.GetValue<string>() == "invalid-type",
                "verify --json lists the recipe problems: " + json);

            var broken = RecipeValidation.Check(single, new Dictionary<string, byte[]> { [file] = Encoding.UTF8.GetBytes("name: demo\npresets:\n  a: [\n") }).Problems;
            check(broken.Count == 1 && broken[0].Code == "yaml-syntax" && broken[0].Line > 1 && broken[0].Message.EndsWith("El resto del archivo no se pudo validar.", StringComparison.Ordinal), "a YAML syntax error is reported once with its line");
            try { RecipeEdit.Copy(single, new Dictionary<string, byte[]> { [file] = Encoding.UTF8.GetBytes(twoErrors) }, ["presets", "c"]); check(false, "clone over an invalid recipe"); }
            catch (RecipeException error) { check(error.Phase == "validate" && error.Detail.Split('\n').Length == 2, "clone requires a valid recipe and lists its problems"); }

            // The schema is generated from the reader's shapes: same keys, same closed maps.
            var schema = RecipeShape.Schema();
            var rootKeys = schema["properties"]!.AsObject().Select(p => p.Key).ToHashSet();
            check(rootKeys.SetEquals(RecipeShape.Root.Fields.Select(f => f.Key)) && schema["additionalProperties"]!.GetValue<bool>() == false &&
                  schema["properties"]!["presets"]!["anyOf"]![0]!["additionalProperties"]!["anyOf"]![0]!["properties"]!["args"] is not null,
                "the schema lists the reader's vocabulary and rejects unknown keys");
            File.WriteAllText(file, "name: demo\nunknown: 1\n");
            check(RecipeValidation.Check(single).Problems.Single().Code == "unknown-key", "the reader rejects the key the schema rejects");

            // Highlighting colors keys, values and flow punctuation also inside {…} and […].
            var document = new Terminal.Gui.Editor.Document.TextDocument("    context: {type: 12, list: [a, \"b, c\"], x: model://m}  # note\n- item\n");
            using (var highlighter = new Terminal.Gui.Editor.Highlighting.DocumentHighlighter(document, DashboardCli.YamlDefinition()))
            {
                var colors = new Dictionary<int, string>();
                foreach (var number in new[] { 1, 2 })
                {
                    var line = highlighter.HighlightLine(number);
                    foreach (var section in line.Sections)
                        for (var at = section.Offset; at < section.Offset + section.Length; at++) colors[at] = section.Color.Name;
                }
                string At(string text, int skip = 0) => colors.GetValueOrDefault(document.Text.IndexOf(text, StringComparison.Ordinal) + skip, "");
                check(At("context") == "Key" && At("{") == "Marker" && At("type") == "Key" && At("12") == "Value" && At(", list") == "Marker" &&
                      At("list") == "Key" && At("[a") == "Marker" && At("[a", 1) == "Value" && At("\"b, c\"") == "Value" && At("model://m") == "Value" &&
                      At("# note") == "Comment" && At("item") == "Value" && At("- item") == "Marker",
                    "flow maps and lists are highlighted: " + string.Join(" ", colors.OrderBy(p => p.Key).Select(p => document.Text[p.Key] + "=" + p.Value[0])));
            }

            check(RecipeWriter.Scalar("123") == "\"123\"" && RecipeWriter.Scalar("true") == "\"true\"" && RecipeWriter.Scalar("model://main") == "model://main" &&
                  RecipeWriter.Scalar("") == "\"\"" && RecipeWriter.Scalar("# x") == "\"# x\"" && RecipeWriter.Uninterpolated("${A}") == "'${A}'",
                "scalars stay plain only when they read back as the same string");
            var list = RecipeWriter.Block(RecipeWriter.Map(("files", new EditSequence([RecipeWriter.String("a.gguf"), RecipeWriter.String("b.gguf")]))), 0, 2, "\n");
            check(list == "\nfiles:\n  - a.gguf\n  - b.gguf", "sequences are written in block without brackets");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
