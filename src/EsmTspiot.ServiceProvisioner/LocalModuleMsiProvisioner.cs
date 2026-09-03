using System;
using System.IO;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiProvisioner
    {
        internal LocalModuleMsiProvisioningItemResult Ensure(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiProvisioningContext context)
        {
            if (context == null) throw new ArgumentNullException("context");
            try
            {
                LocalModuleMsiProvisioningContext.ValidateRequest(request);
                using (context.AcquireMutationLock())
                    return EnsureLocked(request, context);
            }
            catch (Exception exception)
            {
                return Result(
                    request,
                    LmServiceProvisioningStatus.Failed,
                    SafeMessage(exception),
                    string.Empty);
            }
        }

        internal LocalModuleMsiProvisioningItemResult Restart(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiProvisioningContext context)
        {
            if (context == null) throw new ArgumentNullException("context");
            try
            {
                LocalModuleMsiProvisioningContext.ValidateRequest(request);
                using (context.AcquireMutationLock())
                {
                    LocalModuleMsiManifest manifest =
                        RequireCurrentManifest(request, context);
                    LocalModuleMsiObservedState observed =
                        RequireUnconflicted(request, manifest, context);
                    context.WriteStage(request, manifest.OwnershipNonce,
                        LocalModuleMsiLifecycleStage.ServicesStopping,
                        null, false);
                    if (observed.Running)
                        context.Platform.StopAndVerify(request, manifest);
                    context.WriteStage(request, manifest.OwnershipNonce,
                        LocalModuleMsiLifecycleStage.ServicesStarting,
                        null, false);
                    context.Platform.StartAndVerify(request, manifest);
                    RequireReady(request, manifest, context);
                    CompleteJournal(request, manifest, context);
                    return Result(request,
                        LmServiceProvisioningStatus.Succeeded,
                        "Локальный модуль перезапущен штатно.",
                        manifest.ManifestSha256);
                }
            }
            catch (Exception exception)
            {
                return Result(request,
                    LmServiceProvisioningStatus.RequiresAttention,
                    SafeMessage(exception),
                    string.Empty);
            }
        }

        private LocalModuleMsiProvisioningItemResult EnsureLocked(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiProvisioningContext context)
        {
            LocalModuleMsiManifest manifest = context.Manifests.Read(request.Inn);
            LocalModuleMsiLifecycleJournal pending =
                context.Journals.Read(request.Inn);
            if (pending != null &&
                pending.RollbackOwnedChanges)
            {
                if (!TryRollback(request, manifest, context, pending, null))
                    return Result(request,
                        LmServiceProvisioningStatus.CleanupPending,
                        "Не завершена очистка предыдущей установки ЛМ.",
                        manifest == null ? string.Empty :
                            manifest.ManifestSha256);
                manifest = null;
            }

            string ownershipNonce = manifest == null
                ? pending == null
                    ? context.NewOwnershipNonce()
                    : pending.OwnershipNonce
                : manifest.OwnershipNonce;
            bool createdThisOperation = false;
            context.WriteStage(request, ownershipNonce,
                LocalModuleMsiLifecycleStage.Observing, null, false);
            try
            {
                LocalModuleMsiObservedState observed =
                    RequireUnconflicted(request, manifest, context);
                if (manifest == null)
                {
                    bool installProduct = !observed.ProductPresent;
                    if (observed.ProductPresent)
                    {
                        if (request.CloneOrdinal != 0 ||
                            !observed.ProductMatches)
                            throw new InvalidDataException(
                                "Установленный MSI ЛМ не принадлежит этой операции.");
                        manifest = context.Platform.AdoptPreExistingBase(
                            request, ownershipNonce);
                    }
                    else
                    {
                        manifest = context.Platform.PrepareInstall(
                            request, ownershipNonce);
                    }
                    createdThisOperation = true;
                    context.WriteStage(request, ownershipNonce,
                        LocalModuleMsiLifecycleStage.ManifestPersisting,
                        null, true);
                    context.Manifests.Write(manifest);
                    manifest = context.Manifests.Read(request.Inn);
                    if (installProduct)
                    {
                        context.WriteStage(request, ownershipNonce,
                            LocalModuleMsiLifecycleStage.Installing,
                            null, true);
                        context.Platform.Install(request, manifest);
                    }
                    observed = RequireUnconflicted(
                        request, manifest, context);
                }
                else
                {
                    RequireManifestMatches(request, manifest);
                }

                if (observed.IsExactReady)
                {
                    CompleteJournal(request, manifest, context);
                    return Result(request,
                        LmServiceProvisioningStatus.Succeeded,
                        "Локальный модуль уже настроен и запущен.",
                        manifest.ManifestSha256);
                }
                RequireInstalledShape(observed);
                if (!observed.FirewallPresent)
                {
                    context.WriteStage(request, ownershipNonce,
                        LocalModuleMsiLifecycleStage.FirewallEnsuring,
                        null, createdThisOperation);
                    LocalModuleFirewallRule firewall =
                        context.Platform.EnsureFirewall(request, manifest);
                    manifest.AttachFirewallRule(firewall);
                    context.Manifests.Write(manifest);
                    manifest = context.Manifests.Read(request.Inn);
                }
                if (!observed.AutomaticStart)
                {
                    context.WriteStage(request, ownershipNonce,
                        LocalModuleMsiLifecycleStage.StartModeEnsuring,
                        null, createdThisOperation);
                    LocalModuleStartModeAdjustment startModes =
                        context.Platform.EnsureAutomaticStart(request, manifest);
                    if (startModes.Adjusted && !manifest.StartModeAdjusted)
                    {
                        manifest.RecordStartModeAdjustment(
                            startModes.PreviousApiStartMode,
                            startModes.PreviousDatabaseStartMode);
                        context.Manifests.Write(manifest);
                        manifest = context.Manifests.Read(request.Inn);
                    }
                }
                context.WriteStage(request, ownershipNonce,
                    LocalModuleMsiLifecycleStage.ServicesStarting,
                    null, createdThisOperation);
                context.Platform.StartAndVerify(request, manifest);
                RequireReady(request, manifest, context);
                CompleteJournal(request, manifest, context);
                return Result(request,
                    LmServiceProvisioningStatus.Succeeded,
                    "Локальный модуль установлен и запущен; автозапуск служб включён.",
                    manifest.ManifestSha256);
            }
            catch (Exception exception)
            {
                if (createdThisOperation)
                {
                    bool cleaned = TryRollback(
                        request,
                        manifest,
                        context,
                        null,
                        exception);
                    return Result(request,
                        cleaned
                            ? LmServiceProvisioningStatus.Failed
                            : LmServiceProvisioningStatus.CleanupPending,
                        cleaned
                            ? SafeMessage(exception)
                            : "Ошибка установки; очистка будет повторена.",
                        manifest == null ? string.Empty :
                            manifest.ManifestSha256);
                }
                if (manifest == null)
                {
                    context.Journals.Delete(
                        request.Inn,
                        context.OperationId,
                        ownershipNonce);
                    return Result(request,
                        LmServiceProvisioningStatus.Failed,
                        SafeMessage(exception),
                        string.Empty);
                }
                context.WriteStage(request, ownershipNonce,
                    LocalModuleMsiLifecycleStage.CleanupPending,
                    exception, false);
                return Result(request,
                    LmServiceProvisioningStatus.RequiresAttention,
                    SafeMessage(exception),
                    manifest == null ? string.Empty :
                        manifest.ManifestSha256);
            }
        }

        private static bool TryRollback(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest,
            LocalModuleMsiProvisioningContext context,
            LocalModuleMsiLifecycleJournal pending,
            Exception original)
        {
            string nonce = manifest != null
                ? manifest.OwnershipNonce
                : pending == null ? context.NewOwnershipNonce() :
                    pending.OwnershipNonce;
            try
            {
                context.WriteStage(request, nonce,
                    LocalModuleMsiLifecycleStage.CleanupPending,
                    original,
                    true);
                if (manifest != null)
                {
                    LocalModuleMsiObservedState observed =
                        context.Platform.Observe(request, manifest);
                    if (observed.Running && !manifest.PreExisting)
                    {
                        context.WriteStage(request, nonce,
                            LocalModuleMsiLifecycleStage.ServicesStopping,
                            null, true);
                        context.Platform.StopAndVerify(request, manifest);
                    }
                    if (!string.IsNullOrEmpty(manifest.FirewallRuleName))
                    {
                        context.WriteStage(request, nonce,
                            LocalModuleMsiLifecycleStage.FirewallRemoving,
                            null, true);
                        context.Platform.RemoveFirewall(manifest);
                    }
                    if (manifest.StartModeAdjusted)
                    {
                        context.WriteStage(request, nonce,
                            LocalModuleMsiLifecycleStage.StartModeRestoring,
                            null, true);
                        context.Platform.RestoreStartMode(manifest);
                    }
                    if (manifest.CanRemove)
                    {
                        context.WriteStage(request, nonce,
                            LocalModuleMsiLifecycleStage.ProductUninstalling,
                            null, true);
                        context.Platform.Uninstall(manifest);
                    }
                    LocalModuleMsiManifest persisted =
                        context.Manifests.Read(manifest.Inn);
                    if (persisted != null)
                    {
                        context.WriteStage(request, nonce,
                            LocalModuleMsiLifecycleStage.ManifestDeleting,
                            null, true);
                        context.Manifests.Delete(
                            persisted.Inn,
                            persisted.ManifestSha256);
                    }
                }
                context.Journals.Delete(
                    request.Inn,
                    context.OperationId,
                    nonce);
                return true;
            }
            catch (Exception cleanupError)
            {
                context.WriteStage(request, nonce,
                    LocalModuleMsiLifecycleStage.CleanupPending,
                    cleanupError,
                    true);
                return false;
            }
        }

        internal static LocalModuleMsiManifest RequireCurrentManifest(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiProvisioningContext context)
        {
            LocalModuleMsiManifest manifest = context.Manifests.Read(request.Inn);
            if (manifest == null)
                throw new InvalidDataException(
                    "Манифест локального модуля отсутствует.");
            RequireManifestMatches(request, manifest);
            if (string.IsNullOrEmpty(request.ExpectedManifestSha256) ||
                !string.Equals(request.ExpectedManifestSha256,
                    manifest.ManifestSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Отпечаток локального модуля изменился после подтверждения.");
            return manifest;
        }

        internal static LocalModuleMsiObservedState RequireUnconflicted(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest,
            LocalModuleMsiProvisioningContext context)
        {
            LocalModuleMsiObservedState observed =
                context.Platform.Observe(request, manifest);
            if (observed == null)
                throw new InvalidDataException(
                    "Инвентарь локального модуля отсутствует.");
            if (!string.IsNullOrEmpty(observed.ConflictMessage))
                throw new InvalidDataException(observed.ConflictMessage);
            return observed;
        }

        private static void RequireManifestMatches(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest)
        {
            LocalModuleMsiManifest.Validate(manifest);
            if (!string.Equals(manifest.Inn, request.Inn,
                    StringComparison.Ordinal) ||
                manifest.CloneOrdinal != request.CloneOrdinal ||
                manifest.ApiPort != request.ApiPort ||
                manifest.DatabasePort != request.DatabasePort ||
                !string.Equals(
                    manifest.InstallRoot,
                    ExpectedInstallRoot(request),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Сохранённое назначение локального модуля не совпадает с планом.");
            if (!string.IsNullOrEmpty(manifest.FirewallRuleName))
            {
                LocalModuleFirewallRule expected =
                    LocalModuleFirewallRule.Create(
                        manifest.OwnershipNonce,
                        manifest.FirewallProgramPath,
                        request.ApiPort,
                        request.RemoteAddress);
                if (!string.Equals(expected.ExpectedFieldHash,
                        manifest.FirewallRuleHash,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Сохранённое правило сети не совпадает с планом.");
            }
            if (!string.IsNullOrEmpty(request.ExpectedManifestSha256) &&
                !string.Equals(request.ExpectedManifestSha256,
                    manifest.ManifestSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Отпечаток локального модуля изменился после инвентаризации.");
        }

        private static void RequireInstalledShape(
            LocalModuleMsiObservedState observed)
        {
            if (!observed.ProductPresent || !observed.ProductMatches ||
                !observed.ConfigurationMatches ||
                !observed.ServicesPresent || !observed.ServicesMatch ||
                (observed.FirewallPresent && !observed.FirewallMatches))
                throw new InvalidDataException(
                    "Установленный локальный модуль не совпадает с планом.");
        }

        private static void RequireReady(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest,
            LocalModuleMsiProvisioningContext context)
        {
            LocalModuleMsiObservedState ready =
                RequireUnconflicted(request, manifest, context);
            if (!ready.IsExactReady)
                throw new InvalidOperationException(
                    "Локальный модуль не подтвердил службы и listeners.");
        }

        private static void CompleteJournal(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest,
            LocalModuleMsiProvisioningContext context)
        {
            context.WriteStage(request, manifest.OwnershipNonce,
                LocalModuleMsiLifecycleStage.Ready, null, false);
            context.Journals.Delete(request.Inn,
                context.OperationId, manifest.OwnershipNonce);
        }

        internal static LocalModuleMsiProvisioningItemResult Result(
            LocalModuleMsiProvisioningItemRequest request,
            LmServiceProvisioningStatus status,
            string message,
            string manifestSha256)
        {
            return new LocalModuleMsiProvisioningItemResult
            {
                Inn = request == null ? string.Empty : request.Inn,
                CloneOrdinal = request == null ? 0 : request.CloneOrdinal,
                ApiPort = request == null ? 0 : request.ApiPort,
                Status = status,
                Message = message ?? string.Empty,
                ManifestSha256 = manifestSha256 ?? string.Empty
            };
        }

        private static string SafeMessage(Exception exception)
        {
            if (exception == null)
                return "Операция с локальным модулем завершилась ошибкой.";
            System.Collections.Generic.List<string> details =
                new System.Collections.Generic.List<string>();
            Exception current = exception;
            for (int depth = 0; current != null && depth < 8; depth++)
            {
                string message = string.IsNullOrWhiteSpace(current.Message)
                    ? current.GetType().Name
                    : current.GetType().Name + ": " + current.Message;
                System.ComponentModel.Win32Exception win32 =
                    current as System.ComponentModel.Win32Exception;
                if (win32 != null)
                    message += " (Win32 " +
                        win32.NativeErrorCode.ToString() + ")";
                details.Add(message);
                current = current.InnerException;
            }
            return string.Join(" -> ", details.ToArray());
        }

        private static string ExpectedInstallRoot(
            LocalModuleMsiProvisioningItemRequest request)
        {
            return request.CloneOrdinal == 0
                ? Path.Combine(request.InstallVolumeRoot,
                    "Program Files", "Regime")
                : LocalModuleInstallRootPolicy.BuildCloneInstallDirectory(
                    request.InstallVolumeRoot,
                    request.CloneOrdinal);
        }
    }
}
