using System;
using System.Collections.Generic;
using System.IO;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface ILocalModuleMsiCapabilityResolver
    {
        LocalModuleMsiCapabilityProfile Resolve(
            WindowsInstallerPackageMetadata metadata,
            TrustedFileExpectation trust,
            LocalModuleMsiDatabaseSnapshot snapshot);
    }

    /// <summary>
    /// Профиль возможностей пакета ЛМ выводится из самого MSI, а не из вшитого
    /// снимка одной версии. Жёстко проверяются только устойчивые признаки:
    /// подпись ЦРПТ, UpgradeCode и структура, необходимая для изолированного
    /// клона. Хеш, размер, версия, ProductCode и PackageCode фиксируются на
    /// выбранном оператором файле, но не на конкретном релизе вендора.
    /// </summary>
    internal sealed class LocalModuleMsiCapabilityResolver
        : ILocalModuleMsiCapabilityResolver
    {
        internal const int DerivedSchemaVersion = 2;

        public LocalModuleMsiCapabilityProfile Resolve(
            WindowsInstallerPackageMetadata metadata,
            TrustedFileExpectation trust,
            LocalModuleMsiDatabaseSnapshot snapshot)
        {
            if (metadata == null || trust == null || snapshot == null)
            {
                throw Unsupported("Не хватает данных о выбранном пакете.");
            }
            if (!trust.RequireCodeSigningEku)
            {
                throw Unsupported(
                    "У сертификата подписи нет назначения Code Signing.");
            }
            if (!SupportedLocalModulePackageIdentity.MatchesSignerSubject(
                    trust.SignerSubject))
            {
                throw Unsupported("MSI подписан не ЦРПТ.");
            }
            if (!SupportedLocalModulePackageIdentity.MatchesUpgradeCode(
                    metadata.UpgradeCode))
            {
                throw Unsupported(
                    "UpgradeCode пакета не совпадает с ЛМ ЧЗ: ожидался " +
                    SupportedLocalModulePackageIdentity.UpgradeCode + ".");
            }
            RequireFileShape(trust);
            RequireMetadataShape(metadata);
            RequireMedia(snapshot);
            if (snapshot.FileRowCount <= 0 || snapshot.MsiFileHashRowCount <= 0)
            {
                throw Unsupported(
                    "В MSI нет таблиц File или MsiFileHash с содержимым.");
            }

            LocalModuleMsiStructure structure = LocalModuleMsiStructure.Resolve(
                snapshot.Rows,
                metadata.UpgradeCode);
            LocalModuleMsiCapabilityProfile profile =
                new LocalModuleMsiCapabilityProfile
                {
                    SchemaVersion = DerivedSchemaVersion,
                    FileName = trust.FileName,
                    ByteLength = trust.ByteLength,
                    Sha256 = trust.Sha256,
                    SignerThumbprint = trust.SignerThumbprint,
                    ProductName = metadata.ProductName,
                    ProductVersion = metadata.ProductVersion,
                    ProductCode = metadata.ProductCode,
                    UpgradeCode = metadata.UpgradeCode,
                    PackageCode = metadata.PackageCode,
                    FileRowCount = snapshot.FileRowCount,
                    MsiFileHashRowCount = snapshot.MsiFileHashRowCount
                };
            for (int index = 0; index < snapshot.Media.Count; index++)
            {
                MsiMediaSnapshot media = snapshot.Media[index];
                profile.Media.Add(new MsiMediaSnapshot
                {
                    DiskId = media.DiskId,
                    LastSequence = media.LastSequence,
                    Cabinet = media.Cabinet
                });
            }
            AddRows(profile, structure, snapshot);
            LocalModuleInstallerProperties.RequireSupportedBy(profile);
            return profile;
        }

        private static void AddRows(
            LocalModuleMsiCapabilityProfile profile,
            LocalModuleMsiStructure structure,
            LocalModuleMsiDatabaseSnapshot snapshot)
        {
            List<MsiProfileRow> rows = new List<MsiProfileRow>();
            rows.Add(structure.ApplicationFolderRow);
            rows.Add(structure.HiddenPropertiesRow);
            rows.Add(structure.AppSearchRow);
            rows.Add(structure.RegLocatorRow);
            rows.Add(structure.UpgradeRow);
            Append(rows, structure.InstallDirectoryRegistryRows);
            Append(rows, structure.VendorRegistryRows);
            Append(rows, structure.ServiceActionRows);
            Append(rows, structure.ConfigurationFileRows);
            Append(rows, structure.DisabledSequenceRows);
            AppendSequenceRows(
                rows,
                snapshot,
                LocalModuleInstallerProperties.DemandStartActions);
            AppendSequenceRows(
                rows,
                snapshot,
                LocalModuleInstallerProperties.AutomaticStartActions);
            AppendOptionalRow(
                rows,
                snapshot,
                "Property",
                LocalModuleInstallerProperties.ServerUrlProperty);
            for (int index = 0; index < rows.Count; index++)
            {
                MsiProfileRow row = rows[index];
                if (row == null || Contains(profile.Rows, row)) continue;
                profile.Rows.Add(Copy(row));
            }
        }

        private static void AppendSequenceRows(
            IList<MsiProfileRow> rows,
            LocalModuleMsiDatabaseSnapshot snapshot,
            IList<string> actions)
        {
            for (int index = 0; index < actions.Count; index++)
            {
                MsiProfileRow row = Find(
                    snapshot.Rows,
                    "InstallExecuteSequence",
                    actions[index]);
                if (row == null)
                {
                    throw Unsupported(
                        "В MSI нет действия последовательности " +
                        actions[index] + ".");
                }
                rows.Add(row);
            }
        }

        private static void AppendOptionalRow(
            IList<MsiProfileRow> rows,
            LocalModuleMsiDatabaseSnapshot snapshot,
            string table,
            string key)
        {
            MsiProfileRow row = Find(snapshot.Rows, table, key);
            if (row != null) rows.Add(row);
        }

        private static void RequireFileShape(TrustedFileExpectation trust)
        {
            string fileName = trust.FileName ?? string.Empty;
            if (!fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                trust.ByteLength <= 0 ||
                !IsHex(trust.Sha256, 64) ||
                !IsHex(trust.SignerThumbprint, 40))
            {
                throw Unsupported("Имя, размер, хеш или подписант MSI неверны.");
            }
        }

        private static void RequireMetadataShape(
            WindowsInstallerPackageMetadata metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.ProductName) ||
                string.IsNullOrWhiteSpace(metadata.ProductVersion) ||
                !IsGuid(metadata.ProductCode) ||
                !IsGuid(metadata.UpgradeCode) ||
                !IsGuid(metadata.PackageCode))
            {
                throw Unsupported("Метаданные продукта в MSI неполные.");
            }
        }

        private static void RequireMedia(LocalModuleMsiDatabaseSnapshot snapshot)
        {
            if (snapshot.Media.Count == 0)
            {
                throw Unsupported("В MSI нет ни одного cab-архива.");
            }
            for (int index = 0; index < snapshot.Media.Count; index++)
            {
                string cabinet = snapshot.Media[index].Cabinet;
                if (string.IsNullOrEmpty(cabinet) || cabinet[0] != '#')
                {
                    throw Unsupported(
                        "Поддерживаются только встроенные cab-архивы MSI.");
                }
            }
        }

        private static void Append(
            IList<MsiProfileRow> destination,
            IList<MsiProfileRow> source)
        {
            for (int index = 0; index < source.Count; index++)
            {
                destination.Add(source[index]);
            }
        }

        private static bool Contains(
            IList<MsiProfileRow> rows,
            MsiProfileRow candidate)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                MsiProfileRow row = rows[index];
                if (string.Equals(row.Table, candidate.Table, StringComparison.Ordinal) &&
                    string.Equals(row.Key, candidate.Key, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static MsiProfileRow Find(
            IList<MsiProfileRow> rows,
            string table,
            string key)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                MsiProfileRow row = rows[index];
                if (row != null &&
                    string.Equals(row.Table, table, StringComparison.Ordinal) &&
                    string.Equals(row.Key, key, StringComparison.Ordinal))
                {
                    return row;
                }
            }
            return null;
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

        private static InvalidDataException Unsupported(string reason)
        {
            return new InvalidDataException(
                "Выбранный MSI не опознан как ЛМ ЧЗ от ЦРПТ. " + reason);
        }

        private static bool IsGuid(string value)
        {
            Guid parsed;
            return Guid.TryParseExact(
                value == null ? string.Empty : value.Trim(),
                "B",
                out parsed);
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
