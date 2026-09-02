using System;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IWindowsInstallerApi
    {
        uint Install(string packagePath, string hiddenProperties);
        uint Repair(string productCode);
        uint Uninstall(string productCode);
    }

    internal sealed class WindowsInstallerOperationException : Exception
    {
        internal WindowsInstallerOperationException(
            string operation,
            uint msiErrorCode)
            : base(
                "Windows Installer " + operation +
                " failed with code " + msiErrorCode.ToString() + ".")
        {
            MsiErrorCode = msiErrorCode;
        }

        internal uint MsiErrorCode { get; private set; }
    }
}
