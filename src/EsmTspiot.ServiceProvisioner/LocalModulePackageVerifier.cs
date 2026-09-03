using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
            WindowsInstallerPackageMetadata metadata,
            LocalModuleMsiCapabilityProfile capabilityProfile,
            LocalModuleMsiDatabaseSnapshot databaseSnapshot)
        {
            FullPath = fullPath;
            _sourceLock = sourceLock;
            Metadata = metadata.Clone();
            CapabilityProfile = capabilityProfile;
            DatabaseSnapshot = databaseSnapshot;
        }

        internal string FullPath { get; private set; }
        internal WindowsInstallerPackageMetadata Metadata { get; private set; }
        internal LocalModuleMsiCapabilityProfile CapabilityProfile { get; private set; }
        internal LocalModuleMsiDatabaseSnapshot DatabaseSnapshot { get; private set; }

        public void Dispose()
        {
            if (_sourceLock != null)
            {
                _sourceLock.Dispose();
                _sourceLock = null;
            }
        }
    }

    /// <summary>
    /// Пакет опознаётся как ЛМ ЧЗ по подписи ЦРПТ, UpgradeCode и структуре, а
    /// не по номеру версии: новая минорная версия вендора принимается без
    /// правки приложения. Конкретный файл при этом остаётся зафиксированным:
    /// имя, размер, SHA-256 и отпечаток подписанта сверяются с тем, что видел
    /// оператор при выборе.
    /// </summary>
    internal sealed class LocalModulePackageVerifier
    {
        private readonly IWindowsInstallerPackageReader _packageReader;
        private readonly IFileTrustVerifier _trustVerifier;
        private readonly IPathSafety _pathSafety;
        private readonly ILocalModuleMsiCapabilityResolver _capabilityResolver;
        private readonly ILocalModuleMsiProfileReader _profileReader;

        internal LocalModulePackageVerifier(
            IWindowsInstallerPackageReader packageReader,
            IFileTrustVerifier trustVerifier,
            IPathSafety pathSafety,
            ILocalModuleMsiCapabilityResolver capabilityResolver,
            ILocalModuleMsiProfileReader profileReader)
        {
            if (packageReader == null) throw new ArgumentNullException("packageReader");
            if (trustVerifier == null) throw new ArgumentNullException("trustVerifier");
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            if (capabilityResolver == null)
                throw new ArgumentNullException("capabilityResolver");
            if (profileReader == null) throw new ArgumentNullException("profileReader");
            _packageReader = packageReader;
            _trustVerifier = trustVerifier;
            _pathSafety = pathSafety;
            _capabilityResolver = capabilityResolver;
            _profileReader = profileReader;
        }

        internal static LocalModulePackageVerifier Supported()
        {
            return new LocalModulePackageVerifier(
                new WindowsInstallerPackageReader(),
                new WinTrustVerifier(),
                new PathSafety(),
                new LocalModuleMsiCapabilityResolver(),
                new LocalModuleMsiProfileReader());
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
            ValidationResult vendor = LocalModulePackagePolicy.Evaluate(selection);
            if (!vendor.IsValid)
            {
                throw new InvalidDataException(
                    "Это не пакет ЛМ ЧЗ от ЦРПТ. " + vendor.JoinMessages());
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
                        trust.ErrorMessage ??
                        "Проверка подписи MSI ЛМ не пройдена.");
                }
                ValidateObservedTrust(selection, trust.Observed);

                LocalModuleMsiDatabaseSnapshot databaseSnapshot =
                    _profileReader.Read(sourcePath);
                LocalModuleMsiCapabilityProfile profile =
                    _capabilityResolver.Resolve(
                        metadata,
                        trust.Observed,
                        databaseSnapshot);
                IList<MsiProfileMismatch> mismatches =
                    databaseSnapshot.Compare(profile);
                if (mismatches.Count > 0)
                {
                    StringBuilder message = new StringBuilder(
                        "Структура MSI ЛМ разошлась с выведенным профилем: ");
                    for (int index = 0; index < mismatches.Count; index++)
                    {
                        if (index > 0) message.Append("; ");
                        message.Append(mismatches[index].ToString());
                    }
                    throw new InvalidDataException(message.ToString());
                }

                VerifiedLocalModulePackage result = new VerifiedLocalModulePackage(
                    sourcePath,
                    sourceLock,
                    metadata,
                    profile,
                    databaseSnapshot);
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

        // Значения сверяются с тем, что оператор видел при выборе файла: между
        // выбором и запуском с правами администратора файл меняться не должен.
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
                    "ProductName, ProductVersion, ProductCode или UpgradeCode " +
                    "выбранного MSI изменились после подтверждения.");
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
                    "Хеш, размер или подписант выбранного MSI ЛМ изменились " +
                    "после подтверждения.");
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
    }
}
