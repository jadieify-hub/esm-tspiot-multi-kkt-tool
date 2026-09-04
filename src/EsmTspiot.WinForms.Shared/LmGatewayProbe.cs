using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LmGatewayProbe : ILmGatewayProbe
    {
        private const uint ToolhelpSnapshotProcess = 0x00000002;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);
        private readonly ReadOnlyWindowsServiceReader _services;
        private readonly ReadOnlyTcpListenerOwnerReader _listeners;
        private readonly string _helperPath;

        internal LmGatewayProbe()
        {
            _services = new ReadOnlyWindowsServiceReader();
            _listeners = new ReadOnlyTcpListenerOwnerReader();
            _helperPath = new ProvisionerProcessLauncher().HelperPath;
        }

        public Task<LmGatewayProbeResult> ProbeAsync(
            ManagedLmServiceSpec service,
            CancellationToken cancellation)
        {
            if (service == null)
            {
                throw new ArgumentNullException("service");
            }
            return Task.Run(delegate
            {
                cancellation.ThrowIfCancellationRequested();
                return Probe(service, cancellation);
            }, cancellation);
        }

        private LmGatewayProbeResult Probe(
            ManagedLmServiceSpec spec,
            CancellationToken cancellation)
        {
            LmGatewayProbeResult result = new LmGatewayProbeResult();
            try
            {
                ReadOnlyWindowsService service = _services.Query(spec.ServiceName);
                string expectedImage = "\"" + _helperPath + "\" --supervise " + spec.ServiceName;
                if (service == null || service.State != 4 || service.ProcessId <= 0 ||
                    !string.Equals(service.ImagePath, expectedImage, StringComparison.Ordinal) ||
                    !string.Equals(
                        service.Description,
                        "KRS.MultiKKT.LmGateway.Managed.v1:" + spec.KktSerial,
                        StringComparison.Ordinal) ||
                    !string.Equals(service.AccountName, "LocalSystem", StringComparison.OrdinalIgnoreCase) ||
                    service.StartType != 2 || service.Dependencies.Count != 0 ||
                    !File.Exists(_helperPath) ||
                    !LmControllerFileIdentity.FixedTimeEquals(
                        LmServiceInventoryReader.ComputeSha256(_helperPath),
                        ProvisionerIntegrity.ExpectedSha256))
                {
                    result.Message = "Управляемая служба не запущена или не прошла проверку владельца.";
                    return result;
                }
                result.ServiceRunning = true;
                cancellation.ThrowIfCancellationRequested();

                IList<int> grpc = _listeners.Find(spec.Ports.GrpcPort);
                IList<int> rest = _listeners.Find(spec.Ports.RestPort);
                result.GrpcListenerReady = grpc.Count == 1;
                result.RestListenerReady = rest.Count == 1;
                if (!result.GrpcListenerReady || !result.RestListenerReady || grpc[0] != rest[0])
                {
                    result.Message = "Порты контроллера не слушаются одним процессом.";
                    return result;
                }

                int controllerPid = grpc[0];
                string controllerPath = GetProcessPath(controllerPid);
                result.ListenerOwnersVerified = controllerPid != service.ProcessId &&
                    GetParentProcessId(controllerPid) == service.ProcessId &&
                    string.Equals(
                        Path.GetFullPath(controllerPath),
                        Path.GetFullPath(LmControllerFileIdentity.GetOfficialControllerPath()),
                        StringComparison.OrdinalIgnoreCase) &&
                    LmControllerFileIdentity.IsSupportedController(controllerPath);
                result.Message = result.ListenerOwnersVerified
                    ? string.Empty
                    : "Listener принадлежит неподтвержденному процессу контроллера.";
                return result;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException) &&
                    !(ex is Win32Exception) && !(ex is InvalidOperationException) &&
                    !(ex is ArgumentException))
                {
                    throw;
                }
                result.Message = "Не удалось проверить готовность: " + ex.GetType().Name + ".";
                return result;
            }
        }

        private static int GetParentProcessId(int processId)
        {
            IntPtr snapshot = CreateToolhelp32Snapshot(ToolhelpSnapshotProcess, 0);
            if (snapshot == InvalidHandleValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try
            {
                ProcessEntry32 entry = new ProcessEntry32();
                entry.Size = unchecked((uint)Marshal.SizeOf(typeof(ProcessEntry32)));
                if (!Process32FirstW(snapshot, ref entry))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                do
                {
                    if (entry.ProcessId == unchecked((uint)processId))
                    {
                        return unchecked((int)entry.ParentProcessId);
                    }
                }
                while (Process32NextW(snapshot, ref entry));
                throw new InvalidOperationException("Процесс контроллера уже завершился.");
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        private static string GetProcessPath(int processId)
        {
            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (process == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try
            {
                int capacity = 32768;
                StringBuilder path = new StringBuilder(capacity);
                if (!QueryFullProcessImageNameW(process, 0, path, ref capacity))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return path.ToString();
            }
            finally
            {
                CloseHandle(process);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry32
        {
            internal uint Size;
            private uint Usage;
            internal uint ProcessId;
            private IntPtr DefaultHeapId;
            private uint ModuleId;
            private uint Threads;
            internal uint ParentProcessId;
            private int BasePriority;
            private uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            private string ExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(
            IntPtr process,
            uint flags,
            StringBuilder path,
            ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }

    internal sealed class ReadOnlyTcpListenerOwnerReader
    {
        private const int AddressFamilyInet = 2;
        private const int AddressFamilyInet6 = 23;
        private const int ErrorInsufficientBuffer = 122;
        private const int TcpTableOwnerPidListener = 3;

        internal IList<int> Find(int port)
        {
            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException("port");
            }
            HashSet<int> owners = new HashSet<int>();
            ReadTable(AddressFamilyInet, delegate(int hostPort, int processId)
            {
                if (hostPort == port && processId > 0)
                {
                    owners.Add(processId);
                }
            }, false);
            ReadTable(AddressFamilyInet6, delegate(int hostPort, int processId)
            {
                if (hostPort == port && processId > 0)
                {
                    owners.Add(processId);
                }
            }, true);
            List<int> result = new List<int>(owners);
            result.Sort();
            return result;
        }

        private static void ReadTable(
            int family,
            Action<int, int> addListener,
            bool ipv6)
        {
            int size = 0;
            uint first = GetExtendedTcpTable(
                IntPtr.Zero,
                ref size,
                true,
                family,
                TcpTableOwnerPidListener,
                0);
            if (first != ErrorInsufficientBuffer || size <= sizeof(int))
            {
                throw new Win32Exception(unchecked((int)first));
            }
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                uint second = GetExtendedTcpTable(
                    buffer,
                    ref size,
                    true,
                    family,
                    TcpTableOwnerPidListener,
                    0);
                if (second != 0)
                {
                    throw new Win32Exception(unchecked((int)second));
                }
                int count = Marshal.ReadInt32(buffer);
                int offset = sizeof(int);
                int rowSize = Marshal.SizeOf(ipv6
                    ? typeof(MibTcp6RowOwnerPid)
                    : typeof(MibTcpRowOwnerPid));
                for (int index = 0; index < count; index++)
                {
                    IntPtr rowPointer = IntPtr.Add(buffer, offset + index * rowSize);
                    uint networkPort;
                    uint processId;
                    if (ipv6)
                    {
                        MibTcp6RowOwnerPid row = (MibTcp6RowOwnerPid)Marshal.PtrToStructure(
                            rowPointer,
                            typeof(MibTcp6RowOwnerPid));
                        networkPort = row.LocalPort;
                        processId = row.ProcessId;
                    }
                    else
                    {
                        MibTcpRowOwnerPid row = (MibTcpRowOwnerPid)Marshal.PtrToStructure(
                            rowPointer,
                            typeof(MibTcpRowOwnerPid));
                        networkPort = row.LocalPort;
                        processId = row.ProcessId;
                    }
                    short low = unchecked((short)(networkPort & 0xFFFF));
                    int hostPort = unchecked((ushort)IPAddress.NetworkToHostOrder(low));
                    addListener(hostPort, unchecked((int)processId));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcpRowOwnerPid
        {
            private uint State;
            private uint LocalAddress;
            internal uint LocalPort;
            private uint RemoteAddress;
            private uint RemotePort;
            internal uint ProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcp6RowOwnerPid
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            private byte[] LocalAddress;
            private uint LocalScopeId;
            internal uint LocalPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            private byte[] RemoteAddress;
            private uint RemoteScopeId;
            private uint RemotePort;
            private uint State;
            internal uint ProcessId;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr table,
            ref int size,
            bool order,
            int addressFamily,
            int tableClass,
            uint reserved);
    }
}
