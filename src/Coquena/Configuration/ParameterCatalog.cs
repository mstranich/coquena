using System.Text.RegularExpressions;
using Coquena.Contracts;

namespace Coquena.Configuration;

public sealed class ConfigurationException(string message) : Exception(message);
public sealed record ParameterDefinition(string Name, string Type, string Application, string Destination,
    string Description, string[] Aliases);

// Hand-authored, bounded catalog: evidence applies only to official b11482.
// See specs/010-m1-config-presets-ini/README.md. Missing evidence is never rejection.
public static partial class ParameterCatalog
{
    public static readonly ParameterDefinition[] Definitions =
    [
        new("ctx-size", "integer", "model", "INI", "Contexto", ["c", "LLAMA_ARG_CTX_SIZE"]),
        new("threads", "integer", "model", "INI", "Hilos de generación", ["t", "LLAMA_ARG_THREADS"]),
        new("threads-batch", "integer", "model", "INI", "Hilos de batch", ["tb"]),
        new("n-predict", "integer", "model", "INI", "Tokens a generar", ["n", "predict", "LLAMA_ARG_N_PREDICT"]),
        new("n-gpu-layers", "text", "model", "INI", "Capas GPU: número, auto o all", ["ngl", "gpu-layers", "LLAMA_ARG_N_GPU_LAYERS"]),
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
        new("port", "integer", "server", "CLI", "Default Coquena: 8080", ["LLAMA_ARG_PORT"]),
        new("metrics", "boolean", "server", "CLI", "Default Coquena: true", ["LLAMA_ARG_ENDPOINT_METRICS"]),
        new("models-max", "integer", "server", "CLI", "Máximo de modelos cargados", ["LLAMA_ARG_MODELS_MAX"]),
        new("models-autoload", "boolean", "server", "CLI", "Carga automática del router", ["no-models-autoload", "LLAMA_ARG_MODELS_AUTOLOAD"]),
        new("models-preset", "text", "server", "CLI", "INI por instancia generado por Coquena", ["LLAMA_ARG_MODELS_PRESET"]),
        new("model", "reference", "model", "INI", "Ruta resuelta desde ModelId del preset", ["m", "LLAMA_ARG_MODEL"])
    ];

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    public static ParameterDefinition? Find(string key)
    {
        key = key.TrimStart('-');
        return Definitions.SingleOrDefault(d => d.Name == key || d.Aliases.Contains(key, StringComparer.Ordinal));
    }

    public static string Canonical(string key)
    {
        key = key.TrimStart('-');
        if (!KeyPattern().IsMatch(key) || key == "version")
            throw new ConfigurationException("Nombre de parámetro inválido o reservado por el INI.");
        return Find(key)?.Name ?? key;
    }

    public static string Availability(string key, BackendRecord? backend) =>
        Availability(key, backend?.Repository, backend?.Build);

    public static string Availability(string key, string? repository, string? release) =>
        Find(key) is not null && repository is "ggml-org/llama.cpp" or "https://github.com/ggml-org/llama.cpp" && release == "b11482"
            ? "admitido" : "desconocido";

    public static void ModelParameter(string key)
    {
        var canonical = Canonical(key);
        if (Find(canonical)?.Application == "server")
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
