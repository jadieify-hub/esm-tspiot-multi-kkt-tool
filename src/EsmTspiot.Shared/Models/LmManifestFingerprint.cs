using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LmManifestFingerprint
    {
        [DataMember(Order = 1)]
        public string Sha256 { get; set; }
    }
}
