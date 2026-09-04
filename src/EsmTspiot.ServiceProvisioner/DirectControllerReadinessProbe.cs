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
        private readonly IProcessParentReader _parents;

        internal DirectControllerReadinessProbe(
            IWindowsServiceApi services,
            ITcpListenerOwnerReader listeners)
            : this(services, listeners, new NativeProcessParentReader())
        {
        }

        internal DirectControllerReadinessProbe(
            IWindowsServiceApi services,
            ITcpListenerOwnerReader listeners,
            IProcessParentReader parents)
        {
            if (services == null) throw new ArgumentNullException("services");
            if (listeners == null) throw new ArgumentNullException("listeners");
            if (parents == null) throw new ArgumentNullException("parents");
            _services = services;
            _listeners = listeners;
            _parents = parents;
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
            // Контроллер вендора может слушать порты не сам, а из дочернего
            // процесса. Требование точного совпадения PID со службой в этом
            // случае не выполняется никогда: стадия молча выжидает весь
            // таймаут и объявляет отказ на исправно работающем контроллере.
            // Принимаем слушателя из дерева процессов службы — чужой процесс
            // на этих портах по-прежнему готовностью не считается.
            if (!OwnedByService(grpc, service.ProcessId) ||
                !OwnedByService(rest, service.ProcessId))
            {
                return LmReadinessResult.Failed(
                    "Listener-порты прямого контроллера заняты посторонним процессом.");
            }
            return LmReadinessResult.Ready();
        }

        private bool OwnedByService(IList<int> owners, int serviceProcessId)
        {
            if (owners == null || owners.Count == 0) return false;
            for (int index = 0; index < owners.Count; index++)
                if (ProcessTreeOwnership.IsSameOrDescendant(
                        _parents,
                        owners[index],
                        serviceProcessId))
                    return true;
            return false;
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
