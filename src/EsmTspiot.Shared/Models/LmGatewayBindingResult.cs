namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayBindingResult
    {
        public string InstanceId { get; set; }
        public string KktSerial { get; set; }
        public string KktInn { get; set; }
        public LmGatewayBindingStatus Status { get; set; }
        public string Details { get; set; }

        /// <summary>
        /// Технические подробности для журнала: ответ ЕСМ, по которому
        /// принято решение. В Details не входит, чтобы не менять разбор
        /// текста результата.
        /// </summary>
        public string Diagnostics { get; set; }
    }
}
