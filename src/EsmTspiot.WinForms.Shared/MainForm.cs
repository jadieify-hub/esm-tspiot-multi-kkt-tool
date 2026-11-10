using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Button = AntdUI.Button;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class MainForm : AntdUI.BaseForm
    {
        private const int MaximumVisibleLogLength = 1500000;
        private readonly TspiotApiClient _client;
        private readonly IKktConnectionProvider _kktConnections;
        private readonly BulkRegistrationWorkflow _bulkWorkflow;
        private readonly KktDeletionWorkflow _deletionWorkflow;
        private readonly LmGatewayPage _lmGatewayPage;
        private readonly FileLogSink _fileLogSink = FileLogSink.CreateDefault();
        private readonly TextBox _baseUrlTextBox = new TextBox();
        private readonly TextBox _kktSerialTextBox = new TextBox();
        private readonly TextBox _fnSerialTextBox = new TextBox();
        private readonly TextBox _kktInnTextBox = new TextBox();
        private readonly TextBox _portTextBox = new TextBox();
        private readonly TextBox _softPortTextBox = new TextBox();
        private readonly TextBox _dkktPortTextBox = new TextBox();
        private readonly CheckBox _atolConfirmedCheckBox = new CheckBox();
        private readonly TextBox _logTextBox = new TextBox();
        private readonly Button _checkButton = new Button();
        private readonly Button _loadDkktButton = new Button();
        private readonly Button _addButton = new Button();
        private readonly Button _registerButton = new Button();
        private readonly Button _bulkRegisterButton = new Button();
        private readonly Button _automaticStopButton = new Button();
        private readonly CheckBox _fmuApiCheckBox = new CheckBox();
        private readonly Button _configureFrontolButton = new Button();
        private readonly Button _restoreFrontolButton = new Button();
        private readonly TextBox _automaticLmInstallerTextBox = new TextBox();
        private readonly ToolTip _automaticToolTip = new ToolTip();
        private readonly Button _refreshInstancesButton = new Button();
        private readonly Button _refreshManualInstancesButton = new Button();
        private readonly Button _deleteKktButton = new Button();
        private readonly Button _copyDiagnosticsButton = new Button();
        private readonly Button _openLogFolderButton = new Button();
        private readonly DataGridView _instancesGrid = new DataGridView();
        private readonly DataGridView _manualInstancesGrid = new DataGridView();
        private readonly Label _instancesSummaryLabel = new Label();
        private readonly Label _manualInstancesSummaryLabel = new Label();
        private readonly Label _nextPortPairLabel = new Label();
        private readonly Label _manualNextStepLabel = new Label();
        private readonly Label _automationStatusLabel = new Label();
        private readonly TabControl _workspaceTabs = new OperatorTabControl();
        private readonly TabPage _manualKktTab;
        private readonly TabPage _instancesTab = new TabPage();
        private readonly TabPage _automationTab = new TabPage();
        private readonly TabPage _lmGatewayTab;
        private readonly TabPage _logTab = new TabPage();
        private readonly ToolStripStatusLabel _operationStatusLabel = new ToolStripStatusLabel();
        private readonly Button _connectionStatusLabel = new Button();
        private readonly ToolStripMenuItem _operationsMenuItem = new ToolStripMenuItem("Сервис");
        private readonly ToolStripMenuItem _deleteMenuItem = new ToolStripMenuItem("Удалить выбранную ККТ");
        private readonly GroupBox _recoveryGroup = new GroupBox();
        private readonly Label _recoveryLabel = new Label();
        private readonly Button _createServiceButton = new Button();
        private readonly Button _copyRecoveryCommandButton = new Button();
        private readonly IList<Button> _actionButtons = new List<Button>();
        private string _lastControlModulePath = string.Empty;
        private bool _fileLogErrorShown;
        private bool _busy;
        private bool _registrationSupportPending;
        private bool _registrationProcessFinished;
        private bool _frontolWriteInProgress;
        private System.Threading.CancellationTokenSource _automaticCancellation;

        public MainForm() : this(new TspiotApiClient(), new AtolFptrConnectionProvider())
        {
        }

        public MainForm(TspiotApiClient client, IKktConnectionProvider connections)
        {
            _manualKktTab = _automationTab;
            _lmGatewayTab = _instancesTab;
            if (client == null) throw new ArgumentNullException("client");
            if (connections == null) throw new ArgumentNullException("connections");
            _client = client;
            _kktConnections = connections;
            _bulkWorkflow = new BulkRegistrationWorkflow(_client);
            _deletionWorkflow = new KktDeletionWorkflow(_client);
            _lmGatewayPage = new LmGatewayPage(
                delegate { return _baseUrlTextBox.Text; },
                _client,
                AppendLog);
            _lmGatewayPage.DetailLogger = AppendDetailToFile;
            _lmGatewayPage.OrganizationNameProvider = delegate(string serial, string inn) { return _organizations.GetName(serial, inn); };
            _lmGatewayPage.OperationStateChanged += OnLmGatewayOperationStateChanged;
            _lmGatewayPage.InstallerSelectionChanged += UpdateAutomaticInstallerSelection;
            _lmGatewayPage.SetupRequested += ShowWizardForRegisteredKkt;
            _lmGatewayPage.KktSelectionChanged += delegate { UpdateDeleteButtonState(); };
            _lmGatewayPage.SetupProgressChanged += delegate(string status)
            {
                if (_mainOperationRunning) _wizardMessage.Text = status;
            };
            var version = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                typeof(MainForm).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            Text = "MultiKKT " + version.InformationalVersion;
            // Таблица ЛМ ЧЗ шире прежнего окна: её колонки требуют около
            // 925 пикселей, и на 780 оператору приходилось растягивать окно
            // руками при каждом запуске.
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(1120, 720);
            MinimumSize = new Size(920, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10F);
            ForeColor = OperatorTheme.Ink;
            BackColor = OperatorTheme.Background;
            AntdUI.Config.IsLight = true;
            AntdUI.Config.Animation = false;
            Icon = OperatorTheme.AppIcon;

            BuildLayout();
            FillDefaults();
            _wizardDestination = WizardDestination();
            _wizardBuilt = true;
            UpdateWizardActions();
            UpdateAutomaticInstallerSelection();
            Shown += delegate { ConstrainToWorkingArea(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (_busy || _frontolWriteInProgress)
                {
                    e.Cancel = true;
                    _closeAfterOperation = true;
                    RequestWizardStop();
                    return;
                }
                if (_automaticCancellation != null)
                {
                    _automaticCancellation.Cancel();
                }
            };
            AppendLog("Файл журнала: " + _fileLogSink.LogFilePath + "\r\n\r\n");
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.Margin = Padding.Empty;
            root.Padding = Padding.Empty;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            MenuStrip menu = BuildMenu();
            root.Controls.Add(menu, 0, 0);
            Control workspace = BuildWorkspaceTabs();
            root.Controls.Add(BuildWorkspaceNavigation(), 0, 1);
            root.Controls.Add(workspace, 0, 2);
            root.Controls.Add(BuildStatusBar(), 0, 3);

            MainMenuStrip = menu;
            Controls.Add(root);
        }

        private MenuStrip BuildMenu()
        {
            MenuStrip menu = new MenuStrip();
            menu.Dock = DockStyle.Fill;
            menu.BackColor = OperatorTheme.Background;
            menu.ForeColor = OperatorTheme.Ink;
            menu.Font = new Font("Segoe UI", 9F);

            ToolStripMenuItem fileMenu = new ToolStripMenuItem("Файл");
            ToolStripMenuItem openLog = new ToolStripMenuItem("Открыть папку журнала");
            openLog.Click += delegate { OpenLogFolder(); };
            ToolStripMenuItem exit = new ToolStripMenuItem("Выход");
            exit.Click += delegate { Close(); };
            fileMenu.DropDownItems.Add(openLog);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(exit);

            ToolStripMenuItem refresh = new ToolStripMenuItem("Обновить список ККТ");
            refresh.Click += async delegate
            {
                _workspaceTabs.SelectedTab = _instancesTab;
                await _lmGatewayPage.RefreshAsync();
            };
            ToolStripMenuItem lmServer = new ToolStripMenuItem("Сервер ЛМ ЧЗ...");
            lmServer.Click += delegate { _lmGatewayPage.ShowLocalModuleServer(this); };
            _deleteMenuItem.Enabled = false;
            _deleteMenuItem.Click += async delegate { await RunButtonActionAsync(DeleteSelectedKktAsync); };
            _operationsMenuItem.DropDownItems.Add(refresh);
            _operationsMenuItem.DropDownItems.Add(lmServer);
            var serviceOperations = new ToolStripMenuItem("Служебные операции ЛМ ЧЗ…");
            serviceOperations.Click += delegate
            {
                _workspaceTabs.SelectedTab = _instancesTab;
                _lmGatewayPage.ShowServiceOperations();
            };
            _operationsMenuItem.DropDownItems.Add(serviceOperations);
            var restoreFrontol = new ToolStripMenuItem("Восстановить Frontol из снимка…");
            restoreFrontol.Click += async delegate { await RunButtonActionAsync(RestoreFrontolAsync); };
            _operationsMenuItem.DropDownItems.Add(restoreFrontol);
            _operationsMenuItem.DropDownItems.Add(new ToolStripSeparator());
            _operationsMenuItem.DropDownItems.Add(_deleteMenuItem);

            ToolStripMenuItem helpMenu = new ToolStripMenuItem("Справка");
            ToolStripMenuItem instruction = new ToolStripMenuItem("Открыть инструкцию");
            instruction.Click += delegate { OpenInstruction(); };
            ToolStripMenuItem support = new ToolStripMenuItem("Поддержать разработку");
            support.Click += delegate { ShowSupportDevelopment(); };
            ToolStripMenuItem about = new ToolStripMenuItem("О программе");
            about.Click += delegate { ShowAbout(); };
            helpMenu.DropDownItems.Add(instruction);
            helpMenu.DropDownItems.Add(support);
            helpMenu.DropDownItems.Add(new ToolStripSeparator());
            helpMenu.DropDownItems.Add(about);

            menu.Items.Add(fileMenu);
            menu.Items.Add(_operationsMenuItem);
            menu.Items.Add(helpMenu);
            ConfigureUpdateNotice(menu);
            return menu;
        }

        private Control BuildWorkspaceTabs()
        {
            _workspaceTabs.Dock = DockStyle.Fill;
            _workspaceTabs.Margin = new Padding(12, 2, 12, 4);

            ConfigureTabPage(_instancesTab, "Обзор касс");
            ConfigureTabPage(_automationTab, "Настройка ККТ");
            ConfigureTabPage(_logTab, "Журнал");

            ConfigureInstancesGrid();
            ConfigureButton(_deleteKktButton, "Удалить выбранную ККТ", DeleteSelectedKktAsync);
            _automationTab.Controls.Add(BuildWizardPage());
            _instancesTab.Controls.Add(_lmGatewayPage);
            _logTab.Controls.Add(BuildLogGroup());

            _workspaceTabs.TabPages.Add(_automationTab);
            _workspaceTabs.TabPages.Add(_instancesTab);
            _workspaceTabs.TabPages.Add(_logTab);
            _workspaceTabs.SelectedTab = _automationTab;
            _workspaceTabs.Selected += async delegate
            {
                if (_workspaceTabs.SelectedTab == _lmGatewayTab && !_busy)
                {
                    await _lmGatewayPage.RefreshIfNeededAsync();
                }
                UpdateWizardActions();
                UpdateDeleteButtonState();
            };
            return _workspaceTabs;
        }

        private static void ConfigureTabPage(TabPage page, string text)
        {
            page.Text = text;
            page.Padding = new Padding(6, 2, 6, 4);
            page.UseVisualStyleBackColor = false;
            page.BackColor = OperatorTheme.Background;
            page.AutoScroll = true;
        }


        private GroupBox BuildManualKktActionsGroup()
        {
            GroupBox group = CreateGroup("Последовательный режим");
            TableLayoutPanel panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.AutoSize = true;
            panel.ColumnCount = 3;
            panel.RowCount = 2;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            ConfigureButton(_loadDkktButton, "1. Выбрать следующую\r\nККТ", LoadDkktDataAsync);
            ConfigureButton(_addButton, "2. Добавить\r\nэкземпляр", AddSelectedInstanceAsync);
            ConfigureButton(_registerButton, "3. Зарегистрировать\r\nККТ", RegisterSelectedKktAsync);

            ConfigureActionGridButton(_loadDkktButton);
            ConfigureActionGridButton(_addButton);
            ConfigureActionGridButton(_registerButton);

            panel.Controls.Add(_loadDkktButton, 0, 0);
            panel.Controls.Add(_addButton, 1, 0);
            panel.Controls.Add(_registerButton, 2, 0);

            _manualNextStepLabel.AutoSize = false;
            _manualNextStepLabel.Dock = DockStyle.Fill;
            _manualNextStepLabel.MinimumSize = new Size(0, 24);
            _manualNextStepLabel.TextAlign = ContentAlignment.MiddleLeft;
            _manualNextStepLabel.Margin = new Padding(0, 6, 0, 2);
            _manualNextStepLabel.Text =
                "Отдельные операции для ручного ввода и восстановления. " +
                "После регистрации настройка ЛМ ЧЗ доступна на вкладке «Обзор касс».";
            panel.Controls.Add(_manualNextStepLabel, 0, 1);
            panel.SetColumnSpan(_manualNextStepLabel, 3);

            group.Controls.Add(panel);
            return group;
        }

        private GroupBox BuildManualInstancesGroup()
        {
            GroupBox group = CreateGroup("ККТ в ЕСМ и занятые порты");
            group.AutoSize = false;
            group.MinimumSize = new Size(0, 145);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.AutoSize = true;
            toolbar.WrapContents = false;
            toolbar.Margin = new Padding(0, 0, 0, 4);

            ConfigureButton(_refreshManualInstancesButton, "Обновить", CheckCurrentInstancesAsync);
            _refreshManualInstancesButton.MinimumSize = new Size(90, 25);
            _nextPortPairLabel.AutoSize = true;
            _nextPortPairLabel.Anchor = AnchorStyles.Left;
            _nextPortPairLabel.Margin = new Padding(8, 6, 0, 0);
            _nextPortPairLabel.Text =
                "Порты в форме — по умолчанию; обновите список для проверки занятости.";
            toolbar.Controls.Add(_refreshManualInstancesButton);
            toolbar.Controls.Add(_deleteKktButton);
            toolbar.Controls.Add(_nextPortPairLabel);

            ConfigureManualInstancesGrid();
            _manualInstancesSummaryLabel.AutoSize = true;
            _manualInstancesSummaryLabel.Text = "Список еще не загружен.";
            _manualInstancesSummaryLabel.Margin = new Padding(0, 4, 0, 0);

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(_manualInstancesGrid, 0, 1);
            root.Controls.Add(_manualInstancesSummaryLabel, 0, 2);
            group.Controls.Add(root);
            return group;
        }

        private void ConfigureManualInstancesGrid()
        {
            _manualInstancesGrid.Dock = DockStyle.Fill;
            _manualInstancesGrid.ReadOnly = true;
            _manualInstancesGrid.AllowUserToAddRows = false;
            _manualInstancesGrid.AllowUserToDeleteRows = false;
            _manualInstancesGrid.AllowUserToResizeRows = false;
            _manualInstancesGrid.MultiSelect = false;
            _manualInstancesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _manualInstancesGrid.RowHeadersVisible = false;
            _manualInstancesGrid.AutoGenerateColumns = false;
            _manualInstancesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _manualInstancesGrid.BackgroundColor = SystemColors.Window;
            _manualInstancesGrid.BorderStyle = BorderStyle.Fixed3D;
            _manualInstancesGrid.SelectionChanged += delegate { UpdateDeleteButtonState(); };

            _manualInstancesGrid.Columns.Add(CreateInstancesColumn("Роль", 130, 24F));
            _manualInstancesGrid.Columns.Add(CreateInstancesColumn("Серийный номер", 135, 30F));
            _manualInstancesGrid.Columns.Add(CreateInstancesColumn("Порт службы", 90, 14F));
            _manualInstancesGrid.Columns.Add(CreateInstancesColumn("Порт ПО", 76, 12F));
            _manualInstancesGrid.Columns.Add(CreateInstancesColumn("Состояние", 90, 18F));
        }


        private void ConfigureInstancesGrid()
        {
            _instancesGrid.Dock = DockStyle.Fill;
            _instancesGrid.ReadOnly = true;
            _instancesGrid.AllowUserToAddRows = false;
            _instancesGrid.AllowUserToDeleteRows = false;
            _instancesGrid.AllowUserToResizeRows = false;
            _instancesGrid.MultiSelect = false;
            _instancesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _instancesGrid.RowHeadersVisible = false;
            _instancesGrid.AutoGenerateColumns = false;
            _instancesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _instancesGrid.BackgroundColor = SystemColors.Window;
            _instancesGrid.BorderStyle = BorderStyle.Fixed3D;
            _instancesGrid.SelectionChanged += delegate { UpdateDeleteButtonState(); };

            _instancesGrid.Columns.Add(CreateInstancesColumn("Роль", 150, 24F));
            _instancesGrid.Columns.Add(CreateInstancesColumn("Серийный номер ККТ", 150, 30F));
            _instancesGrid.Columns.Add(CreateInstancesColumn("Порт службы", 90, 14F));
            _instancesGrid.Columns.Add(CreateInstancesColumn("Порт ПО", 76, 12F));
            _instancesGrid.Columns.Add(CreateInstancesColumn("Состояние", 100, 18F));
        }

        private static DataGridViewTextBoxColumn CreateInstancesColumn(string header, int minimumWidth, float fillWeight)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                MinimumWidth = minimumWidth,
                FillWeight = fillWeight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }


        private StatusStrip BuildStatusBar()
        {
            StatusStrip status = new StatusStrip();
            status.Dock = DockStyle.Fill;
            status.SizingGrip = false;

            _operationStatusLabel.Text = "Готово";
            _operationStatusLabel.Spring = true;
            _operationStatusLabel.TextAlign = ContentAlignment.MiddleLeft;

            status.Items.Add(_operationStatusLabel);
            return status;
        }

        private GroupBox BuildRecoveryGroup()
        {
            _recoveryGroup.Text = "Ошибка службы";
            _recoveryGroup.Dock = DockStyle.Fill;
            _recoveryGroup.AutoSize = true;
            _recoveryGroup.Padding = new Padding(6);
            _recoveryGroup.Margin = new Padding(0, 0, 0, 4);
            _recoveryGroup.Visible = false;

            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.AutoSize = true;
            table.ColumnCount = 1;
            table.RowCount = 2;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _recoveryLabel.AutoSize = true;
            _recoveryLabel.MaximumSize = new Size(720, 0);
            _recoveryLabel.Text = "ЕСМ не смог создать службу подключаемой ККТ автоматически.";
            _recoveryLabel.Margin = new Padding(0, 0, 0, 4);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.WrapContents = true;

            ConfigureButton(_createServiceButton, "Создать службу от администратора", CreateSelectedServiceAsync);
            ConfigureButton(_copyRecoveryCommandButton, "Скопировать команду", CopyRecoveryCommandAsync);
            buttons.Controls.Add(_createServiceButton);
            buttons.Controls.Add(_copyRecoveryCommandButton);

            table.Controls.Add(_recoveryLabel, 0, 0);
            table.Controls.Add(buttons, 0, 1);
            _recoveryGroup.Controls.Add(table);
            return _recoveryGroup;
        }

        private GroupBox BuildLogGroup()
        {
            GroupBox group = CreateGroup("Журнал выполнения");
            group.AutoSize = false;
            group.MinimumSize = new Size(0, 150);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.ColumnCount = 1;
            table.RowCount = 2;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Top;
            toolbar.AutoSize = true;
            toolbar.FlowDirection = FlowDirection.RightToLeft;
            toolbar.WrapContents = false;

            _copyDiagnosticsButton.Text = "Скопировать диагностику";
            OperatorTheme.Button(_copyDiagnosticsButton, false);
            _copyDiagnosticsButton.AutoSize = true;
            _copyDiagnosticsButton.Margin = new Padding(0, 0, 0, 3);
            _copyDiagnosticsButton.Click += CopyMaskedDiagnostics;
            ToolTip diagnosticsTip = new ToolTip();
            diagnosticsTip.SetToolTip(_copyDiagnosticsButton, "Копирует журнал с замаскированными ИНН, ФН и серийными номерами.");
            toolbar.Controls.Add(_copyDiagnosticsButton);

            _openLogFolderButton.Text = "Открыть папку журнала";
            OperatorTheme.Button(_openLogFolderButton, false);
            _openLogFolderButton.AutoSize = true;
            _openLogFolderButton.Margin = new Padding(0, 0, 6, 3);
            _openLogFolderButton.Click += delegate { OpenLogFolder(); };
            toolbar.Controls.Add(_openLogFolderButton);

            _logTextBox.Dock = DockStyle.Fill;
            _logTextBox.Multiline = true;
            _logTextBox.ScrollBars = ScrollBars.Both;
            _logTextBox.ReadOnly = true;
            _logTextBox.WordWrap = false;
            _logTextBox.HideSelection = false;
            _logTextBox.MinimumSize = new Size(0, 110);
            _logTextBox.Font = new Font("Consolas", 9F);
            _logTextBox.ForeColor = OperatorTheme.Ink;
            _logTextBox.BackColor = OperatorTheme.Surface;
            _logTextBox.BorderStyle = BorderStyle.FixedSingle;

            table.Controls.Add(toolbar, 0, 0);
            table.Controls.Add(_logTextBox, 0, 1);
            group.Controls.Add(table);
            group.Dock = DockStyle.Fill;
            return group;
        }
        private GroupBox CreateGroup(string text)
        {
            GroupBox group = new GroupBox();
            group.Text = text;
            group.ForeColor = OperatorTheme.Ink;
            group.BackColor = OperatorTheme.Surface;
            group.Dock = DockStyle.Fill;
            group.AutoSize = true;
            group.Padding = new Padding(6);
            group.Margin = new Padding(0, 0, 0, 4);
            return group;
        }


        private void ConfigureButton(Button button, string text, Func<Task> action)
        {
            OperatorTheme.Button(button, false);
            button.Text = text;
            button.AutoSize = true;
            button.Margin = new Padding(0, 0, 6, 4);
            button.Padding = Padding.Empty;
            button.Click += async delegate { await RunButtonActionAsync(action); };
            _actionButtons.Add(button);
        }

        private void ConfigureActionGridButton(Button button)
        {
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 6, 4);
            button.Padding = Padding.Empty;
        }

        private void FillDefaults()
        {
            _baseUrlTextBox.Text = TspiotDefaults.BaseUrl;
            _portTextBox.Text = TspiotDefaults.Port;
            _softPortTextBox.Text = TspiotDefaults.SoftPort;
            _dkktPortTextBox.Text = TspiotDefaults.DkktPort;
        }

        private async Task RunButtonActionAsync(Func<Task> action)
        {
            if (_busy)
            {
                return;
            }

            _mainOperationRunning = true;
            SetBusy(true);
            _operationStatusLabel.Text = "Выполняется операция...";
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }

                AppendLog("Ошибка операции: " + SensitiveDataMasker.Mask(ex.Message) + "\r\n\r\n");
                AppendDetailToFile("Подробности ошибки приложения:\r\n" + ex + "\r\n\r\n");
                ShowWizardFailure(ex);
            }
            finally
            {
                _mainOperationRunning = false;
                if (IsDisposed || Disposing)
                {
                    _busy = false;
                }
                else
                {
                    SetBusy(_lmOperationRunning);
                    _operationStatusLabel.Text = _busy ? "Выполняется операция..." : "Готово";
                    if (_closeAfterOperation && !_busy) BeginInvoke(new Action(Close));
                    ShowRegistrationSupportIfReady();
                }
            }
        }

        private async Task CheckCurrentInstancesAsync()
        {
            TspiotFormInput input = ReadEndpointInput();
            ValidationResult validation = TspiotInputValidator.ValidateForCheck(input);
            if (!ShowValidation(validation))
            {
                return;
            }
            if (!ConfirmWarnings(validation))
            {
                return;
            }

            ApiResponse response = await _client.GetInstancesAsync(input.BaseUrl);
            AppendResponse(response);
            AppendReadableInstances(response);
            UpdateInstancesGrid(response);
        }

        private async Task DeleteSelectedKktAsync()
        {
            LmGatewayKkt requested = _workspaceTabs.SelectedTab == _instancesTab ? _lmGatewayPage.SelectedKkt : null;
            KktDeletionCandidate serviceSelection = GetSelectedDeletionCandidate();
            string requestedId = requested == null ? serviceSelection == null || serviceSelection.Instance == null
                ? null : serviceSelection.Instance.Id : requested.InstanceId;
            if (string.IsNullOrEmpty(requestedId))
                throw new InvalidOperationException("Выберите ККТ в рабочем списке или в служебной таблице экземпляров ЕСМ.");
            KktDeletionCandidate refreshedCandidate = null;
            if (requestedId != null)
            {
                TspiotFormInput deleteEndpoint = ReadEndpointInput();
                ValidationResult endpointValidation = TspiotInputValidator.ValidateForCheck(deleteEndpoint);
                if (!endpointValidation.IsValid) throw new InvalidOperationException(endpointValidation.JoinMessages());
                if (!ConfirmWarnings(endpointValidation)) return;
                ApiResponse latest = await _client.GetInstancesAsync(deleteEndpoint.BaseUrl);
                IList<KktInstanceInfo> latestInstances;
                if (!latest.IsSuccess || !InstanceInfoParser.TryParse(latest, out latestInstances))
                    throw new InvalidOperationException("Не удалось получить актуальные экземпляры ЕСМ. Удаление не выполнялось.");
                AppendResponse(latest);
                UpdateInstancesGrid(latest);
                foreach (DataGridViewRow row in _instancesGrid.Rows)
                {
                    var fresh = row.Tag as KktDeletionCandidate;
                    if (fresh != null && fresh.Instance != null && fresh.Instance.Id == requestedId)
                    { _instancesGrid.CurrentCell = row.Cells[0]; refreshedCandidate = fresh; break; }
                }
            }
            KktDeletionCandidate candidate = refreshedCandidate;
            if (candidate == null || !candidate.CanDelete || candidate.Instance == null)
            {
                MessageBox.Show(
                    this,
                    "Выберите в таблице ККТ, доступную для удаления.",
                    "Удаление ККТ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            TspiotFormInput endpoint = ReadEndpointInput();
            ValidationResult validation = TspiotInputValidator.ValidateForCheck(endpoint);
            if (!ShowValidation(validation) || !ConfirmWarnings(validation))
            {
                return;
            }

            using (KktDeletionConfirmationDialog dialog = new KktDeletionConfirmationDialog(candidate.Instance, candidate.IsPrimary))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    AppendLog("Удаление ККТ отменено пользователем.\r\n\r\n");
                    return;
                }

                AppendLog("=== Удаление ККТ " + candidate.Instance.Id + " ===\r\n");
                KktDeletionOutcome outcome = await _deletionWorkflow.DeleteAsync(
                    endpoint.BaseUrl,
                    candidate.Instance.Id,
                    dialog.ConfirmationText,
                    HandleDeletionResponse,
                    System.Threading.CancellationToken.None);

                AppendLog(outcome.Message + "\r\n\r\n");
                if (outcome.IsSuccess)
                {
                    ForgetWizardKkt(candidate.Instance.Id);
                    _lmGatewayPage.InvalidateDestination();
                    await _lmGatewayPage.RefreshFromHostAsync();
                    MessageBox.Show(this, outcome.Message, "Удаление ККТ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                MessageBox.Show(
                    this,
                    outcome.Message,
                    outcome.IsBlocked ? "Удаление заблокировано" : "Удаление не подтверждено",
                    MessageBoxButtons.OK,
                    outcome.IsBlocked ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
            }
        }

        private void HandleDeletionResponse(ApiResponse response)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                Invoke(new Action<ApiResponse>(HandleDeletionResponse), response);
                return;
            }

            AppendResponse(response);
            if (response != null && string.Equals(response.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                AppendReadableInstances(response);
                UpdateInstancesGrid(response);
            }
        }

        private async Task AddSelectedInstanceAsync()
        {
            TspiotFormInput input = ReadInput();
            ValidationResult validation = TspiotInputValidator.ValidatePost(input);
            if (!ShowValidation(validation))
            {
                return;
            }
            if (!ConfirmWarnings(validation))
            {
                return;
            }

            bool canContinue = await ConfirmDuplicateIfNeededAsync(input);
            if (!canContinue)
            {
                return;
            }

            AddTspiotRequest addRequest = TspiotInputValidator.CreateAddRequest(input);
            AppendAddOperationDiagnostics(input, addRequest);

            ApiResponse response = await _client.AddInstanceAsync(input.BaseUrl, addRequest);
            AppendResponse(response);

            if (!response.IsSuccess && ServiceRecoveryCommandBuilder.IsManualServiceRecoveryError(response.ResponseBody))
            {
                PrepareServiceRecovery(input);
                return;
            }

            if (response.IsSuccess)
            {
                HideServiceRecovery();
                await RefreshInstancesAfterSuccessfulMutationAsync(input.BaseUrl);
            }

            ShowResultMessage(response);
        }

        private async Task LoadDkktDataAsync()
        {
            TspiotFormInput input = ReadInput();
            ValidationResult validation = TspiotInputValidator.ValidateForCheck(input);
            if (!ShowValidation(validation))
            {
                return;
            }
            if (!ConfirmWarnings(validation))
            {
                return;
            }

            ApiResponse instancesResponse = await _client.GetInstancesAsync(input.BaseUrl);
            AppendResponse(instancesResponse);
            AppendReadableInstances(instancesResponse);
            UpdateInstancesGrid(instancesResponse);

            IList<KktInstanceInfo> instances;
            if (!instancesResponse.IsSuccess ||
                !InstanceInfoParser.TryParse(instancesResponse, out instances))
            {
                MessageBox.Show(
                    this,
                    "Не удалось получить надежный список экземпляров ЕСМ. Выбор ККТ и расчет портов остановлены.",
                    "Выбор следующей ККТ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            ApiResponse response = await _client.GetDkktListAsync(input.BaseUrl);
            AppendResponse(response);
            if (!response.IsSuccess)
            {
                ShowResultMessage(response);
                return;
            }

            IList<DkktDeviceInfo> devices;
            if (!DkktListParser.TryParse(response.ResponseBody, out devices))
            {
                MessageBox.Show(
                    this,
                    "ЕСМ вернул неожиданный формат списка физических ККТ. Подстановка остановлена.",
                    "Выбор следующей ККТ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
            AppendDkktDevices(devices);

            if (devices.Count == 0)
            {
                MessageBox.Show(this, "Не удалось найти данные ККТ для подстановки.", "Заполнение данных", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            IList<DkktDeviceInfo> candidates = DkktDeviceSelector.FindDevicesWithoutInstances(devices, instances);
            AppendDkktCandidates(candidates);

            if (candidates.Count == 0)
            {
                MessageBox.Show(this, "Все найденные ККТ уже есть в списке экземпляров или кандидаты не определены.", "Выбор следующей ККТ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DkktDeviceInfo selectedDevice = SelectDkktDevice(candidates);
            if (selectedDevice == null)
            {
                AppendLog("Выбор следующей ККТ отменен пользователем.\r\n\r\n");
                return;
            }

            KktPortPair nextPair = new KktPortPairAllocator(instances).ReserveNext();
            if (nextPair == null)
            {
                MessageBox.Show(this, "Не найдено свободной пары портов в поддерживаемом диапазоне.", "Выбор следующей ККТ", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            FillFieldsFromDkkt(selectedDevice);
            _portTextBox.Text = nextPair.Port;
            _softPortTextBox.Text = nextPair.SoftPort;
            _atolConfirmedCheckBox.Checked = false;
            MessageBox.Show(
                this,
                "ККТ выбрана. Данные и свободная пара " + nextPair.Port + "/" + nextPair.SoftPort + " подставлены в форму. Проверьте их перед добавлением.",
                "Выбор следующей ККТ",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private async Task RegisterSelectedKktAsync()
        {
            TspiotFormInput input = ReadInput();
            ValidationResult validation = TspiotInputValidator.ValidatePut(input, _atolConfirmedCheckBox.Checked);
            if (!ShowValidation(validation))
            {
                return;
            }
            if (!ConfirmWarnings(validation))
            {
                return;
            }

            ApiResponse response = await _client.RegisterInstanceAsync(input.BaseUrl, TspiotInputValidator.CreateRegisterRequest(input));
            AppendResponse(response);
            if (response.IsSuccess)
            {
                await RefreshInstancesAfterSuccessfulMutationAsync(input.BaseUrl);
            }
            ShowTspiotIdIfPresent(response);
            ShowResultMessage(response);
            if (response.IsSuccess)
            {
                _registrationSupportPending = true;
                FinishRegistrationProcess();
            }
        }

        private DkktDeviceInfo SelectDkktDevice(IList<DkktDeviceInfo> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            using (KktSelectionDialog dialog = new KktSelectionDialog(candidates))
            {
                return dialog.ShowDialog(this) == DialogResult.OK
                    ? dialog.SelectedDevice
                    : null;
            }
        }

        private async Task RefreshInstancesAfterSuccessfulMutationAsync(string baseUrl)
        {
            ApiResponse instancesResponse = await _client.GetInstancesAsync(baseUrl);
            AppendResponse(instancesResponse);
            AppendReadableInstances(instancesResponse);
            UpdateInstancesGrid(instancesResponse);
        }


        private void UpdateAutomaticInstallerSelection()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            string lmPath = _lmGatewayPage.SelectedLocalModuleInstallerPath;
            _automaticLmInstallerTextBox.Text = string.IsNullOrWhiteSpace(lmPath)
                ? "Не выбран"
                : lmPath;
            _automaticToolTip.SetToolTip(_automaticLmInstallerTextBox, lmPath);
            if (!_busy)
            {
                _automationStatusLabel.Text = "Статус: готово к регистрации";
            }
        }

        private async Task ConfigureAllKktsAutomaticallyAsync()
        {
            _registrationSupportPending = _registrationProcessFinished = false;
            bool fmuApiMode = _lmGatewayPage.FmuApiMode;
            string baseUrl = (_baseUrlTextBox.Text ?? string.Empty).Trim();
            string dkktPort = (_dkktPortTextBox.Text ?? string.Empty).Trim();
            ValidationResult settingsValidation = TspiotInputValidator.ValidateBulkSettings(baseUrl, dkktPort);
            if (!ShowValidation(settingsValidation) || !ConfirmWarnings(settingsValidation))
            {
                _automationStatusLabel.Text = "Статус: запуск отменен";
                return;
            }

            ClearWizardPending();
            _wizardAutomaticPending.Clear();
            _wizardPartialSetup = false;
            _wizardRegistrationWarning = string.Empty;
            _wizardResult.Visible = false;
            _automaticCancellation = new System.Threading.CancellationTokenSource();
            _automaticStopButton.Enabled = true;
            try
            {
                AppendLog(
                    fmuApiMode ? "=== Подготовка ККТ и ЛМ ЧЗ для FMU-API ===\r\n"
                    : "=== Полная автоматическая настройка ККТ, контроллеров и ЛМ ЧЗ ===\r\n");
                _wizardRegistrationWarning = string.Empty;
                System.Threading.CancellationToken token =
                    _automaticCancellation.Token;
                _automationStatusLabel.Text =
                    "Статус: поиск подключенных и зарегистрированных ККТ...";
                KktCandidateScan scan = await DiscoverKktCandidatesAsync(baseUrl, dkktPort, token);
                BulkRegistrationDiscovery discovery = scan.Discovery;
                SequentialKktRegistrationCoordinator sequentialCoordinator = scan.Coordinator;
                SequentialKktDiscovery sequentialDiscovery = scan.Sequential;
                AppendBulkDiscovery(discovery);

                IList<LmGatewayKkt> registered =
                    await _lmGatewayPage
                        .DiscoverRegisteredKktsForAutomaticPlanAsync(
                            baseUrl,
                            token);
                _manualScan = scan;
                FillDiscoveredKkts(scan, registered, false);
                ShowWizardStage(2, "Регистрируем кассы в ЕСМ", "Каждая касса настраивается по очереди.");
                Dictionary<string, BulkRegistrationWorkItem> workBySerial =
                    new Dictionary<string, BulkRegistrationWorkItem>(
                        StringComparer.Ordinal);
                Dictionary<string, int> workOrdinalBySerial =
                    new Dictionary<string, int>(StringComparer.Ordinal);
                BulkRegistrationOutcome registrationOutcome =
                    new BulkRegistrationOutcome();
                CopyBulkResults(
                    discovery.InitialResults,
                    registrationOutcome.Results);
                IList<LmGatewayKkt> candidates = BuildCompleteSetupCandidates(
                    registered,
                    discovery,
                    workBySerial,
                    workOrdinalBySerial,
                    registrationOutcome.Results);
                if (candidates.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Не найдено ни одной ККТ с корректными данными для полной настройки.");
                }

                HashSet<string> attempted =
                    new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < candidates.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    string serial = candidates[index].KktSerial;
                    BulkRegistrationWorkItem work;
                    if (!workBySerial.TryGetValue(serial, out work))
                    {
                        continue;
                    }
                    attempted.Add(serial);
                    _automationStatusLabel.Text =
                        "Статус: регистрация ККТ " + serial + "...";
                    ShowWizardStage(2, "Регистрируем ККТ " + serial, "Другие кассы ожидают своей очереди.");
                    SelectWizardRow(serial);
                    SetManualRowStatus(serial, "Регистрируется");
                    BulkKktRegistrationResult itemResult;
                    try
                    {
                        if (sequentialCoordinator != null)
                        {
                            SequentialKktRegistrationTarget target =
                                sequentialDiscovery.FindBySerial(serial);
                            if (target == null)
                            {
                                throw new InvalidOperationException(
                                    "Для ККТ " + serial +
                                    " не найден ранее проверенный VCOM.");
                            }
                            itemResult = await sequentialCoordinator
                                .ExecuteWithTargetAsync(
                                    baseUrl,
                                    target,
                                    delegate(
                                        System.Threading.CancellationToken
                                            registrationToken)
                                    {
                                        return _bulkWorkflow.ExecuteItemAsync(
                                            work,
                                            workOrdinalBySerial[serial],
                                            discovery.Items.Count,
                                            HandleBulkProgress,
                                            registrationToken);
                                    },
                                    HandleBulkProgress,
                                    token);
                        }
                        else
                        {
                            itemResult = await _bulkWorkflow.ExecuteItemAsync(
                                work,
                                workOrdinalBySerial[serial],
                                discovery.Items.Count,
                                HandleBulkProgress,
                                token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        registrationOutcome.Cancelled = true;
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AppendDetailToFile(
                            "Подробности ошибки регистрации ККТ " + serial +
                            ":\r\n" + ex + "\r\n\r\n");
                        itemResult = new BulkKktRegistrationResult
                        {
                            KktSerial = serial,
                            Status = BulkKktRegistrationStatus.InspectionFailed,
                            Details = SensitiveDataMasker.Mask(ex.Message)
                        };
                    }
                    registrationOutcome.Results.Add(itemResult);
                    AppendBulkResult(itemResult);
                    SetManualRowStatus(serial,
                        itemResult.Status == BulkKktRegistrationStatus.Registered ||
                        itemResult.Status == BulkKktRegistrationStatus.RecoveredRegistration ||
                        itemResult.Status == BulkKktRegistrationStatus.AlreadyExists ? "Зарегистрирована" : "Ошибка регистрации");
                }
                AddUnattemptedBulkResults(
                    discovery,
                    workBySerial,
                    attempted,
                    registrationOutcome.Results,
                    "операция не была начата");

                string finalVcomError = string.Empty;
                if (sequentialCoordinator != null)
                {
                    _automationStatusLabel.Text =
                        "Статус: итоговая проверка всех VCOM ККТ...";
                    try
                    {
                        bool allVisible = await sequentialCoordinator.VerifyAllAsync(
                            baseUrl,
                            sequentialDiscovery,
                            HandleBulkProgress,
                            token);
                        if (!allVisible)
                        {
                            finalVcomError =
                                "Итоговая проверка не увидела в dkktList все сопоставленные ККТ.";
                            AppendLog(finalVcomError + "\r\n");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        finalVcomError =
                            "Итоговая VCOM-проверка не завершена: " +
                            SensitiveDataMasker.Mask(ex.Message);
                        AppendLog(finalVcomError + "\r\n");
                        AppendDetailToFile(
                            "Подробности итоговой VCOM-проверки:\r\n" + ex +
                            "\r\n\r\n");
                    }
                }

                IList<LmGatewayKkt> registeredAfter =
                    await _lmGatewayPage
                        .DiscoverRegisteredKktsForAutomaticPlanAsync(
                            baseUrl,
                            token);
                string unconfirmedWarning;
                IList<LmGatewayKkt> bounded = BoundWizardRegistrationTargets(candidates, registeredAfter, out unconfirmedWarning);
                bool registrationHasFailures = registrationOutcome.Cancelled ||
                    HasBulkFailures(registrationOutcome.Results) || !string.IsNullOrWhiteSpace(finalVcomError) ||
                    !string.IsNullOrEmpty(unconfirmedWarning);
                _wizardRegistrationWarning = registrationHasFailures
                    ? BuildAutomaticSetupCompletionMessage(registrationOutcome,
                        "Регистрация завершена не полностью. " + finalVcomError + " " + unconfirmedWarning) : string.Empty;
                _wizardResult.Text = _wizardRegistrationWarning;
                _wizardResult.Visible = registrationHasFailures;
                if (bounded.Count > 0) ShowWizardLmStep(bounded, true);
                else ShowWizardStage(2, "Регистрация не завершена", "Смотрите результат и журнал; выполненные действия сохранены.", true);
                if (registrationHasFailures)
                    _wizardMessage.Text = "Есть ошибки регистрации. ЛМ можно настроить для подтверждённых касс; подробности ниже.";
                _automationStatusLabel.Text = registrationHasFailures ? "Требуется внимание" : "Регистрация завершена";
                if (!registrationHasFailures && bounded.Count > 0)
                    foreach (BulkKktRegistrationResult result in registrationOutcome.Results)
                        if (result.Status == BulkKktRegistrationStatus.Registered ||
                            result.Status == BulkKktRegistrationStatus.RecoveredRegistration)
                            _registrationSupportPending = true;
            }
            catch (OperationCanceledException)
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }
                _automationStatusLabel.Text = "Статус: автоматическая настройка остановлена";
                AppendLog("Автоматическая настройка остановлена. Уже выполненные действия сохранены.\r\n\r\n");
                ShowWizardFailure(new OperationCanceledException());
            }
            finally
            {
                _automaticStopButton.Enabled = false;
                if (_automaticCancellation != null)
                {
                    _automaticCancellation.Dispose();
                    _automaticCancellation = null;
                }
            }
        }

        private async Task ConfigureFrontolAsync()
        {
            TspiotFormInput input = ReadEndpointInput();
            ValidationResult validation = TspiotInputValidator.ValidateForCheck(input);
            if (!ShowValidation(validation) || !ConfirmWarnings(validation) ||
                !ConfirmFrontolClosed()) return;

            System.Threading.CancellationToken token =
                System.Threading.CancellationToken.None;
            string baseUrl = input.BaseUrl;
            AppendLog("=== Настройка Frontol ===\r\n");
            _operationStatusLabel.Text = "Чтение Frontol.ini и ККТ текущего РМ...";
            FrontolDatabase database = new FrontolDatabase(
                FrontolConfiguration.LoadSettings(
                    FrontolConfiguration.DefaultIniPath));
            IList<FrontolDevice> devices = await Task.Run(
                delegate { return database.ReadDevices(token); });
            if (devices.Count == 0)
                throw new InvalidOperationException(
                    "На текущем рабочем месте Frontol не найдены ККТ.");
            AppendLog("Frontol.ini прочитан. ККТ текущего РМ: " +
                devices.Count + ".\r\n");

            _operationStatusLabel.Text =
                "Проверка физических ККТ и регистрации в ЕСМ...";
            var coordinator = new SequentialKktRegistrationCoordinator(
                _client,
                new AtolFptrConnectionProvider());
            SequentialKktDiscovery discovery =
                await DiscoverSequentialKktsWithRetryAsync(
                    coordinator,
                    baseUrl,
                    token);
            if (discovery == null) return;
            IList<LmGatewayKkt> registered = await _lmGatewayPage
                .DiscoverRegisteredKktsForAutomaticPlanAsync(baseUrl, token);
            var identities = new List<KktConnectionIdentity>();
            foreach (SequentialKktRegistrationTarget target in discovery.Targets)
                identities.Add(target.Identity);
            List<FrontolChange> plan = FrontolConfiguration.BuildPlan(
                devices,
                identities,
                registered,
                baseUrl);

            StringBuilder confirmation = new StringBuilder();
            confirmation.AppendLine(
                "Проверены физические ККТ, их регистрация и порты ЕСМ.");
            confirmation.AppendLine();
            bool hasChanges = false;
            foreach (FrontolChange change in plan)
            {
                confirmation.Append("• ").Append(change.Device.Name)
                    .Append(" (").Append(change.ComPort).Append(", № ")
                    .Append(change.KktSerial).Append("): ");
                bool changed = !string.Equals(
                    change.OldHost,
                    change.NewHost,
                    StringComparison.Ordinal) || change.OldPort != change.NewPort;
                if (changed)
                {
                    hasChanges = true;
                    confirmation.Append(change.OldHost ?? "<не задан>")
                        .Append(":")
                        .Append(change.OldPort.HasValue
                            ? change.OldPort.Value.ToString()
                            : "<не задан>")
                        .Append(" → ");
                }
                confirmation.Append(change.NewHost).Append(":")
                    .Append(change.NewPort);
                confirmation.AppendLine(changed ? string.Empty : " (без изменений)");
            }
            confirmation.AppendLine();
            confirmation.Append(hasChanges
                ? "Записать изменения в Frontol? Прежние значения будут сохранены для отката."
                : "Изменения не требуются. Повторно проверить значения в базе Frontol?");
            if (MessageBox.Show(
                this,
                confirmation.ToString(),
                "Настройка Frontol",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                AppendLog("Запись настроек Frontol отменена оператором.\r\n\r\n");
                return;
            }
            if (!ConfirmFrontolClosed()) return;

            string backup;
            _frontolWriteInProgress = true;
            try { backup = await Task.Run(delegate { return database.Apply(plan, token); }); }
            finally { _frontolWriteInProgress = false; }
            foreach (FrontolChange change in plan)
                AppendLog("Frontol «" + change.Device.Name + "»: " + change.ComPort +
                    " → " + change.NewHost + ":" + change.NewPort + "\r\n");
            if (!string.IsNullOrEmpty(backup)) AppendLog("Снимок для отката Frontol: " + backup + "\r\n");
            string result = "Настройки " + plan.Count + " ККТ проверены в базе. " +
                (string.IsNullOrEmpty(backup) ? "Изменения не потребовались." :
                "Прежние значения сохранены для отката. Запустите Frontol заново для применения.");
            AppendLog(result + "\r\n\r\n");
            MessageBox.Show(this, result, "Frontol", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private bool ConfirmFrontolClosed()
        {
            while (true)
            {
                bool open = false;
                foreach (string name in new[] { "Frontol", "Frontol_Demo", "FrontolAdmin" })
                {
                    foreach (Process process in Process.GetProcessesByName(name))
                    {
                        using (process) { open = true; }
                    }
                }
                if (!open) return true;
                if (MessageBox.Show(this,
                    "Закройте Frontol и Frontol Администратор на время настройки, затем нажмите «Повторить». " +
                    "Программа не завершает их принудительно.",
                    "Настройка Frontol", MessageBoxButtons.RetryCancel, MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button2) != DialogResult.Retry) return false;
            }
        }

        private async Task RestoreFrontolAsync()
        {
            using (var picker = new OpenFileDialog { Title = "Снимок настроек Frontol",
                Filter = "Снимок Frontol (*.json)|*.json", CheckFileExists = true,
                InitialDirectory = FrontolDatabase.BackupDirectory })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                if (MessageBox.Show(this,
                    "Восстановить прежние адреса и порты ЕСМ? Если база, РМ или настройки ККТ изменились после записи, " +
                    "откат будет отменён целиком.", "Откат настроек Frontol",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes ||
                    !ConfirmFrontolClosed()) return;
                var database = new FrontolDatabase(FrontolConfiguration.LoadSettings(FrontolConfiguration.DefaultIniPath));
                _frontolWriteInProgress = true;
                try { await Task.Run(delegate { database.Restore(picker.FileName, System.Threading.CancellationToken.None); }); }
                finally { _frontolWriteInProgress = false; }
                AppendLog("Прежние настройки ЕСМ в Frontol восстановлены и проверены. Запустите Frontol заново.\r\n");
                MessageBox.Show(this, "Настройки восстановлены и проверены в базе. Запустите Frontol заново.",
                    "Frontol", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private async Task<SequentialKktDiscovery>
            DiscoverSequentialKktsWithRetryAsync(
                SequentialKktRegistrationCoordinator coordinator,
                string baseUrl,
                System.Threading.CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SequentialKktDiscovery discovery =
                    await coordinator.DiscoverAsync(
                        baseUrl,
                        HandleBulkProgress,
                        cancellationToken);
                if (discovery.IsValid)
                {
                    AppendLog(
                        "VCOM-карта АТОЛ построена: " +
                        discovery.Targets.Count.ToString() + " ККТ.\r\n");
                    return discovery;
                }

                string reason = string.IsNullOrWhiteSpace(
                    discovery.ErrorMessage)
                    ? "Не удалось построить VCOM-карту ККТ."
                    : discovery.ErrorMessage;
                AppendLog(
                    "VCOM-проверка остановлена: " +
                    SensitiveDataMasker.Mask(reason) + "\r\n");
                DialogResult answer = MessageBox.Show(
                    this,
                    reason +
                        "\r\n\r\nЕсли Frontol или «Тест драйвера ККТ» " +
                        "открыты, закройте их на время регистрации. " +
                        "Программа не завершает их сама.\r\n\r\n" +
                        "Нажмите «Повторить» после освобождения касс.",
                    "ККТ заняты другой программой",
                    MessageBoxButtons.RetryCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Retry)
                {
                    return null;
                }
            }
        }

        private static IList<DkktDeviceInfo> CopySequentialDevices(
            SequentialKktDiscovery discovery)
        {
            List<DkktDeviceInfo> devices = new List<DkktDeviceInfo>();
            if (discovery == null) return devices;
            for (int index = 0; index < discovery.Targets.Count; index++)
            {
                SequentialKktRegistrationTarget target =
                    discovery.Targets[index];
                DkktDeviceInfo source = target == null
                    ? null
                    : target.Device;
                if (source == null) continue;
                devices.Add(new DkktDeviceInfo
                {
                    KktSerial = source.KktSerial,
                    FnSerial = source.FnSerial,
                    KktInn = source.KktInn,
                    KktRnm = source.KktRnm,
                    ModelName = source.ModelName,
                    DkktVersion = source.DkktVersion,
                    Developer = source.Developer,
                    Manufacturer = source.Manufacturer,
                    ShiftState = source.ShiftState
                });
            }
            return devices;
        }

        private static IList<LmGatewayKkt> BuildCompleteSetupCandidates(
            IList<LmGatewayKkt> registered,
            BulkRegistrationDiscovery discovery,
            IDictionary<string, BulkRegistrationWorkItem> workBySerial,
            IDictionary<string, int> workOrdinalBySerial,
            IList<BulkKktRegistrationResult> initialResults)
        {
            Dictionary<string, LmGatewayKkt> bySerial =
                new Dictionary<string, LmGatewayKkt>(StringComparer.Ordinal);
            if (registered != null)
            {
                for (int index = 0; index < registered.Count; index++)
                {
                    AddCompleteSetupCandidate(bySerial, registered[index]);
                }
            }
            for (int index = 0; index < discovery.Items.Count; index++)
            {
                BulkRegistrationWorkItem work = discovery.Items[index];
                if (work == null || work.Item == null ||
                    work.Item.Input == null || work.Item.Validation == null)
                {
                    continue;
                }
                string serial = (work.Item.Input.KktSerial ?? string.Empty).Trim();
                if (!work.Item.Validation.IsValid)
                {
                    initialResults.Add(new BulkKktRegistrationResult
                    {
                        KktSerial = serial,
                        Status = BulkKktRegistrationStatus.InvalidData,
                        Details = work.Item.Validation.JoinMessages()
                    });
                    continue;
                }
                if (workBySerial.ContainsKey(serial))
                {
                    throw new InvalidOperationException(
                        "План регистрации повторяет ККТ " + serial + ".");
                }
                workBySerial.Add(serial, work);
                workOrdinalBySerial.Add(serial, index + 1);
                AddCompleteSetupCandidate(
                    bySerial,
                    CreateGatewayKkt(work.Item.Input));
            }

            List<LmGatewayKkt> result =
                new List<LmGatewayKkt>(bySerial.Values);
            result.Sort(delegate(LmGatewayKkt left, LmGatewayKkt right)
            {
                return string.CompareOrdinal(left.KktSerial, right.KktSerial);
            });
            return result;
        }

        private static void AddCompleteSetupCandidate(
            IDictionary<string, LmGatewayKkt> bySerial,
            LmGatewayKkt candidate)
        {
            string serial = candidate == null
                ? string.Empty
                : (candidate.KktSerial ?? string.Empty).Trim();
            if (serial.Length == 0)
            {
                return;
            }
            LmGatewayKkt existing;
            if (bySerial.TryGetValue(serial, out existing))
            {
                if (!string.Equals(
                    (existing.KktInn ?? string.Empty).Trim(),
                    (candidate.KktInn ?? string.Empty).Trim(),
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Для ККТ " + serial + " обнаружены разные ИНН.");
                }
                return;
            }
            bySerial.Add(serial, candidate);
        }

        private static LmGatewayKkt CreateGatewayKkt(TspiotFormInput input)
        {
            return new LmGatewayKkt
            {
                InstanceId = (input.KktSerial ?? string.Empty).Trim(),
                KktSerial = (input.KktSerial ?? string.Empty).Trim(),
                KktInn = (input.KktInn ?? string.Empty).Trim(),
                FnSerial = (input.FnSerial ?? string.Empty).Trim(),
                Port = (input.Port ?? string.Empty).Trim(),
                SoftPort = (input.SoftPort ?? string.Empty).Trim(),
                DkktPort = (input.DkktPort ?? string.Empty).Trim(),
                ServiceState = string.Empty
            };
        }

        private static void CopyBulkResults(
            IList<BulkKktRegistrationResult> source,
            IList<BulkKktRegistrationResult> target)
        {
            if (source == null) return;
            for (int index = 0; index < source.Count; index++)
            {
                target.Add(source[index]);
            }
        }

        private static void AddUnattemptedBulkResults(
            BulkRegistrationDiscovery discovery,
            IDictionary<string, BulkRegistrationWorkItem> workBySerial,
            ISet<string> attempted,
            IList<BulkKktRegistrationResult> results,
            string stopReason)
        {
            for (int index = 0; index < discovery.Items.Count; index++)
            {
                BulkRegistrationWorkItem work = discovery.Items[index];
                string serial = work == null || work.Item == null ||
                    work.Item.Input == null
                    ? string.Empty
                    : (work.Item.Input.KktSerial ?? string.Empty).Trim();
                if (serial.Length > 0 && workBySerial.ContainsKey(serial) &&
                    !attempted.Contains(serial))
                {
                    results.Add(new BulkKktRegistrationResult
                    {
                        KktSerial = serial,
                        Status = BulkKktRegistrationStatus.Cancelled,
                        Details = string.IsNullOrWhiteSpace(stopReason)
                            ? "не начато: причина остановки не передана"
                            : "не начато: " + stopReason
                    });
                }
            }
        }


        private void AppendBulkResult(BulkKktRegistrationResult result)
        {
            if (result != null)
            {
                AppendLog(result.FormatLogLine() + "\r\n");
            }
        }

        private void HandleBulkProgress(BulkRegistrationProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            if (progress.Response != null)
            {
                AppendResponse(progress.Response);
            }

            if (!string.IsNullOrWhiteSpace(progress.Stage))
            {
                string prefix = progress.Current > 0 && progress.Total > 0
                    ? (progress.CountsAttempts ? "Попытка " : "ККТ ") +
                        progress.Current.ToString() + " из " +
                        progress.Total.ToString() + "; "
                    : string.Empty;
                AppendLog(prefix +
                    (string.IsNullOrWhiteSpace(progress.KktSerial) ? string.Empty : "serial=" + progress.KktSerial + "; ") +
                    progress.Stage +
                    (string.IsNullOrWhiteSpace(progress.Message) ? string.Empty : "; " + progress.Message) +
                    "\r\n");
            }
        }

        private void AppendBulkDiscovery(BulkRegistrationDiscovery discovery)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("План массовой регистрации:");
            for (int i = 0; i < discovery.InitialResults.Count; i++)
            {
                builder.AppendLine(discovery.InitialResults[i].FormatLogLine());
            }

            for (int i = 0; i < discovery.Items.Count; i++)
            {
                BulkRegistrationWorkItem workItem = discovery.Items[i];
                BulkKktRegistrationItem item = workItem.Item;
                builder.AppendLine(
                    (workItem.RequiresAdd ? "ДОБАВИТЬ+PUT: " : "ТОЛЬКО PUT: ") +
                    "kktSerial=" + item.Input.KktSerial +
                    "; fnSerial=" + item.Input.FnSerial +
                    "; kktInn=" + item.Input.KktInn +
                    "; port=" + item.Input.Port +
                    "; softPort=" + item.Input.SoftPort +
                    "; dkktPort=" + item.Input.DkktPort);
                if (!item.Validation.IsValid)
                {
                    builder.AppendLine("  Ошибки: " + item.Validation.JoinMessages().Replace("\r\n", "; "));
                }
                if (item.Validation.Warnings.Count > 0)
                {
                    builder.AppendLine("  Предупреждения: " + item.Validation.JoinWarnings().Replace("\r\n", "; "));
                }
            }

            builder.AppendLine();
            AppendLog(builder.ToString());
        }


        private bool HasBulkFailures(IList<BulkKktRegistrationResult> results)
        {
            for (int i = 0; i < results.Count; i++)
            {
                BulkKktRegistrationStatus status = results[i].Status;
                if (status == BulkKktRegistrationStatus.InspectionFailed ||
                    status == BulkKktRegistrationStatus.InvalidData ||
                    status == BulkKktRegistrationStatus.AddFailed ||
                    status == BulkKktRegistrationStatus.RegistrationFailed ||
                    status == BulkKktRegistrationStatus.Cancelled)
                {
                    return true;
                }
            }

            return false;
        }
        private async Task CreateSelectedServiceAsync()
        {
            TspiotFormInput input = ReadInput();
            ValidationResult validation = TspiotInputValidator.ValidatePost(input);
            if (!ShowValidation(validation))
            {
                return;
            }
            if (!ConfirmWarnings(validation))
            {
                return;
            }

            PrepareServiceRecovery(input);
            if (string.IsNullOrEmpty(_lastControlModulePath))
            {
                MessageBox.Show(
                    this,
                    "Не найден controlModule.exe. Команда скопирована в журнал и доступна по кнопке \"Скопировать команду\". Найдите путь установки ЕСМ и замените строку $exe вручную.",
                    "Создание службы",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult answer = MessageBox.Show(
                this,
                "Будет запущен PowerShell от имени администратора и создана/запущена служба " + ServiceRecoveryCommandBuilder.BuildServiceName(input.KktSerial) + ". Продолжить?",
                "Создание службы",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                AppendLog("Создание службы отменено пользователем.\r\n\r\n");
                return;
            }

            string scriptPath = WriteRecoveryScriptFile(BuildRecoveryScript(input));
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "powershell.exe";
            startInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuoteArgument(scriptPath);
            startInfo.UseShellExecute = true;
            startInfo.Verb = "runas";

            try
            {
                Process process = Process.Start(startInfo);
                if (process != null)
                {
                    await Task.Run(delegate { process.WaitForExit(); });
                    AppendLog("PowerShell завершился с кодом " + process.ExitCode.ToString() + ". Повторная проверка списка ККТ.\r\n\r\n");
                }
            }
            catch (Exception ex)
            {
                AppendLog("Не удалось запустить PowerShell от администратора: " + ex.Message + "\r\n\r\n");
                MessageBox.Show(this, "Не удалось запустить PowerShell от администратора: " + ex.Message, "Создание службы", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ApiResponse response = await _client.GetInstancesAsync(input.BaseUrl);
            AppendResponse(response);
            AppendReadableInstances(response);
        }

        private Task CopyRecoveryCommandAsync()
        {
            TspiotFormInput input = ReadInput();
            ValidationResult validation = TspiotInputValidator.ValidatePost(input);
            if (!ShowValidation(validation) || !ConfirmWarnings(validation))
            {
                return Task.FromResult(0);
            }

            Clipboard.SetText(BuildRecoveryScript(input));
            AppendLog("PowerShell-команда создания службы скопирована в буфер обмена.\r\n\r\n");
            MessageBox.Show(this, "Команда скопирована в буфер обмена.", "Создание службы", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.FromResult(0);
        }

        private async Task<bool> ConfirmDuplicateIfNeededAsync(TspiotFormInput input)
        {
            ApiResponse response = await _client.GetInstancesAsync(input.BaseUrl);
            AppendResponse(response);
            AppendReadableInstances(response);

            if (!response.IsSuccess)
            {
                ShowResultMessage(response);
                return false;
            }

            IList<KktInstanceInfo> instances;
            if (!InstanceInfoParser.TryParse(response, out instances))
            {
                AppendLog("Операция остановлена: ответ /instances/info не соответствует ожидаемому формату.\r\n\r\n");
                MessageBox.Show(
                    this,
                    "ЕСМ вернул неожиданный формат списка ККТ. Добавление остановлено, чтобы не изменить конфигурацию вслепую. Подробности смотрите в журнале.",
                    "Проверка ответа ЕСМ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            bool duplicateFound = false;
            foreach (KktInstanceInfo instance in instances)
            {
                if (string.Equals(instance.Id, input.KktSerial, StringComparison.Ordinal))
                {
                    duplicateFound = true;
                    break;
                }
            }
            if (!duplicateFound)
            {
                return true;
            }

            DialogResult answer = MessageBox.Show(
                this,
                "ККТ с таким id уже есть в списке. Повторное добавление может вызвать ошибку 1010. Продолжить?",
                "Повторное добавление",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            return answer == DialogResult.Yes;
        }

        /// <summary>
        /// Скрипт строится из полей формы в момент обращения. Кэш прежней
        /// попытки переживал смену ККТ: блок «Ошибка службы» оставался
        /// открытым, оператор правил серийник и порты, а кнопка копирования
        /// отдавала команду для предыдущей кассы.
        /// </summary>
        private string BuildRecoveryScript(TspiotFormInput input)
        {
            _lastControlModulePath = ServiceRecoveryCommandBuilder.FindControlModulePath();
            return ServiceRecoveryCommandBuilder.BuildPowerShellScript(
                input.KktSerial,
                input.Port,
                input.SoftPort,
                _lastControlModulePath);
        }

        private void PrepareServiceRecovery(TspiotFormInput input)
        {
            string script = BuildRecoveryScript(input);

            string pathText = string.IsNullOrEmpty(_lastControlModulePath)
                ? "controlModule.exe не найден в стандартных папках. Команду можно скопировать и поправить путь $exe вручную."
                : "Найден controlModule.exe: " + _lastControlModulePath;

            _recoveryLabel.Text =
                "ЕСМ вернул ошибку службы: служба подключаемой ККТ не создана или не запущена. " +
                "Можно создать службу " + ServiceRecoveryCommandBuilder.BuildServiceName(input.KktSerial) +
                " от имени администратора. " + pathText;
            _recoveryGroup.Visible = true;

            AppendLog("Подготовлен аварийный сценарий восстановления службы.\r\n");
            AppendLog(pathText + "\r\n");
            AppendLog("PowerShell-команда:\r\n" + script + "\r\n");

            MessageBox.Show(
                this,
                "ЕСМ не смог создать или запустить службу подключаемой ККТ. Используйте блок \"Ошибка службы\" в форме: можно запустить восстановление службы от администратора или скопировать команду.",
                "Ошибка службы",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void AppendAddOperationDiagnostics(TspiotFormInput input, AddTspiotRequest request)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Диагностика перед добавлением экземпляра подключаемой ККТ:");
            builder.AppendLine("baseUrl=" + input.BaseUrl);
            builder.AppendLine("serviceName=" + ServiceRecoveryCommandBuilder.BuildServiceName(input.KktSerial));
            builder.AppendLine("id=" + request.Id);
            builder.AppendLine("port=" + request.Port.ToString());
            builder.AppendLine("softPort=" + request.SoftPort.ToString());
            builder.AppendLine("dkktPort=" + request.DkktPort.ToString());
            builder.AppendLine("Ожидаемый POST: " + JsonHelper.Serialize(request));
            builder.AppendLine("Если ЕСМ вернёт 1012/1013, используйте блок \"Ошибка службы\". PowerShell сохранит подробный лог в папку TEMP.");
            builder.AppendLine();
            AppendLog(builder.ToString());
        }

        private void HideServiceRecovery()
        {
            _recoveryGroup.Visible = false;
            _lastControlModulePath = string.Empty;
        }

        private string WriteRecoveryScriptFile(string script)
        {
            string fileName = "esm_tspiot_service_recovery_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".ps1";
            string path = Path.Combine(Path.GetTempPath(), fileName);
            string wrappedScript =
                script +
                "\r\nWrite-Host \"\"\r\n" +
                "Read-Host \"Нажмите Enter, чтобы закрыть окно\"";
            File.WriteAllText(path, wrappedScript, Encoding.UTF8);
            AppendLog("PowerShell-скрипт записан: " + path + "\r\n\r\n");
            return path;
        }

        private string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private void CopyMaskedDiagnostics(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(DiagnosticMasker.Mask(_logTextBox.Text));
                AppendLog("Маскированная диагностика скопирована в буфер обмена.\r\n\r\n");
                MessageBox.Show(this, "Диагностика скопирована. ИНН, ФН, серийные номера и локальные пути замаскированы.",
                    "Диагностика", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось скопировать диагностику: " + ex.Message,
                    "Диагностика", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                string directory = Path.GetDirectoryName(_fileLogSink.LogFilePath);
                OpenPath(directory);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть папку журнала: " + ex.Message,
                    "Журнал", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenInstruction()
        {
            try
            {
                string directory = AppDomain.CurrentDomain.BaseDirectory;
                string path = InstructionFileSelector.SelectAvailable(directory);
                if (string.IsNullOrEmpty(path))
                {
                    MessageBox.Show(this,
                        "Инструкция не найдена рядом с программой.",
                        "Инструкция",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                OpenPath(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть инструкцию: " + ex.Message,
                    "Инструкция", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void OpenPath(string path)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }

        private void ShowSupportDevelopment()
        {
            using (SupportDevelopmentDialog dialog =
                new SupportDevelopmentDialog())
            {
                dialog.ShowDialog(this);
            }
        }

        private void FinishRegistrationProcess()
        {
            _registrationProcessFinished = _registrationSupportPending;
            ShowRegistrationSupportIfReady();
        }

        private void ShowRegistrationSupportIfReady()
        {
            if (!_registrationProcessFinished || _busy || _closeAfterOperation || IsDisposed || Disposing) return;
            _registrationSupportPending = _registrationProcessFinished = false;
            ShowSupportDevelopment();
        }

        private static string BuildAutomaticSetupCompletionMessage(
            BulkRegistrationOutcome registrationOutcome,
            string stackSummary)
        {
            StringBuilder message = new StringBuilder();
            message.Append("Автоматическая настройка завершена.\r\n\r\n");
            message.Append(BulkKktRegistrationResult.FormatSummary(
                registrationOutcome.Results));
            message.Append("\r\n\r\n");
            message.Append(stackSummary);
            message.Append(
                "\r\n\r\nРезультаты настройки и порты кассового ПО показаны " +
                "на вкладке «Обзор касс».");
            return message.ToString();
        }


        private void ShowAbout()
        {
            MessageBox.Show(
                this,
                Text + "\r\nУправление ККТ в ЕСМ/ТС ПИоТ\r\n\r\n" +
                    "Ручное последовательное подключение ККТ, автоматическая обработка нескольких ККТ и безопасное удаление дополнительных экземпляров.\r\n\r\n" +
                    "Издатель и владелец: KRS\r\n" +
                    "Разработчик: Руслан Керусов\r\n" +
                    "Copyright © 2026 KRS. Все права защищены.\r\n" +
                    "github.com/jadieify-hub/esm-tspiot-multi-kkt-tool",
                "О программе",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private TspiotFormInput ReadInput()
        {
            return new TspiotFormInput
            {
                BaseUrl = _baseUrlTextBox.Text,
                KktSerial = _kktSerialTextBox.Text,
                FnSerial = _fnSerialTextBox.Text,
                KktInn = _kktInnTextBox.Text,
                Port = _portTextBox.Text,
                SoftPort = _softPortTextBox.Text,
                DkktPort = _dkktPortTextBox.Text
            };
        }

        private TspiotFormInput ReadEndpointInput()
        {
            return new TspiotFormInput
            {
                BaseUrl = _baseUrlTextBox.Text
            };
        }

        private bool ShowValidation(ValidationResult result)
        {
            if (result.IsValid)
            {
                return true;
            }

            string message = result.JoinMessages();
            AppendLog("Проверка данных не пройдена:\r\n" + message + "\r\n\r\n");
            MessageBox.Show(this, message, "Проверка данных", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private bool ConfirmWarnings(ValidationResult result)
        {
            if (result.Warnings.Count == 0)
            {
                return true;
            }

            string message = result.JoinWarnings() + "\r\n\r\nПродолжить?";
            AppendLog("Предупреждение:\r\n" + result.JoinWarnings() + "\r\n\r\n");
            DialogResult answer = MessageBox.Show(
                this,
                message,
                "Проверьте данные",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                AppendLog("Операция отменена пользователем после предупреждения.\r\n\r\n");
                return false;
            }

            return true;
        }

        private void AppendResponse(ApiResponse response)
        {
            if (response == null)
            {
                AppendLog("ЕСМ не вернул ответ.\r\n\r\n");
                UpdateConnectionStatus(null);
                return;
            }

            AppendLog(LogFormatter.Format(response));
            UpdateConnectionStatus(response);
        }

        private void UpdateInstancesGrid(ApiResponse response)
        {
            _instancesGrid.Rows.Clear();
            _manualInstancesGrid.Rows.Clear();

            IList<KktInstanceInfo> instances;
            if (response == null || !response.IsSuccess ||
                !InstanceInfoParser.TryParse(response, out instances))
            {
                _instancesSummaryLabel.Text = response != null && response.IsSuccess
                    ? "ЕСМ вернул неожиданный формат списка экземпляров."
                    : "Не удалось загрузить список экземпляров.";
                _manualInstancesSummaryLabel.Text = _instancesSummaryLabel.Text;
                _nextPortPairLabel.Text = "Следующая свободная пара: недоступна";
                UpdateDeleteButtonState();
                return;
            }

            KktDeletionPlan plan = KktDeletionPlanner.Build(instances);
            int deletableCount = 0;
            for (int i = 0; i < plan.Candidates.Count; i++)
            {
                KktDeletionCandidate candidate = plan.Candidates[i];
                KktInstanceInfo instance = candidate.Instance ?? new KktInstanceInfo();
                string role;
                if (candidate.CanDelete)
                {
                    deletableCount++;
                }

                if (candidate.IsPrimary && candidate.CanDelete)
                {
                    role = "Первая ККТ (полное подтверждение)";
                }
                else if (candidate.IsPrimary)
                {
                    role = "Первая ККТ (удаление заблокировано)";
                }
                else if (candidate.CanDelete)
                {
                    role = "Дополнительная ККТ";
                }
                else
                {
                    role = "Защищена";
                }

                AddInstanceGridRow(_instancesGrid, candidate, role, true);
                AddInstanceGridRow(_manualInstancesGrid, candidate, role, true);
            }

            _instancesGrid.ClearSelection();
            _manualInstancesGrid.ClearSelection();
            KktPortPair nextPair = new KktPortPairAllocator(instances).ReserveNext();
            _nextPortPairLabel.Text = nextPair == null
                ? "Следующая свободная пара: не найдена"
                : "Следующая свободная пара: " + nextPair.Port + " / " + nextPair.SoftPort;
            _manualInstancesSummaryLabel.Text = instances.Count == 0
                ? "Экземпляры ККТ не найдены."
                : "Экземпляров в ЕСМ: " + instances.Count.ToString() + ".";
            if (instances.Count == 0)
            {
                _instancesSummaryLabel.Text = "Экземпляры ККТ не найдены.";
            }
            else if (!plan.HasReliablePrimary)
            {
                _instancesSummaryLabel.Text = "Экземпляров: " + instances.Count.ToString() + ". Удаление заблокировано: первая ККТ не определена однозначно.";
            }
            else
            {
                _instancesSummaryLabel.Text = "Экземпляров: " + instances.Count.ToString() + ". Доступно для удаления: " + deletableCount.ToString() + ".";
            }

            UpdateDeleteButtonState();
        }

        private static void AddInstanceGridRow(
            DataGridView grid,
            KktDeletionCandidate candidate,
            string role,
            bool storeCandidate)
        {
            KktInstanceInfo instance = candidate.Instance ?? new KktInstanceInfo();
            int rowIndex = grid.Rows.Add(
                role,
                instance.Id,
                instance.Port,
                instance.SoftPort,
                KktServiceStateFormatter.ToDisplayText(instance.ServiceState));
            DataGridViewRow row = grid.Rows[rowIndex];
            if (storeCandidate)
            {
                row.Tag = candidate;
            }
            if (!candidate.CanDelete)
            {
                for (int cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    row.Cells[cellIndex].ToolTipText = candidate.ProtectionReason;
                }
            }
            if (candidate.IsPrimary)
            {
                row.DefaultCellStyle.BackColor = SystemColors.ControlLight;
            }
            else if (!candidate.CanDelete)
            {
                row.DefaultCellStyle.ForeColor = OperatorTheme.Error;
            }
        }

        private KktDeletionCandidate GetSelectedDeletionCandidate()
        {
            if (_workspaceTabs.SelectedTab != _automationTab || !WizardManual || _wizardLmEditing || !_manualFallbackToggle.Checked ||
                _manualInstancesGrid.SelectedRows.Count != 1)
            {
                return null;
            }

            return _manualInstancesGrid.SelectedRows[0].Tag as KktDeletionCandidate;
        }

        private void UpdateDeleteButtonState()
        {
            KktDeletionCandidate candidate = GetSelectedDeletionCandidate();
            bool enabled = !_busy && candidate != null && candidate.CanDelete;
            _deleteKktButton.Enabled = enabled;
            _deleteMenuItem.Enabled = enabled || (!_busy && _workspaceTabs.SelectedTab == _instancesTab && _lmGatewayPage.SelectedKkt != null);
        }

        private void UpdateConnectionStatus(ApiResponse response)
        {
            if (response == null || response.IsConnectionFailure || response.StatusCode == 0)
            {
                _connectionStatusLabel.Text = "ЕСМ: нет связи";
                _connectionStatusLabel.ForeColor = OperatorTheme.Error;
                return;
            }

            _connectionStatusLabel.Text = "ЕСМ: доступен";
            _connectionStatusLabel.ForeColor = OperatorTheme.Green;
        }

        private void AppendReadableInstances(ApiResponse response)
        {
            if (response == null || string.IsNullOrEmpty(response.ResponseBody))
            {
                return;
            }

            IList<KktInstanceInfo> instances = InstanceInfoParser.Parse(response.ResponseBody);
            if (instances.Count == 0)
            {
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Найденные экземпляры:");
            for (int i = 0; i < instances.Count; i++)
            {
                KktInstanceInfo item = instances[i];
                builder.AppendLine(
                    "id=" + item.Id +
                    "; port=" + item.Port +
                    "; softPort=" + item.SoftPort +
                    "; dkktPort=" + item.DkktPort +
                    "; serviceState=" + item.ServiceState);
            }

            builder.AppendLine();
            AppendLog(builder.ToString());
        }

        private void AppendDkktDevices(IList<DkktDeviceInfo> devices)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Данные найденных ККТ:");
            if (devices.Count == 0)
            {
                builder.AppendLine("Данные ККТ не найдены.");
            }

            for (int i = 0; i < devices.Count; i++)
            {
                DkktDeviceInfo item = devices[i];
                builder.AppendLine(
                    "ККТ #" + (i + 1).ToString() +
                    "; kktSerial=" + item.KktSerial +
                    "; fnSerial=" + item.FnSerial +
                    "; kktInn=" + item.KktInn +
                    "; modelName=" + item.ModelName +
                    "; dkktVersion=" + item.DkktVersion);
            }

            builder.AppendLine();
            AppendLog(builder.ToString());
        }

        private void AppendDkktCandidates(IList<DkktDeviceInfo> devices)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Кандидаты для ручного подключения:");
            if (devices.Count == 0)
            {
                builder.AppendLine("Нет кандидатов.");
            }

            for (int i = 0; i < devices.Count; i++)
            {
                DkktDeviceInfo item = devices[i];
                builder.AppendLine(
                    "Кандидат #" + (i + 1).ToString() +
                    "; kktSerial=" + item.KktSerial +
                    "; fnSerial=" + item.FnSerial +
                    "; kktInn=" + item.KktInn +
                    "; modelName=" + item.ModelName);
            }

            builder.AppendLine();
            AppendLog(builder.ToString());
        }

        private void FillFieldsFromDkkt(DkktDeviceInfo device)
        {
            if (!string.IsNullOrWhiteSpace(device.KktSerial))
            {
                _kktSerialTextBox.Text = device.KktSerial;
            }

            if (!string.IsNullOrWhiteSpace(device.FnSerial))
            {
                _fnSerialTextBox.Text = device.FnSerial;
            }

            if (!string.IsNullOrWhiteSpace(device.KktInn))
            {
                _kktInnTextBox.Text = device.KktInn;
            }
        }

        private void ShowTspiotIdIfPresent(ApiResponse response)
        {
            if (response == null || string.IsNullOrEmpty(response.ResponseBody))
            {
                return;
            }

            string tspiotId = ExtractSimpleJsonString(response.ResponseBody, "tspiotId");
            if (!string.IsNullOrEmpty(tspiotId))
            {
                MessageBox.Show(this, "Регистрация выполнена. tspiotId: " + tspiotId, "Регистрация", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private string ExtractSimpleJsonString(string json, string propertyName)
        {
            string marker = "\"" + propertyName + "\"";
            int propertyIndex = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (propertyIndex < 0)
            {
                return string.Empty;
            }

            int colonIndex = json.IndexOf(':', propertyIndex + marker.Length);
            if (colonIndex < 0)
            {
                return string.Empty;
            }

            int start = colonIndex + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
            {
                start++;
            }

            if (start >= json.Length)
            {
                return string.Empty;
            }

            if (json[start] == '"')
            {
                int end = json.IndexOf('"', start + 1);
                return end > start ? json.Substring(start + 1, end - start - 1) : string.Empty;
            }

            int valueEnd = start;
            while (valueEnd < json.Length && json[valueEnd] != ',' && json[valueEnd] != '}')
            {
                valueEnd++;
            }

            return json.Substring(start, valueEnd - start).Trim();
        }

        private void ShowResultMessage(ApiResponse response)
        {
            if (response == null)
            {
                return;
            }

            if (response.IsSuccess)
            {
                MessageBox.Show(this, response.DecodedMessage, "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, response.DecodedMessage, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AppendLog(string text)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            string value = text ?? string.Empty;
            if (_logTextBox.TextLength > MaximumVisibleLogLength)
            {
                _logTextBox.Text = DisplayLogTrimmer.TrimIfNeeded(
                    _logTextBox.Text,
                    MaximumVisibleLogLength);
            }
            _logTextBox.AppendText(value);
            _logTextBox.Select(_logTextBox.TextLength, 0);
            _logTextBox.ScrollToCaret();

            if (!_fileLogSink.TryAppend(value) && !_fileLogErrorShown)
            {
                _fileLogErrorShown = true;
                _logTextBox.AppendText("Не удалось записать файловый журнал. Работа программы продолжена.\r\n\r\n");
            }
        }

        // Стек исключения нужен для разбора полевого сбоя, но не на экране:
        // оператору хватает сообщения. В файл он уходит с тем же
        // маскированием, что и остальная диагностика.
        private void AppendDetailToFile(string text)
        {
            _fileLogSink.TryAppend(DiagnosticMasker.Mask(text));
        }

        private void ConstrainToWorkingArea()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int width = Math.Min(Width, Math.Max(320, area.Width - 8));
            int height = Math.Min(Height, Math.Max(300, area.Height - 8));
            MinimumSize = new Size(
                Math.Min(MinimumSize.Width, width),
                Math.Min(MinimumSize.Height, height));
            Size = new Size(width, height);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            foreach (TextBox input in new[] { _baseUrlTextBox, _dkktPortTextBox, _kktSerialTextBox,
                _fnSerialTextBox, _kktInnTextBox, _portTextBox, _softPortTextBox })
                input.Enabled = !busy;
            _discoveredKktsGrid.Enabled = !busy;
            for (int i = 0; i < _actionButtons.Count; i++)
            {
                _actionButtons[i].Enabled = !busy;
            }

            _operationsMenuItem.Enabled = !busy;
            _fmuApiCheckBox.Enabled = !busy;
            _lmGatewayPage.SetHostBusy(busy);
            if (!busy) InvalidateWizardDestination();
            UpdateDeleteButtonState();
            UpdateWizardActions();
        }

        private void OnLmGatewayOperationStateChanged(bool busy)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            _lmOperationRunning = busy;
            SetBusy(busy || _mainOperationRunning);
            _operationStatusLabel.Text = _busy
                ? "Выполняется операция с ЛМ ЧЗ..."
                : "Готово";
            if (_closeAfterOperation && !_busy) BeginInvoke(new Action(Close));
        }
    }
}
