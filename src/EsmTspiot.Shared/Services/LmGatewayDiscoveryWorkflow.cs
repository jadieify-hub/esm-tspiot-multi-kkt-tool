using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class LmGatewayDiscoveryWorkflow
    {
        private readonly ITspiotApiClient _apiClient;

        public LmGatewayDiscoveryWorkflow(ITspiotApiClient apiClient)
        {
            if (apiClient == null)
            {
                throw new ArgumentNullException("apiClient");
            }

            _apiClient = apiClient;
        }

        public async Task<LmGatewayDiscovery> DiscoverAsync(
            string baseUrl,
            Action<LmGatewayProgress> progress,
            CancellationToken cancellationToken)
        {
            LmGatewayDiscovery discovery = new LmGatewayDiscovery();
            cancellationToken.ThrowIfCancellationRequested();

            ApiResponse instancesResponse = await _apiClient.GetInstancesAsync(
                baseUrl,
                cancellationToken).ConfigureAwait(false);
            if (instancesResponse == null || !instancesResponse.IsSuccess)
            {
                discovery.ErrorMessage = "Не удалось получить список экземпляров ККТ из ЕСМ.";
                return discovery;
            }

            IList<KktInstanceInfo> instances;
            if (!InstanceInfoParser.TryParse(instancesResponse, out instances))
            {
                discovery.ErrorMessage = "ЕСМ вернул некорректный список экземпляров ККТ.";
                return discovery;
            }

            for (int index = 0; index < instances.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                KktInstanceInfo instance = instances[index];
                string instanceId = (instance.Id ?? string.Empty).Trim();
                ReportProgress(progress, index + 1, instances.Count, instanceId, "Чтение данных ККТ");

                ApiResponse detailResponse = await _apiClient.GetInstanceAsync(
                    baseUrl,
                    instanceId,
                    cancellationToken).ConfigureAwait(false);
                if (detailResponse == null || !detailResponse.IsSuccess)
                {
                    AddIssue(discovery, instanceId, "Не удалось получить данные экземпляра ККТ из ЕСМ.");
                    continue;
                }

                KktInstanceDetails details;
                if (!InstanceDetailsParser.TryParse(detailResponse.ResponseBody, out details))
                {
                    AddIssue(discovery, instanceId, "ЕСМ вернул некорректные данные экземпляра ККТ.");
                    continue;
                }

                if (!details.HasCompleteRegistrationData)
                {
                    AddIssue(discovery, instanceId, "Экземпляр ККТ не зарегистрирован полностью и исключён из привязки ЛМ.");
                    continue;
                }

                KktRegistrationData registration = details.RegistrationData;
                string kktSerial = (registration.KktSerial ?? string.Empty).Trim();
                if (!string.Equals(instanceId, kktSerial, StringComparison.Ordinal))
                {
                    AddIssue(discovery, instanceId, "Серийный номер зарегистрированной ККТ не совпадает с идентификатором экземпляра ЕСМ.");
                    continue;
                }

                discovery.Items.Add(new LmGatewayKkt
                {
                    InstanceId = instanceId,
                    KktSerial = kktSerial,
                    KktInn = (registration.KktInn ?? string.Empty).Trim(),
                    FnSerial = (registration.FnSerial ?? string.Empty).Trim(),
                    Port = (instance.Port ?? string.Empty).Trim(),
                    SoftPort = (instance.SoftPort ?? string.Empty).Trim(),
                    DkktPort = (instance.DkktPort ?? string.Empty).Trim(),
                    ServiceState = (instance.ServiceState ?? string.Empty).Trim()
                });
            }

            return discovery;
        }

        private static void AddIssue(LmGatewayDiscovery discovery, string instanceId, string message)
        {
            discovery.Issues.Add(new LmGatewayDiscoveryIssue
            {
                InstanceId = instanceId,
                Message = message
            });
        }

        private static void ReportProgress(
            Action<LmGatewayProgress> progress,
            int current,
            int total,
            string instanceId,
            string message)
        {
            if (progress == null)
            {
                return;
            }

            progress(new LmGatewayProgress
            {
                Current = current,
                Total = total,
                InstanceId = instanceId,
                Stage = "Обнаружение",
                Message = message
            });
        }
    }
}
