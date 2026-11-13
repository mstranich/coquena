using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Coquena.Recipes;

// Structural vocabulary lives in RecipeShape; rules across fields are checked here.
// Cross-argument constraints, imports and emission are owned by 046.
internal static partial class RecipeStructure
{
    internal static RecipeValue Layer(RecipeValue root, bool beforeInterpolation)
    {
        RecipeShape.Validate(root, RecipeShape.Root, beforeInterpolation);
        return root;
    }

    internal static void Effective(RecipeValue root)
    {
        if (root.Get("name") is { } name) Name(name.Text!, name);
        if (root.Get("target") is { } target && target.Text is not ("global" or "self"))
            throw target.Error("invalid-target", "validation", "Target sólo admite global o self.");
        if (root.Get("target")?.Text == "self" && root.Get("service") is null)
            throw root.Get("target")!.Error("self-requires-service", "validation", "Target self requiere service.");
        if (root.Get("service") is { } service)
        {
            var backend = service.Get("backend") ?? throw service.Error("backend-required", "validation", "Service requiere un backend efectivo.");
            var external = backend.Get("executable");
            var version = backend.Get("version"); var variant = backend.Get("variant");
            if (external is null ? version is null || variant is null : version is not null || variant is not null)
                throw backend.Error("invalid-backend-selection", "validation", "Usar version y variant, o executable, excluyentes.");
            foreach (var child in backend.Fields.Values) Nonempty(child);
            if (backend.Get("name") is { } alias && (alias.Text!.IndexOfAny([':', '/', '\\']) >= 0 || alias.Text.Any(char.IsControl)))
                throw alias.Error("invalid-backend-name", "validation", "El alias del backend es un componente, sin separadores.");
            if (service.Get("server")?.Get("health")?.Get("interval-ms") is { } interval &&
                (BigInteger.Parse(interval.Text!, CultureInfo.InvariantCulture) < 100 || BigInteger.Parse(interval.Text!, CultureInfo.InvariantCulture) > 10000))
                throw interval.Error("invalid-health-interval", "validation", "Interval-ms debe estar entre 100 y 10000.");
            if (service.Get("server")?.Get("hooks")?.Get("start-failed") is { } hook)
                Nonempty(hook.Get("command") ?? throw hook.Error("hook-command-required", "validation", "Hook requiere command."));
        }
        foreach (var group in new[] { "models", "companions" })
            if (root.Get(group) is { } resources)
                foreach (var resource in resources.Fields.Values)
                {
                    var repository = resource.Get("repository"); var files = resource.Get("files"); var quantization = resource.Get("quantization");
                    if (repository is not null)
                    {
                        if (!ValidRepository(repository.Text!)) throw repository.Error("invalid-repository", "validation", "Se requiere publicador/repositorio.");
                        if ((quantization is null) == (files is null)) throw resource.Error("invalid-resource-selection", "validation", "Repository requiere quantization o files, excluyentes.");
                    }
                    else if (files is null || quantization is not null || resource.Get("revision") is not null)
                        throw resource.Error("invalid-local-selection", "validation", "Recurso local requiere files y no admite revision ni quantization.");
                    if (files is not null)
                    {
                        if (files.Items.Count == 0) throw files.Error("empty-files", "validation", "Files requiere una lista no vacía.");
                        foreach (var file in files.Items)
                        {
                            Nonempty(file);
                            if (repository is not null && (Path.IsPathRooted(file.Text!) || Uri.TryCreate(file.Text, UriKind.Absolute, out _)))
                                throw file.Error("mixed-resource-paths", "validation", "Files con repository requiere rutas del repositorio, no rutas locales o URLs.");
                        }
                    }
                    if (quantization is not null) Nonempty(quantization);
                    if (resource.Get("revision") is { } revision) Nonempty(revision);
                    if (group == "companions" && resource.Get("type")?.Text is not ("mmproj" or "mtp"))
                        throw resource.Error("invalid-companion-type", "validation", "Companion requiere type mmproj o mtp.");
                }
    }

    internal static RecipeValue NormalizeModels(RecipeValue root)
    {
        if (root.Get("models") is not { Kind: RecipeKind.Map } models) return root;
        var fields = models.Fields.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        foreach (var pair in fields.ToArray())
            if (pair.Value.Kind == RecipeKind.String)
            {
                var (repository, quantization) = ShortModel(pair.Value);
                fields[pair.Key] = RecipeValue.Map(pair.Value.Origin, new Dictionary<string, RecipeValue>
                {
                    ["repository"] = new(RecipeKind.String, pair.Value.Origin, repository, pair.Value.Style, pair.Value.Sensitive),
                    ["quantization"] = new(RecipeKind.String, pair.Value.Origin, quantization, pair.Value.Style, pair.Value.Sensitive)
                });
            }
        var result = root.Fields.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        result["models"] = models.WithFields(fields);
        return root.WithFields(result);
    }

    internal static (string Repository, string Quantization) ShortModel(RecipeValue value)
    {
        // Prior to expansion this is checked only as a string, not as a selector.
        var separator = value.Text!.IndexOf(':');
        if (separator <= 0 || separator != value.Text.LastIndexOf(':') || separator == value.Text.Length - 1 || !ValidRepository(value.Text[..separator]))
            throw value.Error("invalid-model-shorthand", "validation", "Modelo escalar requiere publicador/repositorio:cuantización.");
        return (value.Text[..separator], value.Text[(separator + 1)..]);
    }
    private static bool ValidRepository(string text) => text.Split('/') is [var owner, var repo] &&
        owner.Length > 0 && repo.Length > 0 && !text.Any(c => char.IsWhiteSpace(c) || c is ':' or '\\' or '#' or '\0');
    private static void Nonempty(RecipeValue value)
    { if (string.IsNullOrWhiteSpace(value.Text)) throw value.Error("empty-value", "validation", "Se requiere texto no vacío."); }
    private static void Name(string text, RecipeValue value)
    {
        if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(['\0', '\r', '\n', ':', '/', '\\']) >= 0)
            throw value.Error("invalid-name", "structure", "Nombre inválido; se requiere un componente explícito válido.");
    }
}
