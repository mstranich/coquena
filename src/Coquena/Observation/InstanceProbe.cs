using Coquena.Contracts;
using Coquena.Servers;

namespace Coquena.Observation;

// Opening returns a verified process session. Its lifetime pins the process
// object, so PID reuse cannot turn an existing observation into another process.
public interface IInstanceProbe
{
    InstanceCheck Open(InstanceDocument identity);
}
public sealed record InstanceCheck(string State, IObservedInstance? Instance, string? Diagnostic = null);
public interface IObservedInstance : IDisposable
{
    bool IsAlive { get; }
    bool OwnsEndpoint(string host, int port);
    (int Pid, long Creation)? ObserveListener(string host, int port) => null;
    Task<IReadOnlyList<MetricSample>> CaptureAsync(CancellationToken cancellation);
}
public static class InstanceProbe
{
    public static IInstanceProbe Create() => OperatingSystem.IsWindows() ? new WindowsInstanceProbe() : new UnsupportedInstanceProbe();
    private sealed class UnsupportedInstanceProbe : IInstanceProbe
    {
        public InstanceCheck Open(InstanceDocument identity) => new("unsupported", null,
            "El formato persistido de identidad de servicios requiere Windows; el proveedor de hardware es independiente.");
    }
}

internal sealed class WindowsInstanceProbe : IInstanceProbe
{
    public InstanceCheck Open(InstanceDocument identity)
    {
        var handle = WindowsServerProcess.Verified(identity, out var ended);
        return ended ? new("ended-or-reused", null) : new("alive", new Session(identity, handle!));
    }
    private sealed class Session(InstanceDocument identity, Microsoft.Win32.SafeHandles.SafeFileHandle handle) : IObservedInstance
    {
        public bool IsAlive => !WindowsServerProcess.Exited(handle);
        public bool OwnsEndpoint(string host, int port) => WindowsListeningPorts.IsOwned(host, port, identity.InstanceId);
        public (int Pid, long Creation)? ObserveListener(string host, int port) =>
            WindowsListeningPorts.FindOwned(host, port, identity.InstanceId, includeCreation: true);
        public async Task<IReadOnlyList<MetricSample>> CaptureAsync(CancellationToken cancellation)
        {
            var clock = System.Diagnostics.Stopwatch.GetTimestamp();
            var first = WindowsServerProcess.ReadInstanceMetrics(identity);
            await Task.Delay(250, cancellation);
            var second = WindowsServerProcess.ReadInstanceMetrics(identity);
            var window = System.Diagnostics.Stopwatch.GetElapsedTime(clock); var now = DateTimeOffset.UtcNow;
            return [
                new("cpu.utilization", second.CpuTicks >= first.CpuTicks ?
                    (second.CpuTicks - first.CpuTicks) / (window.TotalSeconds * TimeSpan.TicksPerSecond * Environment.ProcessorCount) * 100 : null,
                    "%", "Windows job accounting; normalized to logical CPUs", "instance:" + identity.InstanceId, now, window,
                    second.CpuTicks >= first.CpuTicks ? null : "Contador reiniciado."),
                new("ram.private-commit", second.PrivateBytes, "bytes", "GetProcessMemoryInfo; verified job members PrivateUsage",
                    "instance:" + identity.InstanceId, now, UnavailableReason: second.PrivateBytes is null ? "Miembros cambiaron o acceso no disponible; sin suma parcial." : null),
                new("gpu.utilization", null, "%", "OS", "instance:" + identity.InstanceId, now,
                    UnavailableReason: "Sin fuente acreditada por instancia; consultar hardware compartido por dispositivo."),
                new("vram.used", null, "bytes", "OS", "instance:" + identity.InstanceId, now,
                    UnavailableReason: "Sin fuente acreditada por instancia; consultar hardware compartido por dispositivo."),
                .. second.WorkingSets ];
        }
        public void Dispose() => handle.Dispose();
    }
}
