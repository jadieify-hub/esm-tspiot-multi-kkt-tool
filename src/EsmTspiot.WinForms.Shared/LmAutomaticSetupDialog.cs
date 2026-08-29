using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LmAutomaticSetupDialogRow
    {
        public int Ordinal { get; set; }
        public string KktSerial { get; set; }
        public string KktInn { get; set; }
        public string SoftwarePort { get; set; }
        public string TargetAddress { get; set; }
        public string TargetPort { get; set; }
    }

    internal sealed class LmAutomaticSetupDialog : Form
    {
        private readonly DataGridView _grid = new DataGridView();
        private readonly Button _continueButton = new Button();
        private readonly List<LmAutomaticSetupDialogRow> _initialRows =
            new List<LmAutomaticSetupDialogRow>();
        private bool _synchronizingSharedEndpoint;

        internal LmAutomaticSetupDialog(IList<LmAutomaticSetupDialogRow> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException("rows");
            }

            for (int index = 0; index < rows.Count; index++)
            {
                if (rows[index] != null)
                {
                    _initialRows.Add(Copy(rows[index]));
                }
            }

            Text = "Параметры автоматической настройки";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 460);
            MinimumSize = new Size(760, 360);
            Font = new Font("Segoe UI", 8.25F);
            BuildLayout();
            FillRows();
        }

        internal IList<LmAutomaticSetupDialogRow> Parameters { get; private set; }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label instruction = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(840, 0),
                Text = "Проверьте готовые локальные адреса и порты. " +
                    "Порт кассового ПО нужен для Frontol/другой кассовой программы. " +
                    "ККТ одного ИНН используют один ЛМ ЧЗ; при необходимости измените его API-порт.",
                Margin = new Padding(0, 0, 0, 8)
            };
            root.Controls.Add(instruction, 0, 0);

            ConfigureGrid();
            root.Controls.Add(_grid, 0, 1);

            Label security = new Label
            {
                AutoSize = true,
                Text = "Полный автоматический режим разворачивает ЛМ на этом компьютере: адрес 127.0.0.1 фиксирован.",
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 8, 0, 8)
            };
            root.Controls.Add(security, 0, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            Button cancel = new Button
            {
                Text = "Отмена",
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
                Margin = new Padding(6, 0, 0, 0)
            };
            _continueButton.Text = "Начать автоматическую настройку";
            _continueButton.AutoSize = true;
            _continueButton.Click += Accept;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_continueButton);
            root.Controls.Add(buttons, 0, 3);

            AcceptButton = _continueButton;
            CancelButton = cancel;
            Controls.Add(root);
        }

        private void ConfigureGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.MultiSelect = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.AutoGenerateColumns = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.Columns.Add(ReadOnlyColumn("KktOrdinal", "№", 32));
            _grid.Columns.Add(ReadOnlyColumn("KktSerial", "Серийный № ККТ", 118));
            _grid.Columns.Add(ReadOnlyColumn("KktInn", "ИНН", 92));
            _grid.Columns.Add(ReadOnlyColumn("KktSoftwarePort", "Порт кассового ПО", 105));
            _grid.Columns.Add(ReadOnlyColumn("LmTargetAddress", "Адрес ЛМ ЧЗ", 125));
            _grid.Columns.Add(EditColumn("LmTargetPort", "Порт ЛМ ЧЗ", 82));
            DataGridViewTextBoxColumn validation = ReadOnlyColumn("Validation", "Проверка", 180);
            validation.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            validation.MinimumWidth = 120;
            _grid.Columns.Add(validation);

            _grid.CellValueChanged += delegate { ValidateAllRows(); };
            _grid.CellEndEdit += SynchronizeEndpointForSameInn;
        }

        private void FillRows()
        {
            for (int index = 0; index < _initialRows.Count; index++)
            {
                LmAutomaticSetupDialogRow row = _initialRows[index];
                _grid.Rows.Add(
                    row.Ordinal.ToString(CultureInfo.InvariantCulture),
                    row.KktSerial ?? string.Empty,
                    row.KktInn ?? string.Empty,
                    row.SoftwarePort ?? string.Empty,
                    row.TargetAddress ?? string.Empty,
                    row.TargetPort ?? string.Empty,
                    string.Empty);
            }
            ValidateAllRows();
        }

        private void ValidateAllRows()
        {
            bool allValid = _grid.Rows.Count > 0;
            Dictionary<string, string> endpointByInn =
                new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < _grid.Rows.Count; index++)
            {
                DataGridViewRow row = _grid.Rows[index];
                string message = ValidateRow(row);
                string inn = CellText(row, "KktInn").Trim();
                string endpoint =
                    CellText(row, "LmTargetAddress").Trim() + ":" +
                    CellText(row, "LmTargetPort").Trim();
                string existing;
                if (message.Length == 0 &&
                    endpointByInn.TryGetValue(inn, out existing) &&
                    !string.Equals(existing, endpoint, StringComparison.Ordinal))
                {
                    message = "Для одного ИНН нужен один ЛМ ЧЗ.";
                }
                else if (message.Length == 0 &&
                    !endpointByInn.ContainsKey(inn))
                {
                    endpointByInn.Add(inn, endpoint);
                }
                row.Cells["Validation"].Value = message.Length == 0 ? "Готово" : message;
                row.DefaultCellStyle.BackColor = message.Length == 0
                    ? SystemColors.Window
                    : Color.MistyRose;
                if (message.Length > 0)
                {
                    allValid = false;
                }
            }
            _continueButton.Enabled = allValid;
        }

        private static string ValidateRow(DataGridViewRow row)
        {
            string address = CellText(row, "LmTargetAddress").Trim();
            string portText = CellText(row, "LmTargetPort").Trim();
            int port;
            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port))
            {
                return "Укажите порт ЛМ ЧЗ.";
            }

            ValidationResult targetValidation = LmGatewayInputValidator.ValidateTarget(
                new LmGatewayTarget(address, port));
            if (!targetValidation.IsValid)
            {
                return targetValidation.JoinMessages().Replace("\r\n", "; ");
            }
            string normalized;
            bool loopback;
            if (!LmGatewayInputValidator.TryNormalizeTargetAddress(
                    address,
                    out normalized,
                    out loopback) ||
                !loopback)
            {
                return "Полный автоматический режим устанавливает ЛМ на этот компьютер; укажите 127.0.0.1.";
            }
            return string.Empty;
        }

        private void Accept(object sender, EventArgs e)
        {
            ValidateAllRows();
            if (!_continueButton.Enabled)
            {
                return;
            }

            List<LmAutomaticSetupDialogRow> result = new List<LmAutomaticSetupDialogRow>();
            for (int index = 0; index < _grid.Rows.Count; index++)
            {
                DataGridViewRow gridRow = _grid.Rows[index];
                string normalizedAddress;
                bool isLoopback;
                LmGatewayInputValidator.TryNormalizeTargetAddress(
                    CellText(gridRow, "LmTargetAddress").Trim(),
                    out normalizedAddress,
                    out isLoopback);
                int ordinal;
                int.TryParse(CellText(gridRow, "KktOrdinal"), out ordinal);
                result.Add(new LmAutomaticSetupDialogRow
                {
                    Ordinal = ordinal,
                    KktSerial = CellText(gridRow, "KktSerial").Trim(),
                    KktInn = CellText(gridRow, "KktInn").Trim(),
                    SoftwarePort = CellText(gridRow, "KktSoftwarePort").Trim(),
                    TargetAddress = normalizedAddress,
                    TargetPort = CellText(gridRow, "LmTargetPort").Trim()
                });
            }

            Parameters = result;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void SynchronizeEndpointForSameInn(
            object sender,
            DataGridViewCellEventArgs e)
        {
            if (_synchronizingSharedEndpoint || e.RowIndex < 0 ||
                e.ColumnIndex < 0)
            {
                return;
            }
            string columnName = _grid.Columns[e.ColumnIndex].Name;
            if (columnName != "LmTargetAddress" &&
                columnName != "LmTargetPort")
            {
                ValidateAllRows();
                return;
            }
            DataGridViewRow edited = _grid.Rows[e.RowIndex];
            string inn = CellText(edited, "KktInn").Trim();
            string address = CellText(edited, "LmTargetAddress").Trim();
            string port = CellText(edited, "LmTargetPort").Trim();
            _synchronizingSharedEndpoint = true;
            try
            {
                for (int index = 0; index < _grid.Rows.Count; index++)
                {
                    DataGridViewRow row = _grid.Rows[index];
                    if (index == e.RowIndex || !string.Equals(
                        CellText(row, "KktInn").Trim(),
                        inn,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }
                    row.Cells["LmTargetAddress"].Value = address;
                    row.Cells["LmTargetPort"].Value = port;
                }
            }
            finally
            {
                _synchronizingSharedEndpoint = false;
            }
            ValidateAllRows();
        }

        private static LmAutomaticSetupDialogRow Copy(LmAutomaticSetupDialogRow source)
        {
            return new LmAutomaticSetupDialogRow
            {
                Ordinal = source.Ordinal,
                KktSerial = source.KktSerial ?? string.Empty,
                KktInn = source.KktInn ?? string.Empty,
                SoftwarePort = source.SoftwarePort ?? string.Empty,
                TargetAddress = source.TargetAddress ?? string.Empty,
                TargetPort = source.TargetPort ?? string.Empty
            };
        }

        private static string CellText(DataGridViewRow row, string columnName)
        {
            object value = row.Cells[columnName].Value;
            return value == null ? string.Empty : value.ToString();
        }

        private static DataGridViewTextBoxColumn ReadOnlyColumn(
            string name,
            string text,
            int width)
        {
            DataGridViewTextBoxColumn column = EditColumn(name, text, width);
            column.ReadOnly = true;
            return column;
        }

        private static DataGridViewTextBoxColumn EditColumn(
            string name,
            string text,
            int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = text,
                Width = width,
                ReadOnly = false,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }
    }
}
