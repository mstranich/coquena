using System.Text;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Servers;

public sealed record ServiceLog(string Id, string ServiceId, string Path, string StartedAt, bool Active);

// Independent observer, reusable by visual interfaces; never signals a backend process.
public sealed class ServiceLogReader(ScopeStore store, string serviceId)
{
    public ServiceLog[] List()
    {
        var state = store.Read();
        var service = PresetPublication.Service(state, serviceId);
        var active = state.Documents.GetValueOrDefault(store.RuntimeDocumentPath(serviceId, "active"))?["data"];
        var activeId = active?["terminated"]?.GetValue<bool>() != true ? active?["instance-id"]?.GetValue<string>() : null;
        return (service["log-history"]?.AsArray() ?? []).Select(e => new ServiceLog(
            e!["instance-id"]!.GetValue<string>(), serviceId, e["path"]!.GetValue<string>(),
            e["started-at"]!.GetValue<string>(), e["instance-id"]!.GetValue<string>() == activeId)).ToArray();
    }

    public void Recent(TextWriter output, string? id = null, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var logs = List();
        var log = id is null ? logs.LastOrDefault() : logs.SingleOrDefault(l => l.Id == id)
            ?? throw new StorageException("unknown-log", "Log no registrado en este servicio.");
        if (log is null) { output.WriteLine("Sin logs registrados."); return; }
        using var stream = Open(log.Path);
        var end = stream.Length; // A query never follows later writes, including show of an active file.
        Position(stream, id is null);
        var remaining = end - stream.Position;
        var bytes = new byte[4096]; var chars = new char[4097];
        var decoder = Encoding.UTF8.GetDecoder();
        while (remaining > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = stream.Read(bytes, 0, (int)Math.Min(remaining, bytes.Length));
            if (count == 0) break;
            remaining -= count;
            var length = decoder.GetChars(bytes, 0, count, chars, 0, remaining == 0);
            output.Write(chars, 0, length);
        }
    }

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    private static void Position(FileStream stream, bool recent)
    {
        var offset = recent ? Math.Max(0, stream.Length - 65536) : 0;
        stream.Position = offset;
        if (offset > 0)
        {
            // Skip a partial UTF-8 sequence at the bounded tail boundary.
            int value;
            while ((value = stream.ReadByte()) >= 0 && (value & 0xc0) == 0x80) { }
            if (value >= 0) stream.Position--;
        }
        else
        {
            if (stream.ReadByte() != 0xef || stream.ReadByte() != 0xbb || stream.ReadByte() != 0xbf)
                stream.Position = 0;
        }
    }

    public async Task FollowAsync(TextWriter output, CancellationToken cancellation, Action<ServiceLog>? transition = null)
    {
        FileStream? stream = null;
        string? current = null, diagnostic = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var bytes = new byte[4096]; var chars = new char[4097];
        var decoder = Encoding.UTF8.GetDecoder();
        long offset = 0;
        var first = true;
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var logs = List(); // A reused name or changed default cannot redirect this observer.
                    seen.IntersectWith(logs.Select(l => l.Id));
                    if (stream is not null)
                    {
                        if (stream.Length < offset)
                        {
                            output.WriteLine("\n[Logs] Archivo truncado.");
                            stream.Position = offset = 0; decoder.Reset();
                        }
                        var eof = false;
                        for (var chunk = 0; chunk < 16; chunk++)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            var count = stream.Read(bytes, 0, bytes.Length);
                            if (count == 0) { eof = true; break; }
                            offset = stream.Position;
                            // Keep the decoder across EOF polls: a concurrent UTF-8 write may be incomplete.
                            var length = decoder.GetChars(bytes, 0, count, chars, 0, flush: false);
                            output.Write(chars, 0, length);
                        }
                        if (!eof)
                        {
                            output.Flush();
                            await Task.Delay(25, cancellation);
                            continue;
                        }
                    }
                    var next = current is null && first ? logs.LastOrDefault() : logs.FirstOrDefault(l => !seen.Contains(l.Id));
                    if (next is null && stream is null && current is not null)
                        next = logs.SingleOrDefault(l => l.Id == current);
                    if (next is not null)
                    {
                        stream?.Dispose(); stream = null;
                        if (next.Id != current)
                        {
                            var length = decoder.GetChars([], 0, 0, chars, 0, flush: true);
                            output.Write(chars, 0, length);
                            decoder.Reset(); offset = 0; current = next.Id;
                        }
                        stream = Open(next.Path);
                        if (first) Position(stream, recent: true);
                        else stream.Position = offset;
                        offset = stream.Position;
                        output.WriteLine($"\n[Logs] Servicio {serviceId}; instancia {next.Id}; inicio {next.StartedAt}.");
                        transition?.Invoke(next);
                        if (first) foreach (var log in logs.TakeWhile(l => l.Id != next.Id)) seen.Add(log.Id);
                        seen.Add(next.Id); first = false;
                        diagnostic = null;
                        continue;
                    }
                    first = false;
                    if (current is null && diagnostic != "waiting")
                    { output.WriteLine("[Logs] Esperando la primera ejecución del servicio."); diagnostic = "waiting"; }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (diagnostic != e.GetType().Name)
                    { output.WriteLine("\n[Logs] Archivo no disponible; se reintentará sin controlar el servicio."); diagnostic = e.GetType().Name; }
                    stream?.Dispose(); stream = null;
                    if (current is not null) seen.Add(current);
                }
                output.Flush();
                await Task.Delay(250, cancellation);
            }
        }
        finally { stream?.Dispose(); }
    }
}
