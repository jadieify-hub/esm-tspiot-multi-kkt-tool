using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiTransformPlan
    {
        private LocalModuleMsiTransformPlan()
        {
            ExpectedConfigFiles =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);
        }

        internal LocalModuleMsiCloneIdentity Identity { get; private set; }
        internal string ProductVersion { get; private set; }
        internal int ApiPort { get; private set; }
        internal int DatabasePort { get; private set; }
        internal string ApiNodeName { get; private set; }
        internal string DatabaseNodeName { get; private set; }
        internal string EquironRegistryKey { get; private set; }
        internal string CrptRegistryKey { get; private set; }
        internal IDictionary<string, byte[]> ExpectedConfigFiles { get; private set; }

        internal static LocalModuleMsiTransformPlan Create(
            LocalModuleMsiCloneIdentity identity,
            string productVersion,
            int apiPort,
            int databasePort)
        {
            if (identity == null) throw new ArgumentNullException("identity");
            if (apiPort != LocalModuleMsiIdentity.ApiPortForClone(
                    identity.CloneOrdinal) ||
                databasePort != LocalModuleMsiIdentity.DatabasePortForClone(
                    identity.CloneOrdinal))
            {
                throw new ArgumentException("Local-module clone ports are invalid.");
            }
            string suffix = identity.CloneOrdinal.ToString();
            return new LocalModuleMsiTransformPlan
            {
                Identity = identity,
                ProductVersion = productVersion == null
                    ? string.Empty
                    : productVersion.Trim(),
                ApiPort = apiPort,
                DatabasePort = databasePort,
                ApiNodeName = identity.ApiServiceName + "@127.0.0.1",
                DatabaseNodeName = identity.DatabaseServiceName + "@127.0.0.1",
                EquironRegistryKey =
                    "Software\\Эквирон\\Локальный модуль ЧЗ экземпляр " + suffix,
                CrptRegistryKey =
                    "Software\\ЦРПТ\\Локальный модуль ЧЗ экземпляр " + suffix
            };
        }
    }
}
