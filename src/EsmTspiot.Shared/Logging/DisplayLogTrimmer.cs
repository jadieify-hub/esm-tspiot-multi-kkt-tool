namespace EsmTspiot.Shared.Logging
{
    public static class DisplayLogTrimmer
    {
        private const string TrimmedMarker = "--- начало журнала см. в файле ---\r\n\r\n";

        public static string TrimIfNeeded(string text, int maximumLength)
        {
            string value = text ?? string.Empty;
            if (value.Length <= maximumLength)
            {
                return value;
            }

            return TrimmedMarker + value.Substring(value.Length / 2);
        }
    }
}
