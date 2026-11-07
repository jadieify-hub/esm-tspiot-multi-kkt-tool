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
                int infoBeforeRefresh = api.InfoCalls;
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
                if (api.InfoCalls != infoBeforeRefresh)
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
                CheckUninstalledModuleRows(page, session);
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
                    CheckSelectedDiagnosis(page, api);
                    CheckPartialSetupOutcome();
                }
            }
        }
    }

    private static void CheckUninstalledModuleRows(
        LmGatewayPage page, LmGatewayBindingSession session)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo inventoryField = typeof(LmGatewayPage)
            .GetField("_localModuleMsiInventory", flags);
        object savedInventory = inventoryField.GetValue(page);
        string savedInn = session.Rows[1].Kkt.KktInn;
        object snapshot = Activator.CreateInstance(inventoryField.FieldType, true);
        var items = (IList<LocalModuleMsiInventoryItem>)inventoryField.FieldType
            .GetProperty("Items", flags).GetValue(snapshot, null);
        var installed = (IList<string>)inventoryField.FieldType
            .GetProperty("InstalledModuleInns", flags).GetValue(snapshot, null);
        var grid = (DataGridView)typeof(LmGatewayPage)
            .GetField("_grid", flags).GetValue(page);
        string[] inns = { savedInn, "123456789012" };
        try
        {
            session.Rows[1].Kkt.KktInn = inns[1];
            for (int index = 0; index < 2; index++)
            {
                items.Add(new LocalModuleMsiInventoryItem {
                    Inn = inns[index], CloneOrdinal = index,
                    ApiPort = index == 0 ? 5995 : 6995 });
                installed.Add(inns[index]);
            }
            inventoryField.SetValue(page, snapshot);
            for (int removedCount = 0; removedCount <= 2; removedCount++)
            {
                if (removedCount > 0) installed.Remove(inns[removedCount - 1]);
                typeof(LmGatewayPage).GetMethod("FillRows", flags)
                    .Invoke(page, new object[] { string.Empty });
                if (grid.Rows.Count != 2 || items.Count != 2)
                    throw new Exception("Uninstall must preserve KKTs and saved LM assignments.");
                for (int index = 0; index < 2; index++)
                {
                    string expectedState = index < removedCount
                        ? "Не установлен" : "Установлен; сверка при операции";
                    if ((string)grid.Rows[index].Cells["LmState"].Value != expectedState)
                        throw new Exception("LM status must reflect service presence for each INN.");
                    if ((string)grid.Rows[index].Cells["LmEndpoint"].Value !=
                        (index == 0 ? "127.0.0.1:5995" : "127.0.0.1:6995"))
                        throw new Exception("Uninstall must preserve the assigned LM port.");
                }
            }
        }
        finally
        {
            inventoryField.SetValue(page, savedInventory);
            session.Rows[1].Kkt.KktInn = savedInn;
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

    private static void CheckSelectedDiagnosis(LmGatewayPage page, DiscoveryOnlyApi api)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var method = typeof(LmGatewayPage).GetMethod("DiagnoseKktAsync", flags);
        if (method == null) throw new Exception("Selected-cash diagnostics need an explicit serial scope.");
        api.InfoPorts.Clear();
        api.DetailIds.Clear();
        Task diagnosis = (Task)method.Invoke(page, new object[] { "00105700000002" });
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (!diagnosis.IsCompleted && DateTime.UtcNow < deadline)
        { Application.DoEvents(); Thread.Sleep(5); }
        if (!diagnosis.IsCompleted) throw new Exception("Selected-cash diagnosis timed out.");
        diagnosis.GetAwaiter().GetResult();
        if (api.InfoPorts.Count != 1 || api.InfoPorts[0] != "51402")
            throw new Exception("Diagnostics of KKT 2 must not poll KKT 1.");
        if (api.DetailIds.Count != 1 || api.DetailIds[0] != "00105700000002")
            throw new Exception("Selected diagnostics must not read another KKT's details.");
    }

    private static void CheckPartialSetupOutcome()
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var assembly = typeof(LmGatewayPage).Assembly;
        Type controllerType = assembly.GetType("EsmTspiot.WinForms.Shared.DirectControllerSetupOutcome", true);
        Type outcomeType = assembly.GetType("EsmTspiot.WinForms.Shared.FullAutomaticLocalSetupOutcome", true);
        object controllers = Activator.CreateInstance(controllerType, true);
        var ready = (IDictionary<string, DirectControllerAssignment>)controllerType
            .GetProperty("ReadyAssignments", flags).GetValue(controllers, null);
        ready.Add("A", new DirectControllerAssignment { KktSerial = "A" });
        ready.Add("B", new DirectControllerAssignment { KktSerial = "B" });
        object outcome = Activator.CreateInstance(outcomeType, true);
        outcomeType.GetProperty("Controllers", flags).SetValue(outcome, controllers, null);
        var record = outcomeType.GetMethod("RecordKktStates", flags);
        if (record == null) throw new Exception("Partial setup needs per-KKT states from structured stage results.");
        record.Invoke(outcome, new object[] {
            new[] { new LmGatewayKkt { KktSerial = "A", KktInn = "1234567894" },
                new LmGatewayKkt { KktSerial = "B", KktInn = "1234567894" } },
            new Dictionary<string, LocalModuleMsiProvisioningItemResult>(),
            new Dictionary<string, LmGatewayBindingStatus> {
                { "A", LmGatewayBindingStatus.BindingAccepted }, { "B", LmGatewayBindingStatus.BindingFailed } } });
        var states = (IDictionary<string, string>)outcomeType.GetProperty("KktStates", flags).GetValue(outcome, null);
        var failed = (IList<string>)outcomeType.GetProperty("FailedSerials", flags).GetValue(outcome, null);
        if (states["A"] != "Параметры применены" || states["B"] != "Привязка не применена" ||
            failed.Count != 1 || failed[0] != "B")
            throw new Exception("A failed binding for B must not mark A as failed or select A for recovery.");
        outcomeType.GetProperty("FmuApiMode", flags).SetValue(outcome, true, null);
        var remoteInns = (ISet<string>)outcomeType.GetProperty("RemoteModuleInns", flags).GetValue(outcome, null);
        remoteInns.Add("7701234560");
        record.Invoke(outcome, new object[] {
            new[] { new LmGatewayKkt { KktSerial = "A", KktInn = "1234567894" },
                new LmGatewayKkt { KktSerial = "B", KktInn = "123456789012" },
                new LmGatewayKkt { KktSerial = "C", KktInn = "7701234560" },
                new LmGatewayKkt { KktSerial = "D", KktInn = "7701234567" } },
            new Dictionary<string, LocalModuleMsiProvisioningItemResult> {
                { "1234567894", new LocalModuleMsiProvisioningItemResult { Status = LmServiceProvisioningStatus.Succeeded } },
                { "123456789012", new LocalModuleMsiProvisioningItemResult { Status = LmServiceProvisioningStatus.Failed } },
                { "7701234567", new LocalModuleMsiProvisioningItemResult { Status = LmServiceProvisioningStatus.Cancelled } } },
            new Dictionary<string, LmGatewayBindingStatus>() });
        if (states["A"] != "ЛМ подготовлен; контроллеры пропущены" || states["B"] != "ЛМ не установлен" ||
            states["C"] != "Адрес удалённого ЛМ учтён; контроллеры пропущены" || states["D"] != "Установка ЛМ отложена" ||
            failed.Count != 1 || failed[0] != "B")
            throw new Exception("FMU-mode results must retain per-INN success, remote choices and operator deferrals despite another INN failure.");
        outcomeType.GetProperty("FmuApiMode", flags).SetValue(outcome, false, null);
        outcomeType.GetProperty("Cancelled", flags).SetValue(outcome, true, null);
        outcomeType.GetProperty("Controllers", flags).SetValue(outcome, null, null);
        record.Invoke(outcome, new object[] {
            new[] { new LmGatewayKkt { KktSerial = "A", KktInn = "1234567894" },
                new LmGatewayKkt { KktSerial = "B", KktInn = "123456789012" },
                new LmGatewayKkt { KktSerial = "C", KktInn = "7701234560" } },
            new Dictionary<string, LocalModuleMsiProvisioningItemResult> {
                { "1234567894", new LocalModuleMsiProvisioningItemResult { Status = LmServiceProvisioningStatus.Succeeded } },
                { "123456789012", new LocalModuleMsiProvisioningItemResult { Status = LmServiceProvisioningStatus.Cancelled } } },
            new Dictionary<string, LmGatewayBindingStatus>() });
        if (states["A"] != "ЛМ установлен; контроллеры не настраивались" || states["B"] != "Установка ЛМ отложена" ||
            states["C"] != "Остановлено; состояние компонентов не подтверждено")
            throw new Exception("Stopping after a partial MSI batch must retain confirmed success and distinguish unknown results.");
    }

    private sealed class DiscoveryOnlyApi : ITspiotApiClient
    {
        public int InfoCalls;
        public int BindingCalls;
        public readonly List<string> InfoPorts = new List<string>();
        public readonly List<string> DetailIds = new List<string>();
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
            DetailIds.Add(id);
            return Success("{\"state\":\"Registered\",\"regData\":{\"kktSerial\":\"" + id +
                "\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}}");
        }

        public Task<ApiResponse> GetLmInfoAsync(
            string url, string port, string softPort, CancellationToken cancellation)
        {
            InfoCalls++;
            InfoPorts.Add(softPort);
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
