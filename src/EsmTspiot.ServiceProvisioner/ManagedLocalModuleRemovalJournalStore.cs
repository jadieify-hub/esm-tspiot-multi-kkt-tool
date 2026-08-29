using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal sealed class ManagedLocalModuleRemovalJournal
    {
        internal const int CurrentSchemaVersion = 1;
        internal const string ExpectedOwnershipMarker =
            "KRS.MultiKKT.ManagedLocalModule.Removal.v1";

        [DataMember(Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        internal string OwnershipMarker { get; set; }

        [DataMember(Order = 3)]
        internal string OperationId { get; set; }

        [DataMember(Order = 4)]
        internal string KktSerial { get; set; }

        [DataMember(Order = 5)]
        internal string StackId { get; set; }

        [DataMember(Order = 6)]
        internal string StackOwnershipNonce { get; set; }

        [DataMember(Order = 7)]
        internal string InstanceId { get; set; }

        [DataMember(Order = 8)]
        internal string InstanceOwnershipNonce { get; set; }

        [DataMember(Order = 9)]
        internal string RuntimeId { get; set; }

        [DataMember(Order = 10)]
        internal string RuntimeOwnershipNonce { get; set; }

        [DataMember(Order = 11)]
        internal string LastErrorClass { get; set; }

        [DataMember(Order = 12)]
        internal string UpdatedUtc { get; set; }

        internal ManagedLocalModuleRemovalSnapshot ToSnapshot()
        {
            Validate(this);
            return ManagedLocalModuleRemovalSnapshot.CreateForTesting(
                KktSerial,
                StackId,
                StackOwnershipNonce,
                InstanceId,
                InstanceOwnershipNonce,
                RuntimeId,
                RuntimeOwnershipNonce);
        }

        internal static ManagedLocalModuleRemovalJournal Create(
            ManagedLocalModuleRemovalSnapshot snapshot,
            string operationId,
            string lastErrorClass)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            ManagedLocalModuleRemovalJournal result =
                new ManagedLocalModuleRemovalJournal
                {
                    SchemaVersion = CurrentSchemaVersion,
                    OwnershipMarker = ExpectedOwnershipMarker,
                    OperationId = operationId,
                    KktSerial = snapshot.KktSerial,
                    StackId = snapshot.StackId,
                    StackOwnershipNonce = snapshot.StackOwnershipNonce,
                    InstanceId = snapshot.InstanceId,
                    InstanceOwnershipNonce = snapshot.InstanceOwnershipNonce,
                    RuntimeId = snapshot.RuntimeId,
                    RuntimeOwnershipNonce = snapshot.RuntimeOwnershipNonce,
                    LastErrorClass = lastErrorClass ?? string.Empty,
                    UpdatedUtc = DateTime.UtcNow.ToString(
                        "o",
                        CultureInfo.InvariantCulture)
                };
            Validate(result);
            return result;
        }

        internal static void Validate(ManagedLocalModuleRemovalJournal journal)
        {
            if (journal == null ||
                journal.SchemaVersion != CurrentSchemaVersion ||
                !string.Equals(
                    journal.OwnershipMarker,
                    ExpectedOwnershipMarker,
                    StringComparison.Ordinal) ||
                !ProvisionerCommandLine.IsGuidN(journal.OperationId) ||
                !string.Equals(
                    journal.StackId,
                    LocalModuleManagedIdentity.CreateStackId(journal.KktSerial),
                    StringComparison.Ordinal) ||
                !LocalModuleManagedIdentity.IsLowerHex(
                    journal.StackOwnershipNonce,
                    32) ||
                !LocalModuleManagedIdentity.IsInstanceId(journal.InstanceId) ||
                !LocalModuleManagedIdentity.IsLowerHex(
                    journal.InstanceOwnershipNonce,
                    32) ||
                !LocalModuleManagedIdentity.IsRuntimeId(journal.RuntimeId) ||
                !LocalModuleManagedIdentity.IsLowerHex(
                    journal.RuntimeOwnershipNonce,
                    32) ||
                journal.LastErrorClass == null ||
                journal.LastErrorClass.Length > 128 ||
                string.IsNullOrWhiteSpace(journal.UpdatedUtc))
            {
                throw new InvalidDataException(
                    "Managed local-module removal journal is invalid.");
            }
        }
    }

    internal sealed class ManagedLocalModuleRemovalJournalStore
    {
        private readonly string _machineRoot;
        private readonly string _securityRoot;
        private readonly string _root;
        private readonly IPathSafety _pathSafety;

        internal ManagedLocalModuleRemovalJournalStore(
            string machineRoot,
            IPathSafety pathSafety)
        {
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            if (string.IsNullOrWhiteSpace(machineRoot) ||
                !Path.IsPathRooted(machineRoot))
            {
                throw new ArgumentException(
                    "An absolute machine root is required.",
                    "machineRoot");
            }
            _machineRoot = Path.GetFullPath(machineRoot)
                .TrimEnd(Path.DirectorySeparatorChar);
            _securityRoot = Path.GetPathRoot(_machineRoot);
            _root = Path.Combine(
                _machineRoot,
                "ManagedLocalModuleRemoval");
            _pathSafety = pathSafety;
        }

        internal void Write(
            ManagedLocalModuleRemovalSnapshot snapshot,
            string operationId,
            string lastErrorClass)
        {
            ManagedLocalModuleRemovalJournal journal =
                ManagedLocalModuleRemovalJournal.Create(
                    snapshot,
                    operationId,
                    lastErrorClass);
            EnsureDirectory();
            ManagedLocalModuleRemovalSnapshot existing;
            if (TryRead(snapshot.KktSerial, out existing) &&
                !SameOwnership(existing, snapshot))
            {
                throw new InvalidDataException(
                    "Removal retry cannot replace immutable ownership.");
            }
            AtomicJsonFile.Write(
                GetPath(snapshot.KktSerial),
                AtomicJsonFile.Serialize(journal));
            EnsureSafe(GetPath(snapshot.KktSerial));
        }

        internal bool TryRead(
            string kktSerial,
            out ManagedLocalModuleRemovalSnapshot snapshot)
        {
            string path = GetPath(kktSerial);
            EnsureSafe(path);
            if (!File.Exists(path))
            {
                snapshot = null;
                return false;
            }
            ManagedLocalModuleRemovalJournal journal;
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(
                        typeof(ManagedLocalModuleRemovalJournal));
                journal = (ManagedLocalModuleRemovalJournal)
                    serializer.ReadObject(stream);
            }
            ManagedLocalModuleRemovalJournal.Validate(journal);
            if (!string.Equals(
                    journal.KktSerial,
                    kktSerial,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Removal journal identity does not match its derived path.");
            }
            snapshot = journal.ToSnapshot();
            return true;
        }

        internal bool TryComputeFingerprint(
            string kktSerial,
            out string fingerprint)
        {
            ManagedLocalModuleRemovalSnapshot snapshot;
            if (!TryRead(kktSerial, out snapshot))
            {
                fingerprint = string.Empty;
                return false;
            }
            fingerprint = ManagedStateFileFingerprint.Compute(
                GetPath(kktSerial));
            return true;
        }

        internal void Delete(
            string kktSerial,
            string stackOwnershipNonce)
        {
            ManagedLocalModuleRemovalSnapshot snapshot;
            if (!TryRead(kktSerial, out snapshot))
            {
                return;
            }
            if (!string.Equals(
                    snapshot.StackOwnershipNonce,
                    stackOwnershipNonce,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Removal journal ownership nonce does not match.");
            }
            File.Delete(GetPath(kktSerial));
        }

        private string GetPath(string kktSerial)
        {
            LocalModuleManagedIdentity.CreateStackId(kktSerial);
            return Path.Combine(_root, kktSerial + ".json");
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
            ValidationResult root = _pathSafety.ValidateProtected(
                _machineRoot,
                _securityRoot,
                null);
            if (!root.IsValid)
            {
                throw new InvalidDataException(root.JoinMessages());
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

        private static bool SameOwnership(
            ManagedLocalModuleRemovalSnapshot left,
            ManagedLocalModuleRemovalSnapshot right)
        {
            return string.Equals(left.StackId, right.StackId, StringComparison.Ordinal) &&
                string.Equals(
                    left.StackOwnershipNonce,
                    right.StackOwnershipNonce,
                    StringComparison.Ordinal) &&
                string.Equals(left.InstanceId, right.InstanceId, StringComparison.Ordinal) &&
                string.Equals(
                    left.InstanceOwnershipNonce,
                    right.InstanceOwnershipNonce,
                    StringComparison.Ordinal) &&
                string.Equals(left.RuntimeId, right.RuntimeId, StringComparison.Ordinal) &&
                string.Equals(
                    left.RuntimeOwnershipNonce,
                    right.RuntimeOwnershipNonce,
                    StringComparison.Ordinal);
        }
    }
}
