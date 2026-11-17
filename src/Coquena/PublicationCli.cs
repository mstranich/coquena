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
    bool Confirm(string diagnostic, string destination) => Confirm(Messages.PublicationDestination.Format(destination, diagnostic));
    bool StartService();
    bool StartService(string diagnostic) => StartService();
}

public sealed record ServiceCreationOptions(string Host, int Port, string? HealthUrl, JsonObject Arguments)
{
    internal JsonObject Server(bool external)
    {
        if (string.IsNullOrWhiteSpace(Host) || Host.Any(char.IsControl) || Port is < 1 or > 65535)
            throw new ArgumentException(Messages.PublicationHostPort);
        if (external && string.IsNullOrWhiteSpace(HealthUrl)) throw new ArgumentException(Messages.PublicationHealthRequired);
        if (!string.IsNullOrWhiteSpace(HealthUrl) && (!Uri.TryCreate(HealthUrl, UriKind.Absolute, out var url) ||
            url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo)))
            throw new ArgumentException(Messages.PublicationHealthInvalid);
        var args = Arguments.DeepClone().AsObject();
        if (args.ContainsKey("host") || args.ContainsKey("port")) throw new ArgumentException(Messages.PublicationHostPortFields);
        args["host"] = Host; args["port"] = Port;
        var server = new JsonObject { ["args"] = args };
        if (!string.IsNullOrWhiteSpace(HealthUrl)) server["health"] = new JsonObject { ["url"] = HealthUrl };
        return server;
    }
}

public static class PublicationCli
{
    internal sealed record Frontend(IModelSelectionInteraction Models,
        Func<string[], TextWriter, TextWriter, Task<int>> StartService, CancellationToken Cancellation, StepProgress? Steps = null);

    private sealed class ConsoleInteraction(TextWriter output) : IPublicationInteraction
    {
        public bool Available => !Console.IsInputRedirected;
        public bool Confirm(string diagnostic)
        {
            output.WriteLine(diagnostic); output.WriteLine(Messages.ImportApplyQuestion);
            return Console.ReadLine()?.Trim().Equals("a", StringComparison.OrdinalIgnoreCase) == true;
        }
        public bool StartService()
        {
            output.WriteLine(Messages.PublicationStartQuestion);
            return Messages.IsYes(Console.ReadLine());
        }
    }

    public static int Run(string[] args, TextWriter output, TextWriter error, IPublicationInteraction? interaction = null)
        => Execute(args, output, error, interaction ?? new ConsoleInteraction(output));

    internal static int Execute(string[] args, TextWriter output, TextWriter error, IPublicationInteraction interaction, Frontend? frontend = null)
    {
        // --verbose keeps every detail line; by default each step shows its detail only when it fails.
        var verbose = args.Contains("--verbose");
        args = args.Where(a => a != "--verbose").ToArray();
        using var owned = frontend?.Steps is null ? StepProgress.ForConsole(output, verbose) : null;
        var steps = frontend?.Steps ?? owned!;
        // Ordinary lines go above the running block instead of being erased by its redraw.
        if (owned is not null) { var rawOutput = output; var rawError = error; output = new LineWriter(line => steps.Print(line, rawOutput)); error = new LineWriter(line => steps.Print(line, rawError)); }
        try { return RunAsync(args, output, error, interaction, frontend: frontend, steps: steps).GetAwaiter().GetResult(); }
        catch (Exception e) when (e is StorageException or RecipeException or BackendException or DownloadException or
            ModelSourceException or ContractException or ConfigurationException or System.ComponentModel.Win32Exception or
            ArgumentException or IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException)
        {
            var code = e switch { StorageException s => s.Code, RecipeException r => r.Code, BackendException b => b.Code,
                ModelSourceException m => m.Code, DownloadException d => d.Code, ContractException c => c.Code,
                ConfigurationException => "instance-identity-uncertain", OperationCanceledException => "cancelled", _ => "publication-error" };
            // The message explains; the code stays at the end for scripts and reports.
            error.WriteLine($"Error: {e.Message.TrimEnd().TrimEnd('.')} ({code}).");
            if (e is ArgumentException) error.WriteLine(Messages.SeeSyntax);
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

    private static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, IPublicationInteraction interaction, JsonObject? recordedTarget = null, bool continueAfterRecovery = true, Frontend? frontend = null, StepProgress? steps = null)
    {
        steps ??= StepProgress.Silent();
        var cancellation = frontend?.Cancellation ?? CancellationToken.None;
        cancellation.ThrowIfCancellationRequested();
        var files = new List<string>(); var env = new List<string>(); var positional = new List<string>();
        var seen = new HashSet<string>(); string? basePath = null, selector = null; bool start = false, isPrivate = false, end = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i] switch { "-f" => "--file", "--base" => "--project-directory", _ => args[i] };
            if (end) { positional.Add(arg); continue; }
            if (arg == "--") { end = true; continue; }
            if (arg is "--file" or "--env-file" or "--project-directory" or "--service")
            {
                if (++i == args.Length || arg is not ("--file" or "--env-file") && !seen.Add(arg)) throw new ArgumentException(Messages.OptionInvalidOrDuplicated);
                if (arg == "--file") files.Add(args[i]); else if (arg == "--env-file") env.Add(args[i]);
                else if (arg == "--service") selector = args[i]; else basePath = args[i];
            }
            else if (arg is "--start-service" or "--private")
            {
                if (!seen.Add(arg)) throw new ArgumentException(Messages.OptionDuplicated);
                if (arg == "--start-service") start = true; else isPrivate = true;
            }
            else if (arg.StartsWith('-')) throw new ArgumentException(Messages.PublicationUnknownOption);
            else positional.Add(arg);
        }
        if (positional.FirstOrDefault() == "preset") positional.RemoveAt(0);
        if (positional.Count == 0) throw new ArgumentException(Messages.PublicationMissingOperation);
        var action = positional[0]; string? key = positional.Count == 2 ? positional[1] : null;
        if (action is not ("up" or "down" or "apply" or "withdraw") ||
            (action is "up" or "down" ? positional.Count != 1 : positional.Count != 2) || start && action != "up")
            throw new ArgumentException(Messages.PublicationInvalidSyntax);
        if (!interaction.Available) throw new StorageException("interaction-required", Messages.PublicationNeedsInteraction);
        var withdrawing = action is "down" or "withdraw";
        var selection = new RecipeSelection(Environment.CurrentDirectory, files, basePath, env);
        var context = ScopeContext.Project(selection.Files[0], selection.Base); var store = new ScopeStore(context);
        var registry = new RecipeRegistry(store);
        EffectiveRecipe? recipe = null;
        if (!withdrawing)
        {
            var validating = steps.Begin(Messages.StepValidateRecipe);
            try
            {
                recipe = RecipeReader.Read(selection);
                _ = ArgumentValidation.Validate(recipe);
                _ = ModelIntent.FromRecipe(recipe);
                validating.Done();
            }
            catch { validating.Fail(); throw; }
        }
        RecipeIdentity identity;
        if (withdrawing)
        {
            var found = store.Read().Documents.GetValueOrDefault("local/recipes.json")?["data"]?["recipes"]?.AsArray()
                .SingleOrDefault(r => r!["project-consumer"]?.GetValue<bool>() == true);
            if (found is null)
            {
                if (action == "withdraw") throw new StorageException("unknown-preset", Messages.PublicationNothingToWithdraw);
                output.WriteLine(Messages.DisplayNoOwnPublicationsRecorded.ToString()); return 0;
            }
            identity = registry.Get(found["recipe-id"]!.GetValue<string>());
        }
        else identity = registry.EnsureProjectConsumer(selection.Files, recipe!.Root.Get("name")?.Text);
        var entry = store.Read().Documents["local/recipes.json"]["data"]!["recipes"]!.AsArray()
            .Single(r => r!["recipe-id"]!.GetValue<string>() == identity.RecipeId)!;
        var publication = entry["publication-operation"] ?? recordedTarget ?? entry["publication"];
        if (withdrawing && publication is null)
        {
            if (action == "withdraw") throw new StorageException("unknown-preset", Messages.PublicationNothingToWithdraw);
            output.WriteLine(Messages.DisplayNoOwnPublicationsRecorded.ToString()); return 0;
        }
        if (withdrawing && entry["publication-operation"] is null && recordedTarget is null && selector is null && entry["publications"] is JsonArray all)
        {
            var remaining = all.Where(p => p!["presets"]!.AsArray().Count != 0).ToArray();
            if (action == "withdraw")
            {
                remaining = remaining.Where(p => HasPublishedPreset(p!, key!)).ToArray();
                if (remaining.Length == 0)
                    throw new StorageException("unknown-preset", Messages.PublicationPresetNotOwn);
            }
            if (remaining.Length > 1)
            {
                var exit = 0;
                foreach (var previous in remaining)
                {
                    var previousResult = await RunAsync(args, output, error, interaction, previous!.DeepClone().AsObject(), frontend: frontend, steps: steps);
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
                ?? throw new StorageException("publication-conflict", Messages.PublicationNoOwnInTarget);
        }
        if ((withdrawing || entry["publication-operation"] is not null) && publication is not null)
        {
            targetContext = publication["global"]!.GetValue<bool>() ? ScopeContext.Global(new Home(publication["target-base"]!.GetValue<string>()))
                : ScopeContext.Project(Path.Combine(publication["target-base"]!.GetValue<string>(), "coquena.yaml"));
            var targetRegistry = new RecipeRegistry(new ScopeStore(targetContext));
            service = targetRegistry.Services().SingleOrDefault(s => s.ServiceId == publication["service-id"]!.GetValue<string>())
                ?? throw new StorageException("publication-conflict", Messages.PublicationTargetGone);
            if (selector is not null)
            {
                var explicitId = selector == "self" ? registry.FindProjectService(selection.Files).ServiceId :
                    new RecipeRegistry(new ScopeStore(ScopeContext.Global(home))).ResolveGlobalService(selector == "global" ? "default" : selector);
                if (explicitId != service.ServiceId) throw new StorageException("publication-conflict", Messages.PublicationSelectionMismatch);
            }
        }
        else
        {
            var chosen = selector ?? recipe?.Target ?? "global";
            if (chosen == "self")
            {
                if (recipe?.Root.Get("service") is null) throw new StorageException("self-requires-service", Messages.PublicationSelfNeedsService);
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
            throw new StorageException("publication-conflict", Messages.PublicationTargetIdentityChanged);
        var lifecycle = new ScopedServiceProcess(targetStore, output, error);
        var manager = new PresetPublication(store, targetStore, service.ServiceId);
        bool Confirm(string diagnostic) { using (steps.Hold()) return interaction.Confirm(diagnostic, service.Name + " (" + Messages.GlobalOrProject(targetContext.IsGlobal) + ")"); }
        var running = lifecycle.Reconcile(service.ServiceId);
        if (!running && !withdrawing)
        {
            if (action != "up") throw new StorageException("service-stopped", Messages.PublicationTargetStopped);
            if (!start)
            {
                bool allowed; using (steps.Hold()) allowed = interaction.StartService(Messages.PublicationStartToPublish.Format(service.Name, Messages.GlobalOrProject(targetContext.IsGlobal)));
                if (!allowed)
                    throw new StorageException("service-stopped", Messages.PublicationStartNotAllowed);
            }
            var startArgs = new List<string> { "--service", targetContext.IsGlobal ? service.Name : "self", "start", "-d" };
            if (PresetPublication.Service(targetStore.Read(), service.ServiceId)["publication-operation"] is JsonNode unfinished)
            {
                var recovered = await manager.ReconcileAsync(identity.RecipeId, null, stopped: true, cancellation: cancellation);
                if (!recovered.Confirmed)
                    throw new StorageException("publication-pending", Messages.PublicationUnfinishedNotCompleted.Format(Unfinished(unfinished)));
                output.WriteLine(Messages.PublicationUnfinishedCompleted.Format(Unfinished(unfinished)));
            }
            if (targetContext.IsGlobal) startArgs.Add("--global");
            if (!targetContext.IsGlobal)
            {
                startArgs.AddRange(["--project-directory", selection.Base]);
                foreach (var f in selection.Files) startArgs.AddRange(["-f", f]);
                foreach (var f in selection.EnvFiles) startArgs.AddRange(["--env-file", f]);
                if (isPrivate) startArgs.Add("--private");
            }
            var starting = steps.Begin(Messages.StepStartService.Format(service.Name));
            int exit;
            using (var detail = starting.Writer())
            {
                try
                {
                    exit = frontend is null ? ScopedServiceCli.Run(startArgs.ToArray(), detail, error)
                        : await frontend.StartService(startArgs.ToArray(), detail, error);
                }
                catch { starting.Fail(); throw; }
            }
            if (exit != 0) { starting.Fail(); return exit; }
            starting.Done();
            running = lifecycle.Reconcile(service.ServiceId);
            if (!running) throw new StorageException("service-stopped", Messages.PublicationEndedAfterStart);
        }
        var active = targetStore.Read().Documents.GetValueOrDefault(targetStore.RuntimeDocumentPath(service.ServiceId, "active"))?["data"];
        if (running && active?["execution"]?["arguments"]?.AsArray().Any(a => a?.GetValue<string>() == "--models-preset") != true)
            throw new StorageException("publication-unsupported", Messages.PublicationNoReloadableIni);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(120) };
        var host = active?["endpoint"]?["host"]?.GetValue<string>();
        host = host switch { "0.0.0.0" => "127.0.0.1", "::" => "::1", _ => host };
        IPresetRouter? router = running ? new LlamaPresetRouter(http, new UriBuilder("http", host!, active!["endpoint"]!["port"]!.GetValue<int>()).Uri) : null;
        if (PresetPublication.Service(targetStore.Read(), service.ServiceId)["publication-operation"] is not null)
        {
            var reconciled = await manager.ReconcileAsync(identity.RecipeId, router, !running, cancellation);
            if (reconciled.Confirmed && reconciled.WarmupFailed && router is not null)
            {
                var restored = reconciled.Restored ? reconciled : await manager.RestoreFailedAsync(identity.RecipeId, router, true, Confirm, cancellation);
                error.WriteLine(Messages.PresetsNotLoaded.Format(string.Join(", ", reconciled.FailedEntries ?? [])));
                output.WriteLine(restored?.Confirmed == true ? Messages.PresetsRestored
                    : Messages.PresetsNotLoadedCheck);
                return restored?.Confirmed == false ? 9 : 8;
            }
            if (reconciled.Confirmed && action == "up" && continueAfterRecovery)
            {
                output.WriteLine(Messages.PreviousUpContinuing);
                return await RunAsync(args, output, error, interaction, continueAfterRecovery: false, frontend: frontend, steps: steps);
            }
            output.WriteLine(reconciled.Confirmed ? Messages.PreviousUpCompleted
                : Messages.PreviousUpUnknown);
            return reconciled.Confirmed ? 0 : 9;
        }
        if (!withdrawing && router is not null)
        {
            var restoration = await manager.RestoreFailedAsync(identity.RecipeId, router, true, Confirm, cancellation);
            if (restoration is not null)
            {
                error.WriteLine(Messages.PresetsNotLoaded.Format(string.Join(", ", restoration.FailedEntries ?? [])));
                output.WriteLine(restoration.Confirmed ? Messages.LoadFailedRestored : Messages.RestorationUnconfirmed);
                return restoration.Confirmed ? 8 : 9;
            }
        }
        var presets = new List<PublishedPreset>(); Action? validate = null; StepProgress.Step? preparing = null;
        var name = publication?["presets"] is JsonArray existing && existing.Count != 0
            ? publication["recipe-name"]?.GetValue<string>() ?? identity.RegisteredName
            : recipe?.Name ?? publication?["recipe-name"]?.GetValue<string>() ?? identity.RegisteredName;
        if (recipe is not null && recipe.Name != name)
            output.WriteLine(Messages.PublishedNameKept.Format(recipe.Name, name));
        var combination = publication?["combination"]?.GetValue<string>() ?? "";
        if (action == "withdraw" && !HasPublishedPreset(publication!, key!))
            throw new StorageException("unknown-preset", Messages.PublicationPresetNotInTarget);
        if (key is not null || withdrawing)
            foreach (var p in publication?["presets"]?.AsArray() ?? [])
                if (action != "down" && p!["name"]!.GetValue<string>() != name + "/" + key &&
                    (action != "withdraw" || p["name"]!.GetValue<string>() != key))
                    presets.Add(FromNode(p!));
        if (!withdrawing)
        {
            if (key is not null && recipe!.Root.Get("presets")?.Get(key) is null) throw new StorageException("unknown-preset", Messages.PublicationPresetMissing);
            var inputPaths = selection.Files.Concat(selection.EnvFiles);
            var inputs = inputPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p, SharedFile.ReadAllBytes);
            var adapter = new LlamaB11482ArgumentAdapter();
            var backendResource = active?["resources"]?.AsArray().SingleOrDefault(r => r!["kind"]?.GetValue<string>() == "backend");
            var backend = new ArgumentBackend(backendResource?["resolution"]?["origin"]?.GetValue<string>() == "official" ? "official" : null,
                backendResource?["resolution"]?["selection"]?["release"]?.GetValue<string>());
            var schema = ArgumentSchema.Load(recipe!);
            var blocks = (recipe!.Root.Get("presets")?.Fields ?? new Dictionary<string, RecipeValue>())
                .Where(p => (key is null || p.Key == key) && recipe.PresetEnabled(p.Key))
                .Select(p => ArgumentValidation.Block(recipe, schema, p.Value, "preset", p.Key, recipe.PresetArgsMode(p.Key), backend)).ToArray();
            foreach (var block in blocks)
            {
                foreach (var diagnostic in block.Diagnostics) error.WriteLine(Messages.OutputWarning.Format(diagnostic.Code, diagnostic.Detail));
                _ = ArgumentEmission.Ini(recipe, block, name + "/" + block.Key, adapter, _ => "prepared-resource");
            }
            using var transport = new HuggingFaceTransport();
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(frontend?.Cancellation ?? CancellationToken.None);
            // The same backend is checked once per resource that needs it; each warning is shown once.
            var warned = new HashSet<string>(StringComparer.Ordinal);
            var resources = new ScopedCombinationResources(context, HardwareProbe.Inspect(), new HuggingFaceStore(http), transport,
                new HeldModelSelection(frontend?.Models ?? new ConsoleModelSelectionInteraction(), steps), warning: message => { if (warned.Add(message)) error.WriteLine(message); },
                downloadProgress: operation => { if (preparing is not null) using (var detail = preparing.Writer()) DownloadCli.Show(operation, detail); });
            PreparedCombination prepared;
            preparing = steps.Begin(Messages.StepPrepareResources);
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stopped.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                prepared = await new CombinationPreparation(context).PrepareAsync(selection, resources, isPrivate,
                    presetsOnly: true, presetKey: key, cancellation: stopped.Token);
                preparing.Done();
            }
            catch { preparing.Fail(); throw; }
            finally { Console.CancelKeyPress -= cancel; }
            combination = prepared.Id;
            validate = () =>
            {
                if (inputs.Any(p => !File.Exists(p.Key) || !SharedFile.ReadAllBytes(p.Key).AsSpan().SequenceEqual(p.Value)) ||
                    !JsonNode.DeepEquals(CombinationPreparation.Expanded(recipe.Root), CombinationPreparation.Expanded(RecipeReader.Read(selection).Root)))
                    throw new StorageException("intention-conflict", Messages.PublicationIntentChanged);
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
        var publishing = steps.Begin(withdrawing ? Messages.StepWithdrawPresets : running ? Messages.StepPublishLoadPresets : Messages.StepPublishPresets);
        PublicationResult result;
        try { result = await manager.PublishAsync(identity.RecipeId, presets.ToArray(), combination, name, !running, true, Confirm, router, validate, cancellation); }
        catch { publishing.Fail(); throw; }
        if (result.Confirmed && !result.WarmupFailed) publishing.Done(); else publishing.Fail();
        if (result.WarmupFailed) error.WriteLine(Messages.PresetsNotLoaded.Format(string.Join(", ", result.FailedEntries ?? [])));
        if (result.Confirmed && !result.WarmupFailed)
        {
            var previous = (publication?["presets"] as JsonArray ?? []).Select(p => FromNode(p!)).ToArray();
            foreach (var changed in result.ChangedEntries ?? [])
            {
                var current = presets.SingleOrDefault(p => p.Name == changed);
                var definition = current ?? previous.SingleOrDefault(p => p.Name == changed);
                var argsForPreset = definition?.ExpectedArguments ?? [];
                var modelIndex = Array.IndexOf(argsForPreset, "--model");
                var model = modelIndex >= 0 && modelIndex + 1 < argsForPreset.Length
                    ? Messages.PublishedModel.Format(Path.GetFileName(argsForPreset[modelIndex + 1])) : "";
                output.WriteLine((current is null ? Messages.PresetWithdrawn : Messages.PresetPublished).Format(changed, model));
            }
            if (result.ChangedEntries is null) output.WriteLine(Messages.PresetsNoChanges);
            // Running healthy here retires the recovery this project left in another service.
            if (running && !withdrawing)
                foreach (var released in RecoveryRelease.Release(store, entry["consumer-id"]!.GetValue<string>(), targetStore, service.ServiceId, home))
                    output.WriteLine(Messages.PublicationRecoveryReleased.Format(released.ServiceName, Messages.GlobalOrProject(released.Global)));
            return 0;
        }
        output.WriteLine(result.Confirmed ? (result.WarmupFailed ? (result.Restored ? Messages.LoadFailedEntriesRestored : Messages.LoadFailedNoSnapshot) : Messages.CompositionConfirmed) : Messages.ResultUnconfirmed);
        return !result.Confirmed ? 9 : result.WarmupFailed ? 8 : 0;
    }

    private static bool HasPublishedPreset(JsonNode publication, string key) =>
        publication["presets"]?.AsArray().Any(p => p!["name"]!.GetValue<string>() == key ||
            p["name"]!.GetValue<string>() == publication["recipe-name"]!.GetValue<string>() + "/" + key) == true;

    private static PublishedPreset FromNode(JsonNode p) => new(p["name"]!.GetValue<string>(), p["ini"]!.GetValue<string>(),
        p["expected-arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray(), p["resources"]!.DeepClone().AsArray(), p["warmup"]!.GetValue<bool>());

    public static async Task EnsureGlobalAsync(ScopeContext context, TextWriter output, Func<Task<BackendCandidate?>>? select = null,
        string? serviceName = null, BackendIntent? backend = null, CancellationToken cancellation = default, ServiceCreationOptions? options = null,
        bool installed = false)
    {
        if (!context.IsGlobal) throw new ArgumentException(Messages.ProvisionNeedsGlobal);
        var store = new ScopeStore(context); var snapshot = store.Read();
        var services = new RecipeRegistry(store).Services();
        var hasDefault = snapshot.State?["data"]?["aliases"]?["default"] is not null;
        if (serviceName is null)
        {
            if (hasDefault) return;
            if (services.Length != 0) throw new StorageException("default-required", Messages.ProvisionDefaultRequired);
        }
        else
        {
            if (serviceName is "global" or "default" or "self") throw new StorageException("reserved-service-name", Messages.ServiceNameReserved);
            if (string.IsNullOrWhiteSpace(serviceName) || serviceName.IndexOfAny(['\0', '\r', '\n', ':', '/', '\\']) >= 0)
                throw new ArgumentException(Messages.ServiceNameInvalid);
            if (services.Any(s => s.Name == serviceName)) throw new StorageException("service-name-conflict", Messages.ServiceNameExists);
        }
        var name = serviceName ?? "coquena";
        backend?.Validate();
        var server = options?.Server(backend?.Executable is not null);
        if (backend?.Executable is { } executable && !File.Exists(Path.GetFullPath(executable, context.Base)))
            throw new BackendException("backend-executable-absent", Messages.ExternalExecutableMissing);
        using var http = new HttpClient();
        // An installed backend was already resolved and checked when the scope prepared it.
        if (installed && backend is null) throw new ArgumentException(Messages.InstalledBackendNeedsSelection);
        var selected = backend?.Executable is not null || installed ? null :
            (select is null ? (await new OfficialBackendSource(http).ResolveAsync(HardwareProbe.Inspect(), backend?.Variant, backend?.Version ?? "latest", cancellation)).Selected : await select())
            ?? throw new BackendException("publication-unsupported", Messages.NoCompatibleVariant);
        if (selected is not null && backend is not null) ScopedBackends.CheckCompatibility(selected, HardwareProbe.Inspect(), false, message => output.WriteLine(message));
        backend ??= new("latest", selected!.Variant);
        cancellation.ThrowIfCancellationRequested();
        var serviceId = ProjectDocuments.Id(); var recipeId = ProjectDocuments.Id();
        var path = $"services/{serviceId}/recipe.yaml";
        var definition = backend.Executable is { } external
            ? "    executable: " + JsonValue.Create(Path.GetFullPath(external, context.Base))!.ToJsonString() + "\n"
            : $"    version: {backend.Version}\n    variant: {backend.Variant}\n";
        var yaml = $"name: '{name.Replace("'", "''")}'\nservice:\n  backend:\n" + definition +
            (server is null ? "" : "  server: " + server.ToJsonString() + "\n");
        // Run recipe/argument validation before publishing any definition or registration.
        var selection = new RecipeSelection(context.Base, [context.PathFor(path)], context.Base)
        { CapturedFiles = new Dictionary<string, byte[]> { [context.PathFor(path)] = System.Text.Encoding.UTF8.GetBytes(yaml) } };
        var recipe = RecipeReader.Read(selection);
        _ = ArgumentValidation.Validate(recipe);
        PresetPublication.WriteIni(context.PathFor(path), yaml);
        var docs = RecipeRegistry.Next(snapshot);
        var recipes = docs.GetValueOrDefault("local/recipes.json")?["data"]?["recipes"]?.DeepClone().AsArray() ?? new JsonArray();
        recipes.Add((JsonNode)new JsonObject { ["recipe-id"] = recipeId, ["consumer-id"] = ProjectDocuments.Id(),
                ["name"] = name, ["files"] = new JsonArray(path), ["revision"] = 1, ["pending-withdrawals"] = new JsonArray(),
                ["entities"] = new JsonArray(new JsonObject { ["kind"] = "service", ["id"] = serviceId, ["owner"] = recipeId, ["name"] = name }) });
        docs["local/recipes.json"] = ProjectDocuments.Create("recipe-registry", docs.GetValueOrDefault("local/recipes.json")?["revision"]?.GetValue<long>() ?? 1,
            new JsonObject { ["recipes"] = recipes });
        ScopeStore.CommitTogether([new(store, snapshot.Revision, docs, DefaultServiceId: services.Length == 0 ? serviceId : null)]);
        output.WriteLine(backend.Executable is not null ? Messages.GlobalServiceExternal.Format(name)
            : selected is null ? Messages.GlobalServiceScoped.Format(name, backend.Variant, backend.Version)
            : Messages.GlobalServiceRelease.Format(name, selected.Release, selected.Variant, selected.Diagnostic));
    }

    // Names what an interrupted «up» was publishing, so the user knows what was completed.
    private static string Unfinished(JsonNode operation)
    {
        var recipe = operation["recipe-name"]?.GetValue<string>();
        var presets = (operation["affected"] as JsonArray)?.Select(item => item?.GetValue<string>()).OfType<string>().ToArray() ?? [];
        return (recipe is null ? Messages.UnfinishedUp.ToString() : Messages.UnfinishedUpOf.Format(recipe)) +
            (presets.Length == 0 ? "" : " (presets: " + string.Join(", ", presets) + ")");
    }
}
