using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.Shared.Services
{
    public sealed class LmGatewayReadbackWorkflow
    {
        private const int MaximumInfoBodyLength = 900;

        private readonly ITspiotApiClient _apiClient;

        public LmGatewayReadbackWorkflow(ITspiotApiClient apiClient)
        {
            if (apiClient == null)
            {
                throw new ArgumentNullException("apiClient");
            }

            _apiClient = apiClient;
        }

        public async Task<LmGatewayReadbackObservation> ReadAsync(
            string baseUrl,
            LmGatewayKkt kkt,
            string expectedLmAddress,
            string expectedLmPort,
            CancellationToken cancellationToken)
        {
            LmGatewayReadbackObservation observation = CreateObservation(kkt);
            if (kkt == null)
            {
                observation.Details = "Не задан экземпляр ККТ для проверки привязки.";
                return observation;
            }

            ApiResponse response;
            try
            {
                response = await _apiClient.GetLmInfoAsync(
                    baseUrl,
                    kkt.Port,
                    kkt.SoftPort,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                observation.Details = "Привязку не удалось проверить через /api/v2/info: " +
                    SensitiveDataMasker.Mask(ex.Message);
                return observation;
            }

            if (response != null && response.IsTlsCertificateFailure)
            {
                observation.Details =
                    "Привязку не удалось проверить через /api/v2/info: сертификат ЕСМ " +
                    "не прошёл проверку Windows. Установите доверенный корневой сертификат " +
                    "ЕСМ; не отключайте проверку TLS.";
                return observation;
            }
            if (response == null || response.IsConnectionFailure)
            {
                observation.Details = "Привязку не удалось проверить через /api/v2/info: нет ответа от экземпляра ККТ.";
                return observation;
            }
            if (!response.IsSuccess)
            {
                observation.Details = response.StatusCode > 0
                    ? "Привязку не удалось проверить через /api/v2/info: HTTP " +
                        response.StatusCode.ToString(CultureInfo.InvariantCulture) + "."
                    : "Привязку не удалось проверить через /api/v2/info.";
                return observation;
            }

            observation.InfoResponseBody = DescribeInfoBody(response.ResponseBody);

            LmGatewayInfo info;
            if (!LmGatewayInfoParser.TryParse(response.ResponseBody, out info))
            {
                observation.Details = "Ответ /api/v2/info не содержит ожидаемые данные ККТ и ЛМ.";
                return observation;
            }

            observation.IsAvailable = true;
            observation.IdentityMatches = string.Equals(
                    Trim(kkt.KktSerial),
                    Trim(info.KktSerial),
                    StringComparison.Ordinal) &&
                string.Equals(
                    Trim(kkt.KktInn),
                    Trim(info.KktInn),
                    StringComparison.Ordinal);
            observation.HasLmConfiguration = info.HasLmConfiguration;
            observation.LmAddress = Trim(info.LmAddress);
            observation.LmPort = Trim(info.LmPort);
            observation.LmStatus = Trim(info.LmStatus);
            observation.LmVersion = Trim(info.LmVersion);
            if (string.Equals(
                observation.LmStatus,
                "not_configured",
                StringComparison.OrdinalIgnoreCase))
            {
                observation.HasLmConfiguration = false;
            }

            if (!observation.IdentityMatches)
            {
                observation.Details = "Ответ /api/v2/info относится к другой ККТ или другому ИНН.";
                return observation;
            }
            if (!observation.HasLmConfiguration)
            {
                observation.Details = "ЕСМ не сообщает настроенную привязку ЛМ для этой ККТ.";
                return observation;
            }

            int observedPort;
            string normalizedObservedAddress;
            bool observedIsLoopback;
            if (!LmGatewayInputValidator.TryNormalizeTargetAddress(
                    observation.LmAddress,
                    out normalizedObservedAddress,
                    out observedIsLoopback) ||
                !int.TryParse(observation.LmPort, out observedPort) ||
                observedPort < 1 || observedPort > 65535)
            {
                observation.Details = "ЕСМ вернул некорректный endpoint ЛМ ЧЗ.";
                return observation;
            }
            observation.LmAddress = normalizedObservedAddress;
            observation.LmPort = observedPort.ToString(CultureInfo.InvariantCulture);

            bool hasExpectedEndpoint = !string.IsNullOrWhiteSpace(expectedLmAddress) ||
                !string.IsNullOrWhiteSpace(expectedLmPort);
            if (hasExpectedEndpoint)
            {
                int expectedPort = 0;
                string normalizedExpectedAddress;
                bool expectedIsLoopback;
                bool expectedValid = LmGatewayInputValidator.TryNormalizeTargetAddress(
                        expectedLmAddress,
                        out normalizedExpectedAddress,
                        out expectedIsLoopback) &&
                    int.TryParse(Trim(expectedLmPort), out expectedPort) &&
                    expectedPort >= 1 && expectedPort <= 65535;
                observation.EndpointMatches = expectedValid &&
                    string.Equals(
                        normalizedObservedAddress,
                        normalizedExpectedAddress,
                        StringComparison.Ordinal) &&
                    observedPort == expectedPort;
                if (!observation.EndpointMatches.Value)
                {
                    observation.Details = "ЕСМ сообщает целевой ЛМ " +
                        observation.LmAddress + ":" + observation.LmPort +
                        ", ожидалось " + Trim(expectedLmAddress) + ":" +
                        Trim(expectedLmPort) + ".";
                    return observation;
                }
            }

            if (!hasExpectedEndpoint)
            {
                observation.Details = "ЕСМ сообщает целевой ЛМ " +
                    observation.LmAddress + ":" + observation.LmPort +
                    ", но ожидаемый endpoint не задан для сверки.";
                return observation;
            }

            observation.Details = BuildVerifiedDetails(observation);
            return observation;
        }

        public async Task<IList<LmGatewayReadbackObservation>> ReadAllAsync(
            string baseUrl,
            IList<LmGatewayKkt> items,
            Func<LmGatewayKkt, LmGatewayTarget> expectedTargetProvider,
            Action<int, int, string> progress,
            CancellationToken cancellationToken)
        {
            List<LmGatewayReadbackObservation> result =
                new List<LmGatewayReadbackObservation>();
            if (items == null)
            {
                return result;
            }

            for (int index = 0; index < items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LmGatewayKkt kkt = items[index];
                if (progress != null)
                {
                    progress(index + 1, items.Count, Trim(kkt == null ? null : kkt.KktSerial));
                }

                using (CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    try
                    {
                        LmGatewayTarget expectedTarget = expectedTargetProvider == null
                            ? null
                            : expectedTargetProvider(kkt);
                        result.Add(await ReadAsync(
                            baseUrl,
                            kkt,
                            expectedTarget == null ? null : expectedTarget.Address,
                            expectedTarget == null
                                ? null
                                : expectedTarget.Port.ToString(CultureInfo.InvariantCulture),
                            timeout.Token).ConfigureAwait(false));
                    }
                    catch (OperationCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }

                        LmGatewayReadbackObservation unavailable = CreateObservation(kkt);
                        unavailable.Details = "Проверка /api/v2/info превысила 4 секунды.";
                        result.Add(unavailable);
                    }
                }
            }

            return result;
        }

        private static LmGatewayReadbackObservation CreateObservation(LmGatewayKkt kkt)
        {
            return new LmGatewayReadbackObservation
            {
                InstanceId = Trim(kkt == null ? null : kkt.InstanceId),
                KktSerial = Trim(kkt == null ? null : kkt.KktSerial),
                KktInn = Trim(kkt == null ? null : kkt.KktInn),
                LmAddress = string.Empty,
                LmPort = string.Empty,
                LmStatus = string.Empty,
                LmVersion = string.Empty,
                Details = string.Empty
            };
        }

        private static string BuildVerifiedDetails(LmGatewayReadbackObservation observation)
        {
            string lmStatus = observation.LmStatus ?? string.Empty;
            bool reportsError = lmStatus.StartsWith(
                "error",
                StringComparison.OrdinalIgnoreCase);
            string details = reportsError
                ? "Адрес привязки подтверждён ЕСМ: " +
                    observation.LmAddress + ":" + observation.LmPort + "."
                : "Привязка подтверждена ЕСМ: целевой ЛМ " +
                    observation.LmAddress + ":" + observation.LmPort + ".";
            if (!string.IsNullOrEmpty(lmStatus))
            {
                details += reportsError
                    ? " ЛМ сообщает ошибку: " + lmStatus + "."
                    : " Состояние: " + lmStatus + ".";
            }
            if (!string.IsNullOrEmpty(observation.LmVersion))
            {
                details += " Версия: " + observation.LmVersion + ".";
            }
            return details;
        }

        /// <summary>
        /// Замаскированная и обрезанная выдержка из ответа ЕСМ. Она нужна
        /// именно тогда, когда сверка не сошлась: иначе в журнале остаётся
        /// вывод без исходных данных, и проверить его нечем.
        /// </summary>
        private static string DescribeInfoBody(string body)
        {
            string value = (body ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return string.Empty;
            }

            value = SensitiveDataMasker.Mask(value);
            return value.Length > MaximumInfoBodyLength
                ? value.Substring(0, MaximumInfoBodyLength) + "..."
                : value;
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
