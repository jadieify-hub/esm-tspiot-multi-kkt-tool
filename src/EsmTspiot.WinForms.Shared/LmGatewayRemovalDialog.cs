using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LmGatewayRemovalDialog : Form
    {
        private readonly LmServiceInventoryItem _item;
        private readonly TextBox _confirmation = new TextBox();
        private readonly Button _removeButton = new Button();

        internal LmGatewayRemovalDialog(LmServiceInventoryItem item, string kktInn)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }
            _item = item;
            Text = "Удаление комплекта ККТ";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(590, 360);
            MinimumSize = new Size(520, 320);
            Font = new Font("Segoe UI", 8.25F);
            Build(kktInn);
        }

        internal LmRemovalConfirmation Confirmation { get; private set; }

        private void Build(string inn)
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 5
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label identity = new Label
            {
                AutoSize = true,
                Text = "ККТ: " + _item.KktSerial + "   ИНН: " + (inn ?? string.Empty)
            };
            root.Controls.Add(identity, 0, 0);
            Label warning = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.DarkRed,
                Text = "Будут удалены созданные программой контроллер этой ККТ, профиль и связанный ЛМ ЧЗ, " +
                    "если он больше не нужен другим ККТ того же ИНН.\r\n\r\n" +
                    "Настройка связи в ЕСМ не очищается. Другие комплекты не будут затронуты.",
                Margin = new Padding(0, 12, 0, 12)
            };
            root.Controls.Add(warning, 0, 1);
            root.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Для подтверждения введите полный серийный номер ККТ " + _item.KktSerial + ":"
            }, 0, 2);
            _confirmation.Dock = DockStyle.Top;
            _confirmation.Margin = new Padding(0, 4, 0, 10);
            _confirmation.TextChanged += delegate
            {
                _removeButton.Enabled = string.Equals(
                    _confirmation.Text.Trim(),
                    _item.KktSerial,
                    StringComparison.Ordinal);
            };
            root.Controls.Add(_confirmation, 0, 3);
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };
            Button cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
            _removeButton.Text = "Удалить комплект и данные";
            _removeButton.AutoSize = true;
            _removeButton.Enabled = false;
            _removeButton.Click += delegate
            {
                Confirmation = new LmRemovalConfirmation
                {
                    KktSerial = _item.KktSerial,
                    GrpcPort = _item.Ports.GrpcPort,
                    RestPort = _item.Ports.RestPort,
                    ManifestFingerprint = _item.ManifestFingerprint,
                    ManagedStateFingerprint =
                        _item.ManagedStateFingerprint,
                    RetainedEsmWarningAccepted = true
                };
                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_removeButton);
            root.Controls.Add(buttons, 0, 4);
            CancelButton = cancel;
            Controls.Add(root);
        }
    }

}
