using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiObservedState
    {
        internal bool ProductPresent { get; set; }
        internal bool ProductMatches { get; set; }
        internal bool ConfigurationMatches { get; set; }
        internal bool ServicesPresent { get; set; }
        internal bool ServicesMatch { get; set; }
        internal bool FirewallPresent { get; set; }
        internal bool FirewallMatches { get; set; }
        internal bool Running { get; set; }
        internal bool Ready { get; set; }
        internal bool AutomaticStart { get; set; }
        internal string ConflictMessage { get; set; }

        internal bool IsExactReady
        {
            get
            {
                return ProductPresent && ProductMatches &&
                    ConfigurationMatches && ServicesPresent && ServicesMatch &&
                    FirewallPresent && FirewallMatches && AutomaticStart &&
                    Running && Ready &&
                    string.IsNullOrEmpty(ConflictMessage);
            }
        }
    }

    internal interface ILocalModuleMsiProvisioningPlatform
    {
        LocalModuleMsiObservedState Observe(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest);
        LocalModuleMsiManifest PrepareInstall(
            LocalModuleMsiProvisioningItemRequest request,
            string ownershipNonce);
        void Install(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest);
        LocalModuleMsiManifest AdoptPreExistingBase(
            LocalModuleMsiProvisioningItemRequest request,
            string ownershipNonce);
        LocalModuleFirewallRule EnsureFirewall(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest);
        LocalModuleStartModeAdjustment EnsureAutomaticStart(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest);
        void RestoreStartMode(LocalModuleMsiManifest manifest);
        void StartAndVerify(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest);
        void StopAndVerify(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest);
        void RemoveFirewall(LocalModuleMsiManifest manifest);
        void Uninstall(LocalModuleMsiManifest manifest);
        void WaitForEpmdExit();
    }

    internal interface ILocalModuleMsiManifestRepository
    {
        LocalModuleMsiManifest Read(string inn);
        IList<LocalModuleMsiManifest> ReadAll();
        void Write(LocalModuleMsiManifest manifest);
        void Delete(string inn, string expectedManifestSha256);
    }

    [DataContract]
    internal enum LocalModuleMsiLifecycleStage
    {
        [EnumMember] Observing = 1,
        [EnumMember] ManifestPersisting = 2,
        [EnumMember] Installing = 3,
        [EnumMember] FirewallEnsuring = 4,
        [EnumMember] ServicesStarting = 5,
        [EnumMember] Ready = 6,
        [EnumMember] ServicesStopping = 7,
        [EnumMember] FirewallRemoving = 8,
        [EnumMember] ProductUninstalling = 9,
        [EnumMember] ManifestDeleting = 10,
        [EnumMember] EpmdWaiting = 11,
        [EnumMember] CloneCompensation = 12,
        [EnumMember] CleanupPending = 13,
        [EnumMember] StartModeEnsuring = 14,
        [EnumMember] StartModeRestoring = 15
    }

    [DataContract]
    internal sealed class LocalModuleMsiLifecycleJournal
    {
        internal const int CurrentSchemaVersion = 1;
        internal const string ExpectedOwnershipMarker =
            "KRS.MultiKKT.LocalModuleMsi.Lifecycle.v1";

        [DataMember(Order = 1)] internal int SchemaVersion { get; set; }
        [DataMember(Order = 2)] internal string OwnershipMarker { get; set; }
        [DataMember(Order = 3)] internal string OperationId { get; set; }
        [DataMember(Order = 4)] internal string Inn { get; set; }
        [DataMember(Order = 5)] internal int CloneOrdinal { get; set; }
        [DataMember(Order = 6)] internal string OwnershipNonce { get; set; }
        [DataMember(Order = 7)] internal LocalModuleMsiLifecycleStage Stage { get; set; }
        [DataMember(Order = 8)] internal string LastErrorClass { get; set; }
        [DataMember(Order = 9)] internal string UpdatedUtc { get; set; }
        [DataMember(Order = 10)] internal bool RollbackOwnedChanges { get; set; }

        internal static LocalModuleMsiLifecycleJournal Create(
            string operationId,
            LocalModuleMsiProvisioningItemRequest request,
            string ownershipNonce,
            LocalModuleMsiLifecycleStage stage,
            string lastErrorClass,
            bool rollbackOwnedChanges)
        {
            LocalModuleMsiProvisioningContext.ValidateRequest(request);
            if (!ProvisionerCommandLine.IsGuidN(operationId) ||
                !LocalModuleManagedIdentity.IsLowerHex(ownershipNonce, 32))
                throw new ArgumentException(
                    "Local-module lifecycle journal identity is invalid.");
            return new LocalModuleMsiLifecycleJournal
            {
                SchemaVersion = CurrentSchemaVersion,
                OwnershipMarker = ExpectedOwnershipMarker,
                OperationId = operationId.ToLowerInvariant(),
                Inn = request.Inn,
                CloneOrdinal = request.CloneOrdinal,
                OwnershipNonce = ownershipNonce,
                Stage = stage,
                LastErrorClass = lastErrorClass ?? string.Empty,
                RollbackOwnedChanges = rollbackOwnedChanges,
                UpdatedUtc = DateTime.UtcNow.ToString(
                    "o",
                    CultureInfo.InvariantCulture)
            };
        }

        internal static void Validate(LocalModuleMsiLifecycleJournal journal)
        {
            if (journal == null ||
                journal.SchemaVersion != CurrentSchemaVersion ||
                !string.Equals(journal.OwnershipMarker,
                    ExpectedOwnershipMarker, StringComparison.Ordinal) ||
                !ProvisionerCommandLine.IsGuidN(journal.OperationId) ||
                !LocalModuleMsiIdentity.IsInn(journal.Inn) ||
                journal.CloneOrdinal < 0 ||
                journal.CloneOrdinal > LocalModuleMsiIdentity.MaximumCloneOrdinal ||
                !LocalModuleManagedIdentity.IsLowerHex(
                    journal.OwnershipNonce, 32) ||
                journal.Stage < LocalModuleMsiLifecycleStage.Observing ||
                journal.Stage > LocalModuleMsiLifecycleStage.StartModeRestoring ||
                journal.LastErrorClass == null ||
                string.IsNullOrWhiteSpace(journal.UpdatedUtc))
                throw new InvalidDataException(
                    "Local-module MSI lifecycle journal is invalid.");
        }
    }

    internal interface ILocalModuleMsiLifecycleJournalStore
    {
        LocalModuleMsiLifecycleJournal Read(string inn);
        void Write(LocalModuleMsiLifecycleJournal journal);
        void Delete(string inn, string operationId, string ownershipNonce);
    }

    internal sealed class LocalModuleMsiProvisioningContext
    {
        private readonly Func<string> _newOwnershipNonce;

        internal LocalModuleMsiProvisioningContext(
            string operationId,
            ILocalModuleMsiProvisioningPlatform platform,
            ILocalModuleMsiManifestRepository manifests,
            ILocalModuleMsiLifecycleJournalStore journals,
            Func<string> newOwnershipNonce)
        {
            if (!ProvisionerCommandLine.IsGuidN(operationId))
                throw new ArgumentException(
                    "Operation id must be a GUID without separators.",
                    "operationId");
            if (platform == null) throw new ArgumentNullException("platform");
            if (manifests == null) throw new ArgumentNullException("manifests");
            if (journals == null) throw new ArgumentNullException("journals");
            if (newOwnershipNonce == null)
                throw new ArgumentNullException("newOwnershipNonce");
            OperationId = operationId.ToLowerInvariant();
            Platform = platform;
            Manifests = manifests;
            Journals = journals;
            _newOwnershipNonce = newOwnershipNonce;
        }

        internal string OperationId { get; private set; }
        internal ILocalModuleMsiProvisioningPlatform Platform { get; private set; }
        internal ILocalModuleMsiManifestRepository Manifests { get; private set; }
        internal ILocalModuleMsiLifecycleJournalStore Journals { get; private set; }

        internal static LocalModuleMsiProvisioningContext CreateWindows(
            string operationId,
            VerifiedLocalModulePackage source,
            string machineRoot,
            string initiatingSid)
        {
            PathSafety pathSafety = new PathSafety();
            LocalModuleMsiManifestStore store =
                new LocalModuleMsiManifestStore(
                    machineRoot,
                    pathSafety,
                    initiatingSid);
            return new LocalModuleMsiProvisioningContext(
                operationId,
                WindowsLocalModuleMsiProvisioningPlatform.Create(
                    source,
                    machineRoot),
                store,
                store,
                delegate { return Guid.NewGuid().ToString("N"); });
        }

        internal static LocalModuleMsiProvisioningContext
            CreateWindowsForInstalledProducts(
            string operationId,
            string machineRoot,
            string initiatingSid)
        {
            PathSafety pathSafety = new PathSafety();
            LocalModuleMsiManifestStore store =
                new LocalModuleMsiManifestStore(
                    machineRoot,
                    pathSafety,
                    initiatingSid);
            return new LocalModuleMsiProvisioningContext(
                operationId,
                WindowsLocalModuleMsiProvisioningPlatform
                    .CreateForInstalledProducts(machineRoot),
                store,
                store,
                delegate { return Guid.NewGuid().ToString("N"); });
        }

        internal string NewOwnershipNonce()
        {
            string value = _newOwnershipNonce();
            if (!LocalModuleManagedIdentity.IsLowerHex(value, 32))
                throw new InvalidOperationException(
                    "Generated local-module ownership identity is invalid.");
            return value;
        }

        internal void WriteStage(
            LocalModuleMsiProvisioningItemRequest request,
            string ownershipNonce,
            LocalModuleMsiLifecycleStage stage,
            Exception error,
            bool rollbackOwnedChanges)
        {
            Journals.Write(LocalModuleMsiLifecycleJournal.Create(
                OperationId,
                request,
                ownershipNonce,
                stage,
                error == null ? string.Empty : error.GetType().Name,
                rollbackOwnedChanges));
        }

        internal IDisposable AcquireMutationLock()
        {
            Mutex mutex = new Mutex(
                false,
                "Global\\KRS.MultiKKT.LocalModuleMsi.v1");
            bool acquired = false;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(30000);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
                if (!acquired)
                    throw new TimeoutException(
                        "Another process is changing local-module MSI products.");
                return new MutationLease(mutex);
            }
            catch
            {
                if (!acquired) mutex.Dispose();
                throw;
            }
        }

        internal static void ValidateRequest(
            LocalModuleMsiProvisioningItemRequest request)
        {
            if (request == null ||
                !LocalModuleMsiIdentity.IsInn(request.Inn) ||
                request.CloneOrdinal < 0 ||
                request.CloneOrdinal > LocalModuleMsiIdentity.MaximumCloneOrdinal ||
                request.ApiPort < 1024 || request.ApiPort > 65535 ||
                request.DatabasePort < 1024 || request.DatabasePort > 65535 ||
                request.ApiPort == request.DatabasePort ||
                !LocalModuleInstallRootPolicy.IsCanonicalVolumeRoot(
                    request.InstallVolumeRoot) ||
                (!string.IsNullOrEmpty(request.ExpectedManifestSha256) &&
                 !LocalModuleManagedIdentity.IsHex(
                     request.ExpectedManifestSha256, 64)))
                throw new ArgumentException(
                    "Local-module MSI request is invalid.",
                    "request");
            if (request.CloneOrdinal > 0 &&
                (request.ApiPort != LocalModuleMsiIdentity.ApiPortForClone(
                    request.CloneOrdinal) ||
                 request.DatabasePort !=
                    LocalModuleMsiIdentity.DatabasePortForClone(
                        request.CloneOrdinal)))
                throw new ArgumentException(
                    "Local-module clone ports do not match its ordinal.",
                    "request");
            LocalModuleFirewallRule.Create(
                new string('a', 32),
                Path.Combine(request.InstallVolumeRoot,
                    "Program Files", "Regime", "erts-13.0.4", "bin", "erl.exe"),
                request.ApiPort,
                request.RemoteAddress);
        }

        private sealed class MutationLease : IDisposable
        {
            private Mutex _mutex;

            internal MutationLease(Mutex mutex)
            {
                _mutex = mutex;
            }

            public void Dispose()
            {
                if (_mutex == null) return;
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
            }
        }
    }
}
