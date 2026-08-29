namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayInfo
    {
        public string KktSerial { get; set; }
        public string KktInn { get; set; }
        public bool HasLmConfiguration { get; set; }
        public string LmAddress { get; set; }
        public string LmPort { get; set; }
        public string LmStatus { get; set; }
        public string LmVersion { get; set; }
    }
}
