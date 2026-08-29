using System;
using System.IO;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal enum LmRemovalOwnershipState
    {
        FullyOwnedRunning = 1,
        FullyOwnedStopped = 2,
        CleanupOnly = 3,
        OfficialBase = 4,
        MarkerMismatch = 5,
        ImageMismatch = 6,
        ConfirmationMismatch = 7,
        Missing = 8,
        RequiresAttention = 9
    }

    internal enum LmScmDeletionState
    {
        Absent = 1,
        MarkedForDelete = 2,
        AccessDenied = 3,
        TimedOut = 4
    }

    internal interface ILmServiceRemovalPlatform
    {
        IDisposable AcquireMachineLock();
        IDisposable AcquireItemLock(string kktSerial);
        void ReconcileRemoval(string kktSerial, string operationId);
        LmRemovalOwnershipState InspectRemoval(
            string kktSerial,
            string manifestFingerprint,
            bool cleanupOnly);
        void WriteRemovalJournal(
            string kktSerial,
            string operationId,
            LmServiceOperation operation,
            ManagedServiceLifecycleState state,
            string manifestFingerprint);
        void RequestNormalStop(string kktSerial);
        void WaitUntilStopped(string kktSerial);
        LmScmDeletionState DeleteServiceAndConfirmAbsent(string kktSerial);
        void MarkCleanupPending(string kktSerial, string operationId, string errorClass);
        void CleanupProfile(string kktSerial);
        void CleanupManifest(string kktSerial);
        void CompleteRemoval(string kktSerial);
    }

    internal sealed class LmServiceRemovalWorkflow
    {
        internal const string BindingRetainedMessage =
            "Служба и локальные данные удалены. Настройка связи в ЕСМ не очищена " +
            "и может по-прежнему ссылаться на этот порт.";

        private readonly ILmServiceRemovalPlatform _platform;

        internal LmServiceRemovalWorkflow(ILmServiceRemovalPlatform platform)
        {
            if (platform == null)
            {
                throw new ArgumentNullException("platform");
            }
            _platform = platform;
        }

        internal LmServiceProvisioningItemResult RemoveManaged(
            LmServiceProvisioningBatchRequest request)
        {
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid || request.Operation != LmServiceOperation.RemoveManaged)
            {
                return Failure(
                    request == null || request.RemovalConfirmation == null
                        ? string.Empty
                        : request.RemovalConfirmation.KktSerial,
                    LmServiceProvisioningStatus.RemovalBlocked,
                    validation.IsValid
                        ? "Запрошена неверная операция удаления."
                        : validation.JoinMessages());
            }

            LmRemovalConfirmation confirmation = request.RemovalConfirmation;
            if (confirmation.ManifestFingerprint == null)
            {
                return Failure(
                    confirmation.KktSerial,
                    LmServiceProvisioningStatus.RemovalBlocked,
                    "Показанный манифест контроллера больше недоступен; обновите список.");
            }
            return Execute(
                confirmation.KktSerial,
                confirmation.ManifestFingerprint.Sha256,
                request.OperationId,
                false);
        }

        internal LmServiceProvisioningItemResult CleanupManaged(
            LmServiceProvisioningBatchRequest request)
        {
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid || request.Operation != LmServiceOperation.CleanupManaged)
            {
                return Failure(
                    request == null || request.CleanupConfirmation == null
                        ? string.Empty
                        : request.CleanupConfirmation.KktSerial,
                    LmServiceProvisioningStatus.RemovalBlocked,
                    validation.IsValid
                        ? "Запрошена неверная операция очистки."
                        : validation.JoinMessages());
            }

            LmCleanupConfirmation confirmation = request.CleanupConfirmation;
            if (confirmation.ManifestFingerprint == null)
            {
                return Failure(
                    confirmation.KktSerial,
                    LmServiceProvisioningStatus.RemovalBlocked,
                    "Показанный манифест контроллера больше недоступен; обновите список.");
            }
            return Execute(
                confirmation.KktSerial,
                confirmation.ManifestFingerprint.Sha256,
                request.OperationId,
                true);
        }

        private LmServiceProvisioningItemResult Execute(
            string kktSerial,
            string manifestFingerprint,
            string operationId,
            bool cleanupOnly)
        {
            try
            {
                using (_platform.AcquireMachineLock())
                using (_platform.AcquireItemLock(kktSerial))
                {
                    _platform.ReconcileRemoval(kktSerial, operationId);
                    LmRemovalOwnershipState ownership = _platform.InspectRemoval(
                        kktSerial,
                        manifestFingerprint,
                        cleanupOnly);
                    if (cleanupOnly)
                    {
                        if (ownership != LmRemovalOwnershipState.CleanupOnly)
                        {
                            return Blocked(kktSerial, ownership);
                        }
                        _platform.WriteRemovalJournal(
                            kktSerial,
                            operationId,
                            LmServiceOperation.CleanupManaged,
                            ManagedServiceLifecycleState.Cleaning,
                            manifestFingerprint);
                        return Cleanup(kktSerial, operationId);
                    }

                    if (ownership == LmRemovalOwnershipState.CleanupOnly)
                    {
                        _platform.WriteRemovalJournal(
                            kktSerial,
                            operationId,
                            LmServiceOperation.RemoveManaged,
                            ManagedServiceLifecycleState.Cleaning,
                            manifestFingerprint);
                        return Cleanup(kktSerial, operationId);
                    }
                    if (ownership != LmRemovalOwnershipState.FullyOwnedRunning &&
                        ownership != LmRemovalOwnershipState.FullyOwnedStopped)
                    {
                        return Blocked(kktSerial, ownership);
                    }

                    _platform.WriteRemovalJournal(
                        kktSerial,
                        operationId,
                        LmServiceOperation.RemoveManaged,
                        ManagedServiceLifecycleState.Deleting,
                        manifestFingerprint);
                    if (ownership == LmRemovalOwnershipState.FullyOwnedRunning)
                    {
                        _platform.RequestNormalStop(kktSerial);
                    }
                    _platform.WaitUntilStopped(kktSerial);
                    LmScmDeletionState deletion =
                        _platform.DeleteServiceAndConfirmAbsent(kktSerial);
                    if (deletion == LmScmDeletionState.MarkedForDelete)
                    {
                        return Failure(
                            kktSerial,
                            LmServiceProvisioningStatus.MarkedForDelete,
                            "SCM пометил службу на удаление, но ее отсутствие еще не подтверждено.");
                    }
                    if (deletion != LmScmDeletionState.Absent)
                    {
                        return Failure(
                            kktSerial,
                            LmServiceProvisioningStatus.RequiresAttention,
                            deletion == LmScmDeletionState.AccessDenied
                                ? "SCM отказал в проверке удаления службы."
                                : "Истекло время подтверждения удаления службы из SCM.");
                    }

                    _platform.WriteRemovalJournal(
                        kktSerial,
                        operationId,
                        LmServiceOperation.RemoveManaged,
                        ManagedServiceLifecycleState.Cleaning,
                        manifestFingerprint);
                    return Cleanup(kktSerial, operationId);
                }
            }
            catch (Exception ex)
            {
                return Failure(
                    kktSerial,
                    LmServiceProvisioningStatus.RequiresAttention,
                    ex is InvalidDataException || ex is InvalidOperationException ||
                    ex is UnauthorizedAccessException || ex is System.IO.IOException
                        ? ex.Message
                        : "Удаление завершилось ошибкой: " + ex.GetType().Name + ".");
            }
        }

        private LmServiceProvisioningItemResult Cleanup(
            string kktSerial,
            string operationId)
        {
            _platform.MarkCleanupPending(kktSerial, operationId, string.Empty);
            try
            {
                _platform.CleanupProfile(kktSerial);
                _platform.CleanupManifest(kktSerial);
                _platform.CompleteRemoval(kktSerial);
                return Failure(
                    kktSerial,
                    LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                    BindingRetainedMessage);
            }
            catch (Exception ex)
            {
                try
                {
                    _platform.MarkCleanupPending(
                        kktSerial,
                        operationId,
                        ex.GetType().Name);
                }
                catch
                {
                }
                return Failure(
                    kktSerial,
                    LmServiceProvisioningStatus.CleanupPending,
                    "Локальная очистка не завершена. Используйте «Повторить очистку». " +
                        ex.GetType().Name + ".");
            }
        }

        private static LmServiceProvisioningItemResult Blocked(
            string kktSerial,
            LmRemovalOwnershipState state)
        {
            string reason;
            if (state == LmRemovalOwnershipState.ConfirmationMismatch)
            {
                reason = "Состояние изменилось после подтверждения; обновите список и подтвердите заново.";
            }
            else if (state == LmRemovalOwnershipState.OfficialBase)
            {
                reason = "Штатная служба контроллера ЛМ не управляется этим приложением.";
            }
            else if (state == LmRemovalOwnershipState.MarkerMismatch ||
                state == LmRemovalOwnershipState.ImageMismatch)
            {
                reason = "Не совпали признаки владения службой; изменение SCM запрещено.";
            }
            else
            {
                reason = "Управляемая служба или подтвержденные локальные данные не найдены.";
            }
            return Failure(kktSerial, LmServiceProvisioningStatus.RemovalBlocked, reason);
        }

        private static LmServiceProvisioningItemResult Failure(
            string kktSerial,
            LmServiceProvisioningStatus status,
            string message)
        {
            return new LmServiceProvisioningItemResult
            {
                KktSerial = kktSerial ?? string.Empty,
                Status = status,
                Message = message ?? string.Empty
            };
        }
    }
}
