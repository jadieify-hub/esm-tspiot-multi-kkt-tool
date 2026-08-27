namespace EsmTspiot.Shared.Models
{
    public sealed class LmServiceInventoryItem
    {
        public string KktSerial { get; set; }
        public string ServiceName { get; set; }
        public LmServiceRole Role { get; set; }
        public LmGatewayPorts Ports { get; set; }
        public LmGatewayTarget Target { get; set; }
        public bool IsRunning { get; set; }
    }
}
