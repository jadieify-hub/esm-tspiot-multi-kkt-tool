using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class LmGatewayPage : UserControl
    {
        private readonly Func<string> _baseUrlProvider;
        private readonly Action<string> _logger;
        private readonly LmGatewayDiscoveryWorkflow _discoveryWorkflow;
        private readonly LmGatewayReadbackWorkflow _readbackWorkflow;
        private readonly LmGatewayBindingSession _session = new LmGatewayBindingSession();
        private readonly DataGridView _grid = new DataGridView();
        private readonly Button _refreshButton = new Button();
        private readonly Button _cancelButton = new Button();
        private readonly Label _statusLabel = new Label();
        private readonly Label _selectionActionHintLabel = new Label();
        private readonly Label _officialControllerStatusLabel = new Label();
        private CancellationTokenSource _cancellation;
        private bool _running;
        private bool _hostBusy;
        private bool _loadingGrid;
        private bool _hasLoaded;
        private bool _fmuApiMode;
        private const string FmuControllerNotApplicable =
            "FMU-API: контроллеры и привязка к ЕСМ не применяются.";
        public LmGatewayPage(
            Func<string> baseUrlProvider,
            ITspiotApiClient apiClient,
            Action<string> logger)
        {
            if (baseUrlProvider == null)
            {
                throw new ArgumentNullException("baseUrlProvider");
            }
            if (apiClient == null)
            {
                throw new ArgumentNullException("apiClient");
            }

            _baseUrlProvider = baseUrlProvider;
            _logger = logger;
            _discoveryWorkflow = new LmGatewayDiscoveryWorkflow(apiClient);
            _bindingWorkflow = new LmGatewayBindingWorkflow(apiClient);
            _readbackWorkflow = new LmGatewayReadbackWorkflow(apiClient);
            InitializeServiceFeatures();

            Dock = DockStyle.Fill;
            AutoScroll = false;
            BuildLayout();
            RestoreInstallerSelection();
            FillRows(null);
            UpdateActionState();
        }

        public event Action<bool> OperationStateChanged;
        public event Action InstallerSelectionChanged;

        public bool HasLocalModuleInstallerSelection
        {
            get { return _localModuleInstallerSelection != null; }
        }

        public string SelectedLocalModuleInstallerPath
        {
            get
            {
                return _localModuleInstallerSelection == null
                    ? string.Empty
                    : _localModuleInstallerSelection.SourcePath;
            }
        }

        public string AutomaticSetupStatus
        {
            get { return _statusLabel.Text ?? string.Empty; }
        }

        public bool FmuApiMode
        {
            get { return _fmuApiMode; }
            set
            {
                if (_fmuApiMode == value) return;
                if (_running || _hostBusy)
                    throw new InvalidOperationException(
                        "Режим FMU-API нельзя менять во время операции.");
                _fmuApiMode = value;
                _hasLoaded = false;
                UpdateOfficialControllerStatus();
                FillRows(null);
                _statusLabel.Text = value
                    ? FmuControllerNotApplicable + " Установка FMU-API выполняется отдельно."
                    : "Нажмите «Обновить», чтобы загрузить ККТ и их настройки.";
            }
        }

        public void SetHostBusy(bool busy)
        {
            _hostBusy = busy;
            UpdateActionState();
        }

        public Task RefreshIfNeededAsync()
        {
            return _hasLoaded ? Task.FromResult(0) : RefreshSilentlyAsync();
        }

        public Task RefreshAsync()
        {
            return RunOperationAsync(RefreshCoreAsync, "Получение зарегистрированных ККТ из ЕСМ...");
        }

        private Task RefreshSilentlyAsync()
        {
            return RunOperationAsync(
                RefreshCoreAsync,
                "Получение зарегистрированных ККТ из ЕСМ...",
                false,
                false,
                false,
                CancellationToken.None);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_cancellation != null)
                {
                    _cancellation.Cancel();
                    _cancellation.Dispose();
                    _cancellation = null;
                }
                ClearLocalModuleInstallerSelection();
            }

            base.Dispose(disposing);
        }

        private async Task RunOperationAsync(
            Func<CancellationToken, Task> operation,
            string startMessage)
        {
            await RunOperationAsync(
                operation,
                startMessage,
                false,
                true,
                true,
                CancellationToken.None);
        }

        private async Task RunOperationAsync(
            Func<CancellationToken, Task> operation,
            string startMessage,
            bool allowHostBusy,
            bool raiseOperationState,
            bool showErrors,
            CancellationToken externalCancellation)
        {
            if (_running || (!allowHostBusy && _hostBusy) || operation == null)
            {
                return;
            }

            _running = true;
            _cancellation = externalCancellation.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(externalCancellation)
                : new CancellationTokenSource();
            _statusLabel.Text = startMessage;
            if (raiseOperationState)
            {
                RaiseOperationStateChanged(true);
            }
            UpdateActionState();
            try
            {
                await operation(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !Disposing)
                {
                    _statusLabel.Text = "Операция отменена.";
                    Log("Операция на вкладке ЛМ ЧЗ отменена.\r\n\r\n");
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    string message = SensitiveDataMasker.Mask(ex.Message);
                    _statusLabel.Text = "Ошибка: " + message;
                    Log("Ошибка на вкладке ЛМ ЧЗ: " + message + "\r\n\r\n");
                    if (DetailLogger != null)
                        DetailLogger("Подробности ошибки на вкладке ЛМ ЧЗ:\r\n" +
                            ex + "\r\n\r\n");
                    if (showErrors)
                    {
                        MessageBox.Show(this, message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            finally
            {
                if (_cancellation != null)
                {
                    _cancellation.Dispose();
                    _cancellation = null;
                }
                _running = false;
                if (raiseOperationState)
                {
                    RaiseOperationStateChanged(false);
                }
                UpdateActionState();
            }
        }

        private async Task RefreshCoreAsync(CancellationToken cancellationToken)
        {
            string baseUrl = ValidateAndReadBaseUrl();
            LmGatewayDiscovery discovery = await _discoveryWorkflow.DiscoverAsync(
                baseUrl,
                ReportDiscoveryProgress,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (!discovery.IsSuccessful)
            {
                throw new InvalidOperationException(discovery.ErrorMessage);
            }

            _session.ReplaceDiscovery(discovery);
            _currentDiscovery = discovery;
            RefreshServiceInventory();
            await ProbeManagedServicesAsync(cancellationToken);
            MergeServiceDrafts();
            // Обновление инвентаря не является проверкой готовности ЛМ.
            // /api/v2/info вызывается отдельной кнопкой диагностики.
            FillRows(null);
            _hasLoaded = true;
            _statusLabel.Text = discovery.Items.Count == 0
                ? "Зарегистрированные ККТ для настройки не найдены."
                : "Загружено ККТ: " + discovery.Items.Count.ToString() + ". " +
                    (FmuApiMode ? FmuControllerNotApplicable
                        : "Готовность ЛМ по ЕСМ проверяется отдельно.");
            AppendServiceCapabilityStatus();

            StringBuilder log = new StringBuilder();
            log.AppendLine(FmuApiMode
                ? "=== ККТ и ЛМ ЧЗ для подготовки FMU-API ==="
                : "=== ККТ, контроллеры и управляемые ЛМ ЧЗ ===");
            log.AppendLine("Экземпляров в ЕСМ: " + discovery.Items.Count.ToString() + ".");
            if (FmuApiMode) log.AppendLine(FmuControllerNotApplicable);
            for (int index = 0; index < discovery.Issues.Count; index++)
            {
                LmGatewayDiscoveryIssue issue = discovery.Issues[index];
                log.AppendLine("Пропущен экземпляр " + (issue.InstanceId ?? string.Empty) + ": " +
                    (issue.Message ?? string.Empty));
            }
            log.AppendLine();
            Log(log.ToString());
        }

        private string ValidateAndReadBaseUrl()
        {
            string baseUrl = (_baseUrlProvider() ?? string.Empty).Trim();
            ValidationResult validation = TspiotInputValidator.ValidateForCheck(
                new TspiotFormInput { BaseUrl = baseUrl });
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.JoinMessages());
            }
            if (validation.Warnings.Count > 0 && MessageBox.Show(this,
                validation.JoinWarnings() + "\r\n\r\nПродолжить?", "Проверьте адрес ЕСМ",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                throw new OperationCanceledException();
            }

            return baseUrl;
        }

        private void FillRows(string selectedSerial)
        {
            if (_loadingGrid || IsDisposed || Disposing)
            {
                return;
            }

            if (string.IsNullOrEmpty(selectedSerial))
            {
                LmGatewayBindingSessionRow selected = GetSelectedSessionRow();
                selectedSerial = selected == null || selected.Kkt == null
                    ? string.Empty
                    : selected.Kkt.KktSerial;
            }

            _loadingGrid = true;
            try
            {
                _grid.Rows.Clear();
                ManagedLocalModulePlan displayPlan = BuildCurrentManagedPlan();
                IList<LmServiceInventoryItem> removalInventory =
                    GetRemovableManagedItems(
                        _serviceInventory,
                        _managedLocalModuleInventory);
                HashSet<string> registeredSerials = new HashSet<string>(
                    StringComparer.Ordinal);
                int rowToSelect = -1;
                for (int index = 0; index < _session.Rows.Count; index++)
                {
                    LmGatewayBindingSessionRow item = _session.Rows[index];
                    registeredSerials.Add(item.Kkt.KktSerial);
                    LmServiceInventoryItem inventory = FindInventory(item.Kkt.KktSerial);
                    ManagedLocalModuleInventoryItem managedLm =
                        _managedLocalModuleInventory.Find(
                            item.Kkt.KktSerial);
                    LocalModuleMsiInventoryItem msiLm =
                        FindMsiInventoryByInn(item.Kkt.KktInn);
                    LmGatewayDraft draft = GetOrCreateServiceDraft(item.Kkt.KktSerial);
                    int ordinal = GetCurrentKktOrdinal(
                        displayPlan,
                        item.Kkt,
                        index + 1);
                    int rowIndex = _grid.Rows.Add(
                        ordinal.ToString(),
                        item.Kkt.KktSerial,
                        item.Kkt.KktInn,
                        item.Kkt.SoftPort ?? string.Empty,
                        FmuApiMode && msiLm == null && managedLm == null
                            ? "Не установлен"
                            : GetLmEndpointText(
                                item, draft, inventory, managedLm, msiLm),
                        GetLmStateText(inventory, managedLm, msiLm),
                        GetMsiRoleText(msiLm),
                        msiLm == null ? string.Empty : msiLm.InstallRoot,
                        GetMsiOwnershipText(msiLm),
                        FmuApiMode
                            ? "Не применяется (FMU-API)"
                            : GetEsmLinkStateText(item));
                    LmServiceInventoryItem removable = FindRemovalInventory(
                        removalInventory,
                        item.Kkt.KktSerial);
                    _grid.Rows[rowIndex].Tag = new LmGatewayGridRow(
                        item,
                        removable ?? inventory,
                        item.Kkt.KktInn);
                    if (FmuApiMode)
                    {
                        _grid.Rows[rowIndex].Cells["EsmLinkState"].Style.ForeColor =
                            SystemColors.GrayText;
                    }
                    else if (item.LastBindingStatus == LmGatewayBindingStatus.BindingVerified)
                    {
                        _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.Honeydew;
                    }
                    else if (item.LastBindingStatus == LmGatewayBindingStatus.BindingAccepted)
                    {
                        _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.LightCyan;
                    }
                    else if (item.LastBindingStatus == LmGatewayBindingStatus.BindingObserved)
                    {
                        _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.LightCyan;
                    }
                    else if (item.LastBindingStatus == LmGatewayBindingStatus.RequiresAttention)
                    {
                        _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.LemonChiffon;
                    }
                    else if (item.LastBindingStatus.HasValue)
                    {
                        _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.MistyRose;
                    }
                    if (string.Equals(item.Kkt.KktSerial, selectedSerial, StringComparison.Ordinal))
                    {
                        rowToSelect = rowIndex;
                    }
                }

                for (int index = 0; index < removalInventory.Count; index++)
                {
                    LmServiceInventoryItem inventory = removalInventory[index];
                    if (inventory == null ||
                        string.IsNullOrWhiteSpace(inventory.KktSerial) ||
                        registeredSerials.Contains(inventory.KktSerial))
                    {
                        continue;
                    }

                    ManagedLocalModuleInventoryItem managedLm =
                        _managedLocalModuleInventory.Find(inventory.KktSerial);
                    string inn = managedLm == null
                        ? string.Empty
                        : (managedLm.Inn ?? string.Empty);
                    string ordinal = managedLm != null && managedLm.KktOrdinal > 0
                        ? managedLm.KktOrdinal.ToString(CultureInfo.InvariantCulture)
                        : "—";
                    string softwarePort = managedLm != null &&
                        managedLm.SoftwarePort > 0
                            ? managedLm.SoftwarePort.ToString(
                                CultureInfo.InvariantCulture)
                            : string.Empty;
                    int orphanRowIndex = _grid.Rows.Add(
                        ordinal,
                        inventory.KktSerial,
                        inn,
                        softwarePort,
                        GetLmEndpointText(null, null, inventory, managedLm),
                        GetLmStateText(inventory, managedLm),
                        "Старый комплект",
                        string.Empty,
                        "Старая схема",
                        "Нет в ЕСМ");
                    _grid.Rows[orphanRowIndex].Tag = new LmGatewayGridRow(
                        null,
                        inventory,
                        inn);
                    _grid.Rows[orphanRowIndex].DefaultCellStyle.BackColor =
                        Color.LemonChiffon;
                    if (string.Equals(
                        inventory.KktSerial,
                        selectedSerial,
                        StringComparison.Ordinal))
                    {
                        rowToSelect = orphanRowIndex;
                    }
                }

                if (rowToSelect >= 0)
                {
                    _grid.Rows[rowToSelect].Selected = true;
                    _grid.CurrentCell = _grid.Rows[rowToSelect].Cells[1];
                }
                else if (_grid.Rows.Count > 0)
                {
                    _grid.Rows[0].Selected = true;
                    _grid.CurrentCell = _grid.Rows[0].Cells[1];
                }
            }
            finally
            {
                _loadingGrid = false;
            }

            UpdateActionState();
        }

        private LmGatewayBindingSessionRow GetSelectedSessionRow()
        {
            if (_grid.SelectedRows.Count != 1)
            {
                return null;
            }

            LmGatewayGridRow context = _grid.SelectedRows[0].Tag as LmGatewayGridRow;
            return context == null ? null : context.SessionRow;
        }

        private static LmServiceInventoryItem FindRemovalInventory(
            IList<LmServiceInventoryItem> inventory,
            string serial)
        {
            if (inventory == null) return null;
            for (int index = 0; index < inventory.Count; index++)
            {
                if (inventory[index] != null && string.Equals(
                    inventory[index].KktSerial,
                    serial,
                    StringComparison.Ordinal))
                {
                    return inventory[index];
                }
            }
            return null;
        }

        private void ReportDiscoveryProgress(LmGatewayProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            PostToUi(delegate
            {
                _statusLabel.Text = progress.Stage + " " + progress.Current.ToString() + "/" +
                    progress.Total.ToString() + ": " + progress.InstanceId;
            });
        }

        private void CancelCurrentOperation()
        {
            if (_cancellation == null || _cancellation.IsCancellationRequested)
            {
                return;
            }

            _cancellation.Cancel();
            _statusLabel.Text = "Остановка запрошена. Текущая ККТ завершится безопасно; следующие не начнутся.";
        }

        private void UpdateActionState()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            bool idle = !_running && !_hostBusy;
            bool hasKkts = _session.Rows.Count > 0;

            _refreshButton.Enabled = idle;
            UpdateServiceActionState(idle, hasKkts);
            _grid.Enabled = true;
            _cancelButton.Visible = _running;
            _cancelButton.Enabled = _running && _cancellation != null && !_cancellation.IsCancellationRequested;
        }

        private void RaiseOperationStateChanged(bool busy)
        {
            Action<bool> handler = OperationStateChanged;
            if (handler != null)
            {
                handler(busy);
            }
        }

        private void PostToUi(Action action)
        {
            if (action == null || IsDisposed || Disposing)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void Log(string text)
        {
            if (_logger != null)
            {
                _logger(SensitiveDataMasker.Mask(text));
            }
        }

        /// <summary>
        /// Приёмник подробностей сбоя (полное исключение со стеком), который
        /// хозяин формы направляет только в файл; маскирует он сам.
        /// </summary>
        internal Action<string> DetailLogger { get; set; }

        // Всё, что требует прав, выполняет отдельный elevated-помощник, и до
        // его ответа журнал молчит: со стороны это неотличимо от зависания.
        // Пока идёт ожидание, стадия сама отмечается в журнале.
        private const int HeartbeatMilliseconds = 20000;

        private static string ElevationHint()
        {
            return ProvisionerProcessLauncher.IsAlreadyElevated()
                ? "запрос UAC не потребуется"
                : "подтвердите UAC";
        }

        private async Task<T> WithHeartbeatAsync<T>(
            string stageName,
            Task<T> work)
        {
            if (work == null) throw new ArgumentNullException("work");
            DateTime started = DateTime.UtcNow;
            ProvisionerStepTrace.Restart(stageName);
            Log(stageName + ": начато, работает разовый помощник с правами " +
                "администратора.\r\n");
            string lastReported = string.Empty;
            while (true)
            {
                Task finished = await Task.WhenAny(
                    work,
                    Task.Delay(HeartbeatMilliseconds)).ConfigureAwait(true);
                if (ReferenceEquals(finished, work))
                {
                    break;
                }
                TimeSpan elapsed = DateTime.UtcNow - started;
                // Помощник пишет каждый свой шаг в общий след, поэтому окно
                // может назвать текущую операцию, а не только время ожидания.
                string step = ProvisionerStepTrace.ReadLastStep();
                string suffix = string.IsNullOrEmpty(step)
                    ? string.Empty
                    : "; сейчас: " + step;
                if (!string.Equals(step, lastReported, StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(step))
                {
                    lastReported = step;
                }
                Log(stageName + ": прошло " +
                    ((int)elapsed.TotalMinutes).ToString() + " мин " +
                    elapsed.Seconds.ToString("00") + " с" + suffix + ".\r\n");
            }
            return await work.ConfigureAwait(true);
        }

    }
}
