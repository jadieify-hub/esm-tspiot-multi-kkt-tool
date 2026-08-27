using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public enum LmServiceProvisioningStatus
    {
        [EnumMember]
        Pending = 0,
        [EnumMember]
        Succeeded = 1,
        [EnumMember]
        Failed = 2,
        [EnumMember]
        Cancelled = 3,
        [EnumMember]
        UnsupportedController = 4,
        [EnumMember]
        VersionVerificationPending = 5,
        [EnumMember]
        CleanupPending = 6,
        [EnumMember]
        RemovalBlocked = 7,
        [EnumMember]
        MarkedForDelete = 8,
        [EnumMember]
        RemovedLocalArtifactsBindingRetained = 9,
        [EnumMember]
        RequiresAttention = 10
    }
}
