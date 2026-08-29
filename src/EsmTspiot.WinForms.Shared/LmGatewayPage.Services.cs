using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
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
        private readonly Button _selectInstallerButton = new Button();
        private readonly Button _installControllerButton = new Button();
        private readonly Button _removeServiceButton = new Button();
        private readonly Button _removeAllServicesButton = new Button();
        private readonly Button _cleanupButton = new Button();
        private readonly TextBox _installerPathTextBox = new TextBox();
        private readonly Label _installerStatusLabel = new Label();
        private readonly TextBox _targetAddressTextBox = new TextBox();
        private readonly TextBox _targetPortTextBox = new TextBox();
        private readonly TextBox _restPortTextBox = new TextBox();
        private readonly ToolTip _serviceToolTip = new ToolTip();
        private readonly Dictionary<string, LmGatewayDraft> _serviceDrafts =
            new Dictionary<string, LmGatewayDraft>(StringComparer.Ordinal);
        private readonly List<LmServiceInventoryItem> _serviceInventory =
            new List<LmServiceInventoryItem>();
        private LmGatewayDraftSettingsStore _draftSettingsStore;
        private LmServiceProvisionerClient _serviceProvisioner;
        private LmServiceInventoryReader _inventoryReader;
        private LmGatewayProbe _serviceProbe;
        private ReadOnlyTcpListenerOwnerReader _listenerReader;
        private LmGatewayLifecycleWorkflow _lifecycleWorkflow;
        private LmGatewayRemovalWorkflow _removalWorkflow;
        private LmAutomaticSetupCoordinator _automaticSetupCoordinator;
        private LmControllerInstallerSelection _installerSelection;
        private LmGatewayDiscovery _currentDiscovery;
        private bool _controllerVersionVerified;
        private bool _helperAvailable;
        private string _helperUnavailableReason = string.Empty;
        private string _serviceInventoryWarning = string.Empty;

        private void InitializeServiceFeatures()
        {
            _serviceProvisioner = new LmServiceProvisionerClient();
            _inventoryReader = new LmServiceInventoryReader();
            _serviceProbe = new LmGatewayProbe();
            _listenerReader = new ReadOnlyTcpListenerOwnerReader();
            _draftSettingsStore = LmGatewayDraftSettingsStore.CreateDefault();
            _lifecycleWorkflow = new LmGatewayLifecycleWorkflow(
                _serviceProvisioner,
                _serviceProbe,
                _bindingWorkflow);
            _removalWorkflow = new LmGatewayRemovalWorkflow(
                _serviceProvisioner,
                delegate { return _inventoryReader.Read(); });
            _automaticSetupCoordinator = new LmAutomaticSetupCoordinator();
            _helperAvailable = _serviceProvisioner.IsAvailable(out _helperUnavailableReason);
            RefreshServiceInventory();
        }

        private Control BuildInstallerPanel()
        {
            GroupBox group = new GroupBox
            {
                Text = "Версия контроллера ЛМ ЧЗ",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            TableLayoutPanel table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(6, 3, 6, 5),
                ColumnCount = 4,
                RowCount = 2
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _installerPathTextBox.Dock = DockStyle.Fill;
            _installerPathTextBox.ReadOnly = true;
            _installerPathTextBox.Tag = "Выберите esm-lm-controller_*-windows-setup.exe";
            ConfigureButton(_selectInstallerButton, "Выбрать…");
            ConfigureButton(_installControllerButton, "Установить и настроить ККТ");
            _installControllerButton.Tag = "AutomaticSetup";
            _selectInstallerButton.Click += delegate { SelectInstaller(); };
            _installControllerButton.Click += async delegate { await StartAutomaticSetupAsync(); };
            _installerStatusLabel.AutoSize = true;
            _installerStatusLabel.AutoEllipsis = true;
            _installerStatusLabel.Text = "Установщик не выбран.";
            table.Controls.Add(_installerPathTextBox, 0, 0);
            table.Controls.Add(_selectInstallerButton, 1, 0);
            table.Controls.Add(_installControllerButton, 2, 0);
            table.Controls.Add(_installerStatusLabel, 0, 1);
            table.SetColumnSpan(_installerStatusLabel, 4);
            group.Controls.Add(table);
            return group;
        }

        private Control BuildOfficialControllerPanel()
        {
            GroupBox group = new GroupBox
            {
                Text = "Штатный контроллер ЛМ ЧЗ",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(8, 4, 8, 5)
            };
            _officialControllerStatusLabel.AutoSize = true;
            _officialControllerStatusLabel.Dock = DockStyle.Fill;
            _officialControllerStatusLabel.Text =
                "Не найден. Установите или проверьте официальную версию контроллера.";
            group.Controls.Add(_officialControllerStatusLabel);
            return group;
        }

        public bool SelectControllerInstaller(IWin32Window owner)
        {
            ClearInstallerSelection();
            try
            {
                _installerSelection = LmControllerInstallerPicker.SelectAndInspect(owner ?? this);
                if (_installerSelection == null)
                {
                    _installerStatusLabel.Text = "Выбор отменён; путь к файлу очищен.";
                    UpdateActionState();
                    return false;
                }
                _installerPathTextBox.Text = _installerSelection.SourcePath;
                _serviceToolTip.SetToolTip(_installerPathTextBox, _installerSelection.SourcePath);
                _installerStatusLabel.Text = _installerSelection.FileName +
                    " | версия " + _installerSelection.FileVersion +
                    " | цифровая подпись проверена";
                RaiseInstallerSelectionChanged();
            }
            catch (Exception ex)
            {
                ClearInstallerSelection();
                MessageBox.Show(this, SensitiveDataMasker.Mask(ex.Message),
                    "Проверка установщика", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            UpdateActionState();
            return _installerSelection != null;
        }

        private void SelectInstaller()
        {
            SelectControllerInstaller(this);
        }

        private async Task StartAutomaticSetupAsync()
        {
            if (_installerSelection == null)
            {
                return;
            }

            try
            {
                SaveEditorWithoutChangingSelection();
                if (!CollectAutomaticSetupParameters())
                {
                    return;
                }
                LmGatewayPlan plan = BuildServicePlan();
                if (plan.Items.Count == 0)
                {
                    MessageBox.Show(this, "Нет выбранных ККТ для настройки.",
                        "Автоматическая настройка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string validation = GetAutomaticPlanValidation(plan);
                if (validation.Length > 0)
                {
                    MessageBox.Show(this, validation, "Исправьте параметры ККТ",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                await RunOperationAsync(
                    ExecuteAutomaticSetupAsync,
                    "Запуск защищённой установки и настройки ККТ...");
            }
            finally
            {
                ClearAllCredentials();
            }
        }

        public async Task<bool> RunAutomaticSetupFromHostAsync(
            CancellationToken externalCancellation)
        {
            bool completed = false;
            try
            {
                await RunOperationAsync(
                    async delegate(CancellationToken token)
                    {
                        await RefreshCoreAsync(token);
                        SaveEditorWithoutChangingSelection();
                        _session.SelectAll();
                        FillRows(null);
                        if (!CollectAutomaticSetupParameters())
                        {
                            throw new OperationCanceledException();
                        }
                        LmGatewayPlan plan = BuildServicePlan();
                        if (plan.Items.Count == 0)
                        {
                            throw new InvalidOperationException(
                                "В ЕСМ нет зарегистрированных ККТ для настройки контроллеров ЛМ ЧЗ.");
                        }

                        string validation = GetAutomaticPlanValidation(plan);
                        if (validation.Length > 0)
                        {
                            throw new InvalidOperationException(validation);
                        }

                        completed = await ExecuteAutomaticSetupAsync(token);
                    },
                    "Получение ККТ и автоматическая настройка контроллеров ЛМ ЧЗ...",
                    true,
                    false,
                    false,
                    externalCancellation);
                return completed;
            }
            finally
            {
                ClearAllCredentials();
            }
        }

        private bool CollectAutomaticSetupParameters()
        {
            IList<LmAutomaticSetupDialogRow> rows = CreateAutomaticSetupDialogRows();
            if (rows.Count == 0)
            {
                MessageBox.Show(this,
                    "Нет выбранных ККТ для автоматической настройки.",
                    "Автоматическая настройка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            using (LmAutomaticSetupDialog dialog = new LmAutomaticSetupDialog(rows))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK ||
                    dialog.Parameters == null)
                {
                    _statusLabel.Text = "Автоматическая настройка отменена до изменения служб.";
                    return false;
                }

                ApplyAutomaticSetupParameters(dialog.Parameters);
            }
            return true;
        }

        private IList<LmAutomaticSetupDialogRow> CreateAutomaticSetupDialogRows()
        {
            List<LmAutomaticSetupDialogRow> result =
                new List<LmAutomaticSetupDialogRow>();
            for (int index = 0; index < _session.Rows.Count; index++)
            {
                LmGatewayBindingSessionRow sessionRow = _session.Rows[index];
                if (sessionRow == null || sessionRow.Kkt == null ||
                    !sessionRow.IsSelected)
                {
                    continue;
                }

                LmGatewayDraft draft = GetOrCreateServiceDraft(
                    sessionRow.Kkt.KktSerial);
                LmGatewayCredentials credentials = GetCredentials(
                    sessionRow.Kkt.KktSerial);
                result.Add(new LmAutomaticSetupDialogRow
                {
                    Ordinal = index + 1,
                    KktSerial = sessionRow.Kkt.KktSerial,
                    KktInn = sessionRow.Kkt.KktInn,
                    SoftwarePort = sessionRow.Kkt.SoftPort,
                    TargetAddress = draft.TargetAddress,
                    TargetPort = draft.TargetPort,
                    Login = credentials == null ? string.Empty : credentials.Login,
                    Password = credentials == null ? string.Empty : credentials.Password
                });
            }
            return result;
        }

        private void ApplyAutomaticSetupParameters(
            IList<LmAutomaticSetupDialogRow> parameters)
        {
            string firstSerial = string.Empty;
            for (int index = 0; index < parameters.Count; index++)
            {
                LmAutomaticSetupDialogRow item = parameters[index];
                LmGatewayBindingSessionRow sessionRow = item == null
                    ? null
                    : FindSessionRow(item.KktSerial);
                if (sessionRow == null || sessionRow.Kkt == null ||
                    !string.Equals(
                        (sessionRow.Kkt.KktInn ?? string.Empty).Trim(),
                        (item.KktInn ?? string.Empty).Trim(),
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Состав ККТ изменился во время ввода параметров. Обновите список и повторите запуск.");
                }

                LmGatewayDraft draft = GetOrCreateServiceDraft(item.KktSerial);
                draft.KktInn = (item.KktInn ?? string.Empty).Trim();
                draft.TargetAddress = (item.TargetAddress ?? string.Empty).Trim();
                draft.TargetPort = (item.TargetPort ?? string.Empty).Trim();
                _session.TryUpdateDraft(
                    item.KktSerial,
                    LmGatewayDraftDefaults.ControllerAddress,
                    draft.GrpcPort,
                    true);
                StoreCredentials(item.KktSerial, item.Login, item.Password);
                if (firstSerial.Length == 0)
                {
                    firstSerial = item.KktSerial;
                }
            }

            bool persisted = PersistServiceDrafts();
            FillRows(firstSerial);
            _statusLabel.Text = persisted
                ? "Параметры всех выбранных ККТ приняты. Ожидается подтверждение UAC."
                : "Параметры приняты только для текущего сеанса; файл несекретных настроек недоступен.";
        }

        private async Task<bool> ExecuteAutomaticSetupAsync(CancellationToken cancellation)
        {
            LmControllerInstallerSelection selection = _installerSelection;
            if (selection == null)
            {
                return false;
            }

            try
            {
                bool installationCompleted = false;
                bool completed = await _automaticSetupCoordinator.ExecuteAsync(
                    async delegate(CancellationToken token)
                    {
                        installationCompleted = await InstallControllerStageAsync(selection, token);
                        return installationCompleted;
                    },
                    delegate(CancellationToken token)
                    {
                        LmGatewayPlan currentPlan = BuildServicePlan();
                        string validation = GetAutomaticPlanValidation(currentPlan);
                        if (currentPlan.Items.Count == 0 || validation.Length > 0)
                        {
                            throw new InvalidOperationException(
                                currentPlan.Items.Count == 0
                                    ? "Нет выбранных ККТ для настройки."
                                    : validation);
                        }
                        return ExecuteServicePlanAsync(currentPlan, token);
                    },
                    cancellation);
                if (!completed && !installationCompleted)
                {
                    _statusLabel.Text =
                        "Версия контроллера не прошла проверку; службы ККТ не изменялись.";
                }
                return completed;
            }
            finally
            {
                ClearAllCredentials();
                ClearInstallerSelection();
            }
        }

        private async Task<bool> InstallControllerStageAsync(
            LmControllerInstallerSelection selection,
            CancellationToken cancellation)
        {
            selection.StopManagedInstancesWarningAccepted = true;
            string operationId = Guid.NewGuid().ToString("N");
            LmServiceProvisioningBatchRequest request = new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 1,
                Operation = LmServiceOperation.InstallControllerVersion,
                OperationId = operationId,
                InstallerSelection = selection
            };
            string hash = CanonicalLmPlanHasher.Compute(request);
            LmControllerInstallResult result =
                await _serviceProvisioner.InstallControllerVersionAsync(
                    selection,
                    operationId,
                    hash,
                    cancellation);
            _controllerVersionVerified = result != null &&
                result.Status == LmServiceProvisioningStatus.Succeeded;
            _statusLabel.Text = result == null
                ? "Helper не вернул результат установки."
                : result.Message;
            Log("Контроллер ЛМ: " + _statusLabel.Text + "\r\n");
            RefreshServiceInventory();
            FillRows(null);
            AppendServiceCapabilityStatus();
            return _controllerVersionVerified;
        }

        private static string GetAutomaticPlanValidation(LmGatewayPlan plan)
        {
            StringBuilder result = new StringBuilder();
            for (int index = 0; index < plan.Items.Count; index++)
            {
                LmGatewayPlanItem item = plan.Items[index];
                if (item != null && item.IsValid)
                {
                    continue;
                }
                result.Append("ККТ ");
                result.Append(item == null || item.Kkt == null ? (index + 1).ToString() : item.Kkt.KktSerial);
                result.Append(": ");
                if (item == null)
                {
                    result.Append("строка плана отсутствует.");
                }
                else
                {
                    result.Append(item.ServiceValidation.JoinMessages().Replace("\r\n", "; "));
                }
                result.AppendLine();
            }
            return result.ToString().Trim();
        }

        private void RefreshServiceInventory()
        {
            _serviceInventory.Clear();
            _serviceInventoryWarning = string.Empty;
            try
            {
                IList<LmServiceInventoryItem> read = _inventoryReader.Read();
                for (int index = 0; index < read.Count; index++)
                {
                    _serviceInventory.Add(read[index]);
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException) &&
                    !(ex is InvalidDataException) && !(ex is ArgumentException) &&
                    !(ex is Win32Exception) && !(ex is CryptographicException) &&
                    !(ex is System.Security.SecurityException))
                {
                    throw;
                }
                _serviceInventoryWarning =
                    "Не удалось прочитать службы контроллеров; " +
                    "создание, обновление и удаление служб заблокировано. " +
                    "Ручная привязка к ЕСМ остаётся доступной.";
                Log(_serviceInventoryWarning + " Причина: " + ex.GetType().Name + ".\r\n");
            }
            _controllerVersionVerified = false;
            for (int index = 0; index < _serviceInventory.Count; index++)
            {
                if (_serviceInventory[index] != null &&
                    _serviceInventory[index].Role == LmServiceRole.VerifiedOfficial)
                {
                    _controllerVersionVerified = true;
                    break;
                }
            }
            UpdateOfficialControllerStatus();
            _helperAvailable = _serviceProvisioner.IsAvailable(out _helperUnavailableReason);
            if (!string.IsNullOrEmpty(_serviceInventoryWarning))
            {
                _helperAvailable = false;
                _helperUnavailableReason = _serviceInventoryWarning;
            }
        }

        private void AppendServiceCapabilityStatus()
        {
            string notice = !string.IsNullOrEmpty(_serviceInventoryWarning)
                ? _serviceInventoryWarning
                : !_helperAvailable
                    ? "Управление службами недоступно: " + _helperUnavailableReason
                    : string.Empty;
            if (!string.IsNullOrEmpty(notice))
            {
                _statusLabel.Text = (_statusLabel.Text ?? string.Empty).TrimEnd() + " " + notice;
            }
        }

        private void UpdateOfficialControllerStatus()
        {
            if (!string.IsNullOrEmpty(_serviceInventoryWarning))
            {
                _officialControllerStatusLabel.Text = "Состояние штатного контроллера прочитать не удалось.";
                return;
            }
            LmServiceInventoryDisplay display = LmServiceInventoryDisplay.Create(_serviceInventory);
            if (display.OfficialControllers.Count == 0)
            {
                _officialControllerStatusLabel.Text =
                    "Не найден. Установите или проверьте официальную версию контроллера.";
                return;
            }

            LmServiceInventoryItem official = display.OfficialControllers[0];
            _officialControllerStatusLabel.Text =
                "Установлен, состояние: " + GetServiceStatusText(official) + "." +
                (display.OfficialControllers.Count > 1
                    ? " Внимание: найдено несколько штатных контроллеров."
                    : string.Empty);
        }

        private void LoadPersistedServiceDrafts()
        {
            if (_draftSettingsStore == null || _currentDiscovery == null)
            {
                return;
            }
            try
            {
                IList<LmGatewayDraft> loaded = _draftSettingsStore.LoadFor(_currentDiscovery.Items);
                for (int index = 0; index < loaded.Count; index++)
                {
                    LmGatewayDraft draft = loaded[index];
                    if (draft != null && !string.IsNullOrEmpty(draft.KktSerial) &&
                        !_serviceDrafts.ContainsKey(draft.KktSerial))
                    {
                        _serviceDrafts.Add(draft.KktSerial, draft);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsDraftSettingsException(ex))
                {
                    throw;
                }
                Log("Не удалось прочитать сохранённые параметры контроллеров: " +
                    ex.GetType().Name + ".\r\n");
            }
        }

        private void MergeServiceDrafts()
        {
            LoadPersistedServiceDrafts();
            HashSet<string> existing = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < _session.Rows.Count; index++)
            {
                LmGatewayBindingSessionRow row = _session.Rows[index];
                if (row == null || row.Kkt == null)
                {
                    continue;
                }
                string serial = row.Kkt.KktSerial;
                existing.Add(serial);
                LmGatewayDraft defaults = LmGatewayDraftDefaults.Create(row.Kkt, index + 1);
                LmGatewayDraft draft;
                if (!_serviceDrafts.TryGetValue(serial, out draft))
                {
                    draft = defaults;
                    _serviceDrafts.Add(serial, draft);
                }
                else if (!string.Equals(
                    (draft.KktInn ?? string.Empty).Trim(),
                    (row.Kkt.KktInn ?? string.Empty).Trim(),
                    StringComparison.Ordinal))
                {
                    draft = defaults;
                    _serviceDrafts[serial] = draft;
                }
                else
                {
                    ApplyOrdinalDefaults(draft, defaults);
                }
                _session.TryUpdateDraft(
                    serial,
                    LmGatewayDraftDefaults.ControllerAddress,
                    draft.GrpcPort,
                    row.IsSelected);
            }
            List<string> stale = new List<string>();
            foreach (string serial in _serviceDrafts.Keys)
            {
                if (!existing.Contains(serial))
                {
                    stale.Add(serial);
                }
            }
            for (int index = 0; index < stale.Count; index++)
            {
                _serviceDrafts.Remove(stale[index]);
            }
            PersistServiceDrafts();
        }

        private static void ApplyOrdinalDefaults(
            LmGatewayDraft draft,
            LmGatewayDraft defaults)
        {
            bool legacyEmptyAddress = string.IsNullOrWhiteSpace(draft.TargetAddress);
            if (legacyEmptyAddress)
            {
                draft.TargetAddress = defaults.TargetAddress;
            }
            if (string.IsNullOrWhiteSpace(draft.TargetPort) ||
                (legacyEmptyAddress && string.Equals(
                    draft.TargetPort,
                    LmGatewayDraftDefaults.TargetLmPort,
                    StringComparison.Ordinal)))
            {
                draft.TargetPort = defaults.TargetPort;
            }
            if (string.IsNullOrWhiteSpace(draft.GrpcPort))
            {
                draft.GrpcPort = defaults.GrpcPort;
            }
            if (string.IsNullOrWhiteSpace(draft.RestPort))
            {
                draft.RestPort = defaults.RestPort;
            }
        }

        private async Task ProbeManagedServicesAsync(CancellationToken cancellation)
        {
            for (int index = 0; index < _serviceInventory.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                LmServiceInventoryItem item = _serviceInventory[index];
                if (item == null || item.Role != LmServiceRole.Managed ||
                    !item.IsRunning || item.Ports == null || item.Target == null)
                {
                    continue;
                }
                LmGatewayProbeResult probe = await _serviceProbe.ProbeAsync(
                    new ManagedLmServiceSpec(item.KktSerial, item.Ports, item.Target),
                    cancellation);
                item.IsReady = probe.IsReady;
                if (probe.IsReady)
                {
                    item.Message = "Служба и оба локальных порта готовы.";
                }
                else
                {
                    item.Status = LmServiceProvisioningStatus.RequiresAttention;
                    item.Message = string.IsNullOrWhiteSpace(probe.Message)
                        ? "Служба запущена, но готовность портов не подтверждена."
                        : probe.Message;
                }
            }
        }

        private bool SaveServiceDraft(string serial)
        {
            LmGatewayDraft draft = GetOrCreateServiceDraft(serial);
            LmGatewayBindingSessionRow row = FindSessionRow(serial);
            draft.KktInn = row == null || row.Kkt == null
                ? string.Empty
                : (row.Kkt.KktInn ?? string.Empty).Trim();
            draft.TargetAddress = (_targetAddressTextBox.Text ?? string.Empty).Trim();
            draft.TargetPort = (_targetPortTextBox.Text ?? string.Empty).Trim();
            draft.GrpcPort = (_portTextBox.Text ?? string.Empty).Trim();
            draft.RestPort = (_restPortTextBox.Text ?? string.Empty).Trim();
            return PersistServiceDrafts();
        }

        private bool PersistServiceDrafts()
        {
            if (_draftSettingsStore == null)
            {
                return false;
            }
            try
            {
                _draftSettingsStore.Save(new List<LmGatewayDraft>(_serviceDrafts.Values));
                return true;
            }
            catch (Exception ex)
            {
                if (!IsDraftSettingsException(ex))
                {
                    throw;
                }
                Log("Не удалось сохранить несекретные параметры контроллеров: " +
                    ex.GetType().Name + ".\r\n");
                return false;
            }
        }

        private static bool IsDraftSettingsException(Exception ex)
        {
            return ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is SerializationException ||
                ex is InvalidDataException ||
                ex is ArgumentException ||
                ex is NotSupportedException;
        }

        private void LoadServiceDraft(LmGatewayBindingSessionRow row)
        {
            bool hasRow = row != null && row.Kkt != null;
            LmGatewayDraft draft = hasRow ? GetOrCreateServiceDraft(row.Kkt.KktSerial) : null;
            _targetAddressTextBox.Text = draft == null ? string.Empty : draft.TargetAddress ?? string.Empty;
            _targetPortTextBox.Text = draft == null ? string.Empty : draft.TargetPort ?? string.Empty;
            _restPortTextBox.Text = draft == null ? string.Empty : draft.RestPort ?? string.Empty;
        }

        private async Task<bool> ExecuteServicePlanAsync(
            LmGatewayPlan plan,
            CancellationToken cancellation)
        {
            string operationId = Guid.NewGuid().ToString("N");
            string hash = ComputeEnsureHash(plan, operationId);
            LmGatewayLifecycleOutcome outcome;
            try
            {
                outcome = await _lifecycleWorkflow.ExecuteAsync(
                    ValidateAndReadBaseUrl(),
                    plan,
                    operationId,
                    hash,
                    GetCredentials,
                    ReportLifecycleProgress,
                    cancellation);
            }
            finally
            {
                ClearCredentialsForLifecyclePlan(plan);
            }
            ApplyLifecycleOutcome(outcome);
            bool completed = LmGatewayLifecycleWorkflow.IsFullyVerified(
                outcome,
                plan.Items.Count);
            await RefreshCoreAsync(CancellationToken.None);
            _statusLabel.Text = completed
                ? "Все выбранные ККТ настроены: службы контроллеров запущены, а каждая привязка подтверждена ЕСМ через /api/v2/info."
                : "Службы контроллеров обработаны, но не все привязки подтверждены ЕСМ. " +
                    "Статус «Запрос принят», недоступный read-back или ошибка TLS не считаются полным успехом. Проверьте строки и журнал.";
            return completed;
        }

        private async Task ConfirmAndRemoveServiceAsync()
        {
            LmServiceInventoryItem item = GetSelectedInventoryItem();
            if (item == null || item.Role != LmServiceRole.Managed)
            {
                return;
            }
            LmGatewayBindingSessionRow session = GetSelectedSessionRow();
            using (LmGatewayRemovalDialog dialog = new LmGatewayRemovalDialog(
                item,
                session == null || session.Kkt == null ? string.Empty : session.Kkt.KktInn))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Confirmation == null)
                {
                    return;
                }
                LmRemovalConfirmation confirmation = dialog.Confirmation;
                string operationId = Guid.NewGuid().ToString("N");
                string hash = ComputeRemovalHash(confirmation, operationId);
                await RunOperationAsync(
                    delegate(CancellationToken token)
                    {
                        return RemoveServiceAsync(item, confirmation, operationId, hash, token);
                    },
                    "Ожидание подтверждения UAC для удаления службы...");
            }
        }

        private async Task RemoveServiceAsync(
            LmServiceInventoryItem item,
            LmRemovalConfirmation confirmation,
            string operationId,
            string hash,
            CancellationToken cancellation)
        {
            LmGatewayLifecycleResult result = await _removalWorkflow.RemoveAsync(
                new[] { item }, confirmation, operationId, hash, cancellation);
            _statusLabel.Text = result.Details;
            Log(item.KktSerial + ": " + result.Details + "\r\n");
            if (result.Status == LmGatewayLifecycleStatus.RemovedLocalArtifactsBindingRetained)
            {
                MessageBox.Show(this, result.Details, "Локальный контроллер удалён",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            await RefreshCoreAsync(CancellationToken.None);
        }

        private async Task ConfirmAndRemoveAllServicesAsync()
        {
            IList<LmServiceInventoryItem> items = GetRemovableManagedItems(_serviceInventory);
            if (items.Count == 0)
            {
                MessageBox.Show(this,
                    "Управляемые службы, которые можно безопасно удалить, не найдены.",
                    "Полная очистка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using (LmGatewayRemoveAllDialog dialog = new LmGatewayRemoveAllDialog(items))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK ||
                    dialog.Confirmations == null)
                {
                    return;
                }

                IList<LmRemovalConfirmation> confirmations = dialog.Confirmations;
                IList<LmServiceInventoryItem> fresh;
                try
                {
                    fresh = _inventoryReader.Read();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "Не удалось повторно проверить службы перед удалением: " +
                            SensitiveDataMasker.Mask(ex.Message),
                        "Полная очистка",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                if (!RemovalBatchStillMatches(confirmations, fresh))
                {
                    RefreshServiceInventory();
                    FillRows(null);
                    MessageBox.Show(this,
                        "Состав или состояние служб изменились после подтверждения. " +
                            "Список обновлён; проверьте его и подтвердите удаление заново.",
                        "Полная очистка",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string operationId = Guid.NewGuid().ToString("N");
                string hash = ComputeRemoveAllHash(confirmations, operationId);
                await RunOperationAsync(
                    delegate(CancellationToken token)
                    {
                        return RemoveAllServicesAsync(
                            confirmations,
                            operationId,
                            hash,
                            token);
                    },
                    "Ожидание одного подтверждения UAC для удаления всех созданных служб...");
            }
        }

        private async Task RemoveAllServicesAsync(
            IList<LmRemovalConfirmation> confirmations,
            string operationId,
            string hash,
            CancellationToken cancellation)
        {
            LmServiceProvisioningBatchResult result = await _serviceProvisioner.RemoveAllAsync(
                confirmations,
                operationId,
                hash,
                cancellation);
            int removed = 0;
            int pending = 0;
            for (int index = 0; index < result.Items.Count; index++)
            {
                LmServiceProvisioningItemResult item = result.Items[index];
                if (item.Status ==
                    LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained)
                {
                    removed++;
                }
                else
                {
                    pending++;
                }
                Log(item.KktSerial + ": " + item.Status.ToString() + ". " +
                    (item.Message ?? string.Empty) + "\r\n");
            }

            RefreshServiceInventory();
            FillRows(null);
            string summary = "Удалено управляемых служб и их локальных данных: " +
                removed.ToString(CultureInfo.InvariantCulture) + ".";
            if (pending > 0)
            {
                summary += " Требуют внимания: " +
                    pending.ToString(CultureInfo.InvariantCulture) +
                    ". Подробности показаны в таблице и журнале.";
            }
            else
            {
                summary += " Штатная служба не изменена. Привязки в ЕСМ не очищались; " +
                    "после перезагрузки можно повторить автоматическую настройку.";
            }
            _statusLabel.Text = summary;
            Log(summary + "\r\n\r\n");
            MessageBox.Show(this,
                summary,
                pending == 0 ? "Полная очистка завершена" : "Полная очистка завершена частично",
                MessageBoxButtons.OK,
                pending == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private static IList<LmServiceInventoryItem> GetRemovableManagedItems(
            IList<LmServiceInventoryItem> inventory)
        {
            List<LmServiceInventoryItem> items = new List<LmServiceInventoryItem>();
            if (inventory != null)
            {
                for (int index = 0; index < inventory.Count; index++)
                {
                    LmServiceInventoryItem item = inventory[index];
                    if (item != null && item.Role == LmServiceRole.Managed &&
                        item.Ports != null && item.ManifestFingerprint != null &&
                        CanonicalLmPlanHasher.FixedTimeEqualsHex(
                            item.ManifestFingerprint.Sha256,
                            item.ManifestFingerprint.Sha256) &&
                        item.Ports.GrpcPort > 0 && item.Ports.GrpcPort <= 65535 &&
                        item.Ports.RestPort > 0 && item.Ports.RestPort <= 65535 &&
                        item.Ports.GrpcPort != item.Ports.RestPort)
                    {
                        items.Add(item);
                    }
                }
            }
            items.Sort(delegate(LmServiceInventoryItem left, LmServiceInventoryItem right)
            {
                return string.CompareOrdinal(left.KktSerial, right.KktSerial);
            });
            return items;
        }

        private static bool RemovalBatchStillMatches(
            IList<LmRemovalConfirmation> confirmations,
            IList<LmServiceInventoryItem> inventory)
        {
            if (confirmations == null)
            {
                return false;
            }
            IList<LmServiceInventoryItem> fresh = GetRemovableManagedItems(inventory);
            if (fresh.Count != confirmations.Count)
            {
                return false;
            }
            for (int index = 0; index < confirmations.Count; index++)
            {
                LmRemovalConfirmation confirmation = confirmations[index];
                LmServiceInventoryItem item = fresh[index];
                if (confirmation == null ||
                    !string.Equals(confirmation.KktSerial, item.KktSerial, StringComparison.Ordinal) ||
                    confirmation.GrpcPort != item.Ports.GrpcPort ||
                    confirmation.RestPort != item.Ports.RestPort ||
                    confirmation.ManifestFingerprint == null ||
                    !CanonicalLmPlanHasher.FixedTimeEqualsHex(
                        confirmation.ManifestFingerprint.Sha256,
                        item.ManifestFingerprint.Sha256))
                {
                    return false;
                }
            }
            return true;
        }

        private Task ConfirmAndCleanupServiceAsync()
        {
            LmServiceInventoryItem item = GetSelectedInventoryItem();
            if (item == null || item.Role != LmServiceRole.Managed ||
                item.Status != LmServiceProvisioningStatus.CleanupPending)
            {
                return Task.FromResult(0);
            }
            if (MessageBox.Show(this,
                "Повторить автоматическую очистку локального профиля и метаданных ККТ " +
                    item.KktSerial + "?",
                "Повторить очистку",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return Task.FromResult(0);
            }
            LmCleanupConfirmation confirmation = new LmCleanupConfirmation
            {
                KktSerial = item.KktSerial,
                ManifestFingerprint = item.ManifestFingerprint,
                DisplayedState = LmServiceProvisioningStatus.CleanupPending
            };
            string operationId = Guid.NewGuid().ToString("N");
            string hash = ComputeCleanupHash(confirmation, operationId);
            return RunOperationAsync(
                delegate(CancellationToken token)
                {
                    return CleanupServiceAsync(item, confirmation, operationId, hash, token);
                },
                "Ожидание подтверждения UAC для очистки...");
        }

        private async Task CleanupServiceAsync(
            LmServiceInventoryItem item,
            LmCleanupConfirmation confirmation,
            string operationId,
            string hash,
            CancellationToken cancellation)
        {
            LmGatewayLifecycleResult result = await _removalWorkflow.CleanupAsync(
                new[] { item }, confirmation, operationId, hash, cancellation);
            _statusLabel.Text = result.Details;
            Log(item.KktSerial + ": " + result.Details + "\r\n");
            await RefreshCoreAsync(CancellationToken.None);
        }

        private LmGatewayPlan BuildServicePlan()
        {
            LmGatewayDiscovery discovery = new LmGatewayDiscovery();
            List<LmGatewayDraft> drafts = new List<LmGatewayDraft>();
            for (int index = 0; index < _session.Rows.Count; index++)
            {
                LmGatewayBindingSessionRow row = _session.Rows[index];
                if (row == null || row.Kkt == null || !row.IsSelected)
                {
                    continue;
                }
                discovery.Items.Add(row.Kkt);
                drafts.Add(GetOrCreateServiceDraft(row.Kkt.KktSerial));
            }
            return LmGatewayPlanner.Build(
                discovery,
                drafts,
                _serviceInventory,
                new LmManagedPortPolicy(
                    new TcpPortRange(
                        LmGatewayDraftDefaults.GrpcPortFirst,
                        LmGatewayDraftDefaults.GrpcPortLast),
                    new TcpPortRange(
                        LmGatewayDraftDefaults.RestPortFirst,
                        LmGatewayDraftDefaults.RestPortLast)),
                ReadListenerSnapshot());
        }

        private IList<TcpListenerSnapshotItem> ReadListenerSnapshot()
        {
            List<int> occupiedPorts = new List<int>();
            AddOccupiedPorts(
                occupiedPorts,
                LmGatewayDraftDefaults.GrpcPortFirst,
                LmGatewayDraftDefaults.GrpcPortLast);
            AddOccupiedPorts(
                occupiedPorts,
                LmGatewayDraftDefaults.RestPortFirst,
                LmGatewayDraftDefaults.RestPortLast);
            return LmTcpListenerSnapshotBuilder.Build(occupiedPorts, _serviceInventory);
        }

        private void AddOccupiedPorts(ICollection<int> target, int first, int last)
        {
            IList<int> ports = _listenerReader.FindPorts(first, last);
            for (int index = 0; index < ports.Count; index++)
            {
                target.Add(ports[index]);
            }
        }

        private void ApplyLifecycleOutcome(LmGatewayLifecycleOutcome outcome)
        {
            LmGatewayBindingOutcome binding = new LmGatewayBindingOutcome();
            for (int index = 0; index < outcome.Results.Count; index++)
            {
                LmGatewayLifecycleResult item = outcome.Results[index];
                Log(item.KktSerial + ": " + item.Status + ". " + (item.Details ?? string.Empty) + "\r\n");
                if (item.BindingAttempted)
                {
                    binding.Results.Add(new LmGatewayBindingResult
                    {
                        InstanceId = item.InstanceId,
                        KktSerial = item.KktSerial,
                        KktInn = item.KktInn,
                        Status = item.BindingStatus,
                        Details = item.Details
                    });
                }
            }
            _session.ApplyOutcome(binding);
            _statusLabel.Text = outcome.Cancelled
                ? "Операция остановлена после безопасной границы. Уже выполненные действия сохранены."
                : "Операция контроллеров ЛМ завершена. Подробности записаны в журнал.";
        }

        private void ReportLifecycleProgress(LmGatewayLifecycleProgress progress)
        {
            if (progress == null)
            {
                return;
            }
            PostToUi(delegate
            {
                _statusLabel.Text = progress.Stage + " " + progress.Current + "/" +
                    progress.Total + ": ККТ " + progress.KktSerial + ". " + progress.Message;
            });
        }

        private void UpdateServiceActionState(bool idle, bool hasRow, bool hasSelected)
        {
            LmServiceInventoryItem selected = GetSelectedInventoryItem();
            bool managed = selected != null && selected.Role == LmServiceRole.Managed;
            _selectInstallerButton.Enabled = idle;
            _installControllerButton.Enabled = idle && _installerSelection != null &&
                _helperAvailable && hasSelected;
            _removeServiceButton.Enabled = idle && managed && _helperAvailable &&
                selected.Status != LmServiceProvisioningStatus.CleanupPending;
            _removeAllServicesButton.Enabled = idle && _helperAvailable &&
                GetRemovableManagedItems(_serviceInventory).Count > 0;
            _cleanupButton.Enabled = idle && managed && _helperAvailable &&
                selected.Status == LmServiceProvisioningStatus.CleanupPending;
            _targetAddressTextBox.Enabled = idle && hasRow;
            _targetPortTextBox.Enabled = idle && hasRow;
            _restPortTextBox.Enabled = idle && hasRow;
            string setupReason = !_helperAvailable
                ? _helperUnavailableReason
                : _installerSelection == null
                    ? "Выберите установщик контроллера ЛМ ЧЗ."
                    : !hasSelected
                        ? "В таблице нет выбранных ККТ."
                        : "Установить версию, создать службы и выполнить доступные привязки.";
            _serviceToolTip.SetToolTip(_installControllerButton, setupReason);
            _serviceToolTip.SetToolTip(_removeServiceButton,
                _helperAvailable ? "Удалить одну выбранную управляемую службу." : _helperUnavailableReason);
            _serviceToolTip.SetToolTip(_removeAllServicesButton,
                _helperAvailable
                    ? "Удалить все службы контроллеров ЛМ, созданные этой программой, и их локальные данные."
                    : _helperUnavailableReason);
        }

        private LmServiceInventoryItem GetSelectedInventoryItem()
        {
            if (_grid.SelectedRows.Count != 1)
            {
                return null;
            }
            LmGatewayGridRow row = _grid.SelectedRows[0].Tag as LmGatewayGridRow;
            return row == null ? null : row.Inventory;
        }

        private LmServiceInventoryItem FindInventory(string serial)
        {
            for (int index = 0; index < _serviceInventory.Count; index++)
            {
                LmServiceInventoryItem item = _serviceInventory[index];
                if (item != null && string.Equals(item.KktSerial, serial, StringComparison.Ordinal))
                {
                    return item;
                }
            }
            return null;
        }

        private LmGatewayBindingSessionRow FindSessionRow(string serial)
        {
            for (int index = 0; index < _session.Rows.Count; index++)
            {
                LmGatewayBindingSessionRow row = _session.Rows[index];
                if (row != null && row.Kkt != null && string.Equals(
                    row.Kkt.KktSerial,
                    serial,
                    StringComparison.Ordinal))
                {
                    return row;
                }
            }
            return null;
        }

        private LmGatewayDraft GetOrCreateServiceDraft(string serial)
        {
            LmGatewayDraft draft;
            if (!_serviceDrafts.TryGetValue(serial, out draft))
            {
                LmGatewayBindingSessionRow row = FindSessionRow(serial);
                int ordinal = FindSessionOrdinal(serial);
                draft = LmGatewayDraftDefaults.Create(
                    row == null ? null : row.Kkt,
                    ordinal < 1 ? 1 : ordinal);
                if (string.IsNullOrEmpty(draft.KktSerial))
                {
                    draft.KktSerial = (serial ?? string.Empty).Trim();
                }
                _serviceDrafts.Add(serial, draft);
            }
            return draft;
        }

        private int FindSessionOrdinal(string serial)
        {
            for (int index = 0; index < _session.Rows.Count; index++)
            {
                LmGatewayBindingSessionRow row = _session.Rows[index];
                if (row != null && row.Kkt != null && string.Equals(
                    row.Kkt.KktSerial,
                    serial,
                    StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }
            return 0;
        }

        private void ClearCredentialsForLifecyclePlan(LmGatewayPlan plan)
        {
            for (int index = 0; index < plan.Items.Count; index++)
            {
                if (plan.Items[index] != null && plan.Items[index].Kkt != null)
                {
                    ClearCredential(plan.Items[index].Kkt.KktSerial);
                }
            }
        }

        private void ClearInstallerSelection()
        {
            if (_installerSelection != null)
            {
                _installerSelection.SourcePath = string.Empty;
            }
            _installerSelection = null;
            _installerPathTextBox.Clear();
            _serviceToolTip.SetToolTip(_installerPathTextBox, string.Empty);
            if (!IsDisposed)
            {
                _installerStatusLabel.Text = "Установщик не выбран.";
            }
            RaiseInstallerSelectionChanged();
        }

        private void RaiseInstallerSelectionChanged()
        {
            Action handler = InstallerSelectionChanged;
            if (handler != null && !IsDisposed && !Disposing)
            {
                handler();
            }
        }

        private static string ComputeEnsureHash(LmGatewayPlan plan, string operationId)
        {
            LmServiceProvisioningBatchRequest request = new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 1,
                Operation = LmServiceOperation.EnsureBatch,
                OperationId = operationId
            };
            for (int index = 0; index < plan.Items.Count; index++)
            {
                LmGatewayPlanItem item = plan.Items[index];
                if (item == null || !item.IsValid || item.Spec == null ||
                    (item.Action != LmGatewayPlanAction.CreateManagedService &&
                     item.Action != LmGatewayPlanAction.UpdateManagedService &&
                     item.Action != LmGatewayPlanAction.StartManagedService))
                {
                    continue;
                }
                request.Items.Add(new LmServiceProvisioningItemRequest
                {
                    KktSerial = item.Spec.KktSerial,
                    GrpcPort = item.Spec.Ports.GrpcPort,
                    RestPort = item.Spec.Ports.RestPort,
                    TargetAddress = item.Spec.Target.Address,
                    TargetPort = item.Spec.Target.Port
                });
            }
            return CanonicalLmPlanHasher.Compute(request);
        }

        private static string ComputeRemovalHash(
            LmRemovalConfirmation confirmation,
            string operationId)
        {
            return CanonicalLmPlanHasher.Compute(new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 1,
                Operation = LmServiceOperation.RemoveManaged,
                OperationId = operationId,
                RemovalConfirmation = confirmation
            });
        }

        private static string ComputeCleanupHash(
            LmCleanupConfirmation confirmation,
            string operationId)
        {
            return CanonicalLmPlanHasher.Compute(new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 1,
                Operation = LmServiceOperation.CleanupManaged,
                OperationId = operationId,
                CleanupConfirmation = confirmation
            });
        }

        private static string GetServiceStatusText(LmServiceInventoryItem item)
        {
            if (item == null) return "Отсутствует";
            if (item.Status == LmServiceProvisioningStatus.CleanupPending) return "Требуется очистка";
            if (item.Status == LmServiceProvisioningStatus.VersionVerificationPending) return "Нужна сверка версии";
            if (item.Status == LmServiceProvisioningStatus.RequiresAttention) return "Требуется внимание";
            return item.IsRunning ? "Запущена" : "Остановлена";
        }

        private static string GetUserStatusText(
            LmGatewayBindingSessionRow session,
            LmServiceInventoryItem inventory)
        {
            if (session != null && session.LastBindingStatus.HasValue)
            {
                LmGatewayBindingStatus status = session.LastBindingStatus.Value;
                if (status == LmGatewayBindingStatus.BindingVerified)
                {
                    return "Готово";
                }
                if (status == LmGatewayBindingStatus.BindingAccepted ||
                    status == LmGatewayBindingStatus.BindingObserved)
                {
                    return "Настроено";
                }
                if (status == LmGatewayBindingStatus.Cancelled)
                {
                    return "Остановлено";
                }
                return "Требуется внимание";
            }
            if (inventory == null)
            {
                return "Не настроено";
            }
            if (inventory.Status == LmServiceProvisioningStatus.CleanupPending ||
                inventory.Status == LmServiceProvisioningStatus.RequiresAttention ||
                inventory.Status == LmServiceProvisioningStatus.VersionVerificationPending)
            {
                return "Требуется внимание";
            }
            return inventory.IsRunning ? "Контроллер запущен" : "Контроллер остановлен";
        }

        private static string GetUserResultText(
            LmGatewayBindingSessionRow session,
            LmServiceInventoryItem inventory)
        {
            if (session != null && !string.IsNullOrWhiteSpace(session.LastMessage))
            {
                return session.LastMessage;
            }
            if (session != null && session.LastBindingStatus.HasValue)
            {
                return GetBindingStatusText(session.LastBindingStatus.Value);
            }
            if (inventory == null)
            {
                return "Готово к настройке";
            }
            if (inventory.Status == LmServiceProvisioningStatus.CleanupPending)
            {
                return "Завершите очистку";
            }
            if (inventory.Status == LmServiceProvisioningStatus.VersionVerificationPending)
            {
                return "Проверьте версию контроллера";
            }
            if (inventory.Status == LmServiceProvisioningStatus.RequiresAttention &&
                !string.IsNullOrWhiteSpace(inventory.Message))
            {
                return inventory.Message;
            }
            return inventory.IsRunning ? "Ожидает проверки ЕСМ" : "Готово к настройке";
        }

        private static string ComputeRemoveAllHash(
            IList<LmRemovalConfirmation> confirmations,
            string operationId)
        {
            LmServiceProvisioningBatchRequest request =
                new LmServiceProvisioningBatchRequest
                {
                    SchemaVersion = 1,
                    Operation = LmServiceOperation.RemoveAllManaged,
                    OperationId = operationId
                };
            if (confirmations != null)
            {
                for (int index = 0; index < confirmations.Count; index++)
                {
                    request.RemovalConfirmations.Add(confirmations[index]);
                }
            }
            return CanonicalLmPlanHasher.Compute(request);
        }

        private static string GetTargetAddressText(
            LmGatewayBindingSessionRow session,
            LmGatewayDraft draft,
            LmServiceInventoryItem inventory)
        {
            if (session != null && !string.IsNullOrWhiteSpace(session.ObservedLmAddress) &&
                !string.IsNullOrWhiteSpace(session.ObservedLmPort))
            {
                return session.ObservedLmAddress;
            }
            if (draft != null && !string.IsNullOrWhiteSpace(draft.TargetAddress))
            {
                return draft.TargetAddress;
            }
            return inventory == null || inventory.Target == null
                ? string.Empty
                : inventory.Target.Address;
        }

        private static string GetTargetPortText(
            LmGatewayBindingSessionRow session,
            LmGatewayDraft draft,
            LmServiceInventoryItem inventory)
        {
            if (session != null && !string.IsNullOrWhiteSpace(session.ObservedLmAddress) &&
                !string.IsNullOrWhiteSpace(session.ObservedLmPort))
            {
                return session.ObservedLmPort;
            }
            if (draft != null && !string.IsNullOrWhiteSpace(draft.TargetPort))
            {
                return draft.TargetPort;
            }
            return inventory == null || inventory.Target == null
                ? string.Empty
                : inventory.Target.Port.ToString();
        }

        private LmGatewayTarget GetExpectedLmTarget(LmGatewayKkt kkt)
        {
            if (kkt == null)
            {
                return null;
            }

            LmGatewayDraft draft = GetOrCreateServiceDraft(kkt.KktSerial);
            int port;
            if (draft == null || string.IsNullOrWhiteSpace(draft.TargetAddress) ||
                !int.TryParse(draft.TargetPort, out port) || port < 1 || port > 65535)
            {
                return null;
            }

            return new LmGatewayTarget(draft.TargetAddress, port);
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
                    item.Input.ExpectedLmPort = target.Port.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private sealed class LmGatewayGridRow
        {
            internal LmGatewayGridRow(
                LmGatewayBindingSessionRow sessionRow,
                LmServiceInventoryItem inventory)
            {
                SessionRow = sessionRow;
                Inventory = inventory;
            }

            internal LmGatewayBindingSessionRow SessionRow { get; private set; }
            internal LmServiceInventoryItem Inventory { get; private set; }
        }
    }
}
