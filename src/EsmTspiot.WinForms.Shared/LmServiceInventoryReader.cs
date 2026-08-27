using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LmServiceInventoryReader
    {
        private const string OwnershipMarker = "KRS.MultiKKT.LmGateway.Managed.v1";
        private const string OfficialServiceName = "esm-lm-controller";
        private readonly string _inventoryRoot;
        private readonly string _profilesRoot;
        private readonly string _helperPath;
        private readonly ReadOnlyWindowsServiceReader _services;

        internal LmServiceInventoryReader()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "KRS",
                "MultiKKT");
            _inventoryRoot = Path.Combine(root, "Inventory");
            _profilesRoot = Path.Combine(root, "Profiles");
            _helperPath = new ProvisionerProcessLauncher().HelperPath;
            _services = new ReadOnlyWindowsServiceReader();
        }

        internal IList<LmServiceInventoryItem> Read()
        {
            List<LmServiceInventoryItem> result = new List<LmServiceInventoryItem>();
            AddOfficialService(result);
            if (!Directory.Exists(_inventoryRoot))
            {
                return result;
            }
            EnsureRegularDirectory(_inventoryRoot);
            string[] directories = Directory.GetDirectories(_inventoryRoot);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < directories.Length; index++)
            {
                result.Add(ReadManagedProjection(directories[index]));
            }
            return result;
        }

        private LmServiceInventoryItem ReadManagedProjection(string directory)
        {
            string directoryName = Path.GetFileName(directory);
            string serial;
            bool derivedName = LmServiceIdentity.TryParseName(directoryName, out serial);
            string manifestPath = Path.Combine(directory, "manifest.json");
            LmManifestFingerprint fingerprint = File.Exists(manifestPath)
                ? new LmManifestFingerprint { Sha256 = ComputeSha256(manifestPath) }
                : null;
            try
            {
                EnsureRegularDirectory(directory);
                EnsureRegularFile(manifestPath);
                InventoryManifest manifest = ReadManifest(manifestPath);
                ValidateManifest(manifest, directoryName);
                ReadOnlyWindowsService service = _services.Query(manifest.ServiceName);
                LmServiceProvisioningStatus status = MapStatus(manifest.LocalLifecycleState);
                if (service == null)
                {
                    status = LmServiceProvisioningStatus.CleanupPending;
                }
                bool exactService = service != null && MatchesManagedService(manifest, service);
                if (service != null && !exactService)
                {
                    return CreateAttention(
                        manifest.KktSerial,
                        manifest.ServiceName,
                        fingerprint,
                        "SCM-служба не совпадает с управляемым манифестом.",
                        LmServiceRole.Foreign);
                }
                bool helperVersionChanged = service != null &&
                    !LmControllerFileIdentity.FixedTimeEquals(
                        manifest.SupervisorSha256,
                        ComputeSha256(_helperPath));
                if (helperVersionChanged && status == LmServiceProvisioningStatus.Succeeded)
                {
                    status = LmServiceProvisioningStatus.RequiresAttention;
                }
                return new LmServiceInventoryItem
                {
                    KktSerial = manifest.KktSerial,
                    ServiceName = manifest.ServiceName,
                    Role = LmServiceRole.Managed,
                    Ports = new LmGatewayPorts(manifest.GrpcPort, manifest.RestPort),
                    Target = new LmGatewayTarget(manifest.TargetAddress, manifest.TargetPort),
                    IsRunning = service != null && service.State == 4,
                    Status = status,
                    ManifestFingerprint = fingerprint,
                    Message = service == null
                        ? "Служба уже отсутствует; требуется очистить локальные данные."
                        : helperVersionChanged
                            ? "Версия helper изменилась; требуется сверить управляемый экземпляр."
                            : string.Empty
                };
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException) &&
                    !(ex is InvalidDataException) && !(ex is ArgumentException) &&
                    !(ex is Win32Exception) && !(ex is CryptographicException))
                {
                    throw;
                }
                return CreateAttention(
                    derivedName ? serial : string.Empty,
                    directoryName,
                    fingerprint,
                    "Запись инвентаря не прошла проверку: " + ex.GetType().Name + ".",
                    LmServiceRole.Unknown);
            }
        }

        private void AddOfficialService(ICollection<LmServiceInventoryItem> result)
        {
            ReadOnlyWindowsService service;
            try
            {
                service = _services.Query(OfficialServiceName);
            }
            catch (Win32Exception)
            {
                return;
            }
            if (service == null)
            {
                return;
            }
            string expectedPath = LmControllerFileIdentity.GetOfficialControllerPath();
            bool trusted = LmControllerFileIdentity.IsSupportedController(expectedPath) &&
                string.Equals(
                    NormalizeExecutablePath(service.ImagePath),
                    Path.GetFullPath(expectedPath),
                    StringComparison.OrdinalIgnoreCase);
            result.Add(new LmServiceInventoryItem
            {
                KktSerial = string.Empty,
                ServiceName = OfficialServiceName,
                Role = trusted ? LmServiceRole.VerifiedOfficial : LmServiceRole.Foreign,
                IsRunning = service.State == 4,
                Status = trusted
                    ? LmServiceProvisioningStatus.Succeeded
                    : LmServiceProvisioningStatus.RequiresAttention,
                Message = trusted
                    ? "Официальная базовая служба контроллера ЛМ."
                    : "Базовая служба не совпадает с проверенной поставкой контроллера."
            });
        }

        private void ValidateManifest(InventoryManifest manifest, string directoryName)
        {
            if (manifest == null || manifest.SchemaVersion != 1 ||
                !string.Equals(manifest.OwnershipMarker, OwnershipMarker, StringComparison.Ordinal) ||
                !string.Equals(manifest.ServiceName, directoryName, StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.ServiceName,
                    LmServiceIdentity.CreateName(manifest.KktSerial),
                    StringComparison.Ordinal) ||
                manifest.GrpcPort < 1 || manifest.GrpcPort > 65535 ||
                manifest.RestPort < 1 || manifest.RestPort > 65535 ||
                manifest.GrpcPort == manifest.RestPort ||
                manifest.TargetPort < 1 || manifest.TargetPort > 65535 ||
                string.IsNullOrWhiteSpace(manifest.TargetAddress) ||
                !IsHex(manifest.ControllerBinarySha256, 64) ||
                !IsHex(manifest.SupervisorSha256, 64) ||
                string.IsNullOrWhiteSpace(manifest.ServiceSid) ||
                string.IsNullOrWhiteSpace(manifest.ControllerVersion) ||
                !IsGuidN(manifest.OperationId) ||
                !IsCanonicalTarget(manifest.TargetAddress, manifest.TargetPort))
            {
                throw new InvalidDataException("Манифест имеет неверную схему или идентичность.");
            }
            string expectedProfile = Path.Combine(_profilesRoot, manifest.ServiceName);
            if (!string.Equals(
                    Path.GetFullPath(manifest.SupervisorImagePath),
                    Path.GetFullPath(_helperPath),
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    Path.GetFullPath(manifest.ProfilePath),
                    Path.GetFullPath(expectedProfile),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Манифест содержит неканонический путь.");
            }
        }

        private bool MatchesManagedService(
            InventoryManifest manifest,
            ReadOnlyWindowsService service)
        {
            string expectedImage = Quote(_helperPath) + " --supervise " + manifest.ServiceName;
            return string.Equals(service.ServiceName, manifest.ServiceName, StringComparison.Ordinal) &&
                string.Equals(service.ImagePath, expectedImage, StringComparison.Ordinal) &&
                string.Equals(
                    service.Description,
                    OwnershipMarker + ":" + manifest.KktSerial,
                    StringComparison.Ordinal) &&
                string.Equals(service.AccountName, "LocalSystem", StringComparison.OrdinalIgnoreCase) &&
                service.StartType == 2 &&
                service.Dependencies.Count == 0 &&
                LmControllerFileIdentity.FixedTimeEquals(
                    ComputeSha256(_helperPath),
                    ProvisionerIntegrity.ExpectedSha256);
        }

        private static LmServiceInventoryItem CreateAttention(
            string serial,
            string serviceName,
            LmManifestFingerprint fingerprint,
            string message,
            LmServiceRole role)
        {
            return new LmServiceInventoryItem
            {
                KktSerial = serial,
                ServiceName = serviceName,
                Role = role,
                Status = LmServiceProvisioningStatus.RequiresAttention,
                ManifestFingerprint = fingerprint,
                Message = message
            };
        }

        private static LmServiceProvisioningStatus MapStatus(int state)
        {
            if (state == 5 || state == 6 || state == 7)
            {
                return LmServiceProvisioningStatus.CleanupPending;
            }
            if (state == 4)
            {
                return LmServiceProvisioningStatus.VersionVerificationPending;
            }
            return state == 3
                ? LmServiceProvisioningStatus.Succeeded
                : LmServiceProvisioningStatus.RequiresAttention;
        }

        private static InventoryManifest ReadManifest(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                return (InventoryManifest)new DataContractJsonSerializer(
                    typeof(InventoryManifest)).ReadObject(stream);
            }
        }

        private static void EnsureRegularDirectory(string path)
        {
            if (!Directory.Exists(path) ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Инвентарь содержит reparse point.");
            }
        }

        private static void EnsureRegularFile(string path)
        {
            if (!File.Exists(path) ||
                (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            {
                throw new InvalidDataException("Файл манифеста отсутствует или небезопасен.");
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string NormalizeExecutablePath(string imagePath)
        {
            string value = (imagePath ?? string.Empty).Trim();
            if (value.Length >= 2 && value[0] == '"')
            {
                int closing = value.IndexOf('"', 1);
                if (closing > 0)
                {
                    value = value.Substring(1, closing - 1);
                }
            }
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
        }

        internal static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                StringBuilder result = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    result.Append(hash[index].ToString("x2"));
                }
                return result.ToString();
            }
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length)
            {
                return false;
            }
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!((current >= '0' && current <= '9') ||
                      (current >= 'a' && current <= 'f') ||
                      (current >= 'A' && current <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsGuidN(string value)
        {
            Guid parsed;
            return value != null && value.Length == 32 &&
                Guid.TryParseExact(value, "N", out parsed);
        }

        private static bool IsCanonicalTarget(string address, int port)
        {
            string normalized;
            bool loopback;
            return LmGatewayInputValidator.TryNormalizeTargetAddress(
                    address,
                    out normalized,
                    out loopback) &&
                string.Equals(address, normalized, StringComparison.Ordinal) &&
                port >= 1 && port <= 65535;
        }

        [DataContract]
        private sealed class InventoryManifest
        {
            [DataMember(Order = 1)] public int SchemaVersion { get; set; }
            [DataMember(Order = 2)] public string OwnershipMarker { get; set; }
            [DataMember(Order = 3)] public string KktSerial { get; set; }
            [DataMember(Order = 4)] public string ServiceName { get; set; }
            [DataMember(Order = 5)] public int GrpcPort { get; set; }
            [DataMember(Order = 6)] public int RestPort { get; set; }
            [DataMember(Order = 7)] public string TargetAddress { get; set; }
            [DataMember(Order = 8)] public int TargetPort { get; set; }
            [DataMember(Order = 9)] public string ControllerVersion { get; set; }
            [DataMember(Order = 10)] public string ControllerBinarySha256 { get; set; }
            [DataMember(Order = 11)] public string SupervisorSha256 { get; set; }
            [DataMember(Order = 12)] public string ServiceSid { get; set; }
            [DataMember(Order = 13)] public string OperationId { get; set; }
            [DataMember(Order = 14)] public int LocalLifecycleState { get; set; }
            [DataMember(Order = 15)] public string LastCleanupErrorClass { get; set; }
            [DataMember(Order = 16)] public string UpdatedUtc { get; set; }
            [DataMember(Order = 17)] public string SupervisorImagePath { get; set; }
            [DataMember(Order = 18)] public string ProfilePath { get; set; }
        }
    }

    internal sealed class ReadOnlyWindowsService
    {
        internal string ServiceName { get; set; }
        internal string ImagePath { get; set; }
        internal string Description { get; set; }
        internal string AccountName { get; set; }
        internal uint StartType { get; set; }
        internal IList<string> Dependencies { get; set; }
        internal uint State { get; set; }
        internal int ProcessId { get; set; }
    }

    internal sealed class ReadOnlyWindowsServiceReader
    {
        private const uint ScManagerConnect = 0x0001;
        private const uint ServiceQueryConfig = 0x0001;
        private const uint ServiceQueryStatus = 0x0004;
        private const uint ScStatusProcessInfo = 0;
        private const uint ServiceConfigDescription = 1;
        private const int ErrorInsufficientBuffer = 122;
        private const int ErrorServiceDoesNotExist = 1060;

        internal ReadOnlyWindowsService Query(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName) || serviceName.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("Имя службы недопустимо.", "serviceName");
            }
            using (SafeServiceHandle manager = OpenSCManagerW(null, null, ScManagerConnect))
            {
                ThrowIfInvalid(manager);
                using (SafeServiceHandle service = OpenServiceW(
                    manager,
                    serviceName,
                    ServiceQueryConfig | ServiceQueryStatus))
                {
                    if (service.IsInvalid)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == ErrorServiceDoesNotExist)
                        {
                            return null;
                        }
                        throw new Win32Exception(error);
                    }
                    ServiceConfigSnapshot config = ReadConfig(service);
                    ServiceStatusProcess status = ReadStatus(service);
                    return new ReadOnlyWindowsService
                    {
                        ServiceName = serviceName,
                        ImagePath = config.ImagePath,
                        Description = ReadDescription(service),
                        AccountName = config.AccountName,
                        StartType = config.StartType,
                        Dependencies = config.Dependencies,
                        State = status.CurrentState,
                        ProcessId = unchecked((int)status.ProcessId)
                    };
                }
            }
        }

        private static ServiceConfigSnapshot ReadConfig(SafeServiceHandle service)
        {
            int required;
            QueryServiceConfigW(service, IntPtr.Zero, 0, out required);
            if (required <= 0 || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            IntPtr buffer = Marshal.AllocHGlobal(required);
            try
            {
                if (!QueryServiceConfigW(service, buffer, required, out required))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                QueryServiceConfig config = (QueryServiceConfig)Marshal.PtrToStructure(
                    buffer,
                    typeof(QueryServiceConfig));
                return new ServiceConfigSnapshot
                {
                    StartType = config.StartType,
                    ImagePath = PtrToString(config.BinaryPathName),
                    AccountName = PtrToString(config.ServiceStartName),
                    Dependencies = ReadMultiString(config.Dependencies)
                };
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static ServiceStatusProcess ReadStatus(SafeServiceHandle service)
        {
            int required;
            ServiceStatusProcess status;
            if (!QueryServiceStatusEx(
                service,
                ScStatusProcessInfo,
                out status,
                Marshal.SizeOf(typeof(ServiceStatusProcess)),
                out required))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return status;
        }

        private static string ReadDescription(SafeServiceHandle service)
        {
            int required;
            QueryServiceConfig2W(service, ServiceConfigDescription, IntPtr.Zero, 0, out required);
            if (required <= 0)
            {
                return string.Empty;
            }
            IntPtr buffer = Marshal.AllocHGlobal(required);
            try
            {
                if (!QueryServiceConfig2W(
                    service,
                    ServiceConfigDescription,
                    buffer,
                    required,
                    out required))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                ServiceDescription description = (ServiceDescription)Marshal.PtrToStructure(
                    buffer,
                    typeof(ServiceDescription));
                return PtrToString(description.Description);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static IList<string> ReadMultiString(IntPtr pointer)
        {
            List<string> result = new List<string>();
            int offset = 0;
            while (pointer != IntPtr.Zero)
            {
                string value = Marshal.PtrToStringUni(IntPtr.Add(pointer, offset));
                if (string.IsNullOrEmpty(value))
                {
                    break;
                }
                result.Add(value);
                offset += (value.Length + 1) * 2;
            }
            return result;
        }

        private static string PtrToString(IntPtr value)
        {
            return value == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(value);
        }

        private static void ThrowIfInvalid(SafeServiceHandle handle)
        {
            if (handle == null || handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private sealed class ServiceConfigSnapshot
        {
            internal uint StartType { get; set; }
            internal string ImagePath { get; set; }
            internal string AccountName { get; set; }
            internal IList<string> Dependencies { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct QueryServiceConfig
        {
            internal uint ServiceType;
            internal uint StartType;
            internal uint ErrorControl;
            internal IntPtr BinaryPathName;
            internal IntPtr LoadOrderGroup;
            internal uint TagId;
            internal IntPtr Dependencies;
            internal IntPtr ServiceStartName;
            internal IntPtr DisplayName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatusProcess
        {
            private uint ServiceType;
            internal uint CurrentState;
            private uint ControlsAccepted;
            private uint Win32ExitCode;
            private uint ServiceSpecificExitCode;
            private uint CheckPoint;
            private uint WaitHint;
            internal uint ProcessId;
            private uint ServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceDescription
        {
            internal IntPtr Description;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeServiceHandle OpenSCManagerW(
            string machineName,
            string databaseName,
            uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeServiceHandle OpenServiceW(
            SafeServiceHandle serviceManager,
            string serviceName,
            uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceConfigW(
            SafeServiceHandle service,
            IntPtr queryServiceConfig,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatusEx(
            SafeServiceHandle service,
            uint infoLevel,
            out ServiceStatusProcess status,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceConfig2W(
            SafeServiceHandle service,
            uint infoLevel,
            IntPtr buffer,
            int bufferSize,
            out int bytesNeeded);
    }

    internal static class LmControllerFileIdentity
    {
        private const string ExpectedSha256 =
            "9ce34999ea965e01d8328895bb1776e7b44edabf72fc51bee121ec5091746214";

        internal static string GetOfficialControllerPath()
        {
            string programFiles = Environment.GetEnvironmentVariable("ProgramW6432");
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            }
            return Path.Combine(programFiles, "ESP", "LMController", "bin", "lmcontroller.exe");
        }

        internal static bool IsSupportedController(string path)
        {
            try
            {
                FileInfo file = new FileInfo(path);
                return file.Exists && file.Length == 14647536 &&
                    (file.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    FixedTimeEquals(LmServiceInventoryReader.ComputeSha256(path), ExpectedSha256);
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException ||
                    ex is CryptographicException || ex is ArgumentException)
                {
                    return false;
                }
                throw;
            }
        }

        internal static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= char.ToUpperInvariant(left[index]) ^
                    char.ToUpperInvariant(right[index]);
            }
            return difference == 0;
        }
    }

    internal sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeServiceHandle()
            : base(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return CloseServiceHandle(handle);
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr serviceHandle);
    }
}
