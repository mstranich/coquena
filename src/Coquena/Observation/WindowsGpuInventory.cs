using System.Runtime.InteropServices;

namespace Coquena.Observation;

internal static unsafe partial class WindowsGpuInventory
{
    // Direct COM vtables work in Native AOT without runtime COM marshalling.
    internal static Dictionary<string, (string Name, ulong Dedicated)> Read()
    {
        var result = new Dictionary<string, (string, ulong)>(StringComparer.OrdinalIgnoreCase);
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
        if (CreateDXGIFactory1(in iid, out var factory) < 0) return result;
        try
        {
            var table = *(nint**)factory;
            var enumerate = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)table[12];
            for (uint index = 0; index < 64; index++)
            {
                nint adapter = 0;
                if (enumerate(factory, index, &adapter) < 0) break;
                try
                {
                    var description = new Description();
                    var describe = (delegate* unmanaged[Stdcall]<nint, Description*, int>)(*(nint**)adapter)[10];
                    if (describe(adapter, &description) < 0) continue;
                    if ((description.Flags & 2) != 0) continue; // Software adapter, not a physical GPU.
                    var id = $"luid_0x{(uint)description.LuidHigh:x8}_0x{description.LuidLow:x8}_phys_0";
                    result[id] = (new string(description.Name).TrimEnd('\0'), (ulong)description.DedicatedVideo);
                }
                finally { Release(adapter); }
            }
        }
        finally { Release(factory); }
        return result;
    }
    private static void Release(nint value) => _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)value)[2])(value);
    [StructLayout(LayoutKind.Sequential)] private struct Description
    {
        public fixed char Name[128]; public uint VendorId, DeviceId, SubsystemId, Revision;
        public nuint DedicatedVideo, DedicatedSystem, SharedSystem; public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    [LibraryImport("dxgi.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CreateDXGIFactory1(in Guid iid, out nint factory);
}
