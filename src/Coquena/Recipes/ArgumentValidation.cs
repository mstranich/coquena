using System.Collections.ObjectModel;
using System.Numerics;

namespace Coquena.Recipes;

public sealed record ArgumentBackend(string? Distribution = null, string? Build = null, IReadOnlySet<string>? Capabilities = null);
public sealed record ArgumentDiagnostic(string Code, RecipeOrigin Origin, string Detail);
public sealed record ArgumentReference(string Kind, string Name, bool Canonical = false);
public sealed record ValidatedArgument(string Name, RecipeValue Value, RecipeValue? Definition, ArgumentReference? Reference, bool Literal);
public sealed record ValidatedArgumentBlock(string Scope, string Key, IReadOnlyDictionary<string, ValidatedArgument> Arguments, IReadOnlyList<ArgumentDiagnostic> Diagnostics);

public static class ArgumentValidation
{
    public static IReadOnlyList<ValidatedArgumentBlock> Validate(EffectiveRecipe recipe, ArgumentSchema? schema = null, ArgumentBackend? backend = null)
    {
        schema ??= ArgumentSchema.Load(recipe);
        var blocks = new List<ValidatedArgumentBlock>();
        if (recipe.Root.Get("service")?.Get("server") is { } server)
            blocks.Add(Block(recipe, schema, server, "server", "service", server.Get("args-mode")?.Text ?? recipe.ArgsMode, backend));
        foreach (var preset in recipe.Root.Get("presets")?.Fields ?? new Dictionary<string, RecipeValue>())
            blocks.Add(Block(recipe, schema, preset.Value, "preset", preset.Key, recipe.PresetArgsMode(preset.Key), backend));
        return blocks.AsReadOnly();
    }

    // Returns all affected blocks without mutating the existing schema or recipe.
    public static IReadOnlyList<ArgumentDiagnostic> Impact(EffectiveRecipe recipe, ArgumentSchema candidate, ArgumentBackend? backend = null)
    {
        var failures = new List<ArgumentDiagnostic>();
        void Inspect(RecipeValue value, string scope, string key, string mode)
        {
            try { failures.AddRange(Block(recipe, candidate, value, scope, key, mode, backend).Diagnostics); }
            catch (RecipeException e) { failures.Add(new(e.Code, e.Origin, e.Message)); }
        }
        if (recipe.Root.Get("service")?.Get("server") is { } server) Inspect(server, "server", "service", server.Get("args-mode")?.Text ?? recipe.ArgsMode);
        foreach (var preset in recipe.Root.Get("presets")?.Fields ?? new Dictionary<string, RecipeValue>())
            Inspect(preset.Value, "preset", preset.Key, recipe.PresetArgsMode(preset.Key));
        return failures.AsReadOnly();
    }

    public static ValidatedArgumentBlock Block(EffectiveRecipe recipe, ArgumentSchema schema, RecipeValue block, string scope, string key, string mode, ArgumentBackend? backend = null)
    {
        if (scope is not ("server" or "preset") || mode is not ("strict" or "permissive"))
            throw block.Error("invalid-validation-context", "validation", "Ámbito o modo de validación inválido.");
        var arguments = new Dictionary<string, ValidatedArgument>(StringComparer.Ordinal);
        var diagnostics = new List<ArgumentDiagnostic>();
        foreach (var pair in block.Get("args")?.Fields ?? new Dictionary<string, RecipeValue>())
        {
            if (pair.Value.Kind == RecipeKind.Null) continue;
            var name = schema.Canonical(pair.Key);
            var definition = schema.Definitions.GetValueOrDefault(name);
            if (definition is null && mode == "strict") throw pair.Value.Error("unknown-argument", "validation", "Argumento sin definición en modo estricto.");
            var literal = pair.Value.Kind == RecipeKind.Map;
            var value = literal ? pair.Value.Get("literal") ?? throw pair.Value.Error("invalid-literal", "validation", "Wrapper literal inválido.") : pair.Value;
            if (definition is not null)
            {
                if (definition.Get("scope") is { } allowed && allowed.Text != scope)
                    throw value.Error("argument-scope", "validation", "El argumento no pertenece a este ámbito.");
                if (!ArgumentSchema.Matches(definition, value)) throw value.Error("invalid-argument-value", "validation", "El valor no satisface el tipo o las restricciones del argumento.");
                Availability(definition, backend, diagnostics);
            }
            var reference = Reference(recipe, value, literal, scope == "preset");
            // Lists can contain references as well. Validate identity before resolution.
            foreach (var item in value.Items) Reference(recipe, item, false, scope == "preset");
            if (!arguments.TryAdd(name, new(name, value, definition, reference, literal)))
                throw value.Error("duplicate-argument", "validation", "Un nombre canónico y sus aliases no pueden asignarse más de una vez.");
        }
        // Rules also belong to absent arguments, making present:false meaningful.
        foreach (var definition in schema.Definitions)
        {
            if (definition.Value.Get("scope") is { } allowed && allowed.Text != scope) continue;
            var owner = arguments.GetValueOrDefault(definition.Key);
            foreach (var rule in definition.Value.Get("rules")?.Items ?? [])
            {
                foreach (var condition in (rule.Get("when") is { } conditionWhen ? new[] { conditionWhen } : [])
                    .Concat((rule.Get("requires")?.Fields.Values ?? []).Concat(rule.Get("conflicts")?.Fields.Values ?? [])))
                    if ((condition.Get("equals") ?? condition.Get("contains")) is { } operand) Reference(recipe, operand, false, scope == "preset");
                var active = rule.Get("when") is { } when ? Test(recipe, when, owner, scope == "preset") : owner is not null;
                if (!active) continue;
                foreach (var relation in new[] { "requires", "conflicts" })
                    foreach (var target in rule.Get(relation)?.Fields ?? new Dictionary<string, RecipeValue>())
                    {
                        var matches = Test(recipe, target.Value, arguments.GetValueOrDefault(target.Key), scope == "preset");
                        if (relation == "requires" ? !matches : matches)
                            throw (owner?.Value ?? block).Error("argument-" + relation, "validation", "No se satisface una relación declarativa del argumento " + definition.Key + " con " + target.Key + ".");
                    }
            }
        }
        return new(scope, key, new ReadOnlyDictionary<string, ValidatedArgument>(arguments), diagnostics.AsReadOnly());
    }

    private static bool Test(EffectiveRecipe recipe, RecipeValue condition, ValidatedArgument? argument, bool canonical)
    {
        var value = argument?.Value;
        if (condition.Get("present") is { } present) return (value is not null) == (present.Text == "true");
        if (value is null) return false;
        var operand = condition.Get("equals") ?? condition.Get("contains")!;
        if (Reference(recipe, operand, false, canonical) is not null && argument!.Literal) return false;
        if (condition.Get("equals") is { } equals) return ArgumentSchema.Equal(equals, value);
        return value.Kind == RecipeKind.Sequence && value.Items.Any(x => ArgumentSchema.Equal(x, condition.Get("contains")!));
    }

    internal static ArgumentReference? Reference(EffectiveRecipe recipe, RecipeValue value, bool literal, bool canonical = false)
    {
        if (literal || value.Kind != RecipeKind.String) return null;
        var text = value.Text!;
        var kind = text.StartsWith("model://", StringComparison.Ordinal) ? "model" : text.StartsWith("companion://", StringComparison.Ordinal) ? "companion" : null;
        if (kind is null)
        {
            // The direct selector form is reserved for preset arguments.
            if (!canonical) return null;
            try
            {
                var request = Coquena.Models.ModelRequest.Parse(text);
                return request.Quantization is null ? null : new(request.CompanionType is null ? "model" : "companion", text, true);
            }
            catch (Coquena.Models.ModelSourceException) { return null; }
        }
        var name = text[(text.IndexOf("://", StringComparison.Ordinal) + 3)..];
        if (recipe.Root.Get(kind == "model" ? "models" : "companions")?.Fields.Keys.Any(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)) != true)
            throw value.Error("unknown-resource-reference", "validation", "La referencia tipada no existe en la receta.");
        return new(kind, name);
    }

    private static void Availability(RecipeValue definition, ArgumentBackend? backend, List<ArgumentDiagnostic> diagnostics)
    {
        if (definition.Get("applies-to") is not { } applies) return;
        var unknown = false;
        var incompatible = false;
        if (applies.Get("distribution") is { } distribution)
        {
            if (backend?.Distribution is null) unknown = true;
            else if (backend.Distribution != distribution.Text) incompatible = true;
        }
        if (applies.Get("min-build") is not null || applies.Get("max-build") is not null)
        {
            // Only official bNNNN has an established comparison contract.
            if (backend?.Distribution is not ("official" or "ggml" or "ggml-org/llama.cpp") || backend.Build is null ||
                !System.Text.RegularExpressions.Regex.IsMatch(backend.Build, "^b[0-9]+$")) unknown = true;
            else
            {
                var build = BigInteger.Parse(backend.Build[1..], System.Globalization.CultureInfo.InvariantCulture);
                if (applies.Get("min-build") is { } low && build < ArgumentSchema.Build(low) || applies.Get("max-build") is { } high && build > ArgumentSchema.Build(high)) incompatible = true;
            }
        }
        foreach (var capability in applies.Get("capabilities")?.Items ?? [])
        {
            if (backend?.Capabilities is null) unknown = true;
            else if (!backend.Capabilities.Contains(capability.Text!)) incompatible = true;
        }
        if (incompatible) throw definition.Error("argument-not-applicable", "validation", "El argumento no aplica al backend seleccionado.");
        if (unknown) diagnostics.Add(new("argument-applicability-unknown", definition.Origin, "No se pudo comprobar toda la aplicabilidad declarada; no equivale a incompatibilidad."));
    }
}
