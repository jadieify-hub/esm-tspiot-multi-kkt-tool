using System;
using System.Globalization;
using System.Runtime.Serialization;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal enum ManagedKktStackLifecycleState
    {
        [EnumMember]
        Preparing = 1,
        [EnumMember]
        Ready = 2,
        [EnumMember]
        Deleting = 3,
        [EnumMember]
        CleanupPending = 4,
        [EnumMember]
        Failed = 5
    }

    [DataContract]
    internal sealed class ManagedKktStackManifest
    {
        internal const int CurrentSchemaVersion = 1;
        internal const string ExpectedOwnershipMarker =
            "KRS.MultiKKT.ManagedKkt.Stack.v1";

        [DataMember(Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        internal string OwnershipMarker { get; set; }

        [DataMember(Order = 3)]
        internal string StackId { get; set; }

        [DataMember(Order = 4)]
        internal string KktSerial { get; set; }

        [DataMember(Order = 5)]
        internal string Inn { get; set; }

        [DataMember(Order = 6)]
        internal int KktOrdinal { get; set; }

        [DataMember(Order = 7)]
        internal string LocalModuleInstanceId { get; set; }

        [DataMember(Order = 8)]
        internal string EsmRecordId { get; set; }

        [DataMember(Order = 9)]
        internal string ControllerServiceName { get; set; }

        [DataMember(Order = 10)]
        internal int ControllerGrpcPort { get; set; }

        [DataMember(Order = 11)]
        internal int ControllerRestPort { get; set; }

        [DataMember(Order = 12)]
        internal string OwnershipNonce { get; set; }

        [DataMember(Order = 13)]
        internal ManagedKktStackLifecycleState State { get; set; }

        [DataMember(Order = 14)]
        internal string CurrentOperationId { get; set; }

        [DataMember(Order = 15)]
        internal string LastCompletedOperationId { get; set; }

        [DataMember(Order = 16)]
        internal string UpdatedUtc { get; set; }

        internal static ManagedKktStackManifest Create(
            ManagedLocalModuleProvisioningItemRequest item,
            string localModuleInstanceId,
            string esmRecordId,
            string ownershipNonce,
            string operationId)
        {
            if (item == null) throw new ArgumentNullException("item");
            return new ManagedKktStackManifest
            {
                SchemaVersion = CurrentSchemaVersion,
                OwnershipMarker = ExpectedOwnershipMarker,
                StackId = LocalModuleManagedIdentity.CreateStackId(item.KktSerial),
                KktSerial = item.KktSerial,
                Inn = item.Inn,
                KktOrdinal = item.KktOrdinal,
                LocalModuleInstanceId = localModuleInstanceId,
                EsmRecordId = esmRecordId ?? string.Empty,
                ControllerServiceName = LmServiceIdentity.CreateName(item.KktSerial),
                ControllerGrpcPort = item.ControllerGrpcPort,
                ControllerRestPort = item.ControllerRestPort,
                OwnershipNonce = ownershipNonce,
                State = ManagedKktStackLifecycleState.Preparing,
                CurrentOperationId = operationId,
                LastCompletedOperationId = string.Empty,
                UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }
    }
}
