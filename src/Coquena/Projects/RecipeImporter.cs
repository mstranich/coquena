using System.Text.Json.Nodes;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Projects;

public sealed record RecipeImportResult(bool Confirmed, bool WarmupFailed, string[] Pending, bool ReconciledOnly = false, int ExitCode = 0);

// The imported YAML documents are participants in the scope generation. Resource
// preparation precedes the intention commit; router publication follows it and
// retains a durable list of remaining entities on any failure.
public sealed class RecipeImporter(ScopeContext context)
{
    private readonly ScopeStore store = new(context);
    private const string Registry = "local/recipes.json";
    private static RecipeOrigin Origin => new("import", 1, 1, "$");
    private static RecipeValue Text(string value) => new(RecipeKind.String, Origin, value, RecipeStyle.SingleQuoted);
    private static RecipeValue Map(Dictionary<string, RecipeValue> fields) => RecipeValue.Map(Origin, fields);

    public RecipeSelection ServiceSelection(string serviceId)
    {
        var files = new List<string>();
        if (File.Exists(context.PathFor("recipe.yaml"))) files.Add(context.PathFor("recipe.yaml"));
        files.Add(context.PathFor($"services/{serviceId}/recipe.yaml"));
        return new(context.Base, files, context.Base) { ImportedScope = context, FilterUnusedModels = true };
    }

    public async Task<RecipeImportResult> ImportAsync(RecipeSelection selection, ICombinationResources resources,
        string? select = null, IEnumerable<string>? replace = null, bool force = false, bool asDefault = false,
        IReadOnlyDictionary<string, string?>? environment = null, bool interactive = false, Func<string, bool>? confirm = null,
        Func<string, bool>? running = null, Func<string, IPresetRouter?>? router = null,
        Action<ScopeWriteStage>? fault = null, CancellationToken cancellation = default, Action<PublicationStage>? publicationFault = null)
    {
        if (!context.IsGlobal) throw new ArgumentException("Import requiere ámbito global.");
        var replacements = replace?.ToArray() ?? [];
        if (force && (select is not null || replacements.Length != 0))
            throw new RecipeException("import-option-conflict", "import", new(selection.Files[0], 1, 1, "$selection"), "Force no admite select ni replace.");
        if (select is not null)
        {
            var categories = select.Split(',');
            if (categories.Distinct(StringComparer.Ordinal).Count() != categories.Length || categories.Any(c => c is not ("service" or "presets" or "model" or "params")))
                throw new RecipeException("invalid-import-selection", "import", new(selection.Files[0], 1, 1, "$selection"), "Categorías inválidas, vacías o repetidas.");
            selection = new(selection.Base, selection.Files, selection.Base, selection.EnvFiles) { ImportCategories = categories.ToHashSet(StringComparer.Ordinal) };
        }
        else selection = new(selection.Base, selection.Files, selection.Base, selection.EnvFiles);
        environment = environment is null ? Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(p => (string)p.Key, p => (string?)p.Value, StringComparer.Ordinal) : environment.ToDictionary(StringComparer.Ordinal);
        context.Ensure();
        using var lease = ImportLease();
        var inputs = selection.Files.Concat(selection.EnvFiles).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, RecipeYaml.ReadBytes, StringComparer.OrdinalIgnoreCase);
        selection.CapturedFiles = inputs;
        var source = RecipeReader.Read(selection, environment);
        RecipeStructure.Effective(Map(new() { ["name"] = Text(source.Name) }));
        var before = store.Read(); var documents = RecipeRegistry.Next(before);
        if (!documents.ContainsKey(Registry)) documents[Registry] = ProjectDocuments.Create("recipe-registry", 1, new JsonObject { ["recipes"] = new JsonArray() });
        var recipes = documents[Registry]["data"]!["recipes"]!.AsArray();
        var physical = PhysicalDirectory.Read(selection.Base);
        var entrypoint = PhysicalDirectory.Read(selection.Files[0]);
        var files = new JsonArray(selection.Files.Select(f => (JsonNode?)JsonValue.Create(Path.GetRelativePath(selection.Base, f).Replace('\\', '/'))).ToArray());
        var associations = recipes.Where(r => r!["import-source"]?.GetValue<bool>() == true &&
            r["source-volume"]?.GetValue<string>() == physical.Volume && r["source-file-id"]?.GetValue<string>() == physical.FileId &&
            (r["source-files"]?.AsArray().FirstOrDefault()?.GetValue<string>().Equals(files[0]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase) == true ||
             r["source-entrypoint-volume"]?.GetValue<string>() == entrypoint.Volume && r["source-entrypoint-file-id"]?.GetValue<string>() == entrypoint.FileId)).ToArray();
        if (associations.Length > 1) throw new StorageException("import-source-identity-conflict", "Varias asociaciones reclaman la fuente; no se infiere propietario ni nombre.");
        var association = associations.SingleOrDefault()?.AsObject();
        if (association is null)
        {
            association = Entry(source.Name, []); association["import-source"] = true;
            association["source-volume"] = physical.Volume; association["source-file-id"] = physical.FileId;
            recipes.Add((JsonNode)association);
        }
        association["source-files"] = files;
        association["source-entrypoint-volume"] = entrypoint.Volume; association["source-entrypoint-file-id"] = entrypoint.FileId;
        var registeredName = association["name"]!.GetValue<string>();
        source = RecipeReader.Read(selection, environment, new(association["recipe-id"]!.GetValue<string>(), association["consumer-id"]!.GetValue<string>(), registeredName, selection.Files.ToArray()));
        RecipeReader.CaptureSchemas(source, inputs);
        var chosen = RecipeImportSelection.Create(source, select, replacements, force, asDefault);
        var sourceSchema = chosen.Categories.Contains("params") ? ArgumentSchema.Load(source) : null;
        var sourceInputs = inputs.ToDictionary(StringComparer.OrdinalIgnoreCase);

        var roots = new Dictionary<string, RecipeValue>(StringComparer.Ordinal);
        var bases = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (path, doc) in before.Documents.Where(p => p.Value["kind"]!.GetValue<string>() == "recipe-intention"))
        {
            var file = context.PathFor(path); inputs[file] = RecipeYaml.ReadBytes(file);
            roots[path] = new RecipeYaml(file, inputs[file]).Read();
            bases[path] = doc["data"]!["path-bases"]!.DeepClone().AsObject();
        }
        foreach (var service in new RecipeRegistry(store).Services().Where(s => chosen.Categories.Contains("service") && s.Name == registeredName))
        {
            var path = $"services/{service.ServiceId}/recipe.yaml";
            if (roots.ContainsKey(path)) continue;
            var file = context.PathFor(path);
            if (!File.Exists(file)) throw new StorageException("missing-service-intention", "Falta la intención del servicio registrado; no se sustituye por una importación implícita.");
            inputs[file] = RecipeYaml.ReadBytes(file); roots[path] = new RecipeYaml(file, inputs[file]).Read(); bases[path] = new JsonObject();
        }
        if (!roots.ContainsKey("recipe.yaml") && File.Exists(context.PathFor("recipe.yaml")))
            throw new StorageException("unregistered-global-intention", "Existe intención global sin una generación registrada; se conserva sin sobrescribir ni adoptar por existencia.");
        roots.TryAdd("recipe.yaml", Map([])); bases.TryAdd("recipe.yaml", new JsonObject());
        var originalRoots = roots.ToDictionary(StringComparer.Ordinal);
        var common = roots["recipe.yaml"].Fields.ToDictionary(StringComparer.Ordinal);
        EffectiveRecipe? destinationRecipe = null; ArgumentSchema? destinationSchema = null;
        if (chosen.Categories.Contains("params") && common.ContainsKey("arg-defs") && inputs.ContainsKey(context.PathFor("recipe.yaml")))
        {
            var globalSelection = new RecipeSelection(context.Base, [context.PathFor("recipe.yaml")], context.Base, selection.EnvFiles)
            { CapturedFiles = inputs, ImportedPathBases = bases["recipe.yaml"], FilterUnusedModels = true };
            destinationRecipe = RecipeReader.Read(globalSelection, environment); RecipeReader.CaptureSchemas(destinationRecipe, inputs);
            destinationSchema = ArgumentSchema.Load(destinationRecipe);
        }
        var pending = new List<(string Key, JsonObject Owner, string Path)>();
        string? serviceId = null;
        foreach (var entity in chosen.Entities)
        {
            var split = entity.Selector.IndexOf(':'); var kind = entity.Selector[..split]; var key = entity.Selector[(split + 1)..];
            RecipeValue value;
            if (kind == "param")
            {
                value = ParameterIntention(sourceSchema!, key, source.SourceRoot);
            }
            else value = source.SourceRoot.Get(kind switch { "service" => "service", "preset" => "presets", "model" => "models", _ => "companions" }) is { } group
                ? kind == "service" ? group : group.Get(key)! : throw new StorageException("invalid-import-intention", "Entidad de origen ausente.");
            if (kind == "preset")
            {
                var fields = value.Fields.ToDictionary(StringComparer.Ordinal);
                fields.TryAdd("args-mode", source.SourceRoot.Get("args-mode") ?? Text("permissive")); value = value.WithFields(fields);
            }
            if (kind is "model" or "companion" or "param")
            {
                var group = kind == "model" ? "models" : kind == "companion" ? "companions" : "arg-defs";
                var values = (kind == "param" ? common.GetValueOrDefault(group)?.Get("definitions") : common.GetValueOrDefault(group))?.Fields.ToDictionary(StringComparer.Ordinal) ?? [];
                var previous = kind == "param" && destinationSchema?.Definitions.ContainsKey(key) == true
                    ? ParameterIntention(destinationSchema, key, destinationRecipe!.SourceRoot) : values.GetValueOrDefault(key);
                CheckConflict(entity.Selector, previous, value, bases["recipe.yaml"], group + "/" + (kind == "param" ? "definitions/" : "") + key);
                values[key] = value;
                if (kind == "param")
                {
                    var catalog = common.GetValueOrDefault(group)?.Fields.ToDictionary(StringComparer.Ordinal) ?? [];
                    catalog["definitions"] = Map(values); common[group] = Map(catalog);
                }
                else common[group] = Map(values);
                AddBases(bases["recipe.yaml"], group + "/" + key, value, kind);
                continue;
            }
            JsonObject owner; string path;
            if (kind == "service")
            {
                var existing = recipes.SelectMany(r => r!["entities"]!.AsArray()).SingleOrDefault(e => e!["kind"]!.GetValue<string>() == "service" && e["name"]!.GetValue<string>() == registeredName);
                if (existing is not null)
                {
                    serviceId = existing["id"]!.GetValue<string>(); owner = recipes.Single(r => r!["recipe-id"]!.GetValue<string>() == existing["owner"]!.GetValue<string>())!.AsObject();
                }
                else
                {
                    if (registeredName is "global" or "default" or "self") throw new StorageException("reserved-service-name", "Nombre de servicio reservado.");
                    owner = association; serviceId = ProjectDocuments.Id();
                    owner["entities"]!.AsArray().Add((JsonNode)new JsonObject { ["kind"] = "service", ["name"] = registeredName, ["id"] = serviceId, ["owner"] = owner["recipe-id"]!.DeepClone() });
                }
                path = $"services/{serviceId}/recipe.yaml";
            }
            else
            {
                owner = recipes.SingleOrDefault(r => r!["global-preset"]?.GetValue<string>() == key)?.AsObject() ?? Entry(registeredName, []);
                if (owner["global-preset"] is null) { owner["global-preset"] = key; recipes.Add((JsonNode)owner); }
                path = $"consumers/{owner["consumer-id"]!.GetValue<string>()}/recipe.yaml";
                pending.Add((key, owner, path));
            }
            bases.TryAdd(path, new JsonObject());
            var old = roots.GetValueOrDefault(path);
            CheckConflict(entity.Selector, kind == "service" ? old?.Get("service") : old?.Get("presets")?.Get(key), value, bases[path], kind == "service" ? "service" : "presets/" + key);
            if (kind == "service" && old is not null)
                CheckConflict(entity.Selector, old.Get("args-mode") ?? Text("permissive"), source.SourceRoot.Get("args-mode") ?? Text("permissive"), new JsonObject(), "args-mode");
            var projected = new Dictionary<string, RecipeValue>
            {
                ["name"] = old?.Get("name") ?? source.SourceRoot.Get("name") ?? Text(owner["name"]!.GetValue<string>()),
                [kind == "service" ? "service" : "presets"] = kind == "service" ? value : Map(new() { [key] = value })
            };
            if (kind == "service" && source.SourceRoot.Get("args-mode") is { } rootMode) projected["args-mode"] = rootMode;
            roots[path] = Map(projected);
            AddBases(bases[path], kind == "service" ? "service" : "presets/" + key, value, kind);
            owner["files"] = new JsonArray(path);
        }
        roots["recipe.yaml"] = Map(common);
        var selectedOperation = pending.Select(p => p.Owner["publication-operation"]).FirstOrDefault(op => op is not null);
        if (selectedOperation is not null)
        {
            if (selectedOperation["global"]?.GetValue<bool>() != true || selectedOperation["target-scope-id"]?.GetValue<string>() != before.ScopeId)
                throw new StorageException("import-publication-conflict", "La operación pendiente no pertenece al ámbito global comprobado.");
            serviceId = selectedOperation["service-id"]!.GetValue<string>();
        }
        else if (pending.Count != 0 && serviceId is null)
            serviceId = new RecipeRegistry(store).ResolveGlobalService("default");
        var knownService = new RecipeRegistry(store).Services().Any(s => s.ServiceId == serviceId);
        if (selectedOperation is not null && !knownService) throw new StorageException("import-publication-conflict", "El destino pendiente ya no está registrado; no se redirige al alias actual.");
        var live = pending.Count != 0 && knownService && (running?.Invoke(serviceId!) ?? new ScopedServiceProcess(store).Reconcile(serviceId!));
        if (live && !interactive) throw new StorageException("interaction-required", "Publicar presets importados en una instancia activa requiere interacción.");
        var selectedRouter = live ? router?.Invoke(serviceId!) : null;
        var activePath = serviceId is null ? null : store.RuntimeDocumentPath(serviceId, "active");
        var expectedInstance = live ? store.Read().Documents.GetValueOrDefault(activePath!)?["data"]?["instance-id"]?.GetValue<string>() : null;
        if (live && selectedRouter is null) throw new StorageException("publication-unsupported", "Falta un adaptador del router activo.");
        if (knownService && PresetPublication.Service(store.Read(), serviceId!)["publication-operation"] is JsonNode operation)
        {
            var ownerId = operation["recipe-id"]!.GetValue<string>();
            var owned = pending.SingleOrDefault(p => p.Owner["recipe-id"]!.GetValue<string>() == ownerId);
            if (owned.Owner is null || association["import-pending"] is not JsonArray savedPending || !savedPending.Any(n => n!.GetValue<string>() == owned.Key))
                throw new StorageException("publication-pending", "La operación pendiente corresponde a otra publicación.");
            var reconciled = await new PresetPublication(store, store, serviceId!).ReconcileAsync(ownerId, selectedRouter, !live, cancellation);
            var wasRestoring = operation["restoring"]?.GetValue<bool>() == true;
            var latestOwner = store.Read().Documents[Registry]["data"]!["recipes"]!.AsArray().Single(r => r!["recipe-id"]!.GetValue<string>() == ownerId)!;
            var notSent = latestOwner["publication"]?["status"]?.GetValue<string>() == "not-sent";
            if (reconciled.Confirmed && !reconciled.WarmupFailed && !wasRestoring && !notSent && live)
            {
                var after = operation["after"]!.AsArray().SingleOrDefault(c => c!["consumer-id"]!.GetValue<string>() == owned.Owner["consumer-id"]!.GetValue<string>());
                var savedPresets = (after?["presets"] as JsonArray ?? []).Select(p => new PublishedPreset(p!["name"]!.GetValue<string>(), p["ini"]!.GetValue<string>(),
                    p["expected-arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray(), p["resources"]!.DeepClone().AsArray(), p["warmup"]!.GetValue<bool>())).ToArray();
                reconciled = await new PresetPublication(store, store, serviceId!).PublishAsync(ownerId, savedPresets,
                    operation["combination"]!.GetValue<string>(), operation["recipe-name"]!.GetValue<string>(), false, interactive,
                    confirm ?? (_ => false), selectedRouter, cancellation: cancellation, resumeWarmup: true);
            }
            PublicationResult? restoration = null;
            if (reconciled.Confirmed && reconciled.WarmupFailed && !reconciled.Restored && live)
                restoration = await new PresetPublication(store, store, serviceId!).RestoreFailedAsync(ownerId, selectedRouter!, interactive, confirm ?? (_ => false), cancellation);
            var applied = reconciled.Confirmed && !reconciled.WarmupFailed && !reconciled.Restored && !wasRestoring && !notSent;
            if (applied) MarkCompleted(association["recipe-id"]!.GetValue<string>(), owned.Key);
            var remaining = Remaining(association);
            return new(applied && remaining.Length == 0, reconciled.WarmupFailed, remaining, true,
                !reconciled.Confirmed || restoration?.Confirmed == false ? 9 : reconciled.WarmupFailed || wasRestoring ? 8 : notSent || remaining.Length != 0 ? 3 : 0);
        }

        // Compose only the selected consumer/service with global resource intention.
        var candidate = common.ToDictionary(StringComparer.Ordinal);
        candidate["name"] = Text(registeredName);
        candidate["target"] = Text(chosen.Categories.Contains("service") ? "self" : "global");
        if (serviceId is not null && chosen.Categories.Contains("service"))
        {
            var serviceRecipe = roots[$"services/{serviceId}/recipe.yaml"];
            candidate["service"] = serviceRecipe.Get("service")!;
            if (serviceRecipe.Get("args-mode") is { } mode) candidate["args-mode"] = mode;
        }
        if (pending.Count != 0) candidate["presets"] = Map(pending.ToDictionary(p => p.Key, p => roots[p.Path].Get("presets")!.Get(p.Key)!, StringComparer.Ordinal));
        var candidateBases = bases["recipe.yaml"].DeepClone().AsObject();
        foreach (var pair in bases.Where(p => p.Key != "recipe.yaml" && (pending.Any(x => x.Path == p.Key) || p.Key == $"services/{serviceId}/recipe.yaml")))
            foreach (var item in pair.Value) candidateBases[item.Key] = item.Value!.DeepClone();
        // Include selected-but-unreferenced models in preparation without adding
        // them to the published consumer definitions.
        var preparationPresets = candidate.GetValueOrDefault("presets")?.Fields.ToDictionary(StringComparer.Ordinal) ?? [];
        var synthetic = 0;
        foreach (var entity in chosen.Entities.Where(e => e.Selector.StartsWith("model:", StringComparison.Ordinal) || e.Selector.StartsWith("companion:", StringComparison.Ordinal)))
        {
            var split = entity.Selector.IndexOf(':'); var kind = entity.Selector[..split]; var key = entity.Selector[(split + 1)..];
            string syntheticKey; do { syntheticKey = "import-resource-" + synthetic++; } while (preparationPresets.ContainsKey(syntheticKey));
            preparationPresets[syntheticKey] = Map(new()
            {
                ["args-mode"] = Text("permissive"),
                ["args"] = Map(new() { ["model"] = Text(kind + "://" + key) })
            });
        }
        if (preparationPresets.Count != 0) candidate["presets"] = Map(preparationPresets);
        var candidatePath = context.PathFor($"tmp/import-{association["recipe-id"]!.GetValue<string>()}.yaml");
        PresetPublication.WriteIni(candidatePath, RecipeIntention.Yaml(Map(candidate)));
        var candidateSelection = new RecipeSelection(context.Base, [candidatePath], context.Base, selection.EnvFiles) { ImportedPathBases = candidateBases, CapturedFiles = inputs, FilterUnusedModels = true };
        var effective = RecipeReader.Read(candidateSelection, environment);
        RecipeReader.CaptureSchemas(effective, inputs);
        _ = ArgumentValidation.Validate(effective); _ = Coquena.Models.ModelIntent.FromRecipe(effective);
        if (chosen.Categories.Contains("params"))
        {
            // A global schema replacement must remain valid for the global
            // definitions it governs, without applying/reloading those consumers.
            var schemaCommon = context.PathFor($"tmp/import-{association["recipe-id"]!.GetValue<string>()}-schema-common.yaml");
            PresetPublication.WriteIni(schemaCommon, RecipeIntention.Yaml(roots["recipe.yaml"]));
            foreach (var other in roots.Where(p => p.Key != "recipe.yaml"))
            {
                var schemaEntity = context.PathFor($"tmp/{ProjectDocuments.Id()}-schema-entity.yaml");
                PresetPublication.WriteIni(schemaEntity, RecipeIntention.Yaml(other.Value));
                var pathBases = bases["recipe.yaml"].DeepClone().AsObject();
                foreach (var pair in bases[other.Key]) pathBases[pair.Key] = pair.Value!.DeepClone();
                var affected = new RecipeSelection(context.Base, [schemaCommon, schemaEntity], context.Base, selection.EnvFiles)
                { ImportedPathBases = pathBases, FilterUnusedModels = true, CapturedFiles = inputs };
                _ = ArgumentValidation.Validate(RecipeReader.Read(affected, environment));
            }
        }
        foreach (var block in ArgumentValidation.Validate(effective).Where(b => b.Scope == "preset" && pending.Any(p => p.Key == b.Key)))
        {
            _ = ArgumentEmission.Ini(effective, block, block.Key, new LlamaB11482ArgumentAdapter(), _ => "prepared-resource");
            var adapter = new LlamaB11482ArgumentAdapter();
            _ = ArgumentEmission.Cli(effective, CliBlock(block, adapter), adapter, _ => "prepared-resource");
        }
        var requests = new Dictionary<string, PreparationRequest>(StringComparer.OrdinalIgnoreCase);
        var prepared = await new CombinationPreparation(context).PrepareAsync(candidateSelection,
            new SelectedResources(resources, chosen, context, request => requests[request.Kind + ":" + request.Key] = request), privateSelection: true, environment: environment, cancellation: cancellation);
        void Validate()
        {
            if (Changed(inputs))
                throw new StorageException("intention-conflict", "Cambió la fuente o intención global durante import.");
        }
        Validate();
        var current = store.Read();
        RecipeRegistry.ValidateRuntimeBindings(current, new JsonArray(prepared.Resources.Select(r => (JsonNode?)new JsonObject
        { ["resource-id"] = r.ResourceId, ["installation-id"] = r.InstallationId }).ToArray()));
        if (!JsonNode.DeepEquals(before.Documents.GetValueOrDefault(Registry)?["data"], current.Documents.GetValueOrDefault(Registry)?["data"]))
            throw new StorageException("import-conflict", "Cambió el registro durante la preparación.");
        var next = RecipeRegistry.Next(current); next[Registry] = ProjectDocuments.Create("recipe-registry", next.GetValueOrDefault(Registry)?["revision"]?.GetValue<long>() ?? 1, documents[Registry]["data"]!.AsObject());
        foreach (var resource in prepared.Resources)
            if (resource.Kind == "backend" && chosen.Categories.Contains("service") ||
                resource.Kind != "backend" && chosen.Categories.Contains("model"))
                ImportedResolutions.Record(next[Registry]["data"]!.AsObject(), context, requests[resource.Kind + ":" + resource.Key], resource, registeredName);
        var sourceIdentities = sourceInputs.Keys.Select(PhysicalDirectory.Read).ToHashSet();
        foreach (var pair in roots)
        {
            var text = RecipeIntention.Yaml(pair.Value);
            if (originalRoots.TryGetValue(pair.Key, out var original) && Equivalent(original, pair.Value) && inputs.ContainsKey(context.PathFor(pair.Key)))
                text = RecipeYaml.ReadText(context.PathFor(pair.Key), false, inputs[context.PathFor(pair.Key)]);
            var destination = context.PathFor(pair.Key);
            if (File.Exists(destination) && sourceIdentities.Contains(PhysicalDirectory.Read(destination)) &&
                !RecipeYaml.ReadBytes(destination).AsSpan().SequenceEqual(ProjectDocuments.Utf8.GetBytes(text)))
                throw new StorageException("import-source-conflict", "La publicación modificaría un archivo fuente; se conserva el original.");
            var data = new JsonObject { ["text"] = text, ["path-bases"] = bases[pair.Key].DeepClone() };
            next[pair.Key] = ProjectDocuments.Create("recipe-intention", next.GetValueOrDefault(pair.Key)?["revision"]?.GetValue<long>() ?? 1, data);
        }
        var nextSource = next[Registry]["data"]!["recipes"]!.AsArray().Single(r => r!["recipe-id"]!.GetValue<string>() == association["recipe-id"]!.GetValue<string>())!;
        var outstanding = (association["import-pending"] as JsonArray ?? []).Select(n => n!.GetValue<string>())
            .Concat(pending.Select(p => p.Key)).Distinct(StringComparer.Ordinal);
        nextSource["import-pending"] = new JsonArray(outstanding.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray());
        ScopeStore.CommitTogether([new(store, current.Revision, next, asDefault ? serviceId : null, Validate)], stage =>
        {
            fault?.Invoke(stage);
            if (stage == ScopeWriteStage.Prepared) Validate();
        });
        void ValidatePublication()
        {
            if (Changed(sourceInputs))
                throw new StorageException("intention-conflict", "Cambió la fuente antes de publicar presets importados.");
            foreach (var path in pending.Select(p => p.Path).Append("recipe.yaml"))
                if (File.ReadAllText(context.PathFor(path)) != next[path]["data"]!["text"]!.GetValue<string>())
                    throw new StorageException("intention-conflict", "Cambió la intención global antes de publicar.");
            if (expectedInstance is not null && ProjectDocuments.Read(File.ReadAllBytes(context.PathFor(activePath!)), "active")["data"]?["instance-id"]?.GetValue<string>() != expectedInstance)
                throw new StorageException("instance-conflict", "Cambió la instancia durante la importación.");
        }
        var warmupFailed = false;
        try
        {
            foreach (var p in pending)
            {
                cancellation.ThrowIfCancellationRequested();
                var block = ArgumentValidation.Validate(effective).Single(b => b.Scope == "preset" && b.Key == p.Key);
                var enabled = effective.PresetEnabled(p.Key);
                var bindings = new JsonArray(prepared.Resources.Where(r => r.Kind != "backend").Select(r => (JsonNode?)new JsonObject
                { ["resource-id"] = r.ResourceId, ["installation-id"] = r.InstallationId, ["kind"] = r.Kind }).ToArray());
                string Resolve(ArgumentReference reference) => prepared.Resources.Single(r => r.Kind == reference.Kind && r.Key.Equals(reference.Name, StringComparison.OrdinalIgnoreCase)).EntryPath;
                var adapter = new LlamaB11482ArgumentAdapter();
                var presets = enabled ? new[] { new PublishedPreset(p.Key, ArgumentEmission.Ini(effective, block, p.Key, adapter, Resolve),
                ArgumentEmission.Cli(effective, CliBlock(block, adapter), adapter, Resolve).ToArray(), bindings, effective.PresetWarmup(p.Key)) } : [];
                var manager = new PresetPublication(store, store, serviceId!);
                var result = await manager.PublishAsync(p.Owner["recipe-id"]!.GetValue<string>(), presets, prepared.Id,
                    p.Owner["name"]!.GetValue<string>(), !live, !live || interactive, live ? confirm ?? (_ => false) : _ => true, selectedRouter, ValidatePublication, cancellation: cancellation,
                    fault: publicationFault,
                    resumeWarmup: association["import-pending"]?.AsArray().Any(n => n!.GetValue<string>() == p.Key) == true);
                warmupFailed |= result.WarmupFailed;
                if (!result.Confirmed || result.WarmupFailed)
                {
                    PublicationResult? restoration = null;
                    if (result.Confirmed && result.WarmupFailed && !result.Restored && live)
                        restoration = await manager.RestoreFailedAsync(p.Owner["recipe-id"]!.GetValue<string>(), selectedRouter!, interactive, confirm ?? (_ => false), cancellation);
                    return new(false, warmupFailed, Remaining(association), ExitCode: !result.Confirmed || restoration?.Confirmed == false ? 9 : 8);
                }
                MarkCompleted(association["recipe-id"]!.GetValue<string>(), p.Key);
            }
        }
        catch (OperationCanceledException e)
        { throw new OperationCanceledException("Intención importada; publicación cancelada y remanentes conservados.", e, cancellation); }
        catch (StorageException e)
        { throw new StorageException(e.Code, "Intención importada; publicación incompleta y remanentes conservados. Causa: " + e.Code + ".", true, e); }
        catch (IOException e)
        { throw new StorageException("import-publication-io", "Intención importada; fallo de escritura de publicación y remanentes conservados.", true, e); }
        return new(true, warmupFailed, Remaining(association));

        string[] Remaining(JsonObject owner) => store.Read().Documents[Registry]["data"]!["recipes"]!.AsArray()
            .Single(r => r!["recipe-id"]!.GetValue<string>() == owner["recipe-id"]!.GetValue<string>())!["import-pending"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        void CheckConflict(string selector, RecipeValue? previous, RecipeValue value, JsonObject oldBases, string prefix)
        {
            if (previous is null || Equivalent(Normalize(previous, selector), Normalize(value, selector)) && oldBases.Where(p => p.Key.StartsWith(prefix + "/", StringComparison.Ordinal)).All(p => p.Value!.GetValue<string>() == selection.Base)) return;
            if (!chosen.AllowsReplacement(selector))
            {
                var old = Normalize(previous, selector); var next = Normalize(value, selector);
                var changed = old.Fields.Keys.Union(next.Fields.Keys, StringComparer.Ordinal)
                    .Where(k => old.Get(k) is not { } a || next.Get(k) is not { } b || !Equivalent(a, b)).Order(StringComparer.Ordinal).ToArray();
                var fields = changed.Length == 0 ? (Equivalent(old, next) ? "base de rutas" : "valor/forma") : string.Join(", ", changed);
                var key = selector[(selector.IndexOf(':') + 1)..];
                var kind = selector[..selector.IndexOf(':')];
                var impact = kind == "param" ? "Asignaciones directas del argumento en destino: " + roots.Values.Sum(root => CountAssignments(root, key)) :
                    kind is "model" or "companion" ? "Referencias tipadas en intención global: " + roots.Values.Sum(root => CountReferences(root, kind + "://" + key)) :
                    kind == "preset" ? "Publicaciones registradas del nombre: " + recipes.Sum(r => (r!["publications"] as JsonArray ?? []).Sum(p => p!["presets"]!.AsArray().Count(v => v!["name"]!.GetValue<string>() == key))) :
                    "Consumidores del servicio: " + recipes.SelectMany(r => r!["entities"]!.AsArray()).Where(e => e!["kind"]!.GetValue<string>() == "service" && e["name"]!.GetValue<string>() == key).Sum(e => (e!["consumers"] as JsonArray ?? []).Count);
                throw new StorageException("import-entity-conflict", "Contenido global diferente: " + selector + ". Campos: " + fields +
                    ". " + impact + ". Se conservan intención, publicaciones y revisiones activas. Autorizar reemplazo explícito.");
            }
        }
        void AddBases(JsonObject pathBases, string prefix, RecipeValue value, string kind)
        {
            foreach (var key in pathBases.Select(p => p.Key).Where(k => k == prefix || k.StartsWith(prefix + "/", StringComparison.Ordinal)).ToArray()) pathBases.Remove(key);
            if (kind is "model" or "companion" && value.Kind == RecipeKind.Map && value.Get("repository") is null)
                for (var i = 0; i < (value.Get("files")?.Items.Count ?? 0); i++)
                    if (!Path.IsPathFullyQualified(value.Get("files")!.Items[i].Text!)) pathBases[prefix + "/files/" + i] = selection.Base;
            if (kind == "service")
                foreach (var key in new[] { "backend/executable", "server/logs/directory", "server/hooks/start-failed/command" })
                {
                    var item = value; foreach (var component in key.Split('/')) item = item?.Get(component);
                    if (item is not null && !Path.IsPathFullyQualified(item.Text!)) pathBases[prefix + "/" + key] = selection.Base;
                }
        }
    }

    private static int CountAssignments(RecipeValue value, string key) =>
        (value.Get("args")?.Fields.ContainsKey(key) == true ? 1 : 0) + value.Fields.Values.Sum(v => CountAssignments(v, key)) + value.Items.Sum(v => CountAssignments(v, key));
    private static int CountReferences(RecipeValue value, string reference) => value.Get("literal") is not null ? 0 :
        (value.Kind == RecipeKind.String && value.Text!.Equals(reference, StringComparison.OrdinalIgnoreCase) ? 1 : 0) +
        value.Fields.Values.Sum(v => CountReferences(v, reference)) + value.Items.Sum(v => CountReferences(v, reference));

    private static RecipeValue ParameterIntention(ArgumentSchema schema, string key, RecipeValue rawRoot)
    {
        var effective = schema.Definitions[key];
        var raw = rawRoot.Get("arg-defs")?.Get("definitions")?.Get(key);
        return raw?.Origin == effective.Origin ? raw : effective;
    }
    private static bool Changed(IReadOnlyDictionary<string, byte[]> inputs)
    {
        foreach (var pair in inputs)
        {
            try { if (!RecipeYaml.ReadBytes(pair.Key).AsSpan().SequenceEqual(pair.Value)) return true; }
            catch (RecipeException) { return true; }
        }
        return false;
    }

    private FileStream ImportLease()
    {
        try { return new FileStream(context.PathFor("local/import.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new StorageException("import-busy", "Otra importación está en curso.", inner: e); }
    }
    private static ValidatedArgumentBlock CliBlock(ValidatedArgumentBlock block, IArgumentAdapter adapter) => block with
    { Arguments = block.Arguments.Where(p => adapter.Supports(p.Key, "preset", "cli")).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) };
    private static JsonObject Entry(string name, string[] files) => new()
    {
        ["recipe-id"] = ProjectDocuments.Id(),
        ["consumer-id"] = ProjectDocuments.Id(),
        ["name"] = name,
        ["files"] = new JsonArray(files.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()),
        ["revision"] = 1,
        ["entities"] = new JsonArray(),
        ["pending-withdrawals"] = new JsonArray()
    };
    private static bool Equivalent(RecipeValue a, RecipeValue b)
    {
        if (a.Kind is RecipeKind.Integer or RecipeKind.Number && b.Kind is RecipeKind.Integer or RecipeKind.Number) return ArgumentSchema.Equal(a, b);
        if (a.Kind != b.Kind) return false;
        if (a.Kind == RecipeKind.Map) return a.Fields.Count == b.Fields.Count && a.Fields.All(p => b.Get(p.Key) is { } v && Equivalent(p.Value, v));
        if (a.Kind == RecipeKind.Sequence) return a.Items.Count == b.Items.Count && a.Items.Zip(b.Items).All(p => Equivalent(p.First, p.Second));
        return a.Text == b.Text && (!(a.Text?.Contains('$') ?? false) || (a.Style is RecipeStyle.SingleQuoted or RecipeStyle.Block) == (b.Style is RecipeStyle.SingleQuoted or RecipeStyle.Block));
    }
    internal static RecipeValue Normalize(RecipeValue value, string selector)
    {
        if (selector.StartsWith("model:", StringComparison.Ordinal) && value.Kind == RecipeKind.String && !value.Text!.Contains('$'))
            value = RecipeStructure.NormalizeModels(Map(new() { ["models"] = Map(new() { ["v"] = value }) })).Get("models")!.Get("v")!;
        if (value.Kind != RecipeKind.Map) return value;
        var fields = value.Fields.ToDictionary(StringComparer.Ordinal);
        if (selector.StartsWith("preset:", StringComparison.Ordinal))
        {
            fields.TryAdd("enable", new(RecipeKind.Boolean, Origin, "true")); fields.TryAdd("warmup", new(RecipeKind.Boolean, Origin, "false"));
            fields.TryAdd("args", Map([])); fields.TryAdd("args-mode", Text("permissive"));
        }
        if (selector.StartsWith("model:", StringComparison.Ordinal) || selector.StartsWith("companion:", StringComparison.Ordinal))
            if (fields.ContainsKey("repository")) { fields.TryAdd("revision", Text("main")); fields.TryAdd("files", new(RecipeKind.Sequence, Origin)); }
        return value.WithFields(fields);
    }
    private void MarkCompleted(string sourceId, string key)
    {
        var snapshot = store.Read(); var docs = RecipeRegistry.Next(snapshot);
        var source = docs[Registry]["data"]!["recipes"]!.AsArray().Single(r => r!["recipe-id"]!.GetValue<string>() == sourceId)!;
        source["import-pending"] = new JsonArray(source["import-pending"]!.AsArray().Where(n => n!.GetValue<string>() != key).Select(n => n!.DeepClone()).ToArray());
        store.Commit(snapshot.Revision, docs);
    }
    private sealed class SelectedResources(ICombinationResources inner,
        RecipeImportSelection selected, ScopeContext context, Action<PreparationRequest> capture) : ICombinationResources
    {
        public Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exact, CancellationToken cancellation)
        {
            capture(request);
            if (request.Kind == "backend" || selected.Categories.Contains("model")) return inner.PrepareAsync(request, exact, cancellation);
            var models = new Coquena.Models.ScopedModels(context);
            var path = models.ResolveReference(request.Reference ?? new(request.Kind, request.Key));
            var snapshot = new ScopeStore(context).Read();
            var alias = snapshot.Documents["local/inventory.json"]["data"]?["model-aliases"]?.AsArray().SingleOrDefault(a => a!["kind"]!.GetValue<string>() == request.Kind && a["key"]!.GetValue<string>().Equals(request.Key, StringComparison.OrdinalIgnoreCase));
            var resource = models.List(request.Kind == "companion").Single(r => alias is not null ? r!["resource-id"]!.GetValue<string>() == alias["resource-id"]!.GetValue<string>() : r!["logical-key"]!.GetValue<string>().Equals(request.Key, StringComparison.OrdinalIgnoreCase))!;
            return Task.FromResult(new PreparationResource(request.Key, request.Kind, resource["resource-id"]!.GetValue<string>(),
                alias?["installation-id"]?.GetValue<string>() ?? resource["installations"]!.AsArray().Last()!["installation-id"]!.GetValue<string>(), path, new JsonObject { ["origin"] = resource["ownership"]!.GetValue<string>() == "external" ? "external" : "hugging-face" }));
        }
    }
}
