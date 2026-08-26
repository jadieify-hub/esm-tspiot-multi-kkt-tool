namespace EsmTspiot.Shared.Models
{
    public sealed class TspiotFormInput
    {
        public string BaseUrl { get; set; }
        public string KktSerial { get; set; }
        public string FnSerial { get; set; }
        public string KktInn { get; set; }
        public string Port { get; set; }
        public string SoftPort { get; set; }
        public string DkktPort { get; set; }
    }
}
