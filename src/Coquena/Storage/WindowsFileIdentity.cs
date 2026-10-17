using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Coquena.Storage;

// FILE_ID_INFO identifies the open file, independently of its current pathname.
// Include the volume and the full 128-bit ID (NTFS and ReFS).
internal static partial class WindowsFileIdentity
{
    internal static bool Same(FileStream first, FileStream second)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Publicación por identidad requiere Windows.");
        var a = Read(first.SafeFileHandle); var b = Read(second.SafeFileHandle);
        return a.Volume == b.Volume && a.Low == b.Low && a.High == b.High;
    }

    private static Identity Read(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 18 /* FileIdInfo */, out var value, 24))
            throw new StorageException("file-identity-unavailable", "No se pudo comprobar identidad de archivo; no se confirma publicación.",
                inner: new Win32Exception(Marshal.GetLastPInvokeError()));
        return value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Identity { public ulong Volume; public ulong Low; public ulong High; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out Identity information, uint size);
}
