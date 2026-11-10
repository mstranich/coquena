using System.Text;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Tests;

// B, J and H without a terminal: the combinations of a recipe, Inicializar, draft validation
// and saving with a file changed or removed on disk (Y).
internal static class RecipeEditorTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-recipe-editor-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "nuevo");
        Directory.CreateDirectory(folder);
        try
        {
            var home = new Home(Path.Combine(root, "home"));
            var source = new DashboardCli.DefinitionsSource(home, folder);
            var place = source.Places().Single(item => item.Scope.Here);
            check(place.Context is null && place.Folder == folder && DefinitionsPresentation.Views(place.Scope).SequenceEqual(["Receta"]),
                "a folder without recipe keeps its path for Inicializar");
            check(DefinitionsPresentation.NoRecipe("nuevo").Rows.SelectMany(Markup.Parse).Any(span => span.Action == "init"), "J enables Inicializar");
            check(InitCli.Run([folder], TextWriter.Null, TextWriter.Null) == 0 && File.ReadAllText(Path.Combine(folder, "coquena.yaml")) == "{}\n",
                "Inicializar creates the recipe as coquena init");
            place = source.Places().Single(item => item.Scope.Here);
            check(place.Context is not null && place.Scope.HasRecipe, "after Inicializar the folder shows its recipe");

            // B: before its first use the recipe of the folder is listed as unregistered and can be edited.
            var combinations = DashboardCli.RecipeCombinations(place);
            check(combinations.Length == 1 && combinations[0].Id is null && combinations[0].Current, "an unused recipe is listed without registry id");
            var page = DashboardCli.RecipesPage("nuevo", null, combinations, combinations[0].Key, 56);
            var spans = page.Rows.SelectMany(Markup.Parse).ToArray();
            check(spans.Any(span => span.Action == "recipe-edit") && !spans.Any(span => span.Action == "recipe-forget") &&
                  spans.Any(span => span.Text.Contains("Nueva combinación") && span.Action is null),
                "B enables Editar, keeps Olvidar off for an unregistered recipe and Nueva combinación off");
            check(page.Rows.Count <= 20 && page.Rows.All(row => Markup.Width(Markup.Fit(row, 56)) == 56), "B fits 80×24");

            // H: drafts are validated in memory with the core reader and located by line.
            var recipe = Path.Combine(folder, "coquena.yaml");
            var opened = File.ReadAllBytes(recipe);
            var selection = combinations[0].Selection;
            check(RecipeDrafts.Validate(selection, new Dictionary<string, byte[]>()) is null, "the recipe on disk is valid");
            var invalid = Encoding.UTF8.GetBytes("name: nuevo\npresets:\n  a: [\n");
            var problem = RecipeDrafts.Validate(selection, new Dictionary<string, byte[]> { [recipe] = invalid });
            check(problem is { Line: > 1 } && problem.File == recipe, "an invalid draft is reported with its line");
            check(File.ReadAllBytes(recipe).SequenceEqual(opened), "validating does not write the file");

            var draft = RecipeDrafts.Encode("name: nuevo\n", opened);
            check(RecipeDrafts.Save(recipe, opened, draft, false) == RecipeDrafts.SaveResult.Saved && File.ReadAllText(recipe) == "name: nuevo\n",
                "saving writes the draft when the disk holds what was opened");
            check(RecipeDrafts.Save(recipe, draft, draft, false) == RecipeDrafts.SaveResult.Unchanged, "saving an unchanged draft writes nothing");
            File.WriteAllText(recipe, "name: externo\n");
            var mine = RecipeDrafts.Encode("name: mio\n", draft);
            check(RecipeDrafts.Save(recipe, draft, mine, false) == RecipeDrafts.SaveResult.Changed && File.ReadAllText(recipe) == "name: externo\n",
                "a file changed on disk is kept until the user chooses (Y)");
            check(RecipeDrafts.Save(recipe, draft, mine, true) == RecipeDrafts.SaveResult.Saved && File.ReadAllText(recipe) == "name: mio\n",
                "Sí overwrites with the draft");
            File.Delete(recipe);
            check(RecipeDrafts.Save(recipe, mine, mine, false) == RecipeDrafts.SaveResult.Missing && !File.Exists(recipe),
                "a removed file is reported and not recreated silently");
            check(RecipeDrafts.Save(recipe, mine, mine, true) == RecipeDrafts.SaveResult.Saved && File.Exists(recipe), "the user can recreate it");
            var bom = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes("{}\n")).ToArray();
            check(RecipeDrafts.Encode("name: a\n", bom).Take(3).SequenceEqual(bom.Take(3)), "a UTF-8 BOM of the opened file is kept");
            check(Directory.GetFiles(folder, "*.tmp").Length == 0, "no temporary file is left behind");

            // H without a terminal: tabs per file and .env, change tracking, marks and reload.
            File.WriteAllText(recipe, "name: nuevo\npresets:\n  a:\n    args:\n      temperature: 0.7\n");
            var env = Path.Combine(folder, ".env");
            File.WriteAllText(env, "CLAVE=valor\n");
            var editor = new DashboardCli.RecipeEditorPage(false);
            editor.Open(new RecipeSelection(folder, null, null, [env]), "nuevo", false);
            check(editor.Files.Select(file => file.Name).SequenceEqual(["coquena.yaml", ".env"]) && editor.Files[1].Environment &&
                  editor.Files.All(file => !file.Modified), "H opens one tab per file and per .env without changes");
            var yaml = editor.Files[0];
            yaml.View.Text = "name: nuevo\npresets:\n  a:\n    args:\n      temperature: 0.9\n";
            check(yaml.Modified && yaml.Changed == (5, 5), "editing one line marks only that line as changed");
            yaml.View.Text = "name: nuevo\npresets:\n  a: [\n";
            var located = editor.Validate();
            check(located is { Line: > 1 } && yaml.ErrorLine == located.Line && File.ReadAllText(recipe).Contains("0.7"),
                "Validar locates the error in the tab and does not write");
            editor.Render(80);
            check(editor.Validation.Rows.Count == 1 && Markup.Plain(editor.Validation.Rows[0]).Contains("línea " + located!.Line) &&
                  Markup.Plain(editor.Tabs.Rows[0]).Contains("coquena.yaml ●"), "the tab shows the draft and the validation row its line");
            DashboardCli.RecipeEditorPage.Load(yaml, File.ReadAllBytes(recipe));
            check(!yaml.Modified && yaml.Changed is null && yaml.ErrorLine is null, "No reloads the disk and discards the draft");
            var valid = editor.Validate();
            editor.Render(80);
            check(valid is null && Markup.Plain(editor.Validation.Rows[0]).Contains("válida"), "a valid combination says so");
            editor.Close();
            check(editor.Files.Count == 0 && editor.Selection is null, "closing releases the tabs");
            File.Delete(env);

            // Olvidar: a registered combination leaves the registry; the YAML stays.
            var context = place.Context!;
            var preparation = new CombinationPreparation(context);
            var prepared = await preparation.PrepareAsync(new RecipeSelection(folder), new NoResources());
            check(preparation.List().ContainsKey(prepared.Id), "the recipe is registered as a combination when it is used");
            combinations = DashboardCli.RecipeCombinations(source.Places().Single(item => item.Scope.Here));
            var registered = combinations.Single(item => item.Id is not null);
            new CombinationCleanup(context).Forget(registered.Id!);
            check(preparation.List().Count == 0 && File.Exists(recipe), "Olvidar removes the registry entry and keeps the YAML");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    // A recipe without models or backends asks for no resources.
    private sealed class NoResources : ICombinationResources
    {
        public Task<PreparationResource> PrepareAsync(PreparationRequest request, System.Text.Json.Nodes.JsonObject? exact, CancellationToken cancellation) =>
            throw new InvalidOperationException("unexpected resource " + request.Key);
    }
}
