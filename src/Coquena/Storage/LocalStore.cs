using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Coquena.Contracts;

namespace Coquena.Storage;

public enum DocumentKind { Config, Models, Backends, Parameters, Presets, Servers, Operation, Instance, LastLog }
public enum WriteStage { BeforeReplace, AfterReplace }
// Deliberately no arbitrary text, URLs, paths, names or diagnostics in the journal.
public enum OperationResult { Cancelled, Installed }
public sealed record TransactionEvent(DateTimeOffset At, Guid TransactionId, DocumentKind Document, Guid? RecordId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OperationResult? Result = null);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TransactionEvent))]
internal partial class JournalJsonContext : JsonSerializerContext;

public sealed class LocalStore
{
    private const int DefaultJournalBytes = 262144;
    private const int MaxDocumentBytes = 16777216;
    private readonly Home home;
    private readonly Action<WriteStage>? fault;
    public LocalStore(Home home, Action<WriteStage>? fault = null)
    {
        this.home = home;
        this.fault = fault;
    }

    public T? Read<T>(DocumentKind kind, JsonTypeInfo<T> type, Action<T> validate,
        string? server = null, Guid? operation = null) where T : class
    {
        var bytes = ReadBytes(Location(kind, server, operation));
        if (bytes is null) return null;
        var value = ContractJson.Read(Encoding.UTF8.GetString(bytes), type, validate);
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
        return UpdateLocked(kind, type, validate, change, server, operation, cancellation);
    }

    // Caller must hold this store's home writer lease (Exclusive).
    internal T UpdateLocked<T>(DocumentKind kind, JsonTypeInfo<T> type, Action<T> validate,
        Func<T?, T> change, string? server = null, Guid? operation = null,
        CancellationToken cancellation = default) where T : class
    {
        var path = Location(kind, server, operation);
        var original = ReadBytes(path);
        var current = original is null ? null : ContractJson.Read(Encoding.UTF8.GetString(original), type, validate);
        if (current is not null) CheckRecord(current, operation);
        var next = change(current);
        var json = ContractJson.Write(next, type);
        ContractJson.Read(json, type, validate);
        CheckRecord(next, operation);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxDocumentBytes)
            throw new StorageException("document-too-large", "Documento supera 16 MiB.");
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (kind == DocumentKind.Operation)
            Directory.CreateDirectory(home.PathFor($"downloads/{operation:D}/files"));
        Home.CheckLinks(path);
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
            Home.CheckLinks(path);
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

    private string Location(DocumentKind kind, string? server, Guid? operation)
    {
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
            var path = home.PathFor(".writer.lock");
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                if (watch.ElapsedMilliseconds >= 3000)
                    throw new StorageException("writer-busy", "Otro escritor mantiene el home ocupado (límite 3 s).", inner: error);
                cancellation.WaitHandle.WaitOne(25);
            }
        }
    }

    // Short coordination only; never hold this lease while doing network I/O.
    internal T Exclusive<T>(Func<T> action)
    {
        home.Ensure();
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
