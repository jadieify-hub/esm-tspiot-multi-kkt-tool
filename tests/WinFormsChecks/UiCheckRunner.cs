using System;
using System.Windows.Forms;

public static class UiCheckRunner
{
    public static void Run()
    {
        Exception failure = null;
        using (var host = new Form { ShowInTaskbar = false, Opacity = 0, Width = 1, Height = 1 })
        {
            host.Shown += delegate
            {
                try
                {
                    Control.CheckForIllegalCrossThreadCalls = true;
                    LmEditorCheck.Run();
                    WizardFlowCheck.Run();
                    LmRefreshCheck.Run();
                    SetupFlowCheck.Run();
                    LmSetupDialogCheck.Run();
                }
                catch (Exception ex) { failure = ex; }
                finally { host.Close(); }
            };
            Application.Run(host);
        }
        if (failure != null) throw failure;
    }
}
