using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class LmGatewayBindingSession
    {
        private readonly List<LmGatewayBindingSessionRow> _rows =
            new List<LmGatewayBindingSessionRow>();
        private readonly List<string> _invalidatedKktSerials = new List<string>();

        public IList<LmGatewayBindingSessionRow> Rows
        {
            get { return _rows; }
        }

        public IList<string> InvalidatedKktSerials
        {
            get { return _invalidatedKktSerials; }
        }

        public void ReplaceDiscovery(LmGatewayDiscovery discovery)
        {
            _invalidatedKktSerials.Clear();
            Dictionary<string, LmGatewayBindingSessionRow> existing =
                new Dictionary<string, LmGatewayBindingSessionRow>(StringComparer.Ordinal);
            for (int index = 0; index < _rows.Count; index++)
            {
                LmGatewayBindingSessionRow row = _rows[index];
                string serial = GetSerial(row == null ? null : row.Kkt);
                if (!string.IsNullOrEmpty(serial) && !existing.ContainsKey(serial))
                {
                    existing.Add(serial, row);
                }
            }

            _rows.Clear();
            if (discovery == null || !discovery.IsSuccessful)
            {
                return;
            }

            HashSet<string> added = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < discovery.Items.Count; index++)
            {
                LmGatewayKkt kkt = CopyKkt(discovery.Items[index]);
                string serial = GetSerial(kkt);
                if (string.IsNullOrEmpty(serial) || !added.Add(serial))
                {
                    continue;
                }

                LmGatewayBindingSessionRow row;
                LmGatewayBindingSessionRow previous;
                if (existing.TryGetValue(serial, out previous) &&
                    string.Equals(
                        Trim(previous == null || previous.Kkt == null ? null : previous.Kkt.KktInn),
                        Trim(kkt.KktInn),
                        StringComparison.Ordinal))
                {
                    row = previous;
                }
                else
                {
                    row = new LmGatewayBindingSessionRow
                    {
                        ControllerAddress = "127.0.0.1",
                        ControllerGrpcPort = string.Empty,
                        LastMessage = string.Empty
                    };
                    if (previous != null)
                    {
                        _invalidatedKktSerials.Add(serial);
                    }
                }

                row.Kkt = kkt;
                _rows.Add(row);
            }
        }

        public bool TryUpdateDraft(
            string kktSerial,
            string controllerAddress,
            string controllerGrpcPort,
            bool isSelected)
        {
            LmGatewayBindingSessionRow row = FindRow(kktSerial);
            if (row == null)
            {
                return false;
            }

            row.ControllerAddress = Trim(controllerAddress);
            row.ControllerGrpcPort = Trim(controllerGrpcPort);
            row.IsSelected = isSelected;
            return true;
        }

        public LmGatewayBindingPlan BuildSelectedPlan()
        {
            LmGatewayDiscovery discovery = new LmGatewayDiscovery();
            IList<LmGatewayBindingInput> inputs = new List<LmGatewayBindingInput>();
            for (int index = 0; index < _rows.Count; index++)
            {
                LmGatewayBindingSessionRow row = _rows[index];
                if (row == null || !row.IsSelected || row.Kkt == null)
                {
                    continue;
                }

                LmGatewayKkt kkt = CopyKkt(row.Kkt);
                discovery.Items.Add(kkt);
                inputs.Add(new LmGatewayBindingInput
                {
                    KktSerial = GetSerial(kkt),
                    KktInn = Trim(kkt.KktInn),
                    ControllerAddress = Trim(row.ControllerAddress),
                    ControllerGrpcPort = Trim(row.ControllerGrpcPort)
                });
            }

            return LmGatewayBindingPlanner.Build(discovery, inputs);
        }

        public void ApplyOutcome(LmGatewayBindingOutcome outcome)
        {
            if (outcome == null)
            {
                return;
            }

            for (int index = 0; index < outcome.Results.Count; index++)
            {
                LmGatewayBindingResult result = outcome.Results[index];
                LmGatewayBindingSessionRow row = FindRow(result == null ? null : result.KktSerial);
                if (row == null || result == null)
                {
                    continue;
                }

                row.LastBindingStatus = result.Status;
                row.LastMessage = SensitiveDataMasker.Mask(result.Details);
            }
        }

        private LmGatewayBindingSessionRow FindRow(string kktSerial)
        {
            string serial = Trim(kktSerial);
            for (int index = 0; index < _rows.Count; index++)
            {
                LmGatewayBindingSessionRow row = _rows[index];
                if (string.Equals(GetSerial(row == null ? null : row.Kkt), serial, StringComparison.Ordinal))
                {
                    return row;
                }
            }

            return null;
        }

        private static LmGatewayKkt CopyKkt(LmGatewayKkt source)
        {
            if (source == null)
            {
                return new LmGatewayKkt();
            }

            return new LmGatewayKkt
            {
                InstanceId = Trim(source.InstanceId),
                KktSerial = Trim(source.KktSerial),
                KktInn = Trim(source.KktInn),
                FnSerial = Trim(source.FnSerial),
                Port = Trim(source.Port),
                SoftPort = Trim(source.SoftPort),
                DkktPort = Trim(source.DkktPort),
                ServiceState = Trim(source.ServiceState)
            };
        }

        private static string GetSerial(LmGatewayKkt kkt)
        {
            return kkt == null ? string.Empty : Trim(kkt.KktSerial);
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
