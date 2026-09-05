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
    internal sealed class WindowsLmProvisioningPlatform : ILmServiceRemovalPlatform
    {
        private const int StopTimeoutMilliseconds = 30000;
        private readonly IWindowsServiceApi _serviceApi;
        private readonly ManagedServiceManifestStore _manifestStore;
        private readonly ProvisioningOperationJournalStore _journalStore;
        private readonly LmGatewaySupervisorService _supervisor;
        private readonly LmServiceReadinessProbe _readiness;
        private readonly IPathSafety _pathSafety;
        private readonly string _appDataRoot;
        private readonly VerifiedProvisionerBinary _supervisorBinary;

        private WindowsLmProvisioningPlatform(
            IWindowsServiceApi serviceApi,
            ManagedServiceManifestStore manifestStore,
            ProvisioningOperationJournalStore journalStore,
            LmGatewaySupervisorService supervisor,
            LmServiceReadinessProbe readiness,
            IPathSafety pathSafety,
            string appDataRoot,
            VerifiedProvisionerBinary supervisorBinary)
        {
            _serviceApi = serviceApi;
            _manifestStore = manifestStore;
            _journalStore = journalStore;
            _supervisor = supervisor;
            _readiness = readiness;
            _pathSafety = pathSafety;
            _appDataRoot = appDataRoot;
            _supervisorBinary = supervisorBinary;
        }

        internal static WindowsLmProvisioningPlatform CreateForRemoval(
            string initiatingSid)
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
                profile,
                supervisorBinary);
            LmServiceReadinessProbe readiness = new LmServiceReadinessProbe(
                serviceApi,
                new TcpListenerOwnerReader(),
                locator,
                manifestStore,
                profile);
            return new WindowsLmProvisioningPlatform(
                serviceApi,
                manifestStore,
                journalStore,
                supervisor,
                readiness,
                pathSafety,
                appDataRoot,
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
}
