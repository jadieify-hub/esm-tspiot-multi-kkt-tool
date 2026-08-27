using System;

namespace EsmTspiot.Shared.Models
{
    public sealed class TcpPortRange
    {
        public TcpPortRange(int first, int last)
        {
            if (first < 1 || last > 65535 || first > last)
            {
                throw new ArgumentOutOfRangeException("first", "TCP port range must be within 1-65535 and ordered.");
            }

            First = first;
            Last = last;
        }

        public int First { get; private set; }
        public int Last { get; private set; }

        public bool Contains(int port)
        {
            return port >= First && port <= Last;
        }
    }
}
