using System;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IDirectControllerPlatform
    {
        LmServiceProvisioningItemResult Ensure(
            DirectControllerProvisioningItemRequest item,
            string operationId,
            string initiatingSid);
        LmServiceProvisioningItemResult Restart(
            DirectControllerProvisioningItemRequest item,
            string operationId,
            string initiatingSid);
        LmServiceProvisioningItemResult Remove(
            DirectControllerProvisioningItemRequest item,
            string operationId,
            string initiatingSid);
    }

    internal interface IDirectControllerProvisioner
    {
        LmServiceProvisioningBatchResult Execute(
            LmServiceProvisioningBatchRequest request,
            ILmProvisioningCancellation cancellation);
    }

    internal sealed class DirectControllerProvisioner : IDirectControllerProvisioner
    {
        private readonly IDirectControllerPlatform _platform;

        internal DirectControllerProvisioner(IDirectControllerPlatform platform)
        {
            if (platform == null) throw new ArgumentNullException("platform");
            _platform = platform;
        }

        public LmServiceProvisioningBatchResult Execute(
            LmServiceProvisioningBatchRequest request,
            ILmProvisioningCancellation cancellation)
        {
            LmServiceProvisioningBatchResult result = new LmServiceProvisioningBatchResult
            {
                SchemaVersion = request == null ? 2 : request.SchemaVersion,
                OperationId = request == null ? string.Empty : request.OperationId,
                PlanHash = request == null ? string.Empty : request.PlanHash,
                Status = LmServiceProvisioningStatus.Pending
            };
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid || !IsSupportedOperation(request.Operation))
            {
                result.Status = LmServiceProvisioningStatus.Failed;
                AddValidationFailures(result, request, validation);
                return result;
            }

            ILmProvisioningCancellation effectiveCancellation =
                cancellation ?? NeverCancelLmProvisioning.Instance;
            for (int index = 0; index < request.DirectControllers.Count; index++)
            {
                DirectControllerProvisioningItemRequest item =
                    request.DirectControllers[index];
                if (effectiveCancellation.IsCancellationRequested)
                {
                    for (int remaining = index;
                        remaining < request.DirectControllers.Count;
                        remaining++)
                    {
                        result.Items.Add(CreateResult(
                            request.DirectControllers[remaining],
                            LmServiceProvisioningStatus.Cancelled,
                            "Операция отменена до начала обработки этого контроллера."));
                    }
                    break;
                }

                try
                {
                    result.Items.Add(ExecuteOne(request, item));
                }
                catch (Exception ex)
                {
                    result.Items.Add(CreateResult(
                        item,
                        LmServiceProvisioningStatus.Failed,
                        ex.Message + " (" + ex.GetType().Name + ")"));
                }
            }
            result.Status = Aggregate(result);
            return result;
        }

        private LmServiceProvisioningItemResult ExecuteOne(
            LmServiceProvisioningBatchRequest request,
            DirectControllerProvisioningItemRequest item)
        {
            if (request.Operation == LmServiceOperation.EnsureDirectControllers)
            {
                return _platform.Ensure(
                    item,
                    request.OperationId,
                    request.InitiatingSid);
            }
            if (request.Operation == LmServiceOperation.RestartDirectController)
            {
                return _platform.Restart(
                    item,
                    request.OperationId,
                    request.InitiatingSid);
            }
            return _platform.Remove(
                item,
                request.OperationId,
                request.InitiatingSid);
        }

        private static bool IsSupportedOperation(LmServiceOperation operation)
        {
            return operation == LmServiceOperation.EnsureDirectControllers ||
                operation == LmServiceOperation.RestartDirectController ||
                operation == LmServiceOperation.RemoveDirectController ||
                operation == LmServiceOperation.RemoveAllDirectControllers;
        }

        private static void AddValidationFailures(
            LmServiceProvisioningBatchResult result,
            LmServiceProvisioningBatchRequest request,
            ValidationResult validation)
        {
            if (request == null || request.DirectControllers == null ||
                request.DirectControllers.Count == 0)
            {
                result.Items.Add(new LmServiceProvisioningItemResult
                {
                    KktSerial = string.Empty,
                    Status = LmServiceProvisioningStatus.Failed,
                    Message = validation.JoinMessages()
                });
                return;
            }
            for (int index = 0; index < request.DirectControllers.Count; index++)
            {
                result.Items.Add(CreateResult(
                    request.DirectControllers[index],
                    LmServiceProvisioningStatus.Failed,
                    validation.JoinMessages()));
            }
        }

        private static LmServiceProvisioningItemResult CreateResult(
            DirectControllerProvisioningItemRequest item,
            LmServiceProvisioningStatus status,
            string message)
        {
            return new LmServiceProvisioningItemResult
            {
                KktSerial = item == null ? string.Empty : item.KktSerial,
                Status = status,
                Message = message
            };
        }

        private static LmServiceProvisioningStatus Aggregate(
            LmServiceProvisioningBatchResult result)
        {
            bool cancelled = false;
            for (int index = 0; index < result.Items.Count; index++)
            {
                if (result.Items[index].Status == LmServiceProvisioningStatus.Failed ||
                    result.Items[index].Status ==
                        LmServiceProvisioningStatus.UnsupportedController)
                {
                    return LmServiceProvisioningStatus.Failed;
                }
                cancelled |= result.Items[index].Status ==
                    LmServiceProvisioningStatus.Cancelled;
            }
            return cancelled
                ? LmServiceProvisioningStatus.Cancelled
                : LmServiceProvisioningStatus.Succeeded;
        }
    }
}
