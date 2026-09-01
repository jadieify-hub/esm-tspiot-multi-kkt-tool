namespace EsmTspiot.Shared.Models
{
    public enum DirectControllerRole
    {
        OfficialBase = 1,
        DirectClone = 2
    }

    public sealed class DirectControllerAssignment
    {
        public string KktSerial { get; set; }
        public string KktInn { get; set; }
        public int Ordinal { get; set; }
        public DirectControllerRole Role { get; set; }
        public string ServiceName { get; set; }
        public int GrpcPort { get; set; }
        public int RestPort { get; set; }
        public int FutureLocalModulePort { get; set; }
    }
}
