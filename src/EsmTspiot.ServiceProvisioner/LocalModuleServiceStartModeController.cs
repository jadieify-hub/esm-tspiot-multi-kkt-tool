using System;
using System.IO;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleStartModeAdjustment
    {
        internal WindowsServiceStartMode PreviousApiStartMode { get; set; }
        internal WindowsServiceStartMode PreviousDatabaseStartMode { get; set; }

        internal bool Adjusted
        {
            get
            {
                return PreviousApiStartMode != WindowsServiceStartMode.AutoStart ||
                    PreviousDatabaseStartMode != WindowsServiceStartMode.AutoStart;
            }
        }
    }

    // Switches the exact regime/yenisei pair of one layout to automatic start
    // and hands the recorded previous modes back on removal. Only the SCM start
    // type is touched; a disabled or foreign service is refused, never edited.
    internal sealed class LocalModuleServiceStartModeController
    {
        private readonly IWindowsServiceApi _services;

        internal LocalModuleServiceStartModeController(IWindowsServiceApi services)
        {
            if (services == null) throw new ArgumentNullException("services");
            _services = services;
        }

        internal static bool IsAutomatic(
            WindowsServiceRecord api,
            WindowsServiceRecord database)
        {
            return api != null && database != null &&
                api.StartMode == WindowsServiceStartMode.AutoStart &&
                database.StartMode == WindowsServiceStartMode.AutoStart;
        }

        internal LocalModuleStartModeAdjustment EnsureAutomatic(
            LocalModuleInstalledLayout layout)
        {
            if (layout == null) throw new ArgumentNullException("layout");
            WindowsServiceRecord api = RequireExact(
                layout, LocalModuleProcessRole.Api);
            WindowsServiceRecord database = RequireExact(
                layout, LocalModuleProcessRole.Database);
            if (api.StartMode == WindowsServiceStartMode.Disabled ||
                database.StartMode == WindowsServiceStartMode.Disabled)
                throw new InvalidOperationException(
                    "Local-module services are disabled by an administrator; " +
                    "enable them manually before assigning the base module.");
            LocalModuleStartModeAdjustment result =
                new LocalModuleStartModeAdjustment
                {
                    PreviousApiStartMode = api.StartMode,
                    PreviousDatabaseStartMode = database.StartMode
                };
            SetExact(layout, LocalModuleProcessRole.Database,
                WindowsServiceStartMode.AutoStart);
            SetExact(layout, LocalModuleProcessRole.Api,
                WindowsServiceStartMode.AutoStart);
            return result;
        }

        internal void Restore(
            LocalModuleInstalledLayout layout,
            WindowsServiceStartMode apiStartMode,
            WindowsServiceStartMode databaseStartMode)
        {
            if (layout == null) throw new ArgumentNullException("layout");
            RestoreExact(layout, LocalModuleProcessRole.Api, apiStartMode);
            RestoreExact(layout, LocalModuleProcessRole.Database, databaseStartMode);
        }

        private WindowsServiceRecord RequireExact(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role)
        {
            WindowsServiceRecord record = _services.Query(layout.ServiceName(role));
            if (!layout.MatchesService(role, record))
                throw new InvalidDataException(
                    "Local-module service pair is foreign.");
            return record;
        }

        private void SetExact(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role,
            WindowsServiceStartMode startMode)
        {
            if (RequireExact(layout, role).StartMode == startMode) return;
            _services.SetStartMode(layout.ServiceName(role), startMode);
            if (RequireExact(layout, role).StartMode != startMode)
                throw new InvalidOperationException(
                    "Local-module service did not accept the requested start mode.");
        }

        private void RestoreExact(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role,
            WindowsServiceStartMode startMode)
        {
            // A service that is already gone or foreign is left alone: removal
            // never touches anything outside the exact recorded pair.
            WindowsServiceRecord record = _services.Query(layout.ServiceName(role));
            if (record == null || !layout.MatchesService(role, record)) return;
            if (record.StartMode == startMode) return;
            _services.SetStartMode(layout.ServiceName(role), startMode);
        }
    }
}
