using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coquena.Backends;
using Coquena.Configuration;
using Coquena.Contracts;

namespace Coquena.Storage;

internal sealed record BackendDirectoryMove(string Source, string Destination);
internal sealed record BackendDirectoryFile(string Source, string Destination, string Hash, string? PreviousHash);
internal sealed record BackendDirectoryTransaction(int SchemaVersion, bool Committed, ConfigDocument Config,
    BackendsDocument Backends, ConfigDocument BeforeConfig, BackendsDocument BeforeBackends,
    BackendDirectoryMove[] Moves, string[] Directories, BackendDirectoryFile[] Files);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BackendDirectoryTransaction))]
internal partial class BackendDirectoryTransactionJson : JsonSerializerContext;

public sealed partial class LocalStore
{
    private string BackendDirectoryTransactionPath => home.PathFor("catalogs/backend-directory-transaction.json");
    internal static string DirectoryPlanSignature(BackendDirectoryTransaction plan) =>
        JsonSerializer.Serialize(plan, BackendDirectoryTransactionJson.Default.BackendDirectoryTransaction);
    private BackendDirectoryTransaction? PendingBackendDirectory()
    {
        var bytes = ReadBytes(BackendDirectoryTransactionPath);
        if (bytes is null) return null;
        var value = ContractJson.Read(Encoding.UTF8.GetString(bytes), BackendDirectoryTransactionJson.Default.BackendDirectoryTransaction, ValidateBackendDirectory);
        var paths = new BackendPaths(home);
        foreach (var move in value.Moves)
        {
            // The destructive cleanup path must be a recognized old managed slot,
            // reconstructed from the before snapshot rather than trusted verbatim.
            if (!value.Backends.Backends.Any(b => b.Origin == "ggml" && paths.Slot(b).Equals(move.Destination, StringComparison.OrdinalIgnoreCase)) ||
                !value.BeforeBackends.Backends.Any(b => b.Origin == "ggml" && paths.Slot(b).Equals(move.Source, StringComparison.OrdinalIgnoreCase)))
                throw new StorageException("unsafe-directory-transaction", "Destino no pertenece al catálogo publicado.");
        }
        return value;
    }
    private static void ValidateBackendDirectory(BackendDirectoryTransaction value)
    {
        ContractJson.Check(value.SchemaVersion == 1 && value.Config is not null && value.Backends is not null &&
            value.Moves is not null && value.Directories is not null && value.Files is not null, "Traslado inválido.");
        ContractValidation.Config(value.Config!); ContractValidation.Backends(value.Backends!);
        ContractValidation.Config(value.BeforeConfig); ContractValidation.Backends(value.BeforeBackends);
        foreach (var move in value.Moves!)
            ContractJson.Check(Path.IsPathFullyQualified(move.Source) && Path.IsPathFullyQualified(move.Destination) &&
                !BackendPaths.Within(move.Source, move.Destination) && !BackendPaths.Within(move.Destination, move.Source), "Directorios de traslado inválidos.");
        foreach (var f in value.Files!)
            ContractJson.Check(f.Hash.Length == 64 && (f.PreviousHash is null || f.PreviousHash.Length == 64) &&
                value.Moves.Any(m => BackendPaths.Within(f.Source, m.Source) &&
                    Path.Combine(m.Destination, Path.GetRelativePath(m.Source, f.Source)).Equals(f.Destination, StringComparison.OrdinalIgnoreCase)), "Archivo fuera del traslado.");
        foreach (var d in value.Directories!)
            ContractJson.Check(value.Moves.Any(m => BackendPaths.Within(d, m.Destination)), "Directorio fuera del traslado.");
    }
    private byte[]? BackendDirectoryBytes(DocumentKind kind)
    {
        if (kind is not (DocumentKind.Config or DocumentKind.Backends)) return null;
        var plan = PendingBackendDirectory();
        if (plan?.Committed != true) return null;
        return Encoding.UTF8.GetBytes(kind == DocumentKind.Config
            ? ContractJson.Write(plan.Config, ContractJsonContext.Default.ConfigDocument)
            : ContractJson.Write(plan.Backends, ContractJsonContext.Default.BackendsDocument));
    }
    private void SaveBackendDirectory(BackendDirectoryTransaction plan)
    {
        ValidateBackendDirectory(plan);
        var bytes = Encoding.UTF8.GetBytes(DirectoryPlanSignature(plan));
        if (bytes.Length > MaxDocumentBytes) throw new StorageException("document-too-large", "Traslado supera 16 MiB.");
        AtomicCheckpoint(BackendDirectoryTransactionPath, bytes);
    }
    internal void CommitBackendDirectoryLocked(BackendDirectoryTransaction plan, Action<BackendDirectoryStage>? stage)
    {
        if (PendingBackendDirectory() is not null) throw new StorageException("directory-transaction-pending", "Traslado pendiente de recuperación.");
        SaveBackendDirectory(plan);
        RecoverBackendDirectoryTransaction(stage);
    }
    private void RecoverBackendDirectoryTransaction(Action<BackendDirectoryStage>? stage = null)
    {
        var plan = PendingBackendDirectory();
        if (plan is null) return;
        try
        {
            new BackendDirectoryManager(home, this).CheckUsage(plan.BeforeBackends, plan.Moves);
            if (!plan.Committed)
            {
                var config = ReadBytes(Location(DocumentKind.Config, null, null));
                var catalog = ReadBytes(Location(DocumentKind.Backends, null, null));
                var before = ContractJson.Write(config is null ? new(1, null, null, new(5)) : ContractJson.Read(Encoding.UTF8.GetString(config), ContractJsonContext.Default.ConfigDocument, ContractValidation.Config), ContractJsonContext.Default.ConfigDocument) + "\n" +
                    ContractJson.Write(catalog is null ? new(1, []) : ContractJson.Read(Encoding.UTF8.GetString(catalog), ContractJsonContext.Default.BackendsDocument, ContractValidation.Backends), ContractJsonContext.Default.BackendsDocument);
                if (before != ContractJson.Write(plan.BeforeConfig, ContractJsonContext.Default.ConfigDocument) + "\n" + ContractJson.Write(plan.BeforeBackends, ContractJsonContext.Default.BackendsDocument))
                    throw new StorageException("stale-directory-transaction", "Catálogo/configuración modificados durante un traslado pendiente.");
                BackendDirectoryManager.CheckAccess(plan);
                foreach (var f in plan.Files)
                {
                    if (BackendDirectoryManager.Hash(f.Source) != f.Hash) throw new IOException($"Origen modificado: {f.Source}");
                    var observed = File.Exists(f.Destination) ? BackendDirectoryManager.Hash(f.Destination) : null;
                    if (observed == f.Hash) continue; // Already verified and published by this plan.
                    if (observed != f.PreviousHash) throw new IOException($"Colisión nueva o modificada, sin autorización: {f.Destination}");
                    var temporary = f.Destination + ".coquena-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var input = new FileStream(f.Source, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { input.CopyTo(output); output.Flush(true); }
                        if (BackendDirectoryManager.Hash(temporary) != f.Hash) throw new IOException("Copia no verificada.");
                        Home.CheckLinks(f.Destination);
                        if ((File.Exists(f.Destination) ? BackendDirectoryManager.Hash(f.Destination) : null) != f.PreviousHash)
                            throw new IOException($"Colisión modificada: {f.Destination}");
                        if (f.PreviousHash is null) File.Move(temporary, f.Destination);
                        else File.Replace(temporary, f.Destination, null);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    stage?.Invoke(BackendDirectoryStage.AfterFileCopy);
                }
                stage?.Invoke(BackendDirectoryStage.BeforeCommit);
                foreach (var f in plan.Files)
                    if (BackendDirectoryManager.Hash(f.Source) != f.Hash || BackendDirectoryManager.Hash(f.Destination) != f.Hash)
                        throw new IOException("Archivos modificados antes de publicar; orígenes conservados.");
                plan = plan with { Committed = true };
                SaveBackendDirectory(plan);
                stage?.Invoke(BackendDirectoryStage.AfterCommit);
            }
            AtomicCheckpoint(Location(DocumentKind.Backends, null, null), Encoding.UTF8.GetBytes(ContractJson.Write(plan.Backends, ContractJsonContext.Default.BackendsDocument)));
            stage?.Invoke(BackendDirectoryStage.AfterCheckpoint);
            AtomicCheckpoint(Location(DocumentKind.Config, null, null), Encoding.UTF8.GetBytes(ContractJson.Write(plan.Config, ContractJsonContext.Default.ConfigDocument)));
            stage?.Invoke(BackendDirectoryStage.BeforeSourceRemoval);
            foreach (var move in plan.Moves)
            {
                if (!Directory.Exists(move.Source)) continue;
                BackendPaths.CheckTree(move.Source);
                var expected = plan.Files.Where(f => BackendPaths.Within(f.Source, move.Source)).ToDictionary(f => f.Source, StringComparer.OrdinalIgnoreCase);
                var actual = Directory.GetFiles(move.Source, "*", SearchOption.AllDirectories);
                if (actual.Any(p => !expected.TryGetValue(p, out var f) || BackendDirectoryManager.Hash(p) != f.Hash) ||
                    Directory.GetDirectories(move.Source, "*", SearchOption.AllDirectories).Any(d =>
                        !plan.Directories.Contains(Path.Combine(move.Destination, Path.GetRelativePath(move.Source, d)), StringComparer.OrdinalIgnoreCase)))
                    throw new IOException($"El origen cambió; no se elimina: {move.Source}");
                foreach (var f in expected.Values)
                    if (BackendDirectoryManager.Hash(f.Destination) != f.Hash) throw new IOException($"Destino modificado; origen conservado: {f.Destination}");
                BackendPaths.RemoveOwnedTree(move.Source);
            }
            AppendJournal(new(DateTimeOffset.UtcNow, Guid.NewGuid(), DocumentKind.Backends, null), DefaultJournalBytes);
            File.Delete(BackendDirectoryTransactionPath);
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
