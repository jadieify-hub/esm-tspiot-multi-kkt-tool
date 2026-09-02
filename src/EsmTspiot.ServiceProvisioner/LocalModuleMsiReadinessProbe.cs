using System;
using System.Collections.Generic;
using System.Threading;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiReadinessProbe :
        ILocalModuleMsiReadinessProbe
    {
        private const int DefaultAttempts = 180;
        private readonly IWindowsServiceApi _services;
        private readonly ITcpListenerOwnerReader _listeners;
        private readonly IProcessParentReader _parents;
        private readonly int _attempts;
        private readonly Action _delay;

        internal LocalModuleMsiReadinessProbe(
            IWindowsServiceApi services,
            ITcpListenerOwnerReader listeners,
            IProcessParentReader parents)
            : this(
                services,
                listeners,
                parents,
                DefaultAttempts,
                delegate { Thread.Sleep(250); })
        {
        }

        internal LocalModuleMsiReadinessProbe(
            IWindowsServiceApi services,
            ITcpListenerOwnerReader listeners,
            IProcessParentReader parents,
            int attempts,
            Action delay)
        {
            if (services == null) throw new ArgumentNullException("services");
            if (listeners == null) throw new ArgumentNullException("listeners");
            if (parents == null) throw new ArgumentNullException("parents");
            if (attempts < 1) throw new ArgumentOutOfRangeException("attempts");
            if (delay == null) throw new ArgumentNullException("delay");
            _services = services;
            _listeners = listeners;
            _parents = parents;
            _attempts = attempts;
            _delay = delay;
        }

        public void WaitUntilReady(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role)
        {
            ValidationResult last = null;
            for (int attempt = 0; attempt < _attempts; attempt++)
            {
                last = ProbeOwnedListener(layout, role);
                if (last.IsValid) return;
                if (attempt + 1 < _attempts) _delay();
            }
            throw new InvalidOperationException(
                "Local-module service did not confirm its owned listener: " +
                (last == null ? "unknown" : last.JoinMessages()));
        }

        public void WaitUntilStopped(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role)
        {
            for (int attempt = 0; attempt < _attempts; attempt++)
            {
                WindowsServiceRecord service =
                    _services.Query(layout.ServiceName(role));
                IList<int> owners =
                    _listeners.FindListenerProcessIds(layout.Port(role));
                if ((service == null ||
                     service.State == WindowsServiceState.Stopped) &&
                    owners != null && owners.Count == 0)
                    return;
                if (attempt + 1 < _attempts) _delay();
            }
            throw new InvalidOperationException(
                "Local-module service or listener did not stop.");
        }

        internal ValidationResult ProbeOwnedListener(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role)
        {
            if (layout == null) throw new ArgumentNullException("layout");
            ValidationResult result = new ValidationResult();
            WindowsServiceRecord service =
                _services.Query(layout.ServiceName(role));
            if (!layout.MatchesService(role, service))
            {
                result.Add(
                    "Local-module service image does not match its install root.");
                return result;
            }
            if (service.State != WindowsServiceState.Running ||
                service.ProcessId <= 0)
            {
                result.Add("Local-module service is not running.");
                return result;
            }
            IList<int> owners =
                _listeners.FindListenerProcessIds(layout.Port(role));
            if (owners == null || owners.Count != 1 || owners[0] <= 0)
            {
                result.Add(
                    "Local-module listener does not have one exact owner.");
                return result;
            }
            if (!ProcessTreeOwnership.IsSameOrDescendant(
                    _parents,
                    owners[0],
                    service.ProcessId))
                result.Add(
                    "Local-module listener is outside the service process tree.");
            return result;
        }
    }
}
