using System.Runtime.Serialization;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal sealed class ProvisioningOperationJournal
    {
        [DataMember(Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        internal string OperationId { get; set; }

        [DataMember(Order = 3)]
        internal LmServiceOperation Operation { get; set; }

        [DataMember(Order = 4)]
        internal string KktSerial { get; set; }

        [DataMember(Order = 5)]
        internal ManagedServiceLifecycleState State { get; set; }

        [DataMember(Order = 6)]
        internal string ManifestFingerprint { get; set; }

        [DataMember(Order = 7)]
        internal string UpdatedUtc { get; set; }
    }
}
