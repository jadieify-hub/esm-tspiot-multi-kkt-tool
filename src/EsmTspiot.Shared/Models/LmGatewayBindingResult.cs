namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayBindingResult
    {
        public string InstanceId { get; set; }
        public string KktSerial { get; set; }
        public string KktInn { get; set; }
        public LmGatewayBindingStatus Status { get; set; }
        public string Details { get; set; }
    }
}
