using System;
using System.Collections.Generic;
using System.IO;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiRemovalWorkflow
    {
        internal LocalModuleMsiProvisioningItemResult Remove(
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
                        LocalModuleMsiProvisioner.RequireCurrentManifest(
                            request, context);
                    if (manifest.CloneOrdinal == 0 && manifest.CanRemove)
                        return RemoveOwnedBaseWithCompensation(
                            request, manifest, context);
                    return RemoveOne(request, manifest, context);
                }
            }
            catch (Exception exception)
            {
                LmServiceProvisioningStatus status =
                    LmServiceProvisioningStatus.RemovalBlocked;
                try
                {
                    LocalModuleMsiLifecycleJournal pending =
                        context.Journals.Read(request == null
                            ? string.Empty
                            : request.Inn);
                    if (pending != null && pending.Stage ==
                            LocalModuleMsiLifecycleStage.CleanupPending)
                        status = LmServiceProvisioningStatus.CleanupPending;
                }
                catch
                {
                    status = LmServiceProvisioningStatus.RemovalBlocked;
                }
                return LocalModuleMsiProvisioner.Result(
                    request,
                    status,
                    exception.Message,
                    string.Empty);
            }
        }

        internal IList<LocalModuleMsiProvisioningItemResult> RemoveAll(
            LocalModuleMsiProvisioningContext context)
        {
            if (context == null) throw new ArgumentNullException("context");
            List<LocalModuleMsiProvisioningItemResult> results =
                new List<LocalModuleMsiProvisioningItemResult>();
            using (context.AcquireMutationLock())
            {
                List<LocalModuleMsiManifest> manifests =
                    new List<LocalModuleMsiManifest>(context.Manifests.ReadAll());
                manifests.Sort(delegate(
                    LocalModuleMsiManifest left,
                    LocalModuleMsiManifest right)
                {
                    return right.CloneOrdinal.CompareTo(left.CloneOrdinal);
                });
                for (int index = 0; index < manifests.Count; index++)
                {
                    LocalModuleMsiManifest manifest = manifests[index];
                    LocalModuleMsiProvisioningItemRequest request =
                        RequestFor(manifest);
                    request.ExpectedManifestSha256 = manifest.ManifestSha256;
                    try
                    {
                        results.Add(RemoveOne(request, manifest, context));
                    }
                    catch (Exception exception)
                    {
                        results.Add(LocalModuleMsiProvisioner.Result(
                            request,
                            LmServiceProvisioningStatus.CleanupPending,
                            exception.Message,
                            manifest.ManifestSha256));
                    }
                }
            }
            return results;
        }

        private LocalModuleMsiProvisioningItemResult
            RemoveOwnedBaseWithCompensation(
                LocalModuleMsiProvisioningItemRequest request,
                LocalModuleMsiManifest baseManifest,
            LocalModuleMsiProvisioningContext context)
        {
            List<CloneRestart> clones = new List<CloneRestart>();
            LocalModuleMsiProvisioningItemResult removed = null;
            Exception removalError = null;
            try
            {
                IList<LocalModuleMsiManifest> all =
                    context.Manifests.ReadAll();
                for (int index = 0; index < all.Count; index++)
                {
                    if (all[index].CloneOrdinal == 0) continue;
                    LocalModuleMsiProvisioningItemRequest cloneRequest =
                        RequestFor(all[index]);
                    LocalModuleMsiObservedState observed =
                        LocalModuleMsiProvisioner.RequireUnconflicted(
                            cloneRequest, all[index], context);
                    if (!observed.Running) continue;
                    context.WriteStage(
                        cloneRequest,
                        all[index].OwnershipNonce,
                        LocalModuleMsiLifecycleStage.ServicesStopping,
                        null,
                        false);
                    clones.Add(new CloneRestart
                    {
                        Request = cloneRequest,
                        Manifest = all[index]
                    });
                    context.Platform.StopAndVerify(cloneRequest, all[index]);
                }
                removed = RemoveOne(request, baseManifest, context);
                context.WriteStage(request, baseManifest.OwnershipNonce,
                    LocalModuleMsiLifecycleStage.EpmdWaiting,
                    null, false);
                context.Platform.WaitForEpmdExit();
                context.Journals.Delete(
                    request.Inn,
                    context.OperationId,
                    baseManifest.OwnershipNonce);
            }
            catch (Exception exception)
            {
                removalError = exception;
            }

            List<string> restartErrors = new List<string>();
            for (int index = 0; index < clones.Count; index++)
            {
                try
                {
                    context.WriteStage(
                        clones[index].Request,
                        clones[index].Manifest.OwnershipNonce,
                        LocalModuleMsiLifecycleStage.CloneCompensation,
                        null,
                        false);
                    context.Platform.StartAndVerify(
                        clones[index].Request,
                        clones[index].Manifest);
                    context.Journals.Delete(
                        clones[index].Request.Inn,
                        context.OperationId,
                        clones[index].Manifest.OwnershipNonce);
                }
                catch (Exception exception)
                {
                    restartErrors.Add(exception.Message);
                }
            }
            if (removalError != null)
                return LocalModuleMsiProvisioner.Result(
                    request,
                    LmServiceProvisioningStatus.CleanupPending,
                    removalError.Message,
                    baseManifest.ManifestSha256);
            if (restartErrors.Count != 0)
                return LocalModuleMsiProvisioner.Result(
                    request,
                    LmServiceProvisioningStatus.RequiresAttention,
                    "Базовый ЛМ удалён, но не все клоны перезапустились: " +
                        string.Join("; ", restartErrors.ToArray()),
                    string.Empty);
            return removed;
        }

        private LocalModuleMsiProvisioningItemResult RemoveOne(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest,
            LocalModuleMsiProvisioningContext context)
        {
            try
            {
                LocalModuleMsiObservedState observed =
                    LocalModuleMsiProvisioner.RequireUnconflicted(
                        request, manifest, context);
                if (!manifest.PreExisting && observed.Running)
                {
                    context.WriteStage(request, manifest.OwnershipNonce,
                        LocalModuleMsiLifecycleStage.ServicesStopping,
                        null, false);
                    context.Platform.StopAndVerify(request, manifest);
                }
                if (!string.IsNullOrEmpty(manifest.FirewallRuleName))
                {
                    context.WriteStage(request, manifest.OwnershipNonce,
                        LocalModuleMsiLifecycleStage.FirewallRemoving,
                        null, false);
                    context.Platform.RemoveFirewall(manifest);
                }
                if (manifest.CanRemove)
                {
                    if (observed.ProductPresent)
                    {
                        context.WriteStage(request, manifest.OwnershipNonce,
                            LocalModuleMsiLifecycleStage.ProductUninstalling,
                            null, false);
                        context.Platform.Uninstall(manifest);
                    }
                    LocalModuleMsiObservedState remaining =
                        LocalModuleMsiProvisioner.RequireUnconflicted(
                            request, manifest, context);
                    if (remaining.ProductPresent || remaining.ServicesPresent ||
                        remaining.FirewallPresent || remaining.Running)
                        throw new InvalidOperationException(
                            "После удаления MSI остались компоненты локального модуля.");
                }
                context.WriteStage(request, manifest.OwnershipNonce,
                    LocalModuleMsiLifecycleStage.ManifestDeleting,
                    null, false);
                context.Manifests.Delete(
                    manifest.Inn,
                    manifest.ManifestSha256);
                context.Journals.Delete(
                    manifest.Inn,
                    context.OperationId,
                    manifest.OwnershipNonce);
                return LocalModuleMsiProvisioner.Result(
                    request,
                    LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                    manifest.PreExisting
                        ? "Назначение удалено; предустановленный базовый ЛМ сохранён."
                        : "Созданный локальный модуль удалён; привязка ЕСМ сохранена.",
                    string.Empty);
            }
            catch (Exception exception)
            {
                context.WriteStage(request, manifest.OwnershipNonce,
                    LocalModuleMsiLifecycleStage.CleanupPending,
                    exception, false);
                throw;
            }
        }

        private static LocalModuleMsiProvisioningItemRequest RequestFor(
            LocalModuleMsiManifest manifest)
        {
            string volume = LocalModuleInstallRootPolicy.GetVolumeRoot(
                manifest.InstallRoot);
            return new LocalModuleMsiProvisioningItemRequest
            {
                Inn = manifest.Inn,
                CloneOrdinal = manifest.CloneOrdinal,
                ApiPort = manifest.ApiPort,
                DatabasePort = manifest.DatabasePort,
                InstallVolumeRoot = volume,
                RemoteAddress = "LocalSubnet",
                ExpectedManifestSha256 = manifest.ManifestSha256
            };
        }

        private sealed class CloneRestart
        {
            internal LocalModuleMsiProvisioningItemRequest Request { get; set; }
            internal LocalModuleMsiManifest Manifest { get; set; }
        }
    }
}
