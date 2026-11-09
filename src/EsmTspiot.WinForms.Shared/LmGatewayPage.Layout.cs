using System.Drawing;
using System.Windows.Forms;
using Button = AntdUI.Button;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class LmGatewayPage
    {
        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
                Padding = new Padding(10) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            for (int index = 2; index < 5; index++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
            root.Controls.Add(_selectionActionHintLabel, 0, 2);

            _serviceOperationsWindow = BuildServiceOperationsWindow();

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            actions.Controls.Add(_installControllerButton);
            actions.Controls.Add(_readbackEsmButton);
            ConfigureButton(_showDetailsButton, "Сведения о кассе…");
            _showDetailsButton.Click += delegate {
                using (Form dialog = BuildKktDetailsDialog())
                    if (dialog != null) dialog.ShowDialog(FindForm());
            };
            actions.Controls.Add(_showDetailsButton);
            _installControllerButton.Text = "Изменить настройки ЛМ ЧЗ выбранной ККТ";
            OperatorTheme.Button(_installControllerButton, true);
            root.Controls.Add(actions, 0, 3);
            _diagnosticsTextBox.Multiline = _diagnosticsTextBox.ReadOnly = true;
            _diagnosticsTextBox.BackColor = SystemColors.Window;
            _diagnosticsTextBox.Dock = DockStyle.Top;
            _diagnosticsTextBox.Height = 132;
            _diagnosticsTextBox.ScrollBars = ScrollBars.Vertical;
            _diagnosticsTextBox.Visible = false;
            root.Controls.Add(_diagnosticsTextBox, 0, 4);
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
            _grid.BackgroundColor = SystemColors.Window;
            _grid.BorderStyle = BorderStyle.Fixed3D;
            _grid.ScrollBars = ScrollBars.Both;

            DataGridViewTextBoxColumn ordinal = CreateTextColumn("№", 30);
            ordinal.Name = "KktOrdinal";
            _grid.Columns.Add(ordinal);
            DataGridViewTextBoxColumn serial = CreateTextColumn("Серийный № ККТ", 100);
            serial.Name = "KktSerial";
            _grid.Columns.Add(serial);
            DataGridViewTextBoxColumn inn = CreateTextColumn("ИНН", 70);
            inn.Name = "KktInn";
            _grid.Columns.Add(inn);
            DataGridViewTextBoxColumn softwarePort = CreateTextColumn("Порт ПО", 75);
            softwarePort.Name = "KktSoftwarePort";
            softwarePort.ToolTipText =
                "softPort для Frontol или другой кассовой программы.";
            _grid.Columns.Add(softwarePort);
            DataGridViewTextBoxColumn endpoint = CreateTextColumn(
                "Адрес и порт ЛМ ЧЗ",
                115);
            endpoint.Name = "LmEndpoint";
            _grid.Columns.Add(endpoint);
            DataGridViewTextBoxColumn lmState = CreateTextColumn(
                "Состояние ЛМ",
                85);
            lmState.Name = "LmState";
            _grid.Columns.Add(lmState);
            DataGridViewTextBoxColumn lmRole = CreateTextColumn("Тип", 55);
            lmRole.Name = "LmRole";
            lmRole.ToolTipText = "Базовый поставщицкий ЛМ или независимый клон.";
            _grid.Columns.Add(lmRole);
            DataGridViewTextBoxColumn installRoot = CreateTextColumn(
                "Каталог ЛМ",
                110);
            installRoot.Name = "LmInstallRoot";
            installRoot.ToolTipText = "Фактический каталог установки ЛМ ЧЗ.";
            _grid.Columns.Add(installRoot);
            DataGridViewTextBoxColumn ownership = CreateTextColumn(
                "Владение",
                90);
            ownership.Name = "LmOwnership";
            ownership.ToolTipText =
                "Создан программой или сохранён как поставщицкий компонент.";
            _grid.Columns.Add(ownership);
            DataGridViewTextBoxColumn esmState = CreateTextColumn(
                "Связь с ЕСМ",
                120);
            esmState.Name = "EsmLinkState";
            esmState.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            esmState.MinimumWidth = 120;
            _grid.Columns.Add(esmState);
            DataGridViewTextBoxColumn servicePort = CreateTextColumn("Порт ЕСМ", 75);
            servicePort.Name = "KktServicePort";
            _grid.Columns.Add(servicePort);
            servicePort.DisplayIndex = 3;
            DataGridViewTextBoxColumn organization = CreateTextColumn("Организация", 230);
            organization.Name = "Organization";
            _grid.Columns.Add(organization);
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            _grid.RowTemplate.Height = 28;
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                column.FillWeight = column.Width;
                column.MinimumWidth = column.Name == "KktSerial" ? 120 : 65;
            }
            _grid.Columns["EsmLinkState"].HeaderText = "Параметры ЛМ";
            _grid.Columns["LmState"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.Columns["KktSerial"].MinimumWidth = 160;
            _grid.Columns["KktInn"].MinimumWidth = 100;
            _grid.Columns["LmEndpoint"].MinimumWidth = 140;
            _grid.Columns["LmState"].MinimumWidth = 110;
            _grid.Columns["KktSerial"].FillWeight = 180;
            _grid.Columns["KktInn"].FillWeight = 110;
            _grid.Columns["LmEndpoint"].FillWeight = 160;
            _grid.Columns["LmState"].FillWeight = 150;
            _grid.Columns["EsmLinkState"].FillWeight = 160;
            _grid.Columns["LmEndpoint"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            OperatorTheme.Grid(_grid);
            _grid.ColumnHeadersHeight = 44;
            foreach (string name in new[] { "KktSoftwarePort", "KktServicePort" })
            {
                _grid.Columns[name].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                _grid.Columns[name].Width = 70;
            }
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            string[] order = { "KktOrdinal", "KktSerial", "Organization", "KktInn", "KktServicePort", "KktSoftwarePort",
                "LmEndpoint", "LmState", "EsmLinkState", "LmRole", "LmInstallRoot", "LmOwnership" };
            int[] widths = { 42, 175, 230, 125, 75, 75, 155, 150, 175, 90, 220, 180 };
            for (int index = 0; index < order.Length; index++)
            {
                var column = _grid.Columns[order[index]];
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.DisplayIndex = index;
                column.Width = widths[index];
            }
        }

        private static void ConfigureButton(Button button, string text)
        {
            OperatorTheme.Button(button, false);
            button.Text = text;
            button.AutoSize = true;
            button.Margin = new Padding(0, 0, 6, 4);
            button.Padding = Padding.Empty;
        }

        private static DataGridViewTextBoxColumn CreateTextColumn(string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Width = width,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

    }
}
