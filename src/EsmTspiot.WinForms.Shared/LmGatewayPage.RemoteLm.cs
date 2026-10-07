using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    // Касса без своего ЛМ: контроллер ККТ смотрит на ЛМ «Сервера ЛМ ЧЗ».
    // ИНН из этого списка не получают ЛМ на этом компьютере.
    public sealed partial class LmGatewayPage
    {
        public bool HasRemoteLocalModules
        {
            get { return ReadRemoteLocalModules().Count > 0; }
        }

        public void ShowRemoteLocalModules(IWin32Window owner)
        {
            SortedDictionary<string, string> stored = ReadRemoteLocalModules();
            using (Form dialog = new Form())
            {
                dialog.Text = "ЛМ ЧЗ на другом компьютере";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(620, 420);
                dialog.MinimumSize = new Size(520, 320);

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.Padding = new Padding(10);
                layout.ColumnCount = 1;
                layout.RowCount = 3;
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                dialog.Controls.Add(layout);

                Label intro = new Label();
                intro.AutoSize = true;
                intro.MaximumSize = new Size(590, 0);
                intro.Margin = new Padding(0, 0, 0, 8);
                intro.Text =
                    "Для ИНН, чей ЛМ стоит на «Сервере ЛМ ЧЗ», укажите адрес " +
                    "из окна сервера, например 192.168.1.10:6995. Пустой адрес — " +
                    "ЛМ ставится на этот компьютер. Контроллер ККТ будет " +
                    "направлен на указанный ЛМ при следующей настройке.";
                layout.Controls.Add(intro, 0, 0);

                DataGridView grid = new DataGridView();
                grid.Dock = DockStyle.Fill;
                grid.AllowUserToAddRows = true;
                grid.AllowUserToDeleteRows = true;
                grid.RowHeadersVisible = false;
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                grid.Columns.Add("inn", "ИНН");
                grid.Columns.Add("endpoint", "Адрес ЛМ (адрес:порт)");
                grid.Columns[0].FillWeight = 35;
                grid.Columns[1].FillWeight = 65;
                SortedDictionary<string, string> rows =
                    new SortedDictionary<string, string>(stored, StringComparer.Ordinal);
                if (_currentDiscovery != null)
                    for (int index = 0; index < _currentDiscovery.Items.Count; index++)
                    {
                        LmGatewayKkt kkt = _currentDiscovery.Items[index];
                        string inn = kkt == null ? null : (kkt.KktInn ?? string.Empty).Trim();
                        if (LocalModuleMsiIdentity.IsInn(inn) && !rows.ContainsKey(inn))
                            rows.Add(inn, string.Empty);
                    }
                foreach (KeyValuePair<string, string> pair in rows)
                    grid.Rows.Add(pair.Key, pair.Value);
                layout.Controls.Add(grid, 0, 1);

                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.AutoSize = true;
                buttons.Dock = DockStyle.Fill;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                Button cancel = new Button();
                cancel.AutoSize = true;
                cancel.Text = "Отмена";
                cancel.DialogResult = DialogResult.Cancel;
                Button save = new Button();
                save.AutoSize = true;
                save.Text = "Сохранить";
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(save);
                layout.Controls.Add(buttons, 0, 2);
                dialog.CancelButton = cancel;

                save.Click += delegate
                {
                    Dictionary<string, string> result =
                        new Dictionary<string, string>(StringComparer.Ordinal);
                    HashSet<string> endpoints =
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int index = 0; index < grid.Rows.Count; index++)
                    {
                        DataGridViewRow row = grid.Rows[index];
                        if (row.IsNewRow) continue;
                        string inn = Convert.ToString(row.Cells[0].Value,
                            CultureInfo.InvariantCulture).Trim();
                        string text = Convert.ToString(row.Cells[1].Value,
                            CultureInfo.InvariantCulture).Trim();
                        if (text.Length == 0) continue;
                        string host;
                        int port;
                        string problem = null;
                        int column = 1;
                        if (!LocalModuleMsiIdentity.IsInn(inn))
                        {
                            problem = "ИНН «" + inn + "» должен содержать 10 или 12 цифр.";
                            column = 0;
                        }
                        else if (result.ContainsKey(inn))
                        {
                            problem = "ИНН " + inn + " указан дважды.";
                            column = 0;
                        }
                        else if (!LocalModuleTargetAddress.TryParseEndpoint(
                                text, out host, out port))
                            problem = "Адрес «" + text + "» для ИНН " + inn +
                                " не распознан. Нужен адрес:порт другого " +
                                "компьютера, например 192.168.1.10:6995.";
                        else if (!endpoints.Add(LocalModuleTargetAddress.Key(host, port)))
                            problem = "Адрес " + text + " уже указан для другого ИНН: " +
                                "один ЛМ обслуживает один ИНН.";
                        else
                            result.Add(inn, LocalModuleTargetAddress.Key(host, port));
                        if (problem != null)
                        {
                            grid.CurrentCell = row.Cells[column];
                            MessageBox.Show(dialog, problem, dialog.Text,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    try
                    {
                        RemoteLocalModuleStore.CreateDefault().Save(result);
                    }
                    catch (Exception ex)
                    {
                        if (!(ex is IOException) &&
                            !(ex is UnauthorizedAccessException) &&
                            !(ex is ArgumentException))
                            throw;
                        MessageBox.Show(dialog, SensitiveDataMasker.Mask(ex.Message),
                            dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    Log(result.Count == 0
                        ? "ЛМ на другом компьютере не заданы: все ИНН получают ЛМ здесь.\r\n"
                        : "ЛМ на другом компьютере: " + DescribeRemote(result) + ".\r\n");
                    dialog.DialogResult = DialogResult.OK;
                };
                dialog.ShowDialog(owner);
            }
        }

        private static string DescribeRemote(IDictionary<string, string> remote)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> pair in remote)
                parts.Add("ИНН " + pair.Key + " → " + pair.Value);
            return string.Join(", ", parts.ToArray());
        }

        private SortedDictionary<string, string> ReadRemoteLocalModules()
        {
            try
            {
                return RemoteLocalModuleStore.CreateDefault().Load();
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException) &&
                    !(ex is InvalidDataException))
                    throw;
                Log("Адреса ЛМ на другом компьютере не прочитаны: " +
                    SensitiveDataMasker.Mask(ex.Message) + "\r\n");
                return new SortedDictionary<string, string>(StringComparer.Ordinal);
            }
        }

        // ККТ, чьи ИНН получают ЛМ на этом компьютере.
        private IList<LmGatewayKkt> SelectLocalLmKkts(IList<LmGatewayKkt> kkts)
        {
            SortedDictionary<string, string> remote = ReadRemoteLocalModules();
            List<LmGatewayKkt> result = new List<LmGatewayKkt>();
            if (kkts == null) return result;
            for (int index = 0; index < kkts.Count; index++)
            {
                LmGatewayKkt kkt = kkts[index];
                string inn = kkt == null ? string.Empty : (kkt.KktInn ?? string.Empty).Trim();
                if (!remote.ContainsKey(inn)) result.Add(kkt);
            }
            return result;
        }

        // Порты ЛМ для контроллеров: локальные — из плана MSI, удалённые —
        // из адресов, введённых оператором.
        private IDictionary<string, int> CreateControllerTargetPorts(
            LocalModuleMsiPlan plan)
        {
            IDictionary<string, int> result = plan.CreateTargetApiPortMap();
            foreach (KeyValuePair<string, string> pair in ReadRemoteLocalModules())
            {
                string host;
                int port;
                if (LocalModuleTargetAddress.TryParseEndpoint(pair.Value, out host, out port))
                    result[pair.Key] = port;
            }
            return result;
        }

        private IDictionary<string, string> CreateControllerTargetAddresses()
        {
            Dictionary<string, string> result =
                new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in ReadRemoteLocalModules())
            {
                string host;
                int port;
                if (LocalModuleTargetAddress.TryParseEndpoint(pair.Value, out host, out port))
                    result[pair.Key] = host;
            }
            return result;
        }
    }
}
