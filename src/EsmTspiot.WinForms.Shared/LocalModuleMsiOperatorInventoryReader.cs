using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LocalModuleMsiOperatorInventorySnapshot
    {
        internal LocalModuleMsiOperatorInventorySnapshot()
        {
            Assignments = new List<LocalModuleMsiAssignment>();
            Items = new List<LocalModuleMsiInventoryItem>();
            Listeners = new List<TcpListenerSnapshotItem>();
            InstalledModuleInns = new List<string>();
        }

        internal LocalModuleBaseInventory BaseInventory { get; set; }
        internal IList<LocalModuleMsiAssignment> Assignments { get; private set; }
        internal IList<LocalModuleMsiInventoryItem> Items { get; private set; }
        internal IList<TcpListenerSnapshotItem> Listeners { get; private set; }
        internal IList<string> InstalledModuleInns { get; private set; }

        /// <summary>
        /// Назначение плана — ещё не установка: планировщик выдаёт ЛМ каждому
        /// ИНН, в том числе тому, для кого шаг установки не выполнялся. Запись
        /// инвентаря — тоже не установка: она появляется до вызова MSI и
        /// переживает удаление продукта средствами Windows. Установленным
        /// считается ЛМ, чья запись с тем же ИНН и номером клона есть в
        /// инвентаре и чьи обе вендорские службы стоят в SCM.
        /// </summary>
        internal bool HasInstalledModule(LocalModuleMsiAssignment planned)
        {
            if (planned == null) return false;
            for (int index = 0; index < Items.Count; index++)
            {
                LocalModuleMsiInventoryItem item = Items[index];
                if (item.CloneOrdinal == planned.CloneOrdinal &&
                    string.Equals(item.Inn, planned.Inn, StringComparison.Ordinal))
                    return InstalledModuleInns.Contains(item.Inn);
            }
            return false;
        }
    }

    internal sealed class LocalModuleMsiOperatorInventoryReader
    {
        private const int VendorApiPort = 5995;
        private const int VendorDatabasePort = 5984;
        private readonly string _inventoryRoot;
        private readonly Func<string, bool> _serviceExists;

        internal LocalModuleMsiOperatorInventoryReader(
            Func<string, bool> serviceExists)
            : this(Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "KRS",
                "MultiKKT",
                "OperatorInventory",
                "LocalModuleMsi"),
                serviceExists)
        {
        }

        internal LocalModuleMsiOperatorInventoryReader(
            string inventoryRoot,
            Func<string, bool> serviceExists)
        {
            if (string.IsNullOrWhiteSpace(inventoryRoot))
                throw new ArgumentException(
                    "Operator inventory root is required.",
                    "inventoryRoot");
            if (serviceExists == null)
                throw new ArgumentNullException("serviceExists");
            _inventoryRoot = Path.GetFullPath(inventoryRoot);
            _serviceExists = serviceExists;
        }

        internal LocalModuleMsiOperatorInventorySnapshot Read()
        {
            LocalModuleMsiOperatorInventorySnapshot result =
                new LocalModuleMsiOperatorInventorySnapshot();
            ReadItems(result);
            MarkInstalledModules(result);
            result.BaseInventory = ReadBaseInventory(result.Items);
            ReadListeners(result);
            return result;
        }

        // Запись инвентаря появляется до установки MSI и переживает удаление
        // продукта средствами Windows, поэтому сама по себе установку не
        // доказывает. Установленным ЛМ считается тот, чьи обе вендорские
        // службы стоят в SCM; их состояние здесь не критерий.
        private void MarkInstalledModules(
            LocalModuleMsiOperatorInventorySnapshot result)
        {
            for (int index = 0; index < result.Items.Count; index++)
            {
                LocalModuleMsiInventoryItem item = result.Items[index];
                if (_serviceExists(LocalModuleMsiIdentity.ApiServiceName(
                        item.CloneOrdinal)) &&
                    _serviceExists(LocalModuleMsiIdentity.DatabaseServiceName(
                        item.CloneOrdinal)))
                    result.InstalledModuleInns.Add(item.Inn);
            }
        }

        private void ReadItems(LocalModuleMsiOperatorInventorySnapshot result)
        {
            if (!Directory.Exists(_inventoryRoot)) return;
            if ((File.GetAttributes(_inventoryRoot) &
                FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Инвентарь MSI ЛМ содержит reparse point.");
            string[] files = Directory.GetFiles(_inventoryRoot, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            HashSet<string> inns = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> ordinals = new HashSet<int>();
            for (int index = 0; index < files.Length; index++)
            {
                LocalModuleMsiInventoryItem item = ReadItem(files[index]);
                if (!string.Equals(
                        Path.GetFileNameWithoutExtension(files[index]),
                        item.Inn,
                        StringComparison.Ordinal) ||
                    !inns.Add(item.Inn) || !ordinals.Add(item.CloneOrdinal))
                    throw new InvalidDataException(
                        "Инвентарь MSI ЛМ содержит повторное назначение.");
                result.Items.Add(item);
                result.Assignments.Add(new LocalModuleMsiAssignment
                {
                    Inn = item.Inn,
                    CloneOrdinal = item.CloneOrdinal,
                    ApiPort = item.ApiPort,
                    DatabasePort = item.DatabasePort,
                    InstallVolumeRoot =
                        LocalModuleInstallRootPolicy.GetVolumeRoot(
                            item.InstallRoot),
                    BaseWasPreExisting = item.PreExisting
                });
            }
        }

        private static LocalModuleMsiInventoryItem ReadItem(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Инвентарь MSI ЛМ содержит небезопасный файл.");
            LocalModuleMsiInventoryItem item;
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length <= 0 || stream.Length > 65536)
                    throw new InvalidDataException(
                        "Запись инвентаря MSI ЛМ имеет неверный размер.");
                item = (LocalModuleMsiInventoryItem)
                    new DataContractJsonSerializer(
                        typeof(LocalModuleMsiInventoryItem)).ReadObject(stream);
            }
            Validate(item);
            return item;
        }

        private static void Validate(LocalModuleMsiInventoryItem item)
        {
            if (item == null ||
                item.SchemaVersion !=
                    LocalModuleMsiInventoryItem.CurrentSchemaVersion ||
                !string.Equals(
                    item.OwnershipMarker,
                    LocalModuleMsiInventoryItem.ExpectedOwnershipMarker,
                    StringComparison.Ordinal) ||
                !LocalModuleMsiIdentity.IsInn(item.Inn) ||
                item.CloneOrdinal < 0 ||
                item.CloneOrdinal >
                    LocalModuleMsiIdentity.MaximumCloneOrdinal ||
                item.ApiPort < 1024 || item.ApiPort > 65535 ||
                item.DatabasePort < 1024 || item.DatabasePort > 65535 ||
                item.ApiPort == item.DatabasePort ||
                item.InstalledByApplication == item.PreExisting ||
                (item.CloneOrdinal > 0 &&
                    (item.PreExisting ||
                     item.ApiPort != LocalModuleMsiIdentity.ApiPortForClone(
                         item.CloneOrdinal) ||
                     item.DatabasePort !=
                        LocalModuleMsiIdentity.DatabasePortForClone(
                            item.CloneOrdinal))) ||
                !IsHex(item.ManifestSha256, 64) ||
                string.IsNullOrWhiteSpace(item.InstallRoot) ||
                !Path.IsPathRooted(item.InstallRoot))
                throw new InvalidDataException(
                    "Запись инвентаря MSI ЛМ недействительна.");
        }

        private static LocalModuleBaseInventory ReadBaseInventory(
            IList<LocalModuleMsiInventoryItem> items)
        {
            for (int index = 0; index < items.Count; index++)
            {
                LocalModuleMsiInventoryItem item = items[index];
                if (item.CloneOrdinal == 0)
                {
                    return new LocalModuleBaseInventory
                    {
                        IsInstalled = true,
                        AssignedInn = item.Inn,
                        InstallDirectory = item.InstallRoot,
                        ApiPort = item.ApiPort,
                        DatabasePort = item.DatabasePort,
                        WasInstalledByApplication = item.InstalledByApplication
                    };
                }
            }

            string installedRoot = FindVendorInstallRoot();
            if (!string.IsNullOrEmpty(installedRoot))
            {
                return new LocalModuleBaseInventory
                {
                    IsInstalled = true,
                    AssignedInn = string.Empty,
                    InstallDirectory = installedRoot,
                    ApiPort = ReadIniPort(
                        Path.Combine(installedRoot, "regime", "etc", "local.ini"),
                        "api",
                        "port"),
                    DatabasePort = ReadIniPort(
                        Path.Combine(installedRoot, "yenisei", "etc", "local.ini"),
                        "chttpd",
                        "port"),
                    WasInstalledByApplication = false
                };
            }

            return new LocalModuleBaseInventory
            {
                IsInstalled = false,
                AssignedInn = string.Empty,
                InstallDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Regime"),
                ApiPort = VendorApiPort,
                DatabasePort = VendorDatabasePort,
                WasInstalledByApplication = false
            };
        }

        private static string FindVendorInstallRoot()
        {
            RegistryView[] views =
                { RegistryView.Registry32, RegistryView.Registry64 };
            for (int index = 0; index < views.Length; index++)
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    views[index]))
                using (RegistryKey uninstall = root.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    false))
                {
                    if (uninstall == null) continue;
                    string location = FindVendorLocation(uninstall);
                    if (location.Length > 0) return location;
                }
            }
            return string.Empty;
        }

        // Базовый ЛМ опознаётся по имени продукта вендора в любой версии:
        // точный ProductCode и номер версии намеренно не фиксируются.
        private static string FindVendorLocation(RegistryKey uninstall)
        {
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
                    string name = Convert.ToString(
                        product.GetValue("DisplayName"));
                    string location = Convert.ToString(
                        product.GetValue("InstallLocation"));
                    if (!string.Equals(
                            name,
                            SupportedLocalModulePackageIdentity.ProductName,
                            StringComparison.Ordinal) ||
                        string.IsNullOrWhiteSpace(location) ||
                        !Path.IsPathRooted(location))
                        continue;
                    return Path.GetFullPath(location)
                        .TrimEnd(Path.DirectorySeparatorChar);
                }
            }
            return string.Empty;
        }

        private static int ReadIniPort(
            string path,
            string expectedSection,
            string expectedKey)
        {
            string section = string.Empty;
            string[] lines = File.ReadAllLines(path);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                    continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                int equals = line.IndexOf('=');
                if (equals <= 0 || !string.Equals(
                        section,
                        expectedSection,
                        StringComparison.Ordinal))
                    continue;
                string key = line.Substring(0, equals).Trim();
                if (!string.Equals(key, expectedKey, StringComparison.Ordinal))
                    continue;
                int port;
                if (int.TryParse(
                        line.Substring(equals + 1).Trim(),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out port) && port >= 1024 && port <= 65535)
                    return port;
                break;
            }
            throw new InvalidDataException(
                "Не удалось прочитать фактический порт базового ЛМ.");
        }

        private static void ReadListeners(
            LocalModuleMsiOperatorInventorySnapshot result)
        {
            HashSet<int> owned = new HashSet<int>();
            for (int index = 0; index < result.Assignments.Count; index++)
            {
                owned.Add(result.Assignments[index].ApiPort);
                owned.Add(result.Assignments[index].DatabasePort);
            }
            if (result.BaseInventory.IsInstalled)
            {
                owned.Add(result.BaseInventory.ApiPort);
                owned.Add(result.BaseInventory.DatabasePort);
            }
            System.Net.IPEndPoint[] endpoints =
                IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners();
            for (int index = 0; index < endpoints.Length; index++)
            {
                if (owned.Contains(endpoints[index].Port)) continue;
                result.Listeners.Add(new TcpListenerSnapshotItem
                {
                    Port = endpoints[index].Port,
                    IsOwnerVerified = false,
                    OwnerServiceName = string.Empty
                });
            }
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!((current >= '0' && current <= '9') ||
                      (current >= 'a' && current <= 'f') ||
                      (current >= 'A' && current <= 'F')))
                    return false;
            }
            return true;
        }
    }
}
