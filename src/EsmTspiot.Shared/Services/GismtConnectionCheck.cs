using System;
using System.Runtime.Serialization;
using System.Xml;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    internal static class GismtConnectionCheck
    {
        // Контрольная марка проданного товара для разовой проверки связи.
        private const string SampleCode = "MDEwNDYwNzAxNzA5Mzg5NDIxNUlUbDtLKmFVNChwUR05My9sZUo=";

        internal static string CreateRequest(string clientChecksum)
        {
            return JsonHelper.Serialize(new Request {
                Codes = new[] { SampleCode },
                Client = new ClientInfo { Token = clientChecksum }
            });
        }

        internal static string Describe(ApiResponse response)
        {
            if (response.IsTlsCertificateFailure)
                return "Не удалось проверить HTTPS-сертификат ЕСМ. Связь с ЧЗ не подтверждена.";
            if (response.IsConnectionFailure)
                return "Кассовый API ЕСМ не ответил. Связь с ЧЗ не подтверждена.";
            if (response.StatusCode == 401 || response.StatusCode == 403)
                return "ЕСМ отклонил диагностический запрос (HTTP " + response.StatusCode +
                    "). Проверьте допуск клиента и состояние ЕСМ. Связь с ЧЗ не подтверждена.";
            if (response.StatusCode == 514 || response.StatusCode == 203)
                return DescribeError(response.StatusCode);
            if (!response.IsSuccess)
                return "Кассовый API ЕСМ вернул HTTP " + response.StatusCode +
                    ". Связь с ЧЗ не подтверждена.";

            Envelope envelope = Read<Envelope>(response.ResponseBody);
            if (envelope == null || envelope.Results == null || envelope.Results.Length == 0)
            {
                WrappedResponse wrapped = Read<WrappedResponse>(response.ResponseBody);
                if (wrapped != null && wrapped.Result != null) envelope = wrapped.Result;
            }
            if (envelope != null && envelope.ErrorCode.HasValue && envelope.ErrorCode.Value != 0)
                return DescribeError(envelope.ErrorCode.Value);
            if (envelope != null && envelope.Code.HasValue && envelope.Code.Value != 0)
                return DescribeError(envelope.Code.Value);
            if (envelope == null || envelope.Results == null || envelope.Results.Length != 1 ||
                envelope.Results[0] == null)
                return "Источник ответа не подтверждён: ЕСМ не вернул результат проверки кода маркировки.";

            Result result = envelope.Results[0];
            if (result.Code.HasValue && result.Code.Value != 0) return DescribeError(result.Code.Value);
            if (result.Code != 0 || !result.Offline.HasValue || result.Codes == null ||
                result.Codes.Length != 1 || result.Codes[0] == null || !result.Codes[0].Found.HasValue)
                return "Источник ответа не подтверждён: в результате ЕСМ нет данных о проверке.";
            if (result.Offline.Value)
                return "Ответил ЛМ ЧЗ (офлайн). Связь с ГИС МТ не подтверждена.";

            string text = "ЕСМ вернул онлайн-ответ ГИС МТ.";
            Mark mark = result.Codes[0];
            if (!mark.Found.Value)
                text += " ЧЗ не нашёл контрольный код.";
            else if (mark.Sold == true)
                text += " ЧЗ сообщил о выбытии товара или признаках множественных продаж (sold: true).";
            else if (mark.Sold == false)
                text += " ЧЗ не вернул признак выбытия или множественных продаж (sold: false).";
            Guid requestId;
            if (Guid.TryParse(result.RequestId, out requestId)) text += " ID запроса: " + requestId;
            return text;
        }

        private static string DescribeError(int code)
        {
            if (code == 514) return "ЕСМ не получил ответ от ГИС МТ и ЛМ ЧЗ (514).";
            if (code == 203) return "ЕСМ сообщил об аварийном режиме (203). Онлайн-проверка не подтверждена.";
            if (code == -934) return "ГИС МТ отклонила авторизацию ФН: номер отсутствует в списке допуска (-934).";
            return "ЕСМ вернул код " + code + ". Связь с ЧЗ не подтверждена.";
        }

        private static T Read<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonHelper.Deserialize<T>(json); }
            catch (SerializationException) { return null; }
            catch (XmlException) { return null; }
        }

        [DataContract]
        private sealed class Request
        {
            [DataMember(Name = "codes")] public string[] Codes { get; set; }
            [DataMember(Name = "client_info")] public ClientInfo Client { get; set; }
        }

        [DataContract]
        private sealed class ClientInfo
        {
            [DataMember(Name = "name")] public string Name = "MultiKKT Diagnostics";
            [DataMember(Name = "version")] public string Version = "1.0";
            [DataMember(Name = "id")] public string Id = "59c60f02-e9b8-41a2-b53a-09fa1477c6d0";
            [DataMember(Name = "token")] public string Token { get; set; }
        }

        [DataContract]
        private sealed class WrappedResponse
        {
            [DataMember(Name = "codesResponse")] public Envelope Result { get; set; }
        }

        [DataContract]
        private sealed class Envelope
        {
            [DataMember(Name = "codesResponse")] public Result[] Results { get; set; }
            [DataMember(Name = "code")] public int? Code { get; set; }
            [DataMember(Name = "errorCode")] public int? ErrorCode { get; set; }
        }

        [DataContract]
        private sealed class Result
        {
            [DataMember(Name = "code")] public int? Code { get; set; }
            [DataMember(Name = "isCheckedOffline")] public bool? Offline { get; set; }
            [DataMember(Name = "reqId")] public string RequestId { get; set; }
            [DataMember(Name = "codes")] public Mark[] Codes { get; set; }
        }

        [DataContract]
        private sealed class Mark
        {
            [DataMember(Name = "found")] public bool? Found { get; set; }
            [DataMember(Name = "sold")] public bool? Sold { get; set; }
        }
    }
}
