using System;
using System.Collections.Generic;
using System.IO;

namespace EsmTspiot.ServiceProvisioner
{
    internal enum CapabilityFactProvenance
    {
        OfficialPackage = 1,
        SelfDocumentingCli = 2,
        PrivateBlackBox = 3
    }

    internal enum PeMachine
    {
        Unknown = 0,
        I386 = 0x014c,
        Amd64 = 0x8664
    }

    internal sealed class TrustedFileExpectation
    {
        internal string FileName { get; set; }
        internal long ByteLength { get; set; }
        internal string Sha256 { get; set; }
        internal string FileVersion { get; set; }
        internal string ProductVersion { get; set; }
        internal string ProductName { get; set; }
        internal string CompanyName { get; set; }
        internal PeMachine Machine { get; set; }
        internal string SignerSubject { get; set; }
        internal string SignerThumbprint { get; set; }
        internal bool RequireCodeSigningEku { get; set; }

        internal TrustedFileExpectation Clone()
        {
            return (TrustedFileExpectation)MemberwiseClone();
        }
    }

    internal sealed class ControllerCapabilityProfile
    {
        private const string SignerSubjectValue =
            "E=it@ao-esp.ru, CN=JSC ESP, O=JSC ESP, L=Moscow, S=Moscow, C=RU";
        private const string SignerThumbprintValue =
            "1CD26372850FE30F1559821CF5D318591695271A";

        private ControllerCapabilityProfile()
        {
            Provenance = new Dictionary<string, CapabilityFactProvenance>(StringComparer.Ordinal);
        }

        internal string Version { get; private set; }
        internal string InstalledProductName { get; private set; }
        internal string InstallRoot { get; private set; }
        internal string ControllerRelativePath { get; set; }
        internal TrustedFileExpectation ControllerBinary { get; private set; }
        internal TrustedFileExpectation Installer { get; private set; }
        internal string InstallerArguments { get; private set; }
        internal string ProfileEnvironmentKey { get; private set; }
        internal string VendorProfileRelativePath { get; private set; }
        internal string VendorConfigFileName { get; private set; }
        internal string ServiceAccountName { get; private set; }
        internal IList<string> ServiceDependencies { get; private set; }
        internal WindowsServiceStartMode ServiceStartMode { get; private set; }
        internal WindowsServiceErrorControl ServiceErrorControl { get; private set; }
        internal WindowsServiceSidType ServiceSidType { get; private set; }
        internal WindowsServiceRecoveryPolicy ServiceRecoveryPolicy { get; private set; }
        internal string TerminalArguments { get; private set; }
        internal int GracefulStopTimeoutMilliseconds { get; private set; }
        internal IList<string> GeneratedArtifactFileNames { get; private set; }
        internal bool ListenerUsesDualStackIpv6Wildcard { get; private set; }
        internal IDictionary<string, CapabilityFactProvenance> Provenance { get; private set; }

        internal static ControllerCapabilityProfile SupportedVersion1640()
        {
            string programFiles64 = Environment.GetEnvironmentVariable("ProgramW6432");
            if (string.IsNullOrWhiteSpace(programFiles64))
            {
                programFiles64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            }

            ControllerCapabilityProfile profile = new ControllerCapabilityProfile
            {
                Version = "1.6.4.0",
                InstalledProductName = "ЕСП Контроллер ЛМ ЧЗ",
                InstallRoot = Path.Combine(programFiles64, "ESP", "LMController"),
                ControllerRelativePath = Path.Combine("bin", "lmcontroller.exe"),
                ProfileEnvironmentKey = "ProgramData",
                VendorProfileRelativePath = Path.Combine("ESP", "lmcontroller"),
                VendorConfigFileName = "config.yml",
                ServiceAccountName = "LocalSystem",
                ServiceDependencies = new List<string>(),
                ServiceStartMode = WindowsServiceStartMode.AutoStart,
                ServiceErrorControl = WindowsServiceErrorControl.Ignore,
                ServiceSidType = WindowsServiceSidType.None,
                ServiceRecoveryPolicy = new WindowsServiceRecoveryPolicy(
                    60,
                    new[] { 30000, 60000, 60000 }),
                TerminalArguments = string.Empty,
                InstallerArguments = "/S",
                GracefulStopTimeoutMilliseconds = 30000,
                GeneratedArtifactFileNames = new List<string>
                {
                    "ca.crt",
                    "ca.pem",
                    "server.crt",
                    "server.pem"
                },
                ListenerUsesDualStackIpv6Wildcard = true,
                ControllerBinary = new TrustedFileExpectation
                {
                    FileName = "lmcontroller.exe",
                    ByteLength = 14668016,
                    Sha256 = "0a25b29a39b100fe461eb3ffa06a6b18f2b474f337efdba9f7a9ca89740ffd0a",
                    FileVersion = string.Empty,
                    ProductVersion = string.Empty,
                    ProductName = string.Empty,
                    CompanyName = string.Empty,
                    Machine = PeMachine.Amd64,
                    SignerSubject = SignerSubjectValue,
                    SignerThumbprint = SignerThumbprintValue,
                    RequireCodeSigningEku = true
                },
                Installer = new TrustedFileExpectation
                {
                    FileName = "esm-lm-controller_1.6.4.0-windows-setup.exe",
                    ByteLength = 11088544,
                    Sha256 = "2f97da8b93b6f7bc820385f5b70feab0ce7cbe2c6dbc25c0bca2348928bb3192",
                    FileVersion = "1.6.4.0",
                    ProductVersion = string.Empty,
                    ProductName = "ЕСП Контроллер ЛМ ЧЗ",
                    CompanyName = "ЕСП",
                    Machine = PeMachine.I386,
                    SignerSubject = SignerSubjectValue,
                    SignerThumbprint = SignerThumbprintValue,
                    RequireCodeSigningEku = true
                }
            };
            profile.TagProductionFacts();
            return profile;
        }

        internal static ControllerCapabilityProfile CreateForTesting(
            string version,
            string installRoot,
            string controllerRelativePath,
            TrustedFileExpectation controllerBinary,
            TrustedFileExpectation installer,
            string profileEnvironmentKey)
        {
            ControllerCapabilityProfile profile = new ControllerCapabilityProfile
            {
                Version = version,
                InstalledProductName = "ЕСП Контроллер ЛМ ЧЗ",
                InstallRoot = Path.GetFullPath(installRoot),
                ControllerRelativePath = controllerRelativePath,
                ControllerBinary = controllerBinary.Clone(),
                Installer = installer.Clone(),
                ProfileEnvironmentKey = profileEnvironmentKey,
                VendorProfileRelativePath = Path.Combine("ESP", "lmcontroller"),
                VendorConfigFileName = "config.yml",
                ServiceAccountName = "LocalSystem",
                ServiceDependencies = new List<string>(),
                ServiceStartMode = WindowsServiceStartMode.AutoStart,
                ServiceErrorControl = WindowsServiceErrorControl.Ignore,
                ServiceSidType = WindowsServiceSidType.None,
                ServiceRecoveryPolicy = new WindowsServiceRecoveryPolicy(
                    60,
                    new[] { 30000, 60000, 60000 }),
                TerminalArguments = string.Empty,
                InstallerArguments = string.Empty,
                GracefulStopTimeoutMilliseconds = 30000,
                GeneratedArtifactFileNames = new List<string>
                {
                    "ca.crt",
                    "ca.pem",
                    "server.crt",
                    "server.pem"
                },
                ListenerUsesDualStackIpv6Wildcard = true
            };
            profile.TagProductionFacts();
            return profile;
        }

        private void TagProductionFacts()
        {
            Provenance["Version"] = CapabilityFactProvenance.OfficialPackage;
            Provenance["InstalledProductName"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["InstallRoot"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["ControllerRelativePath"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["ControllerBinary"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["Installer"] = CapabilityFactProvenance.OfficialPackage;
            Provenance["InstallerArguments"] = CapabilityFactProvenance.OfficialPackage;
            Provenance["ProfileEnvironmentKey"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["VendorProfileRelativePath"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["VendorConfigFileName"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["TerminalMode"] = CapabilityFactProvenance.SelfDocumentingCli;
            Provenance["ServiceAccountName"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["ServiceDependencies"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["ServiceStartMode"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["ServiceErrorControl"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["ServiceSidType"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["ServiceRecoveryPolicy"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["GeneratedArtifactFileNames"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["ListenerUsesDualStackIpv6Wildcard"] = CapabilityFactProvenance.PrivateBlackBox;
        }
    }
}
