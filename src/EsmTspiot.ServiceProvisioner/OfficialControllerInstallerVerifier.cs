using System;
using System.IO;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LockedInstallerArtifact : IDisposable
    {
        private FileStream _sourceLock;
        private FileStream _stagedLock;
        private readonly string _stagingRoot;
        private readonly string _operationDirectory;

        internal LockedInstallerArtifact(
            string fullPath,
            FileStream sourceLock,
            FileStream stagedLock,
            string stagingRoot,
            string operationDirectory)
        {
            FullPath = fullPath;
            _sourceLock = sourceLock;
            _stagedLock = stagedLock;
            _stagingRoot = stagingRoot;
            _operationDirectory = operationDirectory;
        }

        internal string FullPath { get; private set; }

        public void Dispose()
        {
            if (_stagedLock != null)
            {
                _stagedLock.Dispose();
                _stagedLock = null;
            }
            if (_sourceLock != null)
            {
                _sourceLock.Dispose();
                _sourceLock = null;
            }

            TryDeleteDerivedDirectory(FullPath, _operationDirectory, _stagingRoot);
        }

        private static void TryDeleteDerivedDirectory(
            string stagedPath,
            string directory,
            string root)
        {
            try
            {
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                if (fullDirectory.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(fullDirectory))
                {
                    string fullStagedPath = Path.GetFullPath(stagedPath);
                    if (string.Equals(
                            Path.GetDirectoryName(fullStagedPath),
                            fullDirectory.TrimEnd(Path.DirectorySeparatorChar),
                            StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(fullStagedPath) &&
                        (File.GetAttributes(fullStagedPath) & FileAttributes.ReparsePoint) == 0)
                    {
                        File.Delete(fullStagedPath);
                    }
                    if (Directory.GetFileSystemEntries(fullDirectory).Length == 0)
                    {
                        Directory.Delete(fullDirectory, false);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal sealed class OfficialControllerInstallerVerifier
    {
        private readonly ControllerCapabilityProfile _profile;
        private readonly IFileTrustVerifier _trustVerifier;
        private readonly IPathSafety _pathSafety;
        private readonly string _stagingRoot;
        private readonly string _operationId;

        internal OfficialControllerInstallerVerifier(
            ControllerCapabilityProfile profile,
            IFileTrustVerifier trustVerifier,
            IPathSafety pathSafety,
            string stagingRoot,
            string operationId)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (trustVerifier == null) throw new ArgumentNullException("trustVerifier");
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            _profile = profile;
            _trustVerifier = trustVerifier;
            _pathSafety = pathSafety;
            _stagingRoot = Path.GetFullPath(stagingRoot);
            if (!ProvisionerCommandLine.IsGuidN(operationId))
            {
                throw new ArgumentException("Operation id must be a 32-character GUID.", "operationId");
            }
            _operationId = operationId.ToLowerInvariant();
        }

        internal LockedInstallerArtifact VerifyStageAndLock(LmControllerInstallerSelection selection)
        {
            ValidateDisplayedSelection(selection);
            string sourcePath = Path.GetFullPath(selection.SourcePath);
            ValidationResult sourcePathValidation = _pathSafety.Validate(
                sourcePath,
                Path.GetDirectoryName(sourcePath));
            if (!sourcePathValidation.IsValid)
            {
                throw new InvalidDataException(sourcePathValidation.JoinMessages());
            }

            FileStream sourceLock = null;
            FileStream stagedLock = null;
            string operationDirectory = Path.Combine(_stagingRoot, _operationId);
            string stagedPath = Path.Combine(operationDirectory, _profile.Installer.FileName);
            try
            {
                sourceLock = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                FileTrustResult sourceTrust = _trustVerifier.Verify(sourcePath, _profile.Installer);
                if (!sourceTrust.IsTrusted || sourceTrust.Observed == null)
                {
                    throw new InvalidDataException(sourceTrust.ErrorMessage ?? "Installer source trust failed.");
                }
                ValidateObservedSelection(selection, sourceTrust.Observed);

                string applicationRoot = Directory.GetParent(_stagingRoot).FullName;
                if (Directory.Exists(applicationRoot))
                {
                    ValidationResult applicationRootValidation = _pathSafety.ValidateProtected(
                        applicationRoot,
                        applicationRoot,
                        null);
                    if (!applicationRootValidation.IsValid)
                    {
                        throw new InvalidDataException(applicationRootValidation.JoinMessages());
                    }
                }
                _pathSafety.EnsureProtectedDirectory(
                    applicationRoot,
                    ProtectedDirectoryKind.Operations,
                    null,
                    null);

                if (Directory.Exists(_stagingRoot))
                {
                    ValidationResult existingStagingValidation = _pathSafety.ValidateProtected(
                        _stagingRoot,
                        _stagingRoot,
                        null);
                    if (!existingStagingValidation.IsValid)
                    {
                        throw new InvalidDataException(existingStagingValidation.JoinMessages());
                    }
                }
                if (Directory.Exists(operationDirectory))
                {
                    ValidationResult existingOperationValidation = _pathSafety.ValidateProtected(
                        operationDirectory,
                        _stagingRoot,
                        null);
                    if (!existingOperationValidation.IsValid)
                    {
                        throw new InvalidDataException(existingOperationValidation.JoinMessages());
                    }
                }

                _pathSafety.EnsureProtectedDirectory(
                    _stagingRoot,
                    ProtectedDirectoryKind.InstallerStaging,
                    null,
                    null);
                _pathSafety.EnsureProtectedDirectory(
                    operationDirectory,
                    ProtectedDirectoryKind.InstallerStaging,
                    null,
                    null);
                ValidationResult stagingValidation = _pathSafety.ValidateProtected(
                    stagedPath,
                    _stagingRoot,
                    null);
                if (!stagingValidation.IsValid)
                {
                    throw new InvalidDataException(stagingValidation.JoinMessages());
                }

                using (FileStream stagedWriter = new FileStream(
                    stagedPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    sourceLock.Position = 0;
                    sourceLock.CopyTo(stagedWriter);
                    stagedWriter.Flush(true);
                }

                stagedLock = new FileStream(
                    stagedPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                stagingValidation = _pathSafety.ValidateProtected(stagedPath, _stagingRoot, null);
                if (!stagingValidation.IsValid)
                {
                    throw new InvalidDataException(stagingValidation.JoinMessages());
                }

                FileTrustResult stagedTrust = _trustVerifier.Verify(stagedPath, _profile.Installer);
                if (!stagedTrust.IsTrusted || stagedTrust.Observed == null)
                {
                    throw new InvalidDataException(stagedTrust.ErrorMessage ?? "Staged installer trust failed.");
                }
                ValidateObservedSelection(selection, stagedTrust.Observed);

                LockedInstallerArtifact artifact = new LockedInstallerArtifact(
                    stagedPath,
                    sourceLock,
                    stagedLock,
                    _stagingRoot,
                    operationDirectory);
                sourceLock = null;
                stagedLock = null;
                return artifact;
            }
            catch
            {
                if (stagedLock != null)
                {
                    stagedLock.Dispose();
                }
                if (sourceLock != null)
                {
                    sourceLock.Dispose();
                }
                TryDeleteFailedStage(stagedPath, operationDirectory);
                throw;
            }
        }

        private void ValidateDisplayedSelection(LmControllerInstallerSelection selection)
        {
            if (selection == null)
            {
                throw new InvalidDataException("Installer selection is missing.");
            }
            LmControllerInstallerSelection expected = ToSelection(
                selection.SourcePath,
                _profile.Installer,
                selection.StopManagedInstancesWarningAccepted);
            ValidationResult comparison = ProvisioningRequestValidator.ValidateInstallerSelection(
                expected,
                selection);
            if (!comparison.IsValid)
            {
                throw new InvalidDataException(comparison.JoinMessages());
            }
        }

        private static void ValidateObservedSelection(
            LmControllerInstallerSelection selected,
            TrustedFileExpectation observed)
        {
            LmControllerInstallerSelection observedSelection = ToSelection(
                selected.SourcePath,
                observed,
                true);
            ValidationResult comparison = ProvisioningRequestValidator.ValidateInstallerSelection(
                selected,
                observedSelection);
            if (!comparison.IsValid)
            {
                throw new InvalidDataException(comparison.JoinMessages());
            }
        }

        private static LmControllerInstallerSelection ToSelection(
            string path,
            TrustedFileExpectation file,
            bool warningAccepted)
        {
            return new LmControllerInstallerSelection
            {
                SourcePath = path,
                FileName = file.FileName,
                ByteLength = file.ByteLength,
                Sha256 = file.Sha256,
                FileVersion = file.FileVersion,
                ProductVersion = file.ProductVersion,
                SignerSubject = file.SignerSubject,
                SignerThumbprint = file.SignerThumbprint,
                StopManagedInstancesWarningAccepted = warningAccepted
            };
        }

        private void TryDeleteFailedStage(string stagedPath, string operationDirectory)
        {
            string fullRoot = _stagingRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullDirectory = Path.GetFullPath(operationDirectory).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!fullDirectory.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                if (File.Exists(stagedPath))
                {
                    File.Delete(stagedPath);
                }
                if (Directory.Exists(operationDirectory) &&
                    Directory.GetFileSystemEntries(operationDirectory).Length == 0)
                {
                    Directory.Delete(operationDirectory, false);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
