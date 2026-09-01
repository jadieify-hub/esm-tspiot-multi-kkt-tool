using System;
using System.Runtime.Serialization;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal enum DirectControllerLifecycleState
    {
        [EnumMember]
        Preparing = 1,
        [EnumMember]
        Ready = 2,
        [EnumMember]
        RequiresAttention = 3,
        [EnumMember]
        Removing = 4
    }

    [DataContract]
    internal sealed class DirectControllerManifest
    {
        internal const int CurrentSchemaVersion = 1;
        internal const string ExpectedOwnershipMarker =
            "KRS.MultiKKT.DirectController.Managed.v1";

        [DataMember(Order = 1)] internal int SchemaVersion { get; set; }
        [DataMember(Order = 2)] internal string OwnershipMarker { get; set; }
        [DataMember(Order = 3)] internal string KktSerial { get; set; }
        [DataMember(Order = 4)] internal string KktInn { get; set; }
        [DataMember(Order = 5)] internal int Ordinal { get; set; }
        [DataMember(Order = 6)] internal string ServiceName { get; set; }
        [DataMember(Order = 7)] internal int GrpcPort { get; set; }
        [DataMember(Order = 8)] internal int RestPort { get; set; }
        [DataMember(Order = 9)] internal int FutureLocalModulePort { get; set; }
        [DataMember(Order = 10)] internal string ControllerVersion { get; set; }
        [DataMember(Order = 11)] internal string ControllerBinarySha256 { get; set; }
        [DataMember(Order = 12)] internal string ProfileEnvironmentRoot { get; set; }
        [DataMember(Order = 13)] internal string OperationId { get; set; }
        [DataMember(Order = 14)] internal DirectControllerLifecycleState State { get; set; }
        [DataMember(Order = 15)] internal string UpdatedUtc { get; set; }

        internal static DirectControllerManifest Create(
            string kktSerial,
            string kktInn,
            int ordinal,
            string controllerVersion,
            string controllerBinarySha256,
            string profileEnvironmentRoot,
            string operationId,
            DirectControllerLifecycleState state)
        {
            return new DirectControllerManifest
            {
                SchemaVersion = CurrentSchemaVersion,
                OwnershipMarker = ExpectedOwnershipMarker,
                KktSerial = kktSerial,
                KktInn = kktInn,
                Ordinal = ordinal,
                ServiceName = DirectControllerIdentity.ServiceNameForOrdinal(ordinal),
                GrpcPort = DirectControllerIdentity.GrpcPortForOrdinal(ordinal),
                RestPort = DirectControllerIdentity.RestPortForOrdinal(ordinal),
                FutureLocalModulePort = DirectControllerIdentity.FutureLmPortForOrdinal(ordinal),
                ControllerVersion = controllerVersion,
                ControllerBinarySha256 = controllerBinarySha256,
                ProfileEnvironmentRoot = profileEnvironmentRoot,
                OperationId = operationId,
                State = state,
                UpdatedUtc = DateTime.UtcNow.ToString("o")
            };
        }
    }

    [DataContract]
    internal sealed class DirectControllerInventoryProjection
    {
        [DataMember(Order = 1)] internal string KktSerial { get; set; }
        [DataMember(Order = 2)] internal string KktInn { get; set; }
        [DataMember(Order = 3)] internal int Ordinal { get; set; }
        [DataMember(Order = 4)] internal string ServiceName { get; set; }
        [DataMember(Order = 5)] internal int GrpcPort { get; set; }
        [DataMember(Order = 6)] internal int RestPort { get; set; }
        [DataMember(Order = 7)] internal int FutureLocalModulePort { get; set; }
        [DataMember(Order = 8)] internal string ControllerVersion { get; set; }
        [DataMember(Order = 9)] internal DirectControllerLifecycleState State { get; set; }
        [DataMember(Order = 10)] internal string RemovalFingerprint { get; set; }
    }
}
