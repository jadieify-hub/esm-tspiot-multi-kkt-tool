using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Button = AntdUI.Button;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class LmGatewayPage
    {
        private Form BuildServiceOperationsWindow()
        {
            var dialog = new Form { Text = "Служебные операции ЛМ ЧЗ", Name = "service-operations-window",
                Icon = OperatorTheme.AppIcon,
                ClientSize = new Size(960, 650), MinimumSize = new Size(760, 480),
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimizeBox = false, Font = Font, ForeColor = OperatorTheme.Ink, BackColor = OperatorTheme.Surface };
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14),
                ColumnCount = 1, RowCount = 4 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var status = new Label { AutoSize = true, MaximumSize = new Size(860, 0),
                ForeColor = OperatorTheme.Muted, Margin = new Padding(0, 0, 0, 10) };
            _statusLabel.TextChanged += delegate { status.Text = _statusLabel.Text; };
            status.Text = _statusLabel.Text;
            root.Controls.Add(status, 0, 0);
            _serviceCapabilityStatusLabel.AutoSize = true;
            _serviceCapabilityStatusLabel.MaximumSize = new Size(860, 0);
            _serviceCapabilityStatusLabel.Margin = new Padding(0, 0, 0, 10);
            _serviceCapabilityStatusLabel.Visible = false;
            root.Controls.Add(_serviceCapabilityStatusLabel, 0, 1);
            var advanced = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            advanced.Controls.Add(BuildInstallerPanel(), 0, 0);
            advanced.Controls.Add(BuildOfficialControllerPanel(), 0, 1);
            var removal = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            ConfigureButton(_bindButton, "Привязать к ЕСМ");
            _bindButton.Tag = "BindSelectedEsm";
            ConfigureButton(_removeServiceButton, "Удалить выбранный комплект");
            ConfigureButton(_removeAllServicesButton, "Удалить контроллеры и ЛМ…");
            ConfigureButton(_resetControllersButton, "Сбросить контроллеры…");
            _resetControllersButton.Click += async delegate { await ConfirmAndResetControllersAsync(); };
            _removeAllServicesButton.Tag = "RemoveAllCreatedComponents";
            ConfigureButton(_cleanupButton, "Завершить очистку");
            _bindButton.Click += async delegate { await BindSelectedAsync(); };
            _removeServiceButton.Click += async delegate { await ConfirmAndRemoveServiceAsync(); };
            _removeAllServicesButton.Click += async delegate { await ConfirmAndRemoveEverythingAsync(); };
            _cleanupButton.Click += async delegate { await ConfirmAndCleanupServiceAsync(); };
            foreach (Button button in new[] { _bindButton, _removeServiceButton, _resetControllersButton, _removeAllServicesButton, _cleanupButton })
                removal.Controls.Add(button);
            advanced.Controls.Add(removal, 0, 2);
            _serviceOperations.Dock = DockStyle.Fill;
            _serviceOperations.AutoScroll = true;
            _serviceOperations.Controls.Add(advanced);
            root.Controls.Add(_serviceOperations, 0, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var close = new Button { Text = "Закрыть" };
            OperatorTheme.Button(close, false);
            close.Click += delegate { dialog.Close(); };
            ConfigureButton(_serviceCancelButton, "Остановить");
            _serviceCancelButton.Click += delegate { CancelCurrentOperation(); };
            actions.Controls.Add(close);
            actions.Controls.Add(_serviceCancelButton);
            root.Controls.Add(actions, 0, 3);
            dialog.Controls.Add(root);
            dialog.CancelButton = close;
            dialog.FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; dialog.Hide(); }
            };
            return dialog;
        }

        private Form BuildKktDetailsDialog()
        {
            DataGridViewRow row = _grid.CurrentRow;
            if (row == null) return null;
            var text = new StringBuilder();
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                object value = row.Cells[column.Index].Value;
                text.AppendLine(column.HeaderText.Replace("\r", " ").Replace("\n", " ") + ": " +
                    (value == null || string.IsNullOrEmpty(value.ToString()) ? "—" : value.ToString()));
                text.AppendLine();
            }
            var dialog = new Form { Text = "Сведения о кассе", Name = "kkt-details-dialog",
                Icon = OperatorTheme.AppIcon,
                ClientSize = new Size(680, 580), MinimumSize = new Size(480, 380),
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false,
                Font = Font, ForeColor = OperatorTheme.Ink, BackColor = OperatorTheme.Surface };
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14),
                ColumnCount = 1, RowCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new TextBox { Name = "kkt-details", Text = text.ToString(), Multiline = true,
                ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White, BorderStyle = BorderStyle.None }, 0, 0);
            var close = new Button { Text = "Закрыть", Anchor = AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            OperatorTheme.Button(close, false);
            root.Controls.Add(close, 0, 1);
            dialog.Controls.Add(root);
            dialog.AcceptButton = dialog.CancelButton = close;
            dialog.ActiveControl = close;
            return dialog;
        }
    }
}
