using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal enum LocalModuleOperationSubject
    {
        [EnumMember]
        Runtime = 1,
        [EnumMember]
        Instance = 2,
        [EnumMember]
        KktStack = 3
    }

    [DataContract]
    internal enum LocalModuleOperationStage
    {
        [EnumMember]
        Preparing = 1,
        [EnumMember]
        StageCreated = 2,
        [EnumMember]
        FilesCopied = 3,
        [EnumMember]
        RuntimePromoted = 4,
        [EnumMember]
        ManifestWritten = 5,
        [EnumMember]
        Deleting = 6,
        [EnumMember]
        CleanupPending = 7
    }

    [DataContract]
    internal sealed class LocalModuleOperationJournal
    {
        internal const int CurrentSchemaVersion = 1;
        internal const string ExpectedOwnershipMarker =
            "KRS.MultiKKT.LocalModule.Operation.v1";

        [DataMember(Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        internal string OwnershipMarker { get; set; }

        [DataMember(Order = 3)]
        internal string OperationId { get; set; }

        [DataMember(Order = 4)]
        internal LocalModuleOperationSubject Subject { get; set; }

        [DataMember(Order = 5)]
        internal string SubjectId { get; set; }

        [DataMember(Order = 6)]
        internal LocalModuleOperationStage Stage { get; set; }

        [DataMember(Order = 7)]
        internal string OwnershipNonce { get; set; }

        [DataMember(Order = 8)]
        internal string CapabilityId { get; set; }

        [DataMember(Order = 9)]
        internal string ExpectedInventorySha256 { get; set; }

        [DataMember(Order = 10)]
        internal string LastErrorClass { get; set; }

        [DataMember(Order = 11)]
        internal string UpdatedUtc { get; set; }

        internal static LocalModuleOperationJournal CreateRuntimeInstall(
            string operationId,
            string runtimeId,
            string ownershipNonce,
            string capabilityId,
            string expectedInventorySha256)
        {
            return new LocalModuleOperationJournal
            {
                SchemaVersion = CurrentSchemaVersion,
                OwnershipMarker = ExpectedOwnershipMarker,
                OperationId = operationId,
                Subject = LocalModuleOperationSubject.Runtime,
                SubjectId = runtimeId,
                Stage = LocalModuleOperationStage.Preparing,
                OwnershipNonce = ownershipNonce,
                CapabilityId = capabilityId,
                ExpectedInventorySha256 = expectedInventorySha256,
                LastErrorClass = string.Empty,
                UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        internal static LocalModuleOperationJournal CreateRuntimeDeletion(
            string operationId,
            string runtimeId,
            string ownershipNonce,
            string capabilityId,
            string expectedInventorySha256)
        {
            LocalModuleOperationJournal journal = CreateRuntimeInstall(
                operationId,
                runtimeId,
                ownershipNonce,
                capabilityId,
                expectedInventorySha256);
            journal.Stage = LocalModuleOperationStage.Deleting;
            return journal;
        }
    }

    internal sealed class LocalModuleOperationJournalStore
    {
        private readonly string _machineRoot;
        private readonly string _machineSecurityRoot;
        private readonly string _root;
        private readonly IPathSafety _pathSafety;

        internal LocalModuleOperationJournalStore(
            string machineRoot,
            IPathSafety pathSafety)
        {
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            _machineRoot = Path.GetFullPath(machineRoot);
            _machineSecurityRoot = Path.GetPathRoot(_machineRoot);
            _root = Path.Combine(_machineRoot, "LocalModuleOperations");
            _pathSafety = pathSafety;
        }

        internal void Write(LocalModuleOperationJournal journal)
        {
            Validate(journal);
            string path = GetPath(journal.OperationId, journal.Subject, journal.SubjectId);
            EnsureDirectory(Path.GetDirectoryName(path));
            EnsureSafe(path);
            if (File.Exists(path))
            {
                LocalModuleOperationJournal existing = Read(
                    journal.OperationId,
                    journal.Subject,
                    journal.SubjectId);
                if (!string.Equals(
                        existing.OwnershipNonce,
                        journal.OwnershipNonce,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        existing.ExpectedInventorySha256,
                        journal.ExpectedInventorySha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    journal.Stage < existing.Stage)
                {
                    throw new InvalidDataException(
                        "Operation journal ownership does not match the pending operation.");
                }
            }
            AtomicJsonFile.Write(path, AtomicJsonFile.Serialize(journal));
            EnsureSafe(path);
        }

        internal LocalModuleOperationJournal Read(
            string operationId,
            LocalModuleOperationSubject subject,
            string subjectId)
        {
            string path = GetPath(operationId, subject, subjectId);
            EnsureSafe(path);
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(LocalModuleOperationJournal));
                LocalModuleOperationJournal journal =
                    (LocalModuleOperationJournal)serializer.ReadObject(stream);
                Validate(journal);
                if (!string.Equals(journal.OperationId, operationId, StringComparison.Ordinal) ||
                    journal.Subject != subject ||
                    !string.Equals(journal.SubjectId, subjectId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Operation journal identity does not match its derived path.");
                }
                return journal;
            }
        }

        internal bool TryRead(
            string operationId,
            LocalModuleOperationSubject subject,
            string subjectId,
            out LocalModuleOperationJournal journal)
        {
            string path = GetPath(operationId, subject, subjectId);
            EnsureSafe(path);
            if (!File.Exists(path))
            {
                journal = null;
                return false;
            }
            journal = Read(operationId, subject, subjectId);
            return true;
        }

        internal IList<LocalModuleOperationJournal> ReadForSubject(
            LocalModuleOperationSubject subject,
            string subjectId)
        {
            ValidateSubject(subject, subjectId);
            List<LocalModuleOperationJournal> result =
                new List<LocalModuleOperationJournal>();
            if (!Directory.Exists(_root)) return result;
            EnsureSafe(_root);
            string[] operationRoots = Directory.GetDirectories(_root);
            for (int index = 0; index < operationRoots.Length; index++)
            {
                string operationId = Path.GetFileName(operationRoots[index]);
                if (!ProvisionerCommandLine.IsGuidN(operationId))
                {
                    throw new InvalidDataException(
                        "Local-module operation store contains an unknown directory.");
                }
                string path = GetPath(operationId, subject, subjectId);
                if (File.Exists(path))
                {
                    result.Add(Read(operationId, subject, subjectId));
                }
            }
            result.Sort(delegate(
                LocalModuleOperationJournal left,
                LocalModuleOperationJournal right)
            {
                return string.CompareOrdinal(left.UpdatedUtc, right.UpdatedUtc);
            });
            return result;
        }

        internal void Delete(
            string operationId,
            LocalModuleOperationSubject subject,
            string subjectId)
        {
            Read(operationId, subject, subjectId);
            string path = GetPath(operationId, subject, subjectId);
            File.Delete(path);
            string directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory) &&
                Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory, false);
            }
        }

        private string GetPath(
            string operationId,
            LocalModuleOperationSubject subject,
            string subjectId)
        {
            if (!ProvisionerCommandLine.IsGuidN(operationId))
            {
                throw new ArgumentException(
                    "Operation id must be a 32-character GUID.",
                    "operationId");
            }
            ValidateSubject(subject, subjectId);
            return Path.Combine(
                _root,
                operationId.ToLowerInvariant(),
                ((int)subject).ToString(CultureInfo.InvariantCulture) + "-" +
                subjectId + ".json");
        }

        private void EnsureDirectory(string path)
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
                path,
                ProtectedDirectoryKind.Operations,
                null,
                null);
        }

        private void EnsureSafe(string path)
        {
            ValidationResult rootValidation = _pathSafety.ValidateProtected(
                _machineRoot,
                _machineSecurityRoot,
                null);
            if (!rootValidation.IsValid)
            {
                throw new InvalidDataException(rootValidation.JoinMessages());
            }
            ValidationResult validation =
                _pathSafety.ValidateProtected(path, _machineRoot, null);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
        }

        private static void Validate(LocalModuleOperationJournal journal)
        {
            if (journal == null ||
                journal.SchemaVersion != LocalModuleOperationJournal.CurrentSchemaVersion ||
                !string.Equals(
                    journal.OwnershipMarker,
                    LocalModuleOperationJournal.ExpectedOwnershipMarker,
                    StringComparison.Ordinal) ||
                !ProvisionerCommandLine.IsGuidN(journal.OperationId) ||
                journal.Stage < LocalModuleOperationStage.Preparing ||
                journal.Stage > LocalModuleOperationStage.CleanupPending ||
                !LocalModuleManagedIdentity.IsLowerHex(journal.OwnershipNonce, 32) ||
                string.IsNullOrWhiteSpace(journal.CapabilityId) ||
                !LocalModuleManagedIdentity.IsHex(journal.ExpectedInventorySha256, 64) ||
                string.IsNullOrWhiteSpace(journal.UpdatedUtc))
            {
                throw new InvalidDataException("Local-module operation journal is invalid.");
            }
            ValidateSubject(journal.Subject, journal.SubjectId);
        }

        private static void ValidateSubject(
            LocalModuleOperationSubject subject,
            string subjectId)
        {
            bool valid = subject == LocalModuleOperationSubject.Runtime
                ? LocalModuleManagedIdentity.IsRuntimeId(subjectId)
                : subject == LocalModuleOperationSubject.Instance
                    ? LocalModuleManagedIdentity.IsInstanceId(subjectId)
                    : subject == LocalModuleOperationSubject.KktStack &&
                      LocalModuleManagedIdentity.IsStackId(subjectId);
            if (!valid)
            {
                throw new InvalidDataException("Local-module operation subject is invalid.");
            }
        }
    }
}
