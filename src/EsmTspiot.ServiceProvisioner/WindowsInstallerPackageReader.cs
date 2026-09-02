using System;
using System.IO;
using WixToolset.Dtf.WindowsInstaller;

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
        internal string PackageCode { get; set; }

        internal WindowsInstallerPackageMetadata Clone()
        {
            return (WindowsInstallerPackageMetadata)MemberwiseClone();
        }
    }

    internal sealed class WindowsInstallerPackageReader :
        IWindowsInstallerPackageReader
    {
        public WindowsInstallerPackageMetadata Read(string path)
        {
            string fullPath = Path.GetFullPath(path);
            try
            {
                using (Database database = new Database(
                    fullPath,
                    DatabaseOpenMode.ReadOnly))
                {
                    return new WindowsInstallerPackageMetadata
                    {
                        ProductName = ReadProperty(database, "ProductName"),
                        ProductVersion = ReadProperty(database, "ProductVersion"),
                        ProductCode = ReadProperty(database, "ProductCode"),
                        UpgradeCode = ReadProperty(database, "UpgradeCode"),
                        PackageCode = RequireValue(
                            database.SummaryInfo.RevisionNumber,
                            "PackageCode")
                    };
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new InvalidDataException(
                    "MSI identity could not be read from the locked package.");
            }
        }

        private static string ReadProperty(
            Database database,
            string propertyName)
        {
            const string query =
                "SELECT `Value` FROM `Property` WHERE `Property` = ?";
            using (View view = database.OpenView(query))
            using (Record parameter = new Record(1))
            {
                parameter.SetString(1, propertyName);
                view.Execute(parameter);
                using (Record value = view.Fetch())
                {
                    if (value == null)
                    {
                        throw new InvalidDataException(
                            "Required MSI property is missing: " +
                            propertyName + ".");
                    }
                    return RequireValue(
                        value.IsNull(1) ? null : value.GetString(1),
                        propertyName);
                }
            }
        }

        private static string RequireValue(string value, string propertyName)
        {
            string result = value == null ? string.Empty : value.Trim();
            if (result.Length == 0)
            {
                throw new InvalidDataException(
                    "Required MSI property is empty: " + propertyName + ".");
            }
            return result;
        }
    }
}
