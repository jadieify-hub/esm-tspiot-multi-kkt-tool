using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LmCleanupConfirmation
    {
        [DataMember(Order = 1)]
        public string KktSerial { get; set; }

        [DataMember(Order = 2)]
        public LmManifestFingerprint ManifestFingerprint { get; set; }

        [DataMember(Order = 3)]
        public LmServiceProvisioningStatus DisplayedState { get; set; }

        [DataMember(Order = 4)]
        public LmManifestFingerprint ManagedStateFingerprint { get; set; }
    }
}
