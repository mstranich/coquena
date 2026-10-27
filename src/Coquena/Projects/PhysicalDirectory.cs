using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Coquena.Storage;

namespace Coquena.Projects;

// Directory identity survives rename and resolves junctions and volume aliases.
public sealed record DirectoryIdentity(string Volume, string FileId);

internal static partial class PhysicalDirectory
{
    // Hold DELETE access without sharing writes/deletes: identity validation and
    // disposition apply to the same opened file, never to a replaceable path.
    internal static long DeleteOwnedFile(string path, string volume, string fileId)
    {
        using var handle = Open(Path.GetFullPath(path), 0x10080, 1, 0, 3, 0, 0);
        if (handle.IsInvalid) throw new IOException("No se pudo abrir el archivo propio para retirada.", new Win32Exception(Marshal.GetLastPInvokeError()));
        if (!Information(handle, 18, out var value, 24) || value.Volume.ToString("x16") != volume ||
            value.Low.ToString("x16") + value.High.ToString("x16") != fileId)
            throw new StorageException("file-ownership-uncertain", "La identidad del archivo no coincide con su publicación.");
        var size = new FileInfo(path).Length;
        var disposition = 1;
        if (!Disposition(handle, 4, ref disposition, 4))
            throw new IOException("No se pudo retirar el archivo propio.", new Win32Exception(Marshal.GetLastPInvokeError()));
        return size;
    }
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
    [LibraryImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Disposition(SafeFileHandle handle, int kind, ref int disposition, uint size);
}
