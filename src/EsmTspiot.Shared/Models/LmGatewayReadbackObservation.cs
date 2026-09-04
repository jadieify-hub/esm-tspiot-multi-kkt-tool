namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayReadbackObservation
    {
        public string InstanceId { get; set; }
        public string KktSerial { get; set; }
        public string KktInn { get; set; }
        public bool IsAvailable { get; set; }
        public bool IdentityMatches { get; set; }
        public bool HasLmConfiguration { get; set; }
        public bool? EndpointMatches { get; set; }
        public string LmAddress { get; set; }
        public string LmPort { get; set; }
        public string LmStatus { get; set; }
        public string LmVersion { get; set; }
        public string Details { get; set; }

        /// <summary>
        /// Замаскированная выдержка из ответа /api/v2/info. Нужна, когда
        /// ЕСМ принял привязку, но не сообщает её обратно: без ответа
        /// причина не видна ни в журнале, ни в поле.
        /// </summary>
        public string InfoResponseBody { get; set; }

        /// <summary>
        /// Привязка подтверждена, когда ЕСМ отвечает по нашей ККТ и сообщает
        /// настроенный ЛМ. Endpoint из lm.ip/lm.port в критерий не входит:
        /// полевой прогон 2026-09-04 показал, что ЕСМ отдаёт там модуль по
        /// умолчанию — 127.0.0.1:5995 у обеих касс, включая ту, которую
        /// обслуживает клон на 6995 и которую сам ЕСМ показывает как «Готов
        /// к работе». Фактическую связку ЕСМ ведёт через контроллер, а не
        /// через это поле, поэтому расхождение — строка оператору.
        /// </summary>
        public bool IsVerified
        {
            get
            {
                return IsAvailable && IdentityMatches && HasLmConfiguration;
            }
        }
    }
}
