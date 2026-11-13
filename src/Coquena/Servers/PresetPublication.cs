using System.Text;
using System.Text.Json.Nodes;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Servers;

public sealed record PublishedPreset(string Name, string Ini, string[] ExpectedArguments, JsonArray Resources, bool Warmup);
public sealed record PublicationResult(bool Confirmed, bool WarmupFailed = false, string[]? FailedEntries = null, bool Restored = false, string[]? ChangedEntries = null);
public enum PublicationStage { Reserved, IniPublished, Sent, Observed }

// Network operations are deliberately separate from short generation commits.
public interface IPresetRouter
{
    Task<JsonArray> ObserveAsync(CancellationToken cancellation);
    Task<string?> ActivityAsync(string[] names, CancellationToken cancellation);
    Task<bool> ReloadAsync(CancellationToken cancellation);
    Task<bool> WarmupAsync(string name, CancellationToken cancellation);
}

public sealed class PresetPublication(ScopeStore consumer, ScopeStore target, string serviceId)
{
    private const string Registry = "local/recipes.json";
    public string IniPath => target.Context.PathFor(target.Context.IsGlobal
        ? $"services/{serviceId}/local/runtime/presets.ini" : $"local/runtime/services/{serviceId}/presets.ini");

    internal static JsonObject Service(ScopeSnapshot state, string id) => state.Documents.GetValueOrDefault(Registry)?["data"]?["recipes"]?.AsArray()
        .SelectMany(r => r!["entities"]!.AsArray()).SingleOrDefault(e => e!["kind"]!.GetValue<string>() == "service" && e["id"]!.GetValue<string>() == id)?.AsObject()
        ?? throw new StorageException("unknown-service", "Servicio no registrado.");

    public static string Render(JsonArray contributions) => "version=1\n" + string.Concat(contributions.SelectMany(c => c?["presets"] as JsonArray
        ?? throw new StorageException("publication-snapshot-required", "La contribución no conserva definiciones publicables; se exige reconciliación explícita."))
        .OrderBy(p => p!["name"]!.GetValue<string>(), StringComparer.Ordinal).Select(p => p!["ini"]!.GetValue<string>()));

    public static string CurrentIni(ScopeStore store, string serviceId)
    {
        var service = Service(store.Read(), serviceId);
        if (service["publication-operation"] is not null) throw new StorageException("publication-pending", "Publicación pendiente; observar antes de iniciar otra composición.");
        return Render(service["consumers"] as JsonArray ?? []);
    }

    public async Task<PublicationResult> PublishAsync(string recipeId, PublishedPreset[] presets, string combination,
        string recipeName, bool stopped, bool interactive, Func<string, bool> confirm,
        IPresetRouter? router, Action? validate = null, CancellationToken cancellation = default, Action<PublicationStage>? fault = null,
        long? expectedConsumerRevision = null, bool skipWarmup = false, bool resumeWarmup = false)
    {
        if (!interactive) throw new StorageException("interaction-required", "La publicación requiere interacción.");
        using var lifecycle = new ScopedServiceProcess(target).Lease(serviceId);
        var own = consumer.Read(); var remote = Same ? own : target.Read();
        if (expectedConsumerRevision is long revision && own.Revision != revision)
            throw new StorageException("publication-conflict", "Cambió la contribución antes de restaurar sus entradas.");
        var recipe = Recipe(own, recipeId); var service = Service(remote, serviceId);
        if (recipe["publication-operation"] is JsonNode pending && pending["operation-id"]?.GetValue<string>() != service["publication-operation"]?["operation-id"]?.GetValue<string>())
            throw new StorageException("publication-pending", "El consumidor conserva una publicación pendiente en otro destino.");
        if (stopped && remote.Documents.GetValueOrDefault(target.RuntimeDocumentPath(serviceId, "active"))?["data"]?["terminated"]?.GetValue<bool>() == false ||
            stopped && service["instance"] is not null)
            throw new StorageException("instance-conflict", "El destino ya no está detenido.");
        if (service["publication-operation"] is not null) return await ReconcileCoreAsync(recipeId, router, stopped, cancellation);
        var before = (service["consumers"] as JsonArray ?? []).DeepClone().AsArray();
        var owner = recipe["consumer-id"]!.GetValue<string>();
        var previous = before.SingleOrDefault(c => c!["consumer-id"]!.GetValue<string>() == owner);
        if (previous is not null && previous["scope-id"]?.GetValue<string>() != own.ScopeId)
            throw new StorageException("publication-conflict", "El propietario no coincide con el ámbito comprobado.");
        var after = before.DeepClone().AsArray();
        var replaced = after.SingleOrDefault(c => c!["consumer-id"]!.GetValue<string>() == owner);
        if (replaced is not null) after.Remove(replaced);
        foreach (var preset in presets)
            if (after.SelectMany(c => c!["presets"]!.AsArray()).Any(p => p!["name"]!.GetValue<string>() == preset.Name))
                throw new StorageException("preset-name-conflict", "El nombre pertenece a otro consumidor.");
        var proposed = new JsonArray(presets.Select(p => (JsonNode?)new JsonObject
        { ["name"] = p.Name, ["ini"] = p.Ini, ["expected-arguments"] = Strings(p.ExpectedArguments),
            ["resources"] = p.Resources.DeepClone(), ["warmup"] = p.Warmup }).ToArray());
        if (presets.Length != 0) after.Add((JsonNode)new JsonObject { ["consumer-id"] = owner, ["scope-id"] = own.ScopeId,
            ["source-base"] = consumer.Context.Base, ["source-root"] = consumer.Context.Root, ["source-global"] = consumer.Context.IsGlobal,
            ["presets"] = proposed, ["names"] = Strings(presets.Select(p => p.Name)) });
        var oldEntries = previous?["presets"] as JsonArray ?? [];
        var affected = oldEntries.Concat(proposed).Select(p => p!["name"]!.GetValue<string>()).Distinct(StringComparer.Ordinal)
            .Where(name => !JsonNode.DeepEquals(oldEntries.SingleOrDefault(p => p!["name"]!.GetValue<string>() == name),
                proposed.SingleOrDefault(p => p!["name"]!.GetValue<string>() == name))).ToArray();
        foreach (var preset in presets.Where(p => affected.Contains(p.Name, StringComparer.Ordinal)))
            RecipeRegistry.ValidateRuntimeBindings(own, preset.Resources);
        if (affected.Length == 0)
        {
            if (!stopped && router is not null)
            {
                var observed = await router.ObserveAsync(cancellation);
                if (ConsumerMismatch(before, observed, owner, candidate: after) is { } difference) throw Conflict(difference);
                var names = presets.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                var failures = observed.Where(p => names.Contains(p!["id"]!.GetValue<string>()) && p["status"]?["failed"]?.GetValue<bool>() == true)
                    .Select(p => p!["id"]!.GetValue<string>()).ToArray();
                if (failures.Length != 0) return new(true, true, failures);
                if (resumeWarmup && !skipWarmup)
                {
                    var pendingWarmup = presets.Where(p => p.Warmup && observed.SingleOrDefault(o => o!["id"]!.GetValue<string>() == p.Name)?["status"]?["value"]?.GetValue<string>() != "loaded").ToArray();
                    if (pendingWarmup.Length != 0)
                    {
                        if (await router.ActivityAsync(pendingWarmup.Select(p => p.Name).ToArray(), cancellation) is string activity && !confirm(activity)) throw new OperationCanceledException();
                        var failed = new List<string>();
                        foreach (var preset in pendingWarmup)
                            if (!await router.WarmupAsync(preset.Name, cancellation)) failed.Add(preset.Name);
                        if (failed.Count != 0) return new(true, true, failed.ToArray());
                    }
                }
            }
            return new(true);
        }
        if (!stopped)
        {
            if (router is null) throw new StorageException("router-required", "Falta adaptador de publicación.");
            try
            {
                var observed = await router.ObserveAsync(cancellation);
                if (ConsumerMismatch(before, observed, owner, candidate: after) is { } difference) throw Conflict(difference);
            }
            catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !cancellation.IsCancellationRequested) { throw new StorageException("router-unreachable", "No se pudo consultar el catálogo del servidor."); }
            var activity = await router.ActivityAsync(affected, cancellation);
            if (activity is not null && !confirm(activity)) throw new OperationCanceledException("Publicación cancelada.");
        }
        else if (!confirm("Servicio detenido; se modifica la composición para su próximo inicio.")) throw new OperationCanceledException();
        cancellation.ThrowIfCancellationRequested();
        validate?.Invoke();
        var operation = new JsonObject { ["operation-id"] = ProjectDocuments.Id(), ["consumer-id"] = owner,
            ["recipe-id"] = recipeId, ["phase"] = "prepared", ["before"] = before, ["after"] = after,
            ["service-id"] = serviceId, ["target-root"] = target.Context.Root, ["target-base"] = target.Context.Base,
            ["global"] = target.Context.IsGlobal, ["target-scope-id"] = remote.ScopeId,
            ["failure-restoration"] = skipWarmup,
            ["affected"] = Strings(affected), ["combination"] = combination, ["recipe-name"] = recipeName,
            ["ini-before"] = Render(before), ["ini-candidate"] = Render(after) };
        Commit(own, remote, recipeId, (r, s) => { r["publication-operation"] = operation.DeepClone(); s["publication-operation"] = operation.DeepClone(); }, validate);
        PersistOperation(operation);
        fault?.Invoke(PublicationStage.Reserved);
        // Prepared is durable before replacing the operative file. A crash in this
        // interval is reconciled by observation, never an automatic second reload.
        WriteIni(IniPath, Render(after));
        fault?.Invoke(PublicationStage.IniPublished);
        if (stopped) { Finish(recipeId, operation, after, "withdrawn"); return new(true, ChangedEntries: affected); }
        SetPhase(recipeId, operation, "sent");
        bool accepted;
        try { accepted = await router!.ReloadAsync(cancellation); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        { SetPhase(recipeId, operation, "uncertain"); return new(false); }
        fault?.Invoke(PublicationStage.Sent);
        if (!accepted)
        {
            // A definitive rejection may be restored once; only affected entries
            // changed, because the reservation blocks other composition writers.
            WriteIni(IniPath, Render(before));
            operation["restoring"] = true;
            SetPhase(recipeId, operation, "sent");
            try { await router!.ReloadAsync(cancellation); }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
            { SetPhase(recipeId, operation, "uncertain"); return new(false); }
            var restored = await ReconcileCoreAsync(recipeId, router, false, cancellation);
            if (!restored.Confirmed) return restored;
            throw new StorageException("publication-rejected", "El router rechazó la publicación; composición anterior restaurada sin reiniciar.");
        }
        var result = await ReconcileCoreAsync(recipeId, router, false, cancellation);
        if (!result.Confirmed || result.Restored) return result;
        var failedEntries = new List<string>(result.FailedEntries ?? []);
        foreach (var preset in presets.Where(p => !skipWarmup && p.Warmup && affected.Contains(p.Name, StringComparer.Ordinal) && !failedEntries.Contains(p.Name, StringComparer.Ordinal)))
            if (!await router!.WarmupAsync(preset.Name, cancellation)) failedEntries.Add(preset.Name);
        if (failedEntries.Count != 0)
        {
            var restoration = RestoreEntries(after, before, owner, failedEntries.ToHashSet(StringComparer.Ordinal));
            operation["restoring"] = true;
            operation["restore"] = restoration;
            operation["failed-entries"] = Strings(failedEntries);
            operation["warmup-failed"] = true;
            operation["phase"] = "prepared";
            var current = consumer.Read(); var remoteCurrent = Same ? current : target.Read();
            Commit(current, remoteCurrent, recipeId, (r, s) =>
            { r["publication-operation"] = operation.DeepClone(); s["publication-operation"] = operation.DeepClone(); });
            WriteIni(IniPath, Render(restoration));
            SetPhase(recipeId, operation, "sent");
            try { await router!.ReloadAsync(cancellation); }
            catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
            { SetPhase(recipeId, operation, "uncertain"); return new(false, true, failedEntries.ToArray()); }
            var restored = await ReconcileCoreAsync(recipeId, router, false, cancellation);
            return new(restored.Confirmed, true, failedEntries.ToArray(), restored.Confirmed);
        }
        return new(true, ChangedEntries: affected);
    }

    // A later operation/observer may discover load failure after publication.
    // Rebuild only this consumer's affected entries against today's composition.
    public async Task<PublicationResult?> RestoreFailedAsync(string recipeId, IPresetRouter router, bool interactive,
        Func<string, bool> confirm, CancellationToken cancellation = default)
    {
        var own = consumer.Read(); var recipe = Recipe(own, recipeId);
        if (recipe["publication-operation"] is not null) return null;
        var previous = recipe["publication-previous"];
        var current = Service(target.Read(), serviceId)["consumers"]?.AsArray()
            .SingleOrDefault(c => c!["consumer-id"]!.GetValue<string>() == recipe["consumer-id"]!.GetValue<string>());
        if (previous is null || current is null || previous["service-id"]?.GetValue<string>() != serviceId) return null;
        var eligible = previous["affected"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        JsonArray observed;
        try { observed = await router.ObserveAsync(cancellation); }
        catch (HttpRequestException) { return null; }
        var affected = observed.Where(p => eligible.Contains(p!["id"]!.GetValue<string>()) && p["status"]?["failed"]?.GetValue<bool>() == true)
            .Select(p => p!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        if (affected.Count == 0) return null;
        var definitions = current["presets"]!.AsArray().Where(p => !affected.Contains(p!["name"]!.GetValue<string>()))
            .Concat(previous["presets"]!.AsArray().Where(p => affected.Contains(p!["name"]!.GetValue<string>()))).ToArray();
        var currentDefinitions = current["presets"]!.AsArray();
        if (definitions.Length == currentDefinitions.Count && definitions.All(p => JsonNode.DeepEquals(p,
            currentDefinitions.SingleOrDefault(c => c!["name"]!.GetValue<string>() == p!["name"]!.GetValue<string>()))))
            return null; // No earlier distinct composition remains; do not block an explicit correction in a loop.
        var restored = definitions.Select(p => new PublishedPreset(p!["name"]!.GetValue<string>(), p["ini"]!.GetValue<string>(),
            p["expected-arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray(), p["resources"]!.DeepClone().AsArray(), p["warmup"]!.GetValue<bool>())).ToArray();
        var result = await PublishAsync(recipeId, restored, previous["combination"]!.GetValue<string>(), previous["recipe-name"]!.GetValue<string>(),
            false, interactive, confirm, router, cancellation: cancellation, expectedConsumerRevision: own.Revision, skipWarmup: true);
        return new(result.Confirmed, true, affected.ToArray(), result.Confirmed);
    }

    private static JsonArray RestoreEntries(JsonArray current, JsonArray previous, string owner, HashSet<string> affected)
    {
        var result = current.DeepClone().AsArray();
        var contribution = result.SingleOrDefault(c => c!["consumer-id"]!.GetValue<string>() == owner);
        if (contribution is null) return result;
        var old = previous.SingleOrDefault(c => c!["consumer-id"]!.GetValue<string>() == owner);
        var restored = new JsonArray(contribution["presets"]!.AsArray().Where(p => !affected.Contains(p!["name"]!.GetValue<string>()))
            .Concat((old?["presets"] as JsonArray ?? []).Where(p => affected.Contains(p!["name"]!.GetValue<string>())))
            .Select(p => p!.DeepClone()).ToArray());
        if (restored.Count == 0) result.Remove(contribution);
        else { contribution["presets"] = restored; contribution["names"] = Strings(restored.Select(p => p!["name"]!.GetValue<string>())); }
        return result;
    }

    public async Task<PublicationResult> ReconcileAsync(string recipeId, IPresetRouter? router, bool stopped, CancellationToken cancellation = default)
    {
        using var lifecycle = new ScopedServiceProcess(target).Lease(serviceId);
        return await ReconcileCoreAsync(recipeId, router, stopped, cancellation);
    }

    private async Task<PublicationResult> ReconcileCoreAsync(string recipeId, IPresetRouter? router, bool stopped, CancellationToken cancellation)
    {
        var state = target.Read();
        if (stopped && Service(state, serviceId)["instance"] is not null)
            throw new StorageException("instance-conflict", "El destino ya no está detenido.");
        var operation = Service(state, serviceId)["publication-operation"]?.DeepClone().AsObject();
        if (operation is null) return new(true);
        var recipe = Recipe(consumer.Read(), recipeId);
        if (operation["consumer-id"]!.GetValue<string>() != recipe["consumer-id"]!.GetValue<string>())
            throw new StorageException("publication-pending", "Otro consumidor conserva una publicación pendiente.");
        var expected = (operation["restoring"]?.GetValue<bool>() == true ? operation["restore"] ?? operation["before"] : operation["after"])!.AsArray();
        if (stopped)
        {
            WriteIni(IniPath, Render(expected)); Finish(recipeId, operation, expected, "stopped"); return new(true);
        }
        if (router is null) return new(false);
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                var observed = await router.ObserveAsync(cancellation);
                var owner = operation["consumer-id"]!.GetValue<string>();
                if (ConsumerMismatch(expected, observed, owner, operation["before"]!.AsArray(), operation["after"]!.AsArray()) is null)
                {
                    var affected = operation["affected"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                    var restoring = operation["restoring"]?.GetValue<bool>() == true || operation["failure-restoration"]?.GetValue<bool>() == true;
                    var failures = restoring ? [] : observed
                        .Where(p => affected.Contains(p!["id"]!.GetValue<string>()) && p["status"]?["failed"]?.GetValue<bool>() == true)
                        .Select(p => p!["id"]!.GetValue<string>()).ToArray();
                    var restoredFailure = restoring && (operation["warmup-failed"]?.GetValue<bool>() == true || operation["failure-restoration"]?.GetValue<bool>() == true);
                    if (restoredFailure) failures = (operation["failed-entries"] ?? operation["affected"])!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
                    Finish(recipeId, operation, expected, "observed"); return new(true, failures.Length != 0, failures, restoring);
                }
                if (operation["phase"]?.GetValue<string>() == "prepared" &&
                    ConsumerMismatch(operation["before"]!.AsArray(), observed, owner, candidate: operation["after"]!.AsArray()) is null)
                {
                    WriteIni(IniPath, Render(operation["before"]!.AsArray()));
                    Finish(recipeId, operation, operation["before"]!.AsArray(), "not-sent");
                    return new(true);
                }
                await Task.Delay(200, cancellation);
            } while (timer.Elapsed < TimeSpan.FromSeconds(120));
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException) { }
        SetPhase(recipeId, operation, "uncertain"); return new(false);
    }

    public static bool Matches(JsonArray expected, JsonArray observed, JsonArray? previous = null, ParameterDefinition[]? definitions = null) =>
        Mismatch(expected, observed, previous, definitions) is null;

    // Sharing a router does not share the consumer's validation scope. Include old
    // and proposed names to detect incomplete withdrawal and occupied new names,
    // but never validate another consumer's configuration or consult its catalog.
    private string? ConsumerMismatch(JsonArray expected, JsonArray observed, string owner,
        JsonArray? previous = null, JsonArray? candidate = null)
    {
        JsonArray Owned(JsonArray? composition) => new((composition ?? []).Where(c =>
            c!["consumer-id"]!.GetValue<string>() == owner).Select(c => c!.DeepClone()).ToArray());
        var ownExpected = Owned(expected);
        var ownPrevious = Owned(previous);
        var names = ownExpected.Concat(ownPrevious).Concat(Owned(candidate))
            .SelectMany(c => c!["presets"]!.AsArray()).Select(p => p!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var ownObserved = new JsonArray(observed.Where(p => names.Contains(p?["id"]?.GetValue<string>() ?? ""))
            .Select(p => p!.DeepClone()).ToArray());
        return Mismatch(ownExpected, ownObserved, previous is null ? null : ownPrevious,
            consumer.Context.IsGlobal ? Catalog() : ParameterCatalog.Definitions);
    }

    // Why the server's catalog differs from the registered composition, in words a user
    // can act on; null when it matches. Argument names are compared through the aliases
    // of the parameter catalog, the same that validates recipes and `param show` lists, so
    // another accepted name for the same argument is equal.
    public static string? Mismatch(JsonArray expected, JsonArray observed, JsonArray? previous = null, ParameterDefinition[]? definitions = null)
    {
        var entries = expected.SelectMany(c => c!["presets"]!.AsArray()).ToArray();
        definitions ??= Catalog();
        string Key(string key) => ParameterCatalog.Comparison(key, "", definitions).Key;
        var registered = entries.Select(e => e!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        if (observed.Select(p => p?["id"]?.GetValue<string>()).FirstOrDefault(id => id is not null && !registered.Contains(id)) is { } extra)
            return $"el servidor ofrece «{extra}», que Coquena no publicó.";
        foreach (var entry in entries)
        {
            var name = entry!["name"]!.GetValue<string>();
            var found = observed.SingleOrDefault(p => p?["id"]?.GetValue<string>() == name);
            if (found is null) return $"falta «{name}» en el servidor.";
            if (found["status"]?["preset"]?.GetValue<string>() is string actualIni && entry["ini"]?.GetValue<string>() is string expectedIni)
            {
                var actual = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in IniValues(actualIni))
                {
                    var normalized = ParameterCatalog.Comparison(pair.Key, pair.Value, definitions);
                    if (actual.TryGetValue(normalized.Key, out var duplicate) && duplicate != normalized.Value)
                        return $"en «{name}», el servidor informa valores contradictorios para {normalized.Key}.";
                    actual[normalized.Key] = normalized.Value;
                }
                foreach (var (key, value) in IniValues(expectedIni))
                {
                    var normalized = ParameterCatalog.Comparison(key, value, definitions);
                    if (!actual.TryGetValue(normalized.Key, out var current)) return $"en «{name}», el servidor no tiene {key} = {value}" +
                        (actual.Where(pair => pair.Value == value).Select(pair => pair.Key).FirstOrDefault() is { } other ? $" (tiene {other} = {value})." : ".");
                    if (current != normalized.Value) return $"en «{name}», {key} vale {current} en el servidor y {value} en la receta.";
                }
                var prior = previous?.SelectMany(c => c!["presets"]!.AsArray()).SingleOrDefault(p => p!["name"]!.GetValue<string>() == name);
                var wanted = IniValues(expectedIni).Keys.Select(Key).ToHashSet(StringComparer.Ordinal);
                if (prior?["ini"]?.GetValue<string>() is string priorIni &&
                    IniValues(priorIni).Keys.FirstOrDefault(k => !wanted.Contains(Key(k)) && actual.ContainsKey(Key(k))) is { } stale)
                    return $"en «{name}», el servidor todavía usa {stale}, que la receta ya no define.";
                continue;
            }
            var args = found["status"]?["args"] as JsonArray;
            var tokens = entry["expected-arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray();
            if (tokens.Length == 0 && entry["ini"]?.GetValue<string>() is string definition && IniValues(definition).Count != 0)
                return $"el servidor no informa la configuración de «{name}».";
            if (tokens.Length != 0 && (args is null || !Subsequence(tokens, args.Select(a => a!.GetValue<string>()).ToArray())))
                return $"«{name}» se ejecuta con argumentos distintos de los publicados.";
        }
        return null;
    }
    private static Dictionary<string, string> IniValues(string ini) => ini.Split('\n').Where(l => l.Contains('=') && !l.StartsWith('['))
        .Select(l => l.Split('=', 2)).ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.Ordinal);
    // The global parameter catalog; without a readable one, the built-in definitions.
    private static ParameterDefinition[] Catalog()
    {
        try { return new ParameterManager(Home.Resolve()).List(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or StorageException or ConfigurationException or ContractException)
        { return ParameterCatalog.Definitions; }
    }
    // Only a difference in this consumer can block its publication.
    private static StorageException Conflict(string difference) => new("remote-composition-conflict",
        "La contribución de esta receta en el servidor no coincide con lo que Coquena publicó: " + difference +
        " No se modificó nada. Revisar los argumentos de esta receta y la configuración que informa el servidor.");

    private static bool Subsequence(string[] wanted, string[] actual)
    {
        var position = 0;
        foreach (var token in actual) if (position < wanted.Length && token == wanted[position]) position++;
        return position == wanted.Length;
    }
    private bool Same => consumer.Context.PhysicalRoot == target.Context.PhysicalRoot;
    private static JsonObject Recipe(ScopeSnapshot state, string id) => state.Documents[Registry]["data"]!["recipes"]!.AsArray()
        .Single(r => r!["recipe-id"]!.GetValue<string>() == id)!.AsObject();
    private void Commit(ScopeSnapshot own, ScopeSnapshot remote, string recipeId, Action<JsonObject, JsonObject> update, Action? validate = null)
    {
        var ownDocs = RecipeRegistry.Next(own); var targetDocs = Same ? ownDocs : RecipeRegistry.Next(remote);
        var ownNext = own with { Documents = ownDocs }; var targetNext = remote with { Documents = targetDocs };
        update(Recipe(ownNext, recipeId), Service(targetNext, serviceId));
        var active = targetDocs.GetValueOrDefault(target.RuntimeDocumentPath(serviceId, "active"))?["data"];
        if (active is not null && active["terminated"]?.GetValue<bool>() != true)
            active["preset-composition"] = Service(targetNext, serviceId)["consumers"]?.DeepClone() ?? new JsonArray();
        if (Same) ScopeStore.CommitTogether([new(consumer, own.Revision, ownDocs, Validate: validate)]);
        else ScopeStore.CommitTogether([new(consumer, own.Revision, ownDocs, Validate: validate), new(target, remote.Revision, targetDocs)]);
    }
    private void SetPhase(string recipeId, JsonObject operation, string phase)
    {
        operation["phase"] = phase;
        var own = consumer.Read(); var other = Same ? own : target.Read();
        Commit(own, other, recipeId, (r, s) =>
        {
            if (s["publication-operation"]?["operation-id"]?.GetValue<string>() != operation["operation-id"]!.GetValue<string>())
                throw new StorageException("publication-conflict", "La operación cambió.");
            r["publication-operation"] = operation.DeepClone(); s["publication-operation"] = operation.DeepClone();
        });
        PersistOperation(operation);
    }
    private void Finish(string recipeId, JsonObject operation, JsonArray composition, string status)
    {
        var own = consumer.Read(); var other = Same ? own : target.Read();
        Commit(own, other, recipeId, (r, s) =>
        {
            if (s["publication-operation"]?["operation-id"]?.GetValue<string>() != operation["operation-id"]!.GetValue<string>())
                throw new StorageException("publication-conflict", "La operación cambió.");
            s["consumers"] = composition.DeepClone(); s.Remove("publication-operation"); r.Remove("publication-operation");
            var contribution = composition.SingleOrDefault(c => c!["consumer-id"]!.GetValue<string>() == r["consumer-id"]!.GetValue<string>());
            var prior = operation["before"]!.AsArray().SingleOrDefault(c => c!["consumer-id"]!.GetValue<string>() == r["consumer-id"]!.GetValue<string>());
            if (contribution is null) r.Remove("publication-previous");
            else if (operation["failure-restoration"]?.GetValue<bool>() == true || operation["warmup-failed"]?.GetValue<bool>() == true)
            {
                // Keep the pre-failure snapshot for successful siblings, but never
                // use the rejected candidate as another fallback for a restored entry.
                if (r["publication-previous"] is JsonNode previous)
                {
                    var restoredNames = (operation["failed-entries"] ?? operation["affected"])!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                    previous["affected"] = Strings(previous["affected"]!.AsArray().Select(n => n!.GetValue<string>()).Where(n => !restoredNames.Contains(n)));
                }
            }
            else if (operation["restoring"]?.GetValue<bool>() != true) r["publication-previous"] = new JsonObject { ["service-id"] = serviceId,
                ["presets"] = prior?["presets"]?.DeepClone() ?? new JsonArray(), ["affected"] = operation["affected"]!.DeepClone(),
                ["combination"] = r["publication"]?["combination"]?.DeepClone() ?? operation["combination"]!.DeepClone(),
                ["recipe-name"] = r["publication"]?["recipe-name"]?.DeepClone() ?? operation["recipe-name"]!.DeepClone() };
            var publication = new JsonObject { ["service-id"] = serviceId, ["target-root"] = target.Context.Root,
                ["target-base"] = target.Context.Base, ["global"] = target.Context.IsGlobal,
                ["target-scope-id"] = other.ScopeId, ["recipe-name"] = operation["recipe-name"]!.DeepClone(),
                ["restored"] = operation["restoring"]?.GetValue<bool>() == true || operation["failure-restoration"]?.GetValue<bool>() == true,
                ["presets"] = contribution?["presets"]?.DeepClone() ?? new JsonArray(),
                ["combination"] = operation["combination"]!.DeepClone(), ["status"] = status };
            r["publication"] = publication.DeepClone();
            var publications = r["publications"] as JsonArray ?? new JsonArray();
            var old = publications.SingleOrDefault(p => p!["service-id"]!.GetValue<string>() == serviceId &&
                (p["target-scope-id"]?.GetValue<string>() == other.ScopeId || p["target-root"]!.GetValue<string>() == target.Context.Root));
            if (old is not null) publications.Remove(old);
            publications.Add((JsonNode)publication);
            if (r["publications"] is null) r["publications"] = publications;
            if (contribution is not null) r["publication-name"] = operation["recipe-name"]!.DeepClone();
            else r.Remove("publication-name");
            r["last-publication-operation"] = operation.DeepClone(); r["last-publication-operation"]!["phase"] = "observed";
        });
        operation["phase"] = "observed";
        PersistOperation(operation);
    }
    private void PersistOperation(JsonObject operation)
    {
        var relative = $"local/operations/{operation["operation-id"]!.GetValue<string>()}/publication.json";
        var document = ProjectDocuments.Create("operation", 1, operation).ToJsonString();
        WriteIni(consumer.Context.PathFor(relative), document);
        if (!Same) WriteIni(target.Context.PathFor(relative), document);
    }
    internal static void WriteIni(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + ProjectDocuments.Id() + ".tmp";
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.Write(Encoding.UTF8.GetBytes(contents)); file.Flush(true); }
        File.Move(temporary, path, true);
    }
    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
}
