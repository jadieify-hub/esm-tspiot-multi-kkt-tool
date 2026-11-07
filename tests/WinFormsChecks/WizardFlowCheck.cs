using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;
using EsmTspiot.WinForms.Shared;

public static class WizardFlowCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        var ports = new SetupFlowCheck.Connections();
        var transport = new SetupFlowCheck.Transport(ports);
        using (var form = new MainForm(new EsmTspiot.Shared.Services.TspiotApiClient(new HttpClient(transport)), ports))
        {
            form.Show();
            form.Size = new System.Drawing.Size(1000, 740);
            Application.DoEvents();
            Capture(form, "wizard-01-auto-start.png");
            CheckAccessible(form, (Button)Field(form, "_bulkRegisterButton"));
            var tabs = (TabControl)Field(form, "_workspaceTabs");
            if (tabs.TabPages.Count != 3)
                throw new Exception("The setup, working inventory and log must use three clear tabs.");
            var grid = (DataGridView)Field(form, "_discoveredKktsGrid");
            if (grid.Rows.Count != 0) throw new Exception("Before search there must be no invented KKT rows.");
            Find<RadioButton>(form, "wizard-manual").Checked = true;
            Capture(form, "wizard-02-manual-empty.png");
            Await((Task)Invoke(form, "ScanKktsCoreAsync", CancellationToken.None));
            grid.CurrentCell = grid.Rows[1].Cells[0];
            Capture(form, "wizard-03-manual-selected.png");
            CheckAccessible(form, (Button)Field(form, "_setupSelectedButton"));
            var serialBox = (TextBox)Field(form, "_kktSerialTextBox");
            if (form.InvokeRequired || serialBox.InvokeRequired)
                throw new Exception("The UI fixture must run its async continuations on the real WinForms message loop.");
            if (((TextBox)Field(form, "_kktSerialTextBox")).Text != "00105700000002" ||
                ((TextBox)Field(form, "_fnSerialTextBox")).Text != "7300000000000001" ||
                ((TextBox)Field(form, "_kktInnTextBox")).Text != "1234567894")
                throw new Exception("Selected KKT fields are not loaded: " +
                    ((TextBox)Field(form, "_kktSerialTextBox")).Text + " / " +
                    ((TextBox)Field(form, "_fnSerialTextBox")).Text + " / " +
                    ((TextBox)Field(form, "_kktInnTextBox")).Text + " selected=" +
                    Field(form, "_manualSelectedSerial") + " tag=" + grid.Rows[1].Tag +
                    " current=" + (grid.CurrentRow == null ? "null" : grid.CurrentRow.Index.ToString()) +
                    " filling=" + Field(form, "_fillingManualGrid") + " busy=" + Field(form, "_busy") +
                    " mode=" + Find<RadioButton>(form, "wizard-manual").Checked);
            Await((Task)Invoke(form, "SetupManualSelectionAsync"));
            if (transport.Writes.Count != 1 || transport.Writes[0] != "00105700000002")
                throw new Exception("The manual wizard must register only the selected KKT before opening its LM step.");
            var host = Find<TextBox>(form, "lm-host-1234567894");
            Capture(form, "wizard-04-lm-local.png");
            Find<RadioButton>(form, "lm-remote-1234567894").Checked = true;
            host.Text = "lm-server";
            Find<TextBox>(form, "lm-port-1234567894").Text = "x";
            Capture(form, "wizard-05-lm-invalid.png");
            grid.CurrentCell = grid.Rows[0].Cells[0];
            grid.CurrentCell = grid.Rows[1].Cells[0];
            if (Find<TextBox>(form, "lm-port-1234567894").Text != "x")
                throw new Exception("Selecting another row must preserve even an invalid raw LM draft.");
            if (Find<Button>(form, "wizard-apply").Enabled)
                throw new Exception("An invalid LM draft must not offer application.");
            Find<TextBox>(form, "lm-port-1234567894").Text = "5995";
            Capture(form, "wizard-06-lm-remote.png");
            CheckAccessible(form, Find<Button>(form, "wizard-apply"));
            CheckAccessible(form, Find<TextBox>(form, "lm-password-1234567894"));
            tabs.SelectedTab = (TabPage)Field(form, "_logTab");
            Invoke(form, "UpdateWizardActions");
            tabs.SelectedTab = (TabPage)Field(form, "_automationTab");
            if (!Find<Button>(form, "wizard-apply").Visible || !Find<Button>(form, "wizard-apply").Enabled)
                throw new Exception("Returning from another tab must preserve the pending editor and its Apply action.");
            transport.MismatchedIdentity = true;
            int attempts = transport.MutationAttempts;
            bool rejected = false;
            try { Await((Task)Invoke(form, "ValidatePendingWizardTargetsAsync", CancellationToken.None)); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || transport.MutationAttempts != attempts)
                throw new Exception("A changed registered identity must be rejected before saving or mutating anything.");
            transport.MismatchedIdentity = false;
            transport.ChangedDkktPort = true;
            rejected = false;
            try { Await((Task)Invoke(form, "ValidatePendingWizardTargetsAsync", CancellationToken.None)); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || transport.MutationAttempts != attempts)
                throw new Exception("A changed ESM driver port must invalidate the pending plan before any writes.");
            transport.ChangedDkktPort = false;
            Invoke(form, "DeferWizardLm");
            ((Button)Field(form, "_wizardDiagnose")).PerformClick();
            DateTime diagnosisDeadline = DateTime.UtcNow.AddSeconds(10);
            while ((bool)Field(form, "_busy") && DateTime.UtcNow < diagnosisDeadline)
            { Application.DoEvents(); Thread.Sleep(5); }
            if (transport.InfoCalls != 1 || (bool)Field(form, "_busy"))
                throw new Exception("The first diagnosis click from the wizard must run before the working tab's lazy refresh.");
            Capture(form, "wizard-07-selected-diagnostics.png");
            tabs.SelectedTab = (TabPage)Field(form, "_automationTab");
            Invoke(form, "ShowWizardLmStep", new[] { new LmGatewayKkt { InstanceId = "00105700000002",
                KktSerial = "00105700000002", FnSerial = "7300000000000001", KktInn = "1234567894",
                Port = "50402", SoftPort = "51402", DkktPort = "4042", RegistrationConfirmed = true } }, false);
            ((TextBox)Field(form, "_baseUrlTextBox")).Text = "http://127.0.0.1:51078";
            rejected = false;
            try { Await((Task)Invoke(form, "ValidatePendingWizardTargetsAsync", CancellationToken.None)); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || transport.MutationAttempts != attempts)
                throw new Exception("Changing the ESM destination must invalidate a pending wizard operation.");
            CheckMissingFinalRegistration(form);
            CheckServiceDeletionSelection(form);
            foreach (Control control in Descendants(form))
                if (control is Button && (control.Text == "Выбрать другую кассу" || control.Text == "Выбрать следующую ККТ"))
                    throw new Exception("Clicking a table row must not be duplicated by another-cash buttons.");
        }
        CheckCloseWaitsForOperation();
        CheckRefreshWaitsForClose();
        CheckAutomaticRegistration();
    }

    private static void CheckAutomaticRegistration()
    {
        var ports = new SetupFlowCheck.Connections { AllowSimultaneous = true };
        var transport = new SetupFlowCheck.Transport(ports) { AllowAll = true };
        using (var form = new MainForm(new EsmTspiot.Shared.Services.TspiotApiClient(new HttpClient(transport)), ports))
        {
            form.Show();
            form.Size = new System.Drawing.Size(1000, 740);
            Await((Task)Invoke(form, "ConfigureAllKktsAutomaticallyAsync"));
            var pending = (IList<LmGatewayKkt>)Field(form, "_wizardPending");
            if (transport.Writes.Count != 2 || pending.Count != 2 || !Find<Button>(form, "wizard-apply").Visible ||
                !string.IsNullOrEmpty((string)Field(form, "_wizardRegistrationWarning")))
                throw new Exception("The automatic mode must register the original two KKTs and then stop at the inline LM choice. " +
                    "writes=" + transport.Writes.Count + "; pending=" + pending.Count + "; warning=" + Field(form, "_wizardRegistrationWarning"));
            if (form.Controls.Find("lm-group-1234567894", true).Length != 1 || ports.Active != -1)
                throw new Exception("The automatic LM step must group the INN and release the physical connection first.");
            Capture(form, "wizard-08-auto-lm.png");
        }
    }

    private static void CheckMissingFinalRegistration(MainForm form)
    {
        var a = new LmGatewayKkt { KktSerial = "A", FnSerial = "FNA", KktInn = "1234567894", RegistrationConfirmed = true };
        var b = new LmGatewayKkt { KktSerial = "B", FnSerial = "FNB", KktInn = "1234567894", RegistrationConfirmed = true };
        var extra = new LmGatewayKkt { KktSerial = "C", FnSerial = "FNC", KktInn = "1234567894", RegistrationConfirmed = true };
        object[] arguments = { new[] { a, b }, new[] { a, extra }, null };
        var bounded = (IList<LmGatewayKkt>)Invoke(form, "BoundWizardRegistrationTargets", arguments);
        if (bounded.Count != 1 || bounded[0].KktSerial != "A" || !((string)arguments[2]).Contains("B"))
            throw new Exception("A missing final registration must make the outcome partial; unrelated new KKTs must not enter its scope.");
    }

    private static void CheckServiceDeletionSelection(MainForm form)
    {
        ((CheckBox)Field(form, "_manualFallbackToggle")).Checked = true;
        Invoke(form, "UpdateInstancesGrid", new ApiResponse { IsSuccess = true, StatusCode = 200,
            ResponseBody = "{\"instances\":[{\"id\":\"00105700000001\",\"port\":50401,\"softPort\":51401},{\"id\":\"00105700000002\",\"port\":50402,\"softPort\":51402}]}" });
        var raw = (DataGridView)Field(form, "_manualInstancesGrid");
        raw.CurrentCell = raw.Rows[1].Cells[0];
        raw.Rows[1].Selected = true;
        if (Invoke(form, "GetSelectedDeletionCandidate") == null)
            throw new Exception("A raw ESM instance without regData must remain selectable for guarded service deletion.");
        Find<RadioButton>(form, "wizard-auto").Checked = true;
        if (Invoke(form, "GetSelectedDeletionCandidate") != null)
            throw new Exception("A hidden manual service selection must not drive automatic-mode deletion.");
        Find<RadioButton>(form, "wizard-manual").Checked = true;
        ((TextBox)Field(form, "_baseUrlTextBox")).Text = "http://127.0.0.1:51079";
        if (raw.Rows.Count != 0 || Invoke(form, "GetSelectedDeletionCandidate") != null)
            throw new Exception("A destination change must clear stale deletion candidates.");
    }

    private static void CheckRefreshWaitsForClose()
    {
        var ports = new SetupFlowCheck.Connections();
        var transport = new SetupFlowCheck.Transport(ports);
        transport.PendingInstances = new TaskCompletionSource<HttpResponseMessage>();
        using (var form = new MainForm(new EsmTspiot.Shared.Services.TspiotApiClient(new HttpClient(transport)), ports))
        {
            form.Show();
            var tabs = (TabControl)Field(form, "_workspaceTabs");
            tabs.SelectedTab = (TabPage)Field(form, "_instancesTab");
            if (!(bool)Field(form, "_busy") || ((TextBox)Field(form, "_baseUrlTextBox")).Enabled)
                throw new Exception("Automatic inventory refresh must lock its destination while a request is pending.");
            form.Close();
            if (form.IsDisposed) throw new Exception("Closing must wait for an inventory request too.");
            transport.PendingInstances.SetResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!form.IsDisposed && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
            if (!form.IsDisposed) throw new Exception("The deferred close must finish after inventory refresh releases its request.");
        }
    }

    private static void CheckCloseWaitsForOperation()
    {
        using (var form = new MainForm())
        {
            form.Show();
            var completion = new TaskCompletionSource<bool>();
            Func<Task> operation = async delegate { await completion.Task; };
            var task = (Task)Invoke(form, "RunButtonActionAsync", operation);
            Invoke(form, "OnLmGatewayOperationStateChanged", false);
            if (!(bool)Field(form, "_busy"))
                throw new Exception("An inner operation must not release the outer wizard's busy state.");
            form.Close();
            Application.DoEvents();
            if (form.IsDisposed || task.IsCompleted)
                throw new Exception("Closing must wait for the current operation's safe completion.");
            completion.SetResult(true);
            Await(task);
            Application.DoEvents();
            if (!form.IsDisposed) throw new Exception("A requested close must finish after the operation has released its resources.");
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }
    private static void CheckAccessible(Form form, Control button)
    {
        var rectangle = form.RectangleToClient(button.Parent.RectangleToScreen(button.Bounds));
        if (!button.Visible || !form.ClientRectangle.Contains(rectangle))
            throw new Exception("The primary wizard action must remain accessible on a small screen: " + button.Text);
    }
    private static void Capture(Form form, string name)
    {
        string directory = Environment.GetEnvironmentVariable("MULTIKKT_UI_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Application.DoEvents();
        System.IO.Directory.CreateDirectory(directory);
        using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
            bitmap.Save(System.IO.Path.Combine(directory, name));
        }
    }
    private static T Find<T>(Control parent, string name) where T : Control
    {
        Control[] matches = parent.Controls.Find(name, true);
        if (matches.Length != 1) throw new Exception("Wizard control is missing: " + name);
        return (T)matches[0];
    }
    private static object Field(object value, string name)
    { return value.GetType().GetField(name, Private).GetValue(value); }
    private static object Invoke(object value, string name, params object[] args)
    {
        try { return value.GetType().GetMethod(name, Private).Invoke(value, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    private static void Await(Task task)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        { Application.DoEvents(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new Exception("Wizard UI check timed out.");
        task.GetAwaiter().GetResult();
    }
}
