using System;
using System.Collections.Generic;
using System.IO;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    /// <summary>
    /// Command-line properties handed to the vendor local-module MSI.
    /// Every name is proven against the pinned capability profile before
    /// msiexec runs, so a renamed vendor property fails before installation
    /// instead of silently leaving the module without credentials or
    /// automatic start.
    /// </summary>
    internal static class LocalModuleInstallerProperties
    {
        internal const string AdminUserProperty = "ADMINUSER";
        internal const string AdminPasswordProperty = "ADMINPASSWORD";
        internal const string AutoServiceProperty = "AUTOSERVICE";
        internal const string ApplicationFolderProperty = "APPLICATIONFOLDER";
        internal const string HiddenPropertiesProperty = "MsiHiddenProperties";
        internal const string ServerUrlProperty = "SERVERURL";

        private const string DemandStartCondition =
            "NOT AUTOSERVICE AND NOT Installed AND NOT REMOVE";
        private const string AutomaticStartCondition =
            "AUTOSERVICE AND NOT Installed AND NOT REMOVE";

        private static readonly string[] DemandStartActions =
        {
            "NotAutoStartlRegimeService",
            "NotAutoStartlYeniseiService"
        };

        private static readonly string[] AutomaticStartActions =
        {
            "StartRegimeService",
            "StartYeniseiService"
        };

        internal static IList<string> Names()
        {
            return new[]
            {
                AdminUserProperty,
                AdminPasswordProperty,
                AutoServiceProperty,
                ApplicationFolderProperty
            };
        }

        internal static string Build(string installRoot)
        {
            if (installRoot == null)
                throw new ArgumentNullException("installRoot");
            if (installRoot.IndexOf('"') >= 0)
                throw new InvalidDataException(
                    "Local-module installation root contains a quote.");
            RequireCommandLineSafe(SupportedLocalModulePackageIdentity.ApiLogin);
            RequireCommandLineSafe(SupportedLocalModulePackageIdentity.ApiPassword);
            string directory = installRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return AdminUserProperty + "=" +
                SupportedLocalModulePackageIdentity.ApiLogin + " " +
                AdminPasswordProperty + "=" +
                SupportedLocalModulePackageIdentity.ApiPassword + " " +
                AutoServiceProperty + "=1 " +
                ApplicationFolderProperty + "=\"" + directory + "\"";
        }

        internal static void RequireSupportedBy(
            LocalModuleMsiCapabilityProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            HashSet<string> hidden = SplitHiddenProperties(
                RequireRow(profile, "Property", HiddenPropertiesProperty)
                    .Value("Value"));
            if (!hidden.Contains(AdminUserProperty) ||
                !hidden.Contains(AdminPasswordProperty))
                throw new InvalidDataException(
                    "Vendor MSI profile does not declare the hidden credential " +
                    "properties used for local-module installation.");
            for (int index = 0; index < DemandStartActions.Length; index++)
                RequireCondition(
                    profile,
                    DemandStartActions[index],
                    DemandStartCondition);
            for (int index = 0; index < AutomaticStartActions.Length; index++)
                RequireCondition(
                    profile,
                    AutomaticStartActions[index],
                    AutomaticStartCondition);
            RequireRow(profile, "Directory", ApplicationFolderProperty);
        }

        private static void RequireCondition(
            LocalModuleMsiCapabilityProfile profile,
            string action,
            string expectedCondition)
        {
            string condition = RequireRow(
                profile,
                "InstallExecuteSequence",
                action).Value("Condition");
            if (!string.Equals(
                    condition,
                    expectedCondition,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Vendor MSI profile no longer gates " + action +
                    " on " + AutoServiceProperty + ".");
        }

        private static MsiProfileRow RequireRow(
            LocalModuleMsiCapabilityProfile profile,
            string table,
            string key)
        {
            for (int index = 0; index < profile.Rows.Count; index++)
            {
                MsiProfileRow row = profile.Rows[index];
                if (row != null &&
                    string.Equals(row.Table, table, StringComparison.Ordinal) &&
                    string.Equals(row.Key, key, StringComparison.Ordinal))
                    return row;
            }
            throw new InvalidDataException(
                "Vendor MSI profile does not pin " + table + ":" + key + ".");
        }

        private static HashSet<string> SplitHiddenProperties(string value)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            string[] parts = (value ?? string.Empty).Split(';');
            for (int index = 0; index < parts.Length; index++)
            {
                string name = parts[index].Trim();
                if (name.Length > 0) result.Add(name);
            }
            return result;
        }

        private static void RequireCommandLineSafe(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.IndexOfAny(new[] { ' ', '"', '\t', '\r', '\n' }) >= 0)
                throw new InvalidDataException(
                    "Local-module installer credential is not command-line safe.");
        }
    }
}
