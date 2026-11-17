using System.Text.Json.Nodes;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Projects;

public sealed record ReleasedRecovery(string ServiceName, bool Global);

// After a project runs healthy in one service, the recovery it left in another service is no
// longer needed: that last good is retired so its resources can be cleaned, in any scope.
// Only a last good whose sole consumer was this project is retired, and only from a service
// where no recipe publishes any more; nothing is deleted from disk here.
public static class RecoveryRelease
{
    public static IReadOnlyList<ReleasedRecovery> Release(ScopeStore consumer, string consumerId, ScopeStore target, string targetService, Home home)
    {
        var stores = new List<ScopeStore> { consumer };
        var global = new ScopeStore(ScopeContext.Global(home));
        if (!consumer.Context.IsGlobal && Directory.Exists(global.Context.Root)) stores.Add(global);
        var released = new List<ReleasedRecovery>();
        foreach (var store in stores)
        {
            var state = store.Read(); var docs = RecipeRegistry.Next(state);
            var removed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var service in new RecipeRegistry(store).Services())
            {
                if (SameRoot(store, target) && service.ServiceId == targetService) continue;
                var path = store.RuntimeDocumentPath(service.ServiceId, "last-good");
                if (docs.GetValueOrDefault(path)?["data"] is not JsonObject good) continue;
                if (!SoleConsumer(good, consumerId, owned: SameRoot(store, consumer) && !store.Context.IsGlobal)) continue;
                if (PresetPublication.Service(state, service.ServiceId)["consumers"] is JsonArray { Count: > 0 }) continue;
                docs.Remove(path); removed.Add(path);
                released.Add(new(service.Name, store.Context.IsGlobal));
            }
            if (removed.Count != 0) ScopeStore.CommitTogether([new(store, state.Revision, docs, RemovedDocuments: removed)]);
        }
        return released;
    }

    // The composition recorded with the last good names its consumers. A last good recorded
    // before that field existed is attributed only when its service belongs to this project.
    private static bool SoleConsumer(JsonObject good, string consumerId, bool owned) =>
        good["preset-composition"] is JsonArray composition
            ? composition.Count > 0 && composition.All(c => c?["consumer-id"]?.GetValue<string>() == consumerId)
            : owned;

    private static bool SameRoot(ScopeStore a, ScopeStore b) =>
        string.Equals(Path.GetFullPath(a.Context.Root), Path.GetFullPath(b.Context.Root), StringComparison.OrdinalIgnoreCase);
}
