using System;

namespace EsmTspiot.Shared.Services
{
    public static class LmServiceIdentity
    {
        private const string Prefix = "krs-esm-lm-";

        public static string CreateName(string kktSerial)
        {
            if (kktSerial == null || kktSerial.Length != 14 || !IsAsciiDigits(kktSerial))
            {
                throw new ArgumentException(
                    "KKT serial must contain exactly 14 ASCII digits.",
                    "kktSerial");
            }

            return Prefix + kktSerial;
        }

        private static bool IsAsciiDigits(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
