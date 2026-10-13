using System.Globalization;
using System.Text.Json;
using Coquena.Backends;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Servers;

public sealed record ServerView(ServerRecord Server, BackendSelection? Selection, BackendRecord? Backend,
    Dictionary<string, JsonElement> Effective, PresetRecord?[] Presets, string[] Diagnostics);
public sealed record ServerStartPlan(ServerRecord Server, BackendRecord Backend,
    Dictionary<string, JsonElement> Parameters, ComposedPresets Presets, ParameterDefinition[]? Definitions = null);

public sealed class ServerManager(Home home, LocalStore? storage = null)
{
    private readonly LocalStore store = storage ?? new LocalStore(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    public static readonly string[] Actions = ["log-retention", "use", "set", "unset", "preset", "show", "remove", "logs", "start", "stop", "restart"];
    public static readonly string[] Reserved = ["create", "default", "list", "ps", .. Actions];
    private ServersDocument Catalog() => store.Read(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers) ?? new(1, []);
    private ConfigDocument Config() => store.Read(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config) ?? new(1, null, null, new(5));
    private void Save(ServersDocument document) => store.UpdateLocked(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers, _ => document);
    internal ServerRecord Named(string? name)
    {
        var catalog = Catalog();
        return (name is null ? catalog.Servers.SingleOrDefault(s => s.Id == Config().DefaultServerId) :
            catalog.Servers.SingleOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            ?? throw new ConfigurationException(name is null ? "Servidor predeterminado ausente." : $"Servidor ausente: {name}.");
    }

    // Only a never-initialized catalog creates the initial default. An empty persisted
    // catalog is intentional; it must never resurrect a removed definition.
    public void EnsureInitial() => store.Exclusive(() =>
    {
        if (store.Read(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers) is null)
        {
            var config = Config();
            if (config.DefaultServerId is not null)
                throw new ConfigurationException("Catálogo de servidores ausente con designación existente; no se reemplaza.");
            var record = new ServerRecord(Guid.NewGuid(), "default", null, [], new() { ["port"] = Scalar("9931") });
            Save(new(1, [record]));
            store.UpdateLocked(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config,
                _ => config with { DefaultServerId = record.Id });
        }
        return true;
    });

    public ServerRecord Create(string name, int? port = null) => store.Exclusive(() =>
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '\\' or ':') ||
            (Reserved.Contains(name, StringComparer.OrdinalIgnoreCase) && !name.Equals("default", StringComparison.OrdinalIgnoreCase)))
            throw new ConfigurationException("Nombre de servidor inválido o reservado.");
        _ = home.PathFor($"run/{name}/instance.json");
        var catalog = Catalog();
        if (catalog.Servers.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ConfigurationException($"Servidor ya existente: {name}.");
        var assigned = port ?? NextPort(catalog);
        CheckPort(catalog, assigned);
        var record = new ServerRecord(Guid.NewGuid(), name, null, [], new() { ["port"] = Scalar(assigned.ToString(CultureInfo.InvariantCulture)) });
        var initial = store.Read(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers) is null;
        var config = Config();
        if (initial && config.DefaultServerId is not null)
            throw new ConfigurationException("Catálogo ausente con designación existente.");
        Save(catalog with { Servers = [.. catalog.Servers, record] });
        if (initial && name.Equals("default", StringComparison.OrdinalIgnoreCase))
            store.UpdateLocked(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config,
                _ => config with { DefaultServerId = record.Id });
        return record;
    });

    public ServerRecord Remove(string? name) => store.Exclusive(() =>
    {
        var record = Named(name);
        var instance = store.Read(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, server: record.Name);
        if (instance is not null)
        {
            using var process = WindowsServerProcess.Verified(instance, out var ended);
            if (!ended)
            {
                if (instance.ServerId != record.Id)
                    throw new ConfigurationException($"Identidad incierta de instancia {instance.InstanceId:D}; servidor distinto, no se retira.");
                throw new ConfigurationException($"Detenga instancia {instance.InstanceId:D} antes de retirar el servidor.");
            }
        }
        var catalog = Catalog(); var config = Config();
        var remaining = catalog with { Servers = catalog.Servers.Where(s => s.Id != record.Id).ToArray() };
        if (config.DefaultServerId == record.Id)
            store.CommitServerRemovalLocked(remaining, config with { DefaultServerId = null });
        else Save(remaining);
        return record;
    });

    private static int NextPort(ServersDocument catalog)
    {
        var maximum = catalog.Servers.Length == 0 ? 9930 : catalog.Servers.Max(s => s.Parameters["port"].GetInt32());
        if (maximum >= 65535) throw new ConfigurationException("Puerto máximo 65535: indique un puerto explícito; sin cambios.");
        return maximum + 1;
    }
    private static void CheckPort(ServersDocument catalog, int port, Guid? except = null)
    {
        if (port is < 1 or > 65535 || catalog.Servers.Any(s => s.Id != except && s.Parameters["port"].GetInt32() == port))
            throw new ConfigurationException("Puerto fuera de rango o ya asignado a otro servidor.");
    }
    public ServerRecord? Default(string? name = null) => store.Exclusive(() =>
    {
        if (name is null) return Catalog().Servers.SingleOrDefault(s => s.Id == Config().DefaultServerId);
        var record = Named(name);
        store.UpdateLocked(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config,
            old => (old ?? new(1, null, null, new(5))) with { DefaultServerId = record.Id });
        return record;
    });
    private ServerRecord Change(string? name, Func<ServerRecord, ServersDocument, ServerRecord> change) => store.Exclusive(() =>
    {
        var record = Named(name); var catalog = Catalog();
        var next = change(record, catalog);
        Save(catalog with { Servers = catalog.Servers.Select(s => s.Id == record.Id ? next : s).ToArray() });
        return next;
    });
    public RetentionView Retention(string? name) => store.Exclusive(() => RetentionLocked(name));
    internal RetentionView RetentionLocked(string? name) => LogRetentionPolicy.Resolve(Config().LogRetention, Named(name).LogRetention);
    public ServerRecord SetRetention(string? name, long? bytes) => Change(name, (record, _) =>
        record with { LogRetention = bytes is null ? null : new(bytes.Value) });

    public ServerRecord Use(string? name, string selector) => Change(name, (record, _) =>
    {
        var selection = selector == "default" ? null : new BackendInstaller(home, store).Resolve(selector);
        if (selection?.Kind == "fixed")
            store.UpdateLocked(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends,
                old => old! with { Backends = old.Backends.Select(b => b.Id == selection.BackendId && b.Ownership == "managed" ? b with { Pinned = true } : b).ToArray() });
        return record with { Backend = selection };
    });

    private static JsonElement Scalar(string value)
    {
        using var parsed = JsonDocument.Parse(value);
        return parsed.RootElement.Clone();
    }
    private static JsonElement Text(string value)
    {
        // JsonDocument gives an owned scalar without reflection-based serialization.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) writer.WriteStringValue(value);
        using var parsed = JsonDocument.Parse(stream.ToArray());
        return parsed.RootElement.Clone();
    }
    public ServerRecord Set(string? name, string parameter, string? value) => Change(name, (record, catalog) =>
    {
        var definitions = new ParameterManager(home, store).Read().Definitions;
        var key = ParameterCatalog.Canonical(parameter, definitions);
        var definition = ParameterCatalog.Find(key, definitions);
        if (definition?.Application != "server" || key == "models-preset")
            throw new ConfigurationException("Parámetro ajeno al catálogo de servidor o gestionado por Coquena.");
        var parameters = new Dictionary<string, JsonElement>(record.Parameters);
        if (key == "port")
        {
            var port = value is null ? NextPort(catalog) : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number : throw new ConfigurationException("Puerto entero requerido.");
            CheckPort(catalog, port, record.Id); parameters[key] = Scalar(port.ToString(CultureInfo.InvariantCulture));
        }
        else if (value is null) parameters.Remove(key);
        else
        {
            ParameterManager.ValidateValue(definition, value);
            if (key is "host" or "models-max" && string.IsNullOrWhiteSpace(value)) throw new ConfigurationException("Valor de servidor inválido.");
            parameters[key] = definition.Type switch
            {
                "boolean" => Scalar(value == "1" || bool.TryParse(value, out var flag) && flag ? "true" : "false"),
                "integer" => Scalar(long.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
                "decimal" => Scalar(double.Parse(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)),
                _ => Text(value)
            };
            if (key == "models-max" && definition.Type == "integer" && parameters[key].GetInt64() <= 0) throw new ConfigurationException("Entero positivo requerido para models-max.");
        }
        return record with { Parameters = parameters };
    });
    public ServerRecord Presets(string? name, string[] names, bool add) => Change(name, (record, _) =>
    {
        var presets = store.Read(DocumentKind.Presets, Json.PresetsDocument, ContractValidation.Presets)?.Presets ?? [];
        var ids = names.Select(n => presets.SingleOrDefault(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase))?.Id
            ?? throw new ConfigurationException($"Preset ausente: {n}.")).ToArray();
        return record with { PresetIds = add ? record.PresetIds.Concat(ids).Distinct().ToArray() : record.PresetIds.Except(ids).ToArray() };
    });
    private BackendRecord Resolve(BackendSelection? selection, BackendRecord[] backends)
    {
        if (selection is null) throw new ConfigurationException("Selección de backend ausente.");
        var matches = backends.Where(b => selection.Kind == "fixed" ? b.Id == selection.BackendId :
            b.Ownership == "managed" && b.Origin == selection.Origin && b.Variant == selection.Variant && b.TracksLatest && b.Platform == "win-x64").ToArray();
        if (matches.Length != 1) throw new ConfigurationException("Backend seleccionado ausente o ambiguo; no se usa un sustituto.");
        var backend = matches[0];
        ResourceRemoval.CheckPending(home, store, [backend.Id]);
        if (!File.Exists(new Coquena.Backends.BackendPaths(home).Server(backend))) throw new ConfigurationException("Archivo del backend seleccionado ausente.");
        return backend;
    }
    private ServerView ViewLocked(ServerRecord record)
    {
        var selection = record.Backend ?? Config().DefaultBackend;
        var diagnostics = new List<string>(); BackendRecord? backend = null;
        try { backend = Resolve(selection, store.Read(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends)?.Backends ?? []); }
        catch (ConfigurationException e) { diagnostics.Add(e.Message); }
        var presets = store.Read(DocumentKind.Presets, Json.PresetsDocument, ContractValidation.Presets)?.Presets ?? [];
        var associated = record.PresetIds.Select(id => presets.SingleOrDefault(p => p.Id == id)).ToArray();
        for (var i = 0; i < associated.Length; i++) if (associated[i] is null) diagnostics.Add($"Preset ausente: {record.PresetIds[i]:D}.");
        var models = store.Read(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models)?.Models ?? [];
        foreach (var preset in associated.OfType<PresetRecord>())
        {
            var model = models.SingleOrDefault(m => m.Id == preset.ModelId);
            if (model is null) diagnostics.Add($"Modelo ausente: {preset.ModelId:D} en {preset.Name}.");
            else if (model.Files.Any(f => !File.Exists(new Coquena.Models.ModelPaths(home).File(model, f)))) diagnostics.Add($"Archivo de modelo ausente en {preset.Name}.");
        }
        var effective = new Dictionary<string, JsonElement> { ["host"] = Text("127.0.0.1"), ["metrics"] = Scalar("true") };
        foreach (var pair in record.Parameters) effective[pair.Key] = pair.Value;
        return new(record, selection, backend, effective, associated, diagnostics.ToArray());
    }
    public ServerView View(string? name) => store.Exclusive(() => ViewLocked(Named(name)));
    public ServerView[] List() => store.Exclusive(() => Catalog().Servers.Select(ViewLocked).ToArray());

    // Dependency injection is limited to composition; selection and definitions stay
    // local. The callback consumes the selected concrete build and every preset ID.
    public ServerStartPlan Compose(string? name, Func<BackendRecord, Guid[], ComposedPresets>? compose = null) => store.Exclusive(() => ComposeLocked(name, compose));

    internal ServerStartPlan ComposeLocked(string? name, Func<BackendRecord, Guid[], ComposedPresets>? compose = null)
    {
        var view = ViewLocked(Named(name));
        if (view.Diagnostics.Length != 0) throw new ConfigurationException(string.Join(" ", view.Diagnostics));
        if (view.Server.PresetIds.Length == 0) throw new ConfigurationException("No se puede iniciar un servidor vacío.");
        var definitions = new ParameterManager(home, store).Read().Definitions;
        var emitted = new Dictionary<string, JsonElement>();
        var iniValues = new Dictionary<string, string>();
        var diagnostics = new List<string>();
        foreach (var pair in view.Effective)
        {
            var definition = ParameterCatalog.Find(pair.Key, definitions);
            if (definition?.Application != "server" || definition.Name != pair.Key || pair.Key == "models-preset")
                throw new ConfigurationException($"Parámetro de servidor inválido: {pair.Key}.");
            if (pair.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined)
                throw new ConfigurationException($"Tipo de parámetro de servidor inválido: {pair.Key}.");
            var value = pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString()! : pair.Value.ToString();
            ParameterManager.ValidateValue(definition, value);
            if (pair.Key == "models-max" && definition.Type == "integer" && (!long.TryParse(value, CultureInfo.InvariantCulture, out var maximum) || maximum <= 0))
                throw new ConfigurationException("Entero positivo requerido para models-max.");
            var availability = ParameterManager.Availability(definition, view.Backend);
            if (availability == "no admitido")
            { diagnostics.Add($"{view.Server.Name}: {pair.Key} no admitido; se omite con warning y se conserva la asignación."); continue; }
            if (availability == "desconocido") diagnostics.Add($"{view.Server.Name}: {pair.Key} desconocido; se conserva.");
            if (definition.Destination == "INI") iniValues[pair.Key] = value;
            else emitted[pair.Key] = definition.Type == "boolean" ? Scalar(value == "1" || bool.TryParse(value, out var boolean) && boolean ? "true" : "false") : pair.Value;
        }
        // ConfigurationManager.Compose owns its own lease. Use its locked core here
        // to keep the entire plan on one consistent catalog snapshot.
        var presets = compose is null ? new ConfigurationManager(home, store).ComposeLocked(view.Backend!, view.Server.PresetIds)
            : compose(view.Backend!, view.Server.PresetIds);
        if (iniValues.Count > 0)
        {
            var block = string.Join("", iniValues.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} = {p.Value}\n"));
            presets = presets with { Ini = presets.Ini.Replace("[*]\n", "[*]\n" + block, StringComparison.Ordinal) };
        }
        presets = presets with { Diagnostics = presets.Diagnostics.Concat(diagnostics).ToArray() };
        return new ServerStartPlan(view.Server, view.Backend!, emitted, presets, definitions);
    }
}
