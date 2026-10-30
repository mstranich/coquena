using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;
using Coquena.Configuration;
using Coquena.Contracts;

namespace Coquena;

public interface IPublicationInteraction
{
    bool Available { get; }
    bool Confirm(string diagnostic);
    bool StartService();
}

public static class PublicationCli
{
    private sealed class ConsoleInteraction(TextWriter output) : IPublicationInteraction
    {
        public bool Available => !Console.IsInputRedirected;
        public bool Confirm(string diagnostic)
        {
            output.WriteLine(diagnostic); output.WriteLine("[a] Aplicar ahora aceptando el riesgo, [c] Cancelar:");
            return Console.ReadLine()?.Trim().Equals("a", StringComparison.OrdinalIgnoreCase) == true;
        }
        public bool StartService()
        {
            output.WriteLine("Destino detenido. ¿Iniciar servicio? [s] Sí, [c] Cancelar:");
            return Console.ReadLine()?.Trim().Equals("s", StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    public static int Run(string[] args, TextWriter output, TextWriter error, IPublicationInteraction? interaction = null)
    {
        try { return RunAsync(args, output, error, interaction ?? new ConsoleInteraction(output)).GetAwaiter().GetResult(); }
        catch (Exception e) when (e is StorageException or RecipeException or BackendException or DownloadException or
            ModelSourceException or ContractException or ConfigurationException or System.ComponentModel.Win32Exception or
            ArgumentException or IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException)
        {
            var code = e switch { StorageException s => s.Code, RecipeException r => r.Code, BackendException b => b.Code,
                ModelSourceException m => m.Code, DownloadException d => d.Code, ContractException c => c.Code,
                ConfigurationException => "instance-identity-uncertain", OperationCanceledException => "cancelled", _ => "publication-error" };
            error.WriteLine($"Error: {code}. {e.Message}");
            if (e is ArgumentException) error.WriteLine("Consulte coquena --help para ver la sintaxis.");
            if (e is OperationCanceledException) return 130;
            if (code == "unsupported-argument-destination") return 5;
            if (e is ArgumentException or RecipeException || code is "unknown-preset" or "unknown-service" or "self-requires-service") return 2;
            if (code is "interaction-required" or "publication-pending" or "preset-name-conflict" or "remote-composition-conflict" or "default-required" || code.Contains("conflict", StringComparison.Ordinal)) return 3;
            if (e is ConfigurationException) return 3;
            if (code.Contains("integrity", StringComparison.Ordinal)) return 6;
            if (code is "publication-unsupported" or "publication-snapshot-required") return 5;
            if (e is System.ComponentModel.Win32Exception || code is "service-stopped" or "publication-rejected") return 8;
            if (e is HttpRequestException) return 4;
            return 7;
        }
    }

    private static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, IPublicationInteraction interaction, JsonObject? recordedTarget = null)
    {
        var files = new List<string>(); var env = new List<string>(); var positional = new List<string>();
        var seen = new HashSet<string>(); string? basePath = null, selector = null; bool start = false, isPrivate = false, end = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i] switch { "-f" => "--file", "--base" => "--project-directory", _ => args[i] };
            if (end) { positional.Add(arg); continue; }
            if (arg == "--") { end = true; continue; }
            if (arg is "--file" or "--env-file" or "--project-directory" or "--service")
            {
                if (++i == args.Length || arg is not ("--file" or "--env-file") && !seen.Add(arg)) throw new ArgumentException("Opción inválida o duplicada.");
                if (arg == "--file") files.Add(args[i]); else if (arg == "--env-file") env.Add(args[i]);
                else if (arg == "--service") selector = args[i]; else basePath = args[i];
            }
            else if (arg is "--start-service" or "--private")
            {
                if (!seen.Add(arg)) throw new ArgumentException("Opción duplicada.");
                if (arg == "--start-service") start = true; else isPrivate = true;
            }
            else if (arg.StartsWith('-')) throw new ArgumentException("Opción de publicación desconocida.");
            else positional.Add(arg);
        }
        if (positional.FirstOrDefault() == "preset") positional.RemoveAt(0);
        if (positional.Count == 0) throw new ArgumentException("Falta operación.");
        var action = positional[0]; string? key = positional.Count == 2 ? positional[1] : null;
        if (action is not ("up" or "down" or "apply" or "withdraw") ||
            (action is "up" or "down" ? positional.Count != 1 : positional.Count != 2) || start && action != "up")
            throw new ArgumentException("Sintaxis de publicación inválida.");
        if (!interaction.Available) throw new StorageException("interaction-required", "Up/down y apply/withdraw requieren interacción.");
        var withdrawing = action is "down" or "withdraw";
        var selection = new RecipeSelection(Environment.CurrentDirectory, files, basePath, env);
        var context = ScopeContext.Project(selection.Files[0], selection.Base); var store = new ScopeStore(context);
        var registry = new RecipeRegistry(store);
        EffectiveRecipe? recipe = withdrawing ? null : RecipeReader.Read(selection);
        if (recipe is not null)
        {
            _ = ArgumentValidation.Validate(recipe);
            _ = ModelIntent.FromRecipe(recipe);
        }
        RecipeIdentity identity;
        if (withdrawing)
        {
            var found = store.Read().Documents.GetValueOrDefault("local/recipes.json")?["data"]?["recipes"]?.AsArray()
                .SingleOrDefault(r => r!["project-consumer"]?.GetValue<bool>() == true);
            if (found is null) { output.WriteLine("Sin publicaciones propias registradas."); return 0; }
            identity = registry.Get(found["recipe-id"]!.GetValue<string>());
        }
        else identity = registry.EnsureProjectConsumer(selection.Files, recipe!.Root.Get("name")?.Text);
        var entry = store.Read().Documents["local/recipes.json"]["data"]!["recipes"]!.AsArray()
            .Single(r => r!["recipe-id"]!.GetValue<string>() == identity.RecipeId)!;
        var publication = entry["publication-operation"] ?? recordedTarget ?? entry["publication"];
        if (withdrawing && publication is null) { output.WriteLine("Sin publicaciones propias registradas."); return 0; }
        if (withdrawing && entry["publication-operation"] is null && recordedTarget is null && selector is null && entry["publications"] is JsonArray all)
        {
            var remaining = all.Where(p => p!["presets"]!.AsArray().Count != 0).ToArray();
            if (remaining.Length > 1)
            {
                var exit = 0;
                foreach (var previous in remaining)
                {
                    var previousResult = await RunAsync(args, output, error, interaction, previous!.DeepClone().AsObject());
                    if (previousResult == 9) exit = 9; else if (exit == 0) exit = previousResult;
                }
                return exit;
            }
            if (remaining.Length == 1) publication = remaining[0];
        }
        ScopeContext targetContext; ServiceIdentity service;
        var home = Home.Resolve();
        new ProjectIndex(home).Refresh(store);
        if (withdrawing && selector is not null && entry["publication-operation"] is null && entry["publications"] is JsonArray savedPublications)
        {
            var selectedId = selector == "self" ? registry.FindProjectService(selection.Files).ServiceId :
                new RecipeRegistry(new ScopeStore(ScopeContext.Global(home))).ResolveGlobalService(selector == "global" ? "default" : selector);
            publication = savedPublications.SingleOrDefault(p => p!["service-id"]!.GetValue<string>() == selectedId)
                ?? throw new StorageException("publication-conflict", "No existe una publicación propia en el destino seleccionado.");
        }
        if ((withdrawing || entry["publication-operation"] is not null) && publication is not null)
        {
            targetContext = publication["global"]!.GetValue<bool>() ? ScopeContext.Global(new Home(publication["target-base"]!.GetValue<string>()))
                : ScopeContext.Project(Path.Combine(publication["target-base"]!.GetValue<string>(), "coquena.yaml"));
            var targetRegistry = new RecipeRegistry(new ScopeStore(targetContext));
            service = targetRegistry.Services().SingleOrDefault(s => s.ServiceId == publication["service-id"]!.GetValue<string>())
                ?? throw new StorageException("publication-conflict", "El destino registrado ya no existe.");
            if (selector is not null)
            {
                var explicitId = selector == "self" ? registry.FindProjectService(selection.Files).ServiceId :
                    new RecipeRegistry(new ScopeStore(ScopeContext.Global(home))).ResolveGlobalService(selector == "global" ? "default" : selector);
                if (explicitId != service.ServiceId) throw new StorageException("publication-conflict", "La selección no coincide con el destino publicado.");
            }
        }
        else
        {
            var chosen = selector ?? recipe?.Target ?? "global";
            if (chosen == "self")
            {
                if (recipe?.Root.Get("service") is null) throw new StorageException("self-requires-service", "Self requiere un servicio propio en la receta.");
                targetContext = context;
                service = registry.EnsureProjectService(selection.Files, recipe?.Root.Get("name")?.Text);
            }
            else
            {
                targetContext = ScopeContext.Global(home); var globals = new RecipeRegistry(new ScopeStore(targetContext));
                if (chosen is "global" or "default") await EnsureGlobalAsync(targetContext, output);
                var id = globals.ResolveGlobalService(chosen == "global" ? "default" : chosen);
                service = globals.Services().Single(s => s.ServiceId == id);
            }
        }
        var targetStore = new ScopeStore(targetContext);
        var targetScopeId = targetStore.Read().ScopeId;
        if (entry["publication-operation"] is null)
            publication = (entry["publications"] as JsonArray)?.SingleOrDefault(p =>
                p!["service-id"]!.GetValue<string>() == service.ServiceId &&
                (p["target-scope-id"]?.GetValue<string>() == targetScopeId || p["target-root"]!.GetValue<string>() == targetContext.Root)) ??
                (publication?["service-id"]?.GetValue<string>() == service.ServiceId ? publication : null);
        if (publication?["target-scope-id"]?.GetValue<string>() is string savedScope && targetStore.Read().ScopeId != savedScope)
            throw new StorageException("publication-conflict", "La identidad física del destino registrado cambió.");
        var lifecycle = new ScopedServiceProcess(targetStore, output, error);
        var running = lifecycle.Reconcile(service.ServiceId);
        if (!running && !withdrawing)
        {
            if (action != "up") throw new StorageException("service-stopped", "El destino está detenido; iniciar el servicio explícitamente o usar up.");
            if (!start)
            {
                if (!interaction.StartService())
                    throw new StorageException("service-stopped", "No se autorizó iniciar el destino.");
            }
            var startArgs = new List<string> { "--service", targetContext.IsGlobal ? service.Name : "self", "start", "-d" };
            if (!targetContext.IsGlobal)
            {
                startArgs.AddRange(["--project-directory", selection.Base]);
                foreach (var f in selection.Files) startArgs.AddRange(["-f", f]);
                foreach (var f in selection.EnvFiles) startArgs.AddRange(["--env-file", f]);
                if (isPrivate) startArgs.Add("--private");
            }
            var exit = ScopedServiceCli.Run(startArgs.ToArray(), output, error);
            if (exit != 0) return exit;
            running = lifecycle.Reconcile(service.ServiceId);
            if (!running) throw new StorageException("service-stopped", "El destino terminó después del inicio; no se acredita publicación.");
        }
        var active = targetStore.Read().Documents.GetValueOrDefault(targetStore.RuntimeDocumentPath(service.ServiceId, "active"))?["data"];
        if (running && active?["execution"]?["arguments"]?.AsArray().Any(a => a?.GetValue<string>() == "--models-preset") != true)
            throw new StorageException("publication-unsupported", "La instancia no ofrece INI operativo recargable; requiere un inicio explícito compatible.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(120) };
        var host = active?["endpoint"]?["host"]?.GetValue<string>();
        host = host switch { "0.0.0.0" => "127.0.0.1", "::" => "::1", _ => host };
        IPresetRouter? router = running ? new LlamaPresetRouter(http, new UriBuilder("http", host!, active!["endpoint"]!["port"]!.GetValue<int>()).Uri) : null;
        var manager = new PresetPublication(store, targetStore, service.ServiceId);
        if (PresetPublication.Service(targetStore.Read(), service.ServiceId)["publication-operation"] is not null)
        {
            var reconciled = await manager.ReconcileAsync(identity.RecipeId, router, !running);
            if (reconciled.Confirmed && reconciled.WarmupFailed && router is not null)
            {
                var restored = reconciled.Restored ? reconciled : await manager.RestoreFailedAsync(identity.RecipeId, router, true, interaction.Confirm);
                error.WriteLine("Error: preset-load-failed. Entradas: " + string.Join(", ", reconciled.FailedEntries ?? []));
                output.WriteLine(restored?.Confirmed == true ? "Entradas afectadas restauradas sin reiniciar." : "Carga fallida; consultar estado de la composición.");
                return restored?.Confirmed == false ? 9 : 8;
            }
            output.WriteLine(reconciled.Confirmed ? "Operación anterior reconciliada; repetir el comando para una nueva publicación." : "Resultado anterior incierto; recursos protegidos.");
            return reconciled.Confirmed ? 0 : 9;
        }
        if (!withdrawing && router is not null)
        {
            var restoration = await manager.RestoreFailedAsync(identity.RecipeId, router, true, interaction.Confirm);
            if (restoration is not null)
            {
                error.WriteLine("Error: preset-load-failed. Entradas: " + string.Join(", ", restoration.FailedEntries ?? []));
                output.WriteLine(restoration.Confirmed ? "Carga fallida detectada; entradas afectadas restauradas sin reiniciar. Repetir para aplicar otra candidata." : "Restauración no confirmada; operación y recursos conservados.");
                return restoration.Confirmed ? 8 : 9;
            }
        }
        var presets = new List<PublishedPreset>(); Action? validate = null;
        var name = publication?["presets"] is JsonArray existing && existing.Count != 0
            ? publication["recipe-name"]?.GetValue<string>() ?? identity.RegisteredName
            : recipe?.Name ?? publication?["recipe-name"]?.GetValue<string>() ?? identity.RegisteredName;
        if (recipe is not null && recipe.Name != name)
            output.WriteLine($"La intención usa {recipe.Name}; se conserva el nombre publicado {name} hasta retirada confirmada.");
        var combination = publication?["combination"]?.GetValue<string>() ?? "";
        if (key is not null || withdrawing)
            foreach (var p in publication?["presets"]?.AsArray() ?? [])
                if (action != "down" && p!["name"]!.GetValue<string>() != name + "/" + key)
                    presets.Add(FromNode(p!));
        if (!withdrawing)
        {
            if (key is not null && recipe!.Root.Get("presets")?.Get(key) is null) throw new StorageException("unknown-preset", "El preset seleccionado no existe.");
            var inputPaths = selection.Files.Concat(selection.EnvFiles).Concat(recipe!.Layers.SelectMany(l => l.Get("arg-defs")?.Get("imports")?.Items ?? []).Select(i => selection.ResolvePath(i.Text!)));
            var inputs = inputPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p, File.ReadAllBytes);
            var adapter = new LlamaB11482ArgumentAdapter();
            var backendResource = active?["resources"]?.AsArray().SingleOrDefault(r => r!["kind"]?.GetValue<string>() == "backend");
            var backend = new ArgumentBackend(backendResource?["resolution"]?["origin"]?.GetValue<string>() == "official" ? "official" : null,
                backendResource?["resolution"]?["selection"]?["release"]?.GetValue<string>());
            var schema = ArgumentSchema.Load(recipe);
            var blocks = (recipe.Root.Get("presets")?.Fields ?? new Dictionary<string, RecipeValue>())
                .Where(p => (key is null || p.Key == key) && recipe.PresetEnabled(p.Key))
                .Select(p => ArgumentValidation.Block(recipe, schema, p.Value, "preset", p.Key, recipe.PresetArgsMode(p.Key), backend)).ToArray();
            foreach (var block in blocks)
            {
                foreach (var diagnostic in block.Diagnostics) error.WriteLine($"Aviso: {diagnostic.Code}. {diagnostic.Detail}");
                _ = ArgumentEmission.Ini(recipe, block, name + "/" + block.Key, adapter, _ => "prepared-resource");
            }
            using var transport = new OfficialArtifactTransport();
            var resources = new ScopedCombinationResources(context, HardwareProbe.Inspect(), new HuggingFaceStore(http), transport,
                new ConsoleModelSelectionInteraction(), warning: message => error.WriteLine(message));
            var prepared = await new CombinationPreparation(context).PrepareAsync(selection, resources, isPrivate,
                presetsOnly: true, presetKey: key);
            combination = prepared.Id;
            validate = () =>
            {
                if (inputs.Any(p => !File.Exists(p.Key) || !File.ReadAllBytes(p.Key).AsSpan().SequenceEqual(p.Value)) ||
                    !JsonNode.DeepEquals(CombinationPreparation.Expanded(recipe.Root), CombinationPreparation.Expanded(RecipeReader.Read(selection).Root)))
                    throw new StorageException("intention-conflict", "Cambió la intención durante la publicación.");
            };
            validate();
            string Resolve(ArgumentReference reference) => prepared.Resources.Single(r => r.Kind == reference.Kind && r.Key.Equals(reference.Name, StringComparison.OrdinalIgnoreCase)).EntryPath;
            foreach (var block in blocks)
            {
                var bindings = new JsonArray(prepared.Resources.Select(r => (JsonNode?)new JsonObject
                { ["resource-id"] = r.ResourceId, ["installation-id"] = r.InstallationId, ["entry-path"] = r.EntryPath }).ToArray());
                presets.Add(new(name + "/" + block.Key, ArgumentEmission.Ini(recipe, block, name + "/" + block.Key, adapter, Resolve),
                    ArgumentEmission.Cli(recipe, block with { Arguments = block.Arguments.Where(p => adapter.Supports(p.Key, "preset", "cli")).ToDictionary(p => p.Key, p => p.Value) }, adapter, Resolve).ToArray(), bindings, recipe.PresetWarmup(block.Key)));
            }
        }
        var result = await manager.PublishAsync(identity.RecipeId, presets.ToArray(), combination, name, !running, true, interaction.Confirm, router, validate);
        if (result.WarmupFailed) error.WriteLine("Error: preset-load-failed. Entradas: " + string.Join(", ", result.FailedEntries ?? []));
        output.WriteLine(result.Confirmed ? (result.WarmupFailed ? (result.Restored ? "Carga fallida; entradas afectadas restauradas sin reiniciar." : "Carga fallida; no se dispone de un snapshot distinto para restaurar.") : "Composición confirmada; carga no comprobada salvo warmup explícito.") : "Resultado no confirmado; operación y recursos conservados.");
        return !result.Confirmed ? 9 : result.WarmupFailed ? 8 : 0;
    }

    private static PublishedPreset FromNode(JsonNode p) => new(p["name"]!.GetValue<string>(), p["ini"]!.GetValue<string>(),
        p["expected-arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray(), p["resources"]!.DeepClone().AsArray(), p["warmup"]!.GetValue<bool>());

    public static async Task EnsureGlobalAsync(ScopeContext context, TextWriter output, Func<Task<BackendCandidate?>>? select = null)
    {
        if (!context.IsGlobal) throw new ArgumentException("La provisión requiere ámbito global.");
        var store = new ScopeStore(context); var snapshot = store.Read();
        if (snapshot.State?["data"]?["aliases"]?["default"] is not null) return;
        if (new RecipeRegistry(store).Services().Length != 0) throw new StorageException("default-required", "Hay servicios globales: elija uno con service default set.");
        using var http = new HttpClient();
        var selected = (select is null ? (await new OfficialBackendSource(http).ResolveAsync(HardwareProbe.Inspect(), null, "latest")).Selected : await select())
            ?? throw new BackendException("publication-unsupported", "No se encontró una variante compatible.");
        var serviceId = ProjectDocuments.Id(); var recipeId = ProjectDocuments.Id();
        var path = $"services/{serviceId}/recipe.yaml";
        PresetPublication.WriteIni(context.PathFor(path), $"name: coquena\nservice:\n  backend:\n    version: latest\n    variant: {selected.Variant}\n");
        var docs = RecipeRegistry.Next(snapshot);
        var recipes = docs.GetValueOrDefault("local/recipes.json")?["data"]?["recipes"]?.DeepClone().AsArray() ?? new JsonArray();
        recipes.Add((JsonNode)new JsonObject { ["recipe-id"] = recipeId, ["consumer-id"] = ProjectDocuments.Id(),
                ["name"] = "coquena", ["files"] = new JsonArray(path), ["revision"] = 1, ["pending-withdrawals"] = new JsonArray(),
                ["entities"] = new JsonArray(new JsonObject { ["kind"] = "service", ["id"] = serviceId, ["owner"] = recipeId, ["name"] = "coquena" }) });
        docs["local/recipes.json"] = ProjectDocuments.Create("recipe-registry", docs.GetValueOrDefault("local/recipes.json")?["revision"]?.GetValue<long>() ?? 1,
            new JsonObject { ["recipes"] = recipes });
        ScopeStore.CommitTogether([new(store, snapshot.Revision, docs, DefaultServiceId: serviceId)]);
        output.WriteLine($"Servidor global inicial: llama.cpp {selected.Release}, {selected.Variant}; {selected.Diagnostic}");
    }
}
