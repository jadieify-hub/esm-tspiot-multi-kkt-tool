using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class OfficialLmProfileAdapter
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly ControllerCapabilityProfile _profile;
        private readonly ManagedServiceManifestStore _manifestStore;
        private readonly AtomicFileWriter _writer;
        private readonly IWindowsServiceApi _serviceApi;

        internal OfficialLmProfileAdapter(
            ControllerCapabilityProfile profile,
            ManagedServiceManifestStore manifestStore,
            AtomicFileWriter writer,
            IWindowsServiceApi serviceApi)
        {
            _profile = profile ?? throw new ArgumentNullException("profile");
            _manifestStore = manifestStore ?? throw new ArgumentNullException("manifestStore");
            _writer = writer ?? throw new ArgumentNullException("writer");
            _serviceApi = serviceApi ?? throw new ArgumentNullException("serviceApi");
            if (!string.Equals(_profile.Version, "1.6.3.2", StringComparison.Ordinal) ||
                !string.Equals(_profile.ProfileEnvironmentKey, "ProgramData", StringComparison.Ordinal) ||
                !string.Equals(
                    _profile.VendorProfileRelativePath,
                    Path.Combine("ESP", "lmcontroller"),
                    StringComparison.Ordinal) ||
                !string.Equals(_profile.VendorConfigFileName, "config.yml", StringComparison.Ordinal))
            {
                throw new NotSupportedException("The controller profile schema is not characterized.");
            }
        }

        internal string PrepareEmptyProfile(ManagedLmServiceSpec spec, string serviceSid)
        {
            ValidateSpec(spec);
            EnsureServiceStoppedOrAbsent(spec.ServiceName);
            string profileRoot = _manifestStore.EnsureProfileRoot(spec.KktSerial, serviceSid);
            _manifestStore.EnsureProfileContentDirectory(
                spec.KktSerial,
                serviceSid,
                "ESP",
                "lmcontroller");
            return profileRoot;
        }

        internal LmProfileConfiguration CreateOrLoadConfiguration(
            ManagedLmServiceSpec spec,
            string serviceSid)
        {
            ValidateSpec(spec);
            PrepareEmptyProfile(spec, serviceSid);
            string path = GetConfigurationPath(spec.KktSerial);
            _manifestStore.ValidateProfileContentPath(spec.KktSerial, serviceSid, path);
            if (File.Exists(path))
            {
                return ReadConfiguration(spec.KktSerial, serviceSid);
            }

            LmProfileConfiguration configuration = LmProfileConfiguration.FromSpec(
                _profile.Version,
                spec);
            string content = BuildNewConfiguration(spec.KktSerial, configuration);
            ExactLmYamlDocument.Parse(content, _profile.Version);
            _writer.WriteUtf8(path, content);
            _manifestStore.ValidateProfileContentPath(spec.KktSerial, serviceSid, path);
            return configuration;
        }

        internal void ApplyConfiguration(ManagedLmServiceSpec spec, string serviceSid)
        {
            ValidateSpec(spec);
            EnsureServiceStoppedOrAbsent(spec.ServiceName);
            string path = GetConfigurationPath(spec.KktSerial);
            _manifestStore.ValidateProfileContentPath(spec.KktSerial, serviceSid, path);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Managed LM configuration was not created.", path);
            }
            string source = File.ReadAllText(path, StrictUtf8);
            ExactLmYamlDocument document = ExactLmYamlDocument.Parse(source, _profile.Version);
            LmProfileConfiguration desired = LmProfileConfiguration.FromSpec(_profile.Version, spec);
            string updated = document.Apply(desired);
            ExactLmYamlDocument.Parse(updated, _profile.Version);
            _writer.WriteUtf8(path, updated);
            _manifestStore.ValidateProfileContentPath(spec.KktSerial, serviceSid, path);
        }

        internal LmProfileConfiguration ReadConfiguration(string kktSerial, string serviceSid)
        {
            string path = GetConfigurationPath(kktSerial);
            _manifestStore.ValidateProfileContentPath(kktSerial, serviceSid, path);
            string source = File.ReadAllText(path, StrictUtf8);
            return ExactLmYamlDocument.Parse(source, _profile.Version).ToConfiguration();
        }

        internal string GetConfigurationPath(string kktSerial)
        {
            string profileRoot = _manifestStore.GetProfileRoot(kktSerial);
            return Path.Combine(
                profileRoot,
                _profile.VendorProfileRelativePath,
                _profile.VendorConfigFileName);
        }

        private string BuildNewConfiguration(
            string kktSerial,
            LmProfileConfiguration configuration)
        {
            string profileRoot = _manifestStore.GetProfileRoot(kktSerial);
            string logDirectory = Path.Combine(
                profileRoot,
                _profile.VendorProfileRelativePath,
                "log");
            string newline = Environment.NewLine;
            StringBuilder yaml = new StringBuilder();
            yaml.Append("settings:").Append(newline);
            yaml.Append("    logs:").Append(newline);
            yaml.Append("        dir: ").Append(QuoteYaml(logDirectory)).Append(newline);
            yaml.Append("        debugInfo: false").Append(newline);
            yaml.Append("    common:").Append(newline);
            yaml.Append("        gRPCPort: ").Append(configuration.GrpcPort).Append(newline);
            yaml.Append("        RESTPort: ").Append(configuration.RestPort).Append(newline);
            yaml.Append("        gRPCSecured: true").Append(newline);
            yaml.Append("        timeout:").Append(newline);
            yaml.Append("            upd: 60").Append(newline);
            yaml.Append("            updPolling: 1").Append(newline);
            yaml.Append("            updCount: 10").Append(newline);
            yaml.Append("    lmConfig:").Append(newline);
            yaml.Append("        url: ").Append(QuoteYaml(ToTargetUrl(configuration.TargetAddress))).Append(newline);
            yaml.Append("        port: ").Append(configuration.TargetPort).Append(newline);
            yaml.Append("        version: not defined").Append(newline);
            yaml.Append("        dbVersion: ''").Append(newline);
            yaml.Append("    certificate:").Append(newline);
            yaml.Append("        hosts: []").Append(newline);
            yaml.Append("    connection:").Append(newline);
            yaml.Append("        pingServers: []").Append(newline);
            yaml.Append("        inetstatus: {}").Append(newline);
            yaml.Append("        lmstatus: {}").Append(newline);
            return yaml.ToString();
        }

        private void EnsureServiceStoppedOrAbsent(string serviceName)
        {
            WindowsServiceRecord service = _serviceApi.Query(serviceName);
            if (service != null &&
                (service.State != WindowsServiceState.Stopped || service.ProcessId != 0))
            {
                throw new InvalidOperationException(
                    "LM profile mutation requires a confirmed stopped service and zero PID.");
            }
        }

        private static void ValidateSpec(ManagedLmServiceSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException("spec");
            }
            if (!string.Equals(
                spec.ServiceName,
                EsmTspiot.Shared.Services.LmServiceIdentity.CreateName(spec.KktSerial),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException("Managed LM spec identity is invalid.");
            }
            LmProfileConfiguration.FromSpec("1.6.3.2", spec);
        }

        private static string ToTargetUrl(string targetAddress)
        {
            return "http://" + (targetAddress.IndexOf(':') >= 0
                ? "[" + targetAddress + "]"
                : targetAddress);
        }

        private static string QuoteYaml(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private sealed class ExactLmYamlDocument
        {
            private readonly string[] _lines;
            private readonly string _newline;
            private readonly IDictionary<string, YamlEntry> _entries;
            private readonly string _controllerVersion;

            private ExactLmYamlDocument(
                string[] lines,
                string newline,
                IDictionary<string, YamlEntry> entries,
                string controllerVersion)
            {
                _lines = lines;
                _newline = newline;
                _entries = entries;
                _controllerVersion = controllerVersion;
            }

            internal static ExactLmYamlDocument Parse(string source, string controllerVersion)
            {
                if (source == null || source.Length == 0 || source.IndexOf('\0') >= 0)
                {
                    throw new InvalidDataException("LM configuration is empty or binary.");
                }
                string newline = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0
                    ? "\r\n"
                    : "\n";
                string normalized = source.Replace("\r\n", "\n");
                if (normalized.IndexOf('\r') >= 0 || normalized.IndexOf('\t') >= 0)
                {
                    throw new InvalidDataException("LM configuration indentation is ambiguous.");
                }
                string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
                Dictionary<string, YamlEntry> entries = new Dictionary<string, YamlEntry>(
                    StringComparer.Ordinal);
                List<YamlContainer> parents = new List<YamlContainer>();
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    int indent = CountLeadingSpaces(line);
                    string trimmed = line.Substring(indent);
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (indent % 4 != 0)
                    {
                        throw new InvalidDataException("LM configuration indentation is unsupported.");
                    }
                    while (parents.Count > 0 && parents[parents.Count - 1].Indent >= indent)
                    {
                        parents.RemoveAt(parents.Count - 1);
                    }
                    int expectedIndent = parents.Count == 0
                        ? 0
                        : parents[parents.Count - 1].Indent + 4;
                    if (indent != expectedIndent)
                    {
                        throw new InvalidDataException("LM configuration nesting is not exact.");
                    }
                    if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                    {
                        if (parents.Count == 0 || indent <= parents[parents.Count - 1].Indent)
                        {
                            throw new InvalidDataException("LM configuration sequence is ambiguous.");
                        }
                        continue;
                    }

                    int colon = trimmed.IndexOf(':');
                    if (colon <= 0)
                    {
                        throw new InvalidDataException("LM configuration contains a non-mapping line.");
                    }
                    string key = trimmed.Substring(0, colon).Trim();
                    if (!IsSafeKey(key))
                    {
                        throw new InvalidDataException("LM configuration contains an unsafe key.");
                    }
                    string path = BuildPath(parents, key);
                    if (entries.ContainsKey(path))
                    {
                        throw new InvalidDataException("LM configuration contains a duplicate path: " + path + ".");
                    }
                    string scalar = trimmed.Substring(colon + 1).Trim();
                    if (scalar.StartsWith("&", StringComparison.Ordinal) ||
                        scalar.StartsWith("*", StringComparison.Ordinal) ||
                        scalar.StartsWith("!", StringComparison.Ordinal) ||
                        scalar == "|" || scalar == ">")
                    {
                        throw new InvalidDataException("LM configuration YAML extensions are unsupported.");
                    }
                    YamlEntry entry = new YamlEntry
                    {
                        Path = path,
                        Key = key,
                        LineIndex = lineIndex,
                        Indent = indent,
                        Scalar = scalar,
                        IsContainer = scalar.Length == 0
                    };
                    entries.Add(path, entry);
                    if (entry.IsContainer)
                    {
                        parents.Add(new YamlContainer { Indent = indent, Key = key });
                    }
                    else if (ContainsSensitivePathFragment(path))
                    {
                        throw new InvalidDataException("LM configuration contains a forbidden secret field.");
                    }
                }

                ExactLmYamlDocument document = new ExactLmYamlDocument(
                    lines,
                    newline,
                    entries,
                    controllerVersion);
                document.ValidateSchema();
                return document;
            }

            internal LmProfileConfiguration ToConfiguration()
            {
                int grpc = ParsePort("settings.common.gRPCPort");
                int rest = ParsePort("settings.common.RESTPort");
                int targetPort = ParsePort("settings.lmConfig.port");
                string address = ParseTargetAddress(GetScalar("settings.lmConfig.url"));
                return new LmProfileConfiguration(
                    _controllerVersion,
                    grpc,
                    rest,
                    address,
                    targetPort);
            }

            internal string Apply(LmProfileConfiguration configuration)
            {
                if (!string.Equals(
                    configuration.ControllerVersion,
                    _controllerVersion,
                    StringComparison.Ordinal))
                {
                    throw new NotSupportedException("LM profile version does not match its adapter.");
                }
                string[] updated = (string[])_lines.Clone();
                Replace(updated, "settings.common.gRPCPort", configuration.GrpcPort.ToString(CultureInfo.InvariantCulture));
                Replace(updated, "settings.common.RESTPort", configuration.RestPort.ToString(CultureInfo.InvariantCulture));
                Replace(updated, "settings.lmConfig.url", QuoteYaml(ToTargetUrl(configuration.TargetAddress)));
                Replace(updated, "settings.lmConfig.port", configuration.TargetPort.ToString(CultureInfo.InvariantCulture));
                return string.Join(_newline, updated);
            }

            private void ValidateSchema()
            {
                RequireContainer("settings");
                RequireContainer("settings.logs");
                RequireScalar("settings.logs.dir");
                RequireBoolean("settings.logs.debugInfo");
                RequireContainer("settings.common");
                ParsePort("settings.common.gRPCPort");
                ParsePort("settings.common.RESTPort");
                if (!ParseBoolean("settings.common.gRPCSecured"))
                {
                    throw new InvalidDataException("The exact LM schema requires secured local gRPC.");
                }
                RequireContainer("settings.common.timeout");
                RequirePositiveInteger("settings.common.timeout.upd");
                RequirePositiveInteger("settings.common.timeout.updPolling");
                RequirePositiveInteger("settings.common.timeout.updCount");
                RequireContainer("settings.lmConfig");
                ParseTargetAddress(GetScalar("settings.lmConfig.url"));
                ParsePort("settings.lmConfig.port");
                if (!string.Equals(
                    Unquote(GetScalar("settings.lmConfig.version")),
                    "not defined",
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException("LM configuration schema discriminator is unsupported.");
                }
                RequireScalar("settings.lmConfig.dbVersion");
                RequireContainer("settings.certificate");
                RequireScalar("settings.certificate.hosts");
                RequireContainer("settings.connection");
                RequirePath("settings.connection.pingServers");
                RequireScalar("settings.connection.inetstatus");
                RequireScalar("settings.connection.lmstatus");
            }

            private int ParsePort(string path)
            {
                int value;
                if (!int.TryParse(
                    Unquote(GetScalar(path)),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out value) || value < 1 || value > 65535)
                {
                    throw new InvalidDataException("LM configuration port is invalid at " + path + ".");
                }
                return value;
            }

            private void RequirePositiveInteger(string path)
            {
                int value;
                if (!int.TryParse(
                    Unquote(GetScalar(path)),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out value) || value <= 0)
                {
                    throw new InvalidDataException("LM configuration integer is invalid at " + path + ".");
                }
            }

            private void RequireBoolean(string path)
            {
                ParseBoolean(path);
            }

            private bool ParseBoolean(string path)
            {
                string value = Unquote(GetScalar(path));
                if (string.Equals(value, "true", StringComparison.Ordinal))
                {
                    return true;
                }
                if (string.Equals(value, "false", StringComparison.Ordinal))
                {
                    return false;
                }
                throw new InvalidDataException("LM configuration boolean is invalid at " + path + ".");
            }

            private string GetScalar(string path)
            {
                YamlEntry entry;
                if (!_entries.TryGetValue(path, out entry) || entry.IsContainer)
                {
                    throw new InvalidDataException("LM configuration scalar is missing at " + path + ".");
                }
                return entry.Scalar;
            }

            private void RequireScalar(string path)
            {
                GetScalar(path);
            }

            private void RequireContainer(string path)
            {
                YamlEntry entry;
                if (!_entries.TryGetValue(path, out entry) || !entry.IsContainer)
                {
                    throw new InvalidDataException("LM configuration object is missing at " + path + ".");
                }
            }

            private void RequirePath(string path)
            {
                if (!_entries.ContainsKey(path))
                {
                    throw new InvalidDataException("LM configuration path is missing at " + path + ".");
                }
            }

            private void Replace(string[] lines, string path, string scalar)
            {
                YamlEntry entry = _entries[path];
                lines[entry.LineIndex] = new string(' ', entry.Indent) + entry.Key + ": " + scalar;
            }

            private static string ParseTargetAddress(string scalar)
            {
                string value = Unquote(scalar);
                const string prefix = "http://";
                if (!value.StartsWith(prefix, StringComparison.Ordinal) ||
                    value.Length == prefix.Length)
                {
                    throw new InvalidDataException("LM target URL must use the exact http scheme.");
                }
                string host = value.Substring(prefix.Length);
                if (host.IndexOf('/') >= 0 || host.IndexOf('?') >= 0 ||
                    host.IndexOf('#') >= 0 || host.IndexOf('@') >= 0)
                {
                    throw new InvalidDataException("LM target URL must contain only a host.");
                }
                if (host.Length > 2 && host[0] == '[' && host[host.Length - 1] == ']')
                {
                    host = host.Substring(1, host.Length - 2);
                }
                else if (host.IndexOf(':') >= 0)
                {
                    throw new InvalidDataException("An IPv6 LM target must be bracketed in the URL.");
                }
                string normalized;
                bool isLoopback;
                if (!LmGatewayInputValidator.TryNormalizeTargetAddress(
                    host,
                    out normalized,
                    out isLoopback))
                {
                    throw new InvalidDataException("LM target host is invalid.");
                }
                return normalized;
            }

            private static string Unquote(string value)
            {
                if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                {
                    return value.Substring(1, value.Length - 2).Replace("''", "'");
                }
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                {
                    return value.Substring(1, value.Length - 2)
                        .Replace("\\\"", "\"")
                        .Replace("\\\\", "\\");
                }
                return value;
            }

            private static int CountLeadingSpaces(string value)
            {
                int count = 0;
                while (count < value.Length && value[count] == ' ')
                {
                    count++;
                }
                return count;
            }

            private static bool IsSafeKey(string key)
            {
                if (key.Length == 0)
                {
                    return false;
                }
                for (int index = 0; index < key.Length; index++)
                {
                    char value = key[index];
                    bool valid = (value >= 'a' && value <= 'z') ||
                        (value >= 'A' && value <= 'Z') ||
                        (value >= '0' && value <= '9') ||
                        value == '_' || value == '-';
                    if (!valid)
                    {
                        return false;
                    }
                }
                return true;
            }

            private static string BuildPath(IList<YamlContainer> parents, string key)
            {
                StringBuilder result = new StringBuilder();
                for (int index = 0; index < parents.Count; index++)
                {
                    if (result.Length > 0)
                    {
                        result.Append('.');
                    }
                    result.Append(parents[index].Key);
                }
                if (result.Length > 0)
                {
                    result.Append('.');
                }
                result.Append(key);
                return result.ToString();
            }

            private static bool ContainsSensitivePathFragment(string path)
            {
                string lower = path.ToLowerInvariant();
                return lower.IndexOf("password", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("credential", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("privatekey", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("private_key", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("secret", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("token", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("apikey", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("api_key", StringComparison.Ordinal) >= 0;
            }

            private sealed class YamlContainer
            {
                internal int Indent { get; set; }
                internal string Key { get; set; }
            }

            private sealed class YamlEntry
            {
                internal string Path { get; set; }
                internal string Key { get; set; }
                internal int LineIndex { get; set; }
                internal int Indent { get; set; }
                internal string Scalar { get; set; }
                internal bool IsContainer { get; set; }
            }
        }
    }
}
