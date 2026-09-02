using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiManifestStore :
        ILocalModuleMsiManifestRepository,
        ILocalModuleMsiLifecycleJournalStore
    {
        private const string ManifestFileName = "manifest.json";
        private const string LifecycleFileName = "lifecycle.json";
        private readonly string _machineRoot;
        private readonly string _securityRoot;
        private readonly string _root;
        private readonly IPathSafety _pathSafety;

        internal LocalModuleMsiManifestStore(
            string machineRoot,
            IPathSafety pathSafety)
        {
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            _machineRoot = Path.GetFullPath(machineRoot)
                .TrimEnd(Path.DirectorySeparatorChar);
            _securityRoot = Path.GetPathRoot(_machineRoot);
            _root = Path.Combine(_machineRoot, "LocalModuleMsiProducts");
            _pathSafety = pathSafety;
        }

        internal string GetManifestPath(string inn)
        {
            if (!LocalModuleMsiIdentity.IsInn(inn))
                throw new ArgumentException("Local-module INN is invalid.", "inn");
            return Path.Combine(_root, inn, ManifestFileName);
        }

        internal void Write(LocalModuleMsiManifest manifest)
        {
            LocalModuleMsiManifest.Validate(manifest);
            string path = GetManifestPath(manifest.Inn);
            EnsureProtected(Path.GetDirectoryName(path));
            if (File.Exists(path))
            {
                LocalModuleMsiManifest existing = Read(manifest.Inn);
                if (!string.Equals(
                        existing.OwnershipNonce,
                        manifest.OwnershipNonce,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Local-module MSI manifest ownership mismatch.");
            }
            manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
            manifest.ManifestSha256 =
                LocalModuleMsiManifest.ComputeSha256(manifest);
            AtomicJsonFile.Write(path, AtomicJsonFile.Serialize(manifest));
            _pathSafety.EnsureProtectedReadOnlyFile(path, null);
            EnsureSafe(path);
            LocalModuleMsiManifest.Validate(ReadRaw(path));
        }

        internal LocalModuleMsiManifest Read(string inn)
        {
            string path = GetManifestPath(inn);
            if (!File.Exists(path)) return null;
            EnsureSafe(path);
            LocalModuleMsiManifest manifest = ReadRaw(path);
            LocalModuleMsiManifest.Validate(manifest);
            if (!string.Equals(manifest.Inn, inn, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Local-module MSI manifest identity mismatch.");
            return manifest;
        }

        LocalModuleMsiManifest ILocalModuleMsiManifestRepository.Read(
            string inn)
        {
            return Read(inn);
        }

        void ILocalModuleMsiManifestRepository.Write(
            LocalModuleMsiManifest manifest)
        {
            Write(manifest);
        }

        internal IList<LocalModuleMsiManifest> ReadAll()
        {
            List<LocalModuleMsiManifest> result =
                new List<LocalModuleMsiManifest>();
            if (!Directory.Exists(_root)) return result;
            EnsureSafe(_root);
            string[] directories = Directory.GetDirectories(_root);
            for (int index = 0; index < directories.Length; index++)
            {
                string inn = Path.GetFileName(directories[index]);
                if (!LocalModuleMsiIdentity.IsInn(inn))
                    throw new InvalidDataException(
                        "Local-module MSI store contains an unknown directory.");
                LocalModuleMsiManifest manifest = Read(inn);
                if (manifest != null) result.Add(manifest);
            }
            result.Sort(delegate(
                LocalModuleMsiManifest left,
                LocalModuleMsiManifest right)
            {
                return left.CloneOrdinal.CompareTo(right.CloneOrdinal);
            });
            return result;
        }

        IList<LocalModuleMsiManifest>
            ILocalModuleMsiManifestRepository.ReadAll()
        {
            return ReadAll();
        }

        internal void Delete(string inn, string expectedManifestSha256)
        {
            LocalModuleMsiManifest manifest = Read(inn);
            if (manifest == null || !string.Equals(
                    manifest.ManifestSha256,
                    expectedManifestSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Local-module MSI manifest fingerprint mismatch.");
            string path = GetManifestPath(inn);
            File.Delete(path);
            DeleteEmptyItemRoot(inn);
        }

        void ILocalModuleMsiManifestRepository.Delete(
            string inn,
            string expectedManifestSha256)
        {
            Delete(inn, expectedManifestSha256);
        }

        internal LocalModuleMsiLifecycleJournal ReadJournal(string inn)
        {
            string path = GetLifecyclePath(inn);
            if (!File.Exists(path)) return null;
            EnsureSafe(path);
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                LocalModuleMsiLifecycleJournal journal =
                    (LocalModuleMsiLifecycleJournal)
                    new DataContractJsonSerializer(
                        typeof(LocalModuleMsiLifecycleJournal))
                        .ReadObject(stream);
                LocalModuleMsiLifecycleJournal.Validate(journal);
                if (!string.Equals(journal.Inn, inn,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Local-module lifecycle journal identity mismatch.");
                return journal;
            }
        }

        LocalModuleMsiLifecycleJournal
            ILocalModuleMsiLifecycleJournalStore.Read(string inn)
        {
            return ReadJournal(inn);
        }

        internal void WriteJournal(LocalModuleMsiLifecycleJournal journal)
        {
            LocalModuleMsiLifecycleJournal.Validate(journal);
            string path = GetLifecyclePath(journal.Inn);
            EnsureProtected(Path.GetDirectoryName(path));
            LocalModuleMsiLifecycleJournal existing = ReadJournal(journal.Inn);
            if (existing != null &&
                !string.Equals(existing.OwnershipNonce,
                    journal.OwnershipNonce, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Local-module lifecycle journal ownership mismatch.");
            AtomicJsonFile.Write(path, AtomicJsonFile.Serialize(journal));
            _pathSafety.EnsureProtectedReadOnlyFile(path, null);
            EnsureSafe(path);
        }

        void ILocalModuleMsiLifecycleJournalStore.Write(
            LocalModuleMsiLifecycleJournal journal)
        {
            WriteJournal(journal);
        }

        internal void DeleteJournal(
            string inn,
            string operationId,
            string ownershipNonce)
        {
            LocalModuleMsiLifecycleJournal journal = ReadJournal(inn);
            if (journal == null) return;
            if (!string.Equals(journal.OperationId, operationId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(journal.OwnershipNonce, ownershipNonce,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Local-module lifecycle journal ownership mismatch.");
            File.Delete(GetLifecyclePath(inn));
            DeleteEmptyItemRoot(inn);
        }

        void ILocalModuleMsiLifecycleJournalStore.Delete(
            string inn,
            string operationId,
            string ownershipNonce)
        {
            DeleteJournal(inn, operationId, ownershipNonce);
        }

        private void EnsureProtected(string itemRoot)
        {
            _pathSafety.EnsureProtectedDirectory(
                _machineRoot,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            _pathSafety.EnsureProtectedDirectory(
                _root,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            _pathSafety.EnsureProtectedDirectory(
                itemRoot,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            EnsureSafe(itemRoot);
        }

        private string GetLifecyclePath(string inn)
        {
            return Path.Combine(
                Path.GetDirectoryName(GetManifestPath(inn)),
                LifecycleFileName);
        }

        private void DeleteEmptyItemRoot(string inn)
        {
            string directory = Path.GetDirectoryName(GetManifestPath(inn));
            if (Directory.Exists(directory) &&
                Directory.GetFileSystemEntries(directory).Length == 0)
                Directory.Delete(directory, false);
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

        private static LocalModuleMsiManifest ReadRaw(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                return (LocalModuleMsiManifest)
                    new DataContractJsonSerializer(
                        typeof(LocalModuleMsiManifest)).ReadObject(stream);
            }
        }
    }
}
