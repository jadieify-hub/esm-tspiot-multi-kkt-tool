using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class ManagedLocalModuleRequestBuilder
    {
        public static IList<ManagedLocalModuleProvisioningItemRequest> Build(
            ManagedLocalModulePlan plan,
            string runtimeVersion)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }
            if (!plan.IsValid)
            {
                throw new InvalidOperationException(
                    "Нельзя сформировать запрос из некорректного плана ЛМ ЧЗ.");
            }
            string version = (runtimeVersion ?? string.Empty).Trim();
            if (version.Length == 0)
            {
                throw new ArgumentException("Версия runtime ЛМ ЧЗ не задана.", "runtimeVersion");
            }

            List<ManagedLocalModuleProvisioningItemRequest> result =
                new List<ManagedLocalModuleProvisioningItemRequest>();
            List<ManagedLocalModulePlanItem> groups =
                new List<ManagedLocalModulePlanItem>(plan.Items);
            groups.Sort(delegate(
                ManagedLocalModulePlanItem left,
                ManagedLocalModulePlanItem right)
            {
                return left.Module.ModuleOrdinal.CompareTo(
                    right.Module.ModuleOrdinal);
            });

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                ManagedLocalModulePlanItem group = groups[groupIndex];
                List<ManagedKktAssignment> kkts =
                    new List<ManagedKktAssignment>(group.KktAssignments);
                kkts.Sort(delegate(
                    ManagedKktAssignment left,
                    ManagedKktAssignment right)
                {
                    return left.KktOrdinal.CompareTo(right.KktOrdinal);
                });
                for (int kktIndex = 0; kktIndex < kkts.Count; kktIndex++)
                {
                    ManagedKktAssignment kkt = kkts[kktIndex];
                    result.Add(new ManagedLocalModuleProvisioningItemRequest
                    {
                        KktSerial = kkt.KktSerial,
                        Inn = kkt.KktInn,
                        KktOrdinal = kkt.KktOrdinal,
                        LocalModuleOrdinal = group.Module.ModuleOrdinal,
                        ApiPort = group.Module.ApiPort,
                        DatabasePort = group.Module.DatabasePort,
                        EpmdPort = group.Module.EpmdPort,
                        ControllerGrpcPort = kkt.GrpcPort,
                        ControllerRestPort = kkt.RestPort,
                        RuntimeVersion = version
                    });
                }
            }
            return result;
        }
    }
}
