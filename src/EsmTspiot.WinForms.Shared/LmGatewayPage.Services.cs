using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
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
        private readonly Button _preparePlanButton = new Button();
        private readonly Button _executePlanButton = new Button();
        private readonly Button _removeServiceButton = new Button();
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
        private LmServiceProvisionerClient _serviceProvisioner;
        private LmServiceInventoryReader _inventoryReader;
        private LmGatewayProbe _serviceProbe;
        private ReadOnlyTcpListenerOwnerReader _listenerReader;
        private LmGatewayLifecycleWorkflow _lifecycleWorkflow;
        private LmGatewayRemovalWorkflow _removalWorkflow;
        private LmControllerInstallerSelection _installerSelection;
        private LmGatewayDiscovery _currentDiscovery;
        private LmGatewayPlan _preparedPlan;
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
            _lifecycleWorkflow = new LmGatewayLifecycleWorkflow(
                _serviceProvisioner,
                _serviceProbe,
                _bindingWorkflow);
            _removalWorkflow = new LmGatewayRemovalWorkflow(
                _serviceProvisioner,
                delegate { return _inventoryReader.Read(); });
            _helperAvailable = _serviceProvisioner.IsAvailable(out _helperUnavailableReason);
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
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _installerPathTextBox.Dock = DockStyle.Fill;
            _installerPathTextBox.ReadOnly = true;
            _installerPathTextBox.Tag = "Выберите esm-lm-controller_*-windows-setup.exe";
            ConfigureButton(_selectInstallerButton, "Выбрать…");
            ConfigureButton(_installControllerButton, "Установить / проверить версию");
            _selectInstallerButton.Click += delegate { SelectInstaller(); };
            _installControllerButton.Click += async delegate { await ConfirmAndInstallControllerAsync(); };
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

        private void SelectInstaller()
        {
            ClearInstallerSelection();
            try
            {
                _installerSelection = LmControllerInstallerPicker.SelectAndInspect(this);
                if (_installerSelection == null)
                {
                    _installerStatusLabel.Text = "Выбор отменён; путь к файлу очищен.";
                    UpdateActionState();
                    return;
                }
                _installerPathTextBox.Text = _installerSelection.SourcePath;
                _installerStatusLabel.Text = _installerSelection.FileName +
                    " | версия " + _installerSelection.FileVersion +
                    " | подписант " + ShortSigner(_installerSelection.SignerSubject) +
                    " | SHA-256 " + ShortHash(_installerSelection.Sha256);
            }
            catch (Exception ex)
            {
                ClearInstallerSelection();
                MessageBox.Show(this, SensitiveDataMasker.Mask(ex.Message),
                    "Проверка установщика", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            UpdateActionState();
        }

        private Task ConfirmAndInstallControllerAsync()
        {
            if (_installerSelection == null)
            {
                return Task.FromResult(0);
            }
            int managedCount = 0;
            for (int index = 0; index < _serviceInventory.Count; index++)
            {
                if (_serviceInventory[index] != null &&
                    _serviceInventory[index].Role == LmServiceRole.Managed)
                {
                    managedCount++;
                }
            }
            string confirmation =
                "Файл: " + _installerSelection.FileName + "\r\n" +
                "Версия: " + _installerSelection.FileVersion + "\r\n" +
                "SHA-256: " + _installerSelection.Sha256 + "\r\n\r\n" +
                "На время установки будут остановлены управляемые службы: " + managedCount +
                ". После установки они останутся остановленными до явного создания/обновления.\r\n\r\nПродолжить?";
            if (MessageBox.Show(this, confirmation, "Установка контроллера ЛМ",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                ClearInstallerSelection();
                UpdateActionState();
                return Task.FromResult(0);
            }
            return RunOperationAsync(InstallControllerAsync, "Ожидание подтверждения UAC...");
        }

        private async Task InstallControllerAsync(CancellationToken cancellation)
        {
            LmControllerInstallerSelection selection = _installerSelection;
            if (selection == null)
            {
                return;
            }
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
            try
            {
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
            }
            finally
            {
                ClearInstallerSelection();
            }
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
                    !(ex is Win32Exception) && !(ex is CryptographicException))
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

        private void MergeServiceDrafts()
        {
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
                LmServiceInventoryItem inventory = FindInventory(serial);
                LmGatewayDraft draft;
                if (!_serviceDrafts.TryGetValue(serial, out draft))
                {
                    draft = new LmGatewayDraft { KktSerial = serial };
                    _serviceDrafts.Add(serial, draft);
                }
                if (inventory != null)
                {
                    if (inventory.Target != null && string.IsNullOrWhiteSpace(draft.TargetAddress))
                    {
                        draft.TargetAddress = inventory.Target.Address;
                        draft.TargetPort = inventory.Target.Port.ToString(CultureInfo.InvariantCulture);
                    }
                    if (inventory.Ports != null)
                    {
                        if (string.IsNullOrWhiteSpace(draft.GrpcPort))
                        {
                            draft.GrpcPort = inventory.Ports.GrpcPort.ToString(CultureInfo.InvariantCulture);
                        }
                        if (string.IsNullOrWhiteSpace(draft.RestPort))
                        {
                            draft.RestPort = inventory.Ports.RestPort.ToString(CultureInfo.InvariantCulture);
                        }
                        if (string.IsNullOrWhiteSpace(row.ControllerGrpcPort))
                        {
                            _session.TryUpdateDraft(serial, "127.0.0.1", draft.GrpcPort, row.IsSelected);
                        }
                    }
                }
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
            _preparedPlan = null;
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

        private void SaveServiceDraft(string serial)
        {
            LmGatewayDraft draft = GetOrCreateServiceDraft(serial);
            draft.TargetAddress = (_targetAddressTextBox.Text ?? string.Empty).Trim();
            draft.TargetPort = (_targetPortTextBox.Text ?? string.Empty).Trim();
            draft.GrpcPort = (_portTextBox.Text ?? string.Empty).Trim();
            draft.RestPort = (_restPortTextBox.Text ?? string.Empty).Trim();
            _preparedPlan = null;
        }

        private void LoadServiceDraft(LmGatewayBindingSessionRow row)
        {
            bool hasRow = row != null && row.Kkt != null;
            LmGatewayDraft draft = hasRow ? GetOrCreateServiceDraft(row.Kkt.KktSerial) : null;
            _targetAddressTextBox.Text = draft == null ? string.Empty : draft.TargetAddress ?? string.Empty;
            _targetPortTextBox.Text = draft == null ? string.Empty : draft.TargetPort ?? string.Empty;
            _restPortTextBox.Text = draft == null ? string.Empty : draft.RestPort ?? string.Empty;
        }

        private void PrepareServicePlan()
        {
            SaveEditorWithoutChangingSelection();
            _preparedPlan = BuildServicePlan();
            if (_preparedPlan.Items.Count == 0)
            {
                MessageBox.Show(this, "Выберите хотя бы одну ККТ.", "План контроллеров ЛМ",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int valid = 0;
            for (int index = 0; index < _preparedPlan.Items.Count; index++)
            {
                if (_preparedPlan.Items[index] != null && _preparedPlan.Items[index].IsValid)
                {
                    valid++;
                }
            }
            _statusLabel.Text = "План подготовлен: строк " + _preparedPlan.Items.Count +
                ", готово " + valid + ". Нажмите «Создать / обновить выбранные» для подтверждения.";
            UpdateActionState();
        }

        private Task ConfirmAndExecuteServicePlanAsync()
        {
            SaveEditorWithoutChangingSelection();
            _preparedPlan = BuildServicePlan();
            if (_preparedPlan.Items.Count == 0)
            {
                PrepareServicePlan();
                return Task.FromResult(0);
            }
            using (LmGatewayPlanDialog dialog = new LmGatewayPlanDialog(
                _preparedPlan,
                HasCompleteCredentials))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedPlan == null)
                {
                    return Task.FromResult(0);
                }
                LmGatewayPlan selected = dialog.SelectedPlan;
                bool mutation = HasServiceMutation(selected);
                if (mutation && (!_controllerVersionVerified || !_helperAvailable))
                {
                    string reason = !_controllerVersionVerified
                        ? "Сначала выберите и установите поддерживаемую версию контроллера ЛМ."
                        : _helperUnavailableReason;
                    MessageBox.Show(this, reason, "Изменение служб недоступно",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return Task.FromResult(0);
                }
                return RunOperationAsync(
                    delegate(CancellationToken token) { return ExecuteServicePlanAsync(selected, token); },
                    mutation ? "Ожидание подтверждения UAC..." : "Проверка и привязка контроллеров...");
            }
        }

        private async Task ExecuteServicePlanAsync(
            LmGatewayPlan plan,
            CancellationToken cancellation)
        {
            string operationId = Guid.NewGuid().ToString("N");
            string hash = ComputeEnsureHash(plan, operationId);
            LmGatewayLifecycleOutcome outcome = await _lifecycleWorkflow.ExecuteAsync(
                ValidateAndReadBaseUrl(),
                plan,
                operationId,
                hash,
                GetCredentials,
                ReportLifecycleProgress,
                cancellation);
            ApplyLifecycleOutcome(outcome);
            ClearCredentialsForLifecyclePlan(plan);
            _preparedPlan = null;
            await RefreshCoreAsync(CancellationToken.None);
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
                    new TcpPortRange(55000, 55031),
                    new TcpPortRange(15000, 15031)),
                ReadListenerSnapshot());
        }

        private IList<TcpListenerSnapshotItem> ReadListenerSnapshot()
        {
            List<TcpListenerSnapshotItem> result = new List<TcpListenerSnapshotItem>();
            AddListeners(result, 55000, 55031);
            AddListeners(result, 15000, 15031);
            return result;
        }

        private void AddListeners(ICollection<TcpListenerSnapshotItem> target, int first, int last)
        {
            IList<int> ports = _listenerReader.FindPorts(first, last);
            for (int index = 0; index < ports.Count; index++)
            {
                target.Add(new TcpListenerSnapshotItem(ports[index], string.Empty, false));
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
            _installControllerButton.Enabled = idle && _installerSelection != null && _helperAvailable;
            _preparePlanButton.Enabled = idle && hasSelected;
            _executePlanButton.Enabled = idle && hasSelected && _helperAvailable &&
                _controllerVersionVerified;
            _removeServiceButton.Enabled = idle && managed && _helperAvailable &&
                selected.Status != LmServiceProvisioningStatus.CleanupPending;
            _cleanupButton.Enabled = idle && managed && _helperAvailable &&
                selected.Status == LmServiceProvisioningStatus.CleanupPending;
            _targetAddressTextBox.Enabled = idle && hasRow;
            _targetPortTextBox.Enabled = idle && hasRow;
            _restPortTextBox.Enabled = idle && hasRow;
            _executePlanButton.Text = "Создать / обновить выбранные";
            string mutationReason = !_helperAvailable
                ? _helperUnavailableReason
                : !_controllerVersionVerified
                    ? "Сначала установите или подтвердите поддерживаемую версию контроллера ЛМ."
                    : "Создать или обновить выбранные управляемые службы.";
            _serviceToolTip.SetToolTip(_executePlanButton, mutationReason);
            _serviceToolTip.SetToolTip(_removeServiceButton,
                _helperAvailable ? "Удалить одну выбранную управляемую службу." : _helperUnavailableReason);
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
                draft = new LmGatewayDraft { KktSerial = serial };
                _serviceDrafts.Add(serial, draft);
            }
            return draft;
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
            if (!IsDisposed)
            {
                _installerStatusLabel.Text = "Установщик не выбран.";
            }
        }

        private static bool HasServiceMutation(LmGatewayPlan plan)
        {
            for (int index = 0; index < plan.Items.Count; index++)
            {
                LmGatewayPlanAction action = plan.Items[index] == null
                    ? LmGatewayPlanAction.Blocked
                    : plan.Items[index].Action;
                if (action == LmGatewayPlanAction.CreateManagedService ||
                    action == LmGatewayPlanAction.UpdateManagedService ||
                    action == LmGatewayPlanAction.StartManagedService)
                {
                    return true;
                }
            }
            return false;
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

        private static string GetRoleText(LmServiceInventoryItem item)
        {
            if (item == null) return "Не создан";
            if (item.Role == LmServiceRole.Managed) return "Управляемая";
            if (item.Role == LmServiceRole.VerifiedOfficial) return "Штатная";
            if (item.Role == LmServiceRole.Foreign) return "Чужая";
            return "Неизвестная";
        }

        private static string GetServiceStatusText(LmServiceInventoryItem item)
        {
            if (item == null) return "Отсутствует";
            if (item.Status == LmServiceProvisioningStatus.CleanupPending) return "Требуется очистка";
            if (item.Status == LmServiceProvisioningStatus.VersionVerificationPending) return "Нужна сверка версии";
            if (item.Status == LmServiceProvisioningStatus.RequiresAttention) return "Требуется внимание";
            return item.IsRunning ? "Запущена" : "Остановлена";
        }

        private static string GetPortText(string draft, int observed)
        {
            return !string.IsNullOrWhiteSpace(draft)
                ? draft
                : observed > 0 ? observed.ToString(CultureInfo.InvariantCulture) : "авто";
        }

        private static string GetTargetText(LmGatewayDraft draft, LmServiceInventoryItem inventory)
        {
            if (draft != null && !string.IsNullOrWhiteSpace(draft.TargetAddress))
            {
                return draft.TargetAddress + ":" + (draft.TargetPort ?? string.Empty);
            }
            return inventory == null || inventory.Target == null
                ? string.Empty
                : inventory.Target.Address + ":" + inventory.Target.Port;
        }

        private static string GetRowMessage(
            LmGatewayBindingSessionRow session,
            LmServiceInventoryItem inventory)
        {
            string service = inventory == null ? string.Empty : inventory.Message ?? string.Empty;
            string binding = session == null ? string.Empty : session.LastMessage ?? string.Empty;
            return service.Length == 0 ? binding : binding.Length == 0 ? service : service + " " + binding;
        }

        private static string ShortHash(string value)
        {
            return string.IsNullOrEmpty(value) || value.Length <= 16
                ? value ?? string.Empty
                : value.Substring(0, 8) + "…" + value.Substring(value.Length - 8);
        }

        private static string ShortSigner(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "нет подписи";
            string[] parts = value.Split(',');
            for (int index = 0; index < parts.Length; index++)
            {
                if (parts[index].TrimStart().StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                {
                    return parts[index].Trim();
                }
            }
            return value;
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
