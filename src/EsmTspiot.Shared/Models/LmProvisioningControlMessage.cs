using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LmProvisioningControlMessage
    {
        [DataMember(Order = 1)]
        public int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        public string OperationId { get; set; }

        [DataMember(Order = 3)]
        public long Sequence { get; set; }

        [DataMember(Order = 4)]
        public LmProvisioningControlKind Kind { get; set; }
    }
}
