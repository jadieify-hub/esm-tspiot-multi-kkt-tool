using System;
using System.IO;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleInstalledLayout
    {
        private LocalModuleInstalledLayout()
        {
        }

        internal string InstallRoot { get; private set; }
        internal int CloneOrdinal { get; private set; }
        internal string ApiServiceName { get; private set; }
        internal string DatabaseServiceName { get; private set; }
        internal int ApiPort { get; private set; }
        internal int DatabasePort { get; private set; }
        internal string ApiNodeName { get; private set; }
        internal string DatabaseNodeName { get; private set; }
        internal string ServiceImagePath { get; private set; }
        internal string ApiLocalIniPath { get; private set; }
        internal string DatabaseLocalIniPath { get; private set; }
        internal string ApiVmArgsPath { get; private set; }
        internal string DatabaseVmArgsPath { get; private set; }
        internal string ErlIniPath { get; private set; }
        internal string ErtsBinPath { get; private set; }

        internal static LocalModuleInstalledLayout Create(
            string installRoot,
            int cloneOrdinal,
            int apiPort,
            int databasePort)
        {
            if (cloneOrdinal < 0 ||
                cloneOrdinal > LocalModuleMsiIdentity.MaximumCloneOrdinal)
                throw new ArgumentOutOfRangeException("cloneOrdinal");
            if (apiPort < 1 || apiPort > 65535 ||
                databasePort < 1 || databasePort > 65535 ||
                apiPort == databasePort)
                throw new ArgumentException("Local-module ports are invalid.");
            if (cloneOrdinal > 0 &&
                (apiPort != LocalModuleMsiIdentity.ApiPortForClone(cloneOrdinal) ||
                 databasePort != LocalModuleMsiIdentity.DatabasePortForClone(
                     cloneOrdinal)))
                throw new ArgumentException(
                    "Local-module clone ports do not match its ordinal.");
            string root = NormalizeRoot(installRoot);
            string apiName = LocalModuleMsiIdentity.ApiServiceName(cloneOrdinal);
            string databaseName =
                LocalModuleMsiIdentity.DatabaseServiceName(cloneOrdinal);
            string erts = Path.Combine(root, "erts-13.0.4", "bin");
            return new LocalModuleInstalledLayout
            {
                InstallRoot = root,
                CloneOrdinal = cloneOrdinal,
                ApiServiceName = apiName,
                DatabaseServiceName = databaseName,
                ApiPort = apiPort,
                DatabasePort = databasePort,
                ApiNodeName = apiName + "@127.0.0.1",
                DatabaseNodeName = databaseName + "@127.0.0.1",
                ServiceImagePath = Path.Combine(root, "bin", "nssm.exe"),
                ApiLocalIniPath = Path.Combine(
                    root, "regime", "etc", "local.ini"),
                DatabaseLocalIniPath = Path.Combine(
                    root, "yenisei", "etc", "local.ini"),
                ApiVmArgsPath = Path.Combine(
                    root, "regime", "etc", "vm.args"),
                DatabaseVmArgsPath = Path.Combine(
                    root, "yenisei", "etc", "vm.args"),
                ErlIniPath = Path.Combine(erts, "erl.ini"),
                ErtsBinPath = erts
            };
        }

        internal string ServiceName(LocalModuleProcessRole role)
        {
            if (role == LocalModuleProcessRole.Api) return ApiServiceName;
            if (role == LocalModuleProcessRole.Database)
                return DatabaseServiceName;
            throw new ArgumentOutOfRangeException("role");
        }

        internal int Port(LocalModuleProcessRole role)
        {
            if (role == LocalModuleProcessRole.Api) return ApiPort;
            if (role == LocalModuleProcessRole.Database) return DatabasePort;
            throw new ArgumentOutOfRangeException("role");
        }

        internal bool MatchesService(
            LocalModuleProcessRole role,
            WindowsServiceRecord service)
        {
            if (service == null ||
                !string.Equals(
                    service.ServiceName,
                    ServiceName(role),
                    StringComparison.Ordinal))
                return false;
            string image = service.ImagePath == null
                ? string.Empty
                : service.ImagePath.Trim();
            if (image.Length >= 2 && image[0] == '"')
            {
                if (image[image.Length - 1] != '"') return false;
                image = image.Substring(1, image.Length - 2);
            }
            try
            {
                return string.Equals(
                    Path.GetFullPath(image),
                    ServiceImagePath,
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

        private static string NormalizeRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
                throw new ArgumentException(
                    "Local-module install root must be absolute.",
                    "installRoot");
            return Path.GetFullPath(value)
                .TrimEnd(Path.DirectorySeparatorChar);
        }
    }
}
