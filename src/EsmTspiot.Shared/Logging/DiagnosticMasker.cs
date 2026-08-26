using System;
using System.IO;
using System.Text.RegularExpressions;

namespace EsmTspiot.Shared.Logging
{
    public static class DiagnosticMasker
    {
        private static readonly Regex FiscalIdentifierPattern =
            new Regex(@"(?<![0-9])[0-9]{10,16}(?![0-9])", RegexOptions.Compiled);
        private static readonly Regex SensitiveJsonValuePattern = new Regex(
            @"(?<prefix>""(?:password|token|secret|authorization|apiKey|connectionString)""\s*:\s*"")(?<value>[^""]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SensitiveKeyValuePattern = new Regex(
            @"(?<prefix>\b(?:password|token|secret|authorization|apiKey)\s*=\s*)(?<value>[^&\s;]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Mask(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            string masked = ReplacePath(text, Path.GetTempPath(), "%TEMP%");
            masked = ReplacePath(
                masked,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "%LOCALAPPDATA%");
            masked = ReplacePath(
                masked,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "%USERPROFILE%");
            masked = MaskSensitiveValues(masked, SensitiveJsonValuePattern);
            masked = MaskSensitiveValues(masked, SensitiveKeyValuePattern);

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

        private static string MaskSensitiveValues(string text, Regex pattern)
        {
            return pattern.Replace(text, delegate(Match match)
            {
                return match.Groups["prefix"].Value + "***";
            });
        }
    }
}
