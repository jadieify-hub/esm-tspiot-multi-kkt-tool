using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal sealed class LocalModuleMsiCapabilityProfile
    {
        internal LocalModuleMsiCapabilityProfile()
        {
            Media = new List<MsiMediaSnapshot>();
            Rows = new List<MsiProfileRow>();
        }

        [DataMember(Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        internal string FileName { get; set; }

        [DataMember(Order = 3)]
        internal long ByteLength { get; set; }

        [DataMember(Order = 4)]
        internal string Sha256 { get; set; }

        [DataMember(Order = 5)]
        internal string SignerThumbprint { get; set; }

        [DataMember(Order = 6)]
        internal string ProductName { get; set; }

        [DataMember(Order = 7)]
        internal string ProductVersion { get; set; }

        [DataMember(Order = 8)]
        internal string ProductCode { get; set; }

        [DataMember(Order = 9)]
        internal string UpgradeCode { get; set; }

        [DataMember(Order = 10)]
        internal string PackageCode { get; set; }

        [DataMember(Order = 11)]
        internal int FileRowCount { get; set; }

        [DataMember(Order = 12)]
        internal int MsiFileHashRowCount { get; set; }

        [DataMember(Order = 13)]
        internal IList<MsiMediaSnapshot> Media { get; set; }

        [DataMember(Order = 14)]
        internal IList<MsiProfileRow> Rows { get; set; }
    }

    [DataContract]
    internal sealed class MsiMediaSnapshot
    {
        [DataMember(Order = 1)]
        internal int DiskId { get; set; }

        [DataMember(Order = 2)]
        internal int LastSequence { get; set; }

        [DataMember(Order = 3)]
        internal string Cabinet { get; set; }
    }

    [DataContract]
    internal sealed class MsiProfileRow
    {
        internal MsiProfileRow()
        {
            Columns = new List<string>();
            Values = new List<string>();
        }

        [DataMember(Order = 1)]
        internal string Table { get; set; }

        [DataMember(Order = 2)]
        internal string Key { get; set; }

        [DataMember(Order = 3)]
        internal IList<string> Columns { get; set; }

        [DataMember(Order = 4)]
        internal IList<string> Values { get; set; }

        internal string Value(string column)
        {
            for (int index = 0; index < Columns.Count; index++)
            {
                if (string.Equals(
                        Columns[index],
                        column,
                        System.StringComparison.Ordinal))
                {
                    return index < Values.Count ? Values[index] : null;
                }
            }
            return null;
        }
    }
}
