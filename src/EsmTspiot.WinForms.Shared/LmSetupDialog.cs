using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LmSetupDialog : Form
    {
        private readonly LmSetupEditor _editor;
        private readonly Action<IDictionary<string, string>, IDictionary<string, LmGatewayCredentials>> _save;

        internal LmSetupDialog(IList<LmGatewayKkt> kkts, IDictionary<string, string> remote,
            Func<string, string, LmGatewayCredentials> credentials,
            Action<IDictionary<string, string>, IDictionary<string, LmGatewayCredentials>> save)
        {
            _save = save;
            Text = "Настройка ЛМ ЧЗ";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(750, 510);
            MinimumSize = new Size(650, 420);
            Font = new Font("Segoe UI", 10F);
            ForeColor = OperatorTheme.Ink;
            Icon = OperatorTheme.AppIcon;
            BackColor = OperatorTheme.Surface;
            Shown += delegate { OperatorTheme.Dialog(this); };
            MinimizeBox = MaximizeBox = ShowInTaskbar = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12),
                ColumnCount = 1, RowCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(700, 0),
                Text = "Один ЛМ ЧЗ на ИНН. Локальные адреса и порты назначаются автоматически. " +
                    "Учётные данные используются только до закрытия программы.",
                Margin = new Padding(0, 0, 0, 8) }, 0, 0);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            _editor = new LmSetupEditor(kkts, remote, credentials, false);
            scroll.Controls.Add(_editor);
            root.Controls.Add(scroll, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var defer = new Button { Name = "lm-defer", Text = "Отложить",
                AutoSize = true, DialogResult = DialogResult.Cancel };
            var configure = new Button { Name = "lm-configure", Text = "Настроить", AutoSize = true };
            configure.Click += delegate { ConfirmSetup(); };
            buttons.Controls.Add(defer);
            buttons.Controls.Add(configure);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);
            AcceptButton = configure;
            CancelButton = defer;
        }

        internal bool TryReadValues(out IDictionary<string, string> endpoints,
            out IDictionary<string, LmGatewayCredentials> credentials, out string problem)
        {
            return _editor.TryReadValues(out endpoints, out credentials, out problem);
        }

        private void ConfirmSetup()
        {
            IDictionary<string, string> endpoints;
            IDictionary<string, LmGatewayCredentials> credentials;
            string problem;
            if (!TryReadValues(out endpoints, out credentials, out problem))
            {
                MessageBox.Show(this, problem ?? "Нет зарегистрированных ККТ для настройки.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                if (_save != null) _save(endpoints, credentials);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is InvalidDataException) &&
                    !(ex is UnauthorizedAccessException) && !(ex is ArgumentException)) throw;
                MessageBox.Show(this, SensitiveDataMasker.Mask(ex.Message), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { LmSetupEditor.ClearCredentials(credentials); }
        }
    }
}
