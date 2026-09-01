using System;
using System.Collections.Generic;
using System.IO;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LmServiceOwnershipVerifier
    {
        private readonly IWindowsServiceApi _serviceApi;
        private readonly ManagedServiceManifestStore _manifestStore;
        private readonly ProvisioningOperationJournalStore _journalStore;
        private readonly OfficialLmProfileAdapter _profileAdapter;
        private readonly LmGatewaySupervisorService _supervisor;

        internal LmServiceOwnershipVerifier(
            IWindowsServiceApi serviceApi,
            ManagedServiceManifestStore manifestStore,
            ProvisioningOperationJournalStore journalStore,
            OfficialLmProfileAdapter profileAdapter,
            LmGatewaySupervisorService supervisor)
        {
            if (serviceApi == null) throw new ArgumentNullException("serviceApi");
            if (manifestStore == null) throw new ArgumentNullException("manifestStore");
            if (journalStore == null) throw new ArgumentNullException("journalStore");
            if (profileAdapter == null) throw new ArgumentNullException("profileAdapter");
            if (supervisor == null) throw new ArgumentNullException("supervisor");
            _serviceApi = serviceApi;
            _manifestStore = manifestStore;
            _journalStore = journalStore;
            _profileAdapter = profileAdapter;
            _supervisor = supervisor;
        }

        internal LmProvisioningObservedState Inspect(
            LmServiceProvisioningItemRequest item,
            LmVerifiedController controller)
        {
            string serviceName = LmServiceIdentity.CreateName(item.KktSerial);
            WindowsServiceRecord service = _serviceApi.Query(serviceName);
            ManagedServiceManifest manifest;
            bool hasManifest = _manifestStore.TryRead(item.KktSerial, out manifest);
            IList<ProvisioningOperationJournal> journals =
                _journalStore.ReadForKkt(item.KktSerial);

            if (service == null)
            {
                if (!hasManifest && journals.Count == 0)
                {
                    return LmProvisioningObservedState.Absent;
                }
                if (hasManifest &&
                    manifest.LocalLifecycleState ==
                        ManagedServiceLifecycleState.VersionVerificationPending)
                {
                    return LmProvisioningObservedState.VersionPending;
                }
                return LmProvisioningObservedState.OwnedMismatch;
            }

            if (!HasExactOwnershipFacts(item.KktSerial, service, hasManifest, journals.Count > 0))
            {
                return LmProvisioningObservedState.Foreign;
            }
            if (service.State != WindowsServiceState.Running &&
                service.State != WindowsServiceState.Stopped)
            {
                return LmProvisioningObservedState.RequiresAttention;
            }
            if (hasManifest &&
                manifest.LocalLifecycleState == ManagedServiceLifecycleState.VersionVerificationPending)
            {
                return LmProvisioningObservedState.VersionPending;
            }
            if (!hasManifest || !ManifestMatchesController(manifest, controller))
            {
                return LmProvisioningObservedState.OwnedMismatch;
            }
            if (!ManifestMatchesItem(manifest, item) ||
                !ProfileMatchesItem(manifest, item))
            {
                return LmProvisioningObservedState.OwnedMismatch;
            }
            return service.State == WindowsServiceState.Running
                ? LmProvisioningObservedState.MatchingReady
                : LmProvisioningObservedState.MatchingStopped;
        }

        private bool HasExactOwnershipFacts(
            string kktSerial,
            WindowsServiceRecord service,
            bool hasManifest,
            bool hasJournal)
        {
            if (!hasManifest && !hasJournal)
            {
                return false;
            }
            if (!string.Equals(
                service.Description,
                LmGatewaySupervisorService.DescriptionPrefix + kktSerial,
                StringComparison.Ordinal))
            {
                return false;
            }
            return _supervisor.IsManagedDefinition(kktSerial, service);
        }

        private static bool ManifestMatchesController(
            ManagedServiceManifest manifest,
            LmVerifiedController controller)
        {
            return string.Equals(
                    manifest.ControllerVersion,
                    controller.Version,
                    StringComparison.Ordinal) &&
                string.Equals(
                    manifest.ControllerBinarySha256,
                    controller.BinarySha256,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    manifest.SupervisorSha256,
                    controller.SupervisorSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    manifest.ServiceSid,
                    RestrictedServiceSid.Derive(manifest.ServiceName),
                    StringComparison.Ordinal);
        }

        private static bool ManifestMatchesItem(
            ManagedServiceManifest manifest,
            LmServiceProvisioningItemRequest item)
        {
            return manifest.GrpcPort == item.GrpcPort &&
                manifest.RestPort == item.RestPort &&
                manifest.TargetPort == item.TargetPort &&
                string.Equals(
                    manifest.TargetAddress,
                    item.TargetAddress,
                    StringComparison.Ordinal);
        }

        private bool ProfileMatchesItem(
            ManagedServiceManifest manifest,
            LmServiceProvisioningItemRequest item)
        {
            try
            {
                LmProfileConfiguration actual = _profileAdapter.ReadConfiguration(
                    item.KktSerial,
                    manifest.ServiceSid);
                return actual.GrpcPort == item.GrpcPort &&
                    actual.RestPort == item.RestPort &&
                    actual.TargetPort == item.TargetPort &&
                    string.Equals(
                        actual.TargetAddress,
                        item.TargetAddress,
                        StringComparison.Ordinal);
            }
            catch (IOException)
            {
                return false;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }
}
