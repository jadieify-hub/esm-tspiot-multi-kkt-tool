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
            Text = "Удаление контроллера ЛМ ЧЗ";
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
                Text = "Будут безвозвратно удалены контроллер этой ККТ и его локальные данные.\r\n\r\n" +
                    "Настройка связи в ЕСМ не очищается. Другие контроллеры не будут затронуты.",
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
            _removeButton.Text = "Удалить контроллер и данные";
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

    internal sealed class LmGatewayRemoveAllDialog : Form
    {
        internal const string ConfirmationPhrase = "УДАЛИТЬ ВСЕ";

        private readonly IList<LmServiceInventoryItem> _items;
        private readonly TextBox _confirmation = new TextBox();
        private readonly Button _removeButton = new Button();

        internal LmGatewayRemoveAllDialog(IList<LmServiceInventoryItem> items)
        {
            if (items == null || items.Count == 0)
            {
                throw new ArgumentException("Не заданы управляемые службы для удаления.", "items");
            }
            _items = items;
            Text = "Удаление всех контроллеров ЛМ ЧЗ";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(680, 500);
            MinimumSize = new Size(600, 430);
            Font = new Font("Segoe UI", 8.25F);
            Build();
        }

        internal IList<LmRemovalConfirmation> Confirmations { get; private set; }

        private void Build()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 6
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Будут удалены все контроллеры ЛМ ЧЗ, созданные этой программой: " +
                    _items.Count.ToString() + "."
            }, 0, 0);

            TextBox list = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                Text = BuildServiceList(),
                Margin = new Padding(0, 8, 0, 8)
            };
            root.Controls.Add(list, 0, 1);

            root.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DarkRed,
                Text = "Вместе с контроллерами будут удалены их локальные данные. Штатный контроллер не затрагивается.\r\n" +
                    "Привязки в ЕСМ автоматически не очищаются и после повторной установки " +
                    "могут потребовать повторной привязки."
            }, 0, 2);

            root.Controls.Add(new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0),
                Text = "Для подтверждения введите: " + ConfirmationPhrase
            }, 0, 3);
            _confirmation.Dock = DockStyle.Top;
            _confirmation.Margin = new Padding(0, 4, 0, 10);
            _confirmation.TextChanged += delegate
            {
                _removeButton.Enabled = string.Equals(
                    _confirmation.Text.Trim(),
                    ConfirmationPhrase,
                    StringComparison.Ordinal);
            };
            root.Controls.Add(_confirmation, 0, 4);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };
            Button cancel = new Button
            {
                Text = "Отмена",
                AutoSize = true,
                DialogResult = DialogResult.Cancel
            };
            _removeButton.Text = "Удалить все контроллеры и данные";
            _removeButton.AutoSize = true;
            _removeButton.Enabled = false;
            _removeButton.Click += delegate
            {
                List<LmRemovalConfirmation> confirmations =
                    new List<LmRemovalConfirmation>();
                for (int index = 0; index < _items.Count; index++)
                {
                    LmServiceInventoryItem item = _items[index];
                    confirmations.Add(new LmRemovalConfirmation
                    {
                        KktSerial = item.KktSerial,
                        GrpcPort = item.Ports.GrpcPort,
                        RestPort = item.Ports.RestPort,
                        ManifestFingerprint = item.ManifestFingerprint,
                        RetainedEsmWarningAccepted = true
                    });
                }
                Confirmations = confirmations;
                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_removeButton);
            root.Controls.Add(buttons, 0, 5);
            CancelButton = cancel;
            Controls.Add(root);
        }

        private string BuildServiceList()
        {
            StringBuilder text = new StringBuilder();
            for (int index = 0; index < _items.Count; index++)
            {
                LmServiceInventoryItem item = _items[index];
                text.Append(index + 1);
                text.Append(". ККТ ");
                text.Append(item.KktSerial);
                text.AppendLine();
            }
            return text.ToString();
        }
    }
}
