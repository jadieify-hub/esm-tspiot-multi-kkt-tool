using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Button = AntdUI.Button;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class MainForm
    {
        private readonly AntdUI.Segmented _wizardMode = new OperatorSegments();
        private readonly SetupStepStrip _wizardSteps = new SetupStepStrip();
        private bool WizardAutomatic { get { return _wizardMode.SelectIndex == 0; } }
        private bool WizardManual { get { return _wizardMode.SelectIndex == 1; } }
        private readonly Label _wizardHeading = new Label();
        private readonly Label _wizardMessage = new Label();
        private readonly Label _wizardContext = new Label();
        private readonly ProgressBar _wizardProgress = new ProgressBar();
        private readonly TableLayoutPanel _wizardEditorsPanel = new TableLayoutPanel();
        private readonly Panel _wizardInstallerPanel = new Panel();
        private readonly TextBox _wizardResult = new TextBox();
        private readonly Button _wizardApply = new Button();
        private readonly Button _wizardDefer = new Button();
        private readonly Button _wizardPlacement = new Button();
        private readonly Button _wizardLog = new Button();
        private readonly Button _wizardEditLm = new Button();
        private readonly Button _wizardContinueSelected = new Button();
        private readonly Button _wizardDiagnose = new Button();
        private readonly Dictionary<string, LmSetupEditor> _wizardEditors =
            new Dictionary<string, LmSetupEditor>(StringComparer.Ordinal);
        private readonly Dictionary<string, LmGatewayKkt> _wizardKnownKkts =
            new Dictionary<string, LmGatewayKkt>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _wizardKktResults =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<LmGatewayKkt> _wizardPending = new List<LmGatewayKkt>();
        private readonly List<LmGatewayKkt> _wizardAutomaticPending = new List<LmGatewayKkt>();
        private Control _wizardManualDetails;
        private Control _wizardGridPanel;
        private Control _wizardFallback;
        private string _wizardPendingDestination = string.Empty;
        private string _wizardAutomaticDestination = string.Empty;
        private string _wizardDestination = string.Empty;
        private string _wizardRegistrationWarning = string.Empty;
        private bool _wizardBuilt;
        private bool _updatingWizard;
        private bool _mainOperationRunning;
        private bool _lmOperationRunning;
        private bool _closeAfterOperation;
        private bool _wizardPartialSetup;
        private bool _wizardLmEditing;
        private bool _wizardNeedsInstaller;
        private string _wizardValidationProblem;

        private Control BuildWizardPage()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
                Padding = new Padding(6), Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_wizardSteps, 0, 0);
            var mode = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            OperatorTheme.Segments(_wizardMode, "Автоматически", "По одной кассе");
            _wizardMode.Name = "wizard-mode";
            _wizardMode.Size = new Size(340, 38);
            _wizardMode.Items[0].IconSvg = "ThunderboltOutlined";
            _wizardMode.Items[1].IconSvg = "SelectOutlined";
            _wizardMode.Margin = new Padding(0, 0, 14, 4);
            mode.Controls.Add(_wizardMode);
            _fmuApiCheckBox.Text = "Пропустить контроллеры ЛМ ЧЗ (для FMU-API)";
            _fmuApiCheckBox.Tag = "FmuApiMode";
            _fmuApiCheckBox.AutoSize = true;
            _fmuApiCheckBox.CheckedChanged += delegate
            {
                if (_busy)
                {
                    _fmuApiCheckBox.Checked = _lmGatewayPage.FmuApiMode;
                    return;
                }
                _lmGatewayPage.FmuApiMode = _fmuApiCheckBox.Checked;
                foreach (LmSetupEditor editor in _wizardEditors.Values)
                    editor.FmuApiMode = _lmGatewayPage.FmuApiMode;
                UpdateWizardActions();
            };
            _automaticToolTip.SetToolTip(_fmuApiCheckBox,
                "Регистрация и ЛМ сохраняются. FMU-API устанавливается и настраивается отдельно.");
            mode.Controls.Add(_fmuApiCheckBox);
            root.Controls.Add(mode, 0, 1);
            var headings = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
            _wizardHeading.AutoSize = true;
            _wizardHeading.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            _wizardHeading.ForeColor = OperatorTheme.Ink;
            _wizardHeading.Margin = new Padding(0, 3, 0, 3);
            _wizardMessage.AutoSize = true;
            _wizardMessage.ForeColor = OperatorTheme.Muted;
            _wizardMessage.Margin = new Padding(0, 0, 0, 4);
            headings.Controls.Add(_wizardHeading, 0, 0);
            headings.Controls.Add(_wizardMessage, 0, 1);
            _wizardProgress.Dock = DockStyle.Top;
            _wizardProgress.Height = 10;
            _wizardProgress.Style = ProgressBarStyle.Marquee;
            _wizardProgress.Visible = false;
            headings.Controls.Add(_wizardProgress, 0, 2);
            root.Controls.Add(headings, 0, 2);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            scroll.Controls.Add(content);
            _wizardGridPanel = BuildDiscoveredKktsPanel();
            _wizardGridPanel.Height = 190;
            _wizardGridPanel.Dock = DockStyle.Top;
            content.Controls.Add(_wizardGridPanel, 0, 0);
            _wizardContext.AutoSize = true;
            _wizardContext.Margin = new Padding(0, 2, 0, 2);
            content.Controls.Add(_wizardContext, 0, 1);
            _wizardManualDetails = BuildWizardKktDetails();
            content.Controls.Add(_wizardManualDetails, 0, 2);
            _wizardEditorsPanel.Name = "wizard-lm-editors";
            _wizardEditorsPanel.Dock = DockStyle.Top;
            _wizardEditorsPanel.AutoSize = true;
            _wizardEditorsPanel.ColumnCount = 1;
            _wizardEditorsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.Controls.Add(_wizardEditorsPanel, 0, 3);
            _wizardInstallerPanel.Dock = DockStyle.Top;
            _wizardInstallerPanel.Height = 76;
            var installer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            installer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            installer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var installerCaption = new Label { AutoSize = true, Text = "Официальный MSI ЛМ ЧЗ — для локальных ЛМ" };
            installer.Controls.Add(installerCaption, 0, 0);
            installer.SetColumnSpan(installerCaption, 2);
            _automaticLmInstallerTextBox.ReadOnly = true;
            _automaticLmInstallerTextBox.Dock = DockStyle.Top;
            installer.Controls.Add(_automaticLmInstallerTextBox, 0, 1);
            var browse = new Button { AutoSize = true, Text = "Выбрать MSI…" };
            browse.Click += delegate { _lmGatewayPage.SelectLocalModuleInstaller(this); UpdateAutomaticInstallerSelection(); UpdateWizardActions(); };
            installer.Controls.Add(browse, 1, 1);
            _wizardInstallerPanel.Controls.Add(installer);
            content.Controls.Add(_wizardInstallerPanel, 0, 4);
            _wizardResult.Multiline = _wizardResult.ReadOnly = true;
            _wizardResult.Dock = DockStyle.Top;
            _wizardResult.Height = 130;
            _wizardResult.ScrollBars = ScrollBars.Vertical;
            _wizardResult.BackColor = SystemColors.Window;
            _wizardResult.BorderStyle = BorderStyle.FixedSingle;
            _wizardResult.Visible = false;
            content.Controls.Add(_wizardResult, 0, 5);
            _wizardFallback = BuildManualFallbackPanel();
            content.Controls.Add(_wizardFallback, 0, 6);
            content.Controls.Add(BuildRecoveryGroup(), 0, 7);
            root.Controls.Add(scroll, 0, 3);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0) };
            ConfigureButton(_bulkRegisterButton, "Начать автоматическую настройку", ConfigureAllKktsAutomaticallyAsync);
            _bulkRegisterButton.Tag = "EndToEndAutomaticSetup";
            ConfigureButton(_setupSelectedButton, "Зарегистрировать выбранную ККТ", SetupManualSelectionAsync);
            _setupSelectedButton.Tag = "SetupSelectedKkt";
            ConfigureButton(_wizardApply, "Настроить ЛМ ЧЗ", ApplyWizardLmAsync);
            _wizardApply.Name = "wizard-apply";
            _wizardDefer.Text = "Отложить";
            _wizardDefer.AutoSize = true;
            _wizardDefer.Click += delegate { DeferWizardLm(); };
            _wizardPlacement.Text = "Изменить размещение ЛМ";
            _wizardPlacement.AutoSize = true;
            _wizardPlacement.Click += delegate { _wizardNeedsInstaller = false; UpdateWizardActions(); };
            ConfigureButton(_configureFrontolButton, "Настроить Frontol…", ConfigureFrontolAsync);
            _configureFrontolButton.Tag = "ConfigureFrontol";
            ConfigureButton(_restoreFrontolButton, "Восстановить Frontol из снимка…", RestoreFrontolAsync);
            _restoreFrontolButton.Tag = "RestoreFrontol";
            _wizardLog.Text = "Открыть журнал";
            _wizardLog.AutoSize = true;
            _wizardLog.Click += delegate { _workspaceTabs.SelectedTab = _logTab; };
            _wizardEditLm.AutoSize = true;
            _wizardEditLm.Click += delegate
            {
                if (_busy) return;
                if (WizardAutomatic && _wizardAutomaticPending.Count > 0)
                    ShowWizardLmStep(_wizardAutomaticPending, true);
                else
                {
                    LmGatewayKkt selected = GetWizardSelectedKkt();
                    if (selected != null) ShowWizardLmStep(new[] { selected }, false);
                }
            };
            _wizardContinueSelected.AutoSize = true;
            _wizardContinueSelected.Text = "Продолжить настройку выбранной ККТ";
            _wizardContinueSelected.Click += delegate { ShowWizardForRegisteredKkt(GetWizardSelectedKkt()); };
            _wizardDiagnose.AutoSize = true;
            _wizardDiagnose.Text = "Диагностика";
            _wizardDiagnose.Click += async delegate
            {
                LmGatewayKkt selected = GetWizardSelectedKkt();
                if (_busy || selected == null) return;
                Task diagnosis = _lmGatewayPage.DiagnoseKktAsync(selected.KktSerial);
                _workspaceTabs.SelectedTab = _instancesTab;
                await diagnosis;
            };
            _automaticStopButton.Text = "Остановить";
            _automaticStopButton.Tag = "CancelEndToEndAutomaticSetup";
            _automaticStopButton.AutoSize = true;
            _automaticStopButton.Click += delegate { RequestWizardStop(); };
            foreach (Button button in new[] { _bulkRegisterButton, _setupSelectedButton, _wizardApply,
                _findKktsButton, _wizardDefer, _wizardPlacement, _wizardEditLm, _wizardContinueSelected, _wizardDiagnose,
                _configureFrontolButton, _wizardLog, _automaticStopButton })
            {
                OperatorTheme.Button(button, false);
                button.Padding = Padding.Empty;
                button.Margin = new Padding(0, 0, 8, 4);
                actions.Controls.Add(button);
            }
            foreach (Button primary in new[] { _bulkRegisterButton, _setupSelectedButton, _wizardApply })
            {
                OperatorTheme.Button(primary, true);
            }
            root.Controls.Add(actions, 0, 4);
            _wizardMode.SelectIndexChanged += delegate { WizardModeChanged(); };
            _automationStatusLabel.TextChanged += delegate
            {
                if (_busy) _wizardMessage.Text = _automationStatusLabel.Text;
            };
            _wizardMode.SelectIndex = 0;
            _wizardDestination = WizardDestination();
            _baseUrlTextBox.TextChanged += delegate { InvalidateWizardDestination(); };
            _dkktPortTextBox.TextChanged += delegate { InvalidateWizardDestination(); };
            root.SizeChanged += delegate
            {
                Size wrap = new Size(Math.Max(260, root.ClientSize.Width - 28), 0);
                _wizardHeading.MaximumSize = _wizardMessage.MaximumSize = _wizardContext.MaximumSize = wrap;
                _manualDiscoveryHint.MaximumSize = wrap;
            };
            ShowWizardStage(1, "Настройка всех подключённых касс",
                "Закройте Frontol и тест драйвера АТОЛ. Найдём и зарегистрируем кассы, затем настроим привязку ЛМ ЧЗ.");
            UpdateWizardActions();
            return OperatorTheme.Card(root, false);
        }

        private string WizardDestination()
        {
            return _baseUrlTextBox.Text.Trim() + "\n" + _dkktPortTextBox.Text.Trim();
        }

        private void WizardModeChanged()
        {
            if (!_wizardBuilt || _busy) return;
            SaveSelectedManualInput();
            _wizardResult.Visible = false;
            if (WizardAutomatic && _wizardAutomaticPending.Count > 0 &&
                _wizardAutomaticDestination == WizardDestination())
                ShowWizardLmStep(_wizardAutomaticPending, true);
            else if (WizardManual) SelectManualDiscoveredKkt();
            else
            {
                ClearWizardPending();
                ShowWizardStage(1, "Настройка всех подключённых касс",
                    "Одна кнопка запускает поиск и регистрацию. Затем — выбор размещения ЛМ.");
            }
            UpdateWizardActions();
        }

        private void ShowWizardStage(int stage, string heading, string message, bool attention = false)
        {
            _wizardHeading.Text = heading;
            _wizardMessage.Text = message;
            _wizardMessage.ForeColor = attention ? OperatorTheme.Error : OperatorTheme.Ink;
            _wizardSteps.SetStage(stage, attention);
        }

        private void ClearWizardPending()
        {
            _wizardPending.Clear();
            _wizardLmEditing = _wizardNeedsInstaller = false;
            _wizardEditorsPanel.Controls.Clear();
            _wizardEditorsPanel.Visible = false;
            _wizardInstallerPanel.Visible = false;
            _wizardContext.Text = string.Empty;
        }

        private void InvalidateWizardDestination()
        {
            if (!_wizardBuilt || _busy || _wizardDestination == WizardDestination()) return;
            _registrationSupportPending = _registrationProcessFinished = false;
            _wizardDestination = WizardDestination();
            ClearWizardPending();
            _connectionStatusLabel.Text = "ЕСМ: не проверено";
            _connectionStatusLabel.ForeColor = SystemColors.ControlText;
            _wizardAutomaticPending.Clear();
            _wizardKnownKkts.Clear();
            _lmGatewayPage.InvalidateDestination();
            _wizardKktResults.Clear();
            _manualRegistered.Clear();
            _manualInputs.Clear();
            _manualSelectedSerial = null;
            _manualScan = null;
            _discoveredKktsGrid.Rows.Clear();
            _instancesGrid.Rows.Clear();
            _manualInstancesGrid.Rows.Clear();
            _manualInstancesSummaryLabel.Text = "Адрес ЕСМ изменён. Обновите список экземпляров.";
            _nextPortPairLabel.Text = "Следующая свободная пара: обновите данные";
            ShowWizardStage(1, "Параметры подключения изменены", "Проверьте связь и повторите поиск ККТ.");
            UpdateWizardActions();
        }

        private void UpdateWizardSelection()
        {
            if (!_wizardBuilt || _updatingWizard || !WizardManual) return;
            _wizardResult.Visible = false;
            LmGatewayKkt registered;
            if (_manualSelectedSerial != null && _wizardKnownKkts.TryGetValue(_manualSelectedSerial, out registered) &&
                registered.RegistrationConfirmed)
            {
                string result;
                if (_wizardKktResults.TryGetValue(registered.KktSerial, out result) && WizardParametersApplied(result))
                {
                    ClearWizardPending();
                    _wizardResult.Text = "ККТ " + registered.KktSerial + " · ИНН " + registered.KktInn +
                        "\r\nПорт ЕСМ: " + registered.Port + "; порт кассового ПО: " + registered.SoftPort +
                        "\r\nЛМ ЧЗ: " + _lmGatewayPage.DescribeAppliedLmTarget(registered.KktSerial) +
                        "\r\n" + result + ". Готовность ЛМ к продаже не проверялась.";
                    _wizardResult.Visible = true;
                    ShowWizardStage(4, "Параметры выбранной ККТ применены", "Для изменений нажмите «Изменить настройки ЛМ».");
                    UpdateWizardActions();
                    return;
                }
                ShowWizardLmStep(new[] { registered }, false);
                return;
            }
            ClearWizardPending();
            bool selected = _manualSelectedSerial != null;
            bool existing = selected && ManualPortsAreFixed(_manualSelectedSerial);
            ShowWizardStage(selected ? 2 : 1,
                selected ? existing ? "Завершите регистрацию выбранной ККТ" : "Зарегистрируйте выбранную кассу" : "Найдите подключённые кассы",
                selected ? "Изменится только выбранная ККТ. После регистрации откроется настройка ЛМ ЧЗ."
                    : "Нажмите «Найти ККТ». Поиск получает реквизиты и ничего не регистрирует.");
            UpdateWizardActions();
        }

        private void ShowWizardLmStep(IList<LmGatewayKkt> kkts, bool automatic)
        {
            _updatingWizard = true;
            try
            {
                ClearWizardPending();
                var byInn = new SortedDictionary<string, List<LmGatewayKkt>>(StringComparer.Ordinal);
                foreach (LmGatewayKkt source in new List<LmGatewayKkt>(kkts))
                {
                    if (source == null || !source.RegistrationConfirmed) continue;
                    LmGatewayKkt kkt = CopyWizardKkt(source);
                    _wizardPending.Add(kkt);
                    RememberWizardKkt(kkt);
                    List<LmGatewayKkt> group;
                    if (!byInn.TryGetValue(kkt.KktInn, out group))
                    { group = new List<LmGatewayKkt>(); byInn.Add(kkt.KktInn, group); }
                    group.Add(kkt);
                }
                _wizardPendingDestination = WizardDestination();
                _wizardResult.Visible = automatic && !string.IsNullOrEmpty(_wizardRegistrationWarning);
                if (_wizardResult.Visible) _wizardResult.Text = _wizardRegistrationWarning;
                if (automatic)
                {
                    _wizardAutomaticPending.Clear();
                    foreach (LmGatewayKkt kkt in _wizardPending) _wizardAutomaticPending.Add(CopyWizardKkt(kkt));
                    _wizardAutomaticDestination = _wizardPendingDestination;
                }
                _wizardEditorsPanel.RowStyles.Clear();
                _wizardEditorsPanel.RowCount = byInn.Count;
                int row = 0;
                foreach (var pair in byInn)
                {
                    string key = _wizardPendingDestination + "\n" + pair.Key;
                    LmSetupEditor editor;
                    if (!_wizardEditors.TryGetValue(key, out editor))
                    {
                        editor = _lmGatewayPage.CreateLmSetupEditor(pair.Value);
                        editor.InputsChanged += delegate { UpdateWizardActions(); };
                        _wizardEditors.Add(key, editor);
                    }
                    editor.FmuApiMode = _lmGatewayPage.FmuApiMode;
                    editor.UpdateKktSummary(new List<LmGatewayKkt>(_wizardKnownKkts.Values));
                    _wizardEditorsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    _wizardEditorsPanel.Controls.Add(editor, 0, row++);
                }
                _wizardLmEditing = true;
                _wizardContext.Text = automatic ? "Зарегистрировано ККТ: " + _wizardPending.Count + ". Один ЛМ на ИНН."
                    : _wizardPending.Count == 0 ? string.Empty
                    : "Выбрана ККТ " + _wizardPending[0].KktSerial + " · ИНН " + _wizardPending[0].KktInn +
                        " · ЕСМ " + _wizardPending[0].Port + " · ПО " + _wizardPending[0].SoftPort;
                ShowWizardStage(3, "Где будет работать ЛМ ЧЗ?",
                    automatic ? "Выберите размещение для каждого ИНН. Регистрация уже сохранена."
                        : "ККТ уже зарегистрирована. Применение затронет только выбранную кассу.");
            }
            catch (Exception error)
            {
                ClearWizardPending();
                ShowWizardFailure(error);
            }
            finally { _updatingWizard = false; }
            UpdateWizardActions();
        }

        private static LmGatewayKkt CopyWizardKkt(LmGatewayKkt source)
        {
            return new LmGatewayKkt { InstanceId = source.InstanceId, KktSerial = source.KktSerial,
                FnSerial = source.FnSerial, KktInn = source.KktInn, Port = source.Port,
                SoftPort = source.SoftPort, DkktPort = source.DkktPort,
                ServiceState = source.ServiceState, RegistrationConfirmed = source.RegistrationConfirmed };
        }

        private async Task<IList<LmGatewayKkt>> ValidatePendingWizardTargetsAsync(CancellationToken cancellation)
        {
            if (_wizardPending.Count == 0 || _wizardPendingDestination != WizardDestination())
                throw new InvalidOperationException("Адрес ЕСМ изменён или касса не выбрана. Повторите поиск и выбор ККТ.");
            var snapshot = new List<LmGatewayKkt>(_wizardPending);
            IList<LmGatewayKkt> fresh = await _lmGatewayPage.DiscoverRegisteredKktsForAutomaticPlanAsync(
                _baseUrlTextBox.Text.Trim(), cancellation);
            var matched = new List<LmGatewayKkt>();
            foreach (LmGatewayKkt expected in snapshot)
            {
                LmGatewayKkt found = null;
                foreach (LmGatewayKkt candidate in fresh)
                    if (candidate.KktSerial == expected.KktSerial) { found = candidate; break; }
                if (found == null || !found.RegistrationConfirmed || found.InstanceId != expected.InstanceId ||
                    found.FnSerial != expected.FnSerial || found.KktInn != expected.KktInn ||
                    found.Port != expected.Port || found.SoftPort != expected.SoftPort || found.DkktPort != expected.DkktPort)
                    throw new InvalidOperationException("Регистрация ККТ " + expected.KktSerial +
                        " изменилась или не подтверждена. Обновите данные. Настройки не записаны.");
                matched.Add(CopyWizardKkt(found));
            }
            return matched;
        }

        private IList<LmGatewayKkt> BoundWizardRegistrationTargets(IList<LmGatewayKkt> expectedKkts,
            IList<LmGatewayKkt> fresh, out string warning)
        {
            var bounded = new List<LmGatewayKkt>();
            var unconfirmed = new List<string>();
            foreach (LmGatewayKkt expected in expectedKkts)
            {
                LmGatewayKkt confirmed = null;
                foreach (LmGatewayKkt actual in fresh)
                    if (actual.KktSerial == expected.KktSerial && actual.RegistrationConfirmed &&
                        actual.FnSerial == expected.FnSerial && actual.KktInn == expected.KktInn)
                    { confirmed = actual; break; }
                if (confirmed != null) bounded.Add(confirmed);
                else
                {
                    unconfirmed.Add(expected.KktSerial);
                    _manualRegistered.Remove(expected.KktSerial);
                    _wizardKnownKkts.Remove(expected.KktSerial);
                    _wizardKktResults.Remove(expected.KktSerial);
                    SetManualRowStatus(expected.KktSerial, "Итоговая регистрация не подтверждена");
                    SetWizardLmRowStatus(expected.KktSerial, "Настройка ЛМ не запускалась");
                }
            }
            warning = unconfirmed.Count == 0 ? string.Empty : "Не подтверждены при итоговом чтении ЕСМ: " +
                string.Join(", ", unconfirmed.ToArray()) + ". Обновите данные и проверьте эти кассы.";
            return bounded;
        }

        private bool TryReadWizardValues(out IDictionary<string, string> endpoints,
            out IDictionary<string, LmGatewayCredentials> credentials, out string problem)
        {
            endpoints = new Dictionary<string, string>(StringComparer.Ordinal);
            credentials = new Dictionary<string, LmGatewayCredentials>(StringComparer.Ordinal);
            problem = null;
            foreach (Control control in _wizardEditorsPanel.Controls)
            {
                var editor = control as LmSetupEditor;
                if (editor == null) continue;
                IDictionary<string, string> groupEndpoints;
                IDictionary<string, LmGatewayCredentials> groupCredentials;
                if (!editor.TryReadValues(out groupEndpoints, out groupCredentials, out problem))
                { LmSetupEditor.ClearCredentials(credentials); return false; }
                foreach (var pair in groupEndpoints) endpoints.Add(pair.Key, pair.Value);
                foreach (var pair in groupCredentials) credentials.Add(pair.Key, pair.Value);
            }
            return endpoints.Count > 0;
        }

        private async Task ApplyWizardLmAsync()
        {
            bool setupStarted = false;
            bool automaticScope = WizardAutomatic;
            IDictionary<string, string> endpoints;
            IDictionary<string, LmGatewayCredentials> credentials;
            string problem;
            if (!TryReadWizardValues(out endpoints, out credentials, out problem))
                throw new InvalidOperationException(problem ?? "Выберите зарегистрированную ККТ.");
            try
            {
                _automaticCancellation = new CancellationTokenSource();
                _automaticStopButton.Enabled = true;
                IList<LmGatewayKkt> kkts = await ValidatePendingWizardTargetsAsync(_automaticCancellation.Token);
                bool local = false;
                foreach (string endpoint in endpoints.Values) if (string.IsNullOrEmpty(endpoint)) local = true;
                if (local && !_lmGatewayPage.HasLocalModuleInstallerSelection)
                {
                    _wizardNeedsInstaller = true;
                    ShowWizardStage(3, "Выберите официальный MSI ЛМ ЧЗ",
                        "Установщик нужен для локальных ЛМ. Регистрация ККТ сохранена.");
                    return;
                }
                _lmGatewayPage.SaveLmSetup(endpoints, credentials);
                ShowWizardStage(3, "Применяем настройки ЛМ ЧЗ", "Выполняется настройка выбранного набора ККТ.");
                setupStarted = true;
                FullAutomaticLocalSetupOutcome outcome = await _lmGatewayPage.RunFullAutomaticLocalSetupFromHostAsync(
                    kkts, _automaticCancellation.Token);
                string registrationWarning = automaticScope ? _wizardRegistrationWarning : string.Empty;
                _wizardResult.Text = registrationWarning +
                    (string.IsNullOrEmpty(registrationWarning) ? string.Empty : "\r\n\r\n") + outcome.FormatSummary();
                _wizardResult.Visible = true;
                _wizardLmEditing = _wizardNeedsInstaller = false;
                foreach (LmGatewayKkt kkt in kkts)
                {
                    string result;
                    if (!outcome.KktStates.TryGetValue(kkt.KktSerial, out result)) result = "Требуется внимание";
                    _wizardKktResults[kkt.KktSerial] = result;
                    SetManualRowStatus(kkt.KktSerial, "Зарегистрирована");
                    SetWizardLmRowStatus(kkt.KktSerial, result);
                }
                if (outcome.FailedSerials.Count > 0) SelectWizardRow(outcome.FailedSerials[0]);
                bool complete = outcome.Complete && string.IsNullOrEmpty(registrationWarning);
                _wizardPartialSetup = !outcome.Complete;
                ShowWizardStage(complete ? 4 : 3,
                    outcome.Cancelled ? "Настройка остановлена" : complete ? "Настройка завершена" : "Настройка завершена не полностью",
                    "Регистрация сохранена. Готовность ЛМ к продаже проверяется отдельно.", !complete);
            }
            finally
            {
                LmSetupEditor.ClearCredentials(credentials);
                if (_automaticCancellation != null) { _automaticCancellation.Dispose(); _automaticCancellation = null; }
                if (setupStarted) FinishRegistrationProcess();
            }
        }

        private void DeferWizardLm()
        {
            if (_busy) return;
            _wizardLmEditing = _wizardNeedsInstaller = false;
            ShowWizardStage(3, "Настройка ЛМ ЧЗ отложена", "Регистрация сохранена. Выберите кассу в таблице, чтобы продолжить.");
            foreach (LmGatewayKkt kkt in _wizardPending)
                if (!_wizardKktResults.ContainsKey(kkt.KktSerial))
                    SetWizardLmRowStatus(kkt.KktSerial, "Настройка отложена");
            _wizardPending.Clear();
            UpdateWizardActions();
            FinishRegistrationProcess();
        }

        private void SetWizardLmRowStatus(string serial, string status)
        {
            foreach (DataGridViewRow row in _discoveredKktsGrid.Rows)
                if ((string)row.Tag == serial) row.Cells["ЛМ ЧЗ"].Value = status;
        }

        private void UpdateWizardActions()
        {
            if (!_wizardBuilt || _updatingWizard || IsDisposed || Disposing) return;
            bool manual = WizardManual;
            LmGatewayKkt selectedKkt = GetWizardSelectedKkt();
            bool confirmed = selectedKkt != null && selectedKkt.RegistrationConfirmed;
            bool hasPending = _wizardPending.Count > 0;
            bool editing = hasPending && _wizardLmEditing;
            bool empty = _discoveredKktsGrid.Rows.Count == 0;
            _wizardEmptyState.Visible = empty;
            _discoveredKktsGrid.Visible = !empty;
            if (empty) _wizardEmptyState.BringToFront();
            _wizardEditorsPanel.Visible = editing && !_wizardNeedsInstaller;
            _wizardInstallerPanel.Visible = editing && _wizardNeedsInstaller;
            int gridHeight = _discoveredKktsGrid.ColumnHeadersHeight + SystemInformation.HorizontalScrollBarHeight + 6;
            for (int row = 0; row < Math.Min(4, _discoveredKktsGrid.Rows.Count); row++)
                gridHeight += _discoveredKktsGrid.Rows[row].Height;
            _wizardGridPanel.Height = empty ? 138 : gridHeight;
            _manualDiscoveryToolbar.Visible = false;
            _manualDiscoveryHint.Visible = false;
            _wizardContext.Visible = !editing && confirmed;
            if (_discoveredKktsGrid.Rows.Count == 0)
                _manualDiscoveryHint.Text = manual
                    ? "Список пуст. Нажмите «Найти ККТ» или раскройте ручные операции ниже."
                    : "Подключите ККТ по USB/VCOM и нажмите «Начать автоматическую настройку».";
            _wizardMode.Enabled = !_busy;
            _wizardEditorsPanel.Enabled = _wizardInstallerPanel.Enabled = !_busy;
            _wizardProgress.Visible = _busy;
            _wizardManualDetails.Visible = manual && !editing &&
                ((_manualSelectedSerial != null && !confirmed) || _manualFallbackToggle.Checked);
            _wizardFallback.Visible = manual && !editing && !_busy;
            _findKktsButton.Visible = manual && !_busy;
            OperatorTheme.Button(_findKktsButton, empty);
            _findKktsButton.Text = empty ? "Найти ККТ" : "Повторить поиск";
            _manualStopButton.Visible = false;
            _bulkRegisterButton.Visible = !manual && !editing && !_busy;
            _setupSelectedButton.Visible = manual && !editing && !_busy && !confirmed && _manualSelectedSerial != null;
            _setupSelectedButton.Enabled = !_busy && _manualSelectedSerial != null;
            _setupSelectedButton.Text = _manualSelectedSerial != null && ManualPortsAreFixed(_manualSelectedSerial) &&
                !_manualRegistered.Contains(_manualSelectedSerial)
                ? "Завершить регистрацию ККТ" : "Зарегистрировать выбранную ККТ";
            _wizardApply.Visible = editing && !_busy;
            _wizardDefer.Visible = editing && !_busy;
            _wizardPlacement.Visible = editing && _wizardNeedsInstaller && !_busy;
            _wizardApply.Text = _wizardNeedsInstaller ? "Продолжить" : manual ? "Применить настройки ЛМ"
                : "Настроить ЛМ для " + _wizardPending.Count + " ККТ";
            _wizardEditLm.Visible = !_busy && !editing && (manual ? confirmed : _wizardAutomaticPending.Count > 0);
            _wizardEditLm.Text = manual ? "Изменить настройки ЛМ" : "Настроить ЛМ для зарегистрированных ККТ";
            _wizardContinueSelected.Visible = !_busy && !manual && !editing && confirmed && _wizardPartialSetup;
            _wizardDiagnose.Visible = !_busy && !editing && confirmed && !_lmGatewayPage.FmuApiMode;
            IDictionary<string, string> endpoints;
            IDictionary<string, LmGatewayCredentials> credentials;
            string problem;
            if (editing)
            {
                bool valid = TryReadWizardValues(out endpoints, out credentials, out problem);
                LmSetupEditor.ClearCredentials(credentials);
                _wizardApply.Enabled = !_busy && valid &&
                    (!_wizardNeedsInstaller || _lmGatewayPage.HasLocalModuleInstallerSelection);
                if (!valid && !string.IsNullOrEmpty(problem))
                { _wizardValidationProblem = problem; _wizardMessage.Text = problem; _wizardMessage.ForeColor = OperatorTheme.Error; }
                else if (!string.IsNullOrEmpty(_wizardValidationProblem))
                {
                    _wizardValidationProblem = null;
                    _wizardMessage.Text = "Параметры заполнены. Они будут применены после нажатия кнопки.";
                    _wizardMessage.ForeColor = SystemColors.ControlText;
                }
            }
            _configureFrontolButton.Visible = !_busy && !editing;
            _wizardLog.Visible = !_busy && _wizardResult.Visible;
            _automaticStopButton.Visible = _busy;
            _automaticStopButton.Enabled = _busy && (_automaticCancellation == null || !_automaticCancellation.IsCancellationRequested);
        }

        private void RequestWizardStop()
        {
            if (_automaticCancellation != null) _automaticCancellation.Cancel();
            _lmGatewayPage.RequestCancellation();
            _automaticStopButton.Enabled = false;
            _wizardHeading.Text = "Останавливаем…";
            _wizardMessage.Text = "Дождитесь безопасного завершения текущей операции. Выполненные действия сохраняются.";
        }

        private void ShowWizardFailure(Exception error)
        {
            if (!_wizardBuilt) return;
            _workspaceTabs.SelectedTab = _automationTab;
            bool cancelled = error is OperationCanceledException;
            _wizardResult.Text = cancelled ? "Уже выполненные действия сохранены. Регистрация не откатывается."
                : SensitiveDataMasker.Mask(error.Message);
            _wizardResult.Visible = true;
            ShowWizardStage(_wizardPending.Count > 0 ? 3 : _discoveredKktsGrid.Rows.Count > 0 ? 2 : 1,
                cancelled ? "Настройка остановлена" : "Не удалось завершить операцию",
                cancelled ? "Можно продолжить после проверки текущего состояния." : SensitiveDataMasker.Mask(error.Message), !cancelled);
        }

        private void ShowWizardForRegisteredKkt(LmGatewayKkt kkt)
        {
            if (_busy || kkt == null || !kkt.RegistrationConfirmed) return;
            _wizardMode.SelectIndex = 1;
            _workspaceTabs.SelectedTab = _automationTab;
            RememberWizardKkt(kkt);
            _manualRegistered.Add(kkt.KktSerial);
            _manualInputs[kkt.KktSerial] = new TspiotFormInput {
                BaseUrl = _baseUrlTextBox.Text.Trim(), DkktPort = _dkktPortTextBox.Text.Trim(),
                KktSerial = kkt.KktSerial, KktInn = kkt.KktInn, FnSerial = kkt.FnSerial,
                Port = kkt.Port, SoftPort = kkt.SoftPort };
            DataGridViewRow selected = null;
            foreach (DataGridViewRow row in _discoveredKktsGrid.Rows)
                if ((string)row.Tag == kkt.KktSerial) selected = row;
            if (selected == null)
            {
                int index = _discoveredKktsGrid.Rows.Add(kkt.KktSerial, kkt.FnSerial, kkt.KktInn, "—",
                    kkt.Port, kkt.SoftPort, "Зарегистрирована", "Не проверялся");
                selected = _discoveredKktsGrid.Rows[index];
                selected.Tag = kkt.KktSerial;
            }
            _discoveredKktsGrid.CurrentCell = selected.Cells[0];
            OperatorTheme.SetOrganizationCell(selected, _organizations.GetName(kkt.KktSerial, kkt.KktInn));
            SelectManualDiscoveredKkt();
            ShowWizardLmStep(new[] { kkt }, false);
        }

        private void SelectWizardRow(string serial)
        {
            foreach (DataGridViewRow row in _discoveredKktsGrid.Rows)
                if ((string)row.Tag == serial) { _discoveredKktsGrid.CurrentCell = row.Cells[0]; break; }
        }

        private LmGatewayKkt GetWizardSelectedKkt()
        {
            string serial = _discoveredKktsGrid.CurrentRow == null ? null : _discoveredKktsGrid.CurrentRow.Tag as string;
            LmGatewayKkt kkt;
            return serial != null && _wizardKnownKkts.TryGetValue(serial, out kkt) ? kkt : null;
        }

        private static bool WizardParametersApplied(string state)
        {
            return state == "Параметры применены" || state == "ЛМ подготовлен; контроллеры пропущены" ||
                state == "Адрес удалённого ЛМ учтён; контроллеры пропущены";
        }

        private void RememberWizardKkt(LmGatewayKkt kkt)
        {
            LmGatewayKkt previous;
            if (_wizardKnownKkts.TryGetValue(kkt.KktSerial, out previous) &&
                (previous.InstanceId != kkt.InstanceId || previous.FnSerial != kkt.FnSerial || previous.KktInn != kkt.KktInn))
                _wizardKktResults.Remove(kkt.KktSerial);
            _wizardKnownKkts[kkt.KktSerial] = CopyWizardKkt(kkt);
        }

        private void ForgetWizardKkt(string serial)
        {
            _wizardKnownKkts.Remove(serial);
            _wizardKktResults.Remove(serial);
            _manualRegistered.Remove(serial);
            _manualInputs.Remove(serial);
            _wizardAutomaticPending.RemoveAll(delegate(LmGatewayKkt kkt) { return kkt.KktSerial == serial; });
            ClearWizardPending();
            for (int index = _discoveredKktsGrid.Rows.Count - 1; index >= 0; index--)
                if ((string)_discoveredKktsGrid.Rows[index].Tag == serial) _discoveredKktsGrid.Rows.RemoveAt(index);
            if (_manualSelectedSerial == serial) _manualSelectedSerial = null;
            UpdateWizardSelection();
        }

        private Control BuildWizardKktDetails()
        {
            var group = new GroupBox { Text = "Параметры выбранной ККТ", Dock = DockStyle.Top,
                AutoSize = true, Padding = new Padding(8), Margin = new Padding(0, 4, 0, 8) };
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 3 };
            for (int index = 0; index < 2; index++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            }
            AddWizardField(table, 0, 0, "Серийный №", _kktSerialTextBox);
            AddWizardField(table, 2, 0, "ФН", _fnSerialTextBox);
            AddWizardField(table, 0, 1, "ИНН", _kktInnTextBox);
            AddWizardField(table, 2, 1, "Порт ЕСМ", _portTextBox);
            AddWizardField(table, 0, 2, "Порт ПО", _softPortTextBox);
            group.Controls.Add(table);
            return OperatorTheme.Card(group, true);
        }

        private static void AddWizardField(TableLayoutPanel table, int column, int row, string caption, TextBox input)
        {
            table.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 5, 6, 5) }, column, row);
            input.Dock = DockStyle.Top;
            input.Margin = new Padding(0, 2, 10, 2);
            input.AccessibleName = caption;
            table.Controls.Add(OperatorTheme.Input(input), column + 1, row);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _updatingWizard = true;
                foreach (LmSetupEditor editor in _wizardEditors.Values)
                    if (!editor.IsDisposed) editor.Dispose();
                _wizardEditors.Clear();
                if (_connectionDialog != null) _connectionDialog.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
