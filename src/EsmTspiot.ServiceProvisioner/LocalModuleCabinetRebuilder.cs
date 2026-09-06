using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WixToolset.Dtf.Compression;
using WixToolset.Dtf.Compression.Cab;
using WixToolset.Dtf.WindowsInstaller;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleCabinetRebuilder
    {
        internal LocalModuleMsiDatabaseSnapshot Rebuild(
            VerifiedLocalModulePackage source,
            string transformedMsiPath,
            LocalModuleMsiTransformPlan plan)
        {
            string stage = "create workspace";
            string workspace = transformedMsiPath + ".cabwork";
            if (Directory.Exists(workspace) || File.Exists(workspace))
                throw new IOException("MSI cabinet workspace already exists.");
            Directory.CreateDirectory(workspace);
            try
            {
                stage = "extract source cabinets";
                string extracted = Path.Combine(workspace, "files");
                Directory.CreateDirectory(extracted);
                LocalModuleCabinetSnapshot sourceCabinets =
                    LocalModuleCabinetTools.Extract(source.FullPath, extracted);
                LocalModuleMsiDatabaseSnapshot sourcePayload =
                    CreateSourcePayloadSnapshot(source, sourceCabinets);

                plan.ExpectedConfigFiles.Clear();
                stage = "generate isolated configuration files";
                IDictionary<string, string> configFiles =
                    LocalModuleConfigBytes.MapConfigurationFiles(
                        source.CapabilityProfile);
                List<LocalModuleConfigRole> roles =
                    new List<LocalModuleConfigRole>();
                for (int index = 0; index < sourceCabinets.Files.Count; index++)
                {
                    MsiFilePayloadSnapshot file = sourceCabinets.Files[index];
                    string longFileName;
                    if (!configFiles.TryGetValue(file.FileId, out longFileName))
                        continue;
                    string filePath = Path.Combine(extracted, file.FileId);
                    byte[] original = File.ReadAllBytes(filePath);
                    LocalModuleConfigRole role = LocalModuleConfigBytes.Classify(
                        longFileName,
                        original);
                    if (role == LocalModuleConfigRole.None) continue;
                    if (roles.Contains(role))
                        throw new InvalidDataException(
                            "В пакете несколько файлов конфигурации одного назначения.");
                    roles.Add(role);
                    byte[] generated = LocalModuleConfigBytes.Transform(
                        role,
                        file.FileId,
                        original,
                        plan);
                    File.WriteAllBytes(filePath, generated);
                    plan.ExpectedConfigFiles.Add(file.FileId, generated);
                }
                if (plan.ExpectedConfigFiles.Count != 4)
                {
                    throw new InvalidDataException(
                        "Должны быть подготовлены ровно четыре файла конфигурации ЛМ.");
                }

                stage = "pack cabinets";
                IDictionary<string, string> packedCabinets = PackCabinets(
                    workspace,
                    extracted,
                    sourceCabinets);
                stage = "update output MSI database";
                UpdateOutputDatabase(
                    transformedMsiPath,
                    packedCabinets,
                    plan,
                    extracted);
                return sourcePayload;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "Local-module cabinet rebuild failed at stage '" +
                    stage + "'.",
                    exception);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(workspace))
                        Directory.Delete(workspace, true);
                }
                catch
                {
                }
            }
        }

        private static LocalModuleMsiDatabaseSnapshot CreateSourcePayloadSnapshot(
            VerifiedLocalModulePackage source,
            LocalModuleCabinetSnapshot cabinets)
        {
            LocalModuleMsiDatabaseSnapshot result =
                new LocalModuleMsiDatabaseSnapshot
                {
                    FileRowCount = source.DatabaseSnapshot.FileRowCount,
                    MsiFileHashRowCount =
                        source.DatabaseSnapshot.MsiFileHashRowCount
                };
            for (int index = 0; index < cabinets.Media.Count; index++)
            {
                result.Media.Add(new MsiMediaSnapshot
                {
                    DiskId = cabinets.Media[index].DiskId,
                    LastSequence = cabinets.Media[index].LastSequence,
                    Cabinet = cabinets.Media[index].Cabinet
                });
            }
            CopyFiles(cabinets.Files, result.Files);
            foreach (KeyValuePair<string, IList<string>> cabinet in
                cabinets.CabinetMembers)
            {
                result.CabinetMembers.Add(
                    cabinet.Key,
                    new List<string>(cabinet.Value));
            }
            return result;
        }

        private static void CopyFiles(
            IList<MsiFilePayloadSnapshot> source,
            IList<MsiFilePayloadSnapshot> target)
        {
            for (int index = 0; index < source.Count; index++)
            {
                MsiFilePayloadSnapshot item = source[index];
                target.Add(new MsiFilePayloadSnapshot
                {
                    FileId = item.FileId,
                    Sequence = item.Sequence,
                    FileSize = item.FileSize,
                    ActualSize = item.ActualSize,
                    HashParts = item.HashParts == null
                        ? null
                        : (int[])item.HashParts.Clone(),
                    ComputedHashParts = item.ComputedHashParts == null
                        ? null
                        : (int[])item.ComputedHashParts.Clone(),
                    Sha256 = item.Sha256
                });
            }
        }

        private static void PackCabinet(
            string cabinetPath,
            string sourceRoot,
            IList<string> members)
        {
            if (members == null || members.Count == 0)
            {
                throw new InvalidDataException("MSI cabinet sequence is empty.");
            }
            // Для временного MSI на слабой кассе скорость важнее размера CAB.
            new CabInfo(cabinetPath).PackFiles(
                sourceRoot, members, members, CompressionLevel.Min, null);
        }

        // Число встроенных cab-архивов берётся из самого пакета: вендор может
        // изменить их количество и имена в новой версии.
        private static IDictionary<string, string> PackCabinets(
            string workspace,
            string extractedRoot,
            LocalModuleCabinetSnapshot sourceCabinets)
        {
            Dictionary<string, string> result =
                new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, IList<string>> cabinet in
                sourceCabinets.CabinetMembers)
            {
                string cabinetPath = Path.Combine(workspace, cabinet.Key);
                PackCabinet(cabinetPath, extractedRoot, cabinet.Value);
                result.Add(cabinet.Key, cabinetPath);
            }
            if (result.Count == 0)
            {
                throw new InvalidDataException(
                    "В пакете нет встроенных cab-архивов.");
            }
            return result;
        }

        private static void UpdateOutputDatabase(
            string path,
            IDictionary<string, string> packedCabinets,
            LocalModuleMsiTransformPlan plan,
            string extractedRoot)
        {
            using (Database database = new Database(path, DatabaseOpenMode.Transact))
            {
                foreach (KeyValuePair<string, string> cabinet in packedCabinets)
                {
                    UpdateStream(database, cabinet.Key, cabinet.Value);
                }
                foreach (KeyValuePair<string, byte[]> item in
                    plan.ExpectedConfigFiles)
                {
                    UpdateFile(
                        database,
                        item.Key,
                        item.Value,
                        Path.Combine(extractedRoot, item.Key));
                }
                database.Commit();
            }
        }

        private static void UpdateStream(
            Database database,
            string name,
            string cabinetPath)
        {
            using (View view = database.OpenView(
                "SELECT `Name`,`Data` FROM `_Streams` WHERE `Name` = ?"))
            using (Record parameter = new Record(1))
            {
                parameter.SetString(1, name);
                view.Execute(parameter);
                using (Record record = view.Fetch())
                {
                    if (record == null)
                        throw new InvalidDataException("MSI cabinet stream is missing.");
                    record.SetStream(2, cabinetPath);
                    view.Modify(ViewModifyMode.Update, record);
                }
            }
        }

        private static void UpdateFile(
            Database database,
            string fileId,
            byte[] content,
            string contentPath)
        {
            UpdateInteger(
                database,
                "File",
                "File",
                fileId,
                "FileSize",
                content.Length);
            int[] parts = MsiFileHashCalculator.Compute(contentPath);
            string[] columns =
            {
                "HashPart1", "HashPart2", "HashPart3", "HashPart4"
            };
            for (int index = 0; index < columns.Length; index++)
            {
                UpdateInteger(
                    database,
                    "MsiFileHash",
                    "File_",
                    fileId,
                    columns[index],
                    parts[index]);
            }
        }

        private static void UpdateInteger(
            Database database,
            string table,
            string keyColumn,
            string key,
            string valueColumn,
            int value)
        {
            string query = "SELECT `" + keyColumn + "`,`" + valueColumn +
                "` FROM `" + table + "` WHERE `" + keyColumn + "` = ?";
            using (View view = database.OpenView(query))
            using (Record parameter = new Record(1))
            {
                parameter.SetString(1, key);
                view.Execute(parameter);
                using (Record record = view.Fetch())
                {
                    if (record == null)
                        throw new InvalidDataException(table + ":" + key + " missing.");
                    record.SetInteger(2, value);
                    view.Modify(ViewModifyMode.Update, record);
                }
            }
        }
    }

    internal sealed class LocalModuleCabinetSnapshot
    {
        internal LocalModuleCabinetSnapshot()
        {
            Files = new List<MsiFilePayloadSnapshot>();
            Media = new List<MsiMediaSnapshot>();
            CabinetMembers = new Dictionary<string, IList<string>>(
                StringComparer.Ordinal);
        }

        internal IList<MsiFilePayloadSnapshot> Files { get; private set; }
        internal IList<MsiMediaSnapshot> Media { get; private set; }
        internal IDictionary<string, IList<string>> CabinetMembers { get; private set; }
    }

    internal static class LocalModuleCabinetTools
    {
        internal static LocalModuleCabinetSnapshot Extract(
            string msiPath,
            string outputRoot)
        {
            LocalModuleCabinetSnapshot result = new LocalModuleCabinetSnapshot();
            using (Database database = new Database(
                msiPath,
                DatabaseOpenMode.ReadOnly))
            {
                ReadMedia(database, result.Media);
                ExtractCabinets(
                    database,
                    result.Media,
                    outputRoot,
                    result.CabinetMembers);
                ReadFiles(database, outputRoot, result.Files);
            }
            return result;
        }

        private static void ReadMedia(
            Database database,
            IList<MsiMediaSnapshot> media)
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
                        media.Add(new MsiMediaSnapshot
                        {
                            DiskId = record.GetInteger(1),
                            LastSequence = record.GetInteger(2),
                            Cabinet = record.GetString(3)
                        });
                    }
                }
            }
        }

        private static void ExtractCabinets(
            Database database,
            IList<MsiMediaSnapshot> media,
            string outputRoot,
            IDictionary<string, IList<string>> cabinetMembers)
        {
            for (int index = 0; index < media.Count; index++)
            {
                string cabinet = media[index].Cabinet;
                if (string.IsNullOrEmpty(cabinet) || cabinet[0] != '#')
                    throw new InvalidDataException("Only embedded MSI cabinets are supported.");
                string streamName = cabinet.Substring(1);
                string cabinetPath = Path.Combine(outputRoot, streamName);
                using (View view = database.OpenView(
                    "SELECT `Data` FROM `_Streams` WHERE `Name` = ?"))
                using (Record parameter = new Record(1))
                {
                    parameter.SetString(1, streamName);
                    view.Execute(parameter);
                    using (Record record = view.Fetch())
                    {
                        if (record == null)
                            throw new InvalidDataException("Embedded cabinet is missing.");
                        record.GetStream(1, cabinetPath);
                    }
                }
                CabInfo info = new CabInfo(cabinetPath);
                IList<CabFileInfo> files = info.GetFiles();
                List<string> names = new List<string>(files.Count);
                for (int member = 0; member < files.Count; member++)
                    names.Add(files[member].Name);
                cabinetMembers.Add(streamName, names);
                info.Unpack(outputRoot);
                File.Delete(cabinetPath);
            }
        }

        private static void ReadFiles(
            Database database,
            string outputRoot,
            IList<MsiFilePayloadSnapshot> files)
        {
            Dictionary<string, int[]> hashes = ReadHashes(database);
            using (View view = database.OpenView(
                "SELECT `File`,`FileSize`,`Sequence` FROM `File`"))
            {
                view.Execute();
                Record record;
                while ((record = view.Fetch()) != null)
                {
                    using (record)
                    {
                        string id = record.GetString(1);
                        string path = Path.Combine(outputRoot, id);
                        int[] parts;
                        if (!File.Exists(path))
                            throw new InvalidDataException("MSI file payload is incomplete.");
                        hashes.TryGetValue(id, out parts);
                        byte[] content = File.ReadAllBytes(path);
                        files.Add(new MsiFilePayloadSnapshot
                        {
                            FileId = id,
                            FileSize = record.GetInteger(2),
                            ActualSize = content.Length,
                            Sequence = record.GetInteger(3),
                            HashParts = parts,
                            ComputedHashParts = parts == null
                                ? null
                                : MsiFileHashCalculator.Compute(path),
                            Sha256 = Sha256(content)
                        });
                    }
                }
            }
        }

        private static Dictionary<string, int[]> ReadHashes(Database database)
        {
            Dictionary<string, int[]> result =
                new Dictionary<string, int[]>(StringComparer.Ordinal);
            using (View view = database.OpenView(
                "SELECT `File_`,`HashPart1`,`HashPart2`,`HashPart3`,`HashPart4` " +
                "FROM `MsiFileHash`"))
            {
                view.Execute();
                Record record;
                while ((record = view.Fetch()) != null)
                {
                    using (record)
                    {
                        result.Add(record.GetString(1), new[]
                        {
                            record.GetInteger(2), record.GetInteger(3),
                            record.GetInteger(4), record.GetInteger(5)
                        });
                    }
                }
            }
            return result;
        }

        internal static string Sha256(byte[] content)
        {
            byte[] digest;
            using (SHA256 algorithm = SHA256.Create())
                digest = algorithm.ComputeHash(content);
            StringBuilder result = new StringBuilder(64);
            for (int index = 0; index < digest.Length; index++)
                result.Append(digest[index].ToString("x2"));
            return result.ToString();
        }
    }

    internal enum LocalModuleConfigRole
    {
        None = 0,
        ApiLocalIni = 1,
        ApiVmArgs = 2,
        DatabaseLocalIni = 3,
        DatabaseVmArgs = 4
    }

    /// <summary>
    /// Шаблоны конфигурации опознаются по имени файла и вендорским значениям
    /// по умолчанию внутри, а не по идентификатору File конкретной версии.
    /// </summary>
    internal static class LocalModuleConfigBytes
    {
        private const string ApiLocalIniName = "local.ini.dist";
        private const string VmArgsName = "vm.args.dist";
        private const string ApiPortMarker = "port = 5995";
        private const string ApiDatabaseUrlMarker =
            "db_url = http://127.0.0.1:5984";
        private const string DatabasePortMarker = "port = 5984";
        private const string DatabaseBindMarker = "bind_address = 0.0.0.0";
        private const string ApiNodeMarker = "-name regime@127.0.0.1";
        private const string DatabaseNodeMarker = "-name yenisei@127.0.0.1";

        internal static IDictionary<string, string> MapConfigurationFiles(
            LocalModuleMsiCapabilityProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            Dictionary<string, string> result =
                new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < profile.Rows.Count; index++)
            {
                MsiProfileRow row = profile.Rows[index];
                if (row == null || !string.Equals(
                        row.Table,
                        "File",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                string name = LocalModuleMsiProfileReader.LongFileName(
                    row.Value("FileName"));
                if (!result.ContainsKey(row.Key)) result.Add(row.Key, name);
            }
            return result;
        }

        internal static LocalModuleConfigRole Classify(
            string longFileName,
            byte[] content)
        {
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(content);
            }
            catch (ArgumentException)
            {
                return LocalModuleConfigRole.None;
            }
            if (string.Equals(
                    longFileName,
                    ApiLocalIniName,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (Contains(text, ApiPortMarker) &&
                    Contains(text, ApiDatabaseUrlMarker))
                    return LocalModuleConfigRole.ApiLocalIni;
                if (Contains(text, DatabasePortMarker) &&
                    Contains(text, DatabaseBindMarker))
                    return LocalModuleConfigRole.DatabaseLocalIni;
                return LocalModuleConfigRole.None;
            }
            if (string.Equals(
                    longFileName,
                    VmArgsName,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (Contains(text, ApiNodeMarker))
                    return LocalModuleConfigRole.ApiVmArgs;
                if (Contains(text, DatabaseNodeMarker))
                    return LocalModuleConfigRole.DatabaseVmArgs;
                return LocalModuleConfigRole.None;
            }
            return LocalModuleConfigRole.None;
        }

        internal static byte[] Transform(
            LocalModuleConfigRole role,
            string fileId,
            byte[] source,
            LocalModuleMsiTransformPlan plan)
        {
            UTF8Encoding utf8 = new UTF8Encoding(false, true);
            string text = utf8.GetString(source);
            if (role == LocalModuleConfigRole.ApiLocalIni)
            {
                text = ReplaceOnce(
                    fileId,
                    text,
                    ApiPortMarker,
                    "port = " + plan.ApiPort);
                text = ReplaceOnce(
                    fileId,
                    text,
                    ApiDatabaseUrlMarker,
                    "db_url = http://127.0.0.1:" + plan.DatabasePort);
            }
            else if (role == LocalModuleConfigRole.ApiVmArgs)
                text = ReplaceOnce(
                    fileId,
                    text,
                    ApiNodeMarker,
                    "-name " + plan.ApiNodeName);
            else if (role == LocalModuleConfigRole.DatabaseLocalIni)
            {
                text = ReplaceOnce(
                    fileId,
                    text,
                    DatabasePortMarker,
                    "port = " + plan.DatabasePort);
                text = ReplaceOnce(
                    fileId,
                    text,
                    DatabaseBindMarker,
                    "bind_address = 127.0.0.1");
            }
            else if (role == LocalModuleConfigRole.DatabaseVmArgs)
                text = ReplaceOnce(
                    fileId,
                    text,
                    DatabaseNodeMarker,
                    "-name " + plan.DatabaseNodeName);
            else
                throw new InvalidDataException(
                    "Неизвестное назначение файла конфигурации ЛМ.");
            return utf8.GetBytes(text);
        }

        private static bool Contains(string text, string marker)
        {
            return text.IndexOf(marker, StringComparison.Ordinal) >= 0;
        }

        private static string ReplaceOnce(
            string fileId,
            string value,
            string expected,
            string replacement)
        {
            int first = value.IndexOf(expected, StringComparison.Ordinal);
            if (first < 0 || value.IndexOf(
                    expected,
                    first + expected.Length,
                    StringComparison.Ordinal) >= 0)
                throw new InvalidDataException(
                    "Generated config schema mismatch: " + fileId +
                    " expected one '" + expected + "'.");
            return value.Substring(0, first) + replacement +
                value.Substring(first + expected.Length);
        }
    }
}
