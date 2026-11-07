using System;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EsmTspiot.Shared.Services;
using EsmTspiot.WinForms.Shared;

public static class AntdWorkingCheck
{
    public static void Run()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var ports = new SetupFlowCheck.Connections();
        using (var form = new MainForm(new TspiotApiClient(new HttpClient(new SetupFlowCheck.Transport(ports))), ports))
        {
            form.Show();
            FieldInfo modeField = typeof(MainForm).GetField("_wizardMode", flags);
            if (modeField == null || !(modeField.GetValue(form) is AntdUI.Segmented))
                throw new Exception("The working wizard must use the approved segmented mode selector.");
            var mode = (AntdUI.Segmented)modeField.GetValue(form);
            if (!mode.TabStop || !mode.CanSelect)
                throw new Exception("The operator must be able to focus the mode selector with Tab.");
            PressKey(mode, Keys.Right);
            if (mode.SelectIndex != 1)
                throw new Exception("The real mode selector must accept keyboard arrows.");
            mode.Enabled = false;
            PressKey(mode, Keys.Left);
            if (mode.SelectIndex != 1)
                throw new Exception("A disabled mode selector must ignore keys during an operation.");
            mode.Enabled = true;
            PressKey(mode, Keys.Home);
            if (mode.SelectIndex != 0)
                throw new Exception("Home must select the first mode.");
            var tabs = (TabControl)typeof(MainForm).GetField("_workspaceTabs", flags).GetValue(form);
            var inventory = (Control)typeof(MainForm).GetField("_lmGatewayPage", flags).GetValue(form);
            var wizardGrid = (Control)typeof(MainForm).GetField("_discoveredKktsGrid", flags).GetValue(form);
            if (tabs.TabPages.Count != 3 || !tabs.TabPages[1].Controls.Contains(inventory) ||
                tabs.TabPages[1].Controls.Contains(wizardGrid))
                throw new Exception("The working inventory must be a distinct real page, not the setup form with a different heading.");
            var navigation = (Control)typeof(MainForm).GetField("_navigation", flags).GetValue(form);
            navigation.GetType().GetProperty("SelectIndex").SetValue(navigation, 1, null);
            Application.DoEvents();
            if (tabs.SelectedTab != tabs.TabPages[1] || !inventory.Visible || wizardGrid.Visible)
                throw new Exception("Selecting inventory must display only the real inventory page.");
            tabs.SelectedTab = tabs.TabPages[0];
            Application.DoEvents();
            if ((int)navigation.GetType().GetProperty("SelectIndex").GetValue(navigation, null) != 0)
                throw new Exception("Workflow-driven navigation must update the visible selector too.");
        }
    }

    internal static void PressKey(Control control, Keys key)
    { SendMessage(control.Handle, 0x100, (IntPtr)key, IntPtr.Zero); }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wparam, IntPtr lparam);
}
