using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class LmGatewayBindingWorkflow
    {
        private const int VerificationBudgetSeconds = 45;
        private const int VerificationAttemptSeconds = 5;
        private const int VerificationRetryDelayMilliseconds = 3000;

        private readonly ITspiotApiClient _apiClient;
        private readonly LmGatewayReadbackWorkflow _readbackWorkflow;
        private readonly TimeSpan _verificationBudget;
        private readonly TimeSpan _verificationRetryDelay;

        public LmGatewayBindingWorkflow(ITspiotApiClient apiClient)
            : this(
                apiClient,
                TimeSpan.FromSeconds(VerificationBudgetSeconds),
                TimeSpan.FromMilliseconds(VerificationRetryDelayMilliseconds))
        {
        }

        /// <summary>
        /// Бюджет проверки вынесен в конструктор: в поле ждём ЕСМ десятками
        /// секунд, а тестам ждать реальное время незачем.
        /// </summary>
        public LmGatewayBindingWorkflow(
            ITspiotApiClient apiClient,
            TimeSpan verificationBudget,
            TimeSpan verificationRetryDelay)
        {
            if (apiClient == null)
            {
                throw new ArgumentNullException("apiClient");
            }

            _apiClient = apiClient;
            _readbackWorkflow = new LmGatewayReadbackWorkflow(apiClient);
            _verificationBudget = verificationBudget;
            _verificationRetryDelay = verificationRetryDelay;
        }

        public async Task<LmGatewayBindingOutcome> ExecuteAsync(
            string baseUrl,
            LmGatewayBindingPlan plan,
            Func<string, LmGatewayCredentials> credentialProvider,
            Action<LmGatewayBindingProgress> progress,
            CancellationToken cancellationToken)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            LmGatewayBindingOutcome outcome = new LmGatewayBindingOutcome();
            for (int index = 0; index < plan.Items.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    outcome.Cancelled = true;
                    AddCancelledResults(plan, index, outcome);
                    return outcome;
                }

                LmGatewayBindingItem item = plan.Items[index];
                if (item == null || !item.IsValid)
                {
                    LmGatewayBindingResult invalidResult = CreateResult(
                        item,
                        LmGatewayBindingStatus.Invalid,
                        item == null || item.Validation == null
                            ? "Строка плана привязки ЛМ некорректна."
                            : item.Validation.JoinMessages());
                    outcome.Results.Add(invalidResult);
                    Report(progress, index + 1, plan.Items.Count, invalidResult, "Проверка", null);
                    continue;
                }

                LmGatewayCredentials credentials = null;
                LmConnectionRequest request = null;
                try
                {
                    int controllerPort;
                    if (!int.TryParse(item.Input.ControllerGrpcPort, out controllerPort) ||
                        controllerPort < 1 || controllerPort > 65535)
                    {
                        LmGatewayBindingResult portResult = CreateResult(
                            item,
                            LmGatewayBindingStatus.Invalid,
                            "gRPC-порт локального контроллера ЛМ некорректен.");
                        outcome.Results.Add(portResult);
                        Report(progress, index + 1, plan.Items.Count, portResult, "Проверка", null);
                        continue;
                    }

                    ReportPreparing(progress, index + 1, plan.Items.Count, item);
                    credentials = credentialProvider == null
                        ? null
                        : credentialProvider(item.Kkt.KktSerial);
                    if (credentials == null ||
                        string.IsNullOrWhiteSpace(credentials.Login) ||
                        string.IsNullOrWhiteSpace(credentials.Password))
                    {
                        LmGatewayBindingResult credentialsResult = CreateResult(
                            item,
                            LmGatewayBindingStatus.Invalid,
                            "Для ККТ не заданы логин и пароль ЛМ ЧЗ.");
                        outcome.Results.Add(credentialsResult);
                        Report(progress, index + 1, plan.Items.Count, credentialsResult, "Проверка", null);
                        continue;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        outcome.Cancelled = true;
                        AddCancelledResults(plan, index, outcome);
                        return outcome;
                    }

                    request = new LmConnectionRequest
                    {
                        Address = item.Input.ControllerAddress,
                        Port = controllerPort,
                        Login = credentials.Login,
                        Password = credentials.Password
                    };

                    ApiResponse response;
                    try
                    {
                        response = await _apiClient.ConfigureLmGatewayAsync(
                            baseUrl,
                            item.Kkt.InstanceId,
                            request,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        LmGatewayBindingResult unknownResult = CreateResult(
                            item,
                            LmGatewayBindingStatus.RequiresAttention,
                            "Ответ на запрос привязки не получен. Проверьте фактические настройки ЕСМ перед повтором.");
                        outcome.Results.Add(unknownResult);
                        Report(progress, index + 1, plan.Items.Count, unknownResult, "Требуется проверка", null);

                        if (cancellationToken.IsCancellationRequested)
                        {
                            outcome.Cancelled = true;
                            AddCancelledResults(plan, index + 1, outcome);
                            return outcome;
                        }

                        continue;
                    }

                    LmGatewayBindingResult bindingResult;
                    if (response != null && response.IsSuccess)
                    {
                        bindingResult = await VerifyAcceptedBindingAsync(
                            baseUrl,
                            item,
                            progress,
                            index + 1,
                            plan.Items.Count,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        bindingResult = BuildTransportResult(item, response);
                    }
                    outcome.Results.Add(bindingResult);
                    Report(
                        progress,
                        index + 1,
                        plan.Items.Count,
                        bindingResult,
                        GetStage(bindingResult.Status),
                        CreateSafeResponse(response));
                }
                finally
                {
                    request = null;
                    credentials = null;
                }
            }

            return outcome;
        }

        /// <summary>
        /// ЕСМ принимает запрос привязки раньше, чем успевает поднять сессию
        /// с контроллером и локальным модулем. Одной проверки на четыре
        /// секунды хватало только уже привязанной ККТ: новая привязка
        /// объявлялась неподтверждённой, хотя ЕСМ сообщал её через несколько
        /// секунд. Поэтому проверка повторяется до общего бюджета.
        /// </summary>
        private async Task<LmGatewayBindingResult> VerifyAcceptedBindingAsync(
            string baseUrl,
            LmGatewayBindingItem item,
            Action<LmGatewayBindingProgress> progress,
            int current,
            int total,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CreateResult(
                    item,
                    LmGatewayBindingStatus.BindingAccepted,
                    "ЕСМ принял запрос настройки; проверка результата отменена.");
            }

            DateTime deadline = DateTime.UtcNow.Add(_verificationBudget);
            LmGatewayReadbackObservation observation = null;
            int attempt = 0;
            while (true)
            {
                attempt++;
                observation = await ReadBindingOnceAsync(
                    baseUrl,
                    item,
                    cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    return CreateResult(
                        item,
                        LmGatewayBindingStatus.BindingAccepted,
                        "ЕСМ принял запрос настройки; проверка результата отменена.");
                }

                if (observation != null && observation.IsVerified)
                {
                    return CreateResult(
                        item,
                        LmGatewayBindingStatus.BindingVerified,
                        observation.Details,
                        observation.InfoResponseBody);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    break;
                }

                ReportWaitingForBinding(progress, current, total, item, attempt);
                try
                {
                    await Task.Delay(
                        _verificationRetryDelay,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return CreateResult(
                        item,
                        LmGatewayBindingStatus.BindingAccepted,
                        "ЕСМ принял запрос настройки; проверка результата отменена.");
                }
            }

            string waited = " Проверка повторялась " +
                ((int)Math.Round(_verificationBudget.TotalSeconds)).ToString(
                    CultureInfo.InvariantCulture) +
                " с, попыток: " + attempt.ToString(CultureInfo.InvariantCulture) + ".";
            if (observation == null)
            {
                return CreateResult(
                    item,
                    LmGatewayBindingStatus.BindingAccepted,
                    "ЕСМ принял запрос, но ответ на проверку не пришёл." + waited);
            }

            if (!observation.IsAvailable)
            {
                return CreateResult(
                    item,
                    LmGatewayBindingStatus.BindingAccepted,
                    "ЕСМ принял запрос, но результат не удалось проверить: " +
                        observation.Details + waited,
                    observation.InfoResponseBody);
            }

            if (observation.IdentityMatches && observation.HasLmConfiguration &&
                !observation.EndpointMatches.HasValue)
            {
                return CreateResult(
                    item,
                    LmGatewayBindingStatus.BindingObserved,
                    observation.Details,
                    observation.InfoResponseBody);
            }

            return CreateResult(
                item,
                LmGatewayBindingStatus.RequiresAttention,
                observation.Details + waited,
                observation.InfoResponseBody);
        }

        private async Task<LmGatewayReadbackObservation> ReadBindingOnceAsync(
            string baseUrl,
            LmGatewayBindingItem item,
            CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                TimeSpan attemptBudget =
                    TimeSpan.FromSeconds(VerificationAttemptSeconds);
                if (_verificationBudget < attemptBudget)
                {
                    attemptBudget = _verificationBudget;
                }

                timeout.CancelAfter(attemptBudget);
                try
                {
                    return await _readbackWorkflow.ReadAsync(
                        baseUrl,
                        item.Kkt,
                        item.Input.ExpectedLmAddress,
                        item.Input.ExpectedLmPort,
                        timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
        }

        private static void ReportWaitingForBinding(
            Action<LmGatewayBindingProgress> progress,
            int current,
            int total,
            LmGatewayBindingItem item,
            int attempt)
        {
            // Оператор смотрит в журнал: без этой строки ожидание выглядит
            // так же, как зависание.
            if (progress == null || (attempt != 1 && attempt % 3 != 0))
            {
                return;
            }

            progress(new LmGatewayBindingProgress
            {
                Current = current,
                Total = total,
                KktSerial = item == null || item.Kkt == null
                    ? string.Empty
                    : (item.Kkt.KktSerial ?? string.Empty),
                Stage = "Ожидание ЕСМ",
                Message = "ЕСМ принял привязку, но ещё не сообщает её обратно; " +
                    "проверка повторяется (попытка " +
                    attempt.ToString(CultureInfo.InvariantCulture) + ")."
            });
        }

        private static LmGatewayBindingResult BuildTransportResult(
            LmGatewayBindingItem item,
            ApiResponse response)
        {
            if (response == null || response.IsConnectionFailure)
            {
                return CreateResult(
                    item,
                    LmGatewayBindingStatus.RequiresAttention,
                    "Ответ ЕСМ не получен. Проверьте фактические настройки привязки перед повтором.");
            }

            if (response.IsSuccess)
            {
                return CreateResult(
                    item,
                    LmGatewayBindingStatus.BindingAccepted,
                    "ЕСМ принял запрос настройки привязки контроллера ЛМ.");
            }

            string details = SensitiveDataMasker.Mask(response.DecodedMessage);
            if (string.IsNullOrWhiteSpace(details))
            {
                details = response.StatusCode > 0
                    ? "ЕСМ отклонил привязку: HTTP " + response.StatusCode.ToString() + "."
                    : "ЕСМ отклонил запрос привязки контроллера ЛМ.";
            }

            return CreateResult(item, LmGatewayBindingStatus.BindingFailed, details);
        }

        private static LmGatewayBindingResult CreateResult(
            LmGatewayBindingItem item,
            LmGatewayBindingStatus status,
            string details)
        {
            return CreateResult(item, status, details, null);
        }

        private static LmGatewayBindingResult CreateResult(
            LmGatewayBindingItem item,
            LmGatewayBindingStatus status,
            string details,
            string diagnostics)
        {
            LmGatewayKkt kkt = item == null ? null : item.Kkt;
            return new LmGatewayBindingResult
            {
                InstanceId = kkt == null ? string.Empty : (kkt.InstanceId ?? string.Empty),
                KktSerial = kkt == null ? string.Empty : (kkt.KktSerial ?? string.Empty),
                KktInn = kkt == null ? string.Empty : (kkt.KktInn ?? string.Empty),
                Status = status,
                Details = SensitiveDataMasker.Mask(details),
                Diagnostics = string.IsNullOrWhiteSpace(diagnostics)
                    ? string.Empty
                    : "    Ответ /api/v2/info: " + diagnostics
            };
        }

        private static void AddCancelledResults(
            LmGatewayBindingPlan plan,
            int startIndex,
            LmGatewayBindingOutcome outcome)
        {
            for (int index = startIndex; index < plan.Items.Count; index++)
            {
                outcome.Results.Add(CreateResult(
                    plan.Items[index],
                    LmGatewayBindingStatus.Cancelled,
                    "Привязка не начата из-за отмены операции."));
            }
        }

        private static void ReportPreparing(
            Action<LmGatewayBindingProgress> progress,
            int current,
            int total,
            LmGatewayBindingItem item)
        {
            if (progress == null)
            {
                return;
            }

            progress(new LmGatewayBindingProgress
            {
                Current = current,
                Total = total,
                KktSerial = item == null || item.Kkt == null
                    ? string.Empty
                    : (item.Kkt.KktSerial ?? string.Empty),
                Stage = "Подготовка",
                Message = "Подготовка запроса привязки контроллера ЛМ."
            });
        }

        private static void Report(
            Action<LmGatewayBindingProgress> progress,
            int current,
            int total,
            LmGatewayBindingResult result,
            string stage,
            ApiResponse response)
        {
            if (progress == null)
            {
                return;
            }

            progress(new LmGatewayBindingProgress
            {
                Current = current,
                Total = total,
                KktSerial = result == null ? string.Empty : (result.KktSerial ?? string.Empty),
                Stage = stage,
                Message = result == null ? string.Empty : (result.Details ?? string.Empty),
                Diagnostics = result == null ? string.Empty : (result.Diagnostics ?? string.Empty),
                Response = response
            });
        }

        private static ApiResponse CreateSafeResponse(ApiResponse response)
        {
            if (response == null)
            {
                return null;
            }

            return new ApiResponse
            {
                Method = response.Method,
                Url = response.Url,
                RequestBody = SensitiveDataMasker.Mask(response.RequestBody),
                StatusCode = response.StatusCode,
                ReasonPhrase = SensitiveDataMasker.Mask(response.ReasonPhrase),
                ResponseBody = SensitiveDataMasker.Mask(response.ResponseBody),
                IsSuccess = response.IsSuccess,
                DecodedMessage = SensitiveDataMasker.Mask(response.DecodedMessage),
                IsConnectionFailure = response.IsConnectionFailure,
                IsTlsCertificateFailure = response.IsTlsCertificateFailure
            };
        }

        private static string GetStage(LmGatewayBindingStatus status)
        {
            if (status == LmGatewayBindingStatus.BindingVerified)
            {
                return "Подтверждено ЕСМ";
            }
            if (status == LmGatewayBindingStatus.BindingObserved)
            {
                return "Обнаружено ЕСМ";
            }
            if (status == LmGatewayBindingStatus.BindingAccepted)
            {
                return "Запрос принят";
            }
            if (status == LmGatewayBindingStatus.RequiresAttention)
            {
                return "Требуется проверка";
            }

            return "Ошибка привязки";
        }
    }
}
