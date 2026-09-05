using System.Collections.Generic;
using System.Globalization;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class LocalModuleRemovalMessagePolicy
    {
        /// <summary>
        /// Базу, поставленную самой программой, снятие удаляет вместе с
        /// клонами; предустановленную не трогает: её службы не
        /// останавливаются и не запускаются, им лишь возвращается прежний
        /// режим запуска. Оба факта оператор должен видеть до подтверждения.
        /// </summary>
        public static string BuildRemoveEverythingConfirmation(
            int controllerCount,
            int cloneCount,
            bool preservesBase,
            bool removesOwnedBase)
        {
            return "Будут удалены созданные программой контроллеры (" +
                controllerCount.ToString(CultureInfo.InvariantCulture) +
                ") и клоны ЛМ ЧЗ (" +
                cloneCount.ToString(CultureInfo.InvariantCulture) +
                ")" +
                (removesOwnedBase
                    ? ", а также базовый ЛМ ЧЗ, установленный самой программой"
                    : string.Empty) +
                ". Экземплярные конфигурации ЕСМ вернутся к резервным " +
                "копиям, если после настройки их никто не менял. " +
                (preservesBase
                    ? "Базовый ЛМ, существовавший до автоматической настройки, " +
                        "останется установленным: его службы удаление не " +
                        "останавливает и не запускает, им лишь возвращается " +
                        "прежний режим запуска. "
                    : string.Empty) +
                "Регистрации ККТ и привязки в ЕСМ сохраняются, штатная " +
                "служба esm-lm-controller остаётся установленной: это не " +
                "полный возврат машины к состоянию до настройки. " +
                "Продолжить?";
        }

        /// <summary>
        /// Итог снятия складывается из фактических статусов пунктов: часть
        /// из них может вернуться заблокированной, незавершённой или
        /// помеченной SCM на удаление. Объявлять такое снятие завершённым
        /// нельзя — оператор уйдёт со стенда, считая кассу чистой.
        /// </summary>
        public static string BuildRemoveEverythingSummary(
            IList<LmServiceProvisioningStatus> statuses,
            bool preservedBase)
        {
            int removed = 0;
            int total = 0;
            for (int index = 0;
                statuses != null && index < statuses.Count;
                index++)
            {
                total++;
                if (statuses[index] == LmServiceProvisioningStatus.Succeeded ||
                    statuses[index] == LmServiceProvisioningStatus
                        .RemovedLocalArtifactsBindingRetained)
                    removed++;
            }
            string tail = preservedBase
                ? " Базовый ЛМ, существовавший до автоматической настройки, " +
                    "сохранён."
                : string.Empty;
            if (removed == total)
                return "Удаление завершено: снято объектов — " +
                    total.ToString(CultureInfo.InvariantCulture) + "." + tail;
            return "Удаление завершено не полностью: снято " +
                removed.ToString(CultureInfo.InvariantCulture) + " из " +
                total.ToString(CultureInfo.InvariantCulture) + ", осталось " +
                (total - removed).ToString(CultureInfo.InvariantCulture) +
                " — причины перечислены в журнале выше." + tail;
        }
    }
}
