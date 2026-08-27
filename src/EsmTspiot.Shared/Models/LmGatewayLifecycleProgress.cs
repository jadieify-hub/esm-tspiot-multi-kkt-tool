namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayLifecycleProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string KktSerial { get; set; }
        public LmGatewayLifecycleStatus Status { get; set; }
        public string Stage { get; set; }
        public string Message { get; set; }
    }
}
