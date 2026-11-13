using System.Text.Json.Nodes;
using System.Threading.Channels;
using Coquena.Contracts;
using Coquena.Projects;
using Coquena.Recipes;

namespace Coquena.Observation;

public sealed record WatchSnapshot(ServiceSnapshot? Snapshot, string Events, string? Transition);
public sealed record ObservedModelEvent(string ServiceId, string InstanceId, ModelEvent Event);

// One worker per service; callbacks publish immutable snapshots for CLI/TUI/Web.
// Neither this class nor its transport owns any backend lifecycle operation.
public sealed class ServiceWatch(ScopeStore store, ServiceIdentity service, RecipeSelection? selection,
    ObserverPreferences preferences, IInstanceProbe? probe = null, HttpClient? client = null,
    Action<ObservedModelEvent>? eventReceived = null)
{
    public static TimeSpan RetryDelay(ObserverPreferences preferences, int failures) =>
        TimeSpan.FromSeconds(Math.Min(preferences.RetryMaxSeconds,
            preferences.RetryInitialSeconds * Math.Pow(2, Math.Min(30, Math.Max(0, failures)))));

    public async Task RunAsync(Action<WatchSnapshot> publish, CancellationToken cancellation, SemaphoreSlim? captureBudget = null)
    {
        var signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        string? instance = null; string events = "sin conexión"; string? transition = null;
        CancellationTokenSource? streamCancellation = null; Task? stream = null;
        using var ownedClient = client is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { Timeout = Timeout.InfiniteTimeSpan } : null;
        var http = client ?? ownedClient!;
        var observation = new ServiceObservation(store, http, probe);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var clock = Tick();
        var lastCapture = System.Diagnostics.Stopwatch.StartNew();
        var first = true;
        var reconcile = 1;
        signals.Writer.TryWrite(true);
        try
        {
            await foreach (var _ in signals.Reader.ReadAllAsync(cancellation))
            {
                if (!first && lastCapture.Elapsed < TimeSpan.FromSeconds(2))
                    await Task.Delay(TimeSpan.FromSeconds(2) - lastCapture.Elapsed, cancellation);
                first = false;
                if (captureBudget is not null) await captureBudget.WaitAsync(cancellation);
                ServiceSnapshot snapshot;
                try { snapshot = await observation.CaptureAsync(service, selection, cancellation, Interlocked.Exchange(ref reconcile, 0) != 0); }
                finally { captureBudget?.Release(); }
                lastCapture.Restart();
                if (stream?.IsCompleted == true)
                {
                    await stream;
                    streamCancellation?.Dispose(); streamCancellation = null; stream = null;
                }
                var live = snapshot.ProcessState == "alive" && snapshot.ApiState != "unverified-listener";
                if (snapshot.InstanceId != instance || !live)
                {
                    if (streamCancellation is not null)
                    {
                        await streamCancellation.CancelAsync();
                        if (stream is not null) await stream;
                        streamCancellation.Dispose(); streamCancellation = null; stream = null;
                    }
                    if (snapshot.InstanceId != instance)
                        transition = $"{Messages.NewInstanceSpan}: {instance ?? "ninguna"} → {snapshot.InstanceId ?? "ninguna"}; sin continuidad entre instancias.";
                    else if (!live) transition = "Instancia detenida, no verificable o terminada; esperando al mismo servicio.";
                    instance = snapshot.InstanceId;
                    Volatile.Write(ref events, "sin conexión: " + snapshot.ProcessState);
                }
                if (live && stream is null)
                {
                    streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    stream = Follow(instance!, streamCancellation.Token);
                }
                publish(new(snapshot, Volatile.Read(ref events), transition));
                transition = null;
            }
        }
        finally
        {
            await stop.CancelAsync();
            if (streamCancellation is not null) await streamCancellation.CancelAsync();
            try { if (stream is not null) await stream; await clock; }
            finally { streamCancellation?.Dispose(); }
        }

        async Task Tick()
        {
            try { while (await timer.WaitForNextTickAsync(stop.Token)) signals.Writer.TryWrite(true); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        async Task Follow(string expected, CancellationToken token)
        {
            var failures = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var state = store.ReadVerified();
                    var active = state.Documents.GetValueOrDefault(store.RuntimeDocumentPath(service.ServiceId, "active"))?["data"]?.AsObject();
                    if (active is null || active["instance-id"]?.GetValue<string>() != expected || active["terminated"]?.GetValue<bool>() == true) return;
                    var registered = Coquena.Servers.PresetPublication.Service(state, service.ServiceId);
                    if (registered["instance"]?["instance-id"]?.GetValue<string>() != expected ||
                        !JsonNode.DeepEquals(registered["instance"]?["process"], active["process"]))
                        throw new IOException("Registro e instancia no coinciden.");
                    using var process = (probe ?? InstanceProbe.Create()).Open(ServiceObservation.Identity(service.ServiceId, active)).Instance;
                    var endpoint = active["endpoint"]!;
                    var host = endpoint["host"]!.GetValue<string>(); var port = endpoint["port"]!.GetValue<int>();
                    if (process is null || !process.IsAlive || !process.OwnsEndpoint(host, port)) throw new IOException("Identidad/escucha no verificable.");
                    var configured = active["snapshot"]?["service"]?["server"]?["health"]?["url"]?["value"]?.GetValue<string>();
                    var scheme = Uri.TryCreate(configured, UriKind.Absolute, out var health) ? health.Scheme : "http";
                    if (scheme is not ("http" or "https")) throw new FormatException("Protocolo de observación inválido.");
                    var url = new UriBuilder(scheme, host == "0.0.0.0" ? "127.0.0.1" : host == "::" ? "::1" : host, port, "/models/sse").Uri;
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token);
                    requestTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") throw new FormatException("SSE no soportado por el endpoint.");
                    Volatile.Write(ref events, "conectado; reconciliando snapshot");
                    Interlocked.Exchange(ref reconcile, 1);
                    signals.Writer.TryWrite(true);
                    using var body = await response.Content.ReadAsStreamAsync(token);
                    await ModelEvents.ReadAsync(body, modelEvent =>
                    {
                        failures = 0;
                        eventReceived?.Invoke(new(service.ServiceId, expected, modelEvent));
                        Interlocked.Exchange(ref reconcile, 1); signals.Writer.TryWrite(true);
                    }, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception e) when (ServiceObservation.ObservationError(e))
                {
                    var delay = RetryDelay(preferences, failures++);
                    Volatile.Write(ref events, $"desconectado/no disponible: {e.GetType().Name}; reintento en {delay.TotalSeconds:0} s; hueco sin replay");
                    signals.Writer.TryWrite(true);
                    try { await Task.Delay(delay, token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                }
            }
        }
    }
}
