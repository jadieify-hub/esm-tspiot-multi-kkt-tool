using System;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public enum LmContourReadbackState
    {
        Unavailable = 0,
        Attention = 1,
        LocalModuleNotInitialized = 2,
        Verified = 3
    }

    // Classifies one ESM /api/v2/info read-back after the contour has been
    // configured. The binding is the contour's responsibility; LM business
    // initialization is not, so an LM error reported by ESM before that
    // initialization is an operator warning line, never a contour failure.
    public static class LmContourReadbackPolicy
    {
        public const string ControllerAddress = "127.0.0.1";

        public static LmGatewayTarget ExpectedTarget(
            DirectControllerAssignment assignment)
        {
            if (assignment == null) return null;
            // Целевой ЛМ по плану. Он нужен для строки оператору: сверять
            // с ним ответ ЕСМ нельзя — в lm.ip/lm.port штатный ЕСМ отдаёт
            // свой модуль по умолчанию (в поле 5995 на обеих кассах), а
            // связку каждой ККТ ведёт через её контроллер.
            return new LmGatewayTarget(
                ControllerAddress,
                assignment.TargetLocalModulePort);
        }

        public static LmContourReadbackState Classify(
            LmGatewayReadbackObservation observation)
        {
            if (observation == null || !observation.IsAvailable)
                return LmContourReadbackState.Unavailable;
            // Состояние «ЛМ ещё не инициализирован» проверяется раньше сверки
            // адреса: до инициализации ЕСМ отдаёт в lm.ip/lm.port значения по
            // умолчанию, и несовпадение порта в этот момент ничего не значит.
            if (observation.IdentityMatches && observation.HasLmConfiguration &&
                IsLocalModulePending(observation.LmStatus))
                return LmContourReadbackState.LocalModuleNotInitialized;
            if (!observation.IsVerified)
                return LmContourReadbackState.Attention;
            return LmContourReadbackState.Verified;
        }

        public static bool IsAcceptable(LmContourReadbackState state)
        {
            return state == LmContourReadbackState.Verified ||
                state == LmContourReadbackState.LocalModuleNotInitialized;
        }

        public static string Describe(LmGatewayReadbackObservation observation)
        {
            string serial = observation == null
                ? string.Empty
                : (observation.KktSerial ?? string.Empty).Trim();
            string prefix = "ККТ " + serial + ": ";
            LmContourReadbackState state = Classify(observation);
            if (state == LmContourReadbackState.Verified)
            {
                string status = (observation.LmStatus ?? string.Empty).Trim();
                bool endpointDiffers = observation.EndpointMatches.HasValue &&
                    !observation.EndpointMatches.Value;
                return prefix + "привязка подтверждена ЕСМ" +
                    (status.Length == 0 ? string.Empty : "; ЛМ: " + status) +
                    "." + (endpointDiffers
                        ? " ЕСМ показывает адрес ЛМ " + observation.LmAddress +
                            ":" + observation.LmPort +
                            " — это его модуль по умолчанию; фактическую " +
                            "связку он ведёт через контроллер, ошибкой это " +
                            "не является."
                        : string.Empty);
            }
            if (state == LmContourReadbackState.LocalModuleNotInitialized)
            {
                string reported = (observation.LmStatus ?? string.Empty).Trim();
                bool endpointConfirmed = observation.EndpointMatches.HasValue &&
                    observation.EndpointMatches.Value;
                return prefix + (endpointConfirmed
                    ? "привязка подтверждена ЕСМ (" + observation.LmAddress +
                        ":" + observation.LmPort + "); "
                    : "привязка принята ЕСМ; ") +
                    "ЛМ ЧЗ ещё не инициализирован — ЕСМ сообщает: " +
                    reported + ". Адрес ЛМ ЕСМ покажет после инициализации.";
            }
            string details = observation == null
                ? string.Empty
                : (observation.Details ?? string.Empty).Trim();
            if (state == LmContourReadbackState.Attention)
                return prefix + "требуется проверка: " + details;
            return prefix + "ЕСМ недоступен для контрольного чтения: " + details;
        }

        /// <summary>
        /// ЕСМ сообщает, что локальный модуль ещё не готов к работе: идёт
        /// инициализация или модуль отвечает ошибкой до неё. Инициализация ЛМ
        /// вне задачи утилиты, поэтому это строка оператору, а не отказ
        /// контура.
        /// </summary>
        public static bool IsLocalModulePending(string status)
        {
            string value = (status ?? string.Empty).Trim();
            if (value.Length == 0)
                return false;
            return value.StartsWith("error", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("init", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("not_initialized", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("not_configured", StringComparison.OrdinalIgnoreCase) ||
                DirectControllerSetupPolicy.IsDeferredLocalModuleWarning(value);
        }
    }
}
