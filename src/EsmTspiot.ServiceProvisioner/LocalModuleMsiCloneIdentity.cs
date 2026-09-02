using System;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiCloneIdentity
    {
        internal string Inn { get; set; }
        internal int CloneOrdinal { get; set; }
        internal Guid ProductCode { get; set; }
        internal Guid UpgradeCode { get; set; }
        internal Guid PackageCode { get; set; }
        internal string ProductName { get; set; }
        internal string InstallDirectoryName { get; set; }
        internal string ApiServiceName { get; set; }
        internal string DatabaseServiceName { get; set; }
    }
}
