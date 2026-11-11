using System;
using System.Drawing;
using System.Windows.Forms;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class MainForm
    {
        private readonly AntdUI.Segmented _navigation = new OperatorSegments();
        private bool _updatingNavigation;
        private Form _connectionDialog;

        private Control BuildWorkspaceNavigation()
        {
            OperatorTheme.Segments(_navigation, "Настройка ККТ", "Обзор касс", "Журнал");
            _navigation.Name = "workspace-navigation";
            _navigation.Items[0].IconSvg = "SettingOutlined";
            _navigation.Items[1].IconSvg = "DesktopOutlined";
            _navigation.Items[2].IconSvg = "FileTextOutlined";
            _navigation.Dock = DockStyle.Fill;
            _navigation.Width = 480;
            _navigation.Margin = Padding.Empty;
            _navigation.SelectIndexChanged += delegate
            {
                if (_updatingNavigation || _navigation.SelectIndex < 0 || _navigation.SelectIndex >= _workspaceTabs.TabPages.Count) return;
                _workspaceTabs.SelectedIndex = _navigation.SelectIndex;
            };
            _workspaceTabs.SelectedIndexChanged += delegate
            {
                _updatingNavigation = true;
                try { _navigation.SelectIndex = _workspaceTabs.SelectedIndex; }
                finally { _updatingNavigation = false; }
            };
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(12, 3, 12, 3), Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var navigationHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            _navigation.Dock = DockStyle.Left;
            navigationHost.Controls.Add(_navigation);
            row.Controls.Add(navigationHost, 0, 0);
            OperatorTheme.Button(_connectionStatusLabel, false);
            _connectionStatusLabel.Name = "connection-settings";
            _connectionStatusLabel.Text = "ЕСМ: не проверено";
            _connectionStatusLabel.IconSvg = "SettingOutlined";
            _connectionStatusLabel.Margin = Padding.Empty;
            _connectionStatusLabel.Click += delegate { ShowConnectionSettings(); };
            row.Controls.Add(_connectionStatusLabel, 1, 0);
            _connectionDialog = BuildConnectionSettings();
            _baseUrlTextBox.TextChanged += delegate {
                _automaticToolTip.SetToolTip(_connectionStatusLabel, "Подключение ЕСМ: " + _baseUrlTextBox.Text);
            };
            return row;
        }

        private Form BuildConnectionSettings()
        {
            var dialog = new Form { Text = "Подключение к ЕСМ", Name = "connection-dialog",
                Icon = OperatorTheme.AppIcon,
                ClientSize = new Size(620, 240), FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MaximizeBox = false, MinimizeBox = false, Font = Font,
                ForeColor = OperatorTheme.Ink, BackColor = OperatorTheme.Surface };
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16),
                ColumnCount = 2, RowCount = 4 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int index = 0; index < 3; index++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(new Label { Text = "Адрес ЕСМ", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            root.Controls.Add(OperatorTheme.Input(_baseUrlTextBox), 1, 0);
            root.Controls.Add(new Label { Text = "Порт ДККТ", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            Control port = OperatorTheme.Input(_dkktPortTextBox);
            port.Dock = DockStyle.Left;
            port.Width = 110;
            root.Controls.Add(port, 1, 1);
            var hint = new Label { AutoSize = true, MaximumSize = new Size(560, 0),
                Text = "Обычно порт ДККТ — 4042. Это не GRPC-порт 4041 теста драйвера АТОЛ.",
                ForeColor = OperatorTheme.Muted, Margin = new Padding(0, 10, 0, 8) };
            root.Controls.Add(hint, 0, 2);
            root.SetColumnSpan(hint, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var close = new AntdUI.Button { Text = "Закрыть" };
            OperatorTheme.Button(close, false);
            close.Click += delegate { dialog.Close(); };
            ConfigureButton(_checkButton, "Проверить связь", CheckCurrentInstancesAsync);
            OperatorTheme.Button(_checkButton, true);
            actions.Controls.Add(close);
            actions.Controls.Add(_checkButton);
            root.Controls.Add(actions, 0, 3);
            root.SetColumnSpan(actions, 2);
            dialog.Controls.Add(root);
            dialog.CancelButton = close;
            dialog.AcceptButton = _checkButton;
            dialog.FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; dialog.Hide(); }
            };
            return dialog;
        }

        private void ShowConnectionSettings()
        {
            if (_connectionDialog.Visible) _connectionDialog.Activate();
            else _connectionDialog.Show(this);
        }
    }
}
