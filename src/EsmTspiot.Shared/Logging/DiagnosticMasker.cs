using System;
using System.IO;
using System.Text.RegularExpressions;

namespace EsmTspiot.Shared.Logging
{
    public static class DiagnosticMasker
    {
        private static readonly Regex FiscalIdentifierPattern =
            new Regex(@"(?<![0-9])[0-9]{10,16}(?![0-9])", RegexOptions.Compiled);
        public static string Mask(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            string masked = SensitiveDataMasker.Mask(text);
            masked = ReplacePath(masked, Path.GetTempPath(), "%TEMP%");
            masked = ReplacePath(
                masked,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "%LOCALAPPDATA%");
            masked = ReplacePath(
                masked,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "%USERPROFILE%");
            return FiscalIdentifierPattern.Replace(masked, delegate(Match match)
            {
                if (match.Value.Length <= 5)
                {
                    return new string('*', match.Value.Length);
                }

                return match.Value.Substring(0, 3) +
                    new string('*', match.Value.Length - 5) +
                    match.Value.Substring(match.Value.Length - 2);
            });
        }

        private static string ReplacePath(string text, string path, string replacement)
        {
            string normalized = (path ?? string.Empty).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (normalized.Length == 0)
            {
                return text;
            }

            return Regex.Replace(
                text,
                Regex.Escape(normalized),
                delegate { return replacement; },
                RegexOptions.IgnoreCase);
        }
    }
}
