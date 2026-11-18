using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class LocalModuleSetupMessagePolicy
    {
        public static string BuildInstallConfirmation(LocalModuleMsiPlan plan, IList<LmGatewayKkt> kkts)
        {
            var details = new StringBuilder();
            var selectedSerials = new HashSet<string>(StringComparer.Ordinal);
            foreach (LocalModuleMsiAssignment assignment in plan.Assignments)
            {
                var serials = new List<string>();
                foreach (LmGatewayKkt kkt in kkts)
                    if (kkt != null && kkt.KktInn == assignment.Inn && selectedSerials.Add(kkt.KktSerial))
                        serials.Add(kkt.KktSerial);
                details.Append("ИНН ").Append(assignment.Inn).Append(": ККТ ")
                    .Append(string.Join(", ", serials.ToArray())).Append("; API-порт ")
                    .Append(assignment.ApiPort.ToString(CultureInfo.InvariantCulture)).Append(".\r\n");
            }
            return "Установка или проверка локальных ЛМ ЧЗ.\r\nККТ: " +
                selectedSerials.Count.ToString(CultureInfo.InvariantCulture) + "; ИНН: " +
                plan.Assignments.Count.ToString(CultureInfo.InvariantCulture) + ".\r\n\r\n" + details +
                "\r\nЛМ общий для касс одного ИНН. Уже установленные ЛМ остаются в своих каталогах. " +
                "Службам перечисленных ЛМ будет включён автозапуск. " +
                "Прежний автозапуск предустановленного базового ЛМ восстанавливается при удалении созданного комплекта.\r\n\r\n" +
                "Подтвердите право использовать пакет ЛМ и продолжить.";
        }
    }
}
