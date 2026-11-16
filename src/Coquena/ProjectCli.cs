using System.Text.Json.Nodes;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena;

// project list|forget: the known-projects index, including folders that no longer resolve.
public static class ProjectCli
{
    private static string Help => Messages.ProjectHelp;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Any(a => a is "--help" or "-h")) { output.WriteLine(Help); return 0; }
        var json = args.Contains("--json");
        var dry = args.Contains("--dry-run"); var yes = args.Contains("--yes"); var inaccessible = args.Contains("--inaccessible");
        var words = args.Where(a => a is not ("--json" or "--dry-run" or "--yes" or "--inaccessible")).ToArray();
        if (words.Length < 2 || words[0] != "project" || words[1] is not ("list" or "forget") ||
            words[1] == "list" && (words.Length != 2 || dry || yes || inaccessible) ||
            words[1] == "forget" && (json || dry && yes || inaccessible == (words.Length > 2)) ||
            args.Where(a => a.StartsWith("--")).GroupBy(a => a).Any(g => g.Count() > 1) || words.Skip(1).Any(a => a.StartsWith('-')))
        {
            error.WriteLine(Messages.Error("invalid-arguments", Messages.ProjectInvalidArguments));
            return 2;
        }
        try
        {
            var home = Home.Resolve();
            var index = new ProjectIndex(home);
            if (words[1] == "forget")
            {
                var plan = index.PreviewForget(words[2..], inaccessible);
                foreach (var project in plan.Projects) output.WriteLine(project.ScopeId + "  " + project.Base);
                output.WriteLine(Messages.ProjectForgetSummary(plan.Projects.Count));
                if (dry || plan.Projects.Count == 0) return 0;
                if (!yes)
                {
                    if (Console.IsInputRedirected) { error.WriteLine(Messages.ProjectForgetConfirmationRequired); return 3; }
                    output.Write(Messages.ProjectForgetAsk); output.Flush();
                    if (!Messages.IsYes(Console.ReadLine())) return 0;
                }
                index.Forget(plan);
                foreach (var project in plan.Projects) output.WriteLine(Messages.ProjectForgotten(project.Base));
                return 0;
            }
            var projects = index.List();
            if (json)
            {
                output.WriteLine(new JsonArray(projects.Select(p => (JsonNode)new JsonObject
                {
                    ["scope-id"] = p.ScopeId, ["base"] = p.Base, ["state"] = Code(p.State)
                }).ToArray()).ToJsonString());
                return 0;
            }
            if (projects.Length == 0) { output.WriteLine(Messages.NoKnownProjects); return 0; }
            var ids = ShortIds(projects);
            var width = ids.Max(id => id.Length);
            for (var i = 0; i < projects.Length; i++)
                output.WriteLine($"{ids[i].PadRight(width)}  {Messages.ProjectState(Code(projects[i].State)),-14}  {projects[i].Base}");
            return 0;
        }
        catch (Exception e) when (e is StorageException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine(Messages.Error(e is StorageException s ? s.Code : "project-index-failed", e.Message));
            return e is StorageException { Code: "unknown-project" or "ambiguous-project" or "invalid-selection" } ? 2 : 1;
        }
    }

    internal static string Code(KnownProjectState state) => state switch
    {
        KnownProjectState.Available => "available",
        KnownProjectState.NoRecipe => "no-recipe",
        _ => "unreachable"
    };

    // The shortest prefix (8 characters at least) that still tells every entry apart.
    internal static string[] ShortIds(IReadOnlyList<KnownProject> projects)
    {
        for (var length = 8; ; length++)
        {
            var ids = projects.Select(p => p.ScopeId[..Math.Min(length, p.ScopeId.Length)]).ToArray();
            if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Length || Enumerable.Range(0, ids.Length).All(i => ids[i] == projects[i].ScopeId)) return ids;
        }
    }
}
