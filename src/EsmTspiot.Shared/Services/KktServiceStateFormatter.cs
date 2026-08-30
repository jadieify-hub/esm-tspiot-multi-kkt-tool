using System;

namespace EsmTspiot.Shared.Services
{
    public static class KktServiceStateFormatter
    {
        public static string ToDisplayText(string value)
        {
            string state = (value ?? string.Empty).Trim();
            if (state.Length == 0)
            {
                return "—";
            }
            if (string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase))
            {
                return "Запущена";
            }
            if (string.Equals(state, "Stopped", StringComparison.OrdinalIgnoreCase))
            {
                return "Остановлена";
            }
            if (string.Equals(state, "StartPending", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "Starting", StringComparison.OrdinalIgnoreCase))
            {
                return "Запускается";
            }
            if (string.Equals(state, "StopPending", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "Stopping", StringComparison.OrdinalIgnoreCase))
            {
                return "Останавливается";
            }
            return state;
        }
    }
}
