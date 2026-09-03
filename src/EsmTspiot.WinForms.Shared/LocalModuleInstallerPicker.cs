using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal static class LocalModuleInstallerPicker
    {
        internal static LocalModuleInstallerSelection SelectAndInspect(
            IWin32Window owner)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Выберите установщик ЛМ ЧЗ от ЦРПТ";
                dialog.Filter = "Windows Installer (*.msi)|*.msi";
                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.Multiselect = false;
                dialog.RestoreDirectory = true;
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                {
                    return null;
                }
                return Inspect(dialog.FileName);
            }
        }

        internal static LocalModuleInstallerSelection Inspect(string path)
        {
            string fullPath = Path.GetFullPath(path);
            FileInfo file = new FileInfo(fullPath);
            if (!file.Exists ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Выберите обычный локальный MSI-файл не через ссылку.");
            }

            string signerSubject;
            string signerThumbprint;
            ReadSigner(fullPath, out signerSubject, out signerThumbprint);

            LocalModuleInstallerSelection selection =
                new LocalModuleInstallerSelection
                {
                    SourcePath = fullPath,
                    FileName = file.Name,
                    ByteLength = file.Length,
                    Sha256 = ComputeSha256(fullPath),
                    ProductName = LocalModuleMsiPropertyReader
                        .ReadRequiredProperty(fullPath, "ProductName"),
                    ProductVersion = LocalModuleMsiPropertyReader
                        .ReadRequiredProperty(fullPath, "ProductVersion"),
                    ProductCode = LocalModuleMsiPropertyReader
                        .ReadRequiredProperty(fullPath, "ProductCode"),
                    UpgradeCode = LocalModuleMsiPropertyReader
                        .ReadRequiredProperty(fullPath, "UpgradeCode"),
                    SignerSubject = signerSubject,
                    SignerThumbprint = signerThumbprint,
                    LicenseNoticeAccepted = false
                };

            ValidationResult policy =
                LocalModulePackagePolicy.Evaluate(selection);
            if (!policy.IsValid)
            {
                throw new InvalidDataException(
                    "Это не пакет ЛМ ЧЗ от ЦРПТ. " + policy.JoinMessages());
            }
            return selection;
        }

        private static void ReadSigner(
            string fullPath,
            out string subject,
            out string thumbprint)
        {
            X509Certificate certificate;
            try
            {
                certificate = X509Certificate.CreateFromSignedFile(fullPath);
            }
            catch (CryptographicException)
            {
                throw new InvalidDataException(
                    "MSI не подписан или подпись нечитаема.");
            }
            if (certificate == null)
            {
                throw new InvalidDataException(
                    "MSI не подписан или подпись нечитаема.");
            }
            using (X509Certificate2 signer = new X509Certificate2(certificate))
            {
                subject = signer.Subject ?? string.Empty;
                thumbprint = signer.Thumbprint ?? string.Empty;
            }
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
                byte[] digest = algorithm.ComputeHash(stream);
                StringBuilder result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    result.Append(digest[index].ToString(
                        "x2",
                        CultureInfo.InvariantCulture));
                }
                return result.ToString();
            }
        }
    }
}
