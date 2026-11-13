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
            { output.WriteLine("prune [--global|--project-directory ruta] [--dry-run|--yes]\nAlias: clean.\ncombination list|forget <combination-id> [--global|--project-directory ruta]"); return 0; }
            var files = new List<string>(); var env = new List<string>(); var words = new List<string>();
            var flags = new HashSet<string>(); string? directory = null;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i] switch { "-g" => "--global", "-f" => "--file", "--base" => "--project-directory", _ => args[i] };
                if (arg is "--global" or "--private" or "--yes" or "--dry-run" or "--json") { if (!flags.Add(arg)) throw new ArgumentException(); continue; }
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
            var clean = words is ["prune"] or ["clean"];
            if (!clean && !(words.Count == 2 && words[0] == "combination" && words[1] == "list") &&
                !(words.Count == 3 && words[0] == "combination" && words[1] == "forget") || !clean && (flags.Contains("--yes") || flags.Contains("--dry-run")) || flags.Contains("--json") && !(words.Count == 2 && words[1] == "list")) throw new ArgumentException();
            var home = Home.Resolve();
            var selection = flags.Contains("--global") ? null : new RecipeSelection(Environment.CurrentDirectory, files, directory, env);
            // Consulting must not create the home: a missing one holds nothing to prune.
            if (selection is null && !Directory.Exists(home.Root)) { output.WriteLine(Messages.PruneReclaimable(0, true)); return 0; }
            var context = selection is null ? ScopeContext.Global(home) : ScopeContext.Project(selection.Files[0], selection.Base);
            if (!clean)
            {
                if (words[1] == "list")
                {
                    var combinations = new CombinationPreparation(context).List();
                    if (flags.Contains("--json")) output.WriteLine(combinations.ToJsonString());
                    else if (combinations.Count == 0) output.WriteLine(Messages.NoCombinations);
                    else TextTree.Write(combinations, output);
                    return 0;
                }
                new CombinationCleanup(context).Forget(words[2]);
                output.WriteLine(Messages.CombinationForgotten);
            }
            var cleanup = new ResourceCleanup(context, home); var plan = cleanup.Preview();
            foreach (var note in plan.Diagnostics) output.WriteLine(Messages.PruneKept(note.Name, note.Code));
            foreach (var item in plan.Items) output.WriteLine(Messages.PruneItem(item.Kind, item.Name, item.Bytes));
            output.WriteLine(Messages.PruneReclaimable(plan.Bytes, context.IsGlobal));
            if (!clean || flags.Contains("--dry-run") || plan.Items.Count == 0) return 0;
            if (!flags.Contains("--yes"))
            {
                if (Console.IsInputRedirected) { error.WriteLine(Messages.PruneConfirmationRequired); return 3; }
                output.Write(Messages.PruneAsk); output.Flush();
                if (Console.ReadLine()?.Trim().ToLowerInvariant() is not ("s" or "si" or "sí" or "y" or "yes")) return 0;
            }
            var result = cleanup.Apply(plan);
            output.WriteLine(Messages.PruneFreed(result.FreedBytes));
            foreach (var note in result.Diagnostics) error.WriteLine(Messages.PruneFailed(note.Name, note.Code));
            return result.Diagnostics.Count == 0 ? 0 : 6;
        }
        catch (Exception e) when (e is ArgumentException or StorageException or DownloadException or RecipeException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine(e switch
            {
                ArgumentException => Messages.UnknownOptions(args.FirstOrDefault(a => a is "prune" or "clean" or "combination") ?? "prune"),
                StorageException s => Messages.Error(s.Code, s.Message), DownloadException d => Messages.Error(d.Code, d.Message),
                RecipeException r => Messages.Error(r.Code, r.Message), _ => Messages.Error("cleanup-failed", e.Message)
            });
            return 2;
        }
    }
}
