using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class ManagedLocalModuleInventoryItem
    {
        internal string KktSerial { get; set; }
        internal string Inn { get; set; }
        internal int KktOrdinal { get; set; }
        internal int SoftwarePort { get; set; }
        internal string Endpoint { get; set; }
        internal string State { get; set; }
        internal bool CleanupPending { get; set; }
        internal LmManifestFingerprint ManagedStateFingerprint { get; set; }
        internal ManagedKktAssignment KktAssignment { get; set; }
        internal ManagedLocalModuleAssignment ModuleAssignment { get; set; }
    }

    internal sealed class ManagedLocalModuleInventorySnapshot
    {
        internal ManagedLocalModuleInventorySnapshot()
        {
            Kkts = new List<ManagedKktAssignment>();
            Modules = new List<ManagedLocalModuleAssignment>();
            Items = new List<ManagedLocalModuleInventoryItem>();
        }

        internal IList<ManagedKktAssignment> Kkts { get; private set; }
        internal IList<ManagedLocalModuleAssignment> Modules { get; private set; }
        internal IList<ManagedLocalModuleInventoryItem> Items { get; private set; }

        internal ManagedLocalModuleInventoryItem Find(string serial)
        {
            for (int index = 0; index < Items.Count; index++)
            {
                if (string.Equals(
                    Items[index].KktSerial,
                    serial,
                    StringComparison.Ordinal))
                {
                    return Items[index];
                }
            }
            return null;
        }
    }

    internal sealed class ManagedLocalModuleInventoryReader
    {
        private const string StackMarker =
            "KRS.MultiKKT.ManagedKkt.Stack.v1";
        private const string InstanceMarker =
            "KRS.MultiKKT.LocalModule.Instance.v1";
        private readonly string _machineRoot;
        private readonly ReadOnlyWindowsServiceReader _services;

        internal ManagedLocalModuleInventoryReader()
            : this(Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "KRS",
                "MultiKKT"))
        {
        }

        internal ManagedLocalModuleInventoryReader(string machineRoot)
        {
            _machineRoot = Path.GetFullPath(machineRoot)
                .TrimEnd(Path.DirectorySeparatorChar);
            _services = new ReadOnlyWindowsServiceReader();
        }

        internal ManagedLocalModuleInventorySnapshot Read()
        {
            ManagedLocalModuleInventorySnapshot result =
                new ManagedLocalModuleInventorySnapshot();
            string modulesRoot = Path.Combine(_machineRoot, "LocalModules");
            string stacksRoot = Path.Combine(_machineRoot, "ManagedKktStacks");
            Dictionary<string, InstanceProjection> modules =
                ReadModules(modulesRoot);
            AddModuleAssignments(result, modules);
            HashSet<string> usedSerials =
                new HashSet<string>(StringComparer.Ordinal);

            if (Directory.Exists(stacksRoot))
            {
                EnsureRegular(stacksRoot, true);
                string[] directories = Directory.GetDirectories(stacksRoot);
                Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < directories.Length; index++)
                {
                    string directory = directories[index];
                    EnsureRegular(directory, true);
                    string path = Path.Combine(directory, "manifest.json");
                    EnsureRegular(path, false);
                    StackProjection stack = Read<StackProjection>(path);
                    ValidateStack(stack, Path.GetFileName(directory));
                    InstanceProjection module;
                    if (!modules.TryGetValue(
                            stack.LocalModuleInstanceId,
                            out module) ||
                        !string.Equals(
                            stack.Inn,
                            module.Inn,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "Запись ККТ ссылается на другой управляемый ЛМ.");
                    }

                    ManagedKktAssignment kkt = new ManagedKktAssignment
                    {
                        KktSerial = stack.KktSerial,
                        KktInn = stack.Inn,
                        KktOrdinal = stack.KktOrdinal,
                        LocalModuleInstanceId =
                            stack.LocalModuleInstanceId,
                        GrpcPort = stack.ControllerGrpcPort,
                        RestPort = stack.ControllerRestPort
                    };
                    ManagedLocalModuleAssignment assignment =
                        ToAssignment(module);
                    string state = GetState(stack, module);
                    result.Kkts.Add(kkt);
                    usedSerials.Add(stack.KktSerial);
                    result.Items.Add(new ManagedLocalModuleInventoryItem
                    {
                        KktSerial = stack.KktSerial,
                        Inn = stack.Inn,
                        KktOrdinal = stack.KktOrdinal,
                        Endpoint = "127.0.0.1:" + module.ApiPort.ToString(
                            CultureInfo.InvariantCulture),
                        State = state,
                        CleanupPending = string.Equals(
                            state,
                            "Требуется очистка",
                            StringComparison.Ordinal),
                        ManagedStateFingerprint = new LmManifestFingerprint
                        {
                            Sha256 = ComputeSha256(path)
                        },
                        KktAssignment = kkt,
                        ModuleAssignment = assignment
                    });
                }
            }
            ReadRemovalJournals(result, modules, usedSerials);
            return result;
        }

        private void ReadRemovalJournals(
            ManagedLocalModuleInventorySnapshot result,
            IDictionary<string, InstanceProjection> modules,
            ISet<string> usedSerials)
        {
            string root = Path.Combine(
                _machineRoot,
                "ManagedLocalModuleRemoval");
            if (!Directory.Exists(root)) return;
            EnsureRegular(root, true);
            string[] files = Directory.GetFiles(root, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < files.Length; index++)
            {
                string path = files[index];
                EnsureRegular(path, false);
                RemovalProjection journal = Read<RemovalProjection>(path);
                ValidateRemovalJournal(journal, Path.GetFileName(path));
                if (!usedSerials.Add(journal.KktSerial))
                {
                    continue;
                }
                InstanceProjection module;
                modules.TryGetValue(journal.InstanceId, out module);
                result.Items.Add(new ManagedLocalModuleInventoryItem
                {
                    KktSerial = journal.KktSerial,
                    Inn = module == null ? string.Empty : module.Inn,
                    KktOrdinal = 0,
                    Endpoint = module == null
                        ? string.Empty
                        : "127.0.0.1:" + module.ApiPort.ToString(
                            CultureInfo.InvariantCulture),
                    State = "Требуется очистка",
                    CleanupPending = true,
                    ManagedStateFingerprint = new LmManifestFingerprint
                    {
                        Sha256 = ComputeSha256(path)
                    },
                    ModuleAssignment = module == null
                        ? null
                        : ToAssignment(module)
                });
            }
        }

        private static void AddModuleAssignments(
            ManagedLocalModuleInventorySnapshot result,
            IDictionary<string, InstanceProjection> modules)
        {
            List<ManagedLocalModuleAssignment> assignments =
                new List<ManagedLocalModuleAssignment>();
            foreach (InstanceProjection module in modules.Values)
            {
                assignments.Add(ToAssignment(module));
            }
            assignments.Sort(delegate(
                ManagedLocalModuleAssignment left,
                ManagedLocalModuleAssignment right)
            {
                int ordinal = left.ModuleOrdinal.CompareTo(
                    right.ModuleOrdinal);
                return ordinal != 0
                    ? ordinal
                    : string.CompareOrdinal(left.Inn, right.Inn);
            });
            for (int index = 0; index < assignments.Count; index++)
            {
                result.Modules.Add(assignments[index]);
            }
        }

        private Dictionary<string, InstanceProjection> ReadModules(
            string root)
        {
            Dictionary<string, InstanceProjection> result =
                new Dictionary<string, InstanceProjection>(StringComparer.Ordinal);
            if (!Directory.Exists(root)) return result;
            EnsureRegular(root, true);
            string[] directories = Directory.GetDirectories(root);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < directories.Length; index++)
            {
                EnsureRegular(directories[index], true);
                string path = Path.Combine(directories[index], "manifest.json");
                EnsureRegular(path, false);
                InstanceProjection item = Read<InstanceProjection>(path);
                ValidateModule(item, Path.GetFileName(directories[index]));
                if (result.ContainsKey(item.InstanceId))
                {
                    throw new InvalidDataException(
                        "Идентификатор управляемого ЛМ повторяется.");
                }
                result.Add(item.InstanceId, item);
            }
            return result;
        }

        private string GetState(
            StackProjection stack,
            InstanceProjection module)
        {
            if (stack.State == 4 || module.State == 4)
            {
                return "Требуется очистка";
            }
            if (stack.State != 2 || module.State != 2)
            {
                return "Требуется внимание";
            }
            ReadOnlyWindowsService database =
                _services.Query(module.DatabaseServiceName);
            ReadOnlyWindowsService api = _services.Query(module.ApiServiceName);
            if (database == null || api == null)
            {
                return "Службы отсутствуют";
            }
            return database.State == 4 && api.State == 4
                ? "Запущен"
                : "Остановлен";
        }

        private static ManagedLocalModuleAssignment ToAssignment(
            InstanceProjection module)
        {
            return new ManagedLocalModuleAssignment
            {
                Inn = module.Inn,
                ModuleOrdinal = module.LocalModuleOrdinal,
                InstanceId = module.InstanceId,
                ApiPort = module.ApiPort,
                DatabasePort = module.DatabasePort,
                EpmdPort = module.EpmdPort,
                RuntimeVersion = SupportedLocalModulePackageIdentity.LegacyManagedRuntimeVersion
            };
        }

        private void EnsureRegular(string path, bool directory)
        {
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(
                    _machineRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(full, _machineRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Путь инвентаря выходит за канонический корень.");
            }
            if ((directory ? !Directory.Exists(full) : !File.Exists(full)) ||
                (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Инвентарь содержит отсутствующий или перенаправленный объект.");
            }
        }

        private static T Read<T>(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                object value = new DataContractJsonSerializer(typeof(T))
                    .ReadObject(stream);
                if (!(value is T))
                {
                    throw new InvalidDataException(
                        "Манифест имеет неожиданный формат.");
                }
                return (T)value;
            }
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                StringBuilder result = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    result.Append(hash[index].ToString("x2"));
                }
                return result.ToString();
            }
        }

        private static void ValidateStack(
            StackProjection item,
            string directoryName)
        {
            if (item == null || item.SchemaVersion != 1 ||
                !string.Equals(item.OwnershipMarker, StackMarker, StringComparison.Ordinal) ||
                !IsDigits(item.KktSerial, 14) ||
                !string.Equals(directoryName, "kkt-" + item.KktSerial, StringComparison.Ordinal) ||
                !IsInn(item.Inn) || item.KktOrdinal < 1 || item.KktOrdinal > 32 ||
                string.IsNullOrWhiteSpace(item.LocalModuleInstanceId) ||
                item.ControllerGrpcPort < 1 || item.ControllerGrpcPort > 65535 ||
                item.ControllerRestPort < 1 || item.ControllerRestPort > 65535)
            {
                throw new InvalidDataException(
                    "Манифест комплекта ККТ не прошёл проверку.");
            }
        }

        private static void ValidateModule(
            InstanceProjection item,
            string directoryName)
        {
            if (item == null || item.SchemaVersion != 1 ||
                !string.Equals(item.OwnershipMarker, InstanceMarker, StringComparison.Ordinal) ||
                !string.Equals(item.InstanceId, directoryName, StringComparison.Ordinal) ||
                !IsInn(item.Inn) || item.LocalModuleOrdinal < 1 ||
                item.LocalModuleOrdinal > 32 ||
                item.ApiPort < 1 || item.ApiPort > 65535 ||
                item.DatabasePort < 1 || item.DatabasePort > 65535 ||
                item.EpmdPort < 1 || item.EpmdPort > 65535 ||
                string.IsNullOrWhiteSpace(item.ApiServiceName) ||
                string.IsNullOrWhiteSpace(item.DatabaseServiceName))
            {
                throw new InvalidDataException(
                    "Манифест управляемого ЛМ не прошёл проверку.");
            }
        }

        private static void ValidateRemovalJournal(
            RemovalProjection item,
            string fileName)
        {
            if (item == null || item.SchemaVersion != 1 ||
                !string.Equals(
                    item.OwnershipMarker,
                    "KRS.MultiKKT.ManagedLocalModule.Removal.v1",
                    StringComparison.Ordinal) ||
                !IsLowerHex(item.OperationId, 32) ||
                !IsDigits(item.KktSerial, 14) ||
                !string.Equals(
                    fileName,
                    item.KktSerial + ".json",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    item.StackId,
                    "kkt-" + item.KktSerial,
                    StringComparison.Ordinal) ||
                !IsLowerHex(item.StackOwnershipNonce, 32) ||
                string.IsNullOrWhiteSpace(item.InstanceId) ||
                !IsLowerHex(item.InstanceOwnershipNonce, 32) ||
                string.IsNullOrWhiteSpace(item.RuntimeId) ||
                !IsLowerHex(item.RuntimeOwnershipNonce, 32))
            {
                throw new InvalidDataException(
                    "Журнал очистки управляемого ЛМ не прошёл проверку.");
            }
        }

        private static bool IsLowerHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!((current >= '0' && current <= '9') ||
                    (current >= 'a' && current <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsInn(string value)
        {
            return IsDigits(value, 10) || IsDigits(value, 12);
        }

        private static bool IsDigits(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9') return false;
            }
            return true;
        }

        [DataContract]
        private sealed class StackProjection
        {
            [DataMember(Order = 1)] internal int SchemaVersion { get; set; }
            [DataMember(Order = 2)] internal string OwnershipMarker { get; set; }
            [DataMember(Order = 3)] internal string StackId { get; set; }
            [DataMember(Order = 4)] internal string KktSerial { get; set; }
            [DataMember(Order = 5)] internal string Inn { get; set; }
            [DataMember(Order = 6)] internal int KktOrdinal { get; set; }
            [DataMember(Order = 7)] internal string LocalModuleInstanceId { get; set; }
            [DataMember(Order = 10)] internal int ControllerGrpcPort { get; set; }
            [DataMember(Order = 11)] internal int ControllerRestPort { get; set; }
            [DataMember(Order = 13)] internal int State { get; set; }
        }

        [DataContract]
        private sealed class InstanceProjection
        {
            [DataMember(Order = 1)] internal int SchemaVersion { get; set; }
            [DataMember(Order = 2)] internal string OwnershipMarker { get; set; }
            [DataMember(Order = 3)] internal string InstanceId { get; set; }
            [DataMember(Order = 4)] internal string Inn { get; set; }
            [DataMember(Order = 5)] internal int LocalModuleOrdinal { get; set; }
            [DataMember(Order = 12)] internal int ApiPort { get; set; }
            [DataMember(Order = 13)] internal int DatabasePort { get; set; }
            [DataMember(Order = 14)] internal int EpmdPort { get; set; }
            [DataMember(Order = 17)] internal string ApiServiceName { get; set; }
            [DataMember(Order = 18)] internal string DatabaseServiceName { get; set; }
            [DataMember(Order = 26)] internal int State { get; set; }
        }

        [DataContract]
        private sealed class RemovalProjection
        {
            [DataMember(Order = 1)] internal int SchemaVersion { get; set; }
            [DataMember(Order = 2)] internal string OwnershipMarker { get; set; }
            [DataMember(Order = 3)] internal string OperationId { get; set; }
            [DataMember(Order = 4)] internal string KktSerial { get; set; }
            [DataMember(Order = 5)] internal string StackId { get; set; }
            [DataMember(Order = 6)] internal string StackOwnershipNonce { get; set; }
            [DataMember(Order = 7)] internal string InstanceId { get; set; }
            [DataMember(Order = 8)] internal string InstanceOwnershipNonce { get; set; }
            [DataMember(Order = 9)] internal string RuntimeId { get; set; }
            [DataMember(Order = 10)] internal string RuntimeOwnershipNonce { get; set; }
        }
    }
}
