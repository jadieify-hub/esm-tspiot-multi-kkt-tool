using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IDirectControllerProfileStore
    {
        string PrepareClone(DirectControllerManifest manifest);

        /// <summary>
        /// Направляет штатный контроллер на удалённый ЛМ точечной правкой
        /// lmConfig.url/port в его config.yml; прежние значения запоминаются
        /// в манифесте. Возвращает true, если файл изменён.
        /// </summary>
        bool RetargetOfficial(DirectControllerManifest manifest);

        /// <summary>
        /// Возвращает запомненные lmConfig.url/port штатного контроллера.
        /// Возвращает true, если файл изменён.
        /// </summary>
        bool RestoreOfficial(DirectControllerManifest manifest);
    }

    internal sealed class DirectControllerProfileStore : IDirectControllerProfileStore
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly ControllerCapabilityProfile _profile;
        private readonly DirectControllerManifestStore _manifests;
        private readonly DirectControllerCaStager _caStager;
        private readonly AtomicFileWriter _writer;
        private readonly IPathSafety _pathSafety;
        private readonly string _officialConfigPath;

        internal DirectControllerProfileStore(
            ControllerCapabilityProfile profile,
            DirectControllerManifestStore manifests,
            DirectControllerCaStager caStager,
            AtomicFileWriter writer,
            IPathSafety pathSafety)
            : this(profile, manifests, caStager, writer, pathSafety, null)
        {
        }

        internal DirectControllerProfileStore(
            ControllerCapabilityProfile profile,
            DirectControllerManifestStore manifests,
            DirectControllerCaStager caStager,
            AtomicFileWriter writer,
            IPathSafety pathSafety,
            string officialConfigPath)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (manifests == null) throw new ArgumentNullException("manifests");
            if (caStager == null) throw new ArgumentNullException("caStager");
            if (writer == null) throw new ArgumentNullException("writer");
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            if (                !string.Equals(profile.ProfileEnvironmentKey, "ProgramData", StringComparison.Ordinal) ||
                !string.Equals(profile.VendorProfileRelativePath,
                    Path.Combine("ESP", "lmcontroller"),
                    StringComparison.Ordinal) ||
                !string.Equals(profile.VendorConfigFileName, "config.yml", StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    "The direct controller profile schema is not characterized.");
            }
            _profile = profile;
            _manifests = manifests;
            _caStager = caStager;
            _writer = writer;
            _pathSafety = pathSafety;
            _officialConfigPath = string.IsNullOrEmpty(officialConfigPath)
                ? Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonApplicationData),
                    profile.VendorProfileRelativePath,
                    profile.VendorConfigFileName)
                : officialConfigPath;
        }

        public bool RetargetOfficial(DirectControllerManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            if (manifest.Ordinal != 1)
                throw new InvalidOperationException(
                    "Only the official controller profile is retargeted.");
            bool remembered = !string.IsNullOrEmpty(manifest.OfficialLmUrlOriginal);
            // ЛМ на этом компьютере: файл вендора программа не трогала —
            // и не трогает; если трогала — возвращает прежние значения.
            if (!LocalModuleTargetAddress.IsRemote(manifest.TargetLocalModuleAddress))
                return remembered && RestoreOfficial(manifest);

            OfficialConfig config = ReadOfficial();
            string url = "http://" + manifest.TargetLocalModuleAddress;
            string port = manifest.TargetLocalModulePort.ToString();
            if (SameUrl(config.Value(config.UrlLine), url) &&
                string.Equals(config.Value(config.PortLine), port, StringComparison.Ordinal))
                return false;
            if (!remembered)
            {
                manifest.OfficialLmUrlOriginal = config.Value(config.UrlLine);
                manifest.OfficialLmPortOriginal = config.Value(config.PortLine);
            }
            Replace(config.Lines, config.UrlLine, "url", "'" + url + "'");
            Replace(config.Lines, config.PortLine, "port", port);
            WriteOfficial(config);
            return true;
        }

        public bool RestoreOfficial(DirectControllerManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            if (string.IsNullOrEmpty(manifest.OfficialLmUrlOriginal)) return false;
            bool changed = false;
            if (File.Exists(_officialConfigPath))
            {
                OfficialConfig config = ReadOfficial();
                if (!string.Equals(config.Value(config.UrlLine),
                        manifest.OfficialLmUrlOriginal, StringComparison.Ordinal) ||
                    !string.Equals(config.Value(config.PortLine),
                        manifest.OfficialLmPortOriginal, StringComparison.Ordinal))
                {
                    Replace(config.Lines, config.UrlLine, "url",
                        manifest.OfficialLmUrlOriginal);
                    Replace(config.Lines, config.PortLine, "port",
                        manifest.OfficialLmPortOriginal);
                    WriteOfficial(config);
                    changed = true;
                }
            }
            manifest.OfficialLmUrlOriginal = null;
            manifest.OfficialLmPortOriginal = null;
            return changed;
        }

        private OfficialConfig ReadOfficial()
        {
            if (!File.Exists(_officialConfigPath) ||
                (File.GetAttributes(_officialConfigPath) &
                    FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Не найден config.yml штатного контроллера ЛМ: " +
                    _officialConfigPath + ".");
            byte[] raw = File.ReadAllBytes(_officialConfigPath);
            bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB &&
                raw[2] == 0xBF;
            string source = StrictUtf8.GetString(
                raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
            string newline;
            string[] lines = SplitLines(source, out newline);
            Dictionary<string, int> targets = FindTargets(lines, false);
            if (!targets.ContainsKey("settings.lmConfig.url") ||
                !targets.ContainsKey("settings.lmConfig.port"))
                throw new InvalidDataException(
                    "В config.yml штатного контроллера нет settings.lmConfig.url/port.");
            return new OfficialConfig
            {
                Lines = lines,
                NewLine = newline,
                Bom = bom,
                UrlLine = targets["settings.lmConfig.url"],
                PortLine = targets["settings.lmConfig.port"]
            };
        }

        private void WriteOfficial(OfficialConfig config)
        {
            byte[] body = StrictUtf8.GetBytes(string.Join(config.NewLine, config.Lines));
            byte[] payload = body;
            if (config.Bom)
            {
                payload = new byte[body.Length + 3];
                payload[0] = 0xEF;
                payload[1] = 0xBB;
                payload[2] = 0xBF;
                Buffer.BlockCopy(body, 0, payload, 3, body.Length);
            }
            _writer.WriteBytes(_officialConfigPath, payload);
        }

        private static bool SameUrl(string current, string expected)
        {
            string value = (current ?? string.Empty).Trim().Trim('\'', '"').TrimEnd('/');
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        public string PrepareClone(DirectControllerManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            if (manifest.Ordinal < 2)
            {
                throw new InvalidOperationException(
                    "The official base controller profile is not a clone profile.");
            }
            string environmentRoot = _manifests.EnsureProfileEnvironmentRoot(
                manifest.Ordinal);
            if (!string.Equals(
                    Path.GetFullPath(manifest.ProfileEnvironmentRoot),
                    environmentRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The direct controller manifest references another profile root.");
            }
            string vendorRoot = Path.Combine(
                environmentRoot,
                _profile.VendorProfileRelativePath);
            EnsureProtectedDirectory(vendorRoot, environmentRoot);
            string logRoot = Path.Combine(vendorRoot, "log");
            EnsureProtectedDirectory(logRoot, environmentRoot);

            _caStager.Stage(vendorRoot);

            string configPath = Path.Combine(vendorRoot, _profile.VendorConfigFileName);
            string content = File.Exists(configPath)
                ? PatchExisting(File.ReadAllText(configPath, StrictUtf8), manifest)
                : BuildNew(manifest, logRoot);
            _writer.WriteUtf8(configPath, content);
            ValidationResult validation = _pathSafety.ValidateProtected(
                configPath,
                environmentRoot,
                null);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
            return configPath;
        }

        private void EnsureProtectedDirectory(string path, string root)
        {
            _pathSafety.EnsureProtectedDirectory(
                path,
                ProtectedDirectoryKind.Operations,
                null,
                null);
            ValidationResult validation = _pathSafety.ValidateProtected(path, root, null);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
        }

        private static string BuildNew(
            DirectControllerManifest manifest,
            string logRoot)
        {
            string newline = Environment.NewLine;
            StringBuilder yaml = new StringBuilder();
            yaml.Append("settings:").Append(newline);
            yaml.Append("    logs:").Append(newline);
            yaml.Append("        dir: ").Append(Quote(logRoot)).Append(newline);
            yaml.Append("        debugInfo: true").Append(newline);
            yaml.Append("    common:").Append(newline);
            yaml.Append("        gRPCPort: ").Append(manifest.GrpcPort).Append(newline);
            yaml.Append("        RESTPort: ").Append(manifest.RestPort).Append(newline);
            yaml.Append("        gRPCSecured: true").Append(newline);
            yaml.Append("        timeout:").Append(newline);
            yaml.Append("            upd: 60").Append(newline);
            yaml.Append("            updPolling: 1").Append(newline);
            yaml.Append("            updCount: 10").Append(newline);
            yaml.Append("    lmConfig:").Append(newline);
            yaml.Append("        url: 'http://")
                .Append(LocalModuleTargetAddress.Describe(
                    manifest.TargetLocalModuleAddress))
                .Append("'").Append(newline);
            yaml.Append("        port: ").Append(manifest.TargetLocalModulePort).Append(newline);
            yaml.Append("        version: not defined").Append(newline);
            yaml.Append("        dbVersion: ''").Append(newline);
            yaml.Append("    certificate:").Append(newline);
            yaml.Append("        hosts: []").Append(newline);
            yaml.Append("    connection:").Append(newline);
            yaml.Append("        pingServers:").Append(newline);
            yaml.Append("            - ya.ru").Append(newline);
            yaml.Append("            - google.com").Append(newline);
            yaml.Append("        inetstatus: {}").Append(newline);
            yaml.Append("        lmstatus: {}").Append(newline);
            return yaml.ToString();
        }

        private static string PatchExisting(
            string source,
            DirectControllerManifest manifest)
        {
            string newline;
            string[] lines = SplitLines(source, out newline);
            Dictionary<string, int> targets = FindTargets(lines, true);
            string[] required =
            {
                "settings.common.gRPCPort",
                "settings.common.RESTPort",
                "settings.lmConfig.url",
                "settings.lmConfig.port"
            };
            for (int index = 0; index < required.Length; index++)
            {
                if (!targets.ContainsKey(required[index]))
                {
                    throw new InvalidDataException(
                        "Direct controller configuration schema discriminator is unsupported.");
                }
            }
            Replace(lines, targets[required[0]], "gRPCPort", manifest.GrpcPort.ToString());
            Replace(lines, targets[required[1]], "RESTPort", manifest.RestPort.ToString());
            Replace(lines, targets[required[2]], "url",
                "'http://" + LocalModuleTargetAddress.Describe(
                    manifest.TargetLocalModuleAddress) + "'");
            Replace(lines, targets[required[3]], "port", manifest.TargetLocalModulePort.ToString());
            return string.Join(newline, lines);
        }

        private static string[] SplitLines(string source, out string newline)
        {
            if (string.IsNullOrWhiteSpace(source) || source.IndexOf('\0') >= 0 ||
                source.IndexOf('\t') >= 0)
            {
                throw new InvalidDataException(
                    "Direct controller configuration is empty or ambiguously indented.");
            }
            newline = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0
                ? "\r\n"
                : "\n";
            string normalized = source.Replace("\r\n", "\n");
            if (normalized.IndexOf('\r') >= 0)
            {
                throw new InvalidDataException(
                    "Direct controller configuration line endings are unsupported.");
            }
            return normalized.Split(new[] { '\n' }, StringSplitOptions.None);
        }

        // strictIndent: клон пишет программа (отступ 4); файл вендора
        // принимается с любым согласованным отступом из пробелов.
        private static Dictionary<string, int> FindTargets(
            string[] lines,
            bool strictIndent)
        {
            Dictionary<string, int> targets = new Dictionary<string, int>(StringComparer.Ordinal);
            List<YamlParent> parents = new List<YamlParent>();
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                int indent = LeadingSpaces(line);
                string trimmed = line.Substring(indent);
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if ((strictIndent && indent % 4 != 0) || trimmed.IndexOf('&') >= 0 ||
                    trimmed.IndexOf('*') >= 0 || trimmed.StartsWith("<<:", StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Direct controller configuration contains unsupported YAML constructs.");
                }
                while (parents.Count > 0 && parents[parents.Count - 1].Indent >= indent)
                {
                    parents.RemoveAt(parents.Count - 1);
                }
                int colon = trimmed.IndexOf(':');
                if (colon <= 0 || trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    continue;
                }
                string key = trimmed.Substring(0, colon).Trim();
                string value = trimmed.Substring(colon + 1).Trim();
                string path = BuildPath(parents, key);
                if (IsTarget(path))
                {
                    if (targets.ContainsKey(path))
                    {
                        throw new InvalidDataException(
                            "Direct controller configuration contains duplicate target fields.");
                    }
                    targets.Add(path, index);
                }
                if (value.Length == 0)
                {
                    parents.Add(new YamlParent { Indent = indent, Key = key });
                }
            }
            return targets;
        }

        private static void Replace(string[] lines, int index, string key, string value)
        {
            int indent = LeadingSpaces(lines[index]);
            lines[index] = new string(' ', indent) + key + ": " + value;
        }

        private static bool IsTarget(string path)
        {
            return path == "settings.common.gRPCPort" ||
                path == "settings.common.RESTPort" ||
                path == "settings.lmConfig.url" ||
                path == "settings.lmConfig.port";
        }

        private static string BuildPath(IList<YamlParent> parents, string key)
        {
            StringBuilder result = new StringBuilder();
            for (int index = 0; index < parents.Count; index++)
            {
                if (result.Length > 0) result.Append('.');
                result.Append(parents[index].Key);
            }
            if (result.Length > 0) result.Append('.');
            result.Append(key);
            return result.ToString();
        }

        private static int LeadingSpaces(string value)
        {
            int count = 0;
            while (count < value.Length && value[count] == ' ') count++;
            return count;
        }

        private static string Quote(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private sealed class OfficialConfig
        {
            internal string[] Lines { get; set; }
            internal string NewLine { get; set; }
            internal bool Bom { get; set; }
            internal int UrlLine { get; set; }
            internal int PortLine { get; set; }

            // Значение скаляра без хвостового комментария.
            internal string Value(int index)
            {
                string line = Lines[index];
                string value = line.Substring(line.IndexOf(':') + 1);
                int comment = value.IndexOf(" #", StringComparison.Ordinal);
                if (comment >= 0) value = value.Substring(0, comment);
                return value.Trim();
            }
        }

        private sealed class YamlParent
        {
            internal int Indent { get; set; }
            internal string Key { get; set; }
        }
    }
}
