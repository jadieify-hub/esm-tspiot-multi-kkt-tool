using System;
using System.Drawing;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LmGatewayBindingDialog : Form
    {
        private readonly TextBox _loginTextBox = new TextBox();
        private readonly TextBox _passwordTextBox = new TextBox();
        private readonly Button _confirmButton = new Button();

        internal LmGatewayBindingDialog(
            string kktSerial,
            string kktInn,
            string controllerEndpoint)
        {
            Text = "Привязка контроллера к ЕСМ";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(530, 255);
            MinimumSize = new Size(500, 245);
            MaximumSize = new Size(700, 330);
            Font = new Font("Segoe UI", 8.25F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BuildLayout(
                (kktSerial ?? string.Empty).Trim(),
                (kktInn ?? string.Empty).Trim(),
                (controllerEndpoint ?? string.Empty).Trim());
        }

        internal LmGatewayCredentials Credentials
        {
            get
            {
                return new LmGatewayCredentials
                {
                    Login = (_loginTextBox.Text ?? string.Empty).Trim(),
                    Password = _passwordTextBox.Text ?? string.Empty
                };
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _loginTextBox.Clear();
                _passwordTextBox.Clear();
            }
            base.Dispose(disposing);
        }

        private void BuildLayout(string serial, string inn, string endpoint)
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 2,
                RowCount = 6
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label identity = new Label
            {
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Text = "ККТ " + serial + "   ИНН " + inn,
                Margin = new Padding(0, 0, 0, 8)
            };
            root.Controls.Add(identity, 0, 0);
            root.SetColumnSpan(identity, 2);

            Label endpointLabel = new Label
            {
                AutoSize = true,
                Text = "Контроллер: " + endpoint,
                Margin = new Padding(0, 0, 0, 10)
            };
            root.Controls.Add(endpointLabel, 0, 1);
            root.SetColumnSpan(endpointLabel, 2);

            AddField(root, 2, "Логин ЛМ ЧЗ", _loginTextBox);
            _passwordTextBox.UseSystemPasswordChar = true;
            AddField(root, 3, "Пароль ЛМ ЧЗ", _passwordTextBox);
            _loginTextBox.TextChanged += delegate { UpdateActionState(); };
            _passwordTextBox.TextChanged += delegate { UpdateActionState(); };

            Label notice = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(490, 0),
                ForeColor = SystemColors.GrayText,
                Text = "Данные используются только для одного документированного запроса к ЕСМ, " +
                    "не сохраняются и очищаются после операции.",
                Margin = new Padding(0, 10, 0, 8)
            };
            root.Controls.Add(notice, 0, 4);
            root.SetColumnSpan(notice, 2);

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
                DialogResult = DialogResult.Cancel
            };
            _confirmButton.Text = "Привязать к ЕСМ";
            _confirmButton.AutoSize = true;
            _confirmButton.Enabled = false;
            _confirmButton.DialogResult = DialogResult.OK;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_confirmButton);
            root.Controls.Add(buttons, 0, 5);
            root.SetColumnSpan(buttons, 2);

            AcceptButton = _confirmButton;
            CancelButton = cancel;
            Controls.Add(root);
            PrefillStandardCredentials();
        }

        private static void AddField(
            TableLayoutPanel table,
            int row,
            string labelText,
            TextBox textBox)
        {
            Label label = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Text = labelText,
                Margin = new Padding(0, 5, 8, 4)
            };
            textBox.Dock = DockStyle.Top;
            textBox.Margin = new Padding(0, 2, 0, 4);
            table.Controls.Add(label, 0, row);
            table.Controls.Add(textBox, 1, row);
        }

        private void UpdateActionState()
        {
            _confirmButton.Enabled =
                !string.IsNullOrWhiteSpace(_loginTextBox.Text) &&
                !string.IsNullOrWhiteSpace(_passwordTextBox.Text);
        }

        private void PrefillStandardCredentials()
        {
            LmGatewayCredentials credentials = LmGatewayCredentialDefaults.Create();
            try
            {
                _loginTextBox.Text = credentials.Login;
                _passwordTextBox.Text = credentials.Password;
            }
            finally
            {
                credentials.Login = string.Empty;
                credentials.Password = string.Empty;
            }
            UpdateActionState();
        }
    }
}
