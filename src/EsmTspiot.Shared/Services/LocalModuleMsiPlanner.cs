using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class LocalModuleMsiPlanner
    {
        public static LocalModuleMsiPlan Build(
            IList<LmGatewayKkt> kkts,
            IList<LocalModuleMsiAssignment> saved,
            LocalModuleBaseInventory baseInventory,
            string requestedVolumeRoot,
            IList<TcpListenerSnapshotItem> listeners)
        {
            LocalModuleMsiPlan plan = new LocalModuleMsiPlan();
            if (baseInventory == null ||
                !IsLocalPort(baseInventory.ApiPort) ||
                !IsLocalPort(baseInventory.DatabasePort) ||
                baseInventory.ApiPort == baseInventory.DatabasePort)
            {
                plan.ValidationMessages.Add(
                    "Фактические порты базового ЛМ отсутствуют или недействительны.");
                return plan;
            }

            string baseVolume;
            string cloneVolume;
            try
            {
                baseVolume = LocalModuleInstallRootPolicy.GetVolumeRoot(
                    baseInventory.InstallDirectory);
                cloneVolume = LocalModuleInstallRootPolicy.ResolveVolumeRoot(
                    requestedVolumeRoot,
                    baseInventory.InstallDirectory);
            }
            catch (ArgumentException ex)
            {
                plan.ValidationMessages.Add(ex.Message);
                return plan;
            }

            List<InnGroup> groups = NormalizeGroups(kkts, plan);
            Dictionary<string, LocalModuleMsiAssignment> savedByInn =
                new Dictionary<string, LocalModuleMsiAssignment>(StringComparer.Ordinal);
            HashSet<int> reservedOrdinals = new HashSet<int>();
            ValidateSaved(
                saved,
                baseInventory,
                savedByInn,
                reservedOrdinals,
                plan);
            SeedBaseInventoryAssignment(
                baseInventory,
                baseVolume,
                savedByInn,
                reservedOrdinals,
                plan);
            HashSet<int> assignedPorts = BuildAssignedPortSet(savedByInn, plan);
            IList<TcpListenerSnapshotItem> safeListeners =
                listeners ?? new List<TcpListenerSnapshotItem>();

            for (int index = 0; index < groups.Count; index++)
            {
                InnGroup group = groups[index];
                LocalModuleMsiAssignment assignment;
                if (savedByInn.TryGetValue(group.Inn, out assignment))
                {
                    if (!ListenersBelongToAssignment(assignment, safeListeners))
                    {
                        plan.ValidationMessages.Add(
                            "Сохранённое назначение ЛМ для ИНН " + group.Inn +
                            " конфликтует с чужим владельцем портов: " +
                            DescribeListenerOwners(assignment, safeListeners) + ".");
                        continue;
                    }
                    plan.Assignments.Add(Copy(assignment));
                    continue;
                }

                int ordinal;
                if (!reservedOrdinals.Contains(0))
                {
                    ordinal = 0;
                }
                else
                {
                    ordinal = AllocateCloneOrdinal(reservedOrdinals, safeListeners);
                    if (ordinal == 0)
                    {
                        plan.ValidationMessages.Add(
                            "Для ИНН " + group.Inn +
                            " нет свободного номера ЛМ в диапазоне 1-31.");
                        continue;
                    }
                }

                assignment = ordinal == 0
                    ? new LocalModuleMsiAssignment
                    {
                        Inn = group.Inn,
                        CloneOrdinal = 0,
                        ApiPort = baseInventory.ApiPort,
                        DatabasePort = baseInventory.DatabasePort,
                        InstallVolumeRoot = baseVolume,
                        BaseWasPreExisting = baseInventory.IsInstalled &&
                            !baseInventory.WasInstalledByApplication
                    }
                    : new LocalModuleMsiAssignment
                    {
                        Inn = group.Inn,
                        CloneOrdinal = ordinal,
                        ApiPort = LocalModuleMsiIdentity.ApiPortForClone(ordinal),
                        DatabasePort = LocalModuleMsiIdentity.DatabasePortForClone(ordinal),
                        InstallVolumeRoot = cloneVolume,
                        BaseWasPreExisting = false
                    };
                if (assignedPorts.Contains(assignment.ApiPort) ||
                    assignedPorts.Contains(assignment.DatabasePort))
                {
                    plan.ValidationMessages.Add(
                        "Порты ЛМ для ИНН " + group.Inn +
                        " конфликтуют с другим назначением.");
                    reservedOrdinals.Add(ordinal);
                    continue;
                }
                bool listenerConflict = ordinal == 0 && baseInventory.IsInstalled
                    ? !ListenersBelongToAssignment(assignment, safeListeners)
                    : HasForeignListener(assignment, safeListeners);
                if (listenerConflict)
                {
                    plan.ValidationMessages.Add(
                        "Порты ЛМ для ИНН " + group.Inn + " уже заняты: " +
                        DescribeListenerOwners(assignment, safeListeners) + ".");
                    reservedOrdinals.Add(ordinal);
                    continue;
                }
                assignedPorts.Add(assignment.ApiPort);
                assignedPorts.Add(assignment.DatabasePort);
                reservedOrdinals.Add(ordinal);
                plan.Assignments.Add(assignment);
            }

            SortAssignments(plan.Assignments);
            return plan;
        }

        private static List<InnGroup> NormalizeGroups(
            IList<LmGatewayKkt> kkts,
            LocalModuleMsiPlan plan)
        {
            Dictionary<string, InnGroup> byInn =
                new Dictionary<string, InnGroup>(StringComparer.Ordinal);
            HashSet<string> serials = new HashSet<string>(StringComparer.Ordinal);
            if (kkts != null)
            {
                for (int index = 0; index < kkts.Count; index++)
                {
                    LmGatewayKkt kkt = kkts[index];
                    string inn = Trim(kkt == null ? null : kkt.KktInn);
                    string serial = Trim(kkt == null ? null : kkt.KktSerial);
                    if (!LocalModuleMsiIdentity.IsInn(inn) ||
                        !IsAsciiDigits(serial, 14))
                    {
                        plan.ValidationMessages.Add(
                            "Обнаружена ККТ с недопустимым серийным номером или ИНН.");
                        continue;
                    }
                    if (!serials.Add(serial))
                    {
                        plan.ValidationMessages.Add(
                            "ККТ " + serial + " продублирована в плане ЛМ.");
                        continue;
                    }
                    InnGroup group;
                    if (!byInn.TryGetValue(inn, out group))
                    {
                        group = new InnGroup { Inn = inn, FirstSerial = serial };
                        byInn.Add(inn, group);
                    }
                    else if (string.CompareOrdinal(serial, group.FirstSerial) < 0)
                    {
                        group.FirstSerial = serial;
                    }
                }
            }
            List<InnGroup> result = new List<InnGroup>(byInn.Values);
            result.Sort(delegate(InnGroup left, InnGroup right)
            {
                int serial = string.CompareOrdinal(left.FirstSerial, right.FirstSerial);
                return serial != 0
                    ? serial
                    : string.CompareOrdinal(left.Inn, right.Inn);
            });
            return result;
        }

        private static void ValidateSaved(
            IList<LocalModuleMsiAssignment> saved,
            LocalModuleBaseInventory baseInventory,
            IDictionary<string, LocalModuleMsiAssignment> savedByInn,
            HashSet<int> reservedOrdinals,
            LocalModuleMsiPlan plan)
        {
            if (saved == null) return;
            for (int index = 0; index < saved.Count; index++)
            {
                LocalModuleMsiAssignment item = saved[index];
                if (!IsCanonicalSaved(item, baseInventory))
                {
                    plan.ValidationMessages.Add(
                        "Сохранённое назначение ЛМ имеет недопустимый формат.");
                    continue;
                }
                string inn = Trim(item.Inn);
                if (savedByInn.ContainsKey(inn) ||
                    !reservedOrdinals.Add(item.CloneOrdinal))
                {
                    plan.ValidationMessages.Add(
                        "Сохранённое назначение ЛМ использует повторный ИНН или номер.");
                    continue;
                }
                savedByInn.Add(inn, Copy(item));
            }

        }

        private static void SeedBaseInventoryAssignment(
            LocalModuleBaseInventory baseInventory,
            string baseVolume,
            IDictionary<string, LocalModuleMsiAssignment> savedByInn,
            HashSet<int> reservedOrdinals,
            LocalModuleMsiPlan plan)
        {
            string inn = Trim(baseInventory.AssignedInn);
            if (inn.Length == 0) return;
            if (!LocalModuleMsiIdentity.IsInn(inn))
            {
                plan.ValidationMessages.Add("ИНН фактического базового ЛМ недействителен.");
                return;
            }
            LocalModuleMsiAssignment existing;
            if (savedByInn.TryGetValue(inn, out existing))
            {
                if (existing.CloneOrdinal != 0)
                {
                    plan.ValidationMessages.Add(
                        "Фактический базовый ЛМ закреплён за номером клона.");
                }
                return;
            }
            if (!reservedOrdinals.Add(0))
            {
                plan.ValidationMessages.Add(
                    "Фактический базовый ЛМ конфликтует с сохранённым назначением.");
                return;
            }
            savedByInn.Add(inn, new LocalModuleMsiAssignment
            {
                Inn = inn,
                CloneOrdinal = 0,
                ApiPort = baseInventory.ApiPort,
                DatabasePort = baseInventory.DatabasePort,
                InstallVolumeRoot = baseVolume,
                BaseWasPreExisting = baseInventory.IsInstalled &&
                    !baseInventory.WasInstalledByApplication
            });
        }

        private static HashSet<int> BuildAssignedPortSet(
            IDictionary<string, LocalModuleMsiAssignment> assignments,
            LocalModuleMsiPlan plan)
        {
            HashSet<int> result = new HashSet<int>();
            foreach (LocalModuleMsiAssignment assignment in assignments.Values)
            {
                if (!result.Add(assignment.ApiPort) ||
                    !result.Add(assignment.DatabasePort))
                {
                    plan.ValidationMessages.Add(
                        "Сохранённые назначения ЛМ используют повторный порт.");
                }
            }
            return result;
        }

        private static bool IsCanonicalSaved(
            LocalModuleMsiAssignment item,
            LocalModuleBaseInventory baseInventory)
        {
            if (item == null || !LocalModuleMsiIdentity.IsInn(Trim(item.Inn)) ||
                item.CloneOrdinal < 0 ||
                item.CloneOrdinal > LocalModuleMsiIdentity.MaximumCloneOrdinal ||
                !IsSupportedSavedVolume(item.InstallVolumeRoot, baseInventory))
            {
                return false;
            }
            if (item.CloneOrdinal == 0)
            {
                return item.ApiPort == baseInventory.ApiPort &&
                    item.DatabasePort == baseInventory.DatabasePort &&
                    item.BaseWasPreExisting ==
                        (baseInventory.IsInstalled &&
                         !baseInventory.WasInstalledByApplication) &&
                    string.Equals(
                        Trim(item.InstallVolumeRoot),
                        LocalModuleInstallRootPolicy.GetVolumeRoot(
                            baseInventory.InstallDirectory),
                        StringComparison.OrdinalIgnoreCase);
            }
            return item.ApiPort == LocalModuleMsiIdentity.ApiPortForClone(item.CloneOrdinal) &&
                item.DatabasePort ==
                    LocalModuleMsiIdentity.DatabasePortForClone(item.CloneOrdinal) &&
                !item.BaseWasPreExisting;
        }

        private static bool IsSupportedSavedVolume(
            string volumeRoot,
            LocalModuleBaseInventory baseInventory)
        {
            string candidate = Trim(volumeRoot);
            if (!LocalModuleInstallRootPolicy.IsCanonicalVolumeRoot(candidate))
            {
                return false;
            }
            try
            {
                return string.Equals(
                    LocalModuleInstallRootPolicy.ResolveVolumeRoot(
                        candidate,
                        baseInventory.InstallDirectory),
                    candidate,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static int AllocateCloneOrdinal(
            ICollection<int> reservedOrdinals,
            IList<TcpListenerSnapshotItem> listeners)
        {
            for (int ordinal = 1;
                ordinal <= LocalModuleMsiIdentity.MaximumCloneOrdinal;
                ordinal++)
            {
                if (reservedOrdinals.Contains(ordinal)) continue;
                int api = LocalModuleMsiIdentity.ApiPortForClone(ordinal);
                int database = LocalModuleMsiIdentity.DatabasePortForClone(ordinal);
                bool occupied = false;
                for (int index = 0; index < listeners.Count; index++)
                {
                    if (listeners[index] != null &&
                        (listeners[index].Port == api ||
                         listeners[index].Port == database))
                    {
                        occupied = true;
                        break;
                    }
                }
                if (!occupied) return ordinal;
            }
            return 0;
        }

        private static string DescribeListenerOwners(
            LocalModuleMsiAssignment assignment,
            IList<TcpListenerSnapshotItem> listeners)
        {
            List<string> parts = new List<string>();
            AppendListenerOwner(parts, assignment.ApiPort, listeners);
            AppendListenerOwner(parts, assignment.DatabasePort, listeners);
            if (parts.Count == 0)
            {
                return "порты " + assignment.ApiPort.ToString() +
                    " и " + assignment.DatabasePort.ToString();
            }
            return string.Join("; ", parts.ToArray());
        }

        private static void AppendListenerOwner(
            IList<string> parts,
            int port,
            IList<TcpListenerSnapshotItem> listeners)
        {
            for (int index = 0; index < listeners.Count; index++)
            {
                TcpListenerSnapshotItem listener = listeners[index];
                if (listener == null || listener.Port != port) continue;
                string owner = Trim(listener.OwnerServiceName);
                parts.Add("порт " + port.ToString() +
                    (listener.IsOwnerVerified && owner.Length > 0
                        ? " (служба " + owner + ")"
                        : " (владелец не определён)"));
                return;
            }
        }

        private static bool HasForeignListener(
            LocalModuleMsiAssignment assignment,
            IList<TcpListenerSnapshotItem> listeners)
        {
            for (int index = 0; index < listeners.Count; index++)
            {
                TcpListenerSnapshotItem listener = listeners[index];
                if (listener != null &&
                    (listener.Port == assignment.ApiPort ||
                     listener.Port == assignment.DatabasePort))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ListenersBelongToAssignment(
            LocalModuleMsiAssignment assignment,
            IList<TcpListenerSnapshotItem> listeners)
        {
            for (int index = 0; index < listeners.Count; index++)
            {
                TcpListenerSnapshotItem listener = listeners[index];
                if (listener == null ||
                    (listener.Port != assignment.ApiPort &&
                     listener.Port != assignment.DatabasePort))
                {
                    continue;
                }
                string expected = listener.Port == assignment.ApiPort
                    ? LocalModuleMsiIdentity.ApiServiceName(assignment.CloneOrdinal)
                    : LocalModuleMsiIdentity.DatabaseServiceName(assignment.CloneOrdinal);
                if (!listener.IsOwnerVerified || !string.Equals(
                        Trim(listener.OwnerServiceName),
                        expected,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static LocalModuleMsiAssignment Copy(LocalModuleMsiAssignment source)
        {
            return new LocalModuleMsiAssignment
            {
                Inn = Trim(source.Inn),
                CloneOrdinal = source.CloneOrdinal,
                ApiPort = source.ApiPort,
                DatabasePort = source.DatabasePort,
                InstallVolumeRoot = Trim(source.InstallVolumeRoot),
                BaseWasPreExisting = source.BaseWasPreExisting
            };
        }

        private static void SortAssignments(IList<LocalModuleMsiAssignment> assignments)
        {
            List<LocalModuleMsiAssignment> sorted =
                new List<LocalModuleMsiAssignment>(assignments);
            sorted.Sort(delegate(
                LocalModuleMsiAssignment left,
                LocalModuleMsiAssignment right)
            {
                return left.CloneOrdinal.CompareTo(right.CloneOrdinal);
            });
            assignments.Clear();
            for (int index = 0; index < sorted.Count; index++)
            {
                assignments.Add(sorted[index]);
            }
        }

        private static bool IsLocalPort(int value)
        {
            return value >= 1024 && value <= 65535;
        }

        private static bool IsAsciiDigits(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9') return false;
            }
            return true;
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private sealed class InnGroup
        {
            internal string Inn { get; set; }
            internal string FirstSerial { get; set; }
        }
    }
}
