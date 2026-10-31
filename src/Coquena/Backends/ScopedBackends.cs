using System.Text.Json;
using System.Text.Json.Nodes;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Backends;

public sealed record BackendIntent(string? Version = null, string? Variant = null, string? Executable = null, bool Sensitive = false)
{
    public static BackendIntent FromRecipe(EffectiveRecipe recipe)
    {
        var value = recipe.Root.Get("service")?.Get("backend") ??
            throw new BackendException("backend-required", "La receta no define backend propio.");
        var intent = new BackendIntent(value.Get("version")?.Text, value.Get("variant")?.Text, value.Get("executable")?.Text, value.Fields.Values.Any(v => v.Sensitive));
        intent.Validate();
        return intent;
    }

    public void Validate()
    {
        if (Executable is not null)
        {
            if (string.IsNullOrWhiteSpace(Executable) || Executable.Any(char.IsControl) || Version is not null || Variant is not null)
                throw new BackendException("invalid-backend-selection", "Usar version y variant, o executable, excluyentes.");
        }
        else
        {
            if (Version is null || Variant is null || !BackendCatalog.ValidFamily(Variant))
                throw new BackendException("invalid-backend-selection", "Se requieren versión y variante oficiales.");
            if (Version != "latest") _ = BackendCatalog.BuildNumber(Version);
        }
    }

    internal JsonObject Document() => Executable is null
        ? new() { ["version"] = Version, ["variant"] = Variant }
        : new() { ["executable"] = Executable };
}

public sealed record PreparedBackend(string ResourceId, string InstallationId, string Executable,
    string Ownership, string? Build, string? Variant);

// Resource preparation only. Combination locks, process promotion and YAML editing
// are callers; a prepared backend never changes the active service snapshot.
public sealed class ScopedBackends(ScopeContext context,
    Func<HardwareContext, string, string, CancellationToken, Task<BackendResolution>>? resolve = null,
    Func<string, CancellationToken, Task<string>>? probe = null)
{
    private readonly ScopeStore store = new(context);
    private const string Inventory = "local/inventory.json";
    private const string Registry = "local/recipes.json";

    public JsonArray List()
    {
        try
        {
            var resources = store.Read().Documents.TryGetValue(Inventory, out var doc)
                ? new JsonArray(doc["data"]!["resources"]!.AsArray().Where(r => r!["kind"]!.GetValue<string>() == "backend")
                    .Select(r => r!.DeepClone()).ToArray()) : new();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var resource in resources)
                foreach (var installation in resource!["installations"]!.AsArray())
                {
                    var id = ProjectDocuments.Uuid(installation?["installation-id"]);
                    if (!ids.Add(id) || installation?["availability"]?.GetValue<string>() is null) ProjectDocuments.Invalid();
                    if (installation!["protected"] is { } protection) _ = protection.GetValue<bool>();
                    if (resource["ownership"]!.GetValue<string>() == "external")
                    {
                        if (string.IsNullOrWhiteSpace(installation["executable"]?.GetValue<string>())) ProjectDocuments.Invalid();
                    }
                    else
                    {
                        if (installation["location"]?.GetValue<string>() != "backends/installations/" + id ||
                            installation["files"] is not JsonArray { Count: > 0 } || installation["selection"]?["backendSelection"] is not JsonObject)
                            ProjectDocuments.Invalid();
                        _ = ReadSelection(installation);
                        foreach (var file in installation["files"]!.AsArray())
                        {
                            if (string.IsNullOrEmpty(file?["path"]?.GetValue<string>()) || file?["size"]?.GetValue<long>() is not >= 0) ProjectDocuments.Invalid();
                            _ = context.PathFor(installation["location"]!.GetValue<string>() + "/" + file!["path"]!.GetValue<string>());
                        }
                    }
                }
            return resources;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        { throw new StorageException("invalid-project-document", "Inventario de backends inválido; se conserva sin sobrescribir.", inner: e); }
    }

    public async Task<PreparedBackend> PrepareAsync(string serviceId, BackendIntent intent, string originBase,
        HardwareContext hardware, IDownloadTransport transport, bool force = false,
        Action<string>? warning = null, CancellationToken cancellation = default)
    {
        intent.Validate();
        originBase = Path.GetFullPath(originBase);
        // Save local intent separately from physical installations. Keep all selections,
        // including pins displaced by another override; no cleanup is inferred here.
        var snapshot = store.Read();
        var docs = RecipeRegistry.Next(snapshot);
        var service = Service(docs, serviceId);
        var intents = service["backend-intents"] as JsonArray ?? new JsonArray();
        var reference = FindIntent(intents, intent, originBase);
        if (reference is null)
        {
            reference = new JsonObject { ["intent"] = intent.Document(), ["origin-base"] = originBase };
            intents.Add(reference);
            if (service["backend-intents"] is null) service["backend-intents"] = intents;
        }
        service["backend-intent"] = intent.Document();
        service["backend-intent-sensitive"] = intent.Sensitive;
        service["backend-origin-base"] = originBase;
        store.Commit(snapshot.Revision, docs);
        var previousInstallation = reference["installation-id"]?.GetValue<string>();
        PreparedBackend prepared;
        if (reference["installation-id"] is not null)
        {
            var (resource, installation) = FindInstallation(reference["installation-id"]!.GetValue<string>());
            if (reference["resource-id"]?.GetValue<string>() != resource["resource-id"]!.GetValue<string>()) ProjectDocuments.Invalid();
            try { prepared = await ReadPrepared(resource, installation, hardware, force, warning, cancellation); }
            catch (BackendException error) when (error.Code == "backend-installation-absent" && intent.Executable is null)
            {
                var exact = ReadSelection(installation);
                prepared = await DownloadAsync(new(exact.Release, exact.Variant), hardware, transport, false, force, warning, cancellation, exact);
            }
        }
        else if (intent.Executable is not null)
            prepared = RegisterExternal(Path.GetFullPath(intent.Executable, originBase), !context.IsGlobal && !Path.IsPathRooted(intent.Executable));
        else
            prepared = await DownloadAsync(intent, hardware, transport, false, force, warning, cancellation);

        if (intent.Executable is null && intent.Version != "latest") Protect(prepared.InstallationId, true);

        snapshot = store.Read(); docs = RecipeRegistry.Next(snapshot);
        service = Service(docs, serviceId);
        reference = FindIntent(service["backend-intents"]!.AsArray(), intent, originBase)
            ?? throw new StorageException("scope-conflict", "La intención cambió durante la preparación.");
        if (reference["installation-id"]?.GetValue<string>() != previousInstallation)
            throw new StorageException("scope-conflict", "Otra preparación publicó una revisión; releer antes de continuar.");
        reference["resource-id"] = prepared.ResourceId;
        reference["installation-id"] = prepared.InstallationId;
        store.Commit(snapshot.Revision, docs);
        return prepared;
    }

    public async Task<PreparedBackend> DownloadAsync(BackendIntent intent, HardwareContext hardware,
        IDownloadTransport transport, bool explicitlyRequested = true, bool force = false,
        Action<string>? warning = null, CancellationToken cancellation = default, BackendDownloadSelection? expectedSelection = null)
    {
        intent.Validate();
        if (intent.Executable is not null) throw new BackendException("external-not-downloadable", "Un externo se usa en su ubicación.");
        var resolution = resolve is not null ? await resolve(hardware, intent.Variant!, intent.Version!, cancellation)
            : await ResolveOfficial(hardware, intent.Variant!, intent.Version!, cancellation);
        var candidate = resolution.Selected ?? throw new BackendException("no-candidate", "Sin artefactos completos para la selección explícita.");
        if (candidate.Variant != intent.Variant || intent.Version != "latest" && candidate.Release != intent.Version || !candidate.Complete)
            throw new BackendException("invalid-selection", "La resolución no coincide con la intención.");
        CheckCompatibility(candidate, hardware, force, warning);
        var selected = new BackendDownloadSelection("ggml", candidate.Release, candidate.Variant.Split('@')[0],
            candidate.Variant, candidate.Platform, candidate.Artifacts.Select(a => new RemoteFile(a.Name, a.Size)).ToArray());
        if (expectedSelection is not null && !JsonNode.DeepEquals(SelectionNode(expectedSelection), SelectionNode(selected)))
            throw new BackendException("artifact-changed", "Los artefactos del build fijado cambiaron; no se reemplaza su resolución.");
        foreach (var resource in List())
            foreach (var installation in resource!["installations"]!.AsArray().Reverse())
                if (resource["ownership"]!.GetValue<string>() == "managed" && SameSelection(installation!, selected))
                {
                    if (installation!["integrity-status"]?.GetValue<string>() is "invalid" or "mismatch")
                    {
                        if (explicitlyRequested) continue;
                        throw new BackendException("backend-integrity-invalid", "Integridad inválida conocida; no se redescarga automáticamente.");
                    }
                    if (expectedSelection is not null && MissingManagedFiles(installation)) continue;
                    if (!Directory.Exists(context.PathFor(installation["location"]!.GetValue<string>()))) continue;
                    var result = await ReadPrepared(resource, installation!, hardware, force, null, cancellation);
                    if (explicitlyRequested) Protect(result.InstallationId, true);
                    return result;
                }
        var id = Guid.NewGuid();
        var operation = new OperationDocument(1, id, "backend", $"ggml/{candidate.Variant}:{candidate.Release}", null, selected,
            selected.Artifacts.Select((a, i) => new TransferRecord(a.Path, $"downloads/{id:D}/files/{i}.part",
                $"backends/archives/{i}.zip", 0, a.Size, a.Identity, null, null, "pending")).ToArray(),
            "pending", "transfer", DateTimeOffset.UtcNow, null);
        EnsureInventory();
        var transfers = new ScopeTransfers(context, validateBackend: ValidateOfficial);
        operation = transfers.Create(operation, explicitlyRequested ? "explicit" : "automatic", ["llama-server.exe"]);
        warning?.Invoke($"Operación de backend: {operation.Id:D}");
        var pending = await transfers.RunAsync(operation.Id, transport, cancellation: cancellation);
        if (pending is not null) throw new DownloadException(pending.Diagnostic ?? "backend-preparation-incomplete", "Preparación no completada; conserve el GUID para reanudar.");
        var found = FindInstallation(operation.ScopePublication!.InstallationId.ToString("D"));
        return await ReadPrepared(found.Resource, found.Installation, hardware, force, null, cancellation);
    }

    internal async Task<PreparedBackend> PrepareCombinationAsync(BackendIntent intent, HardwareContext hardware,
        IDownloadTransport transport, BackendDownloadSelection? exact, bool force, Action<string>? warning, CancellationToken cancellation)
    {
        intent.Validate();
        var pinnedByIntent = intent.Executable is null && intent.Version != "latest";
        if (intent.Executable is not null)
            return RegisterExternal(Path.GetFullPath(intent.Executable, context.Base), !Path.IsPathRooted(intent.Executable));
        if (exact is not null)
        {
            foreach (var resource in List())
                foreach (var installation in resource!["installations"]!.AsArray())
                    if (resource["ownership"]!.GetValue<string>() == "managed" && SameSelection(installation!, exact))
                        try
                        {
                            var reused = await ReadPrepared(resource, installation!, hardware, force, warning, cancellation);
                            if (pinnedByIntent) Protect(reused.InstallationId, true);
                            return reused;
                        }
                        catch (BackendException e) when (e.Code == "backend-installation-absent" ||
                            e.Code == "backend-installation-incomplete" && MissingManagedFiles(installation!)) { }
            intent = new(exact.Release, exact.Variant);
        }
        var prepared = await DownloadAsync(intent, hardware, transport, false, force, warning, cancellation, exact);
        if (pinnedByIntent) Protect(prepared.InstallationId, true);
        return prepared;
    }

    private bool MissingManagedFiles(JsonNode installation) =>
        installation["files"]!.AsArray().Any(file => !File.Exists(context.PathFor(
            installation["location"]!.GetValue<string>() + "/" + file!["path"]!.GetValue<string>())));

    public void Protect(string installationId, bool value)
    {
        var snapshot = store.Read(); var docs = RecipeRegistry.Next(snapshot);
        var installation = docs.TryGetValue(Inventory, out var doc) ? doc["data"]!["resources"]!.AsArray()
            .Where(r => r!["kind"]!.GetValue<string>() == "backend" && r["ownership"]!.GetValue<string>() == "managed")
            .SelectMany(r => r!["installations"]!.AsArray())
            .SingleOrDefault(i => i!["installation-id"]!.GetValue<string>() == installationId) : null;
        if (installation is null) throw new BackendException("unknown-installation", "Seleccione el UUID de una instalación gestionada del ámbito.");
        installation["protected"] = value;
        store.Commit(snapshot.Revision, docs);
    }

    public static void CheckCompatibility(BackendCandidate candidate, HardwareContext hardware, bool force, Action<string>? warning)
    {
        if (candidate.Platform != hardware.Platform)
            throw new BackendException("unsupported-platform", "El artefacto no corresponde a la plataforma nativa.");
        if (candidate.Compatibility == "incompatible")
        {
            warning?.Invoke("Backend incompatible: " + candidate.Diagnostic);
            if (!force) throw new BackendException("backend-incompatible", "Preparación detenida; --force permite intentar con advertencia.");
            warning?.Invoke("--force: se continúa con el backend incompatible; no acredita ejecución.");
        }
        else if (candidate.Compatibility != "compatible") warning?.Invoke("Compatibilidad desconocida: " + candidate.Diagnostic);
    }

    public static void CheckSelection(BackendDownloadSelection selection, HardwareContext hardware, bool force, Action<string>? warning)
    {
        var candidate = BackendCatalog.Resolve([new(selection.Release, selection.Artifacts.Select(a =>
            new ReleaseAsset(a.Path, a.Size ?? 0, "")).ToArray())], hardware, selection.Variant, selection.Release).Selected
            ?? throw new BackendException("invalid-selection", "Los artefactos no describen un backend oficial completo de esta plataforma.");
        if (selection.Origin != "ggml" || selection.Platform != candidate.Platform || selection.Family != candidate.Variant.Split('@')[0])
            throw new BackendException("invalid-selection", "La selección oficial es inconsistente.");
        if (selection.Artifacts.Length != candidate.Artifacts.Length || selection.Artifacts.Where((a, i) =>
            a.Path != candidate.Artifacts[i].Name || a.Size != candidate.Artifacts[i].Size).Any())
            throw new BackendException("invalid-selection", "La selección contiene artefactos ajenos al backend.");
        CheckCompatibility(candidate, hardware, force, warning);
    }

    public async Task ValidateOfficial(string directory, BackendDownloadSelection selection, CancellationToken cancellation)
    {
        var executable = Path.Combine(directory, "llama-server.exe");
        var version = await (probe ?? BackendInstaller.ProbeAsync)(executable, cancellation);
        BackendInstaller.CheckVersion(version, selection.Release);
    }

    private async Task<PreparedBackend> ReadPrepared(JsonNode resource, JsonNode installation, HardwareContext hardware,
        bool force, Action<string>? warning, CancellationToken cancellation)
    {
        var ownership = resource["ownership"]!.GetValue<string>();
        if (installation["integrity-status"]?.GetValue<string>() is "invalid" or "mismatch")
            throw new BackendException("backend-integrity-invalid", "Instalación no utilizable; --force no omite integridad.");
        if (installation["availability"]?.GetValue<string>() == "missing")
            throw new BackendException(ownership == "managed" ? "backend-installation-absent" : "backend-absent", "Falta la instalación seleccionada.");
        if (installation["availability"]?.GetValue<string>() != "available")
            throw new BackendException("backend-unavailable", "La instalación no está disponible; no se infiere corrupción de sus bytes.");
        string executable; string? build = null; string? variant = null;
        if (ownership == "external") executable = Path.GetFullPath(installation["executable"]!.GetValue<string>(), context.Base);
        else
        {
            var selection = ReadSelection(installation);
            CheckSelection(selection, hardware, force, warning);
            var directory = context.PathFor(installation["location"]!.GetValue<string>());
            if (!Directory.Exists(directory)) throw new BackendException("backend-installation-absent", "Falta la instalación fijada; preparar el build exacto.");
            foreach (var file in installation["files"]!.AsArray())
            {
                var path = context.PathFor(installation["location"]!.GetValue<string>() + "/" + file!["path"]!.GetValue<string>());
                if (!File.Exists(path) || new FileInfo(path).Length != file["size"]!.GetValue<long>())
                    throw new BackendException("backend-installation-incomplete", "Faltan archivos o cambió su tamaño; no se adopta otra revisión.");
            }
            await ValidateOfficial(directory, selection, cancellation);
            executable = Path.Combine(directory, "llama-server.exe"); build = selection.Release; variant = selection.Variant;
        }
        if (!File.Exists(executable)) throw new BackendException("backend-absent", "Ejecutable ausente; un externo requiere intervención.");
        return new(resource["resource-id"]!.GetValue<string>(), installation["installation-id"]!.GetValue<string>(), executable, ownership, build, variant);
    }

    private PreparedBackend RegisterExternal(string executable, bool relative)
    {
        if (!File.Exists(executable)) throw new BackendException("backend-absent", "Ejecutable externo ausente.");
        EnsureInventory();
        var snapshot = store.Read(); var docs = RecipeRegistry.Next(snapshot);
        var resources = docs[Inventory]["data"]!["resources"]!.AsArray();
        foreach (var old in resources.Where(r => r!["kind"]!.GetValue<string>() == "backend" && r["ownership"]!.GetValue<string>() == "external"))
            foreach (var installed in old!["installations"]!.AsArray())
                if (string.Equals(Path.GetFullPath(installed!["executable"]!.GetValue<string>(), context.Base), executable, StringComparison.OrdinalIgnoreCase))
                {
                    if (installed["integrity-status"]?.GetValue<string>() is "invalid" or "mismatch")
                        throw new BackendException("backend-integrity-invalid", "Integridad inválida conocida; no se omite durante el inicio.");
                    if (old["removal-pending"]?.GetValue<bool>() == true || installed["availability"]?.GetValue<string>() != "available")
                        throw new BackendException("backend-unavailable", "La instalación externa registrada no está disponible.");
                    return new(old["resource-id"]!.GetValue<string>(), installed["installation-id"]!.GetValue<string>(), executable, "external", null, null);
                }
        var resourceId = Guid.NewGuid().ToString("D"); var installationId = Guid.NewGuid().ToString("D");
        resources.Add((JsonNode)new JsonObject { ["resource-id"] = resourceId, ["logical-key"] = "external-" + resourceId,
            ["kind"] = "backend", ["ownership"] = "external", ["installations"] = new JsonArray(new JsonObject {
                ["installation-id"] = installationId, ["executable"] = relative ? Path.GetRelativePath(context.Base, executable) : executable,
                ["availability"] = "available", ["files"] = new JsonArray() }) });
        store.Commit(snapshot.Revision, docs);
        return new(resourceId, installationId, executable, "external", null, null);
    }

    private (JsonNode Resource, JsonNode Installation) FindInstallation(string id)
    {
        foreach (var resource in List())
            foreach (var installation in resource!["installations"]!.AsArray())
                if (installation!["installation-id"]!.GetValue<string>() == id) return (resource, installation);
        throw new BackendException("backend-installation-missing", "La revisión fijada no figura en el inventario del ámbito.");
    }
    private static JsonNode Service(Dictionary<string, JsonObject> docs, string id) =>
        docs.TryGetValue(Registry, out var registry) ? registry["data"]!["recipes"]!.AsArray().SelectMany(r => r!["entities"]!.AsArray())
            .SingleOrDefault(e => e!["kind"]!.GetValue<string>() == "service" && e["id"]!.GetValue<string>() == id)
            ?? throw new BackendException("unknown-service", "Servicio no registrado en el ámbito.")
            : throw new BackendException("unknown-service", "Servicio no registrado en el ámbito.");
    private JsonNode? FindIntent(JsonArray intents, BackendIntent intent, string originBase) => intents.SingleOrDefault(i =>
        JsonNode.DeepEquals(i!["intent"], intent.Document()) && (!context.IsGlobal || intent.Executable is null ||
            string.Equals(i["origin-base"]!.GetValue<string>(), originBase, StringComparison.OrdinalIgnoreCase)));
    private void EnsureInventory()
    {
        var snapshot = store.Read();
        if (snapshot.Documents.ContainsKey(Inventory)) return;
        var docs = RecipeRegistry.Next(snapshot);
        docs[Inventory] = ProjectDocuments.Create("inventory", 1, new JsonObject { ["resources"] = new JsonArray() });
        store.Commit(snapshot.Revision, docs);
    }
    private static BackendDownloadSelection ReadSelection(JsonNode installation) =>
        installation["selection"]!["backendSelection"]!.Deserialize(ContractJsonContext.Default.BackendDownloadSelection)
            ?? throw new BackendException("invalid-selection", "Selección ausente.");
    private static bool SameSelection(JsonNode installation, BackendDownloadSelection selection) =>
        installation["selection"]?["backendSelection"] is { } node &&
        JsonNode.DeepEquals(node, SelectionNode(selection));
    private static JsonNode SelectionNode(BackendDownloadSelection selection) =>
        JsonNode.Parse(JsonSerializer.Serialize(selection, ContractJsonContext.Default.BackendDownloadSelection))!;
    private static async Task<BackendResolution> ResolveOfficial(HardwareContext hardware, string variant, string version, CancellationToken cancellation)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        return await new OfficialBackendSource(client).ResolveAsync(hardware, variant, version, cancellation);
    }
}
