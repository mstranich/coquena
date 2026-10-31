using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Coquena.Projects;

// Global entity resolutions survive importing the same intention from another
// source and are reusable by the service's generated recipe on its first start.
// A scope HMAC binds the expanded intent without exporting secret fingerprints.
internal static class ImportedResolutions
{
    internal static VerificationSelection? Binding(ScopeContext context, PreparationRequest request, string serviceName)
    {
        var selector = Selector(request, serviceName);
        var record = new ScopeStore(context).Read().Documents.GetValueOrDefault("local/recipes.json")?["data"]?["import-resolutions"]?.AsObject()
            .SingleOrDefault(p => p.Key.Equals(selector, Comparison(request))).Value;
        return record is not null && record["fingerprint"]?.GetValue<string>() == Fingerprint(context, request)
            ? new(request.Key, request.Kind, record["resource-id"]!.GetValue<string>(), record["installation-id"]!.GetValue<string>()) : null;
    }
    internal static string? Selector(PreparationRequest request, string? serviceName) => request.Kind == "backend"
        ? (serviceName ?? request.ProjectName) is string name ? "service:" + name + ":backend" : null
        : request.Kind + ":" + request.Key;

    internal static JsonObject? Exact(ScopeContext context, PreparationRequest request, string? serviceName)
    {
        if (!context.IsGlobal || Selector(request, serviceName) is not string selector || Fingerprint(context, request) is not string fingerprint) return null;
        var records = new ScopeStore(context).Read().Documents.GetValueOrDefault("local/recipes.json")?["data"]?["import-resolutions"] as JsonObject;
        var record = records?.SingleOrDefault(p => p.Key.Equals(selector, Comparison(request))).Value;
        return record?["fingerprint"]?.GetValue<string>() == fingerprint ? record["resolution"]!.DeepClone().AsObject() : null;
    }

    internal static void Record(JsonObject registry, ScopeContext context, PreparationRequest request, PreparationResource resource, string serviceName)
    {
        if (Selector(request, serviceName) is not string selector || Fingerprint(context, request) is not string fingerprint) return;
        var records = registry["import-resolutions"] as JsonObject ?? new JsonObject();
        var existing = records.Select(p => p.Key).SingleOrDefault(k => k.Equals(selector, Comparison(request)));
        records[existing ?? selector] = new JsonObject
        {
            ["fingerprint"] = fingerprint,
            ["resolution"] = resource.Resolution.DeepClone(),
            ["resource-id"] = resource.ResourceId,
            ["installation-id"] = resource.InstallationId
        };
        if (registry["import-resolutions"] is null) registry["import-resolutions"] = records;
    }
    private static StringComparison Comparison(PreparationRequest request) => request.Kind == "backend" || request.Reference?.Canonical == true
        ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static string? Fingerprint(ScopeContext context, PreparationRequest request)
    {
        var path = context.PathFor("local/intent.key");
        if (!File.Exists(path)) return null;
        var key = File.ReadAllBytes(path);
        if (key.Length != 32) return null;
        var normalized = RecipeImporter.Normalize(request.Intent, request.Kind + ":" + request.Key);
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(CombinationPreparation.Expanded(normalized).ToJsonString())));
    }
}
