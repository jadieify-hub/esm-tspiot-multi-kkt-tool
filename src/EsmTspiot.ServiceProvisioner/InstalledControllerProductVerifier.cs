using System;
using System.IO;
using Microsoft.Win32;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IInstalledControllerProductVerifier
    {
        ValidationResult Verify(ControllerCapabilityProfile profile);
    }

    internal sealed class WindowsInstalledControllerProductVerifier :
        IInstalledControllerProductVerifier
    {
        private const string UninstallPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public ValidationResult Verify(ControllerCapabilityProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");

            ValidationResult result = new ValidationResult();
            try
            {
                if (HasExactProduct(profile, RegistryView.Registry64) ||
                    HasExactProduct(profile, RegistryView.Registry32))
                {
                    return result;
                }
            }
            catch (UnauthorizedAccessException)
            {
                result.Add("Official controller installed product registry is not accessible.");
                return result;
            }
            catch (System.Security.SecurityException)
            {
                result.Add("Official controller installed product registry is not accessible.");
                return result;
            }
            catch (IOException)
            {
                result.Add("Official controller installed product registry could not be read.");
                return result;
            }

            result.Add(
                "Official controller installed product does not match the pinned name, version, and location.");
            return result;
        }

        private static bool HasExactProduct(
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
                    return false;
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
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        internal static bool MatchesProductValues(
            ControllerCapabilityProfile profile,
            string name,
            string version,
            string location)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            return string.Equals(
                    name,
                    profile.InstalledProductName,
                    StringComparison.Ordinal) &&
                string.Equals(version, profile.Version, StringComparison.Ordinal) &&
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
