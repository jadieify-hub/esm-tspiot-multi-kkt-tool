using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class DirectControllerProvisioningItemRequest
    {
        [DataMember(Order = 1)]
        public string KktSerial { get; set; }

        [DataMember(Order = 2)]
        public string Inn { get; set; }

        [DataMember(Order = 3)]
        public int Ordinal { get; set; }

        [DataMember(Order = 4, EmitDefaultValue = false)]
        public string ExpectedManifestSha256 { get; set; }

        [DataMember(Order = 5)]
        public int TargetLocalModulePort { get; set; }
    }
}
