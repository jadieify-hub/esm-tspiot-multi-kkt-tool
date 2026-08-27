using System;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LmProfileConfiguration
    {
        internal LmProfileConfiguration(
            string controllerVersion,
            int grpcPort,
            int restPort,
            string targetAddress,
            int targetPort)
        {
            LmGatewayTarget target = new LmGatewayTarget(targetAddress, targetPort);
            ValidationResult validation = LmGatewayInputValidator.ValidateTarget(target);
            if (!validation.IsValid || grpcPort < 1 || grpcPort > 65535 ||
                restPort < 1 || restPort > 65535 || grpcPort == restPort)
            {
                throw new ArgumentException("LM profile configuration is invalid.");
            }
            string normalized;
            bool isLoopback;
            LmGatewayInputValidator.TryNormalizeTargetAddress(
                targetAddress,
                out normalized,
                out isLoopback);
            ControllerVersion = controllerVersion;
            GrpcPort = grpcPort;
            RestPort = restPort;
            TargetAddress = normalized;
            TargetPort = targetPort;
        }

        internal string ControllerVersion { get; private set; }
        internal int GrpcPort { get; private set; }
        internal int RestPort { get; private set; }
        internal string TargetAddress { get; private set; }
        internal int TargetPort { get; private set; }

        internal static LmProfileConfiguration FromSpec(
            string controllerVersion,
            ManagedLmServiceSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException("spec");
            }
            return new LmProfileConfiguration(
                controllerVersion,
                spec.Ports.GrpcPort,
                spec.Ports.RestPort,
                spec.Target.Address,
                spec.Target.Port);
        }
    }
}
