using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;
using EsmTspiot.WinForms.Shared;

public static class LmEditorCheck
{
    public static void Run()
    {
        Type type = typeof(MainForm).Assembly.GetType("EsmTspiot.WinForms.Shared.LmSetupEditor");
        if (type == null) throw new Exception("The inline and dialog LM paths need one reusable editor.");
        var kkts = new[] { new LmGatewayKkt { KktSerial = "KKT-B", KktInn = "1234567894" } };
        Func<string, string, LmGatewayCredentials> defaults = delegate {
            return new LmGatewayCredentials { Login = "admin", Password = "admin" };
        };
        using (var editor = (Control)Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { kkts, new Dictionary<string, string>(), defaults, false }, null))
        {
            var placement = Find<AntdUI.Segmented>(editor, "lm-placement-1234567894");
            AntdWorkingCheck.PressKey(placement, Keys.Right);
            if (placement.SelectIndex != 1)
                throw new Exception("Remote LM placement must be selectable with keyboard arrows.");
            Find<TextBox>(editor, "lm-host-1234567894").Text = "lm-server";
            Find<TextBox>(editor, "lm-port-1234567894").Text = "x";
            object[] values = { null, null, null };
            if ((bool)type.GetMethod("TryReadValues", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, values))
                throw new Exception("An invalid remote port must prevent application.");
            if (Find<TextBox>(editor, "lm-port-1234567894").Text != "x")
                throw new Exception("Validation must keep the operator's raw draft.");
            Find<TextBox>(editor, "lm-port-1234567894").Text = "5995";
            values = new object[] { null, null, null };
            if (!(bool)type.GetMethod("TryReadValues", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, values))
                throw new Exception("A valid separate host and port must be accepted.");
            var endpoints = (IDictionary<string, string>)values[0];
            if (endpoints["1234567894"] != "lm-server:5995")
                throw new Exception("The editor must preserve the selected LM endpoint.");
            var password = Find<TextBox>(editor, "lm-password-1234567894");
            if (!password.UseSystemPasswordChar) throw new Exception("LM passwords must remain masked.");
            password.Text = "endpoint-only-password";
            Find<TextBox>(editor, "lm-host-1234567894").Text = "other-server";
            if (password.Text != "admin")
                throw new Exception("A different endpoint must not inherit the old endpoint's password.");
        }
    }

    private static T Find<T>(Control parent, string name) where T : Control
    { return (T)parent.Controls.Find(name, true)[0]; }
}
