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
        private readonly ITspiotApiClient _apiClient;

        public LmGatewayBindingWorkflow(ITspiotApiClient apiClient)
        {
            if (apiClient == null) throw new ArgumentNullException("apiClient");
            _apiClient = apiClient;
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

                    // Запись привязки и готовность ЛМ — разные операции.
                    // /api/v2/info читается только по запросу диагностики.
                    LmGatewayBindingResult bindingResult =
                        BuildTransportResult(item, response);
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
            LmGatewayKkt kkt = item == null ? null : item.Kkt;
            return new LmGatewayBindingResult
            {
                InstanceId = kkt == null ? string.Empty : (kkt.InstanceId ?? string.Empty),
                KktSerial = kkt == null ? string.Empty : (kkt.KktSerial ?? string.Empty),
                KktInn = kkt == null ? string.Empty : (kkt.KktInn ?? string.Empty),
                Status = status,
                Details = SensitiveDataMasker.Mask(details)
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
