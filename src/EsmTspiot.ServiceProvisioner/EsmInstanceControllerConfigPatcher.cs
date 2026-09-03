using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EsmTspiot.ServiceProvisioner
{
    /// <summary>
    /// Точечный патч экземплярного YAML ЕСМ. Меняются только скаляры
    /// gRPCPort, RESTPort и url у единственного узла settings.ldbControl
    /// (узел допускается на любой глубине, например defaultconfig.settings.ldbControl).
    /// Разбор намеренно ограничен: поддерживаются блочные отображения,
    /// блочные последовательности и блочные скаляры; якоря, ссылки, слияния
    /// и многодокументные файлы отвергаются до записи на диск.
    /// </summary>
    internal static class EsmInstanceControllerConfigPatcher
    {
        private const string TargetSuffix = "settings.ldbControl";

        internal static string Patch(string source, int grpcPort, int restPort)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (grpcPort < 1024 || grpcPort > 65535)
            {
                throw new ArgumentOutOfRangeException("grpcPort");
            }
            if (restPort < 1024 || restPort > 65535 || restPort == grpcPort)
            {
                throw new ArgumentOutOfRangeException("restPort");
            }

            string newline = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0
                ? "\r\n"
                : "\n";
            string normalized = source.Replace("\r\n", "\n");
            if (normalized.IndexOf('\r') >= 0)
            {
                throw new InvalidDataException(
                    "YAML ЕСМ содержит смешанные переводы строк.");
            }
            string[] lines = normalized.Split(new[] { '\n' });
            Dictionary<string, YamlEntry> entries = Parse(lines);
            string target = FindUniqueTarget(entries);
            ReplaceScalar(lines, entries, target + ".gRPCPort",
                grpcPort.ToString(CultureInfo.InvariantCulture));
            ReplaceScalar(lines, entries, target + ".RESTPort",
                restPort.ToString(CultureInfo.InvariantCulture));
            ReplaceScalar(lines, entries, target + ".url", "127.0.0.1");
            return string.Join(newline, lines);
        }

        private static Dictionary<string, YamlEntry> Parse(string[] lines)
        {
            Dictionary<string, YamlEntry> entries =
                new Dictionary<string, YamlEntry>(StringComparer.Ordinal);
            List<YamlScope> scopes = new List<YamlScope>();
            bool documentStarted = false;
            int index = 0;
            while (index < lines.Length)
            {
                string line = lines[index];
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    index++;
                    continue;
                }
                int indent = MeasureIndent(line);
                if (trimmed == "---")
                {
                    if (documentStarted)
                    {
                        throw new InvalidDataException(
                            "YAML ЕСМ содержит несколько документов.");
                    }
                    documentStarted = true;
                    index++;
                    continue;
                }
                if (trimmed == "...")
                {
                    throw new InvalidDataException(
                        "YAML ЕСМ содержит несколько документов.");
                }
                documentStarted = true;
                index = IsSequenceEntry(trimmed)
                    ? ReadSequenceEntry(lines, index, line, trimmed, indent, scopes, entries)
                    : ReadMapping(lines, index, line, trimmed, indent, scopes, entries);
            }
            return entries;
        }

        private static int ReadSequenceEntry(
            string[] lines,
            int index,
            string line,
            string trimmed,
            int indent,
            List<YamlScope> scopes,
            IDictionary<string, YamlEntry> entries)
        {
            PopDeeperThan(scopes, indent);
            if (scopes.Count > 0 &&
                scopes[scopes.Count - 1].Kind == YamlScopeKind.SequenceItem &&
                scopes[scopes.Count - 1].Indent == indent)
            {
                scopes.RemoveAt(scopes.Count - 1);
            }
            int itemIndex;
            if (scopes.Count > 0 &&
                scopes[scopes.Count - 1].Kind == YamlScopeKind.Sequence &&
                scopes[scopes.Count - 1].Indent == indent)
            {
                YamlScope sequence = scopes[scopes.Count - 1];
                sequence.ItemCount = sequence.ItemCount + 1;
                itemIndex = sequence.ItemCount - 1;
            }
            else
            {
                scopes.Add(new YamlScope
                {
                    Kind = YamlScopeKind.Sequence,
                    Indent = indent,
                    ItemCount = 1
                });
                itemIndex = 0;
            }

            string rest = trimmed.Substring(1).Trim();
            if (rest.Length == 0 || rest[0] == '#')
            {
                scopes.Add(new YamlScope
                {
                    Kind = YamlScopeKind.SequenceItem,
                    Indent = indent,
                    ItemIndex = itemIndex
                });
                return index + 1;
            }
            RequireNoAnchorOrAlias(rest);
            int colon = FindMappingColon(rest);
            if (colon <= 0)
            {
                if (IsBlockScalarHeader(StripInlineComment(rest).Trim()))
                {
                    return SkipBlockScalar(lines, index, indent);
                }
                return index + 1;
            }
            scopes.Add(new YamlScope
            {
                Kind = YamlScopeKind.SequenceItem,
                Indent = indent,
                ItemIndex = itemIndex
            });
            int keyColumn = indent + 1;
            while (keyColumn < line.Length && line[keyColumn] == ' ')
            {
                keyColumn++;
            }
            return ReadMapping(lines, index, line, rest, keyColumn, scopes, entries);
        }

        private static int ReadMapping(
            string[] lines,
            int index,
            string line,
            string trimmed,
            int indent,
            List<YamlScope> scopes,
            IDictionary<string, YamlEntry> entries)
        {
            PopAtOrDeeperThan(scopes, indent);
            if (scopes.Count > 0 &&
                scopes[scopes.Count - 1].Kind == YamlScopeKind.Sequence)
            {
                throw new InvalidDataException(
                    "YAML ЕСМ смешивает элементы последовательности и ключи отображения.");
            }
            int colon = FindMappingColon(trimmed);
            if (colon <= 0)
            {
                throw new InvalidDataException(
                    "YAML ЕСМ содержит неподдерживаемую строку отображения.");
            }
            string key = trimmed.Substring(0, colon).Trim();
            if (key == "<<")
            {
                throw new InvalidDataException(
                    "YAML ЕСМ содержит слияние ключей (<<).");
            }
            if (!IsPlainKey(key))
            {
                throw new InvalidDataException(
                    "YAML ЕСМ содержит неподдерживаемый ключ.");
            }
            string path = BuildPath(scopes, key);
            if (entries.ContainsKey(path))
            {
                throw new InvalidDataException(
                    "YAML ЕСМ содержит повторяющийся ключ: " + path + ".");
            }
            YamlEntry entry = new YamlEntry { LineIndex = index, HasNestedBlock = false };
            entries.Add(path, entry);

            string value = StripInlineComment(trimmed.Substring(colon + 1)).Trim();
            if (value.Length == 0)
            {
                entry.HasNestedBlock = true;
                scopes.Add(new YamlScope
                {
                    Kind = YamlScopeKind.Mapping,
                    Indent = indent,
                    Key = key
                });
                return index + 1;
            }
            RequireNoAnchorOrAlias(value);
            if (IsBlockScalarHeader(value))
            {
                entry.HasNestedBlock = true;
                return SkipBlockScalar(lines, index, indent);
            }
            return index + 1;
        }

        private static void PopDeeperThan(List<YamlScope> scopes, int indent)
        {
            while (scopes.Count > 0 && scopes[scopes.Count - 1].Indent > indent)
            {
                scopes.RemoveAt(scopes.Count - 1);
            }
        }

        private static void PopAtOrDeeperThan(List<YamlScope> scopes, int indent)
        {
            while (scopes.Count > 0 && scopes[scopes.Count - 1].Indent >= indent)
            {
                scopes.RemoveAt(scopes.Count - 1);
            }
        }

        private static int SkipBlockScalar(string[] lines, int index, int indent)
        {
            int next = index + 1;
            while (next < lines.Length)
            {
                string line = lines[next];
                if (line.Trim().Length == 0)
                {
                    next++;
                    continue;
                }
                int leading = 0;
                while (leading < line.Length && line[leading] == ' ')
                {
                    leading++;
                }
                if (leading <= indent) break;
                next++;
            }
            return next;
        }

        private static string FindUniqueTarget(IDictionary<string, YamlEntry> entries)
        {
            string found = null;
            foreach (KeyValuePair<string, YamlEntry> entry in entries)
            {
                if (entry.Key != TargetSuffix &&
                    !entry.Key.EndsWith("." + TargetSuffix, StringComparison.Ordinal))
                {
                    continue;
                }
                if (found != null)
                {
                    throw new InvalidDataException(
                        "YAML ЕСМ содержит несколько узлов " + TargetSuffix + ".");
                }
                found = entry.Key;
            }
            if (found == null)
            {
                throw new InvalidDataException(
                    "YAML ЕСМ не содержит узла " + TargetSuffix + ".");
            }
            return found;
        }

        private static void ReplaceScalar(
            string[] lines,
            IDictionary<string, YamlEntry> entries,
            string path,
            string value)
        {
            YamlEntry entry;
            if (!entries.TryGetValue(path, out entry))
            {
                throw new InvalidDataException(
                    "YAML ЕСМ не содержит " + path + ".");
            }
            if (entry.HasNestedBlock)
            {
                throw new InvalidDataException(
                    "YAML ЕСМ содержит блок вместо скаляра в " + path + ".");
            }
            string line = lines[entry.LineIndex];
            int colon = FindMappingColon(line);
            if (colon < 0)
            {
                throw new InvalidDataException(
                    "YAML ЕСМ не содержит " + path + ".");
            }
            int comment = FindInlineComment(line, colon + 1);
            string suffix = comment < 0 ? string.Empty : line.Substring(comment);
            string prefix = line.Substring(0, colon + 1);
            lines[entry.LineIndex] = prefix + " " + value +
                (suffix.Length == 0 ? string.Empty : " " + suffix.TrimStart());
        }

        private static bool IsSequenceEntry(string trimmed)
        {
            return trimmed[0] == '-' && (trimmed.Length == 1 || trimmed[1] == ' ');
        }

        private static void RequireNoAnchorOrAlias(string value)
        {
            if (value.Length > 0 && (value[0] == '&' || value[0] == '*'))
            {
                throw new InvalidDataException(
                    "YAML ЕСМ содержит якорь или ссылку.");
            }
        }

        private static bool IsBlockScalarHeader(string value)
        {
            if (value.Length == 0) return false;
            if (value[0] != '|' && value[0] != '>') return false;
            for (int index = 1; index < value.Length; index++)
            {
                char current = value[index];
                if (current == '+' || current == '-') continue;
                if (current >= '0' && current <= '9') continue;
                return false;
            }
            return true;
        }

        private static int MeasureIndent(string line)
        {
            int indent = 0;
            while (indent < line.Length && line[indent] == ' ')
            {
                indent++;
            }
            if (indent < line.Length && line[indent] == '\t')
            {
                throw new InvalidDataException(
                    "YAML ЕСМ использует табуляцию для отступа.");
            }
            return indent;
        }

        private static int FindMappingColon(string value)
        {
            bool single = false;
            bool doubleQuoted = false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (current == '\'' && !doubleQuoted) single = !single;
                else if (current == '"' && !single) doubleQuoted = !doubleQuoted;
                else if (current == ':' && !single && !doubleQuoted) return index;
            }
            return -1;
        }

        private static int FindInlineComment(string value, int start)
        {
            bool single = false;
            bool doubleQuoted = false;
            for (int index = start; index < value.Length; index++)
            {
                char current = value[index];
                if (current == '\'' && !doubleQuoted) single = !single;
                else if (current == '"' && !single) doubleQuoted = !doubleQuoted;
                else if (current == '#' && !single && !doubleQuoted) return index;
            }
            return -1;
        }

        private static string StripInlineComment(string value)
        {
            int comment = FindInlineComment(value, 0);
            return comment < 0 ? value : value.Substring(0, comment);
        }

        private static bool IsPlainKey(string key)
        {
            if (key.Length == 0) return false;
            for (int index = 0; index < key.Length; index++)
            {
                char current = key[index];
                if (!((current >= 'a' && current <= 'z') ||
                      (current >= 'A' && current <= 'Z') ||
                      (current >= '0' && current <= '9') ||
                      current == '_' || current == '-'))
                {
                    return false;
                }
            }
            return true;
        }

        private static string BuildPath(IList<YamlScope> scopes, string key)
        {
            StringBuilder path = new StringBuilder();
            for (int index = 0; index < scopes.Count; index++)
            {
                YamlScope scope = scopes[index];
                if (scope.Kind == YamlScopeKind.Mapping)
                {
                    if (path.Length > 0) path.Append('.');
                    path.Append(scope.Key);
                }
                else if (scope.Kind == YamlScopeKind.SequenceItem)
                {
                    path.Append('[');
                    path.Append(scope.ItemIndex.ToString(CultureInfo.InvariantCulture));
                    path.Append(']');
                }
            }
            if (path.Length > 0) path.Append('.');
            path.Append(key);
            return path.ToString();
        }

        private enum YamlScopeKind
        {
            Mapping = 0,
            Sequence = 1,
            SequenceItem = 2
        }

        private sealed class YamlScope
        {
            internal YamlScopeKind Kind { get; set; }
            internal int Indent { get; set; }
            internal string Key { get; set; }
            internal int ItemCount { get; set; }
            internal int ItemIndex { get; set; }
        }

        private sealed class YamlEntry
        {
            internal int LineIndex { get; set; }
            internal bool HasNestedBlock { get; set; }
        }
    }
}
