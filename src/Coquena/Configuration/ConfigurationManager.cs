using System.Text;
using Coquena.Contracts;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena.Configuration;

public sealed record PresetView(PresetRecord Preset, ModelRecord? Model,
    Dictionary<string, string> Own, Dictionary<string, string> Effective, string[] Diagnostics);
public sealed record ComposedPresets(string Ini, Guid[] ResourceIds, string[] Diagnostics, string[]? CliArguments = null);

public sealed class ConfigurationManager(Home home, LocalStore? storage = null)
{
    private readonly LocalStore store = storage ?? new LocalStore(home);
    public ParameterManager Parameters => new(home, store);
    public BackendDirectoryManager BackendDirectory => new(home, store);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private ConfigDocument Config() => store.Read(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config) ?? new(1, null, null, new(5));
    private ModelsDocument Models() => store.Read(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models) ?? new(1, []);
    private PresetsDocument Presets() => store.Read(DocumentKind.Presets, Json.PresetsDocument, ContractValidation.Presets) ?? new(1, []);

    private static T Named<T>(IEnumerable<T> entries, string name, Func<T, string> getName) where T : class =>
        entries.SingleOrDefault(e => getName(e).Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ConfigurationException($"Referencia ausente: {name}.");

    public BackendRecord? DefaultBackend()
    {
        var selection = Config().DefaultBackend;
        if (selection is null) return null;
        var backends = store.Read(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends)?.Backends ?? [];
        var matches = backends.Where(b => selection.Kind == "fixed" ? b.Id == selection.BackendId :
            b.Origin == selection.Origin && b.Variant == selection.Variant && b.TracksLatest && b.Platform == "win-x64").ToArray();
        if (matches.Length > 1) throw new ConfigurationException("Selección predeterminada de backend ambigua.");
        return matches.SingleOrDefault();
    }

    public BackendRecord Backend(Guid id) =>
        store.Read(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends)?.Backends.SingleOrDefault(b => b.Id == id)
        ?? throw new ConfigurationException($"Backend ausente: {id:D}.");

    // One canonical key per section, including aliases from existing documents.
    private Dictionary<string, string> Values(Dictionary<string, string>? values) => NormalizeValues(values, Parameters.Read().Definitions);
    private static Dictionary<string, string> NormalizeValues(Dictionary<string, string>? values, ParameterDefinition[] definitions)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values ?? [])
        {
            var key = ParameterCatalog.Canonical(pair.Key, definitions);
            ParameterCatalog.ModelParameter(key, definitions);
            if (ParameterCatalog.Find(key, definitions) is { } definition) ParameterManager.ValidateValue(definition, pair.Value);
            else ParameterCatalog.Literal(pair.Value);
            if (!result.TryAdd(key, pair.Value))
                throw new ConfigurationException($"Alias duplicados para {key}; corrija el documento sin perder valores.");
        }
        return result;
    }

    public void Set(string name, string parameter, string? value)
    {
        store.Exclusive(() =>
        {
            var definitions = Parameters.Read().Definitions;
            var key = ParameterCatalog.Canonical(parameter, definitions);
            if (value is not null)
            {
                ParameterCatalog.ModelParameter(key, definitions);
                if (ParameterCatalog.Find(key, definitions) is { } definition) ParameterManager.ValidateValue(definition, value);
                else ParameterCatalog.Literal(value);
            }
            store.UpdateLocked(DocumentKind.Presets, Json.PresetsDocument, ContractValidation.Presets, old =>
            {
                var catalog = old ?? new(1, []);
                var preset = Named(catalog.Presets, name, p => p.Name);
                // Removing the target first also permits repairing a stale INI literal.
                var next = Values(preset.Parameters.Where(p => ParameterCatalog.Canonical(p.Key, definitions) != key).ToDictionary());
                if (value is not null) next[key] = value;
                return catalog with { Presets = catalog.Presets.Select(p => p.Id == preset.Id ? p with { Parameters = next } : p).ToArray() };
            });
            return true;
        });
    }

    public RetentionView Retention() => store.Exclusive(() => LogRetentionPolicy.Resolve(Config().LogRetention));
    public void SetRetention(long? bytes) => store.Update(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config,
        old => (old ?? new(1, null, null, new(5))) with { LogRetention = bytes is null ? null : new(bytes.Value) });

    public PresetRecord Create(string modelName, string name)
    {
        PresetName(name);
        return store.Exclusive(() =>
        {
            var model = Named(Models().Models, modelName, m => m.Name);
            ModelReference();
            var record = new PresetRecord(Guid.NewGuid(), name, model.Id, []);
            store.UpdateLocked(DocumentKind.Presets, Json.PresetsDocument, ContractValidation.Presets, old =>
            {
                var catalog = old ?? new(1, []);
                if (catalog.Presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new ConfigurationException($"Preset ya existente: {name}.");
                return catalog with { Presets = [.. catalog.Presets, record] };
            });
            return record;
        });
    }

    public void ChangeModel(string name, string modelName) => store.Exclusive(() =>
    {
        var model = Named(Models().Models, modelName, m => m.Name);
        ModelReference();
        store.UpdateLocked(DocumentKind.Presets, Json.PresetsDocument, ContractValidation.Presets, old =>
        {
            var catalog = old ?? new(1, []);
            var preset = Named(catalog.Presets, name, p => p.Name);
            return catalog with { Presets = catalog.Presets.Select(p => p.Id == preset.Id ? p with { ModelId = model.Id } : p).ToArray() };
        });
        return true;
    });

    public void Remove(string name) => store.Update(DocumentKind.Presets, Json.PresetsDocument, ContractValidation.Presets, old =>
    {
        var catalog = old ?? new(1, []);
        var preset = Named(catalog.Presets, name, p => p.Name);
        return catalog with { Presets = catalog.Presets.Where(p => p.Id != preset.Id).ToArray() };
    });

    private void ModelReference()
    {
        var definition = ParameterCatalog.Find("model", Parameters.Read().Definitions);
        if (definition is not { Type: "reference", Application: "model" })
            throw new ConfigurationException("La definición model no admite referencias de preset; restablezca el catálogo antes de asignar un modelo.");
    }

    public PresetView[] List(string? modelName = null) => store.Exclusive(() =>
    {
        var models = Models().Models;
        Guid? filter = modelName is null ? null : Named(models, modelName, m => m.Name).Id;
        return Presets().Presets.Where(p => filter is null || p.ModelId == filter).Select(p =>
        {
            var model = models.SingleOrDefault(m => m.Id == p.ModelId);
            return new PresetView(p, model, Values(p.Parameters), Values(p.Parameters),
                model is null ? [p.ModelId is null ? "Preset sin asignación de modelo." : $"Modelo ausente: {p.ModelId:D}; referencia rota."] : []);
        }).ToArray();
    });

    public PresetView View(string name) => Named(List(), name, p => p.Preset.Name);

    // No server lifecycle or invented CLI export command: 011/012 consume this API.
    // Snapshot under the same writer lease; resolves IDs and paths only at composition.
    public ComposedPresets Compose(BackendRecord backend, Guid[] presetIds) => store.Exclusive(() => ComposeLocked(backend, presetIds));

    internal ComposedPresets ComposeLocked(BackendRecord backend, Guid[] presetIds)
    {
        if (presetIds.Length == 0 || presetIds.Distinct().Count() != presetIds.Length)
            throw new ConfigurationException("Se requiere una lista explícita, no vacía y sin duplicados de presets.");
        var common = new Dictionary<string, string>();
        var models = Models().Models;
        var presets = Presets().Presets;
        var sections = new List<(string Name, Dictionary<string, string> Values)>();
        var resources = new List<Guid>();
        var diagnostics = new List<string>();
        var definitions = Parameters.Read().Definitions;
        var cliValues = new List<Dictionary<string, string>>();
        foreach (var id in presetIds)
        {
            var preset = presets.SingleOrDefault(p => p.Id == id)
                ?? throw new ConfigurationException($"Preset ausente: {id:D}; referencia rota.");
            PresetName(preset.Name);
            ModelReference();
            var model = models.SingleOrDefault(m => m.Id == preset.ModelId)
                ?? throw new ConfigurationException($"Modelo ausente: {preset.ModelId:D}; referencia rota en {preset.Name}.");
            var paths = model.Files.Select(f =>
            {
                if (f.LocalPath != $"models/{model.Selection.Repository}/{f.RemotePath}")
                    throw new ConfigurationException("Ruta de modelo fuera de su unidad gestionada.");
                return home.PathFor(f.LocalPath);
            }).ToArray();
            var inspection = GgufInspection.InspectPaths(paths);
            if (inspection.Status != "complete")
                throw new ConfigurationException($"Modelo {model.Name}: {inspection.Status}; no se omite el preset {preset.Name}.");
            var specific = Values(preset.Parameters);
            foreach (var key in specific.Keys.ToArray())
            {
                var availability = ParameterManager.Availability(ParameterCatalog.Find(key, definitions), backend);
                if (availability == "no admitido")
                { specific.Remove(key); diagnostics.Add($"{preset.Name}: {key} no admitido para {backend.Repository}/{backend.Build}; se omite con warning y se conserva la asignación."); }
                else if (availability == "desconocido") diagnostics.Add($"{preset.Name}: {key} desconocido para {backend.Repository}/{backend.Build}; se conserva.");
            }
            var modelDefinition = ParameterCatalog.Find("model", definitions)!;
            var modelAvailability = ParameterManager.Availability(modelDefinition, backend);
            if (modelAvailability == "no admitido") diagnostics.Add($"{preset.Name}: model no admitido; se omite con warning y se conserva la referencia.");
            else
            {
                if (modelAvailability == "desconocido") diagnostics.Add($"{preset.Name}: model desconocido para {backend.Repository}/{backend.Build}; se conserva.");
                specific["model"] = inspection.Parts[0].Path;
            }
            var cli = specific.Where(p => ParameterCatalog.Find(p.Key, definitions)?.Destination == "CLI").ToDictionary();
            cliValues.Add(cli);
            foreach (var key in cli.Keys) specific.Remove(key);
            sections.Add((preset.Name, specific));
            resources.Add(model.Id);
        }
        // CLI has process scope: a model CLI value must agree in every selected
        // preset, including omission, or it cannot represent their independent values.
        var cliKeys = cliValues.SelectMany(v => v.Keys).Distinct().ToArray();
        foreach (var key in cliKeys)
            if (cliValues.Any(v => !v.TryGetValue(key, out var value) || value != cliValues[0].GetValueOrDefault(key)))
                throw new ConfigurationException($"{key}: valores CLI de modelo distintos entre presets; no se puede representar la ejecución sin cambiar sus valores.");
        var arguments = cliValues[0].SelectMany(p => Arguments(ParameterCatalog.Find(p.Key, definitions)!, p.Value)).ToArray();
        return new ComposedPresets(Render(common, sections, definitions), resources.Distinct().ToArray(), diagnostics.Distinct().ToArray(), arguments);
    }

    internal static string[] Arguments(ParameterDefinition definition, string value) => definition.Type == "flag"
        ? ["--" + definition.Name] : ["--" + definition.Name, value];

    public static string Render(Dictionary<string, string> common,
        IEnumerable<(string Name, Dictionary<string, string> Values)> presets, ParameterDefinition[]? definitions = null)
    {
        var builder = new StringBuilder("version = 1\n\n");
        definitions ??= ParameterCatalog.Definitions;
        Section("*", common);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in presets)
        {
            PresetName(preset.Name);
            if (!names.Add(preset.Name)) throw new ConfigurationException("Sección de preset duplicada.");
            // The model path was resolved by Compose, never accepted as config override.
            var values = NormalizeValues(preset.Values.Where(p => p.Key != "model").ToDictionary(), definitions);
            if (preset.Values.TryGetValue("model", out var model)) values["model"] = model;
            Section(preset.Name, values);
        }
        return builder.ToString();

        void Section(string name, Dictionary<string, string> values)
        {
            builder.Append('[').Append(name).Append("]\n");
            foreach (var pair in values.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                ParameterCatalog.Literal(pair.Value);
                builder.Append(pair.Key).Append(" = ").Append(pair.Value).Append('\n');
            }
            builder.Append('\n');
        }
    }

    private static void PresetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Equals("default", StringComparison.OrdinalIgnoreCase) ||
            name == "*" || name.Any(char.IsControl) || name.IndexOfAny(['[', ']', ':', ';', '#']) >= 0)
            throw new ConfigurationException("Nombre de preset no representable en INI; * y default están reservados por llama.cpp.");
    }
}
