using System.Globalization;
using System.Text.Json;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Configuration;

public sealed class ParameterManager(Home home, LocalStore? storage = null)
{
    private readonly LocalStore store = storage ?? new(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    public static readonly string[] Protected = ["host", "port", "metrics"];
    public static ParametersDocument Factory() => new(1, ParameterCatalog.Definitions.Select(d => d with
    {
        Type = d.Type switch { "number" => "decimal", "text" => "string", _ => d.Type },
        Aliases = d.Aliases.ToArray(),
        Choices = [], Support = Protected.Contains(d.Name) ? [] : [new(From: "b11482", Until: "b11482", EvidenceOnly: true)]
    }).ToArray());
    internal ParametersDocument Read() => store.Read(DocumentKind.Parameters, Json.ParametersDocument, Validate) ?? Factory();
    public ParameterDefinition[] List() => store.Inspect(() => Read().Definitions);
    public ParameterDefinition? Find(string name) => ParameterCatalog.Find(name, List());
    public static long Build(string value)
    {
        try { return BackendCatalog.BuildNumber(value); }
        catch (BackendException e) { throw new ConfigurationException(e.Message); }
    }
    public static bool Official(string? distribution) => distribution is "ggml" or "oficial" or
        "ggml-org/llama.cpp" or "https://github.com/ggml-org/llama.cpp";
    public static bool OfficialBackend(BackendRecord backend) => Official(backend.Repository) && backend.Origin == "ggml" &&
        BackendCatalog.ValidFamily(backend.Variant) && backend.Family == (backend.Variant.StartsWith("cuda@", StringComparison.Ordinal) ? "cuda" : backend.Variant);
    public static void Validate(ParametersDocument document)
    {
        if (document.SchemaVersion != 1 || document.Definitions is null) throw new ConfigurationException("Catálogo de parámetros inválido.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in document.Definitions)
        {
            if (d is null || d.Name is null || ParameterCatalog.Canonical(d.Name, []) != d.Name || d.Name.StartsWith('-') ||
                d.Description is null || d.Description.Any(char.IsControl) || d.Aliases is null || d.Choices is null || d.Support is null ||
                d.Type is not ("integer" or "decimal" or "string" or "boolean" or "flag" or "enum" or "reference") ||
                d.Type == "reference" && d.Name != "model" || d.Application is not ("model" or "server") || d.Destination is not ("CLI" or "INI"))
                throw new ConfigurationException("Metadatos de definición inválidos.");
            if (!keys.Add(d.Name)) throw new ConfigurationException($"Nombre/alias duplicado: {d.Name}.");
            foreach (var alias in d.Aliases)
            {
                if (string.IsNullOrEmpty(alias) || ParameterCatalog.Canonical(alias, []) != alias.TrimStart('-') || !keys.Add(alias.TrimStart('-')))
                    throw new ConfigurationException($"Alias inválido o duplicado: {alias}.");
            }
            if (d.Choices.Any(v => v is null) || d.Choices.Distinct(StringComparer.Ordinal).Count() != d.Choices.Length ||
                d.Type == "enum" && d.Choices.Length == 0 || d.Type != "enum" && d.Choices.Length != 0)
                throw new ConfigurationException("Enumeración inválida o vacía.");
            foreach (var choice in d.Choices) ValidateValue(d, choice);
            if (d.Default is not null) ValidateValue(d, d.Default);
            var selectors = new HashSet<string>();
            foreach (var support in d.Support)
            {
                ValidateSupport(support);
                if (!selectors.Add($"{support.Variant}|{support.BackendId}")) throw new ConfigurationException("Asociación duplicada.");
            }
        }
        foreach (var name in Protected)
        {
            var d = document.Definitions.SingleOrDefault(d => d.Name == name);
            var original = Factory().Definitions.Single(x => x.Name == name);
            if (d is null || d.Type != original.Type || d.Application != original.Application || d.Destination != original.Destination ||
                d.Description != original.Description || !d.Aliases.SequenceEqual(original.Aliases) || d.Default is not null ||
                d.Choices!.Length != 0 || d.Support!.Length != 0)
                throw new ConfigurationException($"Definición protegida: {name}.");
        }
    }
    public static void ValidateSupport(ParameterSupport support)
    {
        if (support is null || support.BackendId == Guid.Empty || support.BackendId is not null && support.Variant is not null ||
            support.Variant is not null && !(BackendCatalog.ValidFamily(support.Variant) || support.Variant == "cuda"))
            throw new ConfigurationException("Selector de soporte inválido.");
        var lower = support.From is null ? (long?)null : Build(support.From);
        var upper = support.Until is null ? (long?)null : Build(support.Until);
        if (lower > upper) throw new ConfigurationException("Rango de builds invertido.");
        if (support.EvidenceOnly && (lower is null || lower != upper || support.Variant is not null || support.BackendId is not null))
            throw new ConfigurationException("Evidencia inicial inválida.");
    }
    public static void ValidateValue(ParameterDefinition definition, string value)
    {
        if (value is null || value.Any(char.IsControl)) throw new ConfigurationException("Valor inválido.");
        if (definition.Destination == "INI") ParameterCatalog.Literal(value);
        var valid = definition.Type switch
        {
            "integer" => value == value.Trim() && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
            "decimal" or "number" => value == value.Trim() && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number),
            "boolean" => value is "0" or "1" || bool.TryParse(value, out _),
            "flag" => value is "" or "1" or "true",
            "enum" => definition.Choices?.Contains(value, StringComparer.Ordinal) == true,
            "reference" => Guid.TryParse(value, out var id) && id != Guid.Empty,
            "string" or "text" => true,
            _ => false
        };
        if (!valid) throw new ConfigurationException($"{definition.Name}: valor incompatible con {definition.Type}.");
    }
    internal static bool Compatible(ParameterDefinition definition, string value, string scope)
    {
        if (definition.Application != scope) return false;
        try { ValidateValue(definition, value); return true; }
        catch (ConfigurationException) { return false; }
    }
    public static string Availability(ParameterDefinition? definition, BackendRecord? backend,
        string? distribution = null, string? release = null, string? variant = null)
    {
        if (definition is null) return "desconocido";
        if (Protected.Contains(definition.Name)) return "admitido";
        if (backend is not null)
        {
            if (!OfficialBackend(backend)) return "desconocido";
            distribution = backend.Repository; release = backend.Build; variant = backend.Variant;
        }
        if (release is null || !Official(distribution)) return "desconocido";
        var associations = definition.Support ?? [];
        var selected = EffectiveSupport(definition, backend, variant);
        if (selected is null) return associations.Length == 0 || backend is null && variant is null ? "desconocido" : "no admitido";
        var build = Build(release);
        return (selected.From is null || build >= Build(selected.From)) && (selected.Until is null || build <= Build(selected.Until))
            ? "admitido" : selected.EvidenceOnly ? "desconocido" : "no admitido";
    }
    public static ParameterSupport? EffectiveSupport(ParameterDefinition definition, BackendRecord? backend, string? variant = null)
    {
        variant ??= backend?.Variant;
        var associations = definition.Support ?? [];
        var selected = backend is null ? null : associations.SingleOrDefault(s => s.BackendId == backend.Id);
        selected ??= variant is null ? null : associations.SingleOrDefault(s => s.BackendId is null && s.Variant == variant);
        selected ??= backend?.Family == "cuda" || variant?.StartsWith("cuda@", StringComparison.Ordinal) == true
            ? associations.SingleOrDefault(s => s.BackendId is null && s.Variant == "cuda") : null;
        return selected ?? associations.SingleOrDefault(s => s.BackendId is null && s.Variant is null);
    }
    public void Define(ParameterDefinition definition) => store.Exclusive(() =>
    {
        var catalog = Read();
        if (ParameterCatalog.Find(definition.Name, catalog.Definitions) is not null) throw new ConfigurationException("Definición ya existente; utilice param edit.");
        var next = catalog with { Definitions = [.. catalog.Definitions, definition] };
        Validate(next); ValidateLocalReferences(definition);
        var presets = store.Read(DocumentKind.Presets, Json.PresetsDocument, ContractValidation.Presets)?.Presets ?? [];
        var servers = store.Read(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers)?.Servers ?? [];
        foreach (var p in presets) CheckExisting(p.Name, p.Parameters, "model");
        foreach (var s in servers) CheckExisting(s.Name, s.Parameters.ToDictionary(p => p.Key,
            p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.ToString()), "server");
        store.UpdateLocked(DocumentKind.Parameters, Json.ParametersDocument, Validate, _ => next);
        return true;

        void CheckExisting(string name, Dictionary<string, string> values, string scope)
        {
            var affected = values.Where(p => ParameterCatalog.Canonical(p.Key, next.Definitions) == definition.Name).ToArray();
            if (affected.Length > 1 || affected.Any(p => !Compatible(definition, p.Value, scope)))
                throw new ConfigurationException($"{name}: asignaciones existentes incompatibles o duplicadas para {definition.Name}; corríjalas antes de definir el parámetro.");
        }
    });
    private void ValidateLocalReferences(ParameterDefinition definition)
    {
        var backends = store.Read(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends)?.Backends ?? [];
        foreach (var s in definition.Support ?? [])
            if (s.BackendId is { } id)
            {
                var b = backends.SingleOrDefault(b => b.Id == id) ?? throw new ConfigurationException($"Backend ausente: {id:D}.");
                if (!OfficialBackend(b)) throw new ConfigurationException("Sólo se admiten backends oficiales CPU, CUDA y Vulkan.");
            }
    }
    private sealed record Change(ParametersDocument Parameters, PresetsDocument? Presets, ServersDocument? Servers,
        string[] Effects, string Signature, bool Confirmation, string Description,
        string[] Definitions);

    // Confirmation is outside the writer lease. A concurrent change invalidates the
    // reviewed snapshot and requires a fresh report and a fresh confirmation.
    public bool ChangeDefinition(string name, Func<ParameterDefinition, ParameterDefinition?> edit,
        string description, bool dryRun, TextWriter output, Func<bool>? confirm = null)
        => Apply(() => Analyze(name, edit, description), name, dryRun, output, confirm);

    public bool Reset(bool dryRun, TextWriter output, Func<bool>? confirm = null)
        => Apply(AnalyzeReset, "catálogo de fábrica", dryRun, output, confirm);

    private bool Apply(Func<Change> analyze, string name, bool dryRun, TextWriter output, Func<bool>? confirm)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var plan = store.Inspect(analyze);
            Print(plan, name, output);
            if (dryRun) return false;
            if (plan.Confirmation && !(confirm?.Invoke() ?? false))
            { output.WriteLine("Cancelado; estado conservado. Se requiere confirmación interactiva para aplicar la operación."); return false; }
            var applied = store.Exclusive(() =>
            {
                var current = analyze();
                if (current.Signature != plan.Signature) return false;
                if (current.Presets is null && current.Servers is null)
                    store.UpdateLocked(DocumentKind.Parameters, Json.ParametersDocument, Validate, _ => current.Parameters);
                else store.CommitParametersLocked(current.Parameters, current.Presets, current.Servers);
                return true;
            });
            if (applied) { output.WriteLine("Catálogo y asignaciones actualizados."); return true; }
            output.WriteLine("El estado cambió durante la revisión; se recalcula el impacto.");
        }
        throw new ConfigurationException("El estado sigue cambiando; vuelva a revisar la operación.");
    }
    private Change AnalyzeReset()
    {
        var old = Read(); var factory = Factory(); Validate(factory);
        var presets = store.Read(DocumentKind.Presets, Json.PresetsDocument, ContractValidation.Presets);
        var servers = store.Read(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers);
        var originalPresets = presets; var originalServers = servers;
        var effects = new List<string>(); var definitions = new List<string>();
        string? signature = null;
        // Evaluate every identity against the final catalog, retaining the original
        // aliases throughout analysis. Publish the combined result only once.
        foreach (var definition in old.Definitions.Concat(factory.Definitions.Where(d => !old.Definitions.Any(o => o.Name == d.Name))))
        {
            if (Protected.Contains(definition.Name)) continue;
            var next = factory.Definitions.SingleOrDefault(d => d.Name == definition.Name);
            var change = Analyze(definition.Name, _ => next, "restablecer", old, presets, servers, factory);
            signature ??= change.Signature;
            if (change.Presets is not null) presets = change.Presets;
            if (change.Servers is not null) servers = change.Servers;
            effects.AddRange(change.Effects);
            if (!old.Definitions.Any(d => d.Name == definition.Name))
                definitions.Add($"Recuperar: {Describe(definition)}");
            else if (next is null || ContractJson.Write(new(1, [definition]), Json.ParametersDocument) !=
                ContractJson.Write(new(1, [next]), Json.ParametersDocument)) definitions.AddRange(change.Definitions);
        }
        return new(factory, ReferenceEquals(originalPresets, presets) ? null : presets,
            ReferenceEquals(originalServers, servers) ? null : servers, effects.ToArray(), signature!, true,
            "restablecer definiciones suministradas por esta versión de Coquena", definitions.ToArray());
    }

    private Change Analyze(string name, Func<ParameterDefinition, ParameterDefinition?> edit, string description,
        ParametersDocument? catalogSnapshot = null, PresetsDocument? presetSnapshot = null, ServersDocument? serverSnapshot = null,
        ParametersDocument? reset = null)
    {
        var old = catalogSnapshot ?? Read();
        var definition = old.Definitions.SingleOrDefault(d => d.Name == name) ??
            (reset is null ? ParameterCatalog.Find(name, old.Definitions) : reset.Definitions.SingleOrDefault(d => d.Name == name)) ??
            throw new ConfigurationException("Definición inexistente.");
        if (Protected.Contains(definition.Name)) throw new ConfigurationException($"Definición protegida: {definition.Name}.");
        var next = edit(definition);
        if (next is not null && next.Name != definition.Name) throw new ConfigurationException("No se cambia el nombre canónico.");
        var parameters = reset ?? old with { Definitions = old.Definitions.Where(d => d.Name != definition.Name).Concat(next is null ? [] : [next]).ToArray() };
        Validate(parameters);
        // Existing dangling local references remain diagnostic; only a newly added
        // or changed association must resolve a registered official backend.
        if (next is not null && reset is null) ValidateLocalReferences(next with { Support = (next.Support ?? []).Except(definition.Support ?? []).ToArray() });
        var presets = reset is null ? store.Read(DocumentKind.Presets, Json.PresetsDocument, ContractValidation.Presets) : presetSnapshot;
        var servers = reset is null ? store.Read(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers) : serverSnapshot;
        var config = store.Read(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config);
        var backends = store.Read(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends);
        var effects = new List<string>(); var removed = false; var presetChanged = false; var serverChanged = false;
        var newPresets = presets?.Presets.Select(p =>
        {
            var values = Cascade(p.Parameters, "preset", p.Name, p.Id, "model");
            var modelId = p.ModelId;
            if (definition.Name == "model" && modelId is { } id && (next is null || next.Type != "reference" || next.Application != "model"))
            {
                effects.Add($"Eliminar | preset {p.Name} | {p.Id:D} | model = {id:D} | referencia guardada ModelId; definición retirada/incompatible.");
                modelId = null; removed = true; presetChanged = true;
            }
            if (!p.Parameters.SequenceEqual(values)) presetChanged = true;
            return p with { Parameters = values, ModelId = modelId };
        }).ToArray();
        var newServers = servers?.Servers.Select(s =>
        {
            var original = s.Parameters.ToDictionary(p => p.Key, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.ToString());
            var remaining = Cascade(original, "servidor", s.Name, s.Id, "server");
            var values = remaining.ToDictionary(p => p.Key, p => s.Parameters.TryGetValue(p.Key, out var scalar) ? scalar :
                s.Parameters.Single(v => Identity(v.Key) == p.Key).Value);
            if (!values.Keys.SequenceEqual(s.Parameters.Keys)) serverChanged = true;
            foreach (var preset in presets?.Presets.Where(p => s.PresetIds.Contains(p.Id)) ?? [])
            {
                foreach (var value in preset.Parameters.Where(v => Identity(v.Key) == definition.Name))
                    if (next is null || !Compatible(next, value.Value, "model"))
                        effects.Add($"Servidor afectado | {s.Name} | {s.Id:D} | preset {preset.Name} | {preset.Id:D} | {value.Key} = {value.Value} | se retira la asignación del preset; se conserva la asociación del servidor.");
                if (definition.Name == "model" && preset.ModelId is { } resource && (next is null || next.Type != "reference" || next.Application != "model"))
                    effects.Add($"Servidor afectado | {s.Name} | {s.Id:D} | preset {preset.Name} | {preset.Id:D} | model = {resource:D} | preset sin modelo para próximas ejecuciones; recurso conservado.");
            }
            var selection = s.Backend ?? config?.DefaultBackend;
            var backend = backends?.Backends.SingleOrDefault(b => selection?.Kind == "fixed" ? b.Id == selection.BackendId :
                selection?.Kind == "latest" && b.Origin == selection.Origin && b.Variant == selection.Variant && b.TracksLatest);
            var availability = Availability(next, backend);
            var previousAvailability = Availability(reset is null ? definition : old.Definitions.SingleOrDefault(d => d.Name == definition.Name), backend);
            if (next is not null && backend is not null && availability != previousAvailability && (reset is not null || availability == "no admitido"))
            {
                var emission = availability switch { "no admitido" => "omitir con warning", "admitido" => "emitir", _ => "disponibilidad desconocida" };
                foreach (var preset in presets?.Presets.Where(p => s.PresetIds.Contains(p.Id)) ?? [])
                {
                    foreach (var value in preset.Parameters.Where(v => Identity(v.Key) == definition.Name))
                        if (Compatible(next, value.Value, "model")) effects.Add($"Conservar/{emission} | preset {preset.Name} | {preset.Id:D} | {value.Key} = {value.Value} | servidor {s.Name} | {s.Id:D} | backend {backend.Id:D}/{backend.Build}.");
                    if (definition.Name == "model" && preset.ModelId is { } resource && next.Type == "reference" && next.Application == "model")
                        effects.Add($"Conservar/{emission} | preset {preset.Name} | {preset.Id:D} | model = {resource:D} | servidor {s.Name} | {s.Id:D} | backend {backend.Id:D}/{backend.Build}.");
                }
                foreach (var value in original.Where(v => Identity(v.Key) == definition.Name))
                    if (Compatible(next, value.Value, "server")) effects.Add($"Conservar/{emission} | servidor {s.Name} | {s.Id:D} | {value.Key} = {value.Value} | backend {backend.Id:D}/{backend.Build}.");
            }
            return s with { Parameters = values };
        }).ToArray();
        var signature = ContractJson.Write(old, Json.ParametersDocument) +
            (presets is null ? "null" : ContractJson.Write(presets, Json.PresetsDocument)) +
            (servers is null ? "null" : ContractJson.Write(servers, Json.ServersDocument)) +
            (config is null ? "null" : ContractJson.Write(config, Json.ConfigDocument)) +
            (backends is null ? "null" : ContractJson.Write(backends, Json.BackendsDocument));
        return new(parameters, presetChanged ? presets! with { Presets = newPresets! } : null,
            serverChanged ? servers! with { Servers = newServers! } : null, effects.ToArray(), signature, removed, description,
            ["Antes: " + Describe(definition), "Después: " + (next is null ? "definición eliminada" : Describe(next))]);

        string Identity(string key) => ParameterCatalog.Canonical(key,
            reset is not null && ParameterCatalog.Find(key, old.Definitions) is null ? reset.Definitions : old.Definitions);

        Dictionary<string, string> Cascade(Dictionary<string, string> values, string entity, string entityName, Guid id, string scope)
        {
            var result = new Dictionary<string, string>(values);
            foreach (var value in values.Where(v => Identity(v.Key) == definition.Name))
                if (next is null || !Compatible(next, value.Value, scope))
                {
                    effects.Add($"Eliminar | {entity} {entityName} | {id:D} | {value.Key} = {value.Value} | definición {(next is null ? "retirada" : "incompatible") }.");
                    result.Remove(value.Key); removed = true;
                }
                else if (value.Key != definition.Name)
                {
                    // Legacy documents may contain aliases. Persist the canonical key
                    // before changing aliases so the assignment keeps its identity.
                    result.Remove(value.Key);
                    if (!result.TryAdd(definition.Name, value.Value)) throw new ConfigurationException("Asignaciones duplicadas por alias; corrija el documento antes de editar.");
                }
            return result;
        }
    }
    private static void Print(Change plan, string name, TextWriter output)
    {
        output.WriteLine($"Impacto: {name} | {plan.Description}");
        foreach (var line in plan.Definitions) output.WriteLine(line);
        foreach (var line in plan.Effects) output.WriteLine(line);
        if (plan.Effects.Length == 0) output.WriteLine("Sin asignaciones afectadas; las existentes se conservan.");
        if (plan.Effects.Any(e => e.StartsWith("Eliminar |", StringComparison.Ordinal)))
            output.WriteLine("Advertencia: la operación elimina asignaciones en cascada. Entidades, recursos e instancias activas se conservan.");
        else if (plan.Confirmation) output.WriteLine("Se requiere confirmación para restablecer el catálogo; las asignaciones se conservan.");
    }
    private static string Describe(ParameterDefinition d) => $"{d.Name} | {d.Type} | {d.Application}/{d.Destination} | descripción: {d.Description} | default: {d.Default ?? "sin default"} | alias: {string.Join(", ", d.Aliases)} | values: {string.Join(", ", d.Choices ?? [])} | soporte: " +
        string.Join("; ", (d.Support ?? []).Select(s => $"{s.BackendId?.ToString() ?? s.Variant ?? "general oficial"} [{s.From ?? "abierto"}, {s.Until ?? "abierto"}]{(s.EvidenceOnly ? " evidencia inicial" : "")}"));
    public bool Support(string name, ParameterSupport support, bool remove, bool dryRun, TextWriter output, Func<bool>? confirm = null)
    {
        ValidateSupport(support);
        return ChangeDefinition(name, d =>
        {
            if (!remove) ValidateLocalReferences(d with { Support = [support] });
            return d with { Support = (d.Support ?? []).Where(s => s.BackendId != support.BackendId || s.Variant != support.Variant)
                .Concat(remove ? [] : [support]).ToArray() };
        },
            $"support {(remove ? "remove" : "set")} {support.Variant ?? support.BackendId?.ToString() ?? "general oficial"} [{support.From ?? "abierto"}, {support.Until ?? "abierto"}]", dryRun, output, confirm);
    }
}
