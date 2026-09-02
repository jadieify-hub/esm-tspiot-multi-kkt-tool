using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LocalModuleInstallVolumeDialog : Form
    {
        private readonly ComboBox _volumes = new ComboBox();
        private readonly Label _space = new Label();
        private readonly Button _continue = new Button();
        private readonly int _newCloneCount;
        private readonly int _newCacheEntryCount;

        internal LocalModuleInstallVolumeDialog(
            string defaultVolumeRoot,
            int newCloneCount,
            int newCacheEntryCount)
        {
            _newCloneCount = newCloneCount;
            _newCacheEntryCount = newCacheEntryCount;
            Text = "Диск для независимых ЛМ ЧЗ";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 190);
            MinimumSize = new Size(520, 220);
            Font = new Font("Segoe UI", 8.25F);
            BuildLayout();
            FillVolumes(defaultVolumeRoot);
            UpdateSpace();
        }

        internal string SelectedVolumeRoot { get; private set; }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Text = "Клоны ЛМ будут установлены в Program Files выбранного " +
                    "локального диска. По умолчанию используется диск базового ЛМ."
            }, 0, 0);
            _volumes.DropDownStyle = ComboBoxStyle.DropDownList;
            _volumes.Width = 160;
            _volumes.Margin = new Padding(0, 10, 0, 8);
            _volumes.SelectedIndexChanged += delegate { UpdateSpace(); };
            root.Controls.Add(_volumes, 0, 1);
            _space.AutoSize = true;
            _space.MaximumSize = new Size(520, 0);
            root.Controls.Add(_space, 0, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            Button cancel = new Button
            {
                Text = "Отмена",
                AutoSize = true,
                DialogResult = DialogResult.Cancel
            };
            _continue.Text = "Продолжить";
            _continue.AutoSize = true;
            _continue.Margin = new Padding(0, 0, 6, 0);
            _continue.Click += delegate
            {
                SelectedVolumeRoot = Convert.ToString(_volumes.SelectedItem);
                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_continue);
            root.Controls.Add(buttons, 0, 3);
            AcceptButton = _continue;
            CancelButton = cancel;
            Controls.Add(root);
        }

        private void FillVolumes(string defaultVolumeRoot)
        {
            string expected = string.IsNullOrWhiteSpace(defaultVolumeRoot)
                ? string.Empty
                : Path.GetPathRoot(Path.GetFullPath(defaultVolumeRoot));
            int selected = -1;
            DriveInfo[] drives = DriveInfo.GetDrives();
            for (int index = 0; index < drives.Length; index++)
            {
                DriveInfo drive = drives[index];
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    continue;
                string root = drive.RootDirectory.FullName;
                if (!LocalModuleInstallRootPolicy.IsCanonicalVolumeRoot(root))
                    continue;
                _volumes.Items.Add(root);
                if (string.Equals(root, expected,
                        StringComparison.OrdinalIgnoreCase))
                    selected = _volumes.Items.Count - 1;
            }
            if (_volumes.Items.Count > 0)
                _volumes.SelectedIndex = selected >= 0 ? selected : 0;
            _continue.Enabled = _volumes.Items.Count > 0;
        }

        private void UpdateSpace()
        {
            if (_volumes.SelectedItem == null)
            {
                _space.Text = "Не найден готовый локальный фиксированный диск.";
                _continue.Enabled = false;
                return;
            }
            string selected = Convert.ToString(_volumes.SelectedItem);
            string system = Path.GetPathRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.System));
            LocalModuleDiskSpaceProjection projection =
                LocalModuleDiskSpacePolicy.Evaluate(
                    _newCloneCount,
                    _newCacheEntryCount,
                    new DriveInfo(selected).AvailableFreeSpace,
                    new DriveInfo(system).AvailableFreeSpace);
            _space.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Нужно на диске установки: {0:N0} МиБ; на системном диске " +
                    "для staging и кэша MSI: {1:N0} МиБ. Свободное место: " +
                    "{2:N0} / {3:N0} МиБ.",
                projection.InstallVolumeRequiredBytes / 1048576L,
                projection.SystemVolumeRequiredBytes / 1048576L,
                projection.InstallVolumeObservedFreeBytes / 1048576L,
                projection.SystemVolumeObservedFreeBytes / 1048576L);
            _continue.Enabled = projection.HasEnoughSpace;
            _space.ForeColor = projection.HasEnoughSpace
                ? SystemColors.ControlText
                : Color.Firebrick;
        }
    }
}
