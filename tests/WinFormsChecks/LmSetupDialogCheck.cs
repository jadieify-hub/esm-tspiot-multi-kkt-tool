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
            Application.DoEvents();
            string captureDirectory = Environment.GetEnvironmentVariable("MULTIKKT_UI_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(captureDirectory))
            {
                System.IO.Directory.CreateDirectory(captureDirectory);
                using (var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height))
                { dialog.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, dialog.Size)); bitmap.Save(System.IO.Path.Combine(captureDirectory, "lm-setup.png")); }
            }
            var local = Find<RadioButton>(dialog, "lm-local-1234567894");
            var remote = Find<RadioButton>(dialog, "lm-remote-7701234560");
            if (!local.Checked || !remote.Checked)
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
