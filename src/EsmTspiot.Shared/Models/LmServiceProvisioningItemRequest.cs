using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LmServiceProvisioningItemRequest
    {
        [DataMember(Order = 1)]
        public string KktSerial { get; set; }

        [DataMember(Order = 2)]
        public int GrpcPort { get; set; }

        [DataMember(Order = 3)]
        public int RestPort { get; set; }

        [DataMember(Order = 4)]
        public string TargetAddress { get; set; }

        [DataMember(Order = 5)]
        public int TargetPort { get; set; }
    }
}
