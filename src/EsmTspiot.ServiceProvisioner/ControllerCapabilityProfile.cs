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
        internal string InstallRoot { get; private set; }
        internal string ControllerRelativePath { get; set; }
        internal TrustedFileExpectation ControllerBinary { get; private set; }
        internal TrustedFileExpectation Installer { get; private set; }
        internal string ProfileEnvironmentKey { get; private set; }
        internal string VendorProfileRelativePath { get; private set; }
        internal string VendorConfigFileName { get; private set; }
        internal IDictionary<string, CapabilityFactProvenance> Provenance { get; private set; }

        internal static ControllerCapabilityProfile SupportedVersion1632()
        {
            string programFiles64 = Environment.GetEnvironmentVariable("ProgramW6432");
            if (string.IsNullOrWhiteSpace(programFiles64))
            {
                programFiles64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            }

            ControllerCapabilityProfile profile = new ControllerCapabilityProfile
            {
                Version = "1.6.3.2",
                InstallRoot = Path.Combine(programFiles64, "ESP", "LMController"),
                ControllerRelativePath = Path.Combine("bin", "lmcontroller.exe"),
                ProfileEnvironmentKey = "ProgramData",
                VendorProfileRelativePath = Path.Combine("ESP", "lmcontroller"),
                VendorConfigFileName = "config.yml",
                ControllerBinary = new TrustedFileExpectation
                {
                    FileName = "lmcontroller.exe",
                    ByteLength = 14647536,
                    Sha256 = "9ce34999ea965e01d8328895bb1776e7b44edabf72fc51bee121ec5091746214",
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
                    FileName = "esm-lm-controller_1.6.3.2-windows-setup.exe",
                    ByteLength = 11094696,
                    Sha256 = "822e047dbef62cbdbe2cf1ae22c457f43930c574fbcf265170987b9c7eae91e7",
                    FileVersion = "1.6.3.2",
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
                InstallRoot = Path.GetFullPath(installRoot),
                ControllerRelativePath = controllerRelativePath,
                ControllerBinary = controllerBinary.Clone(),
                Installer = installer.Clone(),
                ProfileEnvironmentKey = profileEnvironmentKey,
                VendorProfileRelativePath = Path.Combine("ESP", "lmcontroller"),
                VendorConfigFileName = "config.yml"
            };
            profile.TagProductionFacts();
            return profile;
        }

        private void TagProductionFacts()
        {
            Provenance["Version"] = CapabilityFactProvenance.OfficialPackage;
            Provenance["InstallRoot"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["ControllerRelativePath"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["ControllerBinary"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["Installer"] = CapabilityFactProvenance.OfficialPackage;
            Provenance["ProfileEnvironmentKey"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["VendorProfileRelativePath"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["VendorConfigFileName"] = CapabilityFactProvenance.PrivateBlackBox;
            Provenance["TerminalMode"] = CapabilityFactProvenance.SelfDocumentingCli;
        }
    }
}
