using System.Collections.Generic;
using System.Text;

namespace EsmTspiot.Shared.Models
{
    public sealed class BulkKktRegistrationResult
    {
        public string KktSerial { get; set; }
        public BulkKktRegistrationStatus Status { get; set; }
        public string Details { get; set; }

        public static BulkKktRegistrationResult CheckIdentity(string serial, string fn, string inn,
            string registeredSerial, string registeredFn, string registeredInn)
        {
            var fields = new List<string>();
            if ((serial ?? string.Empty).Trim() != registeredSerial) fields.Add("серийный номер ККТ (kktSerial)");
            if ((fn ?? string.Empty).Trim() != registeredFn) fields.Add("номер ФН (fnSerial)");
            if ((inn ?? string.Empty).Trim() != registeredInn) fields.Add("ИНН (kktInn)");
            return fields.Count == 0 ? null : new BulkKktRegistrationResult
            {
                KktSerial = (serial ?? string.Empty).Trim(), Status = BulkKktRegistrationStatus.InspectionFailed,
                Details = "Данные физической ККТ и regData ЕСМ не совпадают: " + string.Join(", ", fields.ToArray())
            };
        }

        public string FormatLogLine()
        {
            string line = "ККТ " + (KktSerial ?? string.Empty) + ": " + GetStatusText(Status);
            if (!string.IsNullOrWhiteSpace(Details))
            {
                line += "; " + Details.Trim();
            }

            return line;
        }

        public static string FormatSummary(IList<BulkKktRegistrationResult> results)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Зарегистрировано: " + Count(results, BulkKktRegistrationStatus.Registered).ToString());
            builder.AppendLine("Завершено ранее созданных: " + Count(results, BulkKktRegistrationStatus.RecoveredRegistration).ToString());
            builder.AppendLine("Уже существует: " + Count(results, BulkKktRegistrationStatus.AlreadyExists).ToString());
            builder.AppendLine("Не удалось проверить: " + Count(results, BulkKktRegistrationStatus.InspectionFailed).ToString());
            builder.AppendLine("Некорректные данные: " + Count(results, BulkKktRegistrationStatus.InvalidData).ToString());
            builder.AppendLine("Ошибок добавления: " + Count(results, BulkKktRegistrationStatus.AddFailed).ToString());
            builder.AppendLine("Регистрация не завершена (экземпляр существует): " + Count(results, BulkKktRegistrationStatus.RegistrationFailed).ToString());
            builder.AppendLine("Результат записи неизвестен: " + Count(results, BulkKktRegistrationStatus.UnknownWriteOutcome).ToString());
            builder.Append("Отменено: " + Count(results, BulkKktRegistrationStatus.Cancelled).ToString());
            return builder.ToString();
        }

        private static int Count(IList<BulkKktRegistrationResult> results, BulkKktRegistrationStatus status)
        {
            int count = 0;
            if (results == null)
            {
                return count;
            }

            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] != null && results[i].Status == status)
                {
                    count++;
                }
            }

            return count;
        }

        private static string GetStatusText(BulkKktRegistrationStatus status)
        {
            switch (status)
            {
                case BulkKktRegistrationStatus.Registered:
                    return "Зарегистрирована";
                case BulkKktRegistrationStatus.RecoveredRegistration:
                    return "Регистрация ранее созданного экземпляра завершена";
                case BulkKktRegistrationStatus.AlreadyExists:
                    return "Уже зарегистрирована";
                case BulkKktRegistrationStatus.InspectionFailed:
                    return "Не удалось проверить состояние регистрации";
                case BulkKktRegistrationStatus.InvalidData:
                    return "Пропущена: некорректные данные";
                case BulkKktRegistrationStatus.AddFailed:
                    return "Ошибка добавления";
                case BulkKktRegistrationStatus.RegistrationFailed:
                    return "Экземпляр создан, регистрация не завершена";
                case BulkKktRegistrationStatus.UnknownWriteOutcome:
                    return "Результат записи неизвестен";
                case BulkKktRegistrationStatus.Cancelled:
                    return "Операция отменена";
                default:
                    return status.ToString();
            }
        }
    }
}
