using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface ILocalModulePortReservationFactory
    {
        IDisposable Acquire(ManagedLocalModuleProvisioningItemRequest item);
    }

    internal sealed class LocalModulePortReservationFactory :
        ILocalModulePortReservationFactory
    {
        public IDisposable Acquire(ManagedLocalModuleProvisioningItemRequest item)
        {
            if (item == null) throw new ArgumentNullException("item");
            return ExclusiveTcpPortReservation.AcquireDualStackWildcard(
                new[] { item.DatabasePort, item.ApiPort, item.EpmdPort });
        }
    }

    internal sealed class WindowsManagedLocalModulePlatform :
        IManagedLocalModuleProvisioningPlatform
    {
        private const string MachineMutexName =
            "Global\\KRS.MultiKKT.ManagedLocalModule.Machine.v1";

        private readonly string _initiatingSid;
        private readonly LocalModuleRuntimeManifest _runtime;
        private readonly LocalModuleCapabilityProfile _capability;
        private readonly LocalModuleManifestStore _manifests;
        private readonly ManagedLocalModuleLifecycleJournalStore _journals;
        private readonly ManagedLocalModuleProfileStore _profiles;
        private readonly IWindowsServiceApi _services;
        private readonly LocalModuleWindowsServicePair _servicePair;
        private readonly LocalModuleServicePairLifecycle _lifecycle;
        private readonly ManagedLocalModuleServiceReadinessProbe _readiness;
        private readonly IEpmdCommandRunner _epmdRunner;
        private readonly ILocalModulePortReservationFactory _ports;
        private readonly Dictionary<string, WorkState> _states;

        internal WindowsManagedLocalModulePlatform(
            string initiatingSid,
            LocalModuleRuntimeManifest runtime,
            LocalModuleCapabilityProfile capability,
            LocalModuleManifestStore manifests,
            ManagedLocalModuleLifecycleJournalStore journals,
            ManagedLocalModuleProfileStore profiles,
            IWindowsServiceApi services,
            LocalModuleWindowsServicePair servicePair,
            LocalModuleServicePairLifecycle lifecycle,
            ManagedLocalModuleServiceReadinessProbe readiness,
            IEpmdCommandRunner epmdRunner,
            ILocalModulePortReservationFactory ports)
        {
            if (string.IsNullOrWhiteSpace(initiatingSid))
            {
                throw new ArgumentException(
                    "The initiating SID is required.",
                    "initiatingSid");
            }
            if (runtime == null) throw new ArgumentNullException("runtime");
            if (capability == null) throw new ArgumentNullException("capability");
            if (manifests == null) throw new ArgumentNullException("manifests");
            if (journals == null) throw new ArgumentNullException("journals");
            if (profiles == null) throw new ArgumentNullException("profiles");
            if (services == null) throw new ArgumentNullException("services");
            if (servicePair == null) throw new ArgumentNullException("servicePair");
            if (lifecycle == null) throw new ArgumentNullException("lifecycle");
            if (readiness == null) throw new ArgumentNullException("readiness");
            if (epmdRunner == null) throw new ArgumentNullException("epmdRunner");
            if (ports == null) throw new ArgumentNullException("ports");
            _initiatingSid = initiatingSid;
            _runtime = runtime;
            _capability = capability;
            _manifests = manifests;
            _journals = journals;
            _profiles = profiles;
            _services = services;
            _servicePair = servicePair;
            _lifecycle = lifecycle;
            _readiness = readiness;
            _epmdRunner = epmdRunner;
            _ports = ports;
            _states = new Dictionary<string, WorkState>(StringComparer.Ordinal);
        }

        internal static CompleteStackProvisioningSession CreateSession(
            LmServiceProvisioningBatchRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid ||
                request.Operation != LmServiceOperation.EnsureManagedLocalModules)
            {
                throw new InvalidDataException(
                    "A valid managed local-module request is required.");
            }

            IDisposable controllerMachineLock = null;
            IDisposable localModuleMachineLock = null;
            ILockedControllerInstaller controllerInstaller = null;
            VerifiedLocalModulePackage localModulePackage = null;
            try
            {
                PathSafety pathSafety = new PathSafety();
                LocalModuleInstallerSelection selection =
                    request.LocalModuleInstallerSelection;
                LocalModuleCapabilityProfile capability =
                    LocalModuleCapabilityProfile.Resolve(selection.ProductVersion);
                WindowsLmProvisioningPlatform controllerPlatform =
                    WindowsLmProvisioningPlatform.Create(
                        request.InitiatingSid,
                        request.OperationId);
                controllerMachineLock = controllerPlatform.AcquireMachineLock();
                localModuleMachineLock =
                    GlobalOperationMutex.Acquire(MachineMutexName);
                controllerInstaller = controllerPlatform.PrepareInstaller(
                    request.InstallerSelection,
                    request.OperationId);
                localModulePackage = LocalModulePackageVerifier
                    .Supported()
                    .VerifyAndLock(selection);
                LocalModuleManifestStore manifests =
                    LocalModuleManifestStore.CreateMachineStore(
                        pathSafety,
                        request.InitiatingSid);
                manifests.RepairOperatorInventoryAccess();
                PreparedCompleteStackResources prepared =
                    new PreparedCompleteStackResources(
                        request,
                        pathSafety,
                        capability,
                        manifests,
                        controllerPlatform,
                        controllerInstaller,
                        localModulePackage,
                        controllerMachineLock,
                        localModuleMachineLock);
                controllerInstaller = null;
                localModulePackage = null;
                controllerMachineLock = null;
                localModuleMachineLock = null;
                return new CompleteStackProvisioningSession(
                    request,
                    prepared.Initialize,
                    prepared);
            }
            catch
            {
                if (controllerInstaller != null) controllerInstaller.Dispose();
                if (localModulePackage != null) localModulePackage.Dispose();
                if (localModuleMachineLock != null) localModuleMachineLock.Dispose();
                if (controllerMachineLock != null) controllerMachineLock.Dispose();
                throw;
            }
        }

        public IDisposable AcquireItemLock(string inn)
        {
            if (!LocalModuleManagedIdentity.IsAsciiDigits(inn, 10) &&
                !LocalModuleManagedIdentity.IsAsciiDigits(inn, 12))
            {
                throw new ArgumentException("INN is invalid.", "inn");
            }
            return GlobalOperationMutex.Acquire(
                "Global\\KRS.MultiKKT.ManagedLocalModule.Inn." + inn);
        }

        public void Reconcile(
            ManagedLocalModuleProvisioningItemRequest item,
            string operationId)
        {
            ValidateItemContext(item, operationId);
            LocalModuleInstanceManifest instance;
            _manifests.TryFindInstanceByInn(item.Inn, out instance);
            ManagedLocalModuleLifecycleJournal journal;
            _journals.TryRead(item.Inn, out journal);
            if (instance != null && journal != null &&
                (!string.Equals(
                    instance.InstanceId,
                    journal.InstanceId,
                    StringComparison.Ordinal) ||
                 !string.Equals(
                    instance.OwnershipNonce,
                    journal.OwnershipNonce,
                    StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "Instance and lifecycle journal ownership do not match.");
            }
            _states[item.Inn] = new WorkState(instance, journal);
        }

        public ManagedLocalModuleObservedState Inspect(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context)
        {
            ValidateContext(context);
            WorkState state = RequireReconciled(item.Inn);
            if (state.Journal != null &&
                state.Journal.Stage ==
                    ManagedLocalModuleProvisioningStage.CleanupPending)
            {
                return ManagedLocalModuleObservedState.CleanupPending;
            }
            LocalModuleInstanceManifest instance = state.Instance;
            if (instance == null)
            {
                if (state.Journal != null &&
                    (!string.Equals(
                        state.Journal.RuntimeId,
                        context.RuntimeId,
                        StringComparison.Ordinal) ||
                     !string.Equals(
                        state.Journal.CapabilityId,
                        context.CapabilityId,
                        StringComparison.Ordinal)))
                {
                    return ManagedLocalModuleObservedState.VersionVerificationPending;
                }
                return ManagedLocalModuleObservedState.Absent;
            }
            LocalModuleRuntimeManifest instanceRuntime =
                _manifests.ReadRuntime(instance.RuntimeId);
            if (!string.Equals(
                    instance.RuntimeId,
                    context.RuntimeId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    instanceRuntime.CapabilityId,
                    context.CapabilityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    instanceRuntime.ProductVersion,
                    context.RuntimeVersion,
                    StringComparison.Ordinal))
            {
                return ManagedLocalModuleObservedState.VersionVerificationPending;
            }
            if (instance.State == LocalModuleInstanceLifecycleState.CleanupPending)
            {
                return ManagedLocalModuleObservedState.CleanupPending;
            }

            WindowsServiceRecord database = _services.Query(
                instance.DatabaseServiceName);
            WindowsServiceRecord api = _services.Query(instance.ApiServiceName);
            bool exactDefinitions = DefinitionsMatch(instance, database, api);
            bool managedDefinitions = ManagedDefinitionsMatch(
                instance,
                database,
                api);
            bool stoppedOrAbsent = IsStoppedOrAbsent(database) &&
                IsStoppedOrAbsent(api);
            bool managedPairCanBeStopped = managedDefinitions &&
                IsStableCompletePair(database, api);
            bool managedPairCanBeRewritten =
                (stoppedOrAbsent && managedDefinitions) ||
                managedPairCanBeStopped;
            bool planMatches = InstanceMatchesItem(instance, item);
            ValidationResult profile = _profiles.ValidateConfiguration(
                instanceRuntime,
                _capability,
                instance);
            if (!planMatches || !profile.IsValid)
            {
                return managedPairCanBeRewritten
                    ? ManagedLocalModuleObservedState.OwnedMismatch
                    : ManagedLocalModuleObservedState.Foreign;
            }
            if (!exactDefinitions)
            {
                return managedPairCanBeRewritten
                    ? ManagedLocalModuleObservedState.OwnedMismatch
                    : ManagedLocalModuleObservedState.Foreign;
            }
            if (database == null && api == null)
            {
                return ManagedLocalModuleObservedState.OwnedMismatch;
            }
            if (database == null || api == null)
            {
                return stoppedOrAbsent
                    ? ManagedLocalModuleObservedState.OwnedMismatch
                    : ManagedLocalModuleObservedState.Foreign;
            }
            if (database.State == WindowsServiceState.Stopped &&
                api.State == WindowsServiceState.Stopped)
            {
                return ManagedLocalModuleObservedState.MatchingStopped;
            }
            if (database.State == WindowsServiceState.Running &&
                api.State == WindowsServiceState.Running &&
                _readiness.ProbeOwnedListener(
                    instance,
                    LocalModuleProcessRole.Database).IsValid &&
                _readiness.ProbeOwnedListener(
                    instance,
                    LocalModuleProcessRole.Api).IsValid)
            {
                return ManagedLocalModuleObservedState.MatchingReady;
            }
            return ManagedLocalModuleObservedState.Foreign;
        }

        public void RecordStage(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string operationId,
            ManagedLocalModuleProvisioningStage stage)
        {
            ValidateContext(context);
            WorkState state = GetOrCreateState(item, context, operationId);
            ManagedLocalModuleLifecycleJournal journal = state.Journal;
            if (journal == null)
            {
                journal = ManagedLocalModuleLifecycleJournal.Create(
                    item.Inn,
                    state.InstanceId,
                    state.OwnershipNonce,
                    context.RuntimeId,
                    context.CapabilityId,
                    operationId,
                    stage);
            }
            else
            {
                journal.OperationId = operationId;
                journal.Stage = stage;
                journal.LastErrorClass = string.Empty;
            }
            _journals.Write(journal);
            state.Journal = journal;
        }

        public void PrepareProfile(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string operationId)
        {
            ValidateContext(context);
            WorkState state = GetOrCreateState(item, context, operationId);
            StopExactOwnedPairForRewrite(state.Instance);
            EnsureServicesStopped(state.Instance);
            DisposeReservation(state);
            state.PortReservation = _ports.Acquire(item);
            LocalModuleConfiguration configuration =
                _profiles.WriteConfiguration(
                    _runtime,
                    _capability,
                    item,
                    state.InstanceId,
                    state.OwnershipNonce);
            LocalModuleInstanceManifest replacement =
                LocalModuleInstanceManifest.Create(
                    item,
                    state.InstanceId,
                    _runtime.RuntimeId,
                    _runtime.RuntimeRoot,
                    configuration,
                    state.OwnershipNonce,
                    operationId);
            if (state.Instance != null)
            {
                replacement.LastCompletedOperationId =
                    state.Instance.LastCompletedOperationId;
            }
            _manifests.WriteInstance(replacement);
            _profiles.ProtectOwnedManifests(_runtime, replacement);
            state.Instance = replacement;
        }

        public void ConfigureServicePair(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string initiatingSid)
        {
            ValidateContext(context);
            WorkState state = RequireInstance(item.Inn);
            if (!string.Equals(
                    initiatingSid,
                    _initiatingSid,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Initiating SID changed inside the immutable session.");
            }
            _servicePair.EnsureConfigured(state.Instance, null);
            if (!DefinitionsMatch(
                    state.Instance,
                    _services.Query(state.Instance.DatabaseServiceName),
                    _services.Query(state.Instance.ApiServiceName)))
            {
                throw new InvalidDataException(
                    "SCM did not retain the exact managed local-module service pair.");
            }
        }

        public void StartDatabaseAndWait(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context)
        {
            ValidateContext(context);
            WorkState state = RequireInstance(item.Inn);
            if (state.PortReservation == null)
            {
                state.PortReservation = _ports.Acquire(item);
            }
            DisposeReservation(state);
            try
            {
                _lifecycle.StartDatabase(state.Instance);
            }
            catch
            {
                TryStopOwnedPair(state.Instance);
                throw;
            }
        }

        public void StartApiAndWait(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context)
        {
            ValidateContext(context);
            WorkState state = RequireInstance(item.Inn);
            try
            {
                _lifecycle.StartApi(state.Instance);
            }
            catch
            {
                TryStopOwnedPair(state.Instance);
                throw;
            }
        }

        public void Complete(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string operationId)
        {
            ValidateContext(context);
            WorkState state = RequireInstance(item.Inn);
            LocalModuleInstanceManifest instance = state.Instance;
            instance.State = LocalModuleInstanceLifecycleState.ReadyToInitialize;
            instance.CurrentOperationId = operationId;
            instance.LastCompletedOperationId = operationId;
            instance.UpdatedUtc = DateTime.UtcNow.ToString(
                "o",
                CultureInfo.InvariantCulture);
            _manifests.WriteInstance(instance);
            _profiles.ProtectOwnedManifests(_runtime, instance);

            EnsureStackReference(item, context, operationId);
        }

        public void EnsureStackReference(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string operationId)
        {
            ValidateContext(context);
            WorkState state = RequireInstance(item.Inn);
            LocalModuleInstanceManifest instance = state.Instance;

            ManagedKktStackManifest existing;
            ManagedKktStackManifest stack;
            if (_manifests.TryReadStack(item.KktSerial, out existing))
            {
                if (!string.Equals(
                        existing.LocalModuleInstanceId,
                        instance.InstanceId,
                        StringComparison.Ordinal) ||
                    !string.Equals(existing.Inn, item.Inn, StringComparison.Ordinal) ||
                    existing.KktOrdinal != item.KktOrdinal ||
                    existing.ControllerGrpcPort != item.ControllerGrpcPort ||
                    existing.ControllerRestPort != item.ControllerRestPort)
                {
                    throw new InvalidDataException(
                        "KKT stack already contains a different immutable plan.");
                }
                if (existing.State == ManagedKktStackLifecycleState.Ready)
                {
                    return;
                }
                stack = ManagedKktStackManifest.Create(
                    item,
                    instance.InstanceId,
                    existing.EsmRecordId,
                    existing.OwnershipNonce,
                    operationId);
            }
            else
            {
                stack = ManagedKktStackManifest.Create(
                    item,
                    instance.InstanceId,
                    string.Empty,
                    OwnershipNonceGenerator.Create(),
                    operationId);
            }
            stack.State = ManagedKktStackLifecycleState.Ready;
            stack.LastCompletedOperationId = operationId;
            stack.UpdatedUtc = DateTime.UtcNow.ToString(
                "o",
                CultureInfo.InvariantCulture);
            _manifests.WriteStack(stack);
        }

        public void MarkRequiresAttention(
            ManagedLocalModuleProvisioningItemRequest item,
            string operationId,
            string errorClass)
        {
            WorkState state;
            if (!_states.TryGetValue(item.Inn, out state))
            {
                return;
            }
            DisposeReservation(state);
            if (state.Journal != null)
            {
                state.Journal.OperationId = operationId;
                state.Journal.Stage =
                    ManagedLocalModuleProvisioningStage.RequiresAttention;
                state.Journal.LastErrorClass = errorClass ?? "Unknown";
                _journals.Write(state.Journal);
            }
            if (state.Instance != null)
            {
                state.Instance.State = LocalModuleInstanceLifecycleState.Failed;
                state.Instance.CurrentOperationId = operationId;
                state.Instance.UpdatedUtc = DateTime.UtcNow.ToString(
                    "o",
                    CultureInfo.InvariantCulture);
                _manifests.WriteInstance(state.Instance);
                _profiles.ProtectOwnedManifests(_runtime, state.Instance);
            }
        }

        private WorkState GetOrCreateState(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string operationId)
        {
            WorkState state = RequireReconciled(item.Inn);
            if (!string.IsNullOrEmpty(state.InstanceId))
            {
                return state;
            }
            string nonce = OwnershipNonceGenerator.Create();
            state.InstanceId = LocalModuleManagedIdentity.CreateInstanceId(
                item.Inn,
                nonce);
            state.OwnershipNonce = nonce;
            state.Journal = ManagedLocalModuleLifecycleJournal.Create(
                item.Inn,
                state.InstanceId,
                state.OwnershipNonce,
                context.RuntimeId,
                context.CapabilityId,
                operationId,
                ManagedLocalModuleProvisioningStage.Created);
            return state;
        }

        private WorkState RequireReconciled(string inn)
        {
            WorkState state;
            if (!_states.TryGetValue(inn, out state))
            {
                throw new InvalidOperationException(
                    "Managed local-module item was not reconciled.");
            }
            return state;
        }

        private WorkState RequireInstance(string inn)
        {
            WorkState state = RequireReconciled(inn);
            if (state.Instance == null)
            {
                throw new InvalidOperationException(
                    "Managed local-module instance manifest is absent.");
            }
            return state;
        }

        private bool DefinitionsMatch(
            LocalModuleInstanceManifest instance,
            WindowsServiceRecord database,
            WindowsServiceRecord api)
        {
            if (database == null && api == null)
            {
                return true;
            }
            if (database == null || api == null)
            {
                WindowsServiceRecord existing = database ?? api;
                LocalModuleProcessRole role = database == null
                    ? LocalModuleProcessRole.Api
                    : LocalModuleProcessRole.Database;
                return WindowsServiceDefinitionMatcher.Matches(
                    _servicePair.BuildDefinition(
                        instance,
                        role,
                        null),
                    existing);
            }
            return WindowsServiceDefinitionMatcher.Matches(
                    _servicePair.BuildDefinition(
                        instance,
                        LocalModuleProcessRole.Database,
                        null),
                    database) &&
                WindowsServiceDefinitionMatcher.Matches(
                    _servicePair.BuildDefinition(
                        instance,
                        LocalModuleProcessRole.Api,
                        null),
                    api);
        }

        private bool ManagedDefinitionsMatch(
            LocalModuleInstanceManifest instance,
            WindowsServiceRecord database,
            WindowsServiceRecord api)
        {
            return ManagedDefinitionMatches(
                    instance,
                    LocalModuleProcessRole.Database,
                    database) &&
                ManagedDefinitionMatches(
                    instance,
                    LocalModuleProcessRole.Api,
                    api);
        }

        private bool ManagedDefinitionMatches(
            LocalModuleInstanceManifest instance,
            LocalModuleProcessRole role,
            WindowsServiceRecord service)
        {
            if (service == null) return true;
            WindowsServiceDefinition expected = _servicePair.BuildDefinition(
                instance,
                role,
                null);
            return string.Equals(
                    service.ServiceName,
                    expected.ServiceName,
                    StringComparison.Ordinal) &&
                string.Equals(
                    service.ImagePath,
                    expected.ImagePath,
                    StringComparison.Ordinal);
        }

        private static bool InstanceMatchesItem(
            LocalModuleInstanceManifest instance,
            ManagedLocalModuleProvisioningItemRequest item)
        {
            return string.Equals(instance.Inn, item.Inn, StringComparison.Ordinal) &&
                instance.LocalModuleOrdinal == item.LocalModuleOrdinal &&
                instance.ApiPort == item.ApiPort &&
                instance.DatabasePort == item.DatabasePort &&
                instance.EpmdPort == item.EpmdPort;
        }

        private static bool IsStoppedOrAbsent(WindowsServiceRecord service)
        {
            return service == null || service.State == WindowsServiceState.Stopped;
        }

        private static bool IsStableCompletePair(
            WindowsServiceRecord database,
            WindowsServiceRecord api)
        {
            return database != null && api != null &&
                (database.State == WindowsServiceState.Stopped ||
                 database.State == WindowsServiceState.Running) &&
                (api.State == WindowsServiceState.Stopped ||
                 api.State == WindowsServiceState.Running);
        }

        private void StopExactOwnedPairForRewrite(
            LocalModuleInstanceManifest instance)
        {
            if (instance == null) return;
            WindowsServiceRecord database = _services.Query(
                instance.DatabaseServiceName);
            WindowsServiceRecord api = _services.Query(instance.ApiServiceName);
            if (IsStoppedOrAbsent(database) && IsStoppedOrAbsent(api)) return;
            if (!IsStableCompletePair(database, api) ||
                !ManagedDefinitionsMatch(instance, database, api))
            {
                throw new InvalidOperationException(
                    "Only an exact manifest-owned service pair may be stopped for profile recovery.");
            }
            if (_lifecycle.Stop(instance) !=
                LocalModuleServicePairStopOutcome.Stopped)
            {
                throw new InvalidOperationException(
                    "Owned local-module processes did not stop cleanly for profile recovery.");
            }
        }

        private void EnsureServicesStopped(LocalModuleInstanceManifest instance)
        {
            if (instance == null)
            {
                return;
            }
            WindowsServiceRecord database = _services.Query(
                instance.DatabaseServiceName);
            WindowsServiceRecord api = _services.Query(instance.ApiServiceName);
            if (!IsStoppedOrAbsent(database) || !IsStoppedOrAbsent(api))
            {
                throw new InvalidOperationException(
                    "Owned services must be stopped before rewriting their profile.");
            }
        }

        private void TryStopOwnedPair(LocalModuleInstanceManifest instance)
        {
            try
            {
                WindowsServiceRecord database = _services.Query(
                    instance.DatabaseServiceName);
                WindowsServiceRecord api = _services.Query(instance.ApiServiceName);
                if (database != null && api != null &&
                    DefinitionsMatch(instance, database, api))
                {
                    new LocalModuleServicePairLifecycle(
                        _services,
                        _readiness,
                        new EpmdInstanceController(
                            _capability,
                            _runtime.RuntimeRoot,
                            Path.Combine(instance.DataRoot, "temp"),
                            instance.EpmdPort,
                            _epmdRunner)).Stop(instance);
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) &&
                    !(ex is InvalidOperationException) &&
                    !(ex is SystemException))
                {
                    throw;
                }
            }
        }

        private void ValidateContext(
            ManagedLocalModuleProvisioningContext context)
        {
            if (context == null ||
                !string.Equals(
                    context.RuntimeId,
                    _runtime.RuntimeId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    context.CapabilityId,
                    _capability.CapabilityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    context.RuntimeVersion,
                    _capability.ProductVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Managed local-module session context changed.");
            }
        }

        private static void ValidateItemContext(
            ManagedLocalModuleProvisioningItemRequest item,
            string operationId)
        {
            if (item == null || !ProvisionerCommandLine.IsGuidN(operationId))
            {
                throw new InvalidDataException(
                    "Managed local-module item context is invalid.");
            }
        }

        private static void DisposeReservation(WorkState state)
        {
            if (state.PortReservation != null)
            {
                state.PortReservation.Dispose();
                state.PortReservation = null;
            }
        }

        private sealed class PreparedCompleteStackResources : IDisposable
        {
            private readonly LmServiceProvisioningBatchRequest _request;
            private readonly PathSafety _pathSafety;
            private readonly LocalModuleCapabilityProfile _capability;
            private readonly LocalModuleManifestStore _manifests;
            private readonly WindowsLmProvisioningPlatform _controllerPlatform;
            private ILockedControllerInstaller _controllerInstaller;
            private VerifiedLocalModulePackage _localModulePackage;
            private IDisposable _controllerMachineLock;
            private IDisposable _localModuleMachineLock;
            private CompleteStackProvisioningComponents _components;
            private bool _disposed;

            internal PreparedCompleteStackResources(
                LmServiceProvisioningBatchRequest request,
                PathSafety pathSafety,
                LocalModuleCapabilityProfile capability,
                LocalModuleManifestStore manifests,
                WindowsLmProvisioningPlatform controllerPlatform,
                ILockedControllerInstaller controllerInstaller,
                VerifiedLocalModulePackage localModulePackage,
                IDisposable controllerMachineLock,
                IDisposable localModuleMachineLock)
            {
                if (request == null) throw new ArgumentNullException("request");
                if (pathSafety == null) throw new ArgumentNullException("pathSafety");
                if (capability == null) throw new ArgumentNullException("capability");
                if (manifests == null) throw new ArgumentNullException("manifests");
                if (controllerPlatform == null)
                {
                    throw new ArgumentNullException("controllerPlatform");
                }
                if (controllerInstaller == null)
                {
                    throw new ArgumentNullException("controllerInstaller");
                }
                if (localModulePackage == null)
                {
                    throw new ArgumentNullException("localModulePackage");
                }
                if (controllerMachineLock == null)
                {
                    throw new ArgumentNullException("controllerMachineLock");
                }
                if (localModuleMachineLock == null)
                {
                    throw new ArgumentNullException("localModuleMachineLock");
                }
                _request = request;
                _pathSafety = pathSafety;
                _capability = capability;
                _manifests = manifests;
                _controllerPlatform = controllerPlatform;
                _controllerInstaller = controllerInstaller;
                _localModulePackage = localModulePackage;
                _controllerMachineLock = controllerMachineLock;
                _localModuleMachineLock = localModuleMachineLock;
            }

            internal CompleteStackProvisioningComponents Initialize()
            {
                ThrowIfDisposed();
                if (_components != null)
                {
                    return _components;
                }

                LmServiceProvisioner controllerProvisioner =
                    new LmServiceProvisioner(_controllerPlatform);
                LmControllerInstallResult controllerInstall =
                    controllerProvisioner.InstallPreparedControllerVersion(
                        _request,
                        _controllerInstaller);
                if (controllerInstall == null ||
                    controllerInstall.Status !=
                        LmServiceProvisioningStatus.Succeeded)
                {
                    string message = controllerInstall == null ||
                        string.IsNullOrWhiteSpace(controllerInstall.Message)
                            ? "Не удалось установить проверенную версию контроллера."
                            : controllerInstall.Message;
                    if (controllerInstall != null &&
                        controllerInstall.Status ==
                            LmServiceProvisioningStatus.UnsupportedController)
                    {
                        throw new NotSupportedException(message);
                    }
                    throw new InvalidDataException(message);
                }

                LocalModuleRuntimeInstaller runtimeInstaller =
                    new LocalModuleRuntimeInstaller(
                        _manifests,
                        _pathSafety,
                        NoopLocalModuleMutationBoundary.Instance);
                LocalModuleInstallerSelection selection =
                    _request.LocalModuleInstallerSelection;
                string runtimeId = LocalModuleManagedIdentity.CreateRuntimeId(
                    _capability.CapabilityId,
                    selection.Sha256);
                LocalModuleRuntimeManifest runtime;
                if (_manifests.TryReadRuntime(runtimeId, out runtime))
                {
                    runtime = runtimeInstaller.VerifyExistingRuntime(
                        runtimeId,
                        selection,
                        _capability);
                }
                else
                {
                    runtime = runtimeInstaller.Install(
                        _localModulePackage,
                        selection,
                        _capability,
                        _request.OperationId,
                        OwnershipNonceGenerator.Create());
                }

                WindowsServiceApi services = new WindowsServiceApi();
                VerifiedProvisionerBinary supervisor =
                    VerifiedProvisionerBinary.ResolveCurrent(_pathSafety);
                LocalModuleWindowsServicePair pair =
                    new LocalModuleWindowsServicePair(services, supervisor);
                ManagedLocalModuleServiceReadinessProbe readiness =
                    new ManagedLocalModuleServiceReadinessProbe(
                        services,
                        new TcpListenerOwnerReader(),
                        new NativeProcessParentReader());
                LocalModuleServicePairLifecycle lifecycle =
                    new LocalModuleServicePairLifecycle(services, readiness);
                ManagedLocalModuleLifecycleJournalStore journals =
                    new ManagedLocalModuleLifecycleJournalStore(
                        _manifests.MachineRoot,
                        _pathSafety);
                ManagedLocalModuleProfileStore profiles =
                    new ManagedLocalModuleProfileStore(
                        _manifests,
                        _pathSafety,
                        new AtomicFileWriter());
                WindowsManagedLocalModulePlatform platform =
                    new WindowsManagedLocalModulePlatform(
                        _request.InitiatingSid,
                        runtime,
                        _capability,
                        _manifests,
                        journals,
                        profiles,
                        services,
                        pair,
                        lifecycle,
                        readiness,
                        new NativeEpmdCommandRunner(),
                        new LocalModulePortReservationFactory());
                ManagedLocalModuleProvisioningContext context =
                    new ManagedLocalModuleProvisioningContext(
                        runtime.RuntimeId,
                        _capability.CapabilityId,
                        _capability.ProductVersion);
                _components = new CompleteStackProvisioningComponents(
                    new ManagedLocalModuleProvisioner(platform),
                    context,
                    delegate(ManagedLocalModuleProvisioningItemRequest item)
                    {
                        return controllerProvisioner.EnsureSessionItem(
                            _request,
                            item);
                    });
                return _components;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                DisposeOne(ref _controllerInstaller);
                DisposeOne(ref _localModulePackage);
                DisposeOne(ref _localModuleMachineLock);
                DisposeOne(ref _controllerMachineLock);
                _components = null;
            }

            private void ThrowIfDisposed()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(
                        "PreparedCompleteStackResources");
                }
            }

            private static void DisposeOne<T>(ref T value)
                where T : class, IDisposable
            {
                T disposable = value;
                value = null;
                if (disposable != null)
                {
                    disposable.Dispose();
                }
            }
        }

        private sealed class WorkState
        {
            internal WorkState(
                LocalModuleInstanceManifest instance,
                ManagedLocalModuleLifecycleJournal journal)
            {
                Instance = instance;
                Journal = journal;
                if (instance != null)
                {
                    InstanceId = instance.InstanceId;
                    OwnershipNonce = instance.OwnershipNonce;
                }
                else if (journal != null)
                {
                    InstanceId = journal.InstanceId;
                    OwnershipNonce = journal.OwnershipNonce;
                }
            }

            internal string InstanceId { get; set; }
            internal string OwnershipNonce { get; set; }
            internal LocalModuleInstanceManifest Instance { get; set; }
            internal ManagedLocalModuleLifecycleJournal Journal { get; set; }
            internal IDisposable PortReservation { get; set; }
        }
    }

    internal static class OwnershipNonceGenerator
    {
        internal static string Create()
        {
            byte[] value = new byte[16];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(value);
            }
            System.Text.StringBuilder text =
                new System.Text.StringBuilder(value.Length * 2);
            for (int index = 0; index < value.Length; index++)
            {
                text.Append(value[index].ToString(
                    "x2",
                    CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }
    }

    internal static class GlobalOperationMutex
    {
        internal static IDisposable Acquire(string name)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                !name.StartsWith("Global\\KRS.MultiKKT.", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A canonical global operation mutex is required.",
                    "name");
            }
            Mutex mutex = new Mutex(false, name);
            try
            {
                try
                {
                    mutex.WaitOne();
                }
                catch (AbandonedMutexException)
                {
                }
                return new MutexLease(mutex);
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
        }

        private sealed class MutexLease : IDisposable
        {
            private Mutex _mutex;

            internal MutexLease(Mutex mutex)
            {
                _mutex = mutex;
            }

            public void Dispose()
            {
                Mutex mutex = _mutex;
                _mutex = null;
                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }
            }
        }
    }

    internal sealed class CompositeDisposable : IDisposable
    {
        private IDisposable _first;
        private IDisposable _second;

        internal CompositeDisposable(IDisposable first, IDisposable second)
        {
            if (first == null) throw new ArgumentNullException("first");
            if (second == null) throw new ArgumentNullException("second");
            _first = first;
            _second = second;
        }

        public void Dispose()
        {
            IDisposable first = _first;
            IDisposable second = _second;
            _first = null;
            _second = null;
            try
            {
                if (first != null) first.Dispose();
            }
            finally
            {
                if (second != null) second.Dispose();
            }
        }
    }
}
