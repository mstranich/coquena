using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Coquena.Storage;

namespace Coquena.Projects;

// Directory identity survives rename and resolves junctions and volume aliases.
public sealed record DirectoryIdentity(string Volume, string FileId);

internal static partial class PhysicalDirectory
{
    internal static DirectoryIdentity Read(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Identidad de proyecto requiere Windows.");
        using var handle = Open(Path.GetFullPath(path), 0, 7, 0, 3, 0x02000000, 0);
        if (handle.IsInvalid || !Information(handle, 18, out var value, 24))
            throw new StorageException("directory-identity-unavailable", "No se pudo verificar identidad física del directorio.",
                inner: new Win32Exception(Marshal.GetLastPInvokeError()));
        return new(value.Volume.ToString("x16"), value.Low.ToString("x16") + value.High.ToString("x16"));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Identity { public ulong Volume; public ulong Low; public ulong High; }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeFileHandle Open(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Information(SafeFileHandle handle, int kind, out Identity identity, uint size);
}
