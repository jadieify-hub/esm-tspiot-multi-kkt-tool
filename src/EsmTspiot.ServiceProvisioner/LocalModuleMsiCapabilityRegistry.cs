using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface ILocalModuleMsiCapabilityRegistry
    {
        LocalModuleMsiCapabilityProfile FindExact(
            WindowsInstallerPackageMetadata metadata,
            TrustedFileExpectation trust);
    }

    internal sealed class LocalModuleMsiCapabilityRegistry
        : ILocalModuleMsiCapabilityRegistry
    {
        private const string SupportedProfileResource =
            "EsmTspiot.ServiceProvisioner.Profiles." +
            "local-module-msi-2.6.1-7-profile.json";

        public LocalModuleMsiCapabilityProfile FindExact(
            WindowsInstallerPackageMetadata metadata,
            TrustedFileExpectation trust)
        {
            if (metadata == null || trust == null)
            {
                throw UnsupportedProfile();
            }
            LocalModuleMsiCapabilityProfile profile = LoadSupportedProfile();
            if (!string.Equals(
                    profile.ProductName,
                    metadata.ProductName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    profile.ProductVersion,
                    metadata.ProductVersion,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    profile.ProductCode,
                    metadata.ProductCode,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    profile.UpgradeCode,
                    metadata.UpgradeCode,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    profile.PackageCode,
                    metadata.PackageCode,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    profile.FileName,
                    trust.FileName,
                    StringComparison.Ordinal) ||
                profile.ByteLength != trust.ByteLength ||
                !CanonicalLmPlanHasher.FixedTimeEqualsHex(
                    profile.Sha256,
                    trust.Sha256) ||
                !string.Equals(
                    profile.SignerThumbprint,
                    trust.SignerThumbprint,
                    StringComparison.OrdinalIgnoreCase) ||
                !trust.RequireCodeSigningEku)
            {
                throw UnsupportedProfile();
            }
            return profile;
        }

        internal static LocalModuleMsiCapabilityProfile LoadSupportedProfile()
        {
            Assembly assembly = typeof(LocalModuleMsiCapabilityRegistry).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(
                SupportedProfileResource))
            {
                if (stream == null)
                {
                    throw new InvalidDataException(
                        "Embedded local-module MSI capability profile is missing.");
                }
                LocalModuleMsiCapabilityProfile profile =
                    (LocalModuleMsiCapabilityProfile)
                    new DataContractJsonSerializer(
                        typeof(LocalModuleMsiCapabilityProfile)).ReadObject(stream);
                ValidateShape(profile);
                return profile;
            }
        }

        private static void ValidateShape(LocalModuleMsiCapabilityProfile profile)
        {
            if (profile == null || profile.SchemaVersion != 1 ||
                string.IsNullOrWhiteSpace(profile.FileName) ||
                profile.ByteLength <= 0 ||
                !IsHex(profile.Sha256, 64) ||
                !IsHex(profile.SignerThumbprint, 40) ||
                string.IsNullOrWhiteSpace(profile.ProductName) ||
                string.IsNullOrWhiteSpace(profile.ProductVersion) ||
                string.IsNullOrWhiteSpace(profile.ProductCode) ||
                string.IsNullOrWhiteSpace(profile.UpgradeCode) ||
                string.IsNullOrWhiteSpace(profile.PackageCode) ||
                profile.FileRowCount <= 0 ||
                profile.MsiFileHashRowCount <= 0 ||
                profile.Media == null || profile.Media.Count != 2 ||
                profile.Rows == null || profile.Rows.Count == 0)
            {
                throw new InvalidDataException(
                    "Embedded local-module MSI capability profile is invalid.");
            }
        }

        private static InvalidDataException UnsupportedProfile()
        {
            return new InvalidDataException(
                "Local-module MSI unsupported-profile: exact identity is unknown.");
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
