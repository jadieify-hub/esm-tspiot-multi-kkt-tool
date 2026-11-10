using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class LmGatewayPage
    {
        private readonly AntdUI.Button _resetControllersButton = new AntdUI.Button();

        private Task ConfirmAndResetControllersAsync()
        {
            return RunOperationAsync(ResetControllersCoreAsync, "Сброс контроллеров; " + ElevationHint() + "...");
        }

        private async Task ResetControllersCoreAsync(CancellationToken cancellation)
        {
            var tracked = new DirectControllerOperatorInventoryReader().ReadRemovalItems();
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var item in new LmServiceInventoryReader().Read())
                if (item.Role == LmServiceRole.Managed && ControllerResetServiceRequest.IsAdditionalController(item.ServiceName))
                    names.Add(item.ServiceName);
            using (var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                if (servicesKey != null)
                    foreach (string name in servicesKey.GetSubKeyNames())
                        if (ControllerResetServiceRequest.IsAdditionalController(name)) names.Add(name);
            foreach (var item in tracked) names.Remove(DirectControllerIdentity.ServiceNameForOrdinal(item.Ordinal));
            var services = new List<ControllerResetServiceRequest>();
            var reader = new ReadOnlyWindowsServiceReader();
            foreach (string name in names)
            {
                var service = reader.Query(name);
                services.Add(new ControllerResetServiceRequest { ServiceName = name,
                    ExpectedImagePath = service == null ? string.Empty : service.ImagePath });
            }
            if (tracked.Count == 0 && services.Count == 0)
            {
                MessageBox.Show(this, "Дополнительные контроллеры и назначения MultiKKT не найдены.",
                    "Сбросить контроллеры", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var text = new StringBuilder("Будут сняты назначения и удалены дополнительные контроллеры:\r\n\r\n");
            foreach (var item in tracked)
                text.AppendLine(DirectControllerIdentity.ServiceNameForOrdinal(item.Ordinal) +
                    (item.Ordinal == 1 ? " — только восстановление прежних настроек; служба остаётся" : " — удаление"));
            foreach (var service in services) text.AppendLine(service.ServiceName + " — проверка принадлежности и удаление");
            text.Append("\r\nШтатная служба esm-lm-controller, файлы установленного ПО, ЛМ ЧЗ и их данные, " +
                "регистрации ККТ в ЕСМ сохранятся. Старые профили ЛМ не удаляются. " +
                "Службы, принадлежность которых не подтвердится, будут оставлены с объяснением в журнале. Продолжить?");
            if (MessageBox.Show(this, text.ToString(), "Сбросить контроллеры",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var result = await WithHeartbeatAsync("Сброс контроллеров",
                _serviceProvisioner.ResetControllersAsync(tracked, services, cancellation));
            var statuses = new List<LmServiceProvisioningStatus>();
            foreach (var item in result.Items) { statuses.Add(item.Status); Log(item.FormatLogLine() + "\r\n"); }
            await RefreshCoreAsync(CancellationToken.None);
            _statusLabel.Text = LocalModuleRemovalMessagePolicy.BuildRemoveEverythingSummary(statuses, false) +
                " ЛМ ЧЗ и регистрации ЕСМ сохранены.";
        }
    }
}
