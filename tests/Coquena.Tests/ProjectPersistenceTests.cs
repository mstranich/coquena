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
        Require(registry.ResolveOwnService(recipe.RecipeId).ServiceId == service.ServiceId, "self resuelve identidad registrada");
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
        var serviceDocuments = globalStore.Read();
        var serviceNext = RecipeRegistryForTests.Next(serviceDocuments);
        var activeA = $"services/{globalService.ServiceId}/local/active.json";
        var activeB = $"services/{Guid.NewGuid():D}/local/active.json";
        serviceNext[activeA] = ProjectDocuments.Create("active", 1, new JsonObject { ["instance-id"] = Guid.NewGuid().ToString("D") });
        serviceNext[activeB] = ProjectDocuments.Create("active", 1, new JsonObject { ["instance-id"] = Guid.NewGuid().ToString("D") });
        globalStore.Commit(serviceDocuments.Revision, serviceNext);
        Require(globalStore.Read().ActiveDocuments().Count() == 2, "instancias globales independientes en una generación");
        serviceDocuments = globalStore.Read(); serviceNext = RecipeRegistryForTests.Next(serviceDocuments);
        serviceNext[activeA]["data"]!["terminated"] = true;
        globalStore.Commit(serviceDocuments.Revision, serviceNext);
        Require(globalStore.Read().ActiveDocuments().Count() == 1, "terminación no retira la otra instancia");
        Require(global.ResolveGlobalService("default") == globalService.ServiceId && global.ResolveGlobalService("global") == globalService.ServiceId, "alias global por identidad");
        global.RemoveStoppedService(globalService.ServiceId);
        Fail(() => global.ResolveGlobalService("default"), "unknown-service");
        Require(global.Services().Length == 0, "baja retira servicio y alias sin reemplazo");
        globalService = global.RegisterService(globalRecipe.RecipeId);
        global.SetDefaultService(globalService.ServiceId);
        var otherGlobalRecipe = global.Register(["other.yaml"], "otro");
        var otherGlobalService = global.RegisterService(otherGlobalRecipe.RecipeId);
        var uncertainRecipe = global.Register(["uncertain.yaml"], "incierto");
        var uncertainService = global.RegisterService(uncertainRecipe.RecipeId);
        using (var current = Process.GetCurrentProcess())
        {
            global.PublishActive(uncertainService.ServiceId, new JsonObject
            {
                ["service-id"] = uncertainService.ServiceId, ["instance-id"] = Guid.NewGuid().ToString("D"),
                ["process"] = new JsonObject { ["pid"] = current.Id, ["creation-time"] = current.StartTime.ToUniversalTime().ToFileTimeUtc(), ["executable"] = Environment.ProcessPath },
                ["resources"] = new JsonArray(), ["snapshot"] = new JsonObject(), ["endpoint"] = new JsonObject(), ["log"] = "fixture.log", ["combination"] = "fixture"
            });
            try { new Coquena.Servers.ScopedServiceProcess(globalStore).Reconcile(uncertainService.ServiceId); throw new Exception("expected uncertain process identity"); }
            catch (Coquena.Configuration.ConfigurationException) { Require(!current.HasExited, "proceso sin job verificable permanece vivo"); }
            Fail(() => global.RemoveStoppedService(uncertainService.ServiceId), "service-in-use");
        }
        var liveId = Guid.NewGuid().ToString("D");
        var live = new JsonObject { ["service-id"] = otherGlobalService.ServiceId, ["instance-id"] = liveId,
            ["process"] = new JsonObject { ["pid"] = int.MaxValue, ["creation-time"] = 1L, ["executable"] = Path.GetFullPath("fixture.exe") },
            ["resources"] = new JsonArray(), ["snapshot"] = new JsonObject(), ["endpoint"] = new JsonObject(),
            ["log"] = "fixture.log", ["combination"] = "fixture" };
        global.PublishActive(otherGlobalService.ServiceId, live);
        Fail(() => global.PublishActive(otherGlobalService.ServiceId, live), "instance-already-registered");
        Fail(() => global.RemoveStoppedService(otherGlobalService.ServiceId), "service-in-use");
        Fail(() => global.RecordTermination(otherGlobalService.ServiceId, Guid.NewGuid().ToString("D")), "instance-conflict");
        Require(!new Coquena.Servers.ScopedServiceProcess(globalStore).Reconcile(otherGlobalService.ServiceId), "PID ausente permite reconciliar terminación");
        var apphost = Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe");
        var childId = Guid.NewGuid();
        using (var child = Coquena.Servers.WindowsServerProcess.Create(apphost, ["--runtime-child"], Path.Combine(root, "service-child.log"), childId))
        {
            var real = new JsonObject { ["service-id"] = otherGlobalService.ServiceId, ["instance-id"] = childId.ToString("D"),
                ["process"] = new JsonObject { ["pid"] = child.Pid, ["creation-time"] = child.Created, ["executable"] = child.Executable },
                ["resources"] = new JsonArray(), ["snapshot"] = new JsonObject(), ["endpoint"] = new JsonObject(),
                ["log"] = Path.Combine(root, "service-child.log"), ["combination"] = "fixture" };
            global.PublishActive(otherGlobalService.ServiceId, real);
            child.Activate();
            var lifecycle = new Coquena.Servers.ScopedServiceProcess(globalStore);
            Require(lifecycle.Reconcile(otherGlobalService.ServiceId), "instancia real viva conserva registro");
            Require(lifecycle.Stop(otherGlobalService.ServiceId, () => true), "parada verifica y termina grupo real");
            Require(!lifecycle.Reconcile(otherGlobalService.ServiceId), "terminación real queda registrada");
        }
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); var healthPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var lifecycleWithHealth = new Coquena.Servers.ScopedServiceProcess(globalStore);
        var plan = new Coquena.Servers.ScopedStartPlan(apphost, ["--scoped-health-child", healthPort.ToString()],
            Path.Combine(root, "service-health.log"), "fixture", new JsonObject { ["version"] = 1 }, new JsonArray(),
            new JsonObject { ["host"] = "127.0.0.1", ["port"] = healthPort }, new Uri($"http://127.0.0.1:{healthPort}/health"),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
        var healthyId = lifecycleWithHealth.StartAsync(otherGlobalService.ServiceId, plan).GetAwaiter().GetResult();
        Fail(() => lifecycleWithHealth.StartAsync(otherGlobalService.ServiceId, plan).GetAwaiter().GetResult(), "service-already-running");
        Require(globalStore.Read().Documents[$"services/{otherGlobalService.ServiceId}/local/last-good.json"]["data"]!["health"]!["status"]!.GetValue<int>() == 200, "salud real promueve snapshot");
        Require(globalStore.Read().Documents[$"services/{otherGlobalService.ServiceId}/local/last-good.json"]["data"]!["execution"]!["arguments"]!.AsArray().Count == 2,
            "última buena conserva argumentos efectivos independientemente del YAML");
        Fail(() => lifecycleWithHealth.RestartAsync(otherGlobalService.ServiceId,
            _ => Task.FromResult(plan with { Executable = Path.Combine(root, "missing.exe") }), () => true).GetAwaiter().GetResult(), "backend-missing");
        Require(lifecycleWithHealth.Reconcile(otherGlobalService.ServiceId), "reinicio conserva instancia si preparación falla");
        Fail(() => lifecycleWithHealth.RestartAsync(otherGlobalService.ServiceId,
            _ => Task.FromResult(plan with { ValidateIntent = () => throw new StorageException("intention-conflict", "fixture") }),
            () => true).GetAwaiter().GetResult(), "intention-conflict");
        Require(lifecycleWithHealth.Reconcile(otherGlobalService.ServiceId), "cambio de intención detectado antes de detener");
        var restartedId = lifecycleWithHealth.RestartAsync(otherGlobalService.ServiceId,
            _ => Task.FromResult(plan with { LogPath = Path.Combine(root, "service-health-restart.log") }), () => true).GetAwaiter().GetResult();
        Require(restartedId != healthyId && lifecycleWithHealth.Reconcile(otherGlobalService.ServiceId), "reinicio reemplaza instancia después de preparar");
        var unrelatedRecipe = global.Register(["unrelated.yaml"], "sin-escucha");
        var unrelatedService = global.RegisterService(unrelatedRecipe.RecipeId);
        var vacant = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); vacant.Start();
        var vacantPort = ((System.Net.IPEndPoint)vacant.LocalEndpoint).Port; vacant.Stop();
        var wrongHealth = plan with { Arguments = ["--runtime-child"], LogPath = Path.Combine(root, "wrong-health.log"),
            Endpoint = new JsonObject { ["host"] = "127.0.0.1", ["port"] = vacantPort }, Timeout = TimeSpan.FromSeconds(2) };
        try { lifecycleWithHealth.StartAsync(unrelatedService.ServiceId, wrongHealth).GetAwaiter().GetResult(); throw new Exception("expected unowned listener"); }
        catch (StorageException e) { Require(e.Code == "health-timeout" && e.Message.Contains("escucha"), "salud ajena no acredita instancia"); }
        Require(!globalStore.Read().Documents.ContainsKey($"services/{unrelatedService.ServiceId}/local/last-good.json"), "salud ajena no promueve última buena");
        lifecycleWithHealth.Stop(unrelatedService.ServiceId, () => true);
        global.RemoveStoppedService(unrelatedService.ServiceId);
        var lockPath = globalStore.Context.PathFor($"local/runtime/{otherGlobalService.ServiceId}/lifecycle.lock");
        using (var busy = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Fail(() => lifecycleWithHealth.Reconcile(otherGlobalService.ServiceId), "service-busy");
        Require(lifecycleWithHealth.Stop(otherGlobalService.ServiceId, () => true), "parada de servicio sano");
        var goodBeforeFailure = globalStore.Read().Documents[$"services/{otherGlobalService.ServiceId}/local/last-good.json"]["data"]!.ToJsonString();
        var unhealthy = plan with { Arguments = ["--runtime-child"], LogPath = Path.Combine(root, "unhealthy.log"), Timeout = TimeSpan.FromSeconds(1) };
        Fail(() => lifecycleWithHealth.StartAsync(otherGlobalService.ServiceId, unhealthy).GetAwaiter().GetResult(), "health-timeout");
        Require(lifecycleWithHealth.Reconcile(otherGlobalService.ServiceId), "API inaccesible no prueba terminación");
        Require(globalStore.Read().Documents[$"services/{otherGlobalService.ServiceId}/local/last-good.json"]["data"]!.ToJsonString() == goodBeforeFailure,
            "salud no confirmada no promueve última buena");
        Require(lifecycleWithHealth.Stop(otherGlobalService.ServiceId, () => true), "proceso sin salud permanece controlable");
        var unready = plan with { Arguments = ["--scoped-health-child", "--port", healthPort.ToString(), "--health-unready"],
            LogPath = Path.Combine(root, "unready.log"), Timeout = TimeSpan.FromSeconds(1) };
        try { lifecycleWithHealth.StartAsync(otherGlobalService.ServiceId, unready).GetAwaiter().GetResult(); throw new Exception("expected unready"); }
        catch (StorageException e) { Require(e.Code == "health-timeout" && e.Message.Contains("HTTP 503"), "503 conserva diagnóstico de salud"); }
        Require(globalStore.Read().Documents[$"services/{otherGlobalService.ServiceId}/local/last-good.json"]["data"]!.ToJsonString() == goodBeforeFailure,
            "503 no promueve última buena");
        Require(lifecycleWithHealth.Stop(otherGlobalService.ServiceId, () => true), "detener instancia no lista");
        listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, healthPort); listener.Start();
        try
        {
            try { lifecycleWithHealth.StartAsync(otherGlobalService.ServiceId, plan).GetAwaiter().GetResult(); throw new Exception("expected occupied port"); }
            catch (Coquena.Configuration.ConfigurationException) { Require(true, "puerto ocupado no se reasigna"); }
        }
        finally { listener.Stop(); }
        Require(!lifecycleWithHealth.Reconcile(otherGlobalService.ServiceId), "puerto ocupado no crea instancia");
        var protectedLog = Path.Combine(root, "protected.log"); File.WriteAllText(protectedLog, "evidencia anterior");
        try { lifecycleWithHealth.StartAsync(otherGlobalService.ServiceId, plan with { LogPath = protectedLog }).GetAwaiter().GetResult(); throw new Exception("expected existing log conflict"); }
        catch (Coquena.Configuration.ConfigurationException e) { Require(File.ReadAllText(protectedLog) == "evidencia anterior" && !e.Message.Contains("La operación se completó correctamente"), "inicio conserva el log y el error Win32 original"); }
        Require(!lifecycleWithHealth.Reconcile(otherGlobalService.ServiceId), "colisión de log no crea instancia");
        global.RemoveStoppedService(otherGlobalService.ServiceId);
        var cliFolder = Path.Combine(root, "cli-project"); Directory.CreateDirectory(cliFolder);
        var cliHome = Path.Combine(root, "cli-home"); Directory.CreateDirectory(cliHome);
        listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); var cliPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        File.WriteAllText(Path.Combine(cliFolder, "coquena.yaml"),
            $"name: servicio\nservice:\n  backend:\n    executable: '{apphost.Replace('\\', '/')}'\n  server:\n    health:\n      url: http://127.0.0.1:{cliPort}/health\n      timeout-seconds: 10\n    args:\n      port: {cliPort}\n      scoped-health-child: true\nmodels:\n  missing:\n    files: [./absent.gguf]\npresets:\n  unused:\n    args:\n      model: model://missing\n");
        var previousHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
        Environment.SetEnvironmentVariable("COQUENA_HOME", cliHome);
        try
        {
            int Cli(params string[] arguments)
            {
                using var output = new StringWriter(); using var error = new StringWriter();
                var code = Coquena.Cli.Run(arguments, output, error);
                if (code != 0) throw new Exception($"CLI {string.Join(' ', arguments)}: {error}");
                return code;
            }
            Cli("--project-directory", cliFolder, "--private", "service", "self", "start", "-d");
            Require(new ProjectIndex(new Home(cliHome)).Locations().Contains(cliFolder), "apertura de servicio actualiza ubicación conocida");
            var recipeFile = Path.Combine(cliFolder, "coquena.yaml");
            var originalRecipe = File.ReadAllText(recipeFile);
            File.WriteAllText(recipeFile, "invalid: [");
            Cli("service", "self", "start", "-d", "--project-directory", cliFolder);
            File.WriteAllText(recipeFile, originalRecipe.Replace(apphost.Replace('\\', '/'), Path.Combine(root, "missing.exe").Replace('\\', '/')));
            using (var failedOutput = new StringWriter()) using (var failedError = new StringWriter())
                Require(Coquena.Cli.Run(["service", "self", "restart", "-d", "--private", "--project-directory", cliFolder], failedOutput, failedError) != 0, "restart CLI falla al preparar backend ausente");
            var cliStore = new ScopeStore(ScopeContext.Project(recipeFile));
            var own = new RecipeRegistry(cliStore).FindProjectService([recipeFile]);
            Require(new Coquena.Servers.ScopedServiceProcess(cliStore).Reconcile(own.ServiceId), "fallo CLI conserva instancia anterior");
            File.WriteAllText(recipeFile, originalRecipe);
            Cli("service", "self", "restart", "-d", "--private", "--project-directory", cliFolder);
            Cli("server", "self", "stop", "--project-directory", cliFolder);
            Cli("service", "self", "remove", "--project-directory", cliFolder);
            var cliGlobalContext = ScopeContext.Global(new Home(cliHome));
            var cliGlobal = new RecipeRegistry(new ScopeStore(cliGlobalContext));
            var cliGlobalRecipe = cliGlobal.Register(["global.yaml"], "global-cli");
            var globalEntry = cliGlobal.RegisterService(cliGlobalRecipe.RecipeId);
            var reservedRecipe = cliGlobal.Register(["reserved.yaml"], "start");
            cliGlobal.RegisterService(reservedRecipe.RecipeId);
            using (var selectionOutput = new StringWriter()) using (var selectionError = new StringWriter())
            {
                Require(Coquena.Cli.Run(["service", "start", "remove"], selectionOutput, selectionError) == 2,
                    "verbo reservado no se interpreta como nombre posicional");
                Require(Coquena.Cli.Run(["service", "stop"], selectionOutput, selectionError) == 2 && selectionError.ToString().Contains("unknown-service"),
                    "default ausente no selecciona sustituto");
            }
            Cli("service", "--service", "start", "remove");
            var globalYaml = cliGlobalContext.PathFor($"services/{globalEntry.ServiceId}/recipe.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(globalYaml)!);
            File.WriteAllText(globalYaml, originalRecipe.Replace("name: servicio", "name: global-cli"));
            Cli("service", "default", "set", "global-cli");
            Cli("service", "start", "-d");
            Cli("server", "default", "stop");
            Cli("service", "default", "remove");
            Fail(() => cliGlobal.ResolveGlobalService("default"), "unknown-service");
        }
        finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previousHome); }
        Require(global.ResolveGlobalService("default") == globalService.ServiceId, "baja de otro servicio conserva alias");
        Fail(() => registry.SetDefaultService(service.ServiceId), "global-alias-required");
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
        var associated = copied.EnsureProjectService(["associated.yaml", "override.yaml"], "asociado");
        var associatedAgain = copied.EnsureProjectService(["associated.yaml", "other.yaml"], "nombre-cambiado");
        Require(associated.ServiceId == associatedAgain.ServiceId && associatedAgain.Name == "asociado", "asociación explícita conserva servicio y nombre entre overrides");
        copied.RemoveStoppedService(associated.ServiceId);
        var reassociated = copied.EnsureProjectService(["associated.yaml"], "nuevo");
        Require(reassociated.ServiceId != associated.ServiceId && reassociated.Name == "nuevo", "alta posterior a baja adopta nombre vigente");
        JsonObject ProjectActive(ServiceIdentity target) => new()
        {
            ["service-id"] = target.ServiceId, ["instance-id"] = Guid.NewGuid().ToString("D"),
            ["process"] = new JsonObject { ["pid"] = int.MaxValue, ["creation-time"] = 1L, ["executable"] = Path.GetFullPath("fixture.exe") },
            ["resources"] = new JsonArray(), ["snapshot"] = new JsonObject(), ["endpoint"] = new JsonObject(),
            ["log"] = "fixture.log", ["combination"] = "fixture"
        };
        var firstProjectActive = ProjectActive(copiedService); var secondProjectActive = ProjectActive(reassociated);
        copied.PublishActive(copiedService.ServiceId, firstProjectActive);
        copied.PublishActive(reassociated.ServiceId, secondProjectActive);
        Require(copiedStore.Read().ActiveDocuments().Count() == 2, "dos servicios propios conservan instancias independientes");
        copied.RecordTermination(copiedService.ServiceId, firstProjectActive["instance-id"]!.GetValue<string>());
        Require(copiedStore.Read().ActiveDocuments().Count() == 1, "detener un servicio propio preserva el otro");
        copied.RecordTermination(reassociated.ServiceId, secondProjectActive["instance-id"]!.GetValue<string>());
        Require(copied.RegisterResource("principal", "model", "external").ResourceId != resource.ResourceId, "recursos aislados");
        Fail(() => copied.RegisterResource("principal", "model", "managed"), "resource-ownership-conflict");
        var anotherRecipe = copied.Register(["coquena.yaml"], "otra");
        Fail(() => copied.ResolveOwnService(anotherRecipe.RecipeId), "unknown-service");
        Require(anotherRecipe.RecipeId != copiedRecipe.RecipeId && anotherRecipe.ConsumerId != copiedRecipe.ConsumerId,
            "archivos iguales no imponen identidad; el alta es explícita");
        Require(copied.Get(copiedRecipe.RecipeId).RegisteredName == "original", "identidad independiente de selección y nombre nuevo");

        registry.RecordContribution(recipe.RecipeId, copiedStore, copiedService.ServiceId, ["original/resumen"], [resource.ResourceId]);
        Fail(() => copied.RemoveStoppedService(copiedService.ServiceId), "service-in-use");
        var contribution = copiedStore.Read().Documents["local/recipes.json"]["data"]!["recipes"]![0]!["entities"]![0]!["consumers"]![0]!;
        Require(contribution["consumer-id"]!.GetValue<string>() == recipe.ConsumerId, "referencia cruzada por identidad");
        var thirdFolder = Path.Combine(root, "third"); Directory.CreateDirectory(thirdFolder);
        var third = new RecipeRegistry(Open(thirdFolder)); var thirdRecipe = third.Register(["coquena.yaml"], "original");
        Fail(() => third.RecordContribution(thirdRecipe.RecipeId, copiedStore, copiedService.ServiceId, ["original/resumen"], []), "preset-name-conflict");
        var instanceId = Guid.NewGuid().ToString("D");
        registry.RegisterInstance(service.ServiceId, instanceId, new JsonObject { ["pid"] = 123,
            ["started-at"] = "2026-10-22T12:00:00Z", ["executable"] = new JsonObject { ["file-id"] = "fixture" } });
        Fail(() => new Coquena.Servers.ScopedServiceProcess(store).Reconcile(service.ServiceId), "instance-conflict");
        Fail(() => registry.RemoveStoppedService(service.ServiceId), "service-in-use");
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
