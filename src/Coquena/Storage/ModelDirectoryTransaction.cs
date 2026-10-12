using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coquena.Backends;
using Coquena.Configuration;
using Coquena.Contracts;

namespace Coquena.Storage;

internal sealed record ModelDirectoryMove(string Source, string Destination);
internal sealed record ModelDirectoryFile(string Source, string Destination, string Hash, string? PreviousHash);
internal sealed record ModelDirectoryTransaction(int SchemaVersion, bool Committed, ConfigDocument Config,
    ModelsDocument Models, ConfigDocument BeforeConfig, ModelsDocument BeforeModels,
    ModelDirectoryMove[] Moves, string[] Directories, ModelDirectoryFile[] Files);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ModelDirectoryTransaction))]
internal partial class ModelDirectoryTransactionJson : JsonSerializerContext;

public sealed partial class LocalStore
{
    private string ModelDirectoryTransactionPath => home.PathFor("catalogs/model-directory-transaction.json");
    internal static string DirectoryPlanSignature(ModelDirectoryTransaction plan) =>
        JsonSerializer.Serialize(plan, ModelDirectoryTransactionJson.Default.ModelDirectoryTransaction);
    private ModelDirectoryTransaction? PendingModelDirectory()
    {
        var bytes = ReadBytes(ModelDirectoryTransactionPath);
        if (bytes is null) return null;
        var value = ContractJson.Read(Encoding.UTF8.GetString(bytes), ModelDirectoryTransactionJson.Default.ModelDirectoryTransaction, ValidateModelDirectory);
        var paths = new Coquena.Models.ModelPaths(home);
        foreach (var move in value.Moves)
        {
            // The destructive cleanup path must be a recognized old managed slot,
            // reconstructed from the before snapshot rather than trusted verbatim.
            if (!value.Models.Models.Any(m => m.Ownership == "managed" && m.Files.Any(f => paths.File(m, f).Equals(move.Destination, StringComparison.OrdinalIgnoreCase))) ||
                !value.BeforeModels.Models.Any(m => m.Ownership == "managed" && m.Files.Any(f => paths.File(m, f).Equals(move.Source, StringComparison.OrdinalIgnoreCase))) ||
                value.BeforeModels.Models.Where(m => m.Ownership == "external").Any(m => m.Files.Any(f =>
                    paths.File(m, f).Equals(move.Source, StringComparison.OrdinalIgnoreCase) || paths.File(m, f).Equals(move.Destination, StringComparison.OrdinalIgnoreCase))))
                throw new StorageException("unsafe-directory-transaction", "Destino no pertenece al catálogo publicado.");
        }
        return value;
    }
    private static void ValidateModelDirectory(ModelDirectoryTransaction value)
    {
        ContractJson.Check(value.SchemaVersion == 1 && value.Config is not null && value.Models is not null &&
            value.Moves is not null && value.Directories is not null && value.Files is not null, "Traslado inválido.");
        ContractValidation.Config(value.Config!); ContractValidation.Models(value.Models!);
        ContractValidation.Config(value.BeforeConfig); ContractValidation.Models(value.BeforeModels);
        foreach (var move in value.Moves!)
            ContractJson.Check(Path.IsPathFullyQualified(move.Source) && Path.IsPathFullyQualified(move.Destination) &&
                !BackendPaths.Within(move.Source, move.Destination) && !BackendPaths.Within(move.Destination, move.Source), "Directorios de traslado inválidos.");
        foreach (var f in value.Files!)
            ContractJson.Check(f.Hash.Length == 64 && (f.PreviousHash is null || f.PreviousHash.Length == 64) &&
                value.Moves.Any(m => BackendPaths.Within(f.Source, m.Source) &&
                    m.Source.Equals(f.Source, StringComparison.OrdinalIgnoreCase) && m.Destination.Equals(f.Destination, StringComparison.OrdinalIgnoreCase)), "Archivo fuera del traslado.");
        foreach (var d in value.Directories!)
            ContractJson.Check(value.Moves.Any(m => Path.GetDirectoryName(m.Destination)!.Equals(d, StringComparison.OrdinalIgnoreCase)), "Directorio fuera del traslado.");
    }
    private byte[]? ModelDirectoryBytes(DocumentKind kind)
    {
        if (kind is not (DocumentKind.Config or DocumentKind.Models)) return null;
        var plan = PendingModelDirectory();
        if (plan?.Committed != true) return null;
        return Encoding.UTF8.GetBytes(kind == DocumentKind.Config
            ? ContractJson.Write(plan.Config, ContractJsonContext.Default.ConfigDocument)
            : ContractJson.Write(plan.Models, ContractJsonContext.Default.ModelsDocument));
    }
    private void SaveModelDirectory(ModelDirectoryTransaction plan)
    {
        ValidateModelDirectory(plan);
        var bytes = Encoding.UTF8.GetBytes(DirectoryPlanSignature(plan));
        if (bytes.Length > MaxDocumentBytes) throw new StorageException("document-too-large", "Traslado supera 16 MiB.");
        AtomicCheckpoint(ModelDirectoryTransactionPath, bytes);
    }
    internal void CommitModelDirectoryLocked(ModelDirectoryTransaction plan, Action<ModelDirectoryStage>? stage)
    {
        if (PendingModelDirectory() is not null) throw new StorageException("directory-transaction-pending", "Traslado pendiente de recuperación.");
        SaveModelDirectory(plan);
        RecoverModelDirectoryTransaction(stage);
    }
    private void RecoverModelDirectoryTransaction(Action<ModelDirectoryStage>? stage = null)
    {
        var plan = PendingModelDirectory();
        if (plan is null) return;
        try
        {
            new ModelDirectoryManager(home, this).CheckUsage(plan.BeforeModels, plan.Moves);
            if (!plan.Committed)
            {
                var config = ReadBytes(Location(DocumentKind.Config, null, null));
                var catalog = ReadBytes(Location(DocumentKind.Models, null, null));
                var before = ContractJson.Write(config is null ? new(1, null, null, new(5)) : ContractJson.Read(Encoding.UTF8.GetString(config), ContractJsonContext.Default.ConfigDocument, ContractValidation.Config), ContractJsonContext.Default.ConfigDocument) + "\n" +
                    ContractJson.Write(catalog is null ? new(1, []) : ContractJson.Read(Encoding.UTF8.GetString(catalog), ContractJsonContext.Default.ModelsDocument, ContractValidation.Models), ContractJsonContext.Default.ModelsDocument);
                if (before != ContractJson.Write(plan.BeforeConfig, ContractJsonContext.Default.ConfigDocument) + "\n" + ContractJson.Write(plan.BeforeModels, ContractJsonContext.Default.ModelsDocument))
                    throw new StorageException("stale-directory-transaction", "Catálogo/configuración modificados durante un traslado pendiente.");
                ModelDirectoryManager.CheckAccess(plan);
                foreach (var f in plan.Files)
                {
                    if (ModelDirectoryManager.Hash(f.Source) != f.Hash) throw new IOException($"Origen modificado: {f.Source}");
                    var observed = File.Exists(f.Destination) ? ModelDirectoryManager.Hash(f.Destination) : null;
                    if (observed == f.Hash) continue; // Already verified and published by this plan.
                    if (observed != f.PreviousHash) throw new IOException($"Colisión nueva o modificada, sin autorización: {f.Destination}");
                    var temporary = f.Destination + ".coquena-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var input = new FileStream(f.Source, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { input.CopyTo(output); output.Flush(true); }
                        File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(f.Source));
                        if (ModelDirectoryManager.Hash(temporary) != f.Hash) throw new IOException("Copia no verificada.");
                        Home.CheckLinks(f.Destination);
                        if ((File.Exists(f.Destination) ? ModelDirectoryManager.Hash(f.Destination) : null) != f.PreviousHash)
                            throw new IOException($"Colisión modificada: {f.Destination}");
                        if (f.PreviousHash is null) File.Move(temporary, f.Destination);
                        else File.Replace(temporary, f.Destination, null);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    stage?.Invoke(ModelDirectoryStage.AfterFileCopy);
                }
                stage?.Invoke(ModelDirectoryStage.BeforeCommit);
                foreach (var f in plan.Files)
                    if (ModelDirectoryManager.Hash(f.Source) != f.Hash || ModelDirectoryManager.Hash(f.Destination) != f.Hash)
                        throw new IOException("Archivos modificados antes de publicar; orígenes conservados.");
                plan = plan with { Committed = true };
                SaveModelDirectory(plan);
                stage?.Invoke(ModelDirectoryStage.AfterCommit);
            }
            AtomicCheckpoint(Location(DocumentKind.Models, null, null), Encoding.UTF8.GetBytes(ContractJson.Write(plan.Models, ContractJsonContext.Default.ModelsDocument)));
            stage?.Invoke(ModelDirectoryStage.AfterCheckpoint);
            AtomicCheckpoint(Location(DocumentKind.Config, null, null), Encoding.UTF8.GetBytes(ContractJson.Write(plan.Config, ContractJsonContext.Default.ConfigDocument)));
            stage?.Invoke(ModelDirectoryStage.BeforeSourceRemoval);
            foreach (var f in plan.Files)
            {
                if (!File.Exists(f.Source)) continue;
                if (ModelDirectoryManager.Hash(f.Source) != f.Hash || ModelDirectoryManager.Hash(f.Destination) != f.Hash)
                    throw new IOException($"Archivo modificado; origen conservado: {f.Source}");
                File.Delete(f.Source);
            }
            AppendJournal(new(DateTimeOffset.UtcNow, Guid.NewGuid(), DocumentKind.Models, null), DefaultJournalBytes);
            File.Delete(ModelDirectoryTransactionPath);
        }
        catch (Exception error)
        {
            var message = plan.Committed
                ? "Ubicación publicada; checkpoint o limpieza pendiente. El siguiente comando de escritura reintenta la recuperación."
                : "Traslado interrumpido; catálogo y configuración anteriores conservados, orígenes retenidos. El siguiente comando de escritura reintenta la copia verificada.";
            throw new StorageException("directory-recovery-pending", message + " " + error.Message, plan.Committed, error);
        }
    }
}
