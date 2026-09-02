using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LocalModuleMsiProvisioningItemRequest
    {
        [DataMember(Order = 1)] public string Inn { get; set; }
        [DataMember(Order = 2)] public int CloneOrdinal { get; set; }
        [DataMember(Order = 3)] public int ApiPort { get; set; }
        [DataMember(Order = 4)] public int DatabasePort { get; set; }
        [DataMember(Order = 5)] public string InstallVolumeRoot { get; set; }
        [DataMember(Order = 6)] public string RemoteAddress { get; set; }
        [DataMember(Order = 7)] public string ExpectedManifestSha256 { get; set; }
    }
}
