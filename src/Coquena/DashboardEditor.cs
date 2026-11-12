using System.Xml;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Recipes;
using Terminal.Gui.Drawing;
using Terminal.Gui.Editor;
using Terminal.Gui.Editor.Highlighting;
using Terminal.Gui.Editor.Highlighting.Xshd;
using Terminal.Gui.Editor.Rendering;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Coquena;

internal static partial class DashboardCli
{
    // Highlighting for recipes: keys, scalar values and comments. Own definition, the package has no YAML.
    private const string YamlHighlighting = """
        <SyntaxDefinition name="YAML" extensions=".yaml;.yml" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#88929A" />
          <Color name="Key" foreground="#EAEBED" />
          <Color name="Value" foreground="#457B9D" />
          <Color name="Marker" foreground="#DDA15E" />
          <RuleSet>
            <Span color="Comment" begin="(?&lt;=^|\s)\#" />
            <Rule color="Marker">^\s*-(?=\s|$)</Rule>
            <Rule color="Key">[^\s:\#'"][^:\#]*?(?=:(\s|$))</Rule>
            <Rule color="Value">(?&lt;=:\s+)[^\#\s][^\#]*?(?=\s*(\#|$))</Rule>
            <Rule color="Value">(?&lt;=^\s*-\s+)[^\#\s][^\#]*?(?=\s*(\#|$))</Rule>
            <Rule color="Value">(?&lt;==)[^\#]*$</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private static IHighlightingDefinition? yamlDefinition;

    private static IHighlightingDefinition YamlDefinition()
    {
        if (yamlDefinition is not null) return yamlDefinition;
        using var reader = XmlReader.Create(new StringReader(YamlHighlighting));
        return yamlDefinition = HighlightingLoader.Load(reader, null);
    }

    // B · Recetas: a combination registered in the scope, or the folder's own recipe before its first use.
    // How a combination is named everywhere: its YAML in order and its .env files. File names are
    // enough unless two share one (Global intentions are recipe.yaml in different folders); then each
    // file is named by its path from the scope base.
    internal static string CombinationLabel(RecipeSelection selection)
    {
        var files = selection.Files.Concat(selection.EnvFiles).ToArray();
        var names = files.Select(file => Path.GetFileName(file)).ToArray();
        var unique = names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length;
        return string.Join(" + ", unique ? names : files.Select(file => Path.GetRelativePath(selection.Base, file).Replace(Path.DirectorySeparatorChar, '/')));
    }

    // The YAML of a combination are its layers, numbered in order: ¹ first file, ² second…
    // A value names the layer it comes from; one outside the YAML (an .env, a default) shows «·».
    internal static string LayerMark(RecipeSelection selection, string? file)
    {
        var index = selection.Files.ToList().FindIndex(path => path.Equals(file, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? "·" : Superscript(index + 1);
    }

    internal static string LayerLegend(RecipeSelection selection) =>
        string.Join("  ", selection.Files.Select((file, index) => Superscript(index + 1) + " " + Path.GetFileName(file)));

    private static string Superscript(int number) => string.Concat(number.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(digit => "⁰¹²³⁴⁵⁶⁷⁸⁹"[digit - '0']));

    internal sealed record RecipeCombination(string Key, string? Id, RecipeSelection Selection, bool Private, bool Current)
    {
        public string Files => CombinationLabel(Selection);
    }

    internal static RecipeCombination[] RecipeCombinations(DefinitionsSource.Place place)
    {
        var context = place.Context!;
        var current = place.Recipe is null ? null : CombinationIdentity.FromSelection(place.Recipe).Id;
        var items = new Coquena.Projects.CombinationPreparation(context).List().Select(item => new RecipeCombination(item.Key, item.Key,
            new RecipeSelection(context.Base, item.Value!["files"]!.AsArray().Select(file => file!.GetValue<string>()), context.Base,
                item.Value["env-files"]?.AsArray().Select(file => file!.GetValue<string>()) ?? []),
            item.Value["private"]?.GetValue<bool>() == true, item.Key == current)).ToList();
        if (place.Recipe is not null && !items.Any(item => item.Current)) items.Insert(0, new("recipe", null, place.Recipe, false, true));
        return items.ToArray();
    }

    internal static DefinitionsPresentation.Page RecipesPage(string scope, string? destination, IReadOnlyList<RecipeCombination> items, string? selected, int width, string? busy = null)
    {
        var rows = new List<string> { " " + Markup.Style("mut", "  " + Pad("COMBINACIÓN", 28) + " " + Pad("ID", 8) + " ESTADO") };
        foreach (var item in items)
        {
            var state = item.Id is null ? "sin registrar" : item.Private ? "privada" : item.Current ? "● actual" : "conocida";
            var text = (item.Key == selected ? "▸ " : "  ") + Pad(item.Files, 28) + " " + Pad(item.Id?[..Math.Min(6, item.Id.Length)] ?? "—", 8) + " " + state;
            rows.Add(item.Key == selected ? Markup.Style("focb", DashboardPresentation.Fit(text, width - 2).PadRight(Math.Max(0, width - 2)), "combination:" + item.Key)
                : Markup.Style("nav", text, "combination:" + item.Key));
        }
        if (items.Count == 0) rows.Add(" " + Markup.Style("mut", "Sin combinaciones registradas."));
        var chosen = items.FirstOrDefault(item => item.Key == selected);
        rows.Add("");
        rows.Add(" " + ManagementPresentation.Button("+ Nueva combinación…", "recipe-new") + " " +
            (chosen is null ? DefinitionsPresentation.Off("Editar") : ManagementPresentation.Button("Editar", "recipe-edit")) + " " +
            (chosen?.Id is null ? DefinitionsPresentation.Off("Olvidar") : ManagementPresentation.Button("Olvidar", "recipe-forget")));
        rows.Add("");
        if (chosen is not null)
            rows.Add(" " + Markup.Style("mut", "archivos") + " " + string.Join(" " + Markup.Style("mut", "→") + " ",
                chosen.Selection.Files.Concat(chosen.Selection.EnvFiles).Select(file => Markup.Escape(Path.GetFileName(file)))));
        rows.Add(" " + Markup.Style("mut", (chosen?.Private == true ? "☑" : "☐") + " preparación privada"));
        rows.Add("");
        // While Up runs, its button keeps its place, disabled, with the spinner, and a status row says so.
        var spinner = DashboardPresentation.Spinner();
        rows.Add(" " + (busy is null ? ManagementPresentation.Button("▲ Up", "up") : DefinitionsPresentation.Off((busy == "up" ? spinner : "▲") + " Up")) + " " +
            DefinitionsPresentation.Off((busy == "down" ? spinner : "▼") + " Down") + " " +
            (chosen is null || busy is not null ? DefinitionsPresentation.Off("Importar") : ManagementPresentation.Button("Importar", "recipe-import")) + " " + DefinitionsPresentation.Off("Ver en vivo"));
        if (busy is not null)
            rows.Add(" " + Markup.Style("warn", spinner + " ") + Markup.Style("mut", (busy == "down" ? "Deteniendo" : "Ejecutando Up") + "… Podés cambiar de vista; la operación sigue."));
        return new("Recetas de " + Markup.Escape(scope) + (destination is null ? "" : " " + Markup.Style("mut", "→") + " " + Markup.Escape(destination)), rows);

        static string Pad(string text, int size) => DashboardPresentation.Fit(text, size - 1).PadRight(size);
    }

    // One open file: what was read from disk, the editor holding the draft and the lines that differ.
    internal sealed class EditorFile
    {
        public required string Path { get; init; }
        public required byte[] Opened { get; set; }
        public required Editor View { get; init; }
        public required bool Environment { get; init; }
        public bool Existed { get; set; } = true;
        public string[] OpenedLines { get; set; } = [];
        public (int First, int Last)? Changed { get; set; }
        public int? ErrorLine { get; set; }
        public string Name => System.IO.Path.GetFileName(Path);
        public bool Modified => !Existed || !RecipeDrafts.Encode(View.Text, Opened).AsSpan().SequenceEqual(Opened);
    }

    // Marks the changed block and the line of the last validation error with a background.
    internal sealed class LineMarks(EditorFile file, bool useColor) : IVisualLineTransformer
    {
        public void Transform(CellVisualLine line)
        {
            if (!useColor) return;
            var number = line.DocumentLine.LineNumber;
            Color? background = number == file.ErrorLine ? new Color("#4A2226")
                : file.Changed is { } changed && number >= changed.First && number <= changed.Last ? Palette.Card : null;
            if (background is not { } color) return;
            foreach (var element in line.Elements) element.Attribute = new Attribute(element.Attribute.Foreground, color);
        }
    }

    // H · Editor: one tab per file of the combination and per .env, the actions of the preview
    // and the last validation. Saving goes through RecipeDrafts; nothing is published from here.
    internal sealed class RecipeEditorPage
    {
        private readonly bool useColor;
        public Canvas Tabs { get; }
        public Canvas Tools { get; }
        public View Host { get; }
        public Canvas ValidationTitle { get; }
        public Canvas Validation { get; }
        public List<EditorFile> Files { get; } = [];
        public int Current { get; private set; }
        public RecipeSelection? Selection { get; private set; }
        public string Scope { get; private set; } = "";
        public bool Private { get; private set; }
        public bool RequireValidSave { get; set; }
        public ScopeContext? ImportedContext { get; set; }
        public RecipeDrafts.Diagnostic? Problem { get; set; }
        public bool Validated { get; set; }
        public string? Status { get; set; }

        public RecipeEditorPage(bool useColor)
        {
            this.useColor = useColor;
            Tabs = new Canvas(useColor) { Y = 1, Height = 1, ZoneName = "hd", Interactive = true };
            Tools = new Canvas(useColor) { Y = 2, Height = 1, ZoneName = "z1", Interactive = true };
            Host = new View { X = 0, Y = 3, Width = Dim.Fill(), CanFocus = true };
            ValidationTitle = new Canvas(useColor) { Height = 1, ZoneName = "hd" };
            Validation = new Canvas(useColor) { Height = 1 };
        }

        public IEnumerable<View> Views => [Tabs, Tools, Host, ValidationTitle, Validation];

        public EditorFile? File => Files.ElementAtOrDefault(Current);

        public void Open(RecipeSelection selection, string scope, bool isPrivate, IReadOnlySet<string>? newFiles = null)
        {
            Close();
            (Selection, Scope, Private) = (selection, scope, isPrivate);
            ImportedContext = selection.ImportedScope;
            foreach (var (path, environment) in selection.Files.Select(file => (file, false)).Concat(selection.EnvFiles.Select(file => (file, true))))
            {
                var isNew = newFiles?.Contains(path) == true && !System.IO.File.Exists(path);
                var bytes = isNew ? Array.Empty<byte>() : RecipeYaml.ReadBytes(path);
                var editor = new Editor
                {
                    Width = Dim.Fill(), Height = Dim.Fill(), GutterOptions = GutterOptions.LineNumbers,
                    ConvertTabsToSpaces = true, IndentationSize = 2, Visible = false
                };
                if (!environment) editor.HighlightingDefinition = YamlDefinition();
                if (useColor) Paint(editor, Palette.Text, Palette.Base);
                var file = new EditorFile { Path = path, Opened = bytes, View = editor, Environment = environment, Existed = !isNew };
                Load(file, bytes);
                editor.LineTransformers.Add(new LineMarks(file, useColor));
                editor.ContentChanged += (_, _) => { Diff(file); Validated = false; Changed?.Invoke(); };
                Files.Add(file);
                Host.Add(editor);
            }
            Current = 0;
            Show(0);
        }

        public void Stage(string path, byte[] bytes)
        {
            var at = Files.FindIndex(file => file.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (at < 0) throw new ArgumentException("El archivo no pertenece a la combinación abierta.");
            Files[at].View.Text = RecipeYaml.ReadText(path, false, bytes);
            Diff(Files[at]); Validated = false; Show(at); Changed?.Invoke();
        }

        public event Action? Changed;

        // Reload from disk: the draft becomes the file as it is now.
        public static void Load(EditorFile file, byte[] bytes)
        {
            var text = RecipeYaml.ReadText(file.Path, false, bytes);
            file.Opened = bytes;
            file.View.Text = text;
            file.View.Document!.UndoStack.ClearAll();
            file.View.Document.UndoStack.MarkAsOriginalFile();
            file.OpenedLines = text.Split('\n');
            file.Changed = null;
            file.ErrorLine = null;
        }

        // Changed block between the common prefix and suffix of opened and current lines.
        private static void Diff(EditorFile file)
        {
            var now = file.View.Text.Split('\n');
            var before = file.OpenedLines;
            var first = 0;
            while (first < now.Length && first < before.Length && now[first] == before[first]) first++;
            if (first == now.Length && first == before.Length) { file.Changed = null; return; }
            var tail = 0;
            while (tail < now.Length - first && tail < before.Length - first && now[^(tail + 1)] == before[^(tail + 1)]) tail++;
            var last = Math.Max(first, now.Length - tail - 1);
            file.Changed = (first + 1, last + 1);
        }

        public void Show(int index)
        {
            if (index < 0 || index >= Files.Count) return;
            Current = index;
            for (var i = 0; i < Files.Count; i++) Files[i].View.Visible = i == index;
            Files[index].View.SetFocus();
        }

        public void Close()
        {
            foreach (var file in Files) { Host.Remove(file.View); file.View.Dispose(); }
            Files.Clear();
            (Selection, Problem, Validated, Status, Current) = (null, null, false, null, 0);
            RequireValidSave = false;
            ImportedContext = null;
        }

        public IReadOnlyDictionary<string, byte[]> Drafts() =>
            Files.ToDictionary(file => file.Path, file => RecipeDrafts.Encode(file.View.Text, file.Opened), StringComparer.OrdinalIgnoreCase);

        // Runs the core checks over the drafts and moves the caret to the error when it has a line.
        public RecipeDrafts.Diagnostic? Validate()
        {
            foreach (var file in Files) file.ErrorLine = null;
            Problem = RecipeDrafts.Validate(Selection!, Drafts());
            Validated = true;
            if (Problem is { Line: { } line } problem &&
                Files.FindIndex(file => file.Path.Equals(problem.File, StringComparison.OrdinalIgnoreCase)) is var at and >= 0)
            {
                Files[at].ErrorLine = line;
                Files[at].View.SetNeedsDraw();
            }
            return Problem;
        }

        public void Render(int width)
        {
            var clock = DateTime.Now.ToString("HH:mm");
            var tabs = string.Join(" ", Files.Select((file, i) =>
            {
                var label = " " + file.Name + (file.Modified ? " ●" : "") + " ";
                return i == Current ? Markup.Style("sel", label, "tab:" + i) : Markup.Style("nav", label, "tab:" + i);
            }));
            Tabs.Rows = [tabs + "{pad}" + Markup.Style("mut", Status ?? clock) + " "];
            Tools.Rows = [" " + Button("Guardar", "save") + " " + Button("Validar", "validate") + " " +
                (ImportedContext is null ? Button("▲ Up", "up") : DefinitionsPresentation.Off("▲ Up")) + " " +
                (ImportedContext is null ? Button("▼ Down", "down") : DefinitionsPresentation.Off("▼ Down")) + "   " + Button("Copiar", "copy") + " " + Button("Cortar", "cut") + " " + Button("Pegar", "paste") +
                "{pad}" + Button("Cerrar", "close") + " "];
            ValidationTitle.Rows = [" Validación"];
            Validation.Rows = [Problem is { } problem
                ? " " + Markup.Style("err", "✗") + " " + Markup.Escape(DashboardPresentation.Fit(
                    (problem.Line is { } line ? System.IO.Path.GetFileName(problem.File) + " · línea " + line + " · " : "") + problem.Message, width - 4))
                : Validated ? " " + Markup.Style("ok", "✓") + " " + Markup.Style("mut", "La combinación es válida con los borradores actuales.")
                : " " + Markup.Style("mut", "Sin validar. Validar comprueba los borradores sin guardarlos.")];
        }

        private static string Button(string text, string action) => Markup.Style("btn", " " + text + " ", action);
    }
}
