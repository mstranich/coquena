using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Coquena.Contracts;
using Coquena.Projects;
using System.Text.Json.Nodes;

namespace Coquena.Storage;

public enum DocumentKind { Config, Models, Backends, Parameters, Presets, Servers, Operation, Instance, LastLog, BackendUpdates, BackendRetention }
public enum WriteStage { BeforeReplace, AfterReplace, AfterParameterCheckpoint, AfterServerCheckpoint }
// Deliberately no arbitrary text, URLs, paths, names or diagnostics in the journal.
public enum OperationResult { Cancelled, Installed }
public sealed record TransactionEvent(DateTimeOffset At, Guid TransactionId, DocumentKind Document, Guid? RecordId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OperationResult? Result = null);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TransactionEvent))]
internal partial class JournalJsonContext : JsonSerializerContext;

public sealed partial class LocalStore
{
    private const int DefaultJournalBytes = 262144;
    private const int MaxDocumentBytes = 16777216;
    private readonly Home home;
    private readonly Action<WriteStage>? fault;
    internal bool IsScoped => home.Scope is not null;
    private readonly string? scopeId;
    public LocalStore(Home home, Action<WriteStage>? fault = null)
    {
        this.home = home;
        this.fault = fault;
        if (home.Scope is not null)
        {
            var scopes = new ScopeStore(home.Scope);
            var snapshot = scopes.Read();
            if (snapshot.ScopeId is null)
            {
                scopes.Commit(snapshot.Revision, new Dictionary<string, JsonObject>
                {
                    ["local/inventory.json"] = ProjectDocuments.Create("inventory", 1, new JsonObject { ["resources"] = new JsonArray() })
                });
                snapshot = scopes.Read();
            }
            scopeId = snapshot.ScopeId;
        }
    }

    public T? Read<T>(DocumentKind kind, JsonTypeInfo<T> type, Action<T> validate,
        string? server = null, Guid? operation = null) where T : class
    {
        var bytes = IsScoped ? ReadBytes(Location(kind, server, operation)) :
            ServerRemovalBytes(kind) ?? ModelDirectoryBytes(kind) ?? BackendDirectoryBytes(kind) ?? TransactionBytes(kind) ?? ReadBytes(Location(kind, server, operation));
        if (bytes is null) return null;
        var value = ContractJson.Read(Decode(bytes, kind), type, validate);
        CheckRecord(value, operation);
        return value;
    }

    // Read-modify-write is performed inside one home-wide, cross-process writer lease.
    // A missing document is passed as null; corruption is never translated to empty.
    public T Update<T>(DocumentKind kind, JsonTypeInfo<T> type, Action<T> validate,
        Func<T?, T> change, string? server = null, Guid? operation = null,
        CancellationToken cancellation = default) where T : class
    {
        home.Ensure();
        using var lease = Acquire(cancellation);
        Recover();
        return UpdateLocked(kind, type, validate, change, server, operation, cancellation);
    }

    // Caller must hold this store's home writer lease (Exclusive).
    internal T UpdateLocked<T>(DocumentKind kind, JsonTypeInfo<T> type, Action<T> validate,
        Func<T?, T> change, string? server = null, Guid? operation = null,
        CancellationToken cancellation = default) where T : class
    {
        var path = Location(kind, server, operation);
        var original = ReadBytes(path);
        var current = original is null ? null : ContractJson.Read(Decode(original, kind), type, validate);
        if (current is not null) CheckRecord(current, operation);
        var next = change(current);
        var json = ContractJson.Write(next, type);
        ContractJson.Read(json, type, validate);
        CheckRecord(next, operation);
        var bytes = IsScoped ? ProjectDocuments.Bytes(ProjectDocuments.Create("operation",
            original is null ? 1 : checked(ProjectDocuments.Read(original, "operation")["revision"]!.GetValue<long>() + 1),
            new JsonObject { ["operation-id"] = operation!.Value.ToString("D"), ["scope-id"] = scopeId,
                ["transfer"] = JsonNode.Parse(json) })) : Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxDocumentBytes)
            throw new StorageException("document-too-large", "Documento supera 16 MiB.");
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (kind == DocumentKind.Operation)
            Directory.CreateDirectory(home.PathFor($"downloads/{operation:D}/files"));
        home.CheckPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var published = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            fault?.Invoke(WriteStage.BeforeReplace);
            cancellation.ThrowIfCancellationRequested();
            home.CheckPath(path);
            var observed = ReadBytes(path);
            if (!(original is null ? observed is null : observed is not null && original.AsSpan().SequenceEqual(observed)))
                throw new StorageException("stale-document", "Documento modificado fuera de la exclusión de Coquena; no se reemplazó.");
            if (original is null) File.Move(temporary, path);
            else File.Replace(temporary, path, null);
            published = true;
            fault?.Invoke(WriteStage.AfterReplace);
            AppendJournal(new(DateTimeOffset.UtcNow, Guid.NewGuid(), kind, operation), DefaultJournalBytes);
            return next;
        }
        catch (Exception error) when (published)
        {
            throw new StorageException("published-incomplete-audit",
                "Estado publicado; registro posterior incompleto. Releer antes de reintentar, no revertir automáticamente.", true, error);
        }
        finally
        {
            // A hard process crash may leave an inert .tmp, never read as current state.
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void CheckRecord<T>(T value, Guid? operation)
    {
        if (value is OperationDocument document && document.Id != operation)
            throw new StorageException("operation-id-mismatch", "GUID del documento no corresponde al directorio de operación.");
    }

    private string Decode(byte[] bytes, DocumentKind kind)
    {
        if (!IsScoped) return Encoding.UTF8.GetString(bytes);
        if (kind != DocumentKind.Operation) throw new StorageException("scoped-transfer-only", "Se requiere checkpoint de transferencia.");
        try
        {
            var data = ProjectDocuments.Read(bytes, "operation")["data"]!;
            if (new ScopeStore(home.Scope!).Read().ScopeId != scopeId || data["scope-id"]?.GetValue<string>() != scopeId)
                throw new StorageException("transfer-scope-conflict", "La operación pertenece a otra identidad de ámbito.");
            if (data["transfer"] is not JsonObject transfer) throw new StorageException("invalid-project-document", "Falta checkpoint de transferencia.");
            if (transfer["id"]?.GetValue<string>() != data["operation-id"]?.GetValue<string>()) ProjectDocuments.Invalid();
            return transfer.ToJsonString();
        }
        catch (InvalidOperationException e)
        { throw new StorageException("invalid-project-document", "Checkpoint inválido; se conserva.", inner: e); }
    }

    private void Recover()
    {
        if (IsScoped)
        {
            if (new ScopeStore(home.Scope!).Read().ScopeId != scopeId)
                throw new StorageException("transfer-scope-conflict", "Cambió la identidad del ámbito de transferencia.");
            return;
        }
        RecoverServerRemovalTransaction();
        RecoverParameterTransaction();
        RecoverBackendDirectoryTransaction();
        RecoverModelDirectoryTransaction();
    }

    private string Location(DocumentKind kind, string? server, Guid? operation)
    {
        if (IsScoped && kind != DocumentKind.Operation)
            throw new StorageException("scoped-transfer-only", "Este almacén sólo conserva checkpoints del ámbito.");
        if (kind == DocumentKind.Operation)
        {
            if (operation is null || operation == Guid.Empty) throw new ArgumentException("GUID de operación requerido.");
            return home.PathFor($"downloads/{operation:D}/operation.json");
        }
        if (kind is DocumentKind.Instance or DocumentKind.LastLog)
        {
            if (string.IsNullOrWhiteSpace(server) || server.IndexOfAny(['/', '\\']) >= 0)
                throw new StorageException("unsafe-path", "Servidor debe ser un solo segmento seguro.");
            return home.PathFor($"run/{server}/{(kind == DocumentKind.Instance ? "instance" : "last-log")}.json");
        }
        return home.PathFor(kind switch
        {
            DocumentKind.Config => "config.json", DocumentKind.Models => "catalogs/models.json",
            DocumentKind.Backends => "catalogs/backends.json", DocumentKind.Parameters => "catalogs/parameters.json",
            DocumentKind.BackendUpdates => "catalogs/backend-updates.json",
            DocumentKind.BackendRetention => "catalogs/backend-retention.json",
            DocumentKind.Presets => "catalogs/presets.json", DocumentKind.Servers => "catalogs/servers.json",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });
    }

    private static byte[]? ReadBytes(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxDocumentBytes) throw new StorageException("document-too-large", "Documento supera 16 MiB.");
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new StorageException("stale-document", "Documento cambió durante lectura.");
            // Throw on invalid UTF-8 instead of accepting replacement characters.
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (DecoderFallbackException error)
        { throw new StorageException("invalid-document", "UTF-8 inválido.", inner: error); }
    }

    private FileStream Acquire(CancellationToken cancellation)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var path = home.PathFor(IsScoped ? "local/transfer-writer.lock" : ".writer.lock");
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                if (watch.ElapsedMilliseconds >= 3000)
                    throw new StorageException("writer-busy", "Otro escritor mantiene el home ocupado (límite 3 s).", inner: error);
                cancellation.WaitHandle.WaitOne(25);
            }
        }
    }

    // Local coordination/publication/recovery; never hold this lease for network I/O.
    internal T Exclusive<T>(Func<T> action)
    {
        home.Ensure();
        using var lease = Acquire(default);
        Recover();
        return action();
    }

    // Read-only snapshots do not initialize home or checkpoint a pending transaction.
    internal T Inspect<T>(Func<T> action)
    {
        if (!Directory.Exists(home.Root)) return action();
        using var lease = Acquire(default);
        return action();
    }

    // Called inside Exclusive, after the executor stopped and preparation was removed.
    internal void RecordCancellation(Guid id) => AppendJournal(new(DateTimeOffset.UtcNow,
        Guid.NewGuid(), DocumentKind.Operation, id, OperationResult.Cancelled), DefaultJournalBytes);

    internal void RecordInstallation(Guid id) => AppendJournal(new(DateTimeOffset.UtcNow,
        Guid.NewGuid(), DocumentKind.Operation, id, OperationResult.Installed), DefaultJournalBytes);

    private void AppendJournal(TransactionEvent entry, int maxBytes)
    {
        var path = home.PathFor("logs/transactions.jsonl");
        var archive = home.PathFor("logs/transactions.1.jsonl");
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, JournalJsonContext.Default.TransactionEvent) + "\n");
        if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > maxBytes)
            File.Move(path, archive, overwrite: true);
        // Oversized externally supplied logs are not kept indefinitely.
        if (File.Exists(archive) && new FileInfo(archive).Length > maxBytes) File.Delete(archive);
        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path); // bounded above by rotation
            if (existing.Length > 0 && existing[^1] != (byte)'\n')
                throw new StorageException("invalid-journal", "JSONL interrumpido; no se agregaron eventos a una línea parcial.");
            var text = new UTF8Encoding(false, true).GetString(existing);
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var parsed = JsonDocument.Parse(line);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                    throw new StorageException("invalid-journal", "Evento JSONL inválido.");
            }
        }
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }
}
