using Coquena.Storage;

namespace Coquena.Projects;

public sealed class ScopeContext
{
    public string Base { get; }
    public string Root { get; }
    public bool IsGlobal { get; }
    public DirectoryIdentity PhysicalBase { get; }
    internal DirectoryIdentity PhysicalRoot => PhysicalDirectory.Read(Root);

    private ScopeContext(string basePath, string root, bool global)
    {
        Base = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath));
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        IsGlobal = global;
        PhysicalBase = PhysicalDirectory.Read(Base);
    }

    public static ScopeContext Project(string firstFile, string? explicitBase = null)
    {
        var basePath = explicitBase is null ? Path.GetDirectoryName(Path.GetFullPath(firstFile))! : Path.GetFullPath(explicitBase);
        return new(basePath, Path.Combine(basePath, ".coquena"), false);
    }

    public static ScopeContext Global(Home home)
    {
        Directory.CreateDirectory(home.Root);
        return new(home.Root, Path.Combine(home.Root, "global"), true);
    }

    internal static ScopeContext At(string basePath, string root, bool global)
    {
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
            Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath)), global ? "global" : ".coquena"), StringComparison.OrdinalIgnoreCase)) Unsafe();
        return new(basePath, root, global);
    }

    // Root itself may be reached through an alias, but managed descendants may not escape it.
    public string PathFor(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) Unsafe();
        var parts = relative.Replace('\\', '/').Split('/');
        foreach (var part in parts)
        {
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) Unsafe();
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3])) Unsafe();
        }
        var path = Root;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) Unsafe();
        }
        return path;
    }

    internal void Ensure()
    {
        if (PhysicalDirectory.Read(Base) != PhysicalBase) throw new StorageException("scope-moved", "Cambió la identidad física de la base.");
        if (Directory.Exists(Root) && (File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0) Unsafe();
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(PathFor("local"));
    }

    private static void Unsafe() => throw new StorageException("unsafe-path", "Ruta fuera del ámbito gestionado o enlace no autorizado.");
}
