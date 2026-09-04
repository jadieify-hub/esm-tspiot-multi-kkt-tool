using System.Globalization;

namespace EsmTspiot.Shared.Services
{
    public static class LocalModuleRemovalMessagePolicy
    {
        public static string BuildRemoveEverythingConfirmation(
            int controllerCount,
            int cloneCount,
            bool preservesBase)
        {
            return "Будут удалены созданные программой контроллеры (" +
                controllerCount.ToString(CultureInfo.InvariantCulture) +
                ") и клоны ЛМ ЧЗ (" +
                cloneCount.ToString(CultureInfo.InvariantCulture) +
                "). Исходные конфигурации ЕСМ будут восстановлены. " +
                (preservesBase
                    ? "Базовый ЛМ, существовавший до автоматической настройки, " +
                        "останется установленным. "
                    : string.Empty) +
                "Штатная служба esm-lm-controller останется установленной. " +
                "Продолжить?";
        }
    }
}
