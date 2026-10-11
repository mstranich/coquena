using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Coquena.Storage;

// Opening for DELETE checks removal permission without renaming or deleting data.
// A write probe alone would miss an ACL on an existing collision or source file.
internal static partial class DirectoryAccess
{
    internal static void Check(string path, bool replace)
    {
        Home.CheckLinks(path);
        if (!OperatingSystem.IsWindows()) return;
        using var handle = Open(path, 0x80000000u | 0x00010000u | (replace ? 0x40000000u : 0),
            1 | 2 | 4, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"Acceso insuficiente para {(replace ? "sobrescribir" : "retirar")} {path}: " +
                new Win32Exception(Marshal.GetLastPInvokeError()).Message);
        if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0 && File.Exists(path))
            throw new IOException($"Archivo de sólo lectura: {path}; no se trasladaron recursos.");
    }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle Open(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
}
