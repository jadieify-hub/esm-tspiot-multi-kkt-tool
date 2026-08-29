using System;
using System.Globalization;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class LmGatewayDraftDefaults
    {
        public const string ControllerAddress = "127.0.0.1";
        public const string TargetLmPort = "5995";
        public const int GrpcPortFirst = 45001;
        public const int GrpcPortLast = 45032;
        public const int RestPortFirst = 15001;
        public const int RestPortLast = 15032;
        private const int TargetLmPortFirst = 5995;
        private const int TargetLmPortStep = 1000;

        public static LmGatewayDraft Create(LmGatewayKkt kkt)
        {
            return Create(kkt, 1);
        }

        public static LmGatewayDraft Create(LmGatewayKkt kkt, int ordinal)
        {
            if (ordinal < 1)
            {
                throw new ArgumentOutOfRangeException("ordinal");
            }
            return new LmGatewayDraft
            {
                KktSerial = Trim(kkt == null ? null : kkt.KktSerial),
                KktInn = Trim(kkt == null ? null : kkt.KktInn),
                TargetAddress = ControllerAddress,
                TargetPort = (TargetLmPortFirst + ((ordinal - 1) * TargetLmPortStep))
                    .ToString(CultureInfo.InvariantCulture),
                GrpcPort = (GrpcPortFirst + ordinal - 1).ToString(CultureInfo.InvariantCulture),
                RestPort = (RestPortFirst + ordinal - 1).ToString(CultureInfo.InvariantCulture)
            };
        }

        public static string FormatAutomaticGrpcPort(LmGatewayDraft draft)
        {
            string explicitPort = Trim(draft == null ? null : draft.GrpcPort);
            return explicitPort.Length > 0
                ? explicitPort
                : "авто: " + GrpcPortFirst + "–" + GrpcPortLast;
        }

        public static string FormatAutomaticRestPort(LmGatewayDraft draft)
        {
            string explicitPort = Trim(draft == null ? null : draft.RestPort);
            return explicitPort.Length > 0
                ? explicitPort
                : "авто: " + RestPortFirst + "–" + RestPortLast;
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
