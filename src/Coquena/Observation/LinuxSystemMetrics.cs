using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Coquena.Observation;

// procfs describes this Linux environment (including the WSL VM), not its host.
// DRM exposes AMD/Intel counters where supported; NVML is loaded from the driver.
public sealed class LinuxSystemMetrics(string procRoot = "/proc", string drmRoot = "/sys/class/drm") : ISystemMetrics
{
    public async Task<HardwareSnapshot> CaptureAsync(CancellationToken cancellation = default)
    {
        var start = DateTimeOffset.UtcNow;
        var diagnostics = new List<string>();
        var first = Cpu();
        await Task.Delay(250, cancellation);
        var second = Cpu();
        var now = DateTimeOffset.UtcNow;
        var total = second.Total - first.Total;
        var valid = first.Valid && second.Valid && total > 0 && second.Idle >= first.Idle;
        var samples = new List<MetricSample>
        {
            new("cpu.utilization", valid ? 100.0 * (total - (second.Idle - first.Idle)) / total : null,
                "%", "procfs/stat", "machine", now, now - start, valid ? null : "Intervalo CPU no disponible.")
        };
        try
        {
            var memory = File.ReadAllLines(Path.Combine(procRoot, "meminfo")).Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts =>
                    double.Parse(parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], CultureInfo.InvariantCulture) * 1024);
            samples.Add(new("ram.total", memory["MemTotal"], "bytes", "procfs/meminfo", "machine", now));
            samples.Add(new("ram.used", memory.TryGetValue("MemAvailable", out var available) ? memory["MemTotal"] - available : null,
                "bytes", "procfs/meminfo: MemTotal - MemAvailable", "machine", now,
                UnavailableReason: memory.ContainsKey("MemAvailable") ? null : "MemAvailable no expuesto."));
        }
        catch (Exception e) when (ReadError(e))
        {
            foreach (var name in new[] { "ram.total", "ram.used" }) samples.Add(new(name, null, "bytes", "procfs/meminfo", "machine", now,
                UnavailableReason: "Memoria no disponible: " + e.GetType().Name));
        }
        var devices = new List<DeviceMetrics>();
        try
        {
            if (Directory.Exists(drmRoot))
                foreach (var card in Directory.EnumerateDirectories(drmRoot).Where(p =>
                    Path.GetFileName(p).StartsWith("card", StringComparison.Ordinal) && int.TryParse(Path.GetFileName(p)[4..], out _)).Take(64))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                    var path = Path.Combine(card, "device");
                    if (!File.Exists(Path.Combine(path, "vendor")) &&
                        !File.Exists(Path.Combine(path, "gpu_busy_percent")) && !File.Exists(Path.Combine(path, "mem_info_vram_total")))
                    { diagnostics.Add($"DRM {Path.GetFileName(card)}: sin identidad física ni contadores de hardware; no se agrega como GPU."); continue; }
                    // NVIDIA is enumerated once by NVML, never again through DRM.
                    if (File.Exists(Path.Combine(path, "vendor")) && File.ReadAllText(Path.Combine(path, "vendor")).Trim() == "0x10de") continue;
                    var id = Path.GetFileName(card);
                    var fields = new List<MetricSample>();
                    foreach (var (name, file, unit) in new[] { ("gpu.utilization", "gpu_busy_percent", "%"),
                        ("vram.used", "mem_info_vram_used", "bytes"), ("vram.total", "mem_info_vram_total", "bytes") })
                    {
                        double? value = null;
                        try { value = double.Parse(File.ReadAllText(Path.Combine(path, file)).Trim(), CultureInfo.InvariantCulture); }
                        catch (Exception e) when (ReadError(e)) { }
                        var validValue = value is >= 0 && double.IsFinite(value.Value);
                        fields.Add(new(name, validValue ? value : null, unit, "DRM sysfs/" + file,
                            "device:" + id, DateTimeOffset.UtcNow, UnavailableReason: validValue ? null : "Driver no expone un contador válido o acceso denegado."));
                    }
                    devices.Add(new(id, id, fields));
                    }
                    catch (Exception e) when (ReadError(e)) { diagnostics.Add($"DRM {Path.GetFileName(card)} no disponible: {e.GetType().Name}"); }
                }
        }
        catch (Exception e) when (ReadError(e)) { diagnostics.Add("DRM no disponible: " + e.GetType().Name); }
        if (OperatingSystem.IsLinux()) devices.AddRange(Nvml(diagnostics));
        if (devices.Count == 0) diagnostics.Add("GPU/VRAM no disponibles: sin dispositivos consultables por DRM/NVML; no implica ausencia de GPU.");
        var wsl = false;
        try { wsl = File.ReadAllText(Path.Combine(procRoot, "sys/kernel/osrelease")).Contains("microsoft", StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (ReadError(e)) { diagnostics.Add("Entorno virtualizado no comprobable mediante procfs/osrelease."); }
        if (wsl) diagnostics.Add("WSL2: CPU/RAM corresponden al entorno Linux virtualizado; GPU expuesta por el driver del host, con soporte parcial. No acredita Linux nativo ni consumo total del host.");
        return new(wsl ? "linux-wsl" : "linux", start, DateTimeOffset.UtcNow, samples, devices, diagnostics);
    }

    private (bool Valid, ulong Idle, ulong Total) Cpu()
    {
        try
        {
            // guest/guest_nice are already included in user/nice.
            var values = File.ReadLines(Path.Combine(procRoot, "stat")).First().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).Take(8).Select(value => ulong.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            return (values.Length >= 5, values[3] + values[4], values.Aggregate(0UL, (a, b) => a + b));
        }
        catch (Exception e) when (ReadError(e)) { return (false, 0, 0); }
    }
    private static bool ReadError(Exception e) => e is IOException or UnauthorizedAccessException or FormatException or OverflowException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException;

    private static unsafe DeviceMetrics[] Nvml(List<string> diagnostics)
    {
        nint library = 0;
        foreach (var path in new[] { "libnvidia-ml.so.1", "/usr/lib/wsl/lib/libnvidia-ml.so.1" })
            if (NativeLibrary.TryLoad(path, out library)) break;
        if (library == 0) { diagnostics.Add("NVML del driver no disponible."); return []; }
        var initialized = false;
        try
        {
            var init = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(library, "nvmlInit_v2");
            var status = init();
            if (status != 0) { diagnostics.Add($"NVML: inicialización no disponible ({status})."); return []; }
            initialized = true;
            var countCall = (delegate* unmanaged[Cdecl]<uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetCount_v2");
            var handleCall = (delegate* unmanaged[Cdecl]<uint, nint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetHandleByIndex_v2");
            var nameCall = (delegate* unmanaged[Cdecl]<nint, byte*, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetName");
            var uuidCall = (delegate* unmanaged[Cdecl]<nint, byte*, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetUUID");
            var memoryCall = (delegate* unmanaged[Cdecl]<nint, NvMemory*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetMemoryInfo");
            var usageCall = (delegate* unmanaged[Cdecl]<nint, NvUsage*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetUtilizationRates");
            uint count = 0;
            if (countCall(&count) != 0 || count > 64) { diagnostics.Add("NVML: inventario no disponible."); return []; }
            var result = new List<DeviceMetrics>();
            var text = stackalloc byte[256];
            for (uint index = 0; index < count; index++)
            {
                nint device = 0;
                if (handleCall(index, &device) != 0) { diagnostics.Add($"NVML: dispositivo {index} no disponible."); continue; }
                new Span<byte>(text, 256).Clear();
                var name = nameCall(device, text, 256) == 0 ? Text(text) : "NVIDIA";
                new Span<byte>(text, 256).Clear();
                var id = uuidCall(device, text, 256) == 0 ? Text(text) : "nvml:" + index;
                var memory = new NvMemory(); var usage = new NvUsage();
                var memStatus = memoryCall(device, &memory); var usageStatus = usageCall(device, &usage);
                var now = DateTimeOffset.UtcNow;
                result.Add(new(id, name, [
                    new("vram.total", memStatus == 0 ? memory.Total : null, "bytes", "NVML nvmlDeviceGetMemoryInfo", "device:" + id, now,
                        UnavailableReason: memStatus == 0 ? null : $"Driver NVML: código {memStatus}."),
                    new("vram.used", memStatus == 0 ? memory.Used : null, "bytes", "NVML nvmlDeviceGetMemoryInfo", "device:" + id, now,
                        UnavailableReason: memStatus == 0 ? null : $"Driver NVML: código {memStatus}."),
                    new("gpu.utilization", usageStatus == 0 ? usage.Gpu : null, "%", "NVML nvmlDeviceGetUtilizationRates; driver sample period", "device:" + id, now,
                        UnavailableReason: usageStatus == 0 ? null : $"Driver NVML: código {usageStatus}; puede no estar soportado en WSL.") ]));
            }
            return result.ToArray();
        }
        catch (EntryPointNotFoundException) { diagnostics.Add("NVML: API del driver incompleta."); return []; }
        finally
        {
            if (initialized && NativeLibrary.TryGetExport(library, "nvmlShutdown", out var shutdown)) _ = ((delegate* unmanaged[Cdecl]<int>)shutdown)();
            NativeLibrary.Free(library);
        }
        static string Text(byte* buffer)
        {
            var span = new ReadOnlySpan<byte>(buffer, 256); var end = span.IndexOf((byte)0);
            return Encoding.UTF8.GetString(end < 0 ? span : span[..end]);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NvMemory { public ulong Total, Free, Used; }
    [StructLayout(LayoutKind.Sequential)] private struct NvUsage { public uint Gpu, Memory; }
}
