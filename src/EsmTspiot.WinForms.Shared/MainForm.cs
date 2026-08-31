using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.WinForms.Shared
{
    public sealed class MainForm : Form
    {
        private const int MaximumVisibleLogLength = 1500000;
        private readonly TspiotApiClient _client = new TspiotApiClient();
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
        private readonly Button _automaticSelectInstallerButton = new Button();
        private readonly TextBox _automaticInstallerTextBox = new TextBox();
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
        private readonly TabControl _workspaceTabs = new TabControl();
        private readonly TabPage _manualKktTab = new TabPage();
        private readonly TabPage _instancesTab = new TabPage();
        private readonly TabPage _automationTab = new TabPage();
        private readonly TabPage _lmGatewayTab = new TabPage();
        private readonly TabPage _logTab = new TabPage();
        private readonly ToolStripStatusLabel _operationStatusLabel = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel _connectionStatusLabel = new ToolStripStatusLabel();
        private readonly ToolStripMenuItem _operationsMenuItem = new ToolStripMenuItem("ККТ");
        private readonly ToolStripMenuItem _deleteMenuItem = new ToolStripMenuItem("Удалить выбранную дополнительную ККТ");
        private readonly GroupBox _recoveryGroup = new GroupBox();
        private readonly Label _recoveryLabel = new Label();
        private readonly Button _createServiceButton = new Button();
        private readonly Button _copyRecoveryCommandButton = new Button();
        private readonly IList<Button> _actionButtons = new List<Button>();
        private string _lastRecoveryScript = string.Empty;
        private string _lastControlModulePath = string.Empty;
        private bool _fileLogErrorShown;
        private bool _busy;
        private System.Threading.CancellationTokenSource _automaticCancellation;

        public MainForm()
        {
            _bulkWorkflow = new BulkRegistrationWorkflow(_client);
            _deletionWorkflow = new KktDeletionWorkflow(_client);
            _lmGatewayPage = new LmGatewayPage(
                delegate { return _baseUrlTextBox.Text; },
                _client,
                AppendLog);
            _lmGatewayPage.OperationStateChanged += OnLmGatewayOperationStateChanged;
            _lmGatewayPage.InstallerSelectionChanged += UpdateAutomaticInstallerSelection;
            Text = "Управление ККТ в ЕСМ/ТС ПИоТ";
            ClientSize = new Size(780, 650);
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 8.25F);
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            BuildLayout();
            FillDefaults();
            Shown += delegate { ConstrainToWorkingArea(); };
            FormClosing += delegate
            {
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
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            MenuStrip menu = BuildMenu();
            root.Controls.Add(menu, 0, 0);
            root.Controls.Add(BuildConnectionBar(), 0, 1);
            root.Controls.Add(BuildWorkspaceTabs(), 0, 2);
            root.Controls.Add(BuildStatusBar(), 0, 3);

            MainMenuStrip = menu;
            Controls.Add(root);
        }

        private MenuStrip BuildMenu()
        {
            MenuStrip menu = new MenuStrip();
            menu.Dock = DockStyle.Fill;

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
                await RunButtonActionAsync(CheckCurrentInstancesAsync);
            };
            ToolStripMenuItem fillNext = new ToolStripMenuItem("Выбрать следующую ККТ");
            fillNext.Click += async delegate
            {
                _workspaceTabs.SelectedTab = _manualKktTab;
                await RunButtonActionAsync(LoadDkktDataAsync);
            };
            ToolStripMenuItem automatic = new ToolStripMenuItem("Автоматическая настройка");
            automatic.Click += delegate { _workspaceTabs.SelectedTab = _automationTab; };
            ToolStripMenuItem lmGateways = new ToolStripMenuItem("ЛМ ЧЗ");
            lmGateways.Click += delegate { _workspaceTabs.SelectedTab = _lmGatewayTab; };
            _deleteMenuItem.Enabled = false;
            _deleteMenuItem.Click += async delegate { await RunButtonActionAsync(DeleteSelectedKktAsync); };
            _operationsMenuItem.DropDownItems.Add(refresh);
            _operationsMenuItem.DropDownItems.Add(fillNext);
            _operationsMenuItem.DropDownItems.Add(automatic);
            _operationsMenuItem.DropDownItems.Add(lmGateways);
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
            return menu;
        }

        private Control BuildConnectionBar()
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Fill;
            bar.Height = 38;
            bar.MinimumSize = new Size(0, 38);
            bar.MaximumSize = new Size(int.MaxValue, 38);
            bar.BackColor = SystemColors.ControlLightLight;

            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.AutoSize = false;
            table.Padding = new Padding(8, 5, 8, 5);
            table.BackColor = SystemColors.ControlLightLight;
            table.ColumnCount = 6;
            table.RowCount = 1;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label addressLabel = new Label();
            addressLabel.Text = "Адрес ЕСМ";
            addressLabel.AutoSize = true;
            addressLabel.Anchor = AnchorStyles.Left;
            addressLabel.Margin = new Padding(0, 3, 6, 3);

            _baseUrlTextBox.Dock = DockStyle.Fill;
            _baseUrlTextBox.Margin = new Padding(0, 2, 12, 2);
            ToolTip connectionTip = new ToolTip();
            connectionTip.SetToolTip(_baseUrlTextBox, "Адрес сервиса ЕСМ/ТС ПИоТ, обычно http://127.0.0.1:51077");

            Label dkktLabel = new Label();
            dkktLabel.Text = "dkktPort";
            dkktLabel.AutoSize = true;
            dkktLabel.Anchor = AnchorStyles.Left;
            dkktLabel.Margin = new Padding(0, 3, 6, 3);

            _dkktPortTextBox.Dock = DockStyle.Fill;
            _dkktPortTextBox.Margin = new Padding(0, 2, 0, 2);
            connectionTip.SetToolTip(
                _dkktPortTextBox,
                "Порт оркестра ДККТ для ЕСМ, обычно 4042. 4041 — порт службы АТОЛ для ККМ.");

            ConfigureButton(_checkButton, "Проверить", CheckCurrentInstancesAsync);
            _checkButton.Margin = new Padding(0);
            _checkButton.MinimumSize = new Size(92, 24);

            table.Controls.Add(addressLabel, 0, 0);
            table.Controls.Add(_baseUrlTextBox, 1, 0);
            table.Controls.Add(dkktLabel, 2, 0);
            table.Controls.Add(_dkktPortTextBox, 3, 0);
            table.Controls.Add(new Panel(), 4, 0);
            table.Controls.Add(_checkButton, 5, 0);
            bar.Controls.Add(table);
            return bar;
        }

        private Control BuildWorkspaceTabs()
        {
            _workspaceTabs.Dock = DockStyle.Fill;
            _workspaceTabs.Margin = new Padding(6, 4, 6, 4);

            ConfigureTabPage(_manualKktTab, "Ручное подключение");
            ConfigureTabPage(_instancesTab, "ККТ в ЕСМ");
            ConfigureTabPage(_automationTab, "Автоматическая настройка");
            ConfigureTabPage(_lmGatewayTab, "ЛМ ЧЗ");
            ConfigureTabPage(_logTab, "Журнал");

            _manualKktTab.Controls.Add(BuildManualKktPage());
            _instancesTab.Controls.Add(BuildInstancesPage());
            _automationTab.Controls.Add(BuildAutomationPage());
            _lmGatewayTab.Controls.Add(_lmGatewayPage);
            _logTab.Controls.Add(BuildLogGroup());

            _workspaceTabs.TabPages.Add(_automationTab);
            _workspaceTabs.TabPages.Add(_manualKktTab);
            _workspaceTabs.TabPages.Add(_instancesTab);
            _workspaceTabs.TabPages.Add(_lmGatewayTab);
            _workspaceTabs.TabPages.Add(_logTab);
            _workspaceTabs.SelectedTab = _automationTab;
            _workspaceTabs.Selected += async delegate
            {
                if (_workspaceTabs.SelectedTab == _lmGatewayTab && !_busy)
                {
                    await _lmGatewayPage.RefreshIfNeededAsync();
                }
            };
            return _workspaceTabs;
        }

        private static void ConfigureTabPage(TabPage page, string text)
        {
            page.Text = text;
            page.Padding = new Padding(6);
            page.UseVisualStyleBackColor = true;
            page.AutoScroll = true;
        }

        private Control BuildManualKktPage()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.AutoScroll = true;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(BuildKktGroup(), 0, 0);
            root.Controls.Add(BuildPortsGroup(), 0, 1);
            root.Controls.Add(BuildManualKktActionsGroup(), 0, 2);
            root.Controls.Add(BuildRecoveryGroup(), 0, 3);
            root.Controls.Add(BuildManualInstancesGroup(), 0, 4);
            return root;
        }

        private GroupBox BuildKktGroup()
        {
            GroupBox group = CreateGroup("Данные подключаемой ККТ");
            TableLayoutPanel table = CreateTwoColumnTable(4);
            AddLabeledTextBox(table, 0, "Серийный номер подключаемой ККТ", _kktSerialTextBox, "Используется как id и kktSerial");
            AddLabeledTextBox(table, 1, "Номер ФН подключаемой ККТ", _fnSerialTextBox, "fnSerial");
            AddLabeledTextBox(table, 2, "ИНН владельца ККТ", _kktInnTextBox, "10 или 12 цифр");

            _atolConfirmedCheckBox.Text = "Я проверил связь в драйвере АТОЛ именно с подключаемой физической ККТ";
            _atolConfirmedCheckBox.AutoSize = true;
            table.Controls.Add(new Label(), 0, 3);
            table.Controls.Add(_atolConfirmedCheckBox, 1, 3);

            group.Controls.Add(table);
            return group;
        }

        private GroupBox BuildPortsGroup()
        {
            GroupBox group = CreateGroup("Порты экземпляра");
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.AutoSize = true;
            table.ColumnCount = 4;
            table.RowCount = 1;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            AddCompactPortTextBox(table, 0, "Порт службы (port)", _portTextBox, "Порт экземпляра сервиса подключаемой ККТ");
            AddCompactPortTextBox(table, 2, "Порт кассового ПО (softPort)", _softPortTextBox, "Порт для Frontol или другой кассовой программы");
            group.Controls.Add(table);
            return group;
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
                "После шага 3 перейдите на вкладку «ЛМ ЧЗ» — там создаются " +
                "контроллер и локальный модуль.";
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

            _manualInstancesGrid.Columns.Add(CreateInstancesColumn("Роль", 130, 24F));
            _manualInstancesGrid.Columns.Add(CreateInstancesColumn("Серийный номер", 135, 30F));
            _manualInstancesGrid.Columns.Add(CreateInstancesColumn("Порт службы", 90, 14F));
            _manualInstancesGrid.Columns.Add(CreateInstancesColumn("Порт ПО", 76, 12F));
            _manualInstancesGrid.Columns.Add(CreateInstancesColumn("Состояние", 90, 18F));
        }

        private Control BuildInstancesPage()
        {
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
            toolbar.Margin = new Padding(0, 0, 0, 6);

            ConfigureButton(_refreshInstancesButton, "Обновить список", CheckCurrentInstancesAsync);
            ConfigureButton(_deleteKktButton, "Удалить дополнительную ККТ", DeleteSelectedKktAsync);
            _refreshInstancesButton.MinimumSize = new Size(120, 27);
            _deleteKktButton.MinimumSize = new Size(190, 27);
            _deleteKktButton.Enabled = false;
            toolbar.Controls.Add(_refreshInstancesButton);
            toolbar.Controls.Add(_deleteKktButton);

            ConfigureInstancesGrid();

            _instancesSummaryLabel.AutoSize = true;
            _instancesSummaryLabel.Text = "Список еще не загружен.";
            _instancesSummaryLabel.Margin = new Padding(0, 6, 0, 0);

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(_instancesGrid, 0, 1);
            root.Controls.Add(_instancesSummaryLabel, 0, 2);
            return root;
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

        private Control BuildAutomationPage()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            GroupBox group = CreateGroup("Полная автоматическая настройка");
            TableLayoutPanel commands = new TableLayoutPanel();
            commands.Dock = DockStyle.Fill;
            commands.AutoSize = true;
            commands.ColumnCount = 3;
            commands.RowCount = 4;
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            commands.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            commands.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            commands.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            commands.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label installerLabel = new Label();
            installerLabel.Text = "Установщик контроллера ЛМ ЧЗ";
            installerLabel.AutoSize = true;
            installerLabel.Anchor = AnchorStyles.Left;
            installerLabel.Margin = new Padding(0, 5, 8, 3);

            _automaticInstallerTextBox.ReadOnly = true;
            _automaticInstallerTextBox.Dock = DockStyle.Fill;
            _automaticInstallerTextBox.Margin = new Padding(0, 2, 6, 4);
            _automaticInstallerTextBox.Text = "Не выбран";
            _automaticLmInstallerTextBox.ReadOnly = true;
            _automaticLmInstallerTextBox.Dock = DockStyle.Fill;
            _automaticLmInstallerTextBox.Margin = new Padding(0, 2, 6, 4);
            _automaticLmInstallerTextBox.Text = "Не выбран";
            ConfigureButton(
                _automaticSelectInstallerButton,
                "Выбрать оба…",
                SelectAutomaticInstallerAsync);
            _automaticSelectInstallerButton.Tag = "ControllerInstallerPicker";

            Label localModuleInstallerLabel = new Label();
            localModuleInstallerLabel.Text = "MSI локального модуля ЧЗ";
            localModuleInstallerLabel.AutoSize = true;
            localModuleInstallerLabel.Anchor = AnchorStyles.Left;
            localModuleInstallerLabel.Margin = new Padding(0, 5, 8, 3);

            ConfigureButton(
                _bulkRegisterButton,
                "Настроить всё автоматически",
                ConfigureAllKktsAutomaticallyAsync);
            _bulkRegisterButton.Tag = "EndToEndAutomaticSetup";
            _bulkRegisterButton.MinimumSize = new Size(235, 36);

            _automaticStopButton.Text = "Остановить";
            _automaticStopButton.AutoSize = true;
            _automaticStopButton.Enabled = false;
            _automaticStopButton.Margin = new Padding(0, 2, 0, 4);
            _automaticStopButton.Tag = "CancelEndToEndAutomaticSetup";
            _automaticStopButton.Click += delegate
            {
                if (_automaticCancellation != null)
                {
                    _automaticCancellation.Cancel();
                    _automaticStopButton.Enabled = false;
                    _automationStatusLabel.Text = "Статус: остановка после текущей безопасной операции...";
                }
            };

            _automationStatusLabel.AutoSize = true;
            _automationStatusLabel.Anchor = AnchorStyles.Left;
            _automationStatusLabel.Text = "Статус: нужны оба пакета";
            _automationStatusLabel.Margin = new Padding(8, 6, 0, 3);

            Label hint = new Label();
            hint.AutoSize = false;
            hint.Dock = DockStyle.Fill;
            hint.MinimumSize = new Size(0, 48);
            hint.Text =
                "Программа найдёт ККТ через драйвер АТОЛ, зарегистрирует их в ЕСМ, " +
                "создаст контроллеры и ЛМ ЧЗ, затем привяжет в ЕСМ каждый контроллер к своей ККТ.";
            hint.Margin = new Padding(0, 5, 0, 0);

            commands.Controls.Add(installerLabel, 0, 0);
            commands.Controls.Add(_automaticInstallerTextBox, 1, 0);
            commands.Controls.Add(_automaticSelectInstallerButton, 2, 0);
            commands.Controls.Add(localModuleInstallerLabel, 0, 1);
            commands.Controls.Add(_automaticLmInstallerTextBox, 1, 1);
            commands.Controls.Add(_bulkRegisterButton, 0, 2);
            commands.Controls.Add(_automationStatusLabel, 1, 2);
            commands.Controls.Add(_automaticStopButton, 2, 2);
            commands.Controls.Add(hint, 0, 3);
            commands.SetColumnSpan(hint, 3);
            group.Controls.Add(commands);
            root.Controls.Add(group, 0, 0);
            return root;
        }

        private StatusStrip BuildStatusBar()
        {
            StatusStrip status = new StatusStrip();
            status.Dock = DockStyle.Fill;
            status.SizingGrip = false;

            _operationStatusLabel.Text = "Готово";
            _operationStatusLabel.Spring = true;
            _operationStatusLabel.TextAlign = ContentAlignment.MiddleLeft;

            _connectionStatusLabel.Text = "ЕСМ: не проверено";
            _connectionStatusLabel.BorderSides = ToolStripStatusLabelBorderSides.Left;
            _connectionStatusLabel.Margin = new Padding(6, 3, 0, 2);

            status.Items.Add(_operationStatusLabel);
            status.Items.Add(_connectionStatusLabel);
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
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.FlowDirection = FlowDirection.RightToLeft;
            toolbar.WrapContents = false;

            _copyDiagnosticsButton.Text = "Скопировать диагностику";
            _copyDiagnosticsButton.AutoSize = true;
            _copyDiagnosticsButton.Margin = new Padding(0, 0, 0, 3);
            _copyDiagnosticsButton.Click += CopyMaskedDiagnostics;
            ToolTip diagnosticsTip = new ToolTip();
            diagnosticsTip.SetToolTip(_copyDiagnosticsButton, "Копирует журнал с замаскированными ИНН, ФН и серийными номерами.");
            toolbar.Controls.Add(_copyDiagnosticsButton);

            _openLogFolderButton.Text = "Открыть папку журнала";
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
            _logTextBox.Font = new Font("Consolas", 8.25F);

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
            group.Dock = DockStyle.Fill;
            group.AutoSize = true;
            group.Padding = new Padding(6);
            group.Margin = new Padding(0, 0, 0, 4);
            return group;
        }

        private TableLayoutPanel CreateTwoColumnTable(int rows)
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.AutoSize = true;
            table.ColumnCount = 2;
            table.RowCount = rows;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < rows; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            return table;
        }

        private void AddLabeledTextBox(TableLayoutPanel table, int row, string labelText, TextBox textBox, string hint)
        {
            Label label = new Label();
            label.Text = labelText;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(0, 3, 6, 3);

            textBox.Dock = DockStyle.Fill;
            textBox.Margin = new Padding(0, 2, 0, 2);
            textBox.Tag = hint;

            ToolTip toolTip = new ToolTip();
            toolTip.SetToolTip(textBox, hint);
            toolTip.SetToolTip(label, hint);

            table.Controls.Add(label, 0, row);
            table.Controls.Add(textBox, 1, row);
        }

        private void AddCompactPortTextBox(TableLayoutPanel table, int column, string labelText, TextBox textBox, string hint)
        {
            Label label = new Label();
            label.Text = labelText;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(0, 3, 4, 3);

            textBox.Dock = DockStyle.Fill;
            textBox.Margin = new Padding(0, 2, 12, 2);
            textBox.Tag = hint;

            ToolTip toolTip = new ToolTip();
            toolTip.SetToolTip(textBox, hint);
            toolTip.SetToolTip(label, hint);

            table.Controls.Add(label, column, 0);
            table.Controls.Add(textBox, column + 1, 0);
        }

        private void ConfigureButton(Button button, string text, Func<Task> action)
        {
            button.Text = text;
            button.AutoSize = true;
            button.Margin = new Padding(0, 0, 6, 4);
            button.Padding = new Padding(6, 2, 6, 2);
            button.Click += async delegate { await RunButtonActionAsync(action); };
            _actionButtons.Add(button);
        }

        private void ConfigureActionGridButton(Button button)
        {
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 6, 4);
            button.Padding = new Padding(4, 1, 4, 1);
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

                AppendLog("Ошибка приложения: " + ex.Message + "\r\n\r\n");
                MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (IsDisposed || Disposing)
                {
                    _busy = false;
                }
                else
                {
                    SetBusy(false);
                    _operationStatusLabel.Text = "Готово";
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
            KktDeletionCandidate candidate = GetSelectedDeletionCandidate();
            if (candidate == null || !candidate.CanDelete || candidate.Instance == null)
            {
                MessageBox.Show(
                    this,
                    "Выберите в таблице дополнительную ККТ, доступную для удаления.",
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

            using (KktDeletionConfirmationDialog dialog = new KktDeletionConfirmationDialog(candidate.Instance))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    AppendLog("Удаление дополнительной ККТ отменено пользователем.\r\n\r\n");
                    return;
                }

                AppendLog("=== Удаление дополнительной ККТ " + candidate.Instance.Id + " ===\r\n");
                KktDeletionOutcome outcome = await _deletionWorkflow.DeleteAsync(
                    endpoint.BaseUrl,
                    candidate.Instance.Id,
                    dialog.ConfirmationText,
                    HandleDeletionResponse,
                    System.Threading.CancellationToken.None);

                AppendLog(outcome.Message + "\r\n\r\n");
                if (outcome.IsSuccess)
                {
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

        private Task SelectAutomaticInstallerAsync()
        {
            bool selected = _lmGatewayPage.SelectRequiredInstallers(this);
            UpdateAutomaticInstallerSelection();
            _automationStatusLabel.Text = selected
                ? "Статус: готово к автоматической настройке"
                : "Статус: нужны оба пакета";
            return Task.FromResult(0);
        }

        private void UpdateAutomaticInstallerSelection()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            string path = _lmGatewayPage.SelectedInstallerPath;
            string lmPath = _lmGatewayPage.SelectedLocalModuleInstallerPath;
            _automaticInstallerTextBox.Text = string.IsNullOrWhiteSpace(path)
                ? "Не выбран"
                : path;
            _automaticLmInstallerTextBox.Text = string.IsNullOrWhiteSpace(lmPath)
                ? "Не выбран"
                : lmPath;
            _automaticToolTip.SetToolTip(_automaticInstallerTextBox, path);
            _automaticToolTip.SetToolTip(_automaticLmInstallerTextBox, lmPath);
            if (!_busy)
            {
                _automationStatusLabel.Text = _lmGatewayPage.HasRequiredInstallerSelections
                    ? "Статус: готово к автоматической настройке"
                    : "Статус: нужны оба пакета";
            }
        }

        private async Task ConfigureAllKktsAutomaticallyAsync()
        {
            string baseUrl = (_baseUrlTextBox.Text ?? string.Empty).Trim();
            string dkktPort = (_dkktPortTextBox.Text ?? string.Empty).Trim();
            ValidationResult settingsValidation = TspiotInputValidator.ValidateBulkSettings(baseUrl, dkktPort);
            if (!ShowValidation(settingsValidation) || !ConfirmWarnings(settingsValidation))
            {
                _automationStatusLabel.Text = "Статус: запуск отменен";
                return;
            }

            if (!_lmGatewayPage.HasRequiredInstallerSelections)
            {
                bool selected = _lmGatewayPage.SelectRequiredInstallers(this);
                UpdateAutomaticInstallerSelection();
                if (!selected)
                {
                    _automationStatusLabel.Text = "Статус: выбор установщика отменён";
                    return;
                }
            }

            _automaticCancellation = new System.Threading.CancellationTokenSource();
            _automaticStopButton.Enabled = true;
            try
            {
                AppendLog(
                    "=== Полная автоматическая настройка ККТ, контроллеров и ЛМ ЧЗ ===\r\n");
                System.Threading.CancellationToken token =
                    _automaticCancellation.Token;
                _automationStatusLabel.Text =
                    "Статус: поиск подключенных и зарегистрированных ККТ...";
                BulkRegistrationDiscovery discovery =
                    await _bulkWorkflow.DiscoverAsync(
                        baseUrl,
                        dkktPort,
                        HandleBulkProgress,
                        token);
                if (!discovery.IsValid)
                {
                    throw new InvalidOperationException(discovery.ErrorMessage);
                }
                AppendBulkDiscovery(discovery);

                IList<LmGatewayKkt> registered =
                    await _lmGatewayPage
                        .DiscoverRegisteredKktsForAutomaticPlanAsync(
                            baseUrl,
                            token);
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
                _automationStatusLabel.Text =
                    "Статус: проверьте готовые адреса и порты...";
                bool complete = await _lmGatewayPage
                    .RunCompleteAutomaticSetupFromHostAsync(
                        candidates,
                        async delegate(
                            string serial,
                            System.Threading.CancellationToken itemToken)
                        {
                            BulkRegistrationWorkItem work;
                            if (!workBySerial.TryGetValue(serial, out work))
                            {
                                return true;
                            }
                            attempted.Add(serial);
                            _automationStatusLabel.Text =
                                "Статус: регистрация ККТ " + serial + "...";
                            BulkKktRegistrationResult itemResult;
                            try
                            {
                                itemResult = await _bulkWorkflow.ExecuteItemAsync(
                                    work,
                                    workOrdinalBySerial[serial],
                                    discovery.Items.Count,
                                    HandleBulkProgress,
                                    itemToken);
                            }
                            catch (OperationCanceledException)
                            {
                                registrationOutcome.Cancelled = true;
                                itemResult = new BulkKktRegistrationResult
                                {
                                    KktSerial = serial,
                                    Status = BulkKktRegistrationStatus.Cancelled,
                                    Details =
                                        "операция остановлена; проверьте фактическое состояние ККТ"
                                };
                                registrationOutcome.Results.Add(itemResult);
                                AppendBulkResult(itemResult);
                                throw;
                            }
                            catch (Exception ex)
                            {
                                itemResult = new BulkKktRegistrationResult
                                {
                                    KktSerial = serial,
                                    Status =
                                        BulkKktRegistrationStatus.InspectionFailed,
                                    Details = SensitiveDataMasker.Mask(ex.Message)
                                };
                            }
                            registrationOutcome.Results.Add(itemResult);
                            AppendBulkResult(itemResult);
                            return IsSuccessfulBulkRegistration(itemResult.Status);
                        },
                        token);
                if (!complete &&
                    _lmGatewayPage.AutomaticSetupCancelledBeforeMutation)
                {
                    _automationStatusLabel.Text =
                        "Статус: запуск отменён до изменений";
                    AppendLog(
                        "Автоматическая настройка отменена до изменения ЕСМ и Windows.\r\n\r\n");
                    return;
                }
                _automaticCancellation.Token.ThrowIfCancellationRequested();

                AddUnattemptedBulkResults(
                    discovery,
                    workBySerial,
                    attempted,
                    registrationOutcome.Results);
                AppendBulkResults(registrationOutcome.Results);

                _workspaceTabs.SelectedTab = _lmGatewayTab;
                bool registrationHasFailures =
                    registrationOutcome.Cancelled ||
                    HasBulkFailures(registrationOutcome.Results);
                _automationStatusLabel.Text = complete &&
                    !registrationHasFailures
                    ? "Статус: полная автоматическая настройка завершена"
                    : "Статус: завершено, требуется внимание";

                string stackSummary = complete
                    ? "Контроллеры и ЛМ ЧЗ запущены; ЕСМ принял настройки связи. " +
                        "ЛМ готовы к бизнес-инициализации ЕСМ или сторонней утилитой."
                    : _lmGatewayPage.AutomaticSetupStatus;
                bool fullySuccessful = complete && !registrationHasFailures;
                MessageBox.Show(
                    this,
                    BuildAutomaticSetupCompletionMessage(
                        registrationOutcome,
                        stackSummary,
                        complete,
                        registrationHasFailures),
                    "Автоматическая настройка",
                    MessageBoxButtons.OK,
                    fullySuccessful
                        ? MessageBoxIcon.Information
                        : MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException)
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }
                _automationStatusLabel.Text = "Статус: автоматическая настройка остановлена";
                AppendLog("Автоматическая настройка остановлена. Уже выполненные действия сохранены.\r\n\r\n");
                MessageBox.Show(
                    this,
                    "Автоматическая настройка остановлена после безопасной границы. " +
                        "Уже выполненные действия не отменены; фактическое состояние показано в таблицах и журнале.",
                    "Автоматическая настройка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
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
            IList<BulkKktRegistrationResult> results)
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
                        Details =
                            "не начато после остановки контрольной ККТ или локального комплекта"
                    });
                }
            }
        }

        private static bool IsSuccessfulBulkRegistration(
            BulkKktRegistrationStatus status)
        {
            return status == BulkKktRegistrationStatus.Registered ||
                status == BulkKktRegistrationStatus.RecoveredRegistration ||
                status == BulkKktRegistrationStatus.AlreadyExists;
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
                    ? "ККТ " + progress.Current.ToString() + " из " + progress.Total.ToString() + "; "
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

        private void AppendBulkResults(IList<BulkKktRegistrationResult> results)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("=== Итог массовой регистрации ===");
            for (int i = 0; i < results.Count; i++)
            {
                builder.AppendLine(results[i].FormatLogLine());
            }
            builder.AppendLine();
            builder.AppendLine(BulkKktRegistrationResult.FormatSummary(results));
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

            string scriptPath = WriteRecoveryScriptFile(_lastRecoveryScript);
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

            if (string.IsNullOrEmpty(_lastRecoveryScript))
            {
                PrepareServiceRecovery(input);
            }

            Clipboard.SetText(_lastRecoveryScript);
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

        private void PrepareServiceRecovery(TspiotFormInput input)
        {
            _lastControlModulePath = ServiceRecoveryCommandBuilder.FindControlModulePath();
            _lastRecoveryScript = ServiceRecoveryCommandBuilder.BuildPowerShellScript(
                input.KktSerial,
                input.Port,
                input.SoftPort,
                _lastControlModulePath);

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
            AppendLog("PowerShell-команда:\r\n" + _lastRecoveryScript + "\r\n");

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
            _lastRecoveryScript = string.Empty;
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

        private static string BuildAutomaticSetupCompletionMessage(
            BulkRegistrationOutcome registrationOutcome,
            string stackSummary,
            bool complete,
            bool registrationHasFailures)
        {
            StringBuilder message = new StringBuilder();
            message.Append("Автоматическая настройка завершена.\r\n\r\n");
            message.Append(BulkKktRegistrationResult.FormatSummary(
                registrationOutcome.Results));
            message.Append("\r\n\r\n");
            message.Append(stackSummary);
            message.Append(
                "\r\n\r\nАдреса ЛМ и порты кассового ПО показаны " +
                "на вкладке «ЛМ ЧЗ».");
            if (complete && !registrationHasFailures)
            {
                message.Append(
                    "\r\n\r\nПрограмма помогла? Поддержать разработку: " +
                    "меню Справка.");
            }
            return message.ToString();
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                this,
                "Управление ККТ в ЕСМ/ТС ПИоТ\r\n\r\n" +
                    "Ручное последовательное подключение ККТ, автоматическая обработка нескольких ККТ и безопасное удаление дополнительных экземпляров.\r\n\r\n" +
                    "Издатель и владелец: KRS\r\n" +
                    "Автор: Руслан Керусов\r\n" +
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
                if (candidate.IsPrimary)
                {
                    role = "Первая ККТ (защищена)";
                }
                else if (candidate.CanDelete)
                {
                    role = "Дополнительная ККТ";
                    deletableCount++;
                }
                else
                {
                    role = "Защищена";
                }

                AddInstanceGridRow(_instancesGrid, candidate, role, true);
                AddInstanceGridRow(_manualInstancesGrid, candidate, role, false);
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
                _instancesSummaryLabel.Text = "Экземпляров: " + instances.Count.ToString() + ". Дополнительных для удаления: " + deletableCount.ToString() + ".";
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
                row.DefaultCellStyle.BackColor = Color.MistyRose;
            }
        }

        private KktDeletionCandidate GetSelectedDeletionCandidate()
        {
            if (_instancesGrid.SelectedRows.Count != 1)
            {
                return null;
            }

            return _instancesGrid.SelectedRows[0].Tag as KktDeletionCandidate;
        }

        private void UpdateDeleteButtonState()
        {
            KktDeletionCandidate candidate = GetSelectedDeletionCandidate();
            bool enabled = !_busy && candidate != null && candidate.CanDelete;
            _deleteKktButton.Enabled = enabled;
            _deleteMenuItem.Enabled = enabled;
        }

        private void UpdateConnectionStatus(ApiResponse response)
        {
            if (response == null || response.IsConnectionFailure || response.StatusCode == 0)
            {
                _connectionStatusLabel.Text = "ЕСМ: нет связи";
                _connectionStatusLabel.ForeColor = Color.Firebrick;
                return;
            }

            _connectionStatusLabel.Text = "ЕСМ: доступен";
            _connectionStatusLabel.ForeColor = Color.DarkGreen;
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
            for (int i = 0; i < _actionButtons.Count; i++)
            {
                _actionButtons[i].Enabled = !busy;
            }

            _operationsMenuItem.Enabled = !busy;
            _lmGatewayPage.SetHostBusy(busy);
            UpdateDeleteButtonState();
        }

        private void OnLmGatewayOperationStateChanged(bool busy)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            SetBusy(busy);
            _operationStatusLabel.Text = busy
                ? "Выполняется операция с ЛМ ЧЗ..."
                : "Готово";
        }
    }
}
