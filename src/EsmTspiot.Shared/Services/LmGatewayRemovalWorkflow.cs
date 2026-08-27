using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class LmGatewayRemovalWorkflow
    {
        public const string BindingRetainedMessage =
            "Служба и локальные данные удалены. Настройка связи в ЕСМ не очищена и может по-прежнему ссылаться на этот порт.";

        private readonly ILmServiceProvisioner _provisioner;
        private readonly Func<IList<LmServiceInventoryItem>> _inventoryProvider;

        public LmGatewayRemovalWorkflow(
            ILmServiceProvisioner provisioner,
            Func<IList<LmServiceInventoryItem>> inventoryProvider)
        {
            if (provisioner == null)
            {
                throw new ArgumentNullException("provisioner");
            }
            if (inventoryProvider == null)
            {
                throw new ArgumentNullException("inventoryProvider");
            }
            _provisioner = provisioner;
            _inventoryProvider = inventoryProvider;
        }

        public async Task<LmGatewayLifecycleResult> RemoveAsync(
            IList<LmServiceInventoryItem> selected,
            LmRemovalConfirmation confirmation,
            string operationId,
            string planHash,
            CancellationToken cancellation)
        {
            LmServiceInventoryItem item;
            string blocked;
            if (!TryValidateSelection(selected, out item, out blocked))
            {
                return Blocked(item, blocked);
            }
            if (!MatchesConfirmation(item, confirmation))
            {
                return Blocked(item, "Показанные данные изменились; обновите список и подтвердите удаление заново.");
            }
            ValidateRemovalHash(confirmation, operationId, planHash);
            if (!InventoryStillMatches(item))
            {
                return Blocked(item, "Инвентарь изменился; требуется обновление и повторное подтверждение.");
            }

            LmServiceProvisioningItemResult helper = await _provisioner.RemoveAsync(
                confirmation,
                operationId,
                planHash,
                cancellation).ConfigureAwait(false);
            return Reconcile(item, helper);
        }

        public async Task<LmGatewayLifecycleResult> CleanupAsync(
            IList<LmServiceInventoryItem> selected,
            LmCleanupConfirmation confirmation,
            string operationId,
            string planHash,
            CancellationToken cancellation)
        {
            LmServiceInventoryItem item;
            string blocked;
            if (!TryValidateSelection(selected, out item, out blocked))
            {
                return Blocked(item, blocked);
            }
            if (confirmation == null ||
                confirmation.DisplayedState != LmServiceProvisioningStatus.CleanupPending ||
                !string.Equals(confirmation.KktSerial, item.KktSerial, StringComparison.Ordinal) ||
                !SameFingerprint(confirmation.ManifestFingerprint, item.ManifestFingerprint))
            {
                return Blocked(item, "Состояние очистки изменилось; обновите список.");
            }
            ValidateCleanupHash(confirmation, operationId, planHash);
            if (!InventoryStillMatches(item))
            {
                return Blocked(item, "Инвентарь изменился; требуется обновление.");
            }
            LmServiceProvisioningItemResult helper = await _provisioner.CleanupAsync(
                confirmation,
                operationId,
                planHash,
                cancellation).ConfigureAwait(false);
            return Reconcile(item, helper);
        }

        private LmGatewayLifecycleResult Reconcile(
            LmServiceInventoryItem item,
            LmServiceProvisioningItemResult helper)
        {
            if (helper == null || !string.Equals(
                helper.KktSerial,
                item.KktSerial,
                StringComparison.Ordinal))
            {
                return Result(
                    item,
                    LmGatewayLifecycleStatus.RequiresAttention,
                    LmServiceProvisioningStatus.RequiresAttention,
                    "Helper не вернул однозначный результат; список нужно обновить.");
            }
            IList<LmServiceInventoryItem> inventory = _inventoryProvider() ??
                new List<LmServiceInventoryItem>();
            LmServiceInventoryItem remaining = Find(inventory, item.KktSerial);
            if (helper.Status == LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained &&
                remaining == null)
            {
                return Result(
                    item,
                    LmGatewayLifecycleStatus.RemovedLocalArtifactsBindingRetained,
                    helper.Status,
                    BindingRetainedMessage);
            }
            if (helper.Status == LmServiceProvisioningStatus.CleanupPending ||
                helper.Status == LmServiceProvisioningStatus.MarkedForDelete ||
                (remaining != null && remaining.Status == LmServiceProvisioningStatus.CleanupPending))
            {
                return Result(
                    item,
                    LmGatewayLifecycleStatus.CleanupPending,
                    LmServiceProvisioningStatus.CleanupPending,
                    "Удаление службы завершено не полностью. Используйте «Повторить очистку». ");
            }
            return Result(
                item,
                helper.Status == LmServiceProvisioningStatus.Cancelled
                    ? LmGatewayLifecycleStatus.Cancelled
                    : LmGatewayLifecycleStatus.RequiresAttention,
                helper.Status,
                helper.Message);
        }

        private bool InventoryStillMatches(LmServiceInventoryItem selected)
        {
            IList<LmServiceInventoryItem> current = _inventoryProvider();
            LmServiceInventoryItem observed = Find(current, selected.KktSerial);
            return observed != null && observed.Role == LmServiceRole.Managed &&
                string.Equals(observed.ServiceName, selected.ServiceName, StringComparison.Ordinal) &&
                SameFingerprint(observed.ManifestFingerprint, selected.ManifestFingerprint);
        }

        private static bool TryValidateSelection(
            IList<LmServiceInventoryItem> selected,
            out LmServiceInventoryItem item,
            out string message)
        {
            item = selected == null || selected.Count == 0 ? null : selected[0];
            if (selected == null || selected.Count != 1)
            {
                message = "Удаление разрешено только для одной выбранной управляемой службы.";
                return false;
            }
            if (item == null || item.Role != LmServiceRole.Managed ||
                item.ManifestFingerprint == null || item.Ports == null)
            {
                message = "Официальную, неизвестную или неподтвержденную службу удалять нельзя.";
                return false;
            }
            message = string.Empty;
            return true;
        }

        private static bool MatchesConfirmation(
            LmServiceInventoryItem item,
            LmRemovalConfirmation confirmation)
        {
            return confirmation != null && confirmation.RetainedEsmWarningAccepted &&
                string.Equals(confirmation.KktSerial, item.KktSerial, StringComparison.Ordinal) &&
                confirmation.GrpcPort == item.Ports.GrpcPort &&
                confirmation.RestPort == item.Ports.RestPort &&
                SameFingerprint(confirmation.ManifestFingerprint, item.ManifestFingerprint);
        }

        private static void ValidateRemovalHash(
            LmRemovalConfirmation confirmation,
            string operationId,
            string planHash)
        {
            LmServiceProvisioningBatchRequest request = new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 1,
                Operation = LmServiceOperation.RemoveManaged,
                OperationId = operationId,
                PlanHash = planHash,
                RemovalConfirmation = confirmation
            };
            ValidateHash(request, planHash);
        }

        private static void ValidateCleanupHash(
            LmCleanupConfirmation confirmation,
            string operationId,
            string planHash)
        {
            LmServiceProvisioningBatchRequest request = new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 1,
                Operation = LmServiceOperation.CleanupManaged,
                OperationId = operationId,
                PlanHash = planHash,
                CleanupConfirmation = confirmation
            };
            ValidateHash(request, planHash);
        }

        private static void ValidateHash(
            LmServiceProvisioningBatchRequest request,
            string planHash)
        {
            if (!CanonicalLmPlanHasher.FixedTimeEqualsHex(
                planHash,
                CanonicalLmPlanHasher.Compute(request)))
            {
                throw new InvalidDataException("Подтверждение изменилось до запуска операции.");
            }
        }

        private static LmServiceInventoryItem Find(
            IList<LmServiceInventoryItem> items,
            string serial)
        {
            if (items == null)
            {
                return null;
            }
            for (int index = 0; index < items.Count; index++)
            {
                if (items[index] != null && string.Equals(
                    items[index].KktSerial,
                    serial,
                    StringComparison.Ordinal))
                {
                    return items[index];
                }
            }
            return null;
        }

        private static bool SameFingerprint(
            LmManifestFingerprint left,
            LmManifestFingerprint right)
        {
            return left != null && right != null &&
                CanonicalLmPlanHasher.FixedTimeEqualsHex(left.Sha256, right.Sha256);
        }

        private static LmGatewayLifecycleResult Blocked(
            LmServiceInventoryItem item,
            string message)
        {
            return Result(
                item,
                LmGatewayLifecycleStatus.Blocked,
                LmServiceProvisioningStatus.RemovalBlocked,
                message);
        }

        private static LmGatewayLifecycleResult Result(
            LmServiceInventoryItem item,
            LmGatewayLifecycleStatus status,
            LmServiceProvisioningStatus serviceStatus,
            string message)
        {
            return new LmGatewayLifecycleResult
            {
                KktSerial = item == null ? string.Empty : item.KktSerial,
                ServiceName = item == null ? string.Empty : item.ServiceName,
                Status = status,
                ServiceStatus = serviceStatus,
                Details = SensitiveDataMasker.Mask(message)
            };
        }
    }
}
