using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class DirectControllerPlanner
    {
        public static DirectControllerPlan Build(
            IList<LmGatewayKkt> kkts,
            IList<DirectControllerAssignment> savedAssignments,
            IList<DirectControllerServiceInventoryItem> services,
            IList<TcpListenerSnapshotItem> listeners)
        {
            return Build(
                kkts,
                savedAssignments,
                services,
                listeners,
                CreateLegacyTargetMap(kkts, savedAssignments));
        }

        public static DirectControllerPlan Build(
            IList<LmGatewayKkt> kkts,
            IList<DirectControllerAssignment> savedAssignments,
            IList<DirectControllerServiceInventoryItem> services,
            IList<TcpListenerSnapshotItem> listeners,
            IDictionary<string, int> targetLmPortsByInn)
        {
            DirectControllerPlan plan = new DirectControllerPlan();
            List<LmGatewayKkt> current = NormalizeAndSortKkts(kkts, plan);
            IList<DirectControllerServiceInventoryItem> safeServices =
                services ?? new List<DirectControllerServiceInventoryItem>();
            IList<TcpListenerSnapshotItem> safeListeners =
                listeners ?? new List<TcpListenerSnapshotItem>();

            Dictionary<string, int> safeTargets = ValidateTargetMap(
                current,
                targetLmPortsByInn,
                plan);
            if (!plan.IsValid)
            {
                return plan;
            }

            if (current.Count > 0 && !HasVerifiedOfficialService(safeServices))
            {
                plan.ValidationMessages.Add(
                    "Штатная служба esm-lm-controller не подтверждена.");
                return plan;
            }

            Dictionary<string, LmGatewayKkt> currentBySerial =
                IndexCurrentBySerial(current);
            Dictionary<string, DirectControllerAssignment> savedBySerial =
                new Dictionary<string, DirectControllerAssignment>(StringComparer.Ordinal);
            HashSet<int> reservedOrdinals = new HashSet<int>();
            ValidateAndIndexSaved(
                savedAssignments,
                currentBySerial,
                savedBySerial,
                reservedOrdinals,
                safeTargets,
                plan);

            for (int index = 0; index < current.Count; index++)
            {
                LmGatewayKkt kkt = current[index];
                DirectControllerAssignment saved;
                if (!savedBySerial.TryGetValue(kkt.KktSerial, out saved))
                {
                    continue;
                }

                if (!CanUseSavedAssignment(saved, safeServices, safeListeners))
                {
                    plan.ValidationMessages.Add(
                        "Сохранённое назначение ККТ " + kkt.KktSerial +
                        " конфликтует с чужой службой или портом.");
                    continue;
                }
                plan.Assignments.Add(Copy(saved));
                if (HasInstalledService(saved, safeServices))
                    plan.InstalledSerials.Add(kkt.KktSerial);
            }

            for (int index = 0; index < current.Count; index++)
            {
                LmGatewayKkt kkt = current[index];
                if (savedBySerial.ContainsKey(kkt.KktSerial))
                {
                    continue;
                }

                int ordinal = AllocateOrdinal(
                    reservedOrdinals,
                    safeServices,
                    safeListeners);
                if (ordinal == 0)
                {
                    plan.ValidationMessages.Add(
                        "Для ККТ " + kkt.KktSerial +
                        " нет свободного номера контроллера в диапазоне 1-32.");
                    continue;
                }

                DirectControllerAssignment assignment = CreateAssignment(
                    kkt,
                    ordinal,
                    safeTargets[kkt.KktInn]);
                reservedOrdinals.Add(ordinal);
                plan.Assignments.Add(assignment);
            }

            SortAssignments(plan.Assignments);
            if (current.Count > 0 && !ContainsCurrentBase(plan.Assignments))
            {
                plan.ValidationMessages.Add(
                    "Ни одна текущая ККТ не закреплена за штатным контроллером.");
            }
            return plan;
        }

        private static Dictionary<string, int> ValidateTargetMap(
            IList<LmGatewayKkt> current,
            IDictionary<string, int> source,
            DirectControllerPlan plan)
        {
            Dictionary<string, int> result =
                new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<int> ports = new HashSet<int>();
            if (source != null)
            {
                foreach (KeyValuePair<string, int> pair in source)
                {
                    string inn = Trim(pair.Key);
                    if (!IsInn(inn) || pair.Value < 1024 || pair.Value > 65535 ||
                        DirectControllerIdentity.IsControllerPort(pair.Value) ||
                        result.ContainsKey(inn) || !ports.Add(pair.Value))
                    {
                        plan.ValidationMessages.Add(
                            "Карта ИНН и портов ЛМ имеет недопустимый или повторный элемент.");
                        continue;
                    }
                    result.Add(inn, pair.Value);
                }
            }
            for (int index = 0; index < current.Count; index++)
            {
                if (!result.ContainsKey(current[index].KktInn))
                {
                    plan.ValidationMessages.Add(
                        "Для ИНН " + current[index].KktInn +
                        " не назначен целевой порт ЛМ.");
                }
            }
            return result;
        }

        private static IDictionary<string, int> CreateLegacyTargetMap(
            IList<LmGatewayKkt> kkts,
            IList<DirectControllerAssignment> savedAssignments)
        {
            Dictionary<string, int> result =
                new Dictionary<string, int>(StringComparer.Ordinal);
            // Один ЛМ обслуживает ровно один ИНН. Сохранённое назначение может
            // указывать на порт, уже занятый другим ИНН: так бывает, когда
            // прошлый прогон присвоил второй ККТ базовый ЛМ. Такой порт не
            // принимаем — ИНН получит свободный ниже.
            HashSet<int> usedPorts = new HashSet<int>();
            if (savedAssignments != null)
            {
                for (int index = 0; index < savedAssignments.Count; index++)
                {
                    DirectControllerAssignment saved = savedAssignments[index];
                    string inn = Trim(saved == null ? null : saved.KktInn);
                    if (IsInn(inn) && !result.ContainsKey(inn) &&
                        saved.TargetLocalModulePort >= 1024 &&
                        saved.TargetLocalModulePort <= 65535 &&
                        usedPorts.Add(saved.TargetLocalModulePort))
                    {
                        result.Add(inn, saved.TargetLocalModulePort);
                    }
                }
            }
            List<LmGatewayKkt> sorted = new List<LmGatewayKkt>();
            if (kkts != null)
            {
                for (int index = 0; index < kkts.Count; index++)
                {
                    if (kkts[index] != null) sorted.Add(kkts[index]);
                }
            }
            sorted.Sort(delegate(LmGatewayKkt left, LmGatewayKkt right)
            {
                return string.CompareOrdinal(
                    Trim(left.KktSerial),
                    Trim(right.KktSerial));
            });
            for (int index = 0; index < sorted.Count; index++)
            {
                string inn = Trim(sorted[index].KktInn);
                if (!IsInn(inn) || result.ContainsKey(inn)) continue;
                int port = FindFreeLocalModulePort(usedPorts);
                if (port == 0) continue;
                result.Add(inn, port);
            }
            return result;
        }

        // Порт ЛМ выбирается по первому свободному номеру, а не по позиции ККТ
        // в списке: иначе ИНН, чей порт уже занят сохранённым назначением,
        // получил бы тот же номер повторно.
        private static int FindFreeLocalModulePort(HashSet<int> usedPorts)
        {
            for (int ordinal = 1;
                ordinal <= DirectControllerIdentity.MaximumOrdinal;
                ordinal++)
            {
                int candidate = DirectControllerIdentity.FutureLmPortForOrdinal(ordinal);
                if (usedPorts.Add(candidate)) return candidate;
            }
            return 0;
        }

        private static List<LmGatewayKkt> NormalizeAndSortKkts(
            IList<LmGatewayKkt> source,
            DirectControllerPlan plan)
        {
            List<LmGatewayKkt> result = new List<LmGatewayKkt>();
            HashSet<string> serials = new HashSet<string>(StringComparer.Ordinal);
            if (source != null)
            {
                for (int index = 0; index < source.Count; index++)
                {
                    LmGatewayKkt item = source[index];
                    string serial = Trim(item == null ? null : item.KktSerial);
                    string inn = Trim(item == null ? null : item.KktInn);
                    if (!IsAsciiDigits(serial, 14) || !IsInn(inn))
                    {
                        plan.ValidationMessages.Add(
                            "Обнаружена ККТ с недопустимым серийным номером или ИНН.");
                        continue;
                    }
                    if (!serials.Add(serial))
                    {
                        plan.ValidationMessages.Add(
                            "ККТ " + serial + " продублирована в плане.");
                        continue;
                    }
                    result.Add(new LmGatewayKkt
                    {
                        InstanceId = Trim(item.InstanceId),
                        KktSerial = serial,
                        KktInn = inn,
                        FnSerial = Trim(item.FnSerial),
                        Port = Trim(item.Port),
                        SoftPort = Trim(item.SoftPort),
                        DkktPort = Trim(item.DkktPort),
                        ServiceState = Trim(item.ServiceState)
                    });
                }
            }
            result.Sort(delegate(LmGatewayKkt left, LmGatewayKkt right)
            {
                return string.CompareOrdinal(left.KktSerial, right.KktSerial);
            });
            return result;
        }

        private static Dictionary<string, LmGatewayKkt> IndexCurrentBySerial(
            IList<LmGatewayKkt> current)
        {
            Dictionary<string, LmGatewayKkt> result =
                new Dictionary<string, LmGatewayKkt>(StringComparer.Ordinal);
            for (int index = 0; index < current.Count; index++)
            {
                result[current[index].KktSerial] = current[index];
            }
            return result;
        }

        private static void ValidateAndIndexSaved(
            IList<DirectControllerAssignment> saved,
            IDictionary<string, LmGatewayKkt> currentBySerial,
            IDictionary<string, DirectControllerAssignment> savedBySerial,
            HashSet<int> reservedOrdinals,
            IDictionary<string, int> targetLmPortsByInn,
            DirectControllerPlan plan)
        {
            if (saved == null)
            {
                return;
            }
            for (int index = 0; index < saved.Count; index++)
            {
                DirectControllerAssignment item = saved[index];
                if (!IsCanonical(item))
                {
                    plan.ValidationMessages.Add(
                        "Сохранённое назначение контроллера имеет недопустимый формат.");
                    continue;
                }
                if (!reservedOrdinals.Add(item.Ordinal))
                {
                    plan.ValidationMessages.Add(
                        "Номер сохранённого контроллера используется более одного раза.");
                    continue;
                }
                if (savedBySerial.ContainsKey(item.KktSerial))
                {
                    plan.ValidationMessages.Add(
                        "Сохранённое назначение ККТ продублировано.");
                    continue;
                }

                LmGatewayKkt current;
                if (!currentBySerial.TryGetValue(item.KktSerial, out current))
                {
                    // ККТ не участвует в этом прогоне: ЕСМ её не отдал, она
                    // снята или временно недоступна. Номер контроллера уже
                    // зарезервирован за ней и другой ККТ не достанется, но
                    // ошибкой это не является: остальные ККТ настраиваются.
                    continue;
                }
                if (!string.Equals(item.KktInn, current.KktInn, StringComparison.Ordinal))
                {
                    plan.ValidationMessages.Add(
                        "ККТ " + item.KktSerial +
                        " ранее была закреплена за другим ИНН.");
                    continue;
                }
                int expectedLocalModulePort;
                if (!targetLmPortsByInn.TryGetValue(
                        Trim(item.KktInn),
                        out expectedLocalModulePort) ||
                    item.TargetLocalModulePort != expectedLocalModulePort)
                {
                    plan.ValidationMessages.Add(
                        "Целевой порт ЛМ для ККТ " + item.KktSerial +
                        " не совпадает с планом.");
                    continue;
                }
                savedBySerial.Add(item.KktSerial, Copy(item));
            }
        }

        // Форма назначения проверяется без привязки к текущему плану:
        // соответствие целевого порта ЛМ имеет смысл только для ККТ, которая
        // в этом прогоне действительно участвует.
        private static bool IsCanonical(DirectControllerAssignment item)
        {
            if (item == null || !IsAsciiDigits(Trim(item.KktSerial), 14) ||
                !IsInn(Trim(item.KktInn)) || item.Ordinal < 1 ||
                item.Ordinal > DirectControllerIdentity.MaximumOrdinal)
            {
                return false;
            }
            return item.Role == DirectControllerIdentity.RoleForOrdinal(item.Ordinal) &&
                string.Equals(
                    Trim(item.ServiceName),
                    DirectControllerIdentity.ServiceNameForOrdinal(item.Ordinal),
                    StringComparison.Ordinal) &&
                item.GrpcPort == DirectControllerIdentity.GrpcPortForOrdinal(item.Ordinal) &&
                item.RestPort == DirectControllerIdentity.RestPortForOrdinal(item.Ordinal) &&
                item.TargetLocalModulePort >= 1024 &&
                item.TargetLocalModulePort <= 65535 &&
                !DirectControllerIdentity.IsControllerPort(item.TargetLocalModulePort);
        }

        private static bool HasVerifiedOfficialService(
            IList<DirectControllerServiceInventoryItem> services)
        {
            int matches = 0;
            for (int index = 0; index < services.Count; index++)
            {
                DirectControllerServiceInventoryItem item = services[index];
                if (item != null && string.Equals(
                        Trim(item.ServiceName),
                        "esm-lm-controller",
                        StringComparison.Ordinal))
                {
                    matches++;
                    if (!item.IsVerifiedOfficial)
                    {
                        return false;
                    }
                }
            }
            return matches == 1;
        }

        // Сохранённое назначение переживает удаление службы вручную, поэтому
        // «установлен» означает живую службу с ожидаемым именем, а не запись
        // в инвентаре.
        private static bool HasInstalledService(
            DirectControllerAssignment assignment,
            IList<DirectControllerServiceInventoryItem> services)
        {
            for (int index = 0; index < services.Count; index++)
            {
                DirectControllerServiceInventoryItem service = services[index];
                if (service == null || !string.Equals(
                        Trim(service.ServiceName),
                        assignment.ServiceName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                return assignment.Role == DirectControllerRole.OfficialBase
                    ? service.IsVerifiedOfficial
                    : service.IsOwned;
            }
            return false;
        }

        private static bool CanUseSavedAssignment(
            DirectControllerAssignment assignment,
            IList<DirectControllerServiceInventoryItem> services,
            IList<TcpListenerSnapshotItem> listeners)
        {
            int serviceMatches = 0;
            for (int index = 0; index < services.Count; index++)
            {
                DirectControllerServiceInventoryItem service = services[index];
                if (service == null || !string.Equals(
                        Trim(service.ServiceName),
                        assignment.ServiceName,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                serviceMatches++;
                if (assignment.Role == DirectControllerRole.OfficialBase)
                {
                    if (!service.IsVerifiedOfficial)
                    {
                        return false;
                    }
                }
                else if (!service.IsOwned ||
                    (!string.IsNullOrWhiteSpace(service.KktSerial) &&
                     !string.Equals(
                         Trim(service.KktSerial),
                         assignment.KktSerial,
                         StringComparison.Ordinal)))
                {
                    return false;
                }
            }
            if (assignment.Role == DirectControllerRole.OfficialBase && serviceMatches != 1)
            {
                return false;
            }
            if (assignment.Role == DirectControllerRole.DirectClone && serviceMatches > 1)
            {
                return false;
            }

            for (int index = 0; index < listeners.Count; index++)
            {
                TcpListenerSnapshotItem listener = listeners[index];
                if (listener == null || !UsesPort(assignment, listener.Port))
                {
                    continue;
                }
                if (!listener.IsOwnerVerified || !string.Equals(
                        Trim(listener.OwnerServiceName),
                        assignment.ServiceName,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static int AllocateOrdinal(
            ICollection<int> reservedOrdinals,
            IList<DirectControllerServiceInventoryItem> services,
            IList<TcpListenerSnapshotItem> listeners)
        {
            for (int ordinal = 1; ordinal <= DirectControllerIdentity.MaximumOrdinal; ordinal++)
            {
                if (reservedOrdinals.Contains(ordinal) ||
                    HasServiceNameConflict(ordinal, services) ||
                    HasPortConflict(ordinal, listeners))
                {
                    continue;
                }
                return ordinal;
            }
            return 0;
        }

        private static bool HasServiceNameConflict(
            int ordinal,
            IList<DirectControllerServiceInventoryItem> services)
        {
            string expected = DirectControllerIdentity.ServiceNameForOrdinal(ordinal);
            int matches = 0;
            for (int index = 0; index < services.Count; index++)
            {
                DirectControllerServiceInventoryItem item = services[index];
                if (item != null && string.Equals(
                        Trim(item.ServiceName),
                        expected,
                        StringComparison.Ordinal))
                {
                    matches++;
                    if (ordinal == 1 && item.IsVerifiedOfficial && matches == 1)
                    {
                        continue;
                    }
                    return true;
                }
            }
            return ordinal == 1 ? matches != 1 : matches != 0;
        }

        private static bool HasPortConflict(
            int ordinal,
            IList<TcpListenerSnapshotItem> listeners)
        {
            int grpc = DirectControllerIdentity.GrpcPortForOrdinal(ordinal);
            int rest = DirectControllerIdentity.RestPortForOrdinal(ordinal);
            for (int index = 0; index < listeners.Count; index++)
            {
                TcpListenerSnapshotItem listener = listeners[index];
                if (listener != null &&
                    (listener.Port == grpc || listener.Port == rest))
                {
                    return true;
                }
            }
            return false;
        }

        private static DirectControllerAssignment CreateAssignment(
            LmGatewayKkt kkt,
            int ordinal,
            int targetLocalModulePort)
        {
            return new DirectControllerAssignment
            {
                KktSerial = kkt.KktSerial,
                KktInn = kkt.KktInn,
                Ordinal = ordinal,
                Role = DirectControllerIdentity.RoleForOrdinal(ordinal),
                ServiceName = DirectControllerIdentity.ServiceNameForOrdinal(ordinal),
                GrpcPort = DirectControllerIdentity.GrpcPortForOrdinal(ordinal),
                RestPort = DirectControllerIdentity.RestPortForOrdinal(ordinal),
                TargetLocalModulePort = targetLocalModulePort
            };
        }

        private static DirectControllerAssignment Copy(DirectControllerAssignment source)
        {
            return new DirectControllerAssignment
            {
                KktSerial = Trim(source.KktSerial),
                KktInn = Trim(source.KktInn),
                Ordinal = source.Ordinal,
                Role = source.Role,
                ServiceName = Trim(source.ServiceName),
                GrpcPort = source.GrpcPort,
                RestPort = source.RestPort,
                TargetLocalModulePort = source.TargetLocalModulePort
            };
        }

        private static void SortAssignments(IList<DirectControllerAssignment> source)
        {
            List<DirectControllerAssignment> result =
                new List<DirectControllerAssignment>(source);
            result.Sort(delegate(
                DirectControllerAssignment left,
                DirectControllerAssignment right)
            {
                return left.Ordinal.CompareTo(right.Ordinal);
            });
            source.Clear();
            for (int index = 0; index < result.Count; index++)
            {
                source.Add(result[index]);
            }
        }

        private static bool ContainsCurrentBase(
            IList<DirectControllerAssignment> assignments)
        {
            for (int index = 0; index < assignments.Count; index++)
            {
                if (assignments[index] != null && assignments[index].Ordinal == 1)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool UsesPort(DirectControllerAssignment assignment, int port)
        {
            return assignment.GrpcPort == port || assignment.RestPort == port;
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

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
