using System;
using System.IO;
using Microsoft.Win32;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class InstalledControllerProductResult
    {
        internal ValidationResult Validation { get; set; }
        internal string DisplayVersion { get; set; }
    }

    internal interface IInstalledControllerProductVerifier
    {
        InstalledControllerProductResult Verify(ControllerCapabilityProfile profile);
    }

    internal sealed class WindowsInstalledControllerProductVerifier :
        IInstalledControllerProductVerifier
    {
        private const string UninstallPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public InstalledControllerProductResult Verify(
            ControllerCapabilityProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");

            ValidationResult result = new ValidationResult();
            try
            {
                string version = FindProductVersion(profile, RegistryView.Registry64);
                if (version == null)
                {
                    version = FindProductVersion(profile, RegistryView.Registry32);
                }
                if (version != null)
                {
                    return Success(version);
                }
            }
            catch (UnauthorizedAccessException)
            {
                result.Add("Official controller installed product registry is not accessible.");
                return Failure(result);
            }
            catch (System.Security.SecurityException)
            {
                result.Add("Official controller installed product registry is not accessible.");
                return Failure(result);
            }
            catch (IOException)
            {
                result.Add("Official controller installed product registry could not be read.");
                return Failure(result);
            }

            result.Add(
                "Установленный контроллер ЛМ ЧЗ от ЕСП в ожидаемом каталоге не найден.");
            return Failure(result);
        }

        private static InstalledControllerProductResult Success(string version)
        {
            return new InstalledControllerProductResult
            {
                Validation = new ValidationResult(),
                DisplayVersion = version
            };
        }

        private static InstalledControllerProductResult Failure(ValidationResult result)
        {
            return new InstalledControllerProductResult
            {
                Validation = result,
                DisplayVersion = string.Empty
            };
        }

        private static string FindProductVersion(
            ControllerCapabilityProfile profile,
            RegistryView view)
        {
            using (RegistryKey machine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                view))
            using (RegistryKey uninstall = machine.OpenSubKey(UninstallPath, false))
            {
                if (uninstall == null)
                {
                    return null;
                }

                string[] subKeyNames = uninstall.GetSubKeyNames();
                for (int index = 0; index < subKeyNames.Length; index++)
                {
                    using (RegistryKey product = uninstall.OpenSubKey(subKeyNames[index], false))
                    {
                        if (product == null)
                        {
                            continue;
                        }

                        string name = Convert.ToString(product.GetValue("DisplayName"));
                        string version = Convert.ToString(product.GetValue("DisplayVersion"));
                        string location = Convert.ToString(product.GetValue("InstallLocation"));
                        if (MatchesProductValues(profile, name, version, location))
                        {
                            return version ?? string.Empty;
                        }
                    }
                }
            }
            return null;
        }

        internal static bool MatchesProductValues(
            ControllerCapabilityProfile profile,
            string name,
            string version,
            string location)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            // Пустая версия в профиле означает «любая версия вендора».
            return string.Equals(
                    name,
                    profile.InstalledProductName,
                    StringComparison.Ordinal) &&
                (string.IsNullOrEmpty(profile.Version) ||
                    string.Equals(version, profile.Version, StringComparison.Ordinal)) &&
                !string.IsNullOrWhiteSpace(version) &&
                PathsEqual(location, profile.InstallRoot);
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                if (ex is ArgumentException || ex is NotSupportedException ||
                    ex is PathTooLongException)
                {
                    return false;
                }
                throw;
            }
        }
    }
}
