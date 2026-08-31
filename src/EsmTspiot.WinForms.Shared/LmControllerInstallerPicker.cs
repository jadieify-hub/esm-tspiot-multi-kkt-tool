using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.WinForms.Shared
{
    internal static class LmControllerInstallerPicker
    {
        internal static LmControllerInstallerSelection SelectAndInspect(IWin32Window owner)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Выберите установщик контроллера ЛМ ЧЗ";
                dialog.Filter =
                    "Установщик контроллера ЛМ (esm-lm-controller_*-windows-setup.exe)|" +
                    "esm-lm-controller_*-windows-setup.exe|Исполняемые файлы (*.exe)|*.exe";
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

        internal static LmControllerInstallerSelection Inspect(string path)
        {
            string fullPath = Path.GetFullPath(path);
            FileInfo file = new FileInfo(fullPath);
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Выбран обычный локальный файл не через ссылку.");
            }
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(fullPath);
            X509Certificate certificate = X509Certificate.CreateFromSignedFile(fullPath);
            using (X509Certificate2 signer = certificate == null
                ? null
                : new X509Certificate2(certificate))
            {
                return new LmControllerInstallerSelection
                {
                    SourcePath = fullPath,
                    FileName = file.Name,
                    ByteLength = file.Length,
                    Sha256 = ComputeSha256(fullPath),
                    FileVersion = version.FileVersion ?? string.Empty,
                    ProductVersion = version.ProductVersion ?? string.Empty,
                    SignerSubject = signer == null ? string.Empty : signer.Subject ?? string.Empty,
                    SignerThumbprint = signer == null ? string.Empty : signer.Thumbprint ?? string.Empty,
                    StopManagedInstancesWarningAccepted = false
                };
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
                    result.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                return result.ToString();
            }
        }
    }
}
