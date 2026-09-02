using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LocalModuleMsiInventoryItem
    {
        public const int CurrentSchemaVersion = 1;
        public const string ExpectedOwnershipMarker =
            "KRS.MultiKKT.LocalModuleMsi.Inventory.v1";

        [DataMember(Order = 1)] public int SchemaVersion { get; set; }
        [DataMember(Order = 2)] public string OwnershipMarker { get; set; }
        [DataMember(Order = 3)] public string Inn { get; set; }
        [DataMember(Order = 4)] public int CloneOrdinal { get; set; }
        [DataMember(Order = 5)] public int ApiPort { get; set; }
        [DataMember(Order = 6)] public int DatabasePort { get; set; }
        [DataMember(Order = 7)] public string InstallRoot { get; set; }
        [DataMember(Order = 8)] public bool InstalledByApplication { get; set; }
        [DataMember(Order = 9)] public bool PreExisting { get; set; }
        [DataMember(Order = 10)] public string ManifestSha256 { get; set; }
    }
}
