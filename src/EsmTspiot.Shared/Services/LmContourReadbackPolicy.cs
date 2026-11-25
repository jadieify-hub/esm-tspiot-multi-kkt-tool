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
                LocalModuleTargetAddress.Describe(
                    assignment.TargetLocalModuleAddress),
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
            if (observation == null) return prefix + "Нет ответа ЕСМ.";
            return prefix + (observation.IsVerified ? "привязка подтверждена ЕСМ. " : string.Empty) +
                (string.IsNullOrWhiteSpace(observation.Details)
                ? FormatModule(observation) : observation.Details);
        }

        internal static string FormatModule(LmGatewayReadbackObservation observation, bool labelAddress = false)
        {
            string status = DescribeStatus(observation.LmStatus);
            string module = "ЛМ ЧЗ";
            if (!string.IsNullOrEmpty(observation.LmVersion)) module += " " + observation.LmVersion;
            if (!string.IsNullOrEmpty(observation.LmAddress))
                module += (labelAddress ? " · Адрес в ЕСМ: " : " · ") + observation.LmAddress + ":" + observation.LmPort;
            return module + " · " + status;
        }

        public static string DescribeStatus(string status)
        {
            string value = (status ?? string.Empty).Trim();
            switch (value.ToLowerInvariant())
            {
                case "initialization": return "Идёт инициализация (" + value + ")";
                case "initialization_failed": return "Ошибка инициализации (" + value + ")";
                case "not_initialized": return "ЛМ не инициализирован (" + value + ")";
                case "not_configured": return "ЛМ не настроен (" + value + ")";
                case "ready": return "Готов к работе (" + value + ")";
                case "": return "ЕСМ не сообщил состояние";
                default: return value.StartsWith("error", StringComparison.OrdinalIgnoreCase)
                    ? "Ошибка ЛМ: " + value : value;
            }
        }

        /// <summary>
        /// ЕСМ сообщает, что локальный модуль ещё не работает: идёт
        /// инициализация, модуль не настроен или отвечает ошибкой. Контур
        /// настраивается независимо от этого: инициализация ЛМ идёт в фоне и
        /// занимает часы, поэтому статус модуля — строка оператору, а не
        /// критерий готовности. Ужесточать список кодов бессмысленно: любой
        /// незнакомый код здесь означает ровно то же — модуль пока не
        /// работает, а контур это не останавливает.
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
