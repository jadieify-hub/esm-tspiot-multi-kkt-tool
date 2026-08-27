using System;
using System.IO;
using System.Runtime.Serialization.Json;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class ProvisioningOperationJournalStore
    {
        private readonly string _appDataRoot;
        private readonly string _root;
        private readonly IPathSafety _pathSafety;

        internal ProvisioningOperationJournalStore(string appDataRoot, IPathSafety pathSafety)
        {
            _appDataRoot = Path.GetFullPath(appDataRoot);
            _root = Path.Combine(_appDataRoot, "Operations");
            _pathSafety = pathSafety ?? throw new ArgumentNullException("pathSafety");
        }

        internal void Write(ProvisioningOperationJournal journal)
        {
            Validate(journal);
            string path = GetPath(journal.OperationId);
            EnsureSafe(path);
            string directory = Path.GetDirectoryName(path);
            _pathSafety.EnsureProtectedDirectory(
                _appDataRoot,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            _pathSafety.EnsureProtectedDirectory(
                _root,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            _pathSafety.EnsureProtectedDirectory(
                directory,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            EnsureSafe(path);
            AtomicJsonFile.Write(path, AtomicJsonFile.Serialize(journal));
        }

        internal ProvisioningOperationJournal Read(string operationId)
        {
            string path = GetPath(operationId);
            EnsureSafe(path);
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(ProvisioningOperationJournal));
                ProvisioningOperationJournal journal =
                    (ProvisioningOperationJournal)serializer.ReadObject(stream);
                Validate(journal);
                if (!string.Equals(journal.OperationId, operationId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Journal identity does not match its derived directory.");
                }
                return journal;
            }
        }

        internal void Delete(string operationId)
        {
            ProvisioningOperationJournal journal = Read(operationId);
            string path = GetPath(journal.OperationId);
            File.Delete(path);
            string directory = Path.GetDirectoryName(path);
            if (Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory, false);
            }
        }

        private string GetPath(string operationId)
        {
            if (!ProvisionerCommandLine.IsGuidN(operationId))
            {
                throw new ArgumentException("Operation id must be a 32-character GUID.", "operationId");
            }
            return Path.Combine(_root, operationId.ToLowerInvariant(), "journal.json");
        }

        private void EnsureSafe(string path)
        {
            ValidationResult validation = _pathSafety.ValidateProtected(path, _appDataRoot, null);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
        }

        private static void Validate(ProvisioningOperationJournal journal)
        {
            if (journal == null || journal.SchemaVersion != 1 ||
                !ProvisionerCommandLine.IsGuidN(journal.OperationId) ||
                string.IsNullOrWhiteSpace(journal.UpdatedUtc))
            {
                throw new InvalidDataException("Operation journal is invalid.");
            }
            LmServiceIdentity.CreateName(journal.KktSerial);
        }
    }
}
