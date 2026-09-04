using System;
using System.IO;

namespace EsmTspiot.Shared.Services
{
    public static class LocalModuleInstallRootPolicy
    {
        public static string GetVolumeRoot(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory) ||
                !Path.IsPathRooted(installDirectory) ||
                installDirectory.StartsWith(@"\\", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Local module install directory must be on a fixed local volume.",
                    "installDirectory");
            }
            string full;
            string root;
            try
            {
                full = Path.GetFullPath(installDirectory);
                root = Path.GetPathRoot(full);
            }
            catch (ArgumentException) { throw; }
            catch (NotSupportedException ex)
            {
                throw new ArgumentException(
                    "Local module install directory is unsupported.",
                    "installDirectory",
                    ex);
            }
            catch (PathTooLongException ex)
            {
                throw new ArgumentException(
                    "Local module install directory is too long.",
                    "installDirectory",
                    ex);
            }
            if (!IsCanonicalVolumeRoot(root))
            {
                throw new ArgumentException(
                    "Local module install directory has no canonical fixed-volume root.",
                    "installDirectory");
            }
            return root;
        }

        public static string GetSystemVolumeRoot()
        {
            string system = Environment.GetFolderPath(
                Environment.SpecialFolder.System);
            if (string.IsNullOrWhiteSpace(system))
            {
                throw new ArgumentException(
                    "Local module installation requires a known system volume.",
                    "system");
            }
            return GetVolumeRoot(system);
        }

        public static bool IsCanonicalVolumeRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return false;
            }
            string trimmed = value.Trim();
            if (trimmed.Length != 3 ||
                !((trimmed[0] >= 'A' && trimmed[0] <= 'Z') ||
                  (trimmed[0] >= 'a' && trimmed[0] <= 'z')) ||
                trimmed[1] != ':' ||
                (trimmed[2] != '\\' && trimmed[2] != '/'))
            {
                return false;
            }
            try
            {
                string full = Path.GetFullPath(trimmed);
                string root = Path.GetPathRoot(full);
                return string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        public static bool IsSupportedVolumeCharacteristics(
            DriveType driveType,
            FileAttributes attributes)
        {
            return driveType == DriveType.Fixed &&
                (attributes & FileAttributes.ReparsePoint) == 0;
        }

        public static string ResolveVolumeRoot(
            string requestedVolumeRoot,
            string baseInstallDirectory)
        {
            // По умолчанию клон ставится на системный диск, а не туда, где
            // лежит базовый ЛМ. Вендор мог поставить базовый на любой том, и
            // наследование его диска приводило к тому, что наш клон молча
            // уезжал, например, на D:, хотя оператор об этом не просил.
            string candidate = string.IsNullOrWhiteSpace(requestedVolumeRoot)
                ? GetSystemVolumeRoot()
                : requestedVolumeRoot.Trim();
            if (!IsCanonicalVolumeRoot(candidate))
            {
                throw new ArgumentException(
                    "Clone installation requires a canonical local volume root.",
                    "requestedVolumeRoot");
            }
            string normalized = Path.GetPathRoot(Path.GetFullPath(candidate));
            try
            {
                // Причину отказа надо называть по имени: одно общее сообщение
                // на четыре разных условия заставляет искать несуществующую
                // точку повторного разбора там, где просто нет такого диска.
                DriveInfo drive = new DriveInfo(normalized);
                if (!drive.IsReady || !Directory.Exists(normalized))
                {
                    throw new ArgumentException(
                        "Диск " + normalized + " недоступен: " +
                        "выберите том, который есть на этой машине.",
                        "requestedVolumeRoot");
                }
                if (drive.DriveType != DriveType.Fixed)
                {
                    throw new ArgumentException(
                        "Диск " + normalized + " не является постоянным " +
                        "локальным (" + drive.DriveType + "); " +
                        "ЛМ ЧЗ на сменный или сетевой том не ставится.",
                        "requestedVolumeRoot");
                }
                if (!IsSupportedVolumeCharacteristics(
                        drive.DriveType,
                        File.GetAttributes(normalized)))
                {
                    throw new ArgumentException(
                        "Корень диска " + normalized +
                        " является точкой повторного разбора.",
                        "requestedVolumeRoot");
                }
            }
            catch (IOException ex)
            {
                throw new ArgumentException(
                    "Clone installation volume could not be inspected.",
                    "requestedVolumeRoot",
                    ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new ArgumentException(
                    "Clone installation volume could not be inspected.",
                    "requestedVolumeRoot",
                    ex);
            }
            return normalized;
        }

        public static string BuildCloneInstallDirectory(
            string volumeRoot,
            int cloneOrdinal)
        {
            if (!IsCanonicalVolumeRoot(volumeRoot))
            {
                throw new ArgumentException(
                    "Clone installation requires a canonical local volume root.",
                    "volumeRoot");
            }
            LocalModuleMsiIdentity.ApiPortForClone(cloneOrdinal);
            return Path.Combine(
                Path.GetPathRoot(Path.GetFullPath(volumeRoot)),
                "Program Files",
                "Regime" + cloneOrdinal.ToString());
        }
    }
}
