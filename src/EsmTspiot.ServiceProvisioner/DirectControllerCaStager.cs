using System;
using System.IO;
using System.Text;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class DirectControllerCaStager
    {
        private readonly AtomicFileWriter _writer;
        private readonly IPathSafety _pathSafety;
        private readonly string _officialProfileRoot;

        internal DirectControllerCaStager(
            ControllerCapabilityProfile profile,
            AtomicFileWriter writer,
            IPathSafety pathSafety)
            : this(
                profile,
                writer,
                pathSafety,
                ResolveOfficialProfileRoot(profile))
        {
        }

        internal DirectControllerCaStager(
            ControllerCapabilityProfile profile,
            AtomicFileWriter writer,
            IPathSafety pathSafety,
            string officialProfileRoot)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (writer == null) throw new ArgumentNullException("writer");
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            if (!string.Equals(profile.Version, "1.6.4.0", StringComparison.Ordinal) ||
                !string.Equals(
                    profile.VendorProfileRelativePath,
                    Path.Combine("ESP", "lmcontroller"),
                    StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    "The direct controller CA profile is not characterized.");
            }
            _writer = writer;
            _pathSafety = pathSafety;
            _officialProfileRoot = NormalizeExistingRoot(
                officialProfileRoot,
                "Official controller profile root");
        }

        internal void Stage(string cloneProfileRoot)
        {
            string cloneRoot = NormalizeExistingRoot(
                cloneProfileRoot,
                "Direct controller profile root");

            StageFile(_officialProfileRoot, cloneRoot, "ca.crt", false);
            StageFile(_officialProfileRoot, cloneRoot, "ca.pem", true);
        }

        private void StageFile(
            string officialRoot,
            string cloneRoot,
            string fileName,
            bool privateKey)
        {
            string source = Path.GetFullPath(Path.Combine(officialRoot, fileName));
            string destination = Path.GetFullPath(Path.Combine(cloneRoot, fileName));
            if (!PathSafety.IsUnderRoot(source, officialRoot) ||
                !PathSafety.IsUnderRoot(destination, cloneRoot))
            {
                throw new InvalidDataException(
                    "Controller CA path escapes its fixed profile root.");
            }

            RequireProtectedPath(source, officialRoot);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    "Official controller certificate authority file is unavailable.",
                    source);
            }
            byte[] payload = ReadBoundedFile(source);
            ValidatePem(payload, privateKey);

            RequireProtectedPath(destination, cloneRoot);
            try
            {
                if (File.Exists(destination))
                {
                    FileAttributes attributes = File.GetAttributes(destination);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException(
                            "Direct controller certificate authority file is a reparse point.");
                    }
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(
                            destination,
                            attributes & ~FileAttributes.ReadOnly);
                    }
                }
                _writer.WriteBytes(destination, payload);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new UnauthorizedAccessException(
                    "Не удалось заменить CA-файл прямого контроллера: " +
                    destination + ".",
                    ex);
            }
            RequireProtectedPath(destination, cloneRoot);
        }

        private void RequireProtectedPath(string path, string root)
        {
            ValidationResult validation = _pathSafety.ValidateProtected(path, root, null);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
        }

        private static byte[] ReadBoundedFile(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                if (stream.Length < 32 || stream.Length > 65536)
                {
                    throw new InvalidDataException(
                        "Official controller certificate authority file size is invalid.");
                }
                byte[] payload = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < payload.Length)
                {
                    int read = stream.Read(payload, offset, payload.Length - offset);
                    if (read == 0)
                    {
                        throw new EndOfStreamException(
                            "Official controller certificate authority file changed while reading.");
                    }
                    offset += read;
                }
                return payload;
            }
        }

        private static void ValidatePem(byte[] payload, bool privateKey)
        {
            string pem = Encoding.ASCII.GetString(payload);
            bool valid = privateKey
                ? (pem.IndexOf(
                       "-----BEGIN PRIVATE KEY-----",
                       StringComparison.Ordinal) >= 0 ||
                   pem.IndexOf(
                       "-----BEGIN RSA PRIVATE KEY-----",
                       StringComparison.Ordinal) >= 0)
                : pem.IndexOf(
                    "-----BEGIN CERTIFICATE-----",
                    StringComparison.Ordinal) >= 0;
            if (!valid)
            {
                throw new InvalidDataException(
                    "Official controller certificate authority file format is invalid.");
            }
        }

        private static string NormalizeExistingRoot(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(name + " is required.", "value");
            }
            string fullPath = Path.GetFullPath(value);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException(name + " does not exist: " + fullPath);
            }
            return fullPath;
        }

        private static string ResolveOfficialProfileRoot(
            ControllerCapabilityProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }
            string programData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, profile.VendorProfileRelativePath);
        }
    }
}
