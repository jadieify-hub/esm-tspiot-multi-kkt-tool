using System.Drawing;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;
using Button = AntdUI.Button;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class LmGatewayPage
    {
        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
                Padding = new Padding(10) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            for (int index = 2; index < 6; index++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var refresh = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
            ConfigureButton(_refreshButton, "Обновить данные");
            ConfigureButton(_cancelButton, "Остановить");
            _refreshButton.Click += async delegate { await RefreshAsync(); };
            _cancelButton.Click += delegate { CancelCurrentOperation(); };
            _cancelButton.Visible = false;
            refresh.Controls.Add(_refreshButton);
            refresh.Controls.Add(_cancelButton);
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Text = "Нажмите «Обновить данные», чтобы получить фактические состояния.";
            _statusLabel.Margin = new Padding(8, 6, 0, 4);
            _statusLabel.TextChanged += delegate
            {
                _serviceToolTip.SetToolTip(_statusLabel, _statusLabel.Text);
                if (SetupProgressChanged != null) SetupProgressChanged(_statusLabel.Text);
            };
            toolbar.Controls.Add(refresh, 0, 0);
            toolbar.Controls.Add(_statusLabel, 1, 0);
            ConfigureButton(_showServiceOperationsButton, "Служебные операции…");
            _showServiceOperationsButton.Name = "service-operations";
            _showServiceOperationsButton.Click += delegate { ShowServiceOperations(); };
            toolbar.Controls.Add(_showServiceOperationsButton, 2, 0);
            root.Controls.Add(toolbar, 0, 0);
            ConfigureGrid();
            _grid.CurrentCellChanged += delegate
            {
                if (_loadingGrid) return;
                UpdateActionState();
                if (KktSelectionChanged != null) KktSelectionChanged();
            };
            root.Controls.Add(_grid, 0, 1);
            _selectionActionHintLabel.AutoSize = true;
            _selectionActionHintLabel.Margin = new Padding(0, 6, 0, 4);
            root.Controls.Add(_selectionSummary, 0, 2);
            root.Controls.Add(_selectionActionHintLabel, 0, 3);
            root.SizeChanged += delegate {
                _selectionSummary.MaximumSize = new Size(System.Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal), 0);
            };

            _serviceOperationsWindow = BuildServiceOperationsWindow();

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            actions.Controls.Add(_installControllerButton);
            actions.Controls.Add(_readbackEsmButton);
            ConfigureButton(_initializeLmButton, "Инициализировать ЛМ ЧЗ…");
            _initializeLmButton.Click += async delegate { await InitializeSelectedLmAsync(); };
            actions.Controls.Add(_initializeLmButton);
            ConfigureButton(_checkGismtButton, "Проверить связь с ЧЗ");
            _checkGismtButton.Name = "check-gismt";
            _checkGismtButton.Click += async delegate
            {
                LmGatewayKkt kkt = SelectedKkt;
                if (kkt != null) await CheckGismtForKktAsync(kkt.KktSerial);
            };
            actions.Controls.Add(_checkGismtButton);
            ConfigureButton(_showDetailsButton, "Сведения о кассе…");
            _showDetailsButton.Click += delegate {
                using (Form dialog = BuildKktDetailsDialog())
                    if (dialog != null) dialog.ShowDialog(FindForm());
            };
            actions.Controls.Add(_showDetailsButton);
            _installControllerButton.Text = "Изменить настройки ЛМ ЧЗ выбранной ККТ";
            OperatorTheme.Button(_installControllerButton, true);
            root.Controls.Add(actions, 0, 4);
            _diagnosticsTextBox.Multiline = _diagnosticsTextBox.ReadOnly = true;
            _diagnosticsTextBox.BackColor = SystemColors.Window;
            _diagnosticsTextBox.Dock = DockStyle.Top;
            _diagnosticsTextBox.Height = 132;
            _diagnosticsTextBox.ScrollBars = ScrollBars.Vertical;
            _diagnosticsTextBox.Visible = false;
            root.Controls.Add(_diagnosticsTextBox, 0, 5);
            Controls.Add(OperatorTheme.Card(root, false));
        }

        private void ConfigureGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.RowHeadersVisible = false;
            _grid.AutoGenerateColumns = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.ScrollBars = ScrollBars.Both;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            OperatorTheme.Grid(_grid);
            _grid.ColumnHeadersHeight = 44;
            AddColumn("KktOrdinal", "№", 42);
            AddColumn("KktSerial", "Серийный № ККТ", 175);
            AddColumn("Organization", "Организация", 230);
            AddColumn("LmState", "Состояние ЛМ", 150, wrap: true);
            AddColumn("KktInn", "ИНН", 125);
            AddColumn("KktServicePort", "Порт ЕСМ", 75);
            AddColumn("KktSoftwarePort", "Порт ПО", 75, "softPort для Frontol или другой кассовой программы.");
            AddColumn("DesiredLmEndpoint", "ЛМ ЧЗ · выбранный адрес", 180, wrap: true);
            AddColumn("LmEndpoint", "ЛМ ЧЗ · учтённый адрес", 180, wrap: true);
            AddColumn("LmTargetState", "Применение адреса ЛМ", 195, wrap: true);
            AddColumn("ControllerEndpoint", "Контроллер для ЕСМ", 180, wrap: true);
            AddColumn("EsmLinkState", "Привязка к ЕСМ", 175, wrap: true);
            AddColumn("LmRole", "Тип", 90, "Базовый поставщицкий ЛМ или независимый клон.");
            AddColumn("LmInstallRoot", "Каталог ЛМ", 220, "Фактический каталог установки ЛМ ЧЗ.");
            AddColumn("LmOwnership", "Владение", 180, "Создан программой или сохранён как поставщицкий компонент.");
        }

        private void AddColumn(string name, string header, int width, string hint = "", bool wrap = false)
        {
            var column = new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = header, Width = width, ToolTipText = hint,
                ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable
            };
            column.DefaultCellStyle.WrapMode = wrap ? DataGridViewTriState.True : DataGridViewTriState.False;
            column.DefaultCellStyle.NullValue = "—";
            _grid.Columns.Add(column);
        }

        private static void ConfigureButton(Button button, string text)
        {
            OperatorTheme.Button(button, false);
            button.Text = text;
            button.AutoSize = true;
            button.Margin = new Padding(0, 0, 6, 4);
            button.Padding = Padding.Empty;
        }

    }
}
