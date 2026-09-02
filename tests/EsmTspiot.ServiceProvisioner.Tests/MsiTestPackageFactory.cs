using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WixToolset.Dtf.Compression.Cab;
using WixToolset.Dtf.WindowsInstaller;

namespace EsmTspiot.ServiceProvisioner.Tests
{
    internal sealed class MsiTestPackageFixture : IDisposable
    {
        internal MsiTestPackageFixture(
            VerifiedLocalModulePackage package,
            LocalModuleMsiCapabilityProfile profile)
        {
            Package = package;
            Profile = profile;
        }

        internal VerifiedLocalModulePackage Package { get; private set; }
        internal LocalModuleMsiCapabilityProfile Profile { get; private set; }

        public void Dispose()
        {
            if (Package != null)
            {
                Package.Dispose();
                Package = null;
            }
        }
    }

    internal static class MsiTestPackageFactory
    {
        internal static MsiTestPackageFixture Create(string root)
        {
            string stage = "initialization";
            try
            {
            string msiPath = Path.Combine(root, "source.msi");
            string idtRoot = Path.Combine(root, "idt");
            Directory.CreateDirectory(idtRoot);
            LocalModuleMsiCapabilityProfile profile =
                LocalModuleMsiCapabilityRegistry.LoadSupportedProfile();
            profile.FileRowCount = 5;
            profile.MsiFileHashRowCount = 5;

            using (Database database = new Database(
                msiPath,
                DatabaseOpenMode.CreateDirect))
            {
                stage = "schema import";
                ImportSchemas(database, idtRoot);
                stage = "property rows";
                InsertProperty(database, "ProductName", profile.ProductName);
                InsertProperty(database, "ProductVersion", profile.ProductVersion);
                InsertProperty(database, "ProductCode", profile.ProductCode);
                InsertProperty(database, "UpgradeCode", profile.UpgradeCode);
                stage = "profile rows";
                InsertProfileRows(database, profile);
                stage = "media rows";
                InsertMedia(database, profile.Media);
                stage = "file hashes";
                InsertFileHashes(database, profile);
                stage = "cabinet streams";
                EmbedTestCabinets(database, root);
                stage = "summary information";
                using (SummaryInfo summary = database.SummaryInfo)
                {
                    summary.RevisionNumber = profile.PackageCode;
                    summary.Template = "x64;1049";
                    summary.PageCount = 200;
                    summary.WordCount = 2;
                    summary.Persist();
                }
                database.Commit();
            }

            stage = "read-back";
            WindowsInstallerPackageMetadata metadata =
                new WindowsInstallerPackageReader().Read(msiPath);
            LocalModuleMsiDatabaseSnapshot snapshot =
                new LocalModuleMsiProfileReader().Read(msiPath);
            if (snapshot.Compare(profile).Count != 0)
            {
                throw new InvalidDataException(
                    "Synthetic MSI does not match its test profile.");
            }
            FileStream sourceLock = new FileStream(
                msiPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return new MsiTestPackageFixture(
                new VerifiedLocalModulePackage(
                    msiPath,
                    sourceLock,
                    metadata,
                    profile,
                    snapshot),
                profile);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Synthetic MSI stage failed: " + stage + ".",
                    exception);
            }
        }

        internal static string ReadProperty(string path, string name)
        {
            using (Database database = new Database(path, DatabaseOpenMode.ReadOnly))
            using (View view = database.OpenView(
                "SELECT `Value` FROM `Property` WHERE `Property` = ?"))
            using (Record parameter = new Record(1))
            {
                parameter.SetString(1, name);
                view.Execute(parameter);
                using (Record result = view.Fetch())
                {
                    if (result == null) throw new InvalidDataException("Property missing.");
                    return result.GetString(1);
                }
            }
        }

        internal static string ReadPackageCode(string path)
        {
            using (Database database = new Database(path, DatabaseOpenMode.ReadOnly))
            using (SummaryInfo summary = database.SummaryInfo)
            {
                return summary.RevisionNumber;
            }
        }

        internal static string ReadRowValue(
            string path,
            string table,
            string keyColumn,
            string key,
            string valueColumn)
        {
            string query = "SELECT `" + valueColumn + "` FROM `" + table +
                "` WHERE `" + keyColumn + "` = ?";
            using (Database database = new Database(path, DatabaseOpenMode.ReadOnly))
            using (View view = database.OpenView(query))
            using (Record parameter = new Record(1))
            {
                parameter.SetString(1, key);
                view.Execute(parameter);
                using (Record result = view.Fetch())
                {
                    if (result == null) throw new InvalidDataException("Row missing.");
                    return result.IsNull(1) ? string.Empty : result.GetString(1);
                }
            }
        }

        private static void ImportSchemas(Database database, string root)
        {
            ImportSchema(database, root, "Property",
                new[] { "Property", "Value" },
                new[] { "s72", "l0" },
                new[] { "Property" });
            ImportSchema(database, root, "Directory",
                new[] { "Directory", "Directory_Parent", "DefaultDir" },
                new[] { "s72", "S72", "l255" },
                new[] { "Directory" });
            ImportSchema(database, root, "Registry",
                new[] { "Registry", "Root", "Key", "Name", "Value", "Component_" },
                new[] { "s72", "i2", "l255", "L255", "L0", "s72" },
                new[] { "Registry" });
            ImportSchema(database, root, "RegLocator",
                new[] { "Signature_", "Root", "Key", "Name", "Type" },
                new[] { "s72", "i2", "l255", "L255", "i2" },
                new[] { "Signature_" });
            ImportSchema(database, root, "AppSearch",
                new[] { "Property", "Signature_" },
                new[] { "s72", "s72" },
                new[] { "Property", "Signature_" });
            ImportSchema(database, root, "File",
                new[] { "File", "Component_", "FileName", "FileSize", "Attributes", "Sequence" },
                new[] { "s72", "s72", "l255", "i4", "I2", "i4" },
                new[] { "File" });
            ImportSchema(database, root, "MsiFileHash",
                new[] { "File_", "Options", "HashPart1", "HashPart2", "HashPart3", "HashPart4" },
                new[] { "s72", "i2", "i4", "i4", "i4", "i4" },
                new[] { "File_" });
            ImportSchema(database, root, "Media",
                new[] { "DiskId", "LastSequence", "Cabinet" },
                new[] { "i2", "i4", "S255" },
                new[] { "DiskId" });
            ImportSchema(database, root, "CustomAction",
                new[] { "Action", "Type", "Source", "Target" },
                new[] { "s72", "i2", "S72", "L0" },
                new[] { "Action" });
            ImportSchema(database, root, "InstallExecuteSequence",
                new[] { "Action", "Condition", "Sequence" },
                new[] { "s72", "S255", "i2" },
                new[] { "Action" });
        }

        private static void ImportSchema(
            Database database,
            string root,
            string table,
            string[] columns,
            string[] types,
            string[] keys)
        {
            string path = Path.Combine(root, table + ".idt");
            string content = string.Join("\t", columns) + "\r\n" +
                string.Join("\t", types) + "\r\n" + table + "\t" +
                string.Join("\t", keys) + "\r\n";
            File.WriteAllText(path, content, Encoding.ASCII);
            database.Import(path);
        }

        private static void InsertProperty(
            Database database,
            string name,
            string value)
        {
            Insert(database, "Property", new[] { "Property", "Value" },
                new[] { name, value });
        }

        private static void InsertProfileRows(
            Database database,
            LocalModuleMsiCapabilityProfile profile)
        {
            for (int index = 0; index < profile.Rows.Count; index++)
            {
                MsiProfileRow row = profile.Rows[index];
                string keyColumn = KeyColumn(row.Table);
                List<string> columns = new List<string>();
                List<string> values = new List<string>();
                if (!string.Equals(row.Table, "AppSearch", StringComparison.Ordinal))
                {
                    columns.Add(keyColumn);
                    values.Add(row.Key);
                }
                for (int field = 0; field < row.Columns.Count; field++)
                {
                    columns.Add(row.Columns[field]);
                    values.Add(row.Values[field]);
                }
                Insert(database, row.Table, columns.ToArray(), values.ToArray());
            }
        }

        private static void InsertMedia(
            Database database,
            IList<MsiMediaSnapshot> media)
        {
            for (int index = 0; index < media.Count; index++)
            {
                Insert(database, "Media",
                    new[] { "DiskId", "LastSequence", "Cabinet" },
                    new[]
                    {
                        media[index].DiskId.ToString(),
                        media[index].LastSequence.ToString(),
                        media[index].Cabinet
                    });
            }
        }

        private static void InsertFileHashes(
            Database database,
            LocalModuleMsiCapabilityProfile profile)
        {
            for (int index = 0; index < profile.Rows.Count; index++)
            {
                MsiProfileRow row = profile.Rows[index];
                if (!string.Equals(row.Table, "File", StringComparison.Ordinal))
                {
                    continue;
                }
                Insert(database, "MsiFileHash",
                    new[] { "File_", "Options", "HashPart1", "HashPart2", "HashPart3", "HashPart4" },
                    new[] { row.Key, "0", "1", "2", "3", "4" });
            }
        }

        private static void EmbedTestCabinets(Database database, string root)
        {
            string firstRoot = Path.Combine(root, "cab1");
            string secondRoot = Path.Combine(root, "cab2");
            Directory.CreateDirectory(firstRoot);
            Directory.CreateDirectory(secondRoot);
            File.WriteAllText(Path.Combine(firstRoot, "first.txt"), "first");
            File.WriteAllText(Path.Combine(secondRoot, "second.txt"), "second");
            string firstCab = Path.Combine(root, "media1.cab");
            string secondCab = Path.Combine(root, "Disk1.cab");
            new CabInfo(firstCab).Pack(firstRoot);
            new CabInfo(secondCab).Pack(secondRoot);
            InsertStream(database, "media1.cab", firstCab);
            InsertStream(database, "Disk1.cab", secondCab);
        }

        private static void InsertStream(
            Database database,
            string name,
            string path)
        {
            using (View view = database.OpenView(
                "SELECT `Name`,`Data` FROM `_Streams`"))
            using (Record record = new Record(2))
            {
                record.SetString(1, name);
                record.SetStream(2, path);
                view.Modify(ViewModifyMode.Insert, record);
            }
        }

        private static void Insert(
            Database database,
            string table,
            string[] columns,
            string[] values)
        {
            StringBuilder query = new StringBuilder("SELECT ");
            for (int index = 0; index < columns.Length; index++)
            {
                if (index > 0) query.Append(',');
                query.Append('`').Append(columns[index]).Append('`');
            }
            query.Append(" FROM `").Append(table).Append('`');
            using (View view = database.OpenView(query.ToString()))
            using (Record record = new Record(columns.Length))
            {
                for (int index = 0; index < values.Length; index++)
                {
                    record.SetString(index + 1, values[index]);
                }
                view.Modify(ViewModifyMode.Insert, record);
            }
        }

        private static string KeyColumn(string table)
        {
            if (table == "Directory") return "Directory";
            if (table == "Registry") return "Registry";
            if (table == "RegLocator") return "Signature_";
            if (table == "File") return "File";
            if (table == "CustomAction") return "Action";
            if (table == "InstallExecuteSequence") return "Action";
            if (table == "AppSearch") return "Property";
            throw new InvalidDataException("Unsupported synthetic table: " + table + ".");
        }
    }
}
