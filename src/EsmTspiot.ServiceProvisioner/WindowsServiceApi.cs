using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class WindowsServiceApi : IWindowsServiceApi
    {
        private const uint ScManagerConnect = 0x0001;
        private const uint ScManagerCreateService = 0x0002;
        private const uint ServiceQueryConfig = 0x0001;
        private const uint ServiceChangeConfig = 0x0002;
        private const uint ServiceQueryStatus = 0x0004;
        private const uint ServiceStart = 0x0010;
        private const uint ServiceStop = 0x0020;
        private const uint DeleteAccess = 0x00010000;
        private const uint ReadControl = 0x00020000;
        private const uint WriteDac = 0x00040000;
        private const uint ServiceWin32OwnProcess = 0x00000010;
        private const uint ServiceNoChange = 0xFFFFFFFF;
        private const uint ScStatusProcessInfo = 0;
        private const uint ServiceControlStop = 1;
        private const uint ServiceConfigDescription = 1;
        private const uint ServiceConfigFailureActions = 2;
        private const uint ServiceConfigServiceSidInfo = 5;
        private const uint DaclSecurityInformation = 0x00000004;
        private const int ErrorInsufficientBuffer = 122;
        private const int ErrorServiceDoesNotExist = 1060;
        private const int ScActionRestart = 1;

        public WindowsServiceRecord Query(string serviceName)
        {
            ValidateExactName(serviceName);
            using (SafeServiceHandle manager = OpenManager(ScManagerConnect))
            using (SafeServiceHandle service = OpenServiceW(
                manager,
                serviceName,
                ServiceQueryConfig | ServiceQueryStatus | ReadControl))
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
                return new WindowsServiceRecord
                {
                    ServiceName = serviceName,
                    DisplayName = config.DisplayName,
                    ImagePath = config.BinaryPathName,
                    Description = ReadDescription(service),
                    AccountName = config.ServiceStartName,
                    Dependencies = config.Dependencies,
                    EnvironmentVariables = ReadEnvironment(serviceName),
                    StartMode = (WindowsServiceStartMode)config.StartType,
                    ErrorControl = (WindowsServiceErrorControl)config.ErrorControl,
                    ServiceSidType = ReadServiceSidType(service),
                    RecoveryPolicy = ReadRecoveryPolicy(service),
                    SecurityDescriptor = ReadSecurityDescriptor(service),
                    State = (WindowsServiceState)status.CurrentState,
                    ProcessId = unchecked((int)status.ProcessId)
                };
            }
        }

        public void Create(WindowsServiceDefinition definition)
        {
            definition.Validate();
            string dependencies = ToMultiString(definition.Dependencies);
            uint access = ServiceChangeConfig | ServiceStart | DeleteAccess |
                ReadControl | WriteDac;
            using (SafeServiceHandle manager = OpenManager(ScManagerConnect | ScManagerCreateService))
            using (SafeServiceHandle service = CreateServiceW(
                manager,
                definition.ServiceName,
                definition.DisplayName,
                access,
                ServiceWin32OwnProcess,
                (uint)definition.StartMode,
                (uint)definition.ErrorControl,
                definition.ImagePath,
                null,
                IntPtr.Zero,
                dependencies,
                null,
                null))
            {
                ThrowIfInvalid(service);
                try
                {
                    ApplyAuxiliaryConfiguration(service, definition);
                }
                catch (Exception configurationException)
                {
                    if (!DeleteService(service))
                    {
                        Exception cleanupException = new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "Failed to roll back a partially configured service.");
                        throw new InvalidOperationException(
                            "Service creation failed and its partial SCM record could not be removed.",
                            new AggregateException(
                                configurationException,
                                cleanupException));
                    }
                    throw;
                }
            }
        }

        public void Update(WindowsServiceDefinition definition)
        {
            definition.Validate();
            uint access = ServiceChangeConfig | ServiceStart | ReadControl | WriteDac;
            using (SafeServiceHandle manager = OpenManager(ScManagerConnect))
            using (SafeServiceHandle service = OpenRequiredService(manager, definition.ServiceName, access))
            {
                string dependencies = ToMultiString(definition.Dependencies);
                if (!ChangeServiceConfigW(
                    service,
                    ServiceNoChange,
                    (uint)definition.StartMode,
                    (uint)definition.ErrorControl,
                    definition.ImagePath,
                    null,
                    IntPtr.Zero,
                    dependencies,
                    definition.AccountName,
                    null,
                    definition.DisplayName))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                ApplyAuxiliaryConfiguration(service, definition);
            }
        }

        public void SetStartMode(
            string serviceName,
            WindowsServiceStartMode startMode)
        {
            ValidateExactName(serviceName);
            using (SafeServiceHandle manager = OpenManager(ScManagerConnect))
            using (SafeServiceHandle service = OpenRequiredService(
                manager,
                serviceName,
                ServiceChangeConfig))
            {
                // Only the start type changes; every other field stays
                // SERVICE_NO_CHANGE/null so the vendor image path, account,
                // dependencies, and display name are left untouched.
                if (!ChangeServiceConfigW(
                    service,
                    ServiceNoChange,
                    (uint)startMode,
                    ServiceNoChange,
                    null,
                    null,
                    IntPtr.Zero,
                    null,
                    null,
                    null,
                    null))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        public void Start(string serviceName)
        {
            ValidateExactName(serviceName);
            using (SafeServiceHandle manager = OpenManager(ScManagerConnect))
            using (SafeServiceHandle service = OpenRequiredService(manager, serviceName, ServiceStart))
            {
                if (!StartServiceW(service, 0, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        public void RequestStop(string serviceName)
        {
            ValidateExactName(serviceName);
            using (SafeServiceHandle manager = OpenManager(ScManagerConnect))
            using (SafeServiceHandle service = OpenRequiredService(
                manager,
                serviceName,
                ServiceStop | ServiceQueryStatus))
            {
                ServiceStatus status;
                if (!ControlService(service, ServiceControlStop, out status))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        public void Delete(string serviceName)
        {
            ValidateExactName(serviceName);
            using (SafeServiceHandle manager = OpenManager(ScManagerConnect))
            using (SafeServiceHandle service = OpenRequiredService(manager, serviceName, DeleteAccess))
            {
                if (!DeleteService(service))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        internal static bool CloseNativeServiceHandle(IntPtr handle)
        {
            return CloseServiceHandle(handle);
        }

        private static SafeServiceHandle OpenManager(uint access)
        {
            SafeServiceHandle handle = OpenSCManagerW(null, null, access);
            ThrowIfInvalid(handle);
            return handle;
        }

        private static SafeServiceHandle OpenRequiredService(
            SafeServiceHandle manager,
            string serviceName,
            uint access)
        {
            SafeServiceHandle handle = OpenServiceW(manager, serviceName, access);
            ThrowIfInvalid(handle);
            return handle;
        }

        private static void ApplyAuxiliaryConfiguration(
            SafeServiceHandle service,
            WindowsServiceDefinition definition)
        {
            SetDescription(service, definition.Description);
            SetRecoveryPolicy(service, definition.RecoveryPolicy);
            SetServiceSidType(service, definition.ServiceSidType);
            SetSecurityDescriptor(service, definition.SecurityDescriptor);
            if (definition.Kind == WindowsServiceDefinitionKind.DirectController)
            {
                WriteEnvironment(definition.ServiceName, definition.EnvironmentVariables);
            }
            ServiceSecurityDescriptor observed = ReadSecurityDescriptor(service);
            if (!observed.IsRestrictive ||
                !string.Equals(
                    observed.Sddl,
                    definition.SecurityDescriptor.Sddl,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Service DACL verification failed after write.");
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
                    ErrorControl = config.ErrorControl,
                    BinaryPathName = PtrToString(config.BinaryPathName),
                    Dependencies = ReadMultiString(config.Dependencies),
                    ServiceStartName = PtrToString(config.ServiceStartName),
                    DisplayName = PtrToString(config.DisplayName)
                };
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static ServiceStatusProcess ReadStatus(SafeServiceHandle service)
        {
            int size = Marshal.SizeOf(typeof(ServiceStatusProcess));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int required;
                if (!QueryServiceStatusEx(service, ScStatusProcessInfo, buffer, size, out required))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return (ServiceStatusProcess)Marshal.PtrToStructure(
                    buffer,
                    typeof(ServiceStatusProcess));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string ReadDescription(SafeServiceHandle service)
        {
            IntPtr buffer = ReadConfig2Buffer(service, ServiceConfigDescription);
            try
            {
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

        private static WindowsServiceSidType ReadServiceSidType(SafeServiceHandle service)
        {
            IntPtr buffer = ReadConfig2Buffer(service, ServiceConfigServiceSidInfo);
            try
            {
                ServiceSidInfo info = (ServiceSidInfo)Marshal.PtrToStructure(
                    buffer,
                    typeof(ServiceSidInfo));
                return (WindowsServiceSidType)info.ServiceSidType;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static WindowsServiceRecoveryPolicy ReadRecoveryPolicy(SafeServiceHandle service)
        {
            IntPtr buffer = ReadConfig2Buffer(service, ServiceConfigFailureActions);
            try
            {
                ServiceFailureActions actions = (ServiceFailureActions)Marshal.PtrToStructure(
                    buffer,
                    typeof(ServiceFailureActions));
                if (actions.ActionCount < 0 || actions.ActionCount > 16 ||
                    (actions.ActionCount > 0 && actions.Actions == IntPtr.Zero))
                {
                    throw new InvalidOperationException("Invalid service recovery policy observed.");
                }
                List<int> restartDelays = new List<int>();
                int actionSize = Marshal.SizeOf(typeof(ServiceAction));
                for (int index = 0; index < actions.ActionCount; index++)
                {
                    ServiceAction action = (ServiceAction)Marshal.PtrToStructure(
                        IntPtr.Add(actions.Actions, index * actionSize),
                        typeof(ServiceAction));
                    if (action.Type != ScActionRestart)
                    {
                        throw new InvalidOperationException("Unsupported service recovery action observed.");
                    }
                    restartDelays.Add(unchecked((int)action.Delay));
                }
                return new WindowsServiceRecoveryPolicy(
                    unchecked((int)actions.ResetPeriod),
                    restartDelays);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static IntPtr ReadConfig2Buffer(SafeServiceHandle service, uint infoLevel)
        {
            int required;
            QueryServiceConfig2W(service, infoLevel, IntPtr.Zero, 0, out required);
            if (required <= 0 || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            IntPtr buffer = Marshal.AllocHGlobal(required);
            if (!QueryServiceConfig2W(service, infoLevel, buffer, required, out required))
            {
                int error = Marshal.GetLastWin32Error();
                Marshal.FreeHGlobal(buffer);
                throw new Win32Exception(error);
            }
            return buffer;
        }

        private static void SetDescription(SafeServiceHandle service, string value)
        {
            IntPtr text = Marshal.StringToHGlobalUni(value);
            IntPtr buffer = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(buffer, text);
                if (!ChangeServiceConfig2W(service, ServiceConfigDescription, buffer))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                Marshal.FreeHGlobal(text);
            }
        }

        private static void SetRecoveryPolicy(
            SafeServiceHandle service,
            WindowsServiceRecoveryPolicy policy)
        {
            int actionSize = Marshal.SizeOf(typeof(ServiceAction));
            IntPtr actionBuffer = Marshal.AllocHGlobal(actionSize * policy.RestartDelaysMilliseconds.Count);
            IntPtr failureBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ServiceFailureActions)));
            try
            {
                for (int index = 0; index < policy.RestartDelaysMilliseconds.Count; index++)
                {
                    ServiceAction action = new ServiceAction
                    {
                        Type = ScActionRestart,
                        Delay = unchecked((uint)policy.RestartDelaysMilliseconds[index])
                    };
                    Marshal.StructureToPtr(action, IntPtr.Add(actionBuffer, index * actionSize), false);
                }
                ServiceFailureActions actions = new ServiceFailureActions
                {
                    ResetPeriod = unchecked((uint)policy.ResetPeriodSeconds),
                    RebootMessage = IntPtr.Zero,
                    Command = IntPtr.Zero,
                    ActionCount = policy.RestartDelaysMilliseconds.Count,
                    Actions = actionBuffer
                };
                Marshal.StructureToPtr(actions, failureBuffer, false);
                if (!ChangeServiceConfig2W(service, ServiceConfigFailureActions, failureBuffer))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(failureBuffer);
                Marshal.FreeHGlobal(actionBuffer);
            }
        }

        private static void SetServiceSidType(
            SafeServiceHandle service,
            WindowsServiceSidType sidType)
        {
            ServiceSidInfo info = new ServiceSidInfo { ServiceSidType = (uint)sidType };
            IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ServiceSidInfo)));
            try
            {
                Marshal.StructureToPtr(info, buffer, false);
                if (!ChangeServiceConfig2W(service, ServiceConfigServiceSidInfo, buffer))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static void SetSecurityDescriptor(
            SafeServiceHandle service,
            ServiceSecurityDescriptor descriptor)
        {
            byte[] binary = descriptor.ToBinary();
            GCHandle pinned = GCHandle.Alloc(binary, GCHandleType.Pinned);
            try
            {
                if (!SetServiceObjectSecurity(
                    service,
                    DaclSecurityInformation,
                    pinned.AddrOfPinnedObject()))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                pinned.Free();
            }
        }

        private static ServiceSecurityDescriptor ReadSecurityDescriptor(SafeServiceHandle service)
        {
            int required;
            QueryServiceObjectSecurity(
                service,
                DaclSecurityInformation,
                IntPtr.Zero,
                0,
                out required);
            if (required <= 0 || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            byte[] buffer = new byte[required];
            if (!QueryServiceObjectSecurity(
                service,
                DaclSecurityInformation,
                buffer,
                buffer.Length,
                out required))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            RawSecurityDescriptor raw = new RawSecurityDescriptor(buffer, 0);
            return ServiceSecurityDescriptor.Parse(raw.GetSddlForm(AccessControlSections.Access));
        }

        private static IList<string> ReadMultiString(IntPtr pointer)
        {
            List<string> values = new List<string>();
            if (pointer == IntPtr.Zero)
            {
                return values;
            }
            int offset = 0;
            while (true)
            {
                string value = Marshal.PtrToStringUni(IntPtr.Add(pointer, offset));
                if (string.IsNullOrEmpty(value))
                {
                    break;
                }
                values.Add(value);
                offset += (value.Length + 1) * 2;
            }
            return values;
        }

        private static string ToMultiString(IList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "\0";
            }
            for (int index = 0; index < values.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(values[index]) || values[index].IndexOf('\0') >= 0)
                {
                    throw new InvalidOperationException("Service dependency is invalid.");
                }
            }
            return string.Join("\0", new List<string>(values).ToArray()) + "\0\0";
        }

        private static string PtrToString(IntPtr pointer)
        {
            return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(pointer);
        }

        private static IDictionary<string, string> ReadEnvironment(string serviceName)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            using (RegistryKey machine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry64))
            using (RegistryKey service = machine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + serviceName,
                false))
            {
                string[] values = service == null
                    ? null
                    : service.GetValue(
                        "Environment",
                        null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames) as string[];
                if (values == null)
                {
                    return result;
                }
                for (int index = 0; index < values.Length; index++)
                {
                    string value = values[index] ?? string.Empty;
                    int separator = value.IndexOf('=');
                    if (separator <= 0 || separator == value.Length - 1)
                    {
                        throw new InvalidDataException(
                            "Service Environment registry value is malformed.");
                    }
                    string key = value.Substring(0, separator);
                    if (result.ContainsKey(key))
                    {
                        throw new InvalidDataException(
                            "Service Environment registry value contains duplicate keys.");
                    }
                    result.Add(key, value.Substring(separator + 1));
                }
            }
            return result;
        }

        private static void WriteEnvironment(
            string serviceName,
            IDictionary<string, string> environment)
        {
            string programData;
            if (environment == null || environment.Count != 1 ||
                !environment.TryGetValue("ProgramData", out programData) ||
                string.IsNullOrWhiteSpace(programData) || !Path.IsPathRooted(programData))
            {
                throw new InvalidOperationException(
                    "Direct controller service Environment is invalid.");
            }
            using (RegistryKey machine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry64))
            using (RegistryKey service = machine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + serviceName,
                true))
            {
                if (service == null)
                {
                    throw new InvalidOperationException(
                        "Direct controller service registry key is unavailable.");
                }
                service.SetValue(
                    "Environment",
                    new[] { "ProgramData=" + programData },
                    RegistryValueKind.MultiString);
            }
            IDictionary<string, string> observed = ReadEnvironment(serviceName);
            string observedProgramData;
            if (observed.Count != 1 ||
                !observed.TryGetValue("ProgramData", out observedProgramData) ||
                !string.Equals(programData, observedProgramData, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Direct controller service Environment verification failed.");
            }
        }

        private static void ValidateExactName(string serviceName)
        {
            string serial;
            if (!EsmTspiot.Shared.Services.LmServiceIdentity.TryParseName(
                    serviceName,
                    out serial) &&
                !LocalModuleServiceIdentity.IsManagedName(serviceName))
            {
                int directOrdinal;
                if (!EsmTspiot.Shared.Services.DirectControllerIdentity.TryParseServiceName(
                        serviceName,
                        out directOrdinal) &&
                    !IsMsiLocalModuleServiceName(serviceName))
                {
                    throw new ArgumentException(
                        "Managed service name is invalid.",
                        "serviceName");
                }
            }
        }

        private static bool IsMsiLocalModuleServiceName(string serviceName)
        {
            for (int ordinal = 0;
                ordinal <= EsmTspiot.Shared.Services.LocalModuleMsiIdentity
                    .MaximumCloneOrdinal;
                ordinal++)
            {
                if (string.Equals(
                        serviceName,
                        EsmTspiot.Shared.Services.LocalModuleMsiIdentity
                            .ApiServiceName(ordinal),
                        StringComparison.Ordinal) ||
                    string.Equals(
                        serviceName,
                        EsmTspiot.Shared.Services.LocalModuleMsiIdentity
                            .DatabaseServiceName(ordinal),
                        StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void ThrowIfInvalid(SafeServiceHandle handle)
        {
            if (handle == null || handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
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

        private sealed class ServiceConfigSnapshot
        {
            internal uint StartType { get; set; }
            internal uint ErrorControl { get; set; }
            internal string BinaryPathName { get; set; }
            internal IList<string> Dependencies { get; set; }
            internal string ServiceStartName { get; set; }
            internal string DisplayName { get; set; }
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
        private struct ServiceStatus
        {
            private uint ServiceType;
            private uint CurrentState;
            private uint ControlsAccepted;
            private uint Win32ExitCode;
            private uint ServiceSpecificExitCode;
            private uint CheckPoint;
            private uint WaitHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceDescription
        {
            internal IntPtr Description;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceSidInfo
        {
            internal uint ServiceSidType;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceFailureActions
        {
            internal uint ResetPeriod;
            internal IntPtr RebootMessage;
            internal IntPtr Command;
            internal int ActionCount;
            internal IntPtr Actions;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceAction
        {
            internal int Type;
            internal uint Delay;
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
        private static extern SafeServiceHandle CreateServiceW(
            SafeServiceHandle serviceManager,
            string serviceName,
            string displayName,
            uint desiredAccess,
            uint serviceType,
            uint startType,
            uint errorControl,
            string binaryPathName,
            string loadOrderGroup,
            IntPtr tagId,
            string dependencies,
            string serviceStartName,
            string password);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceConfigW(
            SafeServiceHandle service,
            IntPtr serviceConfig,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceConfig2W(
            SafeServiceHandle service,
            uint infoLevel,
            IntPtr buffer,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceStatusEx(
            SafeServiceHandle service,
            uint infoLevel,
            IntPtr buffer,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfigW(
            SafeServiceHandle service,
            uint serviceType,
            uint startType,
            uint errorControl,
            string binaryPathName,
            string loadOrderGroup,
            IntPtr tagId,
            string dependencies,
            string serviceStartName,
            string password,
            string displayName);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig2W(
            SafeServiceHandle service,
            uint infoLevel,
            IntPtr info);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool StartServiceW(
            SafeServiceHandle service,
            int argumentCount,
            IntPtr arguments);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ControlService(
            SafeServiceHandle service,
            uint control,
            out ServiceStatus status);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DeleteService(SafeServiceHandle service);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceObjectSecurity(
            SafeServiceHandle service,
            uint securityInformation,
            IntPtr securityDescriptor,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceObjectSecurity(
            SafeServiceHandle service,
            uint securityInformation,
            byte[] securityDescriptor,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceObjectSecurity(
            SafeServiceHandle service,
            uint securityInformation,
            IntPtr securityDescriptor);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr serviceHandle);
    }
}
