using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class LmGatewayPage
    {
        private readonly Dictionary<string, LmGatewayCredentials> _lmSetupCredentials =
            new Dictionary<string, LmGatewayCredentials>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LmGatewayTarget> _appliedLmTargets =
            new Dictionary<string, LmGatewayTarget>(StringComparer.Ordinal);

        public bool HasRemoteLocalModules
        {
            get { return ReadRemoteLocalModules().Count > 0; }
        }

        public bool PromptLmSetup(IWin32Window owner, IList<LmGatewayKkt> kkts)
        {
            SortedDictionary<string, string> stored = ReadRemoteLocalModulesStrict();
            using (LmSetupDialog dialog = new LmSetupDialog(
                kkts, stored, GetLmCredentialsForEndpoint, SaveLmSetup))
                return dialog.ShowDialog(owner) == DialogResult.OK;
        }

        public void ShowRemoteLocalModules(IWin32Window owner)
        {
            List<LmGatewayKkt> kkts = new List<LmGatewayKkt>();
            if (_currentDiscovery != null) kkts.AddRange(_currentDiscovery.Items);
            PromptLmSetup(owner, kkts);
        }

        private void SaveLmSetup(IDictionary<string, string> choices,
            IDictionary<string, LmGatewayCredentials> credentials)
        {
            // Re-read before saving: other INNs may have changed while the dialog was open.
            SortedDictionary<string, string> merged = MergeLmEndpoints(
                ReadRemoteLocalModulesStrict(), choices);
            RemoteLocalModuleStore.CreateDefault().Save(merged);
            foreach (KeyValuePair<string, string> pair in choices)
            {
                LmGatewayCredentials value = credentials[pair.Key];
                _lmSetupCredentials[LmCredentialKey(pair.Key, pair.Value)] =
                    new LmGatewayCredentials { Login = value.Login, Password = value.Password };
            }
            Log("Размещение ЛМ ЧЗ сохранено для ИНН: " +
                string.Join(", ", new List<string>(choices.Keys).ToArray()) +
                ". Настройка компонентов ещё не выполнена.\r\n");
            FillRows(null);
        }

        private void RefreshAppliedLmTargets()
        {
            _appliedLmTargets.Clear();
            if (FmuApiMode) return;
            try
            {
                foreach (DirectControllerProvisioningItemRequest item in new DirectControllerOperatorInventoryReader().ReadRemovalItems())
                    _appliedLmTargets[item.KktSerial] = new LmGatewayTarget(
                        LocalModuleTargetAddress.Describe(item.TargetLocalModuleAddress), item.TargetLocalModulePort);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is InvalidDataException) && !(ex is UnauthorizedAccessException) &&
                    !(ex is ArgumentException) && !(ex is System.Security.SecurityException)) throw;
                Log("Применённые адреса контроллеров не прочитаны: " + SensitiveDataMasker.Mask(ex.Message) + "\r\n");
            }
        }

        private static bool NeedsLmTargetApplication(LmGatewayTarget applied, string desiredRemote)
        {
            string host;
            int port;
            if (LocalModuleTargetAddress.TryParseEndpoint(desiredRemote, out host, out port))
                return applied == null || applied.Port != port ||
                    !string.Equals(applied.Address, host, StringComparison.OrdinalIgnoreCase);
            return applied != null && !string.Equals(applied.Address, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
        }

        private static SortedDictionary<string, string> MergeLmEndpoints(
            IDictionary<string, string> stored, IDictionary<string, string> choices)
        {
            SortedDictionary<string, string> merged =
                new SortedDictionary<string, string>(stored, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in choices)
            {
                if (!LocalModuleMsiIdentity.IsInn(pair.Key))
                    throw new ArgumentException("ИНН должен содержать 10 или 12 цифр.");
                if (string.IsNullOrEmpty(pair.Value)) merged.Remove(pair.Key);
                else merged[pair.Key] = pair.Value;
            }
            HashSet<string> endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string inn in new List<string>(merged.Keys))
            {
                string host;
                int port;
                if (!LocalModuleMsiIdentity.IsInn(inn) ||
                    !LocalModuleTargetAddress.TryParseEndpoint(merged[inn], out host, out port))
                    throw new ArgumentException("Адрес ЛМ для ИНН " + inn + " недопустим.");
                string endpoint = LocalModuleTargetAddress.Key(host, port);
                if (!endpoints.Add(endpoint))
                    throw new ArgumentException("Адрес " + endpoint +
                        " указан для нескольких ИНН: один ЛМ обслуживает один ИНН.");
                merged[inn] = endpoint;
            }
            return merged;
        }

        private LmGatewayCredentials GetLmCredentials(string inn)
        {
            string endpoint;
            ReadRemoteLocalModulesStrict().TryGetValue((inn ?? string.Empty).Trim(), out endpoint);
            return GetLmCredentialsForEndpoint(inn, endpoint);
        }

        private LmGatewayCredentials GetLmCredentialsForEndpoint(string inn, string endpoint)
        {
            LmGatewayCredentials value;
            if (_lmSetupCredentials.TryGetValue(LmCredentialKey(inn, endpoint), out value))
                return new LmGatewayCredentials { Login = value.Login, Password = value.Password };
            return LmGatewayCredentialDefaults.Create();
        }

        private static string LmCredentialKey(string inn, string endpoint)
        {
            string host;
            int port;
            string normalized = string.IsNullOrEmpty(endpoint) ? "local" : endpoint;
            if (LocalModuleTargetAddress.TryParseEndpoint(endpoint, out host, out port))
                normalized = LocalModuleTargetAddress.Key(host, port);
            return (inn ?? string.Empty).Trim() + "\n" + normalized;
        }

        private static SortedDictionary<string, string> ReadRemoteLocalModulesStrict()
        {
            return MergeLmEndpoints(RemoteLocalModuleStore.CreateDefault().LoadStrict(),
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        private SortedDictionary<string, string> ReadRemoteLocalModules()
        {
            try
            {
                return ReadRemoteLocalModulesStrict();
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is InvalidDataException) && !(ex is UnauthorizedAccessException) &&
                    !(ex is ArgumentException)) throw;
                Log("Адреса ЛМ на другом компьютере не прочитаны: " +
                    SensitiveDataMasker.Mask(ex.Message) + "\r\n");
                return new SortedDictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private IList<LmGatewayKkt> SelectLocalLmKkts(IList<LmGatewayKkt> kkts)
        {
            SortedDictionary<string, string> remote = ReadRemoteLocalModulesStrict();
            List<LmGatewayKkt> result = new List<LmGatewayKkt>();
            if (kkts == null) return result;
            for (int index = 0; index < kkts.Count; index++)
            {
                LmGatewayKkt kkt = kkts[index];
                string inn = kkt == null ? string.Empty : (kkt.KktInn ?? string.Empty).Trim();
                if (!remote.ContainsKey(inn)) result.Add(kkt);
            }
            return result;
        }

        private IDictionary<string, int> CreateControllerTargetPorts(LocalModuleMsiPlan plan)
        {
            IDictionary<string, int> result = plan.CreateTargetApiPortMap();
            foreach (KeyValuePair<string, string> pair in ReadRemoteLocalModulesStrict())
            {
                string host;
                int port;
                if (LocalModuleTargetAddress.TryParseEndpoint(pair.Value, out host, out port))
                    result[pair.Key] = port;
            }
            return result;
        }

        private IDictionary<string, string> CreateControllerTargetAddresses()
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in ReadRemoteLocalModulesStrict())
            {
                string host;
                int port;
                if (LocalModuleTargetAddress.TryParseEndpoint(pair.Value, out host, out port))
                    result[pair.Key] = host;
            }
            return result;
        }
    }
}
