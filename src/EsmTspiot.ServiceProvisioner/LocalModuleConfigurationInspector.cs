using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleConfigurationObservation
    {
        internal string ApiBindAddress { get; set; }
        internal int ApiPort { get; set; }
        internal string DatabaseBindAddress { get; set; }
        internal int DatabasePort { get; set; }
        internal string ApiNodeName { get; set; }
        internal string DatabaseNodeName { get; set; }
        internal string CookieDigest { get; set; }
    }

    internal sealed class LocalModuleConfigurationInspector
    {
        internal LocalModuleConfigurationObservation Inspect(
            LocalModuleInstalledLayout layout)
        {
            if (layout == null) throw new ArgumentNullException("layout");
            IDictionary<string, string> api = ReadIni(
                layout.ApiLocalIniPath,
                "api.ip_address",
                "api.port",
                "api.login",
                "api.password",
                "local.db_url");
            IDictionary<string, string> database =
                ReadIni(
                    layout.DatabaseLocalIniPath,
                    "chttpd.bind_address",
                    "chttpd.port");
            IDictionary<string, string> erl = ReadIni(
                layout.ErlIniPath,
                "erlang.Bindir",
                "erlang.Rootdir");
            VmArgs apiVm = ReadVmArgs(layout.ApiVmArgsPath);
            VmArgs databaseVm = ReadVmArgs(layout.DatabaseVmArgsPath);
            RequireApiCredentials(api);

            string apiAddress = Required(api, "api.ip_address");
            int apiPort = RequiredPort(api, "api.port");
            string databaseAddress = Required(database, "chttpd.bind_address");
            int databasePort = RequiredPort(database, "chttpd.port");
            if (!string.Equals(apiAddress, "0.0.0.0", StringComparison.Ordinal) ||
                apiPort != layout.ApiPort ||
                !string.Equals(
                    Required(api, "local.db_url"),
                    "http://127.0.0.1:" + layout.DatabasePort.ToString(
                        CultureInfo.InvariantCulture),
                    StringComparison.Ordinal) ||
                !IsExpectedDatabaseBind(layout, databaseAddress) ||
                databasePort != layout.DatabasePort ||
                !string.Equals(
                    apiVm.NodeName,
                    layout.ApiNodeName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    databaseVm.NodeName,
                    layout.DatabaseNodeName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    apiVm.Cookie,
                    databaseVm.Cookie,
                    StringComparison.Ordinal) ||
                !IsCookie(apiVm.Cookie) ||
                !ErlPathsEqual(
                    Required(erl, "erlang.Bindir"),
                    layout.ErtsBinPath) ||
                !ErlPathsEqual(
                    Required(erl, "erlang.Rootdir"),
                    layout.InstallRoot))
                throw new InvalidDataException(
                    "Installed local-module configuration does not match its plan.");

            return new LocalModuleConfigurationObservation
            {
                ApiBindAddress = apiAddress,
                ApiPort = apiPort,
                DatabaseBindAddress = databaseAddress,
                DatabasePort = databasePort,
                ApiNodeName = apiVm.NodeName,
                DatabaseNodeName = databaseVm.NodeName,
                CookieDigest = Sha256(apiVm.Cookie)
            };
        }

        private static void RequireApiCredentials(
            IDictionary<string, string> api)
        {
            // The vendor installer writes [api] login/password from the
            // ADMINUSER/ADMINPASSWORD properties; values are never logged.
            string login;
            string password;
            if (!api.TryGetValue("api.login", out login) ||
                login.Length == 0 ||
                !api.TryGetValue("api.password", out password) ||
                password.Length == 0)
                throw new InvalidDataException(
                    "Installed local-module API credentials are missing: " +
                    "the installer did not write [api] login and password.");
        }

        private static IDictionary<string, string> ReadIni(
            string path,
            params string[] characterizedKeys)
        {
            Dictionary<string, string> result =
                new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> characterized = new HashSet<string>(
                characterizedKeys,
                StringComparer.Ordinal);
            string section = string.Empty;
            string[] lines = File.ReadAllLines(path);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                    continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2);
                    if (section.Length == 0 || section.IndexOfAny(
                            new[] { '[', ']' }) >= 0)
                        throw new InvalidDataException(
                            "Installed local-module INI section is invalid.");
                    continue;
                }
                int equals = line.IndexOf('=');
                if (equals <= 0 || section.Length == 0)
                    throw new InvalidDataException(
                        "Installed local-module INI line is invalid.");
                string key = section + "." +
                    line.Substring(0, equals).Trim();
                if (!characterized.Contains(key)) continue;
                if (result.ContainsKey(key))
                    throw new InvalidDataException(
                        "Installed local-module INI key is duplicated.");
                result.Add(key, line.Substring(equals + 1).Trim());
            }
            return result;
        }

        private static VmArgs ReadVmArgs(string path)
        {
            string node = null;
            string cookie = null;
            string[] lines = File.ReadAllLines(path);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == '%')
                    continue;
                if (line.StartsWith("-name ", StringComparison.Ordinal))
                {
                    if (node != null)
                        throw new InvalidDataException(
                            "Installed local-module node name is duplicated.");
                    node = SingleToken(line.Substring(6));
                }
                else if (line.StartsWith("-setcookie ", StringComparison.Ordinal))
                {
                    if (cookie != null)
                        throw new InvalidDataException(
                            "Installed local-module cookie is duplicated.");
                    cookie = SingleToken(line.Substring(11));
                }
            }
            if (node == null || cookie == null)
                throw new InvalidDataException(
                    "Installed local-module vm.args is incomplete.");
            return new VmArgs { NodeName = node, Cookie = cookie };
        }

        private static string SingleToken(string value)
        {
            string result = value.Trim();
            if (result.Length == 0 ||
                result.IndexOfAny(new[] { ' ', '\t', '"', '\'' }) >= 0)
                throw new InvalidDataException(
                    "Installed local-module vm.args value is ambiguous.");
            return result;
        }

        private static string Required(
            IDictionary<string, string> values,
            string key)
        {
            string value;
            if (!values.TryGetValue(key, out value) || value.Length == 0)
                throw new InvalidDataException(
                    "Installed local-module configuration key is missing.");
            return value;
        }

        private static int RequiredPort(
            IDictionary<string, string> values,
            string key)
        {
            int port;
            if (!int.TryParse(
                    Required(values, key),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out port) ||
                port < 1 || port > 65535)
                throw new InvalidDataException(
                    "Installed local-module port is invalid.");
            return port;
        }

        private static bool IsCookie(string value)
        {
            if (value == null || value.Length != 32) return false;
            for (int index = 0; index < value.Length; index++)
                if (char.IsWhiteSpace(value[index])) return false;
            return true;
        }

        private static bool IsExpectedDatabaseBind(
            LocalModuleInstalledLayout layout,
            string value)
        {
            return string.Equals(value, "127.0.0.1", StringComparison.Ordinal) ||
                (layout.CloneOrdinal == 0 && string.Equals(
                    value,
                    "0.0.0.0",
                    StringComparison.Ordinal));
        }

        private static bool ErlPathsEqual(string observed, string expected)
        {
            if (string.IsNullOrEmpty(observed)) return false;
            StringBuilder decoded = new StringBuilder(observed.Length);
            for (int index = 0; index < observed.Length; index++)
            {
                if (observed[index] != '\\')
                {
                    decoded.Append(observed[index]);
                    continue;
                }
                if (index + 1 >= observed.Length || observed[index + 1] != '\\')
                    return false;
                decoded.Append('\\');
                index++;
            }
            return PathsEqual(decoded.ToString(), expected);
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                if (exception is ArgumentException ||
                    exception is NotSupportedException ||
                    exception is PathTooLongException)
                    return false;
                throw;
            }
        }

        private static string Sha256(string value)
        {
            byte[] digest;
            using (SHA256 algorithm = SHA256.Create())
                digest = algorithm.ComputeHash(
                    new UTF8Encoding(false, true).GetBytes(value));
            StringBuilder result = new StringBuilder(64);
            for (int index = 0; index < digest.Length; index++)
                result.Append(digest[index].ToString("x2"));
            return result.ToString();
        }

        private sealed class VmArgs
        {
            internal string NodeName { get; set; }
            internal string Cookie { get; set; }
        }
    }
}
