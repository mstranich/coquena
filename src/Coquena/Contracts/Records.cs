using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coquena.Contracts;

// IDs are opaque GUIDs. Names and paths never reconnect a broken reference.
public sealed record BackendSelection(string Kind, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? BackendId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Origin = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Variant = null);
public sealed record BackendRecord(Guid Id, string Build, string Version, string Origin,
    string Repository, string Variant, string Family, string Platform, string ServerPath,
    bool TracksLatest = false, bool Pinned = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InstallationDirectory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    string Ownership = "managed");
public sealed record ContentIdentity(string Algorithm, string Domain, string Value);
public sealed record RemoteFile(string Path, long? Size, ContentIdentity? Identity = null);
public sealed record RemoteSelection(string Repository, string RequestedRevision,
    string Commit, RemoteFile[] Files);
public sealed record InstalledFile(string RemotePath, string LocalPath, long Size,
    ContentIdentity? Integrity = null, DateTimeOffset? LastWriteUtc = null);
// One unit, ordered files (first shard first), empty initial model configuration.
public sealed record ModelRecord(Guid Id, string Name, RemoteSelection? Selection,
    InstalledFile[] Files, Dictionary<string, string> Parameters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InstallationDirectory = null,
    string Ownership = "managed",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CompanionType = null);
public sealed record PresetRecord(Guid Id, string Name, Guid? ModelId,
    Dictionary<string, string> Parameters);
public sealed record ServerRecord(Guid Id, string Name, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] BackendSelection? Backend,
    Guid[] PresetIds, Dictionary<string, JsonElement> Parameters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LogRetention? LogRetention = null);
public sealed record ServersDocument(int SchemaVersion, ServerRecord[] Servers);
public sealed record BackendRetention(int MaxBuilds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Own = false);
public sealed record BackendRetentionJournal(int SchemaVersion, BackendRecord[] Pending);
public sealed record LogRetention(long MaxBytes);
public sealed record LastLogDocument(int SchemaVersion, Guid ServerId, Guid InstanceId, string LogPath);
public sealed record ConfigDocument(int SchemaVersion, Guid? DefaultServerId,
    BackendSelection? DefaultBackend, BackendRetention BackendRetention,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LogRetention? LogRetention = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BackendsDirectory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ModelsDirectory = null);
public sealed record ModelsDocument(int SchemaVersion, ModelRecord[] Models);
public sealed record BackendsDocument(int SchemaVersion, BackendRecord[] Backends);
public sealed record BackendUpdateCandidate(string Build, RemoteFile[] Artifacts,
    string Compatibility, string Diagnostic);
public sealed record BackendUpdateObservation(string Origin, string Variant, string Platform,
    Guid BackendId, string InstalledBuild, DateTimeOffset CheckedAt, string State,
    string Diagnostic, BackendUpdateCandidate? Candidate);
public sealed record BackendUpdatesDocument(int SchemaVersion, BackendUpdateObservation[] Updates);
public sealed record PresetsDocument(int SchemaVersion, PresetRecord[] Presets);
public sealed record InstanceDocument(int SchemaVersion, Guid InstanceId, Guid ServerId,
    Guid BackendId, string Build, int Pid, string Mode, string Host, int Port,
    bool Metrics, Guid[] PresetIds, Guid[] ResourceIds, string IniPath, string LogPath,
    long? ProcessCreated = null, string? ExecutablePath = null);
public sealed record TransferRecord(string SourcePath, string PartialPath, string DestinationPath,
    long ConfirmedBytes, long? ExpectedSize, ContentIdentity? Identity,
    string? ETag, string? LastModified, string State, ContentIdentity? LocalIntegrity = null,
    DateTimeOffset? VerifiedWriteUtc = null);
// Exactly one pinned resource selection; no expiring URL or credentials persisted.
public sealed record BackendDownloadSelection(string Origin, string Release, string Family,
    string Variant, string Platform, RemoteFile[] Artifacts);
public sealed record OperationDocument(int SchemaVersion, Guid Id, string ResourceKind,
    string Name, RemoteSelection? ModelSelection, BackendDownloadSelection? BackendSelection,
    TransferRecord[] Files, string State, string Phase, DateTimeOffset UpdatedAt,
    string? Diagnostic, BackendInstallPlan? BackendInstall = null, ModelInstallPlan? ModelInstall = null);

public sealed record ModelInstallPlan(Guid ModelId, InstalledFile[]? VerifiedFiles = null,
    int MovedFiles = 0, int MovingFile = -1,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InstallationDirectory = null);

public sealed record BackendInstallPlan(string RequestedRelease, Guid BackendId,
    BackendRecord? Previous, BackendRecord? PreviousLatest, string Slot,
    string? ServerRelativePath = null, string? Version = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InstallationDirectory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BackendUpdateObservation? Upgrade = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,

    WriteIndented = true, RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BackendRecord))]
[JsonSerializable(typeof(BackendRetentionJournal))]
[JsonSerializable(typeof(ModelRecord))]
[JsonSerializable(typeof(PresetRecord))]
[JsonSerializable(typeof(ServersDocument))]
[JsonSerializable(typeof(ConfigDocument))]
[JsonSerializable(typeof(InstanceDocument))]
[JsonSerializable(typeof(LastLogDocument))]
[JsonSerializable(typeof(OperationDocument))]
[JsonSerializable(typeof(ModelsDocument))]
[JsonSerializable(typeof(BackendsDocument))]
[JsonSerializable(typeof(BackendUpdatesDocument))]
[JsonSerializable(typeof(PresetsDocument))]
[JsonSerializable(typeof(Coquena.Configuration.ParametersDocument))]
public partial class ContractJsonContext : JsonSerializerContext;
