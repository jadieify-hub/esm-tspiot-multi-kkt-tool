using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IWindowsInstallerPackageReader
    {
        WindowsInstallerPackageMetadata Read(string path);
    }

    internal sealed class WindowsInstallerPackageMetadata
    {
        internal string ProductName { get; set; }
        internal string ProductVersion { get; set; }
        internal string ProductCode { get; set; }
        internal string UpgradeCode { get; set; }

        internal WindowsInstallerPackageMetadata Clone()
        {
            return (WindowsInstallerPackageMetadata)MemberwiseClone();
        }
    }

    internal sealed class WindowsInstallerPackageReader : IWindowsInstallerPackageReader
    {
        private const uint ErrorSuccess = 0;
        private const uint ErrorMoreData = 234;
        private const uint ErrorNoMoreItems = 259;

        public WindowsInstallerPackageMetadata Read(string path)
        {
            string fullPath = Path.GetFullPath(path);
            uint database = 0;
            uint result = MsiOpenDatabaseW(fullPath, IntPtr.Zero, out database);
            if (result != ErrorSuccess || database == 0)
            {
                throw CreateMsiError("MSI database could not be opened read-only", result);
            }

            try
            {
                return new WindowsInstallerPackageMetadata
                {
                    ProductName = ReadProperty(database, "ProductName"),
                    ProductVersion = ReadProperty(database, "ProductVersion"),
                    ProductCode = ReadProperty(database, "ProductCode"),
                    UpgradeCode = ReadProperty(database, "UpgradeCode")
                };
            }
            finally
            {
                MsiCloseHandle(database);
            }
        }

        private static string ReadProperty(uint database, string propertyName)
        {
            uint view = 0;
            uint queryRecord = 0;
            uint valueRecord = 0;
            uint result = MsiDatabaseOpenViewW(
                database,
                "SELECT `Value` FROM `Property` WHERE `Property` = ?",
                out view);
            if (result != ErrorSuccess || view == 0)
            {
                throw CreateMsiError("MSI Property view could not be opened", result);
            }

            try
            {
                queryRecord = MsiCreateRecord(1);
                if (queryRecord == 0)
                {
                    throw new InvalidDataException("MSI query record could not be created.");
                }
                result = MsiRecordSetStringW(queryRecord, 1, propertyName);
                if (result != ErrorSuccess)
                {
                    throw CreateMsiError("MSI property query could not be prepared", result);
                }
                result = MsiViewExecute(view, queryRecord);
                if (result != ErrorSuccess)
                {
                    throw CreateMsiError("MSI property query could not be executed", result);
                }
                result = MsiViewFetch(view, out valueRecord);
                if (result == ErrorNoMoreItems || valueRecord == 0)
                {
                    throw new InvalidDataException(
                        "Required MSI property is missing: " + propertyName + ".");
                }
                if (result != ErrorSuccess)
                {
                    throw CreateMsiError("MSI property could not be read", result);
                }

                uint capacity = 256;
                StringBuilder value = new StringBuilder((int)capacity);
                result = MsiRecordGetStringW(valueRecord, 1, value, ref capacity);
                if (result == ErrorMoreData)
                {
                    capacity++;
                    value = new StringBuilder((int)capacity);
                    result = MsiRecordGetStringW(valueRecord, 1, value, ref capacity);
                }
                if (result != ErrorSuccess)
                {
                    throw CreateMsiError("MSI property value could not be read", result);
                }

                string text = value.ToString().Trim();
                if (text.Length == 0)
                {
                    throw new InvalidDataException(
                        "Required MSI property is empty: " + propertyName + ".");
                }
                return text;
            }
            finally
            {
                if (valueRecord != 0)
                {
                    MsiCloseHandle(valueRecord);
                }
                if (queryRecord != 0)
                {
                    MsiCloseHandle(queryRecord);
                }
                MsiViewClose(view);
                MsiCloseHandle(view);
            }
        }

        private static InvalidDataException CreateMsiError(string message, uint code)
        {
            return new InvalidDataException(message + " (Windows Installer " + code.ToString() + ").");
        }

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint MsiOpenDatabaseW(
            string databasePath,
            IntPtr persist,
            out uint database);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint MsiDatabaseOpenViewW(
            uint database,
            string query,
            out uint view);

        [DllImport("msi.dll", ExactSpelling = true)]
        private static extern uint MsiCreateRecord(uint parameterCount);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint MsiRecordSetStringW(
            uint record,
            uint field,
            string value);

        [DllImport("msi.dll", ExactSpelling = true)]
        private static extern uint MsiViewExecute(uint view, uint record);

        [DllImport("msi.dll", ExactSpelling = true)]
        private static extern uint MsiViewFetch(uint view, out uint record);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint MsiRecordGetStringW(
            uint record,
            uint field,
            StringBuilder value,
            ref uint valueLength);

        [DllImport("msi.dll", ExactSpelling = true)]
        private static extern uint MsiViewClose(uint view);

        [DllImport("msi.dll", ExactSpelling = true)]
        private static extern uint MsiCloseHandle(uint handle);
    }
}
