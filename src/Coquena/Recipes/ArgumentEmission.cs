using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Coquena.Configuration;

namespace Coquena.Recipes;

public sealed record ArgumentEmitRule(string Name, string Mode, string? Separator = null, string? TrueValue = null, string? FalseValue = null, string? NegativeName = null);

// Parser evidence is supplied by a concrete adapter; schema alone is insufficient.
public interface IArgumentAdapter
{
    IArgumentIniCodec? IniCodec { get; }
    ArgumentEmitRule? Rule(string name, string scope, string destination);
    bool Supports(string name, string scope, string destination);
    bool Accumulates(string name, string destination);
    bool SupportsLineBreaks(string name, string destination);
}
public interface IArgumentIniCodec
{
    string Section(string name, RecipeOrigin origin);
    string Entry(string key, string value, RecipeOrigin origin);
}

// Reuses the bounded b11482 codec/evidence from 010; no claim for another build.
public sealed class LlamaB11482ArgumentAdapter : IArgumentAdapter, IArgumentIniCodec
{
    public IArgumentIniCodec IniCodec => this;
    public bool Supports(string name, string scope, string destination) => destination == "cli"
        ? name is not ("load-on-startup" or "stop-timeout") : ParameterCatalog.Find(name)?.Destination == "INI";
    public bool Accumulates(string name, string destination) => false;
    public bool SupportsLineBreaks(string name, string destination) => false;
    public ArgumentEmitRule? Rule(string name, string scope, string destination)
    {
        var definition = ParameterCatalog.Find(name);
        if (definition is null) return null;
        var emitted = definition.Name;
        return definition.Type == "boolean"
            ? new(emitted, destination == "cli" ? "flag" : "value", TrueValue: "true", FalseValue: "false")
            : new(emitted, "value");
    }
    public string Section(string name, RecipeOrigin origin)
    {
        ArgumentSchema.SafeIniName(name, new(RecipeKind.String, origin, name));
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase) || name.Contains(':'))
            throw new RecipeException("reserved-ini-section", "emission", origin, "Sección reservada o normalizada por el router.");
        return "[" + name + "]\n";
    }
    public string Entry(string key, string value, RecipeOrigin origin)
    {
        ArgumentSchema.SafeIniName(key, new(RecipeKind.String, origin, key));
        try { ParameterCatalog.Literal(value); }
        catch (ConfigurationException) { throw new RecipeException("unrepresentable-ini-value", "emission", origin, "El codec INI b11482 no representa el valor sin pérdida."); }
        return key + "=" + value + "\n";
    }
}

public sealed record ArgumentProcessPlan(IReadOnlyList<string> Cli, string Ini)
{
    public ProcessStartInfo StartInfo(string executable)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var token in Cli) start.ArgumentList.Add(token);
        return start;
    }
}

public static class ArgumentEmission
{
    public static IReadOnlyList<string> Cli(EffectiveRecipe recipe, ValidatedArgumentBlock block, IArgumentAdapter? adapter = null,
        Func<ArgumentReference, string>? resolve = null)
    {
        var tokens = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var argument in block.Arguments.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var rule = Rule(argument, block.Scope, "cli", adapter);
            if (!names.Add(rule.Name) || rule.NegativeName is not null && !names.Add(rule.NegativeName))
                throw argument.Value.Error("emission-key-collision", "emission", "Varios argumentos emiten el mismo flag CLI.");
            tokens.AddRange(CliArgument(recipe, block, argument, adapter, resolve));
        }
        return tokens.AsReadOnly();
    }

    public static string Ini(EffectiveRecipe recipe, ValidatedArgumentBlock block, string section, IArgumentAdapter adapter,
        Func<ArgumentReference, string>? resolve = null)
    {
        var codec = adapter.IniCodec ?? throw recipe.Root.Error("ini-codec-required", "emission", "Falta codec acreditado para el build.");
        if (block.Scope == "preset" && section is "*" or "default")
            throw recipe.Root.Error("reserved-ini-section", "emission", "Un preset no puede usar una sección reservada por el router.");
        var result = new StringBuilder(codec.Section(section, recipe.Root.Origin));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var argument in block.Arguments.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var rule = Rule(argument, block.Scope, "ini", adapter);
            if (!keys.Add(rule.Name)) throw argument.Value.Error("emission-key-collision", "emission", "Varios argumentos emiten la misma clave INI.");
            foreach (var value in Values(recipe, argument, rule, "ini", adapter, resolve, block.Scope == "preset"))
                result.Append(codec.Entry(rule.Name, value, argument.Value.Origin));
        }
        return result.ToString();
    }

    // Presets that cannot use INI must agree on every process-wide CLI value.
    // Absence is compared too: a global CLI override cannot be isolated to one preset.
    public static ArgumentProcessPlan Compose(EffectiveRecipe recipe, IReadOnlyList<ValidatedArgumentBlock> blocks,
        IArgumentAdapter adapter, Func<ArgumentReference, string>? resolve = null)
    {
        var server = blocks.SingleOrDefault(x => x.Scope == "server");
        bool ServerIni(ValidatedArgument argument) => argument.Definition?.Get("emit") is { } emit && emit.Get("cli") is null && emit.Get("ini") is not null
            || !adapter.Supports(argument.Name, "server", "cli");
        var processCli = server is null ? null : server with { Arguments = server.Arguments.Where(x => !ServerIni(x.Value)).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal) };
        var tokens = processCli is null ? new List<string>() : Cli(recipe, processCli, adapter, resolve).ToList();
        var presets = blocks.Where(x => x.Scope == "preset" && recipe.PresetEnabled(x.Key)).ToArray();
        var ini = new StringBuilder();
        if (server is not null)
        {
            var processIni = server with { Arguments = server.Arguments.Where(x => ServerIni(x.Value)).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal) };
            if (processIni.Arguments.Count > 0) ini.Append(Ini(recipe, processIni, "*", adapter, resolve));
        }
        foreach (var preset in presets)
            foreach (var global in processCli?.Arguments.Values ?? [])
                if (preset.Arguments.TryGetValue(global.Name, out var local) &&
                    (!ArgumentSchema.Equal(global.Value, local.Value) || global.Literal != local.Literal))
                    throw local.Value.Error("server-preset-cli-conflict", "emission", "Un valor CLI del proceso anularía una asignación distinta del preset.");
        var cliNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var argument in processCli?.Arguments.Values ?? [])
        {
            var rule = Rule(argument, "server", "cli", adapter);
            cliNames.Add(rule.Name);
            if (rule.NegativeName is not null) cliNames.Add(rule.NegativeName);
        }
        var cliOnly = presets.SelectMany(x => x.Arguments.Keys).Distinct(StringComparer.Ordinal)
            .Where(name => !adapter.Supports(name, "preset", "ini")).Order(StringComparer.Ordinal).ToArray();
        foreach (var name in cliOnly)
        {
            string[]? common = null;
            foreach (var preset in presets)
            {
                var emitted = preset.Arguments.TryGetValue(name, out var argument)
                    ? CliArgument(recipe, preset, argument, adapter, resolve).ToArray() : [];
                if (common is not null && !common.SequenceEqual(emitted))
                    throw recipe.Root.Error("incompatible-preset-cli", "emission", "Un argumento CLI no puede representar asignaciones distintas por preset: " + name + ".");
                common = emitted;
            }
            if (processCli?.Arguments.ContainsKey(name) == true) continue;
            var representative = presets.Select(x => x.Arguments.GetValueOrDefault(name)).First(x => x is not null)!;
            var rule = Rule(representative, "preset", "cli", adapter);
            if (!cliNames.Add(rule.Name) || rule.NegativeName is not null && !cliNames.Add(rule.NegativeName))
                throw representative.Value.Error("emission-key-collision", "emission", "Varios argumentos emiten el mismo flag del proceso.");
            tokens.AddRange(common ?? []);
        }
        foreach (var preset in presets)
        {
            var local = preset with { Arguments = preset.Arguments.Where(x => !cliOnly.Contains(x.Key, StringComparer.Ordinal)).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal) };
            ini.Append(Ini(recipe, local, preset.Key, adapter, resolve));
        }
        return new(tokens.AsReadOnly(), ini.ToString());
    }

    private static IEnumerable<string> CliArgument(EffectiveRecipe recipe, ValidatedArgumentBlock block, ValidatedArgument argument,
        IArgumentAdapter? adapter, Func<ArgumentReference, string>? resolve)
    {
        var rule = Rule(argument, block.Scope, "cli", adapter);
        if (rule.Mode == "flag")
        {
            if (argument.Value.Kind != RecipeKind.Boolean) throw argument.Value.Error("invalid-flag", "emission", "Flag requiere boolean.");
            if (argument.Value.Text == "true") return ["--" + rule.Name];
            return rule.NegativeName is null ? [] : ["--" + rule.NegativeName];
        }
        return Values(recipe, argument, rule, "cli", adapter, resolve, block.Scope == "preset").SelectMany(value => new[] { "--" + rule.Name, value });
    }

    private static ArgumentEmitRule Rule(ValidatedArgument argument, string scope, string destination, IArgumentAdapter? adapter)
    {
        if (adapter is not null && !adapter.Supports(argument.Name, scope, destination))
            throw argument.Value.Error("unsupported-argument-destination", "emission", "El adaptador no admite este destino para el argumento.");
        ArgumentEmitRule rule;
        if (argument.Definition?.Get("emit")?.Get(destination) is { } emit)
            rule = new(emit.Get("name")?.Text ?? argument.Name, emit.Get("mode")!.Text!, emit.Get("separator")?.Text,
                emit.Get("true-value")?.Text, emit.Get("false-value")?.Text, emit.Get("negative-name")?.Text);
        else rule = adapter?.Rule(argument.Name, scope, destination) ?? new(argument.Name, argument.Value.Kind == RecipeKind.Boolean ? "flag" : "value");
        if (destination == "cli") ArgumentSchema.Name(rule.Name, argument.Value);
        else ArgumentSchema.SafeIniName(rule.Name, argument.Value);
        if (rule.NegativeName is { } negative) ArgumentSchema.Name(negative, argument.Value);
        return rule;
    }

    private static IReadOnlyList<string> Values(EffectiveRecipe recipe, ValidatedArgument argument, ArgumentEmitRule rule, string destination,
        IArgumentAdapter? adapter, Func<ArgumentReference, string>? resolve, bool canonical)
    {
        var value = argument.Value;
        string Scalar(RecipeValue item)
        {
            var reference = ArgumentValidation.Reference(recipe, item, argument.Literal, canonical);
            var text = reference is not null
                ? resolve?.Invoke(reference) ?? throw item.Error("unresolved-resource-reference", "emission", "La referencia necesita una ruta de entrada resuelta.")
                : item.Kind switch
                {
                    RecipeKind.String => item.Text!,
                    RecipeKind.Integer => BigInteger.Parse(item.Text!, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                    RecipeKind.Number => Normalize(item.Text!),
                    RecipeKind.Boolean when rule.Mode == "value" => item.Text == "true" ? rule.TrueValue ?? throw item.Error("boolean-representation-required", "emission", "Boolean con valor requiere representación explícita.")
                        : rule.FalseValue ?? throw item.Error("boolean-representation-required", "emission", "Boolean con valor requiere representación explícita."),
                    _ => throw item.Error("unrepresentable-value", "emission", "El valor no tiene emisión escalar declarada.")
                };
            CheckText(text, item, argument.Name, destination, adapter);
            return text;
        }
        if (rule.Mode == "flag")
        {
            if (value.Kind != RecipeKind.Boolean) throw value.Error("invalid-flag", "emission", "Flag requiere boolean.");
            return value.Text == "true" ? ["true"] : [];
        }
        if (rule.Mode == "value")
        {
            if (value.Kind == RecipeKind.Sequence) throw value.Error("array-emission-required", "emission", "Una lista requiere repeat o join explícitos.");
            return [Scalar(value)];
        }
        if (value.Kind != RecipeKind.Sequence) throw value.Error("array-required", "emission", "Repeat/join requiere lista.");
        if (value.Items.Count == 0) return [];
        if (rule.Mode == "repeat")
        {
            if (adapter?.Accumulates(argument.Name, destination) != true)
                throw value.Error("repeat-evidence-required", "emission", "Falta evidencia de acumulación del parser.");
            return value.Items.Select(Scalar).ToArray();
        }
        if (rule.Mode != "join" || string.IsNullOrEmpty(rule.Separator)) throw value.Error("invalid-emission-mode", "emission", "Regla de emisión inválida.");
        var items = value.Items.Select(Scalar).ToArray();
        if (items.Any(x => x.Contains(rule.Separator, StringComparison.Ordinal)))
            throw value.Error("unrepresentable-join", "emission", "Un elemento contiene el separador y no hay escape acreditado.");
        var joined = string.Join(rule.Separator, items);
        CheckText(joined, value, argument.Name, destination, adapter);
        return [joined];
    }

    private static void CheckText(string text, RecipeValue value, string name, string destination, IArgumentAdapter? adapter)
    {
        if (text.Contains('\0') || text.IndexOfAny(['\r', '\n']) >= 0 && adapter?.SupportsLineBreaks(name, destination) != true)
            throw value.Error("unrepresentable-text", "emission", "NUL o salto de línea sin representación acreditada.");
    }
    private static string Normalize(string text)
    {
        // RecipeValue already supplies invariant, lossless JSON number syntax.
        var value = new RecipeValue(RecipeKind.Number, new("", 1, 1, "$"), text);
        return value.Redacted()!.ToJsonString();
    }
}
