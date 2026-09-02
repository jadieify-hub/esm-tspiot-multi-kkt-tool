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
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            root.Controls.Add(BuildInstallerPanel(), 0, 0);
            root.Controls.Add(BuildOfficialControllerPanel(), 0, 1);

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.AutoSize = true;
            toolbar.WrapContents = true;
            toolbar.Margin = new Padding(0, 0, 0, 6);

            ConfigureButton(_refreshButton, "Обновить");
            ConfigureButton(_bindButton, "Привязать к ЕСМ");
            _bindButton.Tag = "BindSelectedEsm";
            ConfigureButton(_removeServiceButton, "Удалить выбранный комплект");
            ConfigureButton(_removeAllServicesButton, "Удалить всё созданное");
            _removeAllServicesButton.Tag = "RemoveAllCreatedComponents";
            ConfigureButton(_cleanupButton, "Завершить очистку");
            ConfigureButton(_cancelButton, "Остановить");
            _refreshButton.Click += async delegate { await RefreshAsync(); };
            _bindButton.Click += async delegate { await BindSelectedAsync(); };
            _removeServiceButton.Click += async delegate { await ConfirmAndRemoveServiceAsync(); };
            _removeAllServicesButton.Click += async delegate
            {
                await ConfirmAndRemoveEverythingAsync();
            };
            _cleanupButton.Click += async delegate { await ConfirmAndCleanupServiceAsync(); };
            _cancelButton.Click += delegate { CancelCurrentOperation(); };
            _cancelButton.Visible = false;
            toolbar.Controls.Add(_refreshButton);
            toolbar.Controls.Add(_removeAllServicesButton);
            toolbar.Controls.Add(_cancelButton);

            _statusLabel.AutoSize = false;
            _statusLabel.Size = new Size(720, 36);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Text = "Нажмите «Обновить», чтобы загрузить ККТ и их настройки.";
            _statusLabel.Margin = new Padding(8, 0, 0, 4);
            toolbar.Controls.Add(_statusLabel);

            _selectionActionHintLabel.AutoSize = false;
            _selectionActionHintLabel.Dock = DockStyle.Fill;
            _selectionActionHintLabel.MinimumSize = new Size(0, 24);
            _selectionActionHintLabel.TextAlign = ContentAlignment.MiddleLeft;
            _selectionActionHintLabel.AutoEllipsis = true;
            _selectionActionHintLabel.Margin = new Padding(8, 0, 0, 4);
            _selectionActionHintLabel.Text =
                "Выберите строку ККТ — здесь появятся доступные действия.";

            ConfigureGrid();

            root.Controls.Add(toolbar, 0, 2);
            root.Controls.Add(_selectionActionHintLabel, 0, 3);
            root.Controls.Add(_grid, 0, 4);
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

    }
}
