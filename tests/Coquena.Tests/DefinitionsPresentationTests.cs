using Coquena.Observation;
using Coquena.Projects;
using Coquena.Storage;
using View = Coquena.Observation.DefinitionsPresentation;

namespace Coquena.Tests;

// Rows of the Definiciones tab: scope tree, read-only pages and disabled buttons at 80×24.
internal static class DefinitionsPresentationTests
{
    private const int Width = 80 - View.TreeWidth - 1;

    public static void Run(Action<bool, string> check)
    {
        View.Scope[] scopes =
        [
            new("Global", true, false, true, true),
            new("código", false, true, true, true),
            new("notas", false, false, true, true),
            new("cli-project", false, false, false, true),
            new("nuevo", false, false, true, false)
        ];
        var tree = View.Tree(scopes, 1);
        check(tree.Count == 5 + 9 && tree.Count(node => node.View is not null) == 9 && tree.Where(node => node.View is not null).All(node => node.Scope == 1),
            "only the focused scope is expanded");
        var rows = tree.Select(node => Markup.Plain(View.TreeRow(node, scopes, 1, false))).ToArray();
        check(rows[0] == " ▸ Global" && rows[1] == " ▾ código · aquí" && rows[2] == "  Recetas" && rows.Contains(" ✗ cli-project"),
            "scope headers show collapsed, expanded, here and inaccessible");
        check(tree.All(node => Markup.Width(View.TreeRow(node, scopes, 1, true)) == View.TreeWidth), "the selected tree row fills the column");
        check(View.Views(scopes[0]).SequenceEqual(["Servicios", "Presets", "Modelos", "Backends", "Parámetros", "Descargas", "Actualizaciones", "Mantenimiento"]) &&
              View.Views(scopes[4]).SequenceEqual(["Receta"]) && View.Views(scopes[3]).Length == 0,
            "Global, a recipe, a folder without recipe and an inaccessible path offer their views");

        View.Page[] pages =
        [
            View.Recipes("código", "principal", [new("coquena.yaml + coquena.gpu.yaml", "3f79f6", false, true), new("coquena.yaml", "a1c2e0", true, false)], ["coquena.yaml", "coquena.gpu.yaml"]),
            View.Presets("código", [new("asistente", "gemma-4-12B Q4_0", "loaded"), new("editor", "gemma-4-E4B", null)]),
            View.Services("Global", true, [new("principal", true, "● en línea", "ok"), new("laboratorio", false, null, null)]),
            View.Models("Global", [new("gemma-4-12B-it-qat Q4_0", false, false, 7_600_000_000, true), new("mi-modelo.gguf", false, true, null, false)]),
            View.Backends("Global", [new("cuda@13", "b6120", false, 435_000_000, true)]),
            View.Parameters("Global", Enumerable.Range(0, 40).Select(i => new View.Parameter("p" + i, "entero", "modelo", null)).ToArray(), 20),
            View.Downloads("Global", [new("gemma", "model", "running", "transfer", 50, 100, null), new("qwen", "model", "completed", "done", 10, 10, null)], Width),
            View.Updates("código", DateTimeOffset.Now, [new("backend llama.cpp", "b6120", "b6188", null)]),
            View.Maintenance("código"),
            View.NoRecipe("nuevo")
        ];
        check(pages.All(page => page.Rows.Count <= 20), "every page fits the 20 content rows of 80×24");
        check(pages.All(page => page.Rows.All(row => Markup.Width(Markup.Fit(row, Width)) == Width)), "rows fit the right column");
        check(pages.SelectMany(page => page.Rows.Append(page.Title)).SelectMany(Markup.Parse).All(span => span.Action is null),
            "no button of the read-only layer has an action");
        var recipes = string.Join('\n', pages[0].Rows.Select(Markup.Plain));
        check(recipes.Contains("● actual") && recipes.Contains("privada") && recipes.Contains(" ▲ Up ") &&
              pages[0].Rows.SelectMany(Markup.Parse).Where(span => span.Text.Contains("Up")).All(span => span.Class == "off"),
            "recipes mark the current and private combinations and show Up disabled");
        check(Markup.Plain(pages[5].Rows[^3]).Contains("más"), "a long parameter list says how many rows are left out");
        check(Markup.Plain(pages[6].Rows[1]).Contains("50%") && pages[6].Rows.Any(row => Markup.Plain(row).Contains("TERMINADAS")),
            "downloads show progress and finished operations");
        check(Markup.Plain(View.Updates("x", null, []).Rows[0]).Contains("Sin plan"), "without a recorded plan updates say so");
    }

    // Real reads over an isolated home and project: every view opens and nothing is written.
    public static void RunSource(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-definitions-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "folder-name");
        Directory.CreateDirectory(directory);
        var home = new Home(Path.Combine(root, "home"));
        File.WriteAllText(Path.Combine(directory, "coquena.yaml"),
            "name: news\nservice:\n  backend:\n    variant: cpu\n    version: latest\npresets:\n  asistente:\n    args:\n      model: model://principal\n");
        var globals = new RecipeRegistry(new ScopeStore(ScopeContext.Global(home)));
        globals.SetDefaultService(globals.RegisterService(globals.Register(["global.yaml"], "shared").RecipeId).ServiceId);
        try
        {
            var before = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Select(path => (path, File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default)).ToHashSet();
            var source = new DashboardCli.DefinitionsSource(home, directory);
            var places = source.Places();
            check(places.Select(place => place.Scope.Name).SequenceEqual(["Global", "news"]) && places[1].Scope.Here,
                "the tree lists Global and the recipe of the folder where the TUI opened");
            var pages = places.SelectMany(place => View.Views(place.Scope).Select(view => (view, page: source.Read(place, view, [], new Dictionary<int, ServiceSnapshot>(), 20, 56)))).ToArray();
            check(pages.All(item => !item.page.Rows.Any(row => Markup.Plain(row).Contains('✗'))),
                "every view of an empty scope opens without a read error: " + string.Join("; ", pages.Where(item => item.page.Rows.Any(row => Markup.Plain(row).Contains('✗'))).Select(item => item.view + " " + string.Join(" ", item.page.Rows.Select(Markup.Plain)))));
            check(pages.Any(item => item.view == "Presets" && item.page.Rows.Any(row => Markup.Plain(row).Contains("asistente") && Markup.Plain(row).Contains("principal"))),
                "project presets are read from the recipe with their model");
            check(pages.Any(item => item.view == "Servicios" && item.page.Rows.Any(row => Markup.Plain(row).Contains("★ shared"))),
                "global services mark the default one");
            var after = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Select(path => (path, File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default)).ToHashSet();
            var written = after.Except(before).Where(item => !item.path.EndsWith(".lock", StringComparison.Ordinal)).ToArray();
            check(written.Length == 0, "reading every view writes nothing but the locks of the CLI reads: " + string.Join(", ", written.Select(item => item.path)));
            var gone = new DashboardCli.DefinitionsSource.Place(new("gone", false, false, false, true), null, null);
            check(Markup.Plain(source.Read(gone, "Recetas", [], new Dictionary<int, ServiceSnapshot>(), 20, 56).Rows[1]).Contains("ya no se puede resolver"),
                "an inaccessible project says so instead of reading");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}