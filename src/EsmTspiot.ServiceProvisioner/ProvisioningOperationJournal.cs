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

        [DataMember(Order = 8)]
        internal LmProvisioningJournalStage Stage { get; set; }

        [DataMember(Order = 9)]
        internal int GrpcPort { get; set; }

        [DataMember(Order = 10)]
        internal int RestPort { get; set; }

        [DataMember(Order = 11)]
        internal string TargetAddress { get; set; }

        [DataMember(Order = 12)]
        internal int TargetPort { get; set; }

        [DataMember(Order = 13)]
        internal string ServiceName { get; set; }

        [DataMember(Order = 14)]
        internal string SupervisorImagePath { get; set; }

        [DataMember(Order = 15)]
        internal string ProfilePath { get; set; }

        [DataMember(Order = 16)]
        internal string ServiceSid { get; set; }

        [DataMember(Order = 17)]
        internal string ControllerVersion { get; set; }

        [DataMember(Order = 18)]
        internal string ControllerBinarySha256 { get; set; }

        [DataMember(Order = 19)]
        internal string SupervisorSha256 { get; set; }
    }
}
