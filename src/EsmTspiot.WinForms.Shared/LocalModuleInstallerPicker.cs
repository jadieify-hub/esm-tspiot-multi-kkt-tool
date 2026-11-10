using System;
using System.Globalization;
using System.Drawing;
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
        internal static LocalModuleInstallerSelection ConfirmForInstallation(
            IWin32Window owner, LocalModuleInstallerSelection selection)
        {
            if (selection != null && File.Exists(selection.SourcePath))
                selection = Inspect(selection.SourcePath);
            return ConfirmSelection(selection,
                delegate { return SelectAndInspect(owner); },
                delegate(LocalModuleInstallerSelection selected)
                {
                    using (var dialog = new LocalModuleInstallerConfirmationDialog(selected))
                        return dialog.ShowDialog(owner);
                });
        }

        internal static LocalModuleInstallerSelection ConfirmSelection(
            LocalModuleInstallerSelection selection,
            Func<LocalModuleInstallerSelection> choose,
            Func<LocalModuleInstallerSelection, DialogResult> confirm)
        {
            while (true)
            {
                if (selection == null || !File.Exists(selection.SourcePath))
                    selection = choose();
                if (selection == null) return null;
                DialogResult result = confirm(selection);
                if (result == DialogResult.OK) return selection;
                if (result != DialogResult.Retry) return null;
                selection = null;
            }
        }

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

    internal sealed class LocalModuleInstallerConfirmationDialog : Form
    {
        internal LocalModuleInstallerConfirmationDialog(LocalModuleInstallerSelection selection)
        {
            Text = "Установка ЛМ ЧЗ — выбранный MSI";
            Icon = OperatorTheme.AppIcon;
            Font = new Font("Segoe UI", 10F);
            ForeColor = OperatorTheme.Ink;
            BackColor = OperatorTheme.Surface;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = ShowInTaskbar = false;
            ClientSize = new Size(720, 280);
            MinimumSize = new Size(640, 310);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill,
                Padding = new Padding(12), ColumnCount = 1, RowCount = 5 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill,
                Text = "Локальный ЛМ ЧЗ будет установлен из официального пакета ЦРПТ.",
                Margin = new Padding(0, 0, 0, 12) }, 0, 0);
            layout.Controls.Add(new Label { Name = "lm-msi-file", AutoSize = true,
                Dock = DockStyle.Fill, Text = "Файл: " + selection.FileName }, 0, 1);
            layout.Controls.Add(new Label { Name = "lm-msi-version", AutoSize = true,
                Dock = DockStyle.Fill, Text = "Версия: " + selection.ProductVersion,
                Margin = new Padding(0, 4, 0, 8) }, 0, 2);
            layout.Controls.Add(new TextBox { Name = "lm-msi-path", ReadOnly = true,
                Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                Text = selection.SourcePath, AccessibleName = "Полный путь к MSI",
                Margin = new Padding(0, 0, 0, 10) }, 0, 3);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var cancel = new AntdUI.Button { Name = "lm-msi-cancel", Text = "Отмена", AutoSize = true };
            var choose = new AntdUI.Button { Name = "lm-msi-choose", Text = "Выбрать другой MSI…", AutoSize = true };
            var install = new AntdUI.Button { Name = "lm-msi-install", Text = "Установить эту версию", AutoSize = true };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            choose.Click += delegate { DialogResult = DialogResult.Retry; };
            install.Click += delegate { DialogResult = DialogResult.OK; };
            foreach (AntdUI.Button button in new[] { cancel, choose, install })
            {
                OperatorTheme.Button(button, button == install);
                buttons.Controls.Add(button);
            }
            layout.Controls.Add(buttons, 0, 4);
            Controls.Add(layout);
            CancelButton = cancel;
        }
    }
}
