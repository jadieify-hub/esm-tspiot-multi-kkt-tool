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
            IList<LocalModuleMsiManifest> manifests =
                context.Manifests.ReadAll();
            List<LocalModuleMsiProvisioningItemRequest> requests =
                new List<LocalModuleMsiProvisioningItemRequest>();
            for (int index = 0; index < manifests.Count; index++)
            {
                LocalModuleMsiProvisioningItemRequest request =
                    RequestFor(manifests[index]);
                request.ExpectedManifestSha256 =
                    manifests[index].ManifestSha256;
                requests.Add(request);
            }
            return RemoveAll(requests, context);
        }

        internal IList<LocalModuleMsiProvisioningItemResult> RemoveAll(
            IList<LocalModuleMsiProvisioningItemRequest> requests,
            LocalModuleMsiProvisioningContext context)
        {
            if (requests == null) throw new ArgumentNullException("requests");
            if (context == null) throw new ArgumentNullException("context");
            LocalModuleMsiProvisioningItemResult[] results =
                new LocalModuleMsiProvisioningItemResult[requests.Count];
            using (context.AcquireMutationLock())
            {
                List<RemovalPlanItem> plan = new List<RemovalPlanItem>();
                for (int index = 0; index < requests.Count; index++)
                {
                    LocalModuleMsiProvisioningContext.ValidateRequest(
                        requests[index]);
                    plan.Add(new RemovalPlanItem
                    {
                        OriginalIndex = index,
                        Request = requests[index],
                        Manifest = LocalModuleMsiProvisioner
                            .RequireCurrentManifest(requests[index], context)
                    });
                }
                plan.Sort(delegate(RemovalPlanItem left, RemovalPlanItem right)
                {
                    return right.Manifest.CloneOrdinal.CompareTo(
                        left.Manifest.CloneOrdinal);
                });
                IList<RemovalPlanItem> runningBefore =
                    StopEveryPairBeforeUninstall(plan, context);
                for (int index = 0; index < plan.Count; index++)
                {
                    RemovalPlanItem item = plan[index];
                    try
                    {
                        results[item.OriginalIndex] =
                            item.Manifest.CloneOrdinal == 0 &&
                            item.Manifest.CanRemove
                                ? RemoveOwnedBaseWithCompensation(
                                    item.Request, item.Manifest, context)
                                : RemoveOne(
                                    item.Request, item.Manifest, context);
                    }
                    catch (Exception exception)
                    {
                        results[item.OriginalIndex] =
                            LocalModuleMsiProvisioner.Result(
                                item.Request,
                                LmServiceProvisioningStatus.CleanupPending,
                                exception.Message,
                                item.Manifest.ManifestSha256);
                    }
                }
                RestartSurvivors(runningBefore, results, context);
            }
            return results;
        }

        // EPMD на машине один, и вендорский пакет гасит его при удалении.
        // Пока жив узел Erlang любого другого экземпляра ЛМ, это действие
        // не завершается, а вызов установщика прервать нечем — снятие
        // комплекта встаёт целиком. Поэтому сначала останавливаем все пары
        // и только потом запускаем удаления.
        private static IList<RemovalPlanItem> StopEveryPairBeforeUninstall(
            IList<RemovalPlanItem> plan,
            LocalModuleMsiProvisioningContext context)
        {
            // Кто работал до общей остановки, запоминается здесь: после неё
            // это уже не узнать, а уцелевший комплект надо вернуть в работу.
            List<RemovalPlanItem> runningBefore = new List<RemovalPlanItem>();
            for (int index = 0; index < plan.Count; index++)
            {
                RemovalPlanItem item = plan[index];
                if (!item.Manifest.CanRemove) continue;
                LocalModuleMsiObservedState observed = TryObserve(item, context);
                // Состояние не прочиталось — пару не трогаем: менять её
                // наугад нельзя, а отказ проявится на её собственном шаге.
                if (observed == null) continue;
                if (observed.Running) runningBefore.Add(item);
                try
                {
                    context.Platform.StopAndVerify(item.Request, item.Manifest);
                }
                catch (Exception)
                {
                    // Не сумевшая остановиться пара не должна мешать
                    // остановке остальных: её отказ проявится на своём шаге.
                }
            }
            return runningBefore;
        }

        // Здесь нужно фактическое состояние служб, а не пригодность всего
        // комплекта: конфликт правила сети не делает Running неизвестным.
        // Через RequireUnconflicted такой конфликт превращался то в «не
        // работала» (пара оставалась лежать), то в «работала» (намеренно
        // остановленную пару запускали) — оба ответа выдуманы.
        private static LocalModuleMsiObservedState TryObserve(
            RemovalPlanItem item,
            LocalModuleMsiProvisioningContext context)
        {
            try
            {
                return context.Platform.Observe(item.Request, item.Manifest);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Снятие комплекта начинается с остановки всех пар. Если удаление
        /// какой-то из них не прошло, она остаётся установленной — и лежащей:
        /// касса теряет работавший ЛМ из-за чужой ошибки. Пары, пережившие
        /// снятие, возвращаются в работу.
        /// </summary>
        private static void RestartSurvivors(
            IList<RemovalPlanItem> runningBefore,
            IList<LocalModuleMsiProvisioningItemResult> results,
            LocalModuleMsiProvisioningContext context)
        {
            for (int index = 0; index < runningBefore.Count; index++)
            {
                RemovalPlanItem item = runningBefore[index];
                if (context.Manifests.Read(item.Manifest.Inn) == null) continue;
                LocalModuleMsiProvisioningItemResult result =
                    results[item.OriginalIndex];
                try
                {
                    context.WriteStage(
                        item.Request,
                        item.Manifest.OwnershipNonce,
                        LocalModuleMsiLifecycleStage.CloneCompensation,
                        null,
                        false);
                    context.Platform.StartAndVerify(
                        item.Request, item.Manifest);
                    results[item.OriginalIndex] =
                        LocalModuleMsiProvisioner.Result(
                            item.Request,
                            result.Status,
                            result.Message +
                                " Уцелевший ЛМ возвращён в работу.",
                            item.Manifest.ManifestSha256);
                }
                catch (Exception exception)
                {
                    results[item.OriginalIndex] =
                        LocalModuleMsiProvisioner.Result(
                            item.Request,
                            LmServiceProvisioningStatus.RequiresAttention,
                            result.Message +
                                " Уцелевший ЛМ остался остановленным: " +
                                exception.Message,
                            item.Manifest.ManifestSha256);
                }
            }
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
                if (manifest.StartModeAdjusted)
                {
                    context.WriteStage(request, manifest.OwnershipNonce,
                        LocalModuleMsiLifecycleStage.StartModeRestoring,
                        null, false);
                    context.Platform.RestoreStartMode(manifest);
                }
                bool directoryRemoved = true;
                if (manifest.CanRemove)
                {
                    context.WriteStage(request, manifest.OwnershipNonce,
                        LocalModuleMsiLifecycleStage.ProductUninstalling,
                        null, false);
                    directoryRemoved = context.Platform.Uninstall(manifest);
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
                string removed = manifest.PreExisting
                    ? manifest.StartModeAdjusted
                        ? "Назначение удалено; предустановленный базовый ЛМ " +
                            "сохранён, прежний режим запуска его служб возвращён."
                        : "Назначение удалено; предустановленный базовый ЛМ сохранён."
                    : "Созданный локальный модуль удалён; привязка ЕСМ сохранена.";
                if (!directoryRemoved)
                    removed += " Каталог клона был занят завершающимся " +
                        "процессом и будет удалён при ближайшей перезагрузке. " +
                        "До неё повторная установка ЛМ в этот каталог " +
                        "отклоняется: очередь удаления хранит абсолютные пути " +
                        "и снесла бы уже новые файлы.";
                return LocalModuleMsiProvisioner.Result(
                    request,
                    LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                    removed,
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

        private sealed class RemovalPlanItem
        {
            internal int OriginalIndex { get; set; }
            internal LocalModuleMsiProvisioningItemRequest Request { get; set; }
            internal LocalModuleMsiManifest Manifest { get; set; }
        }
    }
}
