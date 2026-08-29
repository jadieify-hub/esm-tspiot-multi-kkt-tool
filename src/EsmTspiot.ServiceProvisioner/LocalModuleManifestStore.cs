using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleManifestStore
    {
        private readonly string _machineRoot;
        private readonly string _machineSecurityRoot;
        private readonly string _runtimeContainerRoot;
        private readonly string _runtimeSecurityRoot;
        private readonly string _runtimeInventoryRoot;
        private readonly string _instancesRoot;
        private readonly string _stacksRoot;
        private readonly IPathSafety _pathSafety;
        private readonly string _initiatingSid;

        internal LocalModuleManifestStore(
            string machineRoot,
            string runtimeContainerRoot,
            IPathSafety pathSafety,
            string initiatingSid)
        {
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            _machineRoot = NormalizeRoot(machineRoot, "machineRoot");
            _machineSecurityRoot = Path.GetPathRoot(_machineRoot);
            _runtimeContainerRoot = NormalizeRoot(
                runtimeContainerRoot,
                "runtimeContainerRoot");
            _runtimeSecurityRoot = Path.GetPathRoot(_runtimeContainerRoot);
            if (PathSafety.IsUnderRoot(_machineRoot, _runtimeContainerRoot) ||
                PathSafety.IsUnderRoot(_runtimeContainerRoot, _machineRoot))
            {
                throw new ArgumentException(
                    "Runtime and mutable machine-data roots must be separate.");
            }
            _runtimeInventoryRoot = Path.Combine(
                _machineRoot,
                "LocalModuleRuntimeInventory");
            _instancesRoot = Path.Combine(_machineRoot, "LocalModules");
            _stacksRoot = Path.Combine(_machineRoot, "ManagedKktStacks");
            _pathSafety = pathSafety;
            _initiatingSid = initiatingSid;
            OperationJournals = new LocalModuleOperationJournalStore(
                _machineRoot,
                _pathSafety);
        }

        internal static LocalModuleManifestStore CreateMachineStore(
            IPathSafety pathSafety,
            string initiatingSid)
        {
            string machineRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "KRS",
                "MultiKKT");
            string runtimeRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "KRS",
                "MultiKKT",
                "LocalModuleRuntime");
            return new LocalModuleManifestStore(
                machineRoot,
                runtimeRoot,
                pathSafety,
                initiatingSid);
        }

        internal LocalModuleOperationJournalStore OperationJournals { get; private set; }

        internal string MachineRoot { get { return _machineRoot; } }

        internal string MachineSecurityRoot { get { return _machineSecurityRoot; } }

        internal string RuntimeContainerRoot { get { return _runtimeContainerRoot; } }

        internal string RuntimeSecurityRoot { get { return _runtimeSecurityRoot; } }

        internal string InstancesRoot { get { return _instancesRoot; } }

        internal string RuntimeInventoryRoot { get { return _runtimeInventoryRoot; } }

        internal string GetAdministrativeImageRoot(string operationId)
        {
            ValidateOperationId(operationId);
            return Path.Combine(
                _machineRoot,
                "LocalModuleStaging",
                operationId.ToLowerInvariant(),
                "AdministrativeImage");
        }

        internal string GetAdministrativeImageLogPath(string operationId)
        {
            ValidateOperationId(operationId);
            return Path.Combine(
                _machineRoot,
                "LocalModuleStaging",
                operationId.ToLowerInvariant(),
                "msiexec-administrative.log");
        }

        internal string GetRuntimeRoot(string runtimeId)
        {
            ValidateRuntimeId(runtimeId);
            return Path.Combine(_runtimeContainerRoot, runtimeId);
        }

        internal string GetRuntimeStagingRoot(string runtimeId, string operationId)
        {
            ValidateRuntimeId(runtimeId);
            ValidateOperationId(operationId);
            return Path.Combine(
                _runtimeContainerRoot,
                "." + runtimeId + "." + operationId.ToLowerInvariant() + ".stage");
        }

        internal string GetRuntimeManifestPath(string runtimeId)
        {
            ValidateRuntimeId(runtimeId);
            return Path.Combine(_runtimeInventoryRoot, runtimeId, "manifest.json");
        }

        internal string GetInstanceRoot(string instanceId)
        {
            ValidateInstanceId(instanceId);
            return Path.Combine(_instancesRoot, instanceId);
        }

        internal string GetInstanceManifestPath(string instanceId)
        {
            return Path.Combine(GetInstanceRoot(instanceId), "manifest.json");
        }

        internal string GetStackManifestPath(string kktSerial)
        {
            string stackId = LocalModuleManagedIdentity.CreateStackId(kktSerial);
            return Path.Combine(_stacksRoot, stackId, "manifest.json");
        }

        internal void WriteRuntime(LocalModuleRuntimeManifest manifest)
        {
            ValidateRuntime(manifest);
            string path = GetRuntimeManifestPath(manifest.RuntimeId);
            EnsureInventoryPath(path);
            LocalModuleRuntimeManifest existing;
            if (TryReadRuntime(manifest.RuntimeId, out existing))
            {
                ValidateRuntimeTransition(existing, manifest);
            }
            AtomicJsonFile.Write(path, AtomicJsonFile.Serialize(manifest));
            _pathSafety.EnsureProtectedRuntimeFile(path);
            EnsureMachineSafe(path);
        }

        internal LocalModuleRuntimeManifest ReadRuntime(string runtimeId)
        {
            string path = GetRuntimeManifestPath(runtimeId);
            EnsureMachineSafe(path);
            LocalModuleRuntimeManifest manifest = ReadJson<LocalModuleRuntimeManifest>(path);
            ValidateRuntime(manifest);
            if (!string.Equals(manifest.RuntimeId, runtimeId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Runtime manifest identity does not match its derived path.");
            }
            return manifest;
        }

        internal bool TryReadRuntime(
            string runtimeId,
            out LocalModuleRuntimeManifest manifest)
        {
            string path = GetRuntimeManifestPath(runtimeId);
            EnsureMachineSafe(path);
            if (!File.Exists(path))
            {
                manifest = null;
                return false;
            }
            manifest = ReadRuntime(runtimeId);
            return true;
        }

        internal void WriteInstance(LocalModuleInstanceManifest manifest)
        {
            ValidateInstance(manifest);
            LocalModuleRuntimeManifest runtime = ReadRuntime(manifest.RuntimeId);
            if (!string.Equals(
                    manifest.RuntimeRoot,
                    runtime.RuntimeRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Instance references a different runtime root.");
            }
            string path = GetInstanceManifestPath(manifest.InstanceId);
            EnsureInventoryPath(path);
            LocalModuleInstanceManifest existing;
            string previousRuntimeId = null;
            if (TryReadInstance(manifest.InstanceId, out existing))
            {
                ValidateInstanceTransition(existing, manifest);
                previousRuntimeId = existing.RuntimeId;
            }
            AtomicJsonFile.Write(path, AtomicJsonFile.Serialize(manifest));
            _pathSafety.EnsureProtectedReadOnlyFile(
                path,
                new[]
                {
                    RestrictedServiceSid.Derive(
                        manifest.DatabaseServiceName),
                    RestrictedServiceSid.Derive(manifest.ApiServiceName)
                });
            EnsureMachineSafe(path);
            RefreshRuntimeReferenceCount(manifest.RuntimeId);
            if (!string.IsNullOrEmpty(previousRuntimeId) &&
                !string.Equals(
                    previousRuntimeId,
                    manifest.RuntimeId,
                    StringComparison.Ordinal))
            {
                RefreshRuntimeReferenceCount(previousRuntimeId);
            }
        }

        internal LocalModuleInstanceManifest ReadInstance(string instanceId)
        {
            string path = GetInstanceManifestPath(instanceId);
            EnsureMachineSafe(path);
            LocalModuleInstanceManifest manifest =
                ReadJson<LocalModuleInstanceManifest>(path);
            ValidateInstance(manifest);
            if (!string.Equals(manifest.InstanceId, instanceId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Instance manifest identity does not match its derived path.");
            }
            LocalModuleRuntimeManifest runtime = ReadRuntime(manifest.RuntimeId);
            if (!string.Equals(
                    manifest.RuntimeRoot,
                    runtime.RuntimeRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Instance runtime reference is inconsistent.");
            }
            return manifest;
        }

        internal bool TryReadInstance(
            string instanceId,
            out LocalModuleInstanceManifest manifest)
        {
            string path = GetInstanceManifestPath(instanceId);
            EnsureMachineSafe(path);
            if (!File.Exists(path))
            {
                manifest = null;
                return false;
            }
            manifest = ReadInstance(instanceId);
            return true;
        }

        internal bool TryFindInstanceByInn(
            string inn,
            out LocalModuleInstanceManifest manifest)
        {
            if (!LocalModuleManagedIdentity.IsAsciiDigits(inn, 10) &&
                !LocalModuleManagedIdentity.IsAsciiDigits(inn, 12))
            {
                throw new ArgumentException("INN is invalid.", "inn");
            }
            manifest = null;
            if (!Directory.Exists(_instancesRoot))
            {
                return false;
            }
            EnsureMachineSafe(_instancesRoot);
            string[] directories = Directory.GetDirectories(_instancesRoot);
            for (int index = 0; index < directories.Length; index++)
            {
                string instanceId = Path.GetFileName(directories[index]);
                ValidateInstanceId(instanceId);
                LocalModuleInstanceManifest candidate = ReadInstance(instanceId);
                if (!string.Equals(candidate.Inn, inn, StringComparison.Ordinal))
                {
                    continue;
                }
                if (manifest != null)
                {
                    throw new InvalidDataException(
                        "Managed local-module inventory contains more than one instance for the INN.");
                }
                manifest = candidate;
            }
            return manifest != null;
        }

        internal void DeleteInstance(string instanceId, string ownershipNonce)
        {
            LocalModuleInstanceManifest manifest = ReadInstance(instanceId);
            if (!string.Equals(
                    manifest.OwnershipNonce,
                    ownershipNonce,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Instance ownership nonce does not match.");
            }
            if (CountInstanceReferences(instanceId) != 0)
            {
                throw new InvalidOperationException(
                    "Local-module instance still has managed KKT references.");
            }
            string path = GetInstanceManifestPath(instanceId);
            string runtimeId = manifest.RuntimeId;
            File.Delete(path);
            string directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory) &&
                Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory, false);
            }
            RefreshRuntimeReferenceCount(runtimeId);
        }

        internal void WriteStack(ManagedKktStackManifest manifest)
        {
            ValidateStack(manifest);
            LocalModuleInstanceManifest instance =
                ReadInstance(manifest.LocalModuleInstanceId);
            if (!string.Equals(instance.Inn, manifest.Inn, StringComparison.Ordinal))
            {
                throw new InvalidDataException("KKT stack references a different INN instance.");
            }
            string path = GetStackManifestPath(manifest.KktSerial);
            EnsureInventoryPath(path);
            ManagedKktStackManifest existing;
            if (TryReadStack(manifest.KktSerial, out existing))
            {
                ValidateStackTransition(existing, manifest);
            }
            AtomicJsonFile.Write(path, AtomicJsonFile.Serialize(manifest));
            EnsureMachineSafe(path);
        }

        internal ManagedKktStackManifest ReadStack(string kktSerial)
        {
            string path = GetStackManifestPath(kktSerial);
            EnsureMachineSafe(path);
            ManagedKktStackManifest manifest = ReadJson<ManagedKktStackManifest>(path);
            ValidateStack(manifest);
            if (!string.Equals(manifest.KktSerial, kktSerial, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "KKT stack identity does not match its derived path.");
            }
            LocalModuleInstanceManifest instance =
                ReadInstance(manifest.LocalModuleInstanceId);
            if (!string.Equals(instance.Inn, manifest.Inn, StringComparison.Ordinal))
            {
                throw new InvalidDataException("KKT stack INN reference is inconsistent.");
            }
            return manifest;
        }

        internal bool TryReadStack(
            string kktSerial,
            out ManagedKktStackManifest manifest)
        {
            string path = GetStackManifestPath(kktSerial);
            EnsureMachineSafe(path);
            if (!File.Exists(path))
            {
                manifest = null;
                return false;
            }
            manifest = ReadStack(kktSerial);
            return true;
        }

        internal int CountInstanceReferences(string instanceId)
        {
            ValidateInstanceId(instanceId);
            if (!Directory.Exists(_stacksRoot)) return 0;
            EnsureMachineSafe(_stacksRoot);
            int count = 0;
            string[] directories = Directory.GetDirectories(_stacksRoot);
            for (int index = 0; index < directories.Length; index++)
            {
                string stackId = Path.GetFileName(directories[index]);
                if (!LocalModuleManagedIdentity.IsStackId(stackId))
                {
                    throw new InvalidDataException(
                        "Managed KKT inventory contains an unknown directory.");
                }
                string kktSerial = stackId.Substring(4);
                ManagedKktStackManifest manifest = ReadStack(kktSerial);
                if (string.Equals(
                        manifest.LocalModuleInstanceId,
                        instanceId,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        internal void DeleteStack(string kktSerial, string ownershipNonce)
        {
            ManagedKktStackManifest manifest = ReadStack(kktSerial);
            if (!string.Equals(
                    manifest.OwnershipNonce,
                    ownershipNonce,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("KKT stack ownership nonce does not match.");
            }
            string path = GetStackManifestPath(kktSerial);
            File.Delete(path);
            string directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory) &&
                Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory, false);
            }
        }

        internal int CountRuntimeReferences(string runtimeId)
        {
            ValidateRuntimeId(runtimeId);
            if (!Directory.Exists(_instancesRoot)) return 0;
            EnsureMachineSafe(_instancesRoot);
            int count = 0;
            string[] directories = Directory.GetDirectories(_instancesRoot);
            for (int index = 0; index < directories.Length; index++)
            {
                string instanceId = Path.GetFileName(directories[index]);
                ValidateInstanceId(instanceId);
                LocalModuleInstanceManifest manifest = ReadInstance(instanceId);
                if (string.Equals(manifest.RuntimeId, runtimeId, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        internal void DeleteRuntimeManifest(string runtimeId, string ownershipNonce)
        {
            LocalModuleRuntimeManifest manifest = ReadRuntime(runtimeId);
            if (!string.Equals(
                    manifest.OwnershipNonce,
                    ownershipNonce,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Runtime ownership nonce does not match.");
            }
            if (CountRuntimeReferences(runtimeId) != 0)
            {
                throw new InvalidOperationException("Runtime still has managed references.");
            }
            string path = GetRuntimeManifestPath(runtimeId);
            File.Delete(path);
            string directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory) &&
                Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory, false);
            }
        }

        private void ValidateRuntime(LocalModuleRuntimeManifest manifest)
        {
            if (manifest == null ||
                manifest.SchemaVersion != LocalModuleRuntimeManifest.CurrentSchemaVersion ||
                !string.Equals(
                    manifest.OwnershipMarker,
                    LocalModuleRuntimeManifest.ExpectedOwnershipMarker,
                    StringComparison.Ordinal) ||
                !LocalModuleManagedIdentity.IsRuntimeId(manifest.RuntimeId) ||
                !string.Equals(
                    manifest.RuntimeId,
                    LocalModuleManagedIdentity.CreateRuntimeId(
                        manifest.CapabilityId,
                        manifest.PackageSha256),
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.ProductName) ||
                string.IsNullOrWhiteSpace(manifest.ProductVersion) ||
                string.IsNullOrWhiteSpace(manifest.ProductCode) ||
                string.IsNullOrWhiteSpace(manifest.UpgradeCode) ||
                string.IsNullOrWhiteSpace(manifest.PackageFileName) ||
                manifest.PackageByteLength <= 0 ||
                !LocalModuleManagedIdentity.IsHex(manifest.PackageSha256, 64) ||
                string.IsNullOrWhiteSpace(manifest.SignerSubject) ||
                !LocalModuleManagedIdentity.IsHex(manifest.SignerThumbprint, 40) ||
                !LocalModuleManagedIdentity.IsHex(
                    manifest.RequiredFileContractSha256,
                    64) ||
                !LocalModuleManagedIdentity.IsLowerHex(manifest.OwnershipNonce, 32) ||
                manifest.ConfirmedReferenceCount < 0 ||
                manifest.State < LocalModuleRuntimeLifecycleState.Preparing ||
                manifest.State > LocalModuleRuntimeLifecycleState.CleanupPending ||
                string.IsNullOrWhiteSpace(manifest.UpdatedUtc) ||
                manifest.Files == null || manifest.Files.Count == 0 ||
                manifest.Directories == null)
            {
                throw new InvalidDataException("Runtime manifest is invalid.");
            }
            string expectedRoot = GetRuntimeRoot(manifest.RuntimeId);
            if (!PathEquals(manifest.RuntimeRoot, expectedRoot))
            {
                throw new InvalidDataException("Runtime root is not helper-derived.");
            }
            ValidateRuntimeFiles(manifest.Files);
            ValidateRuntimeDirectories(manifest.Directories, manifest.Files);
        }

        private void ValidateInstance(LocalModuleInstanceManifest manifest)
        {
            if (manifest == null ||
                manifest.SchemaVersion != LocalModuleInstanceManifest.CurrentSchemaVersion ||
                !string.Equals(
                    manifest.OwnershipMarker,
                    LocalModuleInstanceManifest.ExpectedOwnershipMarker,
                    StringComparison.Ordinal) ||
                !LocalModuleManagedIdentity.IsInstanceId(manifest.InstanceId) ||
                !string.Equals(
                    manifest.InstanceId,
                    LocalModuleManagedIdentity.CreateInstanceId(
                        manifest.Inn,
                        manifest.OwnershipNonce),
                    StringComparison.Ordinal) ||
                !LocalModuleManagedIdentity.IsRuntimeId(manifest.RuntimeId) ||
                manifest.LocalModuleOrdinal < 1 || manifest.LocalModuleOrdinal > 32 ||
                !PortsAreValidAndDistinct(
                    manifest.ApiPort,
                    manifest.DatabasePort,
                    manifest.EpmdPort) ||
                !string.Equals(
                    manifest.ApiNodeName,
                    "krs_lm_regime_n" + manifest.LocalModuleOrdinal.ToString(
                        "00",
                        CultureInfo.InvariantCulture) +
                        "@127.0.0.1",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.DatabaseNodeName,
                    "krs_lm_yenisei_n" + manifest.LocalModuleOrdinal.ToString(
                        "00",
                        CultureInfo.InvariantCulture) +
                        "@127.0.0.1",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.ApiServiceName,
                    LocalModuleManagedIdentity.CreateApiServiceName(manifest.InstanceId),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.DatabaseServiceName,
                    LocalModuleManagedIdentity.CreateDatabaseServiceName(manifest.InstanceId),
                    StringComparison.Ordinal) ||
                !AllConfigHashesValid(manifest) ||
                !OperationIdsAreValid(
                    manifest.CurrentOperationId,
                    manifest.LastCompletedOperationId) ||
                manifest.State < LocalModuleInstanceLifecycleState.Preparing ||
                manifest.State > LocalModuleInstanceLifecycleState.Failed ||
                string.IsNullOrWhiteSpace(manifest.UpdatedUtc))
            {
                throw new InvalidDataException("Local-module instance manifest is invalid.");
            }
            string profileRoot = GetInstanceRoot(manifest.InstanceId);
            if (!PathEquals(manifest.ProfileRoot, profileRoot) ||
                !PathEquals(manifest.ConfigRoot, Path.Combine(profileRoot, "config")) ||
                !PathEquals(manifest.DataRoot, Path.Combine(profileRoot, "data")) ||
                !PathEquals(manifest.LogsRoot, Path.Combine(profileRoot, "logs")) ||
                !PathEquals(manifest.RuntimeRoot, GetRuntimeRoot(manifest.RuntimeId)))
            {
                throw new InvalidDataException("Instance paths are not helper-derived.");
            }
        }

        private static void ValidateStack(ManagedKktStackManifest manifest)
        {
            if (manifest == null ||
                manifest.SchemaVersion != ManagedKktStackManifest.CurrentSchemaVersion ||
                !string.Equals(
                    manifest.OwnershipMarker,
                    ManagedKktStackManifest.ExpectedOwnershipMarker,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.StackId,
                    LocalModuleManagedIdentity.CreateStackId(manifest.KktSerial),
                    StringComparison.Ordinal) ||
                (!LocalModuleManagedIdentity.IsAsciiDigits(manifest.Inn, 10) &&
                 !LocalModuleManagedIdentity.IsAsciiDigits(manifest.Inn, 12)) ||
                manifest.KktOrdinal < 1 || manifest.KktOrdinal > 32 ||
                !LocalModuleManagedIdentity.IsInstanceId(
                    manifest.LocalModuleInstanceId) ||
                !string.Equals(
                    manifest.ControllerServiceName,
                    LmServiceIdentity.CreateName(manifest.KktSerial),
                    StringComparison.Ordinal) ||
                !PortsAreValidAndDistinct(
                    manifest.ControllerGrpcPort,
                    manifest.ControllerRestPort) ||
                !LocalModuleManagedIdentity.IsLowerHex(manifest.OwnershipNonce, 32) ||
                !OperationIdsAreValid(
                    manifest.CurrentOperationId,
                    manifest.LastCompletedOperationId) ||
                manifest.State < ManagedKktStackLifecycleState.Preparing ||
                manifest.State > ManagedKktStackLifecycleState.Failed ||
                string.IsNullOrWhiteSpace(manifest.UpdatedUtc) ||
                manifest.EsmRecordId == null || manifest.EsmRecordId.Length > 256 ||
                ContainsControl(manifest.EsmRecordId))
            {
                throw new InvalidDataException("Managed KKT stack manifest is invalid.");
            }
        }

        private static void ValidateRuntimeFiles(IList<LocalModuleRuntimeFile> files)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < files.Count; index++)
            {
                LocalModuleRuntimeFile file = files[index];
                if (file == null ||
                    !IsSafeRelativePath(file.RelativePath) ||
                    file.ByteLength < 0 ||
                    !LocalModuleManagedIdentity.IsHex(file.Sha256, 64) ||
                    !paths.Add(NormalizeRelative(file.RelativePath)))
                {
                    throw new InvalidDataException("Runtime file inventory is invalid.");
                }
            }
        }

        private static void ValidateRuntimeDirectories(
            IList<string> directories,
            IList<LocalModuleRuntimeFile> files)
        {
            HashSet<string> paths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < directories.Count; index++)
            {
                string normalized = NormalizeRelative(directories[index]);
                if (!IsSafeRelativePath(normalized) || !paths.Add(normalized))
                {
                    throw new InvalidDataException("Runtime directory inventory is invalid.");
                }
            }
            for (int index = 0; index < files.Count; index++)
            {
                string current = Path.GetDirectoryName(files[index].RelativePath);
                while (!string.IsNullOrEmpty(current))
                {
                    current = NormalizeRelative(current);
                    if (!paths.Contains(current))
                    {
                        throw new InvalidDataException(
                            "Runtime inventory omits a parent directory.");
                    }
                    current = Path.GetDirectoryName(current);
                }
            }
        }

        private static void ValidateRuntimeTransition(
            LocalModuleRuntimeManifest existing,
            LocalModuleRuntimeManifest replacement)
        {
            if (!string.Equals(
                    existing.OwnershipNonce,
                    replacement.OwnershipNonce,
                    StringComparison.Ordinal) ||
                !string.Equals(existing.RuntimeId, replacement.RuntimeId, StringComparison.Ordinal) ||
                !string.Equals(
                    existing.CapabilityId,
                    replacement.CapabilityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existing.PackageSha256,
                    replacement.PackageSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    existing.RequiredFileContractSha256,
                    replacement.RequiredFileContractSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !PathEquals(existing.RuntimeRoot, replacement.RuntimeRoot) ||
                !RuntimeInventoryEquals(existing, replacement))
            {
                throw new InvalidDataException(
                    "Immutable runtime ownership facts cannot be replaced.");
            }
        }

        private static bool RuntimeInventoryEquals(
            LocalModuleRuntimeManifest left,
            LocalModuleRuntimeManifest right)
        {
            if (left.Files.Count != right.Files.Count ||
                left.Directories.Count != right.Directories.Count)
            {
                return false;
            }
            for (int index = 0; index < left.Files.Count; index++)
            {
                if (!string.Equals(
                        NormalizeRelative(left.Files[index].RelativePath),
                        NormalizeRelative(right.Files[index].RelativePath),
                        StringComparison.OrdinalIgnoreCase) ||
                    left.Files[index].ByteLength != right.Files[index].ByteLength ||
                    !string.Equals(
                        left.Files[index].Sha256,
                        right.Files[index].Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            for (int index = 0; index < left.Directories.Count; index++)
            {
                if (!string.Equals(
                        NormalizeRelative(left.Directories[index]),
                        NormalizeRelative(right.Directories[index]),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static void ValidateInstanceTransition(
            LocalModuleInstanceManifest existing,
            LocalModuleInstanceManifest replacement)
        {
            if (!string.Equals(
                    existing.OwnershipNonce,
                    replacement.OwnershipNonce,
                    StringComparison.Ordinal) ||
                !string.Equals(existing.InstanceId, replacement.InstanceId, StringComparison.Ordinal) ||
                !string.Equals(existing.Inn, replacement.Inn, StringComparison.Ordinal) ||
                existing.LocalModuleOrdinal != replacement.LocalModuleOrdinal ||
                !PathEquals(existing.ProfileRoot, replacement.ProfileRoot) ||
                !string.Equals(
                    existing.ApiServiceName,
                    replacement.ApiServiceName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existing.DatabaseServiceName,
                    replacement.DatabaseServiceName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Immutable local-module instance ownership facts cannot be replaced.");
            }
        }

        private static void ValidateStackTransition(
            ManagedKktStackManifest existing,
            ManagedKktStackManifest replacement)
        {
            if (!string.Equals(
                    existing.OwnershipNonce,
                    replacement.OwnershipNonce,
                    StringComparison.Ordinal) ||
                !string.Equals(existing.StackId, replacement.StackId, StringComparison.Ordinal) ||
                !string.Equals(existing.KktSerial, replacement.KktSerial, StringComparison.Ordinal) ||
                !string.Equals(existing.Inn, replacement.Inn, StringComparison.Ordinal) ||
                existing.KktOrdinal != replacement.KktOrdinal ||
                !string.Equals(
                    existing.LocalModuleInstanceId,
                    replacement.LocalModuleInstanceId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existing.ControllerServiceName,
                    replacement.ControllerServiceName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Immutable KKT stack ownership facts cannot be replaced.");
            }
        }

        private void RefreshRuntimeReferenceCount(string runtimeId)
        {
            LocalModuleRuntimeManifest runtime = ReadRuntime(runtimeId);
            int references = CountRuntimeReferences(runtimeId);
            if (runtime.ConfirmedReferenceCount == references) return;
            runtime.ConfirmedReferenceCount = references;
            runtime.UpdatedUtc = DateTime.UtcNow.ToString(
                "o",
                CultureInfo.InvariantCulture);
            WriteRuntime(runtime);
        }

        private void EnsureInventoryPath(string path)
        {
            _pathSafety.EnsureProtectedDirectory(
                _machineRoot,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            string current = Path.GetDirectoryName(path);
            Stack<string> directories = new Stack<string>();
            while (!PathEquals(current, _machineRoot))
            {
                directories.Push(current);
                current = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(current))
                {
                    throw new InvalidDataException("Inventory path escapes machine root.");
                }
            }
            while (directories.Count > 0)
            {
                _pathSafety.EnsureProtectedDirectory(
                    directories.Pop(),
                    ProtectedDirectoryKind.Inventory,
                    _initiatingSid,
                    null);
            }
            EnsureMachineSafe(path);
        }

        private void EnsureMachineSafe(string path)
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

        private static T ReadJson<T>(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(stream);
            }
        }

        private static bool AllConfigHashesValid(LocalModuleInstanceManifest manifest)
        {
            return LocalModuleManagedIdentity.IsHex(manifest.RegimeLocalIniSha256, 64) &&
                LocalModuleManagedIdentity.IsHex(manifest.YeniseiLocalIniSha256, 64) &&
                LocalModuleManagedIdentity.IsHex(manifest.RegimeVmArgsSha256, 64) &&
                LocalModuleManagedIdentity.IsHex(manifest.YeniseiVmArgsSha256, 64) &&
                LocalModuleManagedIdentity.IsHex(manifest.RegimeSysConfigSha256, 64) &&
                LocalModuleManagedIdentity.IsHex(manifest.YeniseiSysConfigSha256, 64);
        }

        private static bool OperationIdsAreValid(
            string currentOperationId,
            string lastCompletedOperationId)
        {
            bool currentValid = string.IsNullOrEmpty(currentOperationId)
                ? ProvisionerCommandLine.IsGuidN(lastCompletedOperationId)
                : ProvisionerCommandLine.IsGuidN(currentOperationId);
            return currentValid &&
                (string.IsNullOrEmpty(lastCompletedOperationId) ||
                 ProvisionerCommandLine.IsGuidN(lastCompletedOperationId));
        }

        private static bool PortsAreValidAndDistinct(params int[] values)
        {
            HashSet<int> ports = new HashSet<int>();
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] < 1 || values[index] > 65535 ||
                    !ports.Add(values[index])) return false;
            }
            return true;
        }

        private static bool IsSafeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) ||
                value.IndexOf(':') >= 0 || value.IndexOf('\0') >= 0)
            {
                return false;
            }
            string normalized = NormalizeRelative(value);
            return normalized != "." && normalized != ".." &&
                !normalized.StartsWith("..\\", StringComparison.Ordinal) &&
                !normalized.Contains("\\..\\") &&
                !normalized.EndsWith("\\..", StringComparison.Ordinal);
        }

        private static string NormalizeRelative(string value)
        {
            return value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
        }

        private static bool ContainsControl(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsControl(value[index])) return true;
            }
            return false;
        }

        private static bool PathEquals(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRoot(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
            {
                throw new ArgumentException("An absolute path is required.", parameterName);
            }
            return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
        }

        private static void ValidateRuntimeId(string runtimeId)
        {
            if (!LocalModuleManagedIdentity.IsRuntimeId(runtimeId))
            {
                throw new ArgumentException("Runtime id is invalid.", "runtimeId");
            }
        }

        private static void ValidateInstanceId(string instanceId)
        {
            if (!LocalModuleManagedIdentity.IsInstanceId(instanceId))
            {
                throw new ArgumentException("Instance id is invalid.", "instanceId");
            }
        }

        private static void ValidateOperationId(string operationId)
        {
            if (!ProvisionerCommandLine.IsGuidN(operationId))
            {
                throw new ArgumentException("Operation id is invalid.", "operationId");
            }
        }
    }
}
