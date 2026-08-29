using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class ManagedLocalModulePortPreflight
    {
        public static IList<int> FindConflicts(
            ManagedLocalModulePlan plan,
            IList<ManagedKktAssignment> savedKkts,
            IList<ManagedLocalModuleAssignment> savedModules,
            IEnumerable<int> occupiedPorts)
        {
            HashSet<int> planned = ReadPlannedPorts(plan);
            HashSet<int> existing = ReadExistingPlanPorts(
                plan,
                savedKkts,
                savedModules);
            HashSet<int> conflicts = new HashSet<int>();
            if (occupiedPorts != null)
            {
                foreach (int port in occupiedPorts)
                {
                    if (planned.Contains(port) && !existing.Contains(port))
                    {
                        conflicts.Add(port);
                    }
                }
            }
            List<int> result = new List<int>(conflicts);
            result.Sort();
            return result;
        }

        private static HashSet<int> ReadPlannedPorts(
            ManagedLocalModulePlan plan)
        {
            HashSet<int> result = new HashSet<int>();
            if (plan == null) return result;
            for (int itemIndex = 0; itemIndex < plan.Items.Count; itemIndex++)
            {
                ManagedLocalModulePlanItem item = plan.Items[itemIndex];
                if (item == null || item.Module == null) continue;
                result.Add(item.Module.ApiPort);
                result.Add(item.Module.DatabasePort);
                result.Add(item.Module.EpmdPort);
                for (int kktIndex = 0;
                    kktIndex < item.KktAssignments.Count;
                    kktIndex++)
                {
                    ManagedKktAssignment kkt = item.KktAssignments[kktIndex];
                    if (kkt == null) continue;
                    result.Add(kkt.GrpcPort);
                    result.Add(kkt.RestPort);
                }
            }
            return result;
        }

        private static HashSet<int> ReadExistingPlanPorts(
            ManagedLocalModulePlan plan,
            IList<ManagedKktAssignment> savedKkts,
            IList<ManagedLocalModuleAssignment> savedModules)
        {
            HashSet<int> result = new HashSet<int>();
            if (plan == null) return result;
            for (int itemIndex = 0; itemIndex < plan.Items.Count; itemIndex++)
            {
                ManagedLocalModulePlanItem item = plan.Items[itemIndex];
                if (item == null || item.Module == null) continue;
                ManagedLocalModuleAssignment savedModule = FindModule(
                    savedModules,
                    item.Module.Inn);
                if (savedModule != null)
                {
                    AddWhenEqual(result, item.Module.ApiPort, savedModule.ApiPort);
                    AddWhenEqual(
                        result,
                        item.Module.DatabasePort,
                        savedModule.DatabasePort);
                    AddWhenEqual(result, item.Module.EpmdPort, savedModule.EpmdPort);
                }
                for (int kktIndex = 0;
                    kktIndex < item.KktAssignments.Count;
                    kktIndex++)
                {
                    ManagedKktAssignment current = item.KktAssignments[kktIndex];
                    ManagedKktAssignment saved = FindKkt(
                        savedKkts,
                        current == null ? null : current.KktSerial,
                        current == null ? null : current.KktInn);
                    if (current == null || saved == null) continue;
                    AddWhenEqual(result, current.GrpcPort, saved.GrpcPort);
                    AddWhenEqual(result, current.RestPort, saved.RestPort);
                }
            }
            return result;
        }

        private static ManagedLocalModuleAssignment FindModule(
            IList<ManagedLocalModuleAssignment> items,
            string inn)
        {
            if (items == null) return null;
            for (int index = 0; index < items.Count; index++)
            {
                ManagedLocalModuleAssignment item = items[index];
                if (item != null && string.Equals(
                    Trim(item.Inn),
                    Trim(inn),
                    StringComparison.Ordinal))
                {
                    return item;
                }
            }
            return null;
        }

        private static ManagedKktAssignment FindKkt(
            IList<ManagedKktAssignment> items,
            string serial,
            string inn)
        {
            if (items == null) return null;
            for (int index = 0; index < items.Count; index++)
            {
                ManagedKktAssignment item = items[index];
                if (item != null &&
                    string.Equals(
                        Trim(item.KktSerial),
                        Trim(serial),
                        StringComparison.Ordinal) &&
                    string.Equals(
                        Trim(item.KktInn),
                        Trim(inn),
                        StringComparison.Ordinal))
                {
                    return item;
                }
            }
            return null;
        }

        private static void AddWhenEqual(
            ISet<int> result,
            int current,
            int saved)
        {
            if (current == saved)
            {
                result.Add(current);
            }
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
