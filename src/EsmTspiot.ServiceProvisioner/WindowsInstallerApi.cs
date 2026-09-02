using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IWindowsInstallerNative
    {
        int SetInternalUi(int uiLevel);
        uint InstallProduct(string packagePath, string commandLine);
        uint ConfigureProduct(
            string productCode,
            int installLevel,
            int installState,
            string commandLine);
    }

    internal sealed class WindowsInstallerApi : IWindowsInstallerApi
    {
        private const int InstallLevelDefault = 0;
        private const int InstallStateAbsent = 2;
        private const int InstallStateDefault = 5;
        private const int InstallUiLevelNone = 2;
        private readonly IWindowsInstallerNative _native;

        internal WindowsInstallerApi()
            : this(new WindowsInstallerNative())
        {
        }

        internal WindowsInstallerApi(IWindowsInstallerNative native)
        {
            if (native == null) throw new ArgumentNullException("native");
            _native = native;
        }

        public uint Install(string packagePath, string hiddenProperties)
        {
            string path = Path.GetFullPath(packagePath);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "Local-module MSI package was not found.",
                    path);
            return RunWithoutInstallerUi(
                "install",
                delegate
                {
                    return _native.InstallProduct(
                        path,
                        hiddenProperties ?? string.Empty);
                });
        }

        public uint Repair(string productCode)
        {
            string product = NormalizeProductCode(productCode);
            return RunWithoutInstallerUi(
                "repair",
                delegate
                {
                    return _native.ConfigureProduct(
                        product,
                        InstallLevelDefault,
                        InstallStateDefault,
                        "REINSTALL=ALL REINSTALLMODE=vomus");
                });
        }

        public uint Uninstall(string productCode)
        {
            string product = NormalizeProductCode(productCode);
            return RunWithoutInstallerUi(
                "uninstall",
                delegate
                {
                    return _native.ConfigureProduct(
                        product,
                        InstallLevelDefault,
                        InstallStateAbsent,
                        "REBOOT=ReallySuppress");
                });
        }

        private uint RunWithoutInstallerUi(
            string operation,
            Func<uint> nativeOperation)
        {
            int previous = _native.SetInternalUi(InstallUiLevelNone);
            try
            {
                return Complete(operation, nativeOperation());
            }
            finally
            {
                _native.SetInternalUi(previous);
            }
        }

        internal static string RedactInstallProperties(string properties)
        {
            if (string.IsNullOrWhiteSpace(properties)) return string.Empty;
            string[] values = properties.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            StringBuilder result = new StringBuilder(properties.Length);
            for (int index = 0; index < values.Length; index++)
            {
                if (index != 0) result.Append(' ');
                int equals = values[index].IndexOf('=');
                if (equals <= 0)
                    result.Append("<redacted>");
                else
                    result.Append(values[index].Substring(0, equals + 1))
                        .Append("<redacted>");
            }
            return result.ToString();
        }

        private static uint Complete(string operation, uint code)
        {
            if (code != 0)
                throw new WindowsInstallerOperationException(operation, code);
            return code;
        }

        private static string NormalizeProductCode(string productCode)
        {
            Guid value;
            if (!Guid.TryParseExact(productCode, "B", out value))
                throw new ArgumentException(
                    "MSI product code must be a braced GUID.",
                    "productCode");
            return value.ToString("B").ToUpperInvariant();
        }
    }

    internal sealed class WindowsInstallerNative : IWindowsInstallerNative
    {
        public int SetInternalUi(int uiLevel)
        {
            return MsiSetInternalUI(uiLevel, IntPtr.Zero);
        }

        public uint InstallProduct(string packagePath, string commandLine)
        {
            return MsiInstallProductW(packagePath, commandLine);
        }

        public uint ConfigureProduct(
            string productCode,
            int installLevel,
            int installState,
            string commandLine)
        {
            return MsiConfigureProductExW(
                productCode,
                installLevel,
                installState,
                commandLine);
        }

        [DllImport(
            "msi.dll",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        private static extern uint MsiInstallProductW(
            string packagePath,
            string commandLine);

        [DllImport(
            "msi.dll",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        private static extern uint MsiConfigureProductExW(
            string productCode,
            int installLevel,
            int installState,
            string commandLine);

        [DllImport(
            "msi.dll",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        private static extern int MsiSetInternalUI(
            int uiLevel,
            IntPtr windowHandle);

    }
}
