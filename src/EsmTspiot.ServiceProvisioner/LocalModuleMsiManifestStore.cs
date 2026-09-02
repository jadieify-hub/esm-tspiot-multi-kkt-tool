using System;
using System.IO;
using System.Runtime.Serialization.Json;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiManifestStore
    {
        private const string ManifestFileName = "manifest.json";
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
            EnsureSafe(path);
            LocalModuleMsiManifest manifest = ReadRaw(path);
            LocalModuleMsiManifest.Validate(manifest);
            if (!string.Equals(manifest.Inn, inn, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Local-module MSI manifest identity mismatch.");
            return manifest;
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
