using System;
using System.Collections.Generic;
using System.IO;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

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
            if (platform == null)
            {
                throw new ArgumentNullException("platform");
            }
            _platform = platform;
            _removalPlatform = platform as ILmServiceRemovalPlatform;
        }

        internal LmServiceProvisioner(
            ILmProvisioningPlatform platform,
            ILmServiceRemovalPlatform removalPlatform)
        {
            if (platform == null)
            {
                throw new ArgumentNullException("platform");
            }
            if (removalPlatform == null)
            {
                throw new ArgumentNullException("removalPlatform");
            }
            _platform = platform;
            _removalPlatform = removalPlatform;
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

        internal LmServiceProvisioningBatchResult RemoveAllManaged(
            LmServiceProvisioningBatchRequest request,
            ILmProvisioningCancellation cancellation)
        {
            if (_removalPlatform == null)
            {
                throw new NotSupportedException("Removal platform is unavailable.");
            }

            LmServiceProvisioningBatchResult result = CreateBatchResult(request);
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid || request.Operation != LmServiceOperation.RemoveAllManaged)
            {
                result.Status = LmServiceProvisioningStatus.Failed;
                AddRemovalValidationFailures(result, request, validation);
                return result;
            }

            ILmProvisioningCancellation effectiveCancellation =
                cancellation ?? NeverCancelLmProvisioning.Instance;
            using (_removalPlatform.AcquireMachineLock())
            {
                for (int index = 0; index < request.RemovalConfirmations.Count; index++)
                {
                    LmRemovalConfirmation confirmation = request.RemovalConfirmations[index];
                    if (effectiveCancellation.IsCancellationRequested)
                    {
                        for (int remaining = index;
                            remaining < request.RemovalConfirmations.Count;
                            remaining++)
                        {
                            result.Items.Add(CreateRemovalResult(
                                request.RemovalConfirmations[remaining],
                                LmServiceProvisioningStatus.Cancelled,
                                "Операция отменена до начала удаления этой службы."));
                        }
                        break;
                    }

                    LmServiceProvisioningBatchRequest single =
                        new LmServiceProvisioningBatchRequest
                        {
                            SchemaVersion = ProvisioningRequestValidator.LegacySchemaVersion,
                            Operation = LmServiceOperation.RemoveManaged,
                            OperationId = request.OperationId,
                            InitiatingSid = request.InitiatingSid,
                            RemovalConfirmation = confirmation
                        };
                    single.PlanHash = CanonicalLmPlanHasher.Compute(single);
                    result.Items.Add(
                        new LmServiceRemovalWorkflow(_removalPlatform).RemoveManaged(single));
                }
            }

            result.Status = AggregateRemovalStatus(result.Items);
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
                        : validation.JoinMessages(),
                    OperationId = request == null ? string.Empty : request.OperationId,
                    PlanHash = request == null ? string.Empty : request.PlanHash
                };
            }

            try
            {
                using (_platform.AcquireMachineLock())
                using (ILockedControllerInstaller installer = _platform.PrepareInstaller(
                    request.InstallerSelection,
                    request.OperationId))
                {
                    return RunPreparedControllerInstaller(request, installer);
                }
            }
            catch (Exception ex)
            {
                return new LmControllerInstallResult
                {
                    Status = ex is NotSupportedException
                        ? LmServiceProvisioningStatus.UnsupportedController
                        : LmServiceProvisioningStatus.Failed,
                    Message = SafeMessage(ex),
                    OperationId = request == null ? string.Empty : request.OperationId,
                    PlanHash = request == null ? string.Empty : request.PlanHash
                };
            }
        }

        private static LmServiceProvisioningBatchResult CreateBatchResult(
            LmServiceProvisioningBatchRequest request)
        {
            return new LmServiceProvisioningBatchResult
            {
                SchemaVersion = request == null
                    ? ProvisioningRequestValidator.LegacySchemaVersion
                    : request.SchemaVersion,
                OperationId = request == null ? string.Empty : request.OperationId,
                PlanHash = request == null ? string.Empty : request.PlanHash,
                Status = LmServiceProvisioningStatus.Pending
            };
        }

        private static void AddRemovalValidationFailures(
            LmServiceProvisioningBatchResult result,
            LmServiceProvisioningBatchRequest request,
            ValidationResult validation)
        {
            string message = validation.IsValid
                ? "Запрошена неверная операция помощника."
                : validation.JoinMessages();
            if (request == null || request.RemovalConfirmations == null)
            {
                return;
            }
            for (int index = 0; index < request.RemovalConfirmations.Count; index++)
            {
                result.Items.Add(CreateRemovalResult(
                    request.RemovalConfirmations[index],
                    LmServiceProvisioningStatus.Failed,
                    message));
            }
        }

        private LmControllerInstallResult RunPreparedControllerInstaller(
            LmServiceProvisioningBatchRequest request,
            ILockedControllerInstaller installer)
        {
            List<string> serials = new List<string>(_platform.GetManagedSerials());
            serials.Sort(StringComparer.Ordinal);
            for (int index = 0; index < serials.Count; index++)
            {
                _platform.MarkVersionPending(serials[index], request.OperationId);
            }
            for (int index = 0; index < serials.Count; index++)
            {
                _platform.RequestStop(new LmServiceProvisioningItemRequest
                {
                    KktSerial = serials[index]
                });
            }

            LmControllerInstallResult installed = installer.Run();
            installed.OperationId = request.OperationId;
            installed.PlanHash = request.PlanHash;
            return installed;
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

        private static LmServiceProvisioningItemResult CreateRemovalResult(
            LmRemovalConfirmation confirmation,
            LmServiceProvisioningStatus status,
            string message)
        {
            return new LmServiceProvisioningItemResult
            {
                KktSerial = confirmation == null ? string.Empty : confirmation.KktSerial,
                Status = status,
                Message = message ?? string.Empty
            };
        }

        private static LmServiceProvisioningStatus AggregateRemovalStatus(
            IList<LmServiceProvisioningItemResult> items)
        {
            if (items == null || items.Count == 0)
            {
                return LmServiceProvisioningStatus.Failed;
            }

            LmServiceProvisioningStatus status =
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained;
            for (int index = 0; index < items.Count; index++)
            {
                LmServiceProvisioningStatus itemStatus = items[index].Status;
                if (itemStatus == LmServiceProvisioningStatus.Failed ||
                    itemStatus == LmServiceProvisioningStatus.RemovalBlocked)
                {
                    return itemStatus;
                }
                if (itemStatus == LmServiceProvisioningStatus.CleanupPending ||
                    itemStatus == LmServiceProvisioningStatus.MarkedForDelete ||
                    itemStatus == LmServiceProvisioningStatus.RequiresAttention)
                {
                    status = itemStatus;
                }
                else if (itemStatus == LmServiceProvisioningStatus.Cancelled &&
                    status == LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained)
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
