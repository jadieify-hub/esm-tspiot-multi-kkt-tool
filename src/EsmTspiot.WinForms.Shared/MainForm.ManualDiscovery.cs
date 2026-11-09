using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Button = AntdUI.Button;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class MainForm
    {
        private readonly DataGridView _discoveredKktsGrid = new DataGridView();
        private KktOrganizationStore _organizations = KktOrganizationStore.CreateDefault();
        private readonly Button _findKktsButton = new Button();
        private readonly Button _setupSelectedButton = new Button();
        private readonly Button _manualStopButton = new Button();
        private readonly Label _manualDiscoveryHint = new Label();
        private readonly AntdUI.Panel _wizardEmptyState = new AntdUI.Panel();
        private readonly FlowLayoutPanel _manualDiscoveryToolbar = new FlowLayoutPanel();
        private readonly CheckBox _manualFallbackToggle = new CheckBox();
        private readonly Dictionary<string, TspiotFormInput> _manualInputs =
            new Dictionary<string, TspiotFormInput>(StringComparer.Ordinal);
        private readonly HashSet<string> _manualRegistered = new HashSet<string>(StringComparer.Ordinal);
        private KktCandidateScan _manualScan;
        private string _manualSelectedSerial;
        private bool _fillingManualGrid;

        private sealed class KktCandidateScan
        {
            internal BulkRegistrationDiscovery Discovery;
            internal SequentialKktDiscovery Sequential;
            internal SequentialKktRegistrationCoordinator Coordinator;
        }

        private Control BuildDiscoveredKktsPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
                RowCount = 3, MinimumSize = new Size(0, 70) };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var toolbar = _manualDiscoveryToolbar;
            toolbar.AutoSize = true;
            toolbar.Dock = DockStyle.Fill;
            ConfigureButton(_findKktsButton, "Найти ККТ", ScanKktsAsync);
            _findKktsButton.Tag = "ScanPhysicalKkts";
            toolbar.Controls.Add(_findKktsButton);
            _manualStopButton.Text = "Остановить";
            _manualStopButton.AutoSize = true;
            _manualStopButton.Enabled = false;
            _manualStopButton.Click += delegate { if (_automaticCancellation != null) _automaticCancellation.Cancel(); };
            toolbar.Controls.Add(_manualStopButton);
            toolbar.Controls.Add(new Label { Text = "Поиск получает данные касс; регистрация выполняется отдельно.",
                AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
            panel.Controls.Add(toolbar, 0, 0);
            _discoveredKktsGrid.Dock = DockStyle.Fill;
            _discoveredKktsGrid.ReadOnly = true;
            _discoveredKktsGrid.AllowUserToAddRows = false;
            _discoveredKktsGrid.AllowUserToDeleteRows = false;
            _discoveredKktsGrid.RowHeadersVisible = false;
            _discoveredKktsGrid.MultiSelect = false;
            _discoveredKktsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _discoveredKktsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _discoveredKktsGrid.BackgroundColor = SystemColors.Window;
            foreach (string heading in new[] { "ККТ", "ФН", "ИНН", "COM", "Порт ЕСМ", "Порт ПО", "Состояние", "ЛМ ЧЗ" })
                _discoveredKktsGrid.Columns.Add(heading, heading);
            _discoveredKktsGrid.Columns["Состояние"].HeaderText = "Регистрация";
            int[] columnWeights = { 145, 150, 115, 60, 80, 80, 160, 180 };
            for (int index = 0; index < columnWeights.Length; index++)
            {
                _discoveredKktsGrid.Columns[index].FillWeight = columnWeights[index];
                _discoveredKktsGrid.Columns[index].MinimumWidth = index == 3 ? 45 : 60;
                _discoveredKktsGrid.Columns[index].SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            _discoveredKktsGrid.Columns["Состояние"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _discoveredKktsGrid.Columns["ЛМ ЧЗ"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _discoveredKktsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            _discoveredKktsGrid.RowTemplate.Height = 28;
            OperatorTheme.Grid(_discoveredKktsGrid);
            _discoveredKktsGrid.Columns.Add("Organization", "Организация");
            _discoveredKktsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            string[] columnOrder = { "ККТ", "Organization", "ИНН", "COM", "Состояние", "ЛМ ЧЗ", "ФН", "Порт ЕСМ", "Порт ПО" };
            int[] widths = { 180, 230, 125, 72, 160, 160, 180, 85, 85 };
            for (int index = 0; index < columnOrder.Length; index++)
            {
                var column = _discoveredKktsGrid.Columns[columnOrder[index]];
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.DisplayIndex = index;
                column.Width = widths[index];
            }
            _discoveredKktsGrid.Columns["ККТ"].HeaderText = "Серийный номер ККТ";
            _discoveredKktsGrid.Columns["ККТ"].MinimumWidth = 180;
            _discoveredKktsGrid.Columns["ККТ"].FillWeight = 230;
            _discoveredKktsGrid.Columns["ИНН"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            _discoveredKktsGrid.Columns["ИНН"].Width = 125;
            _discoveredKktsGrid.Columns["COM"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            _discoveredKktsGrid.Columns["COM"].Width = 72;
            _discoveredKktsGrid.Columns["COM"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _discoveredKktsGrid.CurrentCellChanged += delegate { SelectManualDiscoveredKkt(); };
            var gridHost = new Panel { Dock = DockStyle.Fill };
            gridHost.Controls.Add(_discoveredKktsGrid);
            _wizardEmptyState.Dock = DockStyle.Fill;
            _wizardEmptyState.Back = _wizardEmptyState.BackColor = OperatorTheme.Soft;
            _wizardEmptyState.BorderColor = OperatorTheme.Line;
            _wizardEmptyState.BorderWidth = 1;
            _wizardEmptyState.Radius = 4;
            _wizardEmptyState.Padding = new Padding(22, 16, 20, 12);
            var emptyTitle = new Label { Text = "Подключите кассы — остальное по шагам", Dock = DockStyle.Top,
                Height = 36, Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = OperatorTheme.Ink };
            var emptyHint = new Label { Text = "Подключите ККТ по USB/VCOM.\r\nЗакройте Frontol и тест драйвера АТОЛ.\r\nЛокальный или удалённый ЛМ выберем после регистрации.",
                Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F), ForeColor = OperatorTheme.Muted };
            _wizardEmptyState.Controls.Add(emptyHint);
            _wizardEmptyState.Controls.Add(emptyTitle);
            gridHost.Controls.Add(_wizardEmptyState);
            panel.Controls.Add(gridHost, 0, 1);
            _manualDiscoveryHint.AutoSize = true;
            _manualDiscoveryHint.Text = "Список пуст. Нажмите «Найти ККТ» или раскройте ручные операции ниже.";
            panel.Controls.Add(_manualDiscoveryHint, 0, 2);
            return panel;
        }

        private Control BuildManualSelectionActions()
        {
            var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            ConfigureButton(_setupSelectedButton, "Настроить выбранную ККТ", SetupManualSelectionAsync);
            _setupSelectedButton.Tag = "SetupSelectedKkt";
            _setupSelectedButton.BackColor = OperatorTheme.Blue;
            _setupSelectedButton.ForeColor = Color.White;
            panel.Controls.Add(_setupSelectedButton);
            panel.Controls.Add(new Label { Text = "Регистрация выбранной ККТ → выбор ЛМ ЧЗ → настройка подключения",
                AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
            return panel;
        }

        private Control BuildManualFallbackPanel()
        {
            var panel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var toggle = _manualFallbackToggle;
            toggle.Text = "Отдельные операции и ручной ввод";
            toggle.AutoSize = true;
            var details = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top,
                ColumnCount = 1, Visible = false, MinimumSize = new Size(700, 0) };
            _atolConfirmedCheckBox.Text = "Я проверил связь в драйвере АТОЛ именно с подключаемой физической ККТ";
            _atolConfirmedCheckBox.AutoSize = true;
            details.Controls.Add(_atolConfirmedCheckBox, 0, 0);
            details.Controls.Add(BuildManualKktActionsGroup(), 0, 1);
            details.Controls.Add(BuildManualInstancesGroup(), 0, 2);
            details.Controls.Add(_restoreFrontolButton, 0, 3);
            toggle.CheckedChanged += delegate
            {
                details.Visible = toggle.Checked;
                _atolConfirmedCheckBox.Visible = true;
                _kktSerialTextBox.ReadOnly = _fnSerialTextBox.ReadOnly = _kktInnTextBox.ReadOnly = !toggle.Checked && _manualSelectedSerial != null;
                UpdateWizardActions();
            };
            panel.Controls.Add(toggle);
            panel.Controls.Add(details);
            return panel;
        }

        private async Task<KktCandidateScan> DiscoverKktCandidatesAsync(
            string baseUrl, string dkktPort, CancellationToken cancellation, bool requirePhysicalScan = false)
        {
            var scan = new KktCandidateScan();
            scan.Discovery = await _bulkWorkflow.DiscoverAsync(baseUrl, dkktPort, HandleBulkProgress, cancellation);
            if (!scan.Discovery.IsValid) throw new InvalidOperationException(scan.Discovery.ErrorMessage);
            if (requirePhysicalScan || AutomaticRegistrationModeSelector.Select(scan.Discovery) == AutomaticRegistrationMode.SequentialVcom)
            {
                scan.Coordinator = new SequentialKktRegistrationCoordinator(_client, _kktConnections);
                scan.Sequential = await DiscoverSequentialKktsWithRetryAsync(scan.Coordinator, baseUrl, cancellation);
                if (scan.Sequential == null) throw new OperationCanceledException();
                scan.Discovery = await _bulkWorkflow.DiscoverFromDevicesAsync(baseUrl, dkktPort,
                    CopySequentialDevices(scan.Sequential), HandleBulkProgress, cancellation);
                if (!scan.Discovery.IsValid) throw new InvalidOperationException(scan.Discovery.ErrorMessage);
            }
            if (scan.Sequential != null)
            {
                bool changed = false;
                foreach (var target in scan.Sequential.Targets)
                    if (target.Identity != null && target.Device != null &&
                        target.Identity.OrganizationInn == target.Device.KktInn)
                        changed |= _organizations.Remember(target.Identity.KktSerial,
                            target.Device.KktInn, target.Identity.OrganizationName);
                if (changed)
                    try { _organizations.Save(); }
                    catch (Exception error) { AppendLog("Название организации не сохранено: " + error.Message + "\r\n"); }
            }
            return scan;
        }

        private async Task ScanKktsAsync()
        {
            _automaticCancellation = new CancellationTokenSource();
            _manualStopButton.Enabled = true;
            try { await ScanKktsCoreAsync(_automaticCancellation.Token); }
            finally { _manualStopButton.Enabled = false; _automaticCancellation.Dispose(); _automaticCancellation = null; }
        }

        private async Task ScanKktsCoreAsync(CancellationToken cancellation)
        {
            ShowWizardStage(1, "Ищем подключённые ККТ", "Поиск не выполняет регистрацию. Дождитесь завершения опроса.");
            ValidationResult validation = TspiotInputValidator.ValidateBulkSettings(
                _baseUrlTextBox.Text, _dkktPortTextBox.Text);
            if (!validation.IsValid) throw new InvalidOperationException(validation.JoinMessages());
            SaveSelectedManualInput();
            _manualDiscoveryHint.Text = "Поочерёдный поиск ККТ. Закройте Frontol и тест драйвера.";
            KktCandidateScan scan = await DiscoverKktCandidatesAsync(
                _baseUrlTextBox.Text.Trim(), _dkktPortTextBox.Text.Trim(), cancellation, true);
            IList<LmGatewayKkt> registered = await _lmGatewayPage.DiscoverRegisteredKktsForAutomaticPlanAsync(
                _baseUrlTextBox.Text.Trim(), cancellation);
            _manualScan = scan;
            FillDiscoveredKkts(scan, registered);
            UpdateWizardSelection();
        }

        private void FillDiscoveredKkts(KktCandidateScan scan, IList<LmGatewayKkt> registered, bool preserveInputs = true)
        {
            foreach (LmGatewayKkt kkt in registered)
                if (kkt.RegistrationConfirmed)
                    foreach (DkktDeviceInfo physical in scan.Discovery.ObservedDevices)
                        if (physical.KktSerial == kkt.KktSerial &&
                            (physical.KktInn != kkt.KktInn || physical.FnSerial != kkt.FnSerial))
                            throw new InvalidOperationException("ФН или ИНН физической ККТ " + kkt.KktSerial + " не совпадают с регистрацией ЕСМ.");
            var found = new SortedDictionary<string, TspiotFormInput>(StringComparer.Ordinal);
            _manualRegistered.Clear();
            foreach (BulkRegistrationWorkItem work in scan.Discovery.Items)
            {
                TspiotFormInput input = work.Item.Input;
                TspiotFormInput previous;
                if (preserveInputs && work.RequiresAdd && _manualInputs.TryGetValue(input.KktSerial, out previous) &&
                    previous.KktInn == input.KktInn && previous.FnSerial == input.FnSerial)
                { input.Port = previous.Port; input.SoftPort = previous.SoftPort; }
                found[input.KktSerial] = input;
            }
            foreach (LmGatewayKkt kkt in registered)
            {
                if (!kkt.RegistrationConfirmed) continue;
                TspiotFormInput physical;
                if (found.TryGetValue(kkt.KktSerial, out physical) &&
                    (physical.KktInn != kkt.KktInn || physical.FnSerial != kkt.FnSerial))
                    throw new InvalidOperationException("ФН или ИНН физической ККТ " + kkt.KktSerial + " не совпадают с регистрацией ЕСМ.");
                found[kkt.KktSerial] = new TspiotFormInput { BaseUrl = _baseUrlTextBox.Text.Trim(),
                    DkktPort = _dkktPortTextBox.Text.Trim(), KktSerial = kkt.KktSerial,
                    FnSerial = kkt.FnSerial, KktInn = kkt.KktInn, Port = kkt.Port, SoftPort = kkt.SoftPort };
                _manualRegistered.Add(kkt.KktSerial);
                RememberWizardKkt(kkt);
            }
            foreach (string serial in new List<string>(_wizardKnownKkts.Keys))
                if (!_manualRegistered.Contains(serial))
                { _wizardKnownKkts.Remove(serial); _wizardKktResults.Remove(serial); }
            _fillingManualGrid = true;
            try
            {
                _discoveredKktsGrid.Rows.Clear();
                foreach (var pair in found)
                {
                    _manualInputs[pair.Key] = pair.Value;
                    SequentialKktRegistrationTarget target = scan.Sequential == null ? null : scan.Sequential.FindBySerial(pair.Key);
                    int row = _discoveredKktsGrid.Rows.Add(pair.Key, pair.Value.FnSerial, pair.Value.KktInn,
                        target == null ? "—" : target.Port.PortName, pair.Value.Port, pair.Value.SoftPort,
                        _manualRegistered.Contains(pair.Key) ? "Зарегистрирована" : ManualPortsAreFixed(pair.Key) ? "Экземпляр создан" : "Не зарегистрирована",
                        _wizardKktResults.ContainsKey(pair.Key) ? _wizardKktResults[pair.Key] : "Не настраивался");
                    _discoveredKktsGrid.Rows[row].Tag = pair.Key;
                    string organization = _organizations.GetName(pair.Key, pair.Value.KktInn);
                    OperatorTheme.SetOrganizationCell(_discoveredKktsGrid.Rows[row], organization);
                }
                _manualSelectedSerial = null;
            }
            finally { _fillingManualGrid = false; }
            SelectManualDiscoveredKkt();
            _manualDiscoveryHint.Text = found.Count == 0 ? "ККТ не найдены. Доступен ручной ввод ниже."
                : "Найдено ККТ: " + found.Count + ". Выберите строку и проверьте параметры перед настройкой.";
        }

        private void SaveSelectedManualInput()
        {
            TspiotFormInput input;
            if (_manualSelectedSerial == null || !_manualInputs.TryGetValue(_manualSelectedSerial, out input)) return;
            if (!ManualPortsAreFixed(_manualSelectedSerial))
            {
                input.Port = _portTextBox.Text.Trim(); input.SoftPort = _softPortTextBox.Text.Trim();
                foreach (DataGridViewRow row in _discoveredKktsGrid.Rows)
                    if ((string)row.Tag == _manualSelectedSerial)
                    { row.Cells["Порт ЕСМ"].Value = input.Port; row.Cells["Порт ПО"].Value = input.SoftPort; }
            }
        }

        private bool ManualPortsAreFixed(string serial)
        {
            if (_manualRegistered.Contains(serial)) return true;
            if (_manualScan != null)
                foreach (BulkRegistrationWorkItem work in _manualScan.Discovery.Items)
                    if (work.Item.Input.KktSerial == serial && !work.RequiresAdd) return true;
            return false;
        }

        private void SelectManualDiscoveredKkt()
        {
            if (_fillingManualGrid) return;
            SaveSelectedManualInput();
            DataGridViewRow row = _discoveredKktsGrid.CurrentRow;
            _manualSelectedSerial = row == null ? null : row.Tag as string;
            TspiotFormInput input;
            if (_manualSelectedSerial == null || !_manualInputs.TryGetValue(_manualSelectedSerial, out input))
            { if (!_busy) UpdateWizardSelection(); return; }
            _kktSerialTextBox.Text = input.KktSerial;
            _fnSerialTextBox.Text = input.FnSerial;
            _kktInnTextBox.Text = input.KktInn;
            _portTextBox.Text = input.Port;
            _softPortTextBox.Text = input.SoftPort;
            _atolConfirmedCheckBox.Checked = false;
            _atolConfirmedCheckBox.Visible = _manualScan == null || _manualScan.Sequential == null ||
                _manualScan.Sequential.FindBySerial(input.KktSerial) == null;
            _kktSerialTextBox.ReadOnly = _fnSerialTextBox.ReadOnly = _kktInnTextBox.ReadOnly = true;
            _portTextBox.ReadOnly = _softPortTextBox.ReadOnly = ManualPortsAreFixed(input.KktSerial);
            if (!_busy) UpdateWizardSelection();
        }

        private async Task<LmGatewayKkt> RegisterManualSelectionCoreAsync(CancellationToken cancellation)
        {
            SaveSelectedManualInput();
            TspiotFormInput input;
            if (_manualSelectedSerial == null || !_manualInputs.TryGetValue(_manualSelectedSerial, out input))
                throw new InvalidOperationException("Нажмите «Найти ККТ» и выберите строку. Для ввода руками доступны отдельные операции.");
            if (_kktSerialTextBox.Text.Trim() != input.KktSerial || _fnSerialTextBox.Text.Trim() != input.FnSerial ||
                _kktInnTextBox.Text.Trim() != input.KktInn)
                throw new InvalidOperationException("Данные формы отличаются от найденной физической ККТ. Повторите поиск.");
            if (input.BaseUrl != _baseUrlTextBox.Text.Trim() || input.DkktPort != _dkktPortTextBox.Text.Trim())
                throw new InvalidOperationException("Адрес ЕСМ или dkktPort изменён. Повторите поиск ККТ.");
            ValidationResult settings = TspiotInputValidator.ValidateBulkSettings(input.BaseUrl, input.DkktPort);
            if (!settings.IsValid) throw new InvalidOperationException(settings.JoinMessages());
            if (!ConfirmWarnings(settings)) throw new OperationCanceledException();
            foreach (DataGridViewRow row in _discoveredKktsGrid.Rows)
                if ((string)row.Tag != input.KktSerial &&
                    PortPairsOverlap(_manualInputs[(string)row.Tag], input))
                    throw new InvalidOperationException("Порты выбранной ККТ совпадают с назначением другой кассы.");
            var device = new DkktDeviceInfo { KktSerial = input.KktSerial, KktInn = input.KktInn, FnSerial = input.FnSerial };
            BulkRegistrationDiscovery fresh = await _bulkWorkflow.DiscoverFromDevicesAsync(input.BaseUrl,
                input.DkktPort, new[] { device }, HandleBulkProgress, cancellation);
            if (!fresh.IsValid) throw new InvalidOperationException(fresh.ErrorMessage);
            foreach (BulkKktRegistrationResult result in fresh.InitialResults)
                if (result.Status != BulkKktRegistrationStatus.AlreadyExists)
                    throw new InvalidOperationException(result.FormatLogLine());
            foreach (BulkRegistrationWorkItem work in fresh.Items)
            {
                if (work.Item.Input.KktSerial != input.KktSerial) throw new InvalidOperationException("План содержит постороннюю ККТ.");
                if (work.RequiresAdd)
                {
                    ApiResponse inventoryResponse = await _client.GetInstancesAsync(input.BaseUrl, cancellation);
                    IList<KktInstanceInfo> instances;
                    if (!inventoryResponse.IsSuccess || !InstanceInfoParser.TryParse(inventoryResponse, out instances))
                        throw new InvalidOperationException("Не удалось проверить занятые порты ЕСМ перед регистрацией.");
                    foreach (KktInstanceInfo instance in instances)
                        if (instance.Id != input.KktSerial &&
                            PortPairsOverlap(new TspiotFormInput { Port = instance.Port, SoftPort = instance.SoftPort }, input))
                            throw new InvalidOperationException("Выбранные порты уже заняты другим экземпляром ЕСМ. Повторите поиск.");
                    work.Item.Input.Port = input.Port;
                    work.Item.Input.SoftPort = input.SoftPort;
                    work.Item.Validation = TspiotInputValidator.ValidatePost(work.Item.Input);
                    ValidationResult put = TspiotInputValidator.ValidatePut(work.Item.Input, true);
                    if (!work.Item.Validation.IsValid || !put.IsValid)
                        throw new InvalidOperationException(work.Item.Validation.JoinMessages() + " " + put.JoinMessages());
                }
                BulkKktRegistrationResult result;
                SequentialKktRegistrationTarget target = _manualScan == null || _manualScan.Sequential == null
                    ? null : _manualScan.Sequential.FindBySerial(input.KktSerial);
                if (target != null)
                    result = await _manualScan.Coordinator.ExecuteWithTargetAsync(input.BaseUrl, target,
                        delegate(CancellationToken token) { return _bulkWorkflow.ExecuteItemAsync(work, 1, 1, HandleBulkProgress, token); },
                        HandleBulkProgress, cancellation);
                else
                {
                    throw new InvalidOperationException("Для выбранной ККТ нет подтверждённой VCOM-карты. Повторите поиск либо используйте отдельные ручные операции.");
                }
                AppendBulkResult(result);
                if (result.Status != BulkKktRegistrationStatus.Registered && result.Status != BulkKktRegistrationStatus.RecoveredRegistration &&
                    result.Status != BulkKktRegistrationStatus.AlreadyExists)
                    throw new InvalidOperationException(result.FormatLogLine());
                input = work.Item.Input;
            }
            IList<LmGatewayKkt> registered = await _lmGatewayPage.DiscoverRegisteredKktsForAutomaticPlanAsync(input.BaseUrl, cancellation);
            foreach (LmGatewayKkt kkt in registered)
                if (kkt.RegistrationConfirmed && kkt.KktSerial == input.KktSerial && kkt.KktInn == input.KktInn && kkt.FnSerial == input.FnSerial)
                {
                    _manualRegistered.Add(kkt.KktSerial);
                    RememberWizardKkt(kkt);
                    _portTextBox.ReadOnly = _softPortTextBox.ReadOnly = true;
                    SetManualRowStatus(kkt.KktSerial, "Зарегистрирована");
                    return kkt;
                }
            throw new InvalidOperationException("ЕСМ не подтвердил регистрацию выбранной ККТ с ожидаемыми ИНН и ФН.");
        }

        private async Task SetupManualSelectionAsync()
        {
            _automaticCancellation = new CancellationTokenSource();
            _manualStopButton.Enabled = true;
            _automaticStopButton.Enabled = true;
            try
            {
                ShowWizardStage(2, "Регистрируем выбранную ККТ", "Другие кассы не изменяются.");
                LmGatewayKkt kkt = await RegisterManualSelectionCoreAsync(_automaticCancellation.Token);
                ShowWizardLmStep(new[] { kkt }, false);
            }
            finally { _manualStopButton.Enabled = false; _automaticCancellation.Dispose(); _automaticCancellation = null; }
        }

        private void SetManualRowStatus(string serial, string status)
        {
            foreach (DataGridViewRow row in _discoveredKktsGrid.Rows)
                if ((string)row.Tag == serial) row.Cells["Состояние"].Value = status;
        }

        private static bool PortPairsOverlap(TspiotFormInput first, TspiotFormInput second)
        {
            foreach (string left in new[] { first.Port, first.SoftPort })
                foreach (string right in new[] { second.Port, second.SoftPort })
                {
                    int a, b;
                    if (int.TryParse(left, out a) && int.TryParse(right, out b) && a == b) return true;
                }
            return false;
        }
    }
}
