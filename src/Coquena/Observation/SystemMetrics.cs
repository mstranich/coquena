namespace Coquena.Observation;

// Values are observations, never defaults for missing telemetry. Scope is part of
// the identity: device totals must not be copied onto individual services.
public sealed record MetricSample(string Name, double? Value, string Unit, string Source,
    string Scope, DateTimeOffset ObservedAt, TimeSpan? Window = null, string? UnavailableReason = null)
{
    public bool IsFresh(DateTimeOffset now, TimeSpan maxAge) => now >= ObservedAt && now - ObservedAt <= maxAge;
}
public sealed record DeviceMetrics(string Id, string Name, IReadOnlyList<MetricSample> Samples);
public sealed record HardwareSnapshot(string Environment, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    IReadOnlyList<MetricSample> Samples, IReadOnlyList<DeviceMetrics> Devices, IReadOnlyList<string> Diagnostics);

public interface ISystemMetrics
{
    Task<HardwareSnapshot> CaptureAsync(CancellationToken cancellation = default);
}

public static class SystemMetrics
{
    public static ISystemMetrics Create() => OperatingSystem.IsWindows() ? new WindowsSystemMetrics() :
        OperatingSystem.IsLinux() ? new LinuxSystemMetrics() : new UnsupportedSystemMetrics();

    private sealed class UnsupportedSystemMetrics : ISystemMetrics
    {
        public Task<HardwareSnapshot> CaptureAsync(CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new HardwareSnapshot("unsupported", now, now, [], [], ["Sistema operativo sin proveedor de métricas."]));
        }
    }
}
