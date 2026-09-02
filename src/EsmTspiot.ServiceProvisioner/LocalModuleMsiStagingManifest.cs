using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace EsmTspiot.ServiceProvisioner
{
    [DataContract]
    internal enum LocalModuleMsiStagingStage
    {
        [EnumMember]
        JournalCreated = 1,
        [EnumMember]
        SourceCopied = 2,
        [EnumMember]
        Transformed = 3,
        [EnumMember]
        Verified = 4,
        [EnumMember]
        InstallerReturned = 5,
        [EnumMember]
        ManifestPersisted = 6,
        [EnumMember]
        Installed = 7
    }

    [DataContract]
    internal sealed class LocalModuleMsiStagingManifest
    {
        internal const int CurrentSchemaVersion = 1;
        internal const string ExpectedOwnershipMarker =
            "KRS.MultiKKT.LocalModuleMsi.Staging.v1";

        [DataMember(Order = 1)]
        internal int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        internal string OwnershipMarker { get; set; }

        [DataMember(Order = 3)]
        internal string OperationId { get; set; }

        [DataMember(Order = 4)]
        internal string OwnershipNonce { get; set; }

        [DataMember(Order = 5)]
        internal string RootPath { get; set; }

        [DataMember(Order = 6)]
        internal string SourceCopyPath { get; set; }

        [DataMember(Order = 7)]
        internal string OutputMsiPath { get; set; }

        [DataMember(Order = 8)]
        internal IList<string> ExpectedEntries { get; set; }

        [DataMember(Order = 9)]
        internal LocalModuleMsiStagingStage Stage { get; set; }

        [DataMember(Order = 10)]
        internal string ProductCode { get; set; }

        [DataMember(Order = 11)]
        internal string PackageCode { get; set; }

        [DataMember(Order = 12)]
        internal string UpdatedUtc { get; set; }

        internal static LocalModuleMsiStagingManifest Create(
            string operationId,
            string ownershipNonce,
            string rootPath,
            string sourceCopyPath,
            string outputMsiPath,
            IList<string> expectedEntries)
        {
            return new LocalModuleMsiStagingManifest
            {
                SchemaVersion = CurrentSchemaVersion,
                OwnershipMarker = ExpectedOwnershipMarker,
                OperationId = operationId,
                OwnershipNonce = ownershipNonce,
                RootPath = PathIdentity(rootPath),
                SourceCopyPath = PathIdentity(sourceCopyPath),
                OutputMsiPath = PathIdentity(outputMsiPath),
                ExpectedEntries = new List<string>(expectedEntries),
                Stage = LocalModuleMsiStagingStage.JournalCreated,
                ProductCode = string.Empty,
                PackageCode = string.Empty,
                UpdatedUtc = DateTime.UtcNow.ToString(
                    "o",
                    CultureInfo.InvariantCulture)
            };
        }

        internal void Advance(LocalModuleMsiStagingStage stage)
        {
            if ((int)stage != (int)Stage + 1)
                throw new InvalidOperationException(
                    "Local-module MSI staging stage is not monotonic.");
            Stage = stage;
            UpdatedUtc = DateTime.UtcNow.ToString(
                "o",
                CultureInfo.InvariantCulture);
        }

        internal static string PathIdentity(string path)
        {
            return System.IO.Path.GetFullPath(path)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar);
        }
    }
}
