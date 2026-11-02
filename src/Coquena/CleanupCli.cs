using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;
using Coquena.Downloads;

namespace Coquena;

public static class CleanupCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Any(a => a is "--help" or "-h"))
            { output.WriteLine("clean [--global|--project-directory ruta] [--dry-run|--yes]\ncombination list|forget <combination-id> [--global|--project-directory ruta]"); return 0; }
            var files = new List<string>(); var env = new List<string>(); var words = new List<string>();
            var flags = new HashSet<string>(); string? directory = null;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i] switch { "-g" => "--global", "-f" => "--file", "--base" => "--project-directory", _ => args[i] };
                if (arg is "--global" or "--private" or "--yes" or "--dry-run") { if (!flags.Add(arg)) throw new ArgumentException(); continue; }
                if (arg is "--file" or "--env-file" or "--project-directory")
                {
                    if (++i == args.Length) throw new ArgumentException();
                    if (arg == "--file") files.Add(args[i]);
                    else if (arg == "--env-file") env.Add(args[i]);
                    else { if (directory is not null) throw new ArgumentException(); directory = args[i]; }
                }
                else if (arg.StartsWith('-')) throw new ArgumentException();
                else words.Add(arg);
            }
            if (flags.Contains("--yes") && flags.Contains("--dry-run") || flags.Contains("--global") &&
                (directory is not null || files.Count > 0 || env.Count > 0 || flags.Contains("--private"))) throw new ArgumentException();
            var clean = words.SequenceEqual(new[] { "clean" });
            if (!clean && !(words.Count == 2 && words[0] == "combination" && words[1] == "list") &&
                !(words.Count == 3 && words[0] == "combination" && words[1] == "forget") || !clean && (flags.Contains("--yes") || flags.Contains("--dry-run"))) throw new ArgumentException();
            var home = Home.Resolve();
            var selection = flags.Contains("--global") ? null : new RecipeSelection(Environment.CurrentDirectory, files, directory, env);
            var context = selection is null ? ScopeContext.Global(home) : ScopeContext.Project(selection.Files[0], selection.Base);
            if (!clean)
            {
                if (words[1] == "list") { output.WriteLine(new CombinationPreparation(context).List().ToJsonString()); return 0; }
                new CombinationCleanup(context).Forget(words[2]);
                output.WriteLine("Combinación olvidada; YAML, pesos y publicaciones conservados.");
            }
            var cleanup = new ResourceCleanup(context, home); var plan = cleanup.Preview();
            foreach (var diagnostic in plan.Diagnostics) output.WriteLine("Conservado: " + diagnostic);
            foreach (var item in plan.Items) output.WriteLine($"{item.Kind} {item.InstallationId}: {ByteSize.Format(item.Bytes)}");
            output.WriteLine($"Recuperable: {ByteSize.Format(plan.Bytes)}. Para limpiar: clean{(context.IsGlobal ? " --global" : "")}.");
            if (!clean || flags.Contains("--dry-run") || plan.Items.Count == 0) return 0;
            if (!flags.Contains("--yes"))
            {
                if (Console.IsInputRedirected) { error.WriteLine("Error: confirmation-required; use --yes para confirmar."); return 2; }
                output.Write("¿Eliminar el conjunto mostrado? [s/N] "); output.Flush();
                if (Console.ReadLine()?.Trim().ToLowerInvariant() is not ("s" or "si" or "sí" or "y" or "yes")) return 0;
            }
            var result = cleanup.Apply(plan);
            output.WriteLine($"Liberado: {ByteSize.Format(result.FreedBytes)}.");
            foreach (var diagnostic in result.Diagnostics) error.WriteLine(diagnostic);
            return result.Diagnostics.Count == 0 ? 0 : 6;
        }
        catch (Exception e) when (e is ArgumentException or StorageException or DownloadException or RecipeException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine("Error: " + (e switch { StorageException s => s.Code, DownloadException d => d.Code, RecipeException r => r.Code, ArgumentException => "invalid-selection", _ => "cleanup-failed" }));
            return 2;
        }
    }
}
