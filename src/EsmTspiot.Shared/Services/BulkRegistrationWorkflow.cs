using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.Shared.Services
{
    public sealed class BulkRegistrationWorkflow
    {
        private const int MaximumTransientAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

        private readonly ITspiotApiClient _api;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public BulkRegistrationWorkflow(ITspiotApiClient api)
            : this(api, delegate(TimeSpan delay, CancellationToken token) { return Task.Delay(delay, token); })
        {
        }

        public BulkRegistrationWorkflow(
            ITspiotApiClient api,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            if (api == null)
            {
                throw new ArgumentNullException("api");
            }
            if (delay == null)
            {
                throw new ArgumentNullException("delay");
            }

            _api = api;
            _delay = delay;
        }

        public async Task<BulkRegistrationDiscovery> DiscoverAsync(
            string baseUrl,
            string dkktPort,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            BulkRegistrationDiscovery discovery = new BulkRegistrationDiscovery();

            ApiResponse instancesResponse = await _api.GetInstancesAsync(baseUrl, cancellationToken);
            Report(progress, 0, 0, string.Empty, "Получение экземпляров", string.Empty, instancesResponse);
            if (!instancesResponse.IsSuccess)
            {
                discovery.ErrorMessage = "Не удалось получить список экземпляров ЕСМ/ТС ПИоТ.";
                return discovery;
            }

            IList<KktInstanceInfo> instances;
            if (!InstanceInfoParser.TryParse(instancesResponse.ResponseBody, out instances))
            {
                discovery.ErrorMessage = "Ответ /api/v1/instances/info не соответствует ожидаемому контракту.";
                return discovery;
            }

            ApiResponse devicesResponse = await _api.GetDkktListAsync(baseUrl, cancellationToken);
            Report(progress, 0, 0, string.Empty, "Получение физических ККТ", string.Empty, devicesResponse);
            if (!devicesResponse.IsSuccess)
            {
                discovery.ErrorMessage = "Не удалось получить список физических ККТ.";
                return discovery;
            }

            IList<DkktDeviceInfo> devices;
            if (!DkktListParser.TryParse(devicesResponse.ResponseBody, out devices))
            {
                discovery.ErrorMessage = "Ответ /api/v1/dkktList не соответствует ожидаемому контракту.";
                return discovery;
            }

            BulkKktRegistrationPlan plan = BulkKktRegistrationPlanner.Build(baseUrl, dkktPort, devices, instances);
            discovery.Plan = plan;

            for (int i = 0; i < plan.DuplicateDevices.Count; i++)
            {
                discovery.InitialResults.Add(new BulkKktRegistrationResult
                {
                    KktSerial = plan.DuplicateDevices[i].KktSerial,
                    Status = BulkKktRegistrationStatus.InvalidData,
                    Details = "дубликат серийного номера в ответе /api/v1/dkktList"
                });
            }

            for (int i = 0; i < plan.Items.Count; i++)
            {
                discovery.Items.Add(new BulkRegistrationWorkItem
                {
                    Item = plan.Items[i],
                    RequiresAdd = true
                });
            }

            for (int i = 0; i < plan.ExistingDevices.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DkktDeviceInfo device = plan.ExistingDevices[i];
                string serial = (device.KktSerial ?? string.Empty).Trim();
                KktInstanceInfo instance = FindInstance(instances, serial);
                ApiResponse detailResponse = await _api.GetInstanceAsync(baseUrl, serial, cancellationToken);
                Report(progress, i + 1, plan.ExistingDevices.Count, serial, "Проверка регистрации", string.Empty, detailResponse);

                KktInstanceDetails details;
                if (detailResponse.IsSuccess && InstanceDetailsParser.TryParse(detailResponse.ResponseBody, out details))
                {
                    if (details.HasCompleteRegistrationData)
                    {
                        discovery.InitialResults.Add(new BulkKktRegistrationResult
                        {
                            KktSerial = serial,
                            Status = BulkKktRegistrationStatus.AlreadyExists,
                            Details = "regData получены"
                        });
                    }
                    else
                    {
                        discovery.Items.Add(CreateResumeWorkItem(baseUrl, dkktPort, device, instance));
                        await ReadSettingsForDiagnostics(baseUrl, serial, progress, cancellationToken);
                    }
                }
                else
                {
                    await ReadSettingsForDiagnostics(baseUrl, serial, progress, cancellationToken);
                    discovery.InitialResults.Add(new BulkKktRegistrationResult
                    {
                        KktSerial = serial,
                        Status = BulkKktRegistrationStatus.InspectionFailed,
                        Details = "detail-запрос не подтвердил состояние regData"
                    });
                }
            }

            return discovery;
        }

        public async Task<BulkRegistrationOutcome> ExecuteAsync(
            BulkRegistrationDiscovery discovery,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (discovery == null)
            {
                throw new ArgumentNullException("discovery");
            }

            BulkRegistrationOutcome outcome = new BulkRegistrationOutcome();
            CopyResults(discovery.InitialResults, outcome.Results);
            if (!discovery.IsValid)
            {
                return outcome;
            }

            for (int i = 0; i < discovery.Items.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    outcome.Cancelled = true;
                    AddCancelledResults(discovery.Items, i, outcome.Results);
                    break;
                }

                BulkRegistrationWorkItem workItem = discovery.Items[i];
                BulkKktRegistrationItem item = workItem.Item;
                string serial = item.Input.KktSerial;
                try
                {
                    outcome.Results.Add(await ExecuteItemAsync(
                        workItem,
                        i + 1,
                        discovery.Items.Count,
                        progress,
                        cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    outcome.Cancelled = true;
                    outcome.Results.Add(new BulkKktRegistrationResult
                    {
                        KktSerial = serial,
                        Status = BulkKktRegistrationStatus.Cancelled,
                        Details = "операция прервана; проверьте фактическое состояние экземпляра"
                    });
                    AddCancelledResults(discovery.Items, i + 1, outcome.Results);
                    break;
                }
            }

            return outcome;
        }

        public async Task<BulkKktRegistrationResult> ExecuteItemAsync(
            BulkRegistrationWorkItem workItem,
            int current,
            int total,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (workItem == null || workItem.Item == null ||
                workItem.Item.Input == null ||
                workItem.Item.Validation == null)
            {
                throw new ArgumentException(
                    "Строка плана регистрации не задана.",
                    "workItem");
            }
            if (current < 1 || total < current)
            {
                throw new ArgumentOutOfRangeException("current");
            }

            cancellationToken.ThrowIfCancellationRequested();
            BulkKktRegistrationItem item = workItem.Item;
            string serial = item.Input.KktSerial;
            Report(
                progress,
                current,
                total,
                serial,
                "Начало",
                string.Empty,
                null);
            if (!item.Validation.IsValid)
            {
                return new BulkKktRegistrationResult
                {
                    KktSerial = serial,
                    Status = BulkKktRegistrationStatus.InvalidData,
                    Details = item.Validation.JoinMessages()
                };
            }

            if (workItem.RequiresAdd)
            {
                ApiResponse addResponse = await SendAddWithRetry(
                    item,
                    current,
                    total,
                    progress,
                    cancellationToken);
                bool canInspectCreatedInstance = addResponse.IsSuccess ||
                    TspiotErrorDecoder.ContainsErrorCode(
                        addResponse.ResponseBody,
                        1010);
                if (!canInspectCreatedInstance)
                {
                    return new BulkKktRegistrationResult
                    {
                        KktSerial = serial,
                        Status = BulkKktRegistrationStatus.AddFailed,
                        Details = Describe(addResponse)
                    };
                }

                bool ready = await WaitForReadiness(
                    item.Input.BaseUrl,
                    serial,
                    current,
                    total,
                    progress,
                    cancellationToken);
                if (!ready)
                {
                    return new BulkKktRegistrationResult
                    {
                        KktSerial = serial,
                        Status = BulkKktRegistrationStatus.InspectionFailed,
                        Details =
                            "POST выполнен, но готовность экземпляра не подтверждена; PUT пропущен"
                    };
                }
            }

            ApiResponse registerResponse = await SendRegisterWithRetry(
                item,
                current,
                total,
                progress,
                cancellationToken);
            return new BulkKktRegistrationResult
            {
                KktSerial = serial,
                Status = registerResponse.IsSuccess
                    ? (workItem.RequiresAdd
                        ? BulkKktRegistrationStatus.Registered
                        : BulkKktRegistrationStatus.RecoveredRegistration)
                    : BulkKktRegistrationStatus.RegistrationFailed,
                Details = registerResponse.IsSuccess
                    ? string.Empty
                    : Describe(registerResponse)
            };
        }

        private async Task<ApiResponse> SendAddWithRetry(
            BulkKktRegistrationItem item,
            int current,
            int total,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            ApiResponse response = null;
            for (int attempt = 1; attempt <= MaximumTransientAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                response = await _api.AddInstanceAsync(
                    item.Input.BaseUrl,
                    TspiotInputValidator.CreateAddRequest(item.Input),
                    cancellationToken);
                Report(progress, current, total, item.Input.KktSerial, "Добавление экземпляра",
                    "Попытка " + attempt.ToString(), response);
                if (response.IsSuccess || !IsTransientFailure(response))
                {
                    return response;
                }

                if (attempt < MaximumTransientAttempts)
                {
                    await _delay(RetryDelay, cancellationToken);
                }
            }

            return response;
        }

        private async Task<ApiResponse> SendRegisterWithRetry(
            BulkKktRegistrationItem item,
            int current,
            int total,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            ApiResponse response = null;
            for (int attempt = 1; attempt <= MaximumTransientAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                response = await _api.RegisterInstanceAsync(
                    item.Input.BaseUrl,
                    TspiotInputValidator.CreateRegisterRequest(item.Input),
                    cancellationToken);
                Report(progress, current, total, item.Input.KktSerial, "Регистрация",
                    "Попытка " + attempt.ToString(), response);
                if (response.IsSuccess || !IsTransientFailure(response))
                {
                    return response;
                }

                if (attempt < MaximumTransientAttempts)
                {
                    await _delay(RetryDelay, cancellationToken);
                }
            }

            return response;
        }

        private static bool IsTransientFailure(ApiResponse response)
        {
            return response != null &&
                (response.IsConnectionFailure ||
                    TspiotErrorDecoder.ContainsErrorCode(response.ResponseBody, 1013));
        }

        private async Task<bool> WaitForReadiness(
            string baseUrl,
            string serial,
            int current,
            int total,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MaximumTransientAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ApiResponse response = await _api.GetInstanceAsync(baseUrl, serial, cancellationToken);
                Report(progress, current, total, serial, "Ожидание службы",
                    "Проверка " + attempt.ToString(), response);
                KktInstanceDetails details;
                if (response.IsSuccess && InstanceDetailsParser.TryParse(response.ResponseBody, out details))
                {
                    return true;
                }

                if (attempt < MaximumTransientAttempts)
                {
                    await _delay(RetryDelay, cancellationToken);
                }
            }

            return false;
        }

        private async Task ReadSettingsForDiagnostics(
            string baseUrl,
            string serial,
            Action<BulkRegistrationProgress> progress,
            CancellationToken cancellationToken)
        {
            ApiResponse settingsResponse = await _api.GetSettingsAsync(baseUrl, serial, cancellationToken);
            Report(progress, 0, 0, serial, "Диагностика настроек", "GET settings", settingsResponse);
        }

        private static BulkRegistrationWorkItem CreateResumeWorkItem(
            string baseUrl,
            string dkktPort,
            DkktDeviceInfo device,
            KktInstanceInfo instance)
        {
            string port = instance == null ? string.Empty : instance.Port;
            string softPort = instance == null ? string.Empty : instance.SoftPort;
            int portNumber;
            int softPortNumber;
            if (!int.TryParse(port, out portNumber) || portNumber < 1 || portNumber > 65535)
            {
                port = TspiotDefaults.Port;
                portNumber = int.Parse(port);
            }
            if (!int.TryParse(softPort, out softPortNumber) || softPortNumber < 1 || softPortNumber > 65535)
            {
                softPort = (portNumber + 1000).ToString();
            }

            TspiotFormInput input = new TspiotFormInput
            {
                BaseUrl = baseUrl,
                KktSerial = (device.KktSerial ?? string.Empty).Trim(),
                FnSerial = (device.FnSerial ?? string.Empty).Trim(),
                KktInn = (device.KktInn ?? string.Empty).Trim(),
                Port = port,
                SoftPort = softPort,
                DkktPort = dkktPort
            };

            return new BulkRegistrationWorkItem
            {
                RequiresAdd = false,
                Item = new BulkKktRegistrationItem
                {
                    Device = device,
                    Input = input,
                    Validation = TspiotInputValidator.ValidatePut(input, true)
                }
            };
        }

        private static KktInstanceInfo FindInstance(IList<KktInstanceInfo> instances, string serial)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (string.Equals(instances[i].Id, serial, StringComparison.OrdinalIgnoreCase))
                {
                    return instances[i];
                }
            }

            return null;
        }

        private static void AddCancelledResults(
            IList<BulkRegistrationWorkItem> items,
            int startIndex,
            IList<BulkKktRegistrationResult> results)
        {
            for (int i = startIndex; i < items.Count; i++)
            {
                results.Add(new BulkKktRegistrationResult
                {
                    KktSerial = items[i].Item.Input.KktSerial,
                    Status = BulkKktRegistrationStatus.Cancelled,
                    Details = "запросы не выполнялись"
                });
            }
        }

        private static void CopyResults(
            IList<BulkKktRegistrationResult> source,
            IList<BulkKktRegistrationResult> target)
        {
            for (int i = 0; i < source.Count; i++)
            {
                target.Add(source[i]);
            }
        }

        private static string Describe(ApiResponse response)
        {
            if (response == null)
            {
                return "ответ отсутствует";
            }

            return "HTTP " + response.StatusCode.ToString() + "; " + (response.DecodedMessage ?? string.Empty);
        }

        private static void Report(
            Action<BulkRegistrationProgress> progress,
            int current,
            int total,
            string serial,
            string stage,
            string message,
            ApiResponse response)
        {
            if (progress == null)
            {
                return;
            }

            progress(new BulkRegistrationProgress
            {
                Current = current,
                Total = total,
                KktSerial = serial,
                Stage = stage,
                Message = message,
                Response = response
            });
        }
    }
}
