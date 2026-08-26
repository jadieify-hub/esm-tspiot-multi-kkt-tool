using System;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.Shared.Validation
{
    public static class TspiotInputValidator
    {
        public static ValidationResult ValidatePost(TspiotFormInput input)
        {
            ValidationResult result = ValidateCommon(input, false);
            ValidateFnSerial(input, result, false);
            ValidateInn(input, result, false);
            ValidatePorts(input, result);
            return result;
        }

        public static ValidationResult ValidateForCheck(TspiotFormInput input)
        {
            ValidationResult result = new ValidationResult();

            if (input == null)
            {
                result.Add("Нет данных формы.");
                return result;
            }

            ValidateBaseUrl(input, result);
            ValidateKktSerial(input, result, false);
            ValidateFnSerial(input, result, false);
            ValidateInn(input, result, false);
            ValidateOptionalPorts(input, result);
            return result;
        }

        public static ValidationResult ValidateBulkSettings(string baseUrl, string dkktPort)
        {
            TspiotFormInput input = new TspiotFormInput
            {
                BaseUrl = baseUrl,
                DkktPort = dkktPort
            };
            ValidationResult result = new ValidationResult();
            ValidateBaseUrl(input, result);

            int port;
            TryValidatePort(dkktPort, "dkktPort", result, out port);
            return result;
        }

        public static ValidationResult ValidatePut(TspiotFormInput input, bool atolConnectionConfirmed)
        {
            ValidationResult result = ValidateCommon(input, true);
            ValidatePorts(input, result);

            if (!atolConnectionConfirmed)
            {
                result.Add("Перед регистрацией нужно подтвердить, что связь в драйвере АТОЛ проверена именно со второй физической ККТ.");
            }

            return result;
        }

        public static AddTspiotRequest CreateAddRequest(TspiotFormInput input)
        {
            return new AddTspiotRequest
            {
                Id = Trim(input.KktSerial),
                Port = int.Parse(Trim(input.Port)),
                SoftPort = int.Parse(Trim(input.SoftPort)),
                DkktPort = int.Parse(Trim(input.DkktPort))
            };
        }

        public static RegisterTspiotRequest CreateRegisterRequest(TspiotFormInput input)
        {
            string serial = Trim(input.KktSerial);
            return new RegisterTspiotRequest
            {
                Id = serial,
                KktSerial = serial,
                FnSerial = Trim(input.FnSerial),
                KktInn = Trim(input.KktInn)
            };
        }

        private static ValidationResult ValidateCommon(TspiotFormInput input, bool includeRegistrationFields)
        {
            ValidationResult result = new ValidationResult();

            if (input == null)
            {
                result.Add("Нет данных формы.");
                return result;
            }

            ValidateBaseUrl(input, result);
            ValidateKktSerial(input, result, true);

            if (includeRegistrationFields)
            {
                ValidateFnSerial(input, result, true);
                ValidateInn(input, result, true);
            }

            return result;
        }

        private static void ValidateBaseUrl(TspiotFormInput input, ValidationResult result)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(input.BaseUrl) ||
                !Uri.TryCreate(Trim(input.BaseUrl), UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                result.Add("Адрес сервиса ЕСМ/ТС ПИоТ должен быть корректным HTTP/HTTPS URL.");
                return;
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                result.Add("Адрес сервиса ЕСМ/ТС ПИоТ не должен содержать учетные данные.");
            }
            if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                result.Add("Адрес сервиса ЕСМ/ТС ПИоТ не должен содержать query-параметры или фрагмент.");
            }
            if (uri.AbsolutePath != "/" && !string.IsNullOrEmpty(uri.AbsolutePath))
            {
                result.Add("Адрес сервиса ЕСМ/ТС ПИоТ должен указывать на корень сервиса без дополнительного пути.");
            }

            if (!uri.IsLoopback)
            {
                result.AddWarning("Адрес ЕСМ/ТС ПИоТ не является локальным адресом. Регистрационные данные будут отправлены на " + uri.Host + ". Проверьте адрес.");
            }
        }

        private static void ValidateKktSerial(TspiotFormInput input, ValidationResult result, bool required)
        {
            string value = Trim(input.KktSerial);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required)
                {
                    result.Add("Серийный номер второй ККТ обязателен.");
                }
                return;
            }

            if (!IsDigitsOnly(value))
            {
                result.Add("Серийный номер второй ККТ должен состоять только из цифр 0-9 (ASCII-цифр).");
            }
            else if (value.Length != 14)
            {
                result.Add("Серийный номер второй ККТ/ФР должен содержать 14 цифр.");
            }
            else if (!value.StartsWith("001", StringComparison.Ordinal))
            {
                result.AddWarning("Серийный номер ФР/ККТ обычно начинается с 001. Проверьте, что номер введён правильно.");
            }
            else
            {
                string modelCode;
                if (!AtolKktModelCatalog.IsKnownModelCode(value, out modelCode))
                {
                    result.AddWarning("Код модели ККТ " + modelCode + " не найден в списке известных моделей АТОЛ/Эвотор. Проверьте серийный номер.");
                }
            }
        }

        private static void ValidateFnSerial(TspiotFormInput input, ValidationResult result, bool required)
        {
            string value = Trim(input.FnSerial);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required)
                {
                    result.Add("Номер ФН второй ККТ обязателен.");
                }
                return;
            }

            if (!IsDigitsOnly(value))
            {
                result.Add("Номер ФН второй ККТ должен состоять только из цифр 0-9 (ASCII-цифр).");
            }
            else if (value.Length != 16)
            {
                result.Add("Номер ФН второй ККТ должен содержать 16 цифр.");
            }
            else if (!value.StartsWith("73", StringComparison.Ordinal))
            {
                result.AddWarning("Серийный номер ФН обычно начинается с 73. Проверьте, что номер ФН введён правильно.");
            }
        }

        private static void ValidateInn(TspiotFormInput input, ValidationResult result, bool required)
        {
            string inn = Trim(input.KktInn);
            if (string.IsNullOrWhiteSpace(inn))
            {
                if (required)
                {
                    result.Add("ИНН владельца ККТ обязателен.");
                }
                return;
            }

            if (!IsDigitsOnly(inn))
            {
                result.Add("ИНН должен состоять только из цифр 0-9 (ASCII-цифр).");
            }
            else if (inn.Length != 10 && inn.Length != 12)
            {
                result.Add("ИНН должен содержать 10 или 12 цифр.");
            }
            else if (!IsValidInnControlDigits(inn))
            {
                result.Add("Контрольные цифры ИНН не сходятся. Проверьте ИНН владельца ККТ.");
            }
        }

        private static void ValidatePorts(TspiotFormInput input, ValidationResult result)
        {
            int port;
            int softPort;
            int dkktPort;

            bool portOk = TryValidatePort(input == null ? null : input.Port, "port", result, out port);
            bool softPortOk = TryValidatePort(input == null ? null : input.SoftPort, "softPort", result, out softPort);
            TryValidatePort(input == null ? null : input.DkktPort, "dkktPort", result, out dkktPort);

            if (portOk && softPortOk && port == softPort)
            {
                result.Add("port и softPort не должны совпадать.");
            }
        }

        private static void ValidateOptionalPorts(TspiotFormInput input, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(input.Port) &&
                string.IsNullOrWhiteSpace(input.SoftPort) &&
                string.IsNullOrWhiteSpace(input.DkktPort))
            {
                return;
            }

            ValidatePorts(input, result);
        }

        private static bool TryValidatePort(string text, string name, ValidationResult result, out int value)
        {
            value = 0;
            if (!int.TryParse(Trim(text), out value) || value < 1 || value > 65535)
            {
                result.Add(name + " должен быть целым числом в диапазоне 1-65535.");
                return false;
            }

            return true;
        }

        private static bool IsDigitsOnly(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidInnControlDigits(string inn)
        {
            if (inn.Length == 10)
            {
                int[] weights = { 2, 4, 10, 3, 5, 9, 4, 6, 8 };
                return CalculateControlDigit(inn, weights) == DigitAt(inn, 9);
            }

            if (inn.Length == 12)
            {
                int[] weights11 = { 7, 2, 4, 10, 3, 5, 9, 4, 6, 8 };
                int[] weights12 = { 3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8 };
                return CalculateControlDigit(inn, weights11) == DigitAt(inn, 10)
                    && CalculateControlDigit(inn, weights12) == DigitAt(inn, 11);
            }

            return false;
        }

        private static int CalculateControlDigit(string value, int[] weights)
        {
            int sum = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                sum += DigitAt(value, i) * weights[i];
            }

            return (sum % 11) % 10;
        }

        private static int DigitAt(string value, int index)
        {
            return value[index] - '0';
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
