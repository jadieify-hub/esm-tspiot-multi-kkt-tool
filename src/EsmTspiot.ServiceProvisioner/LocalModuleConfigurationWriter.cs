using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class LocalModuleConfigurationWriter
    {
        internal static LocalModuleConfiguration Build(
            LocalModuleCapabilityProfile capability,
            string runtimeRoot,
            string profileRoot,
            ManagedLocalModuleProvisioningItemRequest item,
            string ownershipNonce,
            LocalModuleTemplateObservation templates)
        {
            if (capability == null) throw new ArgumentNullException("capability");
            ValidateItem(capability, item);
            ValidateNonce(ownershipNonce);
            ValidateTemplates(templates);

            string fullRuntimeRoot = NormalizeRoot(runtimeRoot, "runtimeRoot");
            string fullProfileRoot = NormalizeRoot(profileRoot, "profileRoot");
            if (IsSameOrUnder(fullProfileRoot, fullRuntimeRoot) ||
                IsSameOrUnder(fullRuntimeRoot, fullProfileRoot))
            {
                throw new InvalidDataException(
                    "Mutable profile and shared runtime roots must be separate.");
            }
            string configRoot = Path.Combine(fullProfileRoot, "config");
            string dataRoot = Path.Combine(fullProfileRoot, "data");
            string logsRoot = Path.Combine(fullProfileRoot, "logs");

            LocalModuleConfiguration result = new LocalModuleConfiguration
            {
                ProfileRoot = fullProfileRoot,
                ConfigRoot = configRoot,
                DataRoot = dataRoot,
                LogsRoot = logsRoot,
                RegimeLocalIniPath = Path.Combine(configRoot, "regime-local.ini"),
                YeniseiLocalIniPath = Path.Combine(configRoot, "yenisei-local.ini"),
                RegimeVmArgsPath = Path.Combine(configRoot, "regime-vm.args"),
                YeniseiVmArgsPath = Path.Combine(configRoot, "yenisei-vm.args"),
                RegimeSysConfigPath = Path.Combine(configRoot, "regime-sys.config"),
                YeniseiSysConfigPath = Path.Combine(configRoot, "yenisei-sys.config")
            };

            string generatedCookie = CreatePairCookie(ownershipNonce, item.Inn);
            result.RegimeLocalIni = BuildRegimeLocalIni(item, dataRoot);
            result.YeniseiLocalIni = BuildYeniseiLocalIni(item, dataRoot, logsRoot);
            result.RegimeVmArgs = BuildVmArgs(
                "krs_lm_regime_n" + item.LocalModuleOrdinal.ToString("00", CultureInfo.InvariantCulture),
                generatedCookie);
            result.YeniseiVmArgs = BuildVmArgs(
                "krs_lm_yenisei_n" + item.LocalModuleOrdinal.ToString("00", CultureInfo.InvariantCulture),
                generatedCookie);
            result.RegimeSysConfig = BuildRegimeSysConfig(
                result.RegimeLocalIniPath,
                Path.Combine(logsRoot, "regime.log"));
            result.YeniseiSysConfig = BuildYeniseiSysConfig(
                fullRuntimeRoot,
                capability,
                result.YeniseiLocalIniPath,
                Path.Combine(configRoot, "yenisei-local.d"));

            result.DatabaseStartPlan = BuildStartPlan(
                capability,
                fullRuntimeRoot,
                item,
                LocalModuleProcessRole.Database,
                result.YeniseiVmArgsPath,
                result.YeniseiSysConfigPath);
            result.ApiStartPlan = BuildStartPlan(
                capability,
                fullRuntimeRoot,
                item,
                LocalModuleProcessRole.Api,
                result.RegimeVmArgsPath,
                result.RegimeSysConfigPath);
            return result;
        }

        private static void ValidateItem(
            LocalModuleCapabilityProfile capability,
            ManagedLocalModuleProvisioningItemRequest item)
        {
            if (item == null)
            {
                throw new InvalidDataException("Local-module plan item is missing.");
            }
            HashSet<int> ports = new HashSet<int>();
            if (!IsAsciiDigits(item.KktSerial, 14) ||
                (!IsAsciiDigits(item.Inn, 10) && !IsAsciiDigits(item.Inn, 12)) ||
                item.KktOrdinal < 1 || item.KktOrdinal > 32 ||
                item.LocalModuleOrdinal < 1 || item.LocalModuleOrdinal > 32 ||
                !IsPort(item.ApiPort) || !IsPort(item.DatabasePort) ||
                !IsPort(item.EpmdPort) || !IsPort(item.ControllerGrpcPort) ||
                !IsPort(item.ControllerRestPort) ||
                !ports.Add(item.ApiPort) ||
                !ports.Add(item.DatabasePort) ||
                !ports.Add(item.EpmdPort) ||
                !ports.Add(item.ControllerGrpcPort) ||
                !ports.Add(item.ControllerRestPort) ||
                !string.Equals(
                    item.RuntimeVersion,
                    capability.ProductVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Local-module plan item is invalid for this capability.");
            }
        }

        private static void ValidateNonce(string value)
        {
            if (value == null || value.Length != 32)
            {
                throw new InvalidDataException("Ownership nonce must contain 32 hexadecimal characters.");
            }
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                {
                    throw new InvalidDataException(
                        "Ownership nonce must contain lowercase hexadecimal characters.");
                }
            }
        }

        private static void ValidateTemplates(LocalModuleTemplateObservation templates)
        {
            if (templates == null)
            {
                throw new InvalidDataException("Local-module template observation is missing.");
            }
            ValidateIniContract(
                templates.RegimeLocalIni,
                new[]
                {
                    "api/ip_address",
                    "api/port",
                    "local/db_url",
                    "local/key_store_folder"
                },
                "API template");
            ValidateIniContract(
                templates.YeniseiLocalIni,
                new[]
                {
                    "chttpd/bind_address",
                    "chttpd/port",
                    "log/file",
                    "couchdb/database_dir",
                    "couchdb/view_index_dir"
                },
                "database template");
        }

        private static void ValidateIniContract(
            string text,
            string[] requiredKeys,
            string description)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidDataException(description + " is empty.");
            }
            Dictionary<string, int> occurrences =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string section = string.Empty;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }
                if (line[0] == ';')
                {
                    line = line.Substring(1).TrimStart();
                    if (line.IndexOf('=') < 0)
                    {
                        continue;
                    }
                }
                if (line.Length >= 3 && line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                int equals = line.IndexOf('=');
                if (equals <= 0 || section.Length == 0)
                {
                    continue;
                }
                string key = section + "/" + line.Substring(0, equals).Trim();
                int count;
                occurrences.TryGetValue(key, out count);
                occurrences[key] = count + 1;
            }

            for (int index = 0; index < requiredKeys.Length; index++)
            {
                int count;
                if (!occurrences.TryGetValue(requiredKeys[index], out count) || count != 1)
                {
                    throw new InvalidDataException(
                        description + " must contain exactly one " + requiredKeys[index] + ".");
                }
            }
        }

        private static string BuildRegimeLocalIni(
            ManagedLocalModuleProvisioningItemRequest item,
            string dataRoot)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("[api]");
            text.AppendLine("ip_address = 127.0.0.1");
            text.AppendLine("port = " + item.ApiPort.ToString(CultureInfo.InvariantCulture));
            text.AppendLine();
            text.AppendLine("[local]");
            text.AppendLine("db_url = http://127.0.0.1:" +
                item.DatabasePort.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("key_store_folder = " +
                ToPortablePath(Path.Combine(dataRoot, "key-store")));
            return text.ToString();
        }

        private static string BuildYeniseiLocalIni(
            ManagedLocalModuleProvisioningItemRequest item,
            string dataRoot,
            string logsRoot)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("[chttpd]");
            text.AppendLine("bind_address = 127.0.0.1");
            text.AppendLine("port = " + item.DatabasePort.ToString(CultureInfo.InvariantCulture));
            text.AppendLine();
            text.AppendLine("[log]");
            text.AppendLine("writer = file");
            text.AppendLine("level = notice");
            text.AppendLine("file = " + ToPortablePath(Path.Combine(logsRoot, "yenisei.log")));
            text.AppendLine("rotation_file_size = 104857600");
            text.AppendLine();
            text.AppendLine("[couchdb]");
            text.AppendLine("single_node = true");
            text.AppendLine("database_dir = " +
                ToPortablePath(Path.Combine(dataRoot, "database")));
            text.AppendLine("view_index_dir = " +
                ToPortablePath(Path.Combine(dataRoot, "index")));
            return text.ToString();
        }

        private static string BuildVmArgs(string nodePrefix, string generatedCookie)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("-name " + nodePrefix + "@127.0.0.1");
            text.AppendLine("-setcookie " + generatedCookie);
            text.AppendLine("-kernel error_logger silent");
            text.AppendLine("-kernel inet_dist_use_interface {127,0,0,1}");
            text.AppendLine("-kernel prevent_overlapping_partitions false");
            text.AppendLine("-sasl sasl_error_logger false");
            text.AppendLine("+K true");
            text.AppendLine("+A 16");
            text.AppendLine("+Bd -noinput");
            text.AppendLine("+SDio 16");
            text.AppendLine("-smp enable");
            text.AppendLine("-ssl session_lifetime 300");
            return text.ToString();
        }

        private static string BuildRegimeSysConfig(string localIniPath, string logPath)
        {
            string localIni = QuoteErlangPath(localIniPath);
            string log = QuoteErlangPath(logPath);
            return "[\r\n" +
                "    {kernel, [{logger_level, notice}, {logger, [" +
                "{handler, default, logger_std_h, #{formatter => {logger_formatter, " +
                "#{time_offset => \"Z\", template => [time, \" \", level, \" \", pid, \" : \", msg, \"\\n\"]}}, " +
                "config => #{file => " + log + ", max_no_bytes => 104857600, " +
                "max_no_files => 3, compress_on_rotate => true}}}] }]},\r\n" +
                "    {config, [{ini_files, [" + localIni + "]}]}\r\n" +
                "].\r\n";
        }

        private static string BuildYeniseiSysConfig(
            string runtimeRoot,
            LocalModuleCapabilityProfile capability,
            string localIniPath,
            string localDirectory)
        {
            string defaultIni = QuoteErlangPath(Path.Combine(
                runtimeRoot,
                capability.DatabaseDefaultIniRelativePath));
            string defaultDirectory = QuoteErlangPath(Path.Combine(
                runtimeRoot,
                @"yenisei\etc\default.d"));
            return "[\r\n" +
                "    {config, [{ini_files, [" + defaultIni + ", " +
                defaultDirectory + ", " + QuoteErlangPath(localIniPath) + ", " +
                QuoteErlangPath(localDirectory) + "]}]}\r\n" +
                "].\r\n";
        }

        private static ErlangChildStartPlan BuildStartPlan(
            LocalModuleCapabilityProfile capability,
            string runtimeRoot,
            ManagedLocalModuleProvisioningItemRequest item,
            LocalModuleProcessRole role,
            string vmArgsPath,
            string sysConfigPath)
        {
            string executable = Path.Combine(runtimeRoot, capability.ErlExecutableRelativePath);
            string epmd = Path.Combine(runtimeRoot, capability.EpmdExecutableRelativePath);
            string boot = Path.Combine(
                runtimeRoot,
                role == LocalModuleProcessRole.Api
                    ? capability.ApiBootRelativePath
                    : capability.DatabaseBootRelativePath);
            IList<string> tokens = new List<string>
            {
                "-boot",
                boot,
                "-args_file",
                vmArgsPath,
                "-epmd",
                epmd,
                "-config",
                sysConfigPath
            };
            Dictionary<string, string> environment = BuildEnvironment(
                runtimeRoot,
                capability,
                item.EpmdPort);
            if (role == LocalModuleProcessRole.Database)
            {
                environment["YENISEI_QUERY_SERVER_JAVASCRIPT"] =
                    QuoteQueryPath(Path.Combine(runtimeRoot, @"bin\couchjs.exe")) + " " +
                    QuoteQueryPath(Path.Combine(runtimeRoot, @"yenisei\share\server\main.js"));
                environment["YENISEI_QUERY_SERVER_COFFEESCRIPT"] =
                    QuoteQueryPath(Path.Combine(runtimeRoot, @"bin\couchjs.exe")) + " " +
                    QuoteQueryPath(Path.Combine(runtimeRoot, @"yenisei\share\server\main-coffee.js"));
            }
            return new ErlangChildStartPlan(
                role,
                executable,
                runtimeRoot,
                tokens,
                environment);
        }

        private static Dictionary<string, string> BuildEnvironment(
            string runtimeRoot,
            LocalModuleCapabilityProfile capability,
            int epmdPort)
        {
            string windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string system32 = Path.Combine(windowsRoot, "System32");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ERL_LIBS", Path.Combine(runtimeRoot, "lib") },
                { "ERL_EPMD_PORT", epmdPort.ToString(CultureInfo.InvariantCulture) },
                { "ERL_EPMD_ADDRESS", "127.0.0.1" },
                { "PATH", string.Join(";", new[]
                    {
                        Path.Combine(runtimeRoot, "bin"),
                        Path.Combine(runtimeRoot, "erts-" + capability.ErtsVersion, "bin"),
                        system32,
                        windowsRoot,
                        Path.Combine(system32, "Wbem")
                    }) }
            };
        }

        private static string CreatePairCookie(string ownershipNonce, string inn)
        {
            byte[] source = Encoding.UTF8.GetBytes(
                "multikkt-lm-cookie-v1\n" + ownershipNonce + "\n" + inn);
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(source);
                StringBuilder text = new StringBuilder(32);
                for (int index = 0; index < 16; index++)
                {
                    text.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                return "krs_lm_" + text.ToString();
            }
        }

        private static string NormalizeRoot(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
            {
                throw new ArgumentException("An absolute path is required.", parameterName);
            }
            string fullPath = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
            EnsureSafePathText(fullPath);
            return fullPath;
        }

        private static bool IsSameOrUnder(string path, string root)
        {
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string QuoteErlangPath(string path)
        {
            return "\"" + ToPortablePath(path) + "\"";
        }

        private static string QuoteQueryPath(string path)
        {
            return "\"" + ToPortablePath(path) + "\"";
        }

        private static string ToPortablePath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            EnsureSafePathText(fullPath);
            return fullPath.Replace('\\', '/');
        }

        private static void EnsureSafePathText(string value)
        {
            if (value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
            {
                throw new InvalidDataException("Generated path contains unsupported characters.");
            }
        }

        private static bool IsAsciiDigits(string value, int length)
        {
            if (value == null || value.Length != length)
            {
                return false;
            }
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsPort(int value)
        {
            return value >= 1 && value <= 65535;
        }
    }
}
