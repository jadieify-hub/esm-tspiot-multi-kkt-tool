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
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildInstallerPanel(), 0, 0);

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.AutoSize = true;
            toolbar.WrapContents = true;
            toolbar.Margin = new Padding(0, 0, 0, 6);

            ConfigureButton(_refreshButton, "Обновить");
            ConfigureButton(_preparePlanButton, "Подготовить план");
            ConfigureButton(_executePlanButton, "Создать / обновить выбранные");
            ConfigureButton(_bindButton, "Повторить привязку к ЕСМ");
            ConfigureButton(_removeServiceButton, "Удалить службу");
            ConfigureButton(_cleanupButton, "Повторить очистку");
            ConfigureButton(_cancelButton, "Остановить");
            _refreshButton.Click += async delegate { await RefreshAsync(); };
            _bindButton.Click += async delegate { await BindSelectedAsync(); };
            _preparePlanButton.Click += delegate { PrepareServicePlan(); };
            _executePlanButton.Click += async delegate { await ConfirmAndExecuteServicePlanAsync(); };
            _removeServiceButton.Click += async delegate { await ConfirmAndRemoveServiceAsync(); };
            _cleanupButton.Click += async delegate { await ConfirmAndCleanupServiceAsync(); };
            _cancelButton.Click += delegate { CancelCurrentOperation(); };
            _cancelButton.Visible = false;
            toolbar.Controls.Add(_refreshButton);
            toolbar.Controls.Add(_preparePlanButton);
            toolbar.Controls.Add(_executePlanButton);
            toolbar.Controls.Add(_bindButton);
            toolbar.Controls.Add(_removeServiceButton);
            toolbar.Controls.Add(_cleanupButton);
            toolbar.Controls.Add(_cancelButton);

            _statusLabel.AutoSize = false;
            _statusLabel.Size = new Size(720, 36);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Text = "Read-back ЕСМ не документирован; показан только текущий сеанс.";
            _statusLabel.Margin = new Padding(8, 0, 0, 4);
            toolbar.Controls.Add(_statusLabel);

            ConfigureGrid();
            GroupBox editor = BuildEditor();

            root.Controls.Add(toolbar, 0, 1);
            root.Controls.Add(_grid, 0, 2);
            root.Controls.Add(editor, 0, 3);
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
            selected.Width = 45;
            selected.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(selected);
            _grid.Columns.Add(CreateTextColumn("Серийный номер ККТ", 115));
            _grid.Columns.Add(CreateTextColumn("ИНН", 85));
            _grid.Columns.Add(CreateTextColumn("Состояние ЕСМ", 105));
            _grid.Columns.Add(CreateTextColumn("ЕСМ port", 65));
            _grid.Columns.Add(CreateTextColumn("ЕСМ softPort", 80));
            _grid.Columns.Add(CreateTextColumn("Роль", 85));
            _grid.Columns.Add(CreateTextColumn("Служба", 175));
            _grid.Columns.Add(CreateTextColumn("gRPC", 55));
            _grid.Columns.Add(CreateTextColumn("REST", 55));
            _grid.Columns.Add(CreateTextColumn("Целевой ЛМ", 125));
            _grid.Columns.Add(CreateTextColumn("Состояние службы", 120));
            _grid.Columns.Add(CreateTextColumn("Привязка (сеанс)", 110));
            _grid.Columns.Add(CreateTextColumn("Последнее действие / ошибка", 180));

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
            _editorGroup.Text = "Параметры: ККТ не выбрана — хранятся только до закрытия программы";
            _editorGroup.Dock = DockStyle.Fill;
            _editorGroup.AutoSize = true;
            _editorGroup.Margin = new Padding(0, 6, 0, 0);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.AutoSize = true;
            table.Padding = new Padding(6, 3, 6, 6);
            table.ColumnCount = 5;
            table.RowCount = 4;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddEditorField(table, 0, "Адрес целевого ЛМ", _targetAddressTextBox, false);
            AddEditorField(table, 0, "Порт целевого ЛМ", _targetPortTextBox, true);
            AddEditorField(table, 1, "Адрес контроллера", _addressTextBox, false);
            AddEditorField(table, 1, "Локальный gRPC", _portTextBox, true);
            AddEditorField(table, 2, "Локальный REST", _restPortTextBox, false);
            AddEditorField(table, 2, "Логин ЛМ ЧЗ", _loginTextBox, true);
            AddEditorField(table, 3, "Пароль ЛМ ЧЗ", _passwordTextBox, false);
            _addressTextBox.ReadOnly = true;
            _passwordTextBox.UseSystemPasswordChar = true;

            ConfigureButton(_saveDraftButton, "Сохранить и выбрать ККТ");
            _saveDraftButton.Margin = new Padding(8, 1, 0, 1);
            _saveDraftButton.Click += delegate { SaveSelectedDraft(); };
            table.Controls.Add(_saveDraftButton, 4, 0);
            table.SetRowSpan(_saveDraftButton, 4);
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
