using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Backends;

// Operation slots remain logical (backends/id/operation/...). New catalog paths
// are relative to their stored base; absent bases preserve legacy home paths.
public sealed class BackendPaths(Home home)
{
    public string Root(string? location)
    {
        var result = new Home(Path.GetFullPath(location ?? "backends", home.Root)).Root;
        var volume = Path.GetPathRoot(result)!;
        if (!result.Equals(Path.TrimEndingDirectorySeparator(volume), StringComparison.OrdinalIgnoreCase))
            _ = new Home(volume).PathFor(Path.GetRelativePath(volume, result));
        return result;
    }
    public string Resolve(string? location, string logical)
    {
        if (!logical.StartsWith("backends/", StringComparison.Ordinal))
            throw new StorageException("unsafe-backend-path", "Ruta de backend fuera de su instalación.");
        return new Home(Root(location)).PathFor(logical[9..]);
    }
    public string Server(BackendRecord backend) => backend.Ownership == "managed" && backend.Origin == "ggml"
        ? backend.InstallationDirectory is null ? home.PathFor(backend.ServerPath)
            : new Home(Root(backend.InstallationDirectory)).PathFor(backend.ServerPath)
        : Path.IsPathFullyQualified(backend.ServerPath) ? backend.ServerPath
        : throw new StorageException("unsafe-backend-path", "Referencia externa sin ruta absoluta.");
    public string Slot(BackendRecord backend)
    {
        var parts = backend.ServerPath.Split('/');
        var start = backend.InstallationDirectory is null ? 1 : 0;
        if (parts.Length < start + 3 || start == 1 && parts[0] != "backends" || parts[start] != backend.Id.ToString("D") ||
            !Guid.TryParseExact(parts[start + 1], "D", out _))
            throw new StorageException("unsafe-backend-slot", "No se reconoce el directorio de instalación gestionado.");
        return Resolve(backend.InstallationDirectory, "backends/" + string.Join('/', parts[start..(start + 2)]));
    }
    public static bool Within(string path, string directory) => path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static void CheckTree(string directory)
    {
        Home.CheckLinks(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            Home.CheckLinks(entry);
            if (Directory.Exists(entry)) CheckTree(entry);
        }
    }
    public static void RemoveOwnedTree(string directory)
    {
        CheckTree(directory);
        Directory.Delete(directory, recursive: true);
    }
}
