using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Coquena.Recipes;
using Coquena.Storage;
using Coquena.Models;

namespace Coquena.Projects;

// Local plans preserve exact proposals; reading one never re-queries the origin.
public sealed class RecipeUpdatePlans(ScopeContext context)
{
    private const string PathName = "local/update-plan.json";
    private readonly ScopeStore store = new(context);

    public async Task<JsonObject> UpdateAsync(IReadOnlyList<RecipeSelection> selections,
        IReadOnlySet<string> selectors, ResourceUpdateProbe probe, CancellationToken cancellation = default)
    {
        if (selections.Count == 0) throw new StorageException("update-selection-required", "No hay definiciones registradas que consultar.");
        if (selections.Any(s => !s.Base.Equals(context.Base, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("La selección debe pertenecer a la base del ámbito.");
        var snapshot = store.Read();
        var filters = selectors.Select(ResourceSelector.Parse).ToArray();
        var key = ReadKey();
        var originalInputs = selections.SelectMany(s => s.Files.Concat(s.EnvFiles)).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        var recipes = selections.Select(s => RecipeReader.Read(s)).ToArray();
        var inputs = Inputs(recipes);
        if (originalInputs.Any(p => !inputs[p.Key].SequenceEqual(p.Value)))
            throw new StorageException("stale-update-plan", "Cambió la intención al leerla.");
        var requests = recipes.SelectMany(recipe => Requests(recipe)
            .Select(request =>
            {
                var binding = CombinationPreparation.VerificationBinding(context, recipe, request, false);
                var resource = snapshot.Documents.GetValueOrDefault("local/inventory.json")?["data"]?["resources"]?.AsArray()
                    .SingleOrDefault(r => r!["resource-id"]!.GetValue<string>() == binding.ResourceId)?.AsObject();
                var installation = resource?["installations"]?.AsArray().SingleOrDefault(i => i!["installation-id"]!.GetValue<string>() == binding.InstallationId)?.AsObject();
                return (Recipe: recipe, Request: request, Resource: resource, Installation: installation);
            })).DistinctBy(p => (p.Request.Kind, p.Request.Key, p.Resource?["resource-id"]?.GetValue<string>(),
                p.Installation?["installation-id"]?.GetValue<string>(), CombinationPreparation.Expanded(p.Request.Intent).ToJsonString(),
                p.Request.Kind == "backend" ? CombinationIdentity.FromSelection(p.Recipe.Selection).Id : "")).ToArray();
        foreach (var filter in filters)
        {
            if (!requests.Any(p => filter.Matches(p.Request.Kind, p.Request.Key, p.Resource, p.Installation, p.Request)))
                throw new ArgumentException("Selector desconocido; no se amplía la selección.");
            filter.CheckAmbiguity(requests.Where(p => filter.Matches(p.Request.Kind, p.Request.Key, p.Resource, p.Installation, p.Request)).Select(p => p.Request.ProjectName));
        }
        var chosen = requests.Where(p => filters.Length == 0 || filters.Any(f => f.Matches(p.Request.Kind, p.Request.Key, p.Resource, p.Installation, p.Request))).ToArray();
        var results = new JsonArray();
        foreach (var (recipe, request, resource, installation) in chosen)
        {
            cancellation.ThrowIfCancellationRequested();
            JsonObject observation;
            if (resource is null || installation is null) observation = new() { ["key"] = request.Key, ["kind"] = request.Kind, ["status"] = "not-prepared" };
            else observation = await probe.CheckAsync(request, resource, installation, cancellation);
            observation["combination-id"] = CombinationIdentity.FromSelection(recipe.Selection).Id;
            observation["recipe"] = recipe.Name;
            if (request.Alias is not null) observation["alias"] = request.Alias;
            observation["sensitive"] = request.SensitiveSelection || Sensitive(request.Intent) || recipe.Root.Get("name")?.Sensitive == true;
            results.Add((JsonNode)observation);
        }
        var id = PlanId(selections);
        var plan = new JsonObject { ["scope-id"] = snapshot.ScopeId, ["combination-id"] = id,
            ["base-revision"] = snapshot.Revision, ["created-at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["intent-hmac"] = Fingerprint(key, recipes, inputs),
            ["resolution-hmac"] = ResolutionFingerprint(key, snapshot, recipes),
            ["selectors"] = new JsonArray(selectors.Order(StringComparer.OrdinalIgnoreCase).Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()),
            ["resources"] = results };
        cancellation.ThrowIfCancellationRequested();
        var docs = RecipeRegistry.Next(snapshot);
        var document = docs.GetValueOrDefault(PathName) ?? ProjectDocuments.Create("update-plan", 1, new JsonObject { ["plans"] = new JsonObject() });
        document["data"]!["plans"]![id] = plan.DeepClone();
        docs[PathName] = document;
        store.Locked(() =>
        {
            var path = context.PathFor(".gitignore"); var text = File.Exists(path) ? File.ReadAllText(path) : "";
            if (!text.Split('\n').Any(line => line.TrimEnd('\r') == "/local/"))
                ScopeStore.Replace(path, Encoding.UTF8.GetBytes(text + (text.Length > 0 && !text.EndsWith('\n') ? "\n" : "") + "/local/\n"));
            return true;
        });
        ScopeStore.CommitTogether([new(store, snapshot.Revision, docs, Validate: () =>
        {
            if (!ReadKey().SequenceEqual(key) || inputs.Any(p => !File.ReadAllBytes(p.Key).SequenceEqual(p.Value)) ||
                Fingerprint(key, selections.Select(s => RecipeReader.Read(s)).ToArray(), inputs) != plan["intent-hmac"]!.GetValue<string>())
                throw new StorageException("stale-update-plan", "Cambió la intención durante la consulta.");
        })]);
        return plan;
    }

    public JsonObject ReadValid(IReadOnlyList<RecipeSelection> selections)
    {
        var state = store.Read();
        var plan = state.Documents.GetValueOrDefault(PathName)?["data"]?["plans"]?[PlanId(selections)]?.AsObject()
            ?? throw new StorageException("update-plan-required", "Ejecute update para esta selección.");
        var recipes = selections.Select(s => RecipeReader.Read(s)).ToArray();
        if (plan["scope-id"]?.GetValue<string>() != state.ScopeId || plan["intent-hmac"]!.GetValue<string>() != Fingerprint(ReadKey(), recipes, Inputs(recipes)))
            throw new StorageException("stale-update-plan", "Cambió la intención; se requiere un update nuevo.");
        if (plan["resolution-hmac"]?.GetValue<string>() != ResolutionFingerprint(ReadKey(), state, recipes))
            throw new StorageException("stale-update-plan", "Cambió la revisión de partida; se requiere un update nuevo.");
        foreach (var observation in plan["resources"]!.AsArray().Where(r => r!["installation-id"] is not null))
        {
            var recipe = recipes.Single(r => CombinationIdentity.FromSelection(r.Selection).Id == observation!["combination-id"]!.GetValue<string>());
            var request = Requests(recipe).Single(r =>
                r.Key == observation!["key"]!.GetValue<string>() && r.Kind == observation["kind"]!.GetValue<string>());
            var binding = CombinationPreparation.VerificationBinding(context, recipe, request, false);
            if (binding.ResourceId != observation!["resource-id"]!.GetValue<string>() || binding.InstallationId != observation["installation-id"]!.GetValue<string>())
                throw new StorageException("stale-update-plan", "Cambió la resolución de partida; se requiere un update nuevo.");
        }
        return plan.DeepClone().AsObject();
    }

    public static JsonObject Report(JsonObject plan)
    {
        var resources = plan["resources"]!.DeepClone().AsArray();
        foreach (var resource in resources)
        {
            if (resource!["sensitive"]?.GetValue<bool>() == true)
                foreach (var field in new[] { "key", "recipe", "alias", "current", "proposed", "candidates", "actions", "diagnostic" })
                    if (resource[field] is not null) resource[field] = "<redacted>";
            resource.AsObject().Remove("sensitive");
        }
        return new JsonObject { ["created-at"] = plan["created-at"]!.DeepClone(), ["resources"] = resources };
    }
    private static bool Sensitive(RecipeValue value) => value.Sensitive || value.Fields.Values.Any(Sensitive) || value.Items.Any(Sensitive);

    private byte[] ReadKey()
    {
        var path = context.PathFor("local/intent.key");
        if (!File.Exists(path) || File.ReadAllBytes(path) is not { Length: 32 } key)
            throw new StorageException("update-preparation-required", "Prepare la combinación antes de consultar un plan; falta la identidad local de intención.");
        return key;
    }
    private IReadOnlyList<PreparationRequest> Requests(EffectiveRecipe recipe)
    {
        var requests = CombinationPreparation.Requests(recipe, ArgumentValidation.Validate(recipe));
        if (context.IsGlobal)
            foreach (var intent in ModelIntent.FromRecipe(recipe))
                if (!requests.Any(r => r.Kind == intent.Kind && r.Key.Equals(intent.Key, StringComparison.OrdinalIgnoreCase)))
                    requests.Add(new(intent.Key, intent.Kind, recipe.Root.Get(intent.Kind == "model" ? "models" : "companions")!.Get(intent.Key)!, ProjectName: recipe.Name));
        return requests;
    }
    private static string PlanId(IReadOnlyList<RecipeSelection> selections) => selections.Count == 1 ? CombinationIdentity.FromSelection(selections[0]).Id :
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", selections.Select(s => CombinationIdentity.FromSelection(s).Id).Order(StringComparer.Ordinal)))));
    private static Dictionary<string, byte[]> Inputs(IEnumerable<EffectiveRecipe> recipes)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in recipes)
            foreach (var path in recipe.Selection.Files.Concat(recipe.Selection.EnvFiles).Concat(recipe.Layers.SelectMany(l => l.Get("arg-defs")?.Get("imports")?.Items ?? []).Select(i => recipe.Selection.ResolvePath(i.Text!))))
                files[path] = File.ReadAllBytes(path);
        return files;
    }
    private static string Fingerprint(byte[] key, IEnumerable<EffectiveRecipe> recipes, Dictionary<string, byte[]> inputs)
    {
        var value = new JsonObject { ["recipes"] = new JsonArray(recipes.Select(r => CombinationPreparation.Expanded(r.Root)).ToArray()),
            ["files"] = new JsonArray(inputs.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => (JsonNode)new JsonObject { ["path"] = p.Key, ["bytes"] = Convert.ToBase64String(p.Value) }).ToArray()) };
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value.ToJsonString())));
    }
    private static string ResolutionFingerprint(byte[] key, ScopeSnapshot state, IEnumerable<EffectiveRecipe> recipes)
    {
        var sources = new JsonObject();
        foreach (var recipe in recipes)
        {
            var id = CombinationIdentity.FromSelection(recipe.Selection).Id;
            var entry = state.Documents.GetValueOrDefault("local/combinations.json")?["data"]?["combinations"]?[id];
            sources[id] = entry?.DeepClone();
        }
        if (state.Documents.GetValueOrDefault("local/recipes.json")?["data"]?["import-resolutions"] is JsonNode imports)
            sources["import-resolutions"] = imports.DeepClone();
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(sources.ToJsonString())));
    }
}
