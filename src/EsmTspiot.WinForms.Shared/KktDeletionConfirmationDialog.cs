using System;
using System.Drawing;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    public sealed class KktDeletionConfirmationDialog : Form
    {
        private readonly KktInstanceInfo _instance;
        private readonly TextBox _confirmationTextBox = new TextBox();
        private readonly Button _deleteButton = new Button();
        private readonly Button _cancelButton = new Button();

        public KktDeletionConfirmationDialog(KktInstanceInfo instance)
        {
            if (instance == null)
            {
                throw new ArgumentNullException("instance");
            }

            _instance = instance;
            Text = "Удаление дополнительной ККТ";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(430, 205);
            MinimumSize = new Size(400, 205);
            MaximumSize = new Size(620, 260);
            Font = new Font("Segoe UI", 8.25F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            BuildLayout();
        }

        public string ConfirmationText
        {
            get { return (_confirmationTextBox.Text ?? string.Empty).Trim(); }
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label title = new Label();
            title.AutoSize = true;
            title.Font = new Font(Font, FontStyle.Bold);
            title.Text = "Будет удален экземпляр ККТ " + (_instance.Id ?? string.Empty);
            title.Margin = new Padding(0, 0, 0, 8);

            Label details = new Label();
            details.AutoSize = true;
            details.Text = "port: " + (_instance.Port ?? string.Empty) +
                "    softPort: " + (_instance.SoftPort ?? string.Empty) +
                "    состояние: " +
                KktServiceStateFormatter.ToDisplayText(
                    _instance.ServiceState);
            details.Margin = new Padding(0, 0, 0, 10);

            Label confirmationLabel = new Label();
            confirmationLabel.AutoSize = true;
            confirmationLabel.Text = "Введите последние 4 цифры серийного номера ККТ:";
            confirmationLabel.Margin = new Padding(0, 0, 0, 4);

            _confirmationTextBox.Width = 110;
            _confirmationTextBox.MaxLength = 4;
            _confirmationTextBox.TextChanged += delegate
            {
                _deleteButton.Enabled = KktDeletionConfirmation.Matches(_instance.Id, _confirmationTextBox.Text);
            };

            FlowLayoutPanel confirmationRow = new FlowLayoutPanel();
            confirmationRow.Dock = DockStyle.Fill;
            confirmationRow.AutoSize = true;
            confirmationRow.WrapContents = false;
            confirmationRow.Controls.Add(_confirmationTextBox);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;

            _deleteButton.Text = "Удалить ККТ";
            _deleteButton.AutoSize = true;
            _deleteButton.Enabled = false;
            _deleteButton.DialogResult = DialogResult.OK;

            _cancelButton.Text = "Отмена";
            _cancelButton.AutoSize = true;
            _cancelButton.DialogResult = DialogResult.Cancel;

            buttons.Controls.Add(_cancelButton);
            buttons.Controls.Add(_deleteButton);

            root.Controls.Add(title, 0, 0);
            root.Controls.Add(details, 0, 1);
            root.Controls.Add(confirmationLabel, 0, 2);
            root.Controls.Add(confirmationRow, 0, 3);
            root.Controls.Add(buttons, 0, 4);
            Controls.Add(root);

            AcceptButton = _deleteButton;
            CancelButton = _cancelButton;
        }
    }
}
