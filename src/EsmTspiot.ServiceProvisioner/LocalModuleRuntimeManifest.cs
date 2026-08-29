using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal enum LocalModuleRuntimeLifecycleState
    {
        [EnumMember]
        Preparing = 1,
        [EnumMember]
        Ready = 2,
        [EnumMember]
        Deleting = 3,
        [EnumMember]
        CleanupPending = 4
    }

    [DataContract]
    internal sealed class LocalModuleRuntimeFile
    {
        internal LocalModuleRuntimeFile()
        {
        }

        internal LocalModuleRuntimeFile(string relativePath, long byteLength, string sha256)
        {
            RelativePath = relativePath;
            ByteLength = byteLength;
            Sha256 = sha256;
        }

        [DataMember(Order = 1)]
        internal string RelativePath { get; set; }

        [DataMember(Order = 2)]
        internal long ByteLength { get; set; }

        [DataMember(Order = 3)]
        internal string Sha256 { get; set; }
    }

    [DataContract]
    internal sealed class LocalModuleRuntimeManifest
    {
        internal const int CurrentSchemaVersion = 1;
        internal const string ExpectedOwnershipMarker =
            "KRS.MultiKKT.LocalModule.Runtime.v1";

        internal LocalModuleRuntimeManifest()
        {
            Files = new List<LocalModuleRuntimeFile>();
            Directories = new List<string>();
        }

        [DataMember(Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        internal string OwnershipMarker { get; set; }

        [DataMember(Order = 3)]
        internal string RuntimeId { get; set; }

        [DataMember(Order = 4)]
        internal string CapabilityId { get; set; }

        [DataMember(Order = 5)]
        internal string ProductName { get; set; }

        [DataMember(Order = 6)]
        internal string ProductVersion { get; set; }

        [DataMember(Order = 7)]
        internal string ProductCode { get; set; }

        [DataMember(Order = 8)]
        internal string UpgradeCode { get; set; }

        [DataMember(Order = 9)]
        internal string PackageFileName { get; set; }

        [DataMember(Order = 10)]
        internal long PackageByteLength { get; set; }

        [DataMember(Order = 11)]
        internal string PackageSha256 { get; set; }

        [DataMember(Order = 12)]
        internal string SignerSubject { get; set; }

        [DataMember(Order = 13)]
        internal string SignerThumbprint { get; set; }

        [DataMember(Order = 14)]
        internal string RuntimeRoot { get; set; }

        [DataMember(Order = 15)]
        internal string RequiredFileContractSha256 { get; set; }

        [DataMember(Order = 16)]
        internal List<LocalModuleRuntimeFile> Files { get; set; }

        [DataMember(Order = 17)]
        internal List<string> Directories { get; set; }

        [DataMember(Order = 18)]
        internal string OwnershipNonce { get; set; }

        [DataMember(Order = 19)]
        internal int ConfirmedReferenceCount { get; set; }

        [DataMember(Order = 20)]
        internal LocalModuleRuntimeLifecycleState State { get; set; }

        [DataMember(Order = 21)]
        internal string UpdatedUtc { get; set; }

        internal static LocalModuleRuntimeManifest Create(
            LocalModuleInstallerSelection package,
            LocalModuleCapabilityProfile capability,
            string runtimeRoot,
            string ownershipNonce,
            IEnumerable<LocalModuleRuntimeFile> files)
        {
            return Create(
                package,
                capability,
                runtimeRoot,
                ownershipNonce,
                files,
                LocalModuleManagedIdentity.DeriveRuntimeDirectories(files));
        }

        internal static LocalModuleRuntimeManifest Create(
            LocalModuleInstallerSelection package,
            LocalModuleCapabilityProfile capability,
            string runtimeRoot,
            string ownershipNonce,
            IEnumerable<LocalModuleRuntimeFile> files,
            IEnumerable<string> directories)
        {
            if (package == null) throw new ArgumentNullException("package");
            if (capability == null) throw new ArgumentNullException("capability");
            if (files == null) throw new ArgumentNullException("files");
            string runtimeId = LocalModuleManagedIdentity.CreateRuntimeId(
                capability.CapabilityId,
                package.Sha256);
            List<LocalModuleRuntimeFile> copiedFiles =
                LocalModuleManagedIdentity.CopyAndSortFiles(files);
            List<string> copiedDirectories =
                LocalModuleManagedIdentity.CopyAndSortDirectories(directories);
            return new LocalModuleRuntimeManifest
            {
                SchemaVersion = CurrentSchemaVersion,
                OwnershipMarker = ExpectedOwnershipMarker,
                RuntimeId = runtimeId,
                CapabilityId = capability.CapabilityId,
                ProductName = package.ProductName,
                ProductVersion = package.ProductVersion,
                ProductCode = package.ProductCode,
                UpgradeCode = package.UpgradeCode,
                PackageFileName = package.FileName,
                PackageByteLength = package.ByteLength,
                PackageSha256 = package.Sha256,
                SignerSubject = package.SignerSubject,
                SignerThumbprint = package.SignerThumbprint,
                RuntimeRoot = Path.GetFullPath(runtimeRoot).TrimEnd(Path.DirectorySeparatorChar),
                RequiredFileContractSha256 = capability.RuntimeContractSha256,
                Files = copiedFiles,
                Directories = copiedDirectories,
                OwnershipNonce = ownershipNonce,
                ConfirmedReferenceCount = 0,
                State = LocalModuleRuntimeLifecycleState.Ready,
                UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }
    }

    internal static class LocalModuleManagedIdentity
    {
        internal static string CreateRuntimeId(string capabilityId, string packageSha256)
        {
            if (string.IsNullOrWhiteSpace(capabilityId) || !IsHex(packageSha256, 64))
            {
                throw new ArgumentException("Runtime identity inputs are invalid.");
            }
            return "lmrt-" + HashPrefix(
                "multikkt-local-module-runtime-v1\n" + capabilityId + "\n" +
                packageSha256.ToLowerInvariant(),
                24);
        }

        internal static string CreateInstanceId(string inn, string ownershipNonce)
        {
            if ((!IsAsciiDigits(inn, 10) && !IsAsciiDigits(inn, 12)) ||
                !IsLowerHex(ownershipNonce, 32))
            {
                throw new ArgumentException("Local-module instance identity inputs are invalid.");
            }
            return "lmi-" + HashPrefix(
                "multikkt-local-module-instance-v1\n" + inn + "\n" + ownershipNonce,
                24);
        }

        internal static string CreateStackId(string kktSerial)
        {
            if (!IsAsciiDigits(kktSerial, 14))
            {
                throw new ArgumentException("KKT serial is invalid.", "kktSerial");
            }
            return "kkt-" + kktSerial;
        }

        internal static string CreateDatabaseServiceName(string instanceId)
        {
            ValidateInstanceId(instanceId);
            return "krs-lm-db-" + instanceId.Substring(4);
        }

        internal static string CreateApiServiceName(string instanceId)
        {
            ValidateInstanceId(instanceId);
            return "krs-lm-api-" + instanceId.Substring(4);
        }

        internal static bool IsRuntimeId(string value)
        {
            return value != null && value.StartsWith("lmrt-", StringComparison.Ordinal) &&
                IsLowerHex(value.Substring(5), 24);
        }

        internal static bool IsInstanceId(string value)
        {
            return value != null && value.StartsWith("lmi-", StringComparison.Ordinal) &&
                IsLowerHex(value.Substring(4), 24);
        }

        internal static bool IsStackId(string value)
        {
            return value != null && value.StartsWith("kkt-", StringComparison.Ordinal) &&
                IsAsciiDigits(value.Substring(4), 14);
        }

        internal static bool IsLowerHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        internal static bool IsHex(string value, int length)
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

        internal static bool IsAsciiDigits(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9') return false;
            }
            return true;
        }

        internal static List<LocalModuleRuntimeFile> CopyAndSortFiles(
            IEnumerable<LocalModuleRuntimeFile> files)
        {
            List<LocalModuleRuntimeFile> result = new List<LocalModuleRuntimeFile>();
            foreach (LocalModuleRuntimeFile file in files)
            {
                if (file == null) throw new InvalidDataException("Runtime file record is missing.");
                result.Add(new LocalModuleRuntimeFile(
                    file.RelativePath,
                    file.ByteLength,
                    file.Sha256));
            }
            result.Sort(delegate(LocalModuleRuntimeFile left, LocalModuleRuntimeFile right)
            {
                return string.Compare(
                    left.RelativePath,
                    right.RelativePath,
                    StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        internal static List<string> DeriveRuntimeDirectories(
            IEnumerable<LocalModuleRuntimeFile> files)
        {
            if (files == null) throw new ArgumentNullException("files");
            HashSet<string> result =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LocalModuleRuntimeFile file in files)
            {
                if (file == null) throw new InvalidDataException("Runtime file record is missing.");
                string current = Path.GetDirectoryName(file.RelativePath);
                while (!string.IsNullOrEmpty(current))
                {
                    result.Add(NormalizeRelative(current));
                    current = Path.GetDirectoryName(current);
                }
            }
            return CopyAndSortDirectories(result);
        }

        internal static List<string> CopyAndSortDirectories(
            IEnumerable<string> directories)
        {
            if (directories == null) throw new ArgumentNullException("directories");
            HashSet<string> seen =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> result = new List<string>();
            foreach (string directory in directories)
            {
                string normalized = NormalizeRelative(directory);
                if (!IsSafeRelativePath(normalized) || !seen.Add(normalized))
                {
                    throw new InvalidDataException("Runtime directory inventory is invalid.");
                }
                result.Add(normalized);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        internal static string NormalizeRelative(string value)
        {
            return (value ?? string.Empty)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Trim(Path.DirectorySeparatorChar);
        }

        internal static bool IsSafeRelativePath(string value)
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

        internal static string ComputeSha256(byte[] content)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(content);
                StringBuilder text = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    text.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                return text.ToString();
            }
        }

        internal static string ComputeFileSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(stream);
                StringBuilder text = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    text.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                return text.ToString();
            }
        }

        private static string HashPrefix(string value, int characterCount)
        {
            string hash = ComputeSha256(Encoding.UTF8.GetBytes(value));
            return hash.Substring(0, characterCount);
        }

        private static void ValidateInstanceId(string instanceId)
        {
            if (!IsInstanceId(instanceId))
            {
                throw new ArgumentException("Local-module instance id is invalid.", "instanceId");
            }
        }
    }
}
