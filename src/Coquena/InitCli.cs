namespace Coquena;

internal static class InitCli
{
    internal const string SchemaFile = "recipe.schema.json";
    internal const string SchemaComment = "# yaml-language-server: $schema=./.coquena/" + SchemaFile;

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

            // Init brings the project to this version: the generated schema and the recipe's
            // reference to it are replaced without asking, and each change is reported.
            var schema = Path.Combine(metadata, SchemaFile);
            if (Directory.Exists(schema)) throw new IOException($"Se esperaba un archivo en {schema}.");
            var schemaText = Recipes.RecipeShape.Schema().ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n";
            var schemaExisted = File.Exists(schema);
            if (!schemaExisted || File.ReadAllText(schema) != schemaText)
            {
                File.WriteAllText(schema, schemaText);
                output.WriteLine((schemaExisted ? "Schema actualizado: " : "Schema creado: ") + schema);
            }

            if (File.Exists(recipe))
            {
                output.WriteLine($"Receta existente conservada: {recipe}");
                if (Reference(recipe) is { } change) output.WriteLine(change + ": " + recipe);
                return 0;
            }

            // Only a comment: the reader takes it as an empty recipe and editors find the schema.
            using (var file = new FileStream(recipe, FileMode.CreateNew, FileAccess.Write))
            {
                file.Write(System.Text.Encoding.UTF8.GetBytes(SchemaComment + "\n"));
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

    // Replaces every schema reference line with the current one, or adds it as the first
    // line; the rest of the file, its BOM and line endings stay. Null when nothing changed.
    private static string? Reference(string recipe)
    {
        var bytes = File.ReadAllBytes(recipe);
        var (encoding, offset) = Recipes.RecipeYaml.TextEncoding(bytes, false);
        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var pattern = new System.Text.RegularExpressions.Regex(@"^[ \t]*#[ \t]*yaml-language-server:[ \t]*\$schema=[^\r\n]*", System.Text.RegularExpressions.RegexOptions.Multiline);
        var found = pattern.IsMatch(text);
        var updated = found ? pattern.Replace(text, SchemaComment) : SchemaComment + newline + text;
        if (updated == text) return null;
        File.WriteAllBytes(recipe, [.. bytes.AsSpan(0, offset), .. encoding.GetBytes(updated)]);
        return found ? "Referencia al schema actualizada" : "Referencia al schema agregada";
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
