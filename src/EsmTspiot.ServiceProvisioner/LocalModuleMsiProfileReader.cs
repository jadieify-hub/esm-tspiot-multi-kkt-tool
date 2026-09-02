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

    internal sealed class LocalModuleMsiProfileReader :
        ILocalModuleMsiProfileReader
    {
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
                    "MSI structural profile could not be read safely.");
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
                new[]
                {
                    "RegimeInstallDir",
                    "RegimeInstallDirRoot",
                    "regFC4DCF9969288D5B232FA9DAD986BD25"
                },
                false);
            ReadRows(
                database,
                result,
                "RegLocator",
                new[] { "Signature_", "Root", "Key", "Name", "Type" },
                2,
                new[] { "RegimeInstallDirRegistry" },
                false);
            ReadRows(
                database,
                result,
                "AppSearch",
                new[] { "Property", "Signature_" },
                1,
                new[] { "APPLICATIONFOLDER|RegimeInstallDirRegistry" },
                true);
            ReadRows(
                database,
                result,
                "File",
                new[] { "File", "Component_", "FileName", "FileSize", "Attributes", "Sequence" },
                2,
                new[]
                {
                    "fil4D9BD38000F7BBE9FA37B3949730CD45",
                    "filB64169385D3728F85488BA683E5B1809",
                    "fil6C2420445C448A9D193F9397AB772B3F",
                    "fil8B086431A47A790C3CAEE2AA56EF7E1C",
                    "fil0C0BA2BF1CF3006FEE6418B1FBB8A2DC"
                },
                false);
            ReadRows(
                database,
                result,
                "CustomAction",
                new[] { "Action", "Type", "Source", "Target" },
                2,
                new[]
                {
                    "SetInstallYeniseiService",
                    "SetInstallRegimeService",
                    "SetSetStartPriorityYenisei",
                    "SetSetStartPriorityRegime",
                    "SetSetYeniseiServiceLog",
                    "SetSetRegimeServiceLog",
                    "SetNotAutoStartlYeniseiService",
                    "SetNotAutoStartlRegimeService",
                    "SetSetAppExitYenisei",
                    "SetSetAppExitRegime",
                    "SetStartYeniseiService",
                    "SetStartRegimeService",
                    "SetStopYeniseiService",
                    "SetStopRegimeService",
                    "SetRemoveYeniseiService",
                    "SetRemoveRegimeService",
                    "SetStopEPMD",
                    "StopEPMD"
                },
                false);
            ReadRows(
                database,
                result,
                "InstallExecuteSequence",
                new[] { "Action", "Condition", "Sequence" },
                2,
                new[] { "InstallAutoApdater", "StopEPMD" },
                false);
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
            HashSet<string> allowed = new HashSet<string>(
                allowedKeys,
                StringComparer.Ordinal);
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
                        if (!allowed.Contains(key)) continue;
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
