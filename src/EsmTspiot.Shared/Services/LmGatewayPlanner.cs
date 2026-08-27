using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.Shared.Services
{
    public static class LmGatewayPlanner
    {
        public static LmGatewayPlan Build(
            LmGatewayDiscovery discovery,
            IList<LmGatewayDraft> drafts,
            IList<LmServiceInventoryItem> inventory,
            LmManagedPortPolicy portPolicy,
            IList<TcpListenerSnapshotItem> tcpListeners)
        {
            LmGatewayPlan plan = new LmGatewayPlan();
            if (discovery == null || !discovery.IsSuccessful)
            {
                plan.ErrorMessage = discovery == null || string.IsNullOrWhiteSpace(discovery.ErrorMessage)
                    ? "Нет корректных результатов обнаружения ККТ."
                    : discovery.ErrorMessage;
                return plan;
            }
            if (portPolicy == null)
            {
                plan.ErrorMessage = "Не задана политика управляемых портов контроллера ЛМ.";
                return plan;
            }

            IList<LmServiceInventoryItem> safeInventory = inventory ?? new List<LmServiceInventoryItem>();
            IList<TcpListenerSnapshotItem> safeListeners = tcpListeners ?? new List<TcpListenerSnapshotItem>();
            Dictionary<string, List<LmGatewayDraft>> draftsBySerial = IndexDrafts(drafts);
            Dictionary<string, int> discoveryCounts = CountDiscoverySerials(discovery.Items);
            List<LmGatewayKkt> sortedKkt = CopyAndSortKkt(discovery.Items);
            HashSet<int> plannedPorts = new HashSet<int>();

            for (int index = 0; index < sortedKkt.Count; index++)
            {
                LmGatewayKkt kkt = sortedKkt[index];
                LmGatewayPlanItem item = new LmGatewayPlanItem { Kkt = kkt };
                plan.Items.Add(item);

                string expectedServiceName;
                try
                {
                    expectedServiceName = LmServiceIdentity.CreateName(kkt.KktSerial);
                }
                catch (ArgumentException)
                {
                    item.ServiceValidation.Add("Серийный номер ККТ для службы должен содержать ровно 14 ASCII-цифр.");
                    continue;
                }

                int discoveryCount;
                if (discoveryCounts.TryGetValue(kkt.KktSerial, out discoveryCount) && discoveryCount > 1)
                {
                    item.ServiceValidation.Add("В результатах обнаружения один серийный номер ККТ повторяется.");
                    continue;
                }

                List<LmGatewayDraft> matchingDrafts;
                if (!draftsBySerial.TryGetValue(kkt.KktSerial, out matchingDrafts) || matchingDrafts.Count == 0)
                {
                    item.ServiceValidation.Add("Для ККТ не заданы параметры целевого ЛМ.");
                    continue;
                }
                if (matchingDrafts.Count > 1)
                {
                    item.ServiceValidation.Add("Для одной ККТ задано несколько черновиков контроллера ЛМ.");
                    continue;
                }

                LmGatewayTarget target = ParseAndValidateTarget(matchingDrafts[0], item.ServiceValidation);
                LmServiceInventoryItem existing = FindExistingManagedService(
                    kkt.KktSerial,
                    expectedServiceName,
                    safeInventory,
                    item.ServiceValidation);

                bool hasExplicitPorts;
                LmGatewayPorts explicitPorts = ParseExplicitPorts(
                    matchingDrafts[0],
                    portPolicy,
                    item.ServiceValidation,
                    out hasExplicitPorts);

                if (!item.ServiceValidation.IsValid || target == null)
                {
                    continue;
                }

                LmGatewayPorts selectedPorts = null;
                if (hasExplicitPorts)
                {
                    selectedPorts = explicitPorts;
                }
                else if (existing != null)
                {
                    selectedPorts = ValidateExistingPorts(existing, portPolicy, item.ServiceValidation);
                }
                else
                {
                    selectedPorts = AllocatePorts(
                        portPolicy,
                        target,
                        null,
                        safeInventory,
                        safeListeners,
                        plannedPorts,
                        item.ServiceValidation);
                }

                if (selectedPorts == null || !item.ServiceValidation.IsValid)
                {
                    continue;
                }

                ValidateSelectedPorts(
                    selectedPorts,
                    target,
                    existing,
                    safeInventory,
                    safeListeners,
                    plannedPorts,
                    item.ServiceValidation);
                if (!item.ServiceValidation.IsValid)
                {
                    continue;
                }

                item.Spec = new ManagedLmServiceSpec(kkt, selectedPorts, target);
                item.Action = SelectAction(existing, item.Spec);
                plannedPorts.Add(selectedPorts.GrpcPort);
                plannedPorts.Add(selectedPorts.RestPort);
            }

            return plan;
        }

        private static Dictionary<string, List<LmGatewayDraft>> IndexDrafts(IList<LmGatewayDraft> drafts)
        {
            Dictionary<string, List<LmGatewayDraft>> result =
                new Dictionary<string, List<LmGatewayDraft>>(StringComparer.Ordinal);
            if (drafts == null)
            {
                return result;
            }

            for (int index = 0; index < drafts.Count; index++)
            {
                LmGatewayDraft draft = drafts[index];
                string serial = draft == null ? string.Empty : Trim(draft.KktSerial);
                List<LmGatewayDraft> matches;
                if (!result.TryGetValue(serial, out matches))
                {
                    matches = new List<LmGatewayDraft>();
                    result.Add(serial, matches);
                }

                matches.Add(draft);
            }

            return result;
        }

        private static Dictionary<string, int> CountDiscoverySerials(IList<LmGatewayKkt> items)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (items == null)
            {
                return result;
            }

            for (int index = 0; index < items.Count; index++)
            {
                string serial = items[index] == null ? string.Empty : Trim(items[index].KktSerial);
                int count;
                result.TryGetValue(serial, out count);
                result[serial] = count + 1;
            }

            return result;
        }

        private static List<LmGatewayKkt> CopyAndSortKkt(IList<LmGatewayKkt> source)
        {
            List<LmGatewayKkt> result = new List<LmGatewayKkt>();
            if (source != null)
            {
                for (int index = 0; index < source.Count; index++)
                {
                    result.Add(CopyKkt(source[index]));
                }
            }

            result.Sort(delegate(LmGatewayKkt left, LmGatewayKkt right)
            {
                return string.Compare(left.KktSerial, right.KktSerial, StringComparison.Ordinal);
            });
            return result;
        }

        private static LmGatewayTarget ParseAndValidateTarget(
            LmGatewayDraft draft,
            ValidationResult validation)
        {
            int port;
            if (draft == null || !int.TryParse(Trim(draft.TargetPort), out port))
            {
                validation.Add("Порт целевого ЛМ должен быть целым числом в диапазоне 1-65535.");
                return null;
            }

            LmGatewayTarget rawTarget = new LmGatewayTarget(draft.TargetAddress, port);
            ValidationResult targetValidation = LmGatewayInputValidator.ValidateTarget(rawTarget);
            CopyValidation(targetValidation, validation);
            if (!targetValidation.IsValid)
            {
                return null;
            }

            string normalizedAddress;
            bool isLoopback;
            LmGatewayInputValidator.TryNormalizeTargetAddress(
                rawTarget.Address,
                out normalizedAddress,
                out isLoopback);
            return new LmGatewayTarget(normalizedAddress, port);
        }

        private static LmGatewayPorts ParseExplicitPorts(
            LmGatewayDraft draft,
            LmManagedPortPolicy policy,
            ValidationResult validation,
            out bool hasExplicitPorts)
        {
            string grpcText = draft == null ? string.Empty : Trim(draft.GrpcPort);
            string restText = draft == null ? string.Empty : Trim(draft.RestPort);
            hasExplicitPorts = grpcText.Length > 0 || restText.Length > 0;
            if (!hasExplicitPorts)
            {
                return null;
            }
            if (grpcText.Length == 0 || restText.Length == 0)
            {
                validation.Add("Ручное назначение требует оба локальных порта: gRPC и REST.");
                return null;
            }

            int grpcPort;
            int restPort;
            if (!int.TryParse(grpcText, out grpcPort) || grpcPort < 1 || grpcPort > 65535)
            {
                validation.Add("gRPC-порт должен быть целым числом в диапазоне 1-65535.");
            }
            else if (!policy.GrpcPorts.Contains(grpcPort))
            {
                validation.Add("Выбранный gRPC-порт не входит в разрешенный диапазон.");
            }

            if (!int.TryParse(restText, out restPort) || restPort < 1 || restPort > 65535)
            {
                validation.Add("REST-порт должен быть целым числом в диапазоне 1-65535.");
            }
            else if (!policy.RestPorts.Contains(restPort))
            {
                validation.Add("Выбранный REST-порт не входит в разрешенный диапазон.");
            }

            if (grpcPort == restPort && grpcPort > 0)
            {
                validation.Add("Локальные gRPC- и REST-порты должны различаться.");
            }

            return validation.IsValid ? new LmGatewayPorts(grpcPort, restPort) : null;
        }

        private static LmServiceInventoryItem FindExistingManagedService(
            string kktSerial,
            string expectedServiceName,
            IList<LmServiceInventoryItem> inventory,
            ValidationResult validation)
        {
            LmServiceInventoryItem match = null;
            for (int index = 0; index < inventory.Count; index++)
            {
                LmServiceInventoryItem candidate = inventory[index];
                if (candidate == null)
                {
                    continue;
                }

                bool sameSerial = string.Equals(Trim(candidate.KktSerial), kktSerial, StringComparison.Ordinal);
                bool sameName = string.Equals(Trim(candidate.ServiceName), expectedServiceName, StringComparison.Ordinal);
                if (candidate.Role == LmServiceRole.VerifiedOfficial)
                {
                    if (sameName)
                    {
                        validation.Add("Штатная служба имеет имя, зарезервированное управляемым экземпляром.");
                    }
                    continue;
                }

                if (candidate.Role != LmServiceRole.Managed)
                {
                    if (sameName)
                    {
                        validation.Add("Имя управляемой службы уже занято чужим или нераспознанным объектом.");
                    }
                    continue;
                }

                if (!sameSerial && !sameName)
                {
                    continue;
                }
                if (!sameSerial || !sameName)
                {
                    validation.Add("Идентичность управляемой службы не совпадает с серийным номером ККТ.");
                    continue;
                }
                if (match != null)
                {
                    validation.Add("Для одной ККТ обнаружено несколько управляемых служб.");
                    continue;
                }

                match = candidate;
            }

            return match;
        }

        private static LmGatewayPorts ValidateExistingPorts(
            LmServiceInventoryItem existing,
            LmManagedPortPolicy policy,
            ValidationResult validation)
        {
            if (existing.Ports == null)
            {
                validation.Add("В манифесте управляемой службы нет локальных портов.");
                return null;
            }
            if (!policy.GrpcPorts.Contains(existing.Ports.GrpcPort) ||
                !policy.RestPorts.Contains(existing.Ports.RestPort) ||
                existing.Ports.GrpcPort == existing.Ports.RestPort)
            {
                validation.Add("Существующие порты управляемой службы не входят в разрешенные диапазоны.");
                return null;
            }

            return new LmGatewayPorts(existing.Ports.GrpcPort, existing.Ports.RestPort);
        }

        private static LmGatewayPorts AllocatePorts(
            LmManagedPortPolicy policy,
            LmGatewayTarget target,
            LmServiceInventoryItem existing,
            IList<LmServiceInventoryItem> inventory,
            IList<TcpListenerSnapshotItem> listeners,
            HashSet<int> plannedPorts,
            ValidationResult validation)
        {
            int grpcPort = FindAvailablePort(
                policy.GrpcPorts,
                target,
                existing,
                inventory,
                listeners,
                plannedPorts,
                -1);
            if (grpcPort < 0)
            {
                validation.Add("В разрешенном gRPC-диапазоне нет свободного порта.");
                return null;
            }

            int restPort = FindAvailablePort(
                policy.RestPorts,
                target,
                existing,
                inventory,
                listeners,
                plannedPorts,
                grpcPort);
            if (restPort < 0)
            {
                validation.Add("В разрешенном REST-диапазоне нет свободного порта.");
                return null;
            }

            return new LmGatewayPorts(grpcPort, restPort);
        }

        private static int FindAvailablePort(
            TcpPortRange range,
            LmGatewayTarget target,
            LmServiceInventoryItem existing,
            IList<LmServiceInventoryItem> inventory,
            IList<TcpListenerSnapshotItem> listeners,
            HashSet<int> plannedPorts,
            int excludedPort)
        {
            for (int port = range.First; port <= range.Last; port++)
            {
                if (port == excludedPort || ConflictsWithLoopbackTarget(port, target))
                {
                    continue;
                }
                if (IsPortClaimed(port, existing, inventory, listeners, plannedPorts))
                {
                    continue;
                }

                return port;
            }

            return -1;
        }

        private static void ValidateSelectedPorts(
            LmGatewayPorts ports,
            LmGatewayTarget target,
            LmServiceInventoryItem existing,
            IList<LmServiceInventoryItem> inventory,
            IList<TcpListenerSnapshotItem> listeners,
            HashSet<int> plannedPorts,
            ValidationResult validation)
        {
            if (ports.GrpcPort == ports.RestPort)
            {
                validation.Add("Локальные gRPC- и REST-порты должны различаться.");
            }

            if (ConflictsWithLoopbackTarget(ports.GrpcPort, target) ||
                ConflictsWithLoopbackTarget(ports.RestPort, target))
            {
                validation.Add("Локальный порт контроллера конфликтует с loopback-endpoint целевого ЛМ.");
            }

            ValidatePortClaim(ports.GrpcPort, existing, inventory, listeners, plannedPorts, validation);
            ValidatePortClaim(ports.RestPort, existing, inventory, listeners, plannedPorts, validation);
        }

        private static void ValidatePortClaim(
            int port,
            LmServiceInventoryItem existing,
            IList<LmServiceInventoryItem> inventory,
            IList<TcpListenerSnapshotItem> listeners,
            HashSet<int> plannedPorts,
            ValidationResult validation)
        {
            if (IsPortClaimed(port, existing, inventory, listeners, plannedPorts))
            {
                validation.Add("Локальный TCP-порт " + port.ToString() +
                    " уже занят другой службой, слушателем или строкой плана.");
            }
        }

        private static bool IsPortClaimed(
            int port,
            LmServiceInventoryItem existing,
            IList<LmServiceInventoryItem> inventory,
            IList<TcpListenerSnapshotItem> listeners,
            HashSet<int> plannedPorts)
        {
            if (plannedPorts.Contains(port))
            {
                return true;
            }

            for (int index = 0; index < inventory.Count; index++)
            {
                LmServiceInventoryItem candidate = inventory[index];
                if (candidate == null || candidate.Ports == null)
                {
                    continue;
                }
                if (object.ReferenceEquals(candidate, existing) &&
                    (candidate.Ports.GrpcPort == port || candidate.Ports.RestPort == port))
                {
                    continue;
                }
                if (candidate.Ports.GrpcPort == port || candidate.Ports.RestPort == port)
                {
                    return true;
                }
            }

            string expectedOwner = existing == null ? string.Empty : Trim(existing.ServiceName);
            for (int index = 0; index < listeners.Count; index++)
            {
                TcpListenerSnapshotItem listener = listeners[index];
                if (listener == null || listener.Port != port)
                {
                    continue;
                }
                bool isExistingAssignedPort = existing != null && existing.Ports != null &&
                    (existing.Ports.GrpcPort == port || existing.Ports.RestPort == port);
                if (isExistingAssignedPort && listener.IsOwnerVerified &&
                    string.Equals(Trim(listener.OwnerServiceName), expectedOwner, StringComparison.Ordinal))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool ConflictsWithLoopbackTarget(int localPort, LmGatewayTarget target)
        {
            if (target == null || localPort != target.Port)
            {
                return false;
            }

            string normalized;
            bool isLoopback;
            return LmGatewayInputValidator.TryNormalizeTargetAddress(
                target.Address,
                out normalized,
                out isLoopback) && isLoopback;
        }

        private static LmGatewayPlanAction SelectAction(
            LmServiceInventoryItem existing,
            ManagedLmServiceSpec spec)
        {
            if (existing == null)
            {
                return LmGatewayPlanAction.CreateManagedService;
            }

            bool portsMatch = existing.Ports != null &&
                existing.Ports.GrpcPort == spec.Ports.GrpcPort &&
                existing.Ports.RestPort == spec.Ports.RestPort;
            bool targetMatches = TargetsMatch(existing.Target, spec.Target);
            if (!portsMatch || !targetMatches)
            {
                return LmGatewayPlanAction.UpdateManagedService;
            }
            if (!existing.IsRunning)
            {
                return LmGatewayPlanAction.StartManagedService;
            }

            return LmGatewayPlanAction.NoChange;
        }

        private static bool TargetsMatch(LmGatewayTarget left, LmGatewayTarget right)
        {
            if (left == null || right == null || left.Port != right.Port)
            {
                return false;
            }

            string leftAddress;
            string rightAddress;
            bool leftLoopback;
            bool rightLoopback;
            return LmGatewayInputValidator.TryNormalizeTargetAddress(
                    left.Address,
                    out leftAddress,
                    out leftLoopback) &&
                LmGatewayInputValidator.TryNormalizeTargetAddress(
                    right.Address,
                    out rightAddress,
                    out rightLoopback) &&
                string.Equals(leftAddress, rightAddress, StringComparison.Ordinal);
        }

        private static void CopyValidation(ValidationResult source, ValidationResult destination)
        {
            for (int index = 0; index < source.Messages.Count; index++)
            {
                destination.Add(source.Messages[index]);
            }
            for (int index = 0; index < source.Warnings.Count; index++)
            {
                destination.AddWarning(source.Warnings[index]);
            }
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

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
