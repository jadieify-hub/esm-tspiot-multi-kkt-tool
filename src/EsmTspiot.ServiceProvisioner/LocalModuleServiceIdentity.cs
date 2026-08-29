using System;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class LocalModuleServiceIdentity
    {
        private const string DatabasePrefix = "krs-lm-db-";
        private const string ApiPrefix = "krs-lm-api-";

        internal static string CreateName(
            string instanceId,
            LocalModuleProcessRole role)
        {
            if (role == LocalModuleProcessRole.Database)
            {
                return LocalModuleManagedIdentity.CreateDatabaseServiceName(instanceId);
            }
            if (role == LocalModuleProcessRole.Api)
            {
                return LocalModuleManagedIdentity.CreateApiServiceName(instanceId);
            }
            throw new ArgumentOutOfRangeException("role");
        }

        internal static bool TryParseName(
            string serviceName,
            out string instanceId,
            out LocalModuleProcessRole role)
        {
            instanceId = null;
            role = 0;
            string suffix;
            if (serviceName != null &&
                serviceName.StartsWith(DatabasePrefix, StringComparison.Ordinal))
            {
                suffix = serviceName.Substring(DatabasePrefix.Length);
                role = LocalModuleProcessRole.Database;
            }
            else if (serviceName != null &&
                serviceName.StartsWith(ApiPrefix, StringComparison.Ordinal))
            {
                suffix = serviceName.Substring(ApiPrefix.Length);
                role = LocalModuleProcessRole.Api;
            }
            else
            {
                return false;
            }

            if (!LocalModuleManagedIdentity.IsLowerHex(suffix, 24))
            {
                instanceId = null;
                role = 0;
                return false;
            }
            instanceId = "lmi-" + suffix;
            return string.Equals(
                serviceName,
                CreateName(instanceId, role),
                StringComparison.Ordinal);
        }

        internal static bool IsManagedName(string serviceName)
        {
            string instanceId;
            LocalModuleProcessRole role;
            return TryParseName(serviceName, out instanceId, out role);
        }
    }
}
