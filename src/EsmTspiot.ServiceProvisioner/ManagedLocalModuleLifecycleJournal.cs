using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal sealed class ManagedLocalModuleLifecycleJournal
    {
        internal const int CurrentSchemaVersion = 1;
        internal const string ExpectedOwnershipMarker =
            "KRS.MultiKKT.ManagedLocalModule.Lifecycle.v1";

        [DataMember(Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        internal string OwnershipMarker { get; set; }

        [DataMember(Order = 3)]
        internal string Inn { get; set; }

        [DataMember(Order = 4)]
        internal string InstanceId { get; set; }

        [DataMember(Order = 5)]
        internal string OwnershipNonce { get; set; }

        [DataMember(Order = 6)]
        internal string RuntimeId { get; set; }

        [DataMember(Order = 7)]
        internal string CapabilityId { get; set; }

        [DataMember(Order = 8)]
        internal string OperationId { get; set; }

        [DataMember(Order = 9)]
        internal ManagedLocalModuleProvisioningStage Stage { get; set; }

        [DataMember(Order = 10)]
        internal string LastErrorClass { get; set; }

        [DataMember(Order = 11)]
        internal string UpdatedUtc { get; set; }

        internal static ManagedLocalModuleLifecycleJournal Create(
            string inn,
            string instanceId,
            string ownershipNonce,
            string runtimeId,
            string capabilityId,
            string operationId,
            ManagedLocalModuleProvisioningStage stage)
        {
            ManagedLocalModuleLifecycleJournal result =
                new ManagedLocalModuleLifecycleJournal
                {
                    SchemaVersion = CurrentSchemaVersion,
                    OwnershipMarker = ExpectedOwnershipMarker,
                    Inn = inn,
                    InstanceId = instanceId,
                    OwnershipNonce = ownershipNonce,
                    RuntimeId = runtimeId,
                    CapabilityId = capabilityId,
                    OperationId = operationId,
                    Stage = stage,
                    LastErrorClass = string.Empty,
                    UpdatedUtc = DateTime.UtcNow.ToString(
                        "o",
                        CultureInfo.InvariantCulture)
                };
            Validate(result);
            return result;
        }

        internal static void Validate(
            ManagedLocalModuleLifecycleJournal journal)
        {
            if (journal == null ||
                journal.SchemaVersion != CurrentSchemaVersion ||
                !string.Equals(
                    journal.OwnershipMarker,
                    ExpectedOwnershipMarker,
                    StringComparison.Ordinal) ||
                (!LocalModuleManagedIdentity.IsAsciiDigits(journal.Inn, 10) &&
                 !LocalModuleManagedIdentity.IsAsciiDigits(journal.Inn, 12)) ||
                !string.Equals(
                    journal.InstanceId,
                    LocalModuleManagedIdentity.CreateInstanceId(
                        journal.Inn,
                        journal.OwnershipNonce),
                    StringComparison.Ordinal) ||
                !LocalModuleManagedIdentity.IsRuntimeId(journal.RuntimeId) ||
                string.IsNullOrWhiteSpace(journal.CapabilityId) ||
                !ProvisionerCommandLine.IsGuidN(journal.OperationId) ||
                journal.Stage < ManagedLocalModuleProvisioningStage.Created ||
                journal.Stage > ManagedLocalModuleProvisioningStage.RequiresAttention ||
                journal.LastErrorClass == null ||
                journal.LastErrorClass.Length > 128 ||
                string.IsNullOrWhiteSpace(journal.UpdatedUtc))
            {
                throw new InvalidDataException(
                    "Managed local-module lifecycle journal is invalid.");
            }
        }
    }

    internal sealed class ManagedLocalModuleLifecycleJournalStore
    {
        private readonly string _machineRoot;
        private readonly string _securityRoot;
        private readonly string _root;
        private readonly IPathSafety _pathSafety;

        internal ManagedLocalModuleLifecycleJournalStore(
            string machineRoot,
            IPathSafety pathSafety)
        {
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            if (string.IsNullOrWhiteSpace(machineRoot) ||
                !Path.IsPathRooted(machineRoot))
            {
                throw new ArgumentException(
                    "An absolute machine-data root is required.",
                    "machineRoot");
            }
            _machineRoot = Path.GetFullPath(machineRoot)
                .TrimEnd(Path.DirectorySeparatorChar);
            _securityRoot = Path.GetPathRoot(_machineRoot);
            _root = Path.Combine(
                _machineRoot,
                "ManagedLocalModuleLifecycle");
            _pathSafety = pathSafety;
        }

        internal void Write(ManagedLocalModuleLifecycleJournal journal)
        {
            ManagedLocalModuleLifecycleJournal.Validate(journal);
            string path = GetPath(journal.Inn);
            EnsureDirectory();
            ManagedLocalModuleLifecycleJournal existing;
            if (TryRead(journal.Inn, out existing))
            {
                ValidateReplacement(existing, journal);
            }
            journal.UpdatedUtc = DateTime.UtcNow.ToString(
                "o",
                CultureInfo.InvariantCulture);
            AtomicJsonFile.Write(path, AtomicJsonFile.Serialize(journal));
            EnsureSafe(path);
        }

        internal bool TryRead(
            string inn,
            out ManagedLocalModuleLifecycleJournal journal)
        {
            string path = GetPath(inn);
            EnsureSafe(path);
            if (!File.Exists(path))
            {
                journal = null;
                return false;
            }
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(
                        typeof(ManagedLocalModuleLifecycleJournal));
                journal = (ManagedLocalModuleLifecycleJournal)
                    serializer.ReadObject(stream);
            }
            ManagedLocalModuleLifecycleJournal.Validate(journal);
            if (!string.Equals(journal.Inn, inn, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Lifecycle journal identity does not match its derived path.");
            }
            return true;
        }

        internal void Delete(string inn, string ownershipNonce)
        {
            ManagedLocalModuleLifecycleJournal journal;
            if (!TryRead(inn, out journal))
            {
                return;
            }
            if (!string.Equals(
                    journal.OwnershipNonce,
                    ownershipNonce,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Lifecycle journal ownership nonce does not match.");
            }
            File.Delete(GetPath(inn));
        }

        private string GetPath(string inn)
        {
            if (!LocalModuleManagedIdentity.IsAsciiDigits(inn, 10) &&
                !LocalModuleManagedIdentity.IsAsciiDigits(inn, 12))
            {
                throw new ArgumentException("INN is invalid.", "inn");
            }
            return Path.Combine(_root, inn + ".json");
        }

        private void EnsureDirectory()
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
        }

        private void EnsureSafe(string path)
        {
            ValidationResult machine = _pathSafety.ValidateProtected(
                _machineRoot,
                _securityRoot,
                null);
            if (!machine.IsValid)
            {
                throw new InvalidDataException(machine.JoinMessages());
            }
            ValidationResult child = _pathSafety.ValidateProtected(
                path,
                _machineRoot,
                null);
            if (!child.IsValid)
            {
                throw new InvalidDataException(child.JoinMessages());
            }
        }

        private static void ValidateReplacement(
            ManagedLocalModuleLifecycleJournal existing,
            ManagedLocalModuleLifecycleJournal replacement)
        {
            if (!string.Equals(
                    existing.InstanceId,
                    replacement.InstanceId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existing.OwnershipNonce,
                    replacement.OwnershipNonce,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existing.RuntimeId,
                    replacement.RuntimeId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existing.CapabilityId,
                    replacement.CapabilityId,
                    StringComparison.Ordinal) ||
                (string.Equals(
                    existing.OperationId,
                    replacement.OperationId,
                    StringComparison.Ordinal) &&
                 replacement.Stage < existing.Stage))
            {
                throw new InvalidDataException(
                    "Lifecycle retry cannot replace immutable ownership or regress its stage.");
            }
        }
    }
}
