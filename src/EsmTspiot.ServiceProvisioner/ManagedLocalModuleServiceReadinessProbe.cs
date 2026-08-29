using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IProcessParentReader
    {
        int GetParentProcessId(int processId);
    }

    internal sealed class NativeProcessParentReader : IProcessParentReader
    {
        private const uint SnapshotProcesses = 0x00000002;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        public int GetParentProcessId(int processId)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException("processId");
            }
            IntPtr snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
            if (snapshot == InvalidHandle)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try
            {
                ProcessEntry entry = new ProcessEntry();
                entry.Size = unchecked((uint)Marshal.SizeOf(typeof(ProcessEntry)));
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
                return 0;
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
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
            private string ExecutableFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(
            uint flags,
            uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(
            IntPtr snapshot,
            ref ProcessEntry entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(
            IntPtr snapshot,
            ref ProcessEntry entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }

    internal sealed class ManagedLocalModuleServiceReadinessProbe :
        ILocalModuleServiceReadinessProbe
    {
        private const int DefaultAttempts = 180;
        private readonly IWindowsServiceApi _services;
        private readonly ITcpListenerOwnerReader _listeners;
        private readonly IProcessParentReader _parents;
        private readonly int _attempts;
        private readonly Action _delay;

        internal ManagedLocalModuleServiceReadinessProbe(
            IWindowsServiceApi services,
            ITcpListenerOwnerReader listeners,
            IProcessParentReader parents)
            : this(
                services,
                listeners,
                parents,
                DefaultAttempts,
                delegate { Thread.Sleep(250); })
        {
        }

        internal ManagedLocalModuleServiceReadinessProbe(
            IWindowsServiceApi services,
            ITcpListenerOwnerReader listeners,
            IProcessParentReader parents,
            int attempts,
            Action delay)
        {
            if (services == null) throw new ArgumentNullException("services");
            if (listeners == null) throw new ArgumentNullException("listeners");
            if (parents == null) throw new ArgumentNullException("parents");
            if (attempts < 1) throw new ArgumentOutOfRangeException("attempts");
            if (delay == null) throw new ArgumentNullException("delay");
            _services = services;
            _listeners = listeners;
            _parents = parents;
            _attempts = attempts;
            _delay = delay;
        }

        public void WaitUntilOwnedListener(
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role)
        {
            ValidationResult last = null;
            for (int attempt = 0; attempt < _attempts; attempt++)
            {
                last = ProbeOwnedListener(manifest, role);
                if (last.IsValid)
                {
                    return;
                }
                if (attempt + 1 < _attempts)
                {
                    _delay();
                }
            }
            throw new InvalidOperationException(
                "Служба ЛМ не подтвердила собственный порт: " +
                (last == null ? "неизвестная ошибка" : last.JoinMessages()));
        }

        public void WaitUntilStopped(
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role)
        {
            for (int attempt = 0; attempt < _attempts; attempt++)
            {
                if (IsStopped(manifest, role))
                {
                    return;
                }
                if (attempt + 1 < _attempts)
                {
                    _delay();
                }
            }
            throw new InvalidOperationException(
                "Служба ЛМ или её порт не остановились за отведённое время.");
        }

        internal ValidationResult ProbeOwnedListener(
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role)
        {
            ValidationResult result = new ValidationResult();
            string serviceName;
            int port;
            if (!TryResolve(manifest, role, out serviceName, out port))
            {
                result.Add("Манифест ЛМ не задаёт точную службу и порт.");
                return result;
            }
            WindowsServiceRecord service = _services.Query(serviceName);
            if (service == null ||
                service.State != WindowsServiceState.Running ||
                service.ProcessId <= 0)
            {
                result.Add("Служба ЛМ не находится в состоянии Running.");
                return result;
            }
            IList<int> owners = _listeners.FindListenerProcessIds(port);
            if (owners == null || owners.Count != 1 || owners[0] <= 0)
            {
                result.Add("Порт ЛМ не имеет одного однозначного владельца.");
                return result;
            }
            if (!IsSameOrDescendant(owners[0], service.ProcessId))
            {
                result.Add("Порт ЛМ принадлежит процессу вне дерева точной службы.");
            }
            return result;
        }

        private bool IsStopped(
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role)
        {
            string serviceName;
            int port;
            if (!TryResolve(manifest, role, out serviceName, out port))
            {
                throw new InvalidDataException(
                    "Local-module stop observation is invalid.");
            }
            WindowsServiceRecord service = _services.Query(serviceName);
            IList<int> owners = _listeners.FindListenerProcessIds(port);
            return (service == null || service.State == WindowsServiceState.Stopped) &&
                owners != null && owners.Count == 0;
        }

        private bool IsSameOrDescendant(int processId, int serviceProcessId)
        {
            int current = processId;
            HashSet<int> visited = new HashSet<int>();
            for (int depth = 0; depth < 8 && current > 0; depth++)
            {
                if (current == serviceProcessId)
                {
                    return true;
                }
                if (!visited.Add(current))
                {
                    return false;
                }
                current = _parents.GetParentProcessId(current);
            }
            return false;
        }

        private static bool TryResolve(
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role,
            out string serviceName,
            out int port)
        {
            if (manifest == null)
            {
                serviceName = string.Empty;
                port = 0;
                return false;
            }
            if (role == LocalModuleProcessRole.Database)
            {
                serviceName = manifest.DatabaseServiceName;
                port = manifest.DatabasePort;
            }
            else if (role == LocalModuleProcessRole.Api)
            {
                serviceName = manifest.ApiServiceName;
                port = manifest.ApiPort;
            }
            else
            {
                serviceName = string.Empty;
                port = 0;
                return false;
            }
            return !string.IsNullOrWhiteSpace(serviceName) &&
                port >= 1 && port <= 65535;
        }
    }
}
