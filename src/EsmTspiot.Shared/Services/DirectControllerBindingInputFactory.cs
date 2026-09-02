using System;
using System.Globalization;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class DirectControllerBindingInputFactory
    {
        public static LmGatewayBindingInput Create(
            LmGatewayKkt kkt,
            DirectControllerAssignment assignment)
        {
            if (kkt == null) throw new ArgumentNullException("kkt");
            if (assignment == null)
                throw new ArgumentNullException("assignment");
            if (!string.Equals(
                    (kkt.KktSerial ?? string.Empty).Trim(),
                    assignment.KktSerial,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    (kkt.KktInn ?? string.Empty).Trim(),
                    assignment.KktInn,
                    StringComparison.Ordinal))
                throw new ArgumentException(
                    "Controller assignment does not belong to the KKT.");
            return new LmGatewayBindingInput
            {
                KktSerial = assignment.KktSerial,
                KktInn = assignment.KktInn,
                ControllerAddress = "127.0.0.1",
                ControllerGrpcPort = assignment.GrpcPort.ToString(
                    CultureInfo.InvariantCulture),
                ExpectedLmAddress = "127.0.0.1",
                ExpectedLmPort = assignment.TargetLocalModulePort.ToString(
                    CultureInfo.InvariantCulture)
            };
        }
    }
}
