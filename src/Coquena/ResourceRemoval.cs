using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena;

public sealed record RemovalResult(bool Complete, bool External, long FreedBytes, string[] Deleted, string[] Pending);

// The catalog retains ownership and the original inventory until all files are gone.
// The writer lease serializes deletion against publication and server starts.
public sealed class ResourceRemoval(Home home, LocalStore? storage = null)
{
    private readonly LocalStore store = storage ?? new(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private ModelsDocument Models() => store.Read(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models) ?? new(1, []);
    private BackendsDocument Backends() => store.Read(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends) ?? new(1, []);

    internal static void CheckPending(Home home, LocalStore store, Guid[] ids)
    {
        var pending = (store.Read(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models)?.Models ?? [])
            .Where(m => m.RemovalPending).Select(m => m.Id)
            .Concat((store.Read(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends)?.Backends ?? [])
                .Where(b => b.RemovalPending).Select(b => b.Id));
        if (pending.Any(ids.Contains)) throw new Coquena.Configuration.ConfigurationException("Retirada parcial pendiente; reintente remove/uninstall antes de utilizar el recurso.");
    }

    private void CheckUse(Guid id, bool backend)
    {
        var blockers = new List<string>();
        var run = home.PathFor("run");
        foreach (var area in Directory.Exists(run) ? Directory.GetDirectories(run) : [])
        {
            Home.CheckLinks(area);
            var instance = store.Read(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, server: Path.GetFileName(area));
            if (instance is null || !(backend ? instance.BackendId == id : instance.ResourceIds.Contains(id))) continue;
            try
            {
                using var process = WindowsServerProcess.Verified(instance, out var ended);
                if (ended) continue;
                blockers.Add(instance.InstanceId.ToString("D"));
            }
            catch (Coquena.Configuration.ConfigurationException) { blockers.Add(instance.InstanceId + " (identidad incierta)"); }
        }
        if (blockers.Count != 0) throw new StorageException("resource-in-use", "Detenga instancias: " + string.Join(", ", blockers));
        var operations = new DownloadManager(home, store).SnapshotLocked().Where(o => backend
            ? o.BackendInstall is { } b && (b.BackendId == id || b.PreviousLatest?.Id == id || b.Previous?.Id == id)
            : o.ModelInstall is { } m && (m.ModelId == id || m.Previous?.Id == id || m.ExternalPrevious?.Id == id)).ToArray();
        if (operations.Length != 0) throw new StorageException("operation-pending", "Reanude o cancele operaciones: " + string.Join(", ", operations.Select(o => o.Id)));
    }

    public RemovalResult Model(string name, bool companion = false) => store.Exclusive(() =>
    {
        var all = Models();
        var record = all.Models.SingleOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && (m.CompanionType is not null) == companion)
            ?? throw new StorageException("resource-not-found", "Unidad no registrada en el ámbito solicitado.");
        CheckUse(record.Id, false);
        if (record.Ownership == "external") { ForgetModel(record.Id); return new RemovalResult(true, true, 0, [], []); }
        var paths = new ModelPaths(home);
        var files = record.Files.Select(f => paths.File(record, f)).ToArray();
        var otherFiles = all.Models.Where(m => m.Id != record.Id).SelectMany(m => m.Files.Select(f => paths.File(m, f))).ToArray();
        if (files.Intersect(otherFiles, StringComparer.OrdinalIgnoreCase).Any()) throw new StorageException("ownership-conflict", "Archivo referenciado por otra unidad; no se borró.");
        store.UpdateLocked(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models,
            old => old! with { Models = old.Models.Select(m => m.Id == record.Id ? m with { RemovalPending = true } : m).ToArray() });
        var result = Delete(files, paths.Root(record.InstallationDirectory));
        if (result.Complete) result = Finish(result, () => ForgetModel(record.Id), () => Models().Models.Any(m => m.Id == record.Id));
        return result;
    });

    private void ForgetModel(Guid id) => store.UpdateLocked(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models,
        old => old! with { Models = old.Models.Where(m => m.Id != id).ToArray() });

    public RemovalResult Backend(Guid id) => store.Exclusive(() =>
    {
        var all = Backends();
        var record = all.Backends.SingleOrDefault(b => b.Id == id) ?? throw new StorageException("resource-not-found", "Backend no registrado.");
        CheckUse(id, true);
        if (record.Ownership == "external") { ForgetBackend(id); return new RemovalResult(true, true, 0, [], []); }
        var paths = new BackendPaths(home); var slot = paths.Slot(record);
        if (record.OwnedFiles is not { Length: > 0 }) throw new StorageException("invalid-ownership", "Inventario de instalación requerido.");
        var slotHome = new Home(slot);
        var files = record.OwnedFiles.Select(slotHome.PathFor).ToArray();
        if (all.Backends.Any(b => b.Id != id && BackendPaths.Within(paths.Server(b), slot)))
            throw new StorageException("ownership-conflict", "Directorio referenciado por otro backend; no se borró.");
        store.UpdateLocked(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends,
            old => old! with { Backends = old.Backends.Select(b => b.Id == id ? b with { RemovalPending = true } : b).ToArray() });
        var result = Delete(files, paths.Root(record.InstallationDirectory));
        if (result.Complete) result = Finish(result, () => ForgetBackend(id), () => Backends().Backends.Any(b => b.Id == id));
        return result;
    });

    private void ForgetBackend(Guid id) => store.UpdateLocked(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends,
        old => old! with { Backends = old.Backends.Where(b => b.Id != id).ToArray() });

    private static RemovalResult Finish(RemovalResult result, Action forget, Func<bool> remains)
    {
        try { forget(); return result; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or StorageException)
        {
            // Preserve this attempt's byte/file report even if the final catalog write fails.
            if (!remains()) return result;
            return result with { Complete = false, Pending = [.. result.Pending, "Retirada del registro pendiente | " + e.Message] };
        }
    }

    private static RemovalResult Delete(string[] files, string root)
    {
        long bytes = 0; var deleted = new List<string>(); var pending = new List<string>();
        foreach (var path in files)
        {
            try
            {
                // Resolve again before deletion, including every ancestor's link check.
                _ = new Home(root).PathFor(Path.GetRelativePath(root, path));
                if (Directory.Exists(path)) throw new IOException("La ruta de archivo es un directorio.");
                long length;
                try { length = new FileInfo(path).Length; }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                File.Delete(path); bytes += length; deleted.Add(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or StorageException)
            { pending.Add(path + " | " + e.Message); }
        }
        foreach (var file in files)
        {
            var directory = Path.GetDirectoryName(file);
            while (directory is not null && BackendPaths.Within(directory, root) && !directory.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _ = new Home(root).PathFor(Path.GetRelativePath(root, directory));
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or StorageException) { break; }
                directory = Path.GetDirectoryName(directory);
            }
        }
        return new(pending.Count == 0, false, bytes, deleted.ToArray(), pending.ToArray());
    }

    public static int Cli(string[] args, TextWriter output, TextWriter error, bool backend, bool companion = false)
    {
        if (args.Length != 1 || backend && !Guid.TryParse(args[0], out _)) { error.WriteLine("Error: argumentos inválidos. Consulte coquena --help."); return 2; }
        try
        {
            var removal = new ResourceRemoval(Home.Resolve());
            var result = backend ? removal.Backend(Guid.Parse(args[0])) : removal.Model(args[0], companion);
            output.WriteLine($"Espacio efectivamente liberado en este intento: {ByteSize.Format(result.FreedBytes)}.");
            foreach (var path in result.Deleted) output.WriteLine("Eliminado: " + path);
            foreach (var path in result.Pending) output.WriteLine("Pendiente: " + path);
            output.WriteLine(result.External ? "Registro externo retirado; archivos conservados en disco." : result.Complete ? "Retirada completada." : "Retirada parcial; propiedad conservada. Reintente el mismo comando.");
            return result.Complete ? 0 : 1;
        }
        catch (Exception e) when (e is StorageException or ContractException or IOException or UnauthorizedAccessException or DownloadException or Coquena.Configuration.ConfigurationException)
        { error.WriteLine("Error: " + e.Message + " Consulte coquena --help."); return 1; }
    }
}
