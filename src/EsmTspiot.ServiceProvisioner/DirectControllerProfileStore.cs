using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IDirectControllerProfileStore
    {
        string PrepareClone(DirectControllerManifest manifest);
    }

    internal sealed class DirectControllerProfileStore : IDirectControllerProfileStore
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly ControllerCapabilityProfile _profile;
        private readonly DirectControllerManifestStore _manifests;
        private readonly DirectControllerCaStager _caStager;
        private readonly AtomicFileWriter _writer;
        private readonly IPathSafety _pathSafety;

        internal DirectControllerProfileStore(
            ControllerCapabilityProfile profile,
            DirectControllerManifestStore manifests,
            DirectControllerCaStager caStager,
            AtomicFileWriter writer,
            IPathSafety pathSafety)
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
            yaml.Append("        url: 'http://127.0.0.1'").Append(newline);
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
            if (string.IsNullOrWhiteSpace(source) || source.IndexOf('\0') >= 0 ||
                source.IndexOf('\t') >= 0)
            {
                throw new InvalidDataException(
                    "Direct controller configuration is empty or ambiguously indented.");
            }
            string newline = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0
                ? "\r\n"
                : "\n";
            string normalized = source.Replace("\r\n", "\n");
            if (normalized.IndexOf('\r') >= 0)
            {
                throw new InvalidDataException(
                    "Direct controller configuration line endings are unsupported.");
            }
            string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
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
                if (indent % 4 != 0 || trimmed.IndexOf('&') >= 0 ||
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
            Replace(lines, targets[required[2]], "url", "'http://127.0.0.1'");
            Replace(lines, targets[required[3]], "port", manifest.TargetLocalModulePort.ToString());
            return string.Join(newline, lines);
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

        private sealed class YamlParent
        {
            internal int Indent { get; set; }
            internal string Key { get; set; }
        }
    }
}
