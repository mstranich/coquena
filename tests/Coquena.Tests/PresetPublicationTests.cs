using System.Text.Json.Nodes;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;
using Coquena.Backends;

namespace Coquena.Tests;

internal static class PresetPublicationTests
{
    public static async Task RealRouter(string executable, string folder)
    {
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new ArgumentException("El directorio debe estar vacío.");
        Directory.CreateDirectory(folder);
        var home = new Home(Path.Combine(folder, "home")); var target = new ScopeStore(ScopeContext.Global(home));
        var registry = new RecipeRegistry(target); var recipe = registry.Register(["service.yaml"], "shared");
        var service = registry.RegisterService(recipe.RecipeId);
        registry.SetDefaultService(service.ServiceId);
        using var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        portProbe.Start(); var port = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        var endpoint = new Uri($"http://127.0.0.1:{port}");
        var lifecycle = new ScopedServiceProcess(target, Console.Out);
        var plan = new ScopedStartPlan(executable, ["--host", "127.0.0.1", "--port", port.ToString(), "--no-models-autoload"],
            Path.Combine(folder, "router.log"), "fixture", new JsonObject(), new JsonArray(),
            new JsonObject { ["host"] = "127.0.0.1", ["port"] = port }, new Uri(endpoint, "/health"),
            TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100), PresetIni: true);
        await lifecycle.StartAsync(service.ServiceId, plan);
        try
        {
            using var http = new HttpClient(); var router = new LlamaPresetRouter(http, endpoint);
            var consumers = new List<(ScopeStore Store, RecipeIdentity Recipe)>();
            foreach (var name in new[] { "coding", "news" })
            {
                var directory = Path.Combine(folder, name); Directory.CreateDirectory(directory);
                var file = Path.Combine(directory, "coquena.yaml"); File.WriteAllText(file, "name: " + name + "\n");
                var store = new ScopeStore(ScopeContext.Project(file));
                consumers.Add((store, new RecipeRegistry(store).EnsureProjectConsumer([file], name)));
            }
            var missing = Path.Combine(folder, "missing.gguf");
            PublishedPreset Preset(string name, int context = 1024, bool warmup = false) => new(name,
                $"[{name}]\nctx-size={context}\nmodel={missing}\nn-gpu-layers=0\n", [], new JsonArray(), warmup);
            var a = new PresetPublication(consumers[0].Store, target, service.ServiceId);
            var b = new PresetPublication(consumers[1].Store, target, service.ServiceId);
            async Task Publish(PresetPublication manager, RecipeIdentity owner, PublishedPreset[] presets)
            {
                var result = await manager.PublishAsync(owner.RecipeId, presets, "fixture", owner.RegisteredName, false, true, _ => true, router);
                if (!result.Confirmed) throw new Exception("Publicación real no confirmada.");
            }
            await Publish(a, consumers[0].Recipe, [Preset("coding/chat")]);
            await Publish(b, consumers[1].Recipe, [Preset("news/summary")]);
            if ((await router.ObserveAsync(default)).Count != 2) throw new Exception("Falta consumidor real.");
            await Publish(a, consumers[0].Recipe, [Preset("coding/chat", 1536)]);
            var catalog = await router.ObserveAsync(default);
            if (!catalog.All(p => p!["status"]!["value"]!.GetValue<string>() == "unloaded")) throw new Exception("Publicar provocó carga.");
            await Publish(a, consumers[0].Recipe, []);
            catalog = await router.ObserveAsync(default);
            if (catalog.Count != 1 || catalog[0]!["id"]!.GetValue<string>() != "news/summary") throw new Exception("Retirada real alteró consumidor ajeno.");
            var failed = await b.PublishAsync(consumers[1].Recipe.RecipeId, [Preset("news/summary", 1536, warmup: true), Preset("news/success", 2048)], "fixture", "news", false, true, _ => true, router);
            var partialCatalog = await router.ObserveAsync(default);
            if (!failed.Confirmed || !failed.WarmupFailed || !partialCatalog.Any(p => p!["id"]!.GetValue<string>() == "news/summary") ||
                !partialCatalog.Any(p => p!["id"]!.GetValue<string>() == "news/success" && p["status"]!["args"]!.AsArray().Any(a => a?.GetValue<string>() == "2048")))
                throw new Exception("No restauró tras fallo real de carga.");
            var priorHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
            Environment.SetEnvironmentVariable("COQUENA_HOME", home.Root);
            try
            {
                var cliFolder = Path.Combine(folder, "cli-project"); Directory.CreateDirectory(cliFolder);
                var weights = Path.Combine(cliFolder, "fixture.gguf");
                using (var writer = new BinaryWriter(File.Create(weights)))
                { writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(0ul); }
                var yaml = Path.Combine(cliFolder, "coquena.yaml");
                const string definition = "name: cli\nservice:\n  backend:\n    executable: ./unused-missing.exe\nmodels:\n  tiny:\n    files: [./fixture.gguf]\npresets:\n  first:\n    args:\n      model: model://tiny\n      ctx-size: 1024\n  second:\n    enable: false\n    args:\n      model: model://tiny\n";
                File.WriteAllText(yaml, definition);
                var decision = new Decisions();
                void Cli(int expected, params string[] arguments)
                {
                    using var output = new StringWriter(); using var error = new StringWriter();
                    var result = PublicationCli.Run(arguments.Concat(new[] { "--base", cliFolder }).ToArray(), output, error, decision);
                    if (result != expected) throw new Exception($"CLI {string.Join(' ', arguments)}: {result}; {error}");
                }
                Cli(0, "up");
                if (!(await router.ObserveAsync(default)).Any(p => p!["id"]!.GetValue<string>() == "cli/first")) throw new Exception("CLI no publicó.");
                if (new RecipeRegistry(new ScopeStore(ScopeContext.Project(yaml))).Resources().Any(r => r.Kind == "backend")) throw new Exception("Publicar preparó backend propio no seleccionado.");
                var overlay = Path.Combine(cliFolder, "coquena.local.yaml");
                File.WriteAllText(overlay, "presets:\n  first:\n    enable: false\n  second:\n    enable: true\n");
                decision.AllowConfirm = false;
                Cli(130, "up", "-f", "coquena.yaml", "-f", "coquena.local.yaml");
                if (!(await router.ObserveAsync(default)).Any(p => p!["id"]!.GetValue<string>() == "cli/first")) throw new Exception("Cancelar desde CLI alteró la contribución.");
                decision.AllowConfirm = true;
                Cli(0, "up", "-f", "coquena.yaml", "-f", "coquena.local.yaml");
                catalog = await router.ObserveAsync(default);
                if (catalog.Any(p => p!["id"]!.GetValue<string>() == "cli/first") || !catalog.Any(p => p!["id"]!.GetValue<string>() == "cli/second")) throw new Exception("CLI no reconcilió combinación.");
                Cli(0, "apply", "first");
                if ((await router.ObserveAsync(default)).Count(p => p!["id"]!.GetValue<string>().StartsWith("cli/")) != 2) throw new Exception("Apply individual reemplazó el conjunto.");
                Cli(0, "preset", "withdraw", "first");
                var alternate = registry.Register(["alternate.yaml"], "alternate"); var alternateService = registry.RegisterService(alternate.RecipeId);
                registry.SetDefaultService(alternateService.ServiceId);
                File.WriteAllText(yaml, "invalid: edited-after-publication\n");
                Cli(0, "down");
                if ((await router.ObserveAsync(default)).Any(p => p!["id"]!.GetValue<string>().StartsWith("cli/"))) throw new Exception("Down no retiró destino guardado tras cambiar alias/YAML.");
                registry.SetDefaultService(service.ServiceId);
                File.WriteAllText(yaml, definition.Replace("name: cli", "name: renamed"));
                Cli(0, "up");
                if (!(await router.ObserveAsync(default)).Any(p => p!["id"]!.GetValue<string>() == "renamed/first")) throw new Exception("Down/up no adoptó nombre vigente.");
                var shifted = cliFolder + "-moved"; Directory.Move(cliFolder, shifted); cliFolder = shifted;
                Cli(0, "down");
                if ((await router.ObserveAsync(default)).Any(p => p!["id"]!.GetValue<string>().StartsWith("renamed/"))) throw new Exception("Down tras traslado perdió identidad.");
                if (decision.Starts != 0 || decision.Confirmations < 6) throw new Exception("Contrato de decisiones incorrecto.");
                var selfFolder = Path.Combine(folder, "self-project"); Directory.CreateDirectory(selfFolder);
                File.Copy(weights.Replace("cli-project", "cli-project-moved"), Path.Combine(selfFolder, "fixture.gguf"));
                using var selfPortProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                selfPortProbe.Start(); var selfPort = ((System.Net.IPEndPoint)selfPortProbe.LocalEndpoint).Port; selfPortProbe.Stop();
                var selfYaml = Path.Combine(selfFolder, "coquena.yaml");
                File.WriteAllText(selfYaml, $"name: own\ntarget: self\nservice:\n  backend:\n    executable: '{executable.Replace('\\', '/')}'\n  server:\n    args:\n      host: 127.0.0.1\n      port: {selfPort}\n    health:\n      url: http://127.0.0.1:{selfPort}/health\nmodels:\n  tiny:\n    files: [./fixture.gguf]\npresets:\n  local:\n    args:\n      model: model://tiny\n");
                using var ownOutput = new StringWriter(); using var ownError = new StringWriter();
                var ownDecision = new Decisions { AllowStart = false };
                if (PublicationCli.Run(["up", "--private", "--base", selfFolder], ownOutput, ownError, ownDecision) != 8 || ownDecision.Starts != 1)
                    throw new Exception("Up no pidió autorización para destino detenido.");
                var ownStore = new ScopeStore(ScopeContext.Project(selfYaml));
                var ownService = new RecipeRegistry(ownStore).FindProjectService([selfYaml]);
                var ownLifecycle = new ScopedServiceProcess(ownStore);
                try
                {
                    ownDecision.AllowStart = true;
                    var ownResult = PublicationCli.Run(["up", "--start-service", "--private", "--base", selfFolder], ownOutput, ownError, ownDecision);
                    if (ownResult != 0 || ownDecision.Starts != 1 || ownDecision.Confirmations != 1) throw new Exception($"Up --start-service: {ownResult}; {ownError}");
                    var firstInstance = ownStore.Read().Documents.Values.Single(d => d["kind"]!.GetValue<string>() == "active")["data"]!["instance-id"]!.GetValue<string>();
                    if (PublicationCli.Run(["up", "--start-service", "--private", "--base", selfFolder], ownOutput, ownError, ownDecision) != 0 ||
                        firstInstance != ownStore.Read().Documents.Values.Single(d => d["kind"]!.GetValue<string>() == "active")["data"]!["instance-id"]!.GetValue<string>())
                        throw new Exception("Reutilización self fallida.");
                    File.WriteAllText(selfYaml, File.ReadAllText(selfYaml).Replace("target: self", "target: global"));
                    if (PublicationCli.Run(["up", "--private", "--base", selfFolder], ownOutput, ownError, ownDecision) != 0 ||
                        !(await router.ObserveAsync(default)).Any(p => p!["id"]!.GetValue<string>() == "own/local"))
                        throw new Exception("Cambio de destino no conservó publicaciones independientes.");
                    if (PublicationCli.Run(["down", "--base", selfFolder], ownOutput, ownError, ownDecision) != 0 || !ownLifecycle.Reconcile(ownService.ServiceId))
                        throw new Exception("Down detuvo servicio propio.");
                    if ((await router.ObserveAsync(default)).Any(p => p!["id"]!.GetValue<string>() == "own/local")) throw new Exception("Down no retiró todos los destinos registrados.");
                }
                finally { ownLifecycle.Stop(ownService.ServiceId, () => true); }
            }
            finally { Environment.SetEnvironmentVariable("COQUENA_HOME", priorHome); }
            Console.WriteLine("OK: publicación real de dos consumidores, cambio, retirada y restauración tras carga fallida; sin inferencia.");
            Console.WriteLine("OK: CLI up/down, capas, apply/withdraw, alias cambiado, YAML editado, rename y traslado; decisiones explícitas de interfaz.");
            Console.WriteLine("OK: CLI self, autorización de inicio, --start-service separado de decisión de publicación y down sin detener proceso.");
            lifecycle.Stop(service.ServiceId, () => true);
            await b.PublishAsync(consumers[1].Recipe.RecipeId, [Preset("news/startup", warmup: true)], "fixture", "news", true, true, _ => true, null);
            var checkedBefore = target.Read().Documents.Values.Single(d => d["kind"]!.GetValue<string>() == "last-good")["data"]!["checked-at"]!.GetValue<string>();
            try
            {
                await lifecycle.StartAsync(service.ServiceId, plan with { LogPath = Path.Combine(folder, "restart.log") });
                throw new Exception("Falta fallo de precarga al iniciar.");
            }
            catch (StorageException e) when (e.Code == "preset-warmup-failed")
            {
                if (!lifecycle.Reconcile(service.ServiceId) || checkedBefore != target.Read().Documents.Values.Single(d => d["kind"]!.GetValue<string>() == "last-good")["data"]!["checked-at"]!.GetValue<string>())
                    throw new Exception("Fallo de precarga ocultó salud o promovió última buena.");
            }
            Console.WriteLine("OK: precarga declarada al iniciar; fallo por entrada conserva proceso disponible y no promueve última buena.");
        }
        finally { lifecycle.Stop(service.ServiceId, () => true); }
    }

    private sealed class Decisions : IPublicationInteraction
    {
        public bool Available => true;
        public int Starts, Confirmations;
        public bool AllowStart = true;
        public bool AllowConfirm = true;
        public bool Confirm(string diagnostic) { Confirmations++; return AllowConfirm; }
        public bool StartService() { Starts++; return AllowStart; }
    }

    public static async Task Run()
    {
        var checks = 0;
        void Check(bool condition, string detail) { if (!condition) throw new Exception(detail); checks++; }
        var root = Path.Combine("D:/tmp", "coquena-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        (ScopeStore Store, RecipeIdentity Recipe) Consumer(string name)
        {
            var folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "coquena.yaml"); File.WriteAllText(path, "name: " + name + "\n");
            var store = new ScopeStore(ScopeContext.Project(path));
            var recipe = new RecipeRegistry(store).EnsureProjectConsumer([path], name);
            return (store, recipe);
        }
        var globalContext = ScopeContext.Global(new Home(Path.Combine(root, "home")));
        var target = new ScopeStore(globalContext); var globals = new RecipeRegistry(target);
        var targetRecipe = globals.Register(["service.yaml"], "shared"); var service = globals.RegisterService(targetRecipe.RecipeId);
        globals.SetDefaultService(service.ServiceId);
        var a = Consumer("coding"); var b = Consumer("news");
        var manager = new PresetPublication(a.Store, target, service.ServiceId);
        var second = new PresetPublication(b.Store, target, service.ServiceId);
        var router = new Router(manager.IniPath);
        PublishedPreset Preset(string name, string model = "fixture.gguf", bool warmup = false) => new(name,
            "[" + name + "]\nmodel=" + model + "\n", ["--model", model], new JsonArray(), warmup);
        async Task<PublicationResult> Publish(PresetPublication manager, RecipeIdentity recipe, PublishedPreset[] presets, bool stopped = false,
            bool interactive = true, Func<string, bool>? confirm = null) => await manager.PublishAsync(recipe.RecipeId, presets,
                "combination", recipe.RegisteredName, stopped, interactive, confirm ?? (_ => true), stopped ? null : router);
        var confirmed = await Publish(manager, a.Recipe, [Preset("coding/chat")]);
        Check(confirmed.Confirmed && router.Reloads == 1, "publish observes exact arguments after one reload");
        await Publish(second, b.Recipe, [Preset("news/summary")]);
        Check(router.Catalog.Count == 2 && File.ReadAllText(manager.IniPath).Contains("coding/chat"), "second consumer combines with first");
        await Publish(manager, a.Recipe, [Preset("coding/other")]);
        Check(router.Catalog.Count == 2 && router.Catalog.Any(p => p!["id"]!.GetValue<string>() == "news/summary") &&
            !router.Catalog.Any(p => p!["id"]!.GetValue<string>() == "coding/chat"), "new combination replaces only current project contribution");
        var reloads = router.Reloads;
        await Publish(manager, a.Recipe, [Preset("coding/other")]);
        Check(router.Reloads == reloads, "identical contribution does not reload or warmup");
        try { await Publish(manager, a.Recipe, [Preset("coding/cancel")], confirm: _ => false); throw new Exception("expected cancellation"); }
        catch (OperationCanceledException) { Check(router.Reloads == reloads && !File.ReadAllText(manager.IniPath).Contains("cancel"), "cancel happens before INI mutation"); }
        try { await Publish(manager, a.Recipe, [], interactive: false); throw new Exception("expected interaction requirement"); }
        catch (StorageException e) when (e.Code == "interaction-required") { Check(router.Reloads == reloads, "headless publication has no mutation"); }
        try { await Publish(manager, a.Recipe, [Preset("news/summary")]); throw new Exception("expected collision"); }
        catch (StorageException e) when (e.Code == "preset-name-conflict") { Check(router.Reloads == reloads, "collision preserves other ownership"); }
        router.LoseResponse = true;
        var uncertain = await Publish(manager, a.Recipe, [Preset("coding/new")]);
        Check(!uncertain.Confirmed && router.Reloads == reloads + 1, "lost response remains uncertain");
        try { await Publish(second, b.Recipe, []); throw new Exception("expected reservation"); }
        catch (StorageException e) when (e.Code == "publication-pending") { Check(router.Reloads == reloads + 1, "pending operation reserves service"); }
        var reconciled = await manager.ReconcileAsync(a.Recipe.RecipeId, router, false);
        Check(reconciled.Confirmed && router.Reloads == reloads + 1, "observation reconciles without resending mutation");
        router.Reject = true;
        try { await Publish(manager, a.Recipe, [Preset("coding/rejected")]); throw new Exception("expected rejection"); }
        catch (StorageException e) when (e.Code == "publication-rejected")
        { Check(router.Catalog.Any(p => p!["id"]!.GetValue<string>() == "coding/new") && router.Catalog.Any(p => p!["id"]!.GetValue<string>() == "news/summary"), "definite rejection restores selected contribution only"); }
        var warmed = await Publish(manager, a.Recipe, [Preset("coding/new", warmup: true)]);
        Check(warmed.Confirmed && !warmed.WarmupFailed && router.Warmups == 1, "explicit warmup succeeds after confirmed publication");
        await Publish(manager, a.Recipe, [Preset("coding/new", warmup: true)]);
        Check(router.Warmups == 1, "unchanged entry is not warmed again");
        router.FailWarmup = true;
        var failed = await Publish(manager, a.Recipe, [Preset("coding/new", "failed.gguf", warmup: true), Preset("coding/good", "good.gguf")]);
        Check(failed.Confirmed && failed.WarmupFailed && router.Warmups == 2 &&
            router.Catalog.Any(p => p!["id"]!.GetValue<string>() == "coding/new") && router.Catalog.Any(p => p!["id"]!.GetValue<string>() == "coding/good"),
            "warmup failure restores failed entry and preserves successful sibling without restart");
        await Publish(manager, a.Recipe, []);
        Check(router.Catalog.Count == 1 && router.Catalog[0]!["id"]!.GetValue<string>() == "news/summary", "down removes only current consumer");
        reloads = router.Reloads;
        await Publish(second, b.Recipe, [], stopped: true);
        Check(router.Reloads == reloads && File.ReadAllText(manager.IniPath) == "version=1\n", "stopped withdrawal updates next composition without HTTP");
        var identity = new RecipeRegistry(a.Store).EnsureProjectConsumer([Path.Combine(a.Store.Context.Base, "other.yaml")], "renamed");
        Check(identity.ConsumerId == a.Recipe.ConsumerId, "combination selection does not create another project consumer");
        var snapshot = a.Store.Read();
        Check(snapshot.Documents["local/recipes.json"].ToJsonString().Contains("last-publication-operation"), "prior operation retained for diagnostics");
        var weights = Path.Combine(a.Store.Context.Base, "fixture.gguf");
        using (var writer = new BinaryWriter(File.Create(weights)))
        { writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(0ul); }
        var resource = new Coquena.Models.ScopedModels(a.Store.Context).AddExternal(weights, "protected");
        var installationId = resource.InstallationId;
        var protectedPreset = Preset("coding/protected") with { Resources = new JsonArray(new JsonObject
            { ["resource-id"] = resource.ResourceId, ["installation-id"] = installationId }) };
        router.Catalog = [];
        router.LoseResponse = true;
        await Publish(manager, a.Recipe, [protectedPreset]);
        try { new Coquena.Models.ScopedModels(a.Store.Context).Remove(resource.ResourceId, false); throw new Exception("expected resource protection"); }
        catch (Coquena.Models.ModelSourceException e) when (e.Code == "resource-in-use") { Check(true, "uncertain operation protects consumer resources without a local process"); }
        await manager.ReconcileAsync(a.Recipe.RecipeId, router, false);
        try { new Coquena.Models.ScopedModels(a.Store.Context).Remove(resource.ResourceId, false); throw new Exception("expected active publication protection"); }
        catch (Coquena.Models.ModelSourceException e) when (e.Code == "resource-in-use") { Check(true, "confirmed global publication protects resources owned by project"); }
        await Publish(manager, a.Recipe, []);
        Check(new Coquena.Models.ScopedModels(a.Store.Context).Remove(resource.ResourceId, false).FreedBytes == 0, "confirmed removal releases publication protection without deleting external files");
        try
        {
            await manager.PublishAsync(a.Recipe.RecipeId, [Preset("coding/prepared")], "fixture", "coding", false, true, _ => true, router,
                fault: stage => { if (stage == PublicationStage.IniPublished) throw new IOException("crash before send"); });
            throw new Exception("expected interruption");
        }
        catch (IOException) { Check(true, "prepared phase survives interruption before sending"); }
        reloads = router.Reloads;
        Check((await manager.ReconcileAsync(a.Recipe.RecipeId, router, false)).Confirmed && router.Reloads == reloads && File.ReadAllText(manager.IniPath) == "version=1\n",
            "prepared unsent candidate restored locally after observing old composition");
        await Publish(manager, a.Recipe, [Preset("coding/rename")]);
        var moved = a.Store.Context.Base + "-moved";
        Directory.Move(a.Store.Context.Base, moved);
        a = (new ScopeStore(ScopeContext.Project(Path.Combine(moved, "coquena.yaml"))), a.Recipe);
        manager = new PresetPublication(a.Store, target, service.ServiceId);
        await Publish(manager, a.Recipe, []);
        Check(router.Catalog.Count == 0, "verified directory rename preserves ownership for down");
        await manager.PublishAsync(a.Recipe.RecipeId, [Preset("new-name/chat")], "new-selection", "new-name", false, true, _ => true, router);
        Check(router.Catalog[0]!["id"]!.GetValue<string>() == "new-name/chat", "new up adopts current name after confirmed withdrawal");
        var beforeConflict = File.ReadAllText(manager.IniPath);
        try
        {
            await manager.PublishAsync(a.Recipe.RecipeId, [Preset("new-name/conflict")], "other", "new-name", false, true,
                _ => { new RecipeRegistry(a.Store).Register(["other.yaml"], "concurrent"); return true; }, router);
            throw new Exception("expected revision conflict");
        }
        catch (StorageException e) when (e.Code == "scope-conflict") { Check(File.ReadAllText(manager.IniPath) == beforeConflict, "revision change during interaction rejects before INI mutation"); }
        await Publish(manager, a.Recipe, [Preset("new-name/chat", "original.gguf"), Preset("new-name/sibling", "old-sibling.gguf")]);
        await Publish(manager, a.Recipe, [Preset("new-name/chat", "changed.gguf"), Preset("new-name/sibling", "successful-sibling.gguf")]);
        await Publish(second, b.Recipe, [Preset("news/later")]);
        router.Catalog.Single(p => p!["id"]!.GetValue<string>() == "new-name/chat")!["status"]!["failed"] = true;
        var late = await manager.RestoreFailedAsync(a.Recipe.RecipeId, router, true, _ => true);
        Check(late?.Confirmed == true && late.WarmupFailed && File.ReadAllText(manager.IniPath).Contains("original.gguf") && File.ReadAllText(manager.IniPath).Contains("successful-sibling.gguf") &&
            router.Catalog.Any(p => p!["id"]!.GetValue<string>() == "news/later"), "late observed load failure restores affected entries against current foreign contributions");
        router.Catalog.Single(p => p!["id"]!.GetValue<string>() == "new-name/chat")!["status"]!["failed"] = true;
        reloads = router.Reloads;
        Check(await manager.RestoreFailedAsync(a.Recipe.RecipeId, router, true, _ => true) is null && router.Reloads == reloads,
            "already restored failing snapshot does not trap explicit correction in a restoration loop");
        router.FailAfterReload = "new-name/chat";
        var immediate = await Publish(manager, a.Recipe, [Preset("new-name/chat", "immediate-failure.gguf"), Preset("new-name/sibling", "accepted-sibling.gguf")]);
        Check(immediate.Confirmed && immediate.WarmupFailed && immediate.Restored && File.ReadAllText(manager.IniPath).Contains("accepted-sibling.gguf") &&
            !File.ReadAllText(manager.IniPath).Contains("immediate-failure.gguf"), "failure observed during reload is restored per entry while preserving successful changes");
        router.Unavailable = true;
        reloads = router.Reloads;
        var inaccessible = await Publish(manager, a.Recipe, []);
        Check(!inaccessible.Confirmed && router.Reloads == reloads + 1, "unreachable live target keeps an uncertain withdrawal after explicit decision");
        router.Unavailable = false;
        Check((await manager.ReconcileAsync(a.Recipe.RecipeId, null, true)).Confirmed && router.Reloads == reloads + 1,
            "confirmed termination permits local withdrawal reconciliation without HTTP retry");
        Check(!PresetPublication.Matches(new JsonArray(new JsonObject { ["presets"] = new JsonArray(new JsonObject
            { ["name"] = "test", ["expected-arguments"] = new JsonArray("--model", "new") }) }),
            new JsonArray(new JsonObject { ["id"] = "test", ["status"] = new JsonObject { ["args"] = new JsonArray("--model", "old") } })),
            "matching name alone never certifies a changed definition");
        var copy = Path.Combine(root, "copy"); Directory.CreateDirectory(copy);
        foreach (var file in Directory.EnumerateFiles(a.Store.Context.Base, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(copy, Path.GetRelativePath(a.Store.Context.Base, file)); Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(file, to);
        }
        var copied = new ScopeStore(ScopeContext.Project(Path.Combine(copy, "coquena.yaml")));
        Check(new RecipeRegistry(copied).Recipes().Length == 0 && copied.Read().ScopeId != a.Store.Read().ScopeId, "copied metadata cannot withdraw original contribution");
        var initialContext = ScopeContext.Global(new Home(Path.Combine(root, "initial-global")));
        var resolutions = 0;
        Task<BackendCandidate?> Select()
        {
            resolutions++;
            return Task.FromResult<BackendCandidate?>(new("b11899", "cpu", "win-x64", "compatible", "fixture", [], true));
        }
        await PublicationCli.EnsureGlobalAsync(initialContext, TextWriter.Null, Select);
        var initialRegistry = new RecipeRegistry(new ScopeStore(initialContext));
        var selectedId = initialRegistry.ResolveGlobalService("default");
        Check(initialRegistry.Services().Length == 1 && File.ReadAllText(initialContext.PathFor($"services/{selectedId}/recipe.yaml")).Contains("variant: cpu"), "first global provisioning persists selected variant and default");
        await PublicationCli.EnsureGlobalAsync(initialContext, TextWriter.Null, Select);
        Check(resolutions == 1 && initialRegistry.ResolveGlobalService("default") == selectedId, "existing default is reused without rediscovery or replacement");
        var unselectedContext = ScopeContext.Global(new Home(Path.Combine(root, "unselected-global")));
        var unselected = new RecipeRegistry(new ScopeStore(unselectedContext));
        var unselectedRecipe = unselected.Register(["recipe.yaml"], "existing"); unselected.RegisterService(unselectedRecipe.RecipeId);
        try { await PublicationCli.EnsureGlobalAsync(unselectedContext, TextWriter.Null, Select); throw new Exception("expected missing default"); }
        catch (StorageException e) when (e.Code == "default-required") { Check(resolutions == 1 && unselected.Services().Length == 1, "existing globals without default require explicit designation instead of creating another"); }
        using var cliOutput = new StringWriter(); using var cliError = new StringWriter();
        Check(Cli.Run(["up", "-d"], cliOutput, cliError) == 2 && Cli.Run(["preset", "apply"], cliOutput, cliError) == 2, "publication syntax rejects detached and missing selector");
        if (Console.IsInputRedirected)
            Check(Cli.Run(["up", "--base", a.Store.Context.Base], cliOutput, cliError) == 3, "actual CLI refuses publication without available interaction");
        var invalidFolder = Path.Combine(root, "invalid-recipe"); Directory.CreateDirectory(invalidFolder);
        File.WriteAllText(Path.Combine(invalidFolder, "coquena.yaml"), "presets:\n  p:\n    args:\n      model: model://absent\n");
        var priorHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
        var invalidHome = Path.Combine(root, "invalid-home"); Environment.SetEnvironmentVariable("COQUENA_HOME", invalidHome);
        try
        {
            Check(PublicationCli.Run(["up", "--start-service", "--base", invalidFolder], cliOutput, cliError, new Decisions()) == 2 &&
                !Directory.Exists(invalidHome) && !Directory.Exists(Path.Combine(invalidFolder, ".coquena")), "known invalid references fail before registering or provisioning a global server");
        }
        finally { Environment.SetEnvironmentVariable("COQUENA_HOME", priorHome); }
        using var activityHandler = new ActivityHandler(); using var activityHttp = new HttpClient(activityHandler);
        var activityRouter = new LlamaPresetRouter(activityHttp, new Uri("http://fixture.invalid"));
        var activity = await activityRouter.ActivityAsync(["coding/chat"], default);
        Check(activity.Contains("activas") && activity.Contains("coding/chat") && activityHandler.Requests.Any(p => p.Contains("autoload=false")) &&
            activityHandler.Requests.All(p => !p.Contains("reload") && !p.Contains("/load")), "busy activity attribution uses only observational endpoints with autoload disabled");
        activityHandler.MissingSlots = true;
        Check((await activityRouter.ActivityAsync(["coding/chat"], default)).Contains("desconocida"), "missing activity evidence stays unknown instead of inventing idle state");
        Console.WriteLine($"OK: {checks} verificaciones de publicación 055. Fixtures: {root}");
    }

    private sealed class ActivityHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool MissingSlots;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (request.Method != HttpMethod.Get) throw new Exception("Observación mutante.");
            var uri = request.RequestUri!; Requests.Add(uri.PathAndQuery);
            var slots = uri.AbsolutePath == "/slots";
            var response = new HttpResponseMessage(slots && MissingSlots ? System.Net.HttpStatusCode.NotImplemented : System.Net.HttpStatusCode.OK)
            { Content = new StringContent(slots ? "[{\"is_processing\":true}]" : "{\"data\":[{\"id\":\"coding/chat\",\"status\":{\"value\":\"loaded\",\"args\":[]}}]}") };
            return Task.FromResult(response);
        }
    }

    private sealed class Router(string ini) : IPresetRouter
    {
        public JsonArray Catalog = [];
        public int Reloads, Warmups;
        public bool LoseResponse, Reject, FailWarmup, Unavailable;
        public string? FailAfterReload;
        public Task<JsonArray> ObserveAsync(CancellationToken cancellation) => Unavailable ? throw new HttpRequestException("offline") : Task.FromResult(Catalog.DeepClone().AsArray());
        public Task<string> ActivityAsync(string[] names, CancellationToken cancellation) => Task.FromResult("Actividad desconocida.");
        public Task<bool> ReloadAsync(CancellationToken cancellation)
        {
            Reloads++;
            if (Unavailable) throw new HttpRequestException("offline");
            if (Reject) { Reject = false; return Task.FromResult(false); }
            Catalog = [];
            JsonObject? current = null;
            foreach (var line in File.ReadAllLines(ini))
            {
                if (line.StartsWith('['))
                {
                    current = new JsonObject { ["id"] = line[1..^1], ["status"] = new JsonObject { ["args"] = new JsonArray() } };
                    Catalog.Add((JsonNode)current);
                }
                else if (current is not null && line.Contains('='))
                {
                    var pair = line.Split('=', 2); var args = current["status"]!["args"]!.AsArray();
                    args.Add((JsonNode)JsonValue.Create("--" + pair[0])!); args.Add((JsonNode)JsonValue.Create(pair[1])!);
                }
            }
            if (FailAfterReload is string failedName)
            {
                Catalog.Single(p => p!["id"]!.GetValue<string>() == failedName)!["status"]!["failed"] = true;
                FailAfterReload = null;
            }
            if (LoseResponse) { LoseResponse = false; throw new HttpRequestException("response lost"); }
            return Task.FromResult(true);
        }
        public Task<bool> WarmupAsync(string name, CancellationToken cancellation) { Warmups++; return Task.FromResult(!FailWarmup); }
    }
}
