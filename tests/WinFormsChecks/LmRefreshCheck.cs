using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.WinForms.Shared;

public static class LmRefreshCheck
{
    public static void Run()
    {
        var api = new DiscoveryOnlyApi();
        using (var page = new LmGatewayPage(
            delegate { return "http://127.0.0.1:51077"; }, api, null))
        {
            foreach (bool fmuMode in new[] { false, true })
            {
                page.FmuApiMode = fmuMode;
                int writesBeforeRefresh = api.BindingCalls;
                Task refresh = page.RefreshAsync();
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (!refresh.IsCompleted && DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                if (!refresh.IsCompleted)
                    throw new Exception("LM table refresh did not finish.");
                refresh.GetAwaiter().GetResult();
                if (api.InfoCalls != 0)
                    throw new Exception("Table refresh must not poll LM readiness.");
                if (api.BindingCalls != writesBeforeRefresh)
                    throw new Exception("Table refresh must not write LM settings.");
                var session = (LmGatewayBindingSession)typeof(LmGatewayPage)
                    .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(page);
                if (session.Rows.Count != 2 ||
                    session.Rows[0].Kkt.KktSerial != "00105700000001" ||
                    session.Rows[1].Kkt.KktSerial != "00105700000002")
                    throw new Exception("Both registered KKTs must remain in the table.");
                if (!fmuMode)
                {
                    var grid = (DataGridView)typeof(LmGatewayPage)
                        .GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(page);
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        if ((string)row.Cells["KktSerial"].Value == "00105700000002" &&
                            (string)row.Cells["EsmLinkState"].Value != "Не проверялась")
                            throw new Exception("An unchecked binding must not be displayed as absent.");
                    }
                    CheckBindingResult(page, api, session);
                }
            }
        }
    }

    private static void CheckBindingResult(
        LmGatewayPage page, DiscoveryOnlyApi api, LmGatewayBindingSession session)
    {
        LmGatewayKkt kkt = session.Rows[0].Kkt;
        Type outcomeType = typeof(LmGatewayPage).Assembly.GetType(
            "EsmTspiot.WinForms.Shared.DirectControllerSetupOutcome", true);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (bool accepted in new[] { false, true })
        {
            var previous = new LmGatewayBindingOutcome();
            previous.Results.Add(new LmGatewayBindingResult {
                KktSerial = kkt.KktSerial, Status = LmGatewayBindingStatus.BindingVerified });
            session.ApplyOutcome(previous);
            api.AcceptBinding = accepted;
            object outcome = Activator.CreateInstance(outcomeType, true);
            outcomeType.GetProperty("ExpectedKktCount", flags).SetValue(outcome, 1, null);
            outcomeType.GetProperty("ReadyCount", flags).SetValue(outcome, 1, null);
            var ready = (IDictionary<string, DirectControllerAssignment>)outcomeType
                .GetProperty("ReadyAssignments", flags).GetValue(outcome, null);
            ready.Add(kkt.KktSerial, new DirectControllerAssignment {
                KktSerial = kkt.KktSerial, KktInn = kkt.KktInn, Ordinal = 1,
                ServiceName = "esm-lm-controller", GrpcPort = 50063,
                RestPort = 5063, TargetLocalModulePort = 5995 });
            Task binding = (Task)typeof(LmGatewayPage)
                .GetMethod("BindReadyDirectControllersAsync", flags)
                .Invoke(page, new object[] { new List<LmGatewayKkt> { kkt },
                    outcome, CancellationToken.None });
            // The isolated API completes synchronously; no hardware or waiting.
            if (!binding.IsCompleted) throw new Exception("Binding unexpectedly waited.");
            binding.GetAwaiter().GetResult();
            typeof(LmGatewayPage).GetMethod("FinalizeDirectControllerOutcome", flags)
                .Invoke(page, new[] { outcome });
            bool complete = (bool)outcomeType.GetProperty("Complete", flags)
                .GetValue(outcome, null);
            if (complete != accepted)
                throw new Exception("A rejected settings write must not complete setup.");
            var expected = accepted ? LmGatewayBindingStatus.BindingAccepted
                : LmGatewayBindingStatus.BindingFailed;
            if (session.Rows[0].LastBindingStatus != expected || api.InfoCalls != 0)
                throw new Exception("A fresh binding result must replace stale diagnostics without polling.");
        }
    }

    private sealed class DiscoveryOnlyApi : ITspiotApiClient
    {
        public int InfoCalls;
        public int BindingCalls;
        public bool AcceptBinding;

        private static Task<ApiResponse> Success(string json)
        {
            return Task.FromResult(new ApiResponse {
                IsSuccess = true, StatusCode = 200, ResponseBody = json });
        }

        public Task<ApiResponse> GetInstancesAsync(string url, CancellationToken cancellation)
        {
            return Success("{\"instances\":[{\"id\":\"00105700000001\",\"port\":50401,\"softPort\":51401}," +
                "{\"id\":\"00105700000002\",\"port\":50402,\"softPort\":51402}]}");
        }

        public Task<ApiResponse> GetInstanceAsync(string url, string id, CancellationToken cancellation)
        {
            return Success("{\"regData\":{\"kktSerial\":\"" + id +
                "\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}}");
        }

        public Task<ApiResponse> GetLmInfoAsync(
            string url, string port, string softPort, CancellationToken cancellation)
        {
            InfoCalls++;
            return Task.FromResult(new ApiResponse { IsConnectionFailure = true });
        }

        public Task<ApiResponse> GetDkktListAsync(string url, CancellationToken cancellation)
        { throw new Exception("Refresh must not open hardware."); }
        public Task<ApiResponse> GetSettingsAsync(string url, string id, CancellationToken cancellation)
        { throw new Exception("Unexpected API call."); }
        public Task<ApiResponse> AddInstanceAsync(string url, AddTspiotRequest request, CancellationToken cancellation)
        { throw new Exception("Refresh must not add a KKT."); }
        public Task<ApiResponse> RegisterInstanceAsync(string url, RegisterTspiotRequest request, CancellationToken cancellation)
        { throw new Exception("Refresh must not register a KKT."); }
        public Task<ApiResponse> DeleteInstanceAsync(string url, string id, CancellationToken cancellation)
        { throw new Exception("Refresh must not delete a KKT."); }
        public Task<ApiResponse> ConfigureLmGatewayAsync(
            string url, string id, LmConnectionRequest request, CancellationToken cancellation)
        {
            BindingCalls++;
            return Task.FromResult(new ApiResponse {
                IsSuccess = AcceptBinding, StatusCode = AcceptBinding ? 201 : 500,
                DecodedMessage = AcceptBinding ? string.Empty : "error 2025: LM controller not ready" });
        }
    }
}
