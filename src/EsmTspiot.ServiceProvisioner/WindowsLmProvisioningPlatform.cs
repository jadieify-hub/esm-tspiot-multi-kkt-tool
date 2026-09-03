using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class WindowsLmProvisioningPlatform :
        ILmProvisioningPlatform,
        ILmServiceRemovalPlatform
    {
        private const int StopTimeoutMilliseconds = 30000;
        private const int ReadyTimeoutMilliseconds = 45000;
        private readonly ControllerCapabilityProfile _profile;
        private readonly IWindowsServiceApi _serviceApi;
        private readonly OfficialControllerLocator _controllerLocator;
        private readonly ManagedServiceManifestStore _manifestStore;
        private readonly ProvisioningOperationJournalStore _journalStore;
        private readonly OfficialLmProfileAdapter _profileAdapter;
        private readonly LmGatewaySupervisorService _supervisor;
        private readonly LmServiceOwnershipVerifier _ownership;
        private readonly LmServiceReadinessProbe _readiness;
        private readonly IFileTrustVerifier _trustVerifier;
        private readonly IPathSafety _pathSafety;
        private readonly string _appDataRoot;
        private readonly string _operationId;
        private readonly VerifiedProvisionerBinary _supervisorBinary;
        private LmVerifiedController _lastVerifiedController;

        private WindowsLmProvisioningPlatform(
            ControllerCapabilityProfile profile,
            IWindowsServiceApi serviceApi,
            OfficialControllerLocator controllerLocator,
            ManagedServiceManifestStore manifestStore,
            ProvisioningOperationJournalStore journalStore,
            OfficialLmProfileAdapter profileAdapter,
            LmGatewaySupervisorService supervisor,
            LmServiceOwnershipVerifier ownership,
            LmServiceReadinessProbe readiness,
            IFileTrustVerifier trustVerifier,
            IPathSafety pathSafety,
            string appDataRoot,
            string operationId,
            VerifiedProvisionerBinary supervisorBinary)
        {
            _profile = profile;
            _serviceApi = serviceApi;
            _controllerLocator = controllerLocator;
            _manifestStore = manifestStore;
            _journalStore = journalStore;
            _profileAdapter = profileAdapter;
            _supervisor = supervisor;
            _ownership = ownership;
            _readiness = readiness;
            _trustVerifier = trustVerifier;
            _pathSafety = pathSafety;
            _appDataRoot = appDataRoot;
            _operationId = operationId;
            _supervisorBinary = supervisorBinary;
        }

        internal static WindowsLmProvisioningPlatform Create(
            string initiatingSid,
            string operationId)
        {
            ControllerCapabilityProfile profile =
                ControllerCapabilityProfile.Supported();
            PathSafety pathSafety = new PathSafety();
            WinTrustVerifier trustVerifier = new WinTrustVerifier();
            WindowsServiceApi serviceApi = new WindowsServiceApi();
            string appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "KRS",
                "MultiKKT");
            ManagedServiceManifestStore manifestStore =
                new ManagedServiceManifestStore(appDataRoot, pathSafety, initiatingSid);
            ProvisioningOperationJournalStore journalStore =
                new ProvisioningOperationJournalStore(appDataRoot, pathSafety);
            OfficialControllerLocator locator = new OfficialControllerLocator(
                profile,
                trustVerifier,
                pathSafety);
            VerifiedProvisionerBinary supervisorBinary =
                VerifiedProvisionerBinary.ResolveCurrent(pathSafety);
            LmGatewaySupervisorService supervisor = new LmGatewaySupervisorService(
                serviceApi,
                profile,
                supervisorBinary);
            OfficialLmProfileAdapter profileAdapter = new OfficialLmProfileAdapter(
                profile,
                manifestStore,
                new AtomicFileWriter(),
                serviceApi);
            LmServiceOwnershipVerifier ownership = new LmServiceOwnershipVerifier(
                serviceApi,
                manifestStore,
                journalStore,
                profileAdapter,
                supervisor);
            LmServiceReadinessProbe readiness = new LmServiceReadinessProbe(
                serviceApi,
                new TcpListenerOwnerReader(),
                locator,
                manifestStore,
                profile);
            return new WindowsLmProvisioningPlatform(
                profile,
                serviceApi,
                locator,
                manifestStore,
                journalStore,
                profileAdapter,
                supervisor,
                ownership,
                readiness,
                trustVerifier,
                pathSafety,
                appDataRoot,
                operationId,
                supervisorBinary);
        }

        public IDisposable AcquireMachineLock()
        {
            return AcquireMutex("Global\\KRS.MultiKKT.LmGateway.Machine.v1");
        }

        public IDisposable AcquireItemLock(string kktSerial)
        {
            string serviceName = LmServiceIdentity.CreateName(kktSerial);
            return AcquireMutex("Global\\KRS.MultiKKT.LmGateway.Item." + serviceName);
        }

        public void Reconcile(LmServiceProvisioningItemRequest item, string operationId)
        {
            _journalStore.ReadForKkt(item.KktSerial);
        }

        public LmVerifiedController VerifyController()
        {
            VerifiedControllerBinaryResult resolved =
                _controllerLocator.ResolveVerifiedBinary();
            if (!resolved.IsSuccess)
            {
                throw new NotSupportedException(resolved.ErrorMessage);
            }
            VerifiedProvisionerBinary supervisor =
                VerifiedProvisionerBinary.ResolveCurrent(_pathSafety);
            _lastVerifiedController = new LmVerifiedController
            {
                Version = resolved.Binary.Version,
                BinarySha256 = resolved.Binary.Sha256,
                SupervisorSha256 = supervisor.Sha256
            };
            return _lastVerifiedController;
        }

        public LmProvisioningObservedState Inspect(
            LmServiceProvisioningItemRequest item,
            string initiatingSid)
        {
            if (_lastVerifiedController == null)
            {
                throw new InvalidOperationException("Controller identity was not verified.");
            }
            return _ownership.Inspect(item, _lastVerifiedController);
        }

        public IDisposable ReservePorts(LmServiceProvisioningItemRequest item)
        {
            if (!_profile.ListenerUsesDualStackIpv6Wildcard)
            {
                throw new NotSupportedException("Listener endpoint profile is unsupported.");
            }
            return ExclusiveTcpPortReservation.AcquireDualStackWildcard(
                item.GrpcPort,
                item.RestPort);
        }

        public string DeriveServiceSid(string kktSerial)
        {
            return RestrictedServiceSid.Derive(LmServiceIdentity.CreateName(kktSerial));
        }

        public void WriteJournal(
            LmServiceProvisioningItemRequest item,
            string operationId,
            LmProvisioningJournalStage stage)
        {
            LmVerifiedController controller = _lastVerifiedController;
            if (controller == null)
            {
                throw new InvalidOperationException("Controller identity was not verified.");
            }
            string serviceName = LmServiceIdentity.CreateName(item.KktSerial);
            _journalStore.Write(new ProvisioningOperationJournal
            {
                SchemaVersion = 1,
                OperationId = operationId,
                Operation = LmServiceOperation.EnsureBatch,
                KktSerial = item.KktSerial,
                State = MapJournalState(stage),
                ManifestFingerprint = string.Empty,
                UpdatedUtc = DateTime.UtcNow.ToString("o"),
                Stage = stage,
                GrpcPort = item.GrpcPort,
                RestPort = item.RestPort,
                TargetAddress = item.TargetAddress,
                TargetPort = item.TargetPort,
                ServiceName = serviceName,
                SupervisorImagePath = _supervisorBinary.FullPath,
                ProfilePath = _manifestStore.GetProfileRoot(item.KktSerial),
                ServiceSid = RestrictedServiceSid.Derive(serviceName),
                ControllerVersion = controller.Version,
                ControllerBinarySha256 = controller.BinarySha256,
                SupervisorSha256 = controller.SupervisorSha256
            });
        }

        public void PrepareProfile(
            LmServiceProvisioningItemRequest item,
            string serviceSid)
        {
            ManagedLmServiceSpec spec = ToSpec(item);
            _profileAdapter.CreateOrLoadConfiguration(spec, serviceSid);
            _profileAdapter.ApplyConfiguration(spec, serviceSid);
        }

        public void ConfigureService(
            LmServiceProvisioningItemRequest item,
            string serviceSid,
            string initiatingSid)
        {
            WindowsServiceRecord configured = _supervisor.EnsureConfigured(
                item.KktSerial,
                initiatingSid);
            if (configured.State != WindowsServiceState.Stopped || configured.ProcessId != 0)
            {
                throw new InvalidOperationException(
                    "SCM mutation did not leave the service stopped with zero PID.");
            }
            string resolvedSid = RestrictedServiceSid.Resolve(configured.ServiceName);
            if (!string.Equals(resolvedSid, serviceSid, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Derived and registered service SIDs differ.");
            }
        }

        public void Start(LmServiceProvisioningItemRequest item)
        {
            _serviceApi.Start(LmServiceIdentity.CreateName(item.KktSerial));
        }

        public void RequestStop(LmServiceProvisioningItemRequest item)
        {
            string serviceName = LmServiceIdentity.CreateName(item.KktSerial);
            WindowsServiceRecord service = _serviceApi.Query(serviceName);
            if (service == null)
            {
                return;
            }
            LmServiceProvisioningItemRequest complete = CompletePortsFromManifest(item);
            if (service.State != WindowsServiceState.Stopped || service.ProcessId != 0)
            {
                _serviceApi.RequestStop(serviceName);
            }
            if (complete.GrpcPort > 0 && complete.RestPort > 0)
            {
                if (!_readiness.WaitUntilStopped(complete, StopTimeoutMilliseconds))
                {
                    throw new InvalidOperationException(
                        "Служба или ее listener-процессы не завершились штатно.");
                }
            }
            else
            {
                WaitForServiceStopped(serviceName);
            }
        }

        public LmReadinessResult Probe(LmServiceProvisioningItemRequest item)
        {
            return _readiness.WaitUntilReady(
                item,
                DeriveServiceSid(item.KktSerial),
                ReadyTimeoutMilliseconds);
        }

        public void WriteManifest(
            LmServiceProvisioningItemRequest item,
            LmVerifiedController controller,
            string serviceSid,
            string operationId)
        {
            ManagedServiceManifest manifest = ManagedServiceManifest.Create(
                item.KktSerial,
                new LmGatewayPorts(item.GrpcPort, item.RestPort),
                new LmGatewayTarget(item.TargetAddress, item.TargetPort),
                controller.Version,
                controller.BinarySha256,
                controller.SupervisorSha256,
                serviceSid,
                operationId,
                ManagedServiceLifecycleState.ServiceReady,
                _supervisorBinary.FullPath,
                _manifestStore.GetProfileRoot(item.KktSerial));
            _manifestStore.Write(manifest);
        }

        public void CompleteJournal(
            LmServiceProvisioningItemRequest item,
            string operationId)
        {
            _journalStore.DeleteForKkt(item.KktSerial);
        }

        public IList<string> GetManagedSerials()
        {
            return _manifestStore.ReadManagedSerials();
        }

        public void MarkVersionPending(string kktSerial, string operationId)
        {
            ManagedServiceManifest manifest = _manifestStore.Read(kktSerial);
            manifest.LocalLifecycleState =
                ManagedServiceLifecycleState.VersionVerificationPending;
            manifest.OperationId = operationId;
            manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
            _manifestStore.Write(manifest);
        }

        public ILockedControllerInstaller PrepareInstaller(
            LmControllerInstallerSelection selection,
            string operationId)
        {
            if (!string.Equals(operationId, _operationId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Installer operation identity changed.");
            }
            string stagingRoot = Path.Combine(_appDataRoot, "InstallerStaging");
            OfficialControllerInstallerVerifier verifier =
                new OfficialControllerInstallerVerifier(
                    _profile,
                    _trustVerifier,
                    _pathSafety,
                    stagingRoot,
                    operationId);
            LockedInstallerArtifact artifact = verifier.VerifyStageAndLock(selection);
            return new WindowsLockedControllerInstaller(
                artifact,
                _controllerLocator,
                _profile.Version,
                _profile.InstallerArguments);
        }

        public void ReconcileRemoval(string kktSerial, string operationId)
        {
            _journalStore.ReadForKkt(kktSerial);
        }

        public LmRemovalOwnershipState InspectRemoval(
            string kktSerial,
            string manifestFingerprint,
            bool cleanupOnly)
        {
            ManagedServiceManifest manifest;
            bool hasManifest = _manifestStore.TryRead(kktSerial, out manifest);
            ProvisioningOperationJournal journal = null;
            if (!hasManifest)
            {
                IList<ProvisioningOperationJournal> journals =
                    _journalStore.ReadForKkt(kktSerial);
                if (journals.Count == 0)
                {
                    return LmRemovalOwnershipState.Missing;
                }
                journal = journals[journals.Count - 1];
            }
            string observedFingerprint = hasManifest
                ? _manifestStore.ReadProjection(kktSerial).ManifestFingerprint.Sha256
                : _journalStore.GetFingerprint(journal.OperationId, kktSerial);
            if (!CanonicalLmPlanHasher.FixedTimeEqualsHex(
                manifestFingerprint,
                observedFingerprint))
            {
                return LmRemovalOwnershipState.ConfirmationMismatch;
            }

            string serviceName = LmServiceIdentity.CreateName(kktSerial);
            string ownedServiceName = hasManifest ? manifest.ServiceName : journal.ServiceName;
            string ownedSupervisorPath = hasManifest
                ? manifest.SupervisorImagePath
                : journal.SupervisorImagePath;
            string ownedProfilePath = hasManifest ? manifest.ProfilePath : journal.ProfilePath;
            string ownedServiceSid = hasManifest ? manifest.ServiceSid : journal.ServiceSid;
            if (!string.Equals(ownedServiceName, serviceName, StringComparison.Ordinal) ||
                !string.Equals(
                    ownedServiceSid,
                    RestrictedServiceSid.Derive(serviceName),
                    StringComparison.Ordinal))
            {
                return LmRemovalOwnershipState.RequiresAttention;
            }
            string expectedProfile = _manifestStore.GetProfileRoot(kktSerial);
            if (string.IsNullOrEmpty(ownedSupervisorPath) ||
                !string.Equals(
                    Path.GetFullPath(ownedSupervisorPath),
                    _supervisorBinary.FullPath,
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(ownedProfilePath) ||
                !string.Equals(
                    Path.GetFullPath(ownedProfilePath),
                    expectedProfile,
                    StringComparison.OrdinalIgnoreCase))
            {
                return LmRemovalOwnershipState.ImageMismatch;
            }
            ValidationResult profileSafety = _pathSafety.ValidateProtected(
                expectedProfile,
                _appDataRoot,
                ownedServiceSid);
            if (!profileSafety.IsValid)
            {
                return LmRemovalOwnershipState.RequiresAttention;
            }
            WindowsServiceRecord service = _serviceApi.Query(serviceName);
            if (service == null)
            {
                return LmRemovalOwnershipState.CleanupOnly;
            }
            if (cleanupOnly)
            {
                return LmRemovalOwnershipState.RequiresAttention;
            }
            if (!string.Equals(
                service.Description,
                LmGatewaySupervisorService.DescriptionPrefix + kktSerial,
                StringComparison.Ordinal))
            {
                return LmRemovalOwnershipState.MarkerMismatch;
            }
            if (!_supervisor.IsManagedDefinition(kktSerial, service))
            {
                return LmRemovalOwnershipState.ImageMismatch;
            }

            if (service.State == WindowsServiceState.Running)
            {
                return LmRemovalOwnershipState.FullyOwnedRunning;
            }
            if (service.State == WindowsServiceState.Stopped && service.ProcessId == 0)
            {
                return LmRemovalOwnershipState.FullyOwnedStopped;
            }
            return LmRemovalOwnershipState.RequiresAttention;
        }

        public void WriteRemovalJournal(
            string kktSerial,
            string operationId,
            LmServiceOperation operation,
            ManagedServiceLifecycleState state,
            string manifestFingerprint)
        {
            LmProvisioningJournalStage stage =
                state == ManagedServiceLifecycleState.Deleting
                    ? LmProvisioningJournalStage.Deleting
                    : LmProvisioningJournalStage.Cleaning;
            ManagedServiceManifest manifest;
            bool hasManifest = _manifestStore.TryRead(kktSerial, out manifest);
            ProvisioningOperationJournal source = null;
            if (!hasManifest)
            {
                IList<ProvisioningOperationJournal> existing =
                    _journalStore.ReadForKkt(kktSerial);
                if (existing.Count > 0)
                {
                    source = existing[existing.Count - 1];
                }
                if (source == null)
                {
                    throw new InvalidDataException(
                        "Removal journal has no verified manifest or prior operation state.");
                }
            }
            _journalStore.Write(new ProvisioningOperationJournal
            {
                SchemaVersion = 1,
                OperationId = operationId,
                Operation = operation,
                KktSerial = kktSerial,
                State = state,
                ManifestFingerprint = manifestFingerprint,
                UpdatedUtc = DateTime.UtcNow.ToString("o"),
                Stage = stage,
                GrpcPort = hasManifest ? manifest.GrpcPort : source.GrpcPort,
                RestPort = hasManifest ? manifest.RestPort : source.RestPort,
                TargetAddress = hasManifest ? manifest.TargetAddress : source.TargetAddress,
                TargetPort = hasManifest ? manifest.TargetPort : source.TargetPort,
                ServiceName = hasManifest ? manifest.ServiceName : source.ServiceName,
                SupervisorImagePath = hasManifest
                    ? manifest.SupervisorImagePath
                    : source.SupervisorImagePath,
                ProfilePath = hasManifest ? manifest.ProfilePath : source.ProfilePath,
                ServiceSid = hasManifest ? manifest.ServiceSid : source.ServiceSid,
                ControllerVersion = hasManifest
                    ? manifest.ControllerVersion
                    : source.ControllerVersion,
                ControllerBinarySha256 = hasManifest
                    ? manifest.ControllerBinarySha256
                    : source.ControllerBinarySha256,
                SupervisorSha256 = hasManifest
                    ? manifest.SupervisorSha256
                    : source.SupervisorSha256
            });
        }

        public void RequestNormalStop(string kktSerial)
        {
            _serviceApi.RequestStop(LmServiceIdentity.CreateName(kktSerial));
        }

        public void WaitUntilStopped(string kktSerial)
        {
            LmServiceProvisioningItemRequest item = CompletePortsFromManifest(
                new LmServiceProvisioningItemRequest { KktSerial = kktSerial });
            if (item.GrpcPort <= 0 || item.RestPort <= 0 ||
                !_readiness.WaitUntilStopped(item, StopTimeoutMilliseconds))
            {
                throw new InvalidOperationException(
                    "Служба, дочерний процесс или listener не завершились штатно.");
            }
        }

        public LmScmDeletionState DeleteServiceAndConfirmAbsent(string kktSerial)
        {
            string serviceName = LmServiceIdentity.CreateName(kktSerial);
            try
            {
                _serviceApi.Delete(serviceName);
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1072)
                {
                    return LmScmDeletionState.MarkedForDelete;
                }
                if (ex.NativeErrorCode == 5)
                {
                    return LmScmDeletionState.AccessDenied;
                }
                throw;
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            do
            {
                try
                {
                    if (_serviceApi.Query(serviceName) == null)
                    {
                        return LmScmDeletionState.Absent;
                    }
                }
                catch (Win32Exception ex)
                {
                    if (ex.NativeErrorCode == 1072)
                    {
                        return LmScmDeletionState.MarkedForDelete;
                    }
                    if (ex.NativeErrorCode == 5)
                    {
                        return LmScmDeletionState.AccessDenied;
                    }
                    throw;
                }
                Thread.Sleep(250);
            }
            while (DateTime.UtcNow < deadline);
            return LmScmDeletionState.TimedOut;
        }

        public void MarkCleanupPending(
            string kktSerial,
            string operationId,
            string errorClass)
        {
            ManagedServiceManifest manifest;
            if (!_manifestStore.TryRead(kktSerial, out manifest))
            {
                ProvisioningOperationJournal journal = GetLatestJournal(kktSerial);
                manifest = ManagedServiceManifest.Create(
                    kktSerial,
                    new LmGatewayPorts(journal.GrpcPort, journal.RestPort),
                    new LmGatewayTarget(journal.TargetAddress, journal.TargetPort),
                    journal.ControllerVersion,
                    journal.ControllerBinarySha256,
                    journal.SupervisorSha256,
                    journal.ServiceSid,
                    operationId,
                    ManagedServiceLifecycleState.CleanupPending,
                    journal.SupervisorImagePath,
                    journal.ProfilePath);
            }
            manifest.LocalLifecycleState = ManagedServiceLifecycleState.CleanupPending;
            manifest.OperationId = operationId;
            manifest.LastCleanupErrorClass = errorClass ?? string.Empty;
            manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
            _manifestStore.Write(manifest);
        }

        public void CleanupProfile(string kktSerial)
        {
            ManagedServiceManifest manifest;
            if (!_manifestStore.TryRead(kktSerial, out manifest))
            {
                ProvisioningOperationJournal journal = GetLatestJournal(kktSerial);
                _manifestStore.DeleteProfile(kktSerial, journal.ServiceSid);
                return;
            }
            _manifestStore.DeleteProfile(kktSerial, manifest.ServiceSid);
        }

        public void CleanupManifest(string kktSerial)
        {
            ManagedServiceManifest ignored;
            if (_manifestStore.TryRead(kktSerial, out ignored))
            {
                _manifestStore.Delete(kktSerial);
            }
        }

        public void CompleteRemoval(string kktSerial)
        {
            _journalStore.DeleteForKkt(kktSerial);
        }

        private ProvisioningOperationJournal GetLatestJournal(string kktSerial)
        {
            IList<ProvisioningOperationJournal> journals =
                _journalStore.ReadForKkt(kktSerial);
            if (journals.Count == 0)
            {
                throw new InvalidDataException("Managed removal journal is missing.");
            }
            return journals[journals.Count - 1];
        }

        private LmServiceProvisioningItemRequest CompletePortsFromManifest(
            LmServiceProvisioningItemRequest item)
        {
            if (item.GrpcPort > 0 && item.RestPort > 0)
            {
                return item;
            }
            ManagedServiceManifest manifest;
            if (!_manifestStore.TryRead(item.KktSerial, out manifest))
            {
                IList<ProvisioningOperationJournal> journals =
                    _journalStore.ReadForKkt(item.KktSerial);
                if (journals.Count == 0)
                {
                    return item;
                }
                ProvisioningOperationJournal journal = journals[journals.Count - 1];
                return new LmServiceProvisioningItemRequest
                {
                    KktSerial = item.KktSerial,
                    GrpcPort = journal.GrpcPort,
                    RestPort = journal.RestPort,
                    TargetAddress = journal.TargetAddress,
                    TargetPort = journal.TargetPort
                };
            }
            return new LmServiceProvisioningItemRequest
            {
                KktSerial = item.KktSerial,
                GrpcPort = manifest.GrpcPort,
                RestPort = manifest.RestPort,
                TargetAddress = manifest.TargetAddress,
                TargetPort = manifest.TargetPort
            };
        }

        private void WaitForServiceStopped(string serviceName)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(StopTimeoutMilliseconds);
            do
            {
                WindowsServiceRecord service = _serviceApi.Query(serviceName);
                if (service == null ||
                    (service.State == WindowsServiceState.Stopped && service.ProcessId == 0))
                {
                    return;
                }
                Thread.Sleep(250);
            }
            while (DateTime.UtcNow < deadline);
            throw new InvalidOperationException("Служба не остановилась в отведенное время.");
        }

        private static ManagedLmServiceSpec ToSpec(LmServiceProvisioningItemRequest item)
        {
            return new ManagedLmServiceSpec(
                item.KktSerial,
                new LmGatewayPorts(item.GrpcPort, item.RestPort),
                new LmGatewayTarget(item.TargetAddress, item.TargetPort));
        }

        private static IDisposable AcquireMutex(string name)
        {
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

        private static ManagedServiceLifecycleState MapJournalState(
            LmProvisioningJournalStage stage)
        {
            if (stage == LmProvisioningJournalStage.Failed)
            {
                return ManagedServiceLifecycleState.Failed;
            }
            if (stage == LmProvisioningJournalStage.Started ||
                stage == LmProvisioningJournalStage.ServiceReady)
            {
                return ManagedServiceLifecycleState.Creating;
            }
            if (stage == LmProvisioningJournalStage.Updating)
            {
                return ManagedServiceLifecycleState.Updating;
            }
            return ManagedServiceLifecycleState.Creating;
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

        private sealed class WindowsLockedControllerInstaller : ILockedControllerInstaller
        {
            private LockedInstallerArtifact _artifact;
            private readonly OfficialControllerLocator _locator;
            private readonly string _version;
            private readonly string _arguments;

            internal WindowsLockedControllerInstaller(
                LockedInstallerArtifact artifact,
                OfficialControllerLocator locator,
                string version,
                string arguments)
            {
                _artifact = artifact;
                _locator = locator;
                _version = version;
                _arguments = arguments ?? string.Empty;
            }

            public LmControllerInstallResult Run()
            {
                if (_artifact == null)
                {
                    throw new ObjectDisposedException("installer");
                }
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _artifact.FullPath,
                    Arguments = _arguments,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(_artifact.FullPath)
                };
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        throw new InvalidOperationException("Не удалось запустить установщик.");
                    }
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        return new LmControllerInstallResult
                        {
                            Status = LmServiceProvisioningStatus.Failed,
                            Message = "Установщик завершился с кодом " +
                                process.ExitCode.ToString() + "."
                        };
                    }
                }

                VerifiedControllerBinaryResult installed = _locator.ResolveVerifiedBinary();
                if (!installed.IsSuccess)
                {
                    return new LmControllerInstallResult
                    {
                        Status = LmServiceProvisioningStatus.UnsupportedController,
                        Message = installed.ErrorMessage
                    };
                }
                return new LmControllerInstallResult
                {
                    Status = LmServiceProvisioningStatus.Succeeded,
                    InstalledVersion = _version,
                    Message = "Версия контроллера установлена и проверена. " +
                        "Управляемые службы оставлены остановленными до явного обновления."
                };
            }

            public void Dispose()
            {
                LockedInstallerArtifact artifact = _artifact;
                _artifact = null;
                if (artifact != null)
                {
                    artifact.Dispose();
                }
            }
        }
    }
}
