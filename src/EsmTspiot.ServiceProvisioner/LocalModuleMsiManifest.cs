using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal sealed class LocalModuleMsiManifest
    {
        internal const int CurrentSchemaVersion = 5;
        internal const string ExpectedOwnershipMarker =
            "KRS.MultiKKT.LocalModuleMsi.Product.v1";

        [DataMember(Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        internal string OwnershipMarker { get; set; }

        [DataMember(Order = 3)]
        internal string Inn { get; set; }

        [DataMember(Order = 4)]
        internal int CloneOrdinal { get; set; }

        [DataMember(Order = 5)]
        internal int ApiPort { get; set; }

        [DataMember(Order = 6)]
        internal int DatabasePort { get; set; }

        [DataMember(Order = 7)]
        internal string ProductCode { get; set; }

        [DataMember(Order = 8)]
        internal string PackageCode { get; set; }

        [DataMember(Order = 9)]
        internal string InstallRoot { get; set; }

        [DataMember(Order = 10)]
        internal bool InstalledByApplication { get; set; }

        [DataMember(Order = 11)]
        internal bool PreExisting { get; set; }

        [DataMember(Order = 12)]
        internal string OwnershipNonce { get; set; }

        [DataMember(Order = 13)]
        internal string UpdatedUtc { get; set; }

        [DataMember(Order = 14)]
        internal string FirewallRuleName { get; set; }

        [DataMember(Order = 15)]
        internal string FirewallRuleHash { get; set; }

        [DataMember(Order = 16)]
        internal string ManifestSha256 { get; set; }

        // Start-mode journal for an adopted vendor base: the previous SCM start
        // types are recorded once so removal can hand them back unchanged.
        [DataMember(Order = 17)]
        internal bool StartModeAdjusted { get; set; }

        [DataMember(Order = 18)]
        internal int PreviousApiStartMode { get; set; }

        [DataMember(Order = 19)]
        internal int PreviousDatabaseStartMode { get; set; }

        // Версия установленного продукта фиксируется на том, что реально
        // стоит в системе: базовый ЛМ вендора может быть любой версии.
        [DataMember(Order = 20)]
        internal string ProductVersion { get; set; }

        // Путь erl.exe, на который выписано правило сети: каталог erts-*
        // меняется вместе с версией пакета.
        [DataMember(Order = 21)]
        internal string FirewallProgramPath { get; set; }

        internal bool CanRemove
        {
            get { return InstalledByApplication && !PreExisting; }
        }

        internal static LocalModuleMsiManifest Create(
            string inn,
            int cloneOrdinal,
            int apiPort,
            int databasePort,
            string productCode,
            string packageCode,
            string productVersion,
            string installRoot,
            bool installedByApplication,
            bool preExisting,
            string ownershipNonce)
        {
            LocalModuleMsiManifest result = new LocalModuleMsiManifest
            {
                SchemaVersion = CurrentSchemaVersion,
                OwnershipMarker = ExpectedOwnershipMarker,
                Inn = inn,
                CloneOrdinal = cloneOrdinal,
                ApiPort = apiPort,
                DatabasePort = databasePort,
                ProductCode = NormalizeGuid(productCode, "productCode"),
                PackageCode = NormalizeGuid(packageCode, "packageCode"),
                ProductVersion = productVersion == null
                    ? string.Empty
                    : productVersion.Trim(),
                InstallRoot = NormalizeRoot(installRoot),
                InstalledByApplication = installedByApplication,
                PreExisting = preExisting,
                OwnershipNonce = ownershipNonce,
                UpdatedUtc = DateTime.UtcNow.ToString(
                    "o",
                    CultureInfo.InvariantCulture),
                FirewallRuleName = string.Empty,
                FirewallRuleHash = string.Empty,
                FirewallProgramPath = string.Empty,
                StartModeAdjusted = false,
                PreviousApiStartMode = 0,
                PreviousDatabaseStartMode = 0,
                ManifestSha256 = string.Empty
            };
            ValidateStructure(result, false);
            result.ManifestSha256 = ComputeSha256(result);
            return result;
        }

        internal static void Validate(LocalModuleMsiManifest manifest)
        {
            ValidateStructure(manifest, true);
            if (!string.Equals(
                    manifest.ManifestSha256,
                    ComputeSha256(manifest),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Local-module MSI manifest hash mismatch.");
        }

        internal void AttachFirewallRule(LocalModuleFirewallRule rule)
        {
            if (rule == null) throw new ArgumentNullException("rule");
            if (!string.Equals(
                    rule.OwnershipId,
                    OwnershipNonce,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Firewall rule ownership does not match the manifest.");
            FirewallRuleName = rule.RuleName;
            FirewallRuleHash = rule.ExpectedFieldHash;
            FirewallProgramPath = rule.ProgramPath;
            ManifestSha256 = ComputeSha256(this);
        }

        internal void RecordStartModeAdjustment(
            WindowsServiceStartMode previousApiStartMode,
            WindowsServiceStartMode previousDatabaseStartMode)
        {
            if (StartModeAdjusted)
                throw new InvalidOperationException(
                    "Start-mode adjustment is already recorded.");
            StartModeAdjusted = true;
            PreviousApiStartMode = (int)previousApiStartMode;
            PreviousDatabaseStartMode = (int)previousDatabaseStartMode;
            ManifestSha256 = ComputeSha256(this);
        }

        internal static string ComputeSha256(LocalModuleMsiManifest manifest)
        {
            ValidateStructure(manifest, false);
            string canonical = string.Join("\n", new[]
            {
                manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                manifest.OwnershipMarker,
                manifest.Inn,
                manifest.CloneOrdinal.ToString(CultureInfo.InvariantCulture),
                manifest.ApiPort.ToString(CultureInfo.InvariantCulture),
                manifest.DatabasePort.ToString(CultureInfo.InvariantCulture),
                manifest.ProductCode,
                manifest.PackageCode,
                manifest.ProductVersion ?? string.Empty,
                manifest.InstallRoot,
                manifest.InstalledByApplication ? "1" : "0",
                manifest.PreExisting ? "1" : "0",
                manifest.OwnershipNonce,
                manifest.UpdatedUtc,
                manifest.FirewallRuleName ?? string.Empty,
                manifest.FirewallRuleHash ?? string.Empty,
                manifest.StartModeAdjusted ? "1" : "0",
                manifest.PreviousApiStartMode.ToString(
                    CultureInfo.InvariantCulture),
                manifest.PreviousDatabaseStartMode.ToString(
                    CultureInfo.InvariantCulture),
                manifest.FirewallProgramPath ?? string.Empty
            });
            byte[] digest;
            using (SHA256 algorithm = SHA256.Create())
                digest = algorithm.ComputeHash(
                    new UTF8Encoding(false, true).GetBytes(canonical));
            StringBuilder value = new StringBuilder(64);
            for (int index = 0; index < digest.Length; index++)
                value.Append(digest[index].ToString("x2"));
            return value.ToString();
        }

        private static void ValidateStructure(
            LocalModuleMsiManifest manifest,
            bool requireHash)
        {
            if (manifest == null ||
                manifest.SchemaVersion != CurrentSchemaVersion ||
                !string.Equals(
                    manifest.OwnershipMarker,
                    ExpectedOwnershipMarker,
                    StringComparison.Ordinal) ||
                !LocalModuleMsiIdentity.IsInn(manifest.Inn) ||
                manifest.CloneOrdinal < 0 ||
                manifest.CloneOrdinal >
                    LocalModuleMsiIdentity.MaximumCloneOrdinal ||
                manifest.ApiPort < 1024 || manifest.ApiPort > 65535 ||
                manifest.DatabasePort < 1024 ||
                manifest.DatabasePort > 65535 ||
                manifest.ApiPort == manifest.DatabasePort ||
                (manifest.CloneOrdinal > 0 &&
                 (manifest.ApiPort != LocalModuleMsiIdentity.ApiPortForClone(
                     manifest.CloneOrdinal) ||
                  manifest.DatabasePort !=
                    LocalModuleMsiIdentity.DatabasePortForClone(
                        manifest.CloneOrdinal))) ||
                (manifest.InstalledByApplication == manifest.PreExisting) ||
                (manifest.CloneOrdinal > 0 && manifest.PreExisting) ||
                !LocalModuleManagedIdentity.IsLowerHex(
                    manifest.OwnershipNonce,
                    32) ||
                manifest.ProductVersion == null ||
                string.IsNullOrWhiteSpace(manifest.UpdatedUtc))
                throw new InvalidDataException(
                    "Local-module MSI manifest is invalid.");
            RequireCanonicalGuid(manifest.ProductCode);
            RequireCanonicalGuid(manifest.PackageCode);
            string firewallName = manifest.FirewallRuleName ?? string.Empty;
            string firewallHash = manifest.FirewallRuleHash ?? string.Empty;
            if ((firewallName.Length == 0) != (firewallHash.Length == 0) ||
                (firewallName.Length != 0 &&
                 (!string.Equals(
                     firewallName,
                     LocalModuleFirewallRule.RuleNameFor(
                         manifest.OwnershipNonce),
                     StringComparison.Ordinal) ||
                  !LocalModuleManagedIdentity.IsLowerHex(
                      firewallHash,
                      64))))
                throw new InvalidDataException(
                    "Local-module MSI firewall ownership is invalid.");
            string firewallProgram = manifest.FirewallProgramPath ?? string.Empty;
            if ((firewallName.Length == 0) != (firewallProgram.Length == 0) ||
                (firewallProgram.Length != 0 &&
                 (!Path.IsPathRooted(firewallProgram) ||
                  !string.Equals(
                      Path.GetFileName(firewallProgram),
                      "erl.exe",
                      StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException(
                    "Local-module MSI firewall program path is invalid.");
            if (manifest.StartModeAdjusted
                ? (!IsStartMode(manifest.PreviousApiStartMode) ||
                   !IsStartMode(manifest.PreviousDatabaseStartMode))
                : (manifest.PreviousApiStartMode != 0 ||
                   manifest.PreviousDatabaseStartMode != 0))
                throw new InvalidDataException(
                    "Local-module MSI start-mode record is invalid.");
            if (!string.Equals(
                    manifest.InstallRoot,
                    NormalizeRoot(manifest.InstallRoot),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Local-module MSI install root is invalid.");
            if (requireHash &&
                !LocalModuleManagedIdentity.IsLowerHex(
                    manifest.ManifestSha256,
                    64))
                throw new InvalidDataException(
                    "Local-module MSI manifest hash is invalid.");
        }

        private static bool IsStartMode(int value)
        {
            return value == (int)WindowsServiceStartMode.AutoStart ||
                value == (int)WindowsServiceStartMode.DemandStart ||
                value == (int)WindowsServiceStartMode.Disabled;
        }

        private static string NormalizeGuid(string value, string parameter)
        {
            Guid parsed;
            if (!Guid.TryParseExact(value, "B", out parsed))
                throw new ArgumentException(
                    "MSI identity must be a braced GUID.",
                    parameter);
            return parsed.ToString("B").ToUpperInvariant();
        }

        private static void RequireCanonicalGuid(string value)
        {
            Guid parsed;
            if (!Guid.TryParseExact(value, "B", out parsed) ||
                !string.Equals(
                    value,
                    parsed.ToString("B"),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Local-module MSI identity is invalid.");
        }

        private static string NormalizeRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
                throw new InvalidDataException(
                    "Local-module MSI install root is invalid.");
            return Path.GetFullPath(value)
                .TrimEnd(Path.DirectorySeparatorChar);
        }
    }
}
