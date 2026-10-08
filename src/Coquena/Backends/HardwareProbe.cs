using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace Coquena.Backends;

// Direct OS/driver APIs, source-generated interop, no shell, WMI or NuGet dependency.
public static unsafe partial class HardwareProbe
{
    public static HardwareContext Inspect()
    {
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        if (!OperatingSystem.IsWindows())
            return new($"{(OperatingSystem.IsLinux() ? "linux" : "other")}-{arch}", RuntimeInformation.OSDescription,
                null, [], null, [], new("unknown", "Sondeo CUDA sólo implementado en Windows."), new("unknown", "Sondeo Vulkan sólo implementado en Windows."));
        return Windows(arch);
    }

    [SupportedOSPlatform("windows")]
    private static HardwareContext Windows(string arch)
    {
        string cpu = "Nombre de CPU no disponible";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = key?.GetValue("ProcessorNameString") as string ?? cpu;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        var memory = new MemoryStatus { Length = (uint)sizeof(MemoryStatus) };
        ulong? ram = GlobalMemoryStatusEx(&memory) != 0 ? memory.TotalPhysical : null;
        List<string> adapters = [];
        for (uint i = 0; i < 64; i++)
        {
            var display = new DisplayDevice { Size = (uint)sizeof(DisplayDevice) };
            if (EnumDisplayDevicesW(null, i, &display, 0) == 0) break;
            if ((display.Flags & 8) == 0) adapters.Add(new string(display.Description).Trim());
        }
        var (driver, devices, cuda) = ProbeCuda();
        return new($"win-{arch}", cpu.Trim(), ram, adapters.Distinct().ToArray(), driver, devices, cuda, ProbeVulkan());
    }

    private static (int?, CudaDevice[], AccelerationEvidence) ProbeCuda()
    {
        try
        {
            var status = cuInit(0);
            if (status == 100) return (null, [], new("incompatible", "Driver CUDA no encontró dispositivos utilizables."));
            if (status != 0) return (null, [], new("unknown", $"cuInit falló ({status}); no se deduce ausencia de GPU."));
            if (cuDriverGetVersion(out var version) != 0 || cuDeviceGetCount(out var count) != 0 || count is < 0 or > 64)
                return (null, [], new("unknown", "No se pudo obtener evidencia completa del driver CUDA."));
            List<CudaDevice> devices = [];
            var name = stackalloc byte[256];
            for (var i = 0; i < count; i++)
            {
                new Span<byte>(name, 256).Clear();
                if (cuDeviceGet(out var device, i) != 0 || cuDeviceGetName(name, 256, device) != 0 ||
                    cuDeviceGetAttribute(out var major, 75, device) != 0 || cuDeviceGetAttribute(out var minor, 76, device) != 0)
                    return (version, devices.ToArray(), new("unknown", "Enumeración CUDA incompleta."));
                var length = new ReadOnlySpan<byte>(name, 256).IndexOf((byte)0);
                devices.Add(new(Encoding.UTF8.GetString(new ReadOnlySpan<byte>(name, length < 0 ? 256 : length)), major * 10 + minor));
            }
            return (version, devices.ToArray(), new(count > 0 ? "compatible" : "incompatible",
                count > 0 ? "Driver y dispositivos CUDA detectados." : "Enumeración CUDA completa sin dispositivos."));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return (null, [], new("unknown", "API del driver NVIDIA no disponible; no se afirma ausencia de GPU.")); }
    }

    private static AccelerationEvidence ProbeVulkan()
    {
        nint instance = 0;
        try
        {
            const uint api12 = (1u << 22) | (2u << 12);
            var status = vkEnumerateInstanceVersion(out var version);
            if (status != 0) return new("unknown", $"No se pudo consultar el loader Vulkan ({status}).");
            if (version < api12) return new("incompatible", "Loader Vulkan anterior a 1.2.");
            var app = new ApplicationInfo { Type = 0, ApiVersion = api12 };
            var create = new InstanceCreateInfo { Type = 1, Application = &app };
            status = vkCreateInstance(&create, 0, out instance);
            if (status != 0) return new("unknown", $"No se pudo crear instancia Vulkan 1.2 ({status}).");
            uint count = 0;
            if (vkEnumeratePhysicalDevices(instance, &count, null) != 0 || count > 64)
                return new("unknown", "Enumeración Vulkan incompleta o excesiva.");
            if (count == 0) return new("incompatible", "Enumeración Vulkan completa sin dispositivos.");
            var devices = stackalloc nint[(int)count];
            if (vkEnumeratePhysicalDevices(instance, &count, devices) != 0)
                return new("unknown", "El inventario Vulkan cambió o no pudo completarse.");
            List<string> compatible = [];
            var properties = stackalloc byte[1024];
            var queues = stackalloc QueueFamily[256];
            for (var i = 0; i < count; i++)
            {
                new Span<byte>(properties, 1024).Clear();
                vkGetPhysicalDeviceProperties(devices[i], properties);
                var api = *(uint*)properties;
                var kind = *(uint*)(properties + 16);
                if (api < api12 || kind is not (1 or 2 or 3)) continue; // No software/CPU Vulkan fallback.
                var feature11 = new Vulkan11Features { Type = 49 };
                var features = new PhysicalFeatures2 { Type = 1000059000, Next = &feature11 };
                vkGetPhysicalDeviceFeatures2(devices[i], &features);
                if (feature11.StorageBuffer16BitAccess == 0) continue;
                uint queueCount = 0;
                vkGetPhysicalDeviceQueueFamilyProperties(devices[i], &queueCount, null);
                if (queueCount > 256) return new("unknown", "Demasiadas familias de colas Vulkan.");
                var allocated = queueCount;
                vkGetPhysicalDeviceQueueFamilyProperties(devices[i], &queueCount, queues);
                if (queueCount > allocated) return new("unknown", "Inventario de colas Vulkan cambió.");
                var compute = false;
                for (var q = 0; q < queueCount; q++) if ((queues[q].Flags & 2) != 0 && queues[q].Count > 0) compute = true;
                if (!compute) continue;
                var name = new ReadOnlySpan<byte>(properties + 20, 256);
                var end = name.IndexOf((byte)0);
                compatible.Add(Encoding.UTF8.GetString(end < 0 ? name : name[..end]));
            }
            return compatible.Count > 0
                ? new("compatible", $"Vulkan 1.2, GPU, cola compute y almacenamiento de 16 bits: {string.Join(", ", compatible)}. Sondeo básico; no garantiza inferencia del binario concreto.")
                : new("incompatible", "Ninguna GPU Vulkan enumerada satisface el sondeo básico (1.2, compute y almacenamiento de 16 bits).");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return new("unknown", "API Vulkan no disponible o incompleta; no se deduce soporte a partir del fabricante."); }
        finally { if (instance != 0) vkDestroyInstance(instance, 0); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayDevice { public uint Size; public fixed char Name[32]; public fixed char Description[128]; public uint Flags; public fixed char Id[128]; public fixed char Key[128]; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ApplicationInfo { public uint Type; public nint Next, Name; public uint Version; public nint Engine; public uint EngineVersion, ApiVersion; }
    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceCreateInfo { public uint Type; public nint Next; public uint Flags; public ApplicationInfo* Application; public uint LayerCount; public nint Layers; public uint ExtensionCount; public nint Extensions; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PhysicalFeatures2 { public uint Type; public void* Next; public fixed uint Features[55]; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Vulkan11Features { public uint Type; public nint Next; public uint StorageBuffer16BitAccess; public fixed uint Others[11]; }
    [StructLayout(LayoutKind.Sequential)]
    private struct QueueFamily { public uint Flags, Count, Timestamp, Width, Height, Depth; }

    [LibraryImport("kernel32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GlobalMemoryStatusEx(MemoryStatus* memory);
    [LibraryImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int EnumDisplayDevicesW(char* device, uint index, DisplayDevice* display, uint flags);
    [LibraryImport("nvcuda.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int cuInit(uint flags);
    [LibraryImport("nvcuda.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int cuDriverGetVersion(out int version);
    [LibraryImport("nvcuda.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int cuDeviceGetCount(out int count);
    [LibraryImport("nvcuda.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int cuDeviceGet(out int device, int index);
    [LibraryImport("nvcuda.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int cuDeviceGetName(byte* name, int length, int device);
    [LibraryImport("nvcuda.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int cuDeviceGetAttribute(out int value, int attribute, int device);
    [LibraryImport("vulkan-1.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int vkEnumerateInstanceVersion(out uint version);
    [LibraryImport("vulkan-1.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int vkCreateInstance(InstanceCreateInfo* create, nint allocator, out nint instance);
    [LibraryImport("vulkan-1.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void vkDestroyInstance(nint instance, nint allocator);
    [LibraryImport("vulkan-1.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int vkEnumeratePhysicalDevices(nint instance, uint* count, nint* devices);
    [LibraryImport("vulkan-1.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void vkGetPhysicalDeviceProperties(nint device, byte* properties);
    [LibraryImport("vulkan-1.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void vkGetPhysicalDeviceFeatures2(nint device, PhysicalFeatures2* features);
    [LibraryImport("vulkan-1.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void vkGetPhysicalDeviceQueueFamilyProperties(nint device, uint* count, QueueFamily* queues);
}
