using System;
using System.Collections.Generic;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiDatabaseSnapshot
    {
        internal LocalModuleMsiDatabaseSnapshot()
        {
            Media = new List<MsiMediaSnapshot>();
            Rows = new List<MsiProfileRow>();
        }

        internal int FileRowCount { get; set; }
        internal int MsiFileHashRowCount { get; set; }
        internal IList<MsiMediaSnapshot> Media { get; private set; }
        internal IList<MsiProfileRow> Rows { get; private set; }

        internal IList<MsiProfileMismatch> Compare(
            LocalModuleMsiCapabilityProfile expected)
        {
            if (expected == null) throw new ArgumentNullException("expected");
            List<MsiProfileMismatch> result = new List<MsiProfileMismatch>();
            if (FileRowCount != expected.FileRowCount)
            {
                result.Add(new MsiProfileMismatch("File", "row-count", "count"));
            }
            if (MsiFileHashRowCount != expected.MsiFileHashRowCount)
            {
                result.Add(new MsiProfileMismatch(
                    "MsiFileHash",
                    "row-count",
                    "count"));
            }
            CompareMedia(expected.Media, result);
            CompareRows(expected.Rows, result);
            return result;
        }

        private void CompareMedia(
            IList<MsiMediaSnapshot> expected,
            IList<MsiProfileMismatch> result)
        {
            Dictionary<int, MsiMediaSnapshot> observed =
                new Dictionary<int, MsiMediaSnapshot>();
            for (int index = 0; index < Media.Count; index++)
            {
                MsiMediaSnapshot item = Media[index];
                if (item != null && !observed.ContainsKey(item.DiskId))
                {
                    observed.Add(item.DiskId, item);
                }
            }
            if (observed.Count != expected.Count)
            {
                result.Add(new MsiProfileMismatch("Media", "row-count", "count"));
            }
            for (int index = 0; index < expected.Count; index++)
            {
                MsiMediaSnapshot wanted = expected[index];
                MsiMediaSnapshot actual;
                if (!observed.TryGetValue(wanted.DiskId, out actual))
                {
                    result.Add(new MsiProfileMismatch(
                        "Media",
                        wanted.Cabinet,
                        "row"));
                    continue;
                }
                if (actual.LastSequence != wanted.LastSequence)
                {
                    result.Add(new MsiProfileMismatch(
                        "Media",
                        wanted.Cabinet,
                        "LastSequence"));
                }
                if (!string.Equals(
                        actual.Cabinet,
                        wanted.Cabinet,
                        StringComparison.Ordinal))
                {
                    result.Add(new MsiProfileMismatch(
                        "Media",
                        wanted.Cabinet,
                        "Cabinet"));
                }
            }
        }

        private void CompareRows(
            IList<MsiProfileRow> expected,
            IList<MsiProfileMismatch> result)
        {
            Dictionary<string, MsiProfileRow> observed =
                new Dictionary<string, MsiProfileRow>(StringComparer.Ordinal);
            for (int index = 0; index < Rows.Count; index++)
            {
                MsiProfileRow row = Rows[index];
                if (row == null) continue;
                string identity = Identity(row.Table, row.Key);
                if (!observed.ContainsKey(identity)) observed.Add(identity, row);
            }
            for (int index = 0; index < expected.Count; index++)
            {
                MsiProfileRow wanted = expected[index];
                MsiProfileRow actual;
                if (wanted == null || !observed.TryGetValue(
                        Identity(wanted.Table, wanted.Key),
                        out actual))
                {
                    result.Add(new MsiProfileMismatch(
                        wanted == null ? "Profile" : wanted.Table,
                        wanted == null ? "null-row" : wanted.Key,
                        "row"));
                    continue;
                }
                for (int column = 0; column < wanted.Columns.Count; column++)
                {
                    string name = wanted.Columns[column];
                    string expectedValue = column < wanted.Values.Count
                        ? wanted.Values[column]
                        : null;
                    if (!string.Equals(
                            actual.Value(name),
                            expectedValue,
                            StringComparison.Ordinal))
                    {
                        result.Add(new MsiProfileMismatch(
                            wanted.Table,
                            wanted.Key,
                            name));
                    }
                }
            }
        }

        private static string Identity(string table, string key)
        {
            return (table ?? string.Empty) + "\n" + (key ?? string.Empty);
        }
    }

    internal sealed class MsiProfileMismatch
    {
        internal MsiProfileMismatch(string table, string key, string column)
        {
            Table = table ?? "Profile";
            Key = key ?? "unknown";
            Column = column ?? "value";
        }

        internal string Table { get; private set; }
        internal string Key { get; private set; }
        internal string Column { get; private set; }

        public override string ToString()
        {
            return Table + ":" + Key + " " + Column + " mismatch.";
        }
    }
}
