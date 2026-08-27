using System;
using System.Net;
using System.Net.Sockets;
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

        public static ValidationResult ValidateTarget(LmGatewayTarget target)
        {
            ValidationResult result = new ValidationResult();
            if (target == null)
            {
                result.Add("Не задан endpoint целевого ЛМ.");
                return result;
            }

            string normalized;
            bool isLoopback;
            if (!TryNormalizeTargetAddress(target.Address, out normalized, out isLoopback))
            {
                result.Add("Адрес целевого ЛМ должен быть обычным IPv4, IPv6 или ASCII DNS-именем без схемы, пути и учетных данных.");
            }

            if (target.Port < 1 || target.Port > 65535)
            {
                result.Add("Порт целевого ЛМ должен быть в диапазоне 1-65535.");
            }

            return result;
        }

        public static bool TryNormalizeTargetAddress(
            string address,
            out string normalized,
            out bool isLoopback)
        {
            normalized = address ?? string.Empty;
            isLoopback = false;
            if (string.IsNullOrEmpty(address) || address.Length > 253)
            {
                return false;
            }

            for (int index = 0; index < address.Length; index++)
            {
                char value = address[index];
                if (char.IsWhiteSpace(value) || char.IsControl(value))
                {
                    return false;
                }
            }

            IPAddress parsedAddress;
            if (IPAddress.TryParse(address, out parsedAddress))
            {
                if (parsedAddress.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (!IsStrictIpv4(address))
                    {
                        return false;
                    }
                }
                else if (parsedAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    if (address.IndexOf('%') >= 0 || address.IndexOf('[') >= 0 || address.IndexOf(']') >= 0)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                isLoopback = IPAddress.IsLoopback(parsedAddress) ||
                    (parsedAddress.AddressFamily == AddressFamily.InterNetworkV6 &&
                     parsedAddress.IsIPv4MappedToIPv6 &&
                     IPAddress.IsLoopback(parsedAddress.MapToIPv4()));
                normalized = isLoopback ? "127.0.0.1" : parsedAddress.ToString().ToLowerInvariant();
                return true;
            }

            if (!IsAsciiDnsName(address))
            {
                return false;
            }

            if (string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "127.0.0.1";
                isLoopback = true;
                return true;
            }

            normalized = address.ToLowerInvariant();
            return true;
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

        private static bool IsStrictIpv4(string value)
        {
            string[] parts = value.Split('.');
            if (parts.Length != 4)
            {
                return false;
            }

            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                string part = parts[partIndex];
                if (part.Length == 0 || part.Length > 3 || !IsAsciiDigits(part))
                {
                    return false;
                }

                int parsed;
                if (!int.TryParse(part, out parsed) || parsed > 255)
                {
                    return false;
                }

                if (part.Length > 1 && part[0] == '0')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAsciiDnsName(string value)
        {
            string[] labels = value.Split('.');
            if (labels.Length == 0)
            {
                return false;
            }

            for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
            {
                string label = labels[labelIndex];
                if (label.Length == 0 || label.Length > 63 || label[0] == '-' || label[label.Length - 1] == '-')
                {
                    return false;
                }

                for (int charIndex = 0; charIndex < label.Length; charIndex++)
                {
                    char character = label[charIndex];
                    bool isAsciiLetter =
                        (character >= 'a' && character <= 'z') ||
                        (character >= 'A' && character <= 'Z');
                    bool isAsciiDigit = character >= '0' && character <= '9';
                    if (!isAsciiLetter && !isAsciiDigit && character != '-')
                    {
                        return false;
                    }
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
