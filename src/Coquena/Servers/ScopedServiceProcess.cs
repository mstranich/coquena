using System.Text.Json.Nodes;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Servers;

public sealed record ScopedStartPlan(string Executable, string[] Arguments, string LogPath,
    string Combination, JsonObject Snapshot, JsonArray Resources, JsonObject Endpoint,
    Uri HealthUrl, TimeSpan Timeout, TimeSpan Interval, Guid? InstanceId = null, Action? ValidateIntent = null);

// Lifecycle leases are per service and separate from the short catalog writer lease.
public sealed class ScopedServiceProcess(ScopeStore store, TextWriter? output = null)
{
    private FileStream Lease(string serviceId)
    {
        if (!Guid.TryParseExact(serviceId, "D", out var id) || id == Guid.Empty || id.ToString("D") != serviceId)
            throw new StorageException("invalid-service-id", "Identidad de servicio inválida.");
        var directory = store.Context.PathFor($"local/runtime/{serviceId}");
        Directory.CreateDirectory(directory);
        try { return new FileStream(Path.Combine(directory, "lifecycle.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new StorageException("service-busy", "Otra operación controla el servicio.", inner: e); }
    }

    public bool Reconcile(string serviceId)
    {
        using var lease = Lease(serviceId);
        return ReconcileLocked(serviceId);
    }

    private bool ReconcileLocked(string serviceId)
    {
        var path = store.RuntimeDocumentPath(serviceId, "active");
        var snapshot = store.Read();
        var active = snapshot.Documents.GetValueOrDefault(path)?["data"]?.AsObject();
        var registered = snapshot.Documents.GetValueOrDefault("local/recipes.json")?["data"]?["recipes"]?.AsArray()
            .SelectMany(recipe => recipe!["entities"]!.AsArray())
            .SingleOrDefault(entity => entity!["kind"]!.GetValue<string>() == "service" && entity["id"]!.GetValue<string>() == serviceId);
        if (registered is null) throw new StorageException("unknown-service", "Servicio no registrado.");
        var recorded = registered["instance"];
        if (active is null || active["terminated"]?.GetValue<bool>() == true)
        {
            if (recorded is not null) throw new StorageException("instance-conflict", "El registro conserva una instancia sin evidencia activa coherente.");
            return false;
        }
        if (recorded?["instance-id"]?.GetValue<string>() != active["instance-id"]?.GetValue<string>() ||
            !JsonNode.DeepEquals(recorded?["process"], active["process"]))
            throw new StorageException("instance-conflict", "Las identidades de instancia no coinciden; se conserva evidencia.");
        var instance = Identity(serviceId, active);
        using var process = WindowsServerProcess.Verified(instance, out var ended);
        if (!ended) return true;
        WindowsServerProcess.RequireGroupEnded(instance.InstanceId);
        new RecipeRegistry(store).RecordTermination(serviceId, instance.InstanceId.ToString("D"));
        return false;
    }

    public void Remove(string serviceId)
    {
        using var lease = Lease(serviceId);
        if (ReconcileLocked(serviceId)) throw new StorageException("service-in-use", "El servicio está ejecutándose.");
        new RecipeRegistry(store).RemoveStoppedService(serviceId);
    }

    public bool Stop(string serviceId, Func<bool>? forceRequested = null, string? expectedInstanceId = null)
    {
        using var lease = Lease(serviceId);
        return StopLocked(serviceId, forceRequested, expectedInstanceId);
    }

    private bool StopLocked(string serviceId, Func<bool>? forceRequested, string? expectedInstanceId = null)
    {
        if (!ReconcileLocked(serviceId)) return false;
        var path = store.RuntimeDocumentPath(serviceId, "active");
        var active = store.Read().Documents[path]["data"]!.AsObject();
        var instance = Identity(serviceId, active);
        if (expectedInstanceId is not null && instance.InstanceId.ToString("D") != expectedInstanceId)
            throw new StorageException("instance-conflict", "La instancia cambió; no se detiene su reemplazo.");
        using var process = WindowsServerProcess.Verified(instance, out var ended);
        if (!ended)
        {
            using var group = WindowsServerProcess.VerifiedGroup(instance, process!);
            output?.WriteLine("[Deteniendo] Parada ordenada; espera máxima 10 s.");
            var command = ServerRuntime.Self("--internal-scoped-signal", instance.InstanceId.ToString("D"),
                instance.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                instance.ProcessCreated!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), instance.ExecutablePath!);
            using var signal = System.Diagnostics.Process.Start(command) ?? throw new ConfigurationException("No se pudo solicitar la parada ordenada.");
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var rootEnded = false;
            while (true)
            {
                if (!rootEnded && WindowsServerProcess.Exited(process!))
                {
                    process!.Dispose();
                    rootEnded = true;
                }
                if (rootEnded && WindowsServerProcess.GroupExited(group)) break;
                if (forceRequested?.Invoke() == true || elapsed.Elapsed >= TimeSpan.FromSeconds(10))
                {
                    output?.WriteLine("[Cierre forzado] Terminando el grupo de procesos de la instancia.");
                    WindowsServerProcess.ForceGroup(group);
                    process!.Dispose();
                    var confirmation = System.Diagnostics.Stopwatch.StartNew();
                    while (!WindowsServerProcess.GroupExited(group) && confirmation.Elapsed < TimeSpan.FromSeconds(3)) Thread.Sleep(25);
                    if (!WindowsServerProcess.GroupExited(group))
                        throw new ConfigurationException("No se confirmó la terminación; se conserva el registro.");
                    break;
                }
                Thread.Sleep(25);
            }
            if (!signal.WaitForExit(1000)) { signal.Kill(); signal.WaitForExit(); }
            if (signal.ExitCode != 0) output?.WriteLine("[Aviso] La señal ordenada no pudo confirmarse; se comprobó la terminación del proceso.");
        }
        else WindowsServerProcess.RequireGroupEnded(instance.InstanceId);
        new RecipeRegistry(store).RecordTermination(serviceId, instance.InstanceId.ToString("D"));
        return true;
    }

    public static int SignalWorker(string instanceId, string pid, string creation, string executable)
    {
        if (!Guid.TryParseExact(instanceId, "D", out var id) || id == Guid.Empty ||
            !int.TryParse(pid, out var processId) || processId <= 0 || !long.TryParse(creation, out var started) || started <= 0 ||
            !Path.IsPathFullyQualified(executable)) return 1;
        var instance = new InstanceDocument(1, id, Guid.Empty, Guid.Empty, "", processId, "detached", "", 0,
            false, [], [], "", "", started, executable);
        return WindowsServerProcess.Signal(instance);
    }

    public async Task<string> StartAsync(string serviceId, ScopedStartPlan plan, CancellationToken cancellation = default, Func<bool>? forceRequested = null)
    {
        ValidatePlan(plan);
        using var lease = Lease(serviceId);
        ValidateBindings(plan);
        return await StartLockedAsync(serviceId, plan, cancellation, forceRequested);
    }

    public async Task<string> RestartAsync(string serviceId, Func<CancellationToken, Task<ScopedStartPlan>> prepare,
        Func<bool>? forceRequested = null, CancellationToken cancellation = default)
    {
        using var lease = Lease(serviceId);
        var plan = await prepare(cancellation);
        ValidatePlan(plan);
        ValidateBindings(plan);
        plan.ValidateIntent?.Invoke();
        cancellation.ThrowIfCancellationRequested();
        StopLocked(serviceId, forceRequested);
        return await StartLockedAsync(serviceId, plan, cancellation, forceRequested);
    }

    private static void ValidatePlan(ScopedStartPlan plan)
    {
        if (!Path.IsPathFullyQualified(plan.Executable) || !Path.IsPathFullyQualified(plan.LogPath) ||
            !plan.HealthUrl.IsAbsoluteUri || plan.HealthUrl.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(plan.HealthUrl.UserInfo) || plan.Timeout <= TimeSpan.Zero ||
            plan.Interval < TimeSpan.FromMilliseconds(100) || plan.Interval > TimeSpan.FromSeconds(10))
            throw new StorageException("invalid-start-plan", "El plan de inicio o salud es inválido.");
        if (!File.Exists(plan.Executable)) throw new StorageException("backend-missing", "El ejecutable del backend no existe.");
        var host = plan.Endpoint["host"]?.GetValue<string>();
        var port = plan.Endpoint["port"]?.GetValue<int>();
        if (string.IsNullOrEmpty(host) || port is not (> 0 and <= 65535))
            throw new StorageException("invalid-endpoint", "El inicio requiere host y puerto válidos.");
    }

    private void ValidateBindings(ScopedStartPlan plan) => RecipeRegistry.ValidateRuntimeBindings(store.Read(), plan.Resources);

    private async Task<string> StartLockedAsync(string serviceId, ScopedStartPlan plan, CancellationToken cancellation, Func<bool>? forceRequested)
    {
        if (ReconcileLocked(serviceId)) throw new StorageException("service-already-running", "El servicio ya está ejecutándose.");
        var host = plan.Endpoint["host"]?.GetValue<string>();
        var port = plan.Endpoint["port"]?.GetValue<int>();
        cancellation.ThrowIfCancellationRequested();
        plan.ValidateIntent?.Invoke();
        ServerRuntime.ProbePort(host!, port!.Value);
        Directory.CreateDirectory(Path.GetDirectoryName(plan.LogPath)!);
        var id = plan.InstanceId ?? Guid.NewGuid();
        if (id == Guid.Empty) throw new StorageException("invalid-instance-id", "Identidad de instancia inválida.");
        using var child = WindowsServerProcess.Create(plan.Executable, plan.Arguments, plan.LogPath, id, newLog: true);
        var active = new JsonObject { ["service-id"] = serviceId, ["instance-id"] = id.ToString("D"),
            ["process"] = new JsonObject { ["pid"] = child.Pid, ["creation-time"] = child.Created, ["executable"] = child.Executable },
            ["combination"] = plan.Combination, ["snapshot"] = plan.Snapshot.DeepClone(), ["resources"] = plan.Resources.DeepClone(),
            ["endpoint"] = plan.Endpoint.DeepClone(), ["log"] = plan.LogPath,
            ["execution"] = new JsonObject { ["executable"] = plan.Executable,
                ["arguments"] = new JsonArray(plan.Arguments.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["working-directory"] = Path.GetDirectoryName(plan.Executable) } };
        var registry = new RecipeRegistry(store);
        registry.PublishActive(serviceId, active, plan.ValidateIntent);
        child.Activate();
        var instance = Identity(serviceId, active);
        using var process = WindowsServerProcess.Verified(instance, out var ended);
        if (ended) { RecordEnded(serviceId, id.ToString("D")); throw new StorageException("process-exited", "El proceso terminó durante el inicio."); }
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string diagnostic = "sin respuesta";
        try
        {
            while (clock.Elapsed < plan.Timeout)
            {
                cancellation.ThrowIfCancellationRequested();
                if (WindowsServerProcess.Exited(process!))
                { RecordEnded(serviceId, id.ToString("D")); throw new StorageException("process-exited", "El proceso terminó durante el inicio."); }
                try
                {
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    requestTimeout.CancelAfter(plan.Timeout - clock.Elapsed);
                    using var response = await client.GetAsync(plan.HealthUrl, requestTimeout.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        if (WindowsServerProcess.Exited(process!))
                        { RecordEnded(serviceId, id.ToString("D")); throw new StorageException("process-exited", "El proceso terminó durante el inicio."); }
                        if (WindowsListeningPorts.IsOwned(host!, port.Value, id))
                        {
                            registry.PromoteHealthy(serviceId, id.ToString("D"), plan.HealthUrl, (int)response.StatusCode);
                            return id.ToString("D");
                        }
                        diagnostic = "la escucha no corresponde al grupo de la instancia";
                    }
                    else diagnostic = $"HTTP {(int)response.StatusCode}";
                }
                catch (HttpRequestException) { if (diagnostic == "sin respuesta") diagnostic = "conexión no disponible"; }
                catch (TaskCanceledException) when (!cancellation.IsCancellationRequested) { if (diagnostic == "sin respuesta") diagnostic = "plazo de consulta agotado"; }
                var remaining = plan.Timeout - clock.Elapsed;
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining < plan.Interval ? remaining : plan.Interval, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StopLocked(serviceId, forceRequested, id.ToString("D"));
            throw;
        }
        throw new StorageException("health-timeout", $"No se confirmó salud ({diagnostic}); se conserva la instancia para reconciliación.");
    }

    private void RecordEnded(string serviceId, string instanceId)
    {
        WindowsServerProcess.RequireGroupEnded(Guid.ParseExact(instanceId, "D"));
        new RecipeRegistry(store).RecordTermination(serviceId, instanceId);
    }

    public int Foreground(string serviceId, string instanceId, TextWriter output, ServerInterrupt interrupt)
    {
        var path = store.RuntimeDocumentPath(serviceId, "active");
        var active = store.Read().Documents[path]["data"]!.AsObject();
        var instance = Identity(serviceId, active);
        if (instance.InstanceId.ToString("D") != instanceId) throw new StorageException("instance-conflict", "La instancia cambió.");
        using var process = WindowsServerProcess.Verified(instance, out var ended);
        using var stream = new FileStream(active["log"]!.GetValue<string>(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var buffer = new char[4096];
        output.WriteLine("[Primer plano] Ctrl+C: parada ordenada; segundo Ctrl+C: cierre forzado.");
        while (!ended && !WindowsServerProcess.Exited(process!))
        {
            Drain(16);
            if (interrupt.Count > 0)
            {
                Stop(serviceId, () => interrupt.Count > 1, instanceId);
                Drain(int.MaxValue);
                return 0;
            }
            WindowsServerProcess.Exited(process!, 50);
        }
        Drain(int.MaxValue);
        var code = process is null ? 0 : WindowsServerProcess.ExitCode(process);
        Reconcile(serviceId);
        return code == 0 ? 0 : 1;

        void Drain(int chunks)
        {
            while (chunks-- > 0)
            {
                var count = reader.Read(buffer, 0, buffer.Length);
                if (count == 0) break;
                output.Write(buffer, 0, count);
            }
            output.Flush();
        }
    }

    private static InstanceDocument Identity(string serviceId, JsonObject active)
    {
        try
        {
            if (active["service-id"]?.GetValue<string>() != serviceId) throw new FormatException();
            var process = active["process"]!.AsObject();
            var instanceId = Guid.ParseExact(active["instance-id"]!.GetValue<string>(), "D");
            var pid = process["pid"]!.GetValue<int>();
            var created = process["creation-time"]!.GetValue<long>();
            var executable = process["executable"]!.GetValue<string>();
            if (instanceId == Guid.Empty || pid <= 0 || created <= 0 || !Path.IsPathFullyQualified(executable)) throw new FormatException();
            return new(1, instanceId, Guid.ParseExact(serviceId, "D"), Guid.Empty, "", pid,
                "detached", "", 0, false, [], [], "", "", created, executable);
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or NullReferenceException or ArgumentException)
        { throw new ConfigurationException("Identidad de proceso incompleta; se conserva el registro y no se actúa sobre el PID."); }
    }
}
