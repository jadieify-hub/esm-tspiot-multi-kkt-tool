using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class ManagedServiceManifestStore
    {
        private readonly string _root;
        private readonly string _inventoryRoot;
        private readonly string _profilesRoot;
        private readonly IPathSafety _pathSafety;
        private readonly string _initiatingSid;

        internal ManagedServiceManifestStore(
            string root,
            IPathSafety pathSafety,
            string initiatingSid)
        {
            _root = Path.GetFullPath(root);
            _inventoryRoot = Path.Combine(_root, "Inventory");
            _profilesRoot = Path.Combine(_root, "Profiles");
            _pathSafety = pathSafety ?? throw new ArgumentNullException("pathSafety");
            _initiatingSid = initiatingSid;
        }

        internal static ManagedServiceManifestStore CreateMachineStore(
            IPathSafety pathSafety,
            string initiatingSid)
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "KRS",
                "MultiKKT");
            return new ManagedServiceManifestStore(root, pathSafety, initiatingSid);
        }

        internal string GetManifestPath(string kktSerial)
        {
            string serviceName = LmServiceIdentity.CreateName(kktSerial);
            return Path.Combine(_inventoryRoot, serviceName, "manifest.json");
        }

        internal string GetProfileRoot(string kktSerial)
        {
            string serviceName = LmServiceIdentity.CreateName(kktSerial);
            return Path.Combine(_profilesRoot, serviceName);
        }

        internal string EnsureProfileRoot(string kktSerial, string serviceSid)
        {
            string path = GetProfileRoot(kktSerial);
            EnsureStructuralSafety(path);
            if (Directory.Exists(path))
            {
                EnsureProtectedSafety(path, serviceSid);
            }
            if (Directory.Exists(_root))
            {
                EnsureProtectedSafety(_root, null);
            }
            _pathSafety.EnsureProtectedDirectory(
                _root,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            _pathSafety.EnsureProtectedDirectory(
                _profilesRoot,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            _pathSafety.EnsureProtectedDirectory(
                path,
                ProtectedDirectoryKind.Profile,
                null,
                serviceSid);
            EnsureProtectedSafety(path, serviceSid);
            return path;
        }

        internal void Write(ManagedServiceManifest manifest)
        {
            ValidateManifest(manifest);
            string path = GetManifestPath(manifest.KktSerial);
            EnsureSafe(path);
            if (File.Exists(path))
            {
                ValidateManifest(ReadRaw(path));
            }

            string directory = Path.GetDirectoryName(path);
            _pathSafety.EnsureProtectedDirectory(
                _root,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            _pathSafety.EnsureProtectedDirectory(
                _inventoryRoot,
                ProtectedDirectoryKind.Inventory,
                _initiatingSid,
                null);
            _pathSafety.EnsureProtectedDirectory(
                directory,
                ProtectedDirectoryKind.Inventory,
                _initiatingSid,
                null);
            EnsureSafe(path);

            byte[] payload = AtomicJsonFile.Serialize(manifest);
            AtomicJsonFile.Write(path, payload);
            EnsureSafe(path);
        }

        internal ManagedServiceManifest Read(string kktSerial)
        {
            string path = GetManifestPath(kktSerial);
            EnsureSafe(path);
            ManagedServiceManifest manifest = ReadRaw(path);
            ValidateManifest(manifest);
            if (!string.Equals(manifest.KktSerial, kktSerial, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Manifest identity does not match its derived directory.");
            }
            return manifest;
        }

        internal LmServiceInventoryItem ReadProjection(string kktSerial)
        {
            string path = GetManifestPath(kktSerial);
            ManagedServiceManifest manifest = Read(kktSerial);
            return new LmServiceInventoryItem
            {
                KktSerial = manifest.KktSerial,
                ServiceName = manifest.ServiceName,
                Role = LmServiceRole.Managed,
                Ports = new LmGatewayPorts(manifest.GrpcPort, manifest.RestPort),
                Target = new LmGatewayTarget(manifest.TargetAddress, manifest.TargetPort),
                Status = MapStatus(manifest.LocalLifecycleState),
                ManifestFingerprint = new LmManifestFingerprint
                {
                    Sha256 = ComputeFileSha256(path)
                }
            };
        }

        internal void Delete(string kktSerial)
        {
            string path = GetManifestPath(kktSerial);
            EnsureSafe(path);
            ManagedServiceManifest existing = Read(kktSerial);
            ValidateManifest(existing);
            File.Delete(path);
            string directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory, false);
            }
        }

        private ManagedServiceManifest ReadRaw(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(ManagedServiceManifest));
                return (ManagedServiceManifest)serializer.ReadObject(stream);
            }
        }

        private void EnsureSafe(string path)
        {
            ValidationResult validation = _pathSafety.ValidateProtected(path, _root, null);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
        }

        private void EnsureStructuralSafety(string path)
        {
            ValidationResult validation = _pathSafety.Validate(path, _root);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
        }

        private void EnsureProtectedSafety(string path, string allowedWriterSid)
        {
            ValidationResult validation = _pathSafety.ValidateProtected(path, _root, allowedWriterSid);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
        }

        private static void ValidateManifest(ManagedServiceManifest manifest)
        {
            if (manifest == null ||
                manifest.SchemaVersion != ManagedServiceManifest.CurrentSchemaVersion ||
                !string.Equals(
                    manifest.OwnershipMarker,
                    ManagedServiceManifest.ExpectedOwnershipMarker,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Manifest schema or ownership marker is invalid.");
            }
            string expectedServiceName = LmServiceIdentity.CreateName(manifest.KktSerial);
            if (!string.Equals(manifest.ServiceName, expectedServiceName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Manifest service identity is not app-owned.");
            }
            if (manifest.GrpcPort < 1 || manifest.GrpcPort > 65535 ||
                manifest.RestPort < 1 || manifest.RestPort > 65535 ||
                manifest.GrpcPort == manifest.RestPort ||
                manifest.TargetPort < 1 || manifest.TargetPort > 65535)
            {
                throw new InvalidDataException("Manifest ports are invalid.");
            }
            ValidationResult target = LmGatewayInputValidator.ValidateTarget(
                new LmGatewayTarget(manifest.TargetAddress, manifest.TargetPort));
            if (!target.IsValid)
            {
                throw new InvalidDataException(target.JoinMessages());
            }
            if (!IsHex(manifest.ControllerBinarySha256, 64) ||
                !IsHex(manifest.SupervisorSha256, 64) ||
                !ProvisionerCommandLine.IsGuidN(manifest.OperationId) ||
                string.IsNullOrWhiteSpace(manifest.ServiceSid) ||
                string.IsNullOrWhiteSpace(manifest.ControllerVersion) ||
                string.IsNullOrWhiteSpace(manifest.UpdatedUtc))
            {
                throw new InvalidDataException("Manifest trust or lifecycle metadata is invalid.");
            }
        }

        private static LmServiceProvisioningStatus MapStatus(ManagedServiceLifecycleState state)
        {
            if (state == ManagedServiceLifecycleState.CleanupPending ||
                state == ManagedServiceLifecycleState.Cleaning ||
                state == ManagedServiceLifecycleState.Deleting)
            {
                return LmServiceProvisioningStatus.CleanupPending;
            }
            if (state == ManagedServiceLifecycleState.VersionVerificationPending)
            {
                return LmServiceProvisioningStatus.VersionVerificationPending;
            }
            if (state == ManagedServiceLifecycleState.ServiceReady)
            {
                return LmServiceProvisioningStatus.Succeeded;
            }
            return LmServiceProvisioningStatus.RequiresAttention;
        }

        private static string ComputeFileSha256(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    text.Append(hash[index].ToString("x2"));
                }
                return text.ToString();
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

    internal static class AtomicJsonFile
    {
        internal static byte[] Serialize<T>(T value)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(memory, value);
                return memory.ToArray();
            }
        }

        internal static void Write(string path, byte[] payload)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
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
                {
                    File.Replace(temporary, path, null, true);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }
}
