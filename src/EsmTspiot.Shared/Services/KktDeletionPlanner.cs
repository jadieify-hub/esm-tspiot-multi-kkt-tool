using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class KktDeletionPlanner
    {
        public static KktDeletionPlan Build(IList<KktInstanceInfo> instances)
        {
            KktDeletionPlan plan = new KktDeletionPlan();
            IList<KktInstanceInfo> source = instances ?? new List<KktInstanceInfo>();
            int primaryIndex = -1;
            int primaryMarkerCount = 0;

            for (int i = 0; i < source.Count; i++)
            {
                KktInstanceInfo instance = source[i] ?? new KktInstanceInfo();
                if (HasPrimaryMarker(instance))
                {
                    primaryMarkerCount++;
                    primaryIndex = i;
                }
            }

            plan.HasReliablePrimary = primaryMarkerCount == 1;
            if (!plan.HasReliablePrimary)
            {
                plan.BlockingReason = primaryMarkerCount == 0
                    ? "Не удалось надежно определить первую ККТ: нет экземпляра с port 50401 или softPort 0."
                    : "Не удалось надежно определить первую ККТ: признаки первой ККТ найдены у нескольких экземпляров.";
            }

            for (int i = 0; i < source.Count; i++)
            {
                KktInstanceInfo instance = source[i] ?? new KktInstanceInfo();
                KktDeletionCandidate candidate = new KktDeletionCandidate
                {
                    Instance = instance,
                    IsPrimary = plan.HasReliablePrimary && i == primaryIndex,
                    CanDelete = false,
                    ProtectionReason = string.Empty
                };

                if (!plan.HasReliablePrimary)
                {
                    candidate.ProtectionReason = plan.BlockingReason;
                }
                else if (candidate.IsPrimary)
                {
                    candidate.ProtectionReason = "Первая ККТ защищена от удаления.";
                }
                else if (source.Count < 2)
                {
                    candidate.ProtectionReason = "Нельзя удалить единственный экземпляр ККТ.";
                }
                else if (!KktDeletionConfirmation.IsValidKktId(instance.Id))
                {
                    candidate.ProtectionReason = "Серийный номер экземпляра некорректен; удаление заблокировано.";
                }
                else
                {
                    candidate.CanDelete = true;
                }

                plan.Candidates.Add(candidate);
            }

            return plan;
        }

        private static bool HasPrimaryMarker(KktInstanceInfo instance)
        {
            return string.Equals((instance.Port ?? string.Empty).Trim(), "50401", StringComparison.Ordinal) ||
                string.Equals((instance.SoftPort ?? string.Empty).Trim(), "0", StringComparison.Ordinal);
        }
    }
}
