using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Coquena.Contracts;

public sealed class ContractException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class ContractJson
{
    public static string Write<T>(T value, JsonTypeInfo<T> type) => JsonSerializer.Serialize(value, type);

    public static T Read<T>(string json, JsonTypeInfo<T> type, Action<T> validate)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Check(root.ValueKind == JsonValueKind.Object, "Se esperaba un objeto JSON.");
            RejectDuplicates(root);
            if (root.TryGetProperty("schemaVersion", out var version))
            {
                Check(version.ValueKind == JsonValueKind.Number, "schemaVersion debe ser entero.");
                Check(version.TryGetInt32(out var number), "schemaVersion debe ser entero.");
                if (number != 1)
                    throw new ContractException("unknown-schema", $"Versión de esquema no soportada: {number}.");
            }
            var value = JsonSerializer.Deserialize(json, type);
            Check(value is not null, "Documento nulo.");
            validate(value!);
            return value!;
        }
        catch (JsonException error)
        {
            throw new ContractException("invalid-document", $"JSON inválido: {error.Message}");
        }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Check(names.Add(property.Name), $"Campo duplicado: {property.Name}.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new ContractException("invalid-document", message);
    }
}

// Shape checks only: no network, disk access, backend resolution or referential repair.
public static class ContractValidation
{
    public static void Models(ModelsDocument value)
    {
        Version(value.SchemaVersion);
        Catalog(value.Models, Model, x => x.Id, x => x.Name);
    }
    public static void Backends(BackendsDocument value)
    {
        Version(value.SchemaVersion);
        Catalog(value.Backends, Backend, x => x.Id, null);
        Require(value.Backends.Where(b => b.Name is not null).Select(b => b.Name!).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
            value.Backends.Count(b => b.Name is not null), "Nombre externo duplicado.");
    }
    public static void Presets(PresetsDocument value)
    {
        Version(value.SchemaVersion);
        Catalog(value.Presets, Preset, x => x.Id, x => x.Name);
    }
    private static void Catalog<T>(T[]? entries, Action<T> validate, Func<T, Guid> id, Func<T, string>? name) where T : class
    {
        Require(entries is not null, "Catálogo requerido.");
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries!)
        {
            Require(entry is not null, "Registro nulo.");
            validate(entry!);
            Require(ids.Add(id(entry!)), "ID duplicado.");
            if (name is not null) Require(names.Add(name(entry!)), "Nombre duplicado.");
        }
    }
    private static void Require(bool value, string message) => ContractJson.Check(value, message);
    private static void Id(Guid id) => Require(id != Guid.Empty, "ID vacío.");
    private static void Text(string? text) => Require(!string.IsNullOrWhiteSpace(text), "Texto requerido.");
    private static void Version(int version) => Require(version == 1, "schemaVersion requerido.");
    private static void Ids(Guid[]? ids)
    {
        Require(ids is not null, "Lista de IDs requerida.");
        foreach (var id in ids!) Id(id);
        Require(ids.Distinct().Count() == ids.Length, "IDs duplicados.");
    }
    public static void Selection(BackendSelection? selection)
    {
        if (selection is null) return; // server inherits; config may have no default
        if (selection.Kind == "latest")
        {
            Require(selection.BackendId is null, "latest no fija backendId.");
            Require(selection.Origin == "ggml", "M1 sólo sigue el origen ggml normalizado.");
            Text(selection.Variant);
        }
        else
        {
            Require(selection.Kind == "fixed", "Selección de backend desconocida.");
            Require(selection.BackendId.HasValue, "fixed requiere backendId.");
            Id(selection.BackendId!.Value);
            Require(selection.Origin is null && selection.Variant is null, "fixed sólo conserva backendId.");
        }
    }
    public static void Servers(ServersDocument value)
    {
        Version(value.SchemaVersion);
        Require(value.Servers is not null, "servers requerido.");
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ports = new HashSet<int>();
        foreach (var server in value.Servers!)
        {
            Require(server is not null, "Servidor nulo.");
            Id(server!.Id); Text(server.Name); Selection(server.Backend); Ids(server.PresetIds);
            Require(ids.Add(server.Id) && names.Add(server.Name), "Servidor duplicado.");
            Require(server.LogRetention is null || server.LogRetention.MaxBytes > 0, "Retención de logs inválida.");
            Require(server.Parameters is not null, "parameters requerido.");
            Require(server.Parameters!.TryGetValue("port", out var port) &&
                port.ValueKind == JsonValueKind.Number && port.TryGetInt32(out var number) &&
                number is >= 1 and <= 65535 && ports.Add(number), "Puerto requerido, fuera de rango o duplicado.");
            if (server.Parameters.TryGetValue("host", out var host))
                Require(host.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(host.GetString()), "host inválido.");
            if (server.Parameters.TryGetValue("metrics", out var metrics))
                Require(metrics.ValueKind is JsonValueKind.True or JsonValueKind.False, "metrics debe ser booleano.");
            foreach (var pair in server.Parameters)
                Require(pair.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or
                    JsonValueKind.True or JsonValueKind.False, "Parámetro debe ser literal escalar.");
        }
    }
    public static void Config(ConfigDocument value)
    {
        Version(value.SchemaVersion);
        if (value.BackendsDirectory is not null) Text(value.BackendsDirectory);
        if (value.ModelsDirectory is not null) Text(value.ModelsDirectory);
        Require(value.LogRetention is null || value.LogRetention.MaxBytes > 0, "Retención de logs inválida.");
        if (value.DefaultServerId.HasValue) Id(value.DefaultServerId.Value);
        Selection(value.DefaultBackend);
        Require(value.BackendRetention is not null && value.BackendRetention.MaxBuilds >= 1, "Retención inválida.");
    }
    public static void Backend(BackendRecord value)
    {
        Id(value.Id); Text(value.Build); Text(value.Version); Text(value.Origin);
        if (value.InstallationDirectory is not null) Text(value.InstallationDirectory);
        Text(value.Repository); Text(value.Variant); Text(value.Family); Text(value.Platform); Text(value.ServerPath);
        Require(value.Ownership is "managed" or "external", "Propiedad de backend inválida.");
        if (value.Ownership == "external")
        {
            Require(Coquena.Backends.BackendInstaller.ValidExternalName(value.Name), "Nombre externo inválido.");
            Require(Coquena.Backends.BackendInstaller.ValidExternalFamily(value.Variant) && value.Family == value.Variant.Split('@')[0], "Familia externa inválida.");
            Require(Path.IsPathFullyQualified(value.ServerPath) && value.InstallationDirectory is null && !value.TracksLatest && !value.Pinned,
                "Externo debe conservar ruta absoluta sin gestión ni seguimiento.");
        }
        else Require(value.Name is null, "Nombre reservado a externos.");
    }
    public static void Remote(RemoteSelection value)
    {
        Require(value is not null, "Selección remota requerida.");
        Text(value!.Repository); Text(value.RequestedRevision); Text(value.Commit);
        Require(value.Files is { Length: > 0 }, "Selección sin archivos.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in value.Files!)
        {
            Require(file is not null, "Archivo nulo."); Text(file!.Path);
            Require(paths.Add(file.Path), "Ruta duplicada.");
            Require(file.Size is null or >= 0, "Tamaño negativo.");
            Identity(file.Identity);
        }
    }
    private static void Identity(ContentIdentity? value)
    {
        if (value is null) return;
        Text(value.Algorithm); Text(value.Domain); Text(value.Value);
    }
    public static void Model(ModelRecord value)
    {
        Id(value.Id); Text(value.Name); Remote(value.Selection);
        if (value.InstallationDirectory is not null) Text(value.InstallationDirectory);
        Require(value.Files is not null && value.Files.Length == value.Selection.Files.Length, "Unidad incompleta.");
        for (var i = 0; i < value.Files!.Length; i++)
        {
            var file = value.Files[i];
            Require(file is not null, "Archivo instalado nulo.");
            Require(file!.RemotePath == value.Selection.Files[i].Path, "Orden/correspondencia de unidad inválido.");
            Text(file.LocalPath); Require(file.Size >= 0, "Tamaño negativo."); Identity(file.Integrity);
        }
        Parameters(value.Parameters);
    }
    private static void Parameters(Dictionary<string, string>? values)
    {
        Require(values is not null, "Configuración inicial requerida.");
        foreach (var pair in values!) { Text(pair.Key); Require(pair.Value is not null, "Literal nulo."); }
    }
    public static void Preset(PresetRecord value)
    {
        Id(value.Id); Text(value.Name); if (value.ModelId is { } model) Id(model); Parameters(value.Parameters);
    }
    public static void Instance(InstanceDocument value)
    {
        Version(value.SchemaVersion); Id(value.InstanceId); Id(value.ServerId); Id(value.BackendId);
        Text(value.Build); Text(value.Host); Text(value.IniPath); Text(value.LogPath);
        Require(value.Pid > 0 && value.Port is >= 1 and <= 65535, "PID/puerto inválido.");
        Require(value.Mode is "foreground" or "detached", "Modo desconocido.");
        Ids(value.PresetIds); Ids(value.ResourceIds);
        Require(value.PresetIds.Length > 0 && value.ResourceIds.Length > 0, "Ejecución sin recursos/presets.");
        Require(value.ProcessCreated is null || value.ProcessCreated > 0, "Fecha de creación del proceso inválida.");
        Require(value.ExecutablePath is null || Path.IsPathFullyQualified(value.ExecutablePath), "Ruta de ejecutable no absoluta.");
    }
    public static void Operation(OperationDocument value)
    {
        Version(value.SchemaVersion); Id(value.Id); Text(value.Name); Text(value.Phase);
        Require(value.Files is { Length: > 0 } && value.Files.All(f => f is not null), "Transferencias requeridas y no nulas.");
        if (value.ModelInstall is { } model)
        {
            Require(value.ResourceKind == "model" && value.ModelSelection is not null && value.BackendInstall is null, "Plan exclusivo de modelo.");
            Id(model.ModelId);
            if (model.InstallationDirectory is not null) Text(model.InstallationDirectory);
            Require(model.MovedFiles >= 0 && model.MovedFiles <= value.Files.Length &&
                (model.MovingFile == -1 || model.MovingFile == model.MovedFiles && model.MovingFile < value.Files.Length), "Progreso de publicación inválido.");
            if (model.VerifiedFiles is { } verified)
            {
                Require(verified.Length == value.Files.Length, "Publicación sin unidad completa.");
                for (var i = 0; i < verified.Length; i++)
                {
                    Require(verified[i] is not null, "Archivo verificado nulo.");
                    Require(verified[i].RemotePath == value.Files[i].SourcePath && verified[i].LocalPath == value.Files[i].DestinationPath &&
                        verified[i].Size == value.Files[i].ConfirmedBytes && Coquena.Models.FileIntegrity.Comparable(verified[i].Integrity) &&
                        verified[i].Integrity == value.Files[i].LocalIntegrity && verified[i].LastWriteUtc is not null, "Archivo verificado inválido.");
                }
            }
            if (value.Phase == "publishing" || value.Phase == "cleanup" && value.State != "cancelled")
                Require(model.VerifiedFiles is not null, "Publicación sin referencias verificadas.");
        }
        if (value.BackendInstall is { } plan)
        {
            if (plan.InstallationDirectory is not null) Text(plan.InstallationDirectory);
            Require(value.ResourceKind == "backend" && value.BackendSelection is not null, "Plan sólo para backend.");
            Id(plan.BackendId); Text(plan.RequestedRelease);
            Require(plan.RequestedRelease == "latest" || plan.RequestedRelease == value.BackendSelection!.Release,
                "Intención y release fijado no corresponden.");
            Require(plan.Slot == $"backends/{plan.BackendId:D}/{value.Id:D}", "Slot no pertenece a la operación.");
            if (plan.Previous is { } previous)
            {
                Backend(previous);
                Require(previous.Id == plan.BackendId && previous.Build == value.BackendSelection!.Release &&
                    previous.Origin == "ggml" && previous.Variant == value.BackendSelection.Variant &&
                    previous.Platform == value.BackendSelection.Platform, "Registro anterior no corresponde.");
            }
            if (plan.PreviousLatest is { } latest)
            {
                Backend(latest);
                Require(latest.TracksLatest && latest.Origin == "ggml" && latest.Variant == value.BackendSelection!.Variant &&
                    latest.Platform == value.BackendSelection.Platform, "Seguimiento anterior no corresponde.");
            }
            if (value.Phase == "publishing" || value.Phase == "cleanup" && value.State != "cancelled")
            { Text(plan.ServerRelativePath); Text(plan.Version); }
        }
        Require(value.State is "pending" or "downloading" or "paused" or "verifying" or "failed" or
            "completed" or "cancelled", "Estado desconocido.");
        if (value.ResourceKind == "model")
        {
            Require(value.ModelSelection is not null && value.BackendSelection is null, "Selección de modelo exclusiva.");
            Remote(value.ModelSelection!);
        }
        else
        {
            Require(value.ResourceKind == "backend" && value.ModelSelection is null && value.BackendSelection is not null,
                "Selección de backend exclusiva.");
            var selection = value.BackendSelection!;
            Require(selection.Origin == "ggml", "Origen no descargable.");
            Text(selection.Release); Require(selection.Release != "latest", "Operación debe fijar release.");
            Text(selection.Family); Text(selection.Variant); Text(selection.Platform);
            Require(selection.Artifacts is { Length: > 0 }, "Backend sin artefactos.");
            foreach (var artifact in selection.Artifacts!)
                Require(artifact is not null && artifact.Identity is null && (artifact.Size is null or >= 0),
                    "Backend no guarda digest ni tamaño negativo.");
        }
        var expected = value.ModelSelection?.Files ?? value.BackendSelection!.Artifacts;
        Require(value.Files is not null && value.Files.Length == expected.Length, "Transferencias incompletas.");
        for (var i = 0; i < value.Files!.Length; i++)
        {
            var file = value.Files[i];
            Require(file is not null, "Transferencia nula.");
            Require(file!.SourcePath == expected[i].Path && file.ExpectedSize == expected[i].Size,
                "Transferencia no corresponde a selección fijada.");
            Text(file.PartialPath); Text(file.DestinationPath); Text(file.State);
            Require(file.ConfirmedBytes >= 0 && (file.ExpectedSize is null || file.ConfirmedBytes <= file.ExpectedSize), "Checkpoint inválido.");
            Require(value.ResourceKind != "backend" || file.Identity is null, "Backend no guarda digest.");
            Require(value.ResourceKind != "backend" || file.LocalIntegrity is null, "Backend no calcula digest local.");
            Identity(file.Identity);
            Identity(file.LocalIntegrity);
            if (value.ModelInstall is not null)
            {
                Require(file.Identity == expected[i].Identity, "Referencia remota difiere de la selección fijada.");
                Require(file.State is "pending" or "paused" or "downloading" or "failed" or "transferred", "Estado de transferencia desconocido.");
                if (file.State == "transferred")
                    Require(Coquena.Models.FileIntegrity.Comparable(file.LocalIntegrity) && file.VerifiedWriteUtc is not null,
                        "Transferencia de modelo sin SHA-256 completo ni fecha observada.");
            }
        }
    }
}
