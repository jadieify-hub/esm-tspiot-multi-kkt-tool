using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class DirectControllerOperatorInventoryReader
    {
        private readonly string _inventoryRoot;
        private readonly ReadOnlyWindowsServiceReader _services;
        private readonly Dictionary<string, string> _removalFingerprints;

        internal DirectControllerOperatorInventoryReader()
        {
            _inventoryRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "KRS",
                "MultiKKT",
                "DirectControllers",
                "Inventory");
            _services = new ReadOnlyWindowsServiceReader();
            _removalFingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        internal IList<DirectControllerProvisioningItemRequest> ReadRemovalItems()
        {
            IList<DirectControllerAssignment> assignments = ReadAssignments();
            List<DirectControllerProvisioningItemRequest> result =
                new List<DirectControllerProvisioningItemRequest>();
            for (int index = 0; index < assignments.Count; index++)
            {
                DirectControllerAssignment assignment = assignments[index];
                string fingerprint;
                if (!_removalFingerprints.TryGetValue(
                        assignment.KktSerial,
                        out fingerprint) || !IsHex(fingerprint, 64))
                {
                    throw new InvalidDataException(
                        "Инвентарь прямого контроллера не содержит отпечаток удаления.");
                }
                result.Add(new DirectControllerProvisioningItemRequest
                {
                    KktSerial = assignment.KktSerial,
                    Inn = assignment.KktInn,
                    Ordinal = assignment.Ordinal,
                    TargetLocalModulePort = assignment.TargetLocalModulePort,
                    ExpectedManifestSha256 = fingerprint
                });
            }
            result.Sort(delegate(
                DirectControllerProvisioningItemRequest left,
                DirectControllerProvisioningItemRequest right)
            {
                return left.Ordinal.CompareTo(right.Ordinal);
            });
            return result;
        }

        internal DirectControllerPlan BuildPlan(IList<LmGatewayKkt> kkts)
        {
            IList<DirectControllerAssignment> saved = ReadAssignments();
            IList<DirectControllerServiceInventoryItem> serviceInventory =
                ReadServices(saved);
            IList<TcpListenerSnapshotItem> listeners = ReadForeignListeners(
                saved,
                serviceInventory);
            return DirectControllerPlanner.Build(
                kkts,
                saved,
                serviceInventory,
                listeners);
        }

        private IList<DirectControllerAssignment> ReadAssignments()
        {
            _removalFingerprints.Clear();
            List<DirectControllerAssignment> result =
                new List<DirectControllerAssignment>();
            if (!Directory.Exists(_inventoryRoot)) return result;
            if ((File.GetAttributes(_inventoryRoot) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Инвентарь прямых контроллеров содержит reparse point.");
            }
            string[] files = Directory.GetFiles(_inventoryRoot, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < files.Length; index++)
            {
                if ((File.GetAttributes(files[index]) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "Инвентарь прямых контроллеров содержит небезопасный файл.");
                }
                InventoryProjection projection;
                using (FileStream stream = new FileStream(
                    files[index],
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length <= 0 || stream.Length > 65536)
                    {
                        throw new InvalidDataException(
                            "Запись инвентаря прямого контроллера имеет неверный размер.");
                    }
                    projection = (InventoryProjection)new DataContractJsonSerializer(
                        typeof(InventoryProjection)).ReadObject(stream);
                }
                DirectControllerAssignment assignment = ToAssignment(projection);
                if (!string.Equals(
                        Path.GetFileNameWithoutExtension(files[index]),
                        assignment.KktSerial,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Имя записи инвентаря не совпадает с ККТ.");
                }
                if (!IsHex(projection.RemovalFingerprint, 64))
                {
                    throw new InvalidDataException(
                        "Запись инвентаря не содержит отпечаток удаления.");
                }
                _removalFingerprints[assignment.KktSerial] =
                    projection.RemovalFingerprint;
                result.Add(assignment);
            }
            return result;
        }

        private IList<DirectControllerServiceInventoryItem> ReadServices(
            IList<DirectControllerAssignment> saved)
        {
            List<DirectControllerServiceInventoryItem> result =
                new List<DirectControllerServiceInventoryItem>();
            for (int ordinal = 1;
                ordinal <= DirectControllerIdentity.MaximumOrdinal;
                ordinal++)
            {
                string name = DirectControllerIdentity.ServiceNameForOrdinal(ordinal);
                ReadOnlyWindowsService service = _services.Query(name);
                if (service == null) continue;
                DirectControllerAssignment owner = FindByOrdinal(saved, ordinal);
                string officialPath =
                    LmControllerFileIdentity.GetOfficialControllerPath();
                bool official = ordinal == 1 &&
                    LmControllerFileIdentity.IsSupportedController(officialPath) &&
                    string.Equals(
                        NormalizeExecutablePath(service.ImagePath),
                        Path.GetFullPath(officialPath),
                        StringComparison.OrdinalIgnoreCase);
                result.Add(new DirectControllerServiceInventoryItem
                {
                    ServiceName = name,
                    KktSerial = owner == null ? string.Empty : owner.KktSerial,
                    IsOwned = ordinal > 1 && owner != null,
                    IsVerifiedOfficial = official
                });
            }
            return result;
        }

        private static IList<TcpListenerSnapshotItem> ReadForeignListeners(
            IList<DirectControllerAssignment> saved,
            IList<DirectControllerServiceInventoryItem> services)
        {
            List<TcpListenerSnapshotItem> result = new List<TcpListenerSnapshotItem>();
            System.Net.IPEndPoint[] endpoints =
                IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            for (int index = 0; index < endpoints.Length; index++)
            {
                int port = endpoints[index].Port;
                DirectControllerAssignment assignment = FindByPort(saved, port);
                if (assignment != null && HasExpectedService(services, assignment))
                {
                    continue;
                }
                if (assignment == null && IsVerifiedBasePort(services, port))
                {
                    continue;
                }
                result.Add(new TcpListenerSnapshotItem
                {
                    Port = port,
                    IsOwnerVerified = false,
                    OwnerServiceName = string.Empty
                });
            }
            return result;
        }

        private static DirectControllerAssignment ToAssignment(
            InventoryProjection source)
        {
            int targetPort = source == null
                ? 0
                : source.TargetLocalModulePort;
            if (source != null && targetPort == 0 && source.Ordinal >= 1 &&
                source.Ordinal <= DirectControllerIdentity.MaximumOrdinal &&
                source.LegacyFutureLocalModulePort ==
                    DirectControllerIdentity.FutureLmPortForOrdinal(source.Ordinal))
            {
                targetPort = source.LegacyFutureLocalModulePort;
            }
            if (source == null || source.Ordinal < 1 ||
                source.Ordinal > DirectControllerIdentity.MaximumOrdinal ||
                !IsDigits(source.KktSerial, 14) ||
                !(IsDigits(source.KktInn, 10) || IsDigits(source.KktInn, 12)) ||
                !string.Equals(
                    source.ServiceName,
                    DirectControllerIdentity.ServiceNameForOrdinal(source.Ordinal),
                    StringComparison.Ordinal) ||
                source.GrpcPort != DirectControllerIdentity.GrpcPortForOrdinal(source.Ordinal) ||
                source.RestPort != DirectControllerIdentity.RestPortForOrdinal(source.Ordinal) ||
                targetPort < 1024 || targetPort > 65535)
            {
                throw new InvalidDataException(
                    "Запись инвентаря прямого контроллера недействительна.");
            }
            return new DirectControllerAssignment
            {
                KktSerial = source.KktSerial,
                KktInn = source.KktInn,
                Ordinal = source.Ordinal,
                Role = DirectControllerIdentity.RoleForOrdinal(source.Ordinal),
                ServiceName = source.ServiceName,
                GrpcPort = source.GrpcPort,
                RestPort = source.RestPort,
                TargetLocalModulePort = targetPort
            };
        }

        private static DirectControllerAssignment FindByOrdinal(
            IList<DirectControllerAssignment> source,
            int ordinal)
        {
            for (int index = 0; index < source.Count; index++)
            {
                if (source[index].Ordinal == ordinal) return source[index];
            }
            return null;
        }

        private static DirectControllerAssignment FindByPort(
            IList<DirectControllerAssignment> source,
            int port)
        {
            for (int index = 0; index < source.Count; index++)
            {
                DirectControllerAssignment item = source[index];
                if (item.GrpcPort == port || item.RestPort == port)
                {
                    return item;
                }
            }
            return null;
        }

        private static bool HasExpectedService(
            IList<DirectControllerServiceInventoryItem> services,
            DirectControllerAssignment assignment)
        {
            for (int index = 0; index < services.Count; index++)
            {
                DirectControllerServiceInventoryItem service = services[index];
                if (string.Equals(
                        service.ServiceName,
                        assignment.ServiceName,
                        StringComparison.Ordinal) &&
                    (assignment.Ordinal == 1
                        ? service.IsVerifiedOfficial
                        : service.IsOwned))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsVerifiedBasePort(
            IList<DirectControllerServiceInventoryItem> services,
            int port)
        {
            if (port != DirectControllerIdentity.GrpcPortForOrdinal(1) &&
                port != DirectControllerIdentity.RestPortForOrdinal(1))
            {
                return false;
            }
            for (int index = 0; index < services.Count; index++)
            {
                if (services[index].IsVerifiedOfficial) return true;
            }
            return false;
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


        private static string NormalizeExecutablePath(string imagePath)
        {
            string value = (imagePath ?? string.Empty).Trim();
            if (value.Length >= 2 && value[0] == '"')
            {
                int closing = value.IndexOf('"', 1);
                if (closing > 0) value = value.Substring(1, closing - 1);
            }
            else
            {
                int space = value.IndexOf(' ');
                if (space > 0) value = value.Substring(0, space);
            }
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
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
                {
                    return false;
                }
            }
            return true;
        }

        [DataContract]
        private sealed class InventoryProjection
        {
            [DataMember(Order = 1)] public string KktSerial { get; set; }
            [DataMember(Order = 2)] public string KktInn { get; set; }
            [DataMember(Order = 3)] public int Ordinal { get; set; }
            [DataMember(Order = 4)] public string ServiceName { get; set; }
            [DataMember(Order = 5)] public int GrpcPort { get; set; }
            [DataMember(Order = 6)] public int RestPort { get; set; }
            [DataMember(Order = 7, EmitDefaultValue = false)]
            public int TargetLocalModulePort { get; set; }
            [DataMember(Order = 8)] public string ControllerVersion { get; set; }
            [DataMember(Order = 9)] public int State { get; set; }
            [DataMember(Order = 10)] public string RemovalFingerprint { get; set; }
            [DataMember(Name = "FutureLocalModulePort", Order = 11, EmitDefaultValue = false)]
            public int LegacyFutureLocalModulePort { get; set; }
        }
    }
}
