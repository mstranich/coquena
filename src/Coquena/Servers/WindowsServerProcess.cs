using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Coquena.Configuration;
using Coquena.Contracts;

namespace Coquena.Servers;

// Windows x64 only. Every backend owns a hidden console and a named job; a short
// signal helper attaches to that console, never to the user's foreground console.
public sealed unsafe partial class WindowsServerProcess : IDisposable
{
    private readonly SafeFileHandle process;
    private readonly SafeFileHandle thread;
    private readonly SafeFileHandle job;
    private readonly SafeFileHandle lifetime;
    public int Pid { get; }
    public long Created => Creation(process);
    public string Executable => Image(process);
    private WindowsServerProcess(ProcessInfo info, SafeFileHandle jobHandle, SafeFileHandle lifetimeHandle)
    {
        process = new(info.Process, true); thread = new(info.Thread, true);
        job = jobHandle; lifetime = lifetimeHandle; Pid = (int)info.Pid;
    }
    internal static string JobName(Guid id) => "Local\\Coquena-" + id.ToString("N");

    public static WindowsServerProcess Create(string executable, string[] arguments, string log, Guid id, string? homeRoot = null, bool newLog = false)
    {
        var job = CreateJobObjectW(0, null);
        if (job.IsInvalid) throw Error("Crear job");
        var lifetime = CreateJobObjectW(0, JobName(id));
        try
        {
            if (lifetime.IsInvalid || !SetHandleInformation(lifetime, 1, 1)) throw Error("Crear job de instancia");
            ConfigureJob(job, true);
            var security = new Security { Size = sizeof(Security), Inherit = 1 };
            using var output = CreateFileW(log, 0x40000000, 1 | 2 | 4, ref security, newLog ? 1u : 2u, 0x80, 0);
            if (output.IsInvalid) throw Error("Abrir salida del backend");
            using var input = CreateFileW("NUL", 0x80000000, 1 | 2, ref security, 3, 0x80, 0);
            if (input.IsInvalid) throw Error("Abrir entrada del backend");
            nuint size = 0;
            _ = InitializeProcThreadAttributeList(0, 2, 0, ref size);
            var attributes = Marshal.AllocHGlobal(checked((int)size));
            var initialized = false;
            try
            {
                if (!InitializeProcThreadAttributeList(attributes, 2, 0, ref size)) throw Error("Inicializar atributos");
                initialized = true;
                nint* handles = stackalloc nint[3] { output.DangerousGetHandle(), input.DangerousGetHandle(), lifetime.DangerousGetHandle() };
                nint* jobs = stackalloc nint[2] { job.DangerousGetHandle(), lifetime.DangerousGetHandle() };
                if (!UpdateProcThreadAttribute(attributes, 0, 0x20002, (nint)handles, (nuint)(3 * sizeof(nint)), 0, 0) ||
                    !UpdateProcThreadAttribute(attributes, 0, 0x2000D, (nint)jobs, (nuint)(2 * sizeof(nint)), 0, 0))
                    throw Error("Configurar atributos");
                var start = new StartupEx
                {
                    Startup = new Startup
                    {
                        Size = sizeof(StartupEx), Flags = 0x101, Show = 0,
                        Input = input.DangerousGetHandle(), Output = output.DangerousGetHandle(), Error = output.DangerousGetHandle()
                    },
                    Attributes = attributes
                };
                var command = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote)));
                var variables = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                    .ToDictionary(p => (string)p.Key, p => (string)p.Value!, StringComparer.OrdinalIgnoreCase);
                // Saved parameters are authoritative; inherited llama overrides must
                // not silently enable metrics or replace models-preset/model paths.
                foreach (var key in variables.Keys.Where(k => k.StartsWith("LLAMA_ARG_", StringComparison.OrdinalIgnoreCase)).ToArray()) variables.Remove(key);
                if (homeRoot is not null) variables["COQUENA_HOME"] = homeRoot;
                var environment = Marshal.StringToHGlobalUni(string.Join('\0', variables.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value)) + "\0\0");
                try
                {
                    if (!CreateProcessW(executable, command, 0, 0, true, 0x80000 | 0x10 | 0x4 | 0x400, environment,
                        Path.GetDirectoryName(executable), ref start, out var info)) throw Error("Iniciar backend");
                    return new(info, job, lifetime);
                }
                finally { Marshal.FreeHGlobal(environment); }
            }
            finally
            {
                if (initialized) DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
        }
        catch { job.Dispose(); lifetime.Dispose(); throw; }
    }

    // The staging job kills the suspended process on any failure or manager crash.
    // The instance has been durably published before Resume. After activation the
    // job stays attached to router children, with no manager required to own it.
    public void Activate()
    {
        if (ResumeThread(thread) == uint.MaxValue) throw Error("Activar backend");
        ConfigureJob(job, false);
    }
    private static void ConfigureJob(SafeFileHandle handle, bool killOnClose)
    {
        var limits = new JobLimits { Flags = killOnClose ? 0x2000u : 0 };
        if (!SetInformationJobObject(handle, 9, ref limits, (uint)sizeof(JobLimits))) throw Error("Configurar job");
    }

    // Hold an OS handle throughout verification and action, preventing PID reuse
    // between the check and termination from selecting a different process.
    internal static SafeFileHandle? Verified(InstanceDocument instance, out bool ended)
    {
        ended = false;
        if (instance.ProcessCreated is null || instance.ExecutablePath is null)
            throw new ConfigurationException($"Identidad incierta de instancia {instance.InstanceId:D}; no se actúa sobre el PID.");
        var handle = OpenProcess(0x1000 | 0x100000 | 1, false, (uint)instance.Pid);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError(); handle.Dispose();
            if (code == 87) { ended = true; return null; }
            throw new ConfigurationException($"Identidad incierta de instancia {instance.InstanceId:D}: acceso al proceso ({code}).");
        }
        try
        {
            if (WaitForSingleObject(handle, 0) == 0 || Creation(handle) != instance.ProcessCreated)
            { ended = true; handle.Dispose(); return null; }
            if (!Image(handle).Equals(instance.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                throw new ConfigurationException($"Identidad incierta de instancia {instance.InstanceId:D}: ejecutable distinto.");
            using var job = OpenJobObjectW(4, false, JobName(instance.InstanceId));
            if (job.IsInvalid || !IsProcessInJob(handle, job, out var belongs) || !belongs)
                throw new ConfigurationException($"Identidad incierta de instancia {instance.InstanceId:D}: job no verificable.");
            return handle;
        }
        catch
        {
            // Exit can race image inspection: the process object outlives its
            // accessible image. Only an OS-signalled exit permits cleanup.
            if (WaitForSingleObject(handle, 100) == 0) { ended = true; handle.Dispose(); return null; }
            handle.Dispose(); throw;
        }
    }
    internal static bool Exited(SafeFileHandle handle, int milliseconds = 0) => WaitForSingleObject(handle, (uint)milliseconds) == 0;
    internal static SafeFileHandle VerifiedGroup(InstanceDocument instance, SafeFileHandle verified)
    {
        var group = OpenJobObjectW(8 | 4, false, JobName(instance.InstanceId));
        if (group.IsInvalid || !IsProcessInJob(verified, group, out var belongs) || !belongs)
        {
            group.Dispose();
            throw new ConfigurationException("Grupo de instancia no verificable; se conserva el registro.");
        }
        return group;
    }
    internal static bool GroupExited(SafeFileHandle group)
    {
        if (!QueryInformationJobObject(group, 1, out var accounting, (uint)sizeof(JobAccounting), 0))
            throw Error("Consultar grupo de instancia");
        return accounting.ActiveProcesses == 0;
    }
    internal static void RequireGroupEnded(Guid instanceId)
    {
        using var group = OpenJobObjectW(4, false, JobName(instanceId));
        if (group.IsInvalid)
        {
            if (Marshal.GetLastPInvokeError() == 2) return;
            throw Error("Consultar grupo de instancia");
        }
        if (!GroupExited(group)) throw new ConfigurationException("El proceso principal terminó pero su grupo conserva procesos; no se libera la instancia.");
    }
    internal static void ForceGroup(SafeFileHandle group)
    {
        if (!TerminateJobObject(group, 1)) throw Error("Forzar cierre del grupo");
    }
    internal static bool BelongsToInstance(int pid, Guid instanceId)
    {
        using var process = OpenProcess(0x1000, false, (uint)pid);
        if (process.IsInvalid) return false;
        using var job = OpenJobObjectW(4, false, JobName(instanceId));
        return !job.IsInvalid && IsProcessInJob(process, job, out var belongs) && belongs;
    }
    internal static int ExitCode(SafeFileHandle handle)
    {
        if (!GetExitCodeProcess(handle, out var code)) throw Error("Leer salida");
        return unchecked((int)code);
    }
    internal static void Force(InstanceDocument instance, SafeFileHandle verified)
    {
        if (Exited(verified)) return;
        using var job = OpenJobObjectW(8 | 4, false, JobName(instance.InstanceId));
        if (job.IsInvalid) throw new ConfigurationException("Job de instancia ausente; identidad del grupo incierta, sin cierre forzado.");
        if (!IsProcessInJob(verified, job, out var belongs) || !belongs)
            throw new ConfigurationException("El proceso no pertenece al job de la instancia; sin cierre forzado.");
        if (!TerminateJobObject(job, 1)) throw Error("Forzar cierre");
    }
    public static void Force(InstanceDocument instance)
    {
        using var verified = Verified(instance, out _);
        if (verified is not null) Force(instance, verified);
    }
    public static int Signal(InstanceDocument instance)
    {
        using var verified = Verified(instance, out _);
        if (verified is null) return 0;
        _ = FreeConsole();
        if (!AttachConsole((uint)instance.Pid)) return 1;
        try
        {
            if (!SetConsoleCtrlHandler(0, true)) return 1;
            if (!GenerateConsoleCtrlEvent(0, 0)) return 1;
            // The signal is asynchronous; keep the helper attached for delivery.
            Thread.Sleep(100);
            return 0;
        }
        finally { _ = FreeConsole(); }
    }
    private static long Creation(SafeFileHandle handle)
    {
        if (!GetProcessTimes(handle, out var created, out _, out _, out _)) throw Error("Leer identidad");
        return created;
    }
    private static string Image(SafeFileHandle handle)
    {
        var image = new StringBuilder(32768); uint size = 32768;
        if (!QueryFullProcessImageNameW(handle, 0, image, ref size)) throw Error("Leer ejecutable");
        return image.ToString();
    }
    private static ConfigurationException Error(string action) => new($"{action}: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}");
    internal static string Quote(string argument)
    {
        var result = new StringBuilder("\""); var slashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); result.Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    public void Dispose() { thread.Dispose(); process.Dispose(); job.Dispose(); lifetime.Dispose(); }

    [StructLayout(LayoutKind.Sequential)] private struct Security { public int Size; public nint Descriptor; public int Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public nint Process, Thread; public uint Pid, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct Startup
    {
        public int Size; public nint Reserved, Desktop, Title; public uint X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
        public ushort Show, ReservedSize; public nint ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupEx { public Startup Startup; public nint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct JobLimits
    {
        public long ProcessTime, JobTime; public uint Flags; public nuint MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcesses; public nuint Affinity; public uint Priority, Scheduling;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)] private struct JobAccounting
    {
        public long TotalUserTime, TotalKernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int type, out JobAccounting accounting, uint size, nint returned);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObjectW(nint security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle OpenJobObjectW(uint access, bool inherit, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int type, ref JobLimits limits, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(SafeFileHandle job, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessInJob(SafeFileHandle process, SafeFileHandle job, out bool belongs);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, ref Security security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessW(string application, StringBuilder command, nint processSecurity, nint threadSecurity, bool inherit, uint flags, nint environment, string? directory, ref StartupEx start, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeFileHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(SafeFileHandle process, uint flags, StringBuilder image, ref uint size);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeFileHandle process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleCtrlHandler(nint handler, bool add);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
}
