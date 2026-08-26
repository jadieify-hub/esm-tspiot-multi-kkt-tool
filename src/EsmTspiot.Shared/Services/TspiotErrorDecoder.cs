using System.Text.RegularExpressions;

namespace EsmTspiot.Shared.Services
{
    public static class TspiotErrorDecoder
    {
        public static string Decode(int statusCode, string responseBody)
        {
            if (statusCode == 403)
            {
                return "Сервис вернул Forbidden. Проверьте регистрацию, сертификаты, права доступа и состояние ЕСМ/ТС ПИоТ.";
            }

            string body = responseBody ?? string.Empty;

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
                return "Ошибка 1013: служба подключаемой ККТ создана, но не запущена. Запустите аварийный фикс службы от имени администратора, чтобы проверить параметры, пересоздать службу при необходимости и запустить её.";
            }

            if (ContainsErrorCode(body, 1001))
            {
                return "Некорректное тело запроса. Проверьте заполненные поля.";
            }

            if (ContainsErrorCode(body, 1015))
            {
                return "Не запущен агент-сервис ДККТ. Проверьте службу ATOL: Fptr grpc service и dkktPort. По умолчанию dkktPort = 4041.";
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
