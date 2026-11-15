using Coquena.Projects;
using Coquena.Storage;

namespace Coquena;

internal sealed record MaintenanceOptions(string Verb, bool Global, IReadOnlyList<string> Files,
    IReadOnlyList<string> Projects, CleanupSelection Selection, bool DryRun, bool Yes)
{
    internal static MaintenanceOptions Parse(string[] args)
    {
        var files = new List<string>(); var projects = new List<string>(); var kinds = new List<string>();
        var resources = new List<ResourceSelector>(); var flags = new HashSet<string>(); string? verb = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i] switch { "-f" => "--file", "-g" => "--global", _ => args[i] };
            if (arg is "usage" or "df" or "clean" or "prune")
            { if (verb is not null) throw new ArgumentException(); verb = arg; continue; }
            if (arg is "--file" or "--project" or "--select")
            {
                if (++i == args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith('-')) throw new ArgumentException();
                if (arg == "--file") files.Add(args[i]);
                else if (arg == "--project") projects.Add(args[i]);
                else resources.Add(ResourceSelector.Parse(args[i]));
            }
            else if (arg is "--global" or "--model" or "--companion" or "--backend" or "--downloads" or "--dry-run" or "--yes")
            {
                if (!flags.Add(arg)) throw new ArgumentException();
                if (arg is "--model" or "--companion" or "--backend") kinds.Add(arg[2..]);
            }
            else throw new ArgumentException();
        }
        if (verb is null || flags.Contains("--dry-run") && flags.Contains("--yes") ||
            verb is "usage" or "df" && flags.Any(f => f is "--yes" or "--dry-run")) throw new ArgumentException();
        if (kinds.Count > 0 && resources.Any(r => !kinds.Contains(r.Kind))) throw new ArgumentException(Messages.CleanupTypeMismatch);
        return new(verb, flags.Contains("--global"), files, projects,
            kinds.Count == 0 && resources.Count == 0 && !flags.Contains("--downloads") ? CleanupSelection.All : new(kinds, resources, flags.Contains("--downloads")),
            flags.Contains("--dry-run"), flags.Contains("--yes"));
    }

    internal bool ExplicitScopes => Global || Files.Count > 0 || Projects.Count > 0;
    internal string FilterArguments => string.Concat(Selection.Kinds.Select(k => " --" + k)) +
        string.Concat(Selection.Resources.Select(r => " --select '" + r.ToString().Replace("'", "''") + "'")) +
        (Selection.Downloads ? " --downloads" : "");
    internal ScopeContext[] Scopes(Home home, bool currentByDefault)
    {
        var scopes = new List<ScopeContext>();
        if (Global && Directory.Exists(home.Root)) scopes.Add(ScopeContext.At(home.Root, Path.Combine(home.Root, "global"), true));
        foreach (var file in Files) scopes.Add(ScopeContext.Project(file));
        if (Projects.Count > 0)
        {
            var known = new ProjectIndex(home).List();
            foreach (var prefix in Projects)
            {
                var matches = known.Where(p => p.ScopeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) throw new StorageException(matches.Length == 0 ? "unknown-project" : "ambiguous-project", Messages.ProjectUniqueIdRequired);
                if (matches[0].State == KnownProjectState.Unreachable) throw new StorageException("project-unreachable", Messages.ProjectUnreachableForCleanup);
                scopes.Add(ScopeContext.Project(Path.Combine(matches[0].Base, "coquena.yaml")));
            }
        }
        if (!ExplicitScopes && currentByDefault) scopes.Add(ScopeContext.Project(Path.Combine(Environment.CurrentDirectory, "coquena.yaml")));
        return scopes.DistinctBy(s => (s.PhysicalBase, s.IsGlobal)).ToArray();
    }
}
