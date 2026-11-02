using System.Text.Json.Nodes;
using Coquena.Storage;

namespace Coquena.Projects;

public sealed class CombinationCleanup(ScopeContext context)
{
    public void Forget(string id)
    {
        if (!ScopeStore.IsCombinationId(id)) throw new StorageException("invalid-combination-id", "Se requiere el ID completo.");
        var store = new ScopeStore(context); var state = store.Read(); var docs = RecipeRegistry.Next(state);
        var found = false;
        foreach (var path in new[] { "local/combinations.json", "combinations.json" })
            if (docs.GetValueOrDefault(path)?["data"]?["combinations"] is JsonObject entries) found |= entries.Remove(id);
        if (!found) throw new StorageException("unknown-combination", "Combinación no registrada en este ámbito.");
        var removed = docs.Where(p => p.Value["kind"]!.GetValue<string>() == "resolution" && p.Value["data"]?["combination-id"]?.GetValue<string>() == id ||
            p.Value["kind"]!.GetValue<string>() == "last-good" && p.Value["data"]?["combination"]?.GetValue<string>() == id).Select(p => p.Key).ToHashSet();
        foreach (var path in removed) docs.Remove(path);
        ScopeStore.CommitTogether([new(store, state.Revision, docs, RemovedDocuments: removed)]);
    }
}
