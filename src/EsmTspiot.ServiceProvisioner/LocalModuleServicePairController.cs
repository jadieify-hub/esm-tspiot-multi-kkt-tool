using System;
using System.IO;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface ILocalModuleServicePairController
    {
        void StartDatabaseThenApi(LocalModuleInstalledLayout layout);
        void StopApiThenDatabase(LocalModuleInstalledLayout layout);
    }

    internal interface ILocalModuleMsiReadinessProbe
    {
        void WaitUntilReady(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role);
        void WaitUntilStopped(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role);
    }

    internal sealed class LocalModuleServicePairController :
        ILocalModuleServicePairController
    {
        private readonly IWindowsServiceApi _services;
        private readonly ILocalModuleMsiReadinessProbe _readiness;

        internal LocalModuleServicePairController(
            IWindowsServiceApi services,
            ILocalModuleMsiReadinessProbe readiness)
        {
            if (services == null) throw new ArgumentNullException("services");
            if (readiness == null) throw new ArgumentNullException("readiness");
            _services = services;
            _readiness = readiness;
        }

        public void StartDatabaseThenApi(LocalModuleInstalledLayout layout)
        {
            Start(layout, LocalModuleProcessRole.Database);
            Start(layout, LocalModuleProcessRole.Api);
        }

        public void StopApiThenDatabase(LocalModuleInstalledLayout layout)
        {
            Stop(layout, LocalModuleProcessRole.Api);
            Stop(layout, LocalModuleProcessRole.Database);
        }

        private void Start(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role)
        {
            WindowsServiceRecord service = RequireOwned(layout, role);
            if (service.State == WindowsServiceState.Stopped)
                _services.Start(layout.ServiceName(role));
            else if (service.State != WindowsServiceState.Running)
                throw new InvalidOperationException(
                    "Local-module service is in a transitional state.");
            _readiness.WaitUntilReady(layout, role);
        }

        private void Stop(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role)
        {
            WindowsServiceRecord service = RequireOwned(layout, role);
            if (service.State == WindowsServiceState.Running)
                _services.RequestStop(layout.ServiceName(role));
            else if (service.State != WindowsServiceState.Stopped)
                throw new InvalidOperationException(
                    "Local-module service is in a transitional state.");
            _readiness.WaitUntilStopped(layout, role);
        }

        private WindowsServiceRecord RequireOwned(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role)
        {
            if (layout == null) throw new ArgumentNullException("layout");
            WindowsServiceRecord service =
                _services.Query(layout.ServiceName(role));
            if (!layout.MatchesService(role, service))
                throw new InvalidDataException(
                    "Local-module service image does not match its install root.");
            return service;
        }
    }
}
