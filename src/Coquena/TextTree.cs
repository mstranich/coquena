using System.Text.Json.Nodes;

namespace Coquena;

// Readable rendering of structured data, indented like YAML. Used when a command shows
// data without --json; the --json form keeps the exact structure for scripts.
public static class TextTree
{
    public static void Write(JsonNode? node, TextWriter output, int indent = 0)
    {
        var pad = new string(' ', indent);
        switch (node)
        {
            case JsonObject obj when obj.Count == 0: output.WriteLine(pad + "(vacío)"); break;
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (value is JsonObject or JsonArray && !IsEmpty(value)) { output.WriteLine($"{pad}{key}:"); Write(value, output, indent + 2); }
                    else output.WriteLine($"{pad}{key}: {Scalar(value)}");
                }
                break;
            case JsonArray array when array.Count == 0: output.WriteLine(pad + "(vacío)"); break;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is JsonObject or JsonArray && !IsEmpty(item)) { output.WriteLine(pad + "-"); Write(item, output, indent + 2); }
                    else output.WriteLine($"{pad}- {Scalar(item)}");
                }
                break;
            default: output.WriteLine(pad + Scalar(node)); break;
        }
    }

    private static bool IsEmpty(JsonNode? node) => node is JsonObject { Count: 0 } or JsonArray { Count: 0 };

    private static string Scalar(JsonNode? node) => node switch
    {
        null => "—",
        JsonObject or JsonArray => "(vacío)",
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag ? "sí" : "no",
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString()
    };
}
