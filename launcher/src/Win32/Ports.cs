using System.Net;
using System.Net.Sockets;

namespace EtherBound.Launcher.Win32;

internal sealed record Listener(int Port, string Address, int ProcessId);

internal static unsafe class Ports
{
    public static List<Listener> Listeners(IReadOnlyCollection<int> ports)
    {
        var found = new List<Listener>();
        Read(Native.AddressFamilyInet, ports, found);
        Read(Native.AddressFamilyInet6, ports, found);
        return found;
    }

    // Binding exclusively tells apart a port Windows reserves (an excluded range answers
    // AccessDenied) from one that is merely busy.
    public static SocketError TryBind(int port)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true,
        };
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return SocketError.Success;
        }
        catch (SocketException error)
        {
            return error.SocketErrorCode;
        }
    }

    private static void Read(int family, IReadOnlyCollection<int> ports, List<Listener> found)
    {
        // MIB_TCPROW_OWNER_PID is 24 bytes (port at 8, pid at 20); MIB_TCP6ROW_OWNER_PID is 56
        // (port at 20, pid at 52). Both tables start with a 4-byte row count.
        var (rowSize, portOffset, pidOffset) = family == Native.AddressFamilyInet ? (24, 8, 20) : (56, 20, 52);
        uint size = 0;
        Native.GetExtendedTcpTable(null, &size, false, family, Native.TcpTableOwnerPidListener, 0);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var buffer = new byte[Math.Max(size, 4u)];
            fixed (byte* table = buffer)
            {
                var result = Native.GetExtendedTcpTable(
                    table, &size, false, family, Native.TcpTableOwnerPidListener, 0);
                if (result == Native.ErrorInsufficientBuffer)
                {
                    continue;
                }

                if (result != 0)
                {
                    return;
                }

                var count = *(uint*)table;
                for (var i = 0; i < count; i++)
                {
                    var row = table + 4 + (i * rowSize);
                    var raw = *(uint*)(row + portOffset);
                    var port = (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));
                    if (!ports.Contains(port))
                    {
                        continue;
                    }

                    var address = family == Native.AddressFamilyInet
                        ? new IPAddress(new ReadOnlySpan<byte>(row + 4, 4))
                        : new IPAddress(new ReadOnlySpan<byte>(row, 16));
                    found.Add(new Listener(port, address.ToString(), (int)*(uint*)(row + pidOffset)));
                }

                return;
            }
        }
    }
}
