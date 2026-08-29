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
        private readonly ToolTip _serviceToolTip = new ToolTip();
        private readonly Dictionary<string, LmGatewayDraft> _serviceDrafts =
            new Dictionary<string, LmGatewayDraft>(StringComparer.Ordinal);
        private readonly List<LmServiceInventoryItem> _serviceInventory =
            new List<LmServiceInventoryItem>();
        private LmGatewayDraftSettingsStore _draftSettingsStore;
        private LmServiceProvisionerClient _serviceProvisioner;
        private CompleteStackProvisionerClient _completeStackProvisioner;
        private LmServiceInventoryReader _inventoryReader;
        private ManagedLocalModuleInventoryReader _managedLocalModuleInventoryReader;
        private ManagedLocalModuleInventorySnapshot _managedLocalModuleInventory =
            new ManagedLocalModuleInventorySnapshot();
        private LmGatewayProbe _serviceProbe;
        private ReadOnlyTcpListenerOwnerReader _tcpListenerReader;
        private LmGatewayRemovalWorkflow _removalWorkflow;
        private LmControllerInstallerSelection _installerSelection;
        private LocalModuleInstallerSelection _localModuleInstallerSelection;
        private LmGatewayDiscovery _currentDiscovery;
        private bool _helperAvailable;
        private string _helperUnavailableReason = string.Empty;
        private string _serviceInventoryWarning = string.Empty;

        private void InitializeServiceFeatures()
        {
            _serviceProvisioner = new LmServiceProvisionerClient();
            _completeStackProvisioner = new CompleteStackProvisionerClient();
            _inventoryReader = new LmServiceInventoryReader();
            _managedLocalModuleInventoryReader =
                new ManagedLocalModuleInventoryReader();
            _serviceProbe = new LmGatewayProbe();
            _tcpListenerReader = new ReadOnlyTcpListenerOwnerReader();
            _draftSettingsStore = LmGatewayDraftSettingsStore.CreateDefault();
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
                Text = "Официальные пакеты",
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
                RowCount = 4
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _installerPathTextBox.Dock = DockStyle.Fill;
            _installerPathTextBox.ReadOnly = true;
            _installerPathTextBox.Tag = "Выберите esm-lm-controller_*-windows-setup.exe";
            ConfigureButton(_selectInstallerButton, "Выбрать…");
            ConfigureButton(_installControllerButton, "Настроить все ККТ");
            _installControllerButton.Tag = "AutomaticSetup";
            _selectInstallerButton.Click += delegate { SelectInstaller(); };
            _installControllerButton.Click += async delegate { await StartAutomaticSetupAsync(); };
            _installerStatusLabel.AutoSize = true;
            _installerStatusLabel.AutoEllipsis = true;
            _installerStatusLabel.Text = "Установщик не выбран.";
            _localModuleInstallerPathTextBox.Dock = DockStyle.Fill;
            _localModuleInstallerPathTextBox.ReadOnly = true;
            _localModuleInstallerPathTextBox.Tag =
                "Выберите regime-2.6.1-7.msi";
            ConfigureButton(_selectLocalModuleInstallerButton, "Выбрать…");
            _selectLocalModuleInstallerButton.Click +=
                delegate { SelectLocalModuleInstaller(this); };
            _localModuleInstallerStatusLabel.AutoSize = true;
            _localModuleInstallerStatusLabel.AutoEllipsis = true;
            _localModuleInstallerStatusLabel.Text =
                "MSI ЛМ ЧЗ не выбран.";
            table.Controls.Add(_installerPathTextBox, 0, 0);
            table.Controls.Add(_selectInstallerButton, 1, 0);
            table.Controls.Add(_installControllerButton, 2, 0);
            table.Controls.Add(_installerStatusLabel, 0, 1);
            table.SetColumnSpan(_installerStatusLabel, 4);
            table.Controls.Add(_localModuleInstallerPathTextBox, 0, 2);
            table.Controls.Add(_selectLocalModuleInstallerButton, 1, 2);
            table.Controls.Add(_localModuleInstallerStatusLabel, 0, 3);
            table.SetColumnSpan(_localModuleInstallerStatusLabel, 4);
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
                    UpdateActionState();
                    return false;
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
                RaiseInstallerSelectionChanged();
            }
            catch (Exception ex)
            {
                ClearLocalModuleInstallerSelection();
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

        private void SelectInstaller()
        {
            SelectControllerInstaller(this);
        }

        private async Task StartAutomaticSetupAsync()
        {
            if (!HasRequiredInstallerSelections &&
                !SelectRequiredInstallers(this))
            {
                return;
            }

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
                    await ExecuteCompleteAutomaticSetupAsync(
                        kkts,
                        delegate(string serial, CancellationToken ignored)
                        {
                            return Task.FromResult(true);
                        },
                        token);
                },
                "Подготовка полного плана ККТ, контроллеров и ЛМ ЧЗ...");
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
                result.Add(CopyCompleteSetupKkt(discovery.Items[index]));
            }
            return result;
        }

        public async Task<bool> RunCompleteAutomaticSetupFromHostAsync(
            IList<LmGatewayKkt> candidates,
            Func<string, CancellationToken, Task<bool>> registerKkt,
            CancellationToken externalCancellation)
        {
            _automaticSetupCancelledBeforeMutation = false;
            bool completed = false;
            await RunOperationAsync(
                async delegate(CancellationToken token)
                {
                    completed = await ExecuteCompleteAutomaticSetupAsync(
                        candidates,
                        registerKkt,
                        token);
                },
                "Подготовка полного плана ККТ, контроллеров и ЛМ ЧЗ...",
                true,
                false,
                false,
                externalCancellation);
            return completed;
        }

        private async Task<bool> ExecuteCompleteAutomaticSetupAsync(
            IList<LmGatewayKkt> candidates,
            Func<string, CancellationToken, Task<bool>> registerKkt,
            CancellationToken cancellation)
        {
            _automaticSetupCancelledBeforeMutation = false;
            if (!HasRequiredInstallerSelections)
            {
                throw new InvalidOperationException(
                    "Выберите установщик контроллера и MSI ЛМ ЧЗ.");
            }
            if (registerKkt == null)
            {
                throw new ArgumentNullException("registerKkt");
            }

            cancellation.ThrowIfCancellationRequested();
            RefreshServiceInventory();
            EnsureNoPendingManagedCleanup(_managedLocalModuleInventory);
            ManagedLocalModulePlan plan = ManagedLocalModulePlanner.Build(
                candidates,
                _managedLocalModuleInventory.Kkts,
                _managedLocalModuleInventory.Modules,
                null);
            EnsureCompletePlanIsUsable(plan);

            IList<LmAutomaticSetupDialogRow> rows =
                CreateCompleteSetupDialogRows(plan, candidates);
            using (LmAutomaticSetupDialog dialog =
                new LmAutomaticSetupDialog(rows))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK ||
                    dialog.Parameters == null)
                {
                    _automaticSetupCancelledBeforeMutation = true;
                    _statusLabel.Text =
                        "Автоматическая настройка отменена до изменения системы.";
                    return false;
                }
                ApplyCompleteSetupParameters(plan, dialog.Parameters);
            }
            ValidateEditedCompletePlan(plan);
            ValidateNewCompletePlanPortsAreFree(plan);
            if (!ConfirmCompleteSetup(plan))
            {
                _automaticSetupCancelledBeforeMutation = true;
                _statusLabel.Text =
                    "Автоматическая настройка отменена до изменения системы.";
                return false;
            }

            LmServiceProvisioningBatchRequest request =
                CreateCompleteSetupRequest(plan);
            _statusLabel.Text =
                "Ожидание подтверждения UAC и проверка обоих пакетов...";
            LmServiceProvisioningBatchResult result =
                await _completeStackProvisioner.RunAsync(
                    request,
                    async delegate(int index, CancellationToken token)
                    {
                        ManagedLocalModuleProvisioningItemRequest item =
                            request.ManagedLocalModules[index];
                        _statusLabel.Text = "ККТ " +
                            (index + 1).ToString(CultureInfo.InvariantCulture) +
                            "/" + request.ManagedLocalModules.Count.ToString(
                                CultureInfo.InvariantCulture) +
                            ": регистрация в ЕСМ " + item.KktSerial + "...";
                        return await registerKkt(item.KktSerial, token);
                    },
                    delegate(
                        int index,
                        LmServiceProvisioningItemResult item,
                        CancellationToken token)
                    {
                        _statusLabel.Text = "ККТ " +
                            (index + 1).ToString(CultureInfo.InvariantCulture) +
                            "/" + request.ManagedLocalModules.Count.ToString(
                                CultureInfo.InvariantCulture) +
                            ": локальный комплект готов.";
                        Log((item.KktSerial ?? string.Empty) + ": " +
                            item.Status.ToString() + ". " +
                            SensitiveDataMasker.Mask(item.Message) + "\r\n");
                        return Task.FromResult(true);
                    },
                    cancellation);

            bool success = IsCompleteSetupSuccessful(result);
            try
            {
                await RefreshCoreAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException ||
                    ex is StackOverflowException ||
                    ex is AccessViolationException)
                {
                    throw;
                }
                RefreshServiceInventory();
                FillRows(null);
                Log("Комплекты созданы, но итоговое обновление ЕСМ не удалось: " +
                    SensitiveDataMasker.Mask(ex.Message) + "\r\n");
            }
            _statusLabel.Text = success
                ? "Все комплекты запущены и готовы к инициализации ЛМ ЧЗ."
                : "Настройка завершена не для всех ККТ. Проверьте таблицу и журнал.";
            return success;
        }

        private IList<LmGatewayKkt> CopySessionKkts()
        {
            List<LmGatewayKkt> result = new List<LmGatewayKkt>();
            for (int index = 0; index < _session.Rows.Count; index++)
            {
                LmGatewayBindingSessionRow row = _session.Rows[index];
                if (row != null && row.Kkt != null && row.IsSelected)
                {
                    result.Add(CopyCompleteSetupKkt(row.Kkt));
                }
            }
            return result;
        }

        private static LmGatewayKkt CopyCompleteSetupKkt(LmGatewayKkt source)
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

        private static IList<LmAutomaticSetupDialogRow>
            CreateCompleteSetupDialogRows(
            ManagedLocalModulePlan plan,
            IList<LmGatewayKkt> candidates)
        {
            List<LmAutomaticSetupDialogRow> result =
                new List<LmAutomaticSetupDialogRow>();
            for (int groupIndex = 0;
                groupIndex < plan.Items.Count;
                groupIndex++)
            {
                ManagedLocalModulePlanItem group = plan.Items[groupIndex];
                for (int kktIndex = 0;
                    kktIndex < group.KktAssignments.Count;
                    kktIndex++)
                {
                    ManagedKktAssignment assignment =
                        group.KktAssignments[kktIndex];
                    LmGatewayKkt kkt = FindCompleteSetupKkt(
                        candidates,
                        assignment.KktSerial);
                    result.Add(new LmAutomaticSetupDialogRow
                    {
                        Ordinal = assignment.KktOrdinal,
                        KktSerial = assignment.KktSerial,
                        KktInn = assignment.KktInn,
                        SoftwarePort = kkt == null
                            ? string.Empty
                            : kkt.SoftPort,
                        TargetAddress = "127.0.0.1",
                        TargetPort = group.Module.ApiPort.ToString(
                            CultureInfo.InvariantCulture)
                    });
                }
            }
            return result;
        }

        private void ApplyCompleteSetupParameters(
            ManagedLocalModulePlan plan,
            IList<LmAutomaticSetupDialogRow> parameters)
        {
            for (int index = 0; index < parameters.Count; index++)
            {
                LmAutomaticSetupDialogRow item = parameters[index];
                ManagedLocalModulePlanItem group = item == null
                    ? null
                    : plan.FindByInn(item.KktInn);
                ManagedKktAssignment assignment = FindCompleteAssignment(
                    group,
                    item == null ? null : item.KktSerial);
                string normalizedAddress = string.Empty;
                bool loopback = false;
                int apiPort = 0;
                if (item == null || group == null || assignment == null ||
                    assignment.KktOrdinal != item.Ordinal ||
                    !LmGatewayInputValidator.TryNormalizeTargetAddress(
                        item.TargetAddress,
                        out normalizedAddress,
                        out loopback) ||
                    !loopback ||
                    !int.TryParse(
                        item.TargetPort,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out apiPort) ||
                    apiPort < 1 || apiPort > 65535)
                {
                    throw new InvalidOperationException(
                        "Полный автоматический режим устанавливает ЛМ на этот компьютер; " +
                        "для него нужен локальный адрес 127.0.0.1 и допустимый порт.");
                }

                group.Module.ApiPort = apiPort;
                LmGatewayDraft draft = GetOrCreateServiceDraft(item.KktSerial);
                draft.KktInn = (item.KktInn ?? string.Empty).Trim();
                draft.TargetAddress = normalizedAddress;
                draft.TargetPort = apiPort.ToString(CultureInfo.InvariantCulture);
                draft.GrpcPort = assignment.GrpcPort.ToString(
                    CultureInfo.InvariantCulture);
                draft.RestPort = assignment.RestPort.ToString(
                    CultureInfo.InvariantCulture);
                _session.TryUpdateDraft(
                    item.KktSerial,
                    LmGatewayDraftDefaults.ControllerAddress,
                    draft.GrpcPort,
                    true);
            }
            PersistServiceDrafts();
        }

        private static void EnsureCompletePlanIsUsable(
            ManagedLocalModulePlan plan)
        {
            if (plan == null || plan.Items.Count == 0)
            {
                throw new InvalidOperationException(
                    "Не найдено ККТ с корректными серийными номерами и ИНН.");
            }
            if (!plan.IsValid)
            {
                StringBuilder message = new StringBuilder();
                for (int index = 0;
                    index < plan.ValidationMessages.Count;
                    index++)
                {
                    message.AppendLine(plan.ValidationMessages[index]);
                }
                for (int index = 0; index < plan.Items.Count; index++)
                {
                    if (!plan.Items[index].IsValid)
                    {
                        message.AppendLine(
                            plan.Items[index].JoinValidationMessages());
                    }
                }
                throw new InvalidOperationException(message.ToString().Trim());
            }
        }

        private static void EnsureNoPendingManagedCleanup(
            ManagedLocalModuleInventorySnapshot inventory)
        {
            if (inventory == null) return;
            for (int index = 0; index < inventory.Items.Count; index++)
            {
                ManagedLocalModuleInventoryItem item = inventory.Items[index];
                if (item != null && item.CleanupPending)
                {
                    throw new InvalidOperationException(
                        "Для ККТ " + item.KktSerial +
                        " не завершена предыдущая очистка. " +
                        "Сначала нажмите «Повторить очистку» или выполните " +
                        "«Удалить всё созданное», затем запустите настройку снова.");
                }
            }
        }

        private static void ValidateEditedCompletePlan(
            ManagedLocalModulePlan plan)
        {
            HashSet<int> ports = new HashSet<int>();
            for (int groupIndex = 0;
                groupIndex < plan.Items.Count;
                groupIndex++)
            {
                ManagedLocalModulePlanItem group = plan.Items[groupIndex];
                AddCompleteSetupPort(
                    ports,
                    group.Module.ApiPort,
                    "порт API ЛМ ЧЗ");
                AddCompleteSetupPort(
                    ports,
                    group.Module.DatabasePort,
                    "порт базы ЛМ ЧЗ");
                AddCompleteSetupPort(
                    ports,
                    group.Module.EpmdPort,
                    "порт EPMD ЛМ ЧЗ");
                for (int kktIndex = 0;
                    kktIndex < group.KktAssignments.Count;
                    kktIndex++)
                {
                    ManagedKktAssignment kkt =
                        group.KktAssignments[kktIndex];
                    AddCompleteSetupPort(
                        ports,
                        kkt.GrpcPort,
                        "порт gRPC контроллера");
                    AddCompleteSetupPort(
                        ports,
                        kkt.RestPort,
                        "порт REST контроллера");
                }
            }
        }

        private void ValidateNewCompletePlanPortsAreFree(
            ManagedLocalModulePlan plan)
        {
            _statusLabel.Text = "Проверка локальных портов перед запуском...";
            IList<int> occupied;
            try
            {
                occupied = _tcpListenerReader.FindPorts(1, 65535);
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException ||
                    ex is StackOverflowException ||
                    ex is AccessViolationException)
                {
                    throw;
                }
                throw new InvalidOperationException(
                    "Не удалось проверить занятые TCP-порты. " +
                    "Автоматическая настройка не начата: " + ex.Message,
                    ex);
            }
            IList<int> conflicts = ManagedLocalModulePortPreflight.FindConflicts(
                plan,
                _managedLocalModuleInventory.Kkts,
                _managedLocalModuleInventory.Modules,
                occupied);
            if (conflicts.Count > 0)
            {
                throw new InvalidOperationException(
                    "Новые порты уже заняты: " + string.Join(
                        ", ",
                        ToInvariantStrings(conflicts)) +
                    ". Измените доступный порт ЛМ ЧЗ в плане либо " +
                    "освободите перечисленные порты; " +
                    "система не изменялась.");
            }
        }

        private static string[] ToInvariantStrings(IList<int> values)
        {
            string[] result = new string[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                result[index] = values[index].ToString(
                    CultureInfo.InvariantCulture);
            }
            return result;
        }

        private static void AddCompleteSetupPort(
            ISet<int> ports,
            int port,
            string role)
        {
            if (port < 1 || port > 65535 || !ports.Add(port))
            {
                throw new InvalidOperationException(
                    "Недопустимый или повторяющийся " + role + ": " +
                    port.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        private bool ConfirmCompleteSetup(ManagedLocalModulePlan plan)
        {
            int kktCount = 0;
            for (int index = 0; index < plan.Items.Count; index++)
            {
                kktCount += plan.Items[index].KktAssignments.Count;
            }
            StringBuilder message = new StringBuilder();
            message.AppendLine("Будет выполнена полная автоматическая настройка:");
            message.AppendLine("• ККТ: " + kktCount.ToString(
                CultureInfo.InvariantCulture));
            message.AppendLine("• отдельных ЛМ ЧЗ (по одному на ИНН): " +
                plan.Items.Count.ToString(CultureInfo.InvariantCulture));
            message.AppendLine("• отдельный контроллер: по одному на каждую ККТ.");
            message.AppendLine();
            message.AppendLine(
                "Подтвердите, что для каждой ККТ имеется отдельная действующая лицензия.");
            message.AppendLine(
                "Файлы поставщиков не изменяются и остаются только на этом компьютере.");
            message.AppendLine(
                "При ошибке созданные программой службы и профили можно удалить кнопкой «Удалить всё созданное».");
            return MessageBox.Show(
                this,
                message.ToString(),
                "Подтвердите полную настройку",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private LmServiceProvisioningBatchRequest CreateCompleteSetupRequest(
            ManagedLocalModulePlan plan)
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity == null || identity.User == null)
            {
                throw new InvalidOperationException(
                    "Не удалось определить SID текущего пользователя.");
            }
            LmServiceProvisioningBatchRequest request =
                new LmServiceProvisioningBatchRequest
                {
                    SchemaVersion = 1,
                    Operation = LmServiceOperation.EnsureManagedLocalModules,
                    OperationId = Guid.NewGuid().ToString("N"),
                    InitiatingSid = identity.User.Value,
                    InstallerSelection = CopyControllerInstallerForCompleteSetup(
                        _installerSelection),
                    LocalModuleInstallerSelection =
                        CopyLocalModuleInstallerForCompleteSetup(
                            _localModuleInstallerSelection)
                };
            IList<ManagedLocalModuleProvisioningItemRequest> items =
                ManagedLocalModuleRequestBuilder.Build(
                    plan,
                    SupportedLocalModulePackageIdentity.ProductVersion);
            for (int index = 0; index < items.Count; index++)
            {
                request.ManagedLocalModules.Add(items[index]);
            }
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            return request;
        }

        private static LmControllerInstallerSelection
            CopyControllerInstallerForCompleteSetup(
            LmControllerInstallerSelection source)
        {
            if (source == null) return null;
            return new LmControllerInstallerSelection
            {
                SourcePath = source.SourcePath,
                FileName = source.FileName,
                ByteLength = source.ByteLength,
                Sha256 = source.Sha256,
                FileVersion = source.FileVersion,
                ProductVersion = source.ProductVersion,
                SignerSubject = source.SignerSubject,
                SignerThumbprint = source.SignerThumbprint,
                StopManagedInstancesWarningAccepted = true
            };
        }

        private static LocalModuleInstallerSelection
            CopyLocalModuleInstallerForCompleteSetup(
            LocalModuleInstallerSelection source)
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
                LicenseNoticeAccepted = true
            };
        }

        private static bool IsCompleteSetupSuccessful(
            LmServiceProvisioningBatchResult result)
        {
            if (result == null || result.Items == null ||
                result.Items.Count == 0)
            {
                return false;
            }
            for (int index = 0; index < result.Items.Count; index++)
            {
                LmServiceProvisioningStatus status = result.Items[index].Status;
                if (status != LmServiceProvisioningStatus.Succeeded &&
                    status != LmServiceProvisioningStatus.ReadyToInitialize)
                {
                    return false;
                }
            }
            return true;
        }

        private static ManagedKktAssignment FindCompleteAssignment(
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

        private static LmGatewayKkt FindCompleteSetupKkt(
            IList<LmGatewayKkt> kkts,
            string serial)
        {
            if (kkts == null) return null;
            string expected = (serial ?? string.Empty).Trim();
            for (int index = 0; index < kkts.Count; index++)
            {
                if (kkts[index] != null && string.Equals(
                    (kkts[index].KktSerial ?? string.Empty).Trim(),
                    expected,
                    StringComparison.Ordinal))
                {
                    return kkts[index];
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
                    "Не удалось проверить созданные программой компоненты; " +
                    "настройка и удаление служб заблокированы. " +
                    "Обычные операции с ККТ в ЕСМ доступны на других вкладках.";
                Log(_serviceInventoryWarning + " Причина: " + ex.GetType().Name + ".\r\n");
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
                ManagedKktAssignment assignment = FindCompleteAssignment(
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
                    kkts.Add(CopyCompleteSetupKkt(row.Kkt));
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
            ManagedKktAssignment assignment = FindCompleteAssignment(
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
            IList<LmServiceInventoryItem> items = GetRemovableManagedItems(
                _serviceInventory,
                _managedLocalModuleInventory);
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
                ManagedLocalModuleInventorySnapshot freshManaged;
                try
                {
                    fresh = _inventoryReader.Read();
                    freshManaged = _managedLocalModuleInventoryReader.Read();
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

                if (!RemovalBatchStillMatches(
                    confirmations,
                    fresh,
                    freshManaged))
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
                        LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained ||
                    item.Status == LmServiceProvisioningStatus.Succeeded ||
                    item.Status ==
                        LmServiceProvisioningStatus.SharedLocalModuleRetained)
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
            string summary = "Удалено созданных комплектов и их локальных данных: " +
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
                        CanonicalLmPlanHasher.FixedTimeEqualsHex(
                            item.ManifestFingerprint.Sha256,
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
                        !CanonicalLmPlanHasher.FixedTimeEqualsHex(
                            managed.ManagedStateFingerprint.Sha256,
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
            _selectInstallerButton.Enabled = idle;
            _selectLocalModuleInstallerButton.Enabled = idle;
            _installControllerButton.Enabled = idle && _installerSelection != null &&
                _localModuleInstallerSelection != null &&
                _helperAvailable && hasKkts;
            _removeServiceButton.Enabled = idle && managed && _helperAvailable &&
                selected.Status != LmServiceProvisioningStatus.CleanupPending;
            _removeAllServicesButton.Enabled = idle && _helperAvailable &&
                GetRemovableManagedItems(
                    _serviceInventory,
                    _managedLocalModuleInventory).Count > 0;
            _cleanupButton.Enabled = idle && managed && _helperAvailable &&
                selected.Status == LmServiceProvisioningStatus.CleanupPending;
            string setupReason = !_helperAvailable
                ? _helperUnavailableReason
                : _installerSelection == null
                    ? "Выберите установщик контроллера ЛМ ЧЗ."
                    : _localModuleInstallerSelection == null
                        ? "Выберите MSI ЛМ ЧЗ."
                    : !hasKkts
                        ? "В таблице нет зарегистрированных ККТ."
                        : "Создать или обновить локальные контроллеры и ЛМ ЧЗ для всех ККТ.";
            _serviceToolTip.SetToolTip(_installControllerButton, setupReason);
            _serviceToolTip.SetToolTip(_removeServiceButton,
                _helperAvailable ? "Удалить выбранный комплект ККТ, контроллер и неиспользуемый ЛМ." : _helperUnavailableReason);
            _serviceToolTip.SetToolTip(_removeAllServicesButton,
                _helperAvailable
                    ? "Удалить все службы, профили и копии ЛМ, созданные этой программой."
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

        private static string GetLmEndpointText(
            LmGatewayBindingSessionRow session,
            LmGatewayDraft draft,
            LmServiceInventoryItem controller,
            ManagedLocalModuleInventoryItem managedLm)
        {
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
            if (managedLm != null &&
                !string.IsNullOrWhiteSpace(managedLm.State))
            {
                return managedLm.State;
            }
            return controller == null
                ? "Не создан"
                : "ЛМ не создан";
        }

        private static string GetEsmLinkStateText(
            LmGatewayBindingSessionRow session)
        {
            if (session == null || !session.LastBindingStatus.HasValue)
            {
                return "Ожидает инициализации";
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
