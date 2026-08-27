namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayDraft
    {
        public string KktSerial { get; set; }
        public string TargetAddress { get; set; }
        public string TargetPort { get; set; }
        public string GrpcPort { get; set; }
        public string RestPort { get; set; }
    }
}
