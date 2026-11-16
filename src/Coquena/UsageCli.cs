using Coquena.Projects;
using Coquena.Storage;

namespace Coquena;

// usage (alias df): every file Coquena knows, by scope, and what prune may free.
public static class UsageCli
{
    private static string Help => Messages.UsageHelp;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Any(a => a is "--help" or "-h")) { output.WriteLine(Help); return 0; }
        MaintenanceOptions options;
        try { options = MaintenanceOptions.Parse(args); }
        catch (ArgumentException) { error.WriteLine(Messages.UsageInvalidOptions); return 2; }
        try
        {
            var home = Home.Resolve();
            var scopes = options.Scopes(home, false);
            var recipe = Path.Combine(Environment.CurrentDirectory, "coquena.yaml");
            var project = scopes.FirstOrDefault(s => !s.IsGlobal) ?? (!options.ExplicitScopes &&
                (File.Exists(recipe) || Directory.Exists(Path.Combine(Environment.CurrentDirectory, ".coquena"))) ? ScopeContext.Project(recipe) : null);
            // Without a project, survey from the global scope; neither path creates folders.
            var anchor = scopes.FirstOrDefault() ?? project ?? (Directory.Exists(home.Root) ? ScopeContext.At(home.Root, Path.Combine(home.Root, "global"), true) : null);
            var survey = anchor is null ? new UsageSurvey([], []) : new ResourceCleanup(anchor, home).Survey(options.Selection);
            if (options.ExplicitScopes) survey = survey with { Scopes = survey.Scopes.Where(s => scopes.Any(c => Same(c, s.Scope))).ToArray() };
            if (options.ExplicitScopes)
            {
                foreach (var scope in survey.Scopes) Section(scope.Scope.IsGlobal ? Messages.UsageGlobal : Messages.UsageProject, scope,
                    (scope.Scope.IsGlobal ? "coquena clean --global" : $"coquena clean -f \"{Path.Combine(scope.Scope.Base, "coquena.yaml")}\"") + options.FilterArguments, output);
                if (survey.Scopes.Count == 0) output.WriteLine(Messages.UsageEmptySelection);
                WriteSkipped(survey, output);
            }
            else Write(survey, project, output, options.FilterArguments);
            return 0;
        }
        catch (Exception e) when (e is StorageException or IOException or UnauthorizedAccessException or ArgumentException or Coquena.Downloads.DownloadException)
        {
            error.WriteLine(Messages.UsageFailed(e.Message));
            return 2;
        }
    }

    internal static void Write(UsageSurvey survey, ScopeContext? project, TextWriter output, string filters = "")
    {
        var own = project is null ? null : survey.Scopes.FirstOrDefault(s => Same(s.Scope, project));
        var global = survey.Scopes.FirstOrDefault(s => s.Scope.IsGlobal);
        if (project is null) output.WriteLine(Messages.UsageNoProject);
        else if (own is null) output.WriteLine(Messages.UsageProjectEmpty);
        else Section(Messages.UsageHere, own, "coquena clean" + filters, output);
        output.WriteLine();
        if (global is null) output.WriteLine(Messages.UsageGlobalEmpty);
        else Section(Messages.UsageGlobal, global, "coquena clean --global" + filters, output);
        foreach (var other in survey.Scopes.Where(s => s != own && s != global))
        {
            output.WriteLine();
            Section(Messages.UsageOtherProject, other, $"coquena clean -f \"{Path.Combine(other.Scope.Base, "coquena.yaml")}\"" + filters, output);
        }
        WriteSkipped(survey, output);
    }

    private static void WriteSkipped(UsageSurvey survey, TextWriter output)
    {
        if (survey.Skipped.Count > 0)
        {
            output.WriteLine();
            output.WriteLine(Messages.UsageUnavailableSummary(survey.Skipped.Distinct(StringComparer.OrdinalIgnoreCase).Count()));
            foreach (var path in survey.Skipped.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                output.WriteLine("  " + path);
            }
            output.WriteLine(Messages.UsageUnavailableHint);
        }
    }

    private static bool Same(ScopeContext a, ScopeContext b) =>
        string.Equals(a.Root, b.Root, StringComparison.OrdinalIgnoreCase);

    private static void Section(string title, ScopeUsage usage, string prune, TextWriter output)
    {
        output.WriteLine(Messages.UsageHeading(title, usage.Total, usage.Scope.Base));
        foreach (var (kind, label) in new[] { ("model", Messages.MaintenanceModels), ("companion", Messages.MaintenanceCompanions), ("backend", Messages.MaintenanceBackends) })
        {
            var items = usage.Resources.Where(r => r.Kind == kind).ToArray();
            if (items.Length == 0) continue;
            var free = items.Where(r => r.Status == UsageStatus.Free).Sum(r => r.Bytes);
            output.WriteLine(Messages.UsageResourceRow(label, items.Sum(r => r.Bytes), free));
        }
        if (usage.Downloads > 0) output.WriteLine(Messages.UsageDownloadsRow(usage.Downloads));
        foreach (var download in usage.CleanupDownloads ?? []) output.WriteLine(Messages.UsageDownloadRow(download.Name, download.Bytes, download.Id, false));
        if (usage.Logs > 0) output.WriteLine(Messages.UsageLogsRow(usage.Logs));
        var repeatedNames = usage.Resources.GroupBy(r => r.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        string Label(UsageResource item) => repeatedNames.Contains(item.Name) && item.InstallationId is { } id
            ? Messages.UsageInstallationLabel(item.Name, id) : item.Name;
        if (usage.Resources.Any(r => r.Status == UsageStatus.Free)) output.WriteLine(Messages.UsageRemovableResources);
        foreach (var item in usage.Resources.Where(r => r.Status == UsageStatus.Free))
            output.WriteLine(Messages.UsageUnusedRow(Label(item), item.Bytes));
        var kept = usage.Resources.Where(r => r.Status == UsageStatus.InUse).ToArray();
        if (kept.Length > 0) output.WriteLine(Messages.UsageKeptHeading);
        foreach (var item in kept)
            output.WriteLine(Messages.UsageKeptRow(Label(item), (item.References ?? []).Select(reference =>
                Messages.UsageShortReference(reference, Same(usage.Scope, reference.Scope))).Distinct()));
        var external = usage.Resources.Where(r => r.Status == UsageStatus.External).Sum(r => r.Bytes);
        if (external > 0) output.WriteLine(Messages.UsageExternal(external));
        output.WriteLine(usage.Free > 0 ? Messages.UsageReclaimable(usage.Free, prune) : Messages.UsageNothingToClean);
    }

    private static bool Same(ScopeContext scope, string folder) =>
        string.Equals(scope.Base, folder, StringComparison.OrdinalIgnoreCase);
}
