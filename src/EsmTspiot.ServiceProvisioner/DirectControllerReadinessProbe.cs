using System;
using System.Collections.Generic;
using System.Threading;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IDirectControllerReadinessProbe
    {
        LmReadinessResult WaitUntilReady(int ordinal, int timeoutMilliseconds);
        bool WaitUntilStopped(int ordinal, int timeoutMilliseconds);
    }

    internal sealed class DirectControllerReadinessProbe : IDirectControllerReadinessProbe
    {
        private readonly IWindowsServiceApi _services;
        private readonly ITcpListenerOwnerReader _listeners;

        internal DirectControllerReadinessProbe(
            IWindowsServiceApi services,
            ITcpListenerOwnerReader listeners)
        {
            if (services == null) throw new ArgumentNullException("services");
            if (listeners == null) throw new ArgumentNullException("listeners");
            _services = services;
            _listeners = listeners;
        }

        internal LmReadinessResult Probe(int ordinal)
        {
            string serviceName = DirectControllerIdentity.ServiceNameForOrdinal(ordinal);
            WindowsServiceRecord service = _services.Query(serviceName);
            if (service == null || service.State != WindowsServiceState.Running ||
                service.ProcessId <= 0)
            {
                return LmReadinessResult.Failed(
                    "Служба прямого контроллера не запущена.");
            }
            IList<int> grpc = _listeners.FindListenerProcessIds(
                DirectControllerIdentity.GrpcPortForOrdinal(ordinal));
            IList<int> rest = _listeners.FindListenerProcessIds(
                DirectControllerIdentity.RestPortForOrdinal(ordinal));
            if (grpc.Count != 1 || rest.Count != 1 ||
                grpc[0] != service.ProcessId || rest[0] != service.ProcessId)
            {
                return LmReadinessResult.Failed(
                    "Listener-порты прямого контроллера не принадлежат PID его службы.");
            }
            return LmReadinessResult.Ready();
        }

        public LmReadinessResult WaitUntilReady(int ordinal, int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            LmReadinessResult last;
            do
            {
                last = Probe(ordinal);
                if (last.IsReady) return last;
                Thread.Sleep(250);
            }
            while (DateTime.UtcNow < deadline);
            return last;
        }

        public bool WaitUntilStopped(int ordinal, int timeoutMilliseconds)
        {
            string serviceName = DirectControllerIdentity.ServiceNameForOrdinal(ordinal);
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            do
            {
                WindowsServiceRecord service = _services.Query(serviceName);
                bool stopped = service == null ||
                    (service.State == WindowsServiceState.Stopped && service.ProcessId == 0);
                bool listenersStopped = _listeners.FindListenerProcessIds(
                        DirectControllerIdentity.GrpcPortForOrdinal(ordinal)).Count == 0 &&
                    _listeners.FindListenerProcessIds(
                        DirectControllerIdentity.RestPortForOrdinal(ordinal)).Count == 0;
                if (stopped && listenersStopped) return true;
                Thread.Sleep(250);
            }
            while (DateTime.UtcNow < deadline);
            return false;
        }
    }
}
