using System;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IManagedLocalModuleUpdatePlatform
    {
        void BeginExactMigration(
            string currentVersion,
            string selectedVersion);
    }

    internal sealed class ManagedLocalModuleUpdateWorkflow
    {
        private readonly IManagedLocalModuleUpdatePlatform _platform;

        internal ManagedLocalModuleUpdateWorkflow(
            IManagedLocalModuleUpdatePlatform platform)
        {
            if (platform == null) throw new ArgumentNullException("platform");
            _platform = platform;
        }

        internal LmServiceProvisioningStatus Evaluate(
            string currentVersion,
            string selectedVersion)
        {
            if (string.Equals(
                    currentVersion,
                    selectedVersion,
                    StringComparison.Ordinal))
            {
                return LmServiceProvisioningStatus.Succeeded;
            }
            if (!HasExactMigrationProfile(currentVersion, selectedVersion))
            {
                return LmServiceProvisioningStatus.VersionVerificationPending;
            }
            _platform.BeginExactMigration(currentVersion, selectedVersion);
            return LmServiceProvisioningStatus.Succeeded;
        }

        private static bool HasExactMigrationProfile(
            string currentVersion,
            string selectedVersion)
        {
            // No cross-version migration is approved yet. A future pair must be
            // added here together with its exact capability and VM evidence.
            return false;
        }
    }
}
