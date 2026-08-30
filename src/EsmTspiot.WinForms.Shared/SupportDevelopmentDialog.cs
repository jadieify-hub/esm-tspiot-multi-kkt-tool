using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class SupportDevelopmentDialog : Form
    {
        private const string SupportUrl =
            "https://pay.cloudtips.ru/p/53698013";
        private const string QrResourceName =
            "EsmTspiot.WinForms.Shared.Assets.support-cloudtips-qr.png";

        private readonly PictureBox _qrPictureBox = new PictureBox();
        private readonly TextBox _urlTextBox = new TextBox();
        private readonly Button _copyButton = new Button();
        private readonly Label _copyStatusLabel = new Label();
        private readonly Button _closeButton = new Button();
        private readonly Image _qrImage;

        public SupportDevelopmentDialog()
        {
            Text = "Поддержать разработку";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(392, 476);

            _qrImage = LoadQrImage();
            _qrPictureBox.Image = _qrImage;
            _qrPictureBox.Size = new Size(296, 296);
            _qrPictureBox.SizeMode = PictureBoxSizeMode.Normal;
            _qrPictureBox.Anchor = AnchorStyles.Top;
            _qrPictureBox.Margin = new Padding(0, 0, 0, 10);
            _qrPictureBox.TabStop = false;

            _urlTextBox.Text = SupportUrl;
            _urlTextBox.ReadOnly = true;
            _urlTextBox.Dock = DockStyle.Fill;
            _urlTextBox.Margin = new Padding(0, 3, 8, 3);

            _copyButton.Text = "Скопировать ссылку";
            _copyButton.AutoSize = true;
            _copyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _copyButton.Click += delegate { CopySupportUrl(); };

            TableLayoutPanel linkRow = new TableLayoutPanel();
            linkRow.Dock = DockStyle.Fill;
            linkRow.AutoSize = true;
            linkRow.ColumnCount = 2;
            linkRow.RowCount = 1;
            linkRow.Margin = new Padding(0);
            linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            linkRow.Controls.Add(_urlTextBox, 0, 0);
            linkRow.Controls.Add(_copyButton, 1, 0);

            _copyStatusLabel.AutoSize = false;
            _copyStatusLabel.Dock = DockStyle.Fill;
            _copyStatusLabel.Height = 22;
            _copyStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _copyStatusLabel.ForeColor = SystemColors.GrayText;
            _copyStatusLabel.Margin = new Padding(0, 0, 0, 4);

            Label offlineNotice = new Label();
            offlineNotice.Text =
                "Отсканируйте код телефоном. Программа при этом никуда " +
                "не обращается и ничего не передаёт.";
            offlineNotice.AutoSize = true;
            offlineNotice.Dock = DockStyle.Fill;
            offlineNotice.MaximumSize = new Size(360, 0);
            offlineNotice.TextAlign = ContentAlignment.MiddleCenter;
            offlineNotice.ForeColor = SystemColors.GrayText;
            offlineNotice.Margin = new Padding(0, 0, 0, 10);

            _closeButton.Text = "Закрыть";
            _closeButton.AutoSize = true;
            _closeButton.DialogResult = DialogResult.Cancel;
            _closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(16);
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_qrPictureBox, 0, 0);
            root.Controls.Add(linkRow, 0, 1);
            root.Controls.Add(_copyStatusLabel, 0, 2);
            root.Controls.Add(offlineNotice, 0, 3);
            root.Controls.Add(_closeButton, 0, 4);
            Controls.Add(root);

            AcceptButton = _closeButton;
            CancelButton = _closeButton;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _qrImage != null)
            {
                _qrPictureBox.Image = null;
                _qrImage.Dispose();
            }
            base.Dispose(disposing);
        }

        private void CopySupportUrl()
        {
            try
            {
                Clipboard.SetText(SupportUrl);
                _copyStatusLabel.Text = "Ссылка скопирована.";
            }
            catch (Exception)
            {
                _copyStatusLabel.Text = "Не удалось скопировать ссылку.";
            }
        }

        private static Image LoadQrImage()
        {
            Assembly assembly = typeof(SupportDevelopmentDialog).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(
                QrResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        "Встроенный QR-код поддержки не найден.");
                }
                using (Image source = Image.FromStream(stream, true, true))
                {
                    if (source.Width != 296 || source.Height != 296)
                    {
                        throw new InvalidDataException(
                            "Встроенный QR-код поддержки имеет неверный размер.");
                    }
                    return new Bitmap(source);
                }
            }
        }
    }
}
