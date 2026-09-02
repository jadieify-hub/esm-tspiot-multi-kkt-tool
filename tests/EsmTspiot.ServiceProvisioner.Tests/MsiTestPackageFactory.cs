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
            Dictionary<string, byte[]> payloads = CreatePayloads();
            profile.FileRowCount = 6;
            profile.MsiFileHashRowCount = 6;
            SetProfileFileSizes(profile, payloads);

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
                Insert(database, "File",
                    new[] { "File", "Component_", "FileName", "FileSize", "Attributes", "Sequence" },
                    new[] { "filSECOND00000000000000000000000001", "cmpSecond", "regime_net_probe.beam", payloads["filSECOND00000000000000000000000001"].Length.ToString(), "512", "2247" });
                stage = "media rows";
                InsertMedia(database, profile.Media);
                stage = "cabinet streams";
                EmbedTestCabinets(database, root, payloads);
                stage = "file hashes";
                InsertFileHashes(database, payloads, root);
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

        internal static string[] ReadCabinetMembers(string path, string streamName)
        {
            string workspace = path + "." + streamName + ".members";
            Directory.CreateDirectory(workspace);
            try
            {
                string cabinetPath = Path.Combine(workspace, streamName);
                using (Database database = new Database(path, DatabaseOpenMode.ReadOnly))
                using (View view = database.OpenView(
                    "SELECT `Data` FROM `_Streams` WHERE `Name` = ?"))
                using (Record parameter = new Record(1))
                {
                    parameter.SetString(1, streamName);
                    view.Execute(parameter);
                    using (Record record = view.Fetch())
                    {
                        if (record == null)
                            throw new InvalidDataException("Cabinet stream missing.");
                        record.GetStream(1, cabinetPath);
                    }
                }
                IList<CabFileInfo> files = new CabInfo(cabinetPath).GetFiles();
                string[] result = new string[files.Count];
                for (int index = 0; index < files.Count; index++)
                    result[index] = files[index].Name;
                return result;
            }
            finally
            {
                if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
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

        internal static void ChangeFileSize(string path, string fileId)
        {
            ChangeInteger(path, "File", "File", fileId, "FileSize");
        }

        internal static void ChangeFileHash(string path, string fileId)
        {
            ChangeInteger(
                path,
                "MsiFileHash",
                "File_",
                fileId,
                "HashPart1");
        }

        internal static void ChangeMediaSequence(string path, int diskId)
        {
            using (Database database = new Database(path, DatabaseOpenMode.Transact))
            using (View view = database.OpenView(
                "SELECT `DiskId`,`LastSequence` FROM `Media` WHERE `DiskId` = " +
                diskId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                view.Execute();
                using (Record record = view.Fetch())
                {
                    if (record == null)
                        throw new InvalidDataException("Media row missing.");
                    record.SetInteger(2, record.GetInteger(2) + 1);
                    view.Modify(ViewModifyMode.Update, record);
                }
                database.Commit();
            }
        }

        internal static void ChangeCabinetFile(string path, string fileId)
        {
            string workspace = path + ".mutate";
            Directory.CreateDirectory(workspace);
            try
            {
                LocalModuleCabinetSnapshot snapshot =
                    LocalModuleCabinetTools.Extract(path, workspace);
                File.AppendAllText(Path.Combine(workspace, fileId), "changed");
                string firstCab = Path.Combine(workspace, "media1.cab");
                string secondCab = Path.Combine(workspace, "Disk1.cab");
                PackForSequence(firstCab, workspace, snapshot.Files, 1, 2246);
                PackForSequence(secondCab, workspace, snapshot.Files, 2247, 2247);
                using (Database database = new Database(path, DatabaseOpenMode.Transact))
                {
                    ReplaceStream(database, "media1.cab", firstCab);
                    ReplaceStream(database, "Disk1.cab", secondCab);
                    database.Commit();
                }
            }
            finally
            {
                if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
            }
        }

        internal static void RemoveFileRow(string path, string fileId)
        {
            using (Database database = new Database(path, DatabaseOpenMode.Transact))
            {
                DeleteRow(database, "MsiFileHash", "File_", fileId);
                DeleteRow(database, "File", "File", fileId);
                database.Commit();
            }
        }

        internal static void AddUnexpectedCabinetFile(string path)
        {
            const string fileId = "filUNEXPECTED000000000000000000000001";
            byte[] content = Encoding.UTF8.GetBytes("unexpected-payload");
            string workspace = path + ".unexpected";
            Directory.CreateDirectory(workspace);
            try
            {
                LocalModuleCabinetSnapshot snapshot =
                    LocalModuleCabinetTools.Extract(path, workspace);
                File.WriteAllBytes(Path.Combine(workspace, fileId), content);
                List<string> names = new List<string>();
                for (int index = 0; index < snapshot.Files.Count; index++)
                    if (snapshot.Files[index].Sequence <= 2246)
                        names.Add(snapshot.Files[index].FileId);
                names.Add(fileId);
                string cabinet = Path.Combine(workspace, "media1.cab");
                new CabInfo(cabinet).PackFiles(workspace, names, names);
                using (Database database = new Database(path, DatabaseOpenMode.Transact))
                {
                    ReplaceStream(database, "media1.cab", cabinet);
                    Insert(database, "File",
                        new[] { "File", "Component_", "FileName", "FileSize", "Attributes", "Sequence" },
                        new[] { fileId, "cmpSecond", "unexpected.bin", content.Length.ToString(), "512", "2246" });
                    int[] parts = MsiFileHashCalculator.Compute(
                        Path.Combine(workspace, fileId));
                    Insert(database, "MsiFileHash",
                        new[] { "File_", "Options", "HashPart1", "HashPart2", "HashPart3", "HashPart4" },
                        new[] { fileId, "0", parts[0].ToString(), parts[1].ToString(), parts[2].ToString(), parts[3].ToString() });
                    database.Commit();
                }
            }
            finally
            {
                if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
            }
        }

        private static void DeleteRow(
            Database database,
            string table,
            string keyColumn,
            string key)
        {
            string query = "SELECT `" + keyColumn + "` FROM `" + table +
                "` WHERE `" + keyColumn + "` = ?";
            using (View view = database.OpenView(query))
            using (Record parameter = new Record(1))
            {
                parameter.SetString(1, key);
                view.Execute(parameter);
                using (Record record = view.Fetch())
                {
                    if (record == null)
                        throw new InvalidDataException(table + " row missing.");
                    view.Modify(ViewModifyMode.Delete, record);
                }
            }
        }

        private static void ChangeInteger(
            string path,
            string table,
            string keyColumn,
            string key,
            string valueColumn)
        {
            string query = "SELECT `" + keyColumn + "`,`" + valueColumn +
                "` FROM `" + table + "` WHERE `" + keyColumn + "` = ?";
            using (Database database = new Database(path, DatabaseOpenMode.Transact))
            using (View view = database.OpenView(query))
            using (Record parameter = new Record(1))
            {
                parameter.SetString(1, key);
                view.Execute(parameter);
                using (Record record = view.Fetch())
                {
                    record.SetInteger(2, record.GetInteger(2) + 1);
                    view.Modify(ViewModifyMode.Update, record);
                }
                database.Commit();
            }
        }

        private static void PackForSequence(
            string cabinetPath,
            string root,
            IList<MsiFilePayloadSnapshot> files,
            int minimum,
            int maximum)
        {
            List<string> names = new List<string>();
            for (int index = 0; index < files.Count; index++)
                if (files[index].Sequence >= minimum &&
                    files[index].Sequence <= maximum)
                    names.Add(files[index].FileId);
            new CabInfo(cabinetPath).PackFiles(root, names, names);
        }

        private static void ReplaceStream(
            Database database,
            string name,
            string path)
        {
            using (View view = database.OpenView(
                "SELECT `Name`,`Data` FROM `_Streams` WHERE `Name` = ?"))
            using (Record parameter = new Record(1))
            {
                parameter.SetString(1, name);
                view.Execute(parameter);
                using (Record record = view.Fetch())
                {
                    record.SetStream(2, path);
                    view.Modify(ViewModifyMode.Update, record);
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
            ImportSchema(database, root, "Upgrade",
                new[] { "UpgradeCode", "VersionMin", "VersionMax", "Language", "Attributes", "Remove", "ActionProperty" },
                new[] { "s38", "S20", "S20", "S255", "i4", "S255", "s72" },
                new[] { "UpgradeCode", "VersionMin", "VersionMax", "Language", "Attributes" });
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
            IDictionary<string, byte[]> payloads,
            string root)
        {
            foreach (KeyValuePair<string, byte[]> item in payloads)
            {
                string payloadRoot = item.Key ==
                    "filSECOND00000000000000000000000001"
                    ? Path.Combine(root, "cab2")
                    : Path.Combine(root, "cab1");
                int[] parts = MsiFileHashCalculator.Compute(
                    Path.Combine(payloadRoot, item.Key));
                Insert(database, "MsiFileHash",
                    new[] { "File_", "Options", "HashPart1", "HashPart2", "HashPart3", "HashPart4" },
                    new[] { item.Key, "0", parts[0].ToString(), parts[1].ToString(), parts[2].ToString(), parts[3].ToString() });
            }
        }

        private static void EmbedTestCabinets(
            Database database,
            string root,
            IDictionary<string, byte[]> payloads)
        {
            string firstRoot = Path.Combine(root, "cab1");
            string secondRoot = Path.Combine(root, "cab2");
            Directory.CreateDirectory(firstRoot);
            Directory.CreateDirectory(secondRoot);
            foreach (KeyValuePair<string, byte[]> item in payloads)
            {
                string targetRoot = item.Key ==
                    "filSECOND00000000000000000000000001"
                    ? secondRoot
                    : firstRoot;
                File.WriteAllBytes(Path.Combine(targetRoot, item.Key), item.Value);
                if (item.Key == "filSECOND00000000000000000000000001")
                    File.WriteAllBytes(Path.Combine(firstRoot, item.Key), item.Value);
            }
            string firstCab = Path.Combine(root, "media1.cab");
            string secondCab = Path.Combine(root, "Disk1.cab");
            string[] firstMembers =
            {
                "fil0C0BA2BF1CF3006FEE6418B1FBB8A2DC",
                "fil4D9BD38000F7BBE9FA37B3949730CD45",
                "fil6C2420445C448A9D193F9397AB772B3F",
                "fil8B086431A47A790C3CAEE2AA56EF7E1C",
                "filB64169385D3728F85488BA683E5B1809",
                "filSECOND00000000000000000000000001"
            };
            new CabInfo(firstCab).PackFiles(
                firstRoot,
                firstMembers,
                firstMembers);
            string[] secondMembers =
            {
                "filSECOND00000000000000000000000001"
            };
            new CabInfo(secondCab).PackFiles(
                secondRoot,
                secondMembers,
                secondMembers);
            InsertStream(database, "media1.cab", firstCab);
            InsertStream(database, "Disk1.cab", secondCab);
        }

        private static Dictionary<string, byte[]> CreatePayloads()
        {
            UTF8Encoding encoding = new UTF8Encoding(false);
            Dictionary<string, byte[]> result =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);
            result.Add("fil4D9BD38000F7BBE9FA37B3949730CD45", encoding.GetBytes(
                "[api]\nport = 5995\n[local]\ndb_url = http://127.0.0.1:5984\n"));
            result.Add("filB64169385D3728F85488BA683E5B1809", encoding.GetBytes(
                "-name regime@127.0.0.1\n"));
            result.Add("fil6C2420445C448A9D193F9397AB772B3F", encoding.GetBytes(
                "[vendor]\nname = CRPT\n[couchdb]\nmax_document_size = 4294967296\n"));
            result.Add("fil8B086431A47A790C3CAEE2AA56EF7E1C", encoding.GetBytes(
                "[chttpd]\nport = 5984\nbind_address = 0.0.0.0\n"));
            result.Add("fil0C0BA2BF1CF3006FEE6418B1FBB8A2DC", encoding.GetBytes(
                "-name yenisei@127.0.0.1\n"));
            result.Add("filSECOND00000000000000000000000001", encoding.GetBytes(
                "unchanged-second-cab"));
            return result;
        }

        private static void SetProfileFileSizes(
            LocalModuleMsiCapabilityProfile profile,
            IDictionary<string, byte[]> payloads)
        {
            for (int index = 0; index < profile.Rows.Count; index++)
            {
                MsiProfileRow row = profile.Rows[index];
                byte[] content;
                if (row.Table != "File" || !payloads.TryGetValue(row.Key, out content))
                    continue;
                for (int column = 0; column < row.Columns.Count; column++)
                    if (row.Columns[column] == "FileSize")
                        row.Values[column] = content.Length.ToString();
            }
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
            if (table == "Upgrade") return "UpgradeCode";
            throw new InvalidDataException("Unsupported synthetic table: " + table + ".");
        }
    }
}
