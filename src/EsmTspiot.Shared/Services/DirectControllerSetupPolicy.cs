using System;

namespace EsmTspiot.Shared.Services
{
    public static class DirectControllerSetupPolicy
    {
        public static bool IsDeferredLocalModuleWarning(string message)
        {
            string value = message ?? string.Empty;
            return value.IndexOf("2025", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("2055", StringComparison.Ordinal) >= 0;
        }

        public static bool IsComplete(
            int expectedControllers,
            int readyControllers,
            int failedControllers,
            bool cancelled)
        {
            return expectedControllers > 0 &&
                readyControllers == expectedControllers &&
                failedControllers == 0 &&
                !cancelled;
        }
    }
}
