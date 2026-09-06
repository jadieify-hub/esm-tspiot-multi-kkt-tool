using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.WinForms.Shared;

namespace EsmTspiot.Operator.Tests
{
    internal static class FrontolTests
    {
        public static void CheckLocalTransaction()
        {
            FrontolSettings settings = FrontolConfiguration.LoadSettings(FrontolConfiguration.DefaultIniPath);
            var database = new FrontolDatabase(settings);
            List<FrontolDevice> before = database.ReadDevices(CancellationToken.None);
            if (before.Count < 2) throw new Exception("Create two test KKT in the current Frontol workplace first.");
            var changes = new List<FrontolChange>();
            foreach (FrontolDevice device in before)
                changes.Add(new FrontolChange { Device = device, ComPort = device.ComPort, KktSerial = "test",
                    OldHost = device.Host, OldPort = device.Port, NewHost = "127.0.0.2", NewPort = 51900 + device.Code });
            string result = database.RunSql("ROLLBACK;\nSET TRANSACTION SNAPSHOT NO WAIT;\n" +
                database.BuildWriteBlock(changes, false) +
                "\nSELECT COUNT(*) AS KRS_VERIFIED FROM ECRDEV WHERE ESMHOST='127.0.0.2' AND ID IN (" +
                before[0].Id + "," + before[1].Id + ");\n" +
                database.BuildWriteBlock(changes, true).Replace("KRS_APPLIED", "KRS_RESTORED") +
                "\nROLLBACK;\n", CancellationToken.None);
            Equal(before.Count, FrontolDatabase.ReadInteger(result, "KRS_APPLIED"), "All planned rows were written inside the transaction");
            Equal(2, FrontolDatabase.ReadInteger(result, "KRS_VERIFIED"), "Readback must see changed values before rollback");
            Equal(before.Count, FrontolDatabase.ReadInteger(result, "KRS_RESTORED"), "Restore must accept only the just-applied values");
            List<FrontolDevice> after = database.ReadDevices(CancellationToken.None);
            for (int i = 0; i < before.Count; i++)
            {
                Equal(before[i].Host, after[i].Host, "Rollback preserves the original host");
                Equal(before[i].Port, after[i].Port, "Rollback preserves the original port");
                Equal(before[i].ChangeStamp, after[i].ChangeStamp, "Driver settings must not be updated");
            }
            changes[1].OldPort = -1; // A stale snapshot must prevent every update.
            result = database.RunSql("ROLLBACK;\nSET TRANSACTION SNAPSHOT NO WAIT;\n" +
                database.BuildWriteBlock(changes, false) + "\nROLLBACK;\n", CancellationToken.None);
            Equal(0, FrontolDatabase.ReadInteger(result, "KRS_APPLIED"), "A conflict must reject the entire batch");
            var staleNoOp = new List<FrontolChange> {
                new FrontolChange { Device = before[0], OldHost = "stale-snapshot.invalid", NewHost = "stale-snapshot.invalid",
                    OldPort = 51901, NewPort = 51901 }
            };
            Reject(delegate { database.Apply(staleNoOp, CancellationToken.None); });
            Console.WriteLine("Verified " + before.Count + " Frontol KKT; no test changes committed.");
        }

        public static void ReadsCashierSettings()
        {
            string directory = Path.Combine(Path.GetTempPath(), "frontol-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string ini = Path.Combine(directory, "Frontol.ini");
            try
            {
                File.WriteAllText(ini, "[DATABASE]\r\nPath=localhost:C:\\Касса\r\nDB=MAIN.GDB\r\nUser=SYSDBA\r\n[POS]\r\nNPOS=7\r\n[DB0]\r\nPath=C:\\Wrong\r\n", Encoding.Unicode);
                FrontolSettings settings = FrontolConfiguration.LoadSettings(ini);
                Equal("localhost:C:\\Касса\\MAIN.GDB", settings.Database, "Cashier DATABASE section");
                Equal(7, settings.Workplace, "NPOS is the RMK code, not its internal ID");
                Equal("COM4", FrontolConfiguration.ReadComPort("{\"Port\":0,\"ComFile\":\"4\",\"IPAddress\":\"192.168.1.10\"}"), "Numeric ComFile");
                Equal("COM8", FrontolConfiguration.ReadComPort("{\"Port\":0,\"ComFile\":\"com8\"}"), "COM prefix");
                Reject(delegate { FrontolConfiguration.ReadComPort("{\"Port\":2,\"ComFile\":\"4\"}"); });
                File.WriteAllText(ini, "[DATABASE]\r\nDB=MAIN.GDB\r\n[POS]\r\nNPOS=1\r\n[DB0]\r\nPath=C:\\Wrong\r\n", Encoding.Unicode);
                Reject(delegate { FrontolConfiguration.LoadSettings(ini); });
            }
            finally
            {
                File.Delete(ini);
                Directory.Delete(directory);
            }
        }

        public static void MapsSoftwarePorts()
        {
            List<FrontolDevice> devices = Devices();
            List<FrontolChange> changes = FrontolConfiguration.BuildPlan(devices, Identities(), Instances(), "http://127.0.0.1:51077");
            Equal(2, changes.Count, "Both KKT must be mapped");
            Equal("COM4", changes[0].ComPort, "First hardware port");
            Equal("A", changes[0].KktSerial, "Match by serial, not list order");
            Equal(51409, changes[0].NewPort, "Use real softPort, not 50409 or an ordinal");
            Equal(51402, changes[1].NewPort, "Second software port");
            Equal("127.0.0.1", changes[0].NewHost, "Only host, no scheme or orchestrator port");
            Equal(51401, changes[0].OldPort.Value, "Keep old value for rollback");
            Equal<string>(null, changes[1].OldHost, "Preserve null rather than empty");
        }

        public static void UsesConfirmedApi1ClientPorts()
        {
            var api = new FrontolApi1Fake();
            LmGatewayDiscovery discovery = new LmGatewayDiscoveryWorkflow(api)
                .DiscoverAsync("http://127.0.0.1:51077", null, CancellationToken.None)
                .GetAwaiter().GetResult();

            Equal(true, discovery.IsSuccessful, "API v1 discovery");
            Equal(2, discovery.Items.Count, "All registered KKT must remain visible");
            List<FrontolChange> changes = FrontolConfiguration.BuildPlan(
                Devices(), Identities(), discovery.Items, "http://127.0.0.1:51077");
            Equal(51409, changes[0].NewPort, "Invalid softPort must use freshly read clientPort");
            Equal(51402, changes[1].NewPort, "Valid softPort must be preserved");
            Equal(0, api.LmInfoCalls, "Frontol discovery must not use /api/v2/info");
        }

        public static void RejectsUnsafeMatches()
        {
            List<FrontolDevice> devices = Devices();
            devices[1].ComPort = "COM4";
            Reject(delegate { FrontolConfiguration.BuildPlan(devices, Identities(), Instances(), "http://127.0.0.1:51077"); });
            List<KktConnectionIdentity> identities = Identities();
            identities.RemoveAt(1);
            Reject(delegate { FrontolConfiguration.BuildPlan(Devices(), identities, Instances(), "http://127.0.0.1:51077"); });
            List<LmGatewayKkt> instances = Instances();
            instances[0].SoftPort = "0";
            Reject(delegate { FrontolConfiguration.BuildPlan(Devices(), Identities(), instances, "http://127.0.0.1:51077"); });
            instances = Instances();
            instances[0].RegistrationConfirmed = false;
            Reject(delegate { FrontolConfiguration.BuildPlan(Devices(), Identities(), instances, "http://127.0.0.1:51077"); });
        }

        internal static List<FrontolDevice> Devices()
        {
            return new List<FrontolDevice> {
                new FrontolDevice { Id = 11, DeviceId = 101, Code = 1, Name = "ККТ 1", ComPort = "COM4", Host = "127.0.0.1", Port = 51401, ChangeStamp = 90 },
                new FrontolDevice { Id = 12, DeviceId = 102, Code = 2, Name = "ККТ 2", ComPort = "COM8", Host = null, Port = null, ChangeStamp = 91 }
            };
        }

        private static List<KktConnectionIdentity> Identities()
        {
            return new List<KktConnectionIdentity> {
                new KktConnectionIdentity { PortName = "COM8", KktSerial = "B" },
                new KktConnectionIdentity { PortName = "COM4", KktSerial = "A" }
            };
        }

        private static List<LmGatewayKkt> Instances()
        {
            return new List<LmGatewayKkt> {
                new LmGatewayKkt { InstanceId = "B", KktSerial = "B", Port = "50402", SoftPort = "51402", RegistrationConfirmed = true },
                new LmGatewayKkt { InstanceId = "A", KktSerial = "A", Port = "50409", SoftPort = "51409", RegistrationConfirmed = true }
            };
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!object.Equals(expected, actual)) throw new Exception(message + ": expected " + expected + ", got " + actual);
        }

        private static void Reject(Action action)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            throw new Exception("Unsafe Frontol input must be rejected before any write.");
        }

        private sealed class FrontolApi1Fake : ITspiotApiClient
        {
            public int LmInfoCalls { get; private set; }

            public Task<ApiResponse> GetInstancesAsync(string baseUrl, CancellationToken token)
            {
                return Result("[{\"id\":\"A\",\"port\":\"50409\",\"softPort\":\"0\"}," +
                    "{\"id\":\"B\",\"port\":\"50402\",\"softPort\":\"51402\"}]");
            }

            public Task<ApiResponse> GetInstanceAsync(string baseUrl, string id, CancellationToken token)
            {
                string clientPort = id == "A" ? "51409" : "51402";
                return Result("{\"state\":\"Registered\",\"clientPort\":\"" + clientPort +
                    "\",\"regData\":{\"kktSerial\":\"" + id +
                    "\",\"fnSerial\":\"FN-" + id + "\",\"kktInn\":\"INN-" + id + "\"}}");
            }

            public Task<ApiResponse> GetLmInfoAsync(string baseUrl, string instancePort,
                string softPort, CancellationToken token)
            {
                LmInfoCalls++;
                return Unexpected();
            }

            public Task<ApiResponse> GetDkktListAsync(string baseUrl, CancellationToken token) { return Unexpected(); }
            public Task<ApiResponse> GetSettingsAsync(string baseUrl, string id, CancellationToken token) { return Unexpected(); }
            public Task<ApiResponse> AddInstanceAsync(string baseUrl, AddTspiotRequest request, CancellationToken token) { return Unexpected(); }
            public Task<ApiResponse> RegisterInstanceAsync(string baseUrl, RegisterTspiotRequest request, CancellationToken token) { return Unexpected(); }
            public Task<ApiResponse> DeleteInstanceAsync(string baseUrl, string id, CancellationToken token) { return Unexpected(); }
            public Task<ApiResponse> ConfigureLmGatewayAsync(string baseUrl, string id, LmConnectionRequest request, CancellationToken token) { return Unexpected(); }

            private static Task<ApiResponse> Result(string body)
            {
                return Task.FromResult(new ApiResponse
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    ResponseBody = body
                });
            }

            private static Task<ApiResponse> Unexpected()
            {
                throw new InvalidOperationException("Unexpected API call in Frontol discovery.");
            }
        }
    }
}
