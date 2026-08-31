using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class KktDeletionWorkflow
    {
        private const int VerificationAttemptLimit = 3;
        private readonly ITspiotApiClient _client;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public KktDeletionWorkflow(ITspiotApiClient client)
            : this(client, DefaultDelayAsync)
        {
        }

        public KktDeletionWorkflow(
            ITspiotApiClient client,
            Func<TimeSpan, CancellationToken, Task> delay)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }
            if (delay == null)
            {
                throw new ArgumentNullException("delay");
            }

            _client = client;
            _delay = delay;
        }

        public async Task<KktDeletionOutcome> DeleteAsync(
            string baseUrl,
            string id,
            string confirmation,
            Action<ApiResponse> responseCallback,
            CancellationToken cancellationToken)
        {
            string normalizedId = (id ?? string.Empty).Trim();
            if (!KktDeletionConfirmation.IsValidKktId(normalizedId))
            {
                return Blocked("Серийный номер ККТ должен содержать ровно 14 ASCII-цифр.");
            }
            ApiResponse currentResponse = await _client.GetInstancesAsync(baseUrl, cancellationToken).ConfigureAwait(false);
            Report(responseCallback, currentResponse);
            if (currentResponse == null || !currentResponse.IsSuccess)
            {
                return Blocked("Не удалось повторно получить список экземпляров перед удалением.");
            }

            IList<KktInstanceInfo> currentInstances;
            if (!InstanceInfoParser.TryParse(currentResponse, out currentInstances))
            {
                return Blocked("ЕСМ вернул неожиданный список экземпляров. Удаление заблокировано.");
            }

            KktDeletionPlan plan = KktDeletionPlanner.Build(currentInstances);
            KktDeletionCandidate selected = FindCandidate(plan, normalizedId);
            if (selected == null)
            {
                return Blocked("Выбранный экземпляр больше не найден. Обновите список ККТ.");
            }
            if (!selected.CanDelete)
            {
                return Blocked(string.IsNullOrWhiteSpace(selected.ProtectionReason)
                    ? "Удаление выбранного экземпляра заблокировано."
                    : selected.ProtectionReason);
            }

            bool confirmationMatches = selected.IsPrimary
                ? KktDeletionConfirmation.MatchesFullSerial(normalizedId, confirmation)
                : KktDeletionConfirmation.Matches(normalizedId, confirmation);
            if (!confirmationMatches)
            {
                return Blocked(selected.IsPrimary
                    ? "Для удаления первой ККТ введите ее полный 14-значный серийный номер."
                    : "Последние четыре цифры серийного номера введены неверно.");
            }

            ApiResponse deleteResponse = await _client.DeleteInstanceAsync(baseUrl, normalizedId, cancellationToken).ConfigureAwait(false);
            Report(responseCallback, deleteResponse);
            if (deleteResponse == null || !deleteResponse.IsSuccess)
            {
                return new KktDeletionOutcome
                {
                    IsSuccess = false,
                    DeleteRequestAccepted = false,
                    DeleteResponse = deleteResponse,
                    Message = "ЕСМ не подтвердил запрос на удаление ККТ."
                };
            }

            KktDeletionOutcome outcome = new KktDeletionOutcome
            {
                DeleteRequestAccepted = true,
                DeleteResponse = deleteResponse
            };

            for (int attempt = 1; attempt <= VerificationAttemptLimit; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt > 1)
                {
                    await _delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }

                ApiResponse verificationResponse = await _client.GetInstancesAsync(baseUrl, cancellationToken).ConfigureAwait(false);
                Report(responseCallback, verificationResponse);
                outcome.VerificationAttempts = attempt;

                IList<KktInstanceInfo> verificationInstances;
                if (verificationResponse != null && verificationResponse.IsSuccess &&
                    InstanceInfoParser.TryParse(verificationResponse, out verificationInstances) &&
                    FindInstance(verificationInstances, normalizedId) == null)
                {
                    outcome.IsSuccess = true;
                    outcome.Message = "ККТ удалена. Отсутствие экземпляра подтверждено повторным запросом.";
                    return outcome;
                }
            }

            outcome.Message = "Запрос DELETE принят, но удаление не подтверждено: экземпляр остался в списке или ЕСМ вернул некорректный ответ.";
            return outcome;
        }

        private static KktDeletionCandidate FindCandidate(KktDeletionPlan plan, string id)
        {
            for (int i = 0; i < plan.Candidates.Count; i++)
            {
                KktInstanceInfo instance = plan.Candidates[i].Instance;
                if (instance != null && string.Equals((instance.Id ?? string.Empty).Trim(), id, StringComparison.Ordinal))
                {
                    return plan.Candidates[i];
                }
            }

            return null;
        }

        private static KktInstanceInfo FindInstance(IList<KktInstanceInfo> instances, string id)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                KktInstanceInfo instance = instances[i];
                if (instance != null && string.Equals((instance.Id ?? string.Empty).Trim(), id, StringComparison.Ordinal))
                {
                    return instance;
                }
            }

            return null;
        }

        private static KktDeletionOutcome Blocked(string message)
        {
            return new KktDeletionOutcome
            {
                IsBlocked = true,
                IsSuccess = false,
                Message = message
            };
        }

        private static void Report(Action<ApiResponse> callback, ApiResponse response)
        {
            if (callback != null)
            {
                callback(response);
            }
        }

        private static Task DefaultDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }
}
