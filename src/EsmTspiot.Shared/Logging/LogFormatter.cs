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
                builder.AppendLine(response.RequestBody);
            }

            if (response.StatusCode > 0)
            {
                builder.AppendLine("HTTP-статус: " + response.StatusCode.ToString() + " " + (response.ReasonPhrase ?? string.Empty));
            }
            else
            {
                builder.AppendLine("HTTP-статус: нет ответа");
            }

            if (!string.IsNullOrEmpty(response.ResponseBody))
            {
                builder.AppendLine("Ответ сервера:");
                builder.AppendLine(response.ResponseBody);
            }

            if (!string.IsNullOrEmpty(response.DecodedMessage))
            {
                builder.AppendLine("Пояснение:");
                builder.AppendLine(response.DecodedMessage);
            }

            builder.AppendLine();
            return builder.ToString();
        }
    }
}
