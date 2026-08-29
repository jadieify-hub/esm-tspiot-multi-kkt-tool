using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface ITcpListenerOwnerReader
    {
        IList<int> FindListenerProcessIds(int port);
    }

    internal sealed class TcpListenerOwnerReader : ITcpListenerOwnerReader
    {
        private const int AddressFamilyInet = 2;
        private const int AddressFamilyInet6 = 23;
        private const int ErrorInsufficientBuffer = 122;
        private const int TcpTableOwnerPidListener = 3;

        public IList<int> FindListenerProcessIds(int port)
        {
            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException("port");
            }
            HashSet<int> owners = new HashSet<int>();
            ReadIpv4(port, owners);
            ReadIpv6(port, owners);
            List<int> result = new List<int>(owners);
            result.Sort();
            return result;
        }

        private static void ReadIpv4(int expectedPort, HashSet<int> owners)
        {
            int size;
            IntPtr buffer = ReadTable(AddressFamilyInet, out size);
            try
            {
                int count = Marshal.ReadInt32(buffer);
                int offset = sizeof(int);
                int rowSize = Marshal.SizeOf(typeof(MibTcpRowOwnerPid));
                for (int index = 0; index < count; index++)
                {
                    MibTcpRowOwnerPid row = (MibTcpRowOwnerPid)Marshal.PtrToStructure(
                        IntPtr.Add(buffer, offset + index * rowSize),
                        typeof(MibTcpRowOwnerPid));
                    if (ToHostPort(row.LocalPort) == expectedPort && row.ProcessId > 0)
                    {
                        owners.Add(unchecked((int)row.ProcessId));
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static void ReadIpv6(int expectedPort, HashSet<int> owners)
        {
            int size;
            IntPtr buffer = ReadTable(AddressFamilyInet6, out size);
            try
            {
                int count = Marshal.ReadInt32(buffer);
                int offset = sizeof(int);
                int rowSize = Marshal.SizeOf(typeof(MibTcp6RowOwnerPid));
                for (int index = 0; index < count; index++)
                {
                    MibTcp6RowOwnerPid row = (MibTcp6RowOwnerPid)Marshal.PtrToStructure(
                        IntPtr.Add(buffer, offset + index * rowSize),
                        typeof(MibTcp6RowOwnerPid));
                    if (ToHostPort(row.LocalPort) == expectedPort && row.ProcessId > 0)
                    {
                        owners.Add(unchecked((int)row.ProcessId));
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static IntPtr ReadTable(int addressFamily, out int size)
        {
            size = 0;
            uint first = GetExtendedTcpTable(
                IntPtr.Zero,
                ref size,
                true,
                addressFamily,
                TcpTableOwnerPidListener,
                0);
            if (first != ErrorInsufficientBuffer || size <= sizeof(int))
            {
                throw new Win32Exception(unchecked((int)first));
            }

            IntPtr buffer = Marshal.AllocHGlobal(size);
            uint second = GetExtendedTcpTable(
                buffer,
                ref size,
                true,
                addressFamily,
                TcpTableOwnerPidListener,
                0);
            if (second != 0)
            {
                Marshal.FreeHGlobal(buffer);
                throw new Win32Exception(unchecked((int)second));
            }
            return buffer;
        }

        private static int ToHostPort(uint networkPort)
        {
            short lowWord = unchecked((short)(networkPort & 0xFFFF));
            return unchecked((ushort)IPAddress.NetworkToHostOrder(lowWord));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcpRowOwnerPid
        {
            internal uint State;
            internal uint LocalAddress;
            internal uint LocalPort;
            internal uint RemoteAddress;
            internal uint RemotePort;
            internal uint ProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcp6RowOwnerPid
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            internal byte[] LocalAddress;
            internal uint LocalScopeId;
            internal uint LocalPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            internal byte[] RemoteAddress;
            internal uint RemoteScopeId;
            internal uint RemotePort;
            internal uint State;
            internal uint ProcessId;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr tcpTable,
            ref int size,
            bool order,
            int addressFamily,
            int tableClass,
            uint reserved);
    }
}
