using System.Text;
using Coquena.Servers;

namespace Coquena.Observation;

// Thread-safe, bounded observer storage. View snapshots are separate from capture, so
// pausing or reading history never applies backpressure to the backend or UI thread.
internal sealed class LogBuffer : TextWriter
{
    internal const int MaximumRows = 2000, MaximumCharacters = 262144, MaximumLine = 8192;
    private readonly object gate = new();
    private readonly Queue<string> rows = new();
    private readonly StringBuilder pending = new();
    private int characters;
    private long discarded;
    private string? notice;
    public string? Notice { get { lock (gate) return notice; } }
    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));
    public override void WriteLine(string? value)
    {
        lock (gate)
        {
            if (value?.TrimStart().StartsWith("[Logs]", StringComparison.Ordinal) == true) notice = value.Trim();
            Write(value + "\n");
        }
    }
    public override void Write(string? value)
    {
        if (value is null) return;
        lock (gate)
        {
            foreach (var c in value)
            {
                if (c == '\n') { Commit(); continue; }
                if (c != '\t' && char.IsControl(c)) continue;
                pending.Append(c);
                // Segment long physical lines, preserving surrogate pairs at boundaries.
                if (pending.Length >= MaximumLine && (!char.IsHighSurrogate(c) || pending.Length > MaximumLine)) Commit();
            }
        }
    }

    private void Commit()
    {
        var row = pending.ToString(); pending.Clear();
        rows.Enqueue(row); characters += row.Length;
        while (rows.Count > MaximumRows || characters > MaximumCharacters)
        {
            characters -= rows.Dequeue().Length;
            discarded++;
        }
    }

    public LogFrame Read(bool filter)
    {
        lock (gate)
        {
            var visible = rows.Append(pending.ToString()).Where(row =>
                !filter || !row.Contains("proxying request to model ", StringComparison.Ordinal)).ToArray();
            return new(visible, discarded);
        }
    }
}

internal sealed record LogFrame(string[] Rows, long Discarded);

// Fixed registered service identity; neither a renamed default nor a reused name
// can redirect this observer. All file/catalog I/O stays off the terminal thread.
internal sealed class ServiceLogSession
{
    private readonly ServiceLogReader reader;
    private readonly CancellationTokenSource stop;
    private readonly LogBuffer live = new();
    private readonly object gate = new();
    private ServiceLog[] files = [];
    private string? problem, catalogProblem, currentInstance;
    private CancellationTokenSource? historyStop;
    private Task? historyTask;
    private LogBuffer? history;
    private string? historicalId;
    private string historicalState = "";
    public Task Completion { get; }

    public ServiceLogSession(ServiceLogReader reader, CancellationToken cancellation)
    {
        this.reader = reader;
        stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Completion = Task.WhenAll(Task.Run(Follow), Task.Run(Catalog));
    }

    private async Task Follow()
    {
        try { await reader.FollowAsync(live, stop.Token, log => { lock (gate) currentInstance = log.Id; }); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) when (ServiceObservation.ObservationError(error))
        { lock (gate) problem = "Desconectado · " + error.Message; }
    }

    private async Task Catalog()
    {
        while (!stop.IsCancellationRequested)
        {
            try { var list = reader.List(); lock (gate) { files = list; catalogProblem = null; } }
            catch (Exception error) when (ServiceObservation.ObservationError(error))
            { lock (gate) { catalogProblem = "Desconectado · " + error.Message; files = []; } }
            try { await Task.Delay(1000, stop.Token); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        }
    }

    public ServiceLog[] Files { get { lock (gate) return files.ToArray(); } }
    public string Status
    {
        get
        {
            lock (gate)
            {
                if (historicalId is not null) return $"Histórico · {historicalId[..Math.Min(8, historicalId.Length)]} · {historicalState}";
                var notice = live.Notice;
                if (notice?.StartsWith("[Logs] Servicio", StringComparison.Ordinal) == true)
                {
                    notice = currentInstance is { Length: >= 8 } ? "instancia " + currentInstance[..8] : null;
                }
                else notice = notice?.Replace("[Logs] ", "", StringComparison.Ordinal);
                return problem ?? catalogProblem ??
                    (files.Any(file => file.Active) ? "Siguiendo" : "Espera · detenido") + " · " + notice;
            }
        }
    }
    public bool Historical { get { lock (gate) return historicalId is not null; } }
    public LogFrame Read(bool filter) { lock (gate) return (history ?? live).Read(filter); }

    public void Current()
    {
        lock (gate) { historyStop?.Cancel(); history = null; historicalId = null; }
    }

    public void SelectHistory(string id)
    {
        lock (gate)
        {
            historyStop?.Cancel();
            var cancellation = historyStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            var buffer = history = new LogBuffer();
            historicalId = id; historicalState = "cargando";
            var previous = historyTask ?? Task.CompletedTask;
            historyTask = Task.Run(async () =>
            {
                // At most one historical read runs per service, even with rapid selections.
                await previous;
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    reader.Recent(buffer, id, cancellation.Token);
                    lock (gate) if (history == buffer) historicalState = "lectura fija completada";
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception error) when (ServiceObservation.ObservationError(error))
                { lock (gate) if (history == buffer) historicalState = "No disponible · " + error.Message; }
                finally { lock (gate) if (historyStop == cancellation) historyStop = null; cancellation.Dispose(); }
            });
        }
    }

    public async Task CloseAsync()
    {
        stop.Cancel();
        Task? pending;
        lock (gate) { historyStop?.Cancel(); pending = historyTask; }
        await Completion;
        if (pending is not null) await pending;
        stop.Dispose();
    }
}
