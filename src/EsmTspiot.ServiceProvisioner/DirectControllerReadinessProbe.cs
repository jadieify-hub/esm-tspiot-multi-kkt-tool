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
        bool LocalModuleStartedAfterController(int ordinal, int localModulePort);
    }

    internal interface IProcessStartTimeReader
    {
        bool TryGetStartTimeUtc(int processId, out DateTime startTimeUtc);
    }

    internal sealed class NativeProcessStartTimeReader : IProcessStartTimeReader
    {
        public bool TryGetStartTimeUtc(int processId, out DateTime startTimeUtc)
        {
            startTimeUtc = DateTime.MinValue;
            if (processId <= 0) return false;
            try
            {
                using (System.Diagnostics.Process process =
                    System.Diagnostics.Process.GetProcessById(processId))
                {
                    startTimeUtc = process.StartTime.ToUniversalTime();
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // Процесс завершился между опросом слушателей и чтением.
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Время запуска чужого процесса прочитать не удалось.
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    internal sealed class DirectControllerReadinessProbe : IDirectControllerReadinessProbe
    {
        private readonly IWindowsServiceApi _services;
        private readonly ITcpListenerOwnerReader _listeners;
        private readonly IProcessParentReader _parents;
        private readonly IProcessStartTimeReader _startTimes;

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
            : this(services, listeners, parents, new NativeProcessStartTimeReader())
        {
        }

        internal DirectControllerReadinessProbe(
            IWindowsServiceApi services,
            ITcpListenerOwnerReader listeners,
            IProcessParentReader parents,
            IProcessStartTimeReader startTimes)
        {
            if (services == null) throw new ArgumentNullException("services");
            if (listeners == null) throw new ArgumentNullException("listeners");
            if (parents == null) throw new ArgumentNullException("parents");
            if (startTimes == null) throw new ArgumentNullException("startTimes");
            _services = services;
            _listeners = listeners;
            _parents = parents;
            _startTimes = startTimes;
        }

        /// <summary>
        /// Контроллер ЕСП ищет свой ЛМ ЧЗ при запуске. Если модуль поднялся
        /// позже — при первой установке или из-за гонки автозапуска после
        /// перезагрузки — контроллер остаётся без него: ЕСМ отвечает
        /// «ЛМ Контроллер не смог найти ЛМ ЧЗ» и отвергает привязку кодом
        /// 2025. Сравнение времени запуска процессов отвечает на этот вопрос
        /// и ничего на машине не меняет.
        /// </summary>
        public bool LocalModuleStartedAfterController(
            int ordinal,
            int localModulePort)
        {
            if (localModulePort < 1 || localModulePort > 65535) return false;
            string serviceName =
                DirectControllerIdentity.ServiceNameForOrdinal(ordinal);
            WindowsServiceRecord service = _services.Query(serviceName);
            if (service == null || service.State != WindowsServiceState.Running ||
                service.ProcessId <= 0)
            {
                return false;
            }
            DateTime controllerStartUtc;
            if (!_startTimes.TryGetStartTimeUtc(
                    service.ProcessId,
                    out controllerStartUtc))
            {
                return false;
            }
            IList<int> owners =
                _listeners.FindListenerProcessIds(localModulePort);
            for (int index = 0; owners != null && index < owners.Count; index++)
            {
                DateTime localModuleStartUtc;
                if (_startTimes.TryGetStartTimeUtc(
                        owners[index],
                        out localModuleStartUtc) &&
                    localModuleStartUtc > controllerStartUtc)
                {
                    return true;
                }
            }
            return false;
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
