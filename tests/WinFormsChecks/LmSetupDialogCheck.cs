using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;
using EsmTspiot.WinForms.Shared;

public static class LmSetupDialogCheck
{
    public static void Run()
    {
        CheckInstallerSelection();
        Type type = typeof(LmGatewayPage).Assembly.GetType(
            "EsmTspiot.WinForms.Shared.LmSetupDialog", true);
        var kkts = new List<LmGatewayKkt> {
            new LmGatewayKkt { KktInn = "1234567894", KktSerial = "KKT-A" },
            new LmGatewayKkt { KktInn = "1234567894", KktSerial = "KKT-B" },
            new LmGatewayKkt { KktInn = "7701234560", KktSerial = "KKT-C" }
        };
        var stored = new Dictionary<string, string> {
            { "7701234560", "lm-server:6995" }
        };
        bool saved = false;
        Func<string, string, LmGatewayCredentials> credentials = delegate {
            return new LmGatewayCredentials { Login = "admin", Password = "admin" };
        };
        Action<IDictionary<string, string>, IDictionary<string, LmGatewayCredentials>> save =
            delegate { saved = true; };
        using (var dialog = (Form)Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { kkts, stored, credentials, save }, null))
        {
            dialog.Show();
            CompactLayoutCheck.AssertAppIcon(dialog);
            Application.DoEvents();
            string captureDirectory = Environment.GetEnvironmentVariable("MULTIKKT_UI_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(captureDirectory))
            {
                System.IO.Directory.CreateDirectory(captureDirectory);
                using (var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height))
                { dialog.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, dialog.Size)); bitmap.Save(System.IO.Path.Combine(captureDirectory, "lm-setup.png")); }
            }
            var local = Find<AntdUI.Segmented>(dialog, "lm-placement-1234567894");
            var remote = Find<AntdUI.Segmented>(dialog, "lm-placement-7701234560");
            if (local.SelectIndex != 0 || remote.SelectIndex != 1)
                throw new Exception("New INNs must default to local; saved remote must be restored.");
            if (Find<Label>(dialog, "lm-kkts-1234567894").Text.IndexOf("KKT-A") < 0 ||
                Find<Label>(dialog, "lm-kkts-1234567894").Text.IndexOf("KKT-B") < 0 ||
                Find<Label>(dialog, "lm-kkts-7701234560").Text.IndexOf("KKT-C") < 0)
                throw new Exception("KKTs must be grouped under their INN.");
            if (dialog.Controls.Find("lm-group-1234567894", true).Length != 1 ||
                dialog.Controls.Find("lm-group-7701234560", true).Length != 1)
                throw new Exception("Three KKTs with two INNs must produce two groups.");
            var login = Find<TextBox>(dialog, "lm-login-7701234560");
            var password = Find<TextBox>(dialog, "lm-password-7701234560");
            var host = Find<TextBox>(dialog, "lm-host-7701234560");
            var port = Find<TextBox>(dialog, "lm-port-7701234560");
            if (!password.UseSystemPasswordChar || login.Text != "admin" || password.Text != "admin")
                throw new Exception("Credentials must default to admin/admin and hide the password.");
            if (host.Text != "lm-server" || port.Text != "6995" || !CanRead(type, dialog))
                throw new Exception("Saved remote host and port must be separate valid fields.");
            host.Text = "http://bad/path";
            if (CanRead(type, dialog)) throw new Exception("Invalid remote host was accepted.");
            host.Text = "another-server";
            port.Text = "70000";
            if (CanRead(type, dialog)) throw new Exception("Invalid remote port was accepted.");
            port.Text = "6995";
            password.Text = "endpoint-password";
            host.Text = "third-server";
            if (password.Text != "admin" || !CanRead(type, dialog))
                throw new Exception("Changing endpoint must reset its credentials.");
            Find<Button>(dialog, "lm-defer").PerformClick();
            if (saved || dialog.DialogResult == DialogResult.OK)
                throw new Exception("Defer must not save entered settings.");
        }

        var merge = typeof(LmGatewayPage).GetMethod("MergeLmEndpoints",
            BindingFlags.Static | BindingFlags.NonPublic);
        var full = new Dictionary<string, string> {
            { "1234567894", "old-server:6995" },
            { "7701234560", "saved-server:6995" }
        };
        var choices = new Dictionary<string, string> { { "1234567894", string.Empty } };
        var merged = (IDictionary<string, string>)merge.Invoke(null, new object[] { full, choices });
        if (merged.Count != 1 || merged["7701234560"] != "saved-server:6995")
            throw new Exception("Saving selected INNs must preserve all other remote mappings.");
        choices["1234567894"] = "SAVED-SERVER:6995";
        try
        {
            merge.Invoke(null, new object[] { full, choices });
            throw new Exception("Duplicate endpoints outside the dialog were accepted.");
        }
        catch (TargetInvocationException ex)
        {
            if (!(ex.InnerException is ArgumentException)) throw;
        }
        var needsApply = typeof(LmGatewayPage).GetMethod("NeedsLmTargetApplication", BindingFlags.Static | BindingFlags.NonPublic);
        if (needsApply == null) throw new Exception("Selected LM targets must be compared with each controller's applied target.");
        foreach (var example in new[] {
            new { Applied = new LmGatewayTarget("new-server", 6995), Desired = "new-server:6995", Pending = false },
            new { Applied = new LmGatewayTarget("old-server", 6995), Desired = "new-server:6995", Pending = true },
            new { Applied = new LmGatewayTarget("old-server", 6995), Desired = "", Pending = true },
            new { Applied = new LmGatewayTarget("127.0.0.1", 5995), Desired = "", Pending = false } })
            if ((bool)needsApply.Invoke(null, new object[] { example.Applied, example.Desired }) != example.Pending)
                throw new Exception("A shared INN's unmodified controller must remain pending, including return to a local LM.");
    }

    public static void CheckInstallerSelection()
    {
        CheckInstallerRequirement();
        Type picker = typeof(LmGatewayPage).Assembly.GetType(
            "EsmTspiot.WinForms.Shared.LocalModuleInstallerPicker", true);
        MethodInfo confirm = picker.GetMethod("ConfirmSelection",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (confirm == null) throw new Exception("Remembered MSI must be confirmed before installation.");
        string firstPath = System.IO.Path.GetTempFileName();
        string secondPath = System.IO.Path.GetTempFileName();
        try
        {
            var first = new LocalModuleInstallerSelection {
                SourcePath = firstPath, FileName = "official-1.msi", ProductVersion = "1.0" };
            var second = new LocalModuleInstallerSelection {
                SourcePath = secondPath, FileName = "official-2.msi", ProductVersion = "2.0" };
            int choices = 0;
            int confirmations = 0;
            Func<LocalModuleInstallerSelection> choose = delegate { choices++; return second; };
            Func<LocalModuleInstallerSelection, DialogResult> install = delegate(LocalModuleInstallerSelection shown) {
                confirmations++;
                if (shown != first) throw new Exception("Remembered MSI metadata was lost.");
                return DialogResult.OK;
            };
            if (confirm.Invoke(null, new object[] { first, choose, install }) != first || choices != 0 || confirmations != 1)
                throw new Exception("Remembered MSI must be confirmed without opening the file chooser.");
            confirmations = 0;
            Func<LocalModuleInstallerSelection, DialogResult> replace = delegate(LocalModuleInstallerSelection shown) {
                confirmations++;
                if (confirmations == 1 && shown == first) return DialogResult.Retry;
                if (confirmations == 2 && shown == second) return DialogResult.OK;
                throw new Exception("Replacement MSI must be confirmed with its own version and path.");
            };
            if (confirm.Invoke(null, new object[] { first, choose, replace }) != second || choices != 1)
                throw new Exception("Choosing another MSI must replace the remembered selection.");
            System.IO.File.Delete(firstPath);
            confirmations = 0;
            Func<LocalModuleInstallerSelection, DialogResult> missing = delegate(LocalModuleInstallerSelection shown) {
                confirmations++;
                if (shown != second) throw new Exception("Missing remembered MSI must open the chooser first.");
                return DialogResult.OK;
            };
            if (confirm.Invoke(null, new object[] { first, choose, missing }) != second || choices != 2 || confirmations != 1)
                throw new Exception("Missing MSI must be replaced through the existing chooser.");
            Func<LocalModuleInstallerSelection, DialogResult> cancel = delegate { return DialogResult.Cancel; };
            if (confirm.Invoke(null, new object[] { second, choose, cancel }) != null)
                throw new Exception("Cancel must not authorize installation.");
            Func<LocalModuleInstallerSelection> cancelChooser = delegate { return null; };
            if (confirm.Invoke(null, new object[] { null, cancelChooser, install }) != null ||
                confirm.Invoke(null, new object[] { second, cancelChooser,
                    new Func<LocalModuleInstallerSelection, DialogResult>(delegate { return DialogResult.Retry; }) }) != null)
                throw new Exception("Cancelling the MSI chooser must not authorize installation.");
            CheckInstallerDialog(second);
        }
        finally
        {
            System.IO.File.Delete(firstPath);
            System.IO.File.Delete(secondPath);
        }
    }

    private static void CheckInstallerRequirement()
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        MethodInfo requires = typeof(LmGatewayPage).GetMethod("RequiresLocalModuleInstallation", flags);
        if (requires == null) throw new Exception("MSI confirmation must depend on actual local installation.");
        Type type = typeof(LmGatewayPage).Assembly.GetType(
            "EsmTspiot.WinForms.Shared.LocalModuleMsiOperatorInventorySnapshot", true);
        object inventory = Activator.CreateInstance(type, true);
        var plan = new LocalModuleMsiPlan();
        if ((bool)requires.Invoke(null, new object[] { plan, inventory }))
            throw new Exception("Remote-only LM plan must not request MSI confirmation.");
        var assignment = new LocalModuleMsiAssignment { Inn = "1234567894", CloneOrdinal = 1 };
        plan.Assignments.Add(assignment);
        if (!(bool)requires.Invoke(null, new object[] { plan, inventory }))
            throw new Exception("New local LM must request MSI confirmation.");
        var items = (IList<LocalModuleMsiInventoryItem>)type.GetProperty("Items",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(inventory, null);
        items.Add(new LocalModuleMsiInventoryItem { Inn = assignment.Inn, CloneOrdinal = 1 });
        if (!(bool)requires.Invoke(null, new object[] { plan, inventory }))
            throw new Exception("Inventory record without installed services must still request confirmation.");
        var installed = (IList<string>)type.GetProperty("InstalledModuleInns",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(inventory, null);
        installed.Add(assignment.Inn);
        if ((bool)requires.Invoke(null, new object[] { plan, inventory }))
            throw new Exception("Existing local LM must not request new installation confirmation.");
        plan.Assignments.Clear();
        plan.Assignments.Add(new LocalModuleMsiAssignment { Inn = "7701234560", CloneOrdinal = 0 });
        type.GetProperty("BaseInventory", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(
            inventory, new LocalModuleBaseInventory { IsInstalled = true }, null);
        if ((bool)requires.Invoke(null, new object[] { plan, inventory }))
            throw new Exception("Adopting the installed base LM must not request MSI installation confirmation.");
    }

    private static void CheckInstallerDialog(LocalModuleInstallerSelection selection)
    {
        Type type = typeof(LmGatewayPage).Assembly.GetType(
            "EsmTspiot.WinForms.Shared.LocalModuleInstallerConfirmationDialog", true);
        using (var dialog = (Form)Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { selection }, null))
        {
            dialog.Show();
            Application.DoEvents();
            CompactLayoutCheck.AssertAppIcon(dialog);
            if (Find<Label>(dialog, "lm-msi-file").Text.IndexOf(selection.FileName) < 0 ||
                Find<Label>(dialog, "lm-msi-version").Text.IndexOf(selection.ProductVersion) < 0 ||
                Find<TextBox>(dialog, "lm-msi-path").Text != selection.SourcePath)
                throw new Exception("Installation confirmation must show the exact file, version and full path.");
            foreach (string name in new[] { "lm-msi-install", "lm-msi-choose", "lm-msi-cancel" })
                Find<AntdUI.Button>(dialog, name);
            if (((Control)dialog.CancelButton).Name != "lm-msi-cancel")
                throw new Exception("Escape must cancel MSI installation.");
        }
    }

    private static bool CanRead(Type type, Form dialog)
    {
        return (bool)type.GetMethod("TryReadValues",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(
                dialog, new object[] { null, null, null });
    }

    private static T Find<T>(Control dialog, string name) where T : Control
    {
        var controls = dialog.Controls.Find(name, true);
        if (controls.Length != 1) throw new Exception("LM setup field not found: " + name);
        return (T)controls[0];
    }
}
