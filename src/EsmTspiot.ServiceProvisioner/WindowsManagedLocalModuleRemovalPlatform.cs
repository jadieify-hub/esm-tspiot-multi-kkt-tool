using System;
using System.IO;
using System.Threading;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IManagedKktControllerRemoval
    {
        void Remove(string kktSerial);
    }

    internal sealed class NoopManagedKktControllerRemoval :
        IManagedKktControllerRemoval
    {
        internal static readonly NoopManagedKktControllerRemoval Instance =
            new NoopManagedKktControllerRemoval();

        private NoopManagedKktControllerRemoval()
        {
        }

        public void Remove(string kktSerial)
        {
            EsmTspiot.Shared.Services.LmServiceIdentity.CreateName(kktSerial);
        }
    }

    internal sealed class WindowsManagedKktControllerRemoval :
        IManagedKktControllerRemoval
    {
        private readonly LmServiceProvisioningBatchRequest _request;
        private readonly ManagedServiceManifestStore _manifests;
        private readonly IWindowsServiceApi _services;

        internal WindowsManagedKktControllerRemoval(
            LmServiceProvisioningBatchRequest request,
            ManagedServiceManifestStore manifests,
            IWindowsServiceApi services)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (manifests == null) throw new ArgumentNullException("manifests");
            if (services == null) throw new ArgumentNullException("services");
            _request = request;
            _manifests = manifests;
            _services = services;
        }

        public void Remove(string kktSerial)
        {
            string expectedSerial = _request.Operation ==
                LmServiceOperation.CleanupManaged
                ? _request.CleanupConfirmation.KktSerial
                : _request.RemovalConfirmation.KktSerial;
            if (!string.Equals(
                    kktSerial,
                    expectedSerial,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Controller removal serial changed inside the operation.");
            }
            ManagedServiceManifest ignored;
            bool hasManifest = _manifests.TryRead(kktSerial, out ignored);
            bool hasService = _services.Query(
                LmServiceIdentity.CreateName(kktSerial)) != null;
            if (!hasManifest && !hasService)
            {
                return;
            }

            WindowsLmProvisioningPlatform platform =
                WindowsLmProvisioningPlatform.CreateForRemoval(
                    _request.InitiatingSid);
            LmServiceProvisioner provisioner = new LmServiceProvisioner(platform);
            LmServiceProvisioningItemResult result = _request.Operation ==
                LmServiceOperation.CleanupManaged
                ? provisioner.CleanupManaged(_request)
                : provisioner.RemoveManaged(_request);
            if (result.Status == LmServiceProvisioningStatus.Succeeded ||
                result.Status ==
                    LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained)
            {
                return;
            }
            if (result.Status == LmServiceProvisioningStatus.CleanupPending ||
                result.Status == LmServiceProvisioningStatus.MarkedForDelete)
            {
                throw new IOException(
                    "Controller cleanup has not completed yet.");
            }
            throw new InvalidDataException(
                "Controller ownership could not be confirmed for full cleanup.");
        }
    }

    internal sealed class WindowsManagedLocalModuleRemovalPlatform :
        IManagedLocalModuleRemovalPlatform
    {
        private const string MachineMutexName =
            "Global\\KRS.MultiKKT.ManagedLocalModule.Machine.v1";
        private const string ControllerMachineMutexName =
            "Global\\KRS.MultiKKT.LmGateway.Machine.v1";
        private const int ScmAbsentAttempts = 120;

        private readonly LocalModuleManifestStore _manifests;
        private readonly ManagedLocalModuleRemovalJournalStore _removalJournals;
        private readonly ManagedLocalModuleProfileStore _profiles;
        private readonly LocalModuleRuntimeInstaller _runtimeInstaller;
        private readonly IWindowsServiceApi _services;
        private readonly VerifiedProvisionerBinary _supervisor;
        private readonly ILocalModuleServiceReadinessProbe _readiness;
        private readonly IEpmdCommandRunner _epmdRunner;
        private readonly IManagedKktControllerRemoval _controllerRemoval;
        private readonly Action _delay;
        private readonly string _expectedManagedStateSha256;

        internal WindowsManagedLocalModuleRemovalPlatform(
            LocalModuleManifestStore manifests,
            ManagedLocalModuleRemovalJournalStore removalJournals,
            ManagedLocalModuleProfileStore profiles,
            LocalModuleRuntimeInstaller runtimeInstaller,
            IWindowsServiceApi services,
            VerifiedProvisionerBinary supervisor,
            ILocalModuleServiceReadinessProbe readiness,
            IEpmdCommandRunner epmdRunner,
            IManagedKktControllerRemoval controllerRemoval)
            : this(
                manifests,
                removalJournals,
                profiles,
                runtimeInstaller,
                services,
                supervisor,
                readiness,
                epmdRunner,
                controllerRemoval,
                string.Empty,
                delegate { Thread.Sleep(250); })
        {
        }

        internal WindowsManagedLocalModuleRemovalPlatform(
            LocalModuleManifestStore manifests,
            ManagedLocalModuleRemovalJournalStore removalJournals,
            ManagedLocalModuleProfileStore profiles,
            LocalModuleRuntimeInstaller runtimeInstaller,
            IWindowsServiceApi services,
            VerifiedProvisionerBinary supervisor,
            ILocalModuleServiceReadinessProbe readiness,
            IEpmdCommandRunner epmdRunner,
            IManagedKktControllerRemoval controllerRemoval,
            Action delay)
            : this(
                manifests,
                removalJournals,
                profiles,
                runtimeInstaller,
                services,
                supervisor,
                readiness,
                epmdRunner,
                controllerRemoval,
                string.Empty,
                delay)
        {
        }

        internal WindowsManagedLocalModuleRemovalPlatform(
            LocalModuleManifestStore manifests,
            ManagedLocalModuleRemovalJournalStore removalJournals,
            ManagedLocalModuleProfileStore profiles,
            LocalModuleRuntimeInstaller runtimeInstaller,
            IWindowsServiceApi services,
            VerifiedProvisionerBinary supervisor,
            ILocalModuleServiceReadinessProbe readiness,
            IEpmdCommandRunner epmdRunner,
            IManagedKktControllerRemoval controllerRemoval,
            string expectedManagedStateSha256,
            Action delay)
        {
            if (manifests == null) throw new ArgumentNullException("manifests");
            if (removalJournals == null)
            {
                throw new ArgumentNullException("removalJournals");
            }
            if (profiles == null) throw new ArgumentNullException("profiles");
            if (runtimeInstaller == null)
            {
                throw new ArgumentNullException("runtimeInstaller");
            }
            if (services == null) throw new ArgumentNullException("services");
            if (supervisor == null) throw new ArgumentNullException("supervisor");
            if (readiness == null) throw new ArgumentNullException("readiness");
            if (epmdRunner == null) throw new ArgumentNullException("epmdRunner");
            if (controllerRemoval == null)
            {
                throw new ArgumentNullException("controllerRemoval");
            }
            if (delay == null) throw new ArgumentNullException("delay");
            _manifests = manifests;
            _removalJournals = removalJournals;
            _profiles = profiles;
            _runtimeInstaller = runtimeInstaller;
            _services = services;
            _supervisor = supervisor;
            _readiness = readiness;
            _epmdRunner = epmdRunner;
            _controllerRemoval = controllerRemoval;
            _expectedManagedStateSha256 =
                expectedManagedStateSha256 ?? string.Empty;
            _delay = delay;
        }

        internal static bool HasManagedState(
            string initiatingSid,
            string kktSerial)
        {
            PathSafety pathSafety = new PathSafety();
            LocalModuleManifestStore manifests =
                LocalModuleManifestStore.CreateMachineStore(
                    pathSafety,
                    initiatingSid);
            ManagedLocalModuleRemovalJournalStore removal =
                new ManagedLocalModuleRemovalJournalStore(
                    manifests.MachineRoot,
                    pathSafety,
                    initiatingSid);
            ManagedLocalModuleRemovalSnapshot pending;
            if (removal.TryRead(kktSerial, out pending))
            {
                return true;
            }
            ManagedKktStackManifest stack;
            return manifests.TryReadStack(kktSerial, out stack);
        }

        internal static WindowsManagedLocalModuleRemovalPlatform Create(
            LmServiceProvisioningBatchRequest request)
        {
            if (request == null ||
                (request.Operation != LmServiceOperation.RemoveManaged &&
                 request.Operation != LmServiceOperation.CleanupManaged))
            {
                throw new InvalidDataException(
                    "One exact removal or cleanup request is required.");
            }
            PathSafety pathSafety = new PathSafety();
            WindowsServiceApi services = new WindowsServiceApi();
            LocalModuleManifestStore manifests =
                LocalModuleManifestStore.CreateMachineStore(
                    pathSafety,
                    request.InitiatingSid);
            manifests.RepairOperatorInventoryAccess();
            ManagedLocalModuleRemovalJournalStore removal =
                new ManagedLocalModuleRemovalJournalStore(
                    manifests.MachineRoot,
                    pathSafety,
                    request.InitiatingSid);
            ManagedLocalModuleProfileStore profiles =
                new ManagedLocalModuleProfileStore(
                    manifests,
                    pathSafety,
                    new AtomicFileWriter());
            LocalModuleRuntimeInstaller runtimeInstaller =
                new LocalModuleRuntimeInstaller(
                    manifests,
                    pathSafety,
                    NoopLocalModuleMutationBoundary.Instance);
            VerifiedProvisionerBinary supervisor =
                VerifiedProvisionerBinary.ResolveCurrent(pathSafety);
            ManagedLocalModuleServiceReadinessProbe readiness =
                new ManagedLocalModuleServiceReadinessProbe(
                    services,
                    new TcpListenerOwnerReader(),
                    new NativeProcessParentReader());
            string machineRoot = manifests.MachineRoot;
            ManagedServiceManifestStore controllerManifests =
                new ManagedServiceManifestStore(
                    machineRoot,
                    pathSafety,
                    request.InitiatingSid);
            return new WindowsManagedLocalModuleRemovalPlatform(
                manifests,
                removal,
                profiles,
                runtimeInstaller,
                services,
                supervisor,
                readiness,
                new NativeEpmdCommandRunner(),
                new WindowsManagedKktControllerRemoval(
                    request,
                    controllerManifests,
                    services),
                GetExpectedManagedStateFingerprint(request),
                delegate { Thread.Sleep(250); });
        }

        internal static string GetExpectedManagedStateFingerprint(
            LmServiceProvisioningBatchRequest request)
        {
            LmManifestFingerprint fingerprint = request.Operation ==
                LmServiceOperation.CleanupManaged
                ? request.CleanupConfirmation == null
                    ? null
                    : request.CleanupConfirmation.ManagedStateFingerprint
                : request.RemovalConfirmation == null
                    ? null
                    : request.RemovalConfirmation.ManagedStateFingerprint;
            return fingerprint == null ? string.Empty : fingerprint.Sha256;
        }

        public IDisposable AcquireMachineLock()
        {
            IDisposable controller = GlobalOperationMutex.Acquire(
                ControllerMachineMutexName);
            try
            {
                IDisposable localModule = GlobalOperationMutex.Acquire(
                    MachineMutexName);
                return new OrderedMachineLocks(controller, localModule);
            }
            catch
            {
                controller.Dispose();
                throw;
            }
        }

        public ManagedLocalModuleRemovalSnapshot Inspect(
            string kktSerial,
            string operationId)
        {
            VerifyDisplayedManagedState(kktSerial);
            ManagedLocalModuleRemovalSnapshot pending;
            if (_removalJournals.TryRead(kktSerial, out pending))
            {
                _removalJournals.Write(pending, operationId, string.Empty);
                return pending;
            }
            ManagedKktStackManifest stack = _manifests.ReadStack(kktSerial);
            LocalModuleInstanceManifest instance = _manifests.ReadInstance(
                stack.LocalModuleInstanceId);
            LocalModuleRuntimeManifest runtime = _manifests.ReadRuntime(
                instance.RuntimeId);
            ManagedLocalModuleRemovalSnapshot snapshot =
                ManagedLocalModuleRemovalSnapshot.CreateOwned(
                    stack,
                    instance,
                    runtime);
            _removalJournals.Write(snapshot, operationId, string.Empty);
            return snapshot;
        }

        private void VerifyDisplayedManagedState(string kktSerial)
        {
            if (string.IsNullOrEmpty(_expectedManagedStateSha256))
            {
                return;
            }
            string actual;
            bool found = _removalJournals.TryComputeFingerprint(
                kktSerial,
                out actual);
            if (!found)
            {
                found = _manifests.TryComputeStackFingerprint(
                    kktSerial,
                    out actual);
            }
            if (!found || !CanonicalLmPlanHasher.FixedTimeEqualsHex(
                    _expectedManagedStateSha256,
                    actual))
            {
                throw new InvalidDataException(
                    "Показанное состояние комплекта изменилось до удаления.");
            }
        }

        public void RemoveController(
            ManagedLocalModuleRemovalSnapshot snapshot)
        {
            _controllerRemoval.Remove(snapshot.KktSerial);
        }

        public void DeleteStack(ManagedLocalModuleRemovalSnapshot snapshot)
        {
            ManagedKktStackManifest stack;
            if (!_manifests.TryReadStack(snapshot.KktSerial, out stack))
            {
                return;
            }
            if (!string.Equals(
                    stack.StackId,
                    snapshot.StackId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    stack.OwnershipNonce,
                    snapshot.StackOwnershipNonce,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    stack.LocalModuleInstanceId,
                    snapshot.InstanceId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "KKT stack ownership changed before deletion.");
            }
            _manifests.DeleteStack(
                snapshot.KktSerial,
                snapshot.StackOwnershipNonce);
        }

        public int CountInstanceReferences(
            ManagedLocalModuleRemovalSnapshot snapshot)
        {
            return _manifests.CountInstanceReferences(snapshot.InstanceId);
        }

        public LocalModuleServicePairStopOutcome StopServicePair(
            ManagedLocalModuleRemovalSnapshot snapshot)
        {
            LocalModuleInstanceManifest instance;
            if (!_manifests.TryReadInstance(snapshot.InstanceId, out instance))
            {
                EnsureServicesAbsent(snapshot.InstanceId);
                return LocalModuleServicePairStopOutcome.Stopped;
            }
            ValidateInstanceOwnership(snapshot, instance);
            LocalModuleRuntimeManifest runtime = _manifests.ReadRuntime(
                instance.RuntimeId);
            LocalModuleCapabilityProfile capability =
                LocalModuleCapabilityProfile.Resolve(runtime.ProductVersion);
            LocalModuleWindowsServicePair pair =
                new LocalModuleWindowsServicePair(_services, _supervisor);
            WindowsServiceRecord database = _services.Query(
                instance.DatabaseServiceName);
            WindowsServiceRecord api = _services.Query(instance.ApiServiceName);
            if (database == null && api == null)
            {
                return StopEpmd(capability, runtime, instance);
            }
            if (database == null || api == null ||
                !WindowsServiceDefinitionMatcher.Matches(
                    pair.BuildDefinition(
                        instance,
                        LocalModuleProcessRole.Database,
                        null),
                    database) ||
                !WindowsServiceDefinitionMatcher.Matches(
                    pair.BuildDefinition(
                        instance,
                        LocalModuleProcessRole.Api,
                        null),
                    api))
            {
                throw new InvalidDataException(
                    "Local-module SCM ownership changed before stop.");
            }
            if (database.State == WindowsServiceState.Stopped &&
                api.State == WindowsServiceState.Stopped)
            {
                _readiness.WaitUntilStopped(
                    instance,
                    LocalModuleProcessRole.Api);
                _readiness.WaitUntilStopped(
                    instance,
                    LocalModuleProcessRole.Database);
                return StopEpmd(capability, runtime, instance);
            }
            return new LocalModuleServicePairLifecycle(
                _services,
                _readiness,
                new EpmdInstanceController(
                    capability,
                    runtime.RuntimeRoot,
                    Path.Combine(instance.DataRoot, "temp"),
                    instance.EpmdPort,
                    _epmdRunner)).Stop(instance);
        }

        public void DeleteServicePair(
            ManagedLocalModuleRemovalSnapshot snapshot)
        {
            LocalModuleInstanceManifest instance;
            if (!_manifests.TryReadInstance(snapshot.InstanceId, out instance))
            {
                EnsureServicesAbsent(snapshot.InstanceId);
                return;
            }
            ValidateInstanceOwnership(snapshot, instance);
            LocalModuleWindowsServicePair pair =
                new LocalModuleWindowsServicePair(_services, _supervisor);
            DeleteOne(
                instance,
                LocalModuleProcessRole.Api,
                pair.BuildDefinition(
                    instance,
                    LocalModuleProcessRole.Api,
                    null));
            DeleteOne(
                instance,
                LocalModuleProcessRole.Database,
                pair.BuildDefinition(
                    instance,
                    LocalModuleProcessRole.Database,
                    null));
        }

        public void DeleteProfile(
            ManagedLocalModuleRemovalSnapshot snapshot)
        {
            LocalModuleInstanceManifest instance;
            if (!_manifests.TryReadInstance(snapshot.InstanceId, out instance))
            {
                string root = _manifests.GetInstanceRoot(snapshot.InstanceId);
                if (Directory.Exists(root))
                {
                    throw new InvalidDataException(
                        "A profile directory remains without its owned manifest.");
                }
                return;
            }
            ValidateInstanceOwnership(snapshot, instance);
            _profiles.DeleteProfileContent(instance);
        }

        public void DeleteInstance(
            ManagedLocalModuleRemovalSnapshot snapshot)
        {
            LocalModuleInstanceManifest instance;
            if (!_manifests.TryReadInstance(snapshot.InstanceId, out instance))
            {
                return;
            }
            ValidateInstanceOwnership(snapshot, instance);
            _manifests.DeleteInstance(
                snapshot.InstanceId,
                snapshot.InstanceOwnershipNonce);
        }

        public int CountRuntimeReferences(
            ManagedLocalModuleRemovalSnapshot snapshot)
        {
            return _manifests.CountRuntimeReferences(snapshot.RuntimeId);
        }

        public void DeleteRuntime(
            ManagedLocalModuleRemovalSnapshot snapshot)
        {
            _runtimeInstaller.DeleteUnreferencedRuntime(
                snapshot.RuntimeId,
                snapshot.RuntimeOwnershipNonce);
        }

        public void MarkCleanupPending(
            ManagedLocalModuleRemovalSnapshot snapshot,
            string operationId,
            string errorClass)
        {
            _removalJournals.Write(
                snapshot,
                operationId,
                errorClass ?? "Unknown");
            LocalModuleInstanceManifest instance;
            if (_manifests.TryReadInstance(snapshot.InstanceId, out instance))
            {
                ValidateInstanceOwnership(snapshot, instance);
                instance.State = LocalModuleInstanceLifecycleState.CleanupPending;
                instance.CurrentOperationId = operationId;
                instance.UpdatedUtc = DateTime.UtcNow.ToString("o");
                _manifests.WriteInstance(instance);
            }
        }

        public void CompleteRemoval(
            ManagedLocalModuleRemovalSnapshot snapshot)
        {
            _removalJournals.Delete(
                snapshot.KktSerial,
                snapshot.StackOwnershipNonce);
        }

        private LocalModuleServicePairStopOutcome StopEpmd(
            LocalModuleCapabilityProfile capability,
            LocalModuleRuntimeManifest runtime,
            LocalModuleInstanceManifest instance)
        {
            EpmdShutdownResult result = new EpmdInstanceController(
                capability,
                runtime.RuntimeRoot,
                Path.Combine(instance.DataRoot, "temp"),
                instance.EpmdPort,
                _epmdRunner).StopIfUnused();
            return result.Outcome == EpmdShutdownOutcome.LiveNodes
                ? LocalModuleServicePairStopOutcome.CleanupBlocked
                : LocalModuleServicePairStopOutcome.Stopped;
        }

        private void DeleteOne(
            LocalModuleInstanceManifest instance,
            LocalModuleProcessRole role,
            WindowsServiceDefinition expected)
        {
            string serviceName = role == LocalModuleProcessRole.Api
                ? instance.ApiServiceName
                : instance.DatabaseServiceName;
            WindowsServiceRecord record = _services.Query(serviceName);
            if (record == null)
            {
                return;
            }
            if (record.State != WindowsServiceState.Stopped ||
                !WindowsServiceDefinitionMatcher.Matches(expected, record))
            {
                throw new InvalidDataException(
                    "Only an exact stopped local-module service may be deleted.");
            }
            _services.Delete(serviceName);
            for (int attempt = 0; attempt < ScmAbsentAttempts; attempt++)
            {
                if (_services.Query(serviceName) == null)
                {
                    return;
                }
                if (attempt + 1 < ScmAbsentAttempts)
                {
                    _delay();
                }
            }
            throw new IOException(
                "SCM has not completed local-module service deletion.");
        }

        private void EnsureServicesAbsent(string instanceId)
        {
            string database = LocalModuleManagedIdentity.CreateDatabaseServiceName(
                instanceId);
            string api = LocalModuleManagedIdentity.CreateApiServiceName(instanceId);
            if (_services.Query(database) != null || _services.Query(api) != null)
            {
                throw new InvalidDataException(
                    "SCM services remain without an owned instance manifest.");
            }
        }

        private sealed class OrderedMachineLocks : IDisposable
        {
            private IDisposable _controller;
            private IDisposable _localModule;

            internal OrderedMachineLocks(
                IDisposable controller,
                IDisposable localModule)
            {
                _controller = controller;
                _localModule = localModule;
            }

            public void Dispose()
            {
                IDisposable localModule = _localModule;
                IDisposable controller = _controller;
                _localModule = null;
                _controller = null;
                if (localModule != null)
                {
                    localModule.Dispose();
                }
                if (controller != null)
                {
                    controller.Dispose();
                }
            }
        }

        private static void ValidateInstanceOwnership(
            ManagedLocalModuleRemovalSnapshot snapshot,
            LocalModuleInstanceManifest instance)
        {
            if (!string.Equals(
                    snapshot.InstanceId,
                    instance.InstanceId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    snapshot.InstanceOwnershipNonce,
                    instance.OwnershipNonce,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    snapshot.RuntimeId,
                    instance.RuntimeId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Instance ownership changed during cleanup.");
            }
        }
    }
}
