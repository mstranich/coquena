using System.Text.RegularExpressions;
using Coquena.Contracts;

namespace Coquena.Configuration;

public sealed class ConfigurationException(string message) : Exception(message);
public sealed record ParameterDefinition(string Name, string Type, string Application, string Destination,
    string Description, string[] Aliases, string? Default = null, string[]? Choices = null,
    ParameterSupport[]? Support = null, string[]? NegativeAliases = null, string? Syntax = null, string? Source = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? DescriptionKey = null);
public sealed record ParameterSupport(string? Variant = null, Guid? BackendId = null,
    string? From = null, string? Until = null, bool EvidenceOnly = false);
public sealed record ParametersDocument(int SchemaVersion, ParameterDefinition[] Definitions, string? CatalogRevision = null);

// Complete pinned server documentation plus established Coquena metadata.
// Documented syntax is not evidence of availability in an arbitrary installed build.
public static partial class ParameterCatalog
{
    private static readonly ParameterDefinition[] EstablishedDefinitions =
    [
        new("ctx-size", "integer", "model", "INI", "", ["c", "LLAMA_ARG_CTX_SIZE"], DescriptionKey: Messages.ParameterDescriptionsContext.Key),
        new("threads", "integer", "model", "INI", "", ["t", "LLAMA_ARG_THREADS"], DescriptionKey: Messages.ParameterDescriptionsGenerationThreads.Key),
        new("threads-batch", "integer", "model", "INI", "", ["tb"], DescriptionKey: Messages.ParameterDescriptionsBatchThreads.Key),
        new("n-predict", "integer", "model", "INI", "", ["n", "predict", "LLAMA_ARG_N_PREDICT"], DescriptionKey: Messages.ParameterDescriptionsTokensToGenerate.Key),
        new("n-gpu-layers", "text", "model", "INI", "", ["ngl", "gpu-layers", "LLAMA_ARG_N_GPU_LAYERS"], DescriptionKey: Messages.ParameterDescriptionsGPULayersNumberAutoOrAll.Key),
        new("moe-cache-mib", "integer", "model", "INI", "", ["LLAMA_ARG_MOE_CACHE_MIB"],
            Default: "0", Support: [new(From: "b11480")], DescriptionKey: Messages.ParameterDescriptionsGPUCacheForCPUMoEExpertsIn.Key),
        new("moe-cache-cpu-share", "number", "model", "INI", "", ["LLAMA_ARG_MOE_CACHE_CPU_SHARE"],
            Default: "0", Support: [new(From: "b12191")], DescriptionKey: Messages.ParameterDescriptionsFractionOfMoECacheMissesComputedOn.Key),
        new("temperature", "number", "model", "INI", "", ["temp", "LLAMA_ARG_TEMPERATURE"], DescriptionKey: Messages.ParameterDescriptionsTemperature.Key),
        new("top-k", "integer", "model", "INI", "Top K", ["LLAMA_ARG_TOP_K"]),
        new("top-p", "number", "model", "INI", "Top P", ["LLAMA_ARG_TOP_P"]),
        new("min-p", "number", "model", "INI", "Min P", ["LLAMA_ARG_MIN_P"]),
        new("device", "text", "model", "INI", "", ["dev", "LLAMA_ARG_DEVICE"], DescriptionKey: Messages.ParameterDescriptionsCommaSeparatedDevices.Key),
        new("flash-attn", "text", "model", "INI", "", ["fa", "LLAMA_ARG_FLASH_ATTN"], DescriptionKey: Messages.ParameterDescriptionsFlashAttentionOnOffOrAuto.Key),
        new("cache-type-k", "text", "model", "INI", "", ["ctk", "LLAMA_ARG_CACHE_TYPE_K"], DescriptionKey: Messages.ParameterDescriptionsKCacheType.Key),
        new("cache-type-v", "text", "model", "INI", "", ["ctv", "LLAMA_ARG_CACHE_TYPE_V"], DescriptionKey: Messages.ParameterDescriptionsVCacheType.Key),
        new("load-on-startup", "boolean", "model", "INI", "", ["__PRESET_LOAD_ON_STARTUP"], DescriptionKey: Messages.ParameterDescriptionsLoadAtStartup.Key),
        new("stop-timeout", "integer", "model", "INI", "", ["__PRESET_STOP_TIMEOUT"], DescriptionKey: Messages.ParameterDescriptionsStopTimeoutInSeconds.Key),
        new("host", "text", "server", "CLI", "", ["LLAMA_ARG_HOST"], DescriptionKey: Messages.ParameterHostDefault.Key),
        new("port", "integer", "server", "CLI", "", ["LLAMA_ARG_PORT"], DescriptionKey: Messages.ParameterPortDefault.Key),
        new("metrics", "boolean", "server", "CLI", "", ["LLAMA_ARG_ENDPOINT_METRICS"], DescriptionKey: Messages.ParameterMetricsDefault.Key),
        new("models-max", "integer", "server", "CLI", "", ["LLAMA_ARG_MODELS_MAX"], DescriptionKey: Messages.ParameterDescriptionsMaximumLoadedModels.Key),
        new("models-autoload", "boolean", "server", "CLI", "", ["no-models-autoload", "LLAMA_ARG_MODELS_AUTOLOAD"], DescriptionKey: Messages.ParameterDescriptionsRouterAutoload.Key),
        new("models-preset", "text", "server", "CLI", "", ["LLAMA_ARG_MODELS_PRESET"], DescriptionKey: Messages.ParameterDescriptionsPerInstanceINIGeneratedByCoquena.Key),
        new("model", "reference", "model", "INI", "", ["m", "LLAMA_ARG_MODEL"], DescriptionKey: Messages.ParameterDescriptionsPathResolvedFromPresetModelId.Key)
    ];

    public static readonly ParameterDefinition[] Definitions = CompleteDefinitions();

    private static ParameterDefinition[] CompleteDefinitions()
    {
        var upstream = ServerDefinitions().ToDictionary(d => d.Name, StringComparer.Ordinal);
        foreach (var legacy in EstablishedDefinitions)
        {
            if (!upstream.TryGetValue(legacy.Name, out var current)) { upstream.Add(legacy.Name, legacy); continue; }
            var negative = current.NegativeAliases ?? [];
            upstream[legacy.Name] = current with
            {
                Type = legacy.Type, Application = legacy.Application, Destination = legacy.Destination,
                Description = ParameterManager.Protected.Contains(legacy.Name) ? legacy.Description : current.Description,
                DescriptionKey = ParameterManager.Protected.Contains(legacy.Name) ? legacy.DescriptionKey : current.DescriptionKey,
                Aliases = current.Aliases.Concat(legacy.Aliases).Except(negative, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray(),
                Default = legacy.Default, Support = legacy.Support,
                Choices = legacy.Type == "enum" ? current.Choices : []
            };
        }
        return upstream.Values.ToArray();
    }

    public static (string Key, string Value) Comparison(string key, string value, ParameterDefinition[] definitions)
    {
        var definition = Find(key, definitions);
        var inverse = definition?.NegativeAliases?.Contains(key.TrimStart('-'), StringComparer.Ordinal) == true;
        if (definition is null) return (key, value);
        if (definition.Type is "boolean" or "flag" && (bool.TryParse(value, out var boolean) || value is "0" or "1"))
        {
            if (value is "0" or "1") boolean = value == "1";
            value = (inverse ? !boolean : boolean) ? "true" : "false";
        }
        return (definition.Name, value);
    }

    public static string AssignmentValue(string key, string value, ParameterDefinition[] definitions) =>
        Find(key, definitions)?.NegativeAliases?.Contains(key.TrimStart('-'), StringComparer.Ordinal) == true
            ? Comparison(key, value, definitions).Value : value;

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    public static ParameterDefinition? Find(string key, ParameterDefinition[]? definitions = null)
    {
        key = key.TrimStart('-');
        return (definitions ?? Definitions).SingleOrDefault(d => d.Name == key || d.Aliases.Any(a => a.TrimStart('-') == key) ||
            d.NegativeAliases?.Contains(key, StringComparer.Ordinal) == true);
    }

    public static string Canonical(string key, ParameterDefinition[]? definitions = null)
    {
        key = key.TrimStart('-');
        if (!KeyPattern().IsMatch(key) || key == "version" && Find(key, definitions)?.Destination != "CLI")
            throw new ConfigurationException(Messages.ParameterNameInvalid);
        return Find(key, definitions)?.Name ?? key;
    }

    public static string Availability(string key, BackendRecord? backend) =>
        Availability(key, backend?.Repository, backend?.Build);

    public static string Availability(string key, string? repository, string? release)
    {
        var definition = Find(key);
        if (definition?.Support is not null)
            return ParameterManager.Availability(definition, null, repository, release);
        return definition is not null && repository is "ggml-org/llama.cpp" or "https://github.com/ggml-org/llama.cpp" && release == "b11482"
            ? "supported" : "unknown";
    }

    public static void ModelParameter(string key, ParameterDefinition[]? definitions = null)
    {
        var canonical = Canonical(key, definitions);
        if (Find(canonical, definitions)?.Application == "server")
            throw new ConfigurationException(Messages.ParameterServerOwned.Format(canonical));
        if (canonical == "model")
            throw new ConfigurationException(Messages.ParameterModelByPreset);
    }

    // b11482 does not quote/unquote strings: ; and # start comments even inside quotes.
    // Reject values that cannot round-trip, rather than silently corrupting them.
    public static void Literal(string value)
    {
        if (value.Any(char.IsControl) || value.IndexOfAny([';', '#']) >= 0 || value != value.Trim())
            throw new ConfigurationException(Messages.ParameterIniLossy);
    }
}
