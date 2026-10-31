using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class RecipeImportIntegrationTests
{
    public static async Task Run()
    {
        var count = 0;
        void Check(bool value, string detail) { if (!value) throw new Exception(detail); count++; }
        var root = Path.Combine(@"D:\tmp", "coquena-import-integration-" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "source"); Directory.CreateDirectory(sourceDir);
        var context = ScopeContext.Global(new Home(Path.Combine(root, "home")));
        var importer = new RecipeImporter(context); var store = new ScopeStore(context);
        var sourcePath = Path.Combine(sourceDir, "coquena.yaml");
        var weight = Path.Combine(sourceDir, "model.gguf");
        using (var writer = new BinaryWriter(File.Create(weight))) { writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(0ul); }
        File.WriteAllText(Path.Combine(sourceDir, "backend.exe"), "fixture, never executed by import");
        File.WriteAllText(Path.Combine(sourceDir, "schema.json"), """{"definitions":{"ctx-size":{"type":"integer"}}}""");
        var content = """
            name: laboratorio
            target: self
            service:
              backend:
                executable: ./backend.exe
              server:
                health:
                  url: http://127.0.0.1:29991/health
            models:
              principal:
                files: ["${MODEL_FILE}"]
            presets:
              resumen:
                args:
                  model: model://principal
                  ctx-size: 1024
            arg-defs:
              imports: [./schema.json]
            """;
        File.WriteAllText(sourcePath, content);
        var environment = new Dictionary<string, string?> { ["MODEL_FILE"] = "./model.gguf" };
        using var http = new HttpClient(new NoNetwork());
        using var transport = new OfficialArtifactTransport();
        var provider = new ScopedCombinationResources(context,
            new("win-x64", "fixture", null, [], null, [], new("unknown", ""), new("unknown", "")),
            new HuggingFaceStore(http), transport, new NoInteraction(), allowNewLocalRevision: true);
        var result = await importer.ImportAsync(new(sourceDir), provider, asDefault: true, environment: environment);
        Check(result.Confirmed && result.Pending.Length == 0, "complete import confirmed while stopped");
        var registry = new RecipeRegistry(store); var service = registry.Services().Single();
        Check(service.Name == "laboratorio" && registry.ResolveGlobalService("default") == service.ServiceId, "service and default registered together");
        Check(!store.Read().Documents.Keys.Any(p => p.EndsWith("active.json", StringComparison.Ordinal)), "import never starts service");
        Check(File.ReadAllText(sourcePath) == content && !Directory.Exists(Path.Combine(sourceDir, ".coquena")), "source and local project untouched");
        var common = File.ReadAllText(context.PathFor("recipe.yaml"));
        Check(common.Contains("${MODEL_FILE}") && !common.Contains(weight) && !common.Contains("schema.json"), "raw expression and materialized schema");
        var iniPath = new PresetPublication(store, store, service.ServiceId).IniPath;
        Check(File.ReadAllText(iniPath).Contains("[resumen]") && !File.ReadAllText(iniPath).Contains("[laboratorio/"), "preset promoted without prefix");
        var resource = new ScopedModels(context).List().Single()!;
        Check(resource["logical-key"]!.GetValue<string>() == "principal" && resource["ownership"]!.GetValue<string>() == "external", "global resource keeps local key and external ownership");
        Check(Directory.GetFiles(context.Root, "*.gguf", SearchOption.AllDirectories).Length == 0 && File.Exists(weight), "no weights copied");
        Check((await importer.ImportAsync(new(sourceDir), provider, asDefault: true, environment: environment)).Confirmed, "identical import is idempotent");
        var beforeConflict = File.ReadAllText(context.PathFor("consumers/" + registry.Recipes().Single(r => r.Files.Any(f => f.StartsWith("consumers/", StringComparison.Ordinal))).ConsumerId + "/recipe.yaml"));
        File.WriteAllText(sourcePath, content.Replace("1024", "2048"));
        try { await importer.ImportAsync(new(sourceDir), provider, environment: environment); throw new Exception("Expected conflict"); }
        catch (StorageException e) { Check(e.Code == "import-entity-conflict", "conflicting preset rejected by default"); }
        Check(!File.ReadAllText(iniPath).Contains("2048"), "conflict preserves composition");
        Check((await importer.ImportAsync(new(sourceDir), provider, replace: ["preset:resumen"], environment: environment)).Confirmed, "targeted replacement accepted");
        Check(File.ReadAllText(iniPath).Contains("ctx-size=2048"), "replacement updates stopped composition");
        var newWeight = Path.Combine(sourceDir, "moved.gguf"); File.Copy(weight, newWeight);
        environment["MODEL_FILE"] = "./moved.gguf";
        Check((await importer.ImportAsync(new(sourceDir), provider, environment: environment)).Confirmed, "variable re-evaluated with unchanged intention");
        Check(new ScopedModels(context).List().Single()!["installations"]!.AsArray().Count == 2 && File.Exists(weight), "previous installation retained");
        Check(File.ReadAllText(iniPath).Contains(newWeight), "new command uses newly resolved path");

        // A selective preset import must resolve its model in destination, ignoring
        // the excluded model declaration in the incoming source.
        File.WriteAllText(sourcePath, content.Replace("target: self", "target: global").Replace("1024", "4096").Replace("./schema.json", "./absent-schema.json"));
        var selectedResult = await importer.ImportAsync(new(sourceDir), provider, select: "presets", replace: ["preset:resumen"], environment: environment);
        Check(selectedResult.Confirmed && File.ReadAllText(iniPath).Contains("4096"), "selective preset uses destination model and params without loading excluded schema");
        var receipt = store.Read().Documents["local/recipes.json"]["data"]!["recipes"]!.AsArray().Single(r => r!["global-preset"]?.GetValue<string>() == "resumen")!;
        var consumerId = receipt["recipe-id"]!.GetValue<string>();
        var fakeRouter = new Router(iniPath);
        await fakeRouter.ReloadAsync(default);
        var initialReloads = fakeRouter.Reloads;
        File.WriteAllText(sourcePath, File.ReadAllText(sourcePath).Replace("4096", "8192"));
        var uncertain = await importer.ImportAsync(new(sourceDir), provider, select: "presets", replace: ["preset:resumen"], environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => { fakeRouter.LoseResponse = true; return fakeRouter; });
        Check(!uncertain.Confirmed && uncertain.Pending.SequenceEqual(new[] { "resumen" }), "lost response preserves pending import");
        Check(PublicationPending(store, service.ServiceId), "remote uncertainty durable");
        var alternatePath = Path.Combine(sourceDir, "alternate.yaml");
        File.WriteAllText(alternatePath, "name: alternate\nservice:\n  backend:\n    executable: ./backend.exe\n");
        await importer.ImportAsync(new(sourceDir, [alternatePath]), provider, select: "service", environment: environment);
        var alternateId = registry.Services().Single(s => s.Name == "alternate").ServiceId;
        registry.SetDefaultService(alternateId);
        var reconciled = await importer.ImportAsync(new(sourceDir), provider, select: "presets", environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: id => { Check(id == service.ServiceId, "pending publication retains destination after alias changes"); return fakeRouter; });
        Check(reconciled.ReconciledOnly && reconciled.Confirmed && fakeRouter.Reloads == initialReloads + 1, "retry observes without duplicate reload");
        Check(!PublicationPending(store, service.ServiceId), "confirmed reconciliation clears reservation");
        registry.SetDefaultService(service.ServiceId);
        var allBefore = store.Read().Documents["local/recipes.json"]["data"]!.ToJsonString();
        File.WriteAllText(sourcePath, File.ReadAllText(sourcePath).Replace("8192", "16384"));
        try { await importer.ImportAsync(new(sourceDir), provider, select: "presets", replace: ["preset:resumen"], environment: environment, running: _ => true); throw new Exception("Expected interaction requirement"); }
        catch (StorageException e) { Check(e.Code == "interaction-required" && store.Read().Documents["local/recipes.json"]["data"]!.ToJsonString() == allBefore, "headless active publication rejects before intention changes"); }
        Check(receipt["recipe-id"]!.GetValue<string>() == consumerId, "owner remains stable across replacement");
        File.AppendAllText(context.PathFor("recipe.yaml"), "# editor change\n");
        registry.SetDefaultService(service.ServiceId);
        Check(File.ReadAllText(context.PathFor("recipe.yaml")).EndsWith("# editor change\n", StringComparison.Ordinal), "unrelated commit preserves manual YAML edits");

        var otherPath = Path.Combine(sourceDir, "concurrent.yaml");
        const string serviceYaml = "name: nuevo\nservice:\n  backend:\n    executable: ./backend.exe\n";
        File.WriteAllText(otherPath, serviceYaml);
        var controlled = new Controlled(provider);
        controlled.During = async () =>
        {
            try { await importer.ImportAsync(new(sourceDir, [otherPath]), provider, environment: environment); throw new Exception("Expected import lease"); }
            catch (StorageException e) { Check(e.Code == "import-busy", "overlapping import rejected by OS lease"); }
            File.AppendAllText(otherPath, "# concurrent source edit\n");
        };
        try { await importer.ImportAsync(new(sourceDir, [otherPath]), controlled, environment: environment); throw new Exception("Expected source conflict"); }
        catch (StorageException e) { Check(e.Code == "intention-conflict" && registry.Services().All(s => s.Name != "nuevo"), "concurrent edit prevents intention commit"); }
        File.WriteAllText(otherPath, serviceYaml);
        try
        {
            await importer.ImportAsync(new(sourceDir, [otherPath]), provider, environment: environment,
                fault: point => { if (point == ScopeWriteStage.Prepared) File.AppendAllText(otherPath, "# edit after staging\n"); });
            throw new Exception("Expected pre-decision conflict");
        }
        catch (StorageException e) { Check(e.Code == "intention-conflict" && registry.Services().All(s => s.Name != "nuevo"), "source checked again after staging immediately before commit decision"); }
        foreach (var stage in new[] { ScopeWriteStage.Prepared, ScopeWriteStage.Decided, ScopeWriteStage.ParticipantPublished })
        {
            var crashPath = Path.Combine(sourceDir, "crash-" + stage + ".yaml");
            var name = "crash-" + stage;
            File.WriteAllText(crashPath, serviceYaml.Replace("nuevo", name));
            try
            {
                await importer.ImportAsync(new(sourceDir, [crashPath]), provider, asDefault: true, environment: environment,
                    fault: point => { if (point == stage) throw new InvalidOperationException("fixture interruption"); });
                throw new Exception("Expected interruption");
            }
            catch (InvalidOperationException) when (stage == ScopeWriteStage.Prepared) { }
            catch (StorageException e) when (e.Code == "scope-commit-pending") { }
            var recovered = store.Read();
            var recoveredService = registry.Services().SingleOrDefault(s => s.Name == name);
            Check(stage == ScopeWriteStage.Prepared ? recoveredService is null : recoveredService is not null &&
                registry.ResolveGlobalService("default") == recoveredService.ServiceId && File.Exists(context.PathFor($"services/{recoveredService.ServiceId}/recipe.yaml")),
                "generation recovery publishes YAML, registry and alias together at " + stage);
        }
        registry.SetDefaultService(service.ServiceId);
        environment["CTX"] = "1234"; environment["WARM"] = "false";
        File.WriteAllText(sourcePath, "name: laboratorio\npresets:\n  typed:\n    warmup: ${WARM|boolean}\n    args:\n      model: model://principal\n      ctx-size: ${CTX|integer}\n");
        Check((await importer.ImportAsync(new(sourceDir), provider, select: "presets", environment: environment)).Confirmed, "typed expressions survive materialization");
        Check(File.ReadAllText(iniPath).Contains("[resumen]") && File.ReadAllText(iniPath).Contains("[typed]"), "absent global presets remain published");
        var typedFile = registry.Recipes().Single(r => r.Files.Any(f => f.StartsWith("consumers/", StringComparison.Ordinal)) &&
            File.ReadAllText(context.PathFor(r.Files.Single())).Contains("${CTX|integer}"));
        Check(File.ReadAllText(context.PathFor(typedFile.Files.Single())).Contains("${WARM|boolean}"), "no expanded boolean frozen in recipe");
        var independentPath = Path.Combine(sourceDir, "independent.yaml");
        File.Copy(weight, Path.Combine(sourceDir, "independent.gguf"));
        File.WriteAllText(independentPath, "name: independent\nmodels:\n  independent:\n    files: [./independent.gguf]\n");
        Check((await importer.ImportAsync(new(sourceDir, [independentPath]), provider, environment: new Dictionary<string, string?>())).Confirmed,
            "independent model import does not demand variables of unrelated global models");
        Check(RecipeReader.Read(importer.ServiceSelection(service.ServiceId), new Dictionary<string, string?>()).Root.Get("models")!.Fields.Count == 0,
            "process-only service selection does not expand unused model variables");
        var missingPath = Path.Combine(sourceDir, "missing.yaml");
        File.WriteAllText(missingPath, "name: missing\nmodels:\n  excluded:\n    files: [./missing.gguf]\npresets:\n  missing:\n    args:\n      model: model://excluded\n");
        var beforeMissing = store.Read().Revision;
        try { await importer.ImportAsync(new(sourceDir, [missingPath]), provider, select: "presets", environment: environment); throw new Exception("Expected missing dependency"); }
        catch (RecipeException e) { Check(e.Code == "unknown-resource-reference" && store.Read().Revision == beforeMissing, "excluded missing dependency fails before intention or resource preparation"); }

        await fakeRouter.ReloadAsync(default);
        var warmPath = Path.Combine(sourceDir, "warm.yaml");
        const string warmYaml = "name: warm\npresets:\n  warm:\n    warmup: true\n    args:\n      model: model://principal\n      ctx-size: 256\n";
        File.WriteAllText(warmPath, warmYaml);
        var warmLost = await importer.ImportAsync(new(sourceDir, [warmPath]), provider, select: "presets", environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => { fakeRouter.LoseResponse = true; return fakeRouter; });
        Check(!warmLost.Confirmed && fakeRouter.Warmups == 0, "warmup remains pending after lost reload response");
        var reloadsBeforeWarm = fakeRouter.Reloads;
        var warmResumed = await importer.ImportAsync(new(sourceDir, [warmPath]), provider, select: "presets", environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => fakeRouter);
        Check(warmResumed.Confirmed && warmResumed.ReconciledOnly && fakeRouter.Warmups == 1 && fakeRouter.Reloads == reloadsBeforeWarm, "reconciliation completes declared warmup without another reload");
        File.WriteAllText(warmPath, warmYaml.Replace("256", "512")); fakeRouter.FailWarmup = true;
        var warmFailure = await importer.ImportAsync(new(sourceDir, [warmPath]), provider, select: "presets", replace: ["preset:warm"], environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => fakeRouter);
        Check(!warmFailure.Confirmed && warmFailure.WarmupFailed && warmFailure.Pending.Contains("warm") &&
            fakeRouter.Catalog.Single(p => p!["id"]!.GetValue<string>() == "warm")!["status"]!["args"]!.AsArray().Any(a => a!.GetValue<string>() == "256"), "failed warmup restores previous contribution and keeps imported intention pending");
        fakeRouter.FailWarmup = false;
        Check((await importer.ImportAsync(new(sourceDir, [warmPath]), provider, select: "presets", environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => fakeRouter)).Confirmed, "explicit retry completes pending warmup candidate");
        File.WriteAllText(warmPath, warmYaml.Replace("256", "768")); fakeRouter.FailWarmup = true; fakeRouter.LoseRestorationResponse = true;
        var unknownRestoration = await importer.ImportAsync(new(sourceDir, [warmPath]), provider, select: "presets", replace: ["preset:warm"], environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => fakeRouter);
        Check(!unknownRestoration.Confirmed && unknownRestoration.ExitCode == 9, "uncertain restoration takes precedence over warmup failure");
        fakeRouter.FailWarmup = false; fakeRouter.LoseRestorationResponse = false;
        var restoredWarm = await importer.ImportAsync(new(sourceDir, [warmPath]), provider, select: "presets", environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => fakeRouter);
        Check(!restoredWarm.Confirmed && restoredWarm.ReconciledOnly && restoredWarm.ExitCode == 8, "restoration reconciliation retains desired candidate pending");
        Check((await importer.ImportAsync(new(sourceDir, [warmPath]), provider, select: "presets", environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => fakeRouter)).Confirmed, "retry after restored uncertainty applies candidate");
        var unsentPath = Path.Combine(sourceDir, "unsent.yaml");
        File.WriteAllText(unsentPath, "name: unsent\npresets:\n  unsent:\n    args:\n      model: model://principal\n");
        try
        {
            await importer.ImportAsync(new(sourceDir, [unsentPath]), provider, select: "presets", environment: environment,
                interactive: true, confirm: _ => true, running: _ => true, router: _ => fakeRouter,
                publicationFault: stage => { if (stage == PublicationStage.Reserved) throw new InvalidOperationException("fixture interruption"); });
            throw new Exception("Expected prepared interruption");
        }
        catch (InvalidOperationException) { }
        var unsent = await importer.ImportAsync(new(sourceDir, [unsentPath]), provider, select: "presets", environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => fakeRouter);
        Check(unsent.ReconciledOnly && !unsent.Confirmed && unsent.ExitCode == 3 && unsent.Pending.Contains("unsent"), "observing an unsent candidate never reports its import applied");
        Check((await importer.ImportAsync(new(sourceDir, [unsentPath]), provider, select: "presets", environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => fakeRouter)).Confirmed, "explicit retry publishes previously unsent candidate");
        var policyPath = Path.Combine(sourceDir, "policy.yaml");
        File.WriteAllText(policyPath, "name: policy\narg-defs:\n  definitions:\n    ctx-size:\n      type: boolean\n");
        var beforePolicy = File.ReadAllText(context.PathFor("recipe.yaml"));
        try { await importer.ImportAsync(new(sourceDir, [policyPath]), provider, select: "params", replace: ["param:ctx-size"], environment: environment); throw new Exception("Expected incompatible global schema"); }
        catch (RecipeException) { Check(File.ReadAllText(context.PathFor("recipe.yaml")) == beforePolicy, "parameter replacement validates existing global assignments before changing intention"); }
        File.WriteAllText(policyPath, "name: policy\narg-defs:\n  definitions:\n    extra:\n      type: number\n      minimum: 1\n");
        var beforePolicyIni = File.ReadAllText(iniPath);
        Check((await importer.ImportAsync(new(sourceDir, [policyPath]), provider, select: "params", environment: environment,
            running: _ => throw new Exception("Schema import must not inspect process"), router: _ => throw new Exception("Schema import must not reload"))).Confirmed &&
            File.ReadAllText(iniPath) == beforePolicyIni, "params-only import validates without publishing or probing execution");
        File.WriteAllText(policyPath, File.ReadAllText(policyPath).Replace("minimum: 1", "minimum: 1.0"));
        Check((await importer.ImportAsync(new(sourceDir, [policyPath]), provider, select: "params", environment: environment)).Confirmed,
            "normalized numeric schema values remain idempotent");
        var filteredPath = Path.Combine(sourceDir, "filtered.yaml");
        File.WriteAllText(filteredPath, "name: filtered\nservice:\n  backend:\n    executable: ${EXCLUDED_BACKEND}\nmodels:\n  excluded:\n    files: ['${EXCLUDED_LITERAL}', \"${EXCLUDED_MODEL}\"]\narg-defs:\n  imports: [\"${EXCLUDED_SCHEMA}\"]\npresets:\n  filtered:\n    args:\n      model: model://principal\n");
        var modelCount = new ScopedModels(context).List().Count; var serviceCount = registry.Services().Length;
        Check((await importer.ImportAsync(new(sourceDir, [filteredPath]), provider, select: "presets", environment: environment)).Confirmed &&
            new ScopedModels(context).List().Count == modelCount && registry.Services().Length == serviceCount,
            "selective import neither expands variables nor prepares resources of excluded source categories");
        var integrityPath = Path.Combine(sourceDir, "integrity.yaml");
        File.WriteAllText(integrityPath, "name: integrity\nmodels:\n  principal:\n    files: [\"${MODEL_FILE}\"]\n");
        var protectedModel = new ScopedModels(context).List().Single(r => r!["logical-key"]!.GetValue<string>() == "principal")!;
        var installedId = store.Read().Documents["local/inventory.json"]["data"]!["model-aliases"]!.AsArray()
            .Single(a => a!["key"]!.GetValue<string>() == "principal")!["installation-id"]!.GetValue<string>();
        void Integrity(string status)
        {
            var snapshot = store.Read(); var docs = snapshot.Documents.ToDictionary(p => p.Key,
                p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
            docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["resource-id"]!.GetValue<string>() == protectedModel["resource-id"]!.GetValue<string>())!["installations"]!.AsArray()
                .Single(i => i!["installation-id"]!.GetValue<string>() == installedId)!["integrity-status"] = status;
            store.Commit(snapshot.Revision, docs);
        }
        Integrity("invalid"); var beforeIntegrity = File.ReadAllText(context.PathFor("recipe.yaml"));
        try { await importer.ImportAsync(new(sourceDir, [integrityPath]), provider, force: true, environment: environment); throw new Exception("Expected invalid integrity"); }
        catch (StorageException e) { Check(e.Code == "resource-integrity-invalid" && File.ReadAllText(context.PathFor("recipe.yaml")) == beforeIntegrity && File.Exists(newWeight), "force cannot bypass known integrity or repair external files"); }
        Integrity("no-baseline");

        await fakeRouter.ReloadAsync(default);
        var multiPath = Path.Combine(sourceDir, "multi.yaml");
        File.WriteAllText(multiPath, "name: multi\narg-defs:\n  definitions:\n    multi-param:\n      type: string\npresets:\n  first:\n    args:\n      model: model://principal\n  second:\n    warmup: true\n    args:\n      model: model://principal\n");
        fakeRouter.FailWarmup = true;
        var partial = await importer.ImportAsync(new(sourceDir, [multiPath]), provider, select: "presets", environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => fakeRouter);
        Check(!partial.Confirmed && partial.Pending.SequenceEqual(new[] { "second" }) && fakeRouter.Catalog.Any(p => p!["id"]!.GetValue<string>() == "first"), "partial multi-preset import records only remaining work");
        var paramsOnly = await importer.ImportAsync(new(sourceDir, [multiPath]), provider, select: "params", environment: environment,
            running: _ => throw new Exception("Unselected presets must not be published"));
        Check(paramsOnly.Confirmed && paramsOnly.Pending.SequenceEqual(new[] { "second" }), "selective later import preserves earlier excluded remnant");
        fakeRouter.FailWarmup = false;
        Check((await importer.ImportAsync(new(sourceDir, [multiPath]), provider, select: "presets", environment: environment,
            interactive: true, confirm: _ => true, running: _ => true, router: _ => fakeRouter)).Confirmed, "explicit retry completes remaining multi-preset import");
        await SchemaMaterialization(sourceDir, context, importer, provider, environment, Check);
        File.Delete(Path.Combine(sourceDir, "schema.json")); File.Delete(sourcePath);
        var saved = RecipeReader.Read(importer.ServiceSelection(service.ServiceId), environment);
        Check(ArgumentValidation.Validate(saved).Count > 0 && BackendIntent.FromRecipe(saved).Executable == Path.Combine(sourceDir, "backend.exe"), "imported service is independent of source YAML/schema and preserves origin base");
        await OfficialBackend(root, Check);
        Console.WriteLine($"Recipe import integration: {count} checks passed. Fixtures: {root}");
    }
    private static async Task SchemaMaterialization(string folder, ScopeContext context, RecipeImporter importer,
        ICombinationResources provider, Dictionary<string, string?> environment, Action<bool, string> check)
    {
        var first = Path.Combine(folder, "schema-layered.yaml"); var overlay = Path.Combine(folder, "schema-overlay.yaml");
        var schema = Path.Combine(folder, "later.json");
        File.WriteAllText(first, "name: layered\narg-defs:\n  definitions:\n    layer-arg:\n      type: integer\n      description: ${DESCRIPTION}\n", new System.Text.UnicodeEncoding(false, true));
        var original = File.ReadAllBytes(first);
        File.WriteAllText(overlay, "arg-defs:\n  imports: [./later.json]\n");
        File.WriteAllText(schema, """{"definitions":{"layer-arg":{"type":"string","pattern":"^x$","default":"x","description":"literal ${UNSET} $$\nsecond"},"literal-flag":{"type":"string","default":"false","enum":["false","null","1"]}}}""");
        environment["DESCRIPTION"] = "expanded-private-value";
        check((await importer.ImportAsync(new(folder, [first, overlay]), provider, select: "params", environment: environment)).Confirmed,
            "later JSON schema overrides earlier inline definition during materialization");
        var commonPath = context.PathFor("recipe.yaml");
        var read = RecipeReader.Read(new(context.Base, [commonPath], context.Base), environment);
        var importedDefinitions = ArgumentSchema.Load(read).Definitions; var definition = importedDefinitions["layer-arg"];
        check(definition.Get("type")!.Text == "string" && definition.Get("pattern")!.Text == "^x$" && definition.Get("description")!.Text == "literal ${UNSET} $$\nsecond" &&
            importedDefinitions["literal-flag"].Get("default")!.Kind == RecipeKind.String && importedDefinitions["literal-flag"].Get("default")!.Text == "false",
            "JSON strings remain literal with dollar signs and embedded newline");
        check(!File.ReadAllText(commonPath).Contains("expanded-private-value") && !File.ReadAllText(commonPath).Contains("later.json") && File.ReadAllBytes(first).AsSpan().SequenceEqual(original),
            "materialization neither freezes shadowed expansions nor edits UTF-16 source");
        File.Delete(schema);
        check(ArgumentSchema.Load(RecipeReader.Read(new(context.Base, [commonPath], context.Base), environment)).Definitions.ContainsKey("layer-arg"),
            "materialized schema survives removal of original JSON");

        // Preserve a schema dependency explicitly authored in destination while
        // checking collisions against its effective definitions.
        File.WriteAllText(Path.Combine(context.Base, "destination-schema.json"), """{"definitions":{"kept-param":{"type":"integer"},"gate":{"type":"string","scope":"server","rules":[{"when":{"equals":"model://principal"},"requires":{"ctx-size":{"present":true}}}]}}}""");
        var globalText = File.ReadAllText(commonPath);
        if (!globalText.Contains("\"arg-defs\":")) throw new Exception("Expected materialized catalog key");
        File.WriteAllText(commonPath, globalText.Replace("\"arg-defs\":", "\"arg-defs\":\n  \"imports\": ['./destination-schema.json']"));
        var extra = Path.Combine(folder, "merge-policy.yaml");
        File.WriteAllText(extra, "name: merge-policy\narg-defs:\n  definitions:\n    kept-param:\n      type: boolean\n");
        try { await importer.ImportAsync(new(folder, [extra]), provider, select: "params", environment: environment); throw new Exception("Expected effective schema conflict"); }
        catch (StorageException e) { check(e.Code == "import-entity-conflict", "replacement authorization includes destination JSON definitions"); }
        File.WriteAllText(extra, "name: merge-policy\narg-defs:\n  definitions:\n    new-param:\n      type: string\n");
        await importer.ImportAsync(new(folder, [extra]), provider, select: "params", environment: environment);
        var merged = ArgumentSchema.Load(RecipeReader.Read(new(context.Base, [commonPath], context.Base), environment));
        check(merged.Definitions.ContainsKey("kept-param") && merged.Definitions.ContainsKey("new-param") && merged.Definitions.ContainsKey("layer-arg"),
            "unselected destination schema definitions and import references remain intact");
        var sensitive = Path.Combine(folder, "sensitive.yaml");
        File.WriteAllText(sensitive, "name: sensitive\narg-defs:\n  imports: [\"${SCHEMA_FILE}\"]\n");
        environment["SCHEMA_FILE"] = Path.Combine(folder, "sensitive-route.json");
        try { await importer.ImportAsync(new(folder, [sensitive]), provider, select: "params", environment: environment); throw new Exception("Expected unreadable sensitive schema"); }
        catch (RecipeException e) { check(e.Code == "file-unreadable" && !e.Message.Contains("sensitive-route"), "schema read diagnostic does not disclose expanded variable path"); }
    }
    private static async Task OfficialBackend(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "official-source"); Directory.CreateDirectory(folder);
        var context = ScopeContext.Global(new Home(Path.Combine(root, "official-home")));
        var importer = new RecipeImporter(context); var store = new ScopeStore(context);
        var one = Path.Combine(folder, "one.yaml"); var two = Path.Combine(folder, "two.yaml");
        const string yaml = "name: official\nservice:\n  backend:\n    version: latest\n    variant: cpu\n";
        File.WriteAllText(one, yaml); File.WriteAllText(two, yaml);
        var fixture = new ScopedBackendTests.Fixture(); fixture.Add("b1", "cpu"); fixture.Add("b2", "cpu");
        var backends = new ScopedBackends(context, fixture.Resolve, (path, _) => Task.FromResult("version: " + File.ReadAllText(path) + " (abcdef)"));
        using var http = new HttpClient(new NoNetwork());
        var provider = new ScopedCombinationResources(context,
            new("win-x64", "fixture", null, [], null, [], new("unknown", ""), new("unknown", "")),
            new HuggingFaceStore(http), fixture, new NoInteraction(), backends: backends);
        check((await importer.ImportAsync(new(folder, [one]), provider, asDefault: true)).Confirmed && fixture.Resolutions == 1 && fixture.Calls == 1,
            "official backend prepared in global scope");
        fixture.Build = "b2";
        check((await importer.ImportAsync(new(folder, [two]), provider)).Confirmed && fixture.Resolutions == 1 && fixture.Calls == 1,
            "identical intention from another source reuses global entity resolution without following latest");
        var service = new RecipeRegistry(store).Services().Single();
        var prepared = await new CombinationPreparation(context).PrepareServiceAsync(importer.ServiceSelection(service.ServiceId), provider, true);
        check(fixture.Resolutions == 1 && prepared.Resources.Single().Resolution["selection"]!["release"]!.GetValue<string>() == "b1",
            "first service preparation reuses imported exact backend resolution");
        File.WriteAllText(one, yaml.Replace("latest", "b1"));
        await importer.ImportAsync(new(folder, [one]), provider, replace: ["service:official"]);
        var pinned = backends.List().SelectMany(r => r!["installations"]!.AsArray()).Single()!;
        check(pinned["protected"]!.GetValue<bool>(), "fixed imported backend receives explicit pin protection");
        var registry = new RecipeRegistry(store); var instanceId = Guid.NewGuid().ToString("D");
        registry.RegisterInstance(service.ServiceId, instanceId, new JsonObject
        {
            ["pid"] = 123,
            ["started-at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["executable"] = prepared.Resources.Single().EntryPath
        });
        File.WriteAllText(one, yaml);
        check((await importer.ImportAsync(new(folder, [one]), provider, force: true)).Confirmed, "force replaces declared conflicting service intention");
        var installations = backends.List().SelectMany(r => r!["installations"]!.AsArray()).ToArray();
        check(installations.Length == 2 && installations.Single(i => i!["installation-id"]!.GetValue<string>() == pinned["installation-id"]!.GetValue<string>())!["protected"]!.GetValue<bool>(),
            "force retains old pin and physical revision");
        check(store.Read().Documents["local/recipes.json"]["data"]!["recipes"]!.AsArray().SelectMany(r => r!["entities"]!.AsArray())
            .Single(e => e!["id"]!.GetValue<string>() == service.ServiceId)!["instance"]!["instance-id"]!.GetValue<string>() == instanceId,
            "service definition replacement preserves registered active identity");
        var renamed = Path.Combine(folder, "renamed.yaml"); File.Move(one, renamed);
        File.WriteAllText(renamed, yaml.Replace("name: official", "name: changed"));
        check((await importer.ImportAsync(new(folder, [renamed]), provider, force: true)).Confirmed && registry.Services().Length == 1 && registry.Services().Single().Name == "official",
            "physical source-file rename preserves registered recipe/service name");
        var overlay = Path.Combine(folder, "overlay.yaml"); File.WriteAllText(overlay, "name: another-name\n");
        check((await importer.ImportAsync(new(folder, [renamed, overlay]), provider)).Confirmed && registry.Services().Length == 1 && registry.Services().Single().Name == "official",
            "adding an override does not create another recipe association or rename entities");
    }
    private static bool PublicationPending(ScopeStore store, string serviceId) => store.Read().Documents["local/recipes.json"]["data"]!["recipes"]!.AsArray()
        .SelectMany(r => r!["entities"]!.AsArray()).Single(e => e!["id"]!.GetValue<string>() == serviceId)!["publication-operation"] is not null;
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new Exception("Unexpected network request");
    }
    private sealed class Controlled(ICombinationResources inner) : ICombinationResources
    {
        public Func<Task>? During;
        public async Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exact, CancellationToken cancellation)
        {
            var result = await inner.PrepareAsync(request, exact, cancellation);
            var action = During; During = null;
            if (action is not null) await action();
            return result;
        }
    }
    private sealed class NoInteraction : IModelSelectionInteraction
    {
        public bool IsInteractive => false;
        public Task<int?> ChooseAsync(IReadOnlyList<string> choices, CancellationToken cancellation) => throw new Exception("Unexpected model selection");
        public void Show(string message) { }
    }
    private sealed class Router(string ini) : IPresetRouter
    {
        public JsonArray Catalog = []; public int Reloads, Warmups; public bool LoseResponse, FailWarmup, LoseRestorationResponse;
        public Task<JsonArray> ObserveAsync(CancellationToken cancellation) => Task.FromResult(Catalog.DeepClone().AsArray());
        public Task<string> ActivityAsync(string[] names, CancellationToken cancellation) => Task.FromResult("Actividad desconocida.");
        public Task<bool> ReloadAsync(CancellationToken cancellation)
        {
            Reloads++; Catalog = []; JsonObject? current = null;
            foreach (var line in File.ReadAllLines(ini))
                if (line.StartsWith('[')) { current = new JsonObject { ["id"] = line[1..^1], ["status"] = new JsonObject { ["args"] = new JsonArray() } }; Catalog.Add((JsonNode)current); }
                else if (current is not null && line.Contains('=')) { var pair = line.Split('=', 2); current["status"]!["args"]!.AsArray().Add((JsonNode)JsonValue.Create("--" + pair[0])!); current["status"]!["args"]!.AsArray().Add((JsonNode)JsonValue.Create(pair[1])!); }
            if (LoseResponse) { LoseResponse = false; throw new HttpRequestException("Response lost"); }
            return Task.FromResult(true);
        }
        public Task<bool> WarmupAsync(string name, CancellationToken cancellation)
        {
            Warmups++;
            var entry = Catalog.Single(p => p!["id"]!.GetValue<string>() == name)!;
            if (FailWarmup) { entry["status"]!["failed"] = true; if (LoseRestorationResponse) LoseResponse = true; }
            else entry["status"]!["value"] = "loaded";
            return Task.FromResult(!FailWarmup);
        }
    }
}
