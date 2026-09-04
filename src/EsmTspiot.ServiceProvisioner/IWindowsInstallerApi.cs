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
            : this(operation, msiErrorCode, null)
        {
        }

        internal WindowsInstallerOperationException(
            string operation,
            uint msiErrorCode,
            string explanation)
            : base(
                "Windows Installer " + operation +
                " failed with code " + msiErrorCode.ToString() + "." +
                (string.IsNullOrEmpty(explanation)
                    ? string.Empty
                    : " " + explanation))
        {
            MsiErrorCode = msiErrorCode;
        }

        internal uint MsiErrorCode { get; private set; }
    }
}
