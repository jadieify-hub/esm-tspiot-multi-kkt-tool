using System;
using System.Collections.Generic;
using System.IO;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiOutputVerifier
    {
        internal VerifiedTransformedLocalModuleMsi Verify(
            LocalModuleMsiDatabaseSnapshot source,
            string transformedMsiPath,
            LocalModuleMsiTransformPlan plan)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (plan == null) throw new ArgumentNullException("plan");
            string workspace = Path.GetFullPath(transformedMsiPath) +
                ".verify-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(workspace);
            string stage = "extract transformed cabinets";
            try
            {
                LocalModuleCabinetSnapshot output =
                    LocalModuleCabinetTools.Extract(transformedMsiPath, workspace);
                stage = "verify media";
                VerifyMedia(source.Media, output.Media);
                if (source.Files.Count == 0 ||
                    output.Files.Count != source.Files.Count)
                    throw new InvalidDataException("MSI output file set mismatch.");
                Dictionary<string, MsiFilePayloadSnapshot> expected =
                    Index(source.Files);
                int modified = 0;
                stage = "verify file payloads";
                for (int index = 0; index < output.Files.Count; index++)
                {
                    MsiFilePayloadSnapshot actual = output.Files[index];
                    MsiFilePayloadSnapshot original;
                    if (!expected.TryGetValue(actual.FileId, out original))
                        throw new InvalidDataException(
                            "MSI output contains an unexpected file: " +
                            actual.FileId + ".");
                    if (actual.Sequence != original.Sequence)
                        throw new InvalidDataException(
                            "MSI output Sequence mismatch: " + actual.FileId + ".");
                    if (actual.FileSize != actual.ActualSize)
                        throw new InvalidDataException(
                            "MSI output FileSize mismatch: " + actual.FileId +
                            " table=" + actual.FileSize +
                            " payload=" + actual.ActualSize + ".");
                    if (!HashMatchesPayload(actual, original))
                        throw new InvalidDataException(
                            "MSI output MsiFileHash mismatch: " +
                            actual.FileId + ".");
                    byte[] generated;
                    if (plan.ExpectedConfigFiles.TryGetValue(
                            actual.FileId,
                            out generated))
                    {
                        modified++;
                        if (!string.Equals(
                                actual.Sha256,
                                LocalModuleCabinetTools.Sha256(generated),
                                StringComparison.Ordinal))
                            throw new InvalidDataException(
                                "MSI generated config mismatch: " + actual.FileId + ".");
                    }
                    else if (!string.Equals(
                            actual.Sha256,
                            original.Sha256,
                            StringComparison.Ordinal))
                        throw new InvalidDataException(
                            "MSI unchanged payload mismatch: " + actual.FileId + ".");
                }
                if (modified != 4 || plan.ExpectedConfigFiles.Count != 4)
                    throw new InvalidDataException(
                        "MSI generated config count mismatch.");
                return new VerifiedTransformedLocalModuleMsi
                {
                    FullPath = Path.GetFullPath(transformedMsiPath),
                    TotalFileCount = output.Files.Count,
                    ModifiedFileCount = modified
                };
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "Local-module MSI output verification failed at stage '" +
                    stage + "'.",
                    exception);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(workspace))
                        Directory.Delete(workspace, true);
                }
                catch
                {
                }
            }
        }

        private static void VerifyMedia(
            IList<MsiMediaSnapshot> expected,
            IList<MsiMediaSnapshot> actual)
        {
            if (expected.Count != actual.Count)
                throw new InvalidDataException("MSI Media row count mismatch.");
            Dictionary<int, MsiMediaSnapshot> byDisk =
                new Dictionary<int, MsiMediaSnapshot>();
            for (int index = 0; index < actual.Count; index++)
                byDisk.Add(actual[index].DiskId, actual[index]);
            for (int index = 0; index < expected.Count; index++)
            {
                MsiMediaSnapshot found;
                if (!byDisk.TryGetValue(expected[index].DiskId, out found) ||
                    expected[index].LastSequence != found.LastSequence ||
                    !string.Equals(
                        expected[index].Cabinet,
                        found.Cabinet,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Media:" + expected[index].Cabinet + " mismatch.");
            }
        }

        private static Dictionary<string, MsiFilePayloadSnapshot> Index(
            IList<MsiFilePayloadSnapshot> files)
        {
            Dictionary<string, MsiFilePayloadSnapshot> result =
                new Dictionary<string, MsiFilePayloadSnapshot>(StringComparer.Ordinal);
            for (int index = 0; index < files.Count; index++)
                result.Add(files[index].FileId, files[index]);
            return result;
        }

        private static bool HashMatchesPayload(
            MsiFilePayloadSnapshot file,
            MsiFilePayloadSnapshot original)
        {
            if (original.HashParts == null)
                return file.HashParts == null && file.ComputedHashParts == null;
            if (file.HashParts == null || file.ComputedHashParts == null ||
                file.HashParts.Length != 4 || file.ComputedHashParts.Length != 4)
                return false;
            for (int index = 0; index < 4; index++)
                if (file.HashParts[index] != file.ComputedHashParts[index])
                    return false;
            return true;
        }
    }
}
