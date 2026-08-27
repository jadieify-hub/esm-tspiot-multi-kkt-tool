using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LmRemovalConfirmation
    {
        [DataMember(Order = 1)]
        public string KktSerial { get; set; }

        [DataMember(Order = 2)]
        public int GrpcPort { get; set; }

        [DataMember(Order = 3)]
        public int RestPort { get; set; }

        [DataMember(Order = 4)]
        public LmManifestFingerprint ManifestFingerprint { get; set; }

        [DataMember(Order = 5)]
        public bool RetainedEsmWarningAccepted { get; set; }
    }
}
