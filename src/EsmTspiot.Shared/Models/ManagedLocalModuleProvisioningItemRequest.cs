using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class ManagedLocalModuleProvisioningItemRequest
    {
        [DataMember(Order = 1)]
        public string KktSerial { get; set; }

        [DataMember(Order = 2)]
        public string Inn { get; set; }

        [DataMember(Order = 3)]
        public int KktOrdinal { get; set; }

        [DataMember(Order = 4)]
        public int LocalModuleOrdinal { get; set; }

        [DataMember(Order = 5)]
        public int ApiPort { get; set; }

        [DataMember(Order = 6)]
        public int DatabasePort { get; set; }

        [DataMember(Order = 7)]
        public int EpmdPort { get; set; }

        [DataMember(Order = 8)]
        public int ControllerGrpcPort { get; set; }

        [DataMember(Order = 9)]
        public int ControllerRestPort { get; set; }

        [DataMember(Order = 10)]
        public string RuntimeVersion { get; set; }
    }
}
