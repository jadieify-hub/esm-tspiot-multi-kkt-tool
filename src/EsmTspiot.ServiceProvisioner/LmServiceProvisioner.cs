using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal enum LmProvisioningJournalStage
    {
        Preparing = 1,
        Updating = 2,
        ProfileReady = 3,
        ServiceReady = 4,
        Started = 5,
        Failed = 6,
        Deleting = 7,
        Cleaning = 8
    }

    internal sealed class LmReadinessResult
    {
        private LmReadinessResult(bool isReady, string message)
        {
            IsReady = isReady;
            Message = message ?? string.Empty;
        }

        internal bool IsReady { get; private set; }
        internal string Message { get; private set; }

        internal static LmReadinessResult Ready()
        {
            return new LmReadinessResult(true, string.Empty);
        }

        internal static LmReadinessResult Failed(string message)
        {
            return new LmReadinessResult(false, message);
        }
    }

    internal interface ILmProvisioningCancellation
    {
        bool IsCancellationRequested { get; }
    }

    internal sealed class NeverCancelLmProvisioning : ILmProvisioningCancellation
    {
        internal static readonly NeverCancelLmProvisioning Instance =
            new NeverCancelLmProvisioning();

        private NeverCancelLmProvisioning()
        {
        }

        public bool IsCancellationRequested
        {
            get { return false; }
        }
    }

    internal sealed class LmServiceProvisioner
    {
        private readonly ILmServiceRemovalPlatform _removalPlatform;

        internal LmServiceProvisioner(ILmServiceRemovalPlatform removalPlatform)
        {
            if (removalPlatform == null)
            {
                throw new ArgumentNullException("removalPlatform");
            }
            _removalPlatform = removalPlatform;
        }

        internal LmServiceProvisioningItemResult RemoveManaged(
            LmServiceProvisioningBatchRequest request)
        {
            if (_removalPlatform == null)
            {
                throw new NotSupportedException("Removal platform is unavailable.");
            }
            return new LmServiceRemovalWorkflow(_removalPlatform).RemoveManaged(request);
        }

        internal LmServiceProvisioningItemResult CleanupManaged(
            LmServiceProvisioningBatchRequest request)
        {
            if (_removalPlatform == null)
            {
                throw new NotSupportedException("Cleanup platform is unavailable.");
            }
            return new LmServiceRemovalWorkflow(_removalPlatform).CleanupManaged(request);
        }

        internal LmServiceProvisioningBatchResult RemoveAllManaged(
            LmServiceProvisioningBatchRequest request,
            ILmProvisioningCancellation cancellation)
        {
            if (_removalPlatform == null)
            {
                throw new NotSupportedException("Removal platform is unavailable.");
            }

            LmServiceProvisioningBatchResult result = CreateBatchResult(request);
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid || request.Operation != LmServiceOperation.RemoveAllManaged)
            {
                result.Status = LmServiceProvisioningStatus.Failed;
                AddRemovalValidationFailures(result, request, validation);
                return result;
            }

            ILmProvisioningCancellation effectiveCancellation =
                cancellation ?? NeverCancelLmProvisioning.Instance;
            using (_removalPlatform.AcquireMachineLock())
            {
                for (int index = 0; index < request.RemovalConfirmations.Count; index++)
                {
                    LmRemovalConfirmation confirmation = request.RemovalConfirmations[index];
                    if (effectiveCancellation.IsCancellationRequested)
                    {
                        for (int remaining = index;
                            remaining < request.RemovalConfirmations.Count;
                            remaining++)
                        {
                            result.Items.Add(CreateRemovalResult(
                                request.RemovalConfirmations[remaining],
                                LmServiceProvisioningStatus.Cancelled,
                                "Операция отменена до начала удаления этой службы."));
                        }
                        break;
                    }

                    LmServiceProvisioningBatchRequest single =
                        new LmServiceProvisioningBatchRequest
                        {
                            SchemaVersion = ProvisioningRequestValidator.LegacySchemaVersion,
                            Operation = LmServiceOperation.RemoveManaged,
                            OperationId = request.OperationId,
                            InitiatingSid = request.InitiatingSid,
                            RemovalConfirmation = confirmation
                        };
                    single.PlanHash = CanonicalLmPlanHasher.Compute(single);
                    result.Items.Add(
                        new LmServiceRemovalWorkflow(_removalPlatform).RemoveManaged(single));
                }
            }

            result.Status = AggregateRemovalStatus(result.Items);
            return result;
        }

        private static LmServiceProvisioningBatchResult CreateBatchResult(
            LmServiceProvisioningBatchRequest request)
        {
            return new LmServiceProvisioningBatchResult
            {
                SchemaVersion = request == null
                    ? ProvisioningRequestValidator.LegacySchemaVersion
                    : request.SchemaVersion,
                OperationId = request == null ? string.Empty : request.OperationId,
                PlanHash = request == null ? string.Empty : request.PlanHash,
                Status = LmServiceProvisioningStatus.Pending
            };
        }

        private static void AddRemovalValidationFailures(
            LmServiceProvisioningBatchResult result,
            LmServiceProvisioningBatchRequest request,
            ValidationResult validation)
        {
            string message = validation.IsValid
                ? "Запрошена неверная операция помощника."
                : validation.JoinMessages();
            if (request == null || request.RemovalConfirmations == null)
            {
                return;
            }
            for (int index = 0; index < request.RemovalConfirmations.Count; index++)
            {
                result.Items.Add(CreateRemovalResult(
                    request.RemovalConfirmations[index],
                    LmServiceProvisioningStatus.Failed,
                    message));
            }
        }

        private static LmServiceProvisioningItemResult CreateRemovalResult(
            LmRemovalConfirmation confirmation,
            LmServiceProvisioningStatus status,
            string message)
        {
            return new LmServiceProvisioningItemResult
            {
                KktSerial = confirmation == null ? string.Empty : confirmation.KktSerial,
                Status = status,
                Message = message ?? string.Empty
            };
        }

        private static LmServiceProvisioningStatus AggregateRemovalStatus(
            IList<LmServiceProvisioningItemResult> items)
        {
            if (items == null || items.Count == 0)
            {
                return LmServiceProvisioningStatus.Failed;
            }

            LmServiceProvisioningStatus status =
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained;
            for (int index = 0; index < items.Count; index++)
            {
                LmServiceProvisioningStatus itemStatus = items[index].Status;
                if (itemStatus == LmServiceProvisioningStatus.Failed ||
                    itemStatus == LmServiceProvisioningStatus.RemovalBlocked)
                {
                    return itemStatus;
                }
                if (itemStatus == LmServiceProvisioningStatus.CleanupPending ||
                    itemStatus == LmServiceProvisioningStatus.MarkedForDelete ||
                    itemStatus == LmServiceProvisioningStatus.RequiresAttention)
                {
                    status = itemStatus;
                }
                else if (itemStatus == LmServiceProvisioningStatus.Cancelled &&
                    status == LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained)
                {
                    status = LmServiceProvisioningStatus.Cancelled;
                }
            }
            return status;
        }

    }
}
