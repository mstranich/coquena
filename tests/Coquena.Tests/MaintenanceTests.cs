using System.Diagnostics;
using System.Text.Json.Nodes;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class MaintenanceTests
{
    internal static void Run(string root, string? executable)
    {
        Directory.CreateDirectory(root);
        var home = new Home(Path.Combine(root, "home"));
        Directory.CreateDirectory(home.Root);
        ScopeContext Project(string name)
        {
            var folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
            return ScopeContext.Project(Path.Combine(folder, "coquena.yaml"));
        }
        var first = Project("first"); var second = Project("second"); var global = ScopeContext.Global(home);
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        (string Id, string Path) Add(ScopeContext scope, string key, string kind = "model", bool protect = false)
        {
            var store = new ScopeStore(scope); var snapshot = store.Read(); var docs = RecipeRegistry.Next(snapshot);
            if (!docs.ContainsKey("local/inventory.json")) docs["local/inventory.json"] = ProjectDocuments.Create("inventory", 1, new() { ["resources"] = new JsonArray() });
            var id = Guid.NewGuid().ToString("D"); var location = (kind == "backend" ? "backends" : "models") + "/installations/" + id;
            Directory.CreateDirectory(scope.PathFor(location)); var path = scope.PathFor(location + "/resource.bin");
            File.WriteAllBytes(path, new byte[128]); var identity = PhysicalDirectory.Read(path);
            docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Add(new JsonObject {
                ["resource-id"] = Guid.NewGuid().ToString("D"), ["kind"] = kind, ["logical-key"] = key, ["ownership"] = "managed",
                ["installations"] = new JsonArray(new JsonObject { ["installation-id"] = id, ["location"] = location,
                    ["origin"] = "automatic", ["protected"] = protect, ["files"] = new JsonArray(new JsonObject {
                        ["path"] = "resource.bin", ["size"] = 128, ["volume"] = identity.Volume, ["file-id"] = identity.FileId }) }) });
            store.Commit(snapshot.Revision, docs); if (!scope.IsGlobal) new ProjectIndex(home).Refresh(store);
            return (id, path);
        }
        var one = Add(first, "one"); var companion = Add(first, "aux", "companion"); var backend = Add(global, "runtime", "backend");
        var two = Add(second, "two"); var protectedModel = Add(first, "protected", protect: true);
        var selector = new CleanupSelection(["model"], [ResourceSelector.Parse("model://one")]);
        var cleanup = new ResourceCleanup(first, home);
        var plan = cleanup.Preview(selection: selector);
        var usage = cleanup.Survey(selector).Scopes.Single(s => s.Scope.Root == first.Root);
        Check(plan.Items.Count == 1 && plan.Items[0].InstallationId == one.Id && plan.Bytes == usage.Free && plan.Bytes == 128,
            "shared evaluation agrees on exact model selector and bytes");
        var view = new DashboardCli.MaintenanceView { Plan = plan };
        var page = DashboardCli.MaintenancePage("fixture", view);
        var actions = page.Rows.SelectMany(Coquena.Observation.Markup.Parse).Select(s => s.Action).OfType<string>().ToArray();
        Check(new[] { "model", "companion", "backend", "downloads" }.All(k => actions.Contains("maintenance-filter-" + k)) && actions.Contains("maintenance-clean"),
            "maintenance exposes functional filters and the shared plan action");
        view.Toggle("model");
        Check(view.Plan is null && !view.Selection.Kinds.Contains("model"), "changing a TUI filter invalidates its old plan");
        Check(new DashboardCli.DefinitionsSource(home, first.Base).Places().Single(p => p.Scope.Here).Context is not null &&
            Coquena.Observation.DefinitionsPresentation.Views(new("fixture", false, true, true, false)).Contains("maintenance"),
            "project without YAML has a maintenance context and tree entry");
        var initializeContext = Project("initialize");
        var initializeSource = new DashboardCli.DefinitionsSource(home, initializeContext.Base);
        var initializePlace = initializeSource.Places().Single(p => p.Scope.Here);
        var initOutput = new StringWriter(); var initError = new StringWriter();
        Check(initializePlace.Context is not null && DashboardCli.InitializeProject(initializePlace, initOutput, initError) == 0 &&
            File.Exists(Path.Combine(initializeContext.Base, "coquena.yaml")) &&
            File.Exists(initializeContext.PathFor(InitCli.SchemaFile)) &&
            !File.Exists(initializeContext.PathFor("local/state.json")),
            "TUI initialization works with a maintenance context without manufacturing runtime state");
        Check(initializeSource.Places().Single(p => p.Scope.Here).Scope.HasRecipe,
            "refresh after TUI initialization offers the recipe views");
        File.AppendAllText(Path.Combine(initializeContext.Base, "coquena.yaml"), "# retained\n");
        var recipeBefore = File.ReadAllText(Path.Combine(initializeContext.Base, "coquena.yaml"));
        Check(DashboardCli.InitializeProject(initializePlace, initOutput, initError) == 2 &&
            File.ReadAllText(Path.Combine(initializeContext.Base, "coquena.yaml")) == recipeBefore,
            "stale initialize action rejects an existing recipe without changing it");
        var aliasState = new ScopeStore(first).Read(); var aliasDocs = RecipeRegistry.Next(aliasState);
        var oneResource = aliasDocs["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["logical-key"]!.ToString() == "one")!;
        aliasDocs["local/inventory.json"]["data"]!["model-aliases"] = new JsonArray(new JsonObject {
            ["kind"] = "model", ["key"] = "short", ["resource-id"] = oneResource["resource-id"]!.DeepClone(), ["installation-id"] = one.Id });
        new ScopeStore(first).Commit(aliasState.Revision, aliasDocs);
        Check(cleanup.Preview(selection: new([], [ResourceSelector.Parse("model://short")])).Items.Single().InstallationId == one.Id,
            "local model aliases select the exact bound installation");
        var index = new ProjectIndex(home); var firstId = new ScopeStore(first).Read().ScopeId!;
        var gone = Project("gone"); Add(gone, "gone"); Directory.Move(gone.Base, gone.Base + "-moved");
        Check(cleanup.Survey().Scopes.Single(s => s.Scope.Root == first.Root).Free == cleanup.Preview().Bytes,
            "unreachable navigation entries do not change resource eligibility");
        Check(new ResourceCleanup(global, home).Preview().Bytes == 128, "global cleanup works without forgetting unrelated inaccessible projects");
        var forget = index.PreviewForget([], true);
        Check(forget.Projects.Count == 1 && Directory.Exists(gone.Base + "-moved"), "bulk forget previews inaccessible entries without touching files");
        index.Forget(forget);
        Check(new ResourceCleanup(global, home).Preview().Bytes == 128 && cleanup.Preview().Items.Any(i => i.InstallationId == one.Id),
            "bulk forgetting inaccessible projects unlocks global and project cleanup");
        var forgottenPlan = index.PreviewForget([firstId[..8]]);
        index.Refresh(new ScopeStore(first));
        try { index.Forget(forgottenPlan); throw new Exception("expected stale index"); }
        catch (StorageException e) when (e.Code == "project-index-conflict") { checks++; }

        var operationId = Guid.NewGuid(); var identityHash = new ContentIdentity("sha256", "file-bytes", new string('a', 64));
        var transfers = new ScopeTransfers(first);
        OperationDocument CreateOperation(Guid id) => transfers.Create(new OperationDocument(1, id, "model", "fixture/model:Q4_0",
            new("fixture/model", "main", new string('a', 40), [new("weights.gguf", 64, identityHash)]), null,
            [new("weights.gguf", $"downloads/{id:D}/files/0.part", "models/weights.gguf", 0, 64, identityHash, null, null, "pending")],
            "pending", "transfer", DateTimeOffset.UtcNow, null), "automatic");
        var operation = CreateOperation(operationId);
        var partial = first.PathFor($"downloads/{operationId:D}/files/0.part"); Directory.CreateDirectory(Path.GetDirectoryName(partial)!); File.WriteAllBytes(partial, new byte[64]);
        var downloadsOnly = new CleanupSelection([], [], true);
        var downloadPlan = cleanup.Preview(selection: downloadsOnly);
        Check(downloadPlan.Items.Count == 0 && downloadPlan.Downloads?.Count == 1 &&
            cleanup.Survey(downloadsOnly).Scopes.Single(s => s.Scope.Root == first.Root).Free == downloadPlan.Bytes,
            "download selection measures exactly its plan without installed resources");
        using (var lease = new FileStream(first.PathFor($"downloads/{operationId:D}/executor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Delete))
            Check(cleanup.Preview(selection: downloadsOnly).Downloads?.Count == 0, "live executors excluded without sending cancellation");
        var pendingDestination = first.PathFor($"models/installations/{operation.ScopePublication!.InstallationId:D}");
        Directory.CreateDirectory(pendingDestination);
        Check(cleanup.Preview(selection: downloadsOnly).Downloads?.Count == 0, "unpublished physical preparation is preserved for publication recovery");
        Directory.Delete(pendingDestination);
        File.AppendAllText(partial, "change");
        var conflicted = cleanup.Apply(downloadPlan);
        Check(conflicted.Diagnostics.Any(d => d.Code == "cleanup-conflict") && File.Exists(partial), "changed preparation is preserved after preview");
        using (var locked = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = cleanup.Apply(cleanup.Preview(selection: downloadsOnly));
            Check(failed.Diagnostics.Any(d => d.Code == "cleanup-pending") && File.Exists(first.PathFor($"downloads/{operationId:D}/operation.json")),
                "partial download cleanup failure preserves its recovery record");
        }
        var cleaned = cleanup.Apply(cleanup.Preview(selection: downloadsOnly));
        Check(cleaned.FreedBytes > 64 && cleaned.Diagnostics.Count == 0 && !File.Exists(partial) && File.Exists(one.Path),
            "selected download cleanup removes preparation and records without deleting installed resources");

        var previousHome = Environment.GetEnvironmentVariable("COQUENA_HOME"); var previousCwd = Environment.CurrentDirectory;
        try
        {
            Environment.SetEnvironmentVariable("COQUENA_HOME", home.Root); Environment.CurrentDirectory = first.Base;
            (int Code, string Text) Invoke(params string[] args)
            {
                if (executable is null)
                {
                    var output = new StringWriter(); var error = new StringWriter();
                    var code = Cli.Run(args, output, error); return (code, output + error.ToString());
                }
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, WorkingDirectory = first.Base };
                start.Environment["COQUENA_HOME"] = home.Root; foreach (var arg in args) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!; process.StandardInput.Close();
                var outputTask = process.StandardOutput.ReadToEndAsync(); var errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000)) { process.Kill(true); throw new Exception("maintenance CLI timeout"); }
                return (process.ExitCode, outputTask.GetAwaiter().GetResult() + errorTask.GetAwaiter().GetResult());
            }
            Check(Invoke("usage").Text.Contains(second.Base) && Invoke("df").Text.Contains(home.Root), "usage without flags keeps all-scope overview");
            var filtered = Invoke("usage", "--project", firstId[..8], "--select", "model://one");
            Check(filtered.Code == 0 && filtered.Text.Contains("one") && !filtered.Text.Contains(second.Base) && !filtered.Text.Contains("aux"), "CLI scopes and exact resource filters agree");
            Check(Invoke("usage", "--global").Code == 0 && Invoke("clean", "--global", "--dry-run").Code == 0, "global measurement and preview both work");
            foreach (var command in new[] { "usage", "clean" })
                foreach (var option in new[] { "--project-directory", "--base", "--env-file", "--private" })
                    Check(Invoke(command, option, first.Base).Code == 2, "rejected old option: " + command + " " + option);
            Check(Invoke("clean", "--project", firstId[..8], "--select", "model://one", "--dry-run").Code == 0 && File.Exists(one.Path), "selected preview preserves bytes");
            Check(Invoke("--project", firstId[..8], "usage", "--model").Code == 0, "project scope works before the verb too");
            var cliOperation = CreateOperation(Guid.NewGuid());
            var cliArea = first.PathFor($"downloads/{cliOperation.Id:D}");
            var downloadUsage = Invoke("usage", "-f", Path.Combine(first.Base, "missing.yaml"), "--downloads");
            Check(downloadUsage.Code == 0 && downloadUsage.Text.Contains(cliOperation.Id.ToString("D")), "CLI measures a selected download without requiring YAML");
            Check(Invoke("clean", "--project", firstId[..8], "--downloads", "--dry-run").Code == 0 && Directory.Exists(cliArea), "CLI download preview keeps preparation");
            Check(Invoke("clean", "--project", firstId[..8], "--downloads", "--yes").Code == 0 && !Directory.Exists(cliArea) && File.Exists(one.Path), "CLI download cleanup preserves installed resources");
            Check(Invoke("clean", "--project", firstId[..8], "--select", "model://one", "--yes").Code == 0 && !File.Exists(one.Path) && File.Exists(companion.Path) && File.Exists(protectedModel.Path), "exact confirmed cleanup preserves siblings and protection without YAML");
            Check(Invoke("clean", "--global", "--backend", "--project", new ScopeStore(second).Read().ScopeId![..8], "--yes").Code == 0 && !File.Exists(backend.Path) && File.Exists(two.Path), "multiple scopes with type filter never expand to another type");
            var moved = Project("moved"); Add(moved, "unreachable"); Directory.Move(moved.Base, moved.Base + "-away");
            Check(Invoke("project", "forget", "--inaccessible", "--dry-run").Code == 0 && index.List().Any(p => p.State == KnownProjectState.Unreachable), "CLI bulk forget dry run preserves index");
            Check(Invoke("project", "forget", "--inaccessible", "--yes").Code == 0 && index.List().All(p => p.State != KnownProjectState.Unreachable) && Directory.Exists(moved.Base + "-away"), "CLI bulk forget keeps project files");
        }
        finally { Environment.CurrentDirectory = previousCwd; Environment.SetEnvironmentVariable("COQUENA_HOME", previousHome); }
        var lifecycle = Project("lifecycle"); var endpoint = Project("endpoint");
        var referenced = Add(lifecycle, "referenced"); var unrelated = Add(lifecycle, "unrelated");
        var endpointStore = new ScopeStore(endpoint); var endpointRegistry = new RecipeRegistry(endpointStore);
        var targetRecipe = endpointRegistry.Register(["coquena.yaml"], "endpoint");
        var targetService = endpointRegistry.RegisterService(targetRecipe.RecipeId);
        var lifecycleStore = new ScopeStore(lifecycle); var sourceRegistry = new RecipeRegistry(lifecycleStore);
        var sourceRecipe = sourceRegistry.Register(["coquena.yaml"], "lifecycle");
        var snapshot = lifecycleStore.Read(); var documents = RecipeRegistry.Next(snapshot);
        var resource = documents["local/inventory.json"]["data"]!["resources"]!.AsArray().Single(r => r!["logical-key"]!.ToString() == "referenced")!;
        var contribution = new JsonArray(new JsonObject { ["name"] = "lifecycle/model", ["ini"] = "[lifecycle/model]\n",
            ["expected-arguments"] = new JsonArray(), ["warmup"] = false, ["resources"] = new JsonArray(new JsonObject {
                ["resource-id"] = resource["resource-id"]!.DeepClone(), ["installation-id"] = referenced.Id }) });
        documents["local/recipes.json"]["data"]!["recipes"]!.AsArray().Single(r => r!["recipe-id"]!.ToString() == sourceRecipe.RecipeId)!["publication"] = new JsonObject {
            ["service-id"] = targetService.ServiceId, ["target-root"] = endpoint.Root, ["target-base"] = endpoint.Base,
            ["target-scope-id"] = endpointStore.Read().ScopeId, ["global"] = false, ["status"] = "published", ["presets"] = contribution };
        lifecycleStore.Commit(snapshot.Revision, documents);
        var referenceCleanup = new ResourceCleanup(lifecycle, home);
        Check(referenceCleanup.Preview().Items.All(i => i.InstallationId != referenced.Id) &&
            referenceCleanup.Preview().Items.Any(i => i.InstallationId == unrelated.Id), "live publication protects only its referenced resource");
        using (var lockedState = new FileStream(endpoint.PathFor("local/state.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            Check(referenceCleanup.Preview().Items.All(i => i.InstallationId != referenced.Id) &&
                referenceCleanup.Preview().Items.Any(i => i.InstallationId == unrelated.Id), "unreadable concrete reference preserves its resource without blocking unrelated ones");
        Directory.Move(endpoint.Base, endpoint.Base + "-away");
        var deadReferencePlan = referenceCleanup.Preview();
        Check(deadReferencePlan.Items.Any(i => i.InstallationId == referenced.Id) &&
            referenceCleanup.Survey().Scopes.Single(s => s.Scope.Root == lifecycle.Root).Free == deadReferencePlan.Bytes,
            "publication to a missing destination no longer protects obsolete resources in either interface");
        Directory.Move(endpoint.Base + "-away", endpoint.Base);
        try { referenceCleanup.Apply(deadReferencePlan); throw new Exception("expected revived reference conflict"); }
        catch (StorageException) { Check(File.Exists(referenced.Path), "reference revived after preview prevents deletion"); }
        endpointRegistry.RemoveStoppedService(targetService.ServiceId);
        Check(referenceCleanup.Preview().Items.Any(i => i.InstallationId == referenced.Id), "publication to an explicitly removed service is obsolete");
        var activeState = lifecycleStore.Read(); var activeDocuments = RecipeRegistry.Next(activeState);
        activeDocuments["local/active.json"] = ProjectDocuments.Create("active", 1, new() {
            ["resources"] = contribution[0]!["resources"]!.DeepClone(), ["terminated"] = false });
        lifecycleStore.Commit(activeState.Revision, activeDocuments);
        Check(referenceCleanup.Preview().Items.All(i => i.InstallationId != referenced.Id),
            "independent runtime snapshot protects a resource even after its publication becomes obsolete");
        activeState = lifecycleStore.Read(); activeDocuments = RecipeRegistry.Next(activeState);
        activeDocuments["local/active.json"]["data"]!["terminated"] = true;
        lifecycleStore.Commit(activeState.Revision, activeDocuments);
        var stoppedService = endpointRegistry.RegisterService(targetRecipe.RecipeId);
        var stoppedState = endpointStore.Read(); var stoppedDocuments = RecipeRegistry.Next(stoppedState);
        var serviceEntity = stoppedDocuments["local/recipes.json"]["data"]!["recipes"]!.AsArray()
            .SelectMany(r => r!["entities"]!.AsArray()).Single(e => e!["id"]!.ToString() == stoppedService.ServiceId)!;
        serviceEntity["consumers"] = new JsonArray(new JsonObject {
            ["consumer-id"] = Guid.NewGuid().ToString("D"), ["scope-id"] = Guid.NewGuid().ToString("D"),
            ["source-base"] = gone.Base, ["source-root"] = gone.Root, ["source-global"] = false,
            ["names"] = new JsonArray(JsonValue.Create("lifecycle/model")), ["presets"] = contribution.DeepClone() });
        endpointStore.Commit(stoppedState.Revision, stoppedDocuments);
        Check(referenceCleanup.Preview().Items.Any(i => i.InstallationId == referenced.Id),
            "consumer with vanished source in a stopped service does not keep obsolete resource references alive");
        Console.WriteLine($"OK: {checks} comprobaciones de mantenimiento compartido y CLI.");
    }
}
