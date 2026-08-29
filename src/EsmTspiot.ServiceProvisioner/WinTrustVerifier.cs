using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class WinTrustVerifier : IFileTrustVerifier
    {
        private const string CodeSigningEku = "1.3.6.1.5.5.7.3.3";

        public FileTrustResult Verify(string path, TrustedFileExpectation expectation)
        {
            if (expectation == null)
            {
                return FileTrustResult.Rejected("File trust expectation is missing.");
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                FileInfo file = new FileInfo(fullPath);
                if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return FileTrustResult.Rejected("Expected regular non-reparse file was not found.");
                }

                FileVersionInfo version = FileVersionInfo.GetVersionInfo(fullPath);
                X509Certificate2 signer = ReadSigner(fullPath);
                TrustedFileExpectation observed = new TrustedFileExpectation
                {
                    FileName = file.Name,
                    ByteLength = file.Length,
                    Sha256 = ComputeSha256(fullPath),
                    FileVersion = Normalize(version.FileVersion),
                    ProductVersion = Normalize(version.ProductVersion),
                    ProductName = Normalize(version.ProductName),
                    CompanyName = Normalize(version.CompanyName),
                    Machine = ReadMachine(fullPath),
                    SignerSubject = signer == null ? string.Empty : Normalize(signer.Subject),
                    SignerThumbprint = signer == null ? string.Empty : Normalize(signer.Thumbprint),
                    RequireCodeSigningEku = signer != null && HasCodeSigningEku(signer)
                };

                if (!VerifyAuthenticode(fullPath))
                {
                    return FileTrustResult.Rejected("Authenticode chain validation failed.");
                }
                if (!Matches(expectation, observed))
                {
                    return FileTrustResult.Rejected("File hash, metadata, architecture or signer does not match the capability profile.");
                }

                return FileTrustResult.Trusted(fullPath, observed);
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException ||
                    ex is ArgumentException || ex is InvalidDataException)
                {
                    return FileTrustResult.Rejected("File trust verification failed: " + ex.GetType().Name + ".");
                }
                throw;
            }
        }

        private static bool Matches(
            TrustedFileExpectation expected,
            TrustedFileExpectation observed)
        {
            return string.Equals(expected.FileName, observed.FileName, StringComparison.Ordinal) &&
                expected.ByteLength == observed.ByteLength &&
                FixedTimeEqualsHex(expected.Sha256, observed.Sha256) &&
                string.Equals(Normalize(expected.FileVersion), observed.FileVersion, StringComparison.Ordinal) &&
                string.Equals(Normalize(expected.ProductVersion), observed.ProductVersion, StringComparison.Ordinal) &&
                string.Equals(Normalize(expected.ProductName), observed.ProductName, StringComparison.Ordinal) &&
                string.Equals(Normalize(expected.CompanyName), observed.CompanyName, StringComparison.Ordinal) &&
                expected.Machine == observed.Machine &&
                string.Equals(Normalize(expected.SignerSubject), observed.SignerSubject, StringComparison.Ordinal) &&
                string.Equals(Normalize(expected.SignerThumbprint), observed.SignerThumbprint, StringComparison.OrdinalIgnoreCase) &&
                (!expected.RequireCodeSigningEku || observed.RequireCodeSigningEku);
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    text.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                return text.ToString();
            }
        }

        private static PeMachine ReadMachine(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt16() != 0x5A4D)
                {
                    return PeMachine.Unknown;
                }
                stream.Position = 0x3c;
                int peOffset = reader.ReadInt32();
                if (peOffset < 0 || peOffset > stream.Length - 6)
                {
                    return PeMachine.Unknown;
                }
                stream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550)
                {
                    return PeMachine.Unknown;
                }
                return (PeMachine)reader.ReadUInt16();
            }
        }

        private static X509Certificate2 ReadSigner(string path)
        {
            X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
            return certificate == null ? null : new X509Certificate2(certificate);
        }

        private static bool HasCodeSigningEku(X509Certificate2 certificate)
        {
            for (int extensionIndex = 0; extensionIndex < certificate.Extensions.Count; extensionIndex++)
            {
                X509Extension extension = certificate.Extensions[extensionIndex];
                if (!string.Equals(extension.Oid.Value, "2.5.29.37", StringComparison.Ordinal))
                {
                    continue;
                }

                X509EnhancedKeyUsageExtension eku =
                    new X509EnhancedKeyUsageExtension(extension, extension.Critical);
                for (int oidIndex = 0; oidIndex < eku.EnhancedKeyUsages.Count; oidIndex++)
                {
                    if (string.Equals(
                        eku.EnhancedKeyUsages[oidIndex].Value,
                        CodeSigningEku,
                        StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool VerifyAuthenticode(string path)
        {
            Guid policy = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            WinTrustFileInfo fileInfo = new WinTrustFileInfo(path);
            WinTrustData data = new WinTrustData(fileInfo);
            try
            {
                return WinVerifyTrust(IntPtr.Zero, ref policy, ref data) == 0;
            }
            finally
            {
                data.Dispose();
                fileInfo.Dispose();
            }
        }

        private static bool FixedTimeEqualsHex(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= char.ToUpperInvariant(left[index]) ^ char.ToUpperInvariant(right[index]);
            }
            return difference == 0;
        }

        private static string Normalize(string value)
        {
            return value ?? string.Empty;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(
            IntPtr windowHandle,
            ref Guid actionId,
            ref WinTrustData data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo : IDisposable
        {
            private uint StructSize;
            private IntPtr FilePath;
            private IntPtr FileHandle;
            private IntPtr KnownSubject;

            internal WinTrustFileInfo(string path)
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
                FilePath = Marshal.StringToCoTaskMemUni(path);
                FileHandle = IntPtr.Zero;
                KnownSubject = IntPtr.Zero;
            }

            public void Dispose()
            {
                if (FilePath != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(FilePath);
                    FilePath = IntPtr.Zero;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData : IDisposable
        {
            private uint StructSize;
            private IntPtr PolicyCallbackData;
            private IntPtr SipClientData;
            private uint UiChoice;
            private uint RevocationChecks;
            private uint UnionChoice;
            private IntPtr FileInfoPointer;
            private uint StateAction;
            private IntPtr StateData;
            private IntPtr UrlReference;
            private uint ProviderFlags;
            private uint UiContext;

            internal WinTrustData(WinTrustFileInfo fileInfo)
            {
                StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
                PolicyCallbackData = IntPtr.Zero;
                SipClientData = IntPtr.Zero;
                UiChoice = 2;
                RevocationChecks = 0;
                UnionChoice = 1;
                FileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(fileInfo, FileInfoPointer, false);
                StateAction = 0;
                StateData = IntPtr.Zero;
                UrlReference = IntPtr.Zero;
                ProviderFlags = 0;
                UiContext = 0;
            }

            public void Dispose()
            {
                if (FileInfoPointer != IntPtr.Zero)
                {
                    Marshal.DestroyStructure(FileInfoPointer, typeof(WinTrustFileInfo));
                    Marshal.FreeCoTaskMem(FileInfoPointer);
                    FileInfoPointer = IntPtr.Zero;
                }
            }
        }
    }
}
