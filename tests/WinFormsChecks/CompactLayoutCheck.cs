using System;
using System.Drawing;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Services;
using EsmTspiot.WinForms.Shared;

public static class CompactLayoutCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        var ports = new SetupFlowCheck.Connections();
        var transport = new SetupFlowCheck.Transport(ports);
        using (var form = new MainForm(new TspiotApiClient(new HttpClient(transport)), ports))
        {
            form.Show();
            if (form.FormBorderStyle != FormBorderStyle.Sizable || !form.MaximizeBox)
                throw new Exception("The operator window must provide native edge resizing and maximize.");
            NativeRect bounds;
            if (!GetWindowRect(form.Handle, out bounds)) throw new Exception("The native window bounds are unavailable.");
            int corner = ((bounds.Bottom - 3) << 16) | ((bounds.Right - 3) & 0xffff);
            if (SendMessage(form.Handle, 0x84, IntPtr.Zero, (IntPtr)corner).ToInt32() != 17)
                throw new Exception("Windows must recognize the actual bottom-right resize corner.");
            if (((Control)Field(form, "_navigation")).Height > 48)
                throw new Exception("Navigation must fit its compact row without clipping.");
            var page = (LmGatewayPage)Field(form, "_lmGatewayPage");
            Await(page.RefreshAsync());
            ((TabControl)Field(form, "_workspaceTabs")).SelectedIndex = 1;
            form.Size = new Size(1000, 740);
            Application.DoEvents();
            var grid = (DataGridView)Field(page, "_grid");
            Size small = grid.Size;
            Capture(form, "compact-01-inventory-empty.png");
            form.Size = new Size(1300, 900);
            Application.DoEvents();
            if (grid.Width < small.Width + 200 || grid.Height < small.Height + 100)
                throw new Exception("Resizing must give the inventory more usable width and height.");
            Capture(form, "compact-02-inventory-large.png");
            form.Size = new Size(1000, 740);
            var url = (TextBox)Field(form, "_baseUrlTextBox");
            var driverPort = (TextBox)Field(form, "_dkktPortTextBox");
            Invoke(form, "ShowConnectionSettings");
            var connection = (Form)Field(form, "_connectionDialog");
            if (url.FindForm() != connection || driverPort.FindForm() != connection || connection.Owner != form)
                throw new Exception("The original connection fields must live in the owned settings window.");
            Capture(connection, "compact-03-connection.png");
            string original = url.Text;
            url.Text = "http://127.0.0.1:51078";
            connection.Close();
            if (connection.IsDisposed || url.IsDisposed)
                throw new Exception("Closing connection settings must preserve the shared input controls.");
            Invoke(form, "ShowConnectionSettings");
            if (url.Text != "http://127.0.0.1:51078")
                throw new Exception("Reopening settings must retain the entered address.");
            Invoke(form, "SetBusy", true);
            if (url.Enabled || driverPort.Enabled || ((Control)Field(form, "_checkButton")).Enabled)
                throw new Exception("Connection fields must stay locked while an operation is running.");
            Invoke(form, "SetBusy", false);
            url.Text = original;
            connection.Close();
            Size beforeService = grid.Size;
            Invoke(page, "ShowServiceOperations");
            var service = (Form)Field(page, "_serviceOperationsWindow");
            if (!service.Visible || service.Owner != form || grid.Size != beforeService)
                throw new Exception("Service operations must open separately without shrinking the inventory.");
            Capture(service, "compact-04-service-operations.png");
            foreach (string name in new[] { "LmRole", "LmInstallRoot", "LmOwnership" })
                if (grid.Columns[name].Visible)
                    throw new Exception("Technical details must not expand the main inventory table.");
            Invoke(form, "SetBusy", true);
            if (((Control)Field(page, "_ensureLocalModulesButton")).Enabled)
                throw new Exception("The separate service window must share the operation lock.");
            Invoke(form, "SetBusy", false);
            transport.PendingInstances = new TaskCompletionSource<HttpResponseMessage>();
            Task pending = page.RefreshAsync();
            var stop = (AntdUI.Button)Field(page, "_serviceCancelButton");
            if (!stop.Visible || !stop.Enabled)
                throw new Exception("The service window must offer Stop for its running operation.");
            stop.PerformClick();
            if (!((CancellationTokenSource)Field(page, "_cancellation")).IsCancellationRequested)
                throw new Exception("The service Stop button must cancel the real shared operation.");
            transport.PendingInstances.SetCanceled();
            Await(pending);
            service.Close();
            if (service.IsDisposed)
                throw new Exception("Closing service operations must preserve its original controls.");
        }
    }

    private static object Field(object value, string name)
    {
        FieldInfo field = value.GetType().GetField(name, Private);
        if (field == null) throw new Exception("Missing compact layout member: " + name);
        return field.GetValue(value);
    }
    private static object Invoke(object value, string name, params object[] args)
    { return value.GetType().GetMethod(name, Private).Invoke(value, args); }
    private static void Await(Task task)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new Exception("Compact layout refresh timed out.");
        task.GetAwaiter().GetResult();
    }

    private static void Capture(Form form, string name)
    {
        string directory = Environment.GetEnvironmentVariable("MULTIKKT_UI_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Application.DoEvents();
        System.IO.Directory.CreateDirectory(directory);
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(System.IO.Path.Combine(directory, name));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rectangle);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wparam, IntPtr lparam);
}
