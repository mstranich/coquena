using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coquena.Configuration;
using Coquena.Contracts;

namespace Coquena.Storage;

// The atomic marker is the commit point. While checkpointing is incomplete every
// reader obtains the committed snapshots from it, never a partially cascaded state.
internal sealed record ParameterTransaction(int SchemaVersion, ParametersDocument Parameters,
    PresetsDocument? Presets, ServersDocument? Servers);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ParameterTransaction))]
internal partial class ParameterTransactionJson : JsonSerializerContext;

public sealed partial class LocalStore
{
    private string ParameterTransactionPath => home.PathFor("catalogs/parameter-transaction.json");
    private ParameterTransaction? PendingParameters()
    {
        var bytes = ReadBytes(ParameterTransactionPath);
        return bytes is null ? null : ContractJson.Read(Encoding.UTF8.GetString(bytes),
            ParameterTransactionJson.Default.ParameterTransaction, ValidateTransaction);
    }
    private static void ValidateTransaction(ParameterTransaction value)
    {
        ContractJson.Check(value.SchemaVersion == 1 && value.Parameters is not null, "Transacción de parámetros inválida.");
        ParameterManager.Validate(value.Parameters!);
        if (value.Presets is not null) ContractValidation.Presets(value.Presets);
        if (value.Servers is not null) ContractValidation.Servers(value.Servers);
    }
    private byte[]? TransactionBytes(DocumentKind kind)
    {
        if (kind is not (DocumentKind.Parameters or DocumentKind.Presets or DocumentKind.Servers)) return null;
        var pending = PendingParameters();
        if (pending is null) return null;
        return kind switch
        {
            DocumentKind.Parameters => Encoding.UTF8.GetBytes(ContractJson.Write(pending.Parameters, ContractJsonContext.Default.ParametersDocument)),
            DocumentKind.Presets when pending.Presets is not null => Encoding.UTF8.GetBytes(ContractJson.Write(pending.Presets, ContractJsonContext.Default.PresetsDocument)),
            DocumentKind.Servers when pending.Servers is not null => Encoding.UTF8.GetBytes(ContractJson.Write(pending.Servers, ContractJsonContext.Default.ServersDocument)),
            _ => null
        };
    }
    internal void CommitParametersLocked(ParametersDocument parameters, PresetsDocument? presets, ServersDocument? servers)
    {
        var transaction = new ParameterTransaction(1, parameters, presets, servers);
        ValidateTransaction(transaction);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(transaction, ParameterTransactionJson.Default.ParameterTransaction));
        if (bytes.Length > MaxDocumentBytes) throw new StorageException("document-too-large", "Transacción supera 16 MiB.");
        if (PendingParameters() is not null) throw new StorageException("transaction-pending", "Hay una transacción pendiente.");
        fault?.Invoke(WriteStage.BeforeReplace);
        AtomicCheckpoint(ParameterTransactionPath, bytes);
        try
        {
            fault?.Invoke(WriteStage.AfterReplace);
            RecoverParameterTransaction();
            AppendJournal(new(DateTimeOffset.UtcNow, Guid.NewGuid(), DocumentKind.Parameters, null), DefaultJournalBytes);
        }
        catch (Exception error)
        {
            throw new StorageException("published-pending-checkpoint",
                "Edición y cascada publicadas conjuntamente; releer antes de reintentar. Checkpoint/auditoría pendiente.", true, error);
        }
    }
    private void RecoverParameterTransaction()
    {
        var transaction = PendingParameters();
        if (transaction is null) return;
        AtomicCheckpoint(Location(DocumentKind.Parameters, null, null), Encoding.UTF8.GetBytes(ContractJson.Write(transaction.Parameters, ContractJsonContext.Default.ParametersDocument)));
        fault?.Invoke(WriteStage.AfterParameterCheckpoint);
        if (transaction.Presets is not null)
        {
            AtomicCheckpoint(Location(DocumentKind.Presets, null, null), Encoding.UTF8.GetBytes(ContractJson.Write(transaction.Presets, ContractJsonContext.Default.PresetsDocument)));
            fault?.Invoke(WriteStage.AfterParameterCheckpoint);
        }
        if (transaction.Servers is not null)
        {
            AtomicCheckpoint(Location(DocumentKind.Servers, null, null), Encoding.UTF8.GetBytes(ContractJson.Write(transaction.Servers, ContractJsonContext.Default.ServersDocument)));
            fault?.Invoke(WriteStage.AfterParameterCheckpoint);
        }
        File.Delete(ParameterTransactionPath);
    }
    private static void AtomicCheckpoint(string path, byte[] bytes)
    {
        Home.CheckLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            Home.CheckLinks(path);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
