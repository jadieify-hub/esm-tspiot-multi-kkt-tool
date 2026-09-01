using System;
using System.Text;
using System.Text.RegularExpressions;

namespace EsmTspiot.Shared.Logging
{
    public static class SensitiveDataMasker
    {
        private static readonly string[] SensitiveKeys =
        {
            "password",
            "newPassword",
            "pass",
            "token",
            "secret",
            "authorization",
            "apiKey",
            "connectionString"
        };

        private static readonly Regex SensitiveKeyValuePattern = new Regex(
            @"(?<prefix>\b(?:" + string.Join("|", SensitiveKeys) +
            @")\s*=\s*)(?<value>""(?:\\.|[^""])*""|'(?:\\.|[^'])*'|[^&;\r\n]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Mask(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            string masked = MaskYamlValues(text);
            masked = MaskJsonValues(masked);
            return SensitiveKeyValuePattern.Replace(masked, delegate(Match match)
            {
                return match.Groups["prefix"].Value + "***";
            });
        }

        private static string MaskYamlValues(string text)
        {
            string newline = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0
                ? "\r\n"
                : "\n";
            string normalized = text.Replace("\r\n", "\n");
            string[] lines = normalized.Split(new[] { '\n' });
            StringBuilder builder = new StringBuilder(text.Length);
            int secretBlockIndent = -1;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                int indent = CountLeadingSpaces(line);
                string trimmed = line.Trim();
                if (secretBlockIndent >= 0)
                {
                    if (trimmed.Length == 0 || indent > secretBlockIndent)
                    {
                        if (index + 1 < lines.Length) builder.Append(newline);
                        continue;
                    }
                    secretBlockIndent = -1;
                }

                string maskedLine;
                bool startsBlock;
                if (TryMaskYamlLine(line, out maskedLine, out startsBlock))
                {
                    builder.Append(maskedLine);
                    if (startsBlock) secretBlockIndent = indent;
                }
                else
                {
                    builder.Append(line);
                }
                if (index + 1 < lines.Length) builder.Append(newline);
            }
            return builder.ToString();
        }

        private static bool TryMaskYamlLine(
            string line,
            out string masked,
            out bool startsBlock)
        {
            masked = line;
            startsBlock = false;
            int indent = CountLeadingSpaces(line);
            string content = line.Substring(indent);
            if (content.Length == 0 || content[0] == '#' || content[0] == '-' ||
                content[0] == '"' || content[0] == '\'')
            {
                return false;
            }
            int colon = content.IndexOf(':');
            if (colon <= 0) return false;
            string key = content.Substring(0, colon).Trim();
            if (!IsSensitiveKey(key)) return false;
            string value = content.Substring(colon + 1).TrimStart();
            int comment = FindYamlComment(value);
            string scalar = comment < 0 ? value : value.Substring(0, comment).TrimEnd();
            string suffix = comment < 0
                ? string.Empty
                : " " + value.Substring(comment).TrimStart();
            startsBlock = scalar == "|" || scalar == ">" ||
                scalar.StartsWith("|-", StringComparison.Ordinal) ||
                scalar.StartsWith("|+", StringComparison.Ordinal) ||
                scalar.StartsWith(">-", StringComparison.Ordinal) ||
                scalar.StartsWith(">+", StringComparison.Ordinal);
            masked = new string(' ', indent) + content.Substring(0, colon + 1) +
                " ***" + suffix;
            return true;
        }

        private static int CountLeadingSpaces(string value)
        {
            int count = 0;
            while (count < value.Length && value[count] == ' ') count++;
            return count;
        }

        private static int FindYamlComment(string value)
        {
            bool single = false;
            bool doubleQuoted = false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (current == '\'' && !doubleQuoted) single = !single;
                else if (current == '"' && !single) doubleQuoted = !doubleQuoted;
                else if (current == '#' && !single && !doubleQuoted) return index;
            }
            return -1;
        }

        private static string MaskJsonValues(string text)
        {
            StringBuilder builder = null;
            int copyStart = 0;
            int position = 0;

            while (position < text.Length)
            {
                if (text[position] != '"')
                {
                    position++;
                    continue;
                }

                int keyEnd;
                if (!TryFindStringEnd(text, position, out keyEnd))
                {
                    break;
                }

                string key = TryDecodeJsonString(text, position + 1, keyEnd);
                int valueStart = SkipWhitespace(text, keyEnd + 1);
                if (!IsSensitiveKey(key) || valueStart >= text.Length || text[valueStart] != ':')
                {
                    position = keyEnd + 1;
                    continue;
                }

                valueStart = SkipWhitespace(text, valueStart + 1);
                if (valueStart >= text.Length)
                {
                    break;
                }

                if (builder == null)
                {
                    builder = new StringBuilder(text.Length);
                }

                if (text[valueStart] == '"')
                {
                    builder.Append(text, copyStart, valueStart - copyStart + 1);
                    builder.Append("***");

                    int valueEnd;
                    if (!TryFindStringEnd(text, valueStart, out valueEnd))
                    {
                        copyStart = text.Length;
                        break;
                    }

                    builder.Append('"');
                    copyStart = valueEnd + 1;
                    position = copyStart;
                    continue;
                }

                int unquotedEnd = FindUnquotedValueEnd(text, valueStart);
                builder.Append(text, copyStart, valueStart - copyStart);
                builder.Append("***");
                copyStart = unquotedEnd;
                position = unquotedEnd;
            }

            if (builder == null)
            {
                return text;
            }

            if (copyStart < text.Length)
            {
                builder.Append(text, copyStart, text.Length - copyStart);
            }

            return builder.ToString();
        }

        private static bool TryFindStringEnd(string text, int quoteStart, out int quoteEnd)
        {
            bool escaped = false;
            for (int index = quoteStart + 1; index < text.Length; index++)
            {
                char current = text[index];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    quoteEnd = index;
                    return true;
                }
            }

            quoteEnd = -1;
            return false;
        }

        private static string TryDecodeJsonString(string text, int start, int end)
        {
            StringBuilder builder = new StringBuilder(end - start);
            for (int index = start; index < end; index++)
            {
                char current = text[index];
                if (current != '\\')
                {
                    builder.Append(current);
                    continue;
                }

                index++;
                if (index >= end)
                {
                    return null;
                }

                char escaped = text[index];
                switch (escaped)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        int unicodeValue;
                        if (index + 4 >= end ||
                            !int.TryParse(
                                text.Substring(index + 1, 4),
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out unicodeValue))
                        {
                            return null;
                        }

                        builder.Append((char)unicodeValue);
                        index += 4;
                        break;
                    default:
                        return null;
                }
            }

            return builder.ToString();
        }

        private static bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (key.EndsWith("token", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            for (int index = 0; index < SensitiveKeys.Length; index++)
            {
                if (string.Equals(key, SensitiveKeys[index], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int SkipWhitespace(string text, int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }

            return position;
        }

        private static int FindUnquotedValueEnd(string text, int start)
        {
            int position = start;
            while (position < text.Length)
            {
                char current = text[position];
                if (current == ',' || current == '}' || current == ']' ||
                    current == '\r' || current == '\n')
                {
                    break;
                }

                position++;
            }

            return position;
        }
    }
}
