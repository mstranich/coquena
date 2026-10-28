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

    // Explicitly bind the first selected recipe file when creating a service.
    // Existing unassociated records are never adopted solely by matching names.
    public ServiceIdentity EnsureProjectService(IReadOnlyList<string> files, string? declaredName)
    {
        if (store.Context.IsGlobal) throw new StorageException("project-scope-required", "El alta propia requiere un proyecto.");
        var normalized = Normalize(files);
        var selection = normalized[0];
        var snapshot = store.Read(); var docs = Next(snapshot);
        var registry = Data(docs, RegistryPath, "recipe-registry", "recipes");
        var recipes = registry["recipes"]!.AsArray();
        var matches = recipes.Where(r => r!["service-selection"]?.GetValue<string>() == selection).ToArray();
        if (matches.Length > 1) throw new StorageException("recipe-identity-conflict", "Varias recetas reclaman la misma selección.");
        if (matches.Length == 1)
        {
            var recipe = matches[0]!;
            var existing = recipe["entities"]!.AsArray().SingleOrDefault(e => e!["kind"]!.GetValue<string>() == "service");
            if (existing is not null) return new(existing["id"]!.GetValue<string>(), recipe["recipe-id"]!.GetValue<string>(), existing["name"]!.GetValue<string>());
        }
        var name = declaredName ?? Path.GetFileName(store.Context.Base);
        ValidateName(name);
        if (name is "self" or "default") throw new StorageException("reserved-service-name", "Nombre de servicio reservado.");
        if (recipes.SelectMany(r => r!["entities"]!.AsArray()).Any(e => e!["kind"]!.GetValue<string>() == "service" && e["name"]!.GetValue<string>() == name))
            throw new StorageException("service-name-conflict", "Nombre de servicio ya registrado en el ámbito.");
        var recipeEntry = matches.Length == 1 ? matches[0]!.AsObject() : new JsonObject
        {
            ["recipe-id"] = ProjectDocuments.Id(), ["consumer-id"] = ProjectDocuments.Id(), ["files"] = Strings(normalized),
            ["revision"] = 1, ["entities"] = new JsonArray(), ["pending-withdrawals"] = new JsonArray(), ["service-selection"] = selection
        };
        recipeEntry["name"] = name;
        var serviceId = ProjectDocuments.Id(); var recipeId = recipeEntry["recipe-id"]!.GetValue<string>();
        recipeEntry["entities"]!.AsArray().Add((JsonNode)new JsonObject { ["kind"] = "service", ["id"] = serviceId, ["name"] = name, ["owner"] = recipeId });
        if (matches.Length == 0) recipes.Add((JsonNode)recipeEntry);
        else recipeEntry["revision"] = checked(recipeEntry["revision"]!.GetValue<long>() + 1);
        store.Commit(snapshot.Revision, docs);
        return new(serviceId, recipeId, name);
    }

    public ServiceIdentity FindProjectService(IReadOnlyList<string> files)
    {
        if (store.Context.IsGlobal) throw new StorageException("project-scope-required", "Self requiere un proyecto.");
        var selection = Normalize(files)[0];
        var document = store.Read().Documents.GetValueOrDefault(RegistryPath);
        var matches = document?["data"]?["recipes"]?.AsArray().Where(r => r!["service-selection"]?.GetValue<string>() == selection).ToArray() ?? [];
        if (matches.Length != 1) throw new StorageException(matches.Length == 0 ? "unknown-service" : "recipe-identity-conflict", "No se pudo resolver el servicio propio registrado.");
        var recipe = matches[0]!;
        var service = recipe["entities"]!.AsArray().SingleOrDefault(e => e!["kind"]!.GetValue<string>() == "service");
        return service is null ? throw new StorageException("unknown-service", "El servicio propio no está registrado.") :
            new(service["id"]!.GetValue<string>(), recipe["recipe-id"]!.GetValue<string>(), service["name"]!.GetValue<string>());
    }

    // An instance record requires reconciliation by the lifecycle operator before
    // removal. Its mere presence is not proof that the process has terminated.
    public void RemoveStoppedService(string serviceId)
    {
        ProjectDocuments.Uuid(JsonValue.Create(serviceId));
        var snapshot = store.Read();
        var documents = Next(snapshot);
        var activePath = store.RuntimeDocumentPath(serviceId, "active");
        if (snapshot.Documents.TryGetValue(activePath, out var active) && active["data"]?["terminated"]?.GetValue<bool>() != true)
            throw new StorageException("service-in-use", "El servicio conserva evidencia de una instancia no reconciliada.");
        var registry = Data(documents, RegistryPath, "recipe-registry", "recipes");
        foreach (var recipe in registry["recipes"]!.AsArray())
        {
            var entities = recipe!["entities"]!.AsArray();
            var service = entities.SingleOrDefault(e => e!["kind"]!.GetValue<string>() == "service" && e["id"]!.GetValue<string>() == serviceId);
            if (service is null) continue;
            if (service["instance"] is not null || service["consumers"] is JsonArray consumers && consumers.Count != 0 ||
                service["preparations"] is JsonArray preparations && preparations.Count != 0)
                throw new StorageException("service-in-use", "El servicio conserva una instancia o referencias; no se puede retirar.");
            entities.Remove(service);
            recipe["revision"] = checked(recipe["revision"]!.GetValue<long>() + 1);
            ScopeStore.CommitTogether([new(store, snapshot.Revision, documents, RemovedServiceId: store.Context.IsGlobal ? serviceId : null)]);
            return;
        }
        throw new StorageException("unknown-service", "Servicio no registrado.");
    }

    public ResourceIdentity RegisterResource(string logicalKey, string kind, string ownership)
    {
        if (kind is "model" or "companion" && logicalKey.Contains('/'))
        {
            var selector = Coquena.Models.ModelRequest.Parse(logicalKey);
            if (selector.Quantization is null || (kind == "companion") != (selector.CompanionType is not null))
                throw new StorageException("invalid-resource-name", "Se requiere selección canónica completa y rol coincidente.");
        }
        else ValidateName(logicalKey);
        if (kind is not ("model" or "companion" or "backend") || ownership is not ("managed" or "external")) throw new ArgumentException("Tipo/propiedad inválidos.");
        var snapshot = store.Read();
        var documents = Next(snapshot);
        var inventory = Data(documents, InventoryPath, "inventory", "resources");
        var resources = inventory["resources"]!.AsArray();
        var existing = resources.FirstOrDefault(x => x!["logical-key"]!.GetValue<string>().Equals(logicalKey, StringComparison.OrdinalIgnoreCase) && x["kind"]!.GetValue<string>() == kind);
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

    // A caller must supply the registered recipe identity; matching a file list
    // or a name is not sufficient evidence of ownership.
    public ServiceIdentity ResolveOwnService(string recipeId)
    {
        if (store.Context.IsGlobal) throw new StorageException("project-scope-required", "Self requiere un proyecto.");
        ProjectDocuments.Uuid(JsonValue.Create(recipeId));
        var snapshot = store.Read();
        if (!snapshot.Documents.TryGetValue(RegistryPath, out var document))
            throw new StorageException("unknown-recipe", "Receta no registrada.");
        var recipe = FindRecipe(document["data"]!.AsObject(), recipeId);
        var service = recipe["entities"]!.AsArray().SingleOrDefault(e => e!["kind"]!.GetValue<string>() == "service");
        return service is null ? throw new StorageException("unknown-service", "La receta no tiene un servicio registrado.") :
            new(service["id"]!.GetValue<string>(), recipeId, service["name"]!.GetValue<string>());
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

    // The caller publishes while the child is suspended and owns the lifecycle
    // lease. Resource bindings and process identity become visible together.
    public void PublishActive(string serviceId, JsonObject active, Action? validateIntent = null)
    {
        ProjectDocuments.Uuid(JsonValue.Create(serviceId));
        var instanceId = ProjectDocuments.Uuid(active["instance-id"]);
        if (active["service-id"]?.GetValue<string>() != serviceId || active["process"] is not JsonObject process ||
            process["pid"]?.GetValue<int>() is not > 0 || process["creation-time"]?.GetValue<long>() is not > 0 ||
            process["executable"]?.GetValue<string>() is not string executable || !Path.IsPathFullyQualified(executable) ||
            active["resources"] is not JsonArray || active["snapshot"] is not JsonObject || active["endpoint"] is not JsonObject ||
            active["log"]?.GetValue<string>() is not string || active["combination"]?.GetValue<string>() is not string || active["terminated"] is not null)
            throw new StorageException("invalid-instance-identity", "La instancia requiere identidad, snapshot, recursos, endpoint y log.");
        var snapshot = store.Read();
        var docs = Next(snapshot);
        ValidateRuntimeBindings(snapshot, active["resources"]!.AsArray());
        var registry = Data(docs, RegistryPath, "recipe-registry", "recipes");
        var service = registry["recipes"]!.AsArray().SelectMany(x => x!["entities"]!.AsArray())
            .SingleOrDefault(x => x!["kind"]!.GetValue<string>() == "service" && x["id"]!.GetValue<string>() == serviceId)
            ?? throw new StorageException("unknown-service", "Servicio no registrado.");
        var path = ActivePath(serviceId);
        if (service["instance"] is not null || snapshot.Documents.TryGetValue(path, out var previous) && previous["data"]?["terminated"]?.GetValue<bool>() != true)
            throw new StorageException("instance-already-registered", "La instancia anterior requiere reconciliación.");
        service["instance"] = new JsonObject { ["instance-id"] = instanceId, ["process"] = process.DeepClone() };
        var revision = docs.GetValueOrDefault(path)?["revision"]?.GetValue<long>() ?? 1;
        docs[path] = ProjectDocuments.Create("active", revision, active);
        ScopeStore.CommitTogether([new(store, snapshot.Revision, docs, Validate: validateIntent)]);
    }

    // Only the lifecycle operator may call this after confirming OS termination.
    public void RecordTermination(string serviceId, string instanceId)
    {
        ProjectDocuments.Uuid(JsonValue.Create(serviceId));
        ProjectDocuments.Uuid(JsonValue.Create(instanceId));
        var snapshot = store.Read(); var docs = Next(snapshot);
        var path = ActivePath(serviceId);
        var active = docs.GetValueOrDefault(path)?["data"]?.AsObject();
        var registry = Data(docs, RegistryPath, "recipe-registry", "recipes");
        var service = registry["recipes"]!.AsArray().SelectMany(x => x!["entities"]!.AsArray())
            .SingleOrDefault(x => x!["kind"]!.GetValue<string>() == "service" && x["id"]!.GetValue<string>() == serviceId);
        if (active?["instance-id"]?.GetValue<string>() != instanceId || service?["instance"]?["instance-id"]?.GetValue<string>() != instanceId)
            throw new StorageException("instance-conflict", "La identidad registrada cambió; se conserva la evidencia.");
        active["terminated"] = true;
        service!["instance"] = null;
        store.Commit(snapshot.Revision, docs);
    }

    public void PromoteHealthy(string serviceId, string instanceId, Uri healthUrl, int statusCode)
    {
        if (statusCode is < 200 or >= 300 || !healthUrl.IsAbsoluteUri || !string.IsNullOrEmpty(healthUrl.UserInfo))
            throw new StorageException("invalid-health-evidence", "La promoción requiere salud HTTP 2xx sin credenciales en la URL.");
        var snapshot = store.Read(); var docs = Next(snapshot);
        var active = docs.GetValueOrDefault(ActivePath(serviceId))?["data"]?.AsObject();
        if (active?["instance-id"]?.GetValue<string>() != instanceId || active["terminated"]?.GetValue<bool>() == true)
            throw new StorageException("instance-conflict", "La instancia cambió antes de promover salud.");
        var path = store.RuntimeDocumentPath(serviceId, "last-good");
        var data = new JsonObject { ["service-id"] = serviceId, ["snapshot"] = active["snapshot"]!.DeepClone(),
            ["resources"] = active["resources"]!.DeepClone(), ["checked-at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["health"] = new JsonObject { ["url"] = healthUrl.ToString(), ["status"] = statusCode } };
        foreach (var key in new[] { "combination", "endpoint", "execution" })
            if (active[key] is { } value) data[key] = value.DeepClone();
        docs[path] = ProjectDocuments.Create("last-good", docs.GetValueOrDefault(path)?["revision"]?.GetValue<long>() ?? 1, data);
        store.Commit(snapshot.Revision, docs);
    }

    internal static void ValidateRuntimeBindings(ScopeSnapshot snapshot, JsonArray bindings)
    {
        var inventory = snapshot.Documents.GetValueOrDefault(InventoryPath)?["data"]?["resources"] as JsonArray;
        foreach (var binding in bindings)
        {
            var resourceId = ProjectDocuments.Uuid(binding?["resource-id"]);
            var installationId = ProjectDocuments.Uuid(binding?["installation-id"]);
            var resource = inventory?.SingleOrDefault(item => item!["resource-id"]!.GetValue<string>() == resourceId);
            var installation = resource?["installations"]?.AsArray().SingleOrDefault(item => item!["installation-id"]!.GetValue<string>() == installationId);
            if (resource is null || installation is null || resource["removal-pending"]?.GetValue<bool>() == true ||
                installation["availability"]?.GetValue<string>() == "missing")
                throw new StorageException("unknown-resource", "La instancia requiere instalaciones registradas en su ámbito.");
            if (installation["integrity-status"]?.GetValue<string>() is "invalid" or "mismatch")
                throw new StorageException("resource-integrity-invalid", "La instalación tiene integridad inválida conocida.");
        }
    }

    private string ActivePath(string serviceId) => store.RuntimeDocumentPath(serviceId, "active");

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
