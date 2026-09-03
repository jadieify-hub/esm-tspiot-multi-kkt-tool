using System;
using System.Collections.Generic;
using System.IO;

namespace EsmTspiot.ServiceProvisioner
{
    /// <summary>
    /// Смысловая структура пакета ЛМ: строки находятся по назначению, а не по
    /// сгенерированным идентификаторам конкретной версии. Одинаково
    /// используется при проверке пакета и при подготовке клона.
    /// </summary>
    internal sealed class LocalModuleMsiStructure
    {
        internal const string ApplicationFolder = "APPLICATIONFOLDER";
        internal const string HiddenPropertiesProperty = "MsiHiddenProperties";
        internal const string ApiServiceName = "regime";
        internal const string DatabaseServiceName = "yenisei";

        internal static readonly string[] DisabledSequenceActions =
        {
            "InstallAutoApdater",
            "UninstallAutoApdater",
            "RemoveAll",
            "StopEPMD"
        };

        private LocalModuleMsiStructure()
        {
            InstallDirectoryRegistryRows = new List<MsiProfileRow>();
            VendorRegistryRows = new List<MsiProfileRow>();
            ServiceActionRows = new List<MsiProfileRow>();
            ConfigurationFileRows = new List<MsiProfileRow>();
            DisabledSequenceRows = new List<MsiProfileRow>();
            ServiceNames = new List<string>();
        }

        internal MsiProfileRow ApplicationFolderRow { get; private set; }
        internal MsiProfileRow HiddenPropertiesRow { get; private set; }
        internal MsiProfileRow AppSearchRow { get; private set; }
        internal MsiProfileRow RegLocatorRow { get; private set; }
        internal MsiProfileRow UpgradeRow { get; private set; }
        internal string InstallDirectoryRegistryKey { get; private set; }
        internal string VendorRegistryKey { get; private set; }
        internal IList<MsiProfileRow> InstallDirectoryRegistryRows { get; private set; }
        internal IList<MsiProfileRow> VendorRegistryRows { get; private set; }
        internal IList<MsiProfileRow> ServiceActionRows { get; private set; }
        internal IList<MsiProfileRow> ConfigurationFileRows { get; private set; }
        internal IList<MsiProfileRow> DisabledSequenceRows { get; private set; }
        internal IList<string> ServiceNames { get; private set; }

        internal static LocalModuleMsiStructure Resolve(
            IList<MsiProfileRow> rows,
            string upgradeCode)
        {
            if (rows == null) throw new ArgumentNullException("rows");
            LocalModuleMsiStructure result = new LocalModuleMsiStructure();
            result.ApplicationFolderRow = RequireSingle(
                rows,
                "Directory",
                ApplicationFolder);
            result.HiddenPropertiesRow = RequireSingle(
                rows,
                "Property",
                HiddenPropertiesProperty);
            result.AppSearchRow = RequireAppSearch(rows);
            string signature = result.AppSearchRow.Value("Signature_");
            result.RegLocatorRow = RequireSingle(rows, "RegLocator", signature);
            result.InstallDirectoryRegistryKey = Trim(
                result.RegLocatorRow.Value("Key"));
            if (result.InstallDirectoryRegistryKey.Length == 0)
            {
                throw Unsupported(
                    "RegLocator не указывает раздел реестра установки.");
            }
            SplitRegistryRows(rows, result);
            result.UpgradeRow = RequireUpgrade(rows, upgradeCode);
            CollectServiceActions(rows, result);
            CollectDisabledSequences(rows, result);
            CollectConfigurationFiles(rows, result);
            return result;
        }

        private static void SplitRegistryRows(
            IList<MsiProfileRow> rows,
            LocalModuleMsiStructure result)
        {
            List<string> otherKeys = new List<string>();
            for (int index = 0; index < rows.Count; index++)
            {
                MsiProfileRow row = rows[index];
                if (row == null || !string.Equals(
                        row.Table,
                        "Registry",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                string key = Trim(row.Value("Key"));
                if (string.Equals(
                        key,
                        result.InstallDirectoryRegistryKey,
                        StringComparison.Ordinal))
                {
                    result.InstallDirectoryRegistryRows.Add(row);
                    continue;
                }
                if (!otherKeys.Contains(key)) otherKeys.Add(key);
                result.VendorRegistryRows.Add(row);
            }
            if (result.InstallDirectoryRegistryRows.Count == 0)
            {
                throw Unsupported(
                    "В MSI нет записей реестра для раздела установки.");
            }
            if (otherKeys.Count > 1)
            {
                throw Unsupported(
                    "В MSI больше двух разделов реестра вендора; клонирование " +
                    "остановлено до проверки новой версии пакета.");
            }
            result.VendorRegistryKey = otherKeys.Count == 0
                ? string.Empty
                : otherKeys[0];
        }

        private static void CollectServiceActions(
            IList<MsiProfileRow> rows,
            LocalModuleMsiStructure result)
        {
            List<string> names = new List<string>();
            for (int index = 0; index < rows.Count; index++)
            {
                MsiProfileRow row = rows[index];
                if (row == null || !string.Equals(
                        row.Table,
                        "CustomAction",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                string target = row.Value("Target");
                if (target == null ||
                    target.IndexOf("nssm.exe", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                IList<string> quoted = PlainQuotedTokens(target);
                if (quoted.Count == 0) continue;
                result.ServiceActionRows.Add(row);
                for (int name = 0; name < quoted.Count; name++)
                {
                    if (!names.Contains(quoted[name])) names.Add(quoted[name]);
                }
            }
            names.Sort(StringComparer.Ordinal);
            for (int index = 0; index < names.Count; index++)
            {
                result.ServiceNames.Add(names[index]);
            }
            if (names.Count != 2 ||
                !names.Contains(ApiServiceName) ||
                !names.Contains(DatabaseServiceName))
            {
                throw Unsupported(
                    "Набор имён служб в действиях MSI не совпадает с " +
                    ApiServiceName + "/" + DatabaseServiceName + ": " +
                    string.Join(", ", names.ToArray()) + ".");
            }
        }

        private static void CollectDisabledSequences(
            IList<MsiProfileRow> rows,
            LocalModuleMsiStructure result)
        {
            for (int index = 0; index < DisabledSequenceActions.Length; index++)
            {
                result.DisabledSequenceRows.Add(RequireSingle(
                    rows,
                    "InstallExecuteSequence",
                    DisabledSequenceActions[index]));
            }
        }

        private static void CollectConfigurationFiles(
            IList<MsiProfileRow> rows,
            LocalModuleMsiStructure result)
        {
            int distFiles = 0;
            for (int index = 0; index < rows.Count; index++)
            {
                MsiProfileRow row = rows[index];
                if (row == null || !string.Equals(
                        row.Table,
                        "File",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                result.ConfigurationFileRows.Add(row);
                if (LocalModuleMsiProfileReader.LongFileName(
                        row.Value("FileName"))
                    .EndsWith(".dist", StringComparison.OrdinalIgnoreCase))
                {
                    distFiles++;
                }
            }
            if (distFiles < 4)
            {
                throw Unsupported(
                    "В MSI нет четырёх шаблонов конфигурации local.ini.dist и " +
                    "vm.args.dist.");
            }
        }

        // Кавычки в действиях nssm окружают либо пути с подстановками, либо
        // имена служб; именем считается только простой латинский токен.
        internal static IList<string> PlainQuotedTokens(string value)
        {
            List<string> result = new List<string>();
            string text = value ?? string.Empty;
            int index = 0;
            while (index < text.Length)
            {
                int start = text.IndexOf('"', index);
                if (start < 0) break;
                int end = text.IndexOf('"', start + 1);
                if (end < 0) break;
                string candidate = text.Substring(start + 1, end - start - 1);
                if (IsPlainToken(candidate) && !result.Contains(candidate))
                {
                    result.Add(candidate);
                }
                index = end + 1;
            }
            return result;
        }

        private static bool IsPlainToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!((current >= 'a' && current <= 'z') ||
                      (current >= 'A' && current <= 'Z') ||
                      (current >= '0' && current <= '9')))
                {
                    return false;
                }
            }
            return true;
        }

        private static MsiProfileRow RequireAppSearch(IList<MsiProfileRow> rows)
        {
            MsiProfileRow found = null;
            for (int index = 0; index < rows.Count; index++)
            {
                MsiProfileRow row = rows[index];
                if (row == null || !string.Equals(
                        row.Table,
                        "AppSearch",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (!string.Equals(
                        Trim(row.Value("Property")),
                        ApplicationFolder,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (found != null)
                {
                    throw Unsupported(
                        "В MSI несколько поисков каталога установки.");
                }
                found = row;
            }
            if (found == null)
            {
                throw Unsupported(
                    "В MSI нет поиска каталога установки APPLICATIONFOLDER.");
            }
            return found;
        }

        private static MsiProfileRow RequireUpgrade(
            IList<MsiProfileRow> rows,
            string upgradeCode)
        {
            Guid expected;
            if (!Guid.TryParseExact(Trim(upgradeCode), "B", out expected))
            {
                throw Unsupported("UpgradeCode пакета нечитаем.");
            }
            MsiProfileRow found = null;
            for (int index = 0; index < rows.Count; index++)
            {
                MsiProfileRow row = rows[index];
                if (row == null || !string.Equals(
                        row.Table,
                        "Upgrade",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                Guid observed;
                if (!Guid.TryParseExact(Trim(row.Key), "B", out observed) ||
                    observed != expected)
                {
                    continue;
                }
                if (found != null)
                {
                    throw Unsupported(
                        "В MSI несколько строк Upgrade с кодом пакета.");
                }
                found = row;
            }
            if (found == null)
            {
                throw Unsupported(
                    "В MSI нет строки Upgrade с кодом пакета " +
                    Trim(upgradeCode) + ".");
            }
            return found;
        }

        private static MsiProfileRow RequireSingle(
            IList<MsiProfileRow> rows,
            string table,
            string key)
        {
            MsiProfileRow found = null;
            for (int index = 0; index < rows.Count; index++)
            {
                MsiProfileRow row = rows[index];
                if (row == null ||
                    !string.Equals(row.Table, table, StringComparison.Ordinal) ||
                    !string.Equals(row.Key, key, StringComparison.Ordinal))
                {
                    continue;
                }
                if (found != null)
                {
                    throw Unsupported(
                        "В MSI несколько строк " + table + ":" + key + ".");
                }
                found = row;
            }
            if (found == null)
            {
                throw Unsupported(
                    "В MSI нет строки " + table + ":" + key + ".");
            }
            return found;
        }

        private static InvalidDataException Unsupported(string reason)
        {
            return new InvalidDataException(
                "Структура MSI ЛМ не поддерживается. " + reason);
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
