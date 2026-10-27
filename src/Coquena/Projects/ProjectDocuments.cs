using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coquena.Storage;

namespace Coquena.Projects;

// Uses the JSON DOM, never reflection-based serialization (also used by Native AOT).
public static class ProjectDocuments
{
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    { "combination-index", "resolution", "inventory", "state", "active", "last-good", "update-plan", "operation", "recipe-registry", "project-index" };
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
            foreach (var entry in data["recipes"]!.AsArray())
            {
                Uuid(entry?["recipe-id"]); Uuid(entry?["consumer-id"]);
                if (string.IsNullOrWhiteSpace(entry?["name"]?.GetValue<string>()) || entry?["files"] is not JsonArray ||
                    entry["entities"] is not JsonArray || entry["pending-withdrawals"] is not JsonArray || entry["revision"]?.GetValue<long>() is not > 0) Invalid();
                foreach (var file in entry!["files"]!.AsArray()) if (string.IsNullOrEmpty(file?.GetValue<string>())) Invalid();
                foreach (var entity in entry["entities"]!.AsArray())
                {
                    Uuid(entity?["id"]); Uuid(entity?["owner"]);
                    if (entity?["kind"]?.GetValue<string>() is not ("service" or "preset") || string.IsNullOrWhiteSpace(entity["name"]?.GetValue<string>())) Invalid();
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
        else if (kind == "operation") Uuid(data["operation-id"]);
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
