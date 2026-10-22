using System.Text.Json.Nodes;
using Coquena.Storage;

namespace Coquena.Projects;

public sealed record RecipeIdentity(string RecipeId, string ConsumerId, string RegisteredName, string[] Files);
public sealed record ServiceIdentity(string ServiceId, string RecipeId, string Name);
public sealed record ResourceIdentity(string ResourceId, string LogicalKey, string Kind, string Ownership);

// Registration creates an explicit local identity. The caller selects an existing
// identity through Get; file lists are evidence, never an inferred identity key.
// Parsing/composition and service process control are outside this infrastructure.
public sealed class RecipeRegistry(ScopeStore store)
{
    private const string RegistryPath = "local/recipes.json";
    private const string InventoryPath = "local/inventory.json";

    public RecipeIdentity Register(IReadOnlyList<string> files, string? declaredName = null)
    {
        var normalized = Normalize(files);
        var snapshot = store.Read();
        var documents = Next(snapshot);
        var registry = Data(documents, RegistryPath, "recipe-registry", "recipes");
        var recipes = registry["recipes"]!.AsArray();
        var name = declaredName ?? Path.GetFileName(store.Context.Base);
        ValidateName(name);
        var entry = new JsonObject
        {
            ["recipe-id"] = ProjectDocuments.Id(), ["consumer-id"] = ProjectDocuments.Id(), ["name"] = name,
            ["files"] = Strings(normalized), ["revision"] = 1, ["entities"] = new JsonArray(),
            ["pending-withdrawals"] = new JsonArray()
        };
        recipes.Add((JsonNode)entry);
        store.Commit(snapshot.Revision, documents);
        return Recipe(entry);
    }

    public RecipeIdentity[] Recipes() => store.Read().Documents.TryGetValue(RegistryPath, out var doc)
        ? doc["data"]!["recipes"]!.AsArray().Select(x => Recipe(x!)).ToArray() : [];

    public RecipeIdentity Get(string recipeId)
    {
        ProjectDocuments.Uuid(JsonValue.Create(recipeId));
        var snapshot = store.Read();
        if (!snapshot.Documents.TryGetValue(RegistryPath, out var document))
            throw new StorageException("unknown-recipe", "Receta no registrada.");
        return Recipe(FindRecipe(document["data"]!.AsObject(), recipeId));
    }

    public ServiceIdentity RegisterService(string recipeId)
    {
        var snapshot = store.Read();
        var documents = Next(snapshot);
        var registry = Data(documents, RegistryPath, "recipe-registry", "recipes");
        var recipe = FindRecipe(registry, recipeId);
        var existing = recipe["entities"]!.AsArray().FirstOrDefault(x => x!["kind"]!.GetValue<string>() == "service");
        if (existing is not null) return new(existing["id"]!.GetValue<string>(), recipeId, existing["name"]!.GetValue<string>());
        var name = recipe["name"]!.GetValue<string>();
        if (name is "self" or "default") throw new StorageException("reserved-service-name", "Nombre de servicio reservado.");
        var entities = registry["recipes"]!.AsArray().SelectMany(x => x!["entities"]!.AsArray());
        if (entities.Any(x => x!["kind"]!.GetValue<string>() == "service" && x["name"]!.GetValue<string>() == name))
            throw new StorageException("service-name-conflict", "Nombre de servicio ya registrado en el ámbito.");
        var id = ProjectDocuments.Id();
        recipe["entities"]!.AsArray().Add((JsonNode)new JsonObject { ["kind"] = "service", ["id"] = id, ["name"] = name, ["owner"] = recipeId });
        recipe["revision"] = checked(recipe["revision"]!.GetValue<long>() + 1);
        store.Commit(snapshot.Revision, documents);
        return new(id, recipeId, name);
    }

    public ServiceIdentity[] Services() => store.Read().Documents.TryGetValue(RegistryPath, out var doc)
        ? doc["data"]!["recipes"]!.AsArray().SelectMany(r => r!["entities"]!.AsArray()
            .Where(e => e!["kind"]!.GetValue<string>() == "service")
            .Select(e => new ServiceIdentity(e!["id"]!.GetValue<string>(), r!["recipe-id"]!.GetValue<string>(), e["name"]!.GetValue<string>()))).ToArray() : [];

    public ResourceIdentity RegisterResource(string logicalKey, string kind, string ownership)
    {
        ValidateName(logicalKey);
        if (kind is not ("model" or "companion" or "backend") || ownership is not ("managed" or "external")) throw new ArgumentException("Tipo/propiedad inválidos.");
        var snapshot = store.Read();
        var documents = Next(snapshot);
        var inventory = Data(documents, InventoryPath, "inventory", "resources");
        var resources = inventory["resources"]!.AsArray();
        var existing = resources.FirstOrDefault(x => x!["logical-key"]!.GetValue<string>() == logicalKey && x["kind"]!.GetValue<string>() == kind);
        if (existing is not null)
        {
            if (existing["ownership"]!.GetValue<string>() != ownership) throw new StorageException("resource-ownership-conflict", "La ubicación o clave no autoriza cambiar propiedad.");
            return Resource(existing);
        }
        var entry = new JsonObject { ["resource-id"] = ProjectDocuments.Id(), ["logical-key"] = logicalKey,
            ["kind"] = kind, ["ownership"] = ownership, ["installations"] = new JsonArray() };
        resources.Add((JsonNode)entry);
        store.Commit(snapshot.Revision, documents);
        return Resource(entry);
    }

    public ResourceIdentity[] Resources() => store.Read().Documents.TryGetValue(InventoryPath, out var doc)
        ? doc["data"]!["resources"]!.AsArray().Select(x => Resource(x!)).ToArray() : [];

    public void SetDefaultService(string serviceId)
    {
        var snapshot = store.Read();
        ScopeStore.CommitTogether([new(store, snapshot.Revision, Next(snapshot), serviceId)]);
    }

    public string ResolveGlobalService(string selector)
    {
        if (!store.Context.IsGlobal) throw new StorageException("global-scope-required", "La consulta requiere el ámbito global.");
        var snapshot = store.Read();
        var services = snapshot.State?["data"]?["services"] as JsonArray ?? new JsonArray();
        var id = selector == "default" ? snapshot.State?["data"]?["aliases"]?["default"]?.GetValue<string>()
            : services.SingleOrDefault(s => s!["name"]!.GetValue<string>() == selector)?["id"]?.GetValue<string>();
        if (id is null || !services.Any(s => s!["id"]!.GetValue<string>() == id))
            throw new StorageException("unknown-service", "No resuelve el servicio global seleccionado; no se elige sustituto.");
        return id;
    }

    // Establish both ends and resource protection in the same recoverable commit.
    // The caller passes identities, never names/aliases that could redirect mid-operation.
    public void RecordContribution(string recipeId, ScopeStore serviceStore, string serviceId,
        IReadOnlyList<string> publishedNames, IReadOnlyList<string> resourceIds)
    {
        var own = store.Read();
        var sameScope = store.Context.PhysicalRoot == serviceStore.Context.PhysicalRoot;
        var target = sameScope ? own : serviceStore.Read();
        var ownDocs = Next(own);
        var targetDocs = sameScope ? ownDocs : Next(target);
        var registry = Data(ownDocs, RegistryPath, "recipe-registry", "recipes");
        var recipe = FindRecipe(registry, recipeId);
        var targetRegistry = Data(targetDocs, RegistryPath, "recipe-registry", "recipes");
        var targetService = targetRegistry["recipes"]!.AsArray().SelectMany(x => x!["entities"]!.AsArray())
            .SingleOrDefault(x => x!["kind"]!.GetValue<string>() == "service" && x["id"]!.GetValue<string>() == serviceId);
        if (targetService is null) throw new StorageException("unknown-service", "El servicio destino no está registrado en ese ámbito.");
        var resources = ownDocs.TryGetValue(InventoryPath, out var inv) ? inv["data"]!["resources"]!.AsArray() : new JsonArray();
        foreach (var id in resourceIds)
            if (!resources.Any(x => x!["resource-id"]!.GetValue<string>() == id)) throw new StorageException("unknown-resource", "Recurso no registrado en el ámbito consumidor.");
        var consumerId = recipe["consumer-id"]!.GetValue<string>();
        var contributions = targetService["consumers"] as JsonArray ?? new JsonArray();
        foreach (var name in publishedNames)
        {
            var parts = name.Split('/');
            if (parts.Length != 2) throw new StorageException("invalid-published-name", "Se requiere receta/preset.");
            foreach (var part in parts) ValidateName(part);
            if (contributions.Any(x => x!["consumer-id"]!.GetValue<string>() != consumerId && x["names"]!.AsArray().Any(y => y!.GetValue<string>() == name)))
                throw new StorageException("preset-name-conflict", "Nombre publicado por otro consumidor.");
        }
        var previous = contributions.FirstOrDefault(x => x!["consumer-id"]!.GetValue<string>() == consumerId);
        if (previous is not null) contributions.Remove(previous);
        contributions.Add((JsonNode)new JsonObject { ["consumer-id"] = consumerId, ["scope-id"] = own.ScopeId,
            ["names"] = Strings(publishedNames), ["resources"] = Strings(resourceIds) });
        if (targetService["consumers"] is null) targetService["consumers"] = contributions;
        recipe["target"] = new JsonObject { ["scope-id"] = target.ScopeId, ["service-id"] = serviceId };
        recipe["published-names"] = Strings(publishedNames);
        recipe["revision"] = checked(recipe["revision"]!.GetValue<long>() + 1);
        if (sameScope) store.Commit(own.Revision, ownDocs);
        else ScopeStore.CommitTogether([new(store, own.Revision, ownDocs), new(serviceStore, target.Revision, targetDocs)]);
    }

    public void RegisterInstance(string serviceId, string instanceId, JsonObject processIdentity)
    {
        ProjectDocuments.Uuid(JsonValue.Create(instanceId));
        if (processIdentity["pid"]?.GetValue<int>() is not > 0 || processIdentity["started-at"] is null || processIdentity["executable"] is null)
            throw new StorageException("invalid-instance-identity", "Se requiere PID, inicio e identidad del ejecutable.");
        var snapshot = store.Read();
        var documents = Next(snapshot);
        var registry = Data(documents, RegistryPath, "recipe-registry", "recipes");
        var service = registry["recipes"]!.AsArray().SelectMany(x => x!["entities"]!.AsArray())
            .SingleOrDefault(x => x!["kind"]!.GetValue<string>() == "service" && x["id"]!.GetValue<string>() == serviceId);
        if (service is null) throw new StorageException("unknown-service", "Servicio no registrado.");
        if (service["instance"] is not null) throw new StorageException("instance-already-registered", "El servicio ya conserva una instancia; se exige reconciliar antes de reemplazarla.");
        service["instance"] = new JsonObject { ["instance-id"] = instanceId, ["process"] = processIdentity.DeepClone() };
        store.Commit(snapshot.Revision, documents);
    }

    private string[] Normalize(IReadOnlyList<string> files)
    {
        if (files.Count == 0) throw new ArgumentException("Selección vacía.");
        return files.Select(file => Path.GetRelativePath(store.Context.Base, Path.GetFullPath(file, store.Context.Base)).Replace('\\', '/')).ToArray();
    }
    internal static Dictionary<string, JsonObject> Next(ScopeSnapshot snapshot) => snapshot.Documents.ToDictionary(x => x.Key,
        x => ProjectDocuments.Create(x.Value["kind"]!.GetValue<string>(), checked(x.Value["revision"]!.GetValue<long>() + 1), x.Value["data"]!.AsObject()), StringComparer.Ordinal);
    private static JsonObject Data(Dictionary<string, JsonObject> documents, string path, string kind, string collection)
    {
        if (!documents.TryGetValue(path, out var document))
        {
            document = ProjectDocuments.Create(kind, 1, new JsonObject { [collection] = new JsonArray() });
            documents.Add(path, document);
        }
        return document["data"]!.AsObject();
    }
    private static JsonNode FindRecipe(JsonObject registry, string id) => registry["recipes"]!.AsArray()
        .SingleOrDefault(x => x!["recipe-id"]!.GetValue<string>() == id) ?? throw new StorageException("unknown-recipe", "Receta no registrada.");
    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
    private static RecipeIdentity Recipe(JsonNode node) => new(ProjectDocuments.Uuid(node["recipe-id"]), ProjectDocuments.Uuid(node["consumer-id"]),
        node["name"]!.GetValue<string>(), node["files"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());
    private static ResourceIdentity Resource(JsonNode node) => new(ProjectDocuments.Uuid(node["resource-id"]), node["logical-key"]!.GetValue<string>(), node["kind"]!.GetValue<string>(), node["ownership"]!.GetValue<string>());
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\0', '\r', '\n', ':', '/', '\\']) >= 0)
            throw new StorageException("invalid-entity-name", "Nombre inválido; se requiere un componente explícito válido.");
    }
}
