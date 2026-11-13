using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Coquena.Recipes;

// A recipe that failed validation, with every problem found; Detail lists them as text.
internal sealed class RecipeProblemsException(IReadOnlyList<RecipeDiagnostic> problems) : RecipeException(problems[0].Code, "validate",
    new(problems[0].File, problems[0].Line ?? 0, problems[0].Column ?? 0, "$"), RecipeValidation.Describe(problems))
{
    public IReadOnlyList<RecipeDiagnostic> Problems { get; } = problems;

    public JsonObject ToJson() => new()
    {
        ["valid"] = false,
        ["problems"] = new JsonArray(Problems.Select(problem => (JsonNode)new JsonObject
        {
            ["file"] = problem.File, ["line"] = problem.Line, ["column"] = problem.Column,
            ["code"] = problem.Code, ["message"] = problem.Message
        }).ToArray())
    };
}

internal sealed record RecipeDiagnostic(string File, int? Line, int? Column, string Code, string Message);

// The single validation of a recipe, shared by Validar in the Editor, the precondition
// of authoring actions and `verify`. It reads what the user sees: drafts first, then
// the saved files.
internal static partial class RecipeValidation
{
    internal const int Limit = 50;

    internal sealed record Result(EffectiveRecipe? Recipe, IReadOnlyList<ValidatedArgumentBlock> Blocks, IReadOnlyList<RecipeDiagnostic> Problems)
    {
        public bool Valid => Problems.Count == 0;
    }

    // Reports as many problems as the files allow. After a problem located in an entry,
    // that entry is set aside and validation runs again over the rest. A YAML syntax
    // error stops its file: the parser cannot read past it.
    internal static Result Check(RecipeSelection selection, IReadOnlyDictionary<string, byte[]>? drafts = null,
        Func<RecipeSelection, (EffectiveRecipe, IReadOnlyList<ValidatedArgumentBlock>)>? stage = null)
    {
        stage ??= Single;
        var captured = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<RecipeDiagnostic>();
        try
        {
            foreach (var file in selection.Files.Concat(selection.EnvFiles))
                captured[file] = drafts?.TryGetValue(file, out var draft) == true ? draft! : selection.CapturedFiles?.GetValueOrDefault(file) ?? RecipeYaml.ReadBytes(file);
        }
        catch (RecipeException error) { return new(null, [], [Diagnostic(error, captured)]); }

        // Syntax first, file by file, so every broken file is reported.
        var broken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in selection.Files)
        {
            try { _ = new RecipeYaml(file, captured[file]).Read(); }
            catch (RecipeException error)
            {
                // The parser stops here: nothing after this point of the file was checked.
                var problem = Diagnostic(error, captured);
                problems.Add(error.Code == "yaml-syntax" ? problem with { Message = problem.Message + Messages.RestNotValidated } : problem);
                broken.Add(file);
            }
        }
        if (broken.Count != 0) return new(null, [], problems);

        // Entries set aside that others may reference, with the problem that removed them and
        // how many references to them were left out as consequences of that problem.
        var removed = new Dictionary<string, (int Problem, int References)>(StringComparer.Ordinal);
        Result Done(EffectiveRecipe? recipe = null, IReadOnlyList<ValidatedArgumentBlock>? blocks = null)
        {
            foreach (var (problem, references) in removed.Values.Where(entry => entry.References > 0))
                problems[problem] = problems[problem] with { Message = problems[problem].Message +
                    Messages.ReferencesToEntry(references) };
            return problems.Count == 0 ? new(recipe, blocks ?? [], problems) : new(null, [], problems);
        }

        for (var round = 0; round < Limit; round++)
        {
            try
            {
                var (recipe, blocks) = stage(Candidate(selection, captured));
                return Done(recipe, blocks);
            }
            catch (Problems many)
            {
                problems.AddRange(many.List.Where(problem => !problems.Contains(problem)));
                return Done();
            }
            catch (Exception error) when (error is RecipeException or ArgumentException or InvalidOperationException or FormatException)
            {
                var problem = error is RecipeException recipeError ? Diagnostic(recipeError, captured)
                    : new RecipeDiagnostic(selection.Files[0], null, null, "invalid-recipe", error.Message);
                if (problems.Contains(problem)) return Done();
                if (error is not RecipeException { Origin: var origin } || !captured.ContainsKey(origin.File) || Keys(origin.Path) is not { Length: > 0 } keys)
                {
                    problems.Add(problem);
                    return Done();
                }
                // A reference to an entry already set aside is a consequence, not a new problem.
                var target = Referenced((RecipeException)error, keys, captured[origin.File]);
                var consequence = target is not null && removed.ContainsKey(target);
                if (consequence) removed[target!] = removed[target!] with { References = removed[target!].References + 1 };
                else problems.Add(problem);
                if (RecipeEdit.Prune(origin.File, captured[origin.File], keys) is not { } pruned) return Done();
                if (!consequence && Entity(keys) is { } entity && !Present(origin.File, pruned, entity.Keys))
                    removed.TryAdd(entity.Reference, (problems.Count - 1, 0));
                captured[origin.File] = pruned;
            }
        }
        return Done();
    }

    // One pass of the recipe checks, stopping at the first problem.
    internal static (EffectiveRecipe Recipe, IReadOnlyList<ValidatedArgumentBlock> Blocks) Single(RecipeSelection selection)
    {
        var recipe = RecipeReader.Read(selection);
        Intentions(recipe);
        var blocks = ArgumentValidation.Validate(recipe);
        var adapter = new LlamaB11482ArgumentAdapter();
        foreach (var block in blocks)
        {
            if (block.Scope == "preset" && recipe.PresetEnabled(block.Key))
                _ = ArgumentEmission.Ini(recipe, block, block.Key, adapter, _ => "prepared-resource");
            else if (block.Scope == "server")
                _ = ArgumentEmission.Cli(recipe, block,
                    Backends.BackendIntent.FromRecipe(recipe).Executable is null ? adapter : null,
                    _ => "prepared-resource");
        }
        return (recipe, blocks);
    }

    // Backend and model intentions the recipe declares.
    internal static void Intentions(EffectiveRecipe recipe)
    {
        if (recipe.Root.Get("service")?.Get("backend") is not null) _ = Backends.BackendIntent.FromRecipe(recipe);
        _ = Models.ModelIntent.FromRecipe(recipe);
    }

    // Checks of a point edit: structure and intentions, then the impact of the arguments.
    internal static (EffectiveRecipe, IReadOnlyList<ValidatedArgumentBlock>) Edit(RecipeSelection selection)
    {
        var recipe = RecipeReader.Read(selection);
        Intentions(recipe);
        var impact = ArgumentValidation.Impact(recipe, ArgumentSchema.Load(recipe));
        if (impact.Count != 0)
            throw new Problems(impact.Select(d => Diagnostic(new RecipeException(d.Code, "validation", d.Origin, d.Detail), selection.CapturedFiles!)).ToArray());
        return (recipe, []);
    }

    // Several problems found in one pass; nothing to set aside.
    private sealed class Problems(IReadOnlyList<RecipeDiagnostic> list) : Exception
    {
        public IReadOnlyList<RecipeDiagnostic> List { get; } = list;
    }

    // What to print for a recipe error: the list for an aggregated validation, the message otherwise.
    internal static string Message(RecipeException error) =>
        error.Phase == "validate" ? "Problemas en la receta:" + Environment.NewLine + error.Detail : error.Message;

    // Throws with every problem, for callers that only proceed with a valid recipe.
    internal static (EffectiveRecipe Recipe, IReadOnlyList<ValidatedArgumentBlock> Blocks) Require(RecipeSelection selection, IReadOnlyDictionary<string, byte[]>? drafts = null,
        Func<RecipeSelection, (EffectiveRecipe, IReadOnlyList<ValidatedArgumentBlock>)>? stage = null)
    {
        var result = Check(selection, drafts, stage);
        if (result.Valid) return (result.Recipe!, result.Blocks);
        var first = result.Problems[0];
        throw new RecipeProblemsException(result.Problems);
    }

    internal static string Describe(IEnumerable<RecipeDiagnostic> problems) => string.Join(Environment.NewLine, problems.Select(problem =>
        Path.GetFileName(problem.File) + (problem.Line is { } line ? ":" + line + (problem.Column is { } column ? ":" + column : "") : "") + " · " + problem.Message));

    private static RecipeSelection Candidate(RecipeSelection selection, Dictionary<string, byte[]> captured) =>
        new(selection.Base, selection.Files, selection.Base, selection.EnvFiles)
        {
            CapturedFiles = new Dictionary<string, byte[]>(captured, StringComparer.OrdinalIgnoreCase), ImportedScope = selection.ImportedScope,
            ImportedPathBases = selection.ImportedPathBases, FilterUnusedModels = selection.FilterUnusedModels,
            ImportCategories = selection.ImportCategories
        };

    private static RecipeDiagnostic Diagnostic(RecipeException error, IReadOnlyDictionary<string, byte[]> captured)
    {
        // A position is shown only when it points into one of the selected files.
        var located = error.Origin.Line > 0 && captured.ContainsKey(error.Origin.File);
        // Reader paths ($["a"]["b"]) read as a.b; other paths are shown as they come.
        var place = Keys(error.Origin.Path) is { Length: > 0 } keys ? string.Join(".", keys) + ": "
            : error.Origin.Path is { Length: > 0 } path && path != "$" ? path.TrimStart('$', '.') + ": " : "";
        return new(error.Origin.File, located ? error.Origin.Line : null, located ? error.Origin.Column : null, error.Code, place + error.Detail);
    }

    // The entry a problem refers to by name: a typed reference (model://, companion://) or an
    // argument without definition. Null for any other problem.
    private static string? Referenced(RecipeException error, string[] keys, byte[] bytes)
    {
        if (error.Code is "unknown-argument" or "unknown-rule-argument") return "arg:" + keys[^1];
        if (error.Code != "unknown-resource-reference") return null;
        try
        {
            RecipeValue? node = new RecipeYaml(error.Origin.File, bytes).Read();
            foreach (var key in keys) node = node?.Kind == RecipeKind.Map ? node.Get(key) : null;
            return node?.Kind == RecipeKind.String ? node.Text : null;
        }
        catch (RecipeException) { return null; }
    }

    // The referenceable entry that a path belongs to, if any.
    private static (string Reference, string[] Keys)? Entity(string[] keys) => keys switch
    {
        ["models", var name, ..] => ("model://" + name, keys[..2]),
        ["companions", var name, ..] => ("companion://" + name, keys[..2]),
        ["arg-defs", "definitions", var name, ..] => ("arg:" + name, keys[..3]),
        _ => null
    };

    private static bool Present(string file, byte[] bytes, string[] keys)
    {
        RecipeValue? node = new RecipeYaml(file, bytes).Read();
        foreach (var key in keys) node = node?.Kind == RecipeKind.Map ? node.Get(key) : null;
        return node is not null;
    }

    // Map keys of an origin path, up to the first sequence index.
    private static string[]? Keys(string path)
    {
        if (!path.StartsWith('$')) return null;
        var keys = new List<string>();
        foreach (Match segment in Segment().Matches(path))
        {
            if (!segment.Groups[1].Success) break;
            keys.Add(JsonSerializer.Deserialize(segment.Value[1..^1], RecipeJsonContext.Default.String)!);
        }
        return keys.ToArray();
    }

    [GeneratedRegex("""\[("(?:[^"\\]|\\.)*")\]|\[\d+\]""")]
    private static partial Regex Segment();
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class RecipeJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
