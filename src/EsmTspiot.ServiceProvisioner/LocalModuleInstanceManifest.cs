using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal enum LocalModuleInstanceLifecycleState
    {
        [EnumMember]
        Preparing = 1,
        [EnumMember]
        ReadyToInitialize = 2,
        [EnumMember]
        Running = 3,
        [EnumMember]
        Updating = 4,
        [EnumMember]
        Deleting = 5,
        [EnumMember]
        CleanupPending = 6,
        [EnumMember]
        Failed = 7
    }

    [DataContract]
    internal sealed class LocalModuleInstanceManifest
    {
        internal const int CurrentSchemaVersion = 1;
        internal const string ExpectedOwnershipMarker =
            "KRS.MultiKKT.LocalModule.Instance.v1";

        [DataMember(Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        internal string OwnershipMarker { get; set; }

        [DataMember(Order = 3)]
        internal string InstanceId { get; set; }

        [DataMember(Order = 4)]
        internal string Inn { get; set; }

        [DataMember(Order = 5)]
        internal int LocalModuleOrdinal { get; set; }

        [DataMember(Order = 6)]
        internal string RuntimeId { get; set; }

        [DataMember(Order = 7)]
        internal string RuntimeRoot { get; set; }

        [DataMember(Order = 8)]
        internal string ProfileRoot { get; set; }

        [DataMember(Order = 9)]
        internal string ConfigRoot { get; set; }

        [DataMember(Order = 10)]
        internal string DataRoot { get; set; }

        [DataMember(Order = 11)]
        internal string LogsRoot { get; set; }

        [DataMember(Order = 12)]
        internal int ApiPort { get; set; }

        [DataMember(Order = 13)]
        internal int DatabasePort { get; set; }

        [DataMember(Order = 14)]
        internal int EpmdPort { get; set; }

        [DataMember(Order = 15)]
        internal string ApiNodeName { get; set; }

        [DataMember(Order = 16)]
        internal string DatabaseNodeName { get; set; }

        [DataMember(Order = 17)]
        internal string ApiServiceName { get; set; }

        [DataMember(Order = 18)]
        internal string DatabaseServiceName { get; set; }

        [DataMember(Order = 19)]
        internal string RegimeLocalIniSha256 { get; set; }

        [DataMember(Order = 20)]
        internal string YeniseiLocalIniSha256 { get; set; }

        [DataMember(Order = 21)]
        internal string RegimeVmArgsSha256 { get; set; }

        [DataMember(Order = 22)]
        internal string YeniseiVmArgsSha256 { get; set; }

        [DataMember(Order = 23)]
        internal string RegimeSysConfigSha256 { get; set; }

        [DataMember(Order = 24)]
        internal string YeniseiSysConfigSha256 { get; set; }

        [DataMember(Order = 25)]
        internal string OwnershipNonce { get; set; }

        [DataMember(Order = 26)]
        internal LocalModuleInstanceLifecycleState State { get; set; }

        [DataMember(Order = 27)]
        internal string CurrentOperationId { get; set; }

        [DataMember(Order = 28)]
        internal string LastCompletedOperationId { get; set; }

        [DataMember(Order = 29)]
        internal string UpdatedUtc { get; set; }

        internal static LocalModuleInstanceManifest Create(
            ManagedLocalModuleProvisioningItemRequest item,
            string instanceId,
            string runtimeId,
            string runtimeRoot,
            LocalModuleConfiguration configuration,
            string ownershipNonce,
            string operationId)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (configuration == null) throw new ArgumentNullException("configuration");
            return new LocalModuleInstanceManifest
            {
                SchemaVersion = CurrentSchemaVersion,
                OwnershipMarker = ExpectedOwnershipMarker,
                InstanceId = instanceId,
                Inn = item.Inn,
                LocalModuleOrdinal = item.LocalModuleOrdinal,
                RuntimeId = runtimeId,
                RuntimeRoot = Path.GetFullPath(runtimeRoot).TrimEnd(Path.DirectorySeparatorChar),
                ProfileRoot = configuration.ProfileRoot,
                ConfigRoot = configuration.ConfigRoot,
                DataRoot = configuration.DataRoot,
                LogsRoot = configuration.LogsRoot,
                ApiPort = item.ApiPort,
                DatabasePort = item.DatabasePort,
                EpmdPort = item.EpmdPort,
                ApiNodeName = "krs_lm_regime_n" +
                    item.LocalModuleOrdinal.ToString("00", CultureInfo.InvariantCulture) +
                    "@127.0.0.1",
                DatabaseNodeName = "krs_lm_yenisei_n" +
                    item.LocalModuleOrdinal.ToString("00", CultureInfo.InvariantCulture) +
                    "@127.0.0.1",
                ApiServiceName = LocalModuleManagedIdentity.CreateApiServiceName(instanceId),
                DatabaseServiceName = LocalModuleManagedIdentity.CreateDatabaseServiceName(instanceId),
                RegimeLocalIniSha256 = HashText(configuration.RegimeLocalIni),
                YeniseiLocalIniSha256 = HashText(configuration.YeniseiLocalIni),
                RegimeVmArgsSha256 = HashText(configuration.RegimeVmArgs),
                YeniseiVmArgsSha256 = HashText(configuration.YeniseiVmArgs),
                RegimeSysConfigSha256 = HashText(configuration.RegimeSysConfig),
                YeniseiSysConfigSha256 = HashText(configuration.YeniseiSysConfig),
                OwnershipNonce = ownershipNonce,
                State = LocalModuleInstanceLifecycleState.Preparing,
                CurrentOperationId = operationId,
                LastCompletedOperationId = string.Empty,
                UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        private static string HashText(string value)
        {
            if (value == null) throw new InvalidDataException("Generated configuration is missing.");
            return LocalModuleManagedIdentity.ComputeSha256(Encoding.UTF8.GetBytes(value));
        }
    }
}
