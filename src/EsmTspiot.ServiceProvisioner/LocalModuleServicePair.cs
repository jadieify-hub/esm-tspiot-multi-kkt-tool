using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Threading;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleWindowsServicePair
    {
        internal const string DescriptionPrefix =
            "KRS.MultiKKT.LocalModule.Managed.v1:";

        private readonly IWindowsServiceApi _serviceApi;
        private readonly VerifiedProvisionerBinary _supervisorBinary;

        internal LocalModuleWindowsServicePair(
            IWindowsServiceApi serviceApi,
            VerifiedProvisionerBinary supervisorBinary)
        {
            if (serviceApi == null) throw new ArgumentNullException("serviceApi");
            if (supervisorBinary == null)
            {
                throw new ArgumentNullException("supervisorBinary");
            }
            _serviceApi = serviceApi;
            _supervisorBinary = supervisorBinary;
        }

        internal void EnsureConfigured(
            LocalModuleInstanceManifest manifest,
            string operatorSid)
        {
            WindowsServiceDefinition database = BuildDefinition(
                manifest,
                LocalModuleProcessRole.Database,
                operatorSid);
            WindowsServiceDefinition api = BuildDefinition(
                manifest,
                LocalModuleProcessRole.Api,
                operatorSid);
            EnsureOne(database);
            EnsureOne(api);
        }

        internal WindowsServiceDefinition BuildDefinition(
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role,
            string operatorSid)
        {
            ValidateManifestIdentity(manifest);
            if (role != LocalModuleProcessRole.Database &&
                role != LocalModuleProcessRole.Api)
            {
                throw new ArgumentOutOfRangeException("role");
            }
            string serviceName = LocalModuleServiceIdentity.CreateName(
                manifest.InstanceId,
                role);
            string roleText = role == LocalModuleProcessRole.Database
                ? "База"
                : "API";
            List<string> dependencies = new List<string>();
            if (role == LocalModuleProcessRole.Api)
            {
                dependencies.Add(manifest.DatabaseServiceName);
            }
            WindowsServiceDefinition definition = new WindowsServiceDefinition
            {
                ServiceName = serviceName,
                DisplayName = "KRS: ЛМ ЧЗ №" +
                    manifest.LocalModuleOrdinal.ToString(
                        CultureInfo.InvariantCulture) +
                    " — " + roleText + " (ИНН " + manifest.Inn + ")",
                ImagePath = WindowsCommandLine.QuoteArgument(
                    _supervisorBinary.FullPath) +
                    " --supervise-local-module " + serviceName,
                Description = DescriptionPrefix + manifest.InstanceId + ":" +
                    role.ToString() + ":" + manifest.OwnershipNonce,
                AccountName = "LocalSystem",
                Dependencies = dependencies,
                StartMode = WindowsServiceStartMode.AutoStart,
                ErrorControl = WindowsServiceErrorControl.Normal,
                ServiceSidType = WindowsServiceSidType.Restricted,
                RecoveryPolicy = new WindowsServiceRecoveryPolicy(
                    60,
                    new[] { 30000, 60000, 60000 }),
                SecurityDescriptor =
                    ServiceSecurityDescriptor.CreateRestrictive(operatorSid)
            };
            definition.Validate();
            return definition;
        }

        private void EnsureOne(WindowsServiceDefinition definition)
        {
            WindowsServiceRecord existing = _serviceApi.Query(
                definition.ServiceName);
            if (existing == null)
            {
                _serviceApi.Create(definition);
            }
            else
            {
                _serviceApi.Update(definition);
            }
            WindowsServiceRecord observed = _serviceApi.Query(
                definition.ServiceName);
            if (!WindowsServiceDefinitionMatcher.Matches(
                    definition,
                    observed))
            {
                throw new InvalidDataException(
                    "SCM did not retain the exact local-module service definition.");
            }
        }

        private static void ValidateManifestIdentity(
            LocalModuleInstanceManifest manifest)
        {
            if (manifest == null ||
                !LocalModuleManagedIdentity.IsInstanceId(manifest.InstanceId) ||
                !string.Equals(
                    manifest.DatabaseServiceName,
                    LocalModuleManagedIdentity.CreateDatabaseServiceName(
                        manifest.InstanceId),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.ApiServiceName,
                    LocalModuleManagedIdentity.CreateApiServiceName(
                        manifest.InstanceId),
                    StringComparison.Ordinal) ||
                !LocalModuleManagedIdentity.IsLowerHex(
                    manifest.OwnershipNonce,
                    32))
            {
                throw new InvalidDataException(
                    "Local-module service identity is not manifest-owned.");
            }
        }
    }

    internal interface ILocalModuleServiceReadinessProbe
    {
        void WaitUntilOwnedListener(
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role);

        void WaitUntilStopped(
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role);
    }

    internal enum LocalModuleServicePairStopOutcome
    {
        Stopped = 1,
        CleanupBlocked = 2
    }

    internal sealed class LocalModuleServicePairLifecycle
    {
        private readonly IWindowsServiceApi _serviceApi;
        private readonly ILocalModuleServiceReadinessProbe _readiness;
        private readonly EpmdInstanceController _epmd;

        internal LocalModuleServicePairLifecycle(
            IWindowsServiceApi serviceApi,
            ILocalModuleServiceReadinessProbe readiness,
            EpmdInstanceController epmd)
        {
            if (serviceApi == null) throw new ArgumentNullException("serviceApi");
            if (readiness == null) throw new ArgumentNullException("readiness");
            if (epmd == null) throw new ArgumentNullException("epmd");
            _serviceApi = serviceApi;
            _readiness = readiness;
            _epmd = epmd;
        }

        internal LocalModuleServicePairLifecycle(
            IWindowsServiceApi serviceApi,
            ILocalModuleServiceReadinessProbe readiness)
        {
            if (serviceApi == null) throw new ArgumentNullException("serviceApi");
            if (readiness == null) throw new ArgumentNullException("readiness");
            _serviceApi = serviceApi;
            _readiness = readiness;
            _epmd = null;
        }

        internal void Start(LocalModuleInstanceManifest manifest)
        {
            ValidateServicePair(manifest);
            StartDatabase(manifest);
            StartApi(manifest);
        }

        internal void StartDatabase(LocalModuleInstanceManifest manifest)
        {
            ValidateServicePair(manifest);
            StartOne(
                manifest,
                LocalModuleProcessRole.Database,
                manifest.DatabaseServiceName);
        }

        internal void StartApi(LocalModuleInstanceManifest manifest)
        {
            ValidateServicePair(manifest);
            StartOne(
                manifest,
                LocalModuleProcessRole.Api,
                manifest.ApiServiceName);
        }

        internal LocalModuleServicePairStopOutcome Stop(
            LocalModuleInstanceManifest manifest)
        {
            ValidateServicePair(manifest);
            if (_epmd == null)
            {
                throw new InvalidOperationException(
                    "This lifecycle instance is configured only for ordered start.");
            }
            StopOne(
                manifest,
                LocalModuleProcessRole.Api,
                manifest.ApiServiceName);
            StopOne(
                manifest,
                LocalModuleProcessRole.Database,
                manifest.DatabaseServiceName);
            EpmdShutdownResult result = _epmd.StopIfUnused();
            return result.Outcome == EpmdShutdownOutcome.LiveNodes
                ? LocalModuleServicePairStopOutcome.CleanupBlocked
                : LocalModuleServicePairStopOutcome.Stopped;
        }

        private void StartOne(
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role,
            string serviceName)
        {
            WindowsServiceRecord record = RequireStableService(serviceName);
            if (record.State == WindowsServiceState.Stopped)
            {
                _serviceApi.Start(serviceName);
            }
            else if (record.State != WindowsServiceState.Running)
            {
                throw new InvalidOperationException(
                    "Local-module service is in a transitional state.");
            }
            _readiness.WaitUntilOwnedListener(manifest, role);
        }

        private void StopOne(
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role,
            string serviceName)
        {
            WindowsServiceRecord record = RequireStableService(serviceName);
            if (record.State == WindowsServiceState.Running)
            {
                _serviceApi.RequestStop(serviceName);
            }
            else if (record.State != WindowsServiceState.Stopped)
            {
                throw new InvalidOperationException(
                    "Local-module service is in a transitional state.");
            }
            _readiness.WaitUntilStopped(manifest, role);
        }

        private WindowsServiceRecord RequireStableService(string serviceName)
        {
            WindowsServiceRecord record = _serviceApi.Query(serviceName);
            if (record == null)
            {
                throw new InvalidOperationException(
                    "Owned local-module service is absent.");
            }
            return record;
        }

        private static void ValidateServicePair(
            LocalModuleInstanceManifest manifest)
        {
            if (manifest == null ||
                !string.Equals(
                    manifest.DatabaseServiceName,
                    LocalModuleServiceIdentity.CreateName(
                        manifest.InstanceId,
                        LocalModuleProcessRole.Database),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.ApiServiceName,
                    LocalModuleServiceIdentity.CreateName(
                        manifest.InstanceId,
                        LocalModuleProcessRole.Api),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Local-module service pair is not manifest-owned.");
            }
        }
    }

}
