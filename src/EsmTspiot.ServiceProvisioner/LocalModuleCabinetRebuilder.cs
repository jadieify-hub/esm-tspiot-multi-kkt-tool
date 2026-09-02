using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
            string workspace = transformedMsiPath + ".cabwork-" +
                Guid.NewGuid().ToString("N");
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
                for (int index = 0; index < sourceCabinets.Files.Count; index++)
                {
                    MsiFilePayloadSnapshot file = sourceCabinets.Files[index];
                    if (!LocalModuleConfigBytes.IsGenerated(file.FileId)) continue;
                    string filePath = Path.Combine(extracted, file.FileId);
                    byte[] generated = LocalModuleConfigBytes.Transform(
                        file.FileId,
                        File.ReadAllBytes(filePath),
                        plan);
                    File.WriteAllBytes(filePath, generated);
                    plan.ExpectedConfigFiles.Add(file.FileId, generated);
                }
                if (plan.ExpectedConfigFiles.Count != 4)
                {
                    throw new InvalidDataException(
                        "Exactly four mutable local-module config files must be generated.");
                }

                string firstCab = Path.Combine(workspace, "media1.cab");
                string secondCab = Path.Combine(workspace, "Disk1.cab");
                stage = "pack first cabinet";
                PackCabinet(
                    firstCab,
                    extracted,
                    sourceCabinets.Files,
                    1,
                    2246);
                stage = "pack second cabinet";
                PackCabinet(
                    secondCab,
                    extracted,
                    sourceCabinets.Files,
                    2247,
                    2247);
                stage = "update output MSI database";
                UpdateOutputDatabase(
                    transformedMsiPath,
                    firstCab,
                    secondCab,
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
            IList<MsiFilePayloadSnapshot> files,
            int minimumSequence,
            int maximumSequence)
        {
            List<string> names = new List<string>();
            for (int index = 0; index < files.Count; index++)
            {
                int sequence = files[index].Sequence;
                if (sequence >= minimumSequence && sequence <= maximumSequence)
                {
                    names.Add(files[index].FileId);
                }
            }
            if (names.Count == 0)
            {
                throw new InvalidDataException("MSI cabinet sequence is empty.");
            }
            new CabInfo(cabinetPath).PackFiles(sourceRoot, names, names);
        }

        private static void UpdateOutputDatabase(
            string path,
            string firstCab,
            string secondCab,
            LocalModuleMsiTransformPlan plan,
            string extractedRoot)
        {
            using (Database database = new Database(path, DatabaseOpenMode.Transact))
            {
                UpdateStream(database, "media1.cab", firstCab);
                UpdateStream(database, "Disk1.cab", secondCab);
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
        }

        internal IList<MsiFilePayloadSnapshot> Files { get; private set; }
        internal IList<MsiMediaSnapshot> Media { get; private set; }
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
                ExtractCabinets(database, result.Media, outputRoot);
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
            string outputRoot)
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
                new CabInfo(cabinetPath).Unpack(outputRoot);
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

    internal static class LocalModuleConfigBytes
    {
        private static readonly string[] ConfigIds =
        {
            "fil4D9BD38000F7BBE9FA37B3949730CD45",
            "filB64169385D3728F85488BA683E5B1809",
            "fil8B086431A47A790C3CAEE2AA56EF7E1C",
            "fil0C0BA2BF1CF3006FEE6418B1FBB8A2DC"
        };

        internal static bool IsGenerated(string fileId)
        {
            for (int index = 0; index < ConfigIds.Length; index++)
                if (ConfigIds[index] == fileId) return true;
            return false;
        }

        internal static byte[] Transform(
            string fileId,
            byte[] source,
            LocalModuleMsiTransformPlan plan)
        {
            UTF8Encoding utf8 = new UTF8Encoding(false, true);
            string text = utf8.GetString(source);
            if (fileId == ConfigIds[0])
            {
                text = ReplaceOnce(
                    fileId,
                    text,
                    "port = 5995",
                    "port = " + plan.ApiPort);
                text = ReplaceOnce(
                    fileId,
                    text,
                    "db_url = http://127.0.0.1:5984",
                    "db_url = http://127.0.0.1:" + plan.DatabasePort);
            }
            else if (fileId == ConfigIds[1])
                text = ReplaceOnce(
                    fileId,
                    text,
                    "-name regime@127.0.0.1",
                    "-name " + plan.ApiNodeName);
            else if (fileId == ConfigIds[2])
            {
                text = ReplaceOnce(
                    fileId,
                    text,
                    "port = 5984",
                    "port = " + plan.DatabasePort);
                text = ReplaceOnce(
                    fileId,
                    text,
                    "bind_address = 0.0.0.0",
                    "bind_address = 127.0.0.1");
            }
            else if (fileId == ConfigIds[3])
                text = ReplaceOnce(
                    fileId,
                    text,
                    "-name yenisei@127.0.0.1",
                    "-name " + plan.DatabaseNodeName);
            else
                throw new InvalidDataException("Unexpected generated config identity.");
            return utf8.GetBytes(text);
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
