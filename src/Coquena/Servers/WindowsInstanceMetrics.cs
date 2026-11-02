using System.Runtime.InteropServices;
using Coquena.Contracts;
using Coquena.Observation;
using Microsoft.Win32.SafeHandles;

namespace Coquena.Servers;

public sealed unsafe partial class WindowsServerProcess
{
    // All live descendants in the named job, including identifiable model
    // children. Private commit is additive; working sets are not summed.
    internal static (long CpuTicks, ulong? PrivateBytes, IReadOnlyList<MetricSample> WorkingSets) ReadInstanceMetrics(InstanceDocument instance)
    {
        using var root = Verified(instance, out var ended);
        if (ended) throw new InvalidOperationException("El proceso terminó durante la muestra.");
        using var group = VerifiedGroup(instance, root!);
        if (!QueryInformationJobObject(group, 1, out var accounting, (uint)sizeof(JobAccounting), 0)) throw Error("Leer CPU del grupo");
        var size = 8 + 4096 * sizeof(nuint);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (QueryJobMembers(group, 3, buffer, (uint)size, 0) == 0) return (accounting.TotalKernelTime + accounting.TotalUserTime, null, []);
            var assigned = Marshal.ReadInt32(buffer); var listed = Marshal.ReadInt32(buffer, 4);
            if (assigned != listed || listed is < 1 or > 4096) return (accounting.TotalKernelTime + accounting.TotalUserTime, null, []);
            ulong sum = 0;
            var workingSets = new List<MetricSample>();
            for (var i = 0; i < listed; i++)
            {
                var pid = (uint)Marshal.ReadIntPtr(buffer, 8 + i * sizeof(nuint));
                using var member = OpenProcess(0x1000 | 0x10, false, pid);
                if (member.IsInvalid || !IsProcessInJob(member, group, out var belongs) || !belongs) return (accounting.TotalKernelTime + accounting.TotalUserTime, null, []);
                var memory = new ProcessMemory { Size = (uint)sizeof(ProcessMemory) };
                if (GetProcessMemoryInfo(member, ref memory, memory.Size) == 0) return (accounting.TotalKernelTime + accounting.TotalUserTime, null, []);
                sum = checked(sum + (ulong)memory.PrivateUsage);
                workingSets.Add(new("ram.working-set", (ulong)memory.WorkingSet, "bytes", "GetProcessMemoryInfo.WorkingSetSize; includes shared pages, never summed",
                    $"instance:{instance.InstanceId}/process:{pid}/created:{Creation(member)}", DateTimeOffset.UtcNow));
            }
            return (accounting.TotalKernelTime + accounting.TotalUserTime, sum, workingSets);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessMemory
    {
        public uint Size, PageFaults;
        public nuint PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }
    [LibraryImport("kernel32.dll", EntryPoint = "QueryInformationJobObject")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int QueryJobMembers(SafeFileHandle job, int type, nint buffer, uint size, nint returned);
    [LibraryImport("psapi.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int GetProcessMemoryInfo(SafeFileHandle process, ref ProcessMemory memory, uint size);
}
