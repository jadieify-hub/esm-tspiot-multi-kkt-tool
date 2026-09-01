using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface ILegacyManagedStateCleanup
    {
        IList<string> ReadOwnedSerials();
        LmServiceProvisioningItemResult Cleanup(
            string kktSerial,
            string operationId,
            string initiatingSid);
    }

    internal sealed class LegacyManagedStateMigrationResult
    {
        internal LegacyManagedStateMigrationResult()
        {
            Items = new List<LmServiceProvisioningItemResult>();
        }

        internal bool IsComplete { get; set; }
        internal bool IsCancelled { get; set; }
        internal IList<LmServiceProvisioningItemResult> Items { get; private set; }

        internal string DescribeFailures()
        {
            List<string> failures = new List<string>();
            for (int index = 0; index < Items.Count; index++)
            {
                if (!IsAccepted(Items[index].Status))
                {
                    failures.Add(Items[index].FormatLogLine());
                }
            }
            return string.Join(Environment.NewLine, failures.ToArray());
        }

        internal static bool IsAccepted(LmServiceProvisioningStatus status)
        {
            return status == LmServiceProvisioningStatus.Succeeded ||
                status == LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained ||
                status == LmServiceProvisioningStatus.SharedLocalModuleRetained;
        }
    }

    internal sealed class LegacyManagedStateMigrationWorkflow
    {
        private const int MaximumAttempts = 3;
        private readonly ILegacyManagedStateCleanup _cleanup;
        private readonly Action _delay;

        internal LegacyManagedStateMigrationWorkflow(
            ILegacyManagedStateCleanup cleanup,
            Action delay)
        {
            if (cleanup == null) throw new ArgumentNullException("cleanup");
            if (delay == null) throw new ArgumentNullException("delay");
            _cleanup = cleanup;
            _delay = delay;
        }

        internal LegacyManagedStateMigrationResult Execute(
            string operationId,
            string initiatingSid,
            ILmProvisioningCancellation cancellation)
        {
            if (!ProvisionerCommandLine.IsGuidN(operationId))
            {
                throw new ArgumentException(
                    "Operation id must contain a 32-character GUID.",
                    "operationId");
            }
            LegacyManagedStateMigrationResult result =
                new LegacyManagedStateMigrationResult();
            IList<string> serials = _cleanup.ReadOwnedSerials();
            for (int index = 0; index < serials.Count; index++)
            {
                if (cancellation != null && cancellation.IsCancellationRequested)
                {
                    result.IsCancelled = true;
                    result.IsComplete = false;
                    return result;
                }
                LmServiceProvisioningItemResult item = null;
                for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
                {
                    if (cancellation != null && cancellation.IsCancellationRequested)
                    {
                        result.IsCancelled = true;
                        result.IsComplete = false;
                        return result;
                    }
                    item = _cleanup.Cleanup(
                        serials[index],
                        operationId,
                        initiatingSid);
                    if (LegacyManagedStateMigrationResult.IsAccepted(item.Status))
                    {
                        break;
                    }
                    bool retryable = item.Status ==
                            LmServiceProvisioningStatus.CleanupPending ||
                        item.Status == LmServiceProvisioningStatus.MarkedForDelete;
                    if (!retryable || attempt == MaximumAttempts)
                    {
                        break;
                    }
                    _delay();
                }
                result.Items.Add(item);
            }
            result.IsComplete = true;
            for (int index = 0; index < result.Items.Count; index++)
            {
                result.IsComplete &= LegacyManagedStateMigrationResult.IsAccepted(
                    result.Items[index].Status);
            }
            return result;
        }
    }

    internal sealed class WindowsLegacyManagedStateCleanup :
        ILegacyManagedStateCleanup
    {
        private readonly PathSafety _pathSafety;

        internal WindowsLegacyManagedStateCleanup()
        {
            _pathSafety = new PathSafety();
        }

        public IList<string> ReadOwnedSerials()
        {
            ManagedServiceManifestStore controllers =
                ManagedServiceManifestStore.CreateMachineStore(
                    _pathSafety,
                    null);
            LocalModuleManifestStore modules =
                LocalModuleManifestStore.CreateMachineStore(
                    _pathSafety,
                    null);
            SortedSet<string> serials = new SortedSet<string>(StringComparer.Ordinal);
            IList<string> controllerSerials = controllers.ReadManagedSerials();
            for (int index = 0; index < controllerSerials.Count; index++)
            {
                serials.Add(controllerSerials[index]);
            }
            IList<string> stackSerials = modules.ReadStackSerials();
            for (int index = 0; index < stackSerials.Count; index++)
            {
                serials.Add(stackSerials[index]);
            }
            return new List<string>(serials);
        }

        public LmServiceProvisioningItemResult Cleanup(
            string kktSerial,
            string operationId,
            string initiatingSid)
        {
            ManagedServiceManifestStore controllers =
                ManagedServiceManifestStore.CreateMachineStore(
                    _pathSafety,
                    initiatingSid);
            LocalModuleManifestStore modules =
                LocalModuleManifestStore.CreateMachineStore(
                    _pathSafety,
                    initiatingSid);
            ManagedServiceManifest controllerManifest;
            bool hasController = controllers.TryRead(
                kktSerial,
                out controllerManifest);
            string stackFingerprint;
            bool hasStack = modules.TryComputeStackFingerprint(
                kktSerial,
                out stackFingerprint);
            if (!hasController && !hasStack)
            {
                return new LmServiceProvisioningItemResult
                {
                    KktSerial = kktSerial,
                    Status = LmServiceProvisioningStatus.Succeeded,
                    Message = "Старые управляемые компоненты уже отсутствуют."
                };
            }

            LmRemovalConfirmation confirmation = new LmRemovalConfirmation
            {
                KktSerial = kktSerial,
                RetainedEsmWarningAccepted = true
            };
            if (hasController)
            {
                LmServiceInventoryItem projection =
                    controllers.ReadProjection(kktSerial);
                confirmation.GrpcPort = projection.Ports.GrpcPort;
                confirmation.RestPort = projection.Ports.RestPort;
                confirmation.ManifestFingerprint = projection.ManifestFingerprint;
            }
            if (hasStack)
            {
                confirmation.ManagedStateFingerprint = new LmManifestFingerprint
                {
                    Sha256 = stackFingerprint
                };
            }
            LmServiceProvisioningBatchRequest request =
                new LmServiceProvisioningBatchRequest
                {
                    SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                    Operation = LmServiceOperation.RemoveManaged,
                    OperationId = operationId,
                    InitiatingSid = initiatingSid,
                    RemovalConfirmation = confirmation
                };
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
            if (hasStack)
            {
                return new ManagedLocalModuleRemovalWorkflow(
                    WindowsManagedLocalModuleRemovalPlatform.Create(request))
                    .RemoveKkt(kktSerial, operationId);
            }
            WindowsLmProvisioningPlatform platform =
                WindowsLmProvisioningPlatform.Create(
                    initiatingSid,
                    operationId);
            return new LmServiceProvisioner(platform).RemoveManaged(request);
        }

        internal static LegacyManagedStateMigrationWorkflow CreateWorkflow()
        {
            return new LegacyManagedStateMigrationWorkflow(
                new WindowsLegacyManagedStateCleanup(),
                delegate { Thread.Sleep(500); });
        }
    }
}
