using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
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
        private readonly AntdUI.Button _showServiceOperationsButton = new AntdUI.Button();
        private readonly AntdUI.Button _showDetailsButton = new AntdUI.Button();
        private readonly AntdUI.Button _initializeLmButton = new AntdUI.Button();
        private readonly AntdUI.Button _checkGismtButton = new AntdUI.Button();
        private readonly AntdUI.Button _serviceCancelButton = new AntdUI.Button();
        private Form _serviceOperationsWindow;
        private readonly Panel _serviceOperations = new Panel();
        private readonly TextBox _diagnosticsTextBox = new TextBox();
        private readonly Label _selectionSummary = new Label { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        private readonly KktSelectionReport _selectionReport;
        internal event Action KktSelectionChanged;

        internal LmGatewayKkt SelectedKkt
        {
            get
            {
                LmGatewayBindingSessionRow row = GetSelectedSessionRow();
                return row == null || row.Kkt == null ? null : CopyKktForPlanning(row.Kkt);
            }
        }

        internal void ShowServiceOperations()
        {
            var window = _serviceOperationsWindow;
            if (window.Visible)
            {
                window.Activate();
                return;
            }
            Form owner = FindForm();
            Rectangle area = Screen.FromControl(owner ?? (Control)this).WorkingArea;
            Rectangle bounds = owner == null ? area : owner.Bounds;
            window.Size = new Size(Math.Min(window.Width, area.Width), Math.Min(window.Height, area.Height));
            int x = bounds.Left + (bounds.Width - window.Width) / 2;
            int y = bounds.Top + (bounds.Height - window.Height) / 2;
            window.Location = new Point(Math.Max(area.Left, Math.Min(x, area.Right - window.Width)),
                Math.Max(area.Top, Math.Min(y, area.Bottom - window.Height)));
            window.Show(owner);
        }

        internal void InvalidateDestination()
        {
            _session.ReplaceDiscovery(null);
            _currentDiscovery = null;
            _hasLoaded = false;
            _selectionReport.Clear();
            _grid.Rows.Clear();
            _statusLabel.Text = "Адрес ЕСМ изменён. Обновите данные касс.";
            UpdateActionState();
        }

        internal async Task DiagnoseKktAsync(string serial)
        {
            if (_running || _hostBusy || FmuApiMode) return;
            await RunOperationAsync(async delegate(CancellationToken cancellation)
            {
                string baseUrl = ValidateAndReadBaseUrl();
                if (!SelectOverviewKkt(serial))
                {
                    // Диагностика из мастера может открыться до первого обновления обзора.
                    await RefreshCoreAsync(cancellation);
                    if (!SelectOverviewKkt(serial))
                        throw new InvalidOperationException("ККТ " + serial + " отсутствует в обзоре или её регистрация не подтверждена.");
                }
                LmGatewayKkt selected = await ReadDiagnosticKktAsync(baseUrl, serial, cancellation);
                LmGatewayTarget expected;
                _appliedLmTargets.TryGetValue(serial, out expected);
                LmGatewayReadbackObservation observation = await _readbackWorkflow.ReadAsync(
                    baseUrl, selected, expected == null ? null : expected.Address,
                    expected == null ? null : expected.Port.ToString(), cancellation);
                var text = new StringBuilder();
                text.AppendLine("ККТ " + serial + " · ИНН " + selected.KktInn);
                text.AppendLine("ЕСМ: доступен. Регистрация: подтверждена.");
                text.AppendLine("Результат проверки ЛМ: " + SensitiveDataMasker.Mask(observation.Details));
                if (!string.IsNullOrEmpty(observation.LmAddress))
                    text.AppendLine("Адрес из ответа ЕСМ: " + observation.LmAddress + ":" + observation.LmPort);
                if (!string.IsNullOrEmpty(observation.LmStatus))
                    text.AppendLine("Состояние ЛМ: " + observation.LmStatus);
                text.AppendLine("Диагностика не изменяет настройки. Инициализация ЛМ выполняется отдельно.");
                string observed = observation.Details;
                if (observation.IdentityMatches && !string.IsNullOrWhiteSpace(observation.LmStatus))
                    observed = observation.LmStatus + (string.IsNullOrEmpty(observation.LmAddress)
                        ? string.Empty : " · " + observation.LmAddress + ":" + observation.LmPort);
                _selectionReport.Record(serial, "Диагностика ЛМ", observed, text.ToString());
                _statusLabel.Text = "Диагностика ККТ " + serial + " завершена.";
                Log(text.ToString() + Environment.NewLine);
            }, "Диагностика выбранной ККТ " + serial + "...");
        }

        private Task DiagnoseSelectedKktAsync()
        {
            LmGatewayKkt kkt = SelectedKkt;
            if (kkt == null) return Task.FromResult(0);
            return DiagnoseKktAsync(kkt.KktSerial);
        }

        private Task InitializeSelectedLmAsync()
        {
            LmGatewayKkt kkt = SelectedKkt;
            if (_running || _hostBusy || FmuApiMode || kkt == null || !kkt.RegistrationConfirmed)
                return Task.FromResult(0);

            string title = "Инициализация ЛМ ЧЗ";
            if (MessageBox.Show(this,
                "Запустить инициализацию ЛМ ЧЗ через ЕСМ кассы " + kkt.KktSerial +
                " (ИНН " + kkt.KktInn + ")?",
                title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return Task.FromResult(0);

            return RunOperationAsync(async delegate(CancellationToken cancellation)
            {
                _diagnosticsTextBox.Visible = false;
                string baseUrl = ValidateAndReadBaseUrl();
                LmGatewayKkt selected = await ReadDiagnosticKktAsync(baseUrl, kkt.KktSerial, cancellation);
                ApiResponse response = await _apiClient.InitializeLmAsync(baseUrl, selected.InstanceId, cancellation);
                string result;
                if (response.IsSuccess)
                    result = "ЕСМ принял запрос (HTTP " + response.StatusCode +
                        "). Готовность ЛМ проверьте кнопкой «Диагностика».";
                else if (response.IsConnectionFailure)
                    result = "Ответ ЕСМ не получен. Перед повтором проверьте состояние ЛМ диагностикой.";
                else
                {
                    result = "ЕСМ не выполнил запрос (HTTP " + response.StatusCode + ").";
                    if (response.StatusCode == 404 || response.StatusCode == 405)
                        result += " Проверьте ID экземпляра и версию ЕСМ. Метод появился в 1.6.4.3.";
                    result += TspiotErrorDecoder.DescribeResponseBody(response.ResponseBody);
                }

                string report = title + " · ККТ " + selected.KktSerial + Environment.NewLine + result;
                _selectionReport.Record(selected.KktSerial, title,
                    response.IsSuccess ? "запрос принят" : "запрос не подтверждён", report);
                _statusLabel.Text = "ККТ " + selected.KktSerial + (response.IsSuccess
                    ? " · запрос инициализации принят ЕСМ." : " · запрос инициализации не подтверждён.");
                Log(report + Environment.NewLine);
            }, title + " · ККТ " + kkt.KktSerial + "…");
        }

        private Task CheckGismtForKktAsync(string serial)
        {
            return RunOperationAsync(async delegate(CancellationToken cancellation)
            {
                _diagnosticsTextBox.Visible = false;
                string baseUrl = ValidateAndReadBaseUrl();
                LmGatewayKkt selected = await ReadDiagnosticKktAsync(baseUrl, serial, cancellation);
                string checksum = LmServiceInventoryReader.ComputeSha256(Application.ExecutablePath);
                string result = await _apiClient.CheckGismtAsync(
                    baseUrl, selected.Port, selected.SoftPort, checksum, cancellation);
                string report = "Проверка связи с ЧЗ · ККТ " + serial + Environment.NewLine + result;
                _selectionReport.Record(serial, "Проверка связи с ЧЗ", result, report);
                _statusLabel.Text = "Проверка связи с ЧЗ для ККТ " + serial + " завершена.";
                Log(report + Environment.NewLine);
            }, "Проверка связи с ЧЗ для ККТ " + serial + "...");
        }

        private async Task<LmGatewayKkt> ReadDiagnosticKktAsync(string baseUrl, string serial,
            CancellationToken cancellation)
        {
            LmGatewayDiscovery discovery = await _discoveryWorkflow.DiscoverAsync(baseUrl, null, cancellation, serial);
            if (!discovery.IsSuccessful) throw new InvalidOperationException(discovery.ErrorMessage);
            foreach (LmGatewayKkt kkt in discovery.Items)
                if (kkt.KktSerial == serial && kkt.RegistrationConfirmed) return kkt;
            throw new InvalidOperationException("Регистрация выбранной ККТ не подтверждена. Обновите список.");
        }

        private bool SelectOverviewKkt(string serial)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var context = row.Tag as LmGatewayGridRow;
                var kkt = context == null || context.SessionRow == null ? null : context.SessionRow.Kkt;
                if (kkt == null || !kkt.RegistrationConfirmed || kkt.KktSerial != serial) continue;
                _grid.CurrentCell = row.Cells["KktSerial"];
                return true;
            }
            return false;
        }

        private void UpdateSelectionSummary()
        {
            var selected = GetSelectedSessionRow();
            var row = _grid.CurrentRow;
            _selectionReport.Select(selected == null ? null : selected.Kkt.KktSerial,
                row == null ? null : Convert.ToString(row.Cells["DesiredLmEndpoint"].Value),
                row == null ? null : Convert.ToString(row.Cells["ControllerEndpoint"].Value));
        }

        internal string DescribeAppliedLmTarget(string serial)
        {
            LmGatewayTarget target;
            return _appliedLmTargets.TryGetValue(serial, out target)
                ? target.Address + ":" + target.Port : "Не проверен";
        }
    }
}
