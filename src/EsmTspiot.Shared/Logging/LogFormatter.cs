using System;
using System.Text;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Logging
{
    public static class LogFormatter
    {
        public static string Format(ApiResponse response)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]");
            builder.AppendLine(response.Method + " " + response.Url);

            if (!string.IsNullOrEmpty(response.RequestBody))
            {
                builder.AppendLine("Тело запроса:");
                builder.AppendLine(SensitiveDataMasker.Mask(response.RequestBody));
            }

            if (response.StatusCode > 0)
            {
                builder.AppendLine("HTTP-статус: " + response.StatusCode.ToString() + " " + SensitiveDataMasker.Mask(response.ReasonPhrase));
            }
            else
            {
                builder.AppendLine("HTTP-статус: нет ответа");
            }

            if (!string.IsNullOrEmpty(response.ResponseBody))
            {
                builder.AppendLine("Ответ сервера:");
                builder.AppendLine(SensitiveDataMasker.Mask(response.ResponseBody));
            }

            if (!string.IsNullOrEmpty(response.DecodedMessage))
            {
                builder.AppendLine("Пояснение:");
                builder.AppendLine(SensitiveDataMasker.Mask(response.DecodedMessage));
            }

            builder.AppendLine();
            return builder.ToString();
        }
    }
}
