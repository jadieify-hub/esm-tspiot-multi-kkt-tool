using System;
using System.Net;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Validation
{
    public static class LmGatewayInputValidator
    {
        public static ValidationResult ValidateBinding(LmGatewayBindingInput input)
        {
            ValidationResult result = new ValidationResult();
            if (input == null)
            {
                result.Add("Не заданы параметры привязки контроллера ЛМ.");
                return result;
            }

            string kktSerial = Trim(input.KktSerial);
            if (kktSerial.Length != 14 || !IsAsciiDigits(kktSerial))
            {
                result.Add("Серийный номер ККТ для привязки ЛМ должен содержать ровно 14 ASCII-цифр.");
            }

            string kktInn = Trim(input.KktInn);
            if ((kktInn.Length != 10 && kktInn.Length != 12) || !IsAsciiDigits(kktInn))
            {
                result.Add("ИНН ККТ для привязки ЛМ должен содержать 10 или 12 ASCII-цифр.");
            }

            string normalizedAddress;
            if (!TryNormalizeControllerAddress(input.ControllerAddress, out normalizedAddress))
            {
                result.Add("Адрес локального контроллера ЛМ должен быть loopback: 127.0.0.1, localhost или ::1.");
            }

            int port;
            if (!int.TryParse(Trim(input.ControllerGrpcPort), out port) || port < 1 || port > 65535)
            {
                result.Add("gRPC-порт локального контроллера ЛМ должен быть целым числом в диапазоне 1-65535.");
            }

            return result;
        }

        internal static bool TryNormalizeControllerAddress(string address, out string normalized)
        {
            string value = Trim(address);
            if (string.Equals(value, "127.0.0.1", StringComparison.Ordinal) ||
                string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "127.0.0.1";
                return true;
            }

            string ipText = value;
            if (ipText.Length > 2 && ipText[0] == '[' && ipText[ipText.Length - 1] == ']')
            {
                ipText = ipText.Substring(1, ipText.Length - 2);
            }

            IPAddress addressValue;
            if (IPAddress.TryParse(ipText, out addressValue) &&
                addressValue.Equals(IPAddress.IPv6Loopback))
            {
                normalized = "127.0.0.1";
                return true;
            }

            normalized = value;
            return false;
        }

        private static bool IsAsciiDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
