using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class InstalledLocalModuleProduct
    {
        internal string ProductCode { get; set; }
        internal string DisplayName { get; set; }
        internal string DisplayVersion { get; set; }
        internal string InstallLocation { get; set; }
        internal RegistryView RegistryView { get; set; }
    }

    internal interface IInstalledLocalModuleRegistry
    {
        IList<InstalledLocalModuleProduct> Read(RegistryView view);
    }

    internal sealed class InstalledLocalModuleProductReader
    {
        private readonly IInstalledLocalModuleRegistry _registry;

        internal InstalledLocalModuleProductReader()
            : this(new WindowsInstalledLocalModuleRegistry())
        {
        }

        internal InstalledLocalModuleProductReader(
            IInstalledLocalModuleRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException("registry");
            _registry = registry;
        }

        internal IList<InstalledLocalModuleProduct> ReadAll()
        {
            List<InstalledLocalModuleProduct> result =
                new List<InstalledLocalModuleProduct>();
            Append(result, _registry.Read(RegistryView.Registry64));
            Append(result, _registry.Read(RegistryView.Registry32));
            return result;
        }

        internal InstalledLocalModuleProduct FindExact(
            string productCode,
            string displayName,
            string displayVersion,
            string installLocation)
        {
            string product = NormalizeProductCode(productCode);
            InstalledLocalModuleProduct match = null;
            IList<InstalledLocalModuleProduct> installed = ReadAll();
            for (int index = 0; index < installed.Count; index++)
            {
                InstalledLocalModuleProduct current = installed[index];
                if (!string.Equals(
                        NormalizeProductCode(current.ProductCode),
                        product,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        current.DisplayName,
                        displayName,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        current.DisplayVersion,
                        displayVersion,
                        StringComparison.Ordinal) ||
                    !PathsEqual(current.InstallLocation, installLocation))
                    continue;
                if (match != null)
                    throw new InvalidDataException(
                        "Installed local-module product identity is duplicated.");
                match = current;
            }
            return match;
        }

        // Пустое значение означает "любой": базовый ЛМ вендора опознаётся по
        // имени продукта и каталогу, без привязки к версии и ProductCode.
        internal InstalledLocalModuleProduct Find(
            string productCode,
            string displayName,
            string displayVersion,
            string installLocation)
        {
            string product = string.IsNullOrEmpty(productCode)
                ? string.Empty
                : NormalizeProductCode(productCode);
            InstalledLocalModuleProduct match = null;
            IList<InstalledLocalModuleProduct> installed = ReadAll();
            for (int index = 0; index < installed.Count; index++)
            {
                InstalledLocalModuleProduct current = installed[index];
                if (product.Length != 0 && !string.Equals(
                        NormalizeProductCode(current.ProductCode),
                        product,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(displayName) && !string.Equals(
                        current.DisplayName,
                        displayName,
                        StringComparison.Ordinal))
                    continue;
                if (!string.IsNullOrEmpty(displayVersion) && !string.Equals(
                        current.DisplayVersion,
                        displayVersion,
                        StringComparison.Ordinal))
                    continue;
                if (!PathsEqual(current.InstallLocation, installLocation))
                    continue;
                if (match != null)
                    throw new InvalidDataException(
                        "Installed local-module product identity is duplicated.");
                match = current;
            }
            return match;
        }

        private static void Append(
            IList<InstalledLocalModuleProduct> destination,
            IList<InstalledLocalModuleProduct> source)
        {
            if (source == null)
                throw new InvalidDataException(
                    "Installed local-module registry result is missing.");
            for (int index = 0; index < source.Count; index++)
                destination.Add(source[index]);
        }

        private static string NormalizeProductCode(string productCode)
        {
            Guid value;
            if (!Guid.TryParseExact(productCode, "B", out value))
                throw new InvalidDataException(
                    "Installed local-module product code is invalid.");
            return value.ToString("B").ToUpperInvariant();
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) ||
                string.IsNullOrWhiteSpace(right))
                return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
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
    }

    internal sealed class WindowsInstalledLocalModuleRegistry :
        IInstalledLocalModuleRegistry
    {
        private const string UninstallPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public IList<InstalledLocalModuleProduct> Read(RegistryView view)
        {
            List<InstalledLocalModuleProduct> result =
                new List<InstalledLocalModuleProduct>();
            using (RegistryKey machine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                view))
            using (RegistryKey uninstall = machine.OpenSubKey(
                UninstallPath,
                false))
            {
                if (uninstall == null) return result;
                string[] names = uninstall.GetSubKeyNames();
                for (int index = 0; index < names.Length; index++)
                {
                    Guid productCode;
                    if (!Guid.TryParseExact(names[index], "B", out productCode))
                        continue;
                    using (RegistryKey product = uninstall.OpenSubKey(
                        names[index],
                        false))
                    {
                        if (product == null) continue;
                        result.Add(new InstalledLocalModuleProduct
                        {
                            ProductCode = productCode.ToString("B")
                                .ToUpperInvariant(),
                            DisplayName = Convert.ToString(
                                product.GetValue("DisplayName")),
                            DisplayVersion = Convert.ToString(
                                product.GetValue("DisplayVersion")),
                            InstallLocation = Convert.ToString(
                                product.GetValue("InstallLocation")),
                            RegistryView = view
                        });
                    }
                }
            }
            return result;
        }
    }
}
