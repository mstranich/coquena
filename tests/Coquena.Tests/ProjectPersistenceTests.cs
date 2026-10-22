using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ProjectPersistenceTests
{
    public static void WriterWorker(string[] args)
    {
        var store = Open(args[1]); var snapshot = store.Read();
        File.WriteAllText(args[2], "ready");
        var timeout = Stopwatch.StartNew();
        while (!File.Exists(args[3]))
        {
            if (timeout.ElapsedMilliseconds > 10000) Environment.Exit(75);
            Thread.Sleep(10);
        }
        try { store.Commit(snapshot.Revision, RecipeRegistryForTests.Next(snapshot)); }
        catch (StorageException e) when (e.Code == "scope-conflict") { Environment.Exit(74); }
    }
    public static void Worker(string[] args)
    {
        var a = Open(args[1]);
        var b = Open(args[2]);
        var first = a.Read(); var second = b.Read();
        var stage = Enum.Parse<ScopeWriteStage>(args[3]);
        ScopeStore.CommitTogether([new(a, first.Revision, RecipeRegistryForTests.Next(first)), new(b, second.Revision, RecipeRegistryForTests.Next(second))],
            reached => { if (reached == stage) Environment.Exit(73); });
        throw new Exception("No se alcanzó la interrupción.");
    }

    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-projects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var folder = Path.Combine(root, "original"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "coquena.yaml"), "{}");
        var store = Open(folder); var registry = new RecipeRegistry(store);
        Require(store.Read().Revision == 0 && !Directory.Exists(Path.Combine(folder, ".coquena")), "lectura no inicializa");
        var recipe = registry.Register(["coquena.yaml"]);
        var service = registry.RegisterService(recipe.RecipeId);
        var resource = registry.RegisterResource("principal", "model", "external");
        Require(registry.Get(recipe.RecipeId).RegisteredName == "original", "consulta explícita conserva asociación");
        var replacement = Path.Combine(folder, "replacement.yaml"); File.WriteAllText(replacement, "name: nuevo");
        File.Replace(replacement, Path.Combine(folder, "coquena.yaml"), null);
        Require(registry.Get(recipe.RecipeId).RecipeId == recipe.RecipeId, "edición atómica conserva receta");

        var renamed = Path.Combine(root, "renamed"); Directory.Move(folder, renamed);
        store = Open(renamed); registry = new(store);
        Require(registry.Get(recipe.RecipeId).RegisteredName == "original", "rename conserva nombre");
        Require(registry.Services().Single().ServiceId == service.ServiceId, "rename conserva servicio");
        var homePath = Path.Combine(root, "home"); Directory.CreateDirectory(homePath);
        var index = new ProjectIndex(new Home(homePath)); index.Refresh(store);
        File.Delete(Path.Combine(homePath, "projects.json")); index.Refresh(store);
        Require(index.Locations().Single() == renamed && registry.Recipes().Single().RecipeId == recipe.RecipeId, "índice reconstruible");
        var globalStore = new ScopeStore(ScopeContext.Global(new Home(homePath)));
        var global = new RecipeRegistry(globalStore);
        Fail(() => global.ResolveGlobalService("default"), "unknown-service");
        var globalRecipe = global.Register(["recipe.yaml"], "global");
        var globalService = global.RegisterService(globalRecipe.RecipeId);
        global.SetDefaultService(globalService.ServiceId);
        Require(global.ResolveGlobalService("default") == globalService.ServiceId && global.ResolveGlobalService("global") == globalService.ServiceId, "alias global por identidad");
        Require(!Directory.Exists(Path.Combine(homePath, "catalogs")), "sin migración ni catálogo M2");

        var alias = Path.Combine(root, "alias");
        Junction(alias, renamed);
        var aliasStore = Open(alias);
        Require(aliasStore.Read().ScopeId == store.Read().ScopeId, "alias físico conserva ámbito");
        Fail(() => ScopeStore.CommitTogether([new(store, store.Read().Revision, RecipeRegistryForTests.Next(store.Read())),
            new(aliasStore, aliasStore.Read().Revision, RecipeRegistryForTests.Next(aliasStore.Read()))]), "duplicate-scope");
        var escape = Path.Combine(renamed, ".coquena", "escape"); Junction(escape, homePath);
        Fail(() => store.Context.PathFor("escape/file.json"), "unsafe-path");

        var copy = Path.Combine(root, "copy"); Copy(renamed, copy);
        var copiedStore = Open(copy); var copied = new RecipeRegistry(copiedStore);
        Require(copied.Recipes().Length == 0 && copiedStore.Read().ScopeId != store.Read().ScopeId, "copia no hereda propiedad");
        var copiedRecipe = copied.Register(["coquena.yaml"], "original");
        Require(copiedRecipe.ConsumerId != recipe.ConsumerId, "copia crea consumidor independiente");
        var copiedService = copied.RegisterService(copiedRecipe.RecipeId);
        Require(copiedService.ServiceId != service.ServiceId && copiedService.Name == service.Name, "nombres repetidos aislados");
        Require(copied.RegisterResource("principal", "model", "external").ResourceId != resource.ResourceId, "recursos aislados");
        Fail(() => copied.RegisterResource("principal", "model", "managed"), "resource-ownership-conflict");
        var anotherRecipe = copied.Register(["coquena.yaml"], "otra");
        Require(anotherRecipe.RecipeId != copiedRecipe.RecipeId && anotherRecipe.ConsumerId != copiedRecipe.ConsumerId,
            "archivos iguales no imponen identidad; el alta es explícita");
        Require(copied.Get(copiedRecipe.RecipeId).RegisteredName == "original", "identidad independiente de selección y nombre nuevo");

        registry.RecordContribution(recipe.RecipeId, copiedStore, copiedService.ServiceId, ["original/resumen"], [resource.ResourceId]);
        var contribution = copiedStore.Read().Documents["local/recipes.json"]["data"]!["recipes"]![0]!["entities"]![0]!["consumers"]![0]!;
        Require(contribution["consumer-id"]!.GetValue<string>() == recipe.ConsumerId, "referencia cruzada por identidad");
        var thirdFolder = Path.Combine(root, "third"); Directory.CreateDirectory(thirdFolder);
        var third = new RecipeRegistry(Open(thirdFolder)); var thirdRecipe = third.Register(["coquena.yaml"], "original");
        Fail(() => third.RecordContribution(thirdRecipe.RecipeId, copiedStore, copiedService.ServiceId, ["original/resumen"], []), "preset-name-conflict");
        var instanceId = Guid.NewGuid().ToString("D");
        registry.RegisterInstance(service.ServiceId, instanceId, new JsonObject { ["pid"] = 123,
            ["started-at"] = "2026-10-22T12:00:00Z", ["executable"] = new JsonObject { ["file-id"] = "fixture" } });
        Fail(() => registry.RegisterInstance(service.ServiceId, Guid.NewGuid().ToString("D"), new JsonObject
            { ["pid"] = 124, ["started-at"] = "2026-10-22T12:01:00Z", ["executable"] = new JsonObject() }), "instance-already-registered");

        var before = store.Read();
        registry.RegisterResource("otro", "companion", "external");
        Fail(() => store.Commit(before.Revision, RecipeRegistryForTests.Next(before)), "scope-conflict");
        using (var lease = new FileStream(Path.Combine(renamed, ".coquena", "local", "writer.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Fail(() => store.Read(), "writer-busy");
        foreach (var stage in Enum.GetValues<ScopeWriteStage>())
        {
            var a = store.Read().Revision; var b = copiedStore.Read().Revision;
            using var child = StartWorker(renamed, copy, stage);
            Require(child.WaitForExit(20000) && child.ExitCode == 73, "interrupción proceso " + stage);
            if (stage == ScopeWriteStage.Decided)
            {
                var pending = JsonNode.Parse(File.ReadAllBytes(Path.Combine(copy, ".coquena", "local", "pending.json")))!;
                var operation = JsonNode.Parse(File.ReadAllBytes(pending["data"]!["coordinator"]!.GetValue<string>()))!;
                var generation = operation["data"]!["participants"]![1]!["next"]!["data"]!["generation"]!.GetValue<string>();
                var candidate = Path.Combine(copy, ".coquena", "local", "generations", generation, "local", "recipes.json");
                var originalBytes = File.ReadAllBytes(candidate);
                File.WriteAllText(candidate, "{");
                Fail(() => copiedStore.Read(), "invalid-generation");
                Require(File.Exists(Path.Combine(copy, ".coquena", "local", "pending.json")), "evidencia comprometida preservada");
                File.WriteAllBytes(candidate, originalBytes);
            }
            // Reading either participant must reconcile the complete group first.
            var recoveredB = copiedStore.Read(); var recoveredA = store.Read();
            var delta = stage == ScopeWriteStage.Prepared ? 0 : 1;
            Require(recoveredA.Revision == a + delta && recoveredB.Revision == b + delta, "recuperación conjunta " + stage);
        }
        // Competing processes capture the same revision. Exactly one may publish;
        // the other must reread instead of overwriting an unrelated commit.
        var concurrent = store.Read();
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 2, _ =>
        {
            try { store.Commit(concurrent.Revision, RecipeRegistryForTests.Next(concurrent)); outcomes.Add("committed"); }
            catch (StorageException e) { outcomes.Add(e.Code); }
        });
        Require(outcomes.Count(x => x == "committed") == 1 && outcomes.Count(x => x == "scope-conflict") == 1, "escritores no pisan revisiones");
        var gate = Path.Combine(root, "writers-go");
        var ready1 = Path.Combine(root, "writer-one-ready"); var ready2 = Path.Combine(root, "writer-two-ready");
        using (var one = Start("--project-writer-worker", renamed, ready1, gate))
        using (var two = Start("--project-writer-worker", renamed, ready2, gate))
        {
            var timeout = Stopwatch.StartNew();
            while (!File.Exists(ready1) || !File.Exists(ready2))
            {
                Require(timeout.ElapsedMilliseconds < 10000, "inicio de escritores independientes"); Thread.Sleep(10);
            }
            File.WriteAllText(gate, "go");
            Require(one.WaitForExit(10000) && two.WaitForExit(10000) && new[] { one.ExitCode, two.ExitCode }.Order().SequenceEqual(new[] { 0, 74 }),
                "dos procesos: un commit y un conflicto");
        }

        var inventoryPath = Path.Combine(renamed, ".coquena", "local", "inventory.json");
        File.WriteAllText(inventoryPath, "{");
        Require(registry.Resources().Length == 2, "proyección corrupta no equivale a inventario vacío");
        var snapshot = store.Read(); var data = snapshot.State!["data"]!;
        var immutable = Path.Combine(renamed, ".coquena", "local", "generations", data["generation"]!.GetValue<string>(), "local", "inventory.json");
        var bytes = File.ReadAllBytes(immutable); File.WriteAllText(immutable, "{");
        Fail(() => store.Read(), "invalid-generation");
        File.WriteAllBytes(immutable, bytes);
        var statePath = Path.Combine(renamed, ".coquena", "local", "state.json");
        var stateBytes = File.ReadAllBytes(statePath);
        File.WriteAllText(statePath, "{"); Fail(() => store.Read(), "invalid-project-document");
        Require(File.ReadAllText(statePath) == "{", "corrupción preservada"); File.WriteAllBytes(statePath, stateBytes);
        foreach (var json in new[] { "{\"kind\":\"inventory\",\"kind\":\"inventory\",\"format\":1,\"revision\":1,\"data\":{}}",
            "{\"kind\":\"inventory\",\"format\":2,\"revision\":1,\"data\":{}}", "{\"kind\":\"inventory\",\"format\":1,\"revision\":1,\"data\":null}" })
            Fail(() => ProjectDocuments.Read(Encoding.UTF8.GetBytes(json), "inventory"), "invalid-project-document");
        Fail(() => ProjectDocuments.Read([0xff], "inventory"), "invalid-project-document");
        Console.WriteLine("OK: contextos, identidad física, registro, copia, índice, referencias, corrupción y recuperación entre procesos.");
        Console.WriteLine("Evidencia conservada: " + root);
    }

    private static ScopeStore Open(string folder) => new(ScopeContext.Project(Path.Combine(folder, "coquena.yaml")));
    private static Process StartWorker(string first, string second, ScopeWriteStage stage)
        => Start("--project-crash-worker", first, second, stage.ToString());
    private static Process Start(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }
    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var folder in Directory.GetDirectories(from))
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0) Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
    }
    private static void Junction(string alias, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", alias, target }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Require(process.ExitCode == 0, "crear junction de prueba");
    }
    private static void Require(bool value, string context) { if (!value) throw new Exception("Falló: " + context); }
    private static void Fail(Action action, string code)
    {
        try { action(); } catch (StorageException error) when (error.Code == code) { return; }
        throw new Exception("Falta diagnóstico: " + code);
    }
    private static class RecipeRegistryForTests
    {
        internal static Dictionary<string, JsonObject> Next(ScopeSnapshot snapshot) => snapshot.Documents.ToDictionary(x => x.Key,
            x => ProjectDocuments.Create(x.Value["kind"]!.GetValue<string>(), x.Value["revision"]!.GetValue<long>() + 1, x.Value["data"]!.AsObject()));
    }
}
