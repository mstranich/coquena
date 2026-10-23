namespace Coquena.Storage;

public sealed class StorageException(string code, string message, bool published = false, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
    public bool Published { get; } = published;
}

public sealed class Home
{
    internal Projects.ScopeContext? Scope { get; }
    public string Root { get; }
    internal Home(Projects.ScopeContext scope)
    {
        Scope = scope;
        Root = scope.Root;
    }
    public Home(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new StorageException("invalid-home", "COQUENA_HOME debe ser una ruta absoluta no vacía.");
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        CheckLinks(Root);
    }

    public static Home Resolve() => Resolve(Environment.GetEnvironmentVariable("COQUENA_HOME"),
        Environment.GetEnvironmentVariable("USERPROFILE"));

    public static Home Resolve(string? configured, string? profile)
    {
        if (configured is not null) return new Home(configured);
        if (string.IsNullOrWhiteSpace(profile) || !Path.IsPathFullyQualified(profile))
            throw new StorageException("invalid-home", "USERPROFILE no proporciona un directorio absoluto.");
        return new Home(Path.Combine(profile, ".coquena"));
    }

    public void Ensure()
    {
        if (Scope is not null)
        {
            Scope.Ensure();
            foreach (var area in new[] { "downloads", "logs", "tmp" }) Directory.CreateDirectory(PathFor(area));
            return;
        }
        CheckLinks(Root);
        Directory.CreateDirectory(Root);
        foreach (var area in new[] { "catalogs", "run", "logs", "downloads", "models", "backends" })
            Directory.CreateDirectory(PathFor(area));
    }

    public string PathFor(string relative)
    {
        if (Scope is not null) return Scope.PathFor(relative);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) Invalid();
        var pieces = relative.Replace('\\', '/').Split('/');
        foreach (var piece in pieces)
        {
            if (piece is "" or "." or ".." || piece.EndsWith('.') || piece.EndsWith(' ') ||
                piece.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) Invalid();
            var stem = piece.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                 stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3]))) Invalid();
        }
        var path = Path.GetFullPath(Path.Combine(Root, Path.Combine(pieces)));
        var prefix = Path.EndsInDirectorySeparator(Root) ? Root : Root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix,
                StringComparison.OrdinalIgnoreCase)) Invalid();
        CheckLinks(path);
        return path;

        static void Invalid() => throw new StorageException("unsafe-path", "Ruta fuera del área gestionada o nombre inválido.");
    }

    internal static void CheckLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new StorageException("unsafe-path", "No se admiten enlaces/reparse points en el almacenamiento gestionado.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal void CheckPath(string path)
    {
        if (Scope is null) CheckLinks(path);
        else _ = Scope.PathFor(Path.GetRelativePath(Root, path));
    }
}
