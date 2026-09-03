using System;
using System.Collections.Generic;
using System.IO;
using WixToolset.Dtf.WindowsInstaller;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface ILocalModuleMsiTransformer
    {
        TransformedLocalModuleMsi Transform(
            VerifiedLocalModulePackage source,
            LocalModuleMsiTransformPlan plan,
            string outputPath);
    }

    internal sealed class LocalModuleMsiTransformer : ILocalModuleMsiTransformer
    {
        public TransformedLocalModuleMsi Transform(
            VerifiedLocalModulePackage source,
            LocalModuleMsiTransformPlan plan,
            string outputPath)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (plan == null) throw new ArgumentNullException("plan");
            if (!string.Equals(
                    plan.ProductVersion,
                    source.Metadata.ProductVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "MSI transform plan version does not match the verified source.");
            }
            string target = Path.GetFullPath(outputPath);
            if (string.Equals(
                    target,
                    source.FullPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Source MSI cannot be transformed in place.");
            }
            if (File.Exists(target))
            {
                throw new IOException("Transformed MSI output already exists.");
            }

            FileStream outputLock = null;
            try
            {
                File.Copy(source.FullPath, target, false);
                IList<MsiProfileMismatch> copiedMismatches =
                    new LocalModuleMsiProfileReader()
                        .Read(target)
                        .Compare(source.CapabilityProfile);
                if (copiedMismatches.Count != 0)
                {
                    throw new InvalidDataException(
                        "Copied MSI no longer matches the verified source profile.");
                }
                ApplyTransform(target, source, plan);
                LocalModuleMsiDatabaseSnapshot sourcePayload =
                    new LocalModuleCabinetRebuilder().Rebuild(
                        source,
                        target,
                        plan);
                VerifiedTransformedLocalModuleMsi verified =
                    new LocalModuleMsiOutputVerifier().Verify(
                        sourcePayload,
                        target,
                        plan);
                outputLock = new FileStream(
                    target,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                TransformedLocalModuleMsi result =
                    new TransformedLocalModuleMsi(
                        target,
                        plan,
                        verified,
                        outputLock);
                outputLock = null;
                return result;
            }
            catch
            {
                if (outputLock != null) outputLock.Dispose();
                throw;
            }
        }

        private static void ApplyTransform(
            string path,
            VerifiedLocalModulePackage source,
            LocalModuleMsiTransformPlan plan)
        {
            using (Database database = new Database(path, DatabaseOpenMode.Transact))
            {
                SetProperty(
                    database,
                    "ProductName",
                    source.Metadata.ProductName,
                    plan.Identity.ProductName,
                    false);
                SetProperty(
                    database,
                    "ProductVersion",
                    source.Metadata.ProductVersion,
                    source.Metadata.ProductVersion,
                    false);
                SetProperty(
                    database,
                    "ProductCode",
                    source.Metadata.ProductCode,
                    FormatGuid(plan.Identity.ProductCode),
                    true);
                SetProperty(
                    database,
                    "UpgradeCode",
                    source.Metadata.UpgradeCode,
                    FormatGuid(plan.Identity.UpgradeCode),
                    true);
                // Строки берутся по назначению: идентификаторы Registry и
                // RegLocator генерируются сборкой вендора и меняются от версии
                // к версии.
                LocalModuleMsiStructure structure =
                    LocalModuleMsiStructure.Resolve(
                        source.CapabilityProfile.Rows,
                        source.Metadata.UpgradeCode);
                UpdateUpgradeRow(
                    database,
                    structure.UpgradeRow,
                    FormatGuid(plan.Identity.UpgradeCode));

                UpdateProfileRow(
                    database,
                    structure.ApplicationFolderRow,
                    "Directory",
                    delegate(MsiProfileRow row) {
                        row.Values[IndexOf(row, "DefaultDir")] =
                            plan.Identity.InstallDirectoryName;
                    });
                UpdateRegistryRows(database, structure, plan);
                UpdateProfileRow(
                    database,
                    structure.RegLocatorRow,
                    "Signature_",
                    delegate(MsiProfileRow row) {
                        row.Values[IndexOf(row, "Key")] =
                            plan.EquironRegistryKey;
                    });
                UpdateServiceActions(database, source.CapabilityProfile, plan);
                for (int index = 0;
                    index < structure.DisabledSequenceRows.Count;
                    index++)
                {
                    DisableSequenceAction(
                        database,
                        structure.DisabledSequenceRows[index]);
                }

                using (SummaryInfo summary = database.SummaryInfo)
                {
                    if (!string.Equals(
                            summary.RevisionNumber,
                            source.Metadata.PackageCode,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "SummaryInformation:PackageCode source mismatch.");
                    }
                    summary.RevisionNumber = FormatGuid(plan.Identity.PackageCode);
                    summary.Persist();
                }
                database.Commit();
            }
        }

        private static void UpdateRegistryRows(
            Database database,
            LocalModuleMsiStructure structure,
            LocalModuleMsiTransformPlan plan)
        {
            RewriteRegistryKeys(
                database,
                structure.InstallDirectoryRegistryRows,
                plan.EquironRegistryKey);
            RewriteRegistryKeys(
                database,
                structure.VendorRegistryRows,
                plan.CrptRegistryKey);
        }

        private static void RewriteRegistryKeys(
            Database database,
            IList<MsiProfileRow> rows,
            string replacement)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                UpdateProfileRow(
                    database,
                    rows[index],
                    "Registry",
                    delegate(MsiProfileRow row) {
                        row.Values[IndexOf(row, "Key")] = replacement;
                    });
            }
        }

        private static void UpdateServiceActions(
            Database database,
            LocalModuleMsiCapabilityProfile profile,
            LocalModuleMsiTransformPlan plan)
        {
            for (int index = 0; index < profile.Rows.Count; index++)
            {
                MsiProfileRow expected = profile.Rows[index];
                if (!string.Equals(
                        expected.Table,
                        "CustomAction",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                string target = expected.Value("Target");
                string replaced = ReplaceExactService(
                    target,
                    "regime",
                    plan.Identity.ApiServiceName);
                replaced = ReplaceExactService(
                    replaced,
                    "yenisei",
                    plan.Identity.DatabaseServiceName);
                if (string.Equals(target, replaced, StringComparison.Ordinal))
                {
                    continue;
                }
                UpdateProfileRow(
                    database,
                    expected,
                    "Action",
                    delegate(MsiProfileRow row) {
                        row.Values[IndexOf(row, "Target")] = replaced;
                    });
            }
        }

        private static string ReplaceExactService(
            string value,
            string source,
            string target)
        {
            string needle = "\"" + source + "\"";
            int first = value == null
                ? -1
                : value.IndexOf(needle, StringComparison.Ordinal);
            if (first < 0) return value;
            if (value.IndexOf(
                    needle,
                    first + needle.Length,
                    StringComparison.Ordinal) >= 0)
            {
                throw new InvalidDataException(
                    "CustomAction contains a repeated quoted service name.");
            }
            return value.Substring(0, first) + "\"" + target + "\"" +
                value.Substring(first + needle.Length);
        }

        private static void DisableSequenceAction(
            Database database,
            MsiProfileRow action)
        {
            UpdateProfileRow(
                database,
                action,
                "Action",
                delegate(MsiProfileRow row) {
                    row.Values[IndexOf(row, "Condition")] = "1=0";
                });
        }

        private static void SetProperty(
            Database database,
            string name,
            string expected,
            string replacement,
            bool ignoreCase)
        {
            const string query =
                "SELECT `Property`,`Value` FROM `Property` WHERE `Property` = ?";
            try
            {
            using (View view = database.OpenView(query))
            using (Record parameter = new Record(1))
            {
                parameter.SetString(1, name);
                view.Execute(parameter);
                using (Record record = view.Fetch())
                {
                    if (record == null || !string.Equals(
                            GetString(record, 2),
                            expected,
                            ignoreCase
                                ? StringComparison.OrdinalIgnoreCase
                                : StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "Property:" + name + " source mismatch.");
                    }
                    if (!string.Equals(
                            expected,
                            replacement,
                            StringComparison.Ordinal))
                    {
                        record.SetString(2, replacement);
                        view.Modify(ViewModifyMode.Update, record);
                    }
                }
            }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new InvalidDataException(
                    "Property:" + name + " update failed.");
            }
        }

        private static void UpdateProfileRow(
            Database database,
            MsiProfileRow expected,
            string keyColumn,
            Action<MsiProfileRow> update)
        {
            if (expected == null)
            {
                throw new InvalidDataException("Required MSI profile row is missing.");
            }
            string[] selected = new string[expected.Columns.Count + 1];
            selected[0] = keyColumn;
            for (int index = 0; index < expected.Columns.Count; index++)
            {
                selected[index + 1] = expected.Columns[index];
            }
            string query = BuildSelect(expected.Table, keyColumn, selected);
            try
            {
            using (View view = database.OpenView(query))
            using (Record parameter = new Record(1))
            {
                parameter.SetString(1, expected.Key);
                view.Execute(parameter);
                using (Record record = view.Fetch())
                {
                    if (record == null)
                    {
                        throw new InvalidDataException(
                            expected.Table + ":" + expected.Key +
                            " update-count mismatch.");
                    }
                    for (int field = 2; field <= selected.Length; field++)
                    {
                        if (!string.Equals(
                                GetString(record, field),
                                expected.Values[field - 2],
                                StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                expected.Table + ":" + expected.Key +
                                " source mismatch.");
                        }
                    }
                    MsiProfileRow replacement = Copy(expected);
                    update(replacement);
                    for (int field = 2; field <= selected.Length; field++)
                    {
                        if (!string.Equals(
                                replacement.Values[field - 2],
                                expected.Values[field - 2],
                                StringComparison.Ordinal))
                        {
                            record.SetString(
                                field,
                                replacement.Values[field - 2]);
                        }
                    }
                    view.Modify(ViewModifyMode.Update, record);
                }
            }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new InvalidDataException(
                    expected.Table + ":" + expected.Key + " update failed.");
            }
        }

        private static void UpdateUpgradeRow(
            Database database,
            MsiProfileRow expected,
            string replacementCode)
        {
            if (expected == null)
                throw new InvalidDataException("Required MSI Upgrade row is missing.");
            const string query =
                "SELECT `UpgradeCode`,`VersionMin`,`VersionMax`,`Language`," +
                "`Attributes`,`Remove`,`ActionProperty` FROM `Upgrade` " +
                "WHERE `UpgradeCode` = ?";
            using (View view = database.OpenView(query))
            using (Record parameter = new Record(1))
            {
                parameter.SetString(1, expected.Key);
                view.Execute(parameter);
                using (Record record = view.Fetch())
                {
                    if (record == null)
                        throw new InvalidDataException(
                            "Upgrade:" + expected.Key + " update-count mismatch.");
                    for (int field = 2; field <= 7; field++)
                        if (!string.Equals(
                                GetString(record, field),
                                expected.Values[field - 2],
                                StringComparison.Ordinal))
                            throw new InvalidDataException(
                                "Upgrade:" + expected.Key + " source mismatch.");
                    view.Modify(ViewModifyMode.Delete, record);
                }
            }
            using (View insert = database.OpenView(
                "SELECT `UpgradeCode`,`VersionMin`,`VersionMax`,`Language`," +
                "`Attributes`,`Remove`,`ActionProperty` FROM `Upgrade`"))
            using (Record replacement = new Record(7))
            {
                replacement.SetString(1, replacementCode);
                for (int field = 2; field <= 7; field++)
                {
                    string value = expected.Values[field - 2];
                    if (string.IsNullOrEmpty(value)) continue;
                    if (field == 5)
                        replacement.SetInteger(
                            field,
                            int.Parse(
                                value,
                                System.Globalization.CultureInfo.InvariantCulture));
                    else
                        replacement.SetString(field, value);
                }
                insert.Modify(ViewModifyMode.Insert, replacement);
            }
        }

        private static string BuildSelect(
            string table,
            string keyColumn,
            string[] columns)
        {
            System.Text.StringBuilder result =
                new System.Text.StringBuilder("SELECT ");
            for (int index = 0; index < columns.Length; index++)
            {
                if (index > 0) result.Append(',');
                result.Append('`').Append(columns[index]).Append('`');
            }
            result.Append(" FROM `").Append(table).Append("` WHERE `")
                .Append(keyColumn).Append("` = ?");
            return result.ToString();
        }

        private static int IndexOf(MsiProfileRow row, string column)
        {
            for (int index = 0; index < row.Columns.Count; index++)
            {
                if (string.Equals(
                        row.Columns[index],
                        column,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }
            throw new InvalidDataException(
                row.Table + ":" + row.Key + " column is missing.");
        }

        private static MsiProfileRow Copy(MsiProfileRow source)
        {
            return new MsiProfileRow
            {
                Table = source.Table,
                Key = source.Key,
                Columns = new List<string>(source.Columns),
                Values = new List<string>(source.Values)
            };
        }

        private static string GetString(Record record, int field)
        {
            return record.IsNull(field) ? string.Empty : record.GetString(field);
        }

        private static string FormatGuid(Guid value)
        {
            return value.ToString("B").ToUpperInvariant();
        }
    }
}
