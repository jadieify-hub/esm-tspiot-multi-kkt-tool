using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class WindowsDirectControllerPlatform : IDirectControllerPlatform
    {
        private const int ReadyTimeoutMilliseconds = 45000;
        private const int StopTimeoutMilliseconds = 30000;
        private readonly ControllerCapabilityProfile _profile;
        private readonly VerifiedControllerBinary _binary;
        private readonly DirectControllerManifestStore _manifests;
        private readonly IDirectControllerProfileStore _profiles;
        private readonly IWindowsServiceApi _services;
        private readonly IDirectControllerReadinessProbe _readiness;
        private readonly DirectControllerServiceDefinitionFactory _definitions;
        private readonly IEsmInstanceConfigManager _esmConfigs;

        internal WindowsDirectControllerPlatform(
            ControllerCapabilityProfile profile,
            VerifiedControllerBinary binary,
            DirectControllerManifestStore manifests,
            IDirectControllerProfileStore profiles,
            IWindowsServiceApi services,
            IDirectControllerReadinessProbe readiness)
            : this(
                profile,
                binary,
                manifests,
                profiles,
                services,
                readiness,
                DeferredEsmInstanceConfigManager.Instance)
        {
        }

        internal WindowsDirectControllerPlatform(
            ControllerCapabilityProfile profile,
            VerifiedControllerBinary binary,
            DirectControllerManifestStore manifests,
            IDirectControllerProfileStore profiles,
            IWindowsServiceApi services,
            IDirectControllerReadinessProbe readiness,
            IEsmInstanceConfigManager esmConfigs)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (binary == null) throw new ArgumentNullException("binary");
            if (manifests == null) throw new ArgumentNullException("manifests");
            if (profiles == null) throw new ArgumentNullException("profiles");
            if (services == null) throw new ArgumentNullException("services");
            if (readiness == null) throw new ArgumentNullException("readiness");
            if (esmConfigs == null) throw new ArgumentNullException("esmConfigs");
            _profile = profile;
            _binary = binary;
            _manifests = manifests;
            _profiles = profiles;
            _services = services;
            _readiness = readiness;
            _definitions = new DirectControllerServiceDefinitionFactory(profile, binary);
            _esmConfigs = esmConfigs;
        }

        internal static WindowsDirectControllerPlatform Create(string initiatingSid)
        {
            ControllerCapabilityProfile profile =
                ControllerCapabilityProfile.SupportedVersion1640();
            PathSafety pathSafety = new PathSafety();
            OfficialControllerLocator locator = new OfficialControllerLocator(
                profile,
                new WinTrustVerifier(),
                pathSafety);
            VerifiedControllerBinaryResult resolved = locator.ResolveVerifiedBinary();
            if (!resolved.IsSuccess || resolved.Binary == null)
            {
                throw new NotSupportedException(
                    resolved.ErrorMessage ??
                    "Не найден проверенный контроллер ЛМ ЧЗ версии 1.6.4.0.");
            }
            DirectControllerManifestStore manifests =
                DirectControllerManifestStore.CreateMachineStore(
                    pathSafety,
                    initiatingSid);
            DirectControllerCaStager ca = new DirectControllerCaStager(
                profile,
                new AtomicFileWriter(),
                pathSafety);
            DirectControllerProfileStore profiles = new DirectControllerProfileStore(
                profile,
                manifests,
                ca,
                new AtomicFileWriter(),
                pathSafety);
            WindowsServiceApi services = new WindowsServiceApi();
            DirectControllerReadinessProbe readiness =
                new DirectControllerReadinessProbe(
                    services,
                    new TcpListenerOwnerReader());
            EsmInstanceConfigManager esmConfigs = new EsmInstanceConfigManager(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ESP",
                    "ESM",
                    "um"),
                manifests,
                services,
                new AtomicFileWriter(),
                delegate { Thread.Sleep(250); });
            return new WindowsDirectControllerPlatform(
                profile,
                resolved.Binary,
                manifests,
                profiles,
                services,
                readiness,
                esmConfigs);
        }

        public LmServiceProvisioningItemResult Ensure(
            DirectControllerProvisioningItemRequest item,
            string operationId,
            string initiatingSid)
        {
            using (AcquireMutationLock())
            {
                DirectControllerManifest manifest = CreateManifest(
                    item,
                    operationId,
                    DirectControllerLifecycleState.Preparing);
                DirectControllerManifest existing = _manifests.Read(item.KktSerial);
                if (existing != null)
                {
                    RequireSameAssignment(existing, item);
                    manifest.EsmConfigOriginalSha256 =
                        existing.EsmConfigOriginalSha256;
                    manifest.EsmConfigAppliedSha256 =
                        existing.EsmConfigAppliedSha256;
                }
                string serviceName = DirectControllerIdentity.ServiceNameForOrdinal(item.Ordinal);
                WindowsServiceRecord service = _services.Query(serviceName);
                if (existing == null && service != null && item.Ordinal != 1)
                {
                    throw new InvalidOperationException(
                        "Служба " + serviceName +
                        " уже существует, но не принадлежит этой программе.");
                }
                _manifests.Write(manifest);
                try
                {
                    if (item.Ordinal == 1)
                    {
                        EnsureOfficialBase(service, manifest);
                    }
                    else
                    {
                        EnsureClone(
                            service,
                            manifest,
                            initiatingSid,
                            existing != null);
                    }
                    LmReadinessResult ready = _readiness.WaitUntilReady(
                        item.Ordinal,
                        ReadyTimeoutMilliseconds);
                    if (!ready.IsReady)
                    {
                        throw new InvalidOperationException(ready.Message);
                    }
                    EsmInstanceConfigApplyState configState =
                        _esmConfigs.ApplyAndRestart(manifest);
                    manifest.State = DirectControllerLifecycleState.Ready;
                    manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
                    _manifests.Write(manifest);
                    return Result(
                        item,
                        LmServiceProvisioningStatus.Succeeded,
                        (item.Ordinal == 1
                            ? "Штатный контроллер подтверждён и запущен."
                            : "Независимый контроллер создан и запущен.") +
                        (configState == EsmInstanceConfigApplyState.Deferred
                            ? " Конфигурация экземпляра ЕСМ ещё не создана; настройка отложена до следующего запуска."
                            : " Экземпляр ЕСМ направлен на этот контроллер."));
                }
                catch
                {
                    manifest.State = DirectControllerLifecycleState.RequiresAttention;
                    manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
                    _manifests.Write(manifest);
                    throw;
                }
            }
        }

        public LmServiceProvisioningItemResult Restart(
            DirectControllerProvisioningItemRequest item,
            string operationId,
            string initiatingSid)
        {
            using (AcquireMutationLock())
            {
                DirectControllerManifest manifest = RequireCurrentManifest(item);
                string serviceName = manifest.ServiceName;
                WindowsServiceRecord service = _services.Query(serviceName);
                if (service == null)
                {
                    throw new InvalidOperationException(
                        "Служба прямого контроллера отсутствует.");
                }
                RequireOwnedService(manifest, service, initiatingSid);
                if (service.State != WindowsServiceState.Stopped || service.ProcessId != 0)
                {
                    _services.RequestStop(serviceName);
                    if (!_readiness.WaitUntilStopped(
                            manifest.Ordinal,
                            StopTimeoutMilliseconds))
                    {
                        throw new InvalidOperationException(
                            "Служба прямого контроллера не остановилась штатно.");
                    }
                }
                _services.Start(serviceName);
                LmReadinessResult ready = _readiness.WaitUntilReady(
                    manifest.Ordinal,
                    ReadyTimeoutMilliseconds);
                if (!ready.IsReady) throw new InvalidOperationException(ready.Message);
                manifest.OperationId = operationId;
                manifest.State = DirectControllerLifecycleState.Ready;
                manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
                _manifests.Write(manifest);
                return Result(
                    item,
                    LmServiceProvisioningStatus.Succeeded,
                    "Контроллер перезапущен штатно.");
            }
        }

        public LmServiceProvisioningItemResult Remove(
            DirectControllerProvisioningItemRequest item,
            string operationId,
            string initiatingSid)
        {
            using (AcquireMutationLock())
            {
                DirectControllerManifest manifest = RequireCurrentManifest(item);
                _esmConfigs.RestoreAndRestart(manifest);
                if (manifest.Ordinal == 1)
                {
                    _manifests.Delete(
                        item.KktSerial,
                        _manifests.ComputeFingerprint(manifest));
                    return Result(
                        item,
                        LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                        "Назначение удалено; штатная служба контроллера не изменена, привязка ЕСМ сохранена.");
                }

                WindowsServiceRecord service = _services.Query(manifest.ServiceName);
                if (service != null)
                {
                    RequireOwnedService(manifest, service, initiatingSid);
                    manifest.OperationId = operationId;
                    manifest.State = DirectControllerLifecycleState.Removing;
                    manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
                    _manifests.Write(manifest);
                    if (service.State != WindowsServiceState.Stopped || service.ProcessId != 0)
                    {
                        _services.RequestStop(manifest.ServiceName);
                        if (!_readiness.WaitUntilStopped(
                                manifest.Ordinal,
                                StopTimeoutMilliseconds))
                        {
                            throw new InvalidOperationException(
                                "Служба прямого контроллера не остановилась штатно.");
                        }
                    }
                    _services.Delete(manifest.ServiceName);
                    if (!WaitUntilServiceDeleted(
                            manifest.ServiceName,
                            StopTimeoutMilliseconds))
                    {
                        throw new InvalidOperationException(
                            "Служба прямого контроллера помечена на удаление, но ещё существует.");
                    }
                }
                _manifests.DeleteCloneProfileEnvironmentRoot(manifest.Ordinal);
                _manifests.Delete(
                    item.KktSerial,
                    _manifests.ComputeFingerprint(manifest));
                return Result(
                    item,
                    LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                    "Служба и профиль контроллера удалены; привязка ЕСМ сохранена.");
            }
        }

        private bool WaitUntilServiceDeleted(
            string serviceName,
            int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            do
            {
                if (_services.Query(serviceName) == null) return true;
                Thread.Sleep(250);
            }
            while (DateTime.UtcNow < deadline);
            return _services.Query(serviceName) == null;
        }

        private void EnsureOfficialBase(
            WindowsServiceRecord service,
            DirectControllerManifest manifest)
        {
            if (service == null || !MatchesOfficialBase(service))
            {
                throw new InvalidOperationException(
                    "Штатная служба esm-lm-controller не совпадает с проверенным профилем 1.6.4.0.");
            }
            if (service.State == WindowsServiceState.Stopped && service.ProcessId == 0)
            {
                _services.Start(manifest.ServiceName);
            }
        }

        private void EnsureClone(
            WindowsServiceRecord service,
            DirectControllerManifest manifest,
            string initiatingSid,
            bool hasOwnedManifest)
        {
            WindowsServiceDefinition definition = _definitions.Create(
                manifest.Ordinal,
                manifest.ProfileEnvironmentRoot,
                initiatingSid);
            if (service != null && hasOwnedManifest &&
                MatchesInterruptedCloneCreation(definition, service))
            {
                _services.Delete(manifest.ServiceName);
                if (!WaitUntilServiceDeleted(
                        manifest.ServiceName,
                        StopTimeoutMilliseconds))
                {
                    throw new InvalidOperationException(
                        "Частично созданная служба контроллера ещё ожидает удаления SCM.");
                }
                service = null;
            }
            WindowsServiceRecord official = _services.Query("esm-lm-controller");
            if (official == null || !MatchesOfficialBase(official) ||
                official.RecoveryPolicy == null)
            {
                throw new InvalidOperationException(
                    "Нельзя скопировать параметры неподтверждённой штатной службы контроллера.");
            }
            definition.RecoveryPolicy = official.RecoveryPolicy;
            definition.Validate();
            if (service != null &&
                (service.State != WindowsServiceState.Stopped || service.ProcessId != 0))
            {
                if (WindowsServiceDefinitionMatcher.Matches(definition, service))
                {
                    LmReadinessResult alreadyReady = _readiness.WaitUntilReady(
                        manifest.Ordinal,
                        1000);
                    if (alreadyReady.IsReady) return;
                }
                _services.RequestStop(manifest.ServiceName);
                if (!_readiness.WaitUntilStopped(
                        manifest.Ordinal,
                        StopTimeoutMilliseconds))
                {
                    throw new InvalidOperationException(
                        "Служба прямого контроллера не остановилась штатно.");
                }
                service = _services.Query(manifest.ServiceName);
            }
            _profiles.PrepareClone(manifest);
            if (service == null)
            {
                _services.Create(definition);
            }
            else
            {
                RequireOwnedService(manifest, service, initiatingSid);
                if (!WindowsServiceDefinitionMatcher.Matches(definition, service))
                {
                    _services.Update(definition);
                }
            }
            WindowsServiceRecord configured = _services.Query(manifest.ServiceName);
            if (configured == null ||
                !WindowsServiceDefinitionMatcher.Matches(definition, configured))
            {
                throw new InvalidOperationException(
                    "SCM не подтвердил точную конфигурацию прямого контроллера.");
            }
            if (configured.State == WindowsServiceState.Stopped && configured.ProcessId == 0)
            {
                _services.Start(manifest.ServiceName);
            }
        }

        private static bool MatchesInterruptedCloneCreation(
            WindowsServiceDefinition expected,
            WindowsServiceRecord actual)
        {
            return expected != null && actual != null &&
                actual.State == WindowsServiceState.Stopped &&
                actual.ProcessId == 0 &&
                string.Equals(actual.ServiceName, expected.ServiceName, StringComparison.Ordinal) &&
                string.Equals(actual.DisplayName, expected.DisplayName, StringComparison.Ordinal) &&
                string.Equals(actual.ImagePath, expected.ImagePath, StringComparison.Ordinal) &&
                string.Equals(actual.Description, expected.Description, StringComparison.Ordinal) &&
                string.Equals(actual.AccountName, expected.AccountName, StringComparison.Ordinal) &&
                actual.Dependencies != null && actual.Dependencies.Count == 0 &&
                actual.StartMode == expected.StartMode &&
                actual.ErrorControl == expected.ErrorControl &&
                actual.ServiceSidType == expected.ServiceSidType &&
                (actual.EnvironmentVariables == null ||
                 actual.EnvironmentVariables.Count == 0);
        }

        private bool MatchesOfficialBase(WindowsServiceRecord service)
        {
            return string.Equals(service.ServiceName, "esm-lm-controller", StringComparison.Ordinal) &&
                string.Equals(
                    service.ImagePath,
                    WindowsCommandLine.QuoteArgument(_binary.FullPath),
                    StringComparison.Ordinal) &&
                string.Equals(service.AccountName, _profile.ServiceAccountName, StringComparison.Ordinal) &&
                service.Dependencies != null && service.Dependencies.Count == 0 &&
                service.StartMode == _profile.ServiceStartMode &&
                service.ErrorControl == _profile.ServiceErrorControl &&
                service.ServiceSidType == WindowsServiceSidType.None;
        }

        private void RequireOwnedService(
            DirectControllerManifest manifest,
            WindowsServiceRecord service,
            string initiatingSid)
        {
            if (manifest.Ordinal == 1)
            {
                if (!MatchesOfficialBase(service))
                {
                    throw new InvalidDataException(
                        "Штатная служба контроллера изменилась после инвентаризации.");
                }
                return;
            }
            WindowsServiceDefinition expected = _definitions.Create(
                manifest.Ordinal,
                manifest.ProfileEnvironmentRoot,
                initiatingSid);
            if (!string.Equals(service.ServiceName, expected.ServiceName, StringComparison.Ordinal) ||
                !string.Equals(service.ImagePath, expected.ImagePath, StringComparison.Ordinal) ||
                !string.Equals(service.AccountName, expected.AccountName, StringComparison.Ordinal) ||
                service.Dependencies == null || service.Dependencies.Count != 0 ||
                service.ServiceSidType != WindowsServiceSidType.None ||
                !ExactEnvironment(service.EnvironmentVariables, expected.EnvironmentVariables))
            {
                throw new InvalidDataException(
                    "Служба прямого контроллера больше не совпадает с нашим манифестом.");
            }
        }

        private DirectControllerManifest RequireCurrentManifest(
            DirectControllerProvisioningItemRequest item)
        {
            DirectControllerManifest manifest = _manifests.Read(item.KktSerial);
            if (manifest == null)
            {
                throw new InvalidDataException(
                    "Манифест прямого контроллера отсутствует.");
            }
            RequireSameAssignment(manifest, item);
            if (!string.Equals(
                    _manifests.ComputeFingerprint(manifest),
                    item.ExpectedManifestSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Отпечаток прямого контроллера изменился после подтверждения.");
            }
            return manifest;
        }

        private DirectControllerManifest CreateManifest(
            DirectControllerProvisioningItemRequest item,
            string operationId,
            DirectControllerLifecycleState state)
        {
            return DirectControllerManifest.Create(
                item.KktSerial,
                item.Inn,
                item.Ordinal,
                _binary.Version,
                _binary.Sha256,
                _manifests.GetProfileEnvironmentRoot(item.Ordinal),
                operationId,
                state);
        }

        private static void RequireSameAssignment(
            DirectControllerManifest manifest,
            DirectControllerProvisioningItemRequest item)
        {
            if (!string.Equals(manifest.KktSerial, item.KktSerial, StringComparison.Ordinal) ||
                !string.Equals(manifest.KktInn, item.Inn, StringComparison.Ordinal) ||
                manifest.Ordinal != item.Ordinal)
            {
                throw new InvalidDataException(
                    "Сохранённое назначение прямого контроллера не совпадает с планом.");
            }
        }

        private static bool ExactEnvironment(
            IDictionary<string, string> actual,
            IDictionary<string, string> expected)
        {
            if (actual == null || expected == null || actual.Count != 1 || expected.Count != 1)
            {
                return false;
            }
            string actualRoot;
            string expectedRoot;
            return actual.TryGetValue("ProgramData", out actualRoot) &&
                expected.TryGetValue("ProgramData", out expectedRoot) &&
                string.Equals(actualRoot, expectedRoot, StringComparison.Ordinal);
        }

        private static IDisposable AcquireMutationLock()
        {
            Mutex mutex = new Mutex(false, "Global\\KRS.MultiKKT.DirectControllers.v1");
            bool acquired = false;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(30000);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
                if (!acquired)
                {
                    throw new TimeoutException(
                        "Другой процесс уже изменяет прямые контроллеры.");
                }
                return new MutexLease(mutex);
            }
            catch
            {
                if (!acquired) mutex.Dispose();
                throw;
            }
        }

        private static LmServiceProvisioningItemResult Result(
            DirectControllerProvisioningItemRequest item,
            LmServiceProvisioningStatus status,
            string message)
        {
            return new LmServiceProvisioningItemResult
            {
                KktSerial = item.KktSerial,
                Status = status,
                Message = message
            };
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
                if (_mutex == null) return;
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
            }
        }
    }
}
