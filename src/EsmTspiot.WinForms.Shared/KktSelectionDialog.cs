using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.WinForms.Shared
{
    public sealed class KktSelectionDialog : Form
    {
        private readonly DataGridView _grid = new DataGridView();
        private readonly Button _selectButton = new Button();

        public KktSelectionDialog(IList<DkktDeviceInfo> devices)
        {
            Text = "Выбор подключаемой ККТ";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(700, 300);
            MinimumSize = new Size(560, 260);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 8.25F);

            BuildLayout(devices ?? new List<DkktDeviceInfo>());
        }

        public DkktDeviceInfo SelectedDevice
        {
            get
            {
                return _grid.SelectedRows.Count == 1
                    ? _grid.SelectedRows[0].Tag as DkktDeviceInfo
                    : null;
            }
        }

        private void BuildLayout(IList<DkktDeviceInfo> devices)
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(10);
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label instruction = new Label();
            instruction.AutoSize = true;
            instruction.Text = "Найдено несколько ККТ без экземпляра. Выберите физическую кассу, которую будете подключать сейчас.";
            instruction.Margin = new Padding(0, 0, 0, 8);

            ConfigureGrid();
            for (int i = 0; i < devices.Count; i++)
            {
                DkktDeviceInfo device = devices[i];
                int rowIndex = _grid.Rows.Add(
                    device.KktSerial,
                    device.FnSerial,
                    device.KktInn,
                    device.ModelName);
                _grid.Rows[rowIndex].Tag = device;
            }
            _grid.ClearSelection();

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Margin = new Padding(0, 8, 0, 0);

            Button cancelButton = new Button();
            cancelButton.Text = "Отмена";
            cancelButton.AutoSize = true;
            cancelButton.DialogResult = DialogResult.Cancel;

            _selectButton.Text = "Выбрать ККТ";
            _selectButton.AutoSize = true;
            _selectButton.Enabled = false;
            _selectButton.DialogResult = DialogResult.OK;

            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(_selectButton);
            root.Controls.Add(instruction, 0, 0);
            root.Controls.Add(_grid, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);

            AcceptButton = _selectButton;
            CancelButton = cancelButton;
        }

        private void ConfigureGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.RowHeadersVisible = false;
            _grid.AutoGenerateColumns = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.SelectionChanged += delegate { _selectButton.Enabled = SelectedDevice != null; };
            _grid.CellDoubleClick += delegate
            {
                if (SelectedDevice != null)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };

            _grid.Columns.Add(CreateColumn("Серийный номер ККТ", 145, 27F));
            _grid.Columns.Add(CreateColumn("Номер ФН", 155, 28F));
            _grid.Columns.Add(CreateColumn("ИНН", 110, 19F));
            _grid.Columns.Add(CreateColumn("Модель", 140, 26F));
        }

        private static DataGridViewTextBoxColumn CreateColumn(string text, int minimumWidth, float fillWeight)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = text,
                MinimumWidth = minimumWidth,
                FillWeight = fillWeight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }
    }
}
