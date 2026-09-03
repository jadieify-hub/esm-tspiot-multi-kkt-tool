using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WixToolset.Dtf.WindowsInstaller;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface ILocalModuleMsiProfileReader
    {
        LocalModuleMsiDatabaseSnapshot Read(string lockedMsiPath);
    }

    /// <summary>
    /// Снимок структуры MSI ЛМ. Строки отбираются по смыслу, а не по
    /// сгенерированным идентификаторам конкретной версии пакета: имена
    /// файлов конфигурации, таблицы Registry/RegLocator/AppSearch/Upgrade
    /// и действия читаются целиком, а нужные строки выбирает
    /// <see cref="LocalModuleMsiCapabilityResolver"/>.
    /// </summary>
    internal sealed class LocalModuleMsiProfileReader :
        ILocalModuleMsiProfileReader
    {
        internal static readonly string[] ConfigurationFileNames =
        {
            "local.ini.dist",
            "vm.args.dist",
            "default.ini"
        };

        public LocalModuleMsiDatabaseSnapshot Read(string lockedMsiPath)
        {
            string path = Path.GetFullPath(lockedMsiPath);
            try
            {
                using (Database database = new Database(
                    path,
                    DatabaseOpenMode.ReadOnly))
                {
                    LocalModuleMsiDatabaseSnapshot result =
                        new LocalModuleMsiDatabaseSnapshot
                        {
                            FileRowCount = CountRows(database, "File"),
                            MsiFileHashRowCount = CountRows(
                                database,
                                "MsiFileHash")
                        };
                    ReadMedia(database, result);
                    ReadRequiredRows(database, result);
                    return result;
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new InvalidDataException(
                    "Структуру MSI ЛМ не удалось прочитать безопасно.");
            }
        }

        private static int CountRows(Database database, string table)
        {
            int count = 0;
            using (View view = database.OpenView(
                "SELECT * FROM `" + table + "`"))
            {
                view.Execute();
                Record record;
                while ((record = view.Fetch()) != null)
                {
                    using (record) count++;
                }
            }
            return count;
        }

        private static void ReadMedia(
            Database database,
            LocalModuleMsiDatabaseSnapshot result)
        {
            using (View view = database.OpenView(
                "SELECT `DiskId`,`LastSequence`,`Cabinet` FROM `Media`"))
            {
                view.Execute();
                Record record;
                while ((record = view.Fetch()) != null)
                {
                    using (record)
                    {
                        result.Media.Add(new MsiMediaSnapshot
                        {
                            DiskId = record.GetInteger(1),
                            LastSequence = record.GetInteger(2),
                            Cabinet = GetString(record, 3)
                        });
                    }
                }
            }
        }

        private static void ReadRequiredRows(
            Database database,
            LocalModuleMsiDatabaseSnapshot result)
        {
            ReadRows(
                database,
                result,
                "Property",
                new[] { "Property", "Value" },
                2,
                new[] { "MsiHiddenProperties", "SERVERURL" },
                false);
            ReadRows(
                database,
                result,
                "Directory",
                new[] { "Directory", "Directory_Parent", "DefaultDir" },
                2,
                new[] { "APPLICATIONFOLDER" },
                false);
            ReadRows(
                database,
                result,
                "Registry",
                new[] { "Registry", "Root", "Key", "Name", "Value", "Component_" },
                2,
                null,
                false);
            ReadRows(
                database,
                result,
                "RegLocator",
                new[] { "Signature_", "Root", "Key", "Name", "Type" },
                2,
                null,
                false);
            ReadRows(
                database,
                result,
                "AppSearch",
                new[] { "Property", "Signature_" },
                1,
                null,
                true);
            ReadRows(
                database,
                result,
                "Upgrade",
                new[]
                {
                    "UpgradeCode", "VersionMin", "VersionMax", "Language",
                    "Attributes", "Remove", "ActionProperty"
                },
                2,
                null,
                false);
            ReadConfigurationFileRows(database, result);
            ReadRows(
                database,
                result,
                "CustomAction",
                new[] { "Action", "Type", "Source", "Target" },
                2,
                null,
                false);
            ReadRows(
                database,
                result,
                "InstallExecuteSequence",
                new[] { "Action", "Condition", "Sequence" },
                2,
                null,
                false);
        }

        // Файлы конфигурации опознаются по длинному имени, а не по
        // сгенерированному идентификатору File, который меняется от версии
        // к версии вендорского пакета.
        private static void ReadConfigurationFileRows(
            Database database,
            LocalModuleMsiDatabaseSnapshot result)
        {
            string[] columns =
            {
                "File", "Component_", "FileName", "FileSize", "Attributes",
                "Sequence"
            };
            HashSet<string> wanted = new HashSet<string>(
                ConfigurationFileNames,
                StringComparer.OrdinalIgnoreCase);
            using (View view = database.OpenView(BuildSelect("File", columns)))
            {
                view.Execute();
                Record record;
                while ((record = view.Fetch()) != null)
                {
                    using (record)
                    {
                        string fileName = GetString(record, 3);
                        if (!wanted.Contains(LongFileName(fileName))) continue;
                        MsiProfileRow row = new MsiProfileRow
                        {
                            Table = "File",
                            Key = GetString(record, 1)
                        };
                        for (int field = 2; field <= columns.Length; field++)
                        {
                            row.Columns.Add(columns[field - 1]);
                            row.Values.Add(GetString(record, field));
                        }
                        result.Rows.Add(row);
                    }
                }
            }
        }

        internal static string LongFileName(string value)
        {
            string text = value == null ? string.Empty : value.Trim();
            int separator = text.IndexOf('|');
            return separator < 0 ? text : text.Substring(separator + 1);
        }

        private static void ReadRows(
            Database database,
            LocalModuleMsiDatabaseSnapshot result,
            string table,
            string[] columns,
            int valueStartField,
            string[] allowedKeys,
            bool compositeKey)
        {
            HashSet<string> allowed = allowedKeys == null
                ? null
                : new HashSet<string>(allowedKeys, StringComparer.Ordinal);
            using (View view = database.OpenView(BuildSelect(table, columns)))
            {
                view.Execute();
                Record record;
                while ((record = view.Fetch()) != null)
                {
                    using (record)
                    {
                        string key = compositeKey
                            ? GetString(record, 1) + "|" + GetString(record, 2)
                            : GetString(record, 1);
                        if (allowed != null && !allowed.Contains(key)) continue;
                        MsiProfileRow row = new MsiProfileRow
                        {
                            Table = table,
                            Key = key
                        };
                        for (int field = valueStartField;
                            field <= columns.Length;
                            field++)
                        {
                            row.Columns.Add(columns[field - 1]);
                            row.Values.Add(GetString(record, field));
                        }
                        result.Rows.Add(row);
                    }
                }
            }
        }

        private static string BuildSelect(string table, string[] columns)
        {
            StringBuilder query = new StringBuilder("SELECT ");
            for (int index = 0; index < columns.Length; index++)
            {
                if (index > 0) query.Append(',');
                query.Append('`').Append(columns[index]).Append('`');
            }
            query.Append(" FROM `").Append(table).Append('`');
            return query.ToString();
        }

        private static string GetString(Record record, int field)
        {
            return record.IsNull(field) ? string.Empty : record.GetString(field);
        }
    }
}
