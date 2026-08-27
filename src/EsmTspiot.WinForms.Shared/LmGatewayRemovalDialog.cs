using System;
using System.Drawing;
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
            _item = item ?? throw new ArgumentNullException("item");
            Text = "Удаление службы контроллера ЛМ";
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
                Text = "ККТ: " + _item.KktSerial + "   ИНН: " + (inn ?? string.Empty) +
                    "\r\nСлужба: " + _item.ServiceName +
                    "\r\nПорты: gRPC " + _item.Ports.GrpcPort + ", REST " + _item.Ports.RestPort
            };
            root.Controls.Add(identity, 0, 0);
            Label warning = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.DarkRed,
                Text = "Будут безвозвратно удалены Windows-служба, локальный профиль, сертификаты, логи и метаданные этого экземпляра.\r\n\r\n" +
                    "Настройка связи в ЕСМ НЕ очищается и может по-прежнему ссылаться на этот порт. Другие контроллеры и штатная служба не затрагиваются.",
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
            _removeButton.Text = "Удалить службу и локальные данные";
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
