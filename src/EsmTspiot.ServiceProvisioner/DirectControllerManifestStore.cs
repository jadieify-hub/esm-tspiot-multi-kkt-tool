using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class DirectControllerManifestStore
    {
        private readonly string _root;
        private readonly string _ownedRoot;
        private readonly string _inventoryRoot;
        private readonly string _profilesRoot;
        private readonly string _officialEnvironmentRoot;
        private readonly IPathSafety _pathSafety;
        private readonly string _initiatingSid;
        private readonly AtomicFileWriter _writer;

        internal DirectControllerManifestStore(
            string root,
            IPathSafety pathSafety,
            string initiatingSid)
            : this(
                root,
                pathSafety,
                initiatingSid,
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
        {
        }

        internal DirectControllerManifestStore(
            string root,
            IPathSafety pathSafety,
            string initiatingSid,
            string officialEnvironmentRoot)
        {
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            _root = Path.GetFullPath(root);
            _ownedRoot = Path.Combine(_root, "Owned");
            _inventoryRoot = Path.Combine(_root, "Inventory");
            _profilesRoot = Path.Combine(_root, "Profiles");
            _officialEnvironmentRoot = Path.GetFullPath(officialEnvironmentRoot);
            _pathSafety = pathSafety;
            _initiatingSid = initiatingSid;
            _writer = new AtomicFileWriter();
        }

        internal static DirectControllerManifestStore CreateMachineStore(
            IPathSafety pathSafety,
            string initiatingSid)
        {
            return new DirectControllerManifestStore(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "KRS",
                    "MultiKKT",
                    "DirectControllers"),
                pathSafety,
                initiatingSid);
        }

        internal string GetProfileEnvironmentRoot(int ordinal)
        {
            DirectControllerIdentity.ServiceNameForOrdinal(ordinal);
            if (ordinal == 1)
            {
                return _officialEnvironmentRoot;
            }
            return Path.Combine(_profilesRoot, "controller-" + ordinal.ToString());
        }

        internal string GetEsmConfigBackupPath(string kktSerial)
        {
            return Path.Combine(GetOwnedItemRoot(kktSerial), "esm-config-original.yml");
        }

        internal void ProtectOwnedFile(string path)
        {
            RequireProtected(path);
            _pathSafety.EnsureProtectedRuntimeFile(path);
            RequireProtected(path);
        }

        internal string EnsureProfileEnvironmentRoot(int ordinal)
        {
            if (ordinal == 1)
            {
                throw new InvalidOperationException(
                    "The official base controller profile is vendor-owned.");
            }
            string path = GetProfileEnvironmentRoot(ordinal);
            EnsureProtectedContainer(_root);
            EnsureProtectedContainer(_profilesRoot);
            _pathSafety.EnsureProtectedDirectory(
                path,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            RequireProtected(path);
            return path;
        }

        internal void DeleteCloneProfileEnvironmentRoot(int ordinal)
        {
            if (ordinal < 2 || ordinal > DirectControllerIdentity.MaximumOrdinal)
            {
                throw new ArgumentOutOfRangeException("ordinal");
            }
            string path = GetProfileEnvironmentRoot(ordinal);
            ValidationResult validation = _pathSafety.ValidateProtected(path, _root, null);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
            if (!Directory.Exists(path)) return;
            string[] entries = Directory.GetFileSystemEntries(
                path,
                "*",
                SearchOption.AllDirectories);
            for (int index = 0; index < entries.Length; index++)
            {
                if ((File.GetAttributes(entries[index]) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Direct controller profile contains a reparse point.");
                }
            }
            string[] files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            for (int index = 0; index < files.Length; index++)
            {
                File.SetAttributes(files[index], FileAttributes.Normal);
            }
            Directory.Delete(path, true);
        }

        internal void Write(DirectControllerManifest manifest)
        {
            ValidateManifest(manifest);
            EnsureProtectedContainer(_root);
            EnsureProtectedContainer(_ownedRoot);
            string itemRoot = GetOwnedItemRoot(manifest.KktSerial);
            _pathSafety.EnsureProtectedDirectory(
                itemRoot,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            RequireProtected(itemRoot);
            _writer.WriteBytes(GetManifestPath(manifest.KktSerial), Serialize(manifest));
            RequireProtected(GetManifestPath(manifest.KktSerial));

            _pathSafety.EnsureProtectedDirectory(
                _inventoryRoot,
                ProtectedDirectoryKind.Inventory,
                _initiatingSid,
                null);
            DirectControllerInventoryProjection projection = Project(manifest);
            string inventoryPath = GetInventoryPath(manifest.KktSerial);
            _writer.WriteBytes(inventoryPath, Serialize(projection));
            _pathSafety.EnsureProtectedReadOnlyFile(
                inventoryPath,
                string.IsNullOrWhiteSpace(_initiatingSid)
                    ? null
                    : new[] { _initiatingSid });
        }

        internal DirectControllerManifest Read(string kktSerial)
        {
            ValidateSerial(kktSerial);
            string path = GetManifestPath(kktSerial);
            if (!File.Exists(path))
            {
                return null;
            }
            RequireProtected(path);
            DirectControllerManifest manifest = Deserialize<DirectControllerManifest>(path);
            NormalizeLegacyManifest(manifest);
            ValidateManifest(manifest);
            if (!string.Equals(manifest.KktSerial, kktSerial, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Direct controller manifest identity mismatch.");
            }
            return manifest;
        }

        internal void Delete(string kktSerial, string expectedFingerprint)
        {
            DirectControllerManifest manifest = Read(kktSerial);
            if (manifest == null)
            {
                return;
            }
            if (!string.Equals(
                    ComputeFingerprint(manifest),
                    expectedFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Direct controller removal fingerprint does not match current owned state.");
            }
            string manifestPath = GetManifestPath(kktSerial);
            File.SetAttributes(manifestPath, FileAttributes.Normal);
            File.Delete(manifestPath);
            string itemRoot = GetOwnedItemRoot(kktSerial);
            if (Directory.Exists(itemRoot) &&
                Directory.GetFileSystemEntries(itemRoot).Length == 0)
            {
                Directory.Delete(itemRoot);
            }
            string inventoryPath = GetInventoryPath(kktSerial);
            if (File.Exists(inventoryPath))
            {
                File.SetAttributes(inventoryPath, FileAttributes.Normal);
                File.Delete(inventoryPath);
            }
        }

        internal string ComputeFingerprint(DirectControllerManifest manifest)
        {
            ValidateManifest(manifest);
            string canonical = string.Join("\n", new[]
            {
                manifest.OwnershipMarker,
                manifest.KktSerial,
                manifest.KktInn,
                manifest.Ordinal.ToString(),
                manifest.ServiceName,
                manifest.GrpcPort.ToString(),
                manifest.RestPort.ToString(),
                manifest.TargetLocalModulePort.ToString(),
                manifest.ControllerVersion,
                manifest.ControllerBinarySha256.ToLowerInvariant(),
                Path.GetFullPath(manifest.ProfileEnvironmentRoot),
                manifest.EsmConfigOriginalSha256 ?? string.Empty,
                manifest.EsmConfigAppliedSha256 ?? string.Empty
            });
            using (SHA256 hash = SHA256.Create())
            {
                return ToHex(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
            }
        }

        private DirectControllerInventoryProjection Project(
            DirectControllerManifest manifest)
        {
            return new DirectControllerInventoryProjection
            {
                KktSerial = manifest.KktSerial,
                KktInn = manifest.KktInn,
                Ordinal = manifest.Ordinal,
                ServiceName = manifest.ServiceName,
                GrpcPort = manifest.GrpcPort,
                RestPort = manifest.RestPort,
                TargetLocalModulePort = manifest.TargetLocalModulePort,
                ControllerVersion = manifest.ControllerVersion,
                State = manifest.State,
                RemovalFingerprint = ComputeFingerprint(manifest)
            };
        }

        private void ValidateManifest(DirectControllerManifest manifest)
        {
            if (manifest == null ||
                manifest.SchemaVersion != DirectControllerManifest.CurrentSchemaVersion ||
                !string.Equals(
                    manifest.OwnershipMarker,
                    DirectControllerManifest.ExpectedOwnershipMarker,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Direct controller manifest schema or ownership is invalid.");
            }
            ValidateSerial(manifest.KktSerial);
            if (!IsInn(manifest.KktInn) ||
                !string.Equals(
                    manifest.ServiceName,
                    DirectControllerIdentity.ServiceNameForOrdinal(manifest.Ordinal),
                    StringComparison.Ordinal) ||
                manifest.GrpcPort != DirectControllerIdentity.GrpcPortForOrdinal(manifest.Ordinal) ||
                manifest.RestPort != DirectControllerIdentity.RestPortForOrdinal(manifest.Ordinal) ||
                manifest.LegacyFutureLocalModulePort != 0 ||
                manifest.TargetLocalModulePort < 1024 ||
                manifest.TargetLocalModulePort > 65535 ||
                DirectControllerIdentity.IsControllerPort(
                    manifest.TargetLocalModulePort) ||
                !string.Equals(manifest.ControllerVersion, "1.6.4.0", StringComparison.Ordinal) ||
                !IsHex(manifest.ControllerBinarySha256, 64) ||
                !ValidOptionalHashPair(
                    manifest.EsmConfigOriginalSha256,
                    manifest.EsmConfigAppliedSha256) ||
                !ProvisionerCommandLine.IsGuidN(manifest.OperationId) ||
                string.IsNullOrWhiteSpace(manifest.UpdatedUtc))
            {
                throw new InvalidDataException("Direct controller manifest values are invalid.");
            }
            string expectedProfile = GetProfileEnvironmentRoot(manifest.Ordinal);
            if (!string.Equals(
                    Path.GetFullPath(manifest.ProfileEnvironmentRoot),
                    expectedProfile,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Direct controller profile identity is invalid.");
            }
        }

        private static void NormalizeLegacyManifest(DirectControllerManifest manifest)
        {
            if (manifest == null || manifest.SchemaVersion != 1)
            {
                return;
            }
            if (!string.Equals(
                    manifest.OwnershipMarker,
                    DirectControllerManifest.ExpectedOwnershipMarker,
                    StringComparison.Ordinal) ||
                manifest.Ordinal < 1 ||
                manifest.Ordinal > DirectControllerIdentity.MaximumOrdinal ||
                manifest.TargetLocalModulePort != 0 ||
                manifest.LegacyFutureLocalModulePort !=
                    DirectControllerIdentity.FutureLmPortForOrdinal(manifest.Ordinal))
            {
                throw new InvalidDataException(
                    "Legacy direct controller manifest target is invalid.");
            }
            manifest.TargetLocalModulePort = manifest.LegacyFutureLocalModulePort;
            manifest.LegacyFutureLocalModulePort = 0;
            manifest.SchemaVersion = DirectControllerManifest.CurrentSchemaVersion;
        }

        private void EnsureProtectedContainer(string path)
        {
            _pathSafety.EnsureProtectedDirectory(
                path,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            RequireProtected(path);
        }

        private void RequireProtected(string path)
        {
            ValidationResult validation = _pathSafety.ValidateProtected(path, _root, null);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
        }

        private string GetOwnedItemRoot(string serial)
        {
            ValidateSerial(serial);
            return Path.Combine(_ownedRoot, serial);
        }

        private string GetManifestPath(string serial)
        {
            return Path.Combine(GetOwnedItemRoot(serial), "manifest.json");
        }

        private string GetInventoryPath(string serial)
        {
            ValidateSerial(serial);
            return Path.Combine(_inventoryRoot, serial + ".json");
        }

        private static byte[] Serialize<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        private static T Deserialize<T>(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                if (stream.Length <= 0 || stream.Length > 65536)
                {
                    throw new InvalidDataException("Direct controller manifest size is invalid.");
                }
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
        }

        private static void ValidateSerial(string value)
        {
            if (!IsDigits(value, 14))
            {
                throw new InvalidDataException("Direct controller KKT serial is invalid.");
            }
        }

        private static bool IsInn(string value)
        {
            return IsDigits(value, 10) || IsDigits(value, 12);
        }

        private static bool IsDigits(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9') return false;
            }
            return true;
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
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


        private static bool ValidOptionalHashPair(string first, string second)
        {
            bool bothEmpty = string.IsNullOrEmpty(first) && string.IsNullOrEmpty(second);
            return bothEmpty || (IsHex(first, 64) && IsHex(second, 64));
        }

        private static string ToHex(byte[] value)
        {
            StringBuilder result = new StringBuilder(value.Length * 2);
            for (int index = 0; index < value.Length; index++)
            {
                result.Append(value[index].ToString("x2"));
            }
            return result.ToString();
        }
    }
}
