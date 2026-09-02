using System;
using System.Collections.Generic;
using System.IO;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiWorkspace : IDisposable
    {
        internal const string SourceFileName = "source.msi";
        internal const string OutputFileName = "output.msi";

        private static readonly string[] AllowedEntries =
        {
            LocalModuleMsiStagingJournalStore.JournalFileName,
            LocalModuleMsiStagingJournalStore.JournalTempFileName,
            SourceFileName,
            OutputFileName,
            OutputFileName + ".cabwork",
            OutputFileName + ".verify"
        };

        private readonly LocalModuleMsiStagingJournalStore _store;
        private readonly IPathSafety _pathSafety;
        private LocalModuleMsiStagingManifest _manifest;
        private bool _cleaned;

        private LocalModuleMsiWorkspace(
            LocalModuleMsiStagingJournalStore store,
            IPathSafety pathSafety,
            LocalModuleMsiStagingManifest manifest)
        {
            _store = store;
            _pathSafety = pathSafety;
            _manifest = manifest;
        }

        internal string RootPath
        {
            get { return _manifest.RootPath; }
        }

        internal string SourceCopyPath
        {
            get { return _manifest.SourceCopyPath; }
        }

        internal string OutputMsiPath
        {
            get { return _manifest.OutputMsiPath; }
        }

        internal static LocalModuleMsiWorkspace Create(
            string machineRoot,
            string operationId,
            string ownershipNonce,
            IPathSafety pathSafety)
        {
            LocalModuleMsiStagingJournalStore store =
                new LocalModuleMsiStagingJournalStore(machineRoot, pathSafety);
            string root = store.GetOperationRoot(operationId);
            if (Directory.Exists(root) || File.Exists(root))
                throw new IOException("MSI staging operation already exists.");
            string source = Path.Combine(root, SourceFileName);
            string output = Path.Combine(root, OutputFileName);
            LocalModuleMsiStagingManifest manifest =
                LocalModuleMsiStagingManifest.Create(
                    operationId,
                    ownershipNonce,
                    root,
                    source,
                    output,
                    AllowedEntries);
            store.Write(manifest);
            return new LocalModuleMsiWorkspace(store, pathSafety, manifest);
        }

        internal void CopySource(string sourcePath)
        {
            RequireStage(LocalModuleMsiStagingStage.JournalCreated);
            string source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source))
                throw new FileNotFoundException(
                    "Local-module source MSI was not found.",
                    source);
            File.Copy(source, SourceCopyPath, false);
            _pathSafety.EnsureProtectedReadOnlyFile(SourceCopyPath, null);
            Advance(LocalModuleMsiStagingStage.SourceCopied);
        }

        internal void MarkTransformed()
        {
            RequireStage(LocalModuleMsiStagingStage.SourceCopied);
            if (!File.Exists(OutputMsiPath))
                throw new FileNotFoundException(
                    "Transformed local-module MSI was not found.",
                    OutputMsiPath);
            _pathSafety.EnsureProtectedReadOnlyFile(OutputMsiPath, null);
            Advance(LocalModuleMsiStagingStage.Transformed);
        }

        internal void MarkVerified()
        {
            RequireExistingOutput(LocalModuleMsiStagingStage.Transformed);
            Advance(LocalModuleMsiStagingStage.Verified);
        }

        internal void MarkInstallerReturned()
        {
            RequireExistingOutput(LocalModuleMsiStagingStage.Verified);
            Advance(LocalModuleMsiStagingStage.InstallerReturned);
        }

        internal void MarkManifestPersisted()
        {
            RequireStage(LocalModuleMsiStagingStage.InstallerReturned);
            Advance(LocalModuleMsiStagingStage.ManifestPersisted);
        }

        internal void MarkInstalled(string productCode, string packageCode)
        {
            RequireStage(LocalModuleMsiStagingStage.ManifestPersisted);
            Guid product;
            Guid package;
            if (!Guid.TryParseExact(productCode, "B", out product) ||
                !Guid.TryParseExact(packageCode, "B", out package))
                throw new InvalidDataException(
                    "Installed local-module MSI identity is invalid.");
            _manifest.ProductCode = product.ToString("B").ToUpperInvariant();
            _manifest.PackageCode = package.ToString("B").ToUpperInvariant();
            Advance(LocalModuleMsiStagingStage.Installed);
        }

        internal void Cleanup()
        {
            if (_cleaned) return;
            LocalModuleMsiStagingManifest persisted =
                _store.Read(_manifest.OperationId);
            if (!string.Equals(
                    persisted.OwnershipNonce,
                    _manifest.OwnershipNonce,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "MSI staging ownership nonce mismatch.");
            CleanupOwned(_store, _pathSafety, persisted);
            _cleaned = true;
        }

        internal static void RecoverPending(
            string machineRoot,
            IPathSafety pathSafety)
        {
            LocalModuleMsiStagingJournalStore store =
                new LocalModuleMsiStagingJournalStore(machineRoot, pathSafety);
            IList<LocalModuleMsiStagingManifest> pending = store.ReadAll();
            for (int index = 0; index < pending.Count; index++)
                CleanupOwned(store, pathSafety, pending[index]);
        }

        public void Dispose()
        {
            Cleanup();
        }

        private void RequireExistingOutput(LocalModuleMsiStagingStage stage)
        {
            RequireStage(stage);
            if (!File.Exists(OutputMsiPath))
                throw new InvalidDataException(
                    "Transformed local-module MSI disappeared from staging.");
        }

        private void RequireStage(LocalModuleMsiStagingStage stage)
        {
            if (_cleaned || _manifest.Stage != stage)
                throw new InvalidOperationException(
                    "Local-module MSI staging operation is out of sequence.");
        }

        private void Advance(LocalModuleMsiStagingStage stage)
        {
            _manifest.Advance(stage);
            _store.Write(_manifest);
        }

        private static void CleanupOwned(
            LocalModuleMsiStagingJournalStore store,
            IPathSafety pathSafety,
            LocalModuleMsiStagingManifest manifest)
        {
            LocalModuleMsiStagingJournalStore.Validate(manifest);
            string root = store.GetOperationRoot(manifest.OperationId);
            if (!string.Equals(
                    LocalModuleMsiStagingManifest.PathIdentity(root),
                    manifest.RootPath,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "MSI staging cleanup path mismatch.");
            EnsureSafe(pathSafety, root, store.RootPath);
            string[] entries = Directory.GetFileSystemEntries(root);
            for (int index = 0; index < entries.Length; index++)
            {
                if (!IsAllowedEntry(Path.GetFileName(entries[index])))
                    throw new InvalidDataException(
                        "MSI staging contains an unexpected entry.");
                EnsureTreeHasNoReparsePoint(entries[index]);
            }
            RepairAcl(pathSafety, root);
            DeleteTree(root);
        }

        private static bool IsAllowedEntry(string name)
        {
            for (int index = 0; index < AllowedEntries.Length; index++)
                if (string.Equals(
                        name,
                        AllowedEntries[index],
                        StringComparison.Ordinal))
                    return true;
            return false;
        }

        internal static bool HasExactExpectedEntries(IList<string> entries)
        {
            if (entries == null || entries.Count != AllowedEntries.Length)
                return false;
            for (int index = 0; index < AllowedEntries.Length; index++)
                if (!string.Equals(
                        entries[index],
                        AllowedEntries[index],
                        StringComparison.Ordinal))
                    return false;
            return true;
        }

        private static void EnsureTreeHasNoReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "MSI staging contains a reparse point.");
            if (!Directory.Exists(path)) return;
            string[] children = Directory.GetFileSystemEntries(path);
            for (int index = 0; index < children.Length; index++)
                EnsureTreeHasNoReparsePoint(children[index]);
        }

        private static void RepairAcl(IPathSafety pathSafety, string path)
        {
            if (File.Exists(path))
            {
                pathSafety.EnsureProtectedReadOnlyFile(path, null);
                return;
            }
            pathSafety.EnsureProtectedDirectory(
                path,
                ProtectedDirectoryKind.InstallerStaging,
                null,
                null);
            string[] children = Directory.GetFileSystemEntries(path);
            for (int index = 0; index < children.Length; index++)
                RepairAcl(pathSafety, children[index]);
        }

        private static void DeleteTree(string root)
        {
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            for (int index = 0; index < files.Length; index++)
            {
                File.SetAttributes(files[index], FileAttributes.Normal);
                File.Delete(files[index]);
            }
            string[] directories = Directory.GetDirectories(
                root,
                "*",
                SearchOption.AllDirectories);
            Array.Sort(directories, delegate(string left, string right) {
                return right.Length.CompareTo(left.Length);
            });
            for (int index = 0; index < directories.Length; index++)
                Directory.Delete(directories[index], false);
            Directory.Delete(root, false);
        }

        private static void EnsureSafe(
            IPathSafety pathSafety,
            string path,
            string requiredRoot)
        {
            ValidationResult validation = pathSafety.Validate(
                path,
                requiredRoot);
            if (!validation.IsValid)
                throw new InvalidDataException(validation.JoinMessages());
        }
    }
}
