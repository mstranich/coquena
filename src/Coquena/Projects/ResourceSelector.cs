using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Models;

namespace Coquena.Projects;

// Do not use System.Uri: #type and :quantization belong to model identities.
public sealed record ResourceSelector(string Kind, string Name)
{
    public static ResourceSelector Parse(string value)
    {
        var separator = value.IndexOf("://", StringComparison.Ordinal);
        if (separator < 0) throw new ArgumentException("Use backend://variante, model://clave o companion://clave.");
        var kind = value[..separator]; var name = value[(separator + 3)..];
        if (kind is not ("backend" or "model" or "companion") || name.Length is 0 or > 512 || string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl))
            throw new ArgumentException("Referencia de recurso inválida.");
        if (kind == "backend")
        {
            if (!BackendCatalog.ValidFamily(name) && (name.Split('/') is not { Length: 1 or 2 } parts ||
                parts.Any(p => string.IsNullOrWhiteSpace(p) || p.IndexOfAny([':', '\\']) >= 0)))
                throw new ArgumentException("Use una variante, alias o proyecto/alias de backend.");
        }
        else if (name.Contains('/'))
        {
            var model = ModelRequest.Parse(name);
            if (model.Quantization is null || (kind == "companion") != (model.CompanionType is not null))
                throw new ArgumentException("La referencia remota requiere cuantización y un tipo coherente.");
        }
        else if (name.IndexOfAny([':', '\\']) >= 0) throw new ArgumentException("Clave local inválida.");
        return new(kind, name);
    }

    public bool Matches(string kind, string key, JsonNode? resource, JsonNode? installation, PreparationRequest? request = null, string? alias = null, string? projectName = null)
    {
        if (kind != Kind) return false;
        if (kind == "backend")
        {
            alias ??= request?.Alias; projectName ??= request?.ProjectName;
            if (BackendCatalog.ValidFamily(Name)) return Name == installation?["selection"]?["backendSelection"]?["variant"]?.GetValue<string>() ||
                Name == request?.Intent.Get("variant")?.Text;
            return alias is not null && (Name == alias || Name == projectName + "/" + alias);
        }
        return Name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            Name.Equals(resource?["logical-key"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase) ||
            Name.Equals(RemoteName(request), StringComparison.OrdinalIgnoreCase);
    }
    private static string? RemoteName(PreparationRequest? request)
    {
        if (request is null) return null;
        if (request.Intent.Kind == Coquena.Recipes.RecipeKind.String) return request.Intent.Text;
        if (request.Intent.Get("repository")?.Text is not string repository) return null;
        return repository + (request.Intent.Get("type")?.Text is string type ? "#" + type : "") +
            (request.Intent.Get("quantization")?.Text is string quant ? ":" + quant : "");
    }
    public override string ToString() => Kind + "://" + Name;
    public void CheckAmbiguity(IEnumerable<string?> projects)
    {
        if (Kind == "backend" && !Name.Contains('/') && !BackendCatalog.ValidFamily(Name) && projects.Where(p => p is not null).Distinct(StringComparer.Ordinal).Skip(1).Any())
            throw new Coquena.Storage.StorageException("ambiguous-resource-selector", "Alias de backend ambiguo; indique proyecto/alias en el ámbito elegido.");
    }
}
