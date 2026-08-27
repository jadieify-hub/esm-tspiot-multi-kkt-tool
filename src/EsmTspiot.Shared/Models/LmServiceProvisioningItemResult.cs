using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LmServiceProvisioningItemResult
    {
        [DataMember(Order = 1)]
        public string KktSerial { get; set; }

        [DataMember(Order = 2)]
        public LmServiceProvisioningStatus Status { get; set; }

        [DataMember(Order = 3)]
        public string Message { get; set; }
    }
}
