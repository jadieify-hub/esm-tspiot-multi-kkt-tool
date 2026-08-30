using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class LmGatewayPage
    {
        private readonly Button _bindButton = new Button();
        private readonly LmGatewayBindingWorkflow _bindingWorkflow;

        private async Task BindSelectedAsync()
        {
            if (_running || _hostBusy)
            {
                return;
            }

            LmGatewayBindingSessionRow row = GetSelectedSessionRow();
            LmServiceInventoryItem inventory = GetSelectedInventoryItem();
            if (row == null || row.Kkt == null || inventory == null ||
                inventory.Role != LmServiceRole.Managed ||
                !inventory.IsRunning || !inventory.IsReady)
            {
                MessageBox.Show(
                    this,
                    "Выберите ККТ с запущенным и проверенным управляемым контроллером.",
                    "Привязка к ЕСМ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            LmGatewayBindingPlan plan = _session.BuildPlanFor(row.Kkt.KktSerial);
            ApplyExpectedLmTargets(plan);
            if (plan.Items.Count != 1 || !plan.Items[0].IsValid)
            {
                string details = plan.Items.Count == 1 && plan.Items[0].Validation != null
                    ? plan.Items[0].Validation.JoinMessages()
                    : "Не удалось построить однозначный план для выбранной ККТ.";
                MessageBox.Show(
                    this,
                    details,
                    "Привязка к ЕСМ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            LmGatewayBindingItem item = plan.Items[0];
            LmGatewayCredentials credentials;
            using (LmGatewayBindingDialog dialog = new LmGatewayBindingDialog(
                item.Kkt.KktSerial,
                item.Kkt.KktInn,
                item.Input.ControllerAddress + ":" + item.Input.ControllerGrpcPort))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                credentials = dialog.Credentials;
            }

            try
            {
                await RunOperationAsync(
                    delegate(CancellationToken token)
                    {
                        return ExecuteSelectedBindingAsync(plan, credentials, token);
                    },
                    "Привязка выбранной ККТ к ЕСМ...");
            }
            finally
            {
                credentials.Login = string.Empty;
                credentials.Password = string.Empty;
                credentials = null;
            }
        }

        private async Task ExecuteSelectedBindingAsync(
            LmGatewayBindingPlan plan,
            LmGatewayCredentials credentials,
            CancellationToken cancellationToken)
        {
            string baseUrl = ValidateAndReadBaseUrl();
            string serial = plan.Items[0].Kkt.KktSerial;
            Log("=== Привязка ККТ " + serial + " к ЕСМ ===\r\n");
            LmGatewayBindingOutcome outcome = await _bindingWorkflow.ExecuteAsync(
                baseUrl,
                plan,
                delegate(string requestedSerial)
                {
                    if (!string.Equals(serial, requestedSerial, StringComparison.Ordinal))
                    {
                        return null;
                    }
                    return new LmGatewayCredentials
                    {
                        Login = credentials.Login,
                        Password = credentials.Password
                    };
                },
                ReportBindingProgress,
                cancellationToken);

            _session.ApplyOutcome(outcome);
            FillRows(serial);
            LmGatewayBindingResult result = outcome.Results.Count == 0
                ? null
                : outcome.Results[0];
            _statusLabel.Text = result == null
                ? "ЕСМ не вернул результат привязки."
                : GetBindingResultText(result);
            Log(_statusLabel.Text + "\r\n\r\n");
        }

        private void ReportBindingProgress(LmGatewayBindingProgress progress)
        {
            if (progress == null)
            {
                return;
            }
            PostToUi(delegate
            {
                _statusLabel.Text = progress.Stage + ": " + progress.KktSerial;
                if (!string.IsNullOrWhiteSpace(progress.Message))
                {
                    Log(SensitiveDataMasker.Mask(progress.Message) + "\r\n");
                }
            });
        }

        private static string GetBindingResultText(LmGatewayBindingResult result)
        {
            if (result == null)
            {
                return "Результат привязки отсутствует.";
            }
            return (result.KktSerial ?? string.Empty) + ": " +
                GetBindingStatusText(result.Status) + ". " +
                SensitiveDataMasker.Mask(result.Details);
        }

        private static string GetBindingStatusText(LmGatewayBindingStatus status)
        {
            switch (status)
            {
                case LmGatewayBindingStatus.BindingVerified:
                    return "привязка подтверждена";
                case LmGatewayBindingStatus.BindingAccepted:
                    return "запрос принят, требуется проверка";
                case LmGatewayBindingStatus.BindingObserved:
                    return "настройка обнаружена, требуется сверка";
                case LmGatewayBindingStatus.Cancelled:
                    return "операция остановлена";
                case LmGatewayBindingStatus.Invalid:
                    return "некорректные параметры";
                case LmGatewayBindingStatus.BindingFailed:
                    return "ошибка привязки";
                default:
                    return "требуется внимание";
            }
        }

        private void ApplyExpectedLmTargets(LmGatewayBindingPlan plan)
        {
            if (plan == null)
            {
                return;
            }
            for (int index = 0; index < plan.Items.Count; index++)
            {
                LmGatewayBindingItem item = plan.Items[index];
                if (item == null || item.Input == null)
                {
                    continue;
                }
                LmGatewayTarget target = GetExpectedLmTarget(item.Kkt);
                if (target != null)
                {
                    item.Input.ExpectedLmAddress = target.Address;
                    item.Input.ExpectedLmPort = target.Port.ToString(
                        CultureInfo.InvariantCulture);
                }
            }
        }
    }
}
