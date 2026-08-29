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
                dialog.Title = "Выберите установщик ЛМ ЧЗ";
                dialog.Filter =
                    "Поддерживаемый ЛМ ЧЗ (regime-2.6.1-7.msi)|" +
                    "regime-2.6.1-7.msi|Windows Installer (*.msi)|*.msi";
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
            string sha256 = ComputeSha256(fullPath);
            if (!string.Equals(
                    file.Name,
                    SupportedLocalModulePackageIdentity.FileName,
                    StringComparison.Ordinal) ||
                file.Length != SupportedLocalModulePackageIdentity.ByteLength ||
                !CanonicalLmPlanHasher.FixedTimeEqualsHex(
                    sha256,
                    SupportedLocalModulePackageIdentity.Sha256))
            {
                throw new InvalidDataException(
                    "Этот MSI не совпадает с проверенным regime-2.6.1-7.msi.");
            }

            X509Certificate certificate =
                X509Certificate.CreateFromSignedFile(fullPath);
            using (X509Certificate2 signer = certificate == null
                ? null
                : new X509Certificate2(certificate))
            {
                if (signer == null ||
                    !SupportedLocalModulePackageIdentity
                        .MatchesSignerSubject(signer.Subject) ||
                    !string.Equals(
                        signer.Thumbprint,
                        SupportedLocalModulePackageIdentity.SignerThumbprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Цифровая подпись MSI не совпадает с проверенным пакетом.");
                }
            }

            return SupportedLocalModulePackageIdentity.Create(
                fullPath,
                false);
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
