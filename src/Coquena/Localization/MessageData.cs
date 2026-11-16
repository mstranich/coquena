using System.Globalization;

namespace Coquena;

// A durable diagnostic contains an invariant key and arguments, never rendered text.
public sealed record MessageData(string Code, string[] Arguments, MessageData? Cause = null)
{
    public static string FromNode(System.Text.Json.Nodes.JsonNode node) => node is System.Text.Json.Nodes.JsonObject
        ? System.Text.Json.JsonSerializer.Deserialize(node, Contracts.ContractJsonContext.Default.MessageData)?.ToString() ?? ""
        : node.GetValue<string>();
    public override string ToString()
    {
        var text = Localization.Find(Code);
        var rendered = text is null ? Code : text.Format(Arguments.Cast<object?>().ToArray());
        return Cause is null ? rendered : Cause + " " + rendered;
    }

    public static implicit operator MessageData(string code) => new(code, []);
    public static implicit operator string(MessageData message) => message.ToString();

    public bool Equals(MessageData? other) => other is not null && Code == other.Code && Arguments.SequenceEqual(other.Arguments) && Equals(Cause, other.Cause);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Code);
        hash.Add(Cause);
        foreach (var argument in Arguments) hash.Add(argument);
        return hash.ToHashCode();
    }

    internal static MessageData Create(Text text, object?[] values) => new(text.Key,
        values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").ToArray());
}
