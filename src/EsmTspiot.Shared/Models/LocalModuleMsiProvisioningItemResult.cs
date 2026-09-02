using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LocalModuleMsiProvisioningItemResult
    {
        [DataMember(Order = 1)] public string Inn { get; set; }
        [DataMember(Order = 2)] public int CloneOrdinal { get; set; }
        [DataMember(Order = 3)] public int ApiPort { get; set; }
        [DataMember(Order = 4)] public LmServiceProvisioningStatus Status { get; set; }
        [DataMember(Order = 5)] public string Message { get; set; }
        [DataMember(Order = 6)] public string ManifestSha256 { get; set; }
    }
}
