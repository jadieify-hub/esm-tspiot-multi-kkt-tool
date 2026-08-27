using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class WindowsLmProvisioningPlatform : ILmProvisioningPlatform
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
            string operationId)
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
        }

        internal static WindowsLmProvisioningPlatform Create(
            string initiatingSid,
            string operationId)
        {
            ControllerCapabilityProfile profile =
                ControllerCapabilityProfile.SupportedVersion1632();
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
                operationId);
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
            _journalStore.Write(new ProvisioningOperationJournal
            {
                SchemaVersion = 1,
                OperationId = operationId,
                Operation = LmServiceOperation.EnsureBatch,
                KktSerial = item.KktSerial,
                State = MapJournalState(stage),
                ManifestFingerprint = string.Empty,
                UpdatedUtc = DateTime.UtcNow.ToString("o"),
                Stage = stage
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
                ManagedServiceLifecycleState.ServiceReady);
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
                _profile.Version);
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
                return item;
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

            internal WindowsLockedControllerInstaller(
                LockedInstallerArtifact artifact,
                OfficialControllerLocator locator,
                string version)
            {
                _artifact = artifact;
                _locator = locator;
                _version = version;
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
