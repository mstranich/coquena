using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coquena.Contracts;

namespace Coquena.Storage;

// The marker commits catalog and designation together; readers use its snapshots
// until both checkpoints complete, as in the parameter transaction.
internal sealed record ServerRemovalTransaction(int SchemaVersion, ServersDocument Servers, ConfigDocument Config);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ServerRemovalTransaction))]
internal partial class ServerRemovalTransactionJson : JsonSerializerContext;

public sealed partial class LocalStore
{
    private string ServerRemovalTransactionPath => home.PathFor("catalogs/server-removal-transaction.json");
    private ServerRemovalTransaction? PendingServerRemoval()
    {
        var bytes = ReadBytes(ServerRemovalTransactionPath);
        return bytes is null ? null : ContractJson.Read(Encoding.UTF8.GetString(bytes),
            ServerRemovalTransactionJson.Default.ServerRemovalTransaction, ValidateServerRemoval);
    }
    private static void ValidateServerRemoval(ServerRemovalTransaction value)
    {
        ContractJson.Check(value.SchemaVersion == 1 && value.Servers is not null && value.Config is not null, "Transacción de retirada de servidor inválida.");
        ContractValidation.Servers(value.Servers!); ContractValidation.Config(value.Config!);
        ContractJson.Check(value.Config!.DefaultServerId is null, "La retirada del predeterminado debe limpiar su designación.");
    }
    private byte[]? ServerRemovalBytes(DocumentKind kind)
    {
        if (kind is not (DocumentKind.Config or DocumentKind.Servers)) return null;
        var pending = PendingServerRemoval();
        if (pending is null) return null;
        return Encoding.UTF8.GetBytes(kind == DocumentKind.Config
            ? ContractJson.Write(pending.Config, ContractJsonContext.Default.ConfigDocument)
            : ContractJson.Write(pending.Servers, ContractJsonContext.Default.ServersDocument));
    }
    internal void CommitServerRemovalLocked(ServersDocument servers, ConfigDocument config)
    {
        var transaction = new ServerRemovalTransaction(1, servers, config);
        ValidateServerRemoval(transaction);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(transaction, ServerRemovalTransactionJson.Default.ServerRemovalTransaction));
        if (bytes.Length > MaxDocumentBytes) throw new StorageException("document-too-large", "Transacción supera 16 MiB.");
        if (PendingServerRemoval() is not null) throw new StorageException("transaction-pending", "Hay una retirada de servidor pendiente.");
        fault?.Invoke(WriteStage.BeforeReplace);
        AtomicCheckpoint(ServerRemovalTransactionPath, bytes);
        try
        {
            fault?.Invoke(WriteStage.AfterReplace);
            RecoverServerRemovalTransaction();
            AppendJournal(new(DateTimeOffset.UtcNow, Guid.NewGuid(), DocumentKind.Servers, null), DefaultJournalBytes);
        }
        catch (Exception error)
        {
            throw new StorageException("published-pending-checkpoint", "Retirada y designación publicadas conjuntamente; releer antes de reintentar. Checkpoint/auditoría pendiente.", true, error);
        }
    }
    private void RecoverServerRemovalTransaction()
    {
        var transaction = PendingServerRemoval();
        if (transaction is null) return;
        AtomicCheckpoint(Location(DocumentKind.Servers, null, null), Encoding.UTF8.GetBytes(ContractJson.Write(transaction.Servers, ContractJsonContext.Default.ServersDocument)));
        fault?.Invoke(WriteStage.AfterServerCheckpoint);
        AtomicCheckpoint(Location(DocumentKind.Config, null, null), Encoding.UTF8.GetBytes(ContractJson.Write(transaction.Config, ContractJsonContext.Default.ConfigDocument)));
        fault?.Invoke(WriteStage.AfterServerCheckpoint);
        File.Delete(ServerRemovalTransactionPath);
    }
}
