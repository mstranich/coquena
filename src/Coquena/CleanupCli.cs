using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;
using Coquena.Downloads;

namespace Coquena;

public static class CleanupCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Any(a => a is "clean" or "prune")) return RunClean(args, output, error);
        try
        {
            if (args.Any(a => a is "--help" or "-h"))
            { output.WriteLine(Messages.CombinationUsage); return 0; }
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
            if (!(words.Count == 2 && words[0] == "combination" && words[1] == "list") &&
                !(words.Count == 3 && words[0] == "combination" && words[1] == "forget") || flags.Contains("--yes") || flags.Contains("--dry-run") || flags.Contains("--json") && !(words.Count == 2 && words[1] == "list")) throw new ArgumentException();
            var home = Home.Resolve();
            var selection = flags.Contains("--global") ? null : new RecipeSelection(Environment.CurrentDirectory, files, directory, env);
            // Consulting must not create the home: a missing one holds nothing to prune.
            if (selection is null && !Directory.Exists(home.Root)) { output.WriteLine(Messages.PruneReclaimable(0, true)); return 0; }
            var context = selection is null ? ScopeContext.Global(home) : ScopeContext.Project(selection.Files[0], selection.Base);
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
            var cleanup = new ResourceCleanup(context, home); var plan = cleanup.Preview();
            foreach (var note in plan.Diagnostics) output.WriteLine(Messages.PruneKept(note));
            foreach (var item in plan.Items) output.WriteLine(Messages.PruneItem(item.Kind, item.Name, item.Bytes));
            output.WriteLine(Messages.PruneReclaimable(plan.Bytes, context.IsGlobal));
            return 0;
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

    private static int RunClean(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Any(a => a is "--help" or "-h"))
        { output.WriteLine(Messages.CleanupHelp); return 0; }
        MaintenanceOptions options;
        try { options = MaintenanceOptions.Parse(args); }
        catch (ArgumentException) { error.WriteLine(Messages.CleanupInvalidOptions); return 2; }
        try
        {
            var home = Home.Resolve();
            var scopes = options.Scopes(home, true);
            var plans = scopes.Select(scope => (Scope: scope, Plan: new ResourceCleanup(scope, home).Preview(selection: options.Selection))).ToArray();
            foreach (var (scope, plan) in plans)
            {
                output.WriteLine((scope.IsGlobal ? Messages.CleanupGlobalHeading : Messages.CleanupProjectHeading) + scope.Base);
                foreach (var note in plan.Diagnostics) output.WriteLine(Messages.PruneKept(note));
                foreach (var item in plan.Items) output.WriteLine(Messages.PruneItem(item.Kind, item.Name, item.Bytes));
                foreach (var item in plan.Downloads ?? []) output.WriteLine(Messages.PruneItem("download", item.Name + " · " + item.Id.ToString("D"), item.Bytes));
                output.WriteLine(Messages.CleanupReclaimable(plan.Bytes));
            }
            if (plans.Length == 0) output.WriteLine(Messages.PruneReclaimable(0, options.Global));
            if (options.DryRun || plans.All(p => p.Plan.Items.Count + (p.Plan.Downloads?.Count ?? 0) == 0)) return 0;
            if (!options.Yes)
            {
                if (Console.IsInputRedirected) { error.WriteLine(Messages.PruneConfirmationRequired); return 3; }
                output.Write(Messages.PruneAsk); output.Flush();
                if (!Messages.IsYes(Console.ReadLine())) return 0;
            }
            var failed = false;
            foreach (var (scope, original) in plans)
            {
                if (original.Items.Count + (original.Downloads?.Count ?? 0) == 0) continue;
                // Earlier scope commits advance shared revisions. Re-read, but never enlarge the confirmed set.
                var cleanup = new ResourceCleanup(scope, home);
                var current = cleanup.Preview(selection: options.Selection);
                if (!current.Items.SequenceEqual(original.Items) || !(current.Downloads ?? []).SequenceEqual(original.Downloads ?? []))
                    throw new StorageException("cleanup-conflict", Messages.CleanupSelectionChanged);
                var result = cleanup.Apply(current);
                output.WriteLine(Messages.PruneFreed(result.FreedBytes));
                foreach (var note in result.Diagnostics) error.WriteLine(Messages.PruneFailed(note.Name, note.Code));
                failed |= result.Diagnostics.Count > 0;
            }
            return failed ? 6 : 0;
        }
        catch (Exception e) when (e is StorageException or IOException or UnauthorizedAccessException or DownloadException or ArgumentException)
        {
            error.WriteLine(e switch { StorageException s => Messages.Error(s.Code, s.Message), DownloadException d => Messages.Error(d.Code, d.Message), _ => Messages.Error("cleanup-failed", e.Message) });
            return 2;
        }
    }
}
