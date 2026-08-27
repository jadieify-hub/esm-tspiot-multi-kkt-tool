using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LmServiceReadinessProbe
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint ToolhelpSnapshotProcess = 0x00000002;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        private readonly IWindowsServiceApi _serviceApi;
        private readonly TcpListenerOwnerReader _listeners;
        private readonly OfficialControllerLocator _controllerLocator;
        private readonly ManagedServiceManifestStore _manifestStore;
        private readonly ControllerCapabilityProfile _profile;

        internal LmServiceReadinessProbe(
            IWindowsServiceApi serviceApi,
            TcpListenerOwnerReader listeners,
            OfficialControllerLocator controllerLocator,
            ManagedServiceManifestStore manifestStore,
            ControllerCapabilityProfile profile)
        {
            _serviceApi = serviceApi ?? throw new ArgumentNullException("serviceApi");
            _listeners = listeners ?? throw new ArgumentNullException("listeners");
            _controllerLocator = controllerLocator ?? throw new ArgumentNullException("controllerLocator");
            _manifestStore = manifestStore ?? throw new ArgumentNullException("manifestStore");
            _profile = profile ?? throw new ArgumentNullException("profile");
        }

        internal LmReadinessResult WaitUntilReady(
            LmServiceProvisioningItemRequest item,
            string serviceSid,
            int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            string last = "Служба контроллера ЛМ не перешла в готовое состояние.";
            do
            {
                try
                {
                    LmReadinessResult current = ProbeOnce(item, serviceSid);
                    if (current.IsReady)
                    {
                        return current;
                    }
                    last = current.Message;
                }
                catch (Exception ex)
                {
                    last = "Проверка готовности: " + ex.GetType().Name + ".";
                }
                Thread.Sleep(250);
            }
            while (DateTime.UtcNow < deadline);
            return LmReadinessResult.Failed(last);
        }

        internal LmReadinessResult ProbeOnce(
            LmServiceProvisioningItemRequest item,
            string serviceSid)
        {
            string serviceName = LmServiceIdentity.CreateName(item.KktSerial);
            WindowsServiceRecord service = _serviceApi.Query(serviceName);
            if (service == null || service.State != WindowsServiceState.Running ||
                service.ProcessId <= 0)
            {
                return LmReadinessResult.Failed("Служба контроллера ЛМ не запущена.");
            }

            IList<int> grpcOwners = _listeners.FindListenerProcessIds(item.GrpcPort);
            IList<int> restOwners = _listeners.FindListenerProcessIds(item.RestPort);
            if (grpcOwners.Count != 1 || restOwners.Count != 1 ||
                grpcOwners[0] != restOwners[0])
            {
                return LmReadinessResult.Failed(
                    "Локальные listener-порты принадлежат разным или посторонним процессам.");
            }

            int childProcessId = grpcOwners[0];
            if (childProcessId == service.ProcessId ||
                GetParentProcessId(childProcessId) != service.ProcessId)
            {
                return LmReadinessResult.Failed(
                    "Listener не принадлежит проверенному дочернему процессу службы.");
            }

            VerifiedControllerBinaryResult controller = _controllerLocator.ResolveVerifiedBinary();
            if (!controller.IsSuccess)
            {
                return LmReadinessResult.Failed(controller.ErrorMessage);
            }
            string processPath = GetProcessPath(childProcessId);
            if (!string.Equals(
                Path.GetFullPath(processPath),
                Path.GetFullPath(controller.Binary.FullPath),
                StringComparison.OrdinalIgnoreCase))
            {
                return LmReadinessResult.Failed(
                    "Listener принадлежит не проверенному бинарному файлу контроллера.");
            }

            try
            {
                ValidateGeneratedArtifacts(item.KktSerial, serviceSid);
            }
            catch (Exception ex)
            {
                return LmReadinessResult.Failed(
                    "Контроллер не подготовил независимые сертификаты: " + ex.Message);
            }

            return LmReadinessResult.Ready();
        }

        internal bool WaitUntilStopped(
            LmServiceProvisioningItemRequest item,
            int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            string serviceName = LmServiceIdentity.CreateName(item.KktSerial);
            do
            {
                WindowsServiceRecord service = _serviceApi.Query(serviceName);
                bool serviceStopped = service == null ||
                    (service.State == WindowsServiceState.Stopped && service.ProcessId == 0);
                bool listenersStopped =
                    _listeners.FindListenerProcessIds(item.GrpcPort).Count == 0 &&
                    _listeners.FindListenerProcessIds(item.RestPort).Count == 0;
                if (serviceStopped && listenersStopped)
                {
                    return true;
                }
                Thread.Sleep(250);
            }
            while (DateTime.UtcNow < deadline);
            return false;
        }

        private void ValidateGeneratedArtifacts(string kktSerial, string serviceSid)
        {
            string currentVendorRoot = Path.Combine(
                _manifestStore.GetProfileRoot(kktSerial),
                _profile.VendorProfileRelativePath);
            for (int index = 0; index < _profile.GeneratedArtifactFileNames.Count; index++)
            {
                string path = Path.Combine(
                    currentVendorRoot,
                    _profile.GeneratedArtifactFileNames[index]);
                _manifestStore.ValidateProfileContentPath(kktSerial, serviceSid, path);
                FileInfo file = new FileInfo(path);
                if (!file.Exists || file.Length == 0 ||
                    (file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Не найден ожидаемый runtime-артефакт.");
                }
            }

            string currentCa = ComputeSha256(Path.Combine(currentVendorRoot, "ca.crt"));
            IList<string> serials = _manifestStore.ReadManagedSerials();
            for (int index = 0; index < serials.Count; index++)
            {
                if (string.Equals(serials[index], kktSerial, StringComparison.Ordinal))
                {
                    continue;
                }
                ManagedServiceManifest other = _manifestStore.Read(serials[index]);
                string otherCaPath = Path.Combine(
                    _manifestStore.GetProfileRoot(serials[index]),
                    _profile.VendorProfileRelativePath,
                    "ca.crt");
                if (File.Exists(otherCaPath))
                {
                    _manifestStore.ValidateProfileContentPath(
                        serials[index],
                        other.ServiceSid,
                        otherCaPath);
                    if (string.Equals(currentCa, ComputeSha256(otherCaPath), StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "CA совпадает с другим управляемым профилем.");
                    }
                }
            }
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(stream);
                StringBuilder result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    result.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                return result.ToString();
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
                throw new InvalidOperationException("Дочерний процесс уже завершился.");
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
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(
            IntPtr process,
            uint flags,
            StringBuilder path,
            ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
