using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class SequentialKktRegistrationCoordinator
    {
        private static readonly TimeSpan PollDelay =
            TimeSpan.FromMilliseconds(500);

        private readonly ITspiotApiClient _api;
        private readonly IKktConnectionProvider _connections;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly int _emptyPollAttempts;
        private readonly int _targetPollAttempts;

        public SequentialKktRegistrationCoordinator(
            ITspiotApiClient api,
            IKktConnectionProvider connections)
            : this(
                api,
                connections,
                delegate(TimeSpan delay, CancellationToken token)
                {
                    return Task.Delay(delay, token);
                },
                3,
                20)
        {
        }

        public SequentialKktRegistrationCoordinator(
            ITspiotApiClient api,
            IKktConnectionProvider connections,
            Func<TimeSpan, CancellationToken, Task> delay,
            int emptyPollAttempts,
            int targetPollAttempts)
        {
            if (api == null) throw new ArgumentNullException("api");
            if (connections == null) throw new ArgumentNullException("connections");
            if (delay == null) throw new ArgumentNullException("delay");
            if (emptyPollAttempts < 1)
            {
                throw new ArgumentOutOfRangeException("emptyPollAttempts");
            }
            if (targetPollAttempts < 1)
            {
                throw new ArgumentOutOfRangeException("targetPollAttempts");
            }

            _api = api;
            _connections = connections;
            _delay = delay;
            _emptyPollAttempts = emptyPollAttempts;
            _targetPollAttempts = targetPollAttempts;
        }

        public async Task<SequentialKktDiscovery> DiscoverAsync(
            string baseUrl,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            SequentialKktDiscovery discovery = new SequentialKktDiscovery();
            IList<DkktDeviceInfo> lastBlocking = new List<DkktDeviceInfo>();
            for (int attempt = 1; attempt <= _emptyPollAttempts; attempt++)
            {
                Snapshot snapshot = await ReadSnapshotAsync(
                    baseUrl,
                    "Проверка свободного dkktList",
                    attempt,
                    _emptyPollAttempts,
                    progress,
                    cancellationToken);
                if (!snapshot.IsValid)
                {
                    discovery.ErrorMessage = snapshot.ErrorMessage;
                    return discovery;
                }
                if (snapshot.Devices.Count > 0)
                {
                    lastBlocking = snapshot.Devices;
                }
                if (attempt < _emptyPollAttempts)
                {
                    await _delay(PollDelay, cancellationToken);
                }
            }
            if (lastBlocking.Count > 0)
            {
                discovery.HasExternalSessions = true;
                CopyDevices(lastBlocking, discovery.BlockingDevices);
                discovery.ErrorMessage =
                    "ККТ удерживаются другой программой. Закройте кассовое ПО " +
                    "или «Тест драйвера ККТ» на время регистрации и повторите проверку.";
                return discovery;
            }

            IList<KktConnectionPort> ports = _connections.EnumeratePorts();
            if (ports == null || ports.Count == 0)
            {
                discovery.ErrorMessage =
                    "Не найдены USB/VCOM-порты АТОЛ MI_00. Для этой ККТ используйте ручной режим.";
                return discovery;
            }

            HashSet<string> portNames =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> serials = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < ports.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                KktConnectionPort port = ports[index];
                string portName = port == null
                    ? string.Empty
                    : (port.PortName ?? string.Empty).Trim();
                if (portName.Length == 0 || !portNames.Add(portName))
                {
                    discovery.ErrorMessage =
                        "Список VCOM АТОЛ содержит пустой или повторяющийся порт.";
                    return discovery;
                }

                IKktConnectionLease lease = null;
                try
                {
                    lease = await _connections.OpenAsync(port, cancellationToken);
                    ValidateLease(port, lease);
                    ReportConnection(
                        progress,
                        index + 1,
                        ports.Count,
                        lease.Identity,
                        "VCOM открыт");

                    DkktDeviceInfo device = await WaitForExactTargetAsync(
                        baseUrl,
                        lease.Identity.KktSerial,
                        progress,
                        cancellationToken);
                    if (device == null)
                    {
                        discovery.ErrorMessage =
                            "После открытия " + portName +
                            " ЕСМ не показал только эту ККТ в dkktList.";
                        return discovery;
                    }
                    string serial = (device.KktSerial ?? string.Empty).Trim();
                    if (!serials.Add(serial))
                    {
                        discovery.ErrorMessage =
                            "Один серийный номер ККТ обнаружен на нескольких VCOM: " +
                            serial + ".";
                        return discovery;
                    }
                    discovery.Targets.Add(new SequentialKktRegistrationTarget
                    {
                        Port = CopyPort(port),
                        Identity = CopyIdentity(lease.Identity),
                        Device = CopyDevice(device)
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    discovery.ErrorMessage =
                        "Не удалось проверить " + portName + ": " + ex.Message;
                    return discovery;
                }
                finally
                {
                    if (lease != null)
                    {
                        KktConnectionIdentity identity = lease.Identity;
                        lease.Dispose();
                        ReportConnection(
                            progress,
                            index + 1,
                            ports.Count,
                            identity,
                            "VCOM закрыт");
                    }
                }
            }

            return discovery;
        }

        public async Task<BulkKktRegistrationResult> ExecuteWithTargetAsync(
            string baseUrl,
            SequentialKktRegistrationTarget target,
            Func<CancellationToken, Task<BulkKktRegistrationResult>> registration,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (target == null || target.Port == null || target.Device == null)
            {
                throw new ArgumentNullException("target");
            }
            if (registration == null)
            {
                throw new ArgumentNullException("registration");
            }

            cancellationToken.ThrowIfCancellationRequested();
            IKktConnectionLease lease = null;
            try
            {
                lease = await _connections.OpenAsync(target.Port, cancellationToken);
                ValidateLease(target.Port, lease);
                EnsureSerialMatches(
                    target.Device.KktSerial,
                    lease.Identity.KktSerial,
                    target.Port.PortName);
                ReportConnection(progress, 1, 1, lease.Identity, "VCOM открыт для регистрации");

                DkktDeviceInfo visible = await WaitForExactTargetAsync(
                    baseUrl,
                    target.Device.KktSerial,
                    progress,
                    cancellationToken);
                if (visible == null)
                {
                    throw new InvalidOperationException(
                        "Перед POST/PUT в dkktList не осталась единственная целевая ККТ " +
                        (target.Device.KktSerial ?? string.Empty) + ".");
                }
                EnsureDeviceMatches(target.Device, visible);
                return await registration(cancellationToken);
            }
            finally
            {
                if (lease != null)
                {
                    KktConnectionIdentity identity = lease.Identity;
                    lease.Dispose();
                    ReportConnection(progress, 1, 1, identity, "VCOM закрыт после регистрации");
                }
            }
        }

        public async Task<bool> VerifyAllAsync(
            string baseUrl,
            SequentialKktDiscovery discovery,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (discovery == null)
            {
                throw new ArgumentNullException("discovery");
            }
            List<IKktConnectionLease> leases =
                new List<IKktConnectionLease>();
            try
            {
                for (int index = 0; index < discovery.Targets.Count; index++)
                {
                    SequentialKktRegistrationTarget target = discovery.Targets[index];
                    IKktConnectionLease lease = await _connections.OpenAsync(
                        target.Port,
                        cancellationToken);
                    ValidateLease(target.Port, lease);
                    EnsureSerialMatches(
                        target.Device.KktSerial,
                        lease.Identity.KktSerial,
                        target.Port.PortName);
                    leases.Add(lease);
                    ReportConnection(
                        progress,
                        index + 1,
                        discovery.Targets.Count,
                        lease.Identity,
                        "VCOM открыт для итоговой проверки");
                }

                for (int attempt = 1; attempt <= _targetPollAttempts; attempt++)
                {
                    Snapshot snapshot = await ReadSnapshotAsync(
                        baseUrl,
                        "Итоговая проверка всех ККТ",
                        attempt,
                        _targetPollAttempts,
                        progress,
                        cancellationToken);
                    if (!snapshot.IsValid)
                    {
                        return false;
                    }
                    if (ContainsExactlyTargets(snapshot.Devices, discovery.Targets))
                    {
                        return true;
                    }
                    if (attempt < _targetPollAttempts)
                    {
                        await _delay(PollDelay, cancellationToken);
                    }
                }
                return false;
            }
            finally
            {
                for (int index = leases.Count - 1; index >= 0; index--)
                {
                    KktConnectionIdentity identity = leases[index].Identity;
                    leases[index].Dispose();
                    ReportConnection(
                        progress,
                        index + 1,
                        leases.Count,
                        identity,
                        "VCOM закрыт после итоговой проверки");
                }
            }
        }

        private async Task<DkktDeviceInfo> WaitForExactTargetAsync(
            string baseUrl,
            string serial,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            string expected = (serial ?? string.Empty).Trim();
            for (int attempt = 1; attempt <= _targetPollAttempts; attempt++)
            {
                Snapshot snapshot = await ReadSnapshotAsync(
                    baseUrl,
                    "Ожидание единственной целевой ККТ",
                    attempt,
                    _targetPollAttempts,
                    progress,
                    cancellationToken);
                if (!snapshot.IsValid)
                {
                    throw new InvalidOperationException(snapshot.ErrorMessage);
                }
                if (snapshot.Devices.Count == 1 &&
                    string.Equals(
                        (snapshot.Devices[0].KktSerial ?? string.Empty).Trim(),
                        expected,
                        StringComparison.Ordinal))
                {
                    return snapshot.Devices[0];
                }
                if (attempt < _targetPollAttempts)
                {
                    await _delay(PollDelay, cancellationToken);
                }
            }
            return null;
        }

        private async Task<Snapshot> ReadSnapshotAsync(
            string baseUrl,
            string stage,
            int current,
            int total,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            ApiResponse response = await _api.GetDkktListAsync(
                baseUrl,
                cancellationToken);
            IList<DkktDeviceInfo> devices;
            bool parsed = DkktListParser.TryParse(response, out devices);
            Snapshot snapshot = new Snapshot
            {
                IsValid = response != null && response.IsSuccess && parsed,
                Devices = devices ?? new List<DkktDeviceInfo>(),
                ErrorMessage = response == null || !response.IsSuccess
                    ? "Не удалось получить реальный список dkktList."
                    : (parsed
                        ? string.Empty
                        : "Ответ /api/v1/dkktList не соответствует ожидаемому контракту.")
            };
            if (progress != null)
            {
                progress(new BulkRegistrationProgress
                {
                    Current = current,
                    Total = total,
                    Stage = stage,
                    Message = FormatVisibleDevices(snapshot.Devices),
                    Response = response
                });
            }
            return snapshot;
        }

        private static string FormatVisibleDevices(IList<DkktDeviceInfo> devices)
        {
            HashSet<string> inns = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; devices != null && index < devices.Count; index++)
            {
                string inn = devices[index] == null
                    ? string.Empty
                    : (devices[index].KktInn ?? string.Empty).Trim();
                if (inn.Length > 0)
                {
                    inns.Add(inn);
                }
            }
            StringBuilder builder = new StringBuilder();
            builder.Append("ККТ: ");
            builder.Append(devices == null ? 0 : devices.Count);
            builder.Append("; ИНН: ");
            if (inns.Count == 0)
            {
                builder.Append("нет");
            }
            else
            {
                bool first = true;
                foreach (string inn in inns)
                {
                    if (!first) builder.Append(", ");
                    builder.Append(inn);
                    first = false;
                }
            }
            return builder.ToString();
        }

        private static void ValidateLease(
            KktConnectionPort port,
            IKktConnectionLease lease)
        {
            if (lease == null || lease.Identity == null ||
                string.IsNullOrWhiteSpace(lease.Identity.KktSerial))
            {
                throw new InvalidOperationException(
                    "Драйвер АТОЛ не вернул серийный номер открытой ККТ.");
            }
            string expectedPort = port == null
                ? string.Empty
                : (port.PortName ?? string.Empty).Trim();
            if (!string.Equals(
                expectedPort,
                (lease.Identity.PortName ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Драйвер АТОЛ вернул результат для другого VCOM.");
            }
        }

        private static void EnsureSerialMatches(
            string expected,
            string actual,
            string portName)
        {
            if (!string.Equals(
                (expected ?? string.Empty).Trim(),
                (actual ?? string.Empty).Trim(),
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Серийный номер ККТ на " + (portName ?? string.Empty) +
                    " изменился после построения плана.");
            }
        }

        private static void EnsureDeviceMatches(
            DkktDeviceInfo expected,
            DkktDeviceInfo actual)
        {
            if (!Same(expected.KktSerial, actual.KktSerial) ||
                !Same(expected.FnSerial, actual.FnSerial) ||
                !Same(expected.KktInn, actual.KktInn))
            {
                throw new InvalidOperationException(
                    "Строка dkktList не совпала с ранее проверенной ККТ.");
            }
        }

        private static bool ContainsExactlyTargets(
            IList<DkktDeviceInfo> devices,
            IList<SequentialKktRegistrationTarget> targets)
        {
            if (devices == null || targets == null ||
                devices.Count != targets.Count)
            {
                return false;
            }
            HashSet<string> actual = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < devices.Count; index++)
            {
                actual.Add((devices[index].KktSerial ?? string.Empty).Trim());
            }
            for (int index = 0; index < targets.Count; index++)
            {
                string serial = targets[index] == null ||
                    targets[index].Device == null
                    ? string.Empty
                    : (targets[index].Device.KktSerial ?? string.Empty).Trim();
                if (!actual.Contains(serial))
                {
                    return false;
                }
            }
            return actual.Count == targets.Count;
        }

        private static void ReportConnection(
            Action<BulkRegistrationProgress> progress,
            int current,
            int total,
            KktConnectionIdentity identity,
            string stage)
        {
            if (progress == null) return;
            progress(new BulkRegistrationProgress
            {
                Current = current,
                Total = total,
                KktSerial = identity == null
                    ? string.Empty
                    : (identity.KktSerial ?? string.Empty),
                Stage = stage,
                Message = identity == null
                    ? string.Empty
                    : (identity.PortName ?? string.Empty)
            });
        }

        private static bool Same(string left, string right)
        {
            return string.Equals(
                (left ?? string.Empty).Trim(),
                (right ?? string.Empty).Trim(),
                StringComparison.Ordinal);
        }

        private static KktConnectionPort CopyPort(KktConnectionPort source)
        {
            return new KktConnectionPort
            {
                PortName = source == null ? string.Empty : source.PortName,
                HardwareId = source == null ? string.Empty : source.HardwareId
            };
        }

        private static KktConnectionIdentity CopyIdentity(
            KktConnectionIdentity source)
        {
            return new KktConnectionIdentity
            {
                PortName = source == null ? string.Empty : source.PortName,
                KktSerial = source == null ? string.Empty : source.KktSerial,
                ModelName = source == null ? string.Empty : source.ModelName,
                FirmwareVersion = source == null
                    ? string.Empty
                    : source.FirmwareVersion
            };
        }

        private static DkktDeviceInfo CopyDevice(DkktDeviceInfo source)
        {
            return new DkktDeviceInfo
            {
                KktSerial = source == null ? string.Empty : source.KktSerial,
                FnSerial = source == null ? string.Empty : source.FnSerial,
                KktInn = source == null ? string.Empty : source.KktInn,
                KktRnm = source == null ? string.Empty : source.KktRnm,
                ModelName = source == null ? string.Empty : source.ModelName,
                DkktVersion = source == null ? string.Empty : source.DkktVersion,
                Developer = source == null ? string.Empty : source.Developer,
                Manufacturer = source == null ? string.Empty : source.Manufacturer,
                ShiftState = source == null ? string.Empty : source.ShiftState
            };
        }

        private static void CopyDevices(
            IList<DkktDeviceInfo> source,
            IList<DkktDeviceInfo> target)
        {
            for (int index = 0; source != null && index < source.Count; index++)
            {
                target.Add(CopyDevice(source[index]));
            }
        }

        private sealed class Snapshot
        {
            internal Snapshot()
            {
                Devices = new List<DkktDeviceInfo>();
            }

            internal bool IsValid { get; set; }
            internal IList<DkktDeviceInfo> Devices { get; set; }
            internal string ErrorMessage { get; set; }
        }
    }
}
