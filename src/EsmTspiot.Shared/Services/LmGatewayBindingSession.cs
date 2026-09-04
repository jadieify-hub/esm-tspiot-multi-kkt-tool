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

            List<LmGatewayKkt> ordered = new List<LmGatewayKkt>();
            for (int index = 0; index < discovery.Items.Count; index++)
            {
                ordered.Add(CopyKkt(discovery.Items[index]));
            }
            ordered.Sort(delegate(LmGatewayKkt left, LmGatewayKkt right)
            {
                return string.Compare(GetSerial(left), GetSerial(right), StringComparison.Ordinal);
            });

            HashSet<string> added = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < ordered.Count; index++)
            {
                LmGatewayKkt kkt = ordered[index];
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
                        IsSelected = true,
                        ControllerAddress = LmGatewayDraftDefaults.ControllerAddress,
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

        public void SelectAll()
        {
            for (int index = 0; index < _rows.Count; index++)
            {
                if (_rows[index] != null && _rows[index].Kkt != null)
                {
                    _rows[index].IsSelected = true;
                }
            }
        }

        public LmGatewayBindingPlan BuildSelectedPlan()
        {
            return BuildPlan(delegate(LmGatewayBindingSessionRow row)
            {
                return row != null && row.IsSelected;
            });
        }

        public LmGatewayBindingPlan BuildPlanFor(string kktSerial)
        {
            string expected = Trim(kktSerial);
            return BuildPlan(delegate(LmGatewayBindingSessionRow row)
            {
                return row != null && row.Kkt != null && string.Equals(
                    GetSerial(row.Kkt),
                    expected,
                    StringComparison.Ordinal);
            });
        }

        private LmGatewayBindingPlan BuildPlan(
            Func<LmGatewayBindingSessionRow, bool> include)
        {
            LmGatewayDiscovery discovery = new LmGatewayDiscovery();
            IList<LmGatewayBindingInput> inputs = new List<LmGatewayBindingInput>();
            for (int index = 0; index < _rows.Count; index++)
            {
                LmGatewayBindingSessionRow row = _rows[index];
                if (row == null || row.Kkt == null || include == null || !include(row))
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
            ApplyOutcome(outcome, false);
        }

        public void ApplyOutcomeFallback(LmGatewayBindingOutcome outcome)
        {
            ApplyOutcome(outcome, true);
        }

        private void ApplyOutcome(
            LmGatewayBindingOutcome outcome,
            bool preserveExistingStatus)
        {
            if (outcome == null)
            {
                return;
            }

            for (int index = 0; index < outcome.Results.Count; index++)
            {
                LmGatewayBindingResult result = outcome.Results[index];
                LmGatewayBindingSessionRow row = FindRow(result == null ? null : result.KktSerial);
                if (row == null || result == null ||
                    (preserveExistingStatus && row.LastBindingStatus.HasValue))
                {
                    continue;
                }

                row.LastBindingStatus = result.Status;
                if (!preserveExistingStatus ||
                    string.IsNullOrWhiteSpace(row.LastMessage))
                {
                    row.LastMessage = SensitiveDataMasker.Mask(result.Details);
                }
            }
        }

        public void ApplyReadback(IList<LmGatewayReadbackObservation> observations)
        {
            if (observations == null)
            {
                return;
            }

            for (int index = 0; index < observations.Count; index++)
            {
                LmGatewayReadbackObservation observation = observations[index];
                LmGatewayBindingSessionRow row = FindRow(
                    observation == null ? null : observation.KktSerial);
                if (row == null || row.Kkt == null || observation == null)
                {
                    continue;
                }

                row.LastMessage = SensitiveDataMasker.Mask(observation.Details);
                row.ObservedLmAddress = string.Empty;
                row.ObservedLmPort = string.Empty;
                if (!string.Equals(
                        Trim(row.Kkt.KktInn),
                        Trim(observation.KktInn),
                        StringComparison.Ordinal) ||
                    (observation.IsAvailable && !observation.IdentityMatches))
                {
                    row.LastBindingStatus =
                        LmGatewayBindingStatus.RequiresAttention;
                    continue;
                }
                if (!observation.IsAvailable)
                {
                    row.LastBindingStatus = null;
                    continue;
                }
                if (!observation.HasLmConfiguration)
                {
                    row.LastBindingStatus =
                        LmGatewayBindingStatus.RequiresAttention;
                    continue;
                }
                row.ObservedLmAddress = Trim(observation.LmAddress);
                row.ObservedLmPort = Trim(observation.LmPort);
                if (observation.IdentityMatches && observation.HasLmConfiguration &&
                    LmContourReadbackPolicy.IsLocalModulePending(observation.LmStatus))
                {
                    row.LastBindingStatus = LmGatewayBindingStatus.BindingObserved;
                    continue;
                }
                if (observation.IsVerified)
                {
                    row.LastBindingStatus = LmGatewayBindingStatus.BindingVerified;
                    continue;
                }

                row.LastBindingStatus = LmGatewayBindingStatus.RequiresAttention;
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
