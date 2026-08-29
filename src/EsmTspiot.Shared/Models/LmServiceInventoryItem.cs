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
        public bool IsReady { get; set; }
        public LmServiceProvisioningStatus Status { get; set; }
        public LmManifestFingerprint ManifestFingerprint { get; set; }
        public LmManifestFingerprint ManagedStateFingerprint { get; set; }
        public string Message { get; set; }
    }
}
