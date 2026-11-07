using System.Runtime.InteropServices;

namespace Coquena.Observation;

// PDH's English API avoids dependence on the user's display language. No child
// process, WMI, vendor utility or managed performance-counter package is used.
public sealed partial class WindowsSystemMetrics : ISystemMetrics
{
    internal static double? BusiestEngine(IReadOnlyList<double> engines) =>
        engines.Count == 0 || engines.Any(value => !double.IsFinite(value) || value is < 0 or > 100)
            ? null : engines.Max();

    public async Task<HardwareSnapshot> CaptureAsync(CancellationToken cancellation = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var start = DateTimeOffset.UtcNow;
        var first = Times();
        using var gpu = new GpuQuery();
        await Task.Delay(250, cancellation);
        var second = Times();
        var now = DateTimeOffset.UtcNow;
        var samples = new List<MetricSample>();
        var total = second.Total - first.Total;
        samples.Add(new("cpu.utilization", first.Valid && second.Valid && total > 0 && second.Idle >= first.Idle
            ? 100.0 * (total - (second.Idle - first.Idle)) / total : null, "%", "GetSystemTimes", "machine", now,
            now - start, total > 0 && first.Valid && second.Valid ? null : "No se pudo medir el intervalo de CPU."));
        var memory = new Memory { Length = (uint)Marshal.SizeOf<Memory>() };
        var ok = GlobalMemoryStatusEx(ref memory) != 0;
        samples.Add(new("ram.total", ok ? memory.TotalPhysical : null, "bytes", "GlobalMemoryStatusEx", "machine", now,
            UnavailableReason: ok ? null : "API de memoria no disponible."));
        samples.Add(new("ram.used", ok ? memory.TotalPhysical - memory.AvailablePhysical : null, "bytes", "GlobalMemoryStatusEx", "machine", now,
            UnavailableReason: ok ? null : "API de memoria no disponible."));
        var devices = gpu.Read(now, now - start);
        return new("windows", start, DateTimeOffset.UtcNow, samples, devices, gpu.Diagnostics);
    }

    private static (bool Valid, ulong Idle, ulong Total) Times()
    {
        var ok = GetSystemTimes(out var idle, out var kernel, out var user) != 0;
        return (ok, idle, kernel + user);
    }

    private sealed class GpuQuery : IDisposable
    {
        private nint query;
        private readonly Dictionary<string, nint> counters = [];
        public List<string> Diagnostics { get; } = [];
        public GpuQuery()
        {
            if (PdhOpenQueryW(0, 0, out query) != 0) { Diagnostics.Add("PDH GPU no disponible."); return; }
            foreach (var (name, path) in new[]
            {
                ("gpu.engine", @"\GPU Engine(*)\Utilization Percentage"),
                ("vram.used", @"\GPU Adapter Memory(*)\Dedicated Usage"),
                ("gpu.shared", @"\GPU Adapter Memory(*)\Shared Usage")
            })
            {
                if (PdhAddEnglishCounterW(query, path, 0, out var counter) == 0) counters[name] = counter;
                else Diagnostics.Add($"PDH: contador {name} no disponible.");
            }
            _ = PdhCollectQueryData(query);
        }
        public DeviceMetrics[] Read(DateTimeOffset now, TimeSpan window)
        {
            var collected = query != 0 && PdhCollectQueryData(query) == 0;
            if (!collected) Diagnostics.Add("PDH: lectura GPU no disponible.");
            var devices = new Dictionary<string, List<MetricSample>>(StringComparer.OrdinalIgnoreCase);
            var engines = new Dictionary<(string Device, string Engine), double>();
            foreach (var (metric, counter) in counters)
            {
                if (!collected) break;
                var items = ReadArray(counter);
                if (items is null) { Diagnostics.Add($"PDH: muestras {metric} no disponibles."); continue; }
                foreach (var (instance, value) in items)
                {
                    var at = instance.IndexOf("luid_", StringComparison.Ordinal);
                    var end = instance.IndexOf("_phys_", StringComparison.Ordinal);
                    if (at < 0 || end < at) continue;
                    var tail = instance[(end + 6)..];
                    var separator = tail.IndexOf('_');
                    var device = (instance[at..end] + "_phys_" + (separator < 0 ? tail : tail[..separator])).ToLowerInvariant();
                    if (!devices.ContainsKey(device)) devices[device] = [];
                    if (metric == "gpu.engine")
                    {
                        var engine = instance.IndexOf("_eng_", StringComparison.Ordinal);
                        if (engine < 0) continue;
                        var key = (device, instance[engine..]);
                        engines[key] = engines.GetValueOrDefault(key) + value;
                    }
                    else devices[device].Add(new(metric, value, "bytes", "PDH GPU Adapter Memory", "device:" + device, now));
                }
            }
            foreach (var (id, samples) in devices)
            {
                var busy = engines.Where(e => e.Key.Device == id).Select(e => e.Value).ToArray();
                var utilization = BusiestEngine(busy);
                samples.Add(new("gpu.utilization", utilization, "%", "PDH GPU Engine; busiest engine", "device:" + id,
                    now, window, busy.Length == 0 ? "Contador de actividad no disponible." :
                        utilization is null ? "Agregación PDH fuera del rango 0–100 %; utilización no confirmada." : null));
                foreach (var metric in new[] { "vram.used", "gpu.shared", "vram.total" })
                    if (!samples.Any(s => s.Name == metric)) samples.Add(new(metric, null, "bytes", "PDH GPU Adapter Memory", "device:" + id,
                        now, UnavailableReason: "Contador no expuesto; no se deduce capacidad de uso o presupuesto."));
            }
            if (devices.Count == 0) Diagnostics.Add("GPU/VRAM no disponibles: PDH no expone instancias; no implica ausencia de GPU.");
            var inventory = WindowsGpuInventory.Read();
            foreach (var (id, item) in inventory)
            {
                if (!devices.TryGetValue(id, out var fields)) devices[id] = fields = [];
                fields.RemoveAll(s => s.Name == "vram.total");
                fields.Add(new("vram.total", item.Dedicated, "bytes", "DXGI_ADAPTER_DESC1.DedicatedVideoMemory", "device:" + id, now));
                foreach (var (name, unit) in new[] { ("vram.used", "bytes"), ("gpu.utilization", "%") })
                    if (!fields.Any(s => s.Name == name)) fields.Add(new(name, null, unit, "PDH", "device:" + id, now,
                        UnavailableReason: "Dispositivo enumerado por DXGI; contador PDH no disponible."));
            }
            return devices.Select(d => new DeviceMetrics(d.Key, inventory.TryGetValue(d.Key, out var item) ? item.Name : d.Key, d.Value)).ToArray();
        }
        private static (string, double)[]? ReadArray(nint counter)
        {
            uint size = 0, count = 0;
            const uint moreData = 0x800007D2;
            if (PdhGetFormattedCounterArrayW(counter, 0x200 | 0x8000, ref size, ref count, 0) != moreData || size > 4 * 1024 * 1024) return null;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, 0x200 | 0x8000, ref size, ref count, buffer) != 0 || count > size / Marshal.SizeOf<CounterItem>()) return null;
                var result = new List<(string, double)>();
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(buffer + i * Marshal.SizeOf<CounterItem>());
                    if (item.Status is 0 or 1 && double.IsFinite(item.Value) && item.Value >= 0)
                        result.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value));
                }
                return result.ToArray();
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        public void Dispose() { if (query != 0) { _ = PdhCloseQuery(query); query = 0; } }
    }

    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public nint Name; public uint Status; public double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct Memory
    { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended; }
    [LibraryImport("kernel32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [LibraryImport("kernel32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int GlobalMemoryStatusEx(ref Memory memory);
    [LibraryImport("pdh.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial uint PdhOpenQueryW(nint source, nuint user, out nint query);
    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial uint PdhAddEnglishCounterW(nint query, string path, nuint user, out nint counter);
    [LibraryImport("pdh.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial uint PdhCollectQueryData(nint query);
    [LibraryImport("pdh.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, ref uint count, nint buffer);
    [LibraryImport("pdh.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial uint PdhCloseQuery(nint query);
}
