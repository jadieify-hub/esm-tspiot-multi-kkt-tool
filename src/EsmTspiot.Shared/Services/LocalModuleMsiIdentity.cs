using System;

namespace EsmTspiot.Shared.Services
{
    public static class LocalModuleMsiIdentity
    {
        public const int MaximumCloneOrdinal = 31;

        public static int ApiPortForClone(int ordinal)
        {
            ValidateCloneOrdinal(ordinal);
            return 5995 + (1000 * ordinal);
        }

        public static int DatabasePortForClone(int ordinal)
        {
            ValidateCloneOrdinal(ordinal);
            return 6984 + (1000 * ordinal);
        }

        public static string ApiServiceName(int cloneOrdinal)
        {
            ValidateAnyOrdinal(cloneOrdinal);
            return cloneOrdinal == 0
                ? "regime"
                : "regime" + cloneOrdinal.ToString();
        }

        public static string DatabaseServiceName(int cloneOrdinal)
        {
            ValidateAnyOrdinal(cloneOrdinal);
            return cloneOrdinal == 0
                ? "yenisei"
                : "yenisei" + cloneOrdinal.ToString();
        }

        public static bool IsInn(string value)
        {
            return IsAsciiDigits(value, 10) || IsAsciiDigits(value, 12);
        }

        private static void ValidateCloneOrdinal(int ordinal)
        {
            if (ordinal < 1 || ordinal > MaximumCloneOrdinal)
            {
                throw new ArgumentOutOfRangeException("ordinal");
            }
        }

        private static void ValidateAnyOrdinal(int ordinal)
        {
            if (ordinal < 0 || ordinal > MaximumCloneOrdinal)
            {
                throw new ArgumentOutOfRangeException("cloneOrdinal");
            }
        }

        private static bool IsAsciiDigits(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9') return false;
            }
            return true;
        }
    }
}
