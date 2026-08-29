namespace EsmTspiot.Shared.Models
{
    public sealed class ManagedLocalModuleAssignment
    {
        public string Inn { get; set; }
        public int ModuleOrdinal { get; set; }
        public string InstanceId { get; set; }
        public int ApiPort { get; set; }
        public int DatabasePort { get; set; }
        public int EpmdPort { get; set; }
        public string RuntimeVersion { get; set; }
    }
}
