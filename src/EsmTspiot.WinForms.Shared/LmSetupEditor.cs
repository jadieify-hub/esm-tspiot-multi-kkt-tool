using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LmSetupEditor : UserControl
    {
        private readonly List<InnFields> _groups = new List<InnFields>();
        private readonly Func<string, string, LmGatewayCredentials> _credentials;
        private bool _creating = true;
        private bool _fmuApiMode;

        internal LmSetupEditor(IList<LmGatewayKkt> kkts, IDictionary<string, string> remote,
            Func<string, string, LmGatewayCredentials> credentials, bool fmuApiMode)
        {
            _credentials = credentials;
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = new Font("Segoe UI", 10F);
            ForeColor = OperatorTheme.Ink;
            var groups = new TableLayoutPanel {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 0
            };
            groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Controls.Add(groups);
            SortedDictionary<string, List<string>> byInn =
                new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            if (kkts != null)
                foreach (LmGatewayKkt kkt in kkts)
                {
                    if (kkt == null) continue;
                    string inn = (kkt.KktInn ?? string.Empty).Trim();
                    List<string> serials;
                    if (!byInn.TryGetValue(inn, out serials))
                    {
                        serials = new List<string>();
                        byInn.Add(inn, serials);
                    }
                    string serial = (kkt.KktSerial ?? string.Empty).Trim();
                    if (serial.Length > 0 && !serials.Contains(serial)) serials.Add(serial);
                }
            foreach (KeyValuePair<string, List<string>> pair in byInn)
            {
                string endpoint;
                if (remote == null || !remote.TryGetValue(pair.Key, out endpoint))
                    endpoint = string.Empty;
                InnFields fields = new InnFields { Inn = pair.Key };
                _groups.Add(fields);
                groups.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                int row = groups.RowCount++;
                groups.Controls.Add(BuildGroup(fields, pair.Value, endpoint), 0, row);
            }


            FmuApiMode = fmuApiMode;
            _creating = false;
        }

        internal event Action InputsChanged;

        internal bool FmuApiMode
        {
            get { return _fmuApiMode; }
            set
            {
                _fmuApiMode = value;
                foreach (InnFields fields in _groups)
                    for (int column = 0; column < 4; column++)
                    {
                        Control control = fields.Table.GetControlFromPosition(column, 4);
                        if (control != null) control.Visible = !value;
                    }
                OnInputsChanged();
            }
        }

        internal void UpdateKktSummary(IList<LmGatewayKkt> kkts)
        {
            foreach (InnFields fields in _groups)
            {
                var serials = new List<string>();
                foreach (LmGatewayKkt kkt in kkts)
                    if (kkt != null && kkt.KktInn == fields.Inn && !serials.Contains(kkt.KktSerial))
                        serials.Add(kkt.KktSerial);
                Control[] labels = Controls.Find("lm-kkts-" + fields.Inn, true);
                if (labels.Length > 0)
                {
                    labels[0].Text = "ККТ: " + string.Join(", ", serials.ToArray());
                    labels[0].Visible = serials.Count > 0;
                }
            }
        }

        private void OnInputsChanged()
        {
            if (!_creating && InputsChanged != null) InputsChanged();
        }

        internal static void ClearCredentials(IDictionary<string, LmGatewayCredentials> credentials)
        {
            if (credentials == null) return;
            foreach (LmGatewayCredentials value in credentials.Values)
            {
                value.Login = string.Empty;
                value.Password = string.Empty;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                foreach (InnFields fields in _groups)
                {
                    fields.Login.Clear();
                    fields.Password.Clear();
                }
            base.Dispose(disposing);
        }

        private Control BuildGroup(InnFields fields, IList<string> serials, string endpoint)
        {
            var group = new OperatorCard {
                Name = "lm-group-" + fields.Inn,
                Text = "ИНН " + (fields.Inn.Length == 0 ? "не указан" : fields.Inn),
                Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), Radius = 4,
                BorderWidth = 1, BorderColor = OperatorTheme.Line,
                Back = OperatorTheme.Surface, BackColor = OperatorTheme.Surface,
                Margin = new Padding(0, 0, 18, 8)
            };
            TableLayoutPanel table = new TableLayoutPanel {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 6
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            for (int index = 0; index < table.RowCount; index++)
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label kkts = new Label {
                Name = "lm-kkts-" + fields.Inn, AutoSize = true,
                Text = serials.Count == 0 ? "ККТ: —" : "ККТ: " + string.Join(", ", new List<string>(serials).ToArray()),
                Margin = new Padding(0, 0, 0, 4), MaximumSize = new Size(620, 0), Visible = serials.Count > 0
            };
            table.Controls.Add(kkts, 0, 0);
            table.SetColumnSpan(kkts, 4);
            OperatorTheme.Segments(fields.Placement, "На этом компьютере", "На другом компьютере");
            fields.Placement.Name = "lm-placement-" + fields.Inn;
            fields.Placement.Height = 38;
            fields.Placement.Dock = DockStyle.Fill;
            fields.Placement.Margin = new Padding(0, 0, 0, 4);
            table.Controls.Add(fields.Placement, 0, 1);
            table.SetColumnSpan(fields.Placement, 4);
            AddField(table, 0, 2, "Адрес", fields.Host, "lm-host-" + fields.Inn);
            AddField(table, 2, 2, "Порт", fields.Port, "lm-port-" + fields.Inn);
            fields.HostError.Name = "lm-host-error-" + fields.Inn;
            fields.PortError.Name = "lm-port-error-" + fields.Inn;
            table.Controls.Add(fields.HostError, 1, 3);
            table.Controls.Add(fields.PortError, 3, 3);
            AddField(table, 0, 4, "Логин", fields.Login, "lm-login-" + fields.Inn);
            AddField(table, 2, 4, "Пароль", fields.Password, "lm-password-" + fields.Inn);
            fields.LocalHint.Text = "ЛМ будет установлен локально. Адрес и порт назначаются автоматически.";
            fields.LocalHint.AutoSize = true;
            fields.LocalHint.Margin = new Padding(0, 6, 0, 0);
            table.Controls.Add(fields.LocalHint, 0, 5);
            table.SetColumnSpan(fields.LocalHint, 4);
            fields.Password.UseSystemPasswordChar = true;
            fields.Port.Text = string.Empty;
            string host;
            int port;
            bool isRemote = LocalModuleTargetAddress.TryParseEndpoint(endpoint, out host, out port);
            if (isRemote)
            {
                fields.Host.Text = host;
                fields.Port.Text = port.ToString(CultureInfo.InvariantCulture);
            }
            fields.Placement.SelectIndex = isRemote ? 1 : 0;
            fields.Placement.SelectIndexChanged += delegate { UpdateEndpoint(fields); OnInputsChanged(); };
            fields.Host.TextChanged += delegate { UpdateEndpoint(fields); OnInputsChanged(); };
            fields.Port.TextChanged += delegate { UpdateEndpoint(fields); OnInputsChanged(); };
            fields.Login.TextChanged += delegate { OnInputsChanged(); };
            fields.Password.TextChanged += delegate { OnInputsChanged(); };
            fields.Table = table;
            UpdateEndpoint(fields);
            var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.Controls.Add(new Label { Text = group.Text, AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = OperatorTheme.Ink,
                Margin = new Padding(0, 0, 0, 6) }, 0, 0);
            content.Controls.Add(table, 0, 1);
            group.Controls.Add(content);
            return group;
        }

        private static void AddField(TableLayoutPanel table, int column, int row,
            string text, TextBox input, string name)
        {
            table.Controls.Add(new Label {
                Text = text, AutoSize = true, Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 8, 4)
            }, column, row);
            input.Name = name;
            input.Dock = DockStyle.Top;
            input.Margin = new Padding(0, 2, 12, 4);
            table.Controls.Add(OperatorTheme.Input(input), column + 1, row);
        }

        private void UpdateEndpoint(InnFields fields)
        {
            fields.Host.Enabled = fields.Placement.SelectIndex == 1;
            fields.Port.Enabled = fields.Placement.SelectIndex == 1;
            fields.LocalHint.Visible = fields.Placement.SelectIndex == 0;
            for (int column = 0; column < 4; column++)
            {
                Control control = fields.Table.GetControlFromPosition(column, 2);
                if (control != null) control.Visible = fields.Placement.SelectIndex == 1;
            }
            string endpoint = string.Empty;
            string hostError = null;
            string portError = null;
            if (fields.Placement.SelectIndex == 1)
            {
                string host = fields.Host.Text.Trim();
                int port;
                if (!LocalModuleTargetAddress.IsValidRemoteHost(host))
                    hostError = host.Length == 0 ? "Введите адрес другого компьютера."
                        : "Укажите IPv4 или имя другого компьютера.";
                portError = LocalModuleTargetAddress.GetRemotePortError(fields.Port.Text.Trim().TrimEnd('/'), out port);
                endpoint = hostError == null && portError == null ? LocalModuleTargetAddress.Key(host, port) : null;
            }
            fields.HostError.Text = hostError ?? string.Empty;
            fields.HostError.Visible = hostError != null;
            fields.PortError.Text = portError ?? string.Empty;
            fields.PortError.Visible = portError != null;
            string previousEndpoint = fields.Endpoint;
            fields.Endpoint = endpoint;
            if (string.Equals(endpoint, previousEndpoint, StringComparison.OrdinalIgnoreCase)) return;
            LmGatewayCredentials value = endpoint != null && _credentials != null
                ? _credentials(fields.Inn, endpoint) : LmGatewayCredentialDefaults.Create();
            if (value == null) value = LmGatewayCredentialDefaults.Create();
            try
            {
                fields.Login.Text = value.Login;
                fields.Password.Text = value.Password;
            }
            finally
            {
                value.Login = string.Empty;
                value.Password = string.Empty;
            }
        }

        internal bool TryReadValues(out IDictionary<string, string> endpoints,
            out IDictionary<string, LmGatewayCredentials> credentials, out string problem)
        {
            endpoints = new Dictionary<string, string>(StringComparer.Ordinal);
            credentials = new Dictionary<string, LmGatewayCredentials>(StringComparer.Ordinal);
            problem = null;
            foreach (InnFields fields in _groups)
            {
                if (!LocalModuleMsiIdentity.IsInn(fields.Inn))
                    problem = "ИНН должен содержать 10 или 12 цифр. Проверьте регистрацию ККТ.";
                else if (fields.Endpoint == null)
                    problem = "ИНН " + fields.Inn + ". " +
                        (fields.HostError.Text.Length > 0 ? fields.HostError.Text : fields.PortError.Text);
                else if (!FmuApiMode && (string.IsNullOrWhiteSpace(fields.Login.Text) ||
                    string.IsNullOrWhiteSpace(fields.Password.Text)))
                    problem = "Для ИНН " + fields.Inn + " заполните логин и пароль ЛМ ЧЗ.";
                else
                {
                    endpoints.Add(fields.Inn, fields.Endpoint);
                    credentials.Add(fields.Inn, new LmGatewayCredentials {
                        Login = FmuApiMode ? "admin" : fields.Login.Text.Trim(),
                        Password = FmuApiMode ? "admin" : fields.Password.Text
                    });
                }
                if (problem != null)
                {
                    ClearCredentials(credentials);
                    return false;
                }
            }
            return _groups.Count > 0;
        }


        private sealed class InnFields
        {
            internal TableLayoutPanel Table;
            internal string Inn;
            internal string Endpoint;
            internal readonly Label HostError = new Label { AutoSize = true, ForeColor = OperatorTheme.Error };
            internal readonly Label PortError = new Label { AutoSize = true, ForeColor = OperatorTheme.Error };
            internal readonly AntdUI.Segmented Placement = new OperatorSegments();
            internal readonly TextBox Host = new TextBox();
            internal readonly TextBox Port = new TextBox();
            internal readonly TextBox Login = new TextBox();
            internal readonly TextBox Password = new TextBox();
            internal readonly Label LocalHint = new Label();
        }
    }
}
