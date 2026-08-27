using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class LmGatewayLifecycleWorkflow
    {
        private readonly ILmServiceProvisioner _provisioner;
        private readonly ILmGatewayProbe _probe;
        private readonly LmGatewayBindingWorkflow _binding;

        public LmGatewayLifecycleWorkflow(
            ILmServiceProvisioner provisioner,
            ILmGatewayProbe probe,
            LmGatewayBindingWorkflow binding)
        {
            if (provisioner == null)
            {
                throw new ArgumentNullException("provisioner");
            }
            if (probe == null)
            {
                throw new ArgumentNullException("probe");
            }
            if (binding == null)
            {
                throw new ArgumentNullException("binding");
            }
            _provisioner = provisioner;
            _probe = probe;
            _binding = binding;
        }

        public async Task<LmGatewayLifecycleOutcome> ExecuteAsync(
            string baseUrl,
            LmGatewayPlan plan,
            string operationId,
            string planHash,
            Func<string, LmGatewayCredentials> credentialProvider,
            Action<LmGatewayLifecycleProgress> progress,
            CancellationToken cancellation)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }
            LmGatewayLifecycleOutcome outcome = new LmGatewayLifecycleOutcome
            {
                OperationId = operationId,
                PlanHash = planHash
            };
            List<LmGatewayPlanItem> actionable = GetActionable(plan);
            Dictionary<string, LmServiceProvisioningItemResult> serviceResults =
                new Dictionary<string, LmServiceProvisioningItemResult>(StringComparer.Ordinal);
            bool unknownHelperResult = false;

            if (actionable.Count > 0)
            {
                IList<LmServiceProvisioningItemRequest> requestItems = ToRequests(actionable);
                ValidatePlanHash(requestItems, operationId, planHash);
                try
                {
                    LmServiceProvisioningBatchResult batch =
                        await _provisioner.EnsureBatchAsync(
                            requestItems,
                            operationId,
                            planHash,
                            cancellation).ConfigureAwait(false);
                    if (!TryIndexBatch(batch, actionable, operationId, planHash, serviceResults))
                    {
                        unknownHelperResult = true;
                        outcome.ReconciliationRequired = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    outcome.Cancelled = true;
                    AddCancelledPlan(plan, outcome);
                    return outcome;
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is InvalidDataException) &&
                        !(ex is UnauthorizedAccessException) &&
                        !(ex is InvalidOperationException))
                    {
                        throw;
                    }
                    unknownHelperResult = true;
                    outcome.ReconciliationRequired = true;
                }
            }

            for (int index = 0; index < plan.Items.Count; index++)
            {
                LmGatewayPlanItem item = plan.Items[index];
                if (item == null || !item.IsValid)
                {
                    LmGatewayLifecycleResult blocked = CreateResult(
                        item,
                        LmGatewayLifecycleStatus.Blocked,
                        LmServiceProvisioningStatus.Failed,
                        item == null
                            ? "Строка плана не задана."
                            : JoinValidation(item));
                    outcome.Results.Add(blocked);
                    Report(progress, index, plan.Items.Count, blocked, "Проверка плана");
                    continue;
                }
                LmServiceProvisioningStatus serviceStatus =
                    LmServiceProvisioningStatus.Succeeded;
                if (NeedsProvisioning(item))
                {
                    LmServiceProvisioningItemResult serviceResult;
                    if (!unknownHelperResult && serviceResults.TryGetValue(
                        item.Spec.KktSerial,
                        out serviceResult))
                    {
                        serviceStatus = serviceResult.Status;
                        if (serviceResult.Status != LmServiceProvisioningStatus.Succeeded)
                        {
                            if (serviceResult.Status == LmServiceProvisioningStatus.Cancelled)
                            {
                                outcome.Cancelled = true;
                            }
                            LmGatewayLifecycleResult failed = CreateResult(
                                item,
                                MapServiceFailure(serviceResult.Status),
                                serviceResult.Status,
                                serviceResult.Message);
                            outcome.Results.Add(failed);
                            Report(progress, index, plan.Items.Count, failed, "Служба");
                            continue;
                        }
                    }
                    else if (!unknownHelperResult)
                    {
                        LmGatewayLifecycleResult missing = CreateResult(
                            item,
                            LmGatewayLifecycleStatus.RequiresAttention,
                            LmServiceProvisioningStatus.RequiresAttention,
                            "Helper не вернул однозначный результат для ККТ.");
                        outcome.Results.Add(missing);
                        outcome.ReconciliationRequired = true;
                        Report(progress, index, plan.Items.Count, missing, "Сверка");
                        continue;
                    }
                }

                if (cancellation.IsCancellationRequested)
                {
                    outcome.Cancelled = true;
                    LmGatewayLifecycleResult cancelled = CreateResult(
                        item,
                        LmGatewayLifecycleStatus.Cancelled,
                        serviceStatus,
                        serviceStatus == LmServiceProvisioningStatus.Succeeded
                            ? "Служба подготовлена, но привязка к ЕСМ не начата из-за отмены."
                            : "Операция для ККТ не начата из-за отмены.");
                    outcome.Results.Add(cancelled);
                    Report(progress, index, plan.Items.Count, cancelled, "Отменено");
                    continue;
                }

                Report(progress, index, plan.Items.Count, CreateResult(
                    item,
                    LmGatewayLifecycleStatus.Pending,
                    serviceStatus,
                    "Проверка локальных listener-портов."), "Проверка готовности");
                LmGatewayProbeResult readiness = await _probe.ProbeAsync(
                    item.Spec,
                    cancellation).ConfigureAwait(false);
                if (readiness == null || !readiness.IsReady)
                {
                    LmGatewayLifecycleResult notReady = CreateResult(
                        item,
                        unknownHelperResult
                            ? LmGatewayLifecycleStatus.RequiresAttention
                            : LmGatewayLifecycleStatus.ServiceFailed,
                        LmServiceProvisioningStatus.RequiresAttention,
                        readiness == null
                            ? "Проверка готовности не вернула результат."
                            : readiness.Message);
                    outcome.Results.Add(notReady);
                    Report(progress, index, plan.Items.Count, notReady, "Служба не готова");
                    continue;
                }

                LmGatewayLifecycleResult bound = await BindOneAsync(
                    baseUrl,
                    item,
                    serviceStatus,
                    credentialProvider,
                    progress,
                    index,
                    plan.Items.Count,
                    cancellation).ConfigureAwait(false);
                outcome.Results.Add(bound);
            }
            return outcome;
        }

        public async Task<LmGatewayLifecycleResult> RetryBindingAsync(
            string baseUrl,
            LmGatewayPlanItem item,
            Func<string, LmGatewayCredentials> credentialProvider,
            Action<LmGatewayLifecycleProgress> progress,
            CancellationToken cancellation)
        {
            if (item == null || !item.IsValid)
            {
                return CreateResult(
                    item,
                    LmGatewayLifecycleStatus.Blocked,
                    LmServiceProvisioningStatus.RequiresAttention,
                    item == null ? "ККТ не выбрана." : JoinValidation(item));
            }
            LmGatewayProbeResult readiness = await _probe.ProbeAsync(
                item.Spec,
                cancellation).ConfigureAwait(false);
            if (readiness == null || !readiness.IsReady)
            {
                return CreateResult(
                    item,
                    LmGatewayLifecycleStatus.ServiceFailed,
                    LmServiceProvisioningStatus.RequiresAttention,
                    readiness == null ? "Нет результата проверки готовности." : readiness.Message);
            }
            return await BindOneAsync(
                baseUrl,
                item,
                LmServiceProvisioningStatus.Succeeded,
                credentialProvider,
                progress,
                0,
                1,
                cancellation).ConfigureAwait(false);
        }

        private async Task<LmGatewayLifecycleResult> BindOneAsync(
            string baseUrl,
            LmGatewayPlanItem item,
            LmServiceProvisioningStatus serviceStatus,
            Func<string, LmGatewayCredentials> credentialProvider,
            Action<LmGatewayLifecycleProgress> progress,
            int index,
            int total,
            CancellationToken cancellation)
        {
            LmGatewayBindingPlan bindingPlan = new LmGatewayBindingPlan();
            bindingPlan.Items.Add(new LmGatewayBindingItem
            {
                Kkt = item.Kkt,
                Input = new LmGatewayBindingInput
                {
                    KktSerial = item.Spec.KktSerial,
                    KktInn = item.Kkt == null ? string.Empty : item.Kkt.KktInn,
                    ControllerAddress = "127.0.0.1",
                    ControllerGrpcPort = item.Spec.Ports.GrpcPort.ToString(CultureInfo.InvariantCulture)
                },
                Validation = new ValidationResult()
            });
            LmGatewayBindingOutcome bindingOutcome = await _binding.ExecuteAsync(
                baseUrl,
                bindingPlan,
                credentialProvider,
                null,
                cancellation).ConfigureAwait(false);
            LmGatewayBindingResult bindingResult = bindingOutcome.Results.Count == 0
                ? null
                : bindingOutcome.Results[0];
            LmGatewayLifecycleResult result = CreateResult(
                item,
                MapBinding(bindingResult),
                serviceStatus,
                bindingResult == null
                    ? "Привязка не вернула результат."
                    : bindingResult.Details);
            result.BindingAttempted = bindingResult != null;
            result.BindingStatus = bindingResult == null
                ? LmGatewayBindingStatus.RequiresAttention
                : bindingResult.Status;
            Report(progress, index, total, result, "Привязка к ЕСМ");
            return result;
        }

        private static List<LmGatewayPlanItem> GetActionable(LmGatewayPlan plan)
        {
            List<LmGatewayPlanItem> result = new List<LmGatewayPlanItem>();
            for (int index = 0; index < plan.Items.Count; index++)
            {
                if (plan.Items[index] != null && plan.Items[index].IsValid &&
                    NeedsProvisioning(plan.Items[index]))
                {
                    result.Add(plan.Items[index]);
                }
            }
            return result;
        }

        private static bool NeedsProvisioning(LmGatewayPlanItem item)
        {
            return item.Action == LmGatewayPlanAction.CreateManagedService ||
                item.Action == LmGatewayPlanAction.UpdateManagedService ||
                item.Action == LmGatewayPlanAction.StartManagedService;
        }

        private static IList<LmServiceProvisioningItemRequest> ToRequests(
            IList<LmGatewayPlanItem> items)
        {
            List<LmServiceProvisioningItemRequest> result =
                new List<LmServiceProvisioningItemRequest>();
            for (int index = 0; index < items.Count; index++)
            {
                ManagedLmServiceSpec spec = items[index].Spec;
                result.Add(new LmServiceProvisioningItemRequest
                {
                    KktSerial = spec.KktSerial,
                    GrpcPort = spec.Ports.GrpcPort,
                    RestPort = spec.Ports.RestPort,
                    TargetAddress = spec.Target.Address,
                    TargetPort = spec.Target.Port
                });
            }
            return result;
        }

        private static void ValidatePlanHash(
            IList<LmServiceProvisioningItemRequest> items,
            string operationId,
            string planHash)
        {
            LmServiceProvisioningBatchRequest request = new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 1,
                Operation = LmServiceOperation.EnsureBatch,
                OperationId = operationId,
                PlanHash = planHash
            };
            for (int index = 0; index < items.Count; index++)
            {
                request.Items.Add(items[index]);
            }
            if (!CanonicalLmPlanHasher.FixedTimeEqualsHex(
                planHash,
                CanonicalLmPlanHasher.Compute(request)))
            {
                throw new InvalidDataException("Подтвержденный план изменился до запуска.");
            }
        }

        private static bool TryIndexBatch(
            LmServiceProvisioningBatchResult batch,
            IList<LmGatewayPlanItem> expected,
            string operationId,
            string planHash,
            IDictionary<string, LmServiceProvisioningItemResult> destination)
        {
            if (batch == null || batch.SchemaVersion != 1 ||
                !string.Equals(batch.OperationId, operationId, StringComparison.OrdinalIgnoreCase) ||
                !CanonicalLmPlanHasher.FixedTimeEqualsHex(batch.PlanHash, planHash) ||
                batch.Items == null || batch.Items.Count != expected.Count)
            {
                return false;
            }
            for (int index = 0; index < batch.Items.Count; index++)
            {
                LmServiceProvisioningItemResult item = batch.Items[index];
                if (item == null || !string.Equals(
                    item.KktSerial,
                    expected[index].Spec.KktSerial,
                    StringComparison.Ordinal) || destination.ContainsKey(item.KktSerial))
                {
                    destination.Clear();
                    return false;
                }
                destination.Add(item.KktSerial, item);
            }
            return true;
        }

        private static LmGatewayLifecycleResult CreateResult(
            LmGatewayPlanItem item,
            LmGatewayLifecycleStatus status,
            LmServiceProvisioningStatus serviceStatus,
            string details)
        {
            return new LmGatewayLifecycleResult
            {
                InstanceId = item == null || item.Kkt == null ? string.Empty : item.Kkt.InstanceId,
                KktSerial = item == null || item.Kkt == null ? string.Empty : item.Kkt.KktSerial,
                KktInn = item == null || item.Kkt == null ? string.Empty : item.Kkt.KktInn,
                ServiceName = item == null || item.Spec == null ? string.Empty : item.Spec.ServiceName,
                Status = status,
                ServiceStatus = serviceStatus,
                Details = SensitiveDataMasker.Mask(details)
            };
        }

        private static LmGatewayLifecycleStatus MapServiceFailure(
            LmServiceProvisioningStatus status)
        {
            if (status == LmServiceProvisioningStatus.Cancelled)
            {
                return LmGatewayLifecycleStatus.Cancelled;
            }
            if (status == LmServiceProvisioningStatus.RequiresAttention ||
                status == LmServiceProvisioningStatus.VersionVerificationPending ||
                status == LmServiceProvisioningStatus.CleanupPending)
            {
                return LmGatewayLifecycleStatus.RequiresAttention;
            }
            return LmGatewayLifecycleStatus.ServiceFailed;
        }

        private static LmGatewayLifecycleStatus MapBinding(LmGatewayBindingResult result)
        {
            if (result == null || result.Status == LmGatewayBindingStatus.RequiresAttention)
            {
                return LmGatewayLifecycleStatus.RequiresAttention;
            }
            if (result.Status == LmGatewayBindingStatus.BindingAccepted)
            {
                return LmGatewayLifecycleStatus.BindingAccepted;
            }
            if (result.Status == LmGatewayBindingStatus.Cancelled)
            {
                return LmGatewayLifecycleStatus.Cancelled;
            }
            return LmGatewayLifecycleStatus.BindingFailed;
        }

        private static string JoinValidation(LmGatewayPlanItem item)
        {
            string service = item.ServiceValidation.JoinMessages();
            string binding = item.BindingValidation.JoinMessages();
            return string.IsNullOrEmpty(service) ? binding :
                string.IsNullOrEmpty(binding) ? service : service + " " + binding;
        }

        private static void AddCancelledPlan(
            LmGatewayPlan plan,
            LmGatewayLifecycleOutcome outcome)
        {
            AddCancelledFrom(plan, 0, outcome, null);
        }

        private static void AddCancelledFrom(
            LmGatewayPlan plan,
            int start,
            LmGatewayLifecycleOutcome outcome,
            Action<LmGatewayLifecycleProgress> progress)
        {
            for (int index = start; index < plan.Items.Count; index++)
            {
                LmGatewayLifecycleResult result = CreateResult(
                    plan.Items[index],
                    LmGatewayLifecycleStatus.Cancelled,
                    LmServiceProvisioningStatus.Cancelled,
                    "Операция для ККТ не начата из-за отмены.");
                outcome.Results.Add(result);
                Report(progress, index, plan.Items.Count, result, "Отменено");
            }
        }

        private static void Report(
            Action<LmGatewayLifecycleProgress> progress,
            int index,
            int total,
            LmGatewayLifecycleResult result,
            string stage)
        {
            if (progress == null)
            {
                return;
            }
            progress(new LmGatewayLifecycleProgress
            {
                Current = index + 1,
                Total = total,
                KktSerial = result == null ? string.Empty : result.KktSerial,
                Status = result == null ? LmGatewayLifecycleStatus.Pending : result.Status,
                Stage = stage,
                Message = result == null ? string.Empty : result.Details
            });
        }
    }
}
