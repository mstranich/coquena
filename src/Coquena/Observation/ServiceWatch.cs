using System.Text.Json.Nodes;
using System.Threading.Channels;
using Coquena.Contracts;
using Coquena.Projects;
using Coquena.Recipes;

namespace Coquena.Observation;

public sealed record WatchSnapshot(ServiceSnapshot? Snapshot, MessageData Events, string? Transition);
public sealed record ObservedModelEvent(string ServiceId, string InstanceId, ModelEvent Event);

// One worker per service; callbacks publish immutable snapshots for CLI/TUI/Web.
// Neither this class nor its transport owns any backend lifecycle operation.
public sealed class ServiceWatch(ScopeStore store, ServiceIdentity service, RecipeSelection? selection,
    ObserverPreferences preferences, IInstanceProbe? probe = null, HttpClient? client = null,
    Action<ObservedModelEvent>? eventReceived = null, bool observeInference = false,
    TimeSpan? interval = null)
{
    public static TimeSpan RetryDelay(ObserverPreferences preferences, int failures) =>
        TimeSpan.FromSeconds(Math.Min(preferences.RetryMaxSeconds,
            preferences.RetryInitialSeconds * Math.Pow(2, Math.Min(30, Math.Max(0, failures)))));

    public async Task RunAsync(Action<WatchSnapshot> publish, CancellationToken cancellation, SemaphoreSlim? captureBudget = null)
    {
        var signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        string? instance = null; MessageData events = Messages.ObservationDisconnected2.Data(); string? transition = null;
        CancellationTokenSource? streamCancellation = null; Task? stream = null;
        using var ownedClient = client is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { Timeout = Timeout.InfiniteTimeSpan } : null;
        var http = client ?? ownedClient!;
        var observation = new ServiceObservation(store, http, probe);
        // Two seconds by default; a caller that shows a single service may ask for a faster pace.
        var period = interval ?? TimeSpan.FromSeconds(2);
        using var timer = new PeriodicTimer(period);
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
                if (!first && lastCapture.Elapsed < period)
                    await Task.Delay(period - lastCapture.Elapsed, cancellation);
                first = false;
                if (captureBudget is not null) await captureBudget.WaitAsync(cancellation);
                ServiceSnapshot snapshot;
                try { snapshot = await observation.CaptureAsync(service, selection, cancellation, Interlocked.Exchange(ref reconcile, 0) != 0, observeInference); }
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
                        transition = Messages.ObservationNoContinuityBetweenInstances.Format(Messages.NewInstanceSpan, instance ?? "ninguna", snapshot.InstanceId ?? "ninguna");
                    else if (!live) transition = Messages.ObservationWaitingSameService.ToString();
                    instance = snapshot.InstanceId;
                    Volatile.Write(ref events, Messages.WatchProcessDisconnected.Data(snapshot.ProcessState));
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
                        throw new IOException(Messages.ObservationRegistryAndInstanceDoNotMatch.ToString());
                    using var process = (probe ?? InstanceProbe.Create()).Open(ServiceObservation.Identity(service.ServiceId, active)).Instance;
                    var endpoint = active["endpoint"]!;
                    var host = endpoint["host"]!.GetValue<string>(); var port = endpoint["port"]!.GetValue<int>();
                    if (process is null || !process.IsAlive || !process.OwnsEndpoint(host, port)) throw new IOException(Messages.ObservationIdentityOrListenerUnverified.ToString());
                    var configured = active["snapshot"]?["service"]?["server"]?["health"]?["url"]?["value"]?.GetValue<string>();
                    var scheme = Uri.TryCreate(configured, UriKind.Absolute, out var health) ? health.Scheme : "http";
                    if (scheme is not ("http" or "https")) throw new FormatException(Messages.ObservationInvalidObservationProtocol.ToString());
                    var url = new UriBuilder(scheme, host == "0.0.0.0" ? "127.0.0.1" : host == "::" ? "::1" : host, port, "/models/sse").Uri;
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token);
                    requestTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") throw new FormatException(Messages.ObservationSSEUnsupportedByEndpoint.ToString());
                    Volatile.Write(ref events, Messages.ObservationConnectedReconcilingSnapshot.Data());
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
                    Volatile.Write(ref events, Messages.ObservationDisconnectedRetryGap.Data(e.GetType().Name, delay.TotalSeconds));
                    signals.Writer.TryWrite(true);
                    try { await Task.Delay(delay, token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                }
            }
        }
    }
}
