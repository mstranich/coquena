using System.Text.RegularExpressions;
using Coquena.Contracts;

namespace Coquena.Configuration;

public sealed class ConfigurationException(string message) : Exception(message);
public sealed record ParameterDefinition(string Name, string Type, string Application, string Destination,
    string Description, string[] Aliases, string? Default = null, string[]? Choices = null,
    ParameterSupport[]? Support = null, string[]? NegativeAliases = null, string? Syntax = null, string? Source = null);
public sealed record ParameterSupport(string? Variant = null, Guid? BackendId = null,
    string? From = null, string? Until = null, bool EvidenceOnly = false);
public sealed record ParametersDocument(int SchemaVersion, ParameterDefinition[] Definitions, string? CatalogRevision = null);

// Complete pinned server documentation plus established Coquena metadata.
// Documented syntax is not evidence of availability in an arbitrary installed build.
public static partial class ParameterCatalog
{
    private static readonly ParameterDefinition[] EstablishedDefinitions =
    [
        new("ctx-size", "integer", "model", "INI", "Contexto", ["c", "LLAMA_ARG_CTX_SIZE"]),
        new("threads", "integer", "model", "INI", "Hilos de generación", ["t", "LLAMA_ARG_THREADS"]),
        new("threads-batch", "integer", "model", "INI", "Hilos de batch", ["tb"]),
        new("n-predict", "integer", "model", "INI", "Tokens a generar", ["n", "predict", "LLAMA_ARG_N_PREDICT"]),
        new("n-gpu-layers", "text", "model", "INI", "Capas GPU: número, auto o all", ["ngl", "gpu-layers", "LLAMA_ARG_N_GPU_LAYERS"]),
        new("moe-cache-mib", "integer", "model", "INI", "Caché GPU de expertos MoE en CPU, en MiB; 0 desactiva", ["LLAMA_ARG_MOE_CACHE_MIB"],
            Default: "0", Support: [new(From: "b11480")]),
        new("moe-cache-cpu-share", "number", "model", "INI", "Fracción de fallos de caché MoE calculada en CPU al llenarse; entre 0 y 1", ["LLAMA_ARG_MOE_CACHE_CPU_SHARE"],
            Default: "0", Support: [new(From: "b12191")]),
        new("temperature", "number", "model", "INI", "Temperatura", ["temp", "LLAMA_ARG_TEMPERATURE"]),
        new("top-k", "integer", "model", "INI", "Top K", ["LLAMA_ARG_TOP_K"]),
        new("top-p", "number", "model", "INI", "Top P", ["LLAMA_ARG_TOP_P"]),
        new("min-p", "number", "model", "INI", "Min P", ["LLAMA_ARG_MIN_P"]),
        new("device", "text", "model", "INI", "Dispositivos separados por comas", ["dev", "LLAMA_ARG_DEVICE"]),
        new("flash-attn", "text", "model", "INI", "Flash attention: on, off o auto", ["fa", "LLAMA_ARG_FLASH_ATTN"]),
        new("cache-type-k", "text", "model", "INI", "Tipo de caché K", ["ctk", "LLAMA_ARG_CACHE_TYPE_K"]),
        new("cache-type-v", "text", "model", "INI", "Tipo de caché V", ["ctv", "LLAMA_ARG_CACHE_TYPE_V"]),
        new("load-on-startup", "boolean", "model", "INI", "Carga al inicio", ["__PRESET_LOAD_ON_STARTUP"]),
        new("stop-timeout", "integer", "model", "INI", "Espera de detención en segundos", ["__PRESET_STOP_TIMEOUT"]),
        new("host", "text", "server", "CLI", "Default Coquena: 127.0.0.1", ["LLAMA_ARG_HOST"]),
        new("port", "integer", "server", "CLI", "Default Coquena: 9931", ["LLAMA_ARG_PORT"]),
        new("metrics", "boolean", "server", "CLI", "Default Coquena: true", ["LLAMA_ARG_ENDPOINT_METRICS"]),
        new("models-max", "integer", "server", "CLI", "Máximo de modelos cargados", ["LLAMA_ARG_MODELS_MAX"]),
        new("models-autoload", "boolean", "server", "CLI", "Carga automática del router", ["no-models-autoload", "LLAMA_ARG_MODELS_AUTOLOAD"]),
        new("models-preset", "text", "server", "CLI", "INI por instancia generado por Coquena", ["LLAMA_ARG_MODELS_PRESET"]),
        new("model", "reference", "model", "INI", "Ruta resuelta desde ModelId del preset", ["m", "LLAMA_ARG_MODEL"])
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
            throw new ConfigurationException("Nombre de parámetro inválido o reservado por el INI.");
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
            ? "admitido" : "desconocido";
    }

    public static void ModelParameter(string key, ParameterDefinition[]? definitions = null)
    {
        var canonical = Canonical(key, definitions);
        if (Find(canonical, definitions)?.Application == "server")
            throw new ConfigurationException($"{canonical} pertenece al servidor/router; se configura en su definición (tarea 011).");
        if (canonical == "model")
            throw new ConfigurationException("El modelo se selecciona mediante preset create o preset model set, con ID estable.");
    }

    // b11482 does not quote/unquote strings: ; and # start comments even inside quotes.
    // Reject values that cannot round-trip, rather than silently corrupting them.
    public static void Literal(string value)
    {
        if (value.Any(char.IsControl) || value.IndexOfAny([';', '#']) >= 0 || value != value.Trim())
            throw new ConfigurationException("El INI b11482 no representa controles, comentarios (; o #) ni espacios exteriores sin pérdida.");
    }
}
