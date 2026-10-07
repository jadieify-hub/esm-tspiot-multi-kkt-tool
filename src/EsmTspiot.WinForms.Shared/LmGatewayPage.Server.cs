using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    // Выделенный компьютер под ЛМ ЧЗ: несколько экземпляров по номерам,
    // без ЕСМ и ККТ; кассы обращаются к ним по сети через свои контроллеры.
    public sealed partial class LmGatewayPage
    {
        private const uint ServiceRunningState = 4;

        public void ShowLocalModuleServer(IWin32Window owner)
        {
            if (_running || _hostBusy)
            {
                MessageBox.Show(
                    owner,
                    "Дождитесь завершения текущей операции.",
                    "Сервер ЛМ ЧЗ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            using (Form dialog = new Form())
            {
                dialog.Text = "Сервер ЛМ ЧЗ";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.Sizable;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(780, 480);
                dialog.MinimumSize = new Size(640, 400);

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.Padding = new Padding(10);
                layout.ColumnCount = 1;
                layout.RowCount = 6;
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                dialog.Controls.Add(layout);

                Label intro = new Label();
                intro.AutoSize = true;
                intro.MaximumSize = new Size(740, 0);
                intro.Margin = new Padding(0, 0, 0, 8);
                intro.Text =
                    "Этот компьютер держит ЛМ ЧЗ для касс. ИНН здесь не нужен: " +
                    "экземпляры идут по номерам, ИНН ЛМ получит при " +
                    "инициализации. На кассе укажите адрес нужного ЛМ из " +
                    "таблицы. Правило брандмауэра открывает порт API для " +
                    "локальной подсети во всех профилях сети.";
                layout.Controls.Add(intro, 0, 0);

                FlowLayoutPanel msiRow = CreateServerRow();
                Button selectMsi = new Button();
                selectMsi.AutoSize = true;
                selectMsi.Text = "Выбрать MSI ЛМ ЧЗ...";
                Label msiLabel = new Label();
                msiLabel.AutoSize = true;
                msiLabel.Margin = new Padding(6, 8, 0, 0);
                msiRow.Controls.Add(selectMsi);
                msiRow.Controls.Add(msiLabel);
                layout.Controls.Add(msiRow, 0, 1);

                FlowLayoutPanel countRow = CreateServerRow();
                Label countLabel = new Label();
                countLabel.AutoSize = true;
                countLabel.Margin = new Padding(0, 8, 6, 0);
                countLabel.Text = "Сколько ЛМ нужно:";
                NumericUpDown count = new NumericUpDown();
                count.Minimum = 0;
                count.Maximum = LocalModuleServerKey.MaximumCount;
                count.Width = 60;
                count.Margin = new Padding(0, 5, 6, 0);
                Button apply = new Button();
                apply.AutoSize = true;
                apply.Text = "Установить / привести к числу";
                Button removeAll = new Button();
                removeAll.AutoSize = true;
                removeAll.Text = "Удалить все ЛМ сервера";
                countRow.Controls.Add(countLabel);
                countRow.Controls.Add(count);
                countRow.Controls.Add(apply);
                countRow.Controls.Add(removeAll);
                layout.Controls.Add(countRow, 0, 2);

                ListView table = new ListView();
                table.Dock = DockStyle.Fill;
                table.View = View.Details;
                table.FullRowSelect = true;
                table.GridLines = true;
                table.HideSelection = false;
                table.Columns.Add("№", 40);
                table.Columns.Add("Службы", 170);
                table.Columns.Add("Порт API", 80);
                table.Columns.Add("Адрес для касс", 200);
                table.Columns.Add("Состояние", 240);
                layout.Controls.Add(table, 0, 3);

                Label status = new Label();
                status.AutoSize = true;
                status.MaximumSize = new Size(740, 0);
                status.Margin = new Padding(0, 6, 0, 6);
                layout.Controls.Add(status, 0, 4);

                FlowLayoutPanel bottomRow = CreateServerRow();
                bottomRow.FlowDirection = FlowDirection.RightToLeft;
                bottomRow.Dock = DockStyle.Fill;
                Button close = new Button();
                close.AutoSize = true;
                close.Text = "Закрыть";
                close.DialogResult = DialogResult.Cancel;
                Button copy = new Button();
                copy.AutoSize = true;
                copy.Text = "Скопировать адреса";
                Button reload = new Button();
                reload.AutoSize = true;
                reload.Text = "Обновить";
                bottomRow.Controls.Add(close);
                bottomRow.Controls.Add(copy);
                bottomRow.Controls.Add(reload);
                layout.Controls.Add(bottomRow, 0, 5);
                dialog.CancelButton = close;

                bool busy = false;
                Action showMsi = delegate
                {
                    msiLabel.Text = _localModuleInstallerSelection == null
                        ? "MSI не выбран; он нужен только для установки."
                        : _localModuleInstallerSelection.FileName +
                            " | версия " +
                            _localModuleInstallerSelection.ProductVersion;
                };
                Action reloadTable = delegate
                {
                    IList<LocalModuleMsiInventoryItem> items =
                        ReadServerLocalModules();
                    FillServerTable(table, items);
                    if (!busy) count.Value = items.Count;
                    status.Text = items.Count == 0
                        ? "ЛМ сервера не установлены."
                        : "Установлено ЛМ сервера: " + items.Count.ToString(
                            CultureInfo.InvariantCulture) + ".";
                };
                Func<int, Task> run = async delegate(int target)
                {
                    busy = true;
                    apply.Enabled = removeAll.Enabled = selectMsi.Enabled =
                        reload.Enabled = close.Enabled = false;
                    status.Text = "Выполняется; " + ElevationHint() + "...";
                    try
                    {
                        await RunOperationAsync(
                            delegate(CancellationToken cancellation)
                            {
                                return ApplyServerLocalModuleCountAsync(
                                    dialog, target, cancellation);
                            },
                            "Сервер ЛМ ЧЗ: " + ElevationHint() + "...");
                    }
                    finally
                    {
                        busy = false;
                        apply.Enabled = removeAll.Enabled = selectMsi.Enabled =
                            reload.Enabled = close.Enabled = true;
                        RefreshServiceInventory();
                        reloadTable();
                    }
                };

                selectMsi.Click += delegate
                {
                    SelectLocalModuleInstaller(dialog);
                    showMsi();
                };
                apply.Click += async delegate
                {
                    await run((int)count.Value);
                };
                removeAll.Click += async delegate
                {
                    await run(0);
                };
                reload.Click += delegate { reloadTable(); };
                copy.Click += delegate
                {
                    string text = FormatServerAddresses(
                        ReadServerLocalModules());
                    if (text.Length == 0)
                    {
                        status.Text = "Копировать нечего: ЛМ сервера не установлены.";
                        return;
                    }
                    Clipboard.SetText(text);
                    status.Text = "Адреса скопированы.";
                };
                dialog.FormClosing += delegate(object sender, FormClosingEventArgs e)
                {
                    if (busy) e.Cancel = true;
                };

                showMsi();
                reloadTable();
                dialog.ShowDialog(owner);
            }
        }

        private static FlowLayoutPanel CreateServerRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.WrapContents = false;
            row.Margin = new Padding(0, 0, 0, 4);
            return row;
        }

        private async Task ApplyServerLocalModuleCountAsync(
            IWin32Window owner,
            int target,
            CancellationToken cancellation)
        {
            LocalModuleMsiOperatorInventorySnapshot inventory =
                CreateLocalModuleInventoryReader().Read();
            List<LocalModuleMsiInventoryItem> current =
                SelectServerLocalModules(inventory);
            List<LocalModuleMsiInventoryItem> extra =
                new List<LocalModuleMsiInventoryItem>();
            StringBuilder extraNames = new StringBuilder();
            for (int index = 0; index < current.Count; index++)
            {
                int number;
                LocalModuleServerKey.TryGetNumber(current[index].Inn, out number);
                if (number <= target) continue;
                extra.Add(current[index]);
                if (extraNames.Length > 0) extraNames.Append(", ");
                extraNames.Append("№").Append(number.ToString(
                    CultureInfo.InvariantCulture));
            }

            if (extra.Count > 0)
            {
                if (MessageBox.Show(
                        owner,
                        "Будут удалены ЛМ сервера " + extraNames +
                            " вместе со службами, данными и правилом " +
                            "брандмауэра. Кассы, настроенные на эти адреса, " +
                            "потеряют ЛМ. Продолжить?",
                        "Сервер ЛМ ЧЗ",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    Log("Удаление ЛМ сервера отменено оператором.\r\n");
                    return;
                }
                LmServiceProvisioningBatchResult removed =
                    await WithHeartbeatAsync(
                        "Удаление ЛМ сервера",
                        _localModuleMsiProvisioner.RunAsync(
                            CreateMsiMutationRequest(
                                LmServiceOperation.RemoveAllMsiLocalModules,
                                CreateMsiRemovalItems(extra)),
                            cancellation)).ConfigureAwait(true);
                for (int index = 0;
                    index < removed.LocalModuleMsiItems.Count;
                    index++)
                    Log(DescribeServerResult(
                        removed.LocalModuleMsiItems[index], "удаление") +
                        "\r\n");
            }

            // Уменьшение числа только снимает лишние; установка и
            // восстановление — когда число не меньше текущего.
            if (target == 0 || target < current.Count) return;
            if (!RequireLocalModuleInstaller(owner))
            {
                Log("MSI ЛМ ЧЗ не выбран; установка ЛМ сервера отложена.\r\n");
                return;
            }
            inventory = CreateLocalModuleInventoryReader().Read();
            IList<LmGatewayKkt> input =
                LocalModuleServerKey.CreatePlanInput(target);
            LocalModuleMsiPlan plan = BuildServerPlan(input, inventory, null);
            string volume = ResolveInstallVolume(plan, inventory);
            if (!string.IsNullOrEmpty(volume))
                plan = BuildServerPlan(input, inventory, volume);
            if (MessageBox.Show(
                    owner,
                    "Будет установлено ЛМ ЧЗ на сервере: " +
                        target.ToString(CultureInfo.InvariantCulture) +
                        " (уже установленные проверяются и запускаются). " +
                        "Службам ЛМ будет включён автозапуск. " +
                        "Подтвердите право использовать пакет ЛМ и продолжить.",
                    "Сервер ЛМ ЧЗ",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                Log("Установка ЛМ сервера отложена оператором.\r\n");
                return;
            }
            LmServiceProvisioningBatchResult result =
                await WithHeartbeatAsync(
                    "ЛМ сервера",
                    _localModuleMsiProvisioner.RunEnsureAsync(
                        CreateMsiEnsureRequest(plan, inventory),
                        cancellation)).ConfigureAwait(true);
            for (int index = 0;
                index < result.LocalModuleMsiItems.Count;
                index++)
                Log(DescribeServerResult(
                    result.LocalModuleMsiItems[index], "установка") + "\r\n");
        }

        private static LocalModuleMsiPlan BuildServerPlan(
            IList<LmGatewayKkt> input,
            LocalModuleMsiOperatorInventorySnapshot inventory,
            string volume)
        {
            LocalModuleMsiPlan plan = LocalModuleMsiPlanner.Build(
                input,
                inventory.Assignments,
                inventory.BaseInventory,
                volume,
                inventory.Listeners);
            if (!plan.IsValid)
                throw new InvalidOperationException(
                    "Не удалось построить план ЛМ сервера: " +
                    string.Join("; ", plan.ValidationMessages));
            return plan;
        }

        private static string DescribeServerResult(
            LocalModuleMsiProvisioningItemResult item,
            string action)
        {
            int number;
            LocalModuleServerKey.TryGetNumber(item.Inn, out number);
            return "ЛМ сервера №" +
                number.ToString(CultureInfo.InvariantCulture) + ", API " +
                item.ApiPort.ToString(CultureInfo.InvariantCulture) + ": " +
                action + " " + item.Status.ToString() + "; " +
                SensitiveDataMasker.Mask(item.Message ?? string.Empty);
        }

        private static List<LocalModuleMsiInventoryItem> SelectServerLocalModules(
            LocalModuleMsiOperatorInventorySnapshot inventory)
        {
            List<LocalModuleMsiInventoryItem> result =
                new List<LocalModuleMsiInventoryItem>();
            for (int index = 0;
                inventory != null && index < inventory.Items.Count;
                index++)
                if (LocalModuleServerKey.IsServerKey(inventory.Items[index].Inn))
                    result.Add(inventory.Items[index]);
            result.Sort(delegate(
                LocalModuleMsiInventoryItem left,
                LocalModuleMsiInventoryItem right)
            {
                return string.CompareOrdinal(left.Inn, right.Inn);
            });
            return result;
        }

        private IList<LocalModuleMsiInventoryItem> ReadServerLocalModules()
        {
            try
            {
                return SelectServerLocalModules(
                    CreateLocalModuleInventoryReader().Read());
            }
            catch (Exception ex)
            {
                if (!(ex is System.IO.IOException) &&
                    !(ex is UnauthorizedAccessException) &&
                    !(ex is System.IO.InvalidDataException) &&
                    !(ex is ArgumentException) &&
                    !(ex is System.Security.SecurityException))
                    throw;
                Log("Учёт ЛМ сервера не прочитан: " +
                    SensitiveDataMasker.Mask(ex.Message) + "\r\n");
                return new List<LocalModuleMsiInventoryItem>();
            }
        }

        private static void FillServerTable(
            ListView table,
            IList<LocalModuleMsiInventoryItem> items)
        {
            string host = FirstLocalIPv4();
            ReadOnlyWindowsServiceReader services =
                new ReadOnlyWindowsServiceReader();
            table.BeginUpdate();
            table.Items.Clear();
            for (int index = 0; index < items.Count; index++)
            {
                LocalModuleMsiInventoryItem item = items[index];
                int number;
                LocalModuleServerKey.TryGetNumber(item.Inn, out number);
                string apiService =
                    LocalModuleMsiIdentity.ApiServiceName(item.CloneOrdinal);
                string databaseService =
                    LocalModuleMsiIdentity.DatabaseServiceName(item.CloneOrdinal);
                ListViewItem row = new ListViewItem(
                    number.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(apiService + ", " + databaseService);
                row.SubItems.Add(item.ApiPort.ToString(
                    CultureInfo.InvariantCulture));
                row.SubItems.Add(host + ":" + item.ApiPort.ToString(
                    CultureInfo.InvariantCulture));
                row.SubItems.Add(DescribeServerServices(
                    services, apiService, databaseService));
                table.Items.Add(row);
            }
            table.EndUpdate();
        }

        private static string DescribeServerServices(
            ReadOnlyWindowsServiceReader services,
            string apiService,
            string databaseService)
        {
            try
            {
                ReadOnlyWindowsService api = services.Query(apiService);
                ReadOnlyWindowsService database = services.Query(databaseService);
                if (api == null || database == null) return "службы не найдены";
                return api.State == ServiceRunningState &&
                    database.State == ServiceRunningState
                    ? "службы работают"
                    : "службы остановлены; запустит кнопка установки";
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return "состояние не прочитано: " + ex.Message;
            }
        }

        private static string FormatServerAddresses(
            IList<LocalModuleMsiInventoryItem> items)
        {
            string host = FirstLocalIPv4();
            StringBuilder text = new StringBuilder();
            for (int index = 0; index < items.Count; index++)
            {
                int number;
                LocalModuleServerKey.TryGetNumber(items[index].Inn, out number);
                text.Append("ЛМ №")
                    .Append(number.ToString(CultureInfo.InvariantCulture))
                    .Append(": ")
                    .Append(host)
                    .Append(':')
                    .Append(items[index].ApiPort.ToString(
                        CultureInfo.InvariantCulture))
                    .Append("\r\n");
            }
            return text.ToString();
        }

        // ponytail: первый IPv4 этого компьютера; при нескольких сетевых
        // картах оператор сам выбирает адрес, видимый кассам.
        private static string FirstLocalIPv4()
        {
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(Dns.GetHostName());
                for (int index = 0; index < addresses.Length; index++)
                {
                    IPAddress address = addresses[index];
                    if (address.AddressFamily != AddressFamily.InterNetwork ||
                        IPAddress.IsLoopback(address)) continue;
                    byte[] bytes = address.GetAddressBytes();
                    if (bytes[0] == 169 && bytes[1] == 254) continue;
                    return address.ToString();
                }
            }
            catch (SocketException)
            {
            }
            return "<IP сервера>";
        }
    }
}
