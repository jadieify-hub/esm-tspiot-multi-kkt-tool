using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Security.Principal;
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
    public sealed partial class LmGatewayPage
    {
        private readonly Button _selectInstallerButton = new Button();
        private readonly Button _selectLocalModuleInstallerButton = new Button();
        private readonly Button _installControllerButton = new Button();
        private readonly Button _removeServiceButton = new Button();
        private readonly Button _removeAllServicesButton = new Button();
        private readonly Button _cleanupButton = new Button();
        private readonly TextBox _installerPathTextBox = new TextBox();
        private readonly TextBox _localModuleInstallerPathTextBox = new TextBox();
        private readonly Label _installerStatusLabel = new Label();
        private readonly Label _localModuleInstallerStatusLabel = new Label();
        private readonly Label _setupActionHintLabel = new Label();
        private readonly ToolTip _serviceToolTip = new ToolTip();
        private readonly Dictionary<string, LmGatewayDraft> _serviceDrafts =
            new Dictionary<string, LmGatewayDraft>(StringComparer.Ordinal);
        private readonly List<LmServiceInventoryItem> _serviceInventory =
            new List<LmServiceInventoryItem>();
        private LmGatewayDraftSettingsStore _draftSettingsStore;
        private OperatorPackagePathStore _packagePathStore;
        private LmServiceProvisionerClient _serviceProvisioner;
        private LocalModuleMsiProvisionerClient _localModuleMsiProvisioner;
        private LmServiceInventoryReader _inventoryReader;
        private ManagedLocalModuleInventoryReader _managedLocalModuleInventoryReader;
        private ManagedLocalModuleInventorySnapshot _managedLocalModuleInventory =
            new ManagedLocalModuleInventorySnapshot();
        private LocalModuleMsiOperatorInventorySnapshot
            _localModuleMsiInventory =
                new LocalModuleMsiOperatorInventorySnapshot();
        private LmGatewayProbe _serviceProbe;
        private ReadOnlyTcpListenerOwnerReader _tcpListenerReader;
        private LmGatewayRemovalWorkflow _removalWorkflow;
        private LmControllerInstallerSelection _installerSelection;
        private LocalModuleInstallerSelection _localModuleInstallerSelection;
        private LmGatewayDiscovery _currentDiscovery;
        private bool _helperAvailable;
        private string _helperUnavailableReason = string.Empty;
        private string _serviceInventoryWarning = string.Empty;
        private int _lastAutomaticLocalModulesReady;
        private int _lastAutomaticLocalModulesFailed;

        private void InitializeServiceFeatures()
        {
            _serviceProvisioner = new LmServiceProvisionerClient();
            _localModuleMsiProvisioner = new LocalModuleMsiProvisionerClient();
            _inventoryReader = new LmServiceInventoryReader();
            _managedLocalModuleInventoryReader =
                new ManagedLocalModuleInventoryReader();
            _serviceProbe = new LmGatewayProbe();
            _tcpListenerReader = new ReadOnlyTcpListenerOwnerReader();
            _draftSettingsStore = LmGatewayDraftSettingsStore.CreateDefault();
            _packagePathStore = OperatorPackagePathStore.CreateDefault();
            _removalWorkflow = new LmGatewayRemovalWorkflow(
                _serviceProvisioner,
                ReadCombinedRemovalInventory);
            _helperAvailable = _serviceProvisioner.IsAvailable(out _helperUnavailableReason);
            RefreshServiceInventory();
        }

        private Control BuildInstallerPanel()
        {
            GroupBox group = new GroupBox
            {
                Text = "Независимые контроллеры ЛМ ЧЗ",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            TableLayoutPanel table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(6, 3, 6, 5),
                ColumnCount = 3,
                RowCount = 5
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label description = new Label
            {
                Text = "Используется установленный официальный контроллер ЛМ ЧЗ от ЕСП. " +
                    "Для каждой ККТ создаётся отдельная служба; ЛМ ЧЗ можно " +
                    "установить и инициализировать позже.",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 0, 5)
            };
            Label msiLabel = new Label
            {
                Text = "Официальный MSI ЛМ ЧЗ",
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            _localModuleInstallerPathTextBox.ReadOnly = true;
            _localModuleInstallerPathTextBox.Dock = DockStyle.Fill;
            ConfigureButton(_selectLocalModuleInstallerButton, "Выбрать MSI...");
            _selectLocalModuleInstallerButton.Click += delegate
            {
                SelectLocalModuleInstaller(this);
            };
            _localModuleInstallerStatusLabel.AutoSize = true;
            _localModuleInstallerStatusLabel.Dock = DockStyle.Fill;
            _localModuleInstallerStatusLabel.Text = "MSI ЛМ ЧЗ не выбран; " +
                "контроллеры всё равно можно настроить.";
            ConfigureButton(
                _installControllerButton,
                "Настроить контроллеры и ЛМ ЧЗ");
            _installControllerButton.Tag = "AutomaticSetup";
            _installControllerButton.Click += async delegate { await StartAutomaticSetupAsync(); };
            _setupActionHintLabel.AutoSize = false;
            _setupActionHintLabel.Dock = DockStyle.Fill;
            _setupActionHintLabel.MinimumSize = new Size(0, 32);
            _setupActionHintLabel.TextAlign = ContentAlignment.MiddleLeft;
            _setupActionHintLabel.AutoEllipsis = true;
            _setupActionHintLabel.Margin = new Padding(8, 0, 0, 4);
            table.Controls.Add(description, 0, 0);
            table.SetColumnSpan(description, 3);
            table.Controls.Add(msiLabel, 0, 1);
            table.Controls.Add(_localModuleInstallerPathTextBox, 1, 1);
            table.Controls.Add(_selectLocalModuleInstallerButton, 2, 1);
            table.Controls.Add(_localModuleInstallerStatusLabel, 1, 2);
            table.SetColumnSpan(_localModuleInstallerStatusLabel, 2);
            table.Controls.Add(_installControllerButton, 0, 3);
            table.Controls.Add(_setupActionHintLabel, 1, 3);
            table.SetColumnSpan(_setupActionHintLabel, 2);
            Control manualStages = BuildManualStagePanel();
            table.Controls.Add(manualStages, 0, 4);
            table.SetColumnSpan(manualStages, 3);
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
                "Состояние установленного официального контроллера ЛМ ЧЗ " +
                "будет проверено при обновлении.";
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
                    SaveInstallerPaths();
                    UpdateActionState();
                    return false;
                }
                ApplyControllerInstallerSelection();
                SaveInstallerPaths();
                RaiseInstallerSelectionChanged();
            }
            catch (Exception ex)
            {
                ClearInstallerSelection();
                SaveInstallerPaths();
                MessageBox.Show(this, SensitiveDataMasker.Mask(ex.Message),
                    "Проверка установщика", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            UpdateActionState();
            return _installerSelection != null;
        }

        public bool SelectLocalModuleInstaller(IWin32Window owner)
        {
            ClearLocalModuleInstallerSelection();
            try
            {
                _localModuleInstallerSelection =
                    LocalModuleInstallerPicker.SelectAndInspect(owner ?? this);
                if (_localModuleInstallerSelection == null)
                {
                    _localModuleInstallerStatusLabel.Text =
                        "Выбор MSI отменён; путь очищен.";
                    SaveInstallerPaths();
                    UpdateActionState();
                    return false;
                }
                ApplyLocalModuleInstallerSelection();
                SaveInstallerPaths();
                RaiseInstallerSelectionChanged();
            }
            catch (Exception ex)
            {
                ClearLocalModuleInstallerSelection();
                SaveInstallerPaths();
                MessageBox.Show(
                    this,
                    SensitiveDataMasker.Mask(ex.Message),
                    "Проверка MSI ЛМ ЧЗ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            UpdateActionState();
            return _localModuleInstallerSelection != null;
        }

        /// <summary>
        /// Требует выбранный официальный MSI ЛМ ЧЗ и открывает выбор файла,
        /// если он не задан. Без него автомат раньше проходил весь контур и
        /// молча оставлял кассу без локального модуля, а выяснялось это
        /// только в итоговом окне.
        /// </summary>
        public bool RequireLocalModuleInstaller(IWin32Window owner)
        {
            if (_localModuleInstallerSelection != null) return true;
            SelectLocalModuleInstaller(owner);
            return _localModuleInstallerSelection != null;
        }

        public bool SelectRequiredInstallers(IWin32Window owner)
        {
            if (_installerSelection == null &&
                !SelectControllerInstaller(owner))
            {
                return false;
            }
            if (_localModuleInstallerSelection == null &&
                !SelectLocalModuleInstaller(owner))
            {
                return false;
            }
            return true;
        }

        private async Task StartAutomaticSetupAsync()
        {
            await RunOperationAsync(
                async delegate(CancellationToken token)
                {
                    await RefreshCoreAsync(token);
                    _session.SelectAll();
                    IList<LmGatewayKkt> kkts = CopySessionKkts();
                    if (kkts.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "В ЕСМ нет зарегистрированных ККТ для полной настройки.");
                    }
                    await RunFullAutomaticLocalSetupFromHostAsync(kkts, token);
                },
                "Подготовка контроллеров и независимых ЛМ ЧЗ...");
        }

        public async Task<IList<LmGatewayKkt>>
            DiscoverRegisteredKktsForAutomaticPlanAsync(
            string baseUrl,
            CancellationToken cancellation)
        {
            LmGatewayDiscovery discovery = await _discoveryWorkflow.DiscoverAsync(
                baseUrl,
                null,
                cancellation);
            if (!discovery.IsSuccessful)
            {
                throw new InvalidOperationException(discovery.ErrorMessage);
            }

            List<LmGatewayKkt> result = new List<LmGatewayKkt>();
            for (int index = 0; index < discovery.Items.Count; index++)
            {
                result.Add(CopyKktForPlanning(discovery.Items[index]));
            }
            return result;
        }

        private IList<LmGatewayKkt> CopySessionKkts()
        {
            List<LmGatewayKkt> result = new List<LmGatewayKkt>();
            for (int index = 0; index < _session.Rows.Count; index++)
            {
                LmGatewayBindingSessionRow row = _session.Rows[index];
                if (row != null && row.Kkt != null && row.IsSelected)
                {
                    result.Add(CopyKktForPlanning(row.Kkt));
                }
            }
            return result;
        }

        private static LmGatewayKkt CopyKktForPlanning(LmGatewayKkt source)
        {
            if (source == null) return new LmGatewayKkt();
            return new LmGatewayKkt
            {
                InstanceId = (source.InstanceId ?? string.Empty).Trim(),
                KktSerial = (source.KktSerial ?? string.Empty).Trim(),
                KktInn = (source.KktInn ?? string.Empty).Trim(),
                FnSerial = (source.FnSerial ?? string.Empty).Trim(),
                Port = (source.Port ?? string.Empty).Trim(),
                SoftPort = (source.SoftPort ?? string.Empty).Trim(),
                DkktPort = (source.DkktPort ?? string.Empty).Trim(),
                ServiceState = (source.ServiceState ?? string.Empty).Trim()
            };
        }

        private static LocalModuleInstallerSelection
            CopyLocalModuleInstallerForAutomaticSetup(
            LocalModuleInstallerSelection source,
            bool licenseNoticeAccepted)
        {
            if (source == null) return null;
            return new LocalModuleInstallerSelection
            {
                SourcePath = source.SourcePath,
                FileName = source.FileName,
                ByteLength = source.ByteLength,
                Sha256 = source.Sha256,
                ProductName = source.ProductName,
                ProductVersion = source.ProductVersion,
                ProductCode = source.ProductCode,
                UpgradeCode = source.UpgradeCode,
                SignerSubject = source.SignerSubject,
                SignerThumbprint = source.SignerThumbprint,
                LicenseNoticeAccepted = licenseNoticeAccepted
            };
        }

        private static ManagedKktAssignment FindKktAssignment(
            ManagedLocalModulePlanItem group,
            string serial)
        {
            if (group == null) return null;
            string expected = (serial ?? string.Empty).Trim();
            for (int index = 0; index < group.KktAssignments.Count; index++)
            {
                if (string.Equals(
                    group.KktAssignments[index].KktSerial,
                    expected,
                    StringComparison.Ordinal))
                {
                    return group.KktAssignments[index];
                }
            }
            return null;
        }

        private void RefreshServiceInventory()
        {
            _serviceInventory.Clear();
            _managedLocalModuleInventory =
                new ManagedLocalModuleInventorySnapshot();
            _serviceInventoryWarning = string.Empty;
            try
            {
                IList<LmServiceInventoryItem> read = _inventoryReader.Read();
                for (int index = 0; index < read.Count; index++)
                {
                    _serviceInventory.Add(read[index]);
                }
                _managedLocalModuleInventory =
                    _managedLocalModuleInventoryReader.Read();
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
                    "Старый инвентарь служебных ЛМ недоступен; основной " +
                    "автомат продолжит работу через разовый helper с UAC.";
                Log(_serviceInventoryWarning + " Причина: " + ex.GetType().Name +
                    ". Операция/путь: " +
                    SensitiveDataMasker.Mask(ex.Message) + "\r\n");
            }
            try
            {
                _localModuleMsiInventory =
                    new LocalModuleMsiOperatorInventoryReader().Read();
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) &&
                    !(ex is UnauthorizedAccessException) &&
                    !(ex is InvalidDataException) &&
                    !(ex is ArgumentException) &&
                    !(ex is System.Security.SecurityException))
                    throw;
                _localModuleMsiInventory =
                    new LocalModuleMsiOperatorInventorySnapshot();
                Log("Инвентарь независимых MSI ЛМ будет повторно проверен " +
                    "helper: " + ex.GetType().Name + "; " +
                    SensitiveDataMasker.Mask(ex.Message) + "\r\n");
            }
            UpdateOfficialControllerStatus();
            _helperAvailable = _serviceProvisioner.IsAvailable(out _helperUnavailableReason);
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
                _officialControllerStatusLabel.Text =
                    "Предварительное состояние контроллера ЛМ ЧЗ прочитать не удалось. " +
                    "Разовый helper повторит проверку перед настройкой.";
                return;
            }
            LmServiceInventoryDisplay display = LmServiceInventoryDisplay.Create(_serviceInventory);
            if (display.OfficialControllers.Count == 0)
            {
                _officialControllerStatusLabel.Text =
                    "Официальный контроллер ЛМ ЧЗ от ЕСП не найден. " +
                    "Установите его штатным пакетом поставщика.";
                return;
            }

            LmServiceInventoryItem official = display.OfficialControllers[0];
            _officialControllerStatusLabel.Text =
                "Официальный контроллер ЛМ ЧЗ от ЕСП установлен, состояние: " +
                GetServiceStatusText(official) + "." +
                " Для первой ККТ используется эта штатная служба." +
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
            ManagedLocalModulePlan managedPlan = BuildCurrentManagedPlan();
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
                ManagedLocalModulePlanItem group = managedPlan == null
                    ? null
                    : managedPlan.FindByInn(row.Kkt.KktInn);
                ManagedKktAssignment assignment = FindKktAssignment(
                    group,
                    serial);
                int ordinal = assignment == null
                    ? index + 1
                    : assignment.KktOrdinal;
                LmGatewayDraft defaults = LmGatewayDraftDefaults.Create(
                    row.Kkt,
                    ordinal);
                if (assignment != null)
                {
                    defaults.GrpcPort = assignment.GrpcPort.ToString(
                        CultureInfo.InvariantCulture);
                    defaults.RestPort = assignment.RestPort.ToString(
                        CultureInfo.InvariantCulture);
                }
                if (group != null && group.Module != null)
                {
                    defaults.TargetAddress = "127.0.0.1";
                    defaults.TargetPort = group.Module.ApiPort.ToString(
                        CultureInfo.InvariantCulture);
                }
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
            else if (IsGeneratedLocalModuleEndpoint(
                draft.TargetAddress,
                draft.TargetPort))
            {
                draft.TargetAddress = defaults.TargetAddress;
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

        private ManagedLocalModulePlan BuildCurrentManagedPlan()
        {
            List<LmGatewayKkt> kkts = new List<LmGatewayKkt>();
            for (int index = 0; index < _session.Rows.Count; index++)
            {
                LmGatewayBindingSessionRow row = _session.Rows[index];
                if (row != null && row.Kkt != null)
                {
                    kkts.Add(CopyKktForPlanning(row.Kkt));
                }
            }
            return ManagedLocalModulePlanner.Build(
                kkts,
                _managedLocalModuleInventory.Kkts,
                _managedLocalModuleInventory.Modules,
                null);
        }

        private static int GetCurrentKktOrdinal(
            ManagedLocalModulePlan plan,
            LmGatewayKkt kkt,
            int fallback)
        {
            ManagedLocalModulePlanItem group = plan == null || kkt == null
                ? null
                : plan.FindByInn(kkt.KktInn);
            ManagedKktAssignment assignment = FindKktAssignment(
                group,
                kkt == null ? null : kkt.KktSerial);
            return assignment == null ? fallback : assignment.KktOrdinal;
        }

        private static bool IsGeneratedLocalModuleEndpoint(
            string address,
            string portText)
        {
            int port;
            if (!string.Equals(
                    (address ?? string.Empty).Trim(),
                    "127.0.0.1",
                    StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(
                    portText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out port))
            {
                return false;
            }
            int difference = port - 4995;
            return difference >= 1000 && difference <= 32000 &&
                difference % 1000 == 0;
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

        private async Task ConfirmAndRemoveServiceAsync()
        {
            LmServiceInventoryItem item = GetSelectedInventoryItem();
            if (item == null || item.Role != LmServiceRole.Managed)
            {
                return;
            }
            using (LmGatewayRemovalDialog dialog = new LmGatewayRemovalDialog(
                item,
                GetSelectedKktInn()))
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

        private static IList<LmServiceInventoryItem> GetRemovableManagedItems(
            IList<LmServiceInventoryItem> inventory,
            ManagedLocalModuleInventorySnapshot managedInventory)
        {
            List<LmServiceInventoryItem> items = new List<LmServiceInventoryItem>();
            Dictionary<string, LmServiceInventoryItem> bySerial =
                new Dictionary<string, LmServiceInventoryItem>(
                    StringComparer.Ordinal);
            if (inventory != null)
            {
                for (int index = 0; index < inventory.Count; index++)
                {
                    LmServiceInventoryItem item = inventory[index];
                    if (item != null && item.Role == LmServiceRole.Managed &&
                        item.Ports != null && item.ManifestFingerprint != null &&
                        CanonicalLmPlanHasher.IsWellFormedSha256(
                            item.ManifestFingerprint.Sha256) &&
                        item.Ports.GrpcPort > 0 && item.Ports.GrpcPort <= 65535 &&
                        item.Ports.RestPort > 0 && item.Ports.RestPort <= 65535 &&
                        item.Ports.GrpcPort != item.Ports.RestPort)
                    {
                        items.Add(item);
                        bySerial[item.KktSerial] = item;
                    }
                }
            }
            if (managedInventory != null)
            {
                for (int index = 0;
                    index < managedInventory.Items.Count;
                    index++)
                {
                    ManagedLocalModuleInventoryItem managed =
                        managedInventory.Items[index];
                    if (managed == null ||
                        managed.ManagedStateFingerprint == null ||
                        !CanonicalLmPlanHasher.IsWellFormedSha256(
                            managed.ManagedStateFingerprint.Sha256))
                    {
                        continue;
                    }
                    LmServiceInventoryItem existing;
                    if (bySerial.TryGetValue(
                        managed.KktSerial,
                        out existing))
                    {
                        existing.ManagedStateFingerprint =
                            managed.ManagedStateFingerprint;
                        if (managed.CleanupPending)
                        {
                            existing.Status =
                                LmServiceProvisioningStatus.CleanupPending;
                            existing.Message = managed.State;
                        }
                        continue;
                    }
                    int grpc = managed.KktAssignment == null
                        ? 0
                        : managed.KktAssignment.GrpcPort;
                    int rest = managed.KktAssignment == null
                        ? 0
                        : managed.KktAssignment.RestPort;
                    LmServiceInventoryItem synthetic =
                        new LmServiceInventoryItem
                        {
                            KktSerial = managed.KktSerial,
                            ServiceName = LmServiceIdentity.CreateName(
                                managed.KktSerial),
                            Role = LmServiceRole.Managed,
                            Ports = new LmGatewayPorts(grpc, rest),
                            IsRunning = string.Equals(
                                managed.State,
                                "Запущен",
                                StringComparison.Ordinal),
                            Status = managed.CleanupPending
                                ? LmServiceProvisioningStatus.CleanupPending
                                : LmServiceProvisioningStatus.RequiresAttention,
                            ManagedStateFingerprint =
                                managed.ManagedStateFingerprint,
                            Message = managed.State
                        };
                    items.Add(synthetic);
                    bySerial.Add(synthetic.KktSerial, synthetic);
                }
            }
            items.Sort(delegate(LmServiceInventoryItem left, LmServiceInventoryItem right)
            {
                return string.CompareOrdinal(left.KktSerial, right.KktSerial);
            });
            return items;
        }

        private IList<LmServiceInventoryItem> ReadCombinedRemovalInventory()
        {
            return GetRemovableManagedItems(
                _inventoryReader.Read(),
                _managedLocalModuleInventoryReader.Read());
        }

        private static bool RemovalBatchStillMatches(
            IList<LmRemovalConfirmation> confirmations,
            IList<LmServiceInventoryItem> inventory,
            ManagedLocalModuleInventorySnapshot managedInventory)
        {
            if (confirmations == null)
            {
                return false;
            }
            IList<LmServiceInventoryItem> fresh = GetRemovableManagedItems(
                inventory,
                managedInventory);
            if (fresh.Count != confirmations.Count)
            {
                return false;
            }
            for (int index = 0; index < confirmations.Count; index++)
            {
                LmRemovalConfirmation confirmation = confirmations[index];
                LmServiceInventoryItem item = fresh[index];
                bool legacyMatches = confirmation != null &&
                    SameOptionalRemovalFingerprint(
                        confirmation.ManifestFingerprint,
                        item.ManifestFingerprint) &&
                    (confirmation.ManifestFingerprint == null ||
                     (confirmation.GrpcPort == item.Ports.GrpcPort &&
                      confirmation.RestPort == item.Ports.RestPort));
                bool managedMatches = confirmation != null &&
                    SameOptionalRemovalFingerprint(
                        confirmation.ManagedStateFingerprint,
                        item.ManagedStateFingerprint);
                if (confirmation == null ||
                    !string.Equals(confirmation.KktSerial, item.KktSerial, StringComparison.Ordinal) ||
                    (confirmation.ManifestFingerprint == null &&
                     confirmation.ManagedStateFingerprint == null) ||
                    !legacyMatches || !managedMatches)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameOptionalRemovalFingerprint(
            LmManifestFingerprint left,
            LmManifestFingerprint right)
        {
            return left == null
                ? right == null
                : right != null &&
                    CanonicalLmPlanHasher.FixedTimeEqualsHex(
                        left.Sha256,
                        right.Sha256);
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
                ManagedStateFingerprint = item.ManagedStateFingerprint,
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

        private void UpdateServiceActionState(bool idle, bool hasKkts)
        {
            LmServiceInventoryItem selected = GetSelectedInventoryItem();
            bool managed = selected != null && selected.Role == LmServiceRole.Managed;
            LmGatewayBindingSessionRow selectedSession = GetSelectedSessionRow();
            _selectInstallerButton.Enabled = false;
            _selectLocalModuleInstallerButton.Enabled = idle;
            _installControllerButton.Enabled = idle && _helperAvailable && hasKkts;
            UpdateManualStageActionState(idle, hasKkts);
            _bindButton.Enabled = idle && selectedSession != null && managed &&
                selected.IsRunning && selected.IsReady;
            _removeServiceButton.Enabled = idle && managed && _helperAvailable &&
                selected.Status != LmServiceProvisioningStatus.CleanupPending;
            _removeAllServicesButton.Enabled = idle && _helperAvailable &&
                HasOwnedComponentsForRemoval();
            _cleanupButton.Enabled = idle && managed && _helperAvailable &&
                selected.Status == LmServiceProvisioningStatus.CleanupPending;
            string setupReason = !_helperAvailable
                ? _helperUnavailableReason
                : !hasKkts
                    ? "В таблице нет зарегистрированных ККТ."
                    : "Создать или обновить независимый контроллер для каждой ККТ.";
            _setupActionHintLabel.Text = setupReason;
            _serviceToolTip.SetToolTip(_installControllerButton, setupReason);
            _serviceToolTip.SetToolTip(_bindButton,
                selectedSession == null
                    ? "Выберите зарегистрированную ККТ."
                    : !managed || !selected.IsRunning || !selected.IsReady
                        ? "Сначала создайте и проверьте локальный комплект выбранной ККТ."
                        : "Однократно передать ЕСМ адрес контроллера и введённые данные ЛМ ЧЗ.");
            _serviceToolTip.SetToolTip(_removeServiceButton,
                _helperAvailable ? "Удалить выбранный комплект ККТ, контроллер и неиспользуемый ЛМ." : _helperUnavailableReason);
            _serviceToolTip.SetToolTip(_removeAllServicesButton,
                _helperAvailable
                    ? "Удалить созданные клоны ЛМ и контроллеров; " +
                        "поставщицкий базовый ЛМ сохранить."
                    : _helperUnavailableReason);

            if (!idle)
            {
                _selectionActionHintLabel.Text =
                    "Дождитесь завершения текущей операции.";
            }
            else if (selectedSession == null && selected == null)
            {
                _selectionActionHintLabel.Text =
                    "Выберите строку ККТ — здесь появятся доступные действия.";
            }
            else if (selected == null)
            {
                _selectionActionHintLabel.Text =
                    "Комплект этой ККТ ещё не создан.";
            }
            else if (!managed)
            {
                _selectionActionHintLabel.Text =
                    "Выбранная служба не управляется этой программой.";
            }
            else if (selectedSession == null)
            {
                _selectionActionHintLabel.Text = _helperAvailable
                    ? "ККТ отсутствует в ЕСМ; доступно удаление комплекта."
                    : "ККТ отсутствует в ЕСМ; удаление недоступно — " +
                        _helperUnavailableReason;
            }
            else if (selected.Status ==
                LmServiceProvisioningStatus.CleanupPending)
            {
                _selectionActionHintLabel.Text =
                    "Для выбранного комплекта требуется завершить очистку.";
            }
            else if (!selected.IsRunning || !selected.IsReady)
            {
                _selectionActionHintLabel.Text =
                    "Комплект не готов: создайте или обновите его.";
            }
            else if (!_helperAvailable)
            {
                _selectionActionHintLabel.Text =
                    "Комплект готов: привязка к ЕСМ доступна; " +
                    "удаление недоступно — " + _helperUnavailableReason;
            }
            else
            {
                _selectionActionHintLabel.Text =
                    "Комплект готов: доступны привязка к ЕСМ и удаление.";
            }
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

        private string GetSelectedKktInn()
        {
            if (_grid.SelectedRows.Count != 1)
            {
                return string.Empty;
            }
            LmGatewayGridRow row = _grid.SelectedRows[0].Tag as LmGatewayGridRow;
            return row == null ? string.Empty : row.KktInn;
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

        private void RestoreInstallerSelections()
        {
            if (_packagePathStore == null)
            {
                return;
            }
            OperatorPackagePaths paths;
            try
            {
                paths = _packagePathStore.Load();
            }
            catch (Exception ex)
            {
                if (!IsRecoverableInstallerPathException(ex))
                {
                    throw;
                }
                Log("Не удалось прочитать сохранённые пути к пакетам: " +
                    ex.GetType().Name + ". " +
                    SensitiveDataMasker.Mask(ex.Message) + "\r\n");
                return;
            }

            bool discardInvalidPath = false;
            if (!string.IsNullOrEmpty(paths.ControllerInstallerPath))
            {
                try
                {
                    _installerSelection = LmControllerInstallerPicker.Inspect(
                        paths.ControllerInstallerPath);
                    ApplyControllerInstallerSelection();
                }
                catch (Exception ex)
                {
                    if (!IsRecoverableInstallerPathException(ex))
                    {
                        throw;
                    }
                    ClearInstallerSelection();
                    discardInvalidPath = true;
                    Log("Сохранённый установщик контроллера больше не прошёл проверку: " +
                        ex.GetType().Name + ". " +
                        SensitiveDataMasker.Mask(ex.Message) + "\r\n");
                }
            }
            if (!string.IsNullOrEmpty(paths.LocalModuleInstallerPath))
            {
                try
                {
                    _localModuleInstallerSelection =
                        LocalModuleInstallerPicker.Inspect(
                            paths.LocalModuleInstallerPath);
                    ApplyLocalModuleInstallerSelection();
                }
                catch (Exception ex)
                {
                    if (!IsRecoverableInstallerPathException(ex))
                    {
                        throw;
                    }
                    ClearLocalModuleInstallerSelection();
                    discardInvalidPath = true;
                    Log("Сохранённый MSI ЛМ ЧЗ больше не прошёл проверку: " +
                        ex.GetType().Name + ". " +
                        SensitiveDataMasker.Mask(ex.Message) + "\r\n");
                }
            }
            if (discardInvalidPath)
            {
                SaveInstallerPaths();
            }
        }

        private void ApplyControllerInstallerSelection()
        {
            if (_installerSelection == null)
            {
                return;
            }
            _installerPathTextBox.Text = _installerSelection.SourcePath;
            _serviceToolTip.SetToolTip(
                _installerPathTextBox,
                _installerSelection.SourcePath);
            _installerStatusLabel.Text = _installerSelection.FileName +
                " | версия " + _installerSelection.FileVersion +
                " | цифровая подпись проверена";
        }

        private void ApplyLocalModuleInstallerSelection()
        {
            if (_localModuleInstallerSelection == null)
            {
                return;
            }
            _localModuleInstallerPathTextBox.Text =
                _localModuleInstallerSelection.SourcePath;
            _serviceToolTip.SetToolTip(
                _localModuleInstallerPathTextBox,
                _localModuleInstallerSelection.SourcePath);
            _localModuleInstallerStatusLabel.Text =
                _localModuleInstallerSelection.FileName +
                " | версия " +
                _localModuleInstallerSelection.ProductVersion +
                " | SHA-256 и подпись совпали";
        }

        private void SaveInstallerPaths()
        {
            if (_packagePathStore == null)
            {
                return;
            }
            try
            {
                _packagePathStore.Save(
                    _installerSelection == null
                        ? string.Empty
                        : _installerSelection.SourcePath,
                    _localModuleInstallerSelection == null
                        ? string.Empty
                        : _localModuleInstallerSelection.SourcePath);
            }
            catch (Exception ex)
            {
                if (!IsRecoverableInstallerPathException(ex))
                {
                    throw;
                }
                Log("Не удалось запомнить пути к пакетам: " +
                    ex.GetType().Name + ". " +
                    SensitiveDataMasker.Mask(ex.Message) + "\r\n");
            }
        }

        private static bool IsRecoverableInstallerPathException(Exception ex)
        {
            return ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is SerializationException ||
                ex is InvalidDataException ||
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is CryptographicException ||
                ex is System.Security.SecurityException;
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

        private void ClearLocalModuleInstallerSelection()
        {
            if (_localModuleInstallerSelection != null)
            {
                _localModuleInstallerSelection.SourcePath = string.Empty;
            }
            _localModuleInstallerSelection = null;
            _localModuleInstallerPathTextBox.Clear();
            _serviceToolTip.SetToolTip(
                _localModuleInstallerPathTextBox,
                string.Empty);
            if (!IsDisposed)
            {
                _localModuleInstallerStatusLabel.Text =
                    "MSI ЛМ ЧЗ не выбран.";
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

        private static string GetLmEndpointText(
            LmGatewayBindingSessionRow session,
            LmGatewayDraft draft,
            LmServiceInventoryItem controller,
            ManagedLocalModuleInventoryItem managedLm)
        {
            return GetLmEndpointText(
                session,
                draft,
                controller,
                managedLm,
                null);
        }

        private static string GetLmEndpointText(
            LmGatewayBindingSessionRow session,
            LmGatewayDraft draft,
            LmServiceInventoryItem controller,
            ManagedLocalModuleInventoryItem managedLm,
            LocalModuleMsiInventoryItem msiLm)
        {
            if (msiLm != null)
                return "127.0.0.1:" + msiLm.ApiPort.ToString(
                    CultureInfo.InvariantCulture);
            if (managedLm != null &&
                !string.IsNullOrWhiteSpace(managedLm.Endpoint))
            {
                return managedLm.Endpoint;
            }
            string address = GetTargetAddressText(session, draft, controller);
            string port = GetTargetPortText(session, draft, controller);
            if (string.IsNullOrWhiteSpace(address) ||
                string.IsNullOrWhiteSpace(port))
            {
                return "Не настроен";
            }
            return address + ":" + port;
        }

        private static string GetLmStateText(
            LmServiceInventoryItem controller,
            ManagedLocalModuleInventoryItem managedLm)
        {
            return GetLmStateText(controller, managedLm, null);
        }

        private static string GetLmStateText(
            LmServiceInventoryItem controller,
            ManagedLocalModuleInventoryItem managedLm,
            LocalModuleMsiInventoryItem msiLm)
        {
            if (msiLm != null)
                return "Установлен; сверка при операции";
            if (managedLm != null &&
                !string.IsNullOrWhiteSpace(managedLm.State))
            {
                return managedLm.State;
            }
            return "Не создан";
        }

        private LocalModuleMsiInventoryItem FindMsiInventoryByInn(string inn)
        {
            string expected = (inn ?? string.Empty).Trim();
            for (int index = 0;
                index < _localModuleMsiInventory.Items.Count;
                index++)
            {
                LocalModuleMsiInventoryItem item =
                    _localModuleMsiInventory.Items[index];
                if (item != null && string.Equals(
                        item.Inn,
                        expected,
                        StringComparison.Ordinal))
                    return item;
            }
            return null;
        }

        private static string GetMsiRoleText(LocalModuleMsiInventoryItem item)
        {
            if (item == null) return "Не создан";
            return item.CloneOrdinal == 0
                ? "Базовый"
                : "Клон " + item.CloneOrdinal.ToString(
                    CultureInfo.InvariantCulture);
        }

        private static string GetMsiOwnershipText(
            LocalModuleMsiInventoryItem item)
        {
            if (item == null) return string.Empty;
            return item.PreExisting
                ? "Поставщик; сохраняется"
                : "Создан программой";
        }

        private static string GetEsmLinkStateText(
            LmGatewayBindingSessionRow session)
        {
            if (session == null || !session.LastBindingStatus.HasValue)
            {
                return "Не привязана";
            }
            switch (session.LastBindingStatus.Value)
            {
                case LmGatewayBindingStatus.BindingVerified:
                    return "Подтверждена";
                case LmGatewayBindingStatus.BindingAccepted:
                case LmGatewayBindingStatus.BindingObserved:
                    return "Настроена, ожидает проверки";
                case LmGatewayBindingStatus.Cancelled:
                    return "Не выполнена";
                default:
                    return "Требуется проверка";
            }
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

        private sealed class LmGatewayGridRow
        {
            internal LmGatewayGridRow(
                LmGatewayBindingSessionRow sessionRow,
                LmServiceInventoryItem inventory,
                string kktInn)
            {
                SessionRow = sessionRow;
                Inventory = inventory;
                KktInn = (kktInn ?? string.Empty).Trim();
            }

            internal LmGatewayBindingSessionRow SessionRow { get; private set; }
            internal LmServiceInventoryItem Inventory { get; private set; }
            internal string KktInn { get; private set; }
        }
    }
}
