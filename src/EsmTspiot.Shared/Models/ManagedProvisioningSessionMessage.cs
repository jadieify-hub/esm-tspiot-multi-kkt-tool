using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public enum ManagedProvisioningSessionKind
    {
        [EnumMember]
        SessionReady = 1,
        [EnumMember]
        ExecuteItem = 2,
        [EnumMember]
        ItemResult = 3,
        [EnumMember]
        Finish = 4,
        [EnumMember]
        CancelAfterCurrentItem = 5
    }

    [DataContract]
    public sealed class ManagedProvisioningSessionMessage
    {
        [DataMember(Order = 1)]
        public int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        public string OperationId { get; set; }

        [DataMember(Order = 3)]
        public long Sequence { get; set; }

        [DataMember(Order = 4)]
        public ManagedProvisioningSessionKind Kind { get; set; }

        [DataMember(Order = 5)]
        public int ItemIndex { get; set; }

        [DataMember(Order = 6)]
        public LmServiceProvisioningStatus Status { get; set; }

        [DataMember(Order = 7, EmitDefaultValue = false)]
        public string Message { get; set; }
    }
}
