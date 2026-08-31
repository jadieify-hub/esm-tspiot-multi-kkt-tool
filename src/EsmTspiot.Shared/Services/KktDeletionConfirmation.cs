using System;

namespace EsmTspiot.Shared.Services
{
    public static class KktDeletionConfirmation
    {
        public static bool Matches(string kktId, string confirmation)
        {
            string normalizedId = (kktId ?? string.Empty).Trim();
            string normalizedConfirmation = (confirmation ?? string.Empty).Trim();
            if (!IsValidKktId(normalizedId) || normalizedConfirmation.Length != 4 || !IsAsciiDigits(normalizedConfirmation))
            {
                return false;
            }

            return string.Equals(
                normalizedId.Substring(normalizedId.Length - 4),
                normalizedConfirmation,
                StringComparison.Ordinal);
        }

        public static bool MatchesFullSerial(string kktId, string confirmation)
        {
            string normalizedId = (kktId ?? string.Empty).Trim();
            string normalizedConfirmation = (confirmation ?? string.Empty).Trim();
            return IsValidKktId(normalizedId) &&
                IsValidKktId(normalizedConfirmation) &&
                string.Equals(normalizedId, normalizedConfirmation, StringComparison.Ordinal);
        }

        public static bool IsValidKktId(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.Length == 14 && IsAsciiDigits(normalized);
        }

        private static bool IsAsciiDigits(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
