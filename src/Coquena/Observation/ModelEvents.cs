using System.Text;

namespace Coquena.Observation;

public sealed record ModelEvent(string Name, string Data, DateTimeOffset ObservedAt);
// Events invalidate a snapshot; payloads never become cross-build state contracts.
public static class ModelEvents
{
    public static Task ReadAsync(Stream stream, Action changed, CancellationToken cancellation) =>
        ReadAsync(stream, _ => changed(), cancellation);

    public static async Task ReadAsync(Stream stream, Action<ModelEvent> received, CancellationToken cancellation)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        var buffer = new char[4096]; var line = new StringBuilder();
        var data = false; var payload = new StringBuilder(); var name = "message";
        var size = 0; var carriageReturn = false; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
        {
            for (var i = 0; i < count; i++)
            {
                var ch = buffer[i];
                // Count UTF-8 bytes conservatively (surrogate pairs count as six).
                size += ch <= 0x7f ? 1 : ch <= 0x7ff ? 2 : 3;
                if (size > 1024 * 1024) throw new FormatException("Evento SSE excede 1 MiB.");
                if (ch == '\n' && carriageReturn) { carriageReturn = false; continue; }
                carriageReturn = ch == '\r';
                if (carriageReturn) ch = '\n';
                if (ch != '\n') { line.Append(ch); continue; }
                if (line.Length == 0)
                {
                    if (data) received(new(name, payload.ToString(), DateTimeOffset.UtcNow));
                    data = false; payload.Clear(); name = "message"; size = 0;
                }
                else
                {
                    var text = line.ToString(); var colon = text.IndexOf(':');
                    var field = colon < 0 ? text : text[..colon];
                    var value = colon < 0 ? "" : text[(colon + 1)..];
                    if (value.StartsWith(' ')) value = value[1..];
                    if (field == "data")
                    {
                        if (data) payload.Append('\n');
                        data = true; payload.Append(value);
                    }
                    else if (field == "event") name = value.Length == 0 ? "message" : value;
                }
                line.Clear();
            }
        }
        throw new IOException("El stream de eventos terminó; no se presupone replay.");
    }
}
