namespace EsmTspiot.Shared.Models
{
    public sealed class ManagedKktAssignment
    {
        public string KktSerial { get; set; }
        public string KktInn { get; set; }
        public int KktOrdinal { get; set; }
        public string LocalModuleInstanceId { get; set; }
        public int GrpcPort { get; set; }
        public int RestPort { get; set; }
    }
}
