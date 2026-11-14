using System.Text;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Projects;

// Edit imported intention through its immutable generation. The projections are never
// published on their own, and active snapshots/publications remain unchanged.
internal static class ImportedRecipeEdits
{
    internal static void Save(ScopeContext context, IReadOnlyDictionary<string, byte[]> opened,
        IReadOnlyDictionary<string, byte[]> drafts, bool overwrite = false)
    {
        if (!context.IsGlobal) throw new ArgumentException("La intención importada requiere Global.");
        var store = new ScopeStore(context);
        var snapshot = store.Read();
        var documents = RecipeRegistry.Next(snapshot);
        var captured = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var observed = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, doc) in documents.Where(pair => pair.Value["kind"]?.GetValue<string>() == "recipe-intention"))
        {
            var projection = context.PathFor(path);
            observed[projection] = File.Exists(projection) ? SharedFile.ReadAllBytes(projection) : null;
            captured[projection] = observed[projection] ?? Encoding.UTF8.GetBytes(doc["data"]!["text"]!.GetValue<string>());
        }
        foreach (var path in opened.Keys.Where(path => !observed.ContainsKey(path)))
            observed[path] = File.Exists(path) ? SharedFile.ReadAllBytes(path) : null;
        foreach (var draft in drafts)
        {
            if (!opened.TryGetValue(draft.Key, out var original))
                throw new StorageException("intention-conflict", "El archivo ya no pertenece a la intención global.");
            var relative = Path.GetRelativePath(context.Root, draft.Key).Replace('\\', '/');
            var known = captured.ContainsKey(draft.Key);
            if (!known)
            {
                if (relative != "recipe.yaml" || File.Exists(draft.Key) || original.Length != 0)
                    throw new StorageException("intention-conflict", "Sólo se puede crear la intención común ausente; no se adoptan archivos existentes.");
                documents[relative] = ProjectDocuments.Create("recipe-intention", 1, new System.Text.Json.Nodes.JsonObject { ["text"] = "", ["path-bases"] = new System.Text.Json.Nodes.JsonObject() });
            }
            if (!overwrite && (observed[draft.Key] is { } projection ? !projection.AsSpan().SequenceEqual(original) : known || original.Length != 0))
                throw new StorageException("intention-conflict", "Cambió la intención global; revisar los cambios antes de guardar.");
            captured[draft.Key] = draft.Value;
            documents[relative]["data"]!["text"] = RecipeYaml.ReadText(draft.Key, false, draft.Value);
        }
        // A shared schema change is validated against every imported consumer/service.
        var paths = documents.Where(pair => pair.Value["kind"]?.GetValue<string>() == "recipe-intention").Select(pair => pair.Key).ToArray();
        var commonChanged = drafts.ContainsKey(context.PathFor("recipe.yaml"));
        foreach (var path in paths.Where(path => commonChanged || drafts.ContainsKey(context.PathFor(path))))
        {
            var files = path == "recipe.yaml" || !paths.Contains("recipe.yaml") ? new[] { context.PathFor(path) } : new[] { context.PathFor("recipe.yaml"), context.PathFor(path) };
            if (files.Any(file => observed.GetValueOrDefault(file) is null && !drafts.ContainsKey(file)))
                throw new StorageException("intention-conflict", "Falta una intención global afectada; no se sustituye por su copia anterior.");
            var selection = new RecipeSelection(context.Base, files, context.Base)
            { CapturedFiles = captured, ImportedScope = snapshot.State is null ? null : context, FilterUnusedModels = path != "recipe.yaml" };
            var recipe = RecipeReader.Read(selection);
            if (recipe.Root.Get("service")?.Get("backend") is not null) _ = Backends.BackendIntent.FromRecipe(recipe);
            _ = Models.ModelIntent.FromRecipe(recipe);
            _ = ArgumentValidation.Validate(recipe);
        }
        ScopeStore.CommitTogether([new(store, snapshot.Revision, documents, Validate: () =>
        {
            foreach (var file in observed)
            {
                var current = File.Exists(file.Key) ? SharedFile.ReadAllBytes(file.Key) : null;
                if (current is null != (file.Value is null) || current is not null && !current.AsSpan().SequenceEqual(file.Value))
                    throw new StorageException("intention-conflict", "Cambió una proyección durante el guardado.");
            }
        })]);
    }
}
