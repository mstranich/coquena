using System.Security.Cryptography;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Configuration;

public enum BackendDirectoryStage { AfterFileCopy, BeforeCommit, AfterCommit, AfterCheckpoint, BeforeSourceRemoval }
public sealed record BackendDirectoryView(string Location, string EffectivePath, bool Own);

public sealed class BackendDirectoryManager(Home home, LocalStore? storage = null,
    Action<BackendDirectoryStage>? fault = null)
{
    private readonly LocalStore store = storage ?? new(home);
    private readonly BackendPaths paths = new(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private ConfigDocument Config() => store.Read(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config) ?? new(1, null, null, new(5));
    private BackendsDocument Backends() => store.Read(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends) ?? new(1, []);
    public BackendDirectoryView Show()
    {
        var own = Config().BackendsDirectory;
        return new(own ?? "backends", paths.Root(own), own is not null);
    }

    // Choice and collision confirmation are outside the writer lease. Applying
    // repeats the complete inventory; consent never authorizes a new collision.
    public bool Change(string? location, TextWriter output, Func<string?> choice, Func<bool> overwrite)
    {
        ValidateLocation(location);
        var overview = store.Inspect(() => (Config(), Backends()));
        var target = paths.Root(location);
        output.WriteLine($"Destino: {target} | {(location is null ? "default" : "propio")}");
        foreach (var b in overview.Item2.Backends.Where(b => b.Origin == "ggml"))
            output.WriteLine($"{b.Id:D} | {b.Variant}:{b.Build} | {paths.Slot(b)} -> {paths.Resolve(location, SlotLogical(b))}");
        output.WriteLine("¿Trasladar todos los directorios gestionados? [s] trasladar / [n] conservar en origen / [c] cancelar");
        var answer = choice()?.Trim().ToLowerInvariant();
        if (answer is not ("s" or "si" or "sí" or "n" or "no"))
        { output.WriteLine("Cancelado; ubicación y archivos conservados."); return false; }
        var move = answer is "s" or "si" or "sí";
        if (!move)
        {
            return store.Exclusive(() =>
            {
                if (Snapshot(Config(), Backends()) != Snapshot(overview.Item1, overview.Item2)) Stale();
                EnsureWritable(target);
                store.UpdateLocked(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config,
                    old => (old ?? new(1, null, null, new(5))) with { BackendsDirectory = location });
                output.WriteLine("Ubicación actualizada; instalaciones existentes conservadas en origen.");
                return true;
            });
        }
        var plan = store.Inspect(() => Plan(location));
        if (Snapshot(Config(), Backends()) != Snapshot(overview.Item1, overview.Item2)) Stale();
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
            store.CommitBackendDirectoryLocked(plan, fault);
            output.WriteLine("Ubicación y directorios gestionados actualizados; identidades y referencias conservadas.");
            return true;
        });
    }

    private BackendDirectoryTransaction Plan(string? location)
    {
        var config = Config(); var catalog = Backends();
        var moves = catalog.Backends.Where(b => b.Origin == "ggml")
            .Select(b => new BackendDirectoryMove(paths.Slot(b), paths.Resolve(location, SlotLogical(b))))
            .Where(m => !m.Source.Equals(m.Destination, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var m in moves)
        {
            if (BackendPaths.Within(m.Source, m.Destination) || BackendPaths.Within(m.Destination, m.Source))
                throw new ConfigurationException("Origen y destino de una instalación no pueden contenerse mutuamente.");
            BackendPaths.CheckTree(m.Source);
        }
        for (var i = 0; i < moves.Length; i++)
            for (var j = i + 1; j < moves.Length; j++)
                if (BackendPaths.Within(moves[i].Source, moves[j].Source) ||
                    BackendPaths.Within(moves[i].Destination, moves[j].Source) ||
                    BackendPaths.Within(moves[i].Source, moves[j].Destination))
                    throw new ConfigurationException("Las instalaciones tienen directorios superpuestos; no se trasladaron.");
        var directories = new List<string>(); var files = new List<BackendDirectoryFile>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in moves)
        {
            directories.Add(m.Destination);
            foreach (var d in Directory.GetDirectories(m.Source, "*", SearchOption.AllDirectories))
                directories.Add(Path.Combine(m.Destination, Path.GetRelativePath(m.Source, d)));
            foreach (var source in Directory.GetFiles(m.Source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(m.Destination, Path.GetRelativePath(m.Source, source));
                Home.CheckLinks(destination);
                if (!destinations.Add(destination) || Directory.Exists(destination))
                    throw new ConfigurationException($"Colisión estructural en {destination}; no se trasladaron archivos.");
                files.Add(new(source, destination, Hash(source), File.Exists(destination) ? Hash(destination) : null));
            }
        }
        foreach (var d in directories) ValidateDestination(d);
        CheckUsage(catalog, moves);
        var next = catalog with { Backends = catalog.Backends.Select(b => b.Origin == "ggml"
            ? b with { InstallationDirectory = location ?? "backends", ServerPath = b.InstallationDirectory is null ? b.ServerPath[9..] : b.ServerPath } : b).ToArray() };
        return new(1, false, config with { BackendsDirectory = location }, next,
            config, catalog, moves, directories.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            files.OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal void CheckUsage(BackendsDocument catalog, BackendDirectoryMove[] moves)
    {
        var blockers = new List<string>();
        bool Affected(string file) => moves.Any(m => BackendPaths.Within(file, m.Source) || BackendPaths.Within(file, m.Destination));
        var affected = catalog.Backends.Where(b => Affected(paths.Server(b))).Select(b => b.Id).ToHashSet();
        var run = home.PathFor("run");
        if (Directory.Exists(run)) foreach (var area in Directory.GetDirectories(run))
        {
            Home.CheckLinks(area);
            var instance = store.Read(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, server: Path.GetFileName(area));
            if (instance is null || !(affected.Contains(instance.BackendId) || instance.ExecutablePath is { } exe && Affected(exe))) continue;
            try
            {
                using var handle = WindowsServerProcess.Verified(instance, out var ended);
                if (!ended) blockers.Add($"backend {instance.BackendId:D}, instancia {instance.InstanceId:D}, PID {instance.Pid}");
            }
            catch (ConfigurationException) { blockers.Add($"identidad incierta: backend {instance.BackendId:D}, instancia {instance.InstanceId:D}"); }
        }
        // Read raw operations: inspection must not recover or mutate downloads.
        var downloads = home.PathFor("downloads");
        if (Directory.Exists(downloads)) foreach (var area in Directory.GetDirectories(downloads))
        {
            Home.CheckLinks(area);
            if (!Guid.TryParseExact(Path.GetFileName(area), "D", out var id))
                throw new ConfigurationException("Directorio de descarga no reconocido; no se puede asegurar el traslado.");
            var op = store.Read(DocumentKind.Operation, Json.OperationDocument, ContractValidation.Operation, operation: id);
            if (op?.ResourceKind == "backend") blockers.Add($"descarga pendiente {op.Id:D}: {op.Name} ({op.State}/{op.Phase})");
            else if (op?.Files.Any(f => Affected(home.PathFor(f.DestinationPath))) == true)
                blockers.Add($"descarga en destino {op.Id:D}: {op.Name}");
        }
        foreach (var b in catalog.Backends.Where(b => b.Origin != "ggml" && Affected(paths.Server(b))))
            blockers.Add($"referencia externa protegida {b.Id:D}: {paths.Server(b)}");
        var models = store.Read(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models)?.Models ?? [];
        foreach (var m in models.Where(m => m.Files.Any(f => Affected(home.PathFor(f.LocalPath)))))
            blockers.Add($"modelo protegido {m.Id:D}: {m.Name}");
        if (blockers.Count > 0) throw new ConfigurationException("Traslado completo bloqueado:\n" + string.Join('\n', blockers));
    }

    private void ValidateLocation(string? location)
    {
        if (location is not null && (string.IsNullOrWhiteSpace(location) ||
            Path.IsPathRooted(location) && !Path.IsPathFullyQualified(location)))
            throw new ConfigurationException("La ubicación debe ser absoluta o relativa al home, sin raíz parcial.");
        var target = paths.Root(location);
        foreach (var area in new[] { "catalogs", "downloads", "run", "logs", "models" })
            if (BackendPaths.Within(target, home.PathFor(area)))
                throw new ConfigurationException("El destino invade otra área gestionada del home.");
        if (target.Equals(home.Root, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("El directorio de backends no puede ser el home completo.");
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
    internal static void CheckAccess(BackendDirectoryTransaction plan)
    {
        foreach (var d in plan.Directories) EnsureWritable(d);
        foreach (var m in plan.Moves)
        {
            DirectoryAccess.Check(m.Source, false);
            foreach (var d in Directory.GetDirectories(m.Source, "*", SearchOption.AllDirectories)) DirectoryAccess.Check(d, false);
        }
        foreach (var f in plan.Files)
        {
            DirectoryAccess.Check(f.Source, false);
            if (File.Exists(f.Destination)) DirectoryAccess.Check(f.Destination, true);
        }
    }
    private static string SlotLogical(BackendRecord b)
    {
        var parts = b.ServerPath.Split('/');
        var start = b.InstallationDirectory is null ? 1 : 0;
        return "backends/" + string.Join('/', parts[start..(start + 2)]);
    }
    internal static string Hash(string path)
    {
        Home.CheckLinks(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(file));
    }
    private static string Snapshot(ConfigDocument config, BackendsDocument catalog) =>
        ContractJson.Write(config, Json.ConfigDocument) + "\n" + ContractJson.Write(catalog, Json.BackendsDocument);
    private static void Stale() => throw new ConfigurationException("El catálogo, la configuración o los archivos cambiaron durante la elección; repita el comando para revisar el nuevo informe.");
}
