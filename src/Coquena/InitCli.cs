namespace Coquena;

internal static class InitCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args is ["--help" or "-h"])
        {
            output.WriteLine("Uso: coquena init [<directorio>]");
            return 0;
        }

        if (args.Length > 1 || args.Length == 1 && args[0].StartsWith('-'))
        {
            error.WriteLine("Uso: coquena init [<directorio>]");
            return 2;
        }

        try
        {
            var root = Path.GetFullPath(args.Length == 0 ? "." : args[0]);
            var metadata = Path.Combine(root, ".coquena");
            var recipe = Path.Combine(root, "coquena.yaml");
            var projectIgnore = Path.Combine(root, ".gitignore");
            var metadataIgnore = Path.Combine(metadata, ".gitignore");

            foreach (var file in new[] { recipe, projectIgnore, metadataIgnore })
            {
                if (Directory.Exists(file))
                    throw new IOException($"Se esperaba un archivo en {file}.");
            }

            Directory.CreateDirectory(metadata);
            AddExclusions(projectIgnore, [".env"]);
            AddExclusions(metadataIgnore,
                ["/local/", "/combinations/*/local/", "/models/", "/backends/",
                 "/downloads/", "/tmp/", "/logs/"]);

            if (File.Exists(recipe))
            {
                output.WriteLine($"Receta existente conservada: {recipe}");
                return 0;
            }

            using (var file = new FileStream(recipe, FileMode.CreateNew, FileAccess.Write))
            {
                file.Write("{}\n"u8);
            }
            output.WriteLine($"Receta creada: {recipe}");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error.WriteLine($"Error al inicializar: {exception.Message}");
            return 1;
        }
    }

    private static void AddExclusions(string path, string[] rules)
    {
        using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var reader = new StreamReader(file, leaveOpen: true);
        var existing = reader.ReadToEnd();
        var lines = existing.Split('\n').Select(line => line.TrimEnd('\r')).ToHashSet(StringComparer.Ordinal);
        var missing = rules.Where(rule => !lines.Contains(rule)).ToArray();
        if (missing.Length == 0)
            return;

        using var writer = new StreamWriter(file, reader.CurrentEncoding, leaveOpen: true);
        if (existing.Length > 0 && !existing.EndsWith('\n'))
            writer.WriteLine();
        foreach (var rule in missing)
            writer.WriteLine(rule);
    }
}
