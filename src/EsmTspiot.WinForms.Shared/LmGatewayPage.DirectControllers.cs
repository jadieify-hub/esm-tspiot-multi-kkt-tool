using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class DirectControllerSetupOutcome
    {
        internal DirectControllerSetupOutcome()
        {
            Messages = new List<string>();
            ReadyAssignments = new Dictionary<string, DirectControllerAssignment>(
                StringComparer.Ordinal);
        }

        internal bool Complete { get; set; }
        internal bool Cancelled { get; set; }
        internal int ReadyCount { get; set; }
        internal int WarningCount { get; set; }
        internal int FailedCount { get; set; }
        internal IList<string> Messages { get; private set; }
        internal IDictionary<string, DirectControllerAssignment>
            ReadyAssignments { get; private set; }
        internal int ExpectedKktCount { get; set; }

        internal string FormatSummary()
        {
            return "Контроллеры готовы: " + ReadyCount.ToString() +
                "; предупреждения: " + WarningCount.ToString() +
                "; ошибки: " + FailedCount.ToString() + ".";
        }
    }

    public sealed partial class LmGatewayPage
    {
        internal async Task<DirectControllerSetupOutcome>
            RunDirectControllerSetupFromHostAsync(
                IList<LmGatewayKkt> registeredKkts,
                CancellationToken cancellation)
        {
            DirectControllerSetupOutcome outcome =
                await EnsureDirectControllersFromHostAsync(
                    registeredKkts,
                    null,
                    cancellation).ConfigureAwait(true);
            await BindReadyDirectControllersAsync(
                registeredKkts,
                outcome,
                cancellation).ConfigureAwait(true);
            FinalizeDirectControllerOutcome(outcome);
            return outcome;
        }

        internal async Task<DirectControllerSetupOutcome>
            EnsureDirectControllersFromHostAsync(
                IList<LmGatewayKkt> registeredKkts,
                IDictionary<string, int> targetLmPortsByInn,
                CancellationToken cancellation)
        {
            DirectControllerSetupOutcome outcome =
                new DirectControllerSetupOutcome();
            if (registeredKkts == null || registeredKkts.Count == 0)
            {
                outcome.FailedCount = 1;
                outcome.Messages.Add(
                    "Нет зарегистрированных ККТ для настройки контроллеров.");
                return outcome;
            }
            outcome.ExpectedKktCount = registeredKkts.Count;

            _statusLabel.Text = "Подготовка плана прямых контроллеров ЛМ ЧЗ...";
            DirectControllerPlan plan =
                new DirectControllerOperatorInventoryReader().BuildPlan(
                    registeredKkts,
                    targetLmPortsByInn);
            if (!plan.IsValid)
            {
                outcome.FailedCount = plan.ValidationMessages.Count;
                for (int index = 0; index < plan.ValidationMessages.Count; index++)
                {
                    outcome.Messages.Add(plan.ValidationMessages[index]);
                    Log("Контроллеры: " +
                        SensitiveDataMasker.Mask(plan.ValidationMessages[index]) +
                        "\r\n");
                }
                return outcome;
            }

            List<DirectControllerProvisioningItemRequest> requests =
                new List<DirectControllerProvisioningItemRequest>();
            for (int index = 0; index < plan.Assignments.Count; index++)
            {
                DirectControllerAssignment assignment = plan.Assignments[index];
                requests.Add(new DirectControllerProvisioningItemRequest
                {
                    KktSerial = assignment.KktSerial,
                    Inn = assignment.KktInn,
                    Ordinal = assignment.Ordinal,
                    TargetLocalModulePort = assignment.TargetLocalModulePort
                });
                Log(
                    "ККТ " + assignment.KktSerial +
                    ": контроллер " + assignment.ServiceName +
                    ", gRPC " + assignment.GrpcPort.ToString() +
                    ", REST " + assignment.RestPort.ToString() + ".\r\n");
            }

            _statusLabel.Text =
                "Создание независимых служб контроллеров; " +
                ElevationHint() + "...";
            LmServiceProvisioningBatchResult provisioned =
                await WithHeartbeatAsync(
                    "Контроллеры",
                    _serviceProvisioner.EnsureDirectControllersAsync(
                        requests,
                        Guid.NewGuid().ToString("N"),
                        cancellation)).ConfigureAwait(true);
            for (int index = 0; index < provisioned.Items.Count; index++)
            {
                LmServiceProvisioningItemResult item = provisioned.Items[index];
                Log(item.FormatLogLine() + "\r\n");
                DirectControllerAssignment assignment =
                    plan.FindBySerial(item.KktSerial);
                if (item.Status == LmServiceProvisioningStatus.Succeeded &&
                    assignment != null)
                {
                    outcome.ReadyAssignments[item.KktSerial] = assignment;
                    outcome.ReadyCount++;
                }
                else if (item.Status == LmServiceProvisioningStatus.Cancelled)
                {
                    outcome.Cancelled = true;
                    outcome.WarningCount++;
                }
                else
                {
                    outcome.FailedCount++;
                    outcome.Messages.Add(item.FormatLogLine());
                }
            }

            return outcome;
        }

        internal async Task BindReadyDirectControllersAsync(
            IList<LmGatewayKkt> registeredKkts,
            DirectControllerSetupOutcome outcome,
            CancellationToken cancellation)
        {
            if (outcome == null) throw new ArgumentNullException("outcome");
            if (registeredKkts == null) throw new ArgumentNullException(
                "registeredKkts");
            if (outcome.ReadyAssignments.Count > 0 &&
                !cancellation.IsCancellationRequested)
            {
                _statusLabel.Text = "Привязка экземпляров ЕСМ к контроллерам...";
                LmGatewayDiscovery discovery = new LmGatewayDiscovery();
                List<LmGatewayBindingInput> inputs =
                    new List<LmGatewayBindingInput>();
                for (int index = 0; index < registeredKkts.Count; index++)
                {
                    LmGatewayKkt kkt = registeredKkts[index];
                    DirectControllerAssignment assignment;
                    if (kkt == null || !outcome.ReadyAssignments.TryGetValue(
                            kkt.KktSerial,
                            out assignment))
                    {
                        continue;
                    }
                    discovery.Items.Add(kkt);
                    inputs.Add(DirectControllerBindingInputFactory.Create(
                        kkt,
                        assignment));
                }
                LmGatewayBindingPlan bindingPlan =
                    LmGatewayBindingPlanner.Build(discovery, inputs);
                LmGatewayBindingOutcome binding =
                    await _bindingWorkflow.ExecuteAsync(
                        _baseUrlProvider(),
                        bindingPlan,
                        delegate { return LmGatewayCredentialDefaults.Create(); },
                        delegate(LmGatewayBindingProgress progress)
                        {
                            if (progress == null) return;
                            Log(
                                "ККТ " + progress.KktSerial +
                                ": " + progress.Stage + "; " +
                                SensitiveDataMasker.Mask(progress.Message) +
                                "\r\n");
                            Log(DescribeFailedExchange(progress.Response));
                        },
                        cancellation).ConfigureAwait(true);
                for (int index = 0; index < binding.Results.Count; index++)
                {
                    LmGatewayBindingResult result = binding.Results[index];
                    bool accepted = result.Status ==
                            LmGatewayBindingStatus.BindingAccepted ||
                        result.Status == LmGatewayBindingStatus.BindingVerified ||
                        result.Status == LmGatewayBindingStatus.BindingObserved;
                    bool lmNotReady =
                        DirectControllerSetupPolicy.IsDeferredLocalModuleWarning(
                            result.Details);
                    if (accepted)
                    {
                        continue;
                    }
                    if (lmNotReady)
                    {
                        outcome.WarningCount++;
                        outcome.Messages.Add(
                            "ККТ " + result.KktSerial +
                            ": контроллер настроен; ЛМ ЧЗ пока не готов (2025/2055)." );
                        continue;
                    }
                    outcome.FailedCount++;
                    outcome.Messages.Add(
                        "ККТ " + result.KktSerial +
                        ": привязка ЕСМ требует проверки: " +
                        SensitiveDataMasker.Mask(result.Details));
                }
                outcome.Cancelled |= binding.Cancelled;
            }
        }

        private void FinalizeDirectControllerOutcome(
            DirectControllerSetupOutcome outcome)
        {
            outcome.Complete = DirectControllerSetupPolicy.IsComplete(
                outcome.ExpectedKktCount,
                outcome.ReadyCount,
                outcome.FailedCount,
                outcome.Cancelled);
            _statusLabel.Text = outcome.FormatSummary();
            Log("=== Итог прямых контроллеров ===\r\n" +
                outcome.FormatSummary() + "\r\n");
            for (int index = 0; index < outcome.Messages.Count; index++)
            {
                Log(SensitiveDataMasker.Mask(outcome.Messages[index]) + "\r\n");
            }
        }

        private bool HasDirectControllersForRemoval()
        {
            try
            {
                return new DirectControllerOperatorInventoryReader()
                    .ReadRemovalItems().Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task ConfirmAndRemoveAllDirectControllersAsync()
        {
            IList<DirectControllerProvisioningItemRequest> items;
            try
            {
                items = new DirectControllerOperatorInventoryReader()
                    .ReadRemovalItems();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    SensitiveDataMasker.Mask(ex.Message),
                    "Удаление контроллеров",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
            if (items.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Созданные программой прямые контроллеры не найдены.",
                    "Удаление контроллеров",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            DialogResult confirmation = MessageBox.Show(
                this,
                "Будут удалены созданные клоны контроллеров и восстановлены " +
                    "исходные конфигурации экземпляров ЕСМ. Штатная служба " +
                    "esm-lm-controller останется установленной. Продолжить?",
                "Удалить все контроллеры",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes) return;

            await RunOperationAsync(
                async delegate(CancellationToken operationCancellation)
                {
                    LmServiceProvisioningBatchResult result =
                        await _serviceProvisioner.RemoveAllDirectControllersAsync(
                            items,
                            Guid.NewGuid().ToString("N"),
                            operationCancellation).ConfigureAwait(true);
                    for (int index = 0; index < result.Items.Count; index++)
                    {
                        Log(result.Items[index].FormatLogLine() + "\r\n");
                    }
                    _statusLabel.Text = result.Status ==
                        LmServiceProvisioningStatus.Succeeded
                        ? "Прямые контроллеры удалены; конфигурации ЕСМ восстановлены."
                        : "Удаление завершено не полностью; проверьте журнал.";
                    await RefreshCoreAsync(CancellationToken.None);
                },
                "Удаление прямых контроллеров и восстановление ЕСМ...");
        }

    }
}
