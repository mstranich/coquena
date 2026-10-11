using System.Security.Cryptography;
using Coquena.Backends;
using Coquena.Models;
using Coquena.Contracts;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Configuration;

public enum ModelDirectoryStage { AfterFileCopy, BeforeCommit, AfterCommit, AfterCheckpoint, BeforeSourceRemoval }

public sealed class ModelDirectoryManager(Home home, LocalStore? storage = null,
    Action<ModelDirectoryStage>? fault = null)
{
    private readonly LocalStore store = storage ?? new(home);
    private readonly ModelPaths paths = new(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private ConfigDocument Config() => store.Read(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config) ?? new(1, null, null, new(5));
    private ModelsDocument Models() => store.Read(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models) ?? new(1, []);
    public BackendDirectoryView Show()
    {
        var own = Config().ModelsDirectory;
        return new(own ?? "models", paths.Root(own), own is not null);
    }

    // Choice and collision confirmation are outside the writer lease. Applying
    // repeats the complete inventory; consent never authorizes a new collision.
    public bool Change(string? location, TextWriter output, Func<string?> choice, Func<bool> overwrite)
    {
        ValidateLocation(location);
        var overview = store.Inspect(() => (Config(), Models()));
        var target = paths.Root(location);
        output.WriteLine($"Destino: {target} | {(location is null ? "default" : "propio")}");
        foreach (var model in overview.Item2.Models)
            foreach (var file in model.Files)
                output.WriteLine($"{model.Id:D} | {model.Name} | {paths.File(model, file)} -> {paths.Resolve(location, $"models/{model.Selection.Repository}/{file.RemotePath}")}");
        output.WriteLine("¿Trasladar todos los directorios gestionados? [s] trasladar / [n] conservar en origen / [c] cancelar");
        var answer = choice()?.Trim().ToLowerInvariant();
        if (answer is not ("s" or "si" or "sí" or "n" or "no"))
        { output.WriteLine("Cancelado; ubicación y archivos conservados."); return false; }
        var move = answer is "s" or "si" or "sí";
        if (!move)
        {
            return store.Exclusive(() =>
            {
                if (Snapshot(Config(), Models()) != Snapshot(overview.Item1, overview.Item2)) Stale();
                EnsureWritable(target);
                store.UpdateLocked(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config,
                    old => (old ?? new(1, null, null, new(5))) with { ModelsDirectory = location });
                output.WriteLine("Ubicación actualizada; instalaciones existentes conservadas en origen.");
                return true;
            });
        }
        var plan = store.Inspect(() => Plan(location));
        if (Snapshot(Config(), Models()) != Snapshot(overview.Item1, overview.Item2)) Stale();
        var collisions = plan.Files.Where(f => f.PreviousHash is not null).ToArray();
        foreach (var f in collisions) output.WriteLine($"Colisión: {f.Source} -> {f.Destination}");
        if (collisions.Length > 0)
        {
            output.WriteLine($"La fusión destruirá el contenido anterior de {collisions.Length} archivo(s). ¿Autoriza todas las sobrescrituras informadas? [s/N]");
            if (!overwrite()) { output.WriteLine("Cancelado; ubicación y archivos conservados."); return false; }
        }
        return store.Exclusive(() =>
        {
            var current = Plan(location);
            if (LocalStore.DirectoryPlanSignature(current) != LocalStore.DirectoryPlanSignature(plan)) Stale();
            EnsureWritable(target);
            CheckAccess(plan);
            store.CommitModelDirectoryLocked(plan, fault);
            output.WriteLine("Ubicación y directorios gestionados actualizados; identidades y referencias conservadas.");
            return true;
        });
    }

    private ModelDirectoryTransaction Plan(string? location)
    {
        var config = Config(); var catalog = Models();
        var moves = catalog.Models.SelectMany(model => model.Files.Select(file =>
            new ModelDirectoryMove(paths.File(model, file), paths.Resolve(location, $"models/{model.Selection.Repository}/{file.RemotePath}"))))
            .Where(m => !m.Source.Equals(m.Destination, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (moves.Select(m => m.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() != moves.Length ||
            moves.Select(m => m.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != moves.Length ||
            moves.Any(m => moves.Any(n => m.Source.Equals(n.Destination, StringComparison.OrdinalIgnoreCase))))
            throw new ConfigurationException("Archivos de unidades superpuestos; no se trasladaron.");
        CheckUsage(catalog, moves);
        var directories = new List<string>(); var files = new List<ModelDirectoryFile>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in moves)
        {
            directories.Add(Path.GetDirectoryName(m.Destination)!);
            Home.CheckLinks(m.Source); Home.CheckLinks(m.Destination);
            if (!destinations.Add(m.Destination) || Directory.Exists(m.Destination))
                throw new ConfigurationException($"Colision estructural en {m.Destination}.");
            files.Add(new(m.Source, m.Destination, Hash(m.Source), File.Exists(m.Destination) ? Hash(m.Destination) : null));
        }
        foreach (var d in directories) ValidateDestination(d);
        var next = catalog with { Models = catalog.Models.Select(model => model with {
            InstallationDirectory = location ?? "models",
            Files = model.Files.Select(file => file with { LocalPath = $"{model.Selection.Repository}/{file.RemotePath}" }).ToArray()
        }).ToArray() };
        return new(1, false, config with { ModelsDirectory = location }, next,
            config, catalog, moves, directories.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            files.OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal void CheckUsage(ModelsDocument catalog, ModelDirectoryMove[] moves)
    {
        var blockers = new List<string>();
        bool Affected(string file) => moves.Any(m => BackendPaths.Within(file, m.Source) || BackendPaths.Within(file, m.Destination));
        var affected = catalog.Models.Where(m => m.Files.Any(f => Affected(paths.File(m, f)))).Select(m => m.Id).ToHashSet();
        var run = home.PathFor("run");
        if (Directory.Exists(run)) foreach (var area in Directory.GetDirectories(run))
        {
            Home.CheckLinks(area);
            var instance = store.Read(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, server: Path.GetFileName(area));
            if (instance is null || !instance.ResourceIds.Any(affected.Contains)) continue;
            try
            {
                using var handle = WindowsServerProcess.Verified(instance, out var ended);
                if (!ended) blockers.Add($"modelos {string.Join(",", instance.ResourceIds)}, instancia {instance.InstanceId:D}, PID {instance.Pid}");
            }
            catch (ConfigurationException) { blockers.Add($"identidad incierta: modelos {string.Join(",", instance.ResourceIds)}, instancia {instance.InstanceId:D}"); }
        }
        // Read raw operations: inspection must not recover or mutate downloads.
        var downloads = home.PathFor("downloads");
        if (Directory.Exists(downloads)) foreach (var area in Directory.GetDirectories(downloads))
        {
            Home.CheckLinks(area);
            if (!Guid.TryParseExact(Path.GetFileName(area), "D", out var id))
                throw new ConfigurationException("Directorio de descarga no reconocido; no se puede asegurar el traslado.");
            var op = store.Read(DocumentKind.Operation, Json.OperationDocument, ContractValidation.Operation, operation: id);
            if (op?.ResourceKind is "model" or "companion") blockers.Add($"descarga pendiente {op.Id:D}: {op.Name} ({op.State}/{op.Phase})");
            else if (op?.Files.Any(f => Affected(op.ResourceKind == "backend" ? new BackendPaths(home).Resolve(op.BackendInstall?.InstallationDirectory, f.DestinationPath) : paths.Destination(op, f))) == true)
                blockers.Add($"descarga en destino {op.Id:D}: {op.Name}");
        }
        var backends = store.Read(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends)?.Backends ?? [];
        foreach (var backend in backends.Where(b => Affected(new BackendPaths(home).Server(b))))
            blockers.Add($"backend protegido {backend.Id:D}");
        if (blockers.Count > 0) throw new ConfigurationException("Traslado completo bloqueado:\n" + string.Join('\n', blockers));
    }

    private void ValidateLocation(string? location)
    {
        if (location is not null && (string.IsNullOrWhiteSpace(location) ||
            Path.IsPathRooted(location) && !Path.IsPathFullyQualified(location)))
            throw new ConfigurationException("La ubicación debe ser absoluta o relativa al home, sin raíz parcial.");
        var target = paths.Root(location);
        foreach (var area in new[] { "catalogs", "downloads", "run", "logs", "backends" })
            if (BackendPaths.Within(target, home.PathFor(area)))
                throw new ConfigurationException("El destino invade otra área gestionada del home.");
        if (target.Equals(home.Root, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("El directorio de modelos no puede ser el home completo.");
        ValidateDestination(target);
    }
    private static void ValidateDestination(string target)
    {
        Home.CheckLinks(target);
        for (string? p = target; p is not null; p = Path.GetDirectoryName(p))
            if (File.Exists(p)) throw new ConfigurationException($"El destino o un antecesor es un archivo: {p}.");
    }
    internal static void EnsureWritable(string target)
    {
        ValidateDestination(target);
        Directory.CreateDirectory(target);
        var probe = Path.Combine(target, ".coquena-write-" + Guid.NewGuid().ToString("N"));
        using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
        { stream.WriteByte(0); stream.Flush(true); }
    }
    internal static void CheckAccess(ModelDirectoryTransaction plan)
    {
        foreach (var d in plan.Directories) EnsureWritable(d);
        foreach (var f in plan.Files)
        {
            DirectoryAccess.Check(f.Source, false);
            if (File.Exists(f.Destination)) DirectoryAccess.Check(f.Destination, true);
        }
    }
    internal static string Hash(string path)
    {
        Home.CheckLinks(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(file));
    }
    private static string Snapshot(ConfigDocument config, ModelsDocument catalog) =>
        ContractJson.Write(config, Json.ConfigDocument) + "\n" + ContractJson.Write(catalog, Json.ModelsDocument);
    private static void Stale() => throw new ConfigurationException("El catálogo, la configuración o los archivos cambiaron durante la elección; repita el comando para revisar el nuevo informe.");
}
