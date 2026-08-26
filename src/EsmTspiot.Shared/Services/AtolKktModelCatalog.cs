using System.Collections.Generic;

namespace EsmTspiot.Shared.Services
{
    public static class AtolKktModelCatalog
    {
        private static readonly IDictionary<string, string> Models = new Dictionary<string, string>
        {
            { "057", "Атол 25Ф" },
            { "061", "Атол 30Ф" },
            { "062", "Атол 55Ф" },
            { "063", "Атол FPrint-22ПТК" },
            { "064", "Атол 52Ф" },
            { "067", "Атол 11Ф" },
            { "069", "Атол 77Ф" },
            { "072", "Атол 90Ф" },
            { "075", "Атол 60Ф" },
            { "077", "Атол 42Ф" },
            { "078", "Атол 15Ф" },
            { "080", "Атол 50Ф" },
            { "081", "Атол 20Ф" },
            { "082", "Атол 91Ф" },
            { "083", "Эвотор СТ5Ф" },
            { "084", "Атол 92Ф" },
            { "086", "Атол 150Ф" },
            { "087", "Атол 27Ф" },
            { "090", "Атол Sigma 7Ф" },
            { "091", "Атол Sigma 8Ф" },
            { "093", "Атол 1Ф" },
            { "095", "Атол 22 v2 Ф" },
            { "096", "Эвотор СТ51Ф" }
        };

        public static bool TryGetModelName(string serial, out string modelCode, out string modelName)
        {
            modelCode = string.Empty;
            modelName = string.Empty;

            if (string.IsNullOrEmpty(serial) || serial.Length < 6 || !serial.StartsWith("001"))
            {
                return false;
            }

            modelCode = serial.Substring(3, 3);
            return Models.TryGetValue(modelCode, out modelName);
        }

        public static bool IsKnownModelCode(string serial, out string modelCode)
        {
            string modelName;
            return TryGetModelName(serial, out modelCode, out modelName);
        }
    }
}
