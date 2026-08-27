using System;
using System.Runtime.Serialization;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal enum ManagedServiceLifecycleState
    {
        [EnumMember]
        Creating = 1,
        [EnumMember]
        Updating = 2,
        [EnumMember]
        ServiceReady = 3,
        [EnumMember]
        VersionVerificationPending = 4,
        [EnumMember]
        Deleting = 5,
        [EnumMember]
        Cleaning = 6,
        [EnumMember]
        CleanupPending = 7,
        [EnumMember]
        Failed = 8
    }

    [DataContract]
    internal sealed class ManagedServiceManifest
    {
        internal const int CurrentSchemaVersion = 1;
        internal const string ExpectedOwnershipMarker = "KRS.MultiKKT.LmGateway.Managed.v1";

        [DataMember(Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        internal string OwnershipMarker { get; set; }

        [DataMember(Order = 3)]
        internal string KktSerial { get; set; }

        [DataMember(Order = 4)]
        internal string ServiceName { get; set; }

        [DataMember(Order = 5)]
        internal int GrpcPort { get; set; }

        [DataMember(Order = 6)]
        internal int RestPort { get; set; }

        [DataMember(Order = 7)]
        internal string TargetAddress { get; set; }

        [DataMember(Order = 8)]
        internal int TargetPort { get; set; }

        [DataMember(Order = 9)]
        internal string ControllerVersion { get; set; }

        [DataMember(Order = 10)]
        internal string ControllerBinarySha256 { get; set; }

        [DataMember(Order = 11)]
        internal string SupervisorSha256 { get; set; }

        [DataMember(Order = 12)]
        internal string ServiceSid { get; set; }

        [DataMember(Order = 13)]
        internal string OperationId { get; set; }

        [DataMember(Order = 14)]
        internal ManagedServiceLifecycleState LocalLifecycleState { get; set; }

        [DataMember(Order = 15)]
        internal string LastCleanupErrorClass { get; set; }

        [DataMember(Order = 16)]
        internal string UpdatedUtc { get; set; }

        [DataMember(Order = 17)]
        internal string SupervisorImagePath { get; set; }

        [DataMember(Order = 18)]
        internal string ProfilePath { get; set; }

        internal static ManagedServiceManifest Create(
            string kktSerial,
            LmGatewayPorts ports,
            LmGatewayTarget target,
            string controllerVersion,
            string controllerBinarySha256,
            string supervisorSha256,
            string serviceSid,
            string operationId,
            ManagedServiceLifecycleState state,
            string supervisorImagePath,
            string profilePath)
        {
            if (ports == null)
            {
                throw new ArgumentNullException("ports");
            }
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            string normalizedAddress;
            bool isLoopback;
            if (!LmGatewayInputValidator.TryNormalizeTargetAddress(
                target.Address,
                out normalizedAddress,
                out isLoopback))
            {
                throw new ArgumentException("Target address is invalid.", "target");
            }

            return new ManagedServiceManifest
            {
                SchemaVersion = CurrentSchemaVersion,
                OwnershipMarker = ExpectedOwnershipMarker,
                KktSerial = kktSerial,
                ServiceName = LmServiceIdentity.CreateName(kktSerial),
                GrpcPort = ports.GrpcPort,
                RestPort = ports.RestPort,
                TargetAddress = normalizedAddress,
                TargetPort = target.Port,
                ControllerVersion = controllerVersion,
                ControllerBinarySha256 = controllerBinarySha256,
                SupervisorSha256 = supervisorSha256,
                ServiceSid = serviceSid,
                OperationId = operationId,
                LocalLifecycleState = state,
                LastCleanupErrorClass = string.Empty,
                UpdatedUtc = DateTime.UtcNow.ToString("o"),
                SupervisorImagePath = supervisorImagePath,
                ProfilePath = profilePath
            };
        }
    }
}
