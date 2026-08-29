using System.Drawing;
using System.Windows.Forms;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class LmGatewayPage
    {
        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildInstallerPanel(), 0, 0);
            root.Controls.Add(BuildOfficialControllerPanel(), 0, 1);

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.AutoSize = true;
            toolbar.WrapContents = true;
            toolbar.Margin = new Padding(0, 0, 0, 6);

            ConfigureButton(_refreshButton, "Обновить");
            ConfigureButton(_bindButton, "Повторить привязку к ЕСМ");
            ConfigureButton(_removeServiceButton, "Удалить выбранный контроллер");
            ConfigureButton(_removeAllServicesButton, "Удалить все контроллеры");
            _removeAllServicesButton.Tag = "RemoveAllManaged";
            ConfigureButton(_cleanupButton, "Завершить очистку");
            ConfigureButton(_cancelButton, "Остановить");
            _refreshButton.Click += async delegate { await RefreshAsync(); };
            _bindButton.Click += async delegate { await BindSelectedAsync(); };
            _removeServiceButton.Click += async delegate { await ConfirmAndRemoveServiceAsync(); };
            _removeAllServicesButton.Click += async delegate { await ConfirmAndRemoveAllServicesAsync(); };
            _cleanupButton.Click += async delegate { await ConfirmAndCleanupServiceAsync(); };
            _cancelButton.Click += delegate { CancelCurrentOperation(); };
            _cancelButton.Visible = false;
            toolbar.Controls.Add(_refreshButton);
            toolbar.Controls.Add(_bindButton);
            toolbar.Controls.Add(_removeServiceButton);
            toolbar.Controls.Add(_removeAllServicesButton);
            toolbar.Controls.Add(_cleanupButton);
            toolbar.Controls.Add(_cancelButton);

            _statusLabel.AutoSize = false;
            _statusLabel.Size = new Size(720, 36);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Text = "Нажмите «Обновить», чтобы загрузить ККТ и их настройки.";
            _statusLabel.Margin = new Padding(8, 0, 0, 4);
            toolbar.Controls.Add(_statusLabel);

            ConfigureGrid();
            GroupBox editor = BuildEditor();

            root.Controls.Add(toolbar, 0, 2);
            root.Controls.Add(_grid, 0, 3);
            root.Controls.Add(editor, 0, 4);
            Controls.Add(root);
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

            DataGridViewCheckBoxColumn selected = new DataGridViewCheckBoxColumn();
            selected.HeaderText = "Выбор";
            selected.Name = "SelectKkt";
            selected.Width = 40;
            selected.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(selected);
            DataGridViewTextBoxColumn ordinal = CreateTextColumn("№", 30);
            ordinal.Name = "KktOrdinal";
            _grid.Columns.Add(ordinal);
            DataGridViewTextBoxColumn serial = CreateTextColumn("Серийный № ККТ", 105);
            serial.Name = "KktSerial";
            _grid.Columns.Add(serial);
            DataGridViewTextBoxColumn inn = CreateTextColumn("ИНН", 75);
            inn.Name = "KktInn";
            _grid.Columns.Add(inn);
            DataGridViewTextBoxColumn softwarePort = CreateTextColumn("Порт кассового ПО", 90);
            softwarePort.Name = "KktSoftwarePort";
            _grid.Columns.Add(softwarePort);
            DataGridViewTextBoxColumn lmAddress = CreateTextColumn("Адрес ЛМ ЧЗ", 90);
            lmAddress.Name = "LmTargetAddress";
            _grid.Columns.Add(lmAddress);
            DataGridViewTextBoxColumn lmPort = CreateTextColumn("Порт ЛМ ЧЗ", 70);
            lmPort.Name = "LmTargetPort";
            _grid.Columns.Add(lmPort);
            DataGridViewTextBoxColumn status = CreateTextColumn("Статус", 95);
            status.Name = "UserStatus";
            _grid.Columns.Add(status);
            DataGridViewTextBoxColumn result = CreateTextColumn("Результат", 140);
            result.Name = "UserResult";
            result.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            result.MinimumWidth = 100;
            _grid.Columns.Add(result);

            _grid.SelectionChanged += delegate { LoadSelectedEditor(); };
            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty)
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            _grid.CellValueChanged += OnGridCellValueChanged;
        }

        private GroupBox BuildEditor()
        {
            _editorGroup.Text = "Параметры: выберите ККТ в таблице";
            _editorGroup.Dock = DockStyle.Fill;
            _editorGroup.AutoSize = true;
            _editorGroup.Margin = new Padding(0, 6, 0, 0);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.AutoSize = true;
            table.Padding = new Padding(6, 3, 6, 6);
            table.ColumnCount = 5;
            table.RowCount = 3;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddEditorField(table, 0, "Адрес ЛМ ЧЗ для этой ККТ", _targetAddressTextBox, false);
            AddEditorField(table, 0, "Порт ЛМ ЧЗ (обычно 5995)", _targetPortTextBox, true);
            AddEditorField(table, 1, "Логин ЛМ ЧЗ", _loginTextBox, false);
            AddEditorField(table, 1, "Пароль ЛМ ЧЗ", _passwordTextBox, true);
            _addressTextBox.ReadOnly = true;
            _passwordTextBox.UseSystemPasswordChar = true;

            _automaticPortsHintLabel.AutoSize = true;
            _automaticPortsHintLabel.Text =
                "Адреса и порты заполнены по номеру ККТ: " +
                "№1 — 127.0.0.1:5995, №2 — 127.0.0.1:6995, №3 — 127.0.0.1:7995 и т. д.";
            _automaticPortsHintLabel.ForeColor = SystemColors.GrayText;
            _automaticPortsHintLabel.Margin = new Padding(0, 5, 0, 2);
            table.Controls.Add(_automaticPortsHintLabel, 0, 2);
            table.SetColumnSpan(_automaticPortsHintLabel, 4);

            ConfigureButton(_saveDraftButton, "Сохранить параметры");
            _saveDraftButton.Margin = new Padding(8, 1, 0, 1);
            _saveDraftButton.Click += delegate { SaveSelectedDraft(); };
            table.Controls.Add(_saveDraftButton, 4, 0);
            table.SetRowSpan(_saveDraftButton, 3);
            _editorGroup.Controls.Add(table);
            return _editorGroup;
        }

        private static void ConfigureButton(Button button, string text)
        {
            button.Text = text;
            button.AutoSize = true;
            button.Margin = new Padding(0, 0, 6, 4);
            button.Padding = new Padding(6, 2, 6, 2);
            button.MinimumSize = new Size(110, 27);
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

        private static void AddEditorField(
            TableLayoutPanel table,
            int row,
            string labelText,
            TextBox textBox,
            bool secondPair)
        {
            int labelColumn = secondPair ? 2 : 0;
            int valueColumn = secondPair ? 3 : 1;
            Label label = new Label();
            label.Text = labelText;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(secondPair ? 10 : 0, 4, 5, 3);

            textBox.Dock = DockStyle.Fill;
            textBox.Margin = new Padding(0, 2, 0, 2);
            table.Controls.Add(label, labelColumn, row);
            table.Controls.Add(textBox, valueColumn, row);
        }
    }
}
