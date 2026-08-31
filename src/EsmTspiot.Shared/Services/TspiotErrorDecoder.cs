using System.Text.RegularExpressions;

namespace EsmTspiot.Shared.Services
{
    public static class TspiotErrorDecoder
    {
        public static string Decode(int statusCode, string responseBody)
        {
            string body = responseBody ?? string.Empty;

            if (statusCode == 403 && ContainsErrorCode(body, 1026))
            {
                return "Ошибка 1026: ЕСМ обнаружил несколько ИНН у подключенных ККТ " +
                    "и отказался выполнять общую регистрацию. Оставьте в драйвере связь " +
                    "только с кассами одного ИНН или запустите автоматическую настройку — " +
                    "она поочерёдно подключит и зарегистрирует ККТ через VCOM.";
            }

            if (statusCode == 403)
            {
                return "Сервис вернул Forbidden. Проверьте регистрацию, сертификаты, права доступа и состояние ЕСМ/ТС ПИоТ.";
            }

            if (ContainsErrorCode(body, 1010))
            {
                return "Сервис с таким id уже существует. Возможно, ККТ уже добавлена.";
            }

            if (ContainsErrorCode(body, 1012))
            {
                return "Ошибка 1012: ЕСМ не смог создать службу подключаемой ККТ автоматически. Создайте службу подключаемой ККТ вручную от имени администратора, затем повторите проверку текущих ККТ.";
            }

            if (ContainsErrorCode(body, 1013))
            {
                return "Ошибка 1013: служба подключаемой ККТ создана, но не запущена. Сначала проверьте dkktPort: для оркестра ЕСМ обычно нужен 4042, а 4041 относится к службе АТОЛ для ККМ. Затем запустите аварийный фикс службы от имени администратора.";
            }

            if (ContainsErrorCode(body, 1001))
            {
                return "Некорректное тело запроса. Проверьте заполненные поля.";
            }

            if (ContainsErrorCode(body, 1015))
            {
                return "Не запущен агент-сервис ДККТ. Проверьте службы АТОЛ/оркестра и dkktPort. По умолчанию для ЕСМ dkktPort = 4042; 4041 — порт службы АТОЛ для ККМ.";
            }

            if (ContainsErrorCode(body, 2046))
            {
                return "Служба ЕСМ не зарегистрирована или не запущена. Также проверьте связь драйвера с подключаемой ККТ.";
            }

            if (statusCode >= 200 && statusCode <= 299)
            {
                return "Запрос выполнен успешно.";
            }

            if (statusCode == 0)
            {
                return DecodeConnectionFailure();
            }

            return "Сервис вернул ошибку HTTP " + statusCode.ToString() + ". Подробности смотрите в ответе сервера.";
        }

        public static string DecodeConnectionFailure()
        {
            return "Не удалось подключиться к ЕСМ/ТС ПИоТ. Проверьте, что сервис запущен и адрес указан правильно.";
        }

        public static bool ContainsErrorCode(string body, int code)
        {
            if (string.IsNullOrEmpty(body))
            {
                return false;
            }

            string codeText = code.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string pattern = "(\"code\"|\"errorCode\"|\"error\"|\"error_code\")\\s*:\\s*\"?" + Regex.Escape(codeText) + "\"?(?![0-9])";
            return Regex.IsMatch(body, pattern);
        }
    }
}
