using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;

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
            if (pathSafety == null)
            {
                throw new ArgumentNullException("pathSafety");
            }
            _pathSafety = pathSafety;
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

        internal string GetFingerprint(string operationId, string kktSerial)
        {
            string path = GetPath(operationId, kktSerial);
            EnsureSafe(path);
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(stream);
                StringBuilder result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    result.Append(digest[index].ToString("x2"));
                }
                return result.ToString();
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
                journal.Stage > LmProvisioningJournalStage.Cleaning)
            {
                throw new InvalidDataException("Operation journal is invalid.");
            }
            string serviceName = LmServiceIdentity.CreateName(journal.KktSerial);
            if (!string.Equals(journal.ServiceName, serviceName, StringComparison.Ordinal) ||
                journal.GrpcPort < 1 || journal.GrpcPort > 65535 ||
                journal.RestPort < 1 || journal.RestPort > 65535 ||
                journal.GrpcPort == journal.RestPort ||
                journal.TargetPort < 1 || journal.TargetPort > 65535 ||
                !LmGatewayInputValidator.ValidateTarget(new LmGatewayTarget(
                    journal.TargetAddress,
                    journal.TargetPort)).IsValid ||
                !string.Equals(
                    journal.ServiceSid,
                    RestrictedServiceSid.Derive(serviceName),
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(journal.ControllerVersion) ||
                !IsHex(journal.ControllerBinarySha256, 64) ||
                !IsHex(journal.SupervisorSha256, 64) ||
                !Path.IsPathRooted(journal.SupervisorImagePath) ||
                !string.Equals(
                    Path.GetFileName(journal.SupervisorImagePath),
                    "EsmTspiot.ServiceProvisioner.exe",
                    StringComparison.OrdinalIgnoreCase) ||
                !Path.IsPathRooted(journal.ProfilePath) ||
                !string.Equals(
                    Path.GetFileName(journal.ProfilePath),
                    serviceName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Operation journal ownership facts are invalid.");
            }
            bool ensureStage = journal.Operation == LmServiceOperation.EnsureBatch &&
                journal.Stage >= LmProvisioningJournalStage.Preparing &&
                journal.Stage <= LmProvisioningJournalStage.Failed &&
                string.IsNullOrEmpty(journal.ManifestFingerprint);
            bool removalStage =
                (journal.Operation == LmServiceOperation.RemoveManaged ||
                 journal.Operation == LmServiceOperation.CleanupManaged) &&
                (journal.Stage == LmProvisioningJournalStage.Deleting ||
                 journal.Stage == LmProvisioningJournalStage.Cleaning) &&
                IsHex(journal.ManifestFingerprint, 64);
            if (!ensureStage && !removalStage)
            {
                throw new InvalidDataException("Operation journal stage is invalid.");
            }
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length)
            {
                return false;
            }
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
