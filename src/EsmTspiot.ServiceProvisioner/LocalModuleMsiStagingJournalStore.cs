using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiStagingJournalStore
    {
        internal const string JournalFileName = "staging.json";
        internal const string JournalTempFileName = "staging.tmp";

        private readonly string _machineRoot;
        private readonly string _securityRoot;
        private readonly string _root;
        private readonly IPathSafety _pathSafety;

        internal LocalModuleMsiStagingJournalStore(
            string machineRoot,
            IPathSafety pathSafety)
        {
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            _machineRoot = Path.GetFullPath(machineRoot)
                .TrimEnd(Path.DirectorySeparatorChar);
            _securityRoot = Path.GetPathRoot(_machineRoot);
            _root = Path.Combine(
                _machineRoot,
                "Operations",
                "LocalModuleMsi");
            _pathSafety = pathSafety;
        }

        internal string RootPath
        {
            get { return _root; }
        }

        internal string GetOperationRoot(string operationId)
        {
            ValidateOperationId(operationId);
            return Path.Combine(_root, operationId.ToLowerInvariant());
        }

        internal void Write(LocalModuleMsiStagingManifest manifest)
        {
            Validate(manifest);
            string operationRoot = GetOperationRoot(manifest.OperationId);
            ValidateIdentity(manifest, operationRoot);
            EnsureProtectedRoot(operationRoot);
            string path = Path.Combine(operationRoot, JournalFileName);
            EnsureSafe(path);
            if (File.Exists(path))
            {
                LocalModuleMsiStagingManifest existing = Read(
                    manifest.OperationId);
                if (!string.Equals(
                        existing.OwnershipNonce,
                        manifest.OwnershipNonce,
                        StringComparison.Ordinal) ||
                    manifest.Stage < existing.Stage)
                    throw new InvalidDataException(
                        "MSI staging journal ownership or stage mismatch.");
            }
            WriteAtomic(path, AtomicJsonFile.Serialize(manifest));
            _pathSafety.EnsureProtectedReadOnlyFile(path, null);
            EnsureSafe(path);
        }

        internal LocalModuleMsiStagingManifest Read(string operationId)
        {
            string operationRoot = GetOperationRoot(operationId);
            RejectReparsePoint(operationRoot);
            string path = Path.Combine(operationRoot, JournalFileName);
            RepairExpectedAcl(operationRoot, path);
            EnsureSafe(path);
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(
                        typeof(LocalModuleMsiStagingManifest));
                LocalModuleMsiStagingManifest manifest =
                    (LocalModuleMsiStagingManifest)serializer.ReadObject(stream);
                Validate(manifest);
                ValidateIdentity(manifest, operationRoot);
                return manifest;
            }
        }

        internal IList<LocalModuleMsiStagingManifest> ReadAll()
        {
            List<LocalModuleMsiStagingManifest> result =
                new List<LocalModuleMsiStagingManifest>();
            if (!Directory.Exists(_root)) return result;
            RejectReparsePoint(_root);
            EnsureProtectedBase();
            EnsureSafe(_root);
            string[] rootFiles = Directory.GetFiles(_root);
            if (rootFiles.Length != 0)
                throw new InvalidDataException(
                    "MSI staging root contains an unexpected file.");
            string[] directories = Directory.GetDirectories(_root);
            for (int index = 0; index < directories.Length; index++)
            {
                string operationId = Path.GetFileName(directories[index]);
                ValidateOperationId(operationId);
                result.Add(Read(operationId));
            }
            return result;
        }

        private void EnsureProtectedRoot(string operationRoot)
        {
            EnsureProtectedBase();
            _pathSafety.EnsureProtectedDirectory(
                operationRoot,
                ProtectedDirectoryKind.InstallerStaging,
                null,
                null);
        }

        private void EnsureProtectedBase()
        {
            _pathSafety.EnsureProtectedDirectory(
                _machineRoot,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            string operations = Path.Combine(_machineRoot, "Operations");
            _pathSafety.EnsureProtectedDirectory(
                operations,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            _pathSafety.EnsureProtectedDirectory(
                _root,
                ProtectedDirectoryKind.InstallerStaging,
                null,
                null);
        }

        private static void WriteAtomic(string path, byte[] payload)
        {
            string temporary = Path.Combine(
                Path.GetDirectoryName(path),
                JournalTempFileName);
            if (File.Exists(temporary)) File.Delete(temporary);
            try
            {
                using (FileStream stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(temporary, path, null, true);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private void RepairExpectedAcl(string operationRoot, string journalPath)
        {
            EnsureProtectedRoot(operationRoot);
            if (File.Exists(journalPath))
                _pathSafety.EnsureProtectedReadOnlyFile(journalPath, null);
        }

        private void EnsureSafe(string path)
        {
            ValidationResult machine = _pathSafety.ValidateProtected(
                _machineRoot,
                _securityRoot,
                null);
            if (!machine.IsValid)
                throw new InvalidDataException(machine.JoinMessages());
            ValidationResult value = _pathSafety.ValidateProtected(
                path,
                _machineRoot,
                null);
            if (!value.IsValid)
                throw new InvalidDataException(value.JoinMessages());
        }

        private static void ValidateIdentity(
            LocalModuleMsiStagingManifest manifest,
            string operationRoot)
        {
            string expectedRoot = LocalModuleMsiStagingManifest.PathIdentity(
                operationRoot);
            if (!string.Equals(
                    manifest.RootPath,
                    expectedRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    manifest.SourceCopyPath,
                    Path.Combine(expectedRoot, LocalModuleMsiWorkspace.SourceFileName),
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    manifest.OutputMsiPath,
                    Path.Combine(expectedRoot, LocalModuleMsiWorkspace.OutputFileName),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "MSI staging journal path identity mismatch.");
        }

        internal static void Validate(LocalModuleMsiStagingManifest manifest)
        {
            if (manifest == null ||
                manifest.SchemaVersion !=
                    LocalModuleMsiStagingManifest.CurrentSchemaVersion ||
                !string.Equals(
                    manifest.OwnershipMarker,
                    LocalModuleMsiStagingManifest.ExpectedOwnershipMarker,
                    StringComparison.Ordinal) ||
                !ProvisionerCommandLine.IsGuidN(manifest.OperationId) ||
                !LocalModuleManagedIdentity.IsLowerHex(
                    manifest.OwnershipNonce,
                    32) ||
                manifest.Stage < LocalModuleMsiStagingStage.JournalCreated ||
                manifest.Stage > LocalModuleMsiStagingStage.Installed ||
                string.IsNullOrWhiteSpace(manifest.RootPath) ||
                string.IsNullOrWhiteSpace(manifest.SourceCopyPath) ||
                string.IsNullOrWhiteSpace(manifest.OutputMsiPath) ||
                string.IsNullOrWhiteSpace(manifest.UpdatedUtc) ||
                !LocalModuleMsiWorkspace.HasExactExpectedEntries(
                    manifest.ExpectedEntries))
                throw new InvalidDataException("MSI staging journal is invalid.");
            if (manifest.Stage == LocalModuleMsiStagingStage.Installed)
            {
                Guid product;
                Guid package;
                if (!Guid.TryParse(manifest.ProductCode, out product) ||
                    !Guid.TryParse(manifest.PackageCode, out package) ||
                    !string.Equals(
                        manifest.ProductCode,
                        product.ToString("B"),
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        manifest.PackageCode,
                        package.ToString("B"),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Installed MSI staging identity is invalid.");
            }
        }

        private static void ValidateOperationId(string operationId)
        {
            if (!ProvisionerCommandLine.IsGuidN(operationId))
                throw new InvalidDataException(
                    "MSI staging operation id is invalid.");
        }

        private static void RejectReparsePoint(string path)
        {
            if (Directory.Exists(path) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "MSI staging path is a reparse point.");
        }
    }
}
