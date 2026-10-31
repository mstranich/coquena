using Coquena.Recipes;

namespace Coquena.Tests;

internal static class RecipeImportTests
{
    public static void Run()
    {
        var root = Path.Combine(@"D:\tmp", "coquena-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "coquena.yaml");
        var yaml = """
            name: proyecto
            service:
              backend:
                executable: ${BACKEND}/llama-server.exe
            models:
              propio:
                files: ["${WEIGHTS}/modelo.gguf"]
              remoto: unsloth/LFM2.5-8B-A1B-GGUF:Q4_0
            companions:
              proyector:
                type: mmproj
                files: [./proyector.gguf]
            presets:
              resumen:
                args:
                  model: model://propio
            arg-defs:
              imports: [./schema.json]
              definitions:
                temperatura:
                  type: number
            """;
        File.WriteAllText(file, yaml);
        File.WriteAllText(Path.Combine(root, "schema.json"), """{"definitions":{"ctx-size":{"type":"integer"}}}""");
        var env = new Dictionary<string, string?> { ["BACKEND"] = "./bin", ["WEIGHTS"] = "./weights" };
        var recipe = RecipeReader.Read(new(root), env);
        var count = 0;
        void Check(bool value, string message)
        {
            count++;
            if (!value) throw new Exception("Import selection: " + message);
        }
        void Bad(Func<RecipeImportSelection> action, string code)
        {
            try { action(); }
            catch (RecipeException error) when (error.Code == code) { count++; return; }
            throw new Exception("Expected import error: " + code);
        }
        var all = RecipeImportSelection.Create(recipe);
        Check(all.Categories.SequenceEqual(new[] { "service", "presets", "model", "params" }), "all declared categories");
        Check(all.Entities.Select(e => e.Selector).ToHashSet().SetEquals(new[]
        { "service:proyecto", "preset:resumen", "model:propio", "model:remoto", "companion:proyector", "param:ctx-size", "param:temperatura" }),
            "promoted selectors and imported schema keys");
        Check(!all.AllowsReplacement("model:propio"), "default replacement rejection");
        var selected = RecipeImportSelection.Create(recipe, "model", ["model:propio"]);
        Check(selected.Entities.Count == 3 && selected.Entities.All(e => e.Selector.StartsWith("model:") || e.Selector.StartsWith("companion:")), "model includes companions only");
        Check(selected.AllowsReplacement("model:propio") && !selected.AllowsReplacement("model:remoto"), "entity replacement authorization");
        var forced = RecipeImportSelection.Create(recipe, force: true, asDefault: true);
        Check(forced.AsDefault && forced.AllowsReplacement("preset:resumen") && !forced.AllowsReplacement("preset:ajeno"), "force limited to declared entities");
        Bad(() => RecipeImportSelection.Create(recipe, ""), "invalid-import-selection");
        Bad(() => RecipeImportSelection.Create(recipe, "model,"), "invalid-import-selection");
        Bad(() => RecipeImportSelection.Create(recipe, "model,model"), "invalid-import-selection");
        Bad(() => RecipeImportSelection.Create(recipe, "models"), "invalid-import-selection");
        Bad(() => RecipeImportSelection.Create(recipe, "model", force: true), "import-option-conflict");
        Bad(() => RecipeImportSelection.Create(recipe, replace: ["model:propio"], force: true), "import-option-conflict");
        Bad(() => RecipeImportSelection.Create(recipe, "model", asDefault: true), "import-default-requires-service");
        Bad(() => RecipeImportSelection.Create(recipe, "model", ["preset:resumen"]), "invalid-import-replacement");
        Bad(() => RecipeImportSelection.Create(recipe, replace: ["preset:proyecto/resumen"]), "invalid-import-replacement");
        Bad(() => RecipeImportSelection.Create(recipe, replace: ["model:propio", "model:propio"]), "invalid-import-replacement");
        File.WriteAllText(file, yaml + "\ntarget: self\n");
        var self = RecipeReader.Read(new(root), env);
        Bad(() => RecipeImportSelection.Create(self, "presets"), "import-self-requires-service");
        Check(RecipeImportSelection.Create(self, "service,presets").Entities.Count == 2, "self with selected service");
        File.WriteAllText(file, "presets: {p: {}}\n");
        var minimal = RecipeReader.Read(new(root), env);
        Bad(() => RecipeImportSelection.Create(minimal, "model"), "absent-import-category");
        Check(RecipeImportSelection.Create(minimal).Categories.SequenceEqual(new[] { "presets" }), "no hidden categories");
        // An excluded schema is not opened just to enumerate other categories.
        File.Delete(Path.Combine(root, "schema.json"));
        Check(RecipeImportSelection.Create(recipe, "presets").Entities.Single().Selector == "preset:resumen", "excluded schema is not loaded");
        Check(recipe.SourceRoot.Get("models")!.Get("propio")!.Get("files")!.Items[0].Text == "${WEIGHTS}/modelo.gguf", "source expression is intact");
        Check(!Directory.Exists(Path.Combine(root, ".coquena")), "selection creates no project state or resources");
        Check(File.ReadAllText(file) == "presets: {p: {}}\n", "selection does not edit source");
        Console.WriteLine($"Recipe import selection: {count} assertions passed.");
    }
}
