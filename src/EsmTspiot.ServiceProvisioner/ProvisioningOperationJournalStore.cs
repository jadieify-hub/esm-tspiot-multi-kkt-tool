using System;
using System.Collections.Generic;
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
            string path = GetPath(journal.OperationId, journal.KktSerial);
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

        internal ProvisioningOperationJournal Read(string operationId, string kktSerial)
        {
            string path = GetPath(operationId, kktSerial);
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

        internal void Delete(string operationId, string kktSerial)
        {
            ProvisioningOperationJournal journal = Read(operationId, kktSerial);
            string path = GetPath(journal.OperationId, journal.KktSerial);
            File.Delete(path);
            string directory = Path.GetDirectoryName(path);
            if (Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory, false);
            }
        }

        internal IList<ProvisioningOperationJournal> ReadForKkt(string kktSerial)
        {
            LmServiceIdentity.CreateName(kktSerial);
            List<ProvisioningOperationJournal> journals =
                new List<ProvisioningOperationJournal>();
            if (!Directory.Exists(_root))
            {
                return journals;
            }
            EnsureSafe(_root);
            string fileName = LmServiceIdentity.CreateName(kktSerial) + ".json";
            string[] operationDirectories = Directory.GetDirectories(_root);
            for (int index = 0; index < operationDirectories.Length; index++)
            {
                string operationId = Path.GetFileName(operationDirectories[index]);
                if (!ProvisionerCommandLine.IsGuidN(operationId))
                {
                    throw new InvalidDataException("Operations store contains an unknown directory.");
                }
                string path = Path.Combine(operationDirectories[index], fileName);
                EnsureSafe(path);
                if (File.Exists(path))
                {
                    journals.Add(Read(operationId, kktSerial));
                }
            }
            journals.Sort(delegate(
                ProvisioningOperationJournal left,
                ProvisioningOperationJournal right)
            {
                return string.CompareOrdinal(left.UpdatedUtc, right.UpdatedUtc);
            });
            return journals;
        }

        internal void DeleteForKkt(string kktSerial)
        {
            IList<ProvisioningOperationJournal> journals = ReadForKkt(kktSerial);
            for (int index = 0; index < journals.Count; index++)
            {
                Delete(journals[index].OperationId, kktSerial);
            }
        }

        private string GetPath(string operationId, string kktSerial)
        {
            if (!ProvisionerCommandLine.IsGuidN(operationId))
            {
                throw new ArgumentException("Operation id must be a 32-character GUID.", "operationId");
            }
            string serviceName = LmServiceIdentity.CreateName(kktSerial);
            return Path.Combine(
                _root,
                operationId.ToLowerInvariant(),
                serviceName + ".json");
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
                string.IsNullOrWhiteSpace(journal.UpdatedUtc) ||
                journal.Stage < LmProvisioningJournalStage.Preparing ||
                journal.Stage > LmProvisioningJournalStage.Failed)
            {
                throw new InvalidDataException("Operation journal is invalid.");
            }
            LmServiceIdentity.CreateName(journal.KktSerial);
        }
    }
}
