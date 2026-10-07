using System;
using System.Collections.Generic;
using System.Globalization;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    /// <summary>
    /// Экземпляр ЛМ на выделенном сервере ставится раньше, чем станет
    /// известен ИНН кассы, поэтому учёт MSI ведёт его под синтетическим
    /// ключом «00000000NN». Код региона 00 налоговая не выдаёт, и ключ не
    /// совпадёт с настоящим ИНН. Правило сети такого экземпляра открыто во
    /// всех профилях: к серверу обращаются кассы из любой сети.
    /// </summary>
    public static class LocalModuleServerKey
    {
        public const int MaximumCount =
            LocalModuleMsiIdentity.MaximumCloneOrdinal + 1;

        private const string Prefix = "00000000";
        private const string SerialPrefix = "000000000000";

        public static string Format(int number)
        {
            if (number < 1 || number > MaximumCount)
                throw new ArgumentOutOfRangeException("number");
            return Prefix + number.ToString("00", CultureInfo.InvariantCulture);
        }

        public static bool IsServerKey(string inn)
        {
            int number;
            return TryGetNumber(inn, out number);
        }

        public static bool TryGetNumber(string inn, out int number)
        {
            number = 0;
            return inn != null && inn.Length == 10 &&
                inn.StartsWith(Prefix, StringComparison.Ordinal) &&
                LocalModuleMsiIdentity.IsInn(inn) &&
                int.TryParse(
                    inn.Substring(Prefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out number) &&
                number >= 1 && number <= MaximumCount;
        }

        /// <summary>
        /// Синтетические «ККТ» для планировщика MSI: по одной на номер
        /// экземпляра; серийный номер сохраняет порядок номеров.
        /// </summary>
        public static IList<LmGatewayKkt> CreatePlanInput(int count)
        {
            if (count < 0 || count > MaximumCount)
                throw new ArgumentOutOfRangeException("count");
            List<LmGatewayKkt> result = new List<LmGatewayKkt>();
            for (int number = 1; number <= count; number++)
                result.Add(new LmGatewayKkt
                {
                    KktInn = Format(number),
                    KktSerial = SerialPrefix + number.ToString(
                        "00", CultureInfo.InvariantCulture),
                    RegistrationConfirmed = true
                });
            return result;
        }
    }
}
