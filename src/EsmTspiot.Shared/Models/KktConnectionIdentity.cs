namespace EsmTspiot.Shared.Models
{
    public sealed class KktConnectionIdentity
    {
        public string PortName { get; set; }
        public string KktSerial { get; set; }
        public string ModelName { get; set; }
        public string FirmwareVersion { get; set; }
    }
}
