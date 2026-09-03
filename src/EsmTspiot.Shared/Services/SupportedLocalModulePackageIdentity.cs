using System;

namespace EsmTspiot.Shared.Services
{
    /// <summary>
    /// Устойчивые признаки пакета ЛМ ЧЗ от ЦРПТ. Точный хеш, размер, версия,
    /// ProductCode, PackageCode и отпечаток сертификата намеренно не
    /// фиксируются: пакет опознаётся по издателю, UpgradeCode и структуре,
    /// чтобы очередная версия вендора не выводила утилиту из строя.
    /// </summary>
    public static class SupportedLocalModulePackageIdentity
    {
        public const string ProductName = "Локальный модуль Честный Знак";
        public const string UpgradeCode =
            "{9449123B-61C4-40DE-AA6C-1BB9AA02EB67}";
        public const string SignerSubject =
            "CN=ООО ЦЕНТР РАЗВИТИЯ ПЕРСПЕКТИВНЫХ ТЕХНОЛОГИЙ, " +
            "O=ООО ЦЕНТР РАЗВИТИЯ ПЕРСПЕКТИВНЫХ ТЕХНОЛОГИЙ";
        public const string ApiLogin = "admin";
        public const string ApiPassword = "admin";

        // Версия рантайма для устаревшего управляемого пути: он не ставит
        // пакет и опирается на распакованный вендорный набор 2.6.1.
        public const string LegacyManagedRuntimeVersion = "2.6.1";

        public static bool MatchesSignerSubject(string observedSubject)
        {
            return ContainsRdn(
                    observedSubject,
                    "CN=ООО ЦЕНТР РАЗВИТИЯ ПЕРСПЕКТИВНЫХ ТЕХНОЛОГИЙ") &&
                ContainsRdn(
                    observedSubject,
                    "O=ООО ЦЕНТР РАЗВИТИЯ ПЕРСПЕКТИВНЫХ ТЕХНОЛОГИЙ");
        }

        public static bool MatchesUpgradeCode(string observedUpgradeCode)
        {
            Guid observed;
            Guid expected;
            if (!Guid.TryParseExact(
                    observedUpgradeCode == null
                        ? string.Empty
                        : observedUpgradeCode.Trim(),
                    "B",
                    out observed) ||
                !Guid.TryParseExact(UpgradeCode, "B", out expected))
            {
                return false;
            }
            return observed == expected;
        }

        private static bool ContainsRdn(
            string observedSubject,
            string expectedRdn)
        {
            if (string.IsNullOrWhiteSpace(observedSubject))
            {
                return false;
            }
            string[] parts = observedSubject.Split(',');
            for (int index = 0; index < parts.Length; index++)
            {
                if (string.Equals(
                    parts[index].Trim(),
                    expectedRdn,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
