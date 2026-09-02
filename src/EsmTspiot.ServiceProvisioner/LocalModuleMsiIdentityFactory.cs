using System;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiIdentityFactory
    {
        private const string ProductNamespace =
            "KRS.LocalModule.MsiClone.Product.v1\n";
        private const string UpgradeNamespace =
            "KRS.LocalModule.MsiClone.Upgrade.v1\n";
        private readonly Func<Guid> _newPackageCode;

        internal LocalModuleMsiIdentityFactory(Func<Guid> newPackageCode)
        {
            if (newPackageCode == null)
                throw new ArgumentNullException("newPackageCode");
            _newPackageCode = newPackageCode;
        }

        internal LocalModuleMsiCloneIdentity Create(
            string productVersion,
            string inn,
            int cloneOrdinal)
        {
            string version = NormalizeVersion(productVersion);
            string normalizedInn = NormalizeInn(inn);
            if (cloneOrdinal < 1 ||
                cloneOrdinal > LocalModuleMsiIdentity.MaximumCloneOrdinal)
            {
                throw new ArgumentOutOfRangeException("cloneOrdinal");
            }
            Guid packageCode = _newPackageCode();
            if (packageCode == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "Generated MSI PackageCode must not be empty.");
            }
            Guid productCode = CreateStableGuid(
                ProductNamespace + version + "\n" + normalizedInn);
            Guid upgradeCode = CreateStableGuid(
                UpgradeNamespace + normalizedInn);
            if (productCode == upgradeCode || packageCode == productCode ||
                packageCode == upgradeCode)
            {
                throw new InvalidOperationException(
                    "Generated MSI identity GUIDs must be unique.");
            }
            string suffix = cloneOrdinal.ToString();
            return new LocalModuleMsiCloneIdentity
            {
                Inn = normalizedInn,
                CloneOrdinal = cloneOrdinal,
                ProductCode = productCode,
                UpgradeCode = upgradeCode,
                PackageCode = packageCode,
                ProductName =
                    "Локальный модуль Честный Знак экземпляр " + suffix,
                InstallDirectoryName = "Regime" + suffix,
                ApiServiceName = "regime" + suffix,
                DatabaseServiceName = "yenisei" + suffix
            };
        }

        private static Guid CreateStableGuid(string input)
        {
            byte[] hash;
            using (SHA256 algorithm = SHA256.Create())
            {
                hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(input));
            }
            hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
            hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
            string hex = ToHex(hash, 16);
            string value = hex.Substring(0, 8) + "-" +
                hex.Substring(8, 4) + "-" +
                hex.Substring(12, 4) + "-" +
                hex.Substring(16, 4) + "-" +
                hex.Substring(20, 12);
            return new Guid(value);
        }

        private static string ToHex(byte[] bytes, int count)
        {
            StringBuilder result = new StringBuilder(count * 2);
            for (int index = 0; index < count; index++)
            {
                result.Append(bytes[index].ToString("x2"));
            }
            return result.ToString();
        }

        private static string NormalizeVersion(string value)
        {
            Version parsed;
            string text = value == null ? string.Empty : value.Trim();
            if (!Version.TryParse(text, out parsed) || parsed.Major < 0)
            {
                throw new ArgumentException(
                    "Local-module version is invalid.",
                    "productVersion");
            }
            return text;
        }

        private static string NormalizeInn(string value)
        {
            string result = value == null ? string.Empty : value.Trim();
            if (!LocalModuleMsiIdentity.IsInn(result))
            {
                throw new ArgumentException("INN is invalid.", "inn");
            }
            return result;
        }
    }
}
