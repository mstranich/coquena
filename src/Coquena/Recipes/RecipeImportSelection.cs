using System.Collections.Frozen;

namespace Coquena.Recipes;

// This is a read-only selection, not an authorization to publish. The importer
// must still validate destination dependencies, ownership and resource protection.
public sealed record ImportEntity(string Selector);

public sealed class RecipeImportSelection
{
    public IReadOnlyList<string> Categories { get; }
    public IReadOnlyList<ImportEntity> Entities { get; }
    public IReadOnlySet<string> Replacements { get; }
    public bool AsDefault { get; }
    public bool Force { get; }

    private RecipeImportSelection(string[] categories, ImportEntity[] entities,
        HashSet<string> replacements, bool asDefault, bool force)
    {
        Categories = Array.AsReadOnly(categories);
        Entities = Array.AsReadOnly(entities);
        Replacements = replacements.ToFrozenSet(StringComparer.Ordinal);
        AsDefault = asDefault;
        Force = force;
    }

    public bool AllowsReplacement(string selector) =>
        Entities.Any(e => e.Selector == selector) && (Force || Replacements.Contains(selector));

    public static RecipeImportSelection Create(EffectiveRecipe recipe, string? select = null,
        IEnumerable<string>? replace = null, bool force = false, bool asDefault = false)
    {
        var replacements = replace?.ToArray() ?? [];
        if (force && (select is not null || replacements.Length != 0))
            throw recipe.Root.Error("import-option-conflict", "import", "Force no admite select ni replace.");
        var available = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["service"] = recipe.Root.Get("service") is not null,
            ["presets"] = recipe.Root.Get("presets") is not null,
            ["model"] = recipe.Root.Get("models") is not null || recipe.Root.Get("companions") is not null,
            ["params"] = recipe.Root.Get("arg-defs") is not null
        };
        var categories = select is null ? available.Where(p => p.Value).Select(p => p.Key).ToArray() : select.Split(',');
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var category in categories)
        {
            if (!available.TryGetValue(category, out var present) || !seen.Add(category))
                throw recipe.Root.Error("invalid-import-selection", "import", "Categorías inválidas, vacías o repetidas.");
            if (!present)
                throw recipe.Root.Error("absent-import-category", "import", "La categoría seleccionada no está declarada.");
        }
        if (asDefault && !seen.Contains("service"))
            throw recipe.Root.Error("import-default-requires-service", "import", "As-default requiere seleccionar service.");
        if (recipe.Target == "self" && !seen.Contains("service"))
            throw recipe.Root.Error("import-self-requires-service", "import", "Target self requiere importar service.");

        var entities = new List<ImportEntity>();
        if (seen.Contains("service")) Add("service:" + recipe.Name);
        if (seen.Contains("presets")) Group("presets", "preset");
        if (seen.Contains("model")) { Group("models", "model"); Group("companions", "companion"); }
        // Enumerate schema keys, including JSON imports, without placing expanded
        // values in the selection. Materialization must preserve source expressions.
        if (seen.Contains("params"))
        {
            foreach (var key in ArgumentSchema.Load(recipe).Definitions.Keys)
                Add("param:" + key);
        }
        var selectors = entities.Select(e => e.Selector).ToHashSet(StringComparer.Ordinal);
        var authorized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var replacement in replacements)
            if (!selectors.Contains(replacement) || !authorized.Add(replacement))
                throw recipe.Root.Error("invalid-import-replacement", "import", "Replace requiere una entidad seleccionada y no repetida.");
        return new(categories, entities.ToArray(), authorized, asDefault, force);

        void Add(string selector) => entities.Add(new(selector));
        void Group(string group, string kind)
        {
            if (recipe.Root.Get(group) is { } collection)
                foreach (var key in collection.Fields.Keys) Add(kind + ":" + key);
        }
    }
}
