using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coquena.Storage;

namespace Coquena.Projects;

// Uses the JSON DOM, never reflection-based serialization (also used by Native AOT).
public static class ProjectDocuments
{
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    { "combination-index", "resolution", "inventory", "state", "active", "last-good", "update-plan", "operation", "recipe-registry", "project-index", "recipe-intention" };
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    public static JsonObject Create(string kind, long revision, JsonObject data)
    {
        if (!Kinds.Contains(kind) || revision < 1) Invalid();
        return new() { ["kind"] = kind, ["format"] = 1, ["revision"] = revision, ["data"] = data.DeepClone() };
    }

    public static JsonObject Read(byte[] bytes, string kind)
    {
        try
        {
            if (bytes.Length > 16777216 || bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 })) Invalid();
            using var doc = JsonDocument.Parse(Utf8.GetString(bytes), new() { MaxDepth = 64 });
            Check(doc.RootElement);
            var root = JsonNode.Parse(bytes) as JsonObject;
            if (root is null || root.Count != 4 || root["kind"]?.GetValue<string>() != kind || !Kinds.Contains(kind) ||
                root["format"]?.GetValue<int>() != 1 || root["revision"]?.GetValue<long>() is not > 0 || root["data"] is not JsonObject) Invalid();
            ValidateData(kind, root!["data"]!.AsObject());
            return root;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or DecoderFallbackException)
        { throw new StorageException("invalid-project-document", "Documento de proyecto inválido; se conserva sin sobrescribir.", inner: e); }
    }

    internal static byte[] Bytes(JsonObject document) => Utf8.GetBytes(document.ToJsonString());
    internal static string Id() => Guid.NewGuid().ToString("D");
    internal static string Uuid(JsonNode? value)
    {
        var text = value?.GetValue<string>();
        if (text is null || !Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty || id.ToString("D") != text) Invalid();
        return text!;
    }
    internal static void Invalid() => throw new StorageException("invalid-project-document", "Documento de proyecto inválido; se conserva sin sobrescribir.");
    private static void ValidateData(string kind, JsonObject data)
    {
        if (kind == "recipe-intention")
        {
            if (data["text"]?.GetValue<string>() is not string text || text.Length > 16777216 || data["path-bases"] is not JsonObject) Invalid();
            foreach (var pair in data["path-bases"]!.AsObject())
                if (pair.Value?.GetValue<string>() is not string path || !Path.IsPathFullyQualified(path)) Invalid();
        }
        if (kind == "state")
        {
            foreach (var key in new[] { "scope-id", "generation", "transaction-id" }) Uuid(data[key]);
            if (data["documents"] is not JsonObject) Invalid();
            foreach (var key in new[] { "base-volume", "base-file-id", "root-volume", "root-file-id" })
                if (string.IsNullOrEmpty(data[key]?.GetValue<string>())) Invalid();
        }
        else if (kind == "recipe-registry")
        {
            if (data["recipes"] is not JsonArray) Invalid();
            if (data["import-resolutions"] is JsonNode resolutions)
            {
                if (resolutions is not JsonObject) Invalid();
                foreach (var pair in resolutions.AsObject())
                {
                    if (pair.Value?["fingerprint"]?.GetValue<string>() is not string fingerprint || !ScopeStore.IsCombinationId(fingerprint) || pair.Value["resolution"] is not JsonObject)
                        Invalid();
                    Uuid(pair.Value!["resource-id"]); Uuid(pair.Value["installation-id"]);
                }
            }
            foreach (var entry in data["recipes"]!.AsArray())
            {
                Uuid(entry?["recipe-id"]); Uuid(entry?["consumer-id"]);
                if (string.IsNullOrWhiteSpace(entry?["name"]?.GetValue<string>()) || entry?["files"] is not JsonArray ||
                    entry["entities"] is not JsonArray || entry["pending-withdrawals"] is not JsonArray || entry["revision"]?.GetValue<long>() is not > 0) Invalid();
                foreach (var file in entry!["files"]!.AsArray()) if (string.IsNullOrEmpty(file?.GetValue<string>())) Invalid();
                if (entry["project-consumer"] is JsonNode consumer) _ = consumer.GetValue<bool>();
                if (entry["import-source"] is JsonNode imported && imported.GetValue<bool>())
                {
                    if (entry["source-files"] is not JsonArray { Count: > 0 } || string.IsNullOrEmpty(entry["source-volume"]?.GetValue<string>()) || string.IsNullOrEmpty(entry["source-file-id"]?.GetValue<string>())) Invalid();
                    foreach (var file in entry["source-files"]!.AsArray()) if (string.IsNullOrEmpty(file?.GetValue<string>())) Invalid();
                    if (entry["source-entrypoint-volume"] is not null || entry["source-entrypoint-file-id"] is not null)
                        if (string.IsNullOrEmpty(entry["source-entrypoint-volume"]?.GetValue<string>()) || string.IsNullOrEmpty(entry["source-entrypoint-file-id"]?.GetValue<string>())) Invalid();
                }
                if (entry["global-preset"] is JsonNode globalPreset && string.IsNullOrWhiteSpace(globalPreset.GetValue<string>())) Invalid();
                if (entry["import-pending"] is JsonNode pendingImport)
                {
                    if (pendingImport is not JsonArray) Invalid();
                    var pendingNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var pending in pendingImport.AsArray())
                        if (pending?.GetValue<string>() is not string key || string.IsNullOrWhiteSpace(key) || !pendingNames.Add(key)) Invalid();
                }
                if (entry["publication"] is JsonNode publication) Publication(publication);
                if (entry["publications"] is JsonNode publications)
                {
                    if (publications is not JsonArray) Invalid();
                    foreach (var item in publications.AsArray()) Publication(item!);
                }
                if (entry["publication-operation"] is JsonNode operation) PublicationOperation(operation);
                if (entry["publication-previous"] is JsonNode previous)
                {
                    Uuid(previous["service-id"]); Presets(previous["presets"]);
                    if (previous["affected"] is not JsonArray || previous["combination"]?.GetValue<string>() is null || previous["recipe-name"]?.GetValue<string>() is null) Invalid();
                }
                foreach (var entity in entry["entities"]!.AsArray())
                {
                    Uuid(entity?["id"]); Uuid(entity?["owner"]);
                    if (entity?["kind"]?.GetValue<string>() is not ("service" or "preset") || string.IsNullOrWhiteSpace(entity["name"]?.GetValue<string>())) Invalid();
                    if (entity!["publication-operation"] is JsonNode pending) PublicationOperation(pending);
                    if (entity["consumers"] is JsonNode contributions) Contributions(contributions);
                }
            }
        }
        else if (kind == "inventory")
        {
            Uuid(data["scope-id"]);
            if (data["model-aliases"] is JsonNode aliases)
            {
                if (aliases is not JsonArray) Invalid();
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var alias in aliases.AsArray())
                {
                    Uuid(alias?["resource-id"]); Uuid(alias?["installation-id"]);
                    if (alias?["kind"]?.GetValue<string>() is not ("model" or "companion") ||
                        string.IsNullOrWhiteSpace(alias["key"]?.GetValue<string>()) ||
                        !keys.Add(alias["kind"]!.GetValue<string>() + ":" + alias["key"]!.GetValue<string>())) Invalid();
                }
            }
            if (data["resources"] is not JsonArray) Invalid();
            foreach (var resource in data["resources"]!.AsArray())
            {
                Uuid(resource?["resource-id"]);
                if (string.IsNullOrEmpty(resource?["logical-key"]?.GetValue<string>()) || resource?["kind"]?.GetValue<string>() is not ("model" or "companion" or "backend") ||
                    resource["ownership"]?.GetValue<string>() is not ("external" or "managed") || resource["installations"] is not JsonArray) Invalid();
            }
        }
        else if (kind == "project-index")
        {
            if (data["projects"] is not JsonArray) Invalid();
            foreach (var project in data["projects"]!.AsArray())
            {
                Uuid(project?["scope-id"]);
                if (project?["base"]?.GetValue<string>() is not string path || !Path.IsPathFullyQualified(path) ||
                    project["revision"]?.GetValue<long>() is not > 0 || project["volume"] is null || project["file-id"] is null) Invalid();
            }
        }
        else if (kind == "resolution")
        {
            if (data["combination-id"]?.GetValue<string>() is not string id || !ScopeStore.IsCombinationId(id) ||
                data["selections"] is not JsonObject || data["resources"] is not JsonArray) Invalid();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var resource in data["resources"]!.AsArray())
                if (resource?["key"]?.GetValue<string>() is not string key || string.IsNullOrWhiteSpace(key) ||
                    resource?["kind"]?.GetValue<string>() is not ("backend" or "model" or "companion") ||
                    !keys.Add(resource["kind"]!.GetValue<string>() + ":" + key)) Invalid();
        }
        else if (kind == "combination-index")
        {
            if (data["combinations"] is not JsonObject) Invalid();
            foreach (var combination in data["combinations"]!.AsObject())
            {
                if (!ScopeStore.IsCombinationId(combination.Key) || combination.Value?["files"] is not JsonArray { Count: > 0 } ||
                    combination.Value?["env-files"] is not JsonArray) Invalid();
                foreach (var path in combination.Value!["files"]!.AsArray().Concat(combination.Value["env-files"]!.AsArray()))
                    if (string.IsNullOrWhiteSpace(path?.GetValue<string>())) Invalid();
            }
        }
        else if (kind == "update-plan")
        {
            if (data["plans"] is not JsonObject plans) { Invalid(); return; }
            foreach (var pair in plans)
            {
                if (!ScopeStore.IsCombinationId(pair.Key) || pair.Value is not JsonObject plan) { Invalid(); return; }
                Uuid(plan["scope-id"]);
                if (plan["combination-id"]?.GetValue<string>() != pair.Key || plan["base-revision"]?.GetValue<long>() is not >= 0 ||
                    !DateTimeOffset.TryParse(plan["created-at"]?.GetValue<string>(), out _) ||
                    plan["intent-hmac"]?.GetValue<string>() is not string intent || !ScopeStore.IsCombinationId(intent) ||
                    plan["resolution-hmac"]?.GetValue<string>() is not string resolution || !ScopeStore.IsCombinationId(resolution) ||
                    plan["selectors"] is not JsonArray || plan["resources"] is not JsonArray) Invalid();
                foreach (var selector in plan["selectors"]!.AsArray())
                    try { _ = ResourceSelector.Parse(selector!.GetValue<string>()); }
                    catch (Exception e) when (e is ArgumentException or Coquena.Models.ModelSourceException) { Invalid(); }
                foreach (var resource in plan["resources"]!.AsArray())
                {
                    if (resource?["kind"]?.GetValue<string>() is not ("backend" or "model" or "companion") ||
                        string.IsNullOrWhiteSpace(resource["key"]?.GetValue<string>()) ||
                        resource["status"]?.GetValue<string>() is not ("not-applicable" or "not-prepared" or "inconclusive" or "unchanged" or "update-available" or "intervention-required" or "orphaned" or "query-failed")) Invalid();
                    if (resource!["installation-id"] is not null) { Uuid(resource["installation-id"]); Uuid(resource["resource-id"]); }
                    if (resource["proposed"] is not null && resource["status"]!.GetValue<string>() != "update-available") Invalid();
                }
            }
        }
        else if (kind == "operation") Uuid(data["operation-id"]);
    }

    private static void Publication(JsonNode node)
    {
        if (node is not JsonObject) Invalid();
        Uuid(node["service-id"]); Uuid(node["target-scope-id"]);
        foreach (var key in new[] { "target-root", "target-base" })
            if (node[key]?.GetValue<string>() is not string path || !Path.IsPathFullyQualified(path)) Invalid();
        _ = node["global"]?.GetValue<bool>() ?? throw new InvalidOperationException();
        Presets(node["presets"]);
    }
    private static void PublicationOperation(JsonNode node)
    {
        if (node is not JsonObject) Invalid();
        Uuid(node["operation-id"]); Uuid(node["consumer-id"]); Uuid(node["recipe-id"]); Uuid(node["service-id"]);
        if (node["phase"]?.GetValue<string>() is not ("prepared" or "sent" or "observed" or "uncertain") ||
            node["affected"] is not JsonArray || node["ini-before"]?.GetValue<string>() is null || node["ini-candidate"]?.GetValue<string>() is null) Invalid();
        Contributions(node["before"]); Contributions(node["after"]);
        if (node["restore"] is JsonNode restoration) Contributions(restoration);
    }
    private static void Contributions(JsonNode? node)
    {
        if (node is not JsonArray) Invalid();
        foreach (var contribution in node!.AsArray())
        {
            Uuid(contribution?["consumer-id"]); Uuid(contribution?["scope-id"]);
            // Older registration records may reserve names before definitions exist.
            if (contribution?["presets"] is JsonNode presets) Presets(presets);
        }
    }
    private static void Presets(JsonNode? node)
    {
        if (node is not JsonArray) Invalid();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in node!.AsArray())
        {
            if (preset?["name"]?.GetValue<string>() is not string name || !names.Add(name) ||
                preset["ini"]?.GetValue<string>() is null || preset["expected-arguments"] is not JsonArray || preset["resources"] is not JsonArray) Invalid();
            _ = preset!["warmup"]?.GetValue<bool>() ?? throw new InvalidOperationException();
            foreach (var resource in preset["resources"]!.AsArray()) { Uuid(resource?["resource-id"]); Uuid(resource?["installation-id"]); }
        }
    }
    private static void Check(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) Invalid(); Check(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) Check(item);
    }
}
