using System;
using System.Drawing;
using System.Windows.Forms;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class MainForm
    {
        private readonly AntdUI.Segmented _navigation = new OperatorSegments();
        private bool _updatingNavigation;

        private Control BuildBrandHeader()
        {
            return new AntdUI.PageHeader { Text = "MultiKKT", SubText = Text.Replace("MultiKKT — тестовая версия ", ""),
                Dock = DockStyle.Fill, ShowButton = true, ShowIcon = true, IconSvg = "ShopOutlined",
                Font = new Font("Segoe UI", 14, FontStyle.Bold), SubFont = new Font("Segoe UI", 10),
                Padding = new Padding(16, 0, 8, 0), BackColor = Color.FromArgb(213, 226, 245),
                ForeColor = OperatorTheme.Ink, UseSystemStyleColor = true, UseForeColorDrawIcons = true };
        }

        private Control BuildWorkspaceNavigation()
        {
            OperatorTheme.Segments(_navigation, "Настройка ККТ", "Обзор касс", "Журнал");
            _navigation.Name = "workspace-navigation";
            _navigation.Items[0].IconSvg = "SettingOutlined";
            _navigation.Items[1].IconSvg = "DesktopOutlined";
            _navigation.Items[2].IconSvg = "FileTextOutlined";
            _navigation.Dock = DockStyle.Left;
            _navigation.Width = 480;
            _navigation.Margin = new Padding(16, 2, 16, 2);
            _navigation.SelectIndexChanged += delegate
            {
                if (_updatingNavigation || _navigation.SelectIndex < 0 || _navigation.SelectIndex >= _workspaceTabs.TabPages.Count) return;
                _workspaceTabs.SelectedIndex = _navigation.SelectIndex;
            };
            _workspaceTabs.SelectedIndexChanged += delegate
            {
                _updatingNavigation = true;
                try { _navigation.SelectIndex = _workspaceTabs.SelectedIndex; }
                finally { _updatingNavigation = false; }
            };
            return _navigation;
        }
    }
}
