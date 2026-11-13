using Coquena.Projects;
using Coquena.Storage;

namespace Coquena;

// usage (alias df): every file Coquena knows, by scope, and what prune may free.
public static class UsageCli
{
    private const string Help = "usage [-f archivo] [--project-directory ruta]\n" +
        "Muestra el espacio que ocupa cada ámbito conocido y lo que se puede liberar. Alias: df.";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            string? file = null, directory = null; var seen = false;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i] switch { "-f" => "--file", "--base" => "--project-directory", _ => args[i] };
                if (arg is "--help" or "-h") { output.WriteLine(Help); return 0; }
                if (arg is "usage" or "df" && !seen) { seen = true; continue; }
                if (arg is "--file" or "--project-directory" && i + 1 < args.Length)
                {
                    if (arg == "--file") { if (file is not null) throw new ArgumentException(); file = args[++i]; }
                    else { if (directory is not null) throw new ArgumentException(); directory = args[++i]; }
                    continue;
                }
                throw new ArgumentException();
            }
            var home = Home.Resolve();
            var recipe = Path.GetFullPath(file ?? Path.Combine(directory ?? Environment.CurrentDirectory, "coquena.yaml"));
            var project = File.Exists(recipe) ? ScopeContext.Project(recipe, directory) : null;
            var globalRoot = Path.Combine(home.Root, "global");
            // Without a project, survey from the global scope; neither path creates folders.
            var anchor = project ?? (Directory.Exists(home.Root) ? ScopeContext.Global(home) : null);
            var survey = anchor is null ? new UsageSurvey([], []) : new ResourceCleanup(anchor, home).Survey();
            Write(survey, project, output);
            return 0;
        }
        catch (ArgumentException)
        {
            error.WriteLine("No se reconocen esas opciones. Uso: " + Help.Split('\n')[0]);
            return 2;
        }
        catch (Exception e) when (e is StorageException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine("No se pudo medir el espacio: " + e.Message);
            return 2;
        }
    }

    internal static void Write(UsageSurvey survey, ScopeContext? project, TextWriter output)
    {
        var own = project is null ? null : survey.Scopes.FirstOrDefault(s => Same(s.Scope, project));
        var global = survey.Scopes.FirstOrDefault(s => s.Scope.IsGlobal);
        if (project is null) output.WriteLine("No hay un proyecto en esta carpeta.");
        else if (own is null) output.WriteLine("Este proyecto todavía no guarda archivos.");
        else Section("Este proyecto", own, "coquena prune", output);
        output.WriteLine();
        if (global is null) output.WriteLine("El ámbito global todavía no guarda archivos.");
        else Section("Global", global, "coquena prune --global", output);
        foreach (var other in survey.Scopes.Where(s => s != own && s != global))
        {
            output.WriteLine();
            Section("Otro proyecto", other, $"coquena prune --project-directory \"{other.Scope.Base}\"", output);
        }
        if (survey.Skipped.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("⚠ No se pudieron leer estos proyectos conocidos; lo marcado como liberable puede estar en uso por ellos:");
            foreach (var path in survey.Skipped) output.WriteLine("  " + path);
        }
    }

    private static bool Same(ScopeContext a, ScopeContext b) =>
        string.Equals(a.Root, b.Root, StringComparison.OrdinalIgnoreCase);

    private static void Section(string title, ScopeUsage usage, string prune, TextWriter output)
    {
        output.WriteLine($"{title}: {ByteSize.Format(usage.Total)}  ({usage.Scope.Base})");
        foreach (var (kind, label) in new[] { ("model", "Modelos"), ("companion", "Auxiliares"), ("backend", "Backends") })
        {
            var items = usage.Resources.Where(r => r.Kind == kind).ToArray();
            if (items.Length == 0) continue;
            var free = items.Where(r => r.Status == UsageStatus.Free).Sum(r => r.Bytes);
            output.WriteLine($"  {label,-12}{ByteSize.Format(items.Sum(r => r.Bytes)),12}{(free > 0 ? $"   libre {ByteSize.Format(free)}" : "")}");
        }
        if (usage.Downloads > 0) output.WriteLine($"  {"Descargas",-12}{ByteSize.Format(usage.Downloads),12}   sin terminar");
        if (usage.Logs > 0) output.WriteLine($"  {"Logs",-12}{ByteSize.Format(usage.Logs),12}");
        foreach (var item in usage.Resources.Where(r => r.Status == UsageStatus.Free))
            output.WriteLine($"    sin uso: {item.Name} ({ByteSize.Format(item.Bytes)})");
        var external = usage.Resources.Where(r => r.Status == UsageStatus.External).Sum(r => r.Bytes);
        if (external > 0) output.WriteLine($"  Incluye {ByteSize.Format(external)} de archivos externos que Coquena no borra.");
        output.WriteLine(usage.Free > 0 ? $"  Se pueden liberar {ByteSize.Format(usage.Free)} con {prune}." : "  No hay nada para liberar.");
    }
}
