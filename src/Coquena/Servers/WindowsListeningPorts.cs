using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;
using Coquena.Storage;

namespace Coquena.Servers;

internal static partial class WindowsListeningPorts
{
    // TCP_TABLE_OWNER_PID_LISTENER contains local listening endpoints and PIDs.
    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedTcpTable(nint table, ref int size, int order, int family, int tableClass, uint reserved);

    internal static bool IsOwned(string host, int port, Guid instanceId)
    {
        IPAddress[] expected;
        try { expected = IPAddress.TryParse(host, out var parsed) ? [parsed] : Dns.GetHostAddresses(host); }
        catch (SocketException e) { throw new StorageException("endpoint-observation-failed", "No se pudo resolver la escucha configurada.", inner: e); }
        foreach (var family in new[] { 2, 23 })
        {
            var size = 0;
            var status = GetExtendedTcpTable(0, ref size, 0, family, 3, 0);
            if (status != 122 || size < 4) throw new StorageException("endpoint-observation-failed", "No se pudo consultar la escucha TCP.");
            var table = Marshal.AllocHGlobal(size);
            try
            {
                status = GetExtendedTcpTable(table, ref size, 0, family, 3, 0);
                if (status == 122) throw new StorageException("endpoint-observation-failed", "Cambió la tabla TCP durante la consulta.");
                if (status != 0) throw new StorageException("endpoint-observation-failed", "No se pudo consultar la escucha TCP.");
                var count = Marshal.ReadInt32(table);
                var stride = family == 2 ? 24 : 56;
                var portOffset = family == 2 ? 8 : 20;
                var pidOffset = family == 2 ? 20 : 52;
                if (count < 0 || count > (size - 4) / stride) throw new StorageException("endpoint-observation-failed", "Tabla TCP inválida.");
                for (var i = 0; i < count; i++)
                {
                    var offset = checked(4 + i * stride);
                    var raw = Marshal.ReadInt32(table, offset + portOffset);
                    var observed = (raw & 255) << 8 | raw >> 8 & 255;
                    if (observed != port) continue;
                    var bytes = new byte[family == 2 ? 4 : 16];
                    Marshal.Copy(table + offset + (family == 2 ? 4 : 0), bytes, 0, bytes.Length);
                    var address = family == 2 ? new IPAddress(bytes) : new IPAddress(bytes, (uint)Marshal.ReadInt32(table, offset + 16));
                    var matches = expected.Any(value => value.AddressFamily == address.AddressFamily &&
                        (value.Equals(address) || address.Equals(family == 2 ? IPAddress.Any : IPAddress.IPv6Any)));
                    if (matches && WindowsServerProcess.BelongsToInstance(Marshal.ReadInt32(table, offset + pidOffset), instanceId)) return true;
                }
            }
            finally { Marshal.FreeHGlobal(table); }
        }
        return false;
    }
}
