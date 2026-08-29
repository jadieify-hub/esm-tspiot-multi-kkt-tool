using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleAdministrativeExtractionPlan
    {
        internal LocalModuleAdministrativeExtractionPlan(
            string executablePath,
            IList<string> argumentTokens)
        {
            ExecutablePath = executablePath;
            ArgumentTokens = new List<string>(argumentTokens).AsReadOnly();
        }

        internal string ExecutablePath { get; private set; }
        internal IList<string> ArgumentTokens { get; private set; }
    }

    internal enum LocalModuleRuntimeMutationBoundary
    {
        StageCreated = 1,
        FilesCopied = 2,
        RuntimePromoted = 3,
        RuntimeManifestWritten = 4,
        RuntimeFilesDeleted = 5,
        RuntimeInventoryDeleted = 6
    }

    internal interface ILocalModuleMutationBoundary
    {
        void Reached(LocalModuleRuntimeMutationBoundary boundary);
    }

    internal sealed class NoopLocalModuleMutationBoundary :
        ILocalModuleMutationBoundary
    {
        internal static readonly NoopLocalModuleMutationBoundary Instance =
            new NoopLocalModuleMutationBoundary();

        private NoopLocalModuleMutationBoundary()
        {
        }

        public void Reached(LocalModuleRuntimeMutationBoundary boundary)
        {
        }
    }

    internal sealed class LocalModuleVerifiedRuntimeImage
    {
        internal LocalModuleVerifiedRuntimeImage(
            string imageRoot,
            string capabilityId,
            string runtimeContractSha256,
            IEnumerable<LocalModuleRuntimeFile> files)
            : this(
                imageRoot,
                capabilityId,
                runtimeContractSha256,
                files,
                LocalModuleManagedIdentity.DeriveRuntimeDirectories(files))
        {
        }

        internal LocalModuleVerifiedRuntimeImage(
            string imageRoot,
            string capabilityId,
            string runtimeContractSha256,
            IEnumerable<LocalModuleRuntimeFile> files,
            IEnumerable<string> directories)
        {
            if (string.IsNullOrWhiteSpace(imageRoot) || !Path.IsPathRooted(imageRoot))
            {
                throw new ArgumentException("An absolute image root is required.", "imageRoot");
            }
            if (string.IsNullOrWhiteSpace(capabilityId))
            {
                throw new ArgumentException("Capability id is required.", "capabilityId");
            }
            if (!LocalModuleManagedIdentity.IsHex(runtimeContractSha256, 64))
            {
                throw new ArgumentException("Runtime contract hash is invalid.", "runtimeContractSha256");
            }
            ImageRoot = Path.GetFullPath(imageRoot).TrimEnd(Path.DirectorySeparatorChar);
            CapabilityId = capabilityId;
            RuntimeContractSha256 = runtimeContractSha256.ToLowerInvariant();
            List<LocalModuleRuntimeFile> copiedFiles =
                LocalModuleManagedIdentity.CopyAndSortFiles(files);
            ValidateFiles(copiedFiles);
            Files = new ReadOnlyCollection<LocalModuleRuntimeFile>(copiedFiles);
            Directories = new ReadOnlyCollection<string>(
                LocalModuleManagedIdentity.CopyAndSortDirectories(directories));
            InventorySha256 = ComputeInventorySha256(Files, Directories);
        }

        internal string ImageRoot { get; private set; }
        internal string CapabilityId { get; private set; }
        internal string RuntimeContractSha256 { get; private set; }
        internal IList<LocalModuleRuntimeFile> Files { get; private set; }
        internal IList<string> Directories { get; private set; }
        internal string InventorySha256 { get; private set; }

        private static void ValidateFiles(IList<LocalModuleRuntimeFile> files)
        {
            if (files.Count == 0)
            {
                throw new InvalidDataException("Runtime file inventory is empty.");
            }
            HashSet<string> seen =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < files.Count; index++)
            {
                LocalModuleRuntimeFile file = files[index];
                string relative = NormalizeRelative(file.RelativePath);
                if (!IsSafeRelativePath(relative) ||
                    file.ByteLength < 0 ||
                    !LocalModuleManagedIdentity.IsHex(file.Sha256, 64) ||
                    !seen.Add(relative))
                {
                    throw new InvalidDataException("Runtime file inventory is invalid.");
                }
                file.RelativePath = relative;
                file.Sha256 = file.Sha256.ToLowerInvariant();
            }
        }

        private static string ComputeInventorySha256(
            IList<LocalModuleRuntimeFile> files,
            IList<string> directories)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            for (int index = 0; index < directories.Count; index++)
            {
                text.Append("D\t").Append(directories[index].ToLowerInvariant()).Append('\n');
            }
            for (int index = 0; index < files.Count; index++)
            {
                text.Append("F\t")
                    .Append(NormalizeRelative(files[index].RelativePath).ToLowerInvariant())
                    .Append('\t')
                    .Append(files[index].ByteLength.ToString(CultureInfo.InvariantCulture))
                    .Append('\t')
                    .Append(files[index].Sha256.ToLowerInvariant())
                    .Append('\n');
            }
            return LocalModuleManagedIdentity.ComputeSha256(
                System.Text.Encoding.UTF8.GetBytes(text.ToString()));
        }

        internal static string NormalizeRelative(string value)
        {
            return LocalModuleManagedIdentity.NormalizeRelative(value);
        }

        internal static bool IsSafeRelativePath(string value)
        {
            return LocalModuleManagedIdentity.IsSafeRelativePath(value);
        }
    }

    internal sealed class LocalModuleRuntimeImageVerifier
    {
        private readonly IPathSafety _pathSafety;

        internal LocalModuleRuntimeImageVerifier(IPathSafety pathSafety)
        {
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            _pathSafety = pathSafety;
        }

        internal LocalModuleVerifiedRuntimeImage Verify(
            string imageRoot,
            LocalModuleCapabilityProfile capability)
        {
            if (capability == null) throw new ArgumentNullException("capability");
            string fullRoot = Path.GetFullPath(imageRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(fullRoot))
            {
                throw new DirectoryNotFoundException("Administrative image runtime was not found.");
            }
            EnsureSafe(fullRoot, fullRoot);
            if (!capability.UsesRelativeLayoutLauncherFallback)
            {
                throw new InvalidDataException(
                    "Capability does not confirm the official relative-layout launcher fallback.");
            }
            string installerErlIni = Path.Combine(
                fullRoot,
                @"erts-" + capability.ErtsVersion + @"\bin\erl.ini");
            EnsureSafe(installerErlIni, fullRoot);
            ValidateBlankInstallerErlIni(installerErlIni);
            for (int index = 0; index < capability.RequiredDirectories.Count; index++)
            {
                string requiredDirectory = Path.Combine(
                    fullRoot,
                    capability.RequiredDirectories[index]);
                EnsureSafe(requiredDirectory, fullRoot);
                if (!Directory.Exists(requiredDirectory))
                {
                    throw new InvalidDataException(
                        "Administrative image is missing a required runtime directory.");
                }
            }
            for (int index = 0; index < capability.RequiredFiles.Count; index++)
            {
                LocalModuleRequiredFile required = capability.RequiredFiles[index];
                string path = Path.Combine(fullRoot, required.RelativePath);
                EnsureSafe(path, fullRoot);
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length != required.ByteLength ||
                    !string.Equals(
                        LocalModuleManagedIdentity.ComputeFileSha256(path),
                        required.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Administrative image required-file fingerprint mismatch: " +
                        required.RelativePath + ".");
                }
            }

            List<string> observedDirectories;
            List<string> observedFiles;
            EnumerateSafeTree(fullRoot, out observedDirectories, out observedFiles);
            List<string> directories = new List<string>();
            for (int index = 0; index < observedDirectories.Count; index++)
            {
                string relative = GetRelativePath(fullRoot, observedDirectories[index]);
                if (LocalModuleRuntimeInstaller.ShouldCopyRelativePath(capability, relative))
                {
                    directories.Add(relative);
                }
            }

            List<LocalModuleRuntimeFile> files = new List<LocalModuleRuntimeFile>();
            for (int index = 0; index < observedFiles.Count; index++)
            {
                string relative = GetRelativePath(fullRoot, observedFiles[index]);
                if (!LocalModuleRuntimeInstaller.ShouldCopyRelativePath(capability, relative))
                {
                    continue;
                }
                FileInfo info = new FileInfo(observedFiles[index]);
                files.Add(new LocalModuleRuntimeFile(
                    relative,
                    info.Length,
                    LocalModuleManagedIdentity.ComputeFileSha256(observedFiles[index])));
            }
            if (files.Count == 0)
            {
                throw new InvalidDataException("Administrative image contains no runtime files.");
            }
            return new LocalModuleVerifiedRuntimeImage(
                fullRoot,
                capability.CapabilityId,
                capability.RuntimeContractSha256,
                files,
                directories);
        }

        private void EnsureSafe(string path, string root)
        {
            ValidationResult validation = _pathSafety.Validate(path, root);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
        }

        private void EnumerateSafeTree(
            string root,
            out List<string> directories,
            out List<string> files)
        {
            directories = new List<string>();
            files = new List<string>();
            Queue<string> pending = new Queue<string>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                string[] childDirectories = Directory.GetDirectories(current);
                for (int index = 0; index < childDirectories.Length; index++)
                {
                    EnsureSafe(childDirectories[index], root);
                    directories.Add(childDirectories[index]);
                    pending.Enqueue(childDirectories[index]);
                }
                string[] childFiles = Directory.GetFiles(current);
                for (int index = 0; index < childFiles.Length; index++)
                {
                    EnsureSafe(childFiles[index], root);
                    files.Add(childFiles[index]);
                }
            }
        }

        private static void ValidateBlankInstallerErlIni(string path)
        {
            if (!File.Exists(path))
            {
                throw new InvalidDataException(
                    "Administrative image does not contain the expected installer erl.ini.");
            }
            string normalized = File.ReadAllText(path)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim();
            string expected = "[erlang]\nBindir=\nProgname=erl\nRootdir=";
            if (!string.Equals(normalized, expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Installer erl.ini no longer matches the reviewed blank-path contract.");
            }
        }

        private static string GetRelativePath(string root, string path)
        {
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Runtime image entry escapes its root.");
            }
            return LocalModuleVerifiedRuntimeImage.NormalizeRelative(
                path.Substring(prefix.Length));
        }
    }

    internal sealed class LocalModuleRuntimeInstaller
    {
        private readonly LocalModuleManifestStore _manifests;
        private readonly IPathSafety _pathSafety;
        private readonly ILocalModuleMutationBoundary _mutationBoundary;

        internal LocalModuleRuntimeInstaller(
            LocalModuleManifestStore manifests,
            IPathSafety pathSafety,
            ILocalModuleMutationBoundary mutationBoundary)
        {
            if (manifests == null) throw new ArgumentNullException("manifests");
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            if (mutationBoundary == null) throw new ArgumentNullException("mutationBoundary");
            _manifests = manifests;
            _pathSafety = pathSafety;
            _mutationBoundary = mutationBoundary;
        }

        internal static bool ShouldCopyRelativePath(
            LocalModuleCapabilityProfile capability,
            string relativePath)
        {
            if (capability == null) throw new ArgumentNullException("capability");
            string normalized = LocalModuleVerifiedRuntimeImage.NormalizeRelative(relativePath);
            if (!LocalModuleVerifiedRuntimeImage.IsSafeRelativePath(normalized)) return false;
            for (int index = 0; index < capability.ExcludedRuntimeRelativePaths.Count; index++)
            {
                string excluded = LocalModuleVerifiedRuntimeImage.NormalizeRelative(
                    capability.ExcludedRuntimeRelativePaths[index]);
                if (string.Equals(normalized, excluded, StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(
                        excluded + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        internal static LocalModuleAdministrativeExtractionPlan
            BuildAdministrativeExtractionPlan(
                string lockedMsiPath,
                string targetDirectory,
                string logPath)
        {
            string msi = NormalizeAbsolutePath(lockedMsiPath, "lockedMsiPath");
            string target = NormalizeAbsolutePath(targetDirectory, "targetDirectory");
            string log = NormalizeAbsolutePath(logPath, "logPath");
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windows))
            {
                throw new InvalidOperationException("Windows directory could not be resolved.");
            }
            return new LocalModuleAdministrativeExtractionPlan(
                Path.Combine(windows, "System32", "msiexec.exe"),
                new[] { "/a", msi, "/qn", "TARGETDIR=" + target, "/l*v", log });
        }

        internal LocalModuleRuntimeManifest Install(
            VerifiedLocalModulePackage lockedPackage,
            LocalModuleInstallerSelection packageIdentity,
            LocalModuleCapabilityProfile capability,
            string operationId,
            string ownershipNonce)
        {
            if (lockedPackage == null) throw new ArgumentNullException("lockedPackage");
            if (packageIdentity == null) throw new ArgumentNullException("packageIdentity");
            if (!string.Equals(
                    Path.GetFullPath(lockedPackage.FullPath),
                    Path.GetFullPath(packageIdentity.SourcePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Locked MSI path no longer matches the request.");
            }
            string imageRoot = _manifests.GetAdministrativeImageRoot(operationId);
            string logPath = _manifests.GetAdministrativeImageLogPath(operationId);
            string stagingContainer = Path.GetDirectoryName(imageRoot);
            EnsureProtectedStaging(stagingContainer);
            if (Directory.Exists(imageRoot))
            {
                DeleteTree(imageRoot, stagingContainer);
            }
            Directory.CreateDirectory(imageRoot);
            EnsureProtectedStaging(imageRoot);
            LocalModuleAdministrativeExtractionPlan plan =
                BuildAdministrativeExtractionPlan(
                    lockedPackage.FullPath,
                    imageRoot,
                    logPath);
            RunAdministrativeExtraction(plan);
            string vendorRoot = Path.Combine(imageRoot, "Program Files", "Regime");
            LocalModuleVerifiedRuntimeImage image =
                new LocalModuleRuntimeImageVerifier(_pathSafety).Verify(
                    vendorRoot,
                    capability);
            LocalModuleRuntimeManifest result = InstallVerifiedImage(
                image,
                packageIdentity,
                capability,
                operationId,
                ownershipNonce);
            DeleteTree(stagingContainer, _manifests.MachineRoot);
            return result;
        }

        internal LocalModuleRuntimeManifest InstallVerifiedImage(
            LocalModuleVerifiedRuntimeImage image,
            LocalModuleInstallerSelection package,
            LocalModuleCapabilityProfile capability,
            string operationId,
            string ownershipNonce)
        {
            ValidateInstallInputs(image, package, capability, operationId, ownershipNonce);
            EnsureRuntimeSecurityChain();
            string runtimeId = LocalModuleManagedIdentity.CreateRuntimeId(
                capability.CapabilityId,
                package.Sha256);
            string finalRoot = _manifests.GetRuntimeRoot(runtimeId);
            string stageRoot = _manifests.GetRuntimeStagingRoot(runtimeId, operationId);
            LocalModuleRuntimeManifest existing;
            if (_manifests.TryReadRuntime(runtimeId, out existing))
            {
                EnsureSameRuntime(existing, image, ownershipNonce);
                VerifyRuntimeTree(existing.RuntimeRoot, image, true);
                if (Directory.Exists(stageRoot))
                {
                    throw new InvalidDataException(
                        "Completed runtime has an unexpected staging directory.");
                }
                IList<LocalModuleOperationJournal> pending =
                    _manifests.OperationJournals.ReadForSubject(
                        LocalModuleOperationSubject.Runtime,
                        runtimeId);
                for (int index = 0; index < pending.Count; index++)
                {
                    if (!string.Equals(
                            pending[index].OperationId,
                            operationId,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "A different runtime operation is still pending.");
                    }
                }
                DeleteMatchingJournalIfPresent(operationId, runtimeId, ownershipNonce);
                return existing;
            }

            LocalModuleOperationJournal journal;
            if (!_manifests.OperationJournals.TryRead(
                    operationId,
                    LocalModuleOperationSubject.Runtime,
                    runtimeId,
                    out journal))
            {
                IList<LocalModuleOperationJournal> foreign =
                    _manifests.OperationJournals.ReadForSubject(
                        LocalModuleOperationSubject.Runtime,
                        runtimeId);
                if (foreign.Count != 0 || Directory.Exists(finalRoot))
                {
                    throw new InvalidDataException(
                        "An unowned or concurrently changing runtime already exists.");
                }
                journal = LocalModuleOperationJournal.CreateRuntimeInstall(
                    operationId,
                    runtimeId,
                    ownershipNonce,
                    capability.CapabilityId,
                    image.InventorySha256);
                _manifests.OperationJournals.Write(journal);
            }
            else
            {
                ValidateRecoveryJournal(journal, image, ownershipNonce, capability);
            }

            if (Directory.Exists(finalRoot))
            {
                if (Directory.Exists(stageRoot) ||
                    journal.Stage < LocalModuleOperationStage.FilesCopied)
                {
                    throw new InvalidDataException(
                        "Final runtime exists before the owned promotion checkpoint.");
                }
                VerifyRuntimeTree(finalRoot, image, true);
                LocalModuleRuntimeManifest recovered = LocalModuleRuntimeManifest.Create(
                    package,
                    capability,
                    finalRoot,
                    ownershipNonce,
                    image.Files,
                    image.Directories);
                _manifests.WriteRuntime(recovered);
                _mutationBoundary.Reached(
                    LocalModuleRuntimeMutationBoundary.RuntimeManifestWritten);
                UpdateJournal(journal, LocalModuleOperationStage.ManifestWritten);
                _manifests.OperationJournals.Delete(
                    operationId,
                    LocalModuleOperationSubject.Runtime,
                    runtimeId);
                return recovered;
            }

            if (Directory.Exists(stageRoot))
            {
                DeleteTree(stageRoot, _manifests.RuntimeContainerRoot);
            }
            EnsureRuntimeContainer();
            Directory.CreateDirectory(stageRoot);
            EnsureSafeUnder(stageRoot, _manifests.RuntimeContainerRoot, false);
            _mutationBoundary.Reached(LocalModuleRuntimeMutationBoundary.StageCreated);
            UpdateJournal(journal, LocalModuleOperationStage.StageCreated);

            CopyVerifiedImage(image, stageRoot);
            VerifyRuntimeTree(stageRoot, image, false);
            ProtectRuntimeTree(stageRoot);
            VerifyRuntimeTree(stageRoot, image, true);
            _mutationBoundary.Reached(LocalModuleRuntimeMutationBoundary.FilesCopied);
            UpdateJournal(journal, LocalModuleOperationStage.FilesCopied);

            Directory.Move(stageRoot, finalRoot);
            EnsureSafeUnder(finalRoot, _manifests.RuntimeContainerRoot, true);
            _mutationBoundary.Reached(LocalModuleRuntimeMutationBoundary.RuntimePromoted);
            UpdateJournal(journal, LocalModuleOperationStage.RuntimePromoted);

            LocalModuleRuntimeManifest manifest = LocalModuleRuntimeManifest.Create(
                package,
                capability,
                finalRoot,
                ownershipNonce,
                image.Files,
                image.Directories);
            _manifests.WriteRuntime(manifest);
            _mutationBoundary.Reached(
                LocalModuleRuntimeMutationBoundary.RuntimeManifestWritten);
            UpdateJournal(journal, LocalModuleOperationStage.ManifestWritten);
            _manifests.OperationJournals.Delete(
                operationId,
                LocalModuleOperationSubject.Runtime,
                runtimeId);
            return manifest;
        }

        internal void DeleteUnreferencedRuntime(
            string runtimeId,
            string ownershipNonce)
        {
            EnsureRuntimeSecurityChain();
            LocalModuleRuntimeManifest manifest;
            if (!_manifests.TryReadRuntime(runtimeId, out manifest))
            {
                CompleteAbsentRuntimeDeletion(runtimeId, ownershipNonce);
                return;
            }
            if (!string.Equals(
                    manifest.OwnershipNonce,
                    ownershipNonce,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Runtime ownership nonce does not match.");
            }
            if (_manifests.CountRuntimeReferences(runtimeId) != 0)
            {
                throw new InvalidOperationException("Runtime still has managed references.");
            }
            LocalModuleVerifiedRuntimeImage expected =
                new LocalModuleVerifiedRuntimeImage(
                    manifest.RuntimeRoot,
                    manifest.CapabilityId,
                    manifest.RequiredFileContractSha256,
                    manifest.Files,
                    manifest.Directories);
            bool alreadyDeleting =
                manifest.State == LocalModuleRuntimeLifecycleState.Deleting;
            if (!alreadyDeleting)
            {
                VerifyRuntimeTree(manifest.RuntimeRoot, expected, true);
                manifest.State = LocalModuleRuntimeLifecycleState.Deleting;
                manifest.UpdatedUtc = DateTime.UtcNow.ToString(
                    "o",
                    CultureInfo.InvariantCulture);
                _manifests.WriteRuntime(manifest);
            }
            IList<LocalModuleOperationJournal> pending =
                _manifests.OperationJournals.ReadForSubject(
                    LocalModuleOperationSubject.Runtime,
                    runtimeId);
            LocalModuleOperationJournal deletionJournal = null;
            for (int index = 0; index < pending.Count; index++)
            {
                if (pending[index].Stage != LocalModuleOperationStage.Deleting ||
                    deletionJournal != null ||
                    !string.Equals(
                        pending[index].OwnershipNonce,
                        ownershipNonce,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        pending[index].ExpectedInventorySha256,
                        expected.InventorySha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Runtime has an incompatible pending operation.");
                }
                deletionJournal = pending[index];
            }
            if (deletionJournal == null)
            {
                VerifyRuntimeTreeSubset(manifest.RuntimeRoot, expected, true);
                deletionJournal = LocalModuleOperationJournal.CreateRuntimeDeletion(
                    Guid.NewGuid().ToString("N"),
                    runtimeId,
                    ownershipNonce,
                    manifest.CapabilityId,
                    expected.InventorySha256);
                _manifests.OperationJournals.Write(deletionJournal);
            }
            else
            {
                VerifyRuntimeTreeSubset(manifest.RuntimeRoot, expected, true);
            }
            DeleteTree(manifest.RuntimeRoot, _manifests.RuntimeContainerRoot);
            if (Directory.Exists(manifest.RuntimeRoot))
            {
                throw new IOException("Runtime removal could not be confirmed.");
            }
            _mutationBoundary.Reached(
                LocalModuleRuntimeMutationBoundary.RuntimeFilesDeleted);
            _manifests.OperationJournals.Delete(
                deletionJournal.OperationId,
                LocalModuleOperationSubject.Runtime,
                runtimeId);
            _manifests.DeleteRuntimeManifest(runtimeId, ownershipNonce);
            _mutationBoundary.Reached(
                LocalModuleRuntimeMutationBoundary.RuntimeInventoryDeleted);
        }

        private void CompleteAbsentRuntimeDeletion(
            string runtimeId,
            string ownershipNonce)
        {
            if (Directory.Exists(_manifests.GetRuntimeRoot(runtimeId)))
            {
                throw new InvalidDataException(
                    "A runtime directory without an owned manifest cannot be removed.");
            }
            IList<LocalModuleOperationJournal> pending =
                _manifests.OperationJournals.ReadForSubject(
                    LocalModuleOperationSubject.Runtime,
                    runtimeId);
            for (int index = 0; index < pending.Count; index++)
            {
                if (pending[index].Stage != LocalModuleOperationStage.Deleting ||
                    !string.Equals(
                        pending[index].OwnershipNonce,
                        ownershipNonce,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "An unfinished non-deletion runtime operation still exists.");
                }
                _manifests.OperationJournals.Delete(
                    pending[index].OperationId,
                    pending[index].Subject,
                    pending[index].SubjectId);
            }
        }

        private static void RunAdministrativeExtraction(
            LocalModuleAdministrativeExtractionPlan plan)
        {
            if (!File.Exists(plan.ExecutablePath))
            {
                throw new FileNotFoundException(
                    "System msiexec.exe was not found.",
                    plan.ExecutablePath);
            }
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = plan.ExecutablePath,
                Arguments = JoinArguments(plan.ArgumentTokens),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process process = Process.Start(start))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("msiexec could not be started.");
                }
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidDataException(
                        "Administrative MSI extraction failed with exit code " +
                        process.ExitCode.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }
        }

        private void CopyVerifiedImage(
            LocalModuleVerifiedRuntimeImage image,
            string destinationRoot)
        {
            for (int index = 0; index < image.Directories.Count; index++)
            {
                string destination = Path.Combine(
                    destinationRoot,
                    image.Directories[index]);
                EnsureSafeUnder(destination, destinationRoot, false);
                Directory.CreateDirectory(destination);
            }
            for (int index = 0; index < image.Files.Count; index++)
            {
                LocalModuleRuntimeFile file = image.Files[index];
                string source = Path.Combine(image.ImageRoot, file.RelativePath);
                string destination = Path.Combine(destinationRoot, file.RelativePath);
                EnsureSafeUnder(source, image.ImageRoot, false);
                EnsureSafeUnder(destination, destinationRoot, false);
                VerifyFile(source, file);
                string directory = Path.GetDirectoryName(destination);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.Copy(source, destination, false);
                VerifyFile(destination, file);
            }
        }

        private void ProtectRuntimeTree(string root)
        {
            List<string> directories;
            List<string> files;
            EnumerateSafeTree(root, false, out directories, out files);
            _pathSafety.EnsureProtectedDirectory(
                root,
                ProtectedDirectoryKind.Runtime,
                null,
                null);
            for (int index = 0; index < directories.Count; index++)
            {
                EnsureSafeUnder(directories[index], root, false);
                _pathSafety.EnsureProtectedDirectory(
                    directories[index],
                    ProtectedDirectoryKind.Runtime,
                    null,
                    null);
            }
            for (int index = 0; index < files.Count; index++)
            {
                EnsureSafeUnder(files[index], root, false);
                _pathSafety.EnsureProtectedRuntimeFile(files[index]);
                File.SetAttributes(files[index], File.GetAttributes(files[index]) |
                    FileAttributes.ReadOnly);
            }
        }

        private void VerifyRuntimeTree(
            string root,
            LocalModuleVerifiedRuntimeImage expected,
            bool requireProtected)
        {
            EnsureSafeUnder(root, _manifests.RuntimeContainerRoot, requireProtected);
            Dictionary<string, LocalModuleRuntimeFile> expectedFiles =
                new Dictionary<string, LocalModuleRuntimeFile>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < expected.Files.Count; index++)
            {
                expectedFiles.Add(
                    LocalModuleVerifiedRuntimeImage.NormalizeRelative(
                        expected.Files[index].RelativePath),
                    expected.Files[index]);
            }
            HashSet<string> observedDirectories =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> directories;
            List<string> files;
            EnumerateSafeTree(root, requireProtected, out directories, out files);
            for (int index = 0; index < directories.Count; index++)
            {
                observedDirectories.Add(GetRelativePath(root, directories[index]));
            }
            HashSet<string> expectedDirectories = new HashSet<string>(
                expected.Directories,
                StringComparer.OrdinalIgnoreCase);
            if (!observedDirectories.SetEquals(expectedDirectories))
            {
                throw new InvalidDataException("Runtime directory inventory mismatch.");
            }
            if (files.Count != expectedFiles.Count)
            {
                throw new InvalidDataException("Runtime file inventory mismatch.");
            }
            for (int index = 0; index < files.Count; index++)
            {
                EnsureSafeUnder(files[index], root, requireProtected);
                string relative = GetRelativePath(root, files[index]);
                LocalModuleRuntimeFile expectedFile;
                if (!expectedFiles.TryGetValue(relative, out expectedFile))
                {
                    throw new InvalidDataException("Runtime contains an unknown file.");
                }
                VerifyFile(files[index], expectedFile);
            }
        }

        private void VerifyRuntimeTreeSubset(
            string root,
            LocalModuleVerifiedRuntimeImage expected,
            bool requireProtected)
        {
            if (!Directory.Exists(root)) return;
            EnsureSafeUnder(root, _manifests.RuntimeContainerRoot, requireProtected);
            HashSet<string> expectedDirectories = new HashSet<string>(
                expected.Directories,
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, LocalModuleRuntimeFile> expectedFiles =
                new Dictionary<string, LocalModuleRuntimeFile>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < expected.Files.Count; index++)
            {
                expectedFiles.Add(
                    LocalModuleVerifiedRuntimeImage.NormalizeRelative(
                        expected.Files[index].RelativePath),
                    expected.Files[index]);
            }
            List<string> directories;
            List<string> files;
            EnumerateSafeTree(root, requireProtected, out directories, out files);
            for (int index = 0; index < directories.Count; index++)
            {
                if (!expectedDirectories.Contains(GetRelativePath(root, directories[index])))
                {
                    throw new InvalidDataException(
                        "Partially removed runtime contains an unknown directory.");
                }
            }
            for (int index = 0; index < files.Count; index++)
            {
                EnsureSafeUnder(files[index], root, requireProtected);
                string relative = GetRelativePath(root, files[index]);
                LocalModuleRuntimeFile expectedFile;
                if (!expectedFiles.TryGetValue(relative, out expectedFile))
                {
                    throw new InvalidDataException(
                        "Partially removed runtime contains an unknown file.");
                }
                VerifyFile(files[index], expectedFile);
            }
        }

        private void DeleteTree(string root, string requiredRoot)
        {
            EnsureSafeUnder(root, requiredRoot, false);
            if (!Directory.Exists(root)) return;
            List<string> directories;
            List<string> files;
            EnumerateSafeTree(root, false, out directories, out files);
            for (int index = 0; index < files.Count; index++)
            {
                EnsureSafeUnder(files[index], root, false);
                File.SetAttributes(files[index], FileAttributes.Normal);
                File.Delete(files[index]);
            }
            directories.Sort(delegate(string left, string right)
            {
                return right.Length.CompareTo(left.Length);
            });
            for (int index = 0; index < directories.Count; index++)
            {
                EnsureSafeUnder(directories[index], root, false);
                Directory.Delete(directories[index], false);
            }
            EnsureSafeUnder(root, requiredRoot, false);
            Directory.Delete(root, false);
        }

        private void EnsureRuntimeContainer()
        {
            _pathSafety.EnsureProtectedDirectory(
                _manifests.RuntimeContainerRoot,
                ProtectedDirectoryKind.InstallerStaging,
                null,
                null);
            EnsureSafeUnder(
                _manifests.RuntimeContainerRoot,
                _manifests.RuntimeSecurityRoot,
                true);
        }

        private void EnsureRuntimeSecurityChain()
        {
            EnsureSafeUnder(
                _manifests.RuntimeContainerRoot,
                _manifests.RuntimeSecurityRoot,
                true);
        }

        private void EnsureProtectedStaging(string path)
        {
            _pathSafety.EnsureProtectedDirectory(
                _manifests.MachineRoot,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            EnsureSafeUnder(
                _manifests.MachineRoot,
                _manifests.MachineSecurityRoot,
                true);
            _pathSafety.EnsureProtectedDirectory(
                path,
                ProtectedDirectoryKind.InstallerStaging,
                null,
                null);
            EnsureSafeUnder(path, _manifests.MachineRoot, true);
        }

        private void EnsureSafeUnder(string path, string root, bool requireProtected)
        {
            ValidationResult validation = requireProtected
                ? _pathSafety.ValidateProtected(path, root, null)
                : _pathSafety.Validate(path, root);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
        }

        private void EnumerateSafeTree(
            string root,
            bool requireProtected,
            out List<string> directories,
            out List<string> files)
        {
            directories = new List<string>();
            files = new List<string>();
            Queue<string> pending = new Queue<string>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                string[] childDirectories = Directory.GetDirectories(current);
                for (int index = 0; index < childDirectories.Length; index++)
                {
                    EnsureSafeUnder(childDirectories[index], root, requireProtected);
                    directories.Add(childDirectories[index]);
                    pending.Enqueue(childDirectories[index]);
                }
                string[] childFiles = Directory.GetFiles(current);
                for (int index = 0; index < childFiles.Length; index++)
                {
                    EnsureSafeUnder(childFiles[index], root, requireProtected);
                    files.Add(childFiles[index]);
                }
            }
        }

        private static void VerifyFile(string path, LocalModuleRuntimeFile expected)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length != expected.ByteLength ||
                !string.Equals(
                    LocalModuleManagedIdentity.ComputeFileSha256(path),
                    expected.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Runtime file fingerprint mismatch: " + expected.RelativePath + ".");
            }
        }

        private static void ValidateInstallInputs(
            LocalModuleVerifiedRuntimeImage image,
            LocalModuleInstallerSelection package,
            LocalModuleCapabilityProfile capability,
            string operationId,
            string ownershipNonce)
        {
            if (image == null) throw new ArgumentNullException("image");
            if (package == null) throw new ArgumentNullException("package");
            if (capability == null) throw new ArgumentNullException("capability");
            if (!ProvisionerCommandLine.IsGuidN(operationId) ||
                !LocalModuleManagedIdentity.IsLowerHex(ownershipNonce, 32) ||
                !string.Equals(
                    image.CapabilityId,
                    capability.CapabilityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    image.RuntimeContractSha256,
                    capability.RuntimeContractSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    package.ProductVersion,
                    capability.ProductVersion,
                    StringComparison.Ordinal) ||
                !LocalModuleManagedIdentity.IsHex(package.Sha256, 64) ||
                image.Files.Count == 0)
            {
                throw new InvalidDataException("Verified runtime install input is invalid.");
            }
        }

        private static void EnsureSameRuntime(
            LocalModuleRuntimeManifest manifest,
            LocalModuleVerifiedRuntimeImage image,
            string ownershipNonce)
        {
            if (!string.Equals(
                    manifest.OwnershipNonce,
                    ownershipNonce,
                    StringComparison.Ordinal) ||
                manifest.State != LocalModuleRuntimeLifecycleState.Ready ||
                !string.Equals(
                    manifest.CapabilityId,
                    image.CapabilityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    ComputeInventorySha256(manifest.Files, manifest.Directories),
                    image.InventorySha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Existing runtime ownership or inventory mismatch.");
            }
        }

        private static string ComputeInventorySha256(
            IList<LocalModuleRuntimeFile> files,
            IList<string> directories)
        {
            LocalModuleVerifiedRuntimeImage synthetic =
                new LocalModuleVerifiedRuntimeImage(
                    Path.GetPathRoot(Environment.SystemDirectory),
                    "inventory-only",
                    new string('0', 64),
                    files,
                    directories);
            return synthetic.InventorySha256;
        }

        private static void ValidateRecoveryJournal(
            LocalModuleOperationJournal journal,
            LocalModuleVerifiedRuntimeImage image,
            string ownershipNonce,
            LocalModuleCapabilityProfile capability)
        {
            if (!string.Equals(journal.OwnershipNonce, ownershipNonce, StringComparison.Ordinal) ||
                !string.Equals(
                    journal.CapabilityId,
                    capability.CapabilityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    journal.ExpectedInventorySha256,
                    image.InventorySha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Pending runtime operation does not match the verified retry.");
            }
        }

        private void UpdateJournal(
            LocalModuleOperationJournal journal,
            LocalModuleOperationStage stage)
        {
            journal.Stage = stage;
            journal.UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            _manifests.OperationJournals.Write(journal);
        }

        private void DeleteMatchingJournalIfPresent(
            string operationId,
            string runtimeId,
            string ownershipNonce)
        {
            LocalModuleOperationJournal journal;
            if (_manifests.OperationJournals.TryRead(
                    operationId,
                    LocalModuleOperationSubject.Runtime,
                    runtimeId,
                    out journal))
            {
                if (!string.Equals(
                        journal.OwnershipNonce,
                        ownershipNonce,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Pending runtime ownership mismatch.");
                }
                _manifests.OperationJournals.Delete(
                    operationId,
                    LocalModuleOperationSubject.Runtime,
                    runtimeId);
            }
        }

        private static string GetRelativePath(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Runtime entry escapes its root.");
            }
            return LocalModuleVerifiedRuntimeImage.NormalizeRelative(
                fullPath.Substring(prefix.Length));
        }

        private static string JoinArguments(IList<string> tokens)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            for (int index = 0; index < tokens.Count; index++)
            {
                if (index > 0) text.Append(' ');
                text.Append(WindowsCommandLine.QuoteArgument(tokens[index]));
            }
            return text.ToString();
        }

        private static string NormalizeAbsolutePath(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value) ||
                value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 ||
                value.IndexOf('\n') >= 0)
            {
                throw new ArgumentException("A safe absolute path is required.", parameterName);
            }
            return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
        }
    }
}
