using System.Text;
using System.Text.Json.Nodes;
using Coquena.Models;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class AuthoringDashboardTests
{
    public static async Task Setup(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "coquena.yaml"), "name: authoring-fixture\narg-defs:\n  definitions:\n    context: {type: integer, scope: preset, description: Context length}\npresets:\n  assistant:\n    args:\n      context: 12\n");
        File.WriteAllText(Path.Combine(root, "extra.yaml"), "presets: {assistant: {args: {context: 24}}}\n");
        File.WriteAllText(Path.Combine(root, ".env"), "LOCAL_FIXTURE=not-for-evidence\n");
        await new CombinationPreparation(ScopeContext.Project(Path.Combine(root, "coquena.yaml"))).PrepareAsync(new RecipeSelection(root), new NoResources());
    }

    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-authoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "coquena.yaml");
            const string yaml = "# untouched header\nname: fixture\narg-defs:\n  definitions:\n    context: {type: integer, scope: preset}\npresets:\n  assistant:\n    args:\n      context: 12\n";
            File.WriteAllText(file, yaml);
            var ini = DashboardCli.PublishedValues("[fixture/assistant]\r\nctx-size = 12\r\n# temp = 1\r\n");
            check(ini.Count == 1 && ini["ctx-size"] == "12", "P reads published INI values without padding or comments");
            var selection = new RecipeSelection(root);
            var inputs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [file] = File.ReadAllBytes(file) };
            var recipe = RecipeReader.Read(selection);
            var proposed = RecipeEdit.Propose(selection, inputs, file, ["presets", "copy"], RecipeEdit.Copy(selection, inputs, ["presets", "assistant"]));
            check(File.ReadAllText(file) == yaml && Encoding.UTF8.GetString(proposed).Contains("# untouched header"), "cloning proposes a draft without writing or dropping neighboring comments");
            check(RecipeDrafts.Validate(selection, new Dictionary<string, byte[]> { [file] = proposed }).Count == 0, "cloned preset passes shared validation");
            var presets = DashboardCli.PresetRows(ScopeContext.Project(file), [selection], new Dictionary<int, ServiceSnapshot>());
            var presetPage = DashboardCli.PresetsPage("fixture", presets, presets[0].Id, 56);
            var actions = presetPage.Rows.SelectMany(Markup.Parse).Select(span => span.Action).ToArray();
            check(actions.Contains("preset-clone") && actions.Contains("preset-edit") && actions.Contains("preset-remove") && !actions.Contains("preset-live"), "P enables authoring without fabricating a live target");
            var definitions = DashboardCli.DefinitionRows([selection]);
            check(definitions.Single().Name == "context" && definitions[0].Uses.SequenceEqual(["fixture/assistant"]), "Q reads the selected YAML schema and assignment owners");
            check(DashboardCli.ParametersPage("fixture", definitions, definitions[0].Id, "").Rows.Count <= 20, "Q retains list and detail in 80x24 content");
            // Layout under stress: long names, paging and several arguments still fit the 80×24 content area.
            var stressFile = Path.Combine(root, "stress", "coquena.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(stressFile)!);
            var stressYaml = new StringBuilder("name: a-very-long-combination-name-for-layout\narg-defs:\n  definitions:\n");
            foreach (var name in new[] { "context-length-for-long-sessions", "temperature", "top-p", "top-k", "repeat-penalty", "seed" })
                stressYaml.Append("    ").Append(name).Append(": {type: number, scope: preset, description: A deliberately long description that must be clipped to the content width of the dashboard}\n");
            stressYaml.Append("presets:\n");
            for (var i = 0; i < 6; i++)
                stressYaml.Append("  preset-with-a-very-long-name-").Append(i).Append(":\n    args: {context-length-for-long-sessions: 4096, temperature: 0.7, top-p: 0.9, top-k: 40, repeat-penalty: 1.1, seed: 7}\n");
            File.WriteAllText(stressFile, stressYaml.ToString());
            var stressSelection = new RecipeSelection(Path.GetDirectoryName(stressFile)!);
            var stressPresets = DashboardCli.PresetRows(ScopeContext.Project(stressFile), [stressSelection], new Dictionary<int, ServiceSnapshot>());
            var stressDefinitions = DashboardCli.DefinitionRows([stressSelection]);
            var stressUpdates = new DashboardCli.UpdateView { Problem = "No hay plan para esta combinación con un mensaje deliberadamente largo." };
            foreach (var (label, page) in new (string, Coquena.Observation.DefinitionsPresentation.Page)[] {
                ("P", DashboardCli.PresetsPage("a-very-long-scope-name", stressPresets, stressPresets[5].Id, 56)),
                ("Q", DashboardCli.ParametersPage("a-very-long-scope-name", stressDefinitions, stressDefinitions[5].Id, "")),
                ("U", DashboardCli.UpdatesPage("a-very-long-scope-name", stressUpdates, ["Servicio 8080: sin cambios en la ejecución activa."], busy: true)) })
            {
                var wide = page.Rows.Where(row => Markup.Width(row) > 56).Select(Markup.Plain).ToArray();
                check(page.Rows.Count <= 20, label + " keeps its content within 20 rows under stress (" + page.Rows.Count + ")");
                check(wide.Length == 0, label + " rows fit 56 columns without clipping" + (wide.Length == 0 ? "" : ": " + string.Join(" | ", wide)));
                check(page.Rows.SelectMany(Markup.Parse).Any(span => span.Action is not null), label + " keeps actions reachable under stress");
                if (Environment.GetEnvironmentVariable("COQUENA_LAYOUT_DUMP") is { Length: > 0 } dump)
                    File.WriteAllLines(Path.Combine(dump, label + ".txt"), new[] { Markup.Plain(page.Title) }.Concat(page.Rows.Select(Markup.Plain)));
            }
            check(stressPresets.Length == 6 && DashboardCli.PresetsPage("s", stressPresets, stressPresets[5].Id, 56).Rows.SelectMany(Markup.Parse).Any(span => span.Action == "preset:" + stressPresets[5].Id),
                "P pages to the selected preset beyond the first four");

            try { RecipeEdit.Propose(selection, inputs, file, ["arg-defs", "definitions", "context"], RecipeWriter.Map(("type", RecipeWriter.String("string")))); check(false, "incompatible type accepted"); }
            catch (RecipeException) { check(File.ReadAllText(file) == yaml, "incompatible schema keeps the saved assignments and file"); }

            var newFile = Path.Combine(root, "override.yaml");
            var editor = new DashboardCli.RecipeEditorPage(false);
            editor.Open(new(root, [file, newFile], root), "fixture", false, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { newFile });
            check(editor.Files[1].Modified && !File.Exists(newFile), "V opens a new unsaved layer without creating it");
            editor.Stage(newFile, Encoding.UTF8.GetBytes("presets: {assistant: {args: {context: 24}}}\n"));
            check(editor.Validate() is null && !File.Exists(newFile), "new override validates as a draft");
            editor.Close();
            check(!File.Exists(newFile), "discarding an added file leaves disk unchanged");

            var global = ScopeContext.Global(new Home(Path.Combine(root, "home")));
            var emptyGlobal = ScopeContext.Global(new Home(Path.Combine(root, "empty-home")));
            var emptyPath = emptyGlobal.PathFor("recipe.yaml");
            var emptyInputs = new Dictionary<string, byte[]> { [emptyPath] = [] };
            var firstSchema = RecipeEdit.Propose(new RecipeSelection(emptyGlobal.Base, [emptyPath], emptyGlobal.Base), emptyInputs, emptyPath, ["arg-defs", "definitions", "custom"], RecipeWriter.Map(("type", RecipeWriter.String("string"))));
            check(!File.Exists(emptyPath), "first global schema remains a draft before explicit save");
            ImportedRecipeEdits.Save(emptyGlobal, emptyInputs, new Dictionary<string, byte[]> { [emptyPath] = firstSchema });
            check(new ScopeStore(emptyGlobal).Read().Documents.ContainsKey("recipe.yaml") && File.Exists(emptyPath), "first global schema is created through a generation on explicit save");
            var store = new ScopeStore(global);
            const string common = "arg-defs: {definitions: {context: {type: integer, scope: preset}}}\n";
            const string consumer = "name: imported\nargs-mode: strict\npresets: {assistant: {args: {context: 12}}}\n";
            store.Commit(0, new Dictionary<string, JsonObject>
            {
                ["recipe.yaml"] = Intention(common), ["consumers/11111111-1111-4111-8111-111111111111/recipe.yaml"] = Intention(consumer)
            });
            var commonPath = global.PathFor("recipe.yaml");
            var opened = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [commonPath] = File.ReadAllBytes(commonPath) };
            var revision = store.Read().Revision;
            try { ImportedRecipeEdits.Save(global, opened, new Dictionary<string, byte[]> { [commonPath] = Encoding.UTF8.GetBytes(common.Replace("integer", "string", StringComparison.Ordinal)) }); check(false, "global invalid schema accepted"); }
            catch (RecipeException) { check(store.Read().Revision == revision && File.ReadAllText(commonPath) == common, "global schema validates every consumer before generation publication"); }
            var valid = Encoding.UTF8.GetBytes(common + "# explicit edit\n");
            ImportedRecipeEdits.Save(global, opened, new Dictionary<string, byte[]> { [commonPath] = valid });
            check(store.Read().Revision == revision + 1 && store.Read().Documents["recipe.yaml"]["data"]!["text"]!.GetValue<string>() == File.ReadAllText(commonPath), "global save commits intention and projection together");
            check(!store.Read().Documents.Keys.Any(path => path.Contains("active", StringComparison.Ordinal)), "saving global intention creates no execution");
            try { ImportedRecipeEdits.Save(global, opened, new Dictionary<string, byte[]> { [commonPath] = valid }); check(false, "stale global draft accepted"); }
            catch (StorageException error) { check(error.Code == "intention-conflict", "stale global drafts require explicit conflict resolution"); }
            File.AppendAllText(commonPath, "# manual projection edit before opening\n");
            var manuallyOpened = new Dictionary<string, byte[]> { [commonPath] = File.ReadAllBytes(commonPath) };
            ImportedRecipeEdits.Save(global, manuallyOpened, new Dictionary<string, byte[]> { [commonPath] = Encoding.UTF8.GetBytes(File.ReadAllText(commonPath) + "# explicit save after opening\n") });
            check(store.Read().Documents["recipe.yaml"]["data"]!["text"]!.GetValue<string>().Contains("manual projection edit before opening"), "explicit save retains manual projection changes that the editor opened");
            var existing = DashboardCli.ExistingImportEntities(global, RecipeReader.Read(selection));
            check(existing.Contains("param:context") && existing.Contains("preset:assistant"), "L identifies existing entities before authorizing replacement");

            // U before the first use: no identity yet, so Comprobar is off and says why instead of failing.
            var freshFile = Path.Combine(root, "fresh", "coquena.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(freshFile)!);
            File.WriteAllText(freshFile, "name: fresh\n");
            var fresh = new DashboardCli.UpdateView();
            DashboardCli.ReadUpdates(ScopeContext.Project(freshFile), [new RecipeSelection(Path.GetDirectoryName(freshFile)!)], fresh);
            var freshSpans = DashboardCli.UpdatesPage("fresh", fresh, []).Rows.SelectMany(Markup.Parse).ToArray();
            check(fresh.Unprepared && fresh.Problem is null && !freshSpans.Any(span => span.Action == "update-check") &&
                  fresh.Notice == Messages.CombinationNotPrepared, "U disables Comprobar on an unprepared combination and explains it");
            var noServiceGlobal = ScopeContext.Global(new Home(Path.Combine(root, "no-service-home")));
            check(!DashboardCli.HasDefaultGlobalService(noServiceGlobal), "L detects a Global scope without default service before importing presets");

            File.WriteAllText(file, "name: fixture\npresets: {p: {args: {temperature: 0.5}}}\n");
            var context = ScopeContext.Project(file);
            await new CombinationPreparation(context).PrepareAsync(selection, new NoResources());
            using var client = HuggingFaceStore.CreateClient();
            var probe = new ResourceUpdateProbe(new(client), (_, _) => throw new Exception("unexpected network"), new Quiet());
            await new RecipeUpdatePlans(context).UpdateAsync([selection], new HashSet<string>(), probe);
            // One installation shared by two intentions is one row; its box covers both selectors.
            var shared = new DashboardCli.UpdateView { Plan = JsonNode.Parse("""
                {"created-at":"2026-11-10T20:24:01Z","resources":[
                {"kind":"backend","key":"backend","alias":"svc","recipe":"one","resource-id":"r","installation-id":"i","status":"update-available","current":{"release":"b1"},"proposed":{"release":"b2"}},
                {"kind":"backend","key":"backend","alias":"svc","recipe":"two","resource-id":"r","installation-id":"i","status":"update-available","current":{"release":"b1"},"proposed":{"release":"b2"}}]}
                """)!.AsObject() };
            var sharedSpans = DashboardCli.UpdatesPage("Global", shared, []).Rows.SelectMany(Markup.Parse).ToArray();
            var toggles = sharedSpans.Where(span => span.Action?.StartsWith("update-toggle:", StringComparison.Ordinal) == true).ToArray();
            check(toggles.Length == 1 && System.Text.Encoding.UTF8.GetString(Convert.FromHexString(toggles[0].Action![14..])).Split('\n')
                .ToHashSet().SetEquals(["backend://one/svc", "backend://two/svc"]) &&
                sharedSpans.Any(span => span.Text.Contains(Messages.UsedByIntentions(2))), "a shared installation is one row that selects every intention");
            var state = new DashboardCli.UpdateView();
            DashboardCli.ReadUpdates(context, [selection], state);
            check(state.Problem is null && state.Plan is not null && state.Chosen.Count == 0, "U reads a current plan without inventing proposals");
            File.AppendAllText(file, "# external edit\n");
            DashboardCli.ReadUpdates(context, [selection], state);
            check(state.Problem is not null && state.Plan is not null && !DashboardCli.UpdatesPage("fixture", state, []).Rows.SelectMany(Markup.Parse).Any(span => span.Action == "update-apply"), "stale plan remains visible but cannot be applied");
        }
        finally { Directory.Delete(root, true); }
    }

    private static JsonObject Intention(string text) => ProjectDocuments.Create("recipe-intention", 1, new JsonObject { ["text"] = text, ["path-bases"] = new JsonObject() });
    private sealed class NoResources : ICombinationResources
    {
        public Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exact, CancellationToken cancellation) => throw new Exception("unexpected resource");
    }
    private sealed class Quiet : IModelSelectionInteraction
    {
        public bool IsInteractive => false;
        public void Show(string text) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation) => Task.FromResult<int?>(null);
    }
}
