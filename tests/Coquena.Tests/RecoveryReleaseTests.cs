using System.Text.Json.Nodes;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class RecoveryReleaseTests
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-recovery-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ScopeStore(ScopeContext.Project(Path.Combine(root, "coquena.yaml")));
            var registry = new RecipeRegistry(store);
            var previous = registry.EnsureProjectService(["previous.yaml"], "previous");
            var target = registry.EnsureProjectService(["target.yaml"], "target");
            var home = new Home(Path.Combine(root, "home"));
            string GoodPath(ServiceIdentity service) => store.RuntimeDocumentPath(service.ServiceId, "last-good");
            void Good(ServiceIdentity service, JsonArray? composition, JsonArray? consumers = null)
            {
                var state = store.Read(); var docs = RecipeRegistry.Next(state);
                var data = new JsonObject { ["service-id"] = service.ServiceId, ["resources"] = new JsonArray() };
                if (composition is not null) data["preset-composition"] = composition;
                docs[GoodPath(service)] = ProjectDocuments.Create("last-good", 1, data);
                var entity = docs["local/recipes.json"]["data"]!["recipes"]!.AsArray().SelectMany(r => r!["entities"]!.AsArray())
                    .Single(e => e!["id"]!.GetValue<string>() == service.ServiceId)!.AsObject();
                if (consumers is null) entity.Remove("consumers"); else entity["consumers"] = consumers;
                store.Commit(state.Revision, docs);
            }
            static JsonArray Consumers(params string[] ids) => new(ids.Select(id => (JsonNode?)new JsonObject { ["consumer-id"] = id }).ToArray());
            bool Has(ServiceIdentity service) => store.Read().Documents.ContainsKey(GoodPath(service));

            Good(target, null);
            Good(previous, Consumers("shared", "other"));
            check(RecoveryRelease.Release(store, "shared", store, target.ServiceId, home).Count == 0 && Has(previous),
                "a recovery shared with another consumer is kept");
            Good(previous, Consumers("shared"), Consumers("other"));
            check(RecoveryRelease.Release(store, "shared", store, target.ServiceId, home).Count == 0 && Has(previous),
                "a service where another recipe still publishes keeps its recovery");
            Good(previous, Consumers("shared"));
            var released = RecoveryRelease.Release(store, "shared", store, target.ServiceId, home);
            check(released.Count == 1 && released[0].ServiceName == "previous" && !Has(previous) && Has(target),
                "running healthy elsewhere retires the recovery this project alone left, never the target's");
            Good(previous, null);
            check(RecoveryRelease.Release(store, "shared", store, target.ServiceId, home).Count == 1 && !Has(previous),
                "a recovery recorded without its composition is attributed to the project that owns the service");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
