using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class ExclusiveTcpPortReservation : IDisposable
    {
        private IList<Socket> _sockets;

        private ExclusiveTcpPortReservation(IList<Socket> sockets)
        {
            _sockets = sockets;
        }

        internal static ExclusiveTcpPortReservation AcquireDualStackWildcard(
            int grpcPort,
            int restPort)
        {
            if (grpcPort < 1 || grpcPort > 65535 ||
                restPort < 1 || restPort > 65535 ||
                grpcPort == restPort)
            {
                throw new ArgumentOutOfRangeException("grpcPort");
            }
            return AcquireDualStackWildcard(new[] { grpcPort, restPort });
        }

        internal static ExclusiveTcpPortReservation AcquireDualStackWildcard(
            IList<int> ports)
        {
            if (ports == null || ports.Count == 0)
            {
                throw new ArgumentNullException("ports");
            }
            HashSet<int> unique = new HashSet<int>();
            for (int index = 0; index < ports.Count; index++)
            {
                if (ports[index] < 1 || ports[index] > 65535 ||
                    !unique.Add(ports[index]))
                {
                    throw new ArgumentOutOfRangeException("ports");
                }
            }
            List<Socket> sockets = new List<Socket>();
            try
            {
                for (int index = 0; index < ports.Count; index++)
                {
                    sockets.Add(BindDualStack(ports[index]));
                }
                return new ExclusiveTcpPortReservation(sockets);
            }
            catch
            {
                for (int index = 0; index < sockets.Count; index++)
                {
                    sockets[index].Dispose();
                }
                throw;
            }
        }

        public void Dispose()
        {
            IList<Socket> sockets = _sockets;
            _sockets = null;
            if (sockets == null)
            {
                return;
            }
            for (int index = 0; index < sockets.Count; index++)
            {
                sockets[index].Dispose();
            }
        }

        private static Socket BindDualStack(int port)
        {
            Socket socket = new Socket(
                AddressFamily.InterNetworkV6,
                SocketType.Stream,
                ProtocolType.Tcp);
            try
            {
                socket.ExclusiveAddressUse = true;
                socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    false);
                socket.DualMode = true;
                socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }
}
