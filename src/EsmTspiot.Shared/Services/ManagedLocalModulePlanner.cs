using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class ManagedLocalModulePlanner
    {
        private const int MaximumOrdinal = 32;
        private const int ApiPortBase = 4995;
        private const int DatabasePortBase = 4984;
        private const int LocalModulePortStep = 1000;
        private const int EpmdPortBase = 43690;

        public static ManagedLocalModulePlan Build(
            IList<LmGatewayKkt> kkts,
            IList<ManagedKktAssignment> savedKkts,
            IList<ManagedLocalModuleAssignment> savedModules,
            IList<TcpListenerSnapshotItem> listeners)
        {
            ManagedLocalModulePlan result = new ManagedLocalModulePlan();
            List<LmGatewayKkt> current = NormalizeAndSortKkts(kkts, result);
            Dictionary<string, ManagedKktAssignment> savedKktByKey =
                IndexSavedKkts(savedKkts, result);
            Dictionary<string, ManagedLocalModuleAssignment> savedModuleByInn =
                IndexSavedModules(savedModules, result);
            HashSet<int> usedKktOrdinals = ReadReservedKktOrdinals(savedKkts, result);
            HashSet<int> usedModuleOrdinals = ReadReservedModuleOrdinals(savedModules, result);
            List<ManagedKktAssignment> assignments = new List<ManagedKktAssignment>();

            for (int index = 0; index < current.Count; index++)
            {
                LmGatewayKkt kkt = current[index];
                string key = CreateKktKey(kkt.KktSerial, kkt.KktInn);
                ManagedKktAssignment saved;
                if (savedKktByKey.TryGetValue(key, out saved))
                {
                    assignments.Add(Copy(saved));
                    continue;
                }

                if (HasSavedSerialWithDifferentInn(savedKkts, kkt.KktSerial, kkt.KktInn))
                {
                    result.ValidationMessages.Add(
                        "ККТ " + kkt.KktSerial + " ранее была закреплена за другим ИНН.");
                    continue;
                }

                int ordinal = AllocateOrdinal(usedKktOrdinals);
                if (ordinal == 0)
                {
                    result.ValidationMessages.Add("Достигнут предел 32 управляемых ККТ.");
                    continue;
                }
                usedKktOrdinals.Add(ordinal);
                assignments.Add(new ManagedKktAssignment
                {
                    KktSerial = kkt.KktSerial,
                    KktInn = kkt.KktInn,
                    KktOrdinal = ordinal,
                    LocalModuleInstanceId = string.Empty,
                    GrpcPort = LmGatewayDraftDefaults.GrpcPortFirst + ordinal - 1,
                    RestPort = LmGatewayDraftDefaults.RestPortFirst + ordinal - 1
                });
            }

            assignments.Sort(CompareKktAssignments);
            BuildGroups(
                assignments,
                savedModuleByInn,
                usedModuleOrdinals,
                result);
            ValidatePorts(result, listeners);
            return result;
        }

        private static List<LmGatewayKkt> NormalizeAndSortKkts(
            IList<LmGatewayKkt> kkts,
            ManagedLocalModulePlan plan)
        {
            List<LmGatewayKkt> result = new List<LmGatewayKkt>();
            HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
            if (kkts != null)
            {
                for (int index = 0; index < kkts.Count; index++)
                {
                    LmGatewayKkt source = kkts[index];
                    string serial = Trim(source == null ? null : source.KktSerial);
                    string inn = Trim(source == null ? null : source.KktInn);
                    if (!IsAsciiDigits(serial, 14) || !IsInn(inn))
                    {
                        plan.ValidationMessages.Add("Обнаружена ККТ с недопустимым серийным номером или ИНН.");
                        continue;
                    }
                    string key = CreateKktKey(serial, inn);
                    if (!identities.Add(key))
                    {
                        plan.ValidationMessages.Add("ККТ " + serial + " продублирована в плане.");
                        continue;
                    }
                    result.Add(new LmGatewayKkt
                    {
                        InstanceId = Trim(source.InstanceId),
                        KktSerial = serial,
                        KktInn = inn,
                        FnSerial = Trim(source.FnSerial),
                        Port = Trim(source.Port),
                        SoftPort = Trim(source.SoftPort),
                        DkktPort = Trim(source.DkktPort),
                        ServiceState = Trim(source.ServiceState)
                    });
                }
            }
            result.Sort(CompareKkts);
            return result;
        }

        private static Dictionary<string, ManagedKktAssignment> IndexSavedKkts(
            IList<ManagedKktAssignment> saved,
            ManagedLocalModulePlan plan)
        {
            Dictionary<string, ManagedKktAssignment> result =
                new Dictionary<string, ManagedKktAssignment>(StringComparer.Ordinal);
            if (saved == null)
            {
                return result;
            }
            for (int index = 0; index < saved.Count; index++)
            {
                ManagedKktAssignment item = saved[index];
                string serial = Trim(item == null ? null : item.KktSerial);
                string inn = Trim(item == null ? null : item.KktInn);
                if (item == null || !IsAsciiDigits(serial, 14) || !IsInn(inn) ||
                    !IsOrdinal(item.KktOrdinal) || !IsPort(item.GrpcPort) || !IsPort(item.RestPort))
                {
                    plan.ValidationMessages.Add("Сохранённое назначение ККТ имеет недопустимый формат.");
                    continue;
                }
                string key = CreateKktKey(serial, inn);
                if (result.ContainsKey(key))
                {
                    plan.ValidationMessages.Add("Сохранённое назначение ККТ продублировано.");
                    continue;
                }
                result.Add(key, Copy(item));
            }
            return result;
        }

        private static Dictionary<string, ManagedLocalModuleAssignment> IndexSavedModules(
            IList<ManagedLocalModuleAssignment> saved,
            ManagedLocalModulePlan plan)
        {
            Dictionary<string, ManagedLocalModuleAssignment> result =
                new Dictionary<string, ManagedLocalModuleAssignment>(StringComparer.Ordinal);
            if (saved == null)
            {
                return result;
            }
            for (int index = 0; index < saved.Count; index++)
            {
                ManagedLocalModuleAssignment item = saved[index];
                string inn = Trim(item == null ? null : item.Inn);
                if (item == null || !IsInn(inn) || !IsOrdinal(item.ModuleOrdinal) ||
                    !IsPort(item.ApiPort) || !IsPort(item.DatabasePort) || !IsPort(item.EpmdPort))
                {
                    plan.ValidationMessages.Add("Сохранённое назначение ЛМ имеет недопустимый формат.");
                    continue;
                }
                if (result.ContainsKey(inn))
                {
                    plan.ValidationMessages.Add("Сохранённое назначение ЛМ для ИНН продублировано.");
                    continue;
                }
                result.Add(inn, Copy(item));
            }
            return result;
        }

        private static HashSet<int> ReadReservedKktOrdinals(
            IList<ManagedKktAssignment> saved,
            ManagedLocalModulePlan plan)
        {
            HashSet<int> result = new HashSet<int>();
            if (saved != null)
            {
                for (int index = 0; index < saved.Count; index++)
                {
                    ManagedKktAssignment item = saved[index];
                    if (item != null && IsOrdinal(item.KktOrdinal) && !result.Add(item.KktOrdinal))
                    {
                        plan.ValidationMessages.Add("Номер сохранённой ККТ используется более одного раза.");
                    }
                }
            }
            return result;
        }

        private static HashSet<int> ReadReservedModuleOrdinals(
            IList<ManagedLocalModuleAssignment> saved,
            ManagedLocalModulePlan plan)
        {
            HashSet<int> result = new HashSet<int>();
            if (saved != null)
            {
                for (int index = 0; index < saved.Count; index++)
                {
                    ManagedLocalModuleAssignment item = saved[index];
                    if (item != null && IsOrdinal(item.ModuleOrdinal) && !result.Add(item.ModuleOrdinal))
                    {
                        plan.ValidationMessages.Add("Номер сохранённого ЛМ используется более одного раза.");
                    }
                }
            }
            return result;
        }

        private static void BuildGroups(
            IList<ManagedKktAssignment> assignments,
            IDictionary<string, ManagedLocalModuleAssignment> savedModules,
            HashSet<int> usedModuleOrdinals,
            ManagedLocalModulePlan plan)
        {
            Dictionary<string, ManagedLocalModulePlanItem> byInn =
                new Dictionary<string, ManagedLocalModulePlanItem>(StringComparer.Ordinal);
            for (int index = 0; index < assignments.Count; index++)
            {
                ManagedKktAssignment kkt = assignments[index];
                ManagedLocalModulePlanItem item;
                if (!byInn.TryGetValue(kkt.KktInn, out item))
                {
                    ManagedLocalModuleAssignment saved;
                    if (savedModules.TryGetValue(kkt.KktInn, out saved))
                    {
                        item = new ManagedLocalModulePlanItem { Module = Copy(saved) };
                    }
                    else
                    {
                        int ordinal = kkt.KktOrdinal;
                        if (usedModuleOrdinals.Contains(ordinal))
                        {
                            ordinal = AllocateOrdinal(usedModuleOrdinals);
                        }
                        if (ordinal == 0)
                        {
                            plan.ValidationMessages.Add("Достигнут предел 32 управляемых ЛМ.");
                            continue;
                        }
                        usedModuleOrdinals.Add(ordinal);
                        item = new ManagedLocalModulePlanItem
                        {
                            Module = new ManagedLocalModuleAssignment
                            {
                                Inn = kkt.KktInn,
                                ModuleOrdinal = ordinal,
                                InstanceId = string.Empty,
                                ApiPort = ApiPortBase + (LocalModulePortStep * ordinal),
                                DatabasePort = DatabasePortBase + (LocalModulePortStep * ordinal),
                                EpmdPort = EpmdPortBase + ordinal,
                                RuntimeVersion = string.Empty
                            }
                        };
                    }
                    byInn.Add(kkt.KktInn, item);
                    plan.Items.Add(item);
                }
                if (item.Module.InstanceId.Length > 0)
                {
                    kkt.LocalModuleInstanceId = item.Module.InstanceId;
                }
                item.KktAssignments.Add(kkt);
            }
            List<ManagedLocalModulePlanItem> ordered =
                new List<ManagedLocalModulePlanItem>(plan.Items);
            ordered.Sort(ComparePlanItems);
            plan.Items.Clear();
            for (int index = 0; index < ordered.Count; index++)
            {
                plan.Items.Add(ordered[index]);
            }
        }

        private static void ValidatePorts(
            ManagedLocalModulePlan plan,
            IList<TcpListenerSnapshotItem> listeners)
        {
            Dictionary<int, ManagedLocalModulePlanItem> ports =
                new Dictionary<int, ManagedLocalModulePlanItem>();
            for (int itemIndex = 0; itemIndex < plan.Items.Count; itemIndex++)
            {
                ManagedLocalModulePlanItem item = plan.Items[itemIndex];
                AddPort(ports, item, item.Module.ApiPort);
                AddPort(ports, item, item.Module.DatabasePort);
                AddPort(ports, item, item.Module.EpmdPort);
                for (int kktIndex = 0; kktIndex < item.KktAssignments.Count; kktIndex++)
                {
                    ManagedKktAssignment kkt = item.KktAssignments[kktIndex];
                    AddPort(ports, item, kkt.GrpcPort);
                    AddPort(ports, item, kkt.RestPort);
                }
            }
            if (listeners == null)
            {
                return;
            }
            for (int index = 0; index < listeners.Count; index++)
            {
                TcpListenerSnapshotItem listener = listeners[index];
                ManagedLocalModulePlanItem owner;
                if (listener != null && !listener.IsOwnerVerified &&
                    ports.TryGetValue(listener.Port, out owner))
                {
                    AddMessage(owner, "Порт " + listener.Port.ToString() + " уже занят.");
                }
            }
        }

        private static void AddPort(
            IDictionary<int, ManagedLocalModulePlanItem> ports,
            ManagedLocalModulePlanItem item,
            int port)
        {
            ManagedLocalModulePlanItem existing;
            if (!IsPort(port))
            {
                AddMessage(item, "Порт " + port.ToString() + " недопустим.");
            }
            else if (ports.TryGetValue(port, out existing))
            {
                AddMessage(existing, "Порт " + port.ToString() + " повторяется в плане.");
                AddMessage(item, "Порт " + port.ToString() + " повторяется в плане.");
            }
            else
            {
                ports.Add(port, item);
            }
        }

        private static void AddMessage(ManagedLocalModulePlanItem item, string message)
        {
            for (int index = 0; index < item.ValidationMessages.Count; index++)
            {
                if (string.Equals(item.ValidationMessages[index], message, StringComparison.Ordinal))
                {
                    return;
                }
            }
            item.ValidationMessages.Add(message);
        }

        private static int AllocateOrdinal(ICollection<int> used)
        {
            for (int ordinal = 1; ordinal <= MaximumOrdinal; ordinal++)
            {
                if (!used.Contains(ordinal))
                {
                    return ordinal;
                }
            }
            return 0;
        }

        private static bool HasSavedSerialWithDifferentInn(
            IList<ManagedKktAssignment> saved,
            string serial,
            string inn)
        {
            if (saved == null)
            {
                return false;
            }
            for (int index = 0; index < saved.Count; index++)
            {
                ManagedKktAssignment item = saved[index];
                if (item != null && string.Equals(Trim(item.KktSerial), serial, StringComparison.Ordinal) &&
                    !string.Equals(Trim(item.KktInn), inn, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static ManagedKktAssignment Copy(ManagedKktAssignment source)
        {
            return new ManagedKktAssignment
            {
                KktSerial = Trim(source.KktSerial),
                KktInn = Trim(source.KktInn),
                KktOrdinal = source.KktOrdinal,
                LocalModuleInstanceId = Trim(source.LocalModuleInstanceId),
                GrpcPort = source.GrpcPort,
                RestPort = source.RestPort
            };
        }

        private static ManagedLocalModuleAssignment Copy(ManagedLocalModuleAssignment source)
        {
            return new ManagedLocalModuleAssignment
            {
                Inn = Trim(source.Inn),
                ModuleOrdinal = source.ModuleOrdinal,
                InstanceId = Trim(source.InstanceId),
                ApiPort = source.ApiPort,
                DatabasePort = source.DatabasePort,
                EpmdPort = source.EpmdPort,
                RuntimeVersion = Trim(source.RuntimeVersion)
            };
        }

        private static int CompareKkts(LmGatewayKkt left, LmGatewayKkt right)
        {
            int serial = string.CompareOrdinal(left.KktSerial, right.KktSerial);
            return serial != 0 ? serial : string.CompareOrdinal(left.KktInn, right.KktInn);
        }

        private static int CompareKktAssignments(
            ManagedKktAssignment left,
            ManagedKktAssignment right)
        {
            return left.KktOrdinal.CompareTo(right.KktOrdinal);
        }

        private static int ComparePlanItems(
            ManagedLocalModulePlanItem left,
            ManagedLocalModulePlanItem right)
        {
            return left.Module.ModuleOrdinal.CompareTo(right.Module.ModuleOrdinal);
        }

        private static string CreateKktKey(string serial, string inn)
        {
            return serial + "\n" + inn;
        }

        private static bool IsInn(string value)
        {
            return IsAsciiDigits(value, 10) || IsAsciiDigits(value, 12);
        }

        private static bool IsAsciiDigits(string value, int length)
        {
            if (value == null || value.Length != length)
            {
                return false;
            }
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsOrdinal(int value)
        {
            return value >= 1 && value <= MaximumOrdinal;
        }

        private static bool IsPort(int value)
        {
            return value >= 1 && value <= 65535;
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
