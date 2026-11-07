using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.WinForms.Shared;

public static class SetupFlowCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        var ports = new Connections();
        var transport = new Transport(ports);
        var client = new TspiotApiClient(new HttpClient(transport));
        var constructor = typeof(MainForm).GetConstructor(new[] {
            typeof(TspiotApiClient), typeof(IKktConnectionProvider) });
        if (constructor == null)
            throw new Exception("Manual discovery must use an injectable, shared registration path.");
        using (var form = (MainForm)constructor.Invoke(new object[] { client, ports }))
        {
            form.Show();
            ((TabControl)Field(form, "_workspaceTabs")).SelectedTab = (TabPage)Field(form, "_manualKktTab");
            Application.DoEvents();
            Invoke(form, "SetBusy", true);
            if (((TextBox)Field(form, "_baseUrlTextBox")).Enabled || ((TextBox)Field(form, "_dkktPortTextBox")).Enabled)
                throw new Exception("The ESM destination must remain locked for the entire operation.");
            Invoke(form, "SetBusy", false);
            Await((Task)Invoke(form, "ScanKktsCoreAsync", CancellationToken.None));
            var grid = (DataGridView)Field(form, "_discoveredKktsGrid");
            if (grid.Rows.Count != 2 || transport.Writes.Count != 0 || ports.Active != -1)
                throw new Exception("Scanning must show both unregistered KKTs without writes or an open lease.");
            Capture(form, "manual-discovery.png");
            grid.CurrentCell = grid.Rows[1].Cells[0];
            if ((string)Field(form, "_manualSelectedSerial") != "00105700000002")
                throw new Exception("Selecting a discovered row must load its registration parameters.");
            ((TextBox)Field(form, "_portTextBox")).Text = "50409";
            ((TextBox)Field(form, "_softPortTextBox")).Text = "51409";
            Await((Task)Invoke(form, "ScanKktsCoreAsync", CancellationToken.None));
            grid.CurrentCell = grid.Rows[1].Cells[0];
            if (((TextBox)Field(form, "_portTextBox")).Text != "50409")
                throw new Exception("Rescanning must preserve operator port assignments.");
            Task registration = (Task)Invoke(form, "RegisterManualSelectionCoreAsync", CancellationToken.None);
            Await(registration);
            if (transport.Writes.Count != 1 || transport.Writes[0] != "00105700000002" ||
                ports.Active != -1 || !ports.RegistrationWasIsolated)
                throw new Exception("Only the selected physical KKT may be registered; its lease must close.");
            Await((Task)Invoke(form, "RegisterManualSelectionCoreAsync", CancellationToken.None));
            if (transport.Writes.Count != 1)
                throw new Exception("A confirmed registration must not be repeated.");
            transport.MismatchedIdentity = true;
            bool mismatchRefused = false;
            try { Await((Task)Invoke(form, "ScanKktsCoreAsync", CancellationToken.None)); }
            catch (InvalidOperationException) { mismatchRefused = true; }
            if (!mismatchRefused)
                throw new Exception("An ESM registration must not overwrite a physical KKT's different INN or FN.");
            transport.MismatchedIdentity = false;
            transport.Confirmed = false;
            Await((Task)Invoke(form, "ScanKktsCoreAsync", CancellationToken.None));
            if (((HashSet<string>)Field(form, "_manualRegistered")).Contains("00105700000002"))
                throw new Exception("Registration data without a Registered state must remain an incomplete registration.");
            grid.CurrentCell = grid.Rows[1].Cells[0];
            if (!((TextBox)Field(form, "_portTextBox")).ReadOnly ||
                !((TextBox)Field(form, "_softPortTextBox")).ReadOnly)
                throw new Exception("An existing incomplete ESM instance must keep its actual port assignment.");
            transport.Confirmed = true;
            Await((Task)Invoke(form, "ScanKktsCoreAsync", CancellationToken.None));
            object scan = Field(form, "_manualScan");
            var sequential = (SequentialKktDiscovery)scan.GetType().GetField("Sequential", Private).GetValue(scan);
            sequential.Targets.Clear();
            grid.CurrentCell = grid.Rows[0].Cells[0];
            ((CheckBox)Field(form, "_atolConfirmedCheckBox")).Checked = true;
            int attempts = transport.MutationAttempts;
            try { Await((Task)Invoke(form, "RegisterManualSelectionCoreAsync", CancellationToken.None)); }
            catch (InvalidOperationException) { }
            if (transport.MutationAttempts != attempts)
                throw new Exception("The primary manual path must reject a missing physical target before any POST.");
        }
        CheckPortAssignments();
    }

    private static void CheckPortAssignments()
    {
        var ports = new Connections();
        var transport = new Transport(ports);
        using (var form = new MainForm(new TspiotApiClient(new HttpClient(transport)), ports))
        {
            form.Show();
            ((TabControl)Field(form, "_workspaceTabs")).SelectedTab = (TabPage)Field(form, "_manualKktTab");
            Await((Task)Invoke(form, "ScanKktsCoreAsync", CancellationToken.None));
            var grid = (DataGridView)Field(form, "_discoveredKktsGrid");
            grid.CurrentCell = grid.Rows[1].Cells[0];
            foreach (string conflictingPort in new[] { "51401", "050401" })
            {
                ((TextBox)Field(form, "_portTextBox")).Text = conflictingPort;
                ((TextBox)Field(form, "_softPortTextBox")).Text = "51409";
                try { Await((Task)Invoke(form, "RegisterManualSelectionCoreAsync", CancellationToken.None)); }
                catch (InvalidOperationException) { }
                if (transport.MutationAttempts != 0)
                    throw new Exception("Cross-role and numerically identical ports must be rejected before registration.");
            }
            ports.OnlySecond = true;
            Await((Task)Invoke(form, "ScanKktsCoreAsync", CancellationToken.None));
            ((TextBox)Field(form, "_portTextBox")).Text = "50401";
            ((TextBox)Field(form, "_softPortTextBox")).Text = "51401";
            Await((Task)Invoke(form, "RegisterManualSelectionCoreAsync", CancellationToken.None));
            if (transport.Writes.Count != 1)
                throw new Exception("An absent unregistered KKT's cached ports must not block the current scan.");
        }
    }

    private static object Field(object value, string name)
    { return value.GetType().GetField(name, Private).GetValue(value); }
    private static object Invoke(object value, string name, params object[] args)
    { return value.GetType().GetMethod(name, Private).Invoke(value, args); }
    private static void Capture(Form form, string name)
    {
        string directory = Environment.GetEnvironmentVariable("MULTIKKT_UI_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        System.IO.Directory.CreateDirectory(directory);
        using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size)); bitmap.Save(System.IO.Path.Combine(directory, name)); }
    }
    private static void Await(Task task)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        { Application.DoEvents(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new Exception("Setup UI check timed out.");
        task.GetAwaiter().GetResult();
    }

    internal sealed class Connections : IKktConnectionProvider
    {
        public int Active = -1;
        public bool RegistrationWasIsolated;
        public bool OnlySecond;
        public bool AllowSimultaneous;
        public readonly HashSet<int> OpenIndexes = new HashSet<int>();
        public IList<KktConnectionPort> EnumeratePorts()
        { if (OnlySecond) return new[] { new KktConnectionPort { PortName = "COM8" } };
            return new[] { new KktConnectionPort { PortName = "COM7" },
            new KktConnectionPort { PortName = "COM8" } }; }
        public Task<IKktConnectionLease> OpenAsync(KktConnectionPort port, CancellationToken token)
        {
            if (Active != -1 && !AllowSimultaneous) throw new Exception("A previous lease remained open.");
            Active = port.PortName == "COM7" ? 0 : 1;
            OpenIndexes.Add(Active);
            return Task.FromResult<IKktConnectionLease>(new Lease(this, port.PortName, Active));
        }
        private sealed class Lease : IKktConnectionLease
        {
            private readonly Connections _owner;
            private readonly int _index;
            public Lease(Connections owner, string port, int index)
            { _owner = owner; _index = index; Identity = new KktConnectionIdentity { PortName = port,
                KktSerial = "0010570000000" + (index + 1).ToString() }; }
            public KktConnectionIdentity Identity { get; private set; }
            public void Dispose()
            {
                _owner.OpenIndexes.Remove(_index);
                _owner.Active = -1;
                foreach (int index in _owner.OpenIndexes) _owner.Active = index;
            }
        }
    }

    internal sealed class Transport : HttpMessageHandler
    {
        private readonly Connections _ports;
        public readonly List<string> Writes = new List<string>();
        public int MutationAttempts;
        public bool Confirmed = true;
        public bool MismatchedIdentity;
        public bool ChangedDkktPort;
        public TaskCompletionSource<HttpResponseMessage> PendingInstances;
        public int InfoCalls;
        public bool AllowAll;
        private readonly Dictionary<string, AddTspiotRequest> _instances =
            new Dictionary<string, AddTspiotRequest>();
        public Transport(Connections ports) { _ports = ports; }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellation)
        {
            string path = request.RequestUri.AbsolutePath;
            string json;
            if (request.Method == HttpMethod.Post)
            {
                MutationAttempts++;
                var add = System.Text.RegularExpressions.Regex.Match(
                    await request.Content.ReadAsStringAsync(), "\\\"id\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                string serial = add.Groups[1].Value;
                if (AllowAll ? serial != "0010570000000" + (_ports.Active + 1).ToString()
                    : serial != "00105700000002" || _ports.Active != 1)
                    throw new Exception("Registration was not isolated to the selected target.");
                _ports.RegistrationWasIsolated = true;
                string body = await request.Content.ReadAsStringAsync();
                _instances[serial] = new AddTspiotRequest { Id = serial, Port = ReadPort(body, "port"), SoftPort = ReadPort(body, "softPort") };
                Writes.Add(serial);
                json = "{}";
            }
            else if (request.Method != HttpMethod.Get)
                throw new Exception("Unexpected mutation " + request.Method);
            else if (path.EndsWith("/dkktList"))
            {
                if (_ports.Active < 0) return new HttpResponseMessage(HttpStatusCode.NoContent);
                var devices = new List<string>();
                foreach (int index in _ports.OpenIndexes)
                    devices.Add("{" + Identity("0010570000000" + (index + 1).ToString()) + "}");
                json = "{\"kkt\":[" + string.Join(",", devices.ToArray()) + "]}";
            }
            else if (path.EndsWith("/instances/info"))
            {
                if (PendingInstances != null) return await PendingInstances.Task;
                var rows = new List<string>();
                foreach (var pair in _instances)
                    rows.Add("{\"id\":\"" + pair.Key + "\",\"port\":" + pair.Value.Port + ",\"softPort\":" + pair.Value.SoftPort + ",\"dkktPort\":" + (ChangedDkktPort ? "4043" : "4042") + "}");
                json = "{\"instances\":[" + string.Join(",", rows.ToArray()) + "]}";
            }
            else if (path.Contains("/instances/info/"))
            {
                string serial = path.Substring(path.LastIndexOf('/') + 1);
                string identity = Identity(serial);
                if (MismatchedIdentity) identity = identity.Replace("1234567894", "7701234560");
                json = "{\"state\":\"" + (Confirmed ? "Registered" : "Created") + "\",\"regData\":{" + identity + "}}";
            }
            else if (path.Contains("/settings/")) json = "{}";
            else if (path == "/api/v2/info") { InfoCalls++; await Task.Delay(20); json = "{}"; }
            else throw new Exception("Unexpected request " + path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
        private static string Identity(string serial)
        { return "\"kktSerial\":\"" + serial + "\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\""; }
        private static int ReadPort(string json, string key)
        { return int.Parse(System.Text.RegularExpressions.Regex.Match(json, "\\\"" + key + "\\\"\\s*:\\s*(\\d+)").Groups[1].Value); }
    }
}
