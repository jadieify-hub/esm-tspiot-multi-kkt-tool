using System;
using System.Collections.Generic;
using System.IO;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal enum LmProvisioningObservedState
    {
        Absent = 0,
        MatchingReady = 1,
        MatchingStopped = 2,
        OwnedMismatch = 3,
        Foreign = 4,
        VersionPending = 5,
        RequiresAttention = 6
    }

    internal enum LmProvisioningJournalStage
    {
        Preparing = 1,
        Updating = 2,
        ProfileReady = 3,
        ServiceReady = 4,
        Started = 5,
        Failed = 6,
        Deleting = 7,
        Cleaning = 8
    }

    internal sealed class LmVerifiedController
    {
        internal string Version { get; set; }
        internal string BinarySha256 { get; set; }
        internal string SupervisorSha256 { get; set; }
    }

    internal sealed class LmReadinessResult
    {
        private LmReadinessResult(bool isReady, string message)
        {
            IsReady = isReady;
            Message = message ?? string.Empty;
        }

        internal bool IsReady { get; private set; }
        internal string Message { get; private set; }

        internal static LmReadinessResult Ready()
        {
            return new LmReadinessResult(true, string.Empty);
        }

        internal static LmReadinessResult Failed(string message)
        {
            return new LmReadinessResult(false, message);
        }
    }

    internal interface ILmProvisioningCancellation
    {
        bool IsCancellationRequested { get; }
    }

    internal sealed class NeverCancelLmProvisioning : ILmProvisioningCancellation
    {
        internal static readonly NeverCancelLmProvisioning Instance =
            new NeverCancelLmProvisioning();

        private NeverCancelLmProvisioning()
        {
        }

        public bool IsCancellationRequested
        {
            get { return false; }
        }
    }

    internal interface ILockedControllerInstaller : IDisposable
    {
        LmControllerInstallResult Run();
    }

    internal interface ILmProvisioningPlatform
    {
        IDisposable AcquireMachineLock();
        IDisposable AcquireItemLock(string kktSerial);
        void Reconcile(LmServiceProvisioningItemRequest item, string operationId);
        LmVerifiedController VerifyController();
        LmProvisioningObservedState Inspect(
            LmServiceProvisioningItemRequest item,
            string initiatingSid);
        IDisposable ReservePorts(LmServiceProvisioningItemRequest item);
        string DeriveServiceSid(string kktSerial);
        void WriteJournal(
            LmServiceProvisioningItemRequest item,
            string operationId,
            LmProvisioningJournalStage stage);
        void PrepareProfile(LmServiceProvisioningItemRequest item, string serviceSid);
        void ConfigureService(
            LmServiceProvisioningItemRequest item,
            string serviceSid,
            string initiatingSid);
        void Start(LmServiceProvisioningItemRequest item);
        void RequestStop(LmServiceProvisioningItemRequest item);
        LmReadinessResult Probe(LmServiceProvisioningItemRequest item);
        void WriteManifest(
            LmServiceProvisioningItemRequest item,
            LmVerifiedController controller,
            string serviceSid,
            string operationId);
        void CompleteJournal(LmServiceProvisioningItemRequest item, string operationId);
        IList<string> GetManagedSerials();
        void MarkVersionPending(string kktSerial, string operationId);
        ILockedControllerInstaller PrepareInstaller(
            LmControllerInstallerSelection selection,
            string operationId);
    }

    internal sealed class LmServiceProvisioner
    {
        private readonly ILmProvisioningPlatform _platform;
        private readonly ILmServiceRemovalPlatform _removalPlatform;

        internal LmServiceProvisioner(ILmProvisioningPlatform platform)
        {
            _platform = platform ?? throw new ArgumentNullException("platform");
            _removalPlatform = platform as ILmServiceRemovalPlatform;
        }

        internal LmServiceProvisioningItemResult RemoveManaged(
            LmServiceProvisioningBatchRequest request)
        {
            if (_removalPlatform == null)
            {
                throw new NotSupportedException("Removal platform is unavailable.");
            }
            return new LmServiceRemovalWorkflow(_removalPlatform).RemoveManaged(request);
        }

        internal LmServiceProvisioningItemResult CleanupManaged(
            LmServiceProvisioningBatchRequest request)
        {
            if (_removalPlatform == null)
            {
                throw new NotSupportedException("Cleanup platform is unavailable.");
            }
            return new LmServiceRemovalWorkflow(_removalPlatform).CleanupManaged(request);
        }

        internal LmServiceProvisioningBatchResult EnsureBatch(
            LmServiceProvisioningBatchRequest request,
            ILmProvisioningCancellation cancellation)
        {
            LmServiceProvisioningBatchResult result = CreateBatchResult(request);
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid || request.Operation != LmServiceOperation.EnsureBatch)
            {
                result.Status = LmServiceProvisioningStatus.Failed;
                AddValidationFailures(result, request, validation);
                return result;
            }

            ILmProvisioningCancellation effectiveCancellation =
                cancellation ?? NeverCancelLmProvisioning.Instance;
            List<LmServiceProvisioningItemRequest> items =
                new List<LmServiceProvisioningItemRequest>(request.Items);
            items.Sort(delegate(
                LmServiceProvisioningItemRequest left,
                LmServiceProvisioningItemRequest right)
            {
                return string.CompareOrdinal(left.KktSerial, right.KktSerial);
            });

            using (_platform.AcquireMachineLock())
            {
                for (int index = 0; index < items.Count; index++)
                {
                    LmServiceProvisioningItemRequest item = items[index];
                    if (effectiveCancellation.IsCancellationRequested)
                    {
                        for (int remaining = index; remaining < items.Count; remaining++)
                        {
                            result.Items.Add(CreateItemResult(
                                items[remaining],
                                LmServiceProvisioningStatus.Cancelled,
                                "Операция отменена до начала обработки ККТ."));
                        }
                        break;
                    }

                    using (_platform.AcquireItemLock(item.KktSerial))
                    {
                        result.Items.Add(EnsureOne(request, item));
                    }
                }
            }

            result.Status = AggregateStatus(result.Items);
            return result;
        }

        internal LmControllerInstallResult InstallControllerVersion(
            LmServiceProvisioningBatchRequest request)
        {
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid || request.Operation != LmServiceOperation.InstallControllerVersion)
            {
                return new LmControllerInstallResult
                {
                    Status = LmServiceProvisioningStatus.Failed,
                    Message = validation.IsValid
                        ? "Запрошена неверная операция помощника."
                        : validation.JoinMessages()
                };
            }

            try
            {
                using (_platform.AcquireMachineLock())
                using (ILockedControllerInstaller installer = _platform.PrepareInstaller(
                    request.InstallerSelection,
                    request.OperationId))
                {
                    List<string> serials = new List<string>(_platform.GetManagedSerials());
                    serials.Sort(StringComparer.Ordinal);
                    for (int index = 0; index < serials.Count; index++)
                    {
                        _platform.MarkVersionPending(serials[index], request.OperationId);
                    }
                    for (int index = 0; index < serials.Count; index++)
                    {
                        LmServiceProvisioningItemRequest item = new LmServiceProvisioningItemRequest
                        {
                            KktSerial = serials[index]
                        };
                        _platform.RequestStop(item);
                    }

                    return installer.Run();
                }
            }
            catch (Exception ex)
            {
                return new LmControllerInstallResult
                {
                    Status = ex is NotSupportedException
                        ? LmServiceProvisioningStatus.UnsupportedController
                        : LmServiceProvisioningStatus.Failed,
                    Message = SafeMessage(ex)
                };
            }
        }

        private LmServiceProvisioningItemResult EnsureOne(
            LmServiceProvisioningBatchRequest request,
            LmServiceProvisioningItemRequest item)
        {
            bool mutationStarted = false;
            bool startMayHaveBeenIssued = false;
            IDisposable reservation = null;
            try
            {
                _platform.Reconcile(item, request.OperationId);
                LmVerifiedController controller = _platform.VerifyController();
                LmProvisioningObservedState state = _platform.Inspect(
                    item,
                    request.InitiatingSid);

                if (state == LmProvisioningObservedState.Foreign ||
                    state == LmProvisioningObservedState.RequiresAttention)
                {
                    return CreateItemResult(
                        item,
                        LmServiceProvisioningStatus.RequiresAttention,
                        "Найденная служба не подтверждена как управляемая приложением.");
                }

                if (state == LmProvisioningObservedState.MatchingReady)
                {
                    LmReadinessResult unchangedReadiness = _platform.Probe(item);
                    return unchangedReadiness.IsReady
                        ? CreateItemResult(
                            item,
                            LmServiceProvisioningStatus.Succeeded,
                            "Служба уже готова, изменений не требуется.")
                        : CreateItemResult(
                            item,
                            LmServiceProvisioningStatus.RequiresAttention,
                            unchangedReadiness.Message);
                }

                if (state == LmProvisioningObservedState.OwnedMismatch ||
                    state == LmProvisioningObservedState.VersionPending)
                {
                    mutationStarted = true;
                    _platform.WriteJournal(
                        item,
                        request.OperationId,
                        LmProvisioningJournalStage.Updating);
                    _platform.RequestStop(item);
                }

                reservation = _platform.ReservePorts(item);
                if (!mutationStarted)
                {
                    mutationStarted = true;
                    _platform.WriteJournal(
                        item,
                        request.OperationId,
                        state == LmProvisioningObservedState.Absent
                            ? LmProvisioningJournalStage.Preparing
                            : LmProvisioningJournalStage.Updating);
                }

                string serviceSid = _platform.DeriveServiceSid(item.KktSerial);
                bool configure = state != LmProvisioningObservedState.MatchingStopped;
                if (configure)
                {
                    _platform.PrepareProfile(item, serviceSid);
                    _platform.WriteJournal(
                        item,
                        request.OperationId,
                        LmProvisioningJournalStage.ProfileReady);
                    _platform.ConfigureService(item, serviceSid, request.InitiatingSid);
                    _platform.WriteJournal(
                        item,
                        request.OperationId,
                        LmProvisioningJournalStage.ServiceReady);
                }

                reservation.Dispose();
                reservation = null;
                startMayHaveBeenIssued = true;
                _platform.Start(item);
                _platform.WriteJournal(
                    item,
                    request.OperationId,
                    LmProvisioningJournalStage.Started);

                LmReadinessResult readiness = _platform.Probe(item);
                if (!readiness.IsReady)
                {
                    _platform.RequestStop(item);
                    _platform.WriteJournal(
                        item,
                        request.OperationId,
                        LmProvisioningJournalStage.Failed);
                    return CreateItemResult(
                        item,
                        LmServiceProvisioningStatus.Failed,
                        readiness.Message);
                }

                _platform.WriteManifest(
                    item,
                    controller,
                    serviceSid,
                    request.OperationId);
                _platform.CompleteJournal(item, request.OperationId);
                return CreateItemResult(
                    item,
                    LmServiceProvisioningStatus.Succeeded,
                    "Служба контроллера ЛМ создана и проверена.");
            }
            catch (Exception ex)
            {
                if (mutationStarted)
                {
                    if (startMayHaveBeenIssued)
                    {
                        TryStop(item);
                    }
                    TryRecordFailure(item, request.OperationId);
                }
                return CreateItemResult(
                    item,
                    ex is NotSupportedException
                        ? LmServiceProvisioningStatus.UnsupportedController
                        : LmServiceProvisioningStatus.Failed,
                    SafeMessage(ex));
            }
            finally
            {
                if (reservation != null)
                {
                    reservation.Dispose();
                }
            }
        }

        private void TryStop(LmServiceProvisioningItemRequest item)
        {
            try
            {
                _platform.RequestStop(item);
            }
            catch
            {
            }
        }

        private void TryRecordFailure(
            LmServiceProvisioningItemRequest item,
            string operationId)
        {
            try
            {
                _platform.WriteJournal(
                    item,
                    operationId,
                    LmProvisioningJournalStage.Failed);
            }
            catch
            {
            }
        }

        private static LmServiceProvisioningBatchResult CreateBatchResult(
            LmServiceProvisioningBatchRequest request)
        {
            return new LmServiceProvisioningBatchResult
            {
                SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                OperationId = request == null ? string.Empty : request.OperationId,
                PlanHash = request == null ? string.Empty : request.PlanHash,
                Status = LmServiceProvisioningStatus.Pending
            };
        }

        private static void AddValidationFailures(
            LmServiceProvisioningBatchResult result,
            LmServiceProvisioningBatchRequest request,
            ValidationResult validation)
        {
            string message = validation.IsValid
                ? "Запрошена неверная операция помощника."
                : validation.JoinMessages();
            if (request == null || request.Items == null || request.Items.Count == 0)
            {
                return;
            }
            for (int index = 0; index < request.Items.Count; index++)
            {
                result.Items.Add(CreateItemResult(
                    request.Items[index],
                    LmServiceProvisioningStatus.Failed,
                    message));
            }
        }

        private static LmServiceProvisioningItemResult CreateItemResult(
            LmServiceProvisioningItemRequest item,
            LmServiceProvisioningStatus status,
            string message)
        {
            return new LmServiceProvisioningItemResult
            {
                KktSerial = item == null ? string.Empty : item.KktSerial,
                Status = status,
                Message = message ?? string.Empty
            };
        }

        private static LmServiceProvisioningStatus AggregateStatus(
            IList<LmServiceProvisioningItemResult> items)
        {
            LmServiceProvisioningStatus status = LmServiceProvisioningStatus.Succeeded;
            for (int index = 0; index < items.Count; index++)
            {
                if (items[index].Status == LmServiceProvisioningStatus.Failed)
                {
                    return LmServiceProvisioningStatus.Failed;
                }
                if (items[index].Status == LmServiceProvisioningStatus.UnsupportedController)
                {
                    status = LmServiceProvisioningStatus.UnsupportedController;
                }
                if (items[index].Status == LmServiceProvisioningStatus.RequiresAttention)
                {
                    if (status == LmServiceProvisioningStatus.Succeeded)
                    {
                        status = LmServiceProvisioningStatus.RequiresAttention;
                    }
                }
                else if (items[index].Status == LmServiceProvisioningStatus.Cancelled &&
                    status == LmServiceProvisioningStatus.Succeeded)
                {
                    status = LmServiceProvisioningStatus.Cancelled;
                }
            }
            return status;
        }

        private static string SafeMessage(Exception exception)
        {
            if (exception is InvalidDataException ||
                exception is InvalidOperationException ||
                exception is NotSupportedException ||
                exception is UnauthorizedAccessException ||
                exception is IOException)
            {
                return exception.Message;
            }
            return "Операция завершилась ошибкой: " + exception.GetType().Name + ".";
        }
    }
}
