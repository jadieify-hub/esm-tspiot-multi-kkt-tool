using System;
using System.Collections.Generic;
using System.IO;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class EsmInstanceControllerConfigPatcher
    {
        private const string TargetRoot = "settings.ldbControl";

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
            if (source.IndexOf('\t') >= 0)
            {
                throw new InvalidDataException(
                    "ESM instance YAML must use spaces for indentation.");
            }

            string newline = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0
                ? "\r\n"
                : "\n";
            string normalized = source.Replace("\r\n", "\n");
            if (normalized.IndexOf('\r') >= 0)
            {
                throw new InvalidDataException(
                    "ESM instance YAML uses mixed line endings.");
            }
            string[] lines = normalized.Split(new[] { '\n' });
            List<YamlParent> parents = new List<YamlParent>();
            Dictionary<string, int> entries =
                new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (trimmed == "---" || trimmed == "..." ||
                    trimmed.StartsWith("-", StringComparison.Ordinal) ||
                    ContainsUnsupportedYamlIdentity(trimmed))
                {
                    throw new InvalidDataException(
                        "ESM instance YAML contains unsupported anchors, aliases, merges, or sequences.");
                }
                int indent = line.Length - line.TrimStart(' ').Length;
                while (parents.Count > 0 &&
                    parents[parents.Count - 1].Indent >= indent)
                {
                    parents.RemoveAt(parents.Count - 1);
                }
                int colon = FindMappingColon(trimmed);
                if (colon <= 0)
                {
                    throw new InvalidDataException(
                        "ESM instance YAML contains an unsupported mapping line.");
                }
                string key = trimmed.Substring(0, colon).Trim();
                if (!IsPlainKey(key))
                {
                    throw new InvalidDataException(
                        "ESM instance YAML contains an unsupported key.");
                }
                string path = BuildPath(parents, key);
                if (entries.ContainsKey(path))
                {
                    throw new InvalidDataException(
                        "ESM instance YAML contains a duplicate key: " + path + ".");
                }
                entries.Add(path, index);

                string value = StripInlineComment(trimmed.Substring(colon + 1)).Trim();
                if (value.Length == 0)
                {
                    parents.Add(new YamlParent { Indent = indent, Key = key });
                }
            }

            RequireSingleTarget(entries, TargetRoot);
            ReplaceScalar(lines, entries, TargetRoot + ".gRPCPort", grpcPort.ToString());
            ReplaceScalar(lines, entries, TargetRoot + ".RESTPort", restPort.ToString());
            ReplaceScalar(lines, entries, TargetRoot + ".url", "127.0.0.1");
            return string.Join(newline, lines);
        }

        private static void RequireSingleTarget(
            IDictionary<string, int> entries,
            string path)
        {
            int ignored;
            if (!entries.TryGetValue(path, out ignored))
            {
                throw new InvalidDataException(
                    "ESM instance YAML does not contain settings.ldbControl.");
            }
        }

        private static void ReplaceScalar(
            string[] lines,
            IDictionary<string, int> entries,
            string path,
            string value)
        {
            int index;
            if (!entries.TryGetValue(path, out index))
            {
                throw new InvalidDataException(
                    "ESM instance YAML does not contain " + path + ".");
            }
            string line = lines[index];
            int colon = line.IndexOf(':');
            int comment = FindInlineComment(line, colon + 1);
            string suffix = comment < 0 ? string.Empty : line.Substring(comment);
            string prefix = line.Substring(0, colon + 1);
            lines[index] = prefix + " " + value +
                (suffix.Length == 0 ? string.Empty : " " + suffix.TrimStart());
        }

        private static bool ContainsUnsupportedYamlIdentity(string value)
        {
            return value.IndexOf("<<:", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("&", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("*", StringComparison.Ordinal) >= 0;
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

        private static string BuildPath(IList<YamlParent> parents, string key)
        {
            string path = string.Empty;
            for (int index = 0; index < parents.Count; index++)
            {
                if (path.Length > 0) path += ".";
                path += parents[index].Key;
            }
            return path.Length == 0 ? key : path + "." + key;
        }

        private sealed class YamlParent
        {
            internal int Indent { get; set; }
            internal string Key { get; set; }
        }
    }
}
