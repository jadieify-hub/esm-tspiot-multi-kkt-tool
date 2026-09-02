using System;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class DirectControllerIdentity
    {
        public const int MaximumOrdinal = 32;

        public static DirectControllerRole RoleForOrdinal(int ordinal)
        {
            ValidateOrdinal(ordinal);
            return ordinal == 1
                ? DirectControllerRole.OfficialBase
                : DirectControllerRole.DirectClone;
        }

        public static string ServiceNameForOrdinal(int ordinal)
        {
            ValidateOrdinal(ordinal);
            return ordinal == 1
                ? "esm-lm-controller"
                : "esm-lm-controller-" + ordinal.ToString();
        }

        public static int GrpcPortForOrdinal(int ordinal)
        {
            ValidateOrdinal(ordinal);
            return 50062 + ordinal;
        }

        public static int RestPortForOrdinal(int ordinal)
        {
            ValidateOrdinal(ordinal);
            return 5062 + ordinal;
        }

        public static int FutureLmPortForOrdinal(int ordinal)
        {
            ValidateOrdinal(ordinal);
            return 4995 + (1000 * ordinal);
        }

        public static bool IsControllerPort(int port)
        {
            for (int ordinal = 1; ordinal <= MaximumOrdinal; ordinal++)
            {
                if (port == GrpcPortForOrdinal(ordinal) ||
                    port == RestPortForOrdinal(ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool TryParseServiceName(string serviceName, out int ordinal)
        {
            ordinal = 0;
            if (string.Equals(serviceName, "esm-lm-controller", StringComparison.Ordinal))
            {
                ordinal = 1;
                return true;
            }
            const string prefix = "esm-lm-controller-";
            int parsed;
            if (string.IsNullOrWhiteSpace(serviceName) ||
                !serviceName.StartsWith(prefix, StringComparison.Ordinal) ||
                !int.TryParse(serviceName.Substring(prefix.Length), out parsed) ||
                parsed < 2 || parsed > MaximumOrdinal ||
                !string.Equals(serviceName, prefix + parsed.ToString(), StringComparison.Ordinal))
            {
                return false;
            }
            ordinal = parsed;
            return true;
        }

        private static void ValidateOrdinal(int ordinal)
        {
            if (ordinal < 1 || ordinal > MaximumOrdinal)
            {
                throw new ArgumentOutOfRangeException("ordinal");
            }
        }
    }
}
