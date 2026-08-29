using System;
using System.IO;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class VerifiedLocalModulePackage : IDisposable
    {
        private FileStream _sourceLock;

        internal VerifiedLocalModulePackage(
            string fullPath,
            FileStream sourceLock,
            WindowsInstallerPackageMetadata metadata)
        {
            FullPath = fullPath;
            _sourceLock = sourceLock;
            Metadata = metadata.Clone();
        }

        internal string FullPath { get; private set; }
        internal WindowsInstallerPackageMetadata Metadata { get; private set; }

        public void Dispose()
        {
            if (_sourceLock != null)
            {
                _sourceLock.Dispose();
                _sourceLock = null;
            }
        }
    }

    internal sealed class LocalModulePackageVerifier
    {
        private readonly LocalModuleInstallerSelection _expected;
        private readonly IWindowsInstallerPackageReader _packageReader;
        private readonly IFileTrustVerifier _trustVerifier;
        private readonly IPathSafety _pathSafety;

        internal LocalModulePackageVerifier(
            LocalModuleInstallerSelection expected,
            IWindowsInstallerPackageReader packageReader,
            IFileTrustVerifier trustVerifier,
            IPathSafety pathSafety)
        {
            if (expected == null) throw new ArgumentNullException("expected");
            if (packageReader == null) throw new ArgumentNullException("packageReader");
            if (trustVerifier == null) throw new ArgumentNullException("trustVerifier");
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            _expected = Clone(expected);
            _packageReader = packageReader;
            _trustVerifier = trustVerifier;
            _pathSafety = pathSafety;
        }

        internal static LocalModulePackageVerifier SupportedVersion2617()
        {
            return new LocalModulePackageVerifier(
                CreateSupportedIdentity(),
                new WindowsInstallerPackageReader(),
                new WinTrustVerifier(),
                new PathSafety());
        }

        internal static LocalModuleInstallerSelection CreateSupportedIdentity()
        {
            return SupportedLocalModulePackageIdentity.Create(
                string.Empty,
                true);
        }

        internal VerifiedLocalModulePackage VerifyAndLock(
            LocalModuleInstallerSelection selection)
        {
            ValidationResult shape =
                ProvisioningRequestValidator.ValidateLocalModuleInstallerSelection(selection);
            if (!shape.IsValid)
            {
                throw new InvalidDataException(shape.JoinMessages());
            }
            ValidationResult expected =
                ProvisioningRequestValidator.ValidateLocalModuleInstallerSelection(
                    WithSourcePath(_expected, selection.SourcePath),
                    selection);
            if (!expected.IsValid)
            {
                throw new InvalidDataException(expected.JoinMessages());
            }

            string sourcePath = Path.GetFullPath(selection.SourcePath);
            ValidationResult pathValidation = _pathSafety.Validate(
                sourcePath,
                Path.GetDirectoryName(sourcePath));
            if (!pathValidation.IsValid)
            {
                throw new InvalidDataException(pathValidation.JoinMessages());
            }

            FileStream sourceLock = null;
            try
            {
                sourceLock = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                WindowsInstallerPackageMetadata metadata = _packageReader.Read(sourcePath);
                ValidateMetadata(selection, metadata);

                FileTrustResult trust = _trustVerifier.Verify(
                    sourcePath,
                    CreateTrustExpectation(selection));
                if (!trust.IsTrusted || trust.Observed == null)
                {
                    throw new InvalidDataException(
                        trust.ErrorMessage ?? "Local-module MSI trust verification failed.");
                }
                ValidateObservedTrust(selection, trust.Observed);

                VerifiedLocalModulePackage result = new VerifiedLocalModulePackage(
                    sourcePath,
                    sourceLock,
                    metadata);
                sourceLock = null;
                return result;
            }
            finally
            {
                if (sourceLock != null)
                {
                    sourceLock.Dispose();
                }
            }
        }

        private static void ValidateMetadata(
            LocalModuleInstallerSelection selection,
            WindowsInstallerPackageMetadata observed)
        {
            if (observed == null ||
                !string.Equals(selection.ProductName, observed.ProductName, StringComparison.Ordinal) ||
                !string.Equals(selection.ProductVersion, observed.ProductVersion, StringComparison.Ordinal) ||
                !string.Equals(selection.ProductCode, observed.ProductCode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(selection.UpgradeCode, observed.UpgradeCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "MSI ProductName, ProductVersion, ProductCode or UpgradeCode does not match the confirmed package.");
            }
        }

        private static void ValidateObservedTrust(
            LocalModuleInstallerSelection selection,
            TrustedFileExpectation observed)
        {
            if (!string.Equals(selection.FileName, observed.FileName, StringComparison.Ordinal) ||
                selection.ByteLength != observed.ByteLength ||
                !EsmTspiot.Shared.Services.CanonicalLmPlanHasher.FixedTimeEqualsHex(
                    selection.Sha256,
                    observed.Sha256) ||
                !SignerSubjectMatches(
                    selection.SignerSubject,
                    observed.SignerSubject) ||
                !string.Equals(
                    selection.SignerThumbprint,
                    observed.SignerThumbprint,
                    StringComparison.OrdinalIgnoreCase) ||
                !observed.RequireCodeSigningEku)
            {
                throw new InvalidDataException(
                    "Local-module MSI hash, size or Authenticode signer does not match the confirmed package.");
            }
        }

        private static bool SignerSubjectMatches(
            string expected,
            string observed)
        {
            if (string.Equals(expected, observed, StringComparison.Ordinal))
            {
                return true;
            }
            return string.Equals(
                    expected,
                    SupportedLocalModulePackageIdentity.SignerSubject,
                    StringComparison.Ordinal) &&
                SupportedLocalModulePackageIdentity
                    .MatchesSignerSubject(observed);
        }

        private static TrustedFileExpectation CreateTrustExpectation(
            LocalModuleInstallerSelection selection)
        {
            return new TrustedFileExpectation
            {
                FileName = selection.FileName,
                ByteLength = selection.ByteLength,
                Sha256 = selection.Sha256,
                FileVersion = string.Empty,
                ProductVersion = string.Empty,
                ProductName = string.Empty,
                CompanyName = string.Empty,
                Machine = PeMachine.Unknown,
                SignerSubject = selection.SignerSubject,
                SignerThumbprint = selection.SignerThumbprint,
                RequireCodeSigningEku = true
            };
        }

        private static LocalModuleInstallerSelection WithSourcePath(
            LocalModuleInstallerSelection source,
            string sourcePath)
        {
            LocalModuleInstallerSelection result = Clone(source);
            result.SourcePath = sourcePath;
            return result;
        }

        private static LocalModuleInstallerSelection Clone(
            LocalModuleInstallerSelection source)
        {
            return new LocalModuleInstallerSelection
            {
                SourcePath = source.SourcePath,
                FileName = source.FileName,
                ByteLength = source.ByteLength,
                Sha256 = source.Sha256,
                ProductName = source.ProductName,
                ProductVersion = source.ProductVersion,
                ProductCode = source.ProductCode,
                UpgradeCode = source.UpgradeCode,
                SignerSubject = source.SignerSubject,
                SignerThumbprint = source.SignerThumbprint,
                LicenseNoticeAccepted = source.LicenseNoticeAccepted
            };
        }
    }
}
