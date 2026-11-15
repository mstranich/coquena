using System.Text.Json.Nodes;

namespace Coquena.Projects;

// Interface-independent filters. Scope selection is separate from reference discovery.
public sealed record CleanupSelection(IReadOnlyList<string> Kinds, IReadOnlyList<ResourceSelector> Resources, bool Downloads = false, bool Installations = true)
{
    public static CleanupSelection All { get; } = new([], []);
    public bool IsUnfiltered => Installations && !Downloads && Kinds.Count == 0 && Resources.Count == 0;
    public bool IncludeInstallations => Installations && (!Downloads || Kinds.Count > 0 || Resources.Count > 0);
    public bool Matches(string kind, string key, JsonNode? resource, JsonNode? installation, ScopeSnapshot state)
    {
        if (Kinds.Count > 0 && !Kinds.Contains(kind)) return false;
        if (Resources.Count == 0) return true;
        var aliases = state.Documents.GetValueOrDefault("local/inventory.json")?["data"]?["model-aliases"] as JsonArray;
        return Resources.Any(selector => selector.Matches(kind, key, resource, installation,
                alias: installation?["intent"]?["name"]?.ToString(), projectName: installation?["project-name"]?.ToString()) ||
            (aliases ?? []).Any(alias => alias?["installation-id"]?.ToString() == installation?["installation-id"]?.ToString() &&
                selector.Matches(kind, alias?["name"]?.ToString() ?? alias?["key"]?.ToString() ?? "", resource, installation,
                    alias: alias?["name"]?.ToString(), projectName: alias?["project-name"]?.ToString())) ||
            kind == "backend" && BackendAliases(state).Any(alias => alias.InstallationId == installation?["installation-id"]?.ToString() &&
                selector.Matches(kind, key, resource, installation, alias: alias.Alias, projectName: alias.Project)));
    }

    internal void Validate(ScopeSnapshot state)
    {
        var aliases = BackendAliases(state).ToArray();
        foreach (var selector in Resources.Where(s => s.Kind == "backend"))
            selector.CheckAmbiguity(aliases.Where(a => selector.Name == a.Alias || selector.Name == a.Project + "/" + a.Alias).Select(a => a.Project));
    }

    private static IEnumerable<(string? InstallationId, string Alias, string Project)> BackendAliases(ScopeSnapshot state)
    {
        foreach (var recipe in state.Documents.GetValueOrDefault("local/recipes.json")?["data"]?["recipes"]?.AsArray() ?? [])
            foreach (var entity in recipe!["entities"]?.AsArray() ?? [])
                foreach (var intent in entity?["backend-intents"]?.AsArray() ?? [])
                    yield return (intent?["installation-id"]?.ToString(), intent?["intent"]?["name"]?.ToString() ?? recipe["name"]!.ToString(), recipe["name"]!.ToString());
    }
}
