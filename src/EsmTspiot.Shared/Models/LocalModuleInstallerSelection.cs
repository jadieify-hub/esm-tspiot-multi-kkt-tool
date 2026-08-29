using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LocalModuleInstallerSelection
    {
        [DataMember(Order = 1)]
        public string SourcePath { get; set; }

        [DataMember(Order = 2)]
        public string FileName { get; set; }

        [DataMember(Order = 3)]
        public long ByteLength { get; set; }

        [DataMember(Order = 4)]
        public string Sha256 { get; set; }

        [DataMember(Order = 5)]
        public string ProductName { get; set; }

        [DataMember(Order = 6)]
        public string ProductVersion { get; set; }

        [DataMember(Order = 7)]
        public string ProductCode { get; set; }

        [DataMember(Order = 8)]
        public string UpgradeCode { get; set; }

        [DataMember(Order = 9)]
        public string SignerSubject { get; set; }

        [DataMember(Order = 10)]
        public string SignerThumbprint { get; set; }

        [DataMember(Order = 11)]
        public bool LicenseNoticeAccepted { get; set; }
    }
}
