using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class LmGatewayPage
    {
        private readonly CheckBox _showServiceOperations = new CheckBox();
        private readonly Panel _serviceOperations = new Panel();
        private readonly TextBox _diagnosticsTextBox = new TextBox();
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
            _showServiceOperations.Checked = true;
        }

        internal void InvalidateDestination()
        {
            _session.ReplaceDiscovery(null);
            _currentDiscovery = null;
            _hasLoaded = false;
            _grid.Rows.Clear();
            _diagnosticsTextBox.Visible = false;
            _statusLabel.Text = "Адрес ЕСМ изменён. Обновите данные касс.";
            UpdateActionState();
        }

        internal async Task DiagnoseKktAsync(string serial)
        {
            if (_running || _hostBusy || FmuApiMode) return;
            await RunOperationAsync(async delegate(CancellationToken cancellation)
            {
                string baseUrl = ValidateAndReadBaseUrl();
                LmGatewayDiscovery discovery = await _discoveryWorkflow.DiscoverAsync(baseUrl, null, cancellation, serial);
                if (!discovery.IsSuccessful) throw new InvalidOperationException(discovery.ErrorMessage);
                IList<LmGatewayKkt> fresh = discovery.Items;
                LmGatewayKkt selected = null;
                foreach (LmGatewayKkt kkt in fresh)
                    if (kkt.KktSerial == serial && kkt.RegistrationConfirmed) { selected = kkt; break; }
                if (selected == null) throw new InvalidOperationException("Регистрация выбранной ККТ не подтверждена. Обновите список.");
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
                _diagnosticsTextBox.Text = text.ToString();
                _diagnosticsTextBox.Visible = true;
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

        private void UpdateWorkingGrid()
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var context = row.Tag as LmGatewayGridRow;
                if (context != null && context.SessionRow != null && context.SessionRow.Kkt != null)
                    row.Cells["KktServicePort"].Value = context.SessionRow.Kkt.Port;
            }
        }

        internal string DescribeAppliedLmTarget(string serial)
        {
            LmGatewayTarget target;
            return _appliedLmTargets.TryGetValue(serial, out target)
                ? target.Address + ":" + target.Port : "Не проверен";
        }
    }
}
