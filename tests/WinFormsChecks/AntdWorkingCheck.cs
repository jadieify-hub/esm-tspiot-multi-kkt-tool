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
            var log = (TextBox)typeof(MainForm).GetField("_logTextBox", flags).GetValue(form);
            var copy = (Control)typeof(MainForm).GetField("_copyDiagnosticsButton", flags).GetValue(form);
            var logPage = (TabPage)typeof(MainForm).GetField("_logTab", flags).GetValue(form);
            ((TabControl)typeof(MainForm).GetField("_workspaceTabs", flags).GetValue(form)).SelectedTab = logPage;
            Application.DoEvents();
            if (copy.Bottom > copy.Parent.ClientSize.Height ||
                copy.RectangleToScreen(copy.ClientRectangle).IntersectsWith(log.RectangleToScreen(log.ClientRectangle)))
                throw new Exception("Log toolbar buttons must fit above the log without overlap.");
            ((TabControl)typeof(MainForm).GetField("_workspaceTabs", flags).GetValue(form)).SelectedIndex = 0;
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
        CheckScaledSteps();
    }

    private static void CheckScaledSteps()
    {
        Type type = typeof(MainForm).Assembly.GetType("EsmTspiot.WinForms.Shared.SetupStepStrip", true);
        try
        {
            foreach (float factor in new[] { 1F, 1.25F, 1.5F })
            {
                AntdUI.Config.SetDpi(factor);
                using (var host = new Form())
                using (var steps = (Control)Activator.CreateInstance(type, true))
                {
                    host.Controls.Add(steps);
                    host.ClientSize = new System.Drawing.Size((int)(900 * factor), (int)(52 * factor));
                    steps.Scale(new System.Drawing.SizeF(factor, factor));
                    host.Show();
                    Application.DoEvents();
                    var icons = (Control[])type.GetField("_icons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(steps);
                    var titles = (Control[])type.GetField("_titles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(steps);
                    foreach (Control icon in icons)
                    {
                        if (!icon.Parent.ClientRectangle.Contains(icon.Bounds))
                            throw new Exception("Step circles must fit their container at scale " + factor);
                        var bounds = icon.RectangleToScreen(icon.ClientRectangle);
                        for (Control parent = icon.Parent; parent != null; parent = parent.Parent)
                            if (!parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds))
                                throw new Exception("Step circle clipped at scale " + factor + ": " + parent.GetType().Name +
                                    " client=" + parent.ClientRectangle + " icon=" + parent.RectangleToClient(bounds) + " host=" + host.ClientSize);
                    }
                    for (int index = 0; index < icons.Length; index++)
                        if (Math.Abs(icons[index].Top + icons[index].Height / 2 - titles[index].Top - titles[index].Height / 2) > 3)
                            throw new Exception("Step circles and captions must align at scale " + factor);
                    string directory = Environment.GetEnvironmentVariable("MULTIKKT_UI_SCREENSHOT_DIR");
                    if (!string.IsNullOrEmpty(directory))
                        using (var bitmap = new System.Drawing.Bitmap(host.Width, host.Height))
                        {
                            host.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, host.Size));
                            bitmap.Save(System.IO.Path.Combine(directory, "steps-geometry-" + (int)(factor * 100) + ".png"));
                        }
                }
            }
        }
        finally { AntdUI.Config.SetDpi(null); }
    }

    internal static void PressKey(Control control, Keys key)
    { SendMessage(control.Handle, 0x100, (IntPtr)key, IntPtr.Zero); }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wparam, IntPtr lparam);
}
