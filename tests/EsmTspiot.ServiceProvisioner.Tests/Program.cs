using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using EsmTspiot.ServiceProvisioner;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner.Tests
{
    internal static class Program
    {
        private static int _failures;
        private static int _testCount;

        private static int Main()
        {
            Run("Provisioning protocol accepts bounded ensure batch", ProvisioningProtocolAcceptsBoundedEnsureBatch);
            Run("Provisioning protocol rejects oversized or duplicate batch", ProvisioningProtocolRejectsOversizedOrDuplicateBatch);
            Run("Provisioning protocol rejects unknown schema or operation", ProvisioningProtocolRejectsUnknownSchemaOrOperation);
            Run("Provisioning protocol rejects unsafe item", ProvisioningProtocolRejectsUnsafeItem);
            Run("Provisioning pipe authenticates exact protected peer images", ProvisioningPipeAuthenticatesExactProtectedPeerImages);
            Run("Provisioning pipe accepts protected main images independently of filename", ProvisioningPipeAcceptsProtectedMainImageIndependentlyOfFilename);
            Run("Provisioning protocol rejects plan hash mismatch", ProvisioningProtocolRejectsPlanHashMismatch);
            Run("Remove-all protocol accepts only a confirmed managed batch", RemoveAllProtocolAcceptsOnlyConfirmedManagedBatch);
            Run("Managed stack removal accepts its displayed fingerprint without controller ports", ManagedStackRemovalAcceptsDisplayedFingerprintWithoutControllerPorts);
            Run("Managed cleanup accepts only its displayed managed-state fingerprint", ManagedCleanupAcceptsOnlyDisplayedManagedStateFingerprint);
            Run("Provisioning protocol exposes no credentials paths or commands", ProvisioningProtocolExposesNoCredentialsPathsOrCommands);
            Run("Installer operation accepts only one verified setup selection", InstallerOperationAcceptsOnlyOneVerifiedSetupSelection);
            Run("Installer operation rejects stale or substituted source file", InstallerOperationRejectsStaleOrSubstitutedSourceFile);
            Run("Local module verifier accepts only the exact locked MSI", LocalModuleVerifierAcceptsOnlyExactLockedMsi);
            Run("Local module verifier accepts safe pinned signer identity", LocalModuleVerifierAcceptsSafePinnedSignerIdentity);
            Run("WinTrust matches safe local module identity only with pinned thumbprint", WinTrustMatchesSafeLocalModuleIdentityOnlyWithPinnedThumbprint);
            Run("Local module verifier rejects package identity or signer mismatch", LocalModuleVerifierRejectsPackageIdentityOrSignerMismatch);
            Run("Managed local module protocol accepts consistent shared INN rows", ManagedLocalModuleProtocolAcceptsConsistentSharedInnRows);
            Run("Managed provisioning session accepts only known monotonic messages", ManagedProvisioningSessionAcceptsOnlyKnownMonotonicMessages);
            Run("Managed session server interleaves caller and helper per KKT", ManagedSessionServerInterleavesCallerAndHelperPerKkt);
            Run("Local module configs isolate every mutable path", LocalModuleConfigsIsolateEveryMutablePath);
            Run("Local module start plans share only read-only runtime", LocalModuleStartPlansShareOnlyReadOnlyRuntime);
            Run("Local module child environment supplies Windows runtime and isolated temp", LocalModuleChildEnvironmentSuppliesWindowsRuntimeAndIsolatedTemp);
            Run("Local module config rejects ambiguous template", LocalModuleConfigRejectsAmbiguousTemplate);
            Run("Local module configs contain no customer credential", LocalModuleConfigsContainNoCredential);
            Run("Local module runtime excludes wrappers and fixes extraction", LocalModuleRuntimeExcludesWrappersAndFixesExtraction);
            Run("Administrative MSI extraction uses native command syntax", AdministrativeMsiExtractionUsesNativeCommandSyntax);
            Run("Local module manifests enforce three ownership levels", LocalModuleManifestsEnforceThreeOwnershipLevels);
            Run("Local module runtime deletion requires zero references", LocalModuleRuntimeDeletionRequiresZeroReferences);
            Run("Existing local module runtime verifies without MSI extraction", ExistingLocalModuleRuntimeVerifiesWithoutMsiExtraction);
            Run("Local module runtime recovers every mutation boundary", LocalModuleRuntimeRecoversEveryMutationBoundary);
            Run("Local module runtime deletion recovers every mutation boundary", LocalModuleRuntimeDeletionRecoversEveryMutationBoundary);
            Run("Official controller locator enforces protected allowed root", OfficialControllerLocatorEnforcesProtectedAllowedRoot);
            Run("Official controller locator enforces full product trust", OfficialControllerLocatorEnforcesFullProductTrust);
            Run("Official installer verifier locks verifies and stages atomically", OfficialInstallerVerifierLocksVerifiesAndStagesAtomically);
            Run("Official installer verifier rejects filename signer version or hash mismatch", OfficialInstallerVerifierRejectsFilenameSignerVersionOrHashMismatch);
            Run("Supported installer uses the fixed NSIS silent switch", SupportedInstallerUsesFixedNsisSilentSwitch);
            Run("WinTrust marshals the action GUID as one native pointer", WinTrustMarshalsActionGuidAsOneNativePointer);
            Run("Manifest path is derived only from KKT serial", ManifestPathIsDerivedOnlyFromKktSerial);
            Run("Manifest and profile stores reject reparse points", ManifestAndProfileStoresRejectReparsePoints);
            Run("Manifest is atomic credential free and projects cleanup state", ManifestIsAtomicCredentialFreeAndProjectsCleanupState);
            Run("Manifest ownership mismatch blocks mutation", ManifestOwnershipMismatchBlocksMutation);
            Run("SCM adapter derives service name internally", ScmAdapterDerivesServiceNameInternally);
            Run("SCM adapter uses exact verified image path", ScmAdapterUsesExactVerifiedImagePath);
            Run("SCM adapter enforces restrictive service DACL", ScmAdapterEnforcesRestrictiveServiceDacl);
            Run("SCM adapter never force kills process", ScmAdapterNeverForceKillsProcess);
            Run("SCM handles are disposed on every failure", ScmHandlesAreDisposedOnEveryFailure);
            Run("SCM configures restricted service SID", ScmConfiguresRestrictedServiceSid);
            Run("SCM image path targets only protected supervisor mode", ScmImagePathTargetsOnlyProtectedSupervisorMode);
            Run("Supervisor replaces only child ProgramData", SupervisorReplacesOnlyChildProgramData);
            Run("Supervisor rejects caller supplied environment and arguments", SupervisorRejectsCallerSuppliedEnvironmentAndArguments);
            Run("Supervisor stops child gracefully without process kill", SupervisorStopsChildGracefullyWithoutProcessKill);
            Run("Provisioner accepts only owned local module service mode", ProvisionerAcceptsOnlyOwnedLocalModuleServiceMode);
            Run("Local module SCM creates exact restricted dependency pair", LocalModuleScmCreatesExactRestrictedDependencyPair);
            Run("Local module child plan is rebuilt only from manifest", LocalModuleChildPlanIsRebuiltOnlyFromManifest);
            Run("Local module ownership distinguishes shared Erlang children", LocalModuleOwnershipDistinguishesSharedErlangChildren);
            Run("EPMD controller uses explicit port and blocks live-node kill", EpmdControllerUsesExplicitPortAndBlocksLiveNodeKill);
            Run("Local module lifecycle orders database API and EPMD", LocalModuleLifecycleOrdersDatabaseApiAndEpmd);
            Run("Restricted service SID supports local module pair", RestrictedServiceSidSupportsLocalModulePair);
            Run("Protected ACL accepts exactly two local module writers", ProtectedAclAcceptsExactlyTwoLocalModuleWriters);
            Run("Local module inventory resolves one instance per INN", LocalModuleInventoryResolvesOneInstancePerInn);
            Run("Managed local module profile writes protected six-file contract", ManagedLocalModuleProfileWritesProtectedSixFileContract);
            Run("Managed local module lifecycle journal resumes pre-profile identity", ManagedLocalModuleLifecycleJournalResumesPreProfileIdentity);
            Run("Local module readiness requires owned descendant listener", LocalModuleReadinessRequiresOwnedDescendantListener);
            Run("Windows managed platform persists profile and exact service pair", WindowsManagedPlatformPersistsProfileAndExactServicePair);
            Run("Complete stack canary failure stops remaining groups", CompleteStackCanaryFailureStopsRemainingGroups);
            Run("Complete stack provisions every KKT of shared INN", CompleteStackProvisionsEveryKktOfSharedInn);
            Run("Complete stack skips a later unregistered KKT", CompleteStackSkipsLaterUnregisteredKkt);
            Run("Managed local module same INN ensure is idempotent", ManagedLocalModuleSameInnEnsureIsIdempotent);
            Run("Managed removal retains shared same-INN module", ManagedRemovalRetainsSharedSameInnModule);
            Run("Managed removal cleans complete stack in reverse", ManagedRemovalCleansCompleteStackInReverse);
            Run("Managed removal projects cleanup pending and retries", ManagedRemovalProjectsCleanupPendingAndRetries);
            Run("Managed removal journal survives deleted KKT stack", ManagedRemovalJournalSurvivesDeletedKktStack);
            Run("Windows managed removal deletes owned stack and retry journal", WindowsManagedRemovalDeletesOwnedStackAndRetryJournal);
            Run("Managed update guard never stops unknown version", ManagedUpdateGuardNeverStopsUnknownVersion);
            Run("LM profile adapter changes only supported fields", LmProfileAdapterChangesOnlySupportedFields);
            Run("LM profile adapter rejects ambiguous schema", LmProfileAdapterRejectsAmbiguousSchema);
            Run("LM profile adapter preserves unknown nonsecret fields", LmProfileAdapterPreservesUnknownNonsecretFields);
            Run("LM profile adapter writes atomically", LmProfileAdapterWritesAtomically);
            Run("LM profile adapter never clones official profile", LmProfileAdapterNeverClonesOfficialProfile);
            Run("LM profile adapter detects unsupported controller version", LmProfileAdapterDetectsUnsupportedControllerVersion);
            Run("Ensure creates profile service and listeners in order", EnsureCreatesProfileServiceAndListenersInOrder);
            Run("Ensure is no op for matching ready service", EnsureIsNoOpForMatchingReadyService);
            Run("Ensure starts matching stopped service", EnsureStartsMatchingStoppedService);
            Run("Ensure safely updates owned mismatched service", EnsureSafelyUpdatesOwnedMismatchedService);
            Run("Ensure blocks unknown existing service", EnsureBlocksUnknownExistingService);
            Run("Ensure rechecks and holds exact exclusive endpoints until start", EnsureRechecksAndHoldsExactExclusiveEndpointsUntilStart);
            Run("Ensure rejects listener owned by another process", EnsureRejectsListenerOwnedByAnotherProcess);
            Run("Ensure leaves failed new service stopped for diagnosis", EnsureLeavesFailedNewServiceStoppedForDiagnosis);
            Run("Ensure writes manifest only after confirmed stages", EnsureWritesManifestOnlyAfterConfirmedStages);
            Run("Ensure journal recovers every simulated crash stage", EnsureJournalRecoversEverySimulatedCrashStage);
            Run("Ensure serializes concurrent ensure remove and cleanup", EnsureSerializesConcurrentMutations);
            Run("Ensure batch continues failures and honors cancel boundary", EnsureBatchContinuesFailuresAndHonorsCancelBoundary);
            Run("Ensure batch rejects stale operation result", EnsureBatchRejectsStaleOperationResult);
            Run("Install version marks every managed instance verification pending before launch", InstallVersionMarksEveryManagedInstanceVerificationPendingBeforeLaunch);
            Run("Install version never runs a substituted or unlocked installer", InstallVersionNeverRunsSubstitutedOrUnlockedInstaller);
            Run("Install version leaves services stopped when verification fails", InstallVersionLeavesServicesStoppedWhenVerificationFails);
            Run("Ensure clears version pending only after recreated artifacts are ready", EnsureClearsVersionPendingOnlyAfterRecreatedArtifactsAreReady);
            Run("Remove deletes only fully owned freshly confirmed service", RemoveDeletesOnlyFullyOwnedFreshlyConfirmedService);
            Run("Remove all managed processes every confirmed service in one batch", RemoveAllManagedProcessesEveryConfirmedServiceInOneBatch);
            Run("Remove blocks official base service", RemoveBlocksOfficialBaseService);
            Run("Remove blocks service on marker mismatch", RemoveBlocksServiceOnMarkerMismatch);
            Run("Remove blocks service on image mismatch", RemoveBlocksServiceOnImageMismatch);
            Run("Remove stops before delete and waits", RemoveStopsBeforeDeleteAndWaits);
            Run("Remove deletes profile manifest and app metadata", RemoveDeletesProfileManifestAndAppMetadata);
            Run("Remove reports marked for delete until SCM absence", RemoveReportsMarkedForDeleteUntilScmAbsence);
            Run("Remove does not claim ESM binding was cleared", RemoveDoesNotClaimEsmBindingWasCleared);
            Run("Remove projects cleanup pending across restart and supports retry", RemoveProjectsCleanupPendingAcrossRestartAndSupportsRetry);
            Run("Remove remains possible after vendor binary update", RemoveRemainsPossibleAfterVendorBinaryUpdate);

            if (_failures == 0)
            {
                Console.WriteLine(
                    "All " + _testCount.ToString() +
                    " provisioner tests passed.");
                return 0;
            }

            Console.WriteLine(_failures.ToString() + " provisioner test(s) failed.");
            return 1;
        }

        private static void ProvisioningProtocolAcceptsBoundedEnsureBatch()
        {
            LmServiceProvisioningBatchRequest request = CreateEnsureRequest(3);

            ValidationResult validation = ProvisioningRequestValidator.Validate(request);

            AssertTrue(validation.IsValid, validation.JoinMessages());
            AssertEqual(3, request.Items.Count, "Expected all per-KKT items in one bounded batch.");

            LmServiceProvisioningBatchRequest maximum = CreateEnsureRequest(32);
            AssertTrue(ProvisioningRequestValidator.Validate(maximum).IsValid,
                "A batch of exactly 32 items must be accepted.");

            LmServiceProvisioningBatchRequest roundTripped;
            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(LmServiceProvisioningBatchRequest));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, request);
                AssertTrue(stream.Length < NamedPipeProvisioningChannel.MaximumMessageBytes,
                    "A normal bounded request must remain below the 1 MiB transport limit.");
                stream.Position = 0;
                roundTripped = (LmServiceProvisioningBatchRequest)serializer.ReadObject(stream);
            }
            AssertTrue(ProvisioningRequestValidator.Validate(roundTripped).IsValid,
                "The explicit data-contract schema must survive JSON transport.");
            AssertEqual(request.Items.Count, roundTripped.Items.Count,
                "Transport must retain every requested KKT row.");

            AssertTrue(ProvisioningRequestValidator.Validate(CreateRemovalRequest()).IsValid,
                "A single immutable removal confirmation must be accepted.");
            AssertTrue(ProvisioningRequestValidator.Validate(CreateCleanupRequest()).IsValid,
                "A single displayed CleanupPending projection must be accepted.");
        }

        private static void ProvisioningProtocolRejectsOversizedOrDuplicateBatch()
        {
            LmServiceProvisioningBatchRequest oversized = CreateEnsureRequest(33);
            ValidationResult oversizedValidation = ProvisioningRequestValidator.Validate(oversized);
            AssertFalse(oversizedValidation.IsValid, "A batch larger than 32 items must be rejected.");
            AssertContains(oversizedValidation.JoinMessages(), "32");

            LmServiceProvisioningBatchRequest duplicateKkt = CreateEnsureRequest(2);
            duplicateKkt.Items[1].KktSerial = duplicateKkt.Items[0].KktSerial;
            duplicateKkt.PlanHash = CanonicalLmPlanHasher.Compute(duplicateKkt);
            AssertFalse(ProvisioningRequestValidator.Validate(duplicateKkt).IsValid,
                "Duplicate KKT identities must be rejected.");

            LmServiceProvisioningBatchRequest duplicateCrossColumnPort = CreateEnsureRequest(2);
            duplicateCrossColumnPort.Items[1].RestPort = duplicateCrossColumnPort.Items[0].GrpcPort;
            duplicateCrossColumnPort.PlanHash = CanonicalLmPlanHasher.Compute(duplicateCrossColumnPort);
            AssertFalse(ProvisioningRequestValidator.Validate(duplicateCrossColumnPort).IsValid,
                "A local port duplicated across columns must be rejected.");
        }

        private static void ProvisioningProtocolRejectsUnknownSchemaOrOperation()
        {
            LmServiceProvisioningBatchRequest unknownSchema = CreateEnsureRequest(1);
            unknownSchema.SchemaVersion = 2;
            unknownSchema.PlanHash = CanonicalLmPlanHasher.Compute(unknownSchema);
            AssertFalse(ProvisioningRequestValidator.Validate(unknownSchema).IsValid,
                "Unknown schema versions must fail closed.");

            LmServiceProvisioningBatchRequest unknownOperation = CreateEnsureRequest(1);
            unknownOperation.Operation = (LmServiceOperation)999;
            unknownOperation.PlanHash = CanonicalLmPlanHasher.Compute(unknownOperation);
            AssertFalse(ProvisioningRequestValidator.Validate(unknownOperation).IsValid,
                "Unknown operations must fail closed.");

            ProvisionerCommandLine ignored;
            AssertFalse(ProvisionerCommandLine.TryParse(
                    new[] { "--pipe", Guid.NewGuid().ToString("N"), "--operation", "not-a-guid" },
                    out ignored),
                "Command line must accept only two exact 32-hex identifiers.");
            AssertFalse(ProvisionerCommandLine.TryParse(
                    new[] { "--pipe", Guid.NewGuid().ToString("N"), "--operation", Guid.NewGuid().ToString("N"), "extra" },
                    out ignored),
                "Additional command-line data must be rejected.");
        }

        private static void ProvisioningProtocolRejectsUnsafeItem()
        {
            LmServiceProvisioningBatchRequest unsafeSerial = CreateEnsureRequest(1);
            unsafeSerial.Items[0].KktSerial = "0010570000000A";
            unsafeSerial.PlanHash = CanonicalLmPlanHasher.Compute(unsafeSerial);
            AssertFalse(ProvisioningRequestValidator.Validate(unsafeSerial).IsValid,
                "Unsafe KKT serial must be rejected.");

            LmServiceProvisioningBatchRequest unsafeTarget = CreateEnsureRequest(1);
            unsafeTarget.Items[0].TargetAddress = "https://lm.example/path";
            unsafeTarget.PlanHash = CanonicalLmPlanHasher.Compute(unsafeTarget);
            AssertFalse(ProvisioningRequestValidator.Validate(unsafeTarget).IsValid,
                "A URL must not be accepted where a plain target address is required.");

            LmServiceProvisioningBatchRequest unsafePorts = CreateEnsureRequest(1);
            unsafePorts.Items[0].RestPort = unsafePorts.Items[0].GrpcPort;
            unsafePorts.PlanHash = CanonicalLmPlanHasher.Compute(unsafePorts);
            AssertFalse(ProvisioningRequestValidator.Validate(unsafePorts).IsValid,
                "The two local listeners must not share one port.");
        }

        private static void ProvisioningPipeAuthenticatesExactProtectedPeerImages()
        {
            ProvisioningPeerEvidence server = CreateValidServerEvidence();
            AssertTrue(ProvisioningPipePeerAuthenticator.ValidateServer(server).IsValid,
                "Expected exact protected main process to authenticate.");

            ProvisioningPeerEvidence wrongServerImage = CloneEvidence(server);
            wrongServerImage.ActualImagePath = @"C:\Temp\MultiKKT.exe";
            AssertFalse(ProvisioningPipePeerAuthenticator.ValidateServer(wrongServerImage).IsValid,
                "Same-SID server with another image must fail.");

            ProvisioningPeerEvidence writableServer = CloneEvidence(server);
            writableServer.IsImagePathProtected = false;
            AssertFalse(ProvisioningPipePeerAuthenticator.ValidateServer(writableServer).IsValid,
                "A peer image under an unprotected path must fail.");

            ProvisioningPeerEvidence unlimitedServer = CloneEvidence(server);
            unlimitedServer.MaxServerInstances = -1;
            AssertFalse(ProvisioningPipePeerAuthenticator.ValidateServer(unlimitedServer).IsValid,
                "The UI pipe must have exactly one server instance.");

            ProvisioningPeerEvidence secondServer = CloneEvidence(server);
            secondServer.IsSecondServerAttempt = true;
            AssertFalse(ProvisioningPipePeerAuthenticator.ValidateServer(secondServer).IsValid,
                "A repeated server for the same operation must fail.");

            ProvisioningPeerEvidence client = CloneEvidence(server);
            client.ExpectedProcessId = 4200;
            client.ActualProcessId = 4200;
            client.IsHighIntegrity = true;
            AssertTrue(ProvisioningPipePeerAuthenticator.ValidateClient(client).IsValid,
                "Expected exact elevated helper process to authenticate.");

            ProvisioningPeerEvidence wrongClientPid = CloneEvidence(client);
            wrongClientPid.ActualProcessId = 4201;
            AssertFalse(ProvisioningPipePeerAuthenticator.ValidateClient(wrongClientPid).IsValid,
                "A same-user process winning the pipe race must fail by PID.");

            ProvisioningPeerEvidence wrongSid = CloneEvidence(client);
            wrongSid.ActualSid = "S-1-5-21-111-222-333-1002";
            AssertFalse(ProvisioningPipePeerAuthenticator.ValidateClient(wrongSid).IsValid,
                "Over-the-shoulder elevation under another account must fail.");
        }

        private static void ProvisioningPipeAcceptsProtectedMainImageIndependentlyOfFilename()
        {
            MethodInfo method = typeof(NativePeerEvidenceReader).GetMethod(
                "IsAllowedMainImage",
                BindingFlags.NonPublic | BindingFlags.Static);
            AssertTrue(method != null, "Expected the production image-name check.");
            string appDirectory = @"C:\Program Files\KRS\MultiKKT";
            bool compact = (bool)method.Invoke(null, new object[]
            {
                Path.Combine(appDirectory, "MultiKKT-ESM-TSPioT.exe"),
                appDirectory
            });
            bool renamed = (bool)method.Invoke(null, new object[]
            {
                Path.Combine(appDirectory, "operator-renamed-tool.exe"),
                appDirectory
            });
            bool outside = (bool)method.Invoke(null, new object[]
            {
                @"C:\Temp\MultiKKT-ESM-TSPioT.exe",
                appDirectory
            });
            AssertTrue(compact, "The exact compact release image name must be accepted.");
            AssertTrue(renamed,
                "A renamed image in the exact protected application directory must be accepted.");
            AssertFalse(outside, "An image outside the application directory must remain rejected.");
        }

        private static void ProvisioningProtocolRejectsPlanHashMismatch()
        {
            LmServiceProvisioningBatchRequest request = CreateEnsureRequest(1);
            request.Items[0].TargetPort++;

            ValidationResult validation = ProvisioningRequestValidator.Validate(request);

            AssertFalse(validation.IsValid, "A changed displayed plan must invalidate its confirmation hash.");
            AssertContains(validation.JoinMessages(), "SHA-256");
        }

        private static void RemoveAllProtocolAcceptsOnlyConfirmedManagedBatch()
        {
            LmServiceProvisioningBatchRequest request = CreateRemoveAllRequest(2);
            AssertTrue(ProvisioningRequestValidator.Validate(request).IsValid,
                "A bounded list of exact removal confirmations must be accepted.");

            LmServiceProvisioningBatchRequest duplicate = CreateRemoveAllRequest(2);
            duplicate.RemovalConfirmations[1].KktSerial =
                duplicate.RemovalConfirmations[0].KktSerial;
            duplicate.PlanHash = CanonicalLmPlanHasher.Compute(duplicate);
            AssertFalse(ProvisioningRequestValidator.Validate(duplicate).IsValid,
                "The same managed KKT must not appear twice in a remove-all request.");
        }

        private static void
            ManagedStackRemovalAcceptsDisplayedFingerprintWithoutControllerPorts()
        {
            LmServiceProvisioningBatchRequest request =
                CreateRequest(LmServiceOperation.RemoveManaged);
            request.RemovalConfirmation = new LmRemovalConfirmation
            {
                KktSerial = "00105700000001",
                GrpcPort = 0,
                RestPort = 0,
                ManagedStateFingerprint = new LmManifestFingerprint
                {
                    Sha256 = new string('e', 64)
                },
                RetainedEsmWarningAccepted = true
            };
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            AssertTrue(
                ProvisioningRequestValidator.Validate(request).IsValid,
                "A displayed managed-stack or cleanup-journal fingerprint must authorize removal even when the controller manifest is absent.");

            request.RemovalConfirmation.ManagedStateFingerprint.Sha256 =
                new string('g', 64);
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            AssertFalse(
                ProvisioningRequestValidator.Validate(request).IsValid,
                "A non-hex managed-state fingerprint must be rejected.");
        }

        private static void ManagedCleanupAcceptsOnlyDisplayedManagedStateFingerprint()
        {
            LmServiceProvisioningBatchRequest request =
                CreateRequest(LmServiceOperation.CleanupManaged);
            request.CleanupConfirmation = new LmCleanupConfirmation
            {
                KktSerial = "00105700000001",
                ManagedStateFingerprint = new LmManifestFingerprint
                {
                    Sha256 = new string('f', 64)
                },
                DisplayedState = LmServiceProvisioningStatus.CleanupPending
            };
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);

            AssertTrue(
                ProvisioningRequestValidator.Validate(request).IsValid,
                "A displayed full-stack cleanup fingerprint must authorize cleanup without a legacy controller manifest.");
            AssertEqual(
                new string('f', 64),
                WindowsManagedLocalModuleRemovalPlatform
                    .GetExpectedManagedStateFingerprint(request),
                "The concrete removal platform must recheck the cleanup fingerprint immediately before mutation.");

            request.CleanupConfirmation.ManagedStateFingerprint.Sha256 =
                new string('z', 64);
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            AssertFalse(
                ProvisioningRequestValidator.Validate(request).IsValid,
                "A non-hex managed cleanup fingerprint must be rejected.");
        }

        private static void ProvisioningProtocolExposesNoCredentialsPathsOrCommands()
        {
            Type[] credentialFreeTypes =
            {
                typeof(LmServiceProvisioningItemRequest),
                typeof(LmRemovalConfirmation),
                typeof(LmCleanupConfirmation),
                typeof(LmManifestFingerprint),
                typeof(ManagedLocalModuleProvisioningItemRequest),
                typeof(ManagedProvisioningSessionMessage),
                typeof(LmControllerInstallResult),
                typeof(LmServiceProvisioningItemResult),
                typeof(LmServiceProvisioningBatchResult)
            };

            for (int typeIndex = 0; typeIndex < credentialFreeTypes.Length; typeIndex++)
            {
                PropertyInfo[] properties = credentialFreeTypes[typeIndex].GetProperties();
                for (int propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
                {
                    string name = properties[propertyIndex].Name;
                    AssertFalse(ContainsForbiddenPropertyFragment(name),
                        credentialFreeTypes[typeIndex].Name + " exposes forbidden property " + name + ".");
                }
            }

            Type[] pathFreeSessionTypes =
            {
                typeof(ManagedLocalModuleProvisioningItemRequest),
                typeof(ManagedProvisioningSessionMessage)
            };
            for (int typeIndex = 0; typeIndex < pathFreeSessionTypes.Length; typeIndex++)
            {
                PropertyInfo[] properties = pathFreeSessionTypes[typeIndex].GetProperties();
                for (int propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
                {
                    AssertFalse(properties[propertyIndex].Name.IndexOf(
                            "Path",
                            StringComparison.OrdinalIgnoreCase) >= 0,
                        pathFreeSessionTypes[typeIndex].Name +
                        " must not accept caller-supplied paths.");
                }
            }

            PropertyInfo[] requestProperties = typeof(LmServiceProvisioningBatchRequest).GetProperties();
            for (int index = 0; index < requestProperties.Length; index++)
            {
                string name = requestProperties[index].Name;
                AssertFalse(name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Argument", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Environment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("BinaryPath", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("ProfilePath", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Top-level protocol must not expose arbitrary execution fields.");
            }

            PropertyInfo[] installerProperties = typeof(LmControllerInstallerSelection).GetProperties();
            for (int index = 0; index < installerProperties.Length; index++)
            {
                string name = installerProperties[index].Name;
                AssertFalse(name.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Credential", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Argument", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Environment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("ServiceName", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Installer selection may expose only its scoped source path and displayed file metadata.");
            }

            PropertyInfo[] localModuleInstallerProperties =
                typeof(LocalModuleInstallerSelection).GetProperties();
            for (int index = 0; index < localModuleInstallerProperties.Length; index++)
            {
                string name = localModuleInstallerProperties[index].Name;
                bool allowedSourcePath = string.Equals(name, "SourcePath", StringComparison.Ordinal);
                AssertFalse(!allowedSourcePath && ContainsForbiddenPropertyFragment(name),
                    "The LM installer selection may expose only its scoped SourcePath and immutable package identity.");
                AssertFalse(name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Argument", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Environment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The LM installer selection must not expose execution or secret fields.");
            }
        }

        private static void InstallerOperationAcceptsOnlyOneVerifiedSetupSelection()
        {
            LmServiceProvisioningBatchRequest request = CreateInstallerRequest();

            ValidationResult validation = ProvisioningRequestValidator.Validate(request);

            AssertTrue(validation.IsValid, validation.JoinMessages());

            LmServiceProvisioningBatchRequest mixed = CreateInstallerRequest();
            mixed.Items.Add(CreateEnsureItem(1));
            mixed.PlanHash = CanonicalLmPlanHasher.Compute(mixed);
            AssertFalse(ProvisioningRequestValidator.Validate(mixed).IsValid,
                "Installer operation must contain exactly one installer selection and no service items.");

            LmServiceProvisioningBatchRequest wrongName = CreateInstallerRequest();
            wrongName.InstallerSelection.FileName = "controller-setup.exe";
            wrongName.PlanHash = CanonicalLmPlanHasher.Compute(wrongName);
            AssertFalse(ProvisioningRequestValidator.Validate(wrongName).IsValid,
                "Only the versioned official setup filename shape is accepted.");
        }

        private static void InstallerOperationRejectsStaleOrSubstitutedSourceFile()
        {
            LmControllerInstallerSelection selected = CreateInstallerSelection();
            LmControllerInstallerSelection observed = CreateInstallerSelection();
            AssertTrue(ProvisioningRequestValidator.ValidateInstallerSelection(selected, observed).IsValid,
                "An unchanged locked source snapshot must match the displayed selection.");

            LmControllerInstallerSelection substituted = CreateInstallerSelection();
            substituted.Sha256 = new string('b', 64);
            AssertFalse(ProvisioningRequestValidator.ValidateInstallerSelection(selected, substituted).IsValid,
                "A substituted source hash must fail before installer execution.");

            LmControllerInstallerSelection stale = CreateInstallerSelection();
            stale.ByteLength++;
            AssertFalse(ProvisioningRequestValidator.ValidateInstallerSelection(selected, stale).IsValid,
                "A changed source length must fail before installer execution.");
        }

        private static void LocalModuleVerifierAcceptsOnlyExactLockedMsi()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "regime-test.msi");
                byte[] content = Encoding.ASCII.GetBytes("exact-local-module-msi");
                File.WriteAllBytes(path, content);
                LocalModuleInstallerSelection selection = CreateLocalModuleInstallerSelection(path, content);
                WindowsInstallerPackageMetadata metadata = CreateLocalModulePackageMetadata();
                FakeWindowsInstallerPackageReader reader =
                    new FakeWindowsInstallerPackageReader(metadata);
                FakeFileTrustVerifier trust = new FakeFileTrustVerifier(
                    CreateLocalModuleTrustExpectation(selection),
                    true);
                LocalModulePackageVerifier verifier = new LocalModulePackageVerifier(
                    selection,
                    reader,
                    trust,
                    new FakePathSafety(true));

                using (VerifiedLocalModulePackage package = verifier.VerifyAndLock(selection))
                {
                    AssertEqual(Path.GetFullPath(path), package.FullPath,
                        "The verified package must retain the exact locked source path.");
                    AssertEqual("2.6.1", package.Metadata.ProductVersion,
                        "The exact MSI ProductVersion must be retained.");
                    AssertEqual("{556FD8AD-43A3-4645-BC54-EBF3043ADF82}",
                        package.Metadata.ProductCode,
                        "The exact MSI ProductCode must be retained.");
                    AssertThrows<IOException>(delegate {
                        using (FileStream ignored = new FileStream(
                            path,
                            FileMode.Open,
                            FileAccess.Write,
                            FileShare.ReadWrite))
                        {
                        }
                    }, "The source package must remain locked against substitution.");
                }
            }

            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void LocalModuleVerifierAcceptsSafePinnedSignerIdentity()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "regime-test.msi");
                byte[] content = Encoding.ASCII.GetBytes("exact-local-module-msi");
                File.WriteAllBytes(path, content);
                LocalModuleInstallerSelection selection =
                    CreateLocalModuleInstallerSelection(path, content);
                selection.SignerSubject =
                    "CN=ООО ЦЕНТР РАЗВИТИЯ ПЕРСПЕКТИВНЫХ ТЕХНОЛОГИЙ, " +
                    "O=ООО ЦЕНТР РАЗВИТИЯ ПЕРСПЕКТИВНЫХ ТЕХНОЛОГИЙ";
                TrustedFileExpectation observed =
                    CreateLocalModuleTrustExpectation(selection);
                observed.SignerSubject =
                    "E=employee@example.invalid, " + selection.SignerSubject +
                    ", C=RU";

                using (VerifiedLocalModulePackage package =
                    new LocalModulePackageVerifier(
                        selection,
                        new FakeWindowsInstallerPackageReader(
                            CreateLocalModulePackageMetadata()),
                        new FakeFileTrustVerifier(observed, true),
                        new FakePathSafety(true)).VerifyAndLock(selection))
                {
                    AssertEqual(Path.GetFullPath(path), package.FullPath,
                        "A valid package must not require storing the signer's email.");
                }
                AssertFalse(selection.SignerSubject.IndexOf(
                        '@') >= 0,
                    "The persisted capability identity must contain no personal email.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void
            WinTrustMatchesSafeLocalModuleIdentityOnlyWithPinnedThumbprint()
        {
            string safeSubject =
                "CN=ООО ЦЕНТР РАЗВИТИЯ ПЕРСПЕКТИВНЫХ ТЕХНОЛОГИЙ, " +
                "O=ООО ЦЕНТР РАЗВИТИЯ ПЕРСПЕКТИВНЫХ ТЕХНОЛОГИЙ";
            TrustedFileExpectation expected = new TrustedFileExpectation
            {
                FileName = "regime-2.6.1-7.msi",
                ByteLength = 51007488,
                Sha256 = new string('a', 64),
                FileVersion = string.Empty,
                ProductVersion = string.Empty,
                ProductName = string.Empty,
                CompanyName = string.Empty,
                Machine = PeMachine.Unknown,
                SignerSubject = safeSubject,
                SignerThumbprint =
                    "6BA5F6BBE4BE27658253C78889334D0E24858C19",
                RequireCodeSigningEku = true
            };
            TrustedFileExpectation observed = new TrustedFileExpectation
            {
                FileName = expected.FileName,
                ByteLength = expected.ByteLength,
                Sha256 = expected.Sha256,
                FileVersion = expected.FileVersion,
                ProductVersion = expected.ProductVersion,
                ProductName = expected.ProductName,
                CompanyName = expected.CompanyName,
                Machine = expected.Machine,
                SignerSubject = "E=employee@example.invalid, " + safeSubject +
                    ", C=RU",
                SignerThumbprint = expected.SignerThumbprint,
                RequireCodeSigningEku = true
            };
            MethodInfo matches = typeof(WinTrustVerifier).GetMethod(
                "Matches",
                BindingFlags.Static | BindingFlags.NonPublic);

            AssertTrue(matches != null && (bool)matches.Invoke(
                    null,
                    new object[] { expected, observed }),
                "A safe CN/O identity plus the pinned thumbprint must match the full certificate subject.");
            observed.SignerThumbprint = new string('0', 40);
            AssertFalse((bool)matches.Invoke(
                    null,
                    new object[] { expected, observed }),
                "The safe subject subset must never replace thumbprint pinning.");
        }

        private static void LocalModuleVerifierRejectsPackageIdentityOrSignerMismatch()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "regime-test.msi");
                byte[] content = Encoding.ASCII.GetBytes("exact-local-module-msi");
                File.WriteAllBytes(path, content);
                LocalModuleInstallerSelection selection = CreateLocalModuleInstallerSelection(path, content);

                WindowsInstallerPackageMetadata wrongProduct = CreateLocalModulePackageMetadata();
                wrongProduct.ProductCode = "{00000000-0000-0000-0000-000000000000}";
                AssertThrows<InvalidDataException>(delegate {
                    new LocalModulePackageVerifier(
                        selection,
                        new FakeWindowsInstallerPackageReader(wrongProduct),
                        new FakeFileTrustVerifier(CreateLocalModuleTrustExpectation(selection), true),
                        new FakePathSafety(true)).VerifyAndLock(selection).Dispose();
                }, "A different ProductCode must be rejected.");

                WindowsInstallerPackageMetadata wrongVersion = CreateLocalModulePackageMetadata();
                wrongVersion.ProductVersion = "2.6.2";
                AssertThrows<InvalidDataException>(delegate {
                    new LocalModulePackageVerifier(
                        selection,
                        new FakeWindowsInstallerPackageReader(wrongVersion),
                        new FakeFileTrustVerifier(CreateLocalModuleTrustExpectation(selection), true),
                        new FakePathSafety(true)).VerifyAndLock(selection).Dispose();
                }, "A different ProductVersion must be rejected.");

                TrustedFileExpectation wrongSigner = CreateLocalModuleTrustExpectation(selection);
                wrongSigner.SignerSubject = "CN=Unexpected Signer";
                AssertThrows<InvalidDataException>(delegate {
                    new LocalModulePackageVerifier(
                        selection,
                        new FakeWindowsInstallerPackageReader(CreateLocalModulePackageMetadata()),
                        new FakeFileTrustVerifier(wrongSigner, true),
                        new FakePathSafety(true)).VerifyAndLock(selection).Dispose();
                }, "A substituted signer must be rejected even if a trust adapter reports success.");

                TrustedFileExpectation substitutedFile =
                    CreateLocalModuleTrustExpectation(selection);
                substitutedFile.Sha256 = new string('c', 64);
                AssertThrows<InvalidDataException>(delegate {
                    new LocalModulePackageVerifier(
                        selection,
                        new FakeWindowsInstallerPackageReader(CreateLocalModulePackageMetadata()),
                        new FakeFileTrustVerifier(substitutedFile, true),
                        new FakePathSafety(true)).VerifyAndLock(selection).Dispose();
                }, "A source file substituted after confirmation must be rejected.");

                LocalModuleInstallerSelection staleSelection = CloneLocalModuleInstallerSelection(selection);
                staleSelection.Sha256 = new string('b', 64);
                AssertThrows<InvalidDataException>(delegate {
                    new LocalModulePackageVerifier(
                        selection,
                        new FakeWindowsInstallerPackageReader(CreateLocalModulePackageMetadata()),
                        new FakeFileTrustVerifier(CreateLocalModuleTrustExpectation(selection), true),
                        new FakePathSafety(true)).VerifyAndLock(staleSelection).Dispose();
                }, "A selection changed after confirmation must be rejected.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void ManagedLocalModuleProtocolAcceptsConsistentSharedInnRows()
        {
            LmServiceProvisioningBatchRequest request = CreateManagedLocalModuleRequest(2);
            AssertTrue(ProvisioningRequestValidator.Validate(request).IsValid,
                "A sorted exact-version local-module request must be accepted.");

            LmServiceProvisioningBatchRequest sharedInn = CreateManagedLocalModuleRequest(2);
            ManagedLocalModuleProvisioningItemRequest first = sharedInn.ManagedLocalModules[0];
            ManagedLocalModuleProvisioningItemRequest second = sharedInn.ManagedLocalModules[1];
            second.Inn = first.Inn;
            second.LocalModuleOrdinal = first.LocalModuleOrdinal;
            second.ApiPort = first.ApiPort;
            second.DatabasePort = first.DatabasePort;
            second.EpmdPort = first.EpmdPort;
            sharedInn.PlanHash = CanonicalLmPlanHasher.Compute(sharedInn);
            AssertTrue(ProvisioningRequestValidator.Validate(sharedInn).IsValid,
                "Two KKT of one INN must share one immutable LM definition in one session.");

            LmServiceProvisioningBatchRequest inconsistent = CreateManagedLocalModuleRequest(2);
            first = inconsistent.ManagedLocalModules[0];
            second = inconsistent.ManagedLocalModules[1];
            second.Inn = first.Inn;
            second.LocalModuleOrdinal = first.LocalModuleOrdinal;
            second.ApiPort = first.ApiPort + 1000;
            second.DatabasePort = first.DatabasePort;
            second.EpmdPort = first.EpmdPort;
            inconsistent.PlanHash = CanonicalLmPlanHasher.Compute(inconsistent);
            AssertFalse(ProvisioningRequestValidator.Validate(inconsistent).IsValid,
                "One INN must never carry two different LM endpoints in one session.");

            LmServiceProvisioningBatchRequest changedPlan = CreateManagedLocalModuleRequest(1);
            changedPlan.ManagedLocalModules[0].ApiPort++;
            ValidationResult changedValidation = ProvisioningRequestValidator.Validate(changedPlan);
            AssertFalse(changedValidation.IsValid,
                "A port changed after confirmation must invalidate the plan hash.");
            AssertContains(changedValidation.JoinMessages(), "SHA-256");

            LmServiceProvisioningBatchRequest changedController =
                CreateManagedLocalModuleRequest(1);
            changedController.InstallerSelection.Sha256 = new string('b', 64);
            AssertFalse(ProvisioningRequestValidator.Validate(changedController).IsValid,
                "The controller package must belong to the same immutable full-stack plan.");
        }

        private static void ManagedProvisioningSessionAcceptsOnlyKnownMonotonicMessages()
        {
            string operationId = Guid.NewGuid().ToString("N");
            ManagedProvisioningSessionMessage ready = new ManagedProvisioningSessionMessage
            {
                SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                OperationId = operationId,
                Sequence = 1,
                Kind = ManagedProvisioningSessionKind.SessionReady,
                ItemIndex = -1
            };
            AssertTrue(ProvisioningRequestValidator.ValidateSessionMessage(
                    ready, operationId, 0, 2).IsValid,
                "The first SessionReady message must be accepted.");

            ManagedProvisioningSessionMessage execute = new ManagedProvisioningSessionMessage
            {
                SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                OperationId = operationId,
                Sequence = 2,
                Kind = ManagedProvisioningSessionKind.ExecuteItem,
                ItemIndex = 0
            };
            AssertTrue(ProvisioningRequestValidator.ValidateSessionMessage(
                    execute, operationId, 1, 2).IsValid,
                "The next ExecuteItem message must be accepted.");
            AssertFalse(ProvisioningRequestValidator.ValidateSessionMessage(
                    execute, operationId, 2, 2).IsValid,
                "A repeated sequence must be rejected.");

            ManagedProvisioningSessionMessage unknown = new ManagedProvisioningSessionMessage
            {
                SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                OperationId = operationId,
                Sequence = 3,
                Kind = (ManagedProvisioningSessionKind)999,
                ItemIndex = -1
            };
            AssertFalse(ProvisioningRequestValidator.ValidateSessionMessage(
                    unknown, operationId, 2, 2).IsValid,
                "An unknown session message kind must be rejected.");
            AssertEqual(5, Enum.GetValues(typeof(ManagedProvisioningSessionKind)).Length,
                "The session protocol must expose exactly the five reviewed message kinds.");
        }

        private static void ManagedSessionServerInterleavesCallerAndHelperPerKkt()
        {
            LmServiceProvisioningBatchRequest request =
                CreateManagedLocalModuleRequest(2);
            FakeManagedLocalModuleProvisioningPlatform platform =
                new FakeManagedLocalModuleProvisioningPlatform();
            using (CompleteStackProvisioningSession session =
                new CompleteStackProvisioningSession(
                    request,
                    new ManagedLocalModuleProvisioner(platform),
                    new ManagedLocalModuleProvisioningContext(
                        "lmrt-0123456789abcdef01234567",
                        "local-module-2.6.1-7",
                        "2.6.1")))
            {
                FakeManagedProvisioningSessionChannel channel =
                    new FakeManagedProvisioningSessionChannel(
                        new[]
                        {
                            new ManagedProvisioningSessionMessage
                            {
                                SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                                OperationId = request.OperationId,
                                Sequence = 2,
                                Kind = ManagedProvisioningSessionKind.ExecuteItem,
                                ItemIndex = 0,
                                Status = LmServiceProvisioningStatus.Pending
                            },
                            new ManagedProvisioningSessionMessage
                            {
                                SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                                OperationId = request.OperationId,
                                Sequence = 4,
                                Kind = ManagedProvisioningSessionKind.ExecuteItem,
                                ItemIndex = 1,
                                Status = LmServiceProvisioningStatus.Cancelled,
                                Message = "ККТ не зарегистрирована в ЕСМ."
                            },
                            new ManagedProvisioningSessionMessage
                            {
                                SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                                OperationId = request.OperationId,
                                Sequence = 6,
                                Kind = ManagedProvisioningSessionKind.Finish,
                                ItemIndex = -1,
                                Status = LmServiceProvisioningStatus.Pending
                            }
                        });

                LmServiceProvisioningBatchResult result =
                    new ManagedProvisioningSessionServer(channel)
                        .Run(request, session);

                AssertEqual(3, channel.SessionMessages.Count,
                    "The helper must send ready and one result per requested KKT.");
                AssertEqual(ManagedProvisioningSessionKind.SessionReady,
                    channel.SessionMessages[0].Kind,
                    "The helper must complete immutable preflight before caller registration.");
                AssertEqual(1L, channel.SessionMessages[0].Sequence,
                    "The helper must start the global session sequence.");
                AssertEqual(ManagedProvisioningSessionKind.ItemResult,
                    channel.SessionMessages[1].Kind,
                    "The first registered KKT must be provisioned before the next caller step.");
                AssertEqual(3L, channel.SessionMessages[1].Sequence,
                    "Caller and helper messages must share one monotonic sequence.");
                AssertEqual(LmServiceProvisioningStatus.Cancelled,
                    channel.SessionMessages[2].Status,
                    "An unregistered later KKT must be skipped without local mutation.");
                AssertEqual(2, result.Items.Count,
                    "The final batch result must cover the immutable full plan.");
                AssertEqual(1, platform.ReconciledInns.Count,
                    "Only the successfully registered KKT may enter local provisioning.");
            }
        }

        private static void LocalModuleConfigsIsolateEveryMutablePath()
        {
            string runtimeRoot =
                @"C:\Program Files\KRS\MultiKKT\LocalModuleRuntime\2.6.1";
            string profileRoot =
                @"C:\ProgramData\KRS\MultiKKT\LocalModules\lm-n01";
            LocalModuleConfiguration configuration = LocalModuleConfigurationWriter.Build(
                LocalModuleCapabilityProfile.Resolve("2.6.1"),
                runtimeRoot,
                profileRoot,
                CreateManagedLocalModuleRequest(1).ManagedLocalModules[0],
                "0123456789abcdef0123456789abcdef",
                CreateLocalModuleTemplateObservation());
            string profileUri = profileRoot.Replace('\\', '/');

            AssertContains(configuration.RegimeLocalIni, "ip_address = 127.0.0.1");
            AssertContains(configuration.RegimeLocalIni, "port = 5995");
            AssertContains(configuration.RegimeLocalIni, "db_url = http://127.0.0.1:5984");
            AssertContains(configuration.RegimeLocalIni, profileUri + "/data/key-store");
            AssertContains(configuration.YeniseiLocalIni, "bind_address = 127.0.0.1");
            AssertContains(configuration.YeniseiLocalIni, profileUri + "/data/database");
            AssertContains(configuration.YeniseiLocalIni, profileUri + "/data/index");
            AssertContains(configuration.YeniseiLocalIni, profileUri + "/logs/yenisei.log");
            AssertContains(configuration.RegimeSysConfig, profileUri + "/logs/regime.log");
            AssertFalse(configuration.AllText.IndexOf("./yenisei/data", StringComparison.Ordinal) >= 0 ||
                        configuration.AllText.IndexOf("var/log", StringComparison.Ordinal) >= 0,
                "Generated configuration must not retain mutable paths relative to shared runtime.");
            AssertThrows<InvalidDataException>(delegate {
                LocalModuleConfigurationWriter.Build(
                    LocalModuleCapabilityProfile.Resolve("2.6.1"),
                    runtimeRoot,
                    runtimeRoot,
                    CreateManagedLocalModuleRequest(1).ManagedLocalModules[0],
                    "0123456789abcdef0123456789abcdef",
                    CreateLocalModuleTemplateObservation());
            }, "A mutable profile must never be placed inside shared runtime.");
        }

        private static void LocalModuleStartPlansShareOnlyReadOnlyRuntime()
        {
            string runtimeRoot =
                @"C:\Program Files\KRS\MultiKKT\LocalModuleRuntime\2.6.1";
            LmServiceProvisioningBatchRequest request = CreateManagedLocalModuleRequest(2);
            LocalModuleCapabilityProfile profile = LocalModuleCapabilityProfile.Resolve("2.6.1");
            AssertEqual(
                "a6d537344f70f4396614bba6095b1d21bd061f717bf609175f4584c24510f272",
                profile.RuntimeContractSha256,
                "The exact required-file contract must stay pinned to the reviewed image.");
            AssertTrue(profile.ExcludedRuntimeRelativePaths.Contains(
                    @"erts-13.0.4\bin\erl.ini"),
                "The blank installation-specific erl.ini must not enter managed runtime.");
            AssertThrows<NotSupportedException>(delegate {
                profile.RequiredDirectories.Add("caller-controlled");
            }, "The exact runtime contract must be immutable.");
            AssertFalse(
                typeof(LocalModuleRequiredFile).GetProperty(
                    "RelativePath",
                    BindingFlags.Instance | BindingFlags.NonPublic).CanWrite,
                "Required-file identities must be immutable after capability resolution.");
            LocalModuleTemplateObservation templates = CreateLocalModuleTemplateObservation();
            LocalModuleConfiguration first = LocalModuleConfigurationWriter.Build(
                profile,
                runtimeRoot,
                @"C:\ProgramData\KRS\MultiKKT\LocalModules\lm-n01",
                request.ManagedLocalModules[0],
                "0123456789abcdef0123456789abcdef",
                templates);
            LocalModuleConfiguration second = LocalModuleConfigurationWriter.Build(
                profile,
                runtimeRoot,
                @"C:\ProgramData\KRS\MultiKKT\LocalModules\lm-n02",
                request.ManagedLocalModules[1],
                "abcdef0123456789abcdef0123456789",
                templates);

            AssertEqual(first.ApiStartPlan.ExecutablePath, second.ApiStartPlan.ExecutablePath,
                "All instances must use the same verified erl.exe.");
            AssertEqual(first.DatabaseStartPlan.ExecutablePath, second.DatabaseStartPlan.ExecutablePath,
                "Both database instances must use the same verified erl.exe.");
            AssertEqual(first.ApiStartPlan.WorkingDirectory, second.ApiStartPlan.WorkingDirectory,
                "The shared read-only runtime must be the fixed working directory.");
            AssertFalse(string.Equals(
                    first.ApiStartPlan.ArgumentTokens[3],
                    second.ApiStartPlan.ArgumentTokens[3],
                    StringComparison.OrdinalIgnoreCase),
                "Each API process must receive its own vm.args path.");
            AssertFalse(string.Equals(
                    first.DatabaseStartPlan.ArgumentTokens[7],
                    second.DatabaseStartPlan.ArgumentTokens[7],
                    StringComparison.OrdinalIgnoreCase),
                "Each database process must receive its own sys.config path.");
            AssertEqual("43691", first.ApiStartPlan.Environment["ERL_EPMD_PORT"],
                "LM #1 must receive only EPMD 43691.");
            AssertEqual("43692", second.ApiStartPlan.Environment["ERL_EPMD_PORT"],
                "LM #2 must receive only EPMD 43692.");
            AssertContains(first.DatabaseStartPlan.Environment["YENISEI_QUERY_SERVER_JAVASCRIPT"],
                runtimeRoot.Replace('\\', '/'));
            AssertThrows<NotSupportedException>(delegate {
                first.ApiStartPlan.Environment["CALLER_VALUE"] = "not-allowed";
            }, "The fixed process environment must be immutable after planning.");
        }

        private static void LocalModuleChildEnvironmentSuppliesWindowsRuntimeAndIsolatedTemp()
        {
            string runtimeRoot =
                @"C:\Program Files\KRS\MultiKKT\LocalModuleRuntime\2.6.1";
            string profileRoot =
                @"C:\ProgramData\KRS\MultiKKT\LocalModules\lm-n01";
            LocalModuleConfiguration configuration =
                LocalModuleConfigurationWriter.Build(
                    LocalModuleCapabilityProfile.Resolve("2.6.1"),
                    runtimeRoot,
                    profileRoot,
                    CreateManagedLocalModuleRequest(1).ManagedLocalModules[0],
                    "0123456789abcdef0123456789abcdef",
                    CreateLocalModuleTemplateObservation());
            string windowsRoot = Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);
            string tempRoot = Path.Combine(profileRoot, "data", "temp");

            AssertEqual(windowsRoot,
                configuration.ApiStartPlan.Environment["SystemRoot"],
                "A replaced environment must retain the Windows runtime root.");
            AssertEqual(windowsRoot,
                configuration.ApiStartPlan.Environment["windir"],
                "Windows children must receive the canonical windir value.");
            AssertEqual(tempRoot,
                configuration.ApiStartPlan.Environment["TEMP"],
                "API temporary files must stay inside the owned LM profile.");
            AssertEqual(tempRoot,
                configuration.ApiStartPlan.Environment["TMP"],
                "TMP must use the same isolated owned directory.");
            AssertEqual(Path.Combine(tempRoot, "regime-erl-crash.dump"),
                configuration.ApiStartPlan.Environment["ERL_CRASH_DUMP"],
                "API crash dumps must not target the shared read-only runtime.");
            AssertEqual(Path.Combine(tempRoot, "yenisei-erl-crash.dump"),
                configuration.DatabaseStartPlan.Environment["ERL_CRASH_DUMP"],
                "Database crash dumps must not target the shared read-only runtime.");
            AssertFalse(configuration.ApiStartPlan.Environment.ContainsKey(
                    "USERPROFILE"),
                "The fixed child environment must not inherit a user profile.");
        }

        private static void LocalModuleConfigRejectsAmbiguousTemplate()
        {
            LocalModuleTemplateObservation ambiguous = CreateLocalModuleTemplateObservation();
            ambiguous.RegimeLocalIni = ambiguous.RegimeLocalIni +
                Environment.NewLine + "[api]" + Environment.NewLine + "port = 9999";

            AssertThrows<InvalidDataException>(delegate {
                LocalModuleConfigurationWriter.Build(
                    LocalModuleCapabilityProfile.Resolve("2.6.1"),
                    @"C:\Program Files\KRS\MultiKKT\LocalModuleRuntime\2.6.1",
                    @"C:\ProgramData\KRS\MultiKKT\LocalModules\lm-n01",
                    CreateManagedLocalModuleRequest(1).ManagedLocalModules[0],
                    "0123456789abcdef0123456789abcdef",
                    ambiguous);
            }, "A duplicated required section or key must fail closed.");
        }

        private static void LocalModuleConfigsContainNoCredential()
        {
            LocalModuleConfiguration configuration = LocalModuleConfigurationWriter.Build(
                LocalModuleCapabilityProfile.Resolve("2.6.1"),
                @"C:\Program Files\KRS\MultiKKT\LocalModuleRuntime\2.6.1",
                @"C:\ProgramData\KRS\MultiKKT\LocalModules\lm-n01",
                CreateManagedLocalModuleRequest(1).ManagedLocalModules[0],
                "0123456789abcdef0123456789abcdef",
                CreateLocalModuleTemplateObservation());
            string text = configuration.AllText.ToLowerInvariant();

            AssertFalse(text.IndexOf("password", StringComparison.Ordinal) >= 0 ||
                        text.IndexOf("login", StringComparison.Ordinal) >= 0 ||
                        text.IndexOf("proxy_user", StringComparison.Ordinal) >= 0 ||
                        text.IndexOf("authorization", StringComparison.Ordinal) >= 0 ||
                        text.IndexOf("cpu_util_cmd", StringComparison.Ordinal) >= 0,
                "Generated configuration must not copy customer credentials or optional shell hooks.");
            AssertContains(configuration.RegimeVmArgs, "-setcookie krs_lm_");
            AssertEqual(
                ReadLineStarting(configuration.RegimeVmArgs, "-setcookie "),
                ReadLineStarting(configuration.YeniseiVmArgs, "-setcookie "),
                "The two processes of one LM must share only their generated Erlang cookie.");
        }

        private static void LocalModuleRuntimeExcludesWrappersAndFixesExtraction()
        {
            LocalModuleCapabilityProfile capability =
                LocalModuleCapabilityProfile.Resolve("2.6.1");
            for (int index = 0; index < capability.ExcludedRuntimeRelativePaths.Count; index++)
            {
                AssertFalse(LocalModuleRuntimeInstaller.ShouldCopyRelativePath(
                        capability,
                        capability.ExcludedRuntimeRelativePaths[index]),
                    "A fixed installer wrapper, updater or erl.ini must never enter runtime.");
            }
            AssertTrue(LocalModuleRuntimeInstaller.ShouldCopyRelativePath(
                    capability,
                    @"erts-13.0.4\bin\erl.exe"),
                "The verified Erlang launcher must remain in runtime.");

            LocalModuleAdministrativeExtractionPlan plan =
                LocalModuleRuntimeInstaller.BuildAdministrativeExtractionPlan(
                    @"C:\locked\regime-2.6.1-7.msi",
                    @"C:\protected\stage",
                    @"C:\protected\logs\extract.log");
            AssertEqual(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32",
                    "msiexec.exe"),
                plan.ExecutablePath,
                "Administrative extraction must use the system msiexec image.");
            AssertEqual(6, plan.ArgumentTokens.Count,
                "The extraction command must expose only the reviewed fixed token set.");
            AssertEqual("/a", plan.ArgumentTokens[0], "Expected administrative extraction.");
            AssertEqual(@"C:\locked\regime-2.6.1-7.msi", plan.ArgumentTokens[1],
                "The locked MSI must be passed as one token.");
            AssertEqual("/qn", plan.ArgumentTokens[2], "Extraction must be silent.");
            AssertEqual(@"TARGETDIR=C:\protected\stage", plan.ArgumentTokens[3],
                "TARGETDIR must be derived by the helper.");
            AssertEqual("/l*v", plan.ArgumentTokens[4], "A verbose protected log is required.");
            AssertEqual(@"C:\protected\logs\extract.log", plan.ArgumentTokens[5],
                "The log path must be passed as one token.");
            AssertThrows<InvalidDataException>(delegate {
                new LocalModuleVerifiedRuntimeImage(
                    @"C:\protected\image",
                    capability.CapabilityId,
                    capability.RuntimeContractSha256,
                    new[] { new LocalModuleRuntimeFile(
                        @"..\outside.bin",
                        1,
                        new string('0', 64)) });
            }, "A verified image inventory must reject lexical path escape before copying.");
        }

        private static void AdministrativeMsiExtractionUsesNativeCommandSyntax()
        {
            LocalModuleAdministrativeExtractionPlan plan =
                LocalModuleRuntimeInstaller.BuildAdministrativeExtractionPlan(
                    @"C:\locked package\regime-2.6.1-7.msi",
                    @"C:\protected stage\administrative image",
                    @"C:\protected logs\extract.log");
            MethodInfo join = typeof(LocalModuleRuntimeInstaller).GetMethod(
                "JoinArguments",
                BindingFlags.NonPublic | BindingFlags.Static);
            AssertTrue(join != null,
                "The production argument renderer must remain directly testable.");

            string commandLine = (string)join.Invoke(
                null,
                new object[] { plan.ArgumentTokens });

            AssertEqual(
                @"/a ""C:\locked package\regime-2.6.1-7.msi"" /qn TARGETDIR=""C:\protected stage\administrative image"" /l*v ""C:\protected logs\extract.log""",
                commandLine,
                "msiexec switches must stay bare while path values remain quoted.");
        }

        private static void LocalModuleManifestsEnforceThreeOwnershipLevels()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string appRoot = Path.Combine(root, "app");
                string runtimeContainer = Path.Combine(root, "program", "LocalModuleRuntime");
                LocalModuleManifestStore store = new LocalModuleManifestStore(
                    appRoot,
                    runtimeContainer,
                    new FakePathSafety(true),
                    "S-1-5-21-111-222-333-1001");
                LocalModuleInstallerSelection package =
                    LocalModulePackageVerifier.CreateSupportedIdentity();
                LocalModuleCapabilityProfile capability =
                    LocalModuleCapabilityProfile.Resolve("2.6.1");
                string runtimeNonce = "11111111111111111111111111111111";
                string runtimeId = LocalModuleManagedIdentity.CreateRuntimeId(
                    capability.CapabilityId,
                    package.Sha256);
                string runtimeRoot = store.GetRuntimeRoot(runtimeId);
                Directory.CreateDirectory(runtimeRoot);
                byte[] runtimeBytes = Encoding.ASCII.GetBytes("verified-runtime");
                string runtimeFile = Path.Combine(runtimeRoot, "lib", "sample.beam");
                Directory.CreateDirectory(Path.GetDirectoryName(runtimeFile));
                File.WriteAllBytes(runtimeFile, runtimeBytes);
                LocalModuleRuntimeManifest runtime = LocalModuleRuntimeManifest.Create(
                    package,
                    capability,
                    runtimeRoot,
                    runtimeNonce,
                    new[] { new LocalModuleRuntimeFile(
                        @"lib\sample.beam",
                        runtimeBytes.Length,
                        ComputeSha256(runtimeBytes)) });
                store.WriteRuntime(runtime);

                ManagedLocalModuleProvisioningItemRequest item =
                    CreateManagedLocalModuleRequest(1).ManagedLocalModules[0];
                string instanceNonce = "22222222222222222222222222222222";
                string instanceId = LocalModuleManagedIdentity.CreateInstanceId(
                    item.Inn,
                    instanceNonce);
                string profileRoot = store.GetInstanceRoot(instanceId);
                LocalModuleConfiguration configuration = LocalModuleConfigurationWriter.Build(
                    capability,
                    runtimeRoot,
                    profileRoot,
                    item,
                    instanceNonce,
                    CreateLocalModuleTemplateObservation());
                LocalModuleInstanceManifest instance = LocalModuleInstanceManifest.Create(
                    item,
                    instanceId,
                    runtime.RuntimeId,
                    runtimeRoot,
                    configuration,
                    instanceNonce,
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
                store.WriteInstance(instance);

                ManagedKktStackManifest stack = ManagedKktStackManifest.Create(
                    item,
                    instance.InstanceId,
                    "esm-record-1",
                    "33333333333333333333333333333333",
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
                store.WriteStack(stack);

                AssertFalse(string.Equals(
                        runtime.OwnershipMarker,
                        instance.OwnershipMarker,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        instance.OwnershipMarker,
                        stack.OwnershipMarker,
                        StringComparison.Ordinal),
                    "Runtime, INN profile and KKT stack must have distinct ownership domains.");
                AssertEqual(runtime.RuntimeId, store.ReadRuntime(runtime.RuntimeId).RuntimeId,
                    "Runtime ownership must round-trip independently.");
                AssertEqual(instance.InstanceId, store.ReadInstance(instance.InstanceId).InstanceId,
                    "INN ownership must round-trip independently.");
                AssertEqual(stack.KktSerial, store.ReadStack(stack.KktSerial).KktSerial,
                    "KKT ownership must round-trip independently.");
                AssertTrue(instance.CurrentOperationId.Length == 32 &&
                           instance.LastCompletedOperationId.Length == 0,
                    "A preparing INN manifest must not claim that its operation already completed.");
                AssertEqual(1, store.CountRuntimeReferences(runtime.RuntimeId),
                    "Runtime reference count must be reconstructed from verified INN manifests.");
                AssertEqual(1, store.ReadRuntime(runtime.RuntimeId).ConfirmedReferenceCount,
                    "The stored reference count must mirror the independently reconstructed count.");
                AssertEqual(1, store.CountInstanceReferences(instance.InstanceId),
                    "INN references must be reconstructed from verified KKT stack manifests.");
                AssertThrows<InvalidOperationException>(delegate {
                    store.DeleteInstance(instance.InstanceId, instance.OwnershipNonce);
                }, "A KKT stack must prevent deletion of its shared INN instance.");

                runtime.OwnershipMarker = ManagedKktStackManifest.ExpectedOwnershipMarker;
                AssertThrows<InvalidDataException>(delegate { store.WriteRuntime(runtime); },
                    "A marker from another ownership level must never authorize mutation.");
                runtime = store.ReadRuntime(runtime.RuntimeId);
                runtime.Files[0].Sha256 = new string('0', 64);
                AssertThrows<InvalidDataException>(delegate { store.WriteRuntime(runtime); },
                    "A matching nonce must not authorize replacement of runtime inventory.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void LocalModuleRuntimeDeletionRequiresZeroReferences()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore store;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(root, out store, out runtime, out instance);
                LocalModuleRuntimeInstaller installer = new LocalModuleRuntimeInstaller(
                    store,
                    new FakePathSafety(true),
                    NoopLocalModuleMutationBoundary.Instance);

                AssertThrows<InvalidOperationException>(delegate {
                    installer.DeleteUnreferencedRuntime(
                        runtime.RuntimeId,
                        runtime.OwnershipNonce);
                }, "A runtime referenced by an INN manifest must not be removed.");
                store.DeleteInstance(instance.InstanceId, instance.OwnershipNonce);
                AssertEqual(0, store.ReadRuntime(runtime.RuntimeId).ConfirmedReferenceCount,
                    "Removing an INN manifest must refresh the advisory runtime count.");
                installer.DeleteUnreferencedRuntime(
                    runtime.RuntimeId,
                    runtime.OwnershipNonce);

                AssertFalse(Directory.Exists(runtime.RuntimeRoot),
                    "An exactly owned zero-reference runtime must be deleted.");
                LocalModuleRuntimeManifest ignored;
                AssertFalse(store.TryReadRuntime(runtime.RuntimeId, out ignored),
                    "Runtime inventory must be removed only after filesystem deletion succeeds.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void ExistingLocalModuleRuntimeVerifiesWithoutMsiExtraction()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore store;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    out store,
                    out runtime,
                    out instance);
                LocalModuleRuntimeInstaller installer =
                    new LocalModuleRuntimeInstaller(
                        store,
                        new FakePathSafety(true),
                        NoopLocalModuleMutationBoundary.Instance);

                LocalModuleRuntimeManifest verified =
                    installer.VerifyExistingRuntime(
                        runtime.RuntimeId,
                        LocalModulePackageVerifier.CreateSupportedIdentity(),
                        LocalModuleCapabilityProfile.Resolve("2.6.1"));

                AssertEqual(runtime.RuntimeId, verified.RuntimeId,
                    "A matching runtime must be reused without another administrative extraction.");
                string runtimeFile = Path.Combine(
                    runtime.RuntimeRoot,
                    runtime.Files[0].RelativePath);
                File.WriteAllText(runtimeFile, "tampered", Encoding.ASCII);
                AssertThrows<InvalidDataException>(delegate {
                    installer.VerifyExistingRuntime(
                        runtime.RuntimeId,
                        LocalModulePackageVerifier.CreateSupportedIdentity(),
                        LocalModuleCapabilityProfile.Resolve("2.6.1"));
                }, "A reused runtime must be rehashed before any service is started.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void LocalModuleRuntimeRecoversEveryMutationBoundary()
        {
            LocalModuleRuntimeMutationBoundary[] boundaries =
            {
                LocalModuleRuntimeMutationBoundary.StageCreated,
                LocalModuleRuntimeMutationBoundary.FilesCopied,
                LocalModuleRuntimeMutationBoundary.RuntimePromoted,
                LocalModuleRuntimeMutationBoundary.RuntimeManifestWritten
            };
            for (int boundaryIndex = 0; boundaryIndex < boundaries.Length; boundaryIndex++)
            {
                string root = CreateTemporaryDirectory();
                try
                {
                    string sourceRoot = Path.Combine(root, "administrative-image", "Program Files", "Regime");
                    Directory.CreateDirectory(Path.Combine(sourceRoot, "lib"));
                    Directory.CreateDirectory(Path.Combine(sourceRoot, "bin"));
                    Directory.CreateDirectory(Path.Combine(sourceRoot, "erts-13.0.4", "bin"));
                    byte[] runtimeBytes = Encoding.ASCII.GetBytes("runtime-copy");
                    File.WriteAllBytes(Path.Combine(sourceRoot, "lib", "sample.beam"), runtimeBytes);
                    File.WriteAllText(Path.Combine(sourceRoot, "bin", "nssm.exe"), "excluded");
                    File.WriteAllText(
                        Path.Combine(sourceRoot, "erts-13.0.4", "bin", "erl.ini"),
                        "[erlang]\nBindir=\nProgname=erl\nRootdir=\n");

                    string appRoot = Path.Combine(root, "app");
                    string runtimeContainer = Path.Combine(root, "program", "LocalModuleRuntime");
                    LocalModuleManifestStore store = new LocalModuleManifestStore(
                        appRoot,
                        runtimeContainer,
                        new FakePathSafety(true),
                        null);
                    LocalModuleCapabilityProfile capability =
                        LocalModuleCapabilityProfile.Resolve("2.6.1");
                    LocalModuleInstallerSelection package =
                        LocalModulePackageVerifier.CreateSupportedIdentity();
                    LocalModuleVerifiedRuntimeImage image = new LocalModuleVerifiedRuntimeImage(
                        sourceRoot,
                        capability.CapabilityId,
                        capability.RuntimeContractSha256,
                        new[] { new LocalModuleRuntimeFile(
                            @"lib\sample.beam",
                            runtimeBytes.Length,
                            ComputeSha256(runtimeBytes)) });
                    string operationId = Guid.NewGuid().ToString("N");
                    string nonce = "44444444444444444444444444444444";
                    ThrowOnceLocalModuleMutationBoundary fault =
                        new ThrowOnceLocalModuleMutationBoundary(boundaries[boundaryIndex]);

                    AssertThrows<IOException>(delegate {
                        new LocalModuleRuntimeInstaller(store, new FakePathSafety(true), fault)
                            .InstallVerifiedImage(image, package, capability, operationId, nonce);
                    }, "The selected mutation boundary must simulate interruption.");

                    LocalModuleRuntimeManifest recovered = new LocalModuleRuntimeInstaller(
                        store,
                        new FakePathSafety(true),
                        NoopLocalModuleMutationBoundary.Instance).InstallVerifiedImage(
                            image,
                            package,
                            capability,
                            operationId,
                            nonce);
                    AssertTrue(File.Exists(Path.Combine(recovered.RuntimeRoot, "lib", "sample.beam")),
                        "Recovery must complete the exact verified runtime.");
                    AssertFalse(File.Exists(Path.Combine(recovered.RuntimeRoot, "bin", "nssm.exe")) ||
                                File.Exists(Path.Combine(
                                    recovered.RuntimeRoot,
                                    "erts-13.0.4",
                                    "bin",
                                    "erl.ini")),
                        "Recovery must not reintroduce excluded wrappers or blank erl.ini.");
                    AssertEqual(0, store.OperationJournals.ReadForSubject(
                        LocalModuleOperationSubject.Runtime,
                        recovered.RuntimeId).Count,
                        "A completed recovery must remove its operation journal.");
                }
                finally
                {
                    DeleteTestTreeWithReadOnlyFiles(root);
                }
            }
        }

        private static void LocalModuleRuntimeDeletionRecoversEveryMutationBoundary()
        {
            LocalModuleRuntimeMutationBoundary[] boundaries =
            {
                LocalModuleRuntimeMutationBoundary.RuntimeFilesDeleted,
                LocalModuleRuntimeMutationBoundary.RuntimeInventoryDeleted
            };
            for (int boundaryIndex = 0; boundaryIndex < boundaries.Length; boundaryIndex++)
            {
                string root = CreateTemporaryDirectory();
                try
                {
                    LocalModuleManifestStore store;
                    LocalModuleRuntimeManifest runtime;
                    LocalModuleInstanceManifest instance;
                    CreateTestManagedLocalModuleOwnership(
                        root,
                        out store,
                        out runtime,
                        out instance);
                    store.DeleteInstance(instance.InstanceId, instance.OwnershipNonce);
                    ThrowOnceLocalModuleMutationBoundary fault =
                        new ThrowOnceLocalModuleMutationBoundary(boundaries[boundaryIndex]);

                    AssertThrows<IOException>(delegate {
                        new LocalModuleRuntimeInstaller(store, new FakePathSafety(true), fault)
                            .DeleteUnreferencedRuntime(
                                runtime.RuntimeId,
                                runtime.OwnershipNonce);
                    }, "The selected deletion boundary must simulate interruption.");

                    new LocalModuleRuntimeInstaller(
                        store,
                        new FakePathSafety(true),
                        NoopLocalModuleMutationBoundary.Instance).DeleteUnreferencedRuntime(
                            runtime.RuntimeId,
                            runtime.OwnershipNonce);
                    AssertFalse(Directory.Exists(runtime.RuntimeRoot),
                        "Deletion recovery must leave no runtime tree.");
                    LocalModuleRuntimeManifest ignored;
                    AssertFalse(store.TryReadRuntime(runtime.RuntimeId, out ignored),
                        "Deletion recovery must leave no runtime manifest.");
                    AssertEqual(0, store.OperationJournals.ReadForSubject(
                        LocalModuleOperationSubject.Runtime,
                        runtime.RuntimeId).Count,
                        "Deletion recovery must consume its journal.");
                }
                finally
                {
                    DeleteTestTreeWithReadOnlyFiles(root);
                }
            }
        }

        private static void OfficialControllerLocatorEnforcesProtectedAllowedRoot()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string binaryPath = Path.Combine(root, "bin", "lmcontroller.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(binaryPath));
                File.WriteAllBytes(binaryPath, Encoding.ASCII.GetBytes("trusted-controller"));
                ControllerCapabilityProfile profile = CreateTestCapabilityProfile(root, binaryPath, null);
                FakePathSafety safePaths = new FakePathSafety(true);
                FakeFileTrustVerifier trust = new FakeFileTrustVerifier(profile.ControllerBinary, true);

                OfficialControllerLocator locator = new OfficialControllerLocator(profile, trust, safePaths);
                VerifiedControllerBinaryResult result = locator.ResolveVerifiedBinary();

                AssertTrue(result.IsSuccess, result.ErrorMessage);
                AssertEqual(Path.GetFullPath(binaryPath), result.Binary.FullPath,
                    "Controller must resolve only under the fixed allowed root.");

                profile.ControllerRelativePath = @"..\outside\lmcontroller.exe";
                VerifiedControllerBinaryResult escaped =
                    new OfficialControllerLocator(profile, trust, safePaths).ResolveVerifiedBinary();
                AssertFalse(escaped.IsSuccess, "A relative path escaping the allowed root must fail closed.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void OfficialControllerLocatorEnforcesFullProductTrust()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string binaryPath = Path.Combine(root, "bin", "lmcontroller.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(binaryPath));
                File.WriteAllBytes(binaryPath, Encoding.ASCII.GetBytes("trusted-controller"));
                ControllerCapabilityProfile profile = CreateTestCapabilityProfile(root, binaryPath, null);
                FakeFileTrustVerifier rejectedTrust = new FakeFileTrustVerifier(
                    profile.ControllerBinary,
                    false,
                    "signer mismatch");

                VerifiedControllerBinaryResult rejected = new OfficialControllerLocator(
                    profile,
                    rejectedTrust,
                    new FakePathSafety(true)).ResolveVerifiedBinary();

                AssertFalse(rejected.IsSuccess, "A path match without full product trust must be rejected.");
                AssertContains(rejected.ErrorMessage, "signer mismatch");
                AssertEqual(PeMachine.Amd64, rejectedTrust.LastExpectation.Machine,
                    "The exact characterized PE architecture must reach the trust boundary.");
                AssertEqual("CN=JSC ESP", rejectedTrust.LastExpectation.SignerSubject,
                    "The exact signer must reach the trust boundary.");
                AssertTrue(rejectedTrust.LastExpectation.RequireCodeSigningEku,
                    "A generic certificate is not sufficient for executable trust.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void OfficialInstallerVerifierLocksVerifiesAndStagesAtomically()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string source = Path.Combine(root, "source", "esm-lm-controller_1.6.3.2-windows-setup.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(source));
                byte[] content = Encoding.UTF8.GetBytes("official-installer-content");
                File.WriteAllBytes(source, content);
                string stagingRoot = Path.Combine(root, "staging");
                ControllerCapabilityProfile profile = CreateTestCapabilityProfile(root, null, source);
                FakeFileTrustVerifier trust = new FakeFileTrustVerifier(profile.Installer, true);
                LmControllerInstallerSelection selection = CreateSelectionFromExpectation(source, profile.Installer);
                OfficialControllerInstallerVerifier verifier = new OfficialControllerInstallerVerifier(
                    profile,
                    trust,
                    new FakePathSafety(true),
                    stagingRoot,
                    Guid.NewGuid().ToString("N"));

                string stagedPath;
                using (LockedInstallerArtifact artifact = verifier.VerifyStageAndLock(selection))
                {
                    stagedPath = artifact.FullPath;
                    AssertTrue(File.Exists(stagedPath), "Expected an administrator-owned staged copy.");
                    AssertFalse(string.Equals(source, stagedPath, StringComparison.OrdinalIgnoreCase),
                        "Installer must never execute directly from the selected source path.");
                    AssertEqual(Convert.ToBase64String(content), Convert.ToBase64String(File.ReadAllBytes(stagedPath)),
                        "Staging must copy the exact locked bytes.");

                    bool sourceLocked = false;
                    try
                    {
                        using (FileStream ignored = new FileStream(
                            source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        {
                        }
                    }
                    catch (IOException)
                    {
                        sourceLocked = true;
                    }
                    AssertTrue(sourceLocked, "Source must remain locked against write/delete through staging.");
                }

                using (FileStream writable = new FileStream(
                    source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    AssertTrue(writable.CanWrite, "Disposing the artifact must release the source lock.");
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void OfficialInstallerVerifierRejectsFilenameSignerVersionOrHashMismatch()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string source = Path.Combine(root, "esm-lm-controller_1.6.3.2-windows-setup.exe");
                File.WriteAllBytes(source, Encoding.UTF8.GetBytes("official-installer-content"));
                ControllerCapabilityProfile profile = CreateTestCapabilityProfile(root, null, source);
                FakeFileTrustVerifier trust = new FakeFileTrustVerifier(profile.Installer, true);
                OfficialControllerInstallerVerifier verifier = new OfficialControllerInstallerVerifier(
                    profile,
                    trust,
                    new FakePathSafety(true),
                    Path.Combine(root, "staging"),
                    Guid.NewGuid().ToString("N"));

                LmControllerInstallerSelection wrongName = CreateSelectionFromExpectation(source, profile.Installer);
                wrongName.FileName = "controller-setup.exe";
                AssertThrows<InvalidDataException>(delegate { verifier.VerifyStageAndLock(wrongName); },
                    "Filename mismatch must fail before staging.");

                LmControllerInstallerSelection wrongSigner = CreateSelectionFromExpectation(source, profile.Installer);
                wrongSigner.SignerThumbprint = new string('f', 40);
                AssertThrows<InvalidDataException>(delegate { verifier.VerifyStageAndLock(wrongSigner); },
                    "Signer mismatch must fail before staging.");

                LmControllerInstallerSelection wrongVersion = CreateSelectionFromExpectation(source, profile.Installer);
                wrongVersion.FileVersion = "9.9.9.9";
                AssertThrows<InvalidDataException>(delegate { verifier.VerifyStageAndLock(wrongVersion); },
                    "Version mismatch must fail before staging.");

                LmControllerInstallerSelection wrongHash = CreateSelectionFromExpectation(source, profile.Installer);
                wrongHash.Sha256 = new string('e', 64);
                AssertThrows<InvalidDataException>(delegate { verifier.VerifyStageAndLock(wrongHash); },
                    "Hash mismatch must fail before staging.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void SupportedInstallerUsesFixedNsisSilentSwitch()
        {
            ControllerCapabilityProfile profile =
                ControllerCapabilityProfile.SupportedVersion1632();

            AssertEqual("/S", profile.InstallerArguments,
                "The verified NSIS package must use its case-sensitive silent switch.");
            AssertEqual(CapabilityFactProvenance.OfficialPackage,
                profile.Provenance["InstallerArguments"],
                "Installer arguments must come from the fixed capability profile, never from UI input.");
        }

        private static void WinTrustMarshalsActionGuidAsOneNativePointer()
        {
            MethodInfo nativeMethod = typeof(WinTrustVerifier).GetMethod(
                "WinVerifyTrust",
                BindingFlags.NonPublic | BindingFlags.Static);

            AssertTrue(nativeMethod != null, "The WinTrust native boundary must be present.");
            ParameterInfo actionId = nativeMethod.GetParameters()[1];
            AssertEqual(typeof(Guid).MakeByRefType(), actionId.ParameterType,
                "The action GUID must be passed as exactly one native GUID pointer.");
            AssertEqual(0, actionId.GetCustomAttributes(typeof(MarshalAsAttribute), false).Length,
                "LPStruct on a ref GUID adds incompatible marshalling and yields TRUST_E_PROVIDER_UNKNOWN.");
        }

        private static void ManifestPathIsDerivedOnlyFromKktSerial()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                ManagedServiceManifestStore store = new ManagedServiceManifestStore(
                    root,
                    new FakePathSafety(true),
                    "S-1-5-21-111-222-333-1001");
                string serviceName = LmServiceIdentity.CreateName("00105700000001");

                string manifestPath = store.GetManifestPath("00105700000001");
                string profilePath = store.GetProfileRoot("00105700000001");

                AssertContains(manifestPath, Path.Combine("Inventory", serviceName, "manifest.json"));
                AssertContains(profilePath, Path.Combine("Profiles", serviceName));
                AssertFalse(manifestPath.IndexOf("00105700000002", StringComparison.Ordinal) >= 0,
                    "No caller-provided path may influence another KKT identity.");
                AssertThrows<ArgumentException>(delegate { store.GetManifestPath(@"..\outside"); },
                    "Unsafe serial must never become a path component.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void ManifestAndProfileStoresRejectReparsePoints()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                FakePathSafety unsafePaths = new FakePathSafety(false);
                ManagedServiceManifestStore store = new ManagedServiceManifestStore(
                    root,
                    unsafePaths,
                    "S-1-5-21-111-222-333-1001");
                ManagedServiceManifest manifest = CreateTestManifest(ManagedServiceLifecycleState.ServiceReady);

                AssertThrows<InvalidDataException>(delegate { store.Write(manifest); },
                    "Manifest store must fail before traversing a reparse path.");
                AssertThrows<InvalidDataException>(delegate {
                    store.EnsureProfileRoot(manifest.KktSerial, manifest.ServiceSid);
                }, "Profile store must fail before traversing a reparse path.");

                DirectorySecurity protectedAcl = new DirectorySecurity();
                protectedAcl.SetOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
                protectedAcl.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));
                protectedAcl.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));
                AssertTrue(PathSafety.IsSecurityProtected(protectedAcl, null),
                    "SYSTEM/Administrators-only DACL must be accepted.");

                protectedAcl.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.ReadAndExecute,
                    AccessControlType.Allow));
                AssertTrue(PathSafety.IsSecurityProtected(protectedAcl, null),
                    "An unprivileged read-and-execute ACE must be accepted.");

                protectedAcl.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.Modify,
                    AccessControlType.Allow));
                AssertFalse(PathSafety.IsSecurityProtected(protectedAcl, null),
                    "An unprivileged write ACE must fail closed.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void ManifestIsAtomicCredentialFreeAndProjectsCleanupState()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                ManagedServiceManifestStore store = new ManagedServiceManifestStore(
                    root,
                    new FakePathSafety(true),
                    "S-1-5-21-111-222-333-1001");
                ManagedServiceManifest manifest = CreateTestManifest(ManagedServiceLifecycleState.CleanupPending);

                store.Write(manifest);
                ManagedServiceManifest loaded = store.Read(manifest.KktSerial);
                LmServiceInventoryItem projection = store.ReadProjection(manifest.KktSerial);
                string json = File.ReadAllText(store.GetManifestPath(manifest.KktSerial), Encoding.UTF8);

                AssertEqual(manifest.ServiceName, loaded.ServiceName, "Expected the owned manifest to round-trip.");
                AssertEqual(LmServiceProvisioningStatus.CleanupPending, projection.Status,
                    "CleanupPending must remain visible after an app restart.");
                AssertTrue(projection.ManifestFingerprint != null &&
                           projection.ManifestFingerprint.Sha256.Length == 64,
                    "Inventory projection must carry the observed immutable fingerprint.");
                AssertFalse(json.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            json.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            json.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Manifest must remain credential-free.");
                AssertEqual(0, Directory.GetFiles(
                    Path.GetDirectoryName(store.GetManifestPath(manifest.KktSerial)), "*.tmp").Length,
                    "Atomic write must not leave a temporary file after success.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void ManifestOwnershipMismatchBlocksMutation()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                ManagedServiceManifestStore store = new ManagedServiceManifestStore(
                    root,
                    new FakePathSafety(true),
                    "S-1-5-21-111-222-333-1001");
                ManagedServiceManifest manifest = CreateTestManifest(ManagedServiceLifecycleState.ServiceReady);
                store.Write(manifest);
                string path = store.GetManifestPath(manifest.KktSerial);
                string tampered = File.ReadAllText(path, Encoding.UTF8).Replace(
                    manifest.ServiceName,
                    "foreign-service");
                File.WriteAllText(path, tampered, new UTF8Encoding(false));

                AssertThrows<InvalidDataException>(delegate { store.Write(manifest); },
                    "An existing ownership mismatch must block overwrite.");
                AssertThrows<InvalidDataException>(delegate { store.Delete(manifest.KktSerial); },
                    "An existing ownership mismatch must block deletion.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void ScmAdapterDerivesServiceNameInternally()
        {
            FakeWindowsServiceApi api = new FakeWindowsServiceApi();
            LmGatewaySupervisorService service = CreateTestSupervisorService(api);

            service.EnsureConfigured("00105700000001");

            AssertEqual("krs-esm-lm-00105700000001", api.LastDefinition.ServiceName,
                "SCM identity must be derived only from the validated KKT serial.");
            AssertThrows<ArgumentException>(delegate { service.EnsureConfigured("foreign-service"); },
                "A caller-supplied service name must not cross the adapter boundary.");
        }

        private static void ScmAdapterUsesExactVerifiedImagePath()
        {
            FakeWindowsServiceApi api = new FakeWindowsServiceApi();
            string expectedPath = @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe";
            LmGatewaySupervisorService service = new LmGatewaySupervisorService(
                api,
                ControllerCapabilityProfile.SupportedVersion1632(),
                VerifiedProvisionerBinary.CreateForTesting(expectedPath));

            service.EnsureConfigured("00105700000001");

            AssertEqual(
                WindowsCommandLine.QuoteArgument(expectedPath) +
                    " --supervise krs-esm-lm-00105700000001",
                api.LastDefinition.ImagePath,
                "CreateService must receive the exact verified provisioner image.");
            AssertEqual(@"""C:\tail\\""", WindowsCommandLine.QuoteArgument(@"C:\tail\"),
                "Windows quoting must preserve a trailing backslash before the closing quote.");
        }

        private static void ScmAdapterEnforcesRestrictiveServiceDacl()
        {
            FakeWindowsServiceApi api = new FakeWindowsServiceApi();
            string operatorSid = "S-1-5-21-111-222-333-1001";
            CreateTestSupervisorService(api).EnsureConfigured("00105700000001", operatorSid);

            AssertTrue(api.LastDefinition.SecurityDescriptor.IsRestrictive,
                "Managed service DACL must contain only the fixed privileged trustees and rights.");
            AssertEqual(operatorSid, api.LastDefinition.SecurityDescriptor.OperatorSid,
                "The initiating user may receive only the typed read-only service projection.");
            RawSecurityDescriptor raw = new RawSecurityDescriptor(
                api.LastDefinition.SecurityDescriptor.Sddl);
            int forbiddenOperatorRights = 0x00000002 | 0x00000010 | 0x00000020 |
                0x00010000 | 0x00040000;
            for (int index = 0; index < raw.DiscretionaryAcl.Count; index++)
            {
                CommonAce ace = raw.DiscretionaryAcl[index] as CommonAce;
                if (ace != null && ace.SecurityIdentifier != null &&
                    string.Equals(ace.SecurityIdentifier.Value, operatorSid, StringComparison.Ordinal))
                {
                    AssertEqual(0, ace.AccessMask & forbiddenOperatorRights,
                        "The operator SID must not change, start, stop or delete the service.");
                }
            }
            AssertThrows<ArgumentException>(delegate {
                ServiceSecurityDescriptor.CreateRestrictive("S-1-5-11");
            }, "Authenticated Users must never become the service operator trustee.");
            AssertFalse(api.LastDefinition.SecurityDescriptor.Sddl.IndexOf(";;;AU)", StringComparison.Ordinal) >= 0 ||
                        api.LastDefinition.SecurityDescriptor.Sddl.IndexOf(";;;BU)", StringComparison.Ordinal) >= 0 ||
                        api.LastDefinition.SecurityDescriptor.Sddl.IndexOf(";;;IU)", StringComparison.Ordinal) >= 0 ||
                        api.LastDefinition.SecurityDescriptor.Sddl.IndexOf(";;;WD)", StringComparison.Ordinal) >= 0,
                "Interactive or broad user trustees must not control the service.");
        }

        private static void ScmAdapterNeverForceKillsProcess()
        {
            string sourceRoot = Path.Combine("src", "EsmTspiot.ServiceProvisioner");
            string[] files = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);
            string[] forbidden = { "taskkill", "Kill(", "sc.exe", "powershell", "cmd.exe", "TerminateProcess" };
            for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                string source = File.ReadAllText(files[fileIndex]);
                for (int tokenIndex = 0; tokenIndex < forbidden.Length; tokenIndex++)
                {
                    AssertFalse(source.IndexOf(forbidden[tokenIndex], StringComparison.OrdinalIgnoreCase) >= 0,
                        "Forced termination or shell token found in " + files[fileIndex] + ".");
                }
            }
        }

        private static void ScmHandlesAreDisposedOnEveryFailure()
        {
            bool released = false;
            try
            {
                using (SafeServiceHandle handle = SafeServiceHandle.CreateForTesting(
                    new IntPtr(123),
                    delegate { released = true; return true; }))
                {
                    throw new InvalidOperationException("injected failure");
                }
            }
            catch (InvalidOperationException)
            {
            }

            AssertTrue(released, "Safe SCM handles must close during exception unwinding.");
        }

        private static void ScmConfiguresRestrictedServiceSid()
        {
            FakeWindowsServiceApi api = new FakeWindowsServiceApi();
            CreateTestSupervisorService(api).EnsureConfigured("00105700000001");

            AssertEqual(WindowsServiceSidType.Restricted, api.LastDefinition.ServiceSidType,
                "Every managed service must use SERVICE_SID_TYPE_RESTRICTED.");
            AssertTrue(api.LastDefinition.RecoveryPolicy != null &&
                       api.LastDefinition.RecoveryPolicy.RestartDelaysMilliseconds.Count == 3,
                "The exact characterized recovery policy must accompany creation.");
            AssertEqual(
                "S-1-5-80-1562836834-2535112711-1761592931-1687635663-3051341797",
                RestrictedServiceSid.Derive("krs-esm-lm-00105700000001"),
                "Service SID derivation must match the Windows service-SID algorithm before creation.");
        }

        private static void ScmImagePathTargetsOnlyProtectedSupervisorMode()
        {
            FakeWindowsServiceApi api = new FakeWindowsServiceApi();
            CreateTestSupervisorService(api).EnsureConfigured("00105700000001");
            ProvisionerCommandLine parsed;

            AssertTrue(ProvisionerCommandLine.TryParse(
                new[] { "--supervise", api.LastDefinition.ServiceName }, out parsed),
                "The internally generated supervisor mode must parse.");
            AssertEqual(ProvisionerMode.Supervisor, parsed.Mode,
                "The managed ImagePath must select only supervisor mode.");
            AssertFalse(ProvisionerCommandLine.TryParse(
                new[] { "--supervise", api.LastDefinition.ServiceName, "--environment", "x" }, out parsed),
                "Supervisor mode must reject appended arguments.");
            AssertFalse(api.LastDefinition.ImagePath.IndexOf("--pipe", StringComparison.Ordinal) >= 0,
                "SCM must not target the elevated IPC mode.");
        }

        private static void SupervisorReplacesOnlyChildProgramData()
        {
            Dictionary<string, string> baseline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Path", @"C:\Windows\System32" },
                { "TEMP", @"C:\Windows\Temp" },
                { "ProgramData", @"C:\ProgramData" }
            };
            FakeControllerChildRuntime runtime = new FakeControllerChildRuntime();
            LmControllerChildProcess child = CreateTestChildProcess(baseline, runtime);

            child.Start();

            AssertEqual(@"C:\ProgramData\KRS\MultiKKT\Profiles\krs-esm-lm-00105700000001",
                runtime.LastPlan.Environment["ProgramData"],
                "Only the process-local ProgramData root may select the profile.");
            AssertEqual(baseline["Path"], runtime.LastPlan.Environment["Path"],
                "PATH must be inherited unchanged.");
            AssertEqual(baseline["TEMP"], runtime.LastPlan.Environment["TEMP"],
                "TEMP must be inherited unchanged.");
            AssertEqual(3, runtime.LastPlan.Environment.Count,
                "The supervisor must not add arbitrary environment variables.");
        }

        private static void SupervisorRejectsCallerSuppliedEnvironmentAndArguments()
        {
            FakeControllerChildRuntime runtime = new FakeControllerChildRuntime();
            LmControllerChildProcess child = CreateTestChildProcess(
                new Dictionary<string, string> { { "ProgramData", @"C:\ProgramData" } },
                runtime);

            child.Start();

            AssertEqual(string.Empty, runtime.LastPlan.Arguments,
                "The exact-version terminal contract takes no caller arguments.");
            MethodInfo[] methods = typeof(LmControllerChildProcess).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                if (methods[index].Name == "Start" || methods[index].Name == "StopGracefully")
                {
                    AssertEqual(0, methods[index].GetParameters().Length,
                        "Runtime methods must not accept caller environment or arguments.");
                }
            }
        }

        private static void SupervisorStopsChildGracefullyWithoutProcessKill()
        {
            FakeControllerChildRuntime runtime = new FakeControllerChildRuntime();
            LmControllerChildProcess child = CreateTestChildProcess(
                new Dictionary<string, string> { { "ProgramData", @"C:\ProgramData" } },
                runtime);

            int processId = child.Start();
            bool stopped = child.StopGracefully();

            AssertTrue(stopped, "A graceful console stop followed by bounded wait must succeed.");
            AssertEqual(processId, runtime.GracefulStopProcessId,
                "The supervisor must signal only its own verified child PID.");
            AssertEqual(processId, runtime.WaitProcessId,
                "The supervisor must wait for the same child PID.");
            AssertTrue(runtime.WaitMilliseconds > 0 && runtime.WaitMilliseconds <= 60000,
                "Graceful stop wait must be bounded.");
        }

        private static void ProvisionerAcceptsOnlyOwnedLocalModuleServiceMode()
        {
            string instanceId = "lmi-0123456789abcdef01234567";
            string databaseService =
                LocalModuleManagedIdentity.CreateDatabaseServiceName(instanceId);
            ProvisionerCommandLine parsed;

            AssertTrue(ProvisionerCommandLine.TryParse(
                    new[] { "--supervise-local-module", databaseService },
                    out parsed),
                "The helper must accept its exact manifest-owned local-module service name.");
            AssertEqual(ProvisionerMode.LocalModuleSupervisor, parsed.Mode,
                "The local-module service switch must select only the dedicated host mode.");
            AssertEqual(databaseService, parsed.ServiceName,
                "The service identity must round-trip without caller-controlled normalization.");
            AssertFalse(ProvisionerCommandLine.TryParse(
                    new[] { "--supervise-local-module", databaseService, "--config", @"C:\caller" },
                    out parsed),
                "A caller must not append a config path or any other start token.");
            AssertFalse(ProvisionerCommandLine.TryParse(
                    new[] { "--supervise-local-module", databaseService.ToUpperInvariant() },
                    out parsed),
                "Non-canonical service spelling must fail closed.");
            AssertFalse(ProvisionerCommandLine.TryParse(
                    new[] { "--supervise-local-module", LmServiceIdentity.CreateName("00105700000001") },
                    out parsed),
                "The local-module switch must never accept a controller service.");
        }

        private static void LocalModuleScmCreatesExactRestrictedDependencyPair()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore store;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    out store,
                    out runtime,
                    out instance);
                FakeWindowsServiceCollectionApi api =
                    new FakeWindowsServiceCollectionApi();
                LocalModuleWindowsServicePair pair =
                    new LocalModuleWindowsServicePair(
                        api,
                        VerifiedProvisionerBinary.CreateForTesting(
                            @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe"));

                pair.EnsureConfigured(
                    instance,
                    "S-1-5-21-111-222-333-1001");

                AssertEqual(2, api.CreatedDefinitions.Count,
                    "One INN must produce exactly one database and one API service.");
                WindowsServiceDefinition database = api.CreatedDefinitions[0];
                WindowsServiceDefinition apiService = api.CreatedDefinitions[1];
                AssertEqual(instance.DatabaseServiceName, database.ServiceName,
                    "The database service must be created first from the manifest identity.");
                AssertEqual(0, database.Dependencies.Count,
                    "The database service must have no local-module service dependency.");
                AssertEqual(instance.ApiServiceName, apiService.ServiceName,
                    "The API service must be created second from the manifest identity.");
                AssertEqual(1, apiService.Dependencies.Count,
                    "The API service must depend on exactly the database service.");
                AssertEqual(instance.DatabaseServiceName, apiService.Dependencies[0],
                    "SCM must enforce database-before-API ordering.");
                AssertContains(database.ImagePath,
                    "--supervise-local-module " + instance.DatabaseServiceName);
                AssertContains(apiService.ImagePath,
                    "--supervise-local-module " + instance.ApiServiceName);
                AssertEqual(WindowsServiceSidType.Restricted, database.ServiceSidType,
                    "Database service must use a restricted service SID.");
                AssertEqual(WindowsServiceSidType.Restricted, apiService.ServiceSidType,
                    "API service must use a restricted service SID.");
                AssertTrue(database.SecurityDescriptor.IsRestrictive &&
                           apiService.SecurityDescriptor.IsRestrictive,
                    "Both services must retain the restrictive DACL contract.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void LocalModuleChildPlanIsRebuiltOnlyFromManifest()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore store;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    out store,
                    out runtime,
                    out instance);
                LocalModuleCapabilityProfile capability =
                    LocalModuleCapabilityProfile.Resolve("2.6.1");

                ErlangChildStartPlan apiPlan =
                    LocalModuleConfigurationWriter.RebuildStartPlan(
                        capability,
                        instance,
                        LocalModuleProcessRole.Api);
                ErlangChildStartPlan databasePlan =
                    LocalModuleConfigurationWriter.RebuildStartPlan(
                        capability,
                        instance,
                        LocalModuleProcessRole.Database);

                AssertEqual(8, apiPlan.ArgumentTokens.Count,
                    "The API command must contain only the fixed capability token set.");
                AssertEqual("-args_file", apiPlan.ArgumentTokens[2],
                    "The fixed command must select its manifest-derived vm.args file.");
                AssertEqual(Path.Combine(instance.ConfigRoot, "regime-vm.args"),
                    apiPlan.ArgumentTokens[3],
                    "API vm.args path must be derived from the owned instance root.");
                AssertEqual(Path.Combine(instance.ConfigRoot, "yenisei-sys.config"),
                    databasePlan.ArgumentTokens[7],
                    "Database sys.config path must be derived from the owned instance root.");
                AssertEqual(instance.EpmdPort.ToString(),
                    apiPlan.Environment["ERL_EPMD_PORT"],
                    "Every child plan must carry its own explicit EPMD port.");
                MethodInfo method = typeof(LocalModuleConfigurationWriter).GetMethod(
                    "RebuildStartPlan",
                    BindingFlags.Static | BindingFlags.NonPublic);
                AssertEqual(3, method.GetParameters().Length,
                    "The service host must not accept caller command, arguments or environment.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void LocalModuleOwnershipDistinguishesSharedErlangChildren()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore store;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    out store,
                    out runtime,
                    out instance);
                LocalModuleCapabilityProfile capability =
                    LocalModuleCapabilityProfile.Resolve("2.6.1");
                VerifiedProvisionerBinary supervisor =
                    VerifiedProvisionerBinary.CreateForTesting(
                        @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe");
                LocalModuleWindowsServicePair pair =
                    new LocalModuleWindowsServicePair(
                        new FakeWindowsServiceCollectionApi(),
                        supervisor);
                WindowsServiceDefinition definition = pair.BuildDefinition(
                    instance,
                    LocalModuleProcessRole.Api,
                    "S-1-5-21-111-222-333-1001");
                WindowsServiceRecord service =
                    WindowsServiceRecord.FromDefinition(definition);
                service.State = WindowsServiceState.Running;
                service.ProcessId = 5001;
                ErlangChildStartPlan plan =
                    LocalModuleConfigurationWriter.RebuildStartPlan(
                        capability,
                        instance,
                        LocalModuleProcessRole.Api);
                LocalModuleProcessObservation observation =
                    LocalModuleProcessObservation.CreateForTesting(
                        service,
                        6001,
                        5001,
                        plan.ExecutablePath,
                        FindRequiredFileHash(capability, capability.ErlExecutableRelativePath),
                        plan.ArgumentTokens,
                        plan.Environment,
                        instance.OwnershipNonce,
                        new[] { 6001 });
                LocalModuleServiceOwnershipVerifier verifier =
                    new LocalModuleServiceOwnershipVerifier(capability);

                AssertTrue(verifier.Verify(
                        instance,
                        LocalModuleProcessRole.Api,
                        definition,
                        observation).IsValid,
                    "The exact service, parent, config, environment, nonce and listener must pass.");

                List<string> anotherInstanceTokens =
                    new List<string>(plan.ArgumentTokens);
                anotherInstanceTokens[3] =
                    @"C:\ProgramData\KRS\MultiKKT\LocalModules\lmi-ffffffffffffffffffffffff\config\regime-vm.args";
                LocalModuleProcessObservation wrongInstance =
                    LocalModuleProcessObservation.CreateForTesting(
                        service,
                        6002,
                        5001,
                        plan.ExecutablePath,
                        FindRequiredFileHash(capability, capability.ErlExecutableRelativePath),
                        anotherInstanceTokens,
                        plan.Environment,
                        instance.OwnershipNonce,
                        new[] { 6002 });
                AssertFalse(verifier.Verify(
                        instance,
                        LocalModuleProcessRole.Api,
                        definition,
                        wrongInstance).IsValid,
                    "The same shared erl.exe must not authorize another instance config path.");

                LocalModuleProcessObservation wrongParent =
                    LocalModuleProcessObservation.CreateForTesting(
                        service,
                        6001,
                        9999,
                        plan.ExecutablePath,
                        FindRequiredFileHash(capability, capability.ErlExecutableRelativePath),
                        plan.ArgumentTokens,
                        plan.Environment,
                        instance.OwnershipNonce,
                        new[] { 6001 });
                AssertFalse(verifier.Verify(
                        instance,
                        LocalModuleProcessRole.Api,
                        definition,
                        wrongParent).IsValid,
                    "A matching erl.exe outside the exact service process tree must fail.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void EpmdControllerUsesExplicitPortAndBlocksLiveNodeKill()
        {
            LocalModuleCapabilityProfile capability =
                LocalModuleCapabilityProfile.Resolve("2.6.1");
            FakeEpmdCommandRunner liveRunner = new FakeEpmdCommandRunner(
                new EpmdCommandResult(0, "epmd: up and running\nname krs_lm_regime_n01 at port 51001\n", string.Empty));
            EpmdInstanceController liveController = new EpmdInstanceController(
                capability,
                @"C:\Program Files\KRS\MultiKKT\LocalModuleRuntime\lmrt-0123456789abcdef01234567",
                @"C:\ProgramData\KRS\MultiKKT\LocalModules\lm-n01\data\temp",
                43691,
                liveRunner);

            EpmdShutdownResult blocked = liveController.StopIfUnused();

            AssertEqual(EpmdShutdownOutcome.LiveNodes, blocked.Outcome,
                "EPMD shutdown must be blocked while any node remains registered.");
            AssertEqual(1, liveRunner.Plans.Count,
                "Blocked shutdown must not execute epmd -kill.");
            AssertEqual("-names", liveRunner.Plans[0].ArgumentTokens[0],
                "The first command must inspect this instance only.");
            AssertEqual("-port", liveRunner.Plans[0].ArgumentTokens[1],
                "Every EPMD command must carry an explicit -port token.");
            AssertEqual("43691", liveRunner.Plans[0].ArgumentTokens[2],
                "The inspected port must be the manifest-owned EPMD port.");
            AssertEqual("43691", liveRunner.Plans[0].Environment["ERL_EPMD_PORT"],
                "The command environment must repeat the same isolated EPMD port.");

            FakeEpmdCommandRunner emptyRunner = new FakeEpmdCommandRunner(
                new EpmdCommandResult(0, "epmd: up and running\n", string.Empty),
                new EpmdCommandResult(0, "Killed\n", string.Empty));
            EpmdShutdownResult stopped = new EpmdInstanceController(
                capability,
                @"C:\Program Files\KRS\MultiKKT\LocalModuleRuntime\lmrt-0123456789abcdef01234567",
                @"C:\ProgramData\KRS\MultiKKT\LocalModules\lm-n01\data\temp",
                43691,
                emptyRunner).StopIfUnused();

            AssertEqual(EpmdShutdownOutcome.Stopped, stopped.Outcome,
                "An empty instance-specific EPMD may be stopped gracefully.");
            AssertEqual(2, emptyRunner.Plans.Count,
                "An empty EPMD requires one inspection and one exact kill command.");
            AssertEqual("-kill", emptyRunner.Plans[1].ArgumentTokens[0],
                "Only the empty instance may receive epmd -kill.");
            AssertEqual("-port", emptyRunner.Plans[1].ArgumentTokens[1],
                "The kill command must also include an explicit -port token.");
            AssertFalse(emptyRunner.Plans[1].Environment.ContainsKey(
                    "ERL_EPMD_RELAXED_COMMAND_CHECK"),
                "Relaxed EPMD command checks are forbidden.");
        }

        private static void LocalModuleLifecycleOrdersDatabaseApiAndEpmd()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore store;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    out store,
                    out runtime,
                    out instance);
                List<string> events = new List<string>();
                FakeWindowsServiceCollectionApi services =
                    new FakeWindowsServiceCollectionApi(events);
                LocalModuleWindowsServicePair pair =
                    new LocalModuleWindowsServicePair(
                        services,
                        VerifiedProvisionerBinary.CreateForTesting(
                            @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe"));
                pair.EnsureConfigured(instance, null);
                events.Clear();
                FakeLocalModuleServiceReadinessProbe readiness =
                    new FakeLocalModuleServiceReadinessProbe(events);
                RecordingEpmdCommandRunner epmdRunner =
                    new RecordingEpmdCommandRunner(
                        events,
                        new EpmdCommandResult(0, "epmd: up and running\n", string.Empty),
                        new EpmdCommandResult(0, "Killed\n", string.Empty));
                EpmdInstanceController epmd = new EpmdInstanceController(
                    LocalModuleCapabilityProfile.Resolve("2.6.1"),
                    instance.RuntimeRoot,
                    Path.Combine(instance.DataRoot, "temp"),
                    instance.EpmdPort,
                    epmdRunner);
                LocalModuleServicePairLifecycle lifecycle =
                    new LocalModuleServicePairLifecycle(
                        services,
                        readiness,
                        epmd);

                lifecycle.Start(instance);
                LocalModuleServicePairStopOutcome outcome =
                    lifecycle.Stop(instance);

                AssertEqual(LocalModuleServicePairStopOutcome.Stopped, outcome,
                    "The exact pair and its empty EPMD must stop cleanly.");
                string[] expected =
                {
                    "start:" + instance.DatabaseServiceName,
                    "ready:Database",
                    "start:" + instance.ApiServiceName,
                    "ready:Api",
                    "stop:" + instance.ApiServiceName,
                    "stopped:Api",
                    "stop:" + instance.DatabaseServiceName,
                    "stopped:Database",
                    "epmd:-names",
                    "epmd:-kill"
                };
                AssertEqual(expected.Length, events.Count,
                    "The lifecycle must expose one deterministic operation sequence.");
                for (int index = 0; index < expected.Length; index++)
                {
                    AssertEqual(expected[index], events[index],
                        "Unexpected lifecycle event at index " + index.ToString() + ".");
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void RestrictedServiceSidSupportsLocalModulePair()
        {
            string instanceId = "lmi-0123456789abcdef01234567";
            string database = LocalModuleManagedIdentity.CreateDatabaseServiceName(
                instanceId);
            string api = LocalModuleManagedIdentity.CreateApiServiceName(instanceId);

            string databaseSid = RestrictedServiceSid.Derive(database);
            string apiSid = RestrictedServiceSid.Derive(api);

            AssertTrue(databaseSid.StartsWith("S-1-5-80-", StringComparison.Ordinal),
                "The database service must receive a deterministic service SID.");
            AssertTrue(apiSid.StartsWith("S-1-5-80-", StringComparison.Ordinal),
                "The API service must receive a deterministic service SID.");
            AssertFalse(string.Equals(databaseSid, apiSid, StringComparison.Ordinal),
                "The two restricted services must never share one service SID.");
        }

        private static void ProtectedAclAcceptsExactlyTwoLocalModuleWriters()
        {
            string databaseSid = RestrictedServiceSid.Derive(
                "krs-lm-db-0123456789abcdef01234567");
            string apiSid = RestrictedServiceSid.Derive(
                "krs-lm-api-0123456789abcdef01234567");
            DirectorySecurity security = new DirectorySecurity();
            SecurityIdentifier administrators = new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid,
                null);
            SecurityIdentifier system = new SecurityIdentifier(
                WellKnownSidType.LocalSystemSid,
                null);
            security.SetOwner(administrators);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                administrators,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                system,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(databaseSid),
                FileSystemRights.Modify,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(apiSid),
                FileSystemRights.Modify,
                AccessControlType.Allow));

            AssertTrue(PathSafety.IsSecurityProtectedForServices(
                    security,
                    new[] { databaseSid, apiSid }),
                "Only the exact DB/API service pair may write its mutable profile.");
            AssertFalse(PathSafety.IsSecurityProtectedForServices(
                    security,
                    new[] { databaseSid }),
                "Omitting either actual writer from the allow-list must fail closed.");
        }

        private static void LocalModuleInventoryResolvesOneInstancePerInn()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore store;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    out store,
                    out runtime,
                    out instance);
                LocalModuleInstanceManifest found;

                AssertTrue(store.TryFindInstanceByInn(instance.Inn, out found),
                    "The exact owned INN instance must be discoverable after restart.");
                AssertEqual(instance.InstanceId, found.InstanceId,
                    "INN lookup must return the manifest-owned instance identity.");

                ManagedLocalModuleProvisioningItemRequest item =
                    CreateManagedLocalModuleRequest(1).ManagedLocalModules[0];
                string secondNonce = "99999999999999999999999999999999";
                string secondId = LocalModuleManagedIdentity.CreateInstanceId(
                    item.Inn,
                    secondNonce);
                LocalModuleConfiguration configuration =
                    LocalModuleConfigurationWriter.Build(
                        LocalModuleCapabilityProfile.Resolve("2.6.1"),
                        runtime.RuntimeRoot,
                        store.GetInstanceRoot(secondId),
                        item,
                        secondNonce,
                        CreateLocalModuleTemplateObservation());
                store.WriteInstance(LocalModuleInstanceManifest.Create(
                    item,
                    secondId,
                    runtime.RuntimeId,
                    runtime.RuntimeRoot,
                    configuration,
                    secondNonce,
                    "cccccccccccccccccccccccccccccccc"));

                AssertThrows<InvalidDataException>(delegate {
                    store.TryFindInstanceByInn(item.Inn, out found);
                }, "Two owned instance manifests for one INN must fail closed.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void ManagedLocalModuleProfileWritesProtectedSixFileContract()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore store;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    out store,
                    out runtime,
                    out instance);
                LocalModuleTemplateObservation templates =
                    CreateLocalModuleTemplateObservation();
                string regimeTemplate = Path.Combine(
                    runtime.RuntimeRoot,
                    @"regime\etc\local.ini.dist");
                string databaseTemplate = Path.Combine(
                    runtime.RuntimeRoot,
                    @"yenisei\etc\local.ini.dist");
                Directory.CreateDirectory(Path.GetDirectoryName(regimeTemplate));
                Directory.CreateDirectory(Path.GetDirectoryName(databaseTemplate));
                File.WriteAllText(
                    regimeTemplate,
                    templates.RegimeLocalIni,
                    new UTF8Encoding(false));
                File.WriteAllText(
                    databaseTemplate,
                    templates.YeniseiLocalIni,
                    new UTF8Encoding(false));
                ManagedLocalModuleProfileStore profiles =
                    new ManagedLocalModuleProfileStore(
                        store,
                        new FakePathSafety(true),
                        new AtomicFileWriter());
                ManagedLocalModuleProvisioningItemRequest item =
                    CreateManagedLocalModuleRequest(1).ManagedLocalModules[0];

                LocalModuleConfiguration configuration = profiles.WriteConfiguration(
                    runtime,
                    LocalModuleCapabilityProfile.Resolve("2.6.1"),
                    item,
                    instance.InstanceId,
                    instance.OwnershipNonce);
                LocalModuleInstanceManifest replacement =
                    LocalModuleInstanceManifest.Create(
                        item,
                        instance.InstanceId,
                        runtime.RuntimeId,
                        runtime.RuntimeRoot,
                        configuration,
                        instance.OwnershipNonce,
                        "dddddddddddddddddddddddddddddddd");
                store.WriteInstance(replacement);
                profiles.ProtectOwnedManifests(runtime, replacement);

                AssertTrue(profiles.ValidateConfiguration(
                        runtime,
                        LocalModuleCapabilityProfile.Resolve("2.6.1"),
                        replacement).IsValid,
                    "The exact six generated files and protected roots must validate.");
                AssertEqual(6, Directory.GetFiles(replacement.ConfigRoot).Length,
                    "One INN profile must contain exactly the six generated configuration files.");
                AssertTrue(Directory.Exists(Path.Combine(replacement.DataRoot, "database")) &&
                           Directory.Exists(Path.Combine(replacement.DataRoot, "index")) &&
                           Directory.Exists(Path.Combine(replacement.DataRoot, "key-store")) &&
                           Directory.Exists(Path.Combine(replacement.DataRoot, "temp")),
                    "Every mutable LM path must be created under its owned profile.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void ManagedLocalModuleLifecycleJournalResumesPreProfileIdentity()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                ManagedLocalModuleProvisioningItemRequest item =
                    CreateManagedLocalModuleRequest(1).ManagedLocalModules[0];
                string nonce = "abababababababababababababababab";
                string instanceId = LocalModuleManagedIdentity.CreateInstanceId(
                    item.Inn,
                    nonce);
                ManagedLocalModuleLifecycleJournalStore store =
                    new ManagedLocalModuleLifecycleJournalStore(
                        root,
                        new FakePathSafety(true));
                ManagedLocalModuleLifecycleJournal journal =
                    ManagedLocalModuleLifecycleJournal.Create(
                        item.Inn,
                        instanceId,
                        nonce,
                        "lmrt-0123456789abcdef01234567",
                        "local-module-2.6.1-7",
                        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        ManagedLocalModuleProvisioningStage.Created);
                store.Write(journal);
                journal.Stage = ManagedLocalModuleProvisioningStage.RuntimeReady;
                store.Write(journal);

                ManagedLocalModuleLifecycleJournal loaded;
                AssertTrue(store.TryRead(item.Inn, out loaded),
                    "A pre-profile operation identity must survive helper restart.");
                AssertEqual(instanceId, loaded.InstanceId,
                    "Retry must reuse the same instance id and ownership nonce.");
                AssertEqual(ManagedLocalModuleProvisioningStage.RuntimeReady, loaded.Stage,
                    "The durable lifecycle stage must advance monotonically inside one operation.");

                ManagedLocalModuleLifecycleJournal replacement =
                    ManagedLocalModuleLifecycleJournal.Create(
                        item.Inn,
                        LocalModuleManagedIdentity.CreateInstanceId(
                            item.Inn,
                            "cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd"),
                        "cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd",
                        journal.RuntimeId,
                        journal.CapabilityId,
                        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                        ManagedLocalModuleProvisioningStage.Created);
                AssertThrows<InvalidDataException>(delegate {
                    store.Write(replacement);
                }, "A retry must not replace the durable INN ownership identity.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void LocalModuleReadinessRequiresOwnedDescendantListener()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore store;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    out store,
                    out runtime,
                    out instance);
                FakeWindowsServiceCollectionApi services =
                    new FakeWindowsServiceCollectionApi();
                LocalModuleWindowsServicePair pair =
                    new LocalModuleWindowsServicePair(
                        services,
                        VerifiedProvisionerBinary.CreateForTesting(
                            @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe"));
                pair.EnsureConfigured(instance, null);
                WindowsServiceRecord api = services.Query(instance.ApiServiceName);
                api.State = WindowsServiceState.Running;
                api.ProcessId = 5001;
                FakeTcpListenerOwnerReader listeners =
                    new FakeTcpListenerOwnerReader(instance.ApiPort, 6001);
                FakeProcessParentReader parents =
                    new FakeProcessParentReader(6001, 5001);
                ManagedLocalModuleServiceReadinessProbe readiness =
                    new ManagedLocalModuleServiceReadinessProbe(
                        services,
                        listeners,
                        parents,
                        1,
                        delegate { });

                AssertTrue(readiness.ProbeOwnedListener(
                        instance,
                        LocalModuleProcessRole.Api).IsValid,
                    "A unique listener descended from the exact service supervisor must pass.");

                parents.ParentProcessId = 7001;
                AssertFalse(readiness.ProbeOwnedListener(
                        instance,
                        LocalModuleProcessRole.Api).IsValid,
                    "The same port owned outside the exact service process tree must fail.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void WindowsManagedPlatformPersistsProfileAndExactServicePair()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore store;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    out store,
                    out runtime,
                    out instance);
                LocalModuleTemplateObservation templates =
                    CreateLocalModuleTemplateObservation();
                string regimeTemplate = Path.Combine(
                    runtime.RuntimeRoot,
                    @"regime\etc\local.ini.dist");
                string databaseTemplate = Path.Combine(
                    runtime.RuntimeRoot,
                    @"yenisei\etc\local.ini.dist");
                Directory.CreateDirectory(Path.GetDirectoryName(regimeTemplate));
                Directory.CreateDirectory(Path.GetDirectoryName(databaseTemplate));
                File.WriteAllText(regimeTemplate, templates.RegimeLocalIni, new UTF8Encoding(false));
                File.WriteAllText(databaseTemplate, templates.YeniseiLocalIni, new UTF8Encoding(false));

                string initiatingSid = "S-1-5-21-111-222-333-1001";
                FakePathSafety pathSafety = new FakePathSafety(true);
                FakeWindowsServiceCollectionApi services =
                    new FakeWindowsServiceCollectionApi();
                VerifiedProvisionerBinary supervisor =
                    VerifiedProvisionerBinary.CreateForTesting(
                        @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe");
                LocalModuleWindowsServicePair pair =
                    new LocalModuleWindowsServicePair(services, supervisor);
                ManagedLocalModuleServiceReadinessProbe readiness =
                    new ManagedLocalModuleServiceReadinessProbe(
                        services,
                        new FakeTcpListenerOwnerReader(instance.ApiPort, 6001),
                        new FakeProcessParentReader(6001, 5001),
                        1,
                        delegate { });
                FakeEpmdCommandRunner epmd = new FakeEpmdCommandRunner();
                LocalModuleServicePairLifecycle lifecycle =
                    new LocalModuleServicePairLifecycle(
                        services,
                        new FakeLocalModuleServiceReadinessProbe(new List<string>()),
                        new EpmdInstanceController(
                            LocalModuleCapabilityProfile.Resolve("2.6.1"),
                            runtime.RuntimeRoot,
                            Path.Combine(instance.DataRoot, "temp"),
                            instance.EpmdPort,
                            epmd));
                ManagedLocalModuleLifecycleJournalStore journals =
                    new ManagedLocalModuleLifecycleJournalStore(
                        store.MachineRoot,
                        pathSafety);
                WindowsManagedLocalModulePlatform platform =
                    new WindowsManagedLocalModulePlatform(
                        initiatingSid,
                        runtime,
                        LocalModuleCapabilityProfile.Resolve("2.6.1"),
                        store,
                        journals,
                        new ManagedLocalModuleProfileStore(
                            store,
                            pathSafety,
                            new AtomicFileWriter()),
                        services,
                        pair,
                        lifecycle,
                        readiness,
                        epmd,
                        new FakeLocalModulePortReservationFactory());
                ManagedLocalModuleProvisioningItemRequest item =
                    CreateManagedLocalModuleRequest(1).ManagedLocalModules[0];
                ManagedLocalModuleProvisioningContext context =
                    new ManagedLocalModuleProvisioningContext(
                        runtime.RuntimeId,
                        runtime.CapabilityId,
                        runtime.ProductVersion);
                string operationId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

                platform.Reconcile(item, operationId);
                AssertEqual(ManagedLocalModuleObservedState.OwnedMismatch,
                    platform.Inspect(item, context),
                    "An owned instance without generated profile/services must be repairable.");
                platform.RecordStage(
                    item,
                    context,
                    operationId,
                    ManagedLocalModuleProvisioningStage.Created);
                platform.RecordStage(
                    item,
                    context,
                    operationId,
                    ManagedLocalModuleProvisioningStage.RuntimeReady);
                platform.PrepareProfile(item, context, operationId);
                platform.RecordStage(
                    item,
                    context,
                    operationId,
                    ManagedLocalModuleProvisioningStage.ProfileReady);
                platform.ConfigureServicePair(item, context, initiatingSid);

                AssertEqual(ManagedLocalModuleObservedState.MatchingStopped,
                    platform.Inspect(item, context),
                    "The persisted exact profile and DB/API definitions must be restartable.");
                AssertTrue(File.Exists(Path.Combine(instance.ConfigRoot, "regime-local.ini")),
                    "The production platform must persist generated configuration before SCM start.");
                AssertEqual(2, services.CreatedDefinitions.Count,
                    "The production platform must create exactly one restricted DB/API pair.");
                ManagedLocalModuleLifecycleJournal durable;
                AssertTrue(journals.TryRead(item.Inn, out durable) &&
                           durable.Stage == ManagedLocalModuleProvisioningStage.ProfileReady,
                    "The last completed mutation boundary must be durable before service start.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void CompleteStackCanaryFailureStopsRemainingGroups()
        {
            LmServiceProvisioningBatchRequest request =
                CreateManagedLocalModuleRequest(3);
            FakeManagedLocalModuleProvisioningPlatform platform =
                new FakeManagedLocalModuleProvisioningPlatform();
            platform.FailureStage = ManagedLocalModuleProvisioningStage.ProfileReady;
            ManagedLocalModuleProvisioner provisioner =
                new ManagedLocalModuleProvisioner(platform);
            CompleteStackProvisioningSession session =
                new CompleteStackProvisioningSession(
                    request,
                    provisioner,
                    new ManagedLocalModuleProvisioningContext(
                        "lmrt-0123456789abcdef01234567",
                        "local-module-2.6.1-7",
                        "2.6.1"));

            LmServiceProvisioningBatchResult result = session.ExecuteAll();

            AssertEqual(3, result.Items.Count,
                "Every prehashed group must receive an explicit session result.");
            AssertEqual(LmServiceProvisioningStatus.Failed, result.Items[0].Status,
                "The first planned group is the canary and must expose its failure.");
            AssertEqual(LmServiceProvisioningStatus.Cancelled, result.Items[1].Status,
                "The second group must remain untouched after canary failure.");
            AssertEqual(LmServiceProvisioningStatus.Cancelled, result.Items[2].Status,
                "The third group must remain untouched after canary failure.");
            AssertEqual(1, platform.ReconciledInns.Count,
                "Only the canary may enter the mutation workflow.");
            AssertEqual(request.ManagedLocalModules[0].Inn, platform.ReconciledInns[0],
                "The deterministic first group must be used as canary.");
            AssertFalse(platform.Events.Contains("profile:" + request.ManagedLocalModules[1].Inn),
                "No later profile may be created after canary failure.");
        }

        private static void CompleteStackProvisionsEveryKktOfSharedInn()
        {
            LmServiceProvisioningBatchRequest request =
                CreateManagedLocalModuleRequest(2);
            ManagedLocalModuleProvisioningItemRequest first =
                request.ManagedLocalModules[0];
            ManagedLocalModuleProvisioningItemRequest second =
                request.ManagedLocalModules[1];
            second.Inn = first.Inn;
            second.LocalModuleOrdinal = first.LocalModuleOrdinal;
            second.ApiPort = first.ApiPort;
            second.DatabasePort = first.DatabasePort;
            second.EpmdPort = first.EpmdPort;
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            FakeManagedLocalModuleProvisioningPlatform platform =
                new FakeManagedLocalModuleProvisioningPlatform();
            List<string> controllers = new List<string>();
            using (CompleteStackProvisioningSession session =
                new CompleteStackProvisioningSession(
                    request,
                    new ManagedLocalModuleProvisioner(platform),
                    new ManagedLocalModuleProvisioningContext(
                        "lmrt-0123456789abcdef01234567",
                        "local-module-2.6.1-7",
                        "2.6.1"),
                    delegate(ManagedLocalModuleProvisioningItemRequest item)
                    {
                        controllers.Add(item.KktSerial);
                        return new LmServiceProvisioningItemResult
                        {
                            KktSerial = item.KktSerial,
                            Status = LmServiceProvisioningStatus.Succeeded,
                            Message = "controller ready"
                        };
                    }))
            {
                LmServiceProvisioningBatchResult result = session.ExecuteAll();
                AssertEqual(2, result.Items.Count,
                    "Every KKT must receive a full-stack result.");
                AssertEqual(LmServiceProvisioningStatus.ReadyToInitialize,
                    result.Items[0].Status,
                    "The first shared-INN KKT must be ready.");
                AssertEqual(LmServiceProvisioningStatus.ReadyToInitialize,
                    result.Items[1].Status,
                    "The second shared-INN KKT must be ready.");
            }
            AssertEqual(2, controllers.Count,
                "Each KKT of one INN must receive its own controller service.");
            AssertEqual(1, platform.ProfilePreparationCount,
                "The shared LM profile must be created only once.");
        }

        private static void CompleteStackSkipsLaterUnregisteredKkt()
        {
            LmServiceProvisioningBatchRequest request =
                CreateManagedLocalModuleRequest(3);
            FakeManagedLocalModuleProvisioningPlatform platform =
                new FakeManagedLocalModuleProvisioningPlatform();
            CompleteStackProvisioningSession session =
                new CompleteStackProvisioningSession(
                    request,
                    new ManagedLocalModuleProvisioner(platform),
                    new ManagedLocalModuleProvisioningContext(
                        "lmrt-0123456789abcdef01234567",
                        "local-module-2.6.1-7",
                        "2.6.1"));

            session.ExecuteItem(0);
            LmServiceProvisioningItemResult skipped = session.SkipItem(
                1,
                "ККТ не зарегистрирована в ЕСМ.");
            session.ExecuteItem(2);
            LmServiceProvisioningBatchResult result = session.Finish();

            AssertEqual(LmServiceProvisioningStatus.Cancelled, skipped.Status,
                "A nonregistered later KKT must be explicitly skipped.");
            AssertEqual(LmServiceProvisioningStatus.ReadyToInitialize,
                result.Items[2].Status,
                "An independent later KKT must continue after the skipped row.");
            AssertEqual(2, platform.ReconciledInns.Count,
                "The skipped KKT must never enter the mutation workflow.");
        }

        private static void ManagedLocalModuleSameInnEnsureIsIdempotent()
        {
            ManagedLocalModuleProvisioningItemRequest item =
                CreateManagedLocalModuleRequest(1).ManagedLocalModules[0];
            FakeManagedLocalModuleProvisioningPlatform platform =
                new FakeManagedLocalModuleProvisioningPlatform();
            ManagedLocalModuleProvisioner provisioner =
                new ManagedLocalModuleProvisioner(platform);
            ManagedLocalModuleProvisioningContext context =
                new ManagedLocalModuleProvisioningContext(
                    "lmrt-0123456789abcdef01234567",
                    "local-module-2.6.1-7",
                    "2.6.1");

            LmServiceProvisioningItemResult first = provisioner.Ensure(
                item,
                context,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "S-1-5-21-111-222-333-1001");
            int mutationsAfterFirst = platform.MutationCount;
            LmServiceProvisioningItemResult second = provisioner.Ensure(
                item,
                context,
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "S-1-5-21-111-222-333-1001");

            AssertEqual(LmServiceProvisioningStatus.ReadyToInitialize, first.Status,
                "The first ensure must reach a ready but not falsely initialized state.");
            AssertEqual(LmServiceProvisioningStatus.ReadyToInitialize, second.Status,
                "A matching second ensure must return the same factual state.");
            AssertEqual(mutationsAfterFirst, platform.MutationCount,
                "A matching same-INN ensure must not rewrite profile, SCM or listeners.");
            AssertEqual(1, platform.ProfilePreparationCount,
                "One INN must own only one prepared profile.");
            AssertEqual(1, platform.ServicePairConfigurationCount,
                "One INN must own only one DB/API service pair.");
            AssertEqual(1, platform.StackReferenceEnsureCount,
                "A ready shared LM must still restore the KKT ownership reference idempotently.");
        }

        private static void ManagedRemovalRetainsSharedSameInnModule()
        {
            FakeManagedLocalModuleRemovalPlatform platform =
                new FakeManagedLocalModuleRemovalPlatform();
            platform.InstanceReferencesAfterStackDeletion = 1;
            ManagedLocalModuleRemovalWorkflow workflow =
                new ManagedLocalModuleRemovalWorkflow(platform);

            LmServiceProvisioningItemResult result = workflow.RemoveKkt(
                "00105700000001",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            AssertEqual(LmServiceProvisioningStatus.SharedLocalModuleRetained, result.Status,
                "Removing one KKT must retain the same-INN LM referenced by another KKT.");
            AssertSequence(
                new[] { "controller", "stack" },
                platform.Events,
                "Shared-INN removal must stop after deleting only KKT-owned components.");
            AssertEqual(0, platform.InstanceDeletionCount,
                "The shared INN profile must remain owned and intact.");
            AssertEqual(0, platform.RuntimeDeletionCount,
                "A referenced runtime must never enter deletion.");
        }

        private static void ManagedRemovalCleansCompleteStackInReverse()
        {
            FakeManagedLocalModuleRemovalPlatform platform =
                new FakeManagedLocalModuleRemovalPlatform();
            platform.InstanceReferencesAfterStackDeletion = 0;
            platform.RuntimeReferencesAfterInstanceDeletion = 0;
            ManagedLocalModuleRemovalWorkflow workflow =
                new ManagedLocalModuleRemovalWorkflow(platform);

            LmServiceProvisioningItemResult result = workflow.RemoveKkt(
                "00105700000001",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

            AssertEqual(LmServiceProvisioningStatus.Succeeded, result.Status,
                "An unreferenced full stack must be removed completely.");
            AssertSequence(
                new[]
                {
                    "controller",
                    "stack",
                    "stop-pair",
                    "services",
                    "profile",
                    "instance",
                    "runtime"
                },
                platform.Events,
                "Cleanup must follow reverse ownership and dependency order.");
        }

        private static void ManagedRemovalProjectsCleanupPendingAndRetries()
        {
            FakeManagedLocalModuleRemovalPlatform platform =
                new FakeManagedLocalModuleRemovalPlatform();
            platform.InstanceReferencesAfterStackDeletion = 0;
            platform.RuntimeReferencesAfterInstanceDeletion = 0;
            platform.FailProfileDeletionOnce = true;
            ManagedLocalModuleRemovalWorkflow workflow =
                new ManagedLocalModuleRemovalWorkflow(platform);

            LmServiceProvisioningItemResult first = workflow.RemoveKkt(
                "00105700000001",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            LmServiceProvisioningItemResult retry = workflow.RemoveKkt(
                "00105700000001",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

            AssertEqual(LmServiceProvisioningStatus.CleanupPending, first.Status,
                "A transient occupied profile must be projected as CleanupPending.");
            AssertEqual(LmServiceProvisioningStatus.Succeeded, retry.Status,
                "A retry after the transient failure must complete idempotently.");
            AssertEqual(1, platform.CleanupPendingCount,
                "The pending state must be persisted exactly once.");
            AssertEqual(1, platform.InstanceDeletionCount,
                "Retry must delete the instance manifest only after profile deletion succeeds.");
            AssertEqual(1, platform.RuntimeDeletionCount,
                "Retry must finish zero-reference runtime cleanup.");
        }

        private static void ManagedRemovalJournalSurvivesDeletedKktStack()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore manifests;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    out manifests,
                    out runtime,
                    out instance);
                ManagedKktStackManifest stack = ManagedKktStackManifest.Create(
                    CreateManagedLocalModuleRequest(1).ManagedLocalModules[0],
                    instance.InstanceId,
                    string.Empty,
                    "78787878787878787878787878787878",
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
                manifests.WriteStack(stack);
                ManagedLocalModuleRemovalSnapshot snapshot =
                    ManagedLocalModuleRemovalSnapshot.CreateOwned(
                        stack,
                        instance,
                        runtime);
                ManagedLocalModuleRemovalJournalStore journal =
                    new ManagedLocalModuleRemovalJournalStore(
                        manifests.MachineRoot,
                        new FakePathSafety(true));
                journal.Write(
                    snapshot,
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    string.Empty);
                manifests.DeleteStack(stack.KktSerial, stack.OwnershipNonce);

                ManagedLocalModuleRemovalSnapshot recovered;
                AssertTrue(journal.TryRead(stack.KktSerial, out recovered),
                    "Cleanup retry must retain ownership after the KKT stack file is gone.");
                AssertEqual(snapshot.InstanceId, recovered.InstanceId,
                    "The retry journal must retain the exact INN profile identity.");
                AssertEqual(snapshot.RuntimeId, recovered.RuntimeId,
                    "The retry journal must retain the exact shared runtime identity.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void WindowsManagedRemovalDeletesOwnedStackAndRetryJournal()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                LocalModuleManifestStore manifests;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    out manifests,
                    out runtime,
                    out instance);
                LocalModuleTemplateObservation templates =
                    CreateLocalModuleTemplateObservation();
                string regimeTemplate = Path.Combine(
                    runtime.RuntimeRoot,
                    @"regime\etc\local.ini.dist");
                string databaseTemplate = Path.Combine(
                    runtime.RuntimeRoot,
                    @"yenisei\etc\local.ini.dist");
                Directory.CreateDirectory(Path.GetDirectoryName(regimeTemplate));
                Directory.CreateDirectory(Path.GetDirectoryName(databaseTemplate));
                File.WriteAllText(regimeTemplate, templates.RegimeLocalIni, new UTF8Encoding(false));
                File.WriteAllText(databaseTemplate, templates.YeniseiLocalIni, new UTF8Encoding(false));
                FakePathSafety pathSafety = new FakePathSafety(true);
                ManagedLocalModuleProfileStore profiles =
                    new ManagedLocalModuleProfileStore(
                        manifests,
                        pathSafety,
                        new AtomicFileWriter());
                ManagedLocalModuleProvisioningItemRequest item =
                    CreateManagedLocalModuleRequest(1).ManagedLocalModules[0];
                LocalModuleConfiguration configuration = profiles.WriteConfiguration(
                    runtime,
                    LocalModuleCapabilityProfile.Resolve("2.6.1"),
                    item,
                    instance.InstanceId,
                    instance.OwnershipNonce);
                LocalModuleInstanceManifest replacement =
                    LocalModuleInstanceManifest.Create(
                        item,
                        instance.InstanceId,
                        runtime.RuntimeId,
                        runtime.RuntimeRoot,
                        configuration,
                        instance.OwnershipNonce,
                        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
                manifests.WriteInstance(replacement);
                profiles.ProtectOwnedManifests(runtime, replacement);
                ManagedKktStackManifest stack = ManagedKktStackManifest.Create(
                    item,
                    replacement.InstanceId,
                    string.Empty,
                    "89898989898989898989898989898989",
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
                manifests.WriteStack(stack);
                Directory.Delete(Path.Combine(runtime.RuntimeRoot, "regime"), true);
                Directory.Delete(Path.Combine(runtime.RuntimeRoot, "yenisei"), true);

                FakeWindowsServiceCollectionApi services =
                    new FakeWindowsServiceCollectionApi();
                ManagedLocalModuleRemovalJournalStore removalJournal =
                    new ManagedLocalModuleRemovalJournalStore(
                        manifests.MachineRoot,
                        pathSafety);
                string displayedFingerprint;
                AssertTrue(
                    manifests.TryComputeStackFingerprint(
                        item.KktSerial,
                        out displayedFingerprint),
                    "The displayed managed stack must have an exact fingerprint.");
                WindowsManagedLocalModuleRemovalPlatform stalePlatform =
                    new WindowsManagedLocalModuleRemovalPlatform(
                        manifests,
                        removalJournal,
                        profiles,
                        new LocalModuleRuntimeInstaller(
                            manifests,
                            pathSafety,
                            NoopLocalModuleMutationBoundary.Instance),
                        services,
                        VerifiedProvisionerBinary.CreateForTesting(
                            @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe"),
                        new FakeLocalModuleServiceReadinessProbe(
                            new List<string>()),
                        new FakeEpmdCommandRunner(
                            new EpmdCommandResult(
                                0,
                                "epmd: up and running\n",
                                string.Empty)),
                        NoopManagedKktControllerRemoval.Instance,
                        new string('f', 64),
                        delegate { });
                AssertThrows<InvalidDataException>(delegate
                {
                    stalePlatform.Inspect(
                        item.KktSerial,
                        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
                }, "A changed managed stack must be rejected before cleanup mutation.");
                WindowsManagedLocalModuleRemovalPlatform platform =
                    new WindowsManagedLocalModuleRemovalPlatform(
                        manifests,
                        removalJournal,
                        profiles,
                        new LocalModuleRuntimeInstaller(
                            manifests,
                            pathSafety,
                            NoopLocalModuleMutationBoundary.Instance),
                        services,
                        VerifiedProvisionerBinary.CreateForTesting(
                            @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe"),
                        new FakeLocalModuleServiceReadinessProbe(new List<string>()),
                        new FakeEpmdCommandRunner(
                            new EpmdCommandResult(0, "epmd: up and running\n", string.Empty),
                            new EpmdCommandResult(0, "Killed\n", string.Empty)),
                        NoopManagedKktControllerRemoval.Instance,
                        displayedFingerprint,
                        delegate { });

                LmServiceProvisioningItemResult result =
                    new ManagedLocalModuleRemovalWorkflow(platform).RemoveKkt(
                        item.KktSerial,
                        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

                AssertEqual(LmServiceProvisioningStatus.Succeeded, result.Status,
                    "The concrete cleanup must remove every zero-reference owned layer.");
                AssertFalse(Directory.Exists(replacement.ProfileRoot),
                    "The owned INN profile must leave no data, log or config debris.");
                AssertFalse(Directory.Exists(runtime.RuntimeRoot),
                    "The zero-reference verified runtime must be removed last.");
                ManagedLocalModuleRemovalSnapshot pending;
                AssertFalse(removalJournal.TryRead(item.KktSerial, out pending),
                    "A completed cleanup must remove its retry journal.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void ManagedUpdateGuardNeverStopsUnknownVersion()
        {
            FakeManagedLocalModuleUpdatePlatform platform =
                new FakeManagedLocalModuleUpdatePlatform();
            ManagedLocalModuleUpdateWorkflow workflow =
                new ManagedLocalModuleUpdateWorkflow(platform);

            LmServiceProvisioningStatus result = workflow.Evaluate(
                "2.6.1",
                "2.6.2");

            AssertEqual(LmServiceProvisioningStatus.VersionVerificationPending, result,
                "A package without an exact capability and migration pair must be blocked.");
            AssertEqual(0, platform.BeginMigrationCount,
                "Unknown-version evaluation must not stop or mutate running instances.");
        }

        private static void LmProfileAdapterChangesOnlySupportedFields()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                FakeWindowsServiceApi api = new FakeWindowsServiceApi();
                OfficialLmProfileAdapter adapter = CreateTestProfileAdapter(root, api);
                ManagedLmServiceSpec initial = CreateManagedSpec(
                    "00105700000001", 55000, 15000, "10.20.30.40", 5995);
                adapter.CreateOrLoadConfiguration(initial, TestServiceSid());
                string path = adapter.GetConfigurationPath(initial.KktSerial);
                string before = File.ReadAllText(path, Encoding.UTF8);
                ManagedLmServiceSpec changed = CreateManagedSpec(
                    initial.KktSerial, 55001, 15001, "lm-two.example", 6995);

                adapter.ApplyConfiguration(changed, TestServiceSid());
                LmProfileConfiguration observed = adapter.ReadConfiguration(
                    initial.KktSerial, TestServiceSid());
                string after = File.ReadAllText(path, Encoding.UTF8);

                AssertEqual(55001, observed.GrpcPort, "Expected only the typed gRPC port update.");
                AssertEqual(15001, observed.RestPort, "Expected only the typed REST port update.");
                AssertEqual("lm-two.example", observed.TargetAddress,
                    "Expected only the normalized target address update.");
                AssertEqual(6995, observed.TargetPort, "Expected only the typed target port update.");
                AssertEqual(RemoveSupportedProfileLines(before), RemoveSupportedProfileLines(after),
                    "Every unsupported schema line must remain byte-for-byte stable.");

                CreateTestSupervisorService(api).EnsureConfigured(initial.KktSerial);
                api.Start(initial.ServiceName);
                AssertThrows<InvalidOperationException>(delegate {
                    adapter.ApplyConfiguration(changed, TestServiceSid());
                }, "A running supervisor must block every profile mutation.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void LmProfileAdapterRejectsAmbiguousSchema()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                OfficialLmProfileAdapter adapter = CreateTestProfileAdapter(root);
                ManagedLmServiceSpec spec = CreateManagedSpec(
                    "00105700000001", 55000, 15000, "10.20.30.40", 5995);
                adapter.CreateOrLoadConfiguration(spec, TestServiceSid());
                string path = adapter.GetConfigurationPath(spec.KktSerial);
                File.AppendAllText(path, Environment.NewLine +
                    "settings:" + Environment.NewLine +
                    "    common:" + Environment.NewLine +
                    "        gRPCPort: 55999" + Environment.NewLine, Encoding.UTF8);

                AssertThrows<InvalidDataException>(delegate {
                    adapter.ReadConfiguration(spec.KktSerial, TestServiceSid());
                },
                    "Duplicate schema paths must fail closed before configuration changes.");

                ManagedLmServiceSpec second = CreateManagedSpec(
                    "00105700000002", 55001, 15001, "10.20.30.41", 5996);
                adapter.CreateOrLoadConfiguration(second, TestServiceSid());
                string secondPath = adapter.GetConfigurationPath(second.KktSerial);
                string malformedIndent = File.ReadAllText(secondPath, Encoding.UTF8).Replace(
                    "        gRPCPort:",
                    "            gRPCPort:");
                File.WriteAllText(secondPath, malformedIndent, Encoding.UTF8);
                AssertThrows<InvalidDataException>(delegate {
                    adapter.ReadConfiguration(second.KktSerial, TestServiceSid());
                }, "A path with non-exact nesting must fail closed.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void LmProfileAdapterPreservesUnknownNonsecretFields()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                OfficialLmProfileAdapter adapter = CreateTestProfileAdapter(root);
                ManagedLmServiceSpec spec = CreateManagedSpec(
                    "00105700000001", 55000, 15000, "10.20.30.40", 5995);
                adapter.CreateOrLoadConfiguration(spec, TestServiceSid());
                string path = adapter.GetConfigurationPath(spec.KktSerial);
                File.AppendAllText(path,
                    "    extension:" + Environment.NewLine +
                    "        harmlessFlag: keep-me" + Environment.NewLine,
                    Encoding.UTF8);

                adapter.ApplyConfiguration(CreateManagedSpec(
                    spec.KktSerial, 55002, 15002, "10.20.30.41", 5996), TestServiceSid());

                AssertContains(File.ReadAllText(path, Encoding.UTF8), "harmlessFlag: keep-me");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void LmProfileAdapterWritesAtomically()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                OfficialLmProfileAdapter adapter = CreateTestProfileAdapter(root);
                ManagedLmServiceSpec spec = CreateManagedSpec(
                    "00105700000001", 55000, 15000, "10.20.30.40", 5995);

                adapter.CreateOrLoadConfiguration(spec, TestServiceSid());
                adapter.ApplyConfiguration(CreateManagedSpec(
                    spec.KktSerial, 55003, 15003, "10.20.30.42", 5997), TestServiceSid());

                string directory = Path.GetDirectoryName(adapter.GetConfigurationPath(spec.KktSerial));
                AssertEqual(0, Directory.GetFiles(directory, "*.tmp").Length,
                    "Atomic profile writes must not leave a temporary file.");
                AssertEqual(55003, adapter.ReadConfiguration(
                    spec.KktSerial, TestServiceSid()).GrpcPort,
                    "The atomically replaced file must remain readable.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void LmProfileAdapterNeverClonesOfficialProfile()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                OfficialLmProfileAdapter adapter = CreateTestProfileAdapter(root);
                ManagedLmServiceSpec spec = CreateManagedSpec(
                    "00105700000001", 55000, 15000, "10.20.30.40", 5995);
                string profileRoot = adapter.PrepareEmptyProfile(spec, TestServiceSid());
                adapter.CreateOrLoadConfiguration(spec, TestServiceSid());
                string[] files = Directory.GetFiles(profileRoot, "*", SearchOption.AllDirectories);

                AssertEqual(1, files.Length,
                    "A new profile may contain only the independently generated config before first start.");
                AssertEqual("config.yml", Path.GetFileName(files[0]),
                    "Certificates and keys must be generated by the official controller itself.");
                string fixturePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Fixtures",
                    "official-lm-profile-sanitized.json");
                string fixture = File.ReadAllText(fixturePath, Encoding.UTF8);
                AssertContains(fixture, "PrivateBlackBox");
                AssertContains(fixture, "run3-empty-profile");
                AssertFalse(fixture.IndexOf("BEGIN ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            fixture.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            fixture.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The sanitized schema fixture must contain no copied secrets.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void LmProfileAdapterDetectsUnsupportedControllerVersion()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                TrustedFileExpectation binary = CreateExpectation(
                    Path.Combine(root, "lmcontroller.exe"), "lmcontroller.exe");
                TrustedFileExpectation installer = CreateExpectation(
                    Path.Combine(root, "setup.exe"),
                    "esm-lm-controller_9.9.9.9-windows-setup.exe");
                ControllerCapabilityProfile unsupported = ControllerCapabilityProfile.CreateForTesting(
                    "9.9.9.9",
                    root,
                    "lmcontroller.exe",
                    binary,
                    installer,
                    "ProgramData");

                AssertThrows<NotSupportedException>(delegate {
                    new OfficialLmProfileAdapter(
                        unsupported,
                        new ManagedServiceManifestStore(
                            root, new FakePathSafety(true), "S-1-5-21-111-222-333-1001"),
                        new AtomicFileWriter(),
                        new FakeWindowsServiceApi());
                }, "An uncharacterized controller version must be rejected before profile access.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static OfficialLmProfileAdapter CreateTestProfileAdapter(
            string root,
            FakeWindowsServiceApi serviceApi = null)
        {
            return new OfficialLmProfileAdapter(
                ControllerCapabilityProfile.SupportedVersion1632(),
                new ManagedServiceManifestStore(
                    root,
                    new FakePathSafety(true),
                    "S-1-5-21-111-222-333-1001"),
                new AtomicFileWriter(),
                serviceApi ?? new FakeWindowsServiceApi());
        }

        private static ManagedLmServiceSpec CreateManagedSpec(
            string serial,
            int grpcPort,
            int restPort,
            string targetAddress,
            int targetPort)
        {
            return new ManagedLmServiceSpec(
                new LmGatewayKkt
                {
                    KktSerial = serial,
                    KktInn = "1234567894"
                },
                new LmGatewayPorts(grpcPort, restPort),
                new LmGatewayTarget(targetAddress, targetPort));
        }

        private static string TestServiceSid()
        {
            return "S-1-5-80-123456789-123456789-123456789-123456789-123456789";
        }

        private static string RemoveSupportedProfileLines(string value)
        {
            string[] lines = value.Replace("\r\n", "\n").Split('\n');
            StringBuilder result = new StringBuilder();
            for (int index = 0; index < lines.Length; index++)
            {
                string trimmed = lines[index].TrimStart();
                if (trimmed.StartsWith("gRPCPort:", StringComparison.Ordinal) ||
                    trimmed.StartsWith("RESTPort:", StringComparison.Ordinal) ||
                    trimmed.StartsWith("url:", StringComparison.Ordinal) ||
                    trimmed.StartsWith("port:", StringComparison.Ordinal))
                {
                    continue;
                }
                result.AppendLine(lines[index]);
            }
            return result.ToString();
        }

        private static void EnsureCreatesProfileServiceAndListenersInOrder()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            LmServiceProvisioningBatchResult result = new LmServiceProvisioner(platform)
                .EnsureBatch(CreateEnsureRequest(1), NeverCancelLmProvisioning.Instance);

            AssertEqual(LmServiceProvisioningStatus.Succeeded, result.Items[0].Status,
                "A fully confirmed new service must succeed.");
            AssertEventOrder(platform.Events,
                "MachineLock", "ItemLock:00105700000001", "Reconcile:00105700000001",
                "VerifyController", "Reserve:00105700000001", "Journal:Preparing",
                "Profile:00105700000001", "Journal:ProfileReady", "Configure:00105700000001",
                "Journal:ServiceReady", "ReleasePorts:00105700000001", "Start:00105700000001",
                "Journal:Started", "Probe:00105700000001", "Manifest:00105700000001",
                "CompleteJournal:00105700000001");
        }

        private static void EnsureIsNoOpForMatchingReadyService()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            platform.SetState("00105700000001", LmProvisioningObservedState.MatchingReady);

            LmServiceProvisioningBatchResult result = new LmServiceProvisioner(platform)
                .EnsureBatch(CreateEnsureRequest(1), NeverCancelLmProvisioning.Instance);

            AssertEqual(LmServiceProvisioningStatus.Succeeded, result.Items[0].Status,
                "A matching ready service must be reported unchanged.");
            AssertContains(result.Items[0].Message, "изменений");
            AssertFalse(platform.ContainsEventPrefix("Reserve:") ||
                        platform.ContainsEventPrefix("Configure:") ||
                        platform.ContainsEventPrefix("Start:"),
                "A matching ready service must not be mutated.");
        }

        private static void EnsureStartsMatchingStoppedService()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            platform.SetState("00105700000001", LmProvisioningObservedState.MatchingStopped);

            LmServiceProvisioningBatchResult result = new LmServiceProvisioner(platform)
                .EnsureBatch(CreateEnsureRequest(1), NeverCancelLmProvisioning.Instance);

            AssertEqual(LmServiceProvisioningStatus.Succeeded, result.Items[0].Status,
                "A matching stopped service must start and pass readiness.");
            AssertTrue(platform.ContainsEventPrefix("Start:"), "Expected StartService.");
            AssertFalse(platform.ContainsEventPrefix("Configure:"),
                "An exact stopped service needs no SCM rewrite.");
        }

        private static void EnsureSafelyUpdatesOwnedMismatchedService()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            platform.SetState("00105700000001", LmProvisioningObservedState.OwnedMismatch);

            LmServiceProvisioningBatchResult result = new LmServiceProvisioner(platform)
                .EnsureBatch(CreateEnsureRequest(1), NeverCancelLmProvisioning.Instance);

            AssertEqual(LmServiceProvisioningStatus.Succeeded, result.Items[0].Status,
                "An owned mismatch must be updated transactionally.");
            AssertEventOrder(platform.Events, "Stop:00105700000001", "Reserve:00105700000001",
                "Profile:00105700000001", "Configure:00105700000001",
                "ReleasePorts:00105700000001", "Start:00105700000001");
        }

        private static void EnsureBlocksUnknownExistingService()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            platform.SetState("00105700000001", LmProvisioningObservedState.Foreign);

            LmServiceProvisioningBatchResult result = new LmServiceProvisioner(platform)
                .EnsureBatch(CreateEnsureRequest(1), NeverCancelLmProvisioning.Instance);

            AssertEqual(LmServiceProvisioningStatus.RequiresAttention, result.Items[0].Status,
                "A foreign service at the derived name must block mutation.");
            AssertFalse(platform.ContainsEventPrefix("Stop:") ||
                        platform.ContainsEventPrefix("Configure:"),
                "Unknown ownership must not trigger stop or update.");
        }

        private static void EnsureRechecksAndHoldsExactExclusiveEndpointsUntilStart()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            platform.RequireReservationReleasedBeforeStart = true;

            LmServiceProvisioningBatchResult result = new LmServiceProvisioner(platform)
                .EnsureBatch(CreateEnsureRequest(1), NeverCancelLmProvisioning.Instance);

            AssertEqual(LmServiceProvisioningStatus.Succeeded, result.Items[0].Status,
                "The exact dual-stack reservation must be released only at the start boundary.");
            AssertTrue(platform.ReservationHeldDuringProfileAndScm,
                "Both listener ports must remain exclusively held through profile and SCM mutation.");

            int first = FindUnusedDualStackPort();
            int second = FindUnusedDualStackPort(first);
            using (ExclusiveTcpPortReservation reservation =
                ExclusiveTcpPortReservation.AcquireDualStackWildcard(first, second))
            {
                AssertThrows<SocketException>(
                    delegate { BindAndClose(AddressFamily.InterNetwork, first, false); },
                    "The reservation must occupy the IPv4 side of the dual-stack endpoint.");
                AssertThrows<SocketException>(
                    delegate { BindAndClose(AddressFamily.InterNetworkV6, second, false); },
                    "The reservation must occupy the IPv6 side of the dual-stack endpoint.");
            }

            using (Socket ipv4Owner = CreateExclusiveSocket(AddressFamily.InterNetwork, true))
            {
                ipv4Owner.Bind(new IPEndPoint(IPAddress.Any, 0));
                int occupied = ((IPEndPoint)ipv4Owner.LocalEndPoint).Port;
                int peer = FindUnusedDualStackPort(occupied);
                AssertThrows<SocketException>(
                    delegate
                    {
                        using (ExclusiveTcpPortReservation ignored =
                            ExclusiveTcpPortReservation.AcquireDualStackWildcard(occupied, peer))
                        {
                        }
                    },
                    "An existing IPv4 listener must block a dual-stack reservation.");
            }

            using (Socket ipv6Owner = CreateExclusiveSocket(AddressFamily.InterNetworkV6, false))
            {
                ipv6Owner.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
                int occupied = ((IPEndPoint)ipv6Owner.LocalEndPoint).Port;
                int peer = FindUnusedDualStackPort(occupied);
                AssertThrows<SocketException>(
                    delegate
                    {
                        using (ExclusiveTcpPortReservation ignored =
                            ExclusiveTcpPortReservation.AcquireDualStackWildcard(occupied, peer))
                        {
                        }
                    },
                    "An existing IPv6 listener must block a dual-stack reservation.");
            }
        }

        private static void EnsureRejectsListenerOwnedByAnotherProcess()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            platform.Readiness = LmReadinessResult.Failed("listener belongs to another process");

            LmServiceProvisioningBatchResult result = new LmServiceProvisioner(platform)
                .EnsureBatch(CreateEnsureRequest(1), NeverCancelLmProvisioning.Instance);

            AssertEqual(LmServiceProvisioningStatus.Failed, result.Items[0].Status,
                "Foreign listener ownership must fail readiness.");
            AssertContains(result.Items[0].Message, "listener");

            using (Socket listener = CreateExclusiveSocket(
                AddressFamily.InterNetworkV6,
                true))
            {
                listener.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
                listener.Listen(1);
                int port = ((IPEndPoint)listener.LocalEndPoint).Port;
                IList<int> owners = new TcpListenerOwnerReader()
                    .FindListenerProcessIds(port);
                AssertTrue(
                    owners.Contains(System.Diagnostics.Process.GetCurrentProcess().Id),
                    "The IP Helper reader must map an IPv6 dual-stack listener to its owner PID.");
            }
        }

        private static void EnsureLeavesFailedNewServiceStoppedForDiagnosis()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            platform.Readiness = LmReadinessResult.Failed("readiness timeout");

            new LmServiceProvisioner(platform).EnsureBatch(
                CreateEnsureRequest(1), NeverCancelLmProvisioning.Instance);

            AssertTrue(platform.ContainsEventPrefix("Stop:"),
                "A failed new service must receive a normal stop request.");
            AssertFalse(platform.ContainsEventPrefix("Delete:"),
                "A failed new service must remain for diagnosis and reconciliation.");
            AssertTrue(platform.Events.Contains("Journal:Failed"),
                "A terminal failure journal must remain.");
        }

        private static void EnsureWritesManifestOnlyAfterConfirmedStages()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            new LmServiceProvisioner(platform).EnsureBatch(
                CreateEnsureRequest(1), NeverCancelLmProvisioning.Instance);

            AssertTrue(platform.Events.IndexOf("Probe:00105700000001") <
                       platform.Events.IndexOf("Manifest:00105700000001"),
                "Manifest ownership must be committed only after readiness.");
            AssertTrue(platform.Events.IndexOf("Manifest:00105700000001") <
                       platform.Events.IndexOf("CompleteJournal:00105700000001"),
                "The crash journal must be removed last.");
        }

        private static void EnsureJournalRecoversEverySimulatedCrashStage()
        {
            string[] stages = { "Profile", "Configure", "Start", "Manifest" };
            for (int index = 0; index < stages.Length; index++)
            {
                FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
                platform.ThrowOnceAtPrefix = stages[index] + ":";
                LmServiceProvisioningBatchRequest request = CreateEnsureRequest(1);
                new LmServiceProvisioner(platform).EnsureBatch(
                    request, NeverCancelLmProvisioning.Instance);
                platform.ThrowOnceAtPrefix = null;
                platform.Events.Clear();

                new LmServiceProvisioner(platform).EnsureBatch(
                    request, NeverCancelLmProvisioning.Instance);

                AssertTrue(platform.Events.IndexOf("Reconcile:00105700000001") >= 0,
                    "Every resumed stage must reconcile its journal first.");
                AssertTrue(platform.Events.IndexOf("Reconcile:00105700000001") <
                           platform.Events.IndexOf("VerifyController"),
                    "Recovery must precede fresh mutation after " + stages[index] + ".");
            }

            string root = CreateTemporaryDirectory();
            try
            {
                ProvisioningOperationJournalStore store =
                    new ProvisioningOperationJournalStore(root, new FakePathSafety(true));
                string operationId = Guid.NewGuid().ToString("N");
                ProvisioningOperationJournal first = CreateTestJournal(
                    operationId,
                    "00105700000001",
                    55000,
                    15000);
                ProvisioningOperationJournal second = CreateTestJournal(
                    operationId,
                    "00105700000002",
                    55001,
                    15001);
                store.Write(first);
                store.Write(second);

                AssertEqual(
                    "00105700000001",
                    store.Read(operationId, "00105700000001").KktSerial,
                    "Batch journals must not overwrite the first KKT under one operation id.");
                AssertEqual(
                    "00105700000002",
                    store.Read(operationId, "00105700000002").KktSerial,
                    "Batch journals must retain an independent second KKT record.");
                AssertTrue(store.GetFingerprint(operationId, "00105700000001").Length == 64,
                    "A journal-only recovery projection needs a stable confirmation fingerprint.");
                store.DeleteForKkt("00105700000001");
                AssertEqual(
                    "00105700000002",
                    store.Read(operationId, "00105700000002").KktSerial,
                    "Completing one KKT must not delete another row's journal.");
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static void EnsureSerializesConcurrentMutations()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            new LmServiceProvisioner(platform).EnsureBatch(
                CreateEnsureRequest(2), NeverCancelLmProvisioning.Instance);

            AssertEqual(1, platform.MaximumMachineLockDepth,
                "Only one machine mutation boundary may be held.");
            AssertEqual(1, platform.MaximumItemLockDepth,
                "Only one per-KKT mutation boundary may be held at a time.");
            AssertEqual(0, platform.CurrentMachineLockDepth,
                "Machine lock must be released after the batch.");
            AssertEqual(0, platform.CurrentItemLockDepth,
                "Item lock must be released after each KKT.");
        }

        private static void EnsureBatchContinuesFailuresAndHonorsCancelBoundary()
        {
            FakeLmProvisioningPlatform continuing = new FakeLmProvisioningPlatform();
            continuing.FailSerial = "00105700000001";
            LmServiceProvisioningBatchResult continued = new LmServiceProvisioner(continuing)
                .EnsureBatch(CreateEnsureRequest(2), NeverCancelLmProvisioning.Instance);
            AssertEqual(LmServiceProvisioningStatus.Failed, continued.Items[0].Status,
                "The injected item failure must be preserved.");
            AssertEqual(LmServiceProvisioningStatus.Succeeded, continued.Items[1].Status,
                "A failed row must not hide or skip the next row.");

            FakeLmProvisioningPlatform cancelling = new FakeLmProvisioningPlatform();
            CancelAfterCompletedItems cancel = new CancelAfterCompletedItems(cancelling, 1);
            LmServiceProvisioningBatchResult cancelled = new LmServiceProvisioner(cancelling)
                .EnsureBatch(CreateEnsureRequest(3), cancel);
            AssertEqual(LmServiceProvisioningStatus.Succeeded, cancelled.Items[0].Status,
                "The current item must finish before cancellation.");
            AssertEqual(LmServiceProvisioningStatus.Cancelled, cancelled.Items[1].Status,
                "The first untouched item must be cancelled.");
            AssertEqual(LmServiceProvisioningStatus.Cancelled, cancelled.Items[2].Status,
                "Every remaining untouched item must be cancelled.");
        }

        private static void EnsureBatchRejectsStaleOperationResult()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            LmServiceProvisioningBatchRequest request = CreateEnsureRequest(1);
            request.PlanHash = new string('f', 64);

            LmServiceProvisioningBatchResult result = new LmServiceProvisioner(platform)
                .EnsureBatch(request, NeverCancelLmProvisioning.Instance);

            AssertEqual(LmServiceProvisioningStatus.Failed, result.Status,
                "A stale canonical operation hash must fail before locks or SCM.");
            AssertEqual(0, platform.Events.Count,
                "A stale request must not reach the mutation platform.");
        }

        private static void InstallVersionMarksEveryManagedInstanceVerificationPendingBeforeLaunch()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            platform.ManagedSerials.Add("00105700000001");
            platform.ManagedSerials.Add("00105700000002");

            LmControllerInstallResult result = new LmServiceProvisioner(platform)
                .InstallControllerVersion(CreateInstallerRequest());

            AssertEqual(LmServiceProvisioningStatus.Succeeded, result.Status,
                "A verified staged installer may complete.");
            AssertEventOrder(platform.Events,
                "PrepareInstaller", "VersionPending:00105700000001",
                "VersionPending:00105700000002", "Stop:00105700000001",
                "Stop:00105700000002", "LaunchInstaller");
            AssertFalse(platform.ContainsEventPrefix("Start:"),
                "Managed services must not restart automatically after installer exit.");
        }

        private static void InstallVersionNeverRunsSubstitutedOrUnlockedInstaller()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            platform.RejectInstaller = true;

            LmControllerInstallResult result = new LmServiceProvisioner(platform)
                .InstallControllerVersion(CreateInstallerRequest());

            AssertEqual(LmServiceProvisioningStatus.Failed, result.Status,
                "A substituted installer must fail before machine state changes.");
            AssertFalse(platform.Events.Contains("LaunchInstaller") ||
                        platform.ContainsEventPrefix("VersionPending:"),
                "An unverified or unlocked source must never launch or stop services.");
        }

        private static void InstallVersionLeavesServicesStoppedWhenVerificationFails()
        {
            FakeLmProvisioningPlatform platform = new FakeLmProvisioningPlatform();
            platform.ManagedSerials.Add("00105700000001");
            platform.InstallerResult = new LmControllerInstallResult
            {
                Status = LmServiceProvisioningStatus.UnsupportedController,
                Message = "installed identity mismatch"
            };

            LmControllerInstallResult result = new LmServiceProvisioner(platform)
                .InstallControllerVersion(CreateInstallerRequest());

            AssertEqual(LmServiceProvisioningStatus.UnsupportedController, result.Status,
                "Post-install identity mismatch must remain explicit.");
            AssertTrue(platform.ContainsEventPrefix("Stop:"), "Managed services must be stopped first.");
            AssertFalse(platform.ContainsEventPrefix("Start:"),
                "Verification failure must never restart an old managed instance.");
        }

        private static void EnsureClearsVersionPendingOnlyAfterRecreatedArtifactsAreReady()
        {
            FakeLmProvisioningPlatform success = new FakeLmProvisioningPlatform();
            success.SetState("00105700000001", LmProvisioningObservedState.VersionPending);
            new LmServiceProvisioner(success).EnsureBatch(
                CreateEnsureRequest(1), NeverCancelLmProvisioning.Instance);
            AssertTrue(success.ContainsEventPrefix("Manifest:"),
                "A ready recreated instance may replace VersionVerificationPending.");

            FakeLmProvisioningPlatform failure = new FakeLmProvisioningPlatform();
            failure.SetState("00105700000001", LmProvisioningObservedState.VersionPending);
            failure.Readiness = LmReadinessResult.Failed("not ready");
            new LmServiceProvisioner(failure).EnsureBatch(
                CreateEnsureRequest(1), NeverCancelLmProvisioning.Instance);
            AssertFalse(failure.ContainsEventPrefix("Manifest:"),
                "A failed recreate must retain VersionVerificationPending ownership state.");
        }

        private static void RemoveDeletesOnlyFullyOwnedFreshlyConfirmedService()
        {
            FakeLmRemovalPlatform platform = new FakeLmRemovalPlatform();
            LmServiceProvisioningItemResult result = new LmServiceRemovalWorkflow(platform)
                .RemoveManaged(CreateRemovalRequest());

            AssertEqual(
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                result.Status,
                "A fully owned and freshly confirmed instance must be removed locally.");

            FakeLmRemovalPlatform stale = new FakeLmRemovalPlatform();
            stale.ConfirmationMatches = false;
            LmServiceProvisioningItemResult staleResult =
                new LmServiceRemovalWorkflow(stale).RemoveManaged(CreateRemovalRequest());
            AssertEqual(LmServiceProvisioningStatus.RemovalBlocked, staleResult.Status,
                "A changed manifest fingerprint must require a fresh confirmation.");
            AssertFalse(stale.ContainsEventPrefix("Stop:"),
                "A stale confirmation must fail before stopping the service.");
        }

        private static void RemoveBlocksOfficialBaseService()
        {
            FakeLmRemovalPlatform platform = new FakeLmRemovalPlatform();
            platform.Ownership = LmRemovalOwnershipState.OfficialBase;

            LmServiceProvisioningItemResult result = new LmServiceRemovalWorkflow(platform)
                .RemoveManaged(CreateRemovalRequest());

            AssertEqual(LmServiceProvisioningStatus.RemovalBlocked, result.Status,
                "The official vendor service must never be removed by this workflow.");
            AssertFalse(platform.ContainsEventPrefix("Stop:"),
                "The official service must not be stopped.");
        }

        private static void RemoveBlocksServiceOnMarkerMismatch()
        {
            FakeLmRemovalPlatform platform = new FakeLmRemovalPlatform();
            platform.Ownership = LmRemovalOwnershipState.MarkerMismatch;

            LmServiceProvisioningItemResult result = new LmServiceRemovalWorkflow(platform)
                .RemoveManaged(CreateRemovalRequest());

            AssertEqual(LmServiceProvisioningStatus.RemovalBlocked, result.Status,
                "A service-description marker mismatch must block removal.");
            AssertFalse(platform.ContainsEventPrefix("Stop:"),
                "Marker mismatch must not mutate SCM.");
        }

        private static void RemoveBlocksServiceOnImageMismatch()
        {
            FakeLmRemovalPlatform platform = new FakeLmRemovalPlatform();
            platform.Ownership = LmRemovalOwnershipState.ImageMismatch;

            LmServiceProvisioningItemResult result = new LmServiceRemovalWorkflow(platform)
                .RemoveManaged(CreateRemovalRequest());

            AssertEqual(LmServiceProvisioningStatus.RemovalBlocked, result.Status,
                "An ImagePath mismatch must block removal.");
            AssertFalse(platform.ContainsEventPrefix("Stop:"),
                "Image mismatch must not mutate SCM.");
        }

        private static void RemoveStopsBeforeDeleteAndWaits()
        {
            FakeLmRemovalPlatform platform = new FakeLmRemovalPlatform();
            new LmServiceRemovalWorkflow(platform).RemoveManaged(CreateRemovalRequest());

            AssertEventOrder(platform.Events,
                "Journal:Deleting", "Stop:00105700000001", "WaitStopped:00105700000001",
                "DeleteService:00105700000001", "ConfirmScmAbsent:00105700000001",
                "Journal:Cleaning");
        }

        private static void RemoveDeletesProfileManifestAndAppMetadata()
        {
            FakeLmRemovalPlatform platform = new FakeLmRemovalPlatform();
            new LmServiceRemovalWorkflow(platform).RemoveManaged(CreateRemovalRequest());

            AssertEventOrder(platform.Events,
                "CleanupProfile:00105700000001",
                "CleanupManifest:00105700000001",
                "CompleteRemoval:00105700000001");

            string root = CreateTemporaryDirectory();
            try
            {
                ManagedServiceManifestStore store = new ManagedServiceManifestStore(
                    root,
                    new FakePathSafety(true),
                    "S-1-5-21-111-222-333-1001");
                string profile = store.EnsureProfileContentDirectory(
                    "00105700000001",
                    TestServiceSid(),
                    "ESP",
                    "lmcontroller");
                string file = Path.Combine(profile, "runtime.tmp");
                File.WriteAllText(file, "runtime");
                File.SetAttributes(file, FileAttributes.ReadOnly);

                store.DeleteProfile("00105700000001", TestServiceSid());

                AssertFalse(Directory.Exists(store.GetProfileRoot("00105700000001")),
                    "Cleanup must remove the entire derived profile, including read-only files.");
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static void RemoveReportsMarkedForDeleteUntilScmAbsence()
        {
            FakeLmRemovalPlatform platform = new FakeLmRemovalPlatform();
            platform.DeletionState = LmScmDeletionState.MarkedForDelete;

            LmServiceProvisioningItemResult result = new LmServiceRemovalWorkflow(platform)
                .RemoveManaged(CreateRemovalRequest());

            AssertEqual(LmServiceProvisioningStatus.MarkedForDelete, result.Status,
                "SCM marked-for-delete is not confirmed absence.");
            AssertFalse(platform.ContainsEventPrefix("CleanupProfile:"),
                "Local data must remain until SCM confirms exact absence.");
        }

        private static void RemoveDoesNotClaimEsmBindingWasCleared()
        {
            FakeLmRemovalPlatform platform = new FakeLmRemovalPlatform();
            LmServiceProvisioningItemResult result = new LmServiceRemovalWorkflow(platform)
                .RemoveManaged(CreateRemovalRequest());

            AssertContains(result.Message, "ЕСМ");
            AssertContains(result.Message, "не очищена");
        }

        private static void RemoveProjectsCleanupPendingAcrossRestartAndSupportsRetry()
        {
            FakeLmRemovalPlatform platform = new FakeLmRemovalPlatform();
            platform.CleanupFails = true;
            LmServiceProvisioningItemResult first = new LmServiceRemovalWorkflow(platform)
                .RemoveManaged(CreateRemovalRequest());
            AssertEqual(LmServiceProvisioningStatus.CleanupPending, first.Status,
                "A locked local file must remain visible as CleanupPending.");
            AssertTrue(platform.ManifestProjectedCleanupPending,
                "CleanupPending must be persisted before recursive deletion.");

            platform.CleanupFails = false;
            platform.Events.Clear();
            LmServiceProvisioningItemResult retried = new LmServiceRemovalWorkflow(platform)
                .CleanupManaged(CreateCleanupRequest());
            AssertEqual(
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                retried.Status,
                "A later in-app cleanup must finish without manual folder search.");
            AssertTrue(platform.ContainsEventPrefix("CleanupProfile:"),
                "The reopened cleanup path must retry derived local artifacts.");
        }

        private static void RemoveRemainsPossibleAfterVendorBinaryUpdate()
        {
            FakeLmRemovalPlatform platform = new FakeLmRemovalPlatform();
            platform.CurrentVendorBinaryMatchesManifest = false;

            LmServiceProvisioningItemResult result = new LmServiceRemovalWorkflow(platform)
                .RemoveManaged(CreateRemovalRequest());

            AssertEqual(
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                result.Status,
                "Removal must validate stored ownership paths without executing an old vendor binary.");
            AssertFalse(platform.Events.Contains("VerifyCurrentVendorBinary"),
                "Removal must not require the replaced vendor binary identity.");
        }

        private static void AssertEventOrder(IList<string> events, params string[] expected)
        {
            int previous = -1;
            for (int index = 0; index < expected.Length; index++)
            {
                int actual = events.IndexOf(expected[index]);
                if (actual <= previous)
                {
                    throw new InvalidOperationException(
                        "Expected event order at '" + expected[index] + "'. Actual: " +
                        string.Join(" | ", new List<string>(events).ToArray()));
                }
                previous = actual;
            }
        }

        private static int FindUnusedDualStackPort(params int[] excluded)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                using (Socket socket = CreateExclusiveSocket(
                    AddressFamily.InterNetworkV6,
                    false))
                {
                    socket.DualMode = true;
                    socket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
                    int port = ((IPEndPoint)socket.LocalEndPoint).Port;
                    bool skip = false;
                    for (int index = 0; index < excluded.Length; index++)
                    {
                        skip |= excluded[index] == port;
                    }
                    if (!skip)
                    {
                        return port;
                    }
                }
            }
            throw new InvalidOperationException("Could not select two distinct test ports.");
        }

        private static Socket CreateExclusiveSocket(
            AddressFamily family,
            bool dualMode)
        {
            Socket socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
            socket.ExclusiveAddressUse = true;
            socket.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                false);
            if (family == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = dualMode;
            }
            return socket;
        }

        private static void BindAndClose(
            AddressFamily family,
            int port,
            bool dualMode)
        {
            using (Socket socket = CreateExclusiveSocket(family, dualMode))
            {
                socket.Bind(new IPEndPoint(
                    family == AddressFamily.InterNetwork
                        ? IPAddress.Any
                        : IPAddress.IPv6Any,
                    port));
            }
        }

        private static LmGatewaySupervisorService CreateTestSupervisorService(
            FakeWindowsServiceApi api)
        {
            return new LmGatewaySupervisorService(
                api,
                ControllerCapabilityProfile.SupportedVersion1632(),
                VerifiedProvisionerBinary.CreateForTesting(
                    @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe"));
        }

        private static LmControllerChildProcess CreateTestChildProcess(
            IDictionary<string, string> environment,
            FakeControllerChildRuntime runtime)
        {
            return new LmControllerChildProcess(
                ControllerCapabilityProfile.SupportedVersion1632(),
                new VerifiedControllerBinary
                {
                    FullPath = @"C:\Program Files\ESP\LMController\bin\lmcontroller.exe",
                    Version = "1.6.3.2",
                    Sha256 = new string('a', 64),
                    SignerThumbprint = "1CD26372850FE30F1559821CF5D318591695271A",
                    Machine = PeMachine.Amd64
                },
                @"C:\ProgramData\KRS\MultiKKT\Profiles\krs-esm-lm-00105700000001",
                new FakeProcessEnvironmentReader(environment),
                runtime);
        }

        private static LmServiceProvisioningBatchRequest CreateEnsureRequest(int count)
        {
            LmServiceProvisioningBatchRequest request = CreateRequest(LmServiceOperation.EnsureBatch);
            for (int index = 0; index < count; index++)
            {
                request.Items.Add(CreateEnsureItem(index));
            }

            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            return request;
        }

        private static LmServiceProvisioningBatchRequest CreateInstallerRequest()
        {
            LmServiceProvisioningBatchRequest request = CreateRequest(LmServiceOperation.InstallControllerVersion);
            request.InstallerSelection = CreateInstallerSelection();
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            return request;
        }

        private static LmServiceProvisioningBatchRequest CreateRemovalRequest()
        {
            LmServiceProvisioningBatchRequest request = CreateRequest(LmServiceOperation.RemoveManaged);
            request.RemovalConfirmation = new LmRemovalConfirmation
            {
                KktSerial = "00105700000001",
                GrpcPort = 55000,
                RestPort = 15000,
                ManifestFingerprint = new LmManifestFingerprint { Sha256 = new string('c', 64) },
                RetainedEsmWarningAccepted = true
            };
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            return request;
        }

        private static void RemoveAllManagedProcessesEveryConfirmedServiceInOneBatch()
        {
            FakeLmRemovalPlatform removal = new FakeLmRemovalPlatform();
            removal.PreserveOwnershipAcrossSerials = true;
            LmServiceProvisioningBatchResult result = new LmServiceProvisioner(
                new FakeLmProvisioningPlatform(),
                removal).RemoveAllManaged(
                    CreateRemoveAllRequest(2),
                    NeverCancelLmProvisioning.Instance);

            AssertEqual(2, result.Items.Count,
                "Every confirmed managed service must receive its own result.");
            AssertEqual(
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                result.Items[0].Status,
                "The first managed service must be removed completely.");
            AssertEqual(
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                result.Items[1].Status,
                "The second managed service must be removed completely.");
            AssertTrue(removal.Events.Contains("Stop:00105700000001"),
                "The first service must be stopped.");
            AssertTrue(removal.Events.Contains("Stop:00105700000002"),
                "The second service must be stopped in the same helper operation.");
        }

        private static LmServiceProvisioningBatchRequest CreateRemoveAllRequest(int count)
        {
            LmServiceProvisioningBatchRequest request =
                CreateRequest(LmServiceOperation.RemoveAllManaged);
            for (int index = 0; index < count; index++)
            {
                request.RemovalConfirmations.Add(new LmRemovalConfirmation
                {
                    KktSerial = "001057000000" + (index + 1).ToString("00"),
                    GrpcPort = 55001 + index,
                    RestPort = 15001 + index,
                    ManifestFingerprint = new LmManifestFingerprint
                    {
                        Sha256 = new string((char)('a' + index), 64)
                    },
                    RetainedEsmWarningAccepted = true
                });
            }
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            return request;
        }

        private static LmServiceProvisioningBatchRequest CreateCleanupRequest()
        {
            LmServiceProvisioningBatchRequest request = CreateRequest(LmServiceOperation.CleanupManaged);
            request.CleanupConfirmation = new LmCleanupConfirmation
            {
                KktSerial = "00105700000001",
                ManifestFingerprint = new LmManifestFingerprint { Sha256 = new string('d', 64) },
                DisplayedState = LmServiceProvisioningStatus.CleanupPending
            };
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            return request;
        }

        private static LmServiceProvisioningBatchRequest CreateRequest(LmServiceOperation operation)
        {
            return new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 1,
                Operation = operation,
                OperationId = Guid.NewGuid().ToString("N"),
                InitiatingSid = "S-1-5-21-111-222-333-1001"
            };
        }

        private static LmServiceProvisioningItemRequest CreateEnsureItem(int index)
        {
            return new LmServiceProvisioningItemRequest
            {
                KktSerial = (105700000001L + index).ToString("00000000000000"),
                GrpcPort = 55000 + index,
                RestPort = 15000 + index,
                TargetAddress = "10.20.30." + (40 + index).ToString(),
                TargetPort = 5995
            };
        }

        private static LmControllerInstallerSelection CreateInstallerSelection()
        {
            return new LmControllerInstallerSelection
            {
                SourcePath = @"C:\Users\operator\Downloads\esm-lm-controller_1.6.3.2-windows-setup.exe",
                FileName = "esm-lm-controller_1.6.3.2-windows-setup.exe",
                ByteLength = 12345678,
                Sha256 = new string('a', 64),
                FileVersion = "1.6.3.2",
                ProductVersion = "1.6.3.2",
                SignerSubject = "CN=JSC ESP",
                SignerThumbprint = "1CD26372850FE30F1559821CF5D318591695271A",
                StopManagedInstancesWarningAccepted = true
            };
        }

        private static LmServiceProvisioningBatchRequest CreateManagedLocalModuleRequest(int count)
        {
            LmServiceProvisioningBatchRequest request =
                CreateRequest(LmServiceOperation.EnsureManagedLocalModules);
            request.InstallerSelection = CreateInstallerSelection();
            request.LocalModuleInstallerSelection = new LocalModuleInstallerSelection
            {
                SourcePath = @"C:\Users\operator\Downloads\regime-2.6.1-7.msi",
                FileName = "regime-2.6.1-7.msi",
                ByteLength = 51007488,
                Sha256 = "68a9633cefc912c2c1defae40d1c8f433bb794ee66060b822f0895410ab6c5c6",
                ProductName = "Локальный модуль Честный Знак",
                ProductVersion = "2.6.1",
                ProductCode = "{556FD8AD-43A3-4645-BC54-EBF3043ADF82}",
                UpgradeCode = "{9449123B-61C4-40DE-AA6C-1BB9AA02EB67}",
                SignerSubject = "CN=ООО ЦЕНТР РАЗВИТИЯ ПЕРСПЕКТИВНЫХ ТЕХНОЛОГИЙ",
                SignerThumbprint = "6BA5F6BBE4BE27658253C78889334D0E24858C19",
                LicenseNoticeAccepted = true
            };
            for (int index = 0; index < count; index++)
            {
                int ordinal = index + 1;
                request.ManagedLocalModules.Add(new ManagedLocalModuleProvisioningItemRequest
                {
                    KktSerial = (105700000001L + index).ToString("00000000000000"),
                    Inn = (1000000002L + (index * 1000000001L)).ToString("0000000000"),
                    KktOrdinal = ordinal,
                    LocalModuleOrdinal = ordinal,
                    ApiPort = 4995 + (1000 * ordinal),
                    DatabasePort = 4984 + (1000 * ordinal),
                    EpmdPort = 43690 + ordinal,
                    ControllerGrpcPort = 45000 + ordinal,
                    ControllerRestPort = 15000 + ordinal,
                    RuntimeVersion = "2.6.1"
                });
            }
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            return request;
        }

        private static LocalModuleInstallerSelection CreateLocalModuleInstallerSelection(
            string path,
            byte[] content)
        {
            return new LocalModuleInstallerSelection
            {
                SourcePath = path,
                FileName = Path.GetFileName(path),
                ByteLength = content.Length,
                Sha256 = ComputeSha256(content),
                ProductName = "Локальный модуль Честный Знак",
                ProductVersion = "2.6.1",
                ProductCode = "{556FD8AD-43A3-4645-BC54-EBF3043ADF82}",
                UpgradeCode = "{9449123B-61C4-40DE-AA6C-1BB9AA02EB67}",
                SignerSubject = "CN=Test CRPT",
                SignerThumbprint = "6BA5F6BBE4BE27658253C78889334D0E24858C19",
                LicenseNoticeAccepted = true
            };
        }

        private static LocalModuleInstallerSelection CloneLocalModuleInstallerSelection(
            LocalModuleInstallerSelection source)
        {
            return new LocalModuleInstallerSelection
            {
                SourcePath = source.SourcePath,
                FileName = source.FileName,
                ByteLength = source.ByteLength,
                Sha256 = source.Sha256,
                ProductName = source.ProductName,
                ProductVersion = source.ProductVersion,
                ProductCode = source.ProductCode,
                UpgradeCode = source.UpgradeCode,
                SignerSubject = source.SignerSubject,
                SignerThumbprint = source.SignerThumbprint,
                LicenseNoticeAccepted = source.LicenseNoticeAccepted
            };
        }

        private static WindowsInstallerPackageMetadata CreateLocalModulePackageMetadata()
        {
            return new WindowsInstallerPackageMetadata
            {
                ProductName = "Локальный модуль Честный Знак",
                ProductVersion = "2.6.1",
                ProductCode = "{556FD8AD-43A3-4645-BC54-EBF3043ADF82}",
                UpgradeCode = "{9449123B-61C4-40DE-AA6C-1BB9AA02EB67}"
            };
        }

        private static TrustedFileExpectation CreateLocalModuleTrustExpectation(
            LocalModuleInstallerSelection selection)
        {
            return new TrustedFileExpectation
            {
                FileName = selection.FileName,
                ByteLength = selection.ByteLength,
                Sha256 = selection.Sha256,
                FileVersion = string.Empty,
                ProductVersion = string.Empty,
                ProductName = string.Empty,
                CompanyName = string.Empty,
                Machine = PeMachine.Unknown,
                SignerSubject = selection.SignerSubject,
                SignerThumbprint = selection.SignerThumbprint,
                RequireCodeSigningEku = true
            };
        }

        private static LocalModuleTemplateObservation CreateLocalModuleTemplateObservation()
        {
            return new LocalModuleTemplateObservation
            {
                RegimeLocalIni =
                    "[api]\n" +
                    "ip_address = 0.0.0.0\n" +
                    "port = 5995\n" +
                    "[local]\n" +
                    "db_url = http://127.0.0.1:5984\n" +
                    ";key_store_folder =\n" +
                    "[remote]\n",
                YeniseiLocalIni =
                    "[admins]\n" +
                    "[chttpd]\n" +
                    "bind_address = 0.0.0.0\n" +
                    "port = 5984\n" +
                    "[log]\n" +
                    "file = var/log/yenisei.log\n" +
                    "[couchdb]\n" +
                    "database_dir = ./yenisei/data\n" +
                    "view_index_dir = ./yenisei/data\n"
            };
        }

        private static void CreateTestManagedLocalModuleOwnership(
            string root,
            out LocalModuleManifestStore store,
            out LocalModuleRuntimeManifest runtime,
            out LocalModuleInstanceManifest instance)
        {
            string appRoot = Path.Combine(root, "app");
            string runtimeContainer = Path.Combine(root, "program", "LocalModuleRuntime");
            store = new LocalModuleManifestStore(
                appRoot,
                runtimeContainer,
                new FakePathSafety(true),
                null);
            LocalModuleInstallerSelection package =
                LocalModulePackageVerifier.CreateSupportedIdentity();
            LocalModuleCapabilityProfile capability =
                LocalModuleCapabilityProfile.Resolve("2.6.1");
            string runtimeNonce = "55555555555555555555555555555555";
            string runtimeId = LocalModuleManagedIdentity.CreateRuntimeId(
                capability.CapabilityId,
                package.Sha256);
            string runtimeRoot = store.GetRuntimeRoot(runtimeId);
            byte[] runtimeBytes = Encoding.ASCII.GetBytes("owned-runtime");
            string runtimeFile = Path.Combine(runtimeRoot, "lib", "sample.beam");
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeFile));
            File.WriteAllBytes(runtimeFile, runtimeBytes);
            runtime = LocalModuleRuntimeManifest.Create(
                package,
                capability,
                runtimeRoot,
                runtimeNonce,
                new[] { new LocalModuleRuntimeFile(
                    @"lib\sample.beam",
                    runtimeBytes.Length,
                    ComputeSha256(runtimeBytes)) });
            store.WriteRuntime(runtime);

            ManagedLocalModuleProvisioningItemRequest item =
                CreateManagedLocalModuleRequest(1).ManagedLocalModules[0];
            string instanceNonce = "66666666666666666666666666666666";
            string instanceId = LocalModuleManagedIdentity.CreateInstanceId(
                item.Inn,
                instanceNonce);
            string profileRoot = store.GetInstanceRoot(instanceId);
            LocalModuleConfiguration configuration = LocalModuleConfigurationWriter.Build(
                capability,
                runtimeRoot,
                profileRoot,
                item,
                instanceNonce,
                CreateLocalModuleTemplateObservation());
            instance = LocalModuleInstanceManifest.Create(
                item,
                instanceId,
                runtime.RuntimeId,
                runtimeRoot,
                configuration,
                instanceNonce,
                "cccccccccccccccccccccccccccccccc");
            store.WriteInstance(instance);
        }

        private static string ReadLineStarting(string text, string prefix)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].StartsWith(prefix, StringComparison.Ordinal))
                {
                    return lines[index];
                }
            }
            return string.Empty;
        }

        private static string FindRequiredFileHash(
            LocalModuleCapabilityProfile capability,
            string relativePath)
        {
            for (int index = 0; index < capability.RequiredFiles.Count; index++)
            {
                LocalModuleRequiredFile file = capability.RequiredFiles[index];
                if (string.Equals(
                        file.RelativePath,
                        relativePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return file.Sha256;
                }
            }
            throw new InvalidOperationException(
                "Required file is absent from the exact capability profile.");
        }

        private static ProvisioningPeerEvidence CreateValidServerEvidence()
        {
            return new ProvisioningPeerEvidence
            {
                ExpectedSid = "S-1-5-21-111-222-333-1001",
                ActualSid = "S-1-5-21-111-222-333-1001",
                ExpectedImagePath = @"C:\Program Files\KRS\MultiKKT\MultiKKT.exe",
                ActualImagePath = @"C:\Program Files\KRS\MultiKKT\MultiKKT.exe",
                ExpectedProcessId = 0,
                ActualProcessId = 3100,
                IsImagePathProtected = true,
                HasExpectedMetadata = true,
                HasReparseComponent = false,
                IsHighIntegrity = false,
                MaxServerInstances = 1,
                IsSecondServerAttempt = false
            };
        }

        private static ProvisioningPeerEvidence CloneEvidence(ProvisioningPeerEvidence source)
        {
            return new ProvisioningPeerEvidence
            {
                ExpectedSid = source.ExpectedSid,
                ActualSid = source.ActualSid,
                ExpectedImagePath = source.ExpectedImagePath,
                ActualImagePath = source.ActualImagePath,
                ExpectedProcessId = source.ExpectedProcessId,
                ActualProcessId = source.ActualProcessId,
                IsImagePathProtected = source.IsImagePathProtected,
                HasExpectedMetadata = source.HasExpectedMetadata,
                HasReparseComponent = source.HasReparseComponent,
                IsHighIntegrity = source.IsHighIntegrity,
                MaxServerInstances = source.MaxServerInstances,
                IsSecondServerAttempt = source.IsSecondServerAttempt
            };
        }

        private static string CreateTemporaryDirectory()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "esm_tspiot_provisioner_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteTestTreeWithReadOnlyFiles(string root)
        {
            if (!Directory.Exists(root)) return;
            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            for (int index = 0; index < files.Length; index++)
            {
                File.SetAttributes(files[index], FileAttributes.Normal);
            }
            Directory.Delete(root, true);
        }

        private static ControllerCapabilityProfile CreateTestCapabilityProfile(
            string installRoot,
            string binaryPath,
            string installerPath)
        {
            string actualBinaryPath = binaryPath ?? Path.Combine(installRoot, "bin", "lmcontroller.exe");
            string actualInstallerPath = installerPath ?? Path.Combine(
                installRoot,
                "esm-lm-controller_1.6.3.2-windows-setup.exe");
            TrustedFileExpectation binary = CreateExpectation(actualBinaryPath, "lmcontroller.exe");
            TrustedFileExpectation installer = CreateExpectation(
                actualInstallerPath,
                "esm-lm-controller_1.6.3.2-windows-setup.exe");
            installer.FileVersion = "1.6.3.2";
            installer.ProductVersion = string.Empty;

            return ControllerCapabilityProfile.CreateForTesting(
                "1.6.3.2",
                installRoot,
                Path.Combine("bin", "lmcontroller.exe"),
                binary,
                installer,
                "ProgramData");
        }

        private static TrustedFileExpectation CreateExpectation(string path, string fileName)
        {
            byte[] content = File.Exists(path) ? File.ReadAllBytes(path) : Encoding.UTF8.GetBytes(fileName);
            return new TrustedFileExpectation
            {
                FileName = fileName,
                ByteLength = content.Length,
                Sha256 = ComputeSha256(content),
                FileVersion = string.Empty,
                ProductVersion = string.Empty,
                ProductName = string.Empty,
                CompanyName = string.Empty,
                Machine = PeMachine.Amd64,
                SignerSubject = "CN=JSC ESP",
                SignerThumbprint = "1CD26372850FE30F1559821CF5D318591695271A",
                RequireCodeSigningEku = true
            };
        }

        private static LmControllerInstallerSelection CreateSelectionFromExpectation(
            string sourcePath,
            TrustedFileExpectation expectation)
        {
            return new LmControllerInstallerSelection
            {
                SourcePath = sourcePath,
                FileName = expectation.FileName,
                ByteLength = expectation.ByteLength,
                Sha256 = expectation.Sha256,
                FileVersion = expectation.FileVersion,
                ProductVersion = expectation.ProductVersion,
                SignerSubject = expectation.SignerSubject,
                SignerThumbprint = expectation.SignerThumbprint,
                StopManagedInstancesWarningAccepted = true
            };
        }

        private static ManagedServiceManifest CreateTestManifest(ManagedServiceLifecycleState state)
        {
            return ManagedServiceManifest.Create(
                "00105700000001",
                new LmGatewayPorts(55000, 15000),
                new LmGatewayTarget("10.20.30.40", 5995),
                "1.6.3.2",
                new string('a', 64),
                new string('b', 64),
                "S-1-5-80-123456789-123456789-123456789-123456789-123456789",
                Guid.NewGuid().ToString("N"),
                state,
                @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe",
                @"C:\ProgramData\KRS\MultiKKT\Profiles\krs-esm-lm-00105700000001");
        }

        private static ProvisioningOperationJournal CreateTestJournal(
            string operationId,
            string kktSerial,
            int grpcPort,
            int restPort)
        {
            string serviceName = LmServiceIdentity.CreateName(kktSerial);
            return new ProvisioningOperationJournal
            {
                SchemaVersion = 1,
                OperationId = operationId,
                Operation = LmServiceOperation.EnsureBatch,
                KktSerial = kktSerial,
                State = ManagedServiceLifecycleState.Creating,
                ManifestFingerprint = string.Empty,
                UpdatedUtc = DateTime.UtcNow.ToString("o"),
                Stage = LmProvisioningJournalStage.Preparing,
                GrpcPort = grpcPort,
                RestPort = restPort,
                TargetAddress = "10.20.30.40",
                TargetPort = 5995,
                ServiceName = serviceName,
                SupervisorImagePath =
                    @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe",
                ProfilePath = @"C:\ProgramData\KRS\MultiKKT\Profiles\" + serviceName,
                ServiceSid = RestrictedServiceSid.Derive(serviceName),
                ControllerVersion = "1.6.3.2",
                ControllerBinarySha256 = new string('a', 64),
                SupervisorSha256 = new string('b', 64)
            };
        }

        private static string ComputeSha256(byte[] content)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(content);
                StringBuilder result = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    result.Append(hash[index].ToString("x2"));
                }
                return result.ToString();
            }
        }

        private static bool ContainsForbiddenPropertyFragment(string name)
        {
            string[] fragments =
            {
                "Password", "Login", "Credential", "Secret", "Token", "Command",
                "Argument", "Environment", "BinaryPath", "ProfilePath", "ServiceName"
            };
            for (int index = 0; index < fragments.Length; index++)
            {
                if (name.IndexOf(fragments[index], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Run(string name, Action action)
        {
            _testCount++;
            try
            {
                action();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine("FAIL " + name);
                Console.WriteLine(ex.Message);
            }
        }

        private static void AssertTrue(bool value, string message)
        {
            if (!value)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertFalse(bool value, string message)
        {
            if (value)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    message + " Expected: " + expected + ". Actual: " + actual + ".");
            }
        }

        private static void AssertContains(string text, string expected)
        {
            if (text == null || text.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    "Expected text to contain: " + expected + ". Actual: " + text);
            }
        }

        private static void AssertThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }

        private sealed class CancelAfterCompletedItems : ILmProvisioningCancellation
        {
            private readonly FakeLmProvisioningPlatform _platform;
            private readonly int _completedItems;

            internal CancelAfterCompletedItems(
                FakeLmProvisioningPlatform platform,
                int completedItems)
            {
                _platform = platform;
                _completedItems = completedItems;
            }

            public bool IsCancellationRequested
            {
                get { return _platform.CompletedItems >= _completedItems; }
            }
        }

        private sealed class FakeLmRemovalPlatform : ILmServiceRemovalPlatform
        {
            internal FakeLmRemovalPlatform()
            {
                Events = new List<string>();
                Ownership = LmRemovalOwnershipState.FullyOwnedRunning;
                DeletionState = LmScmDeletionState.Absent;
                ConfirmationMatches = true;
                CurrentVendorBinaryMatchesManifest = true;
            }

            internal List<string> Events { get; private set; }
            internal LmRemovalOwnershipState Ownership { get; set; }
            internal LmScmDeletionState DeletionState { get; set; }
            internal bool ConfirmationMatches { get; set; }
            internal bool CleanupFails { get; set; }
            internal bool ManifestProjectedCleanupPending { get; private set; }
            internal bool CurrentVendorBinaryMatchesManifest { get; set; }
            internal bool PreserveOwnershipAcrossSerials { get; set; }

            internal bool ContainsEventPrefix(string prefix)
            {
                for (int index = 0; index < Events.Count; index++)
                {
                    if (Events[index].StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                return false;
            }

            public IDisposable AcquireMachineLock()
            {
                Events.Add("MachineLock");
                return new CallbackDisposable(delegate { });
            }

            public IDisposable AcquireItemLock(string kktSerial)
            {
                Events.Add("ItemLock:" + kktSerial);
                return new CallbackDisposable(delegate { });
            }

            public void ReconcileRemoval(string kktSerial, string operationId)
            {
                Events.Add("ReconcileRemoval:" + kktSerial);
            }

            public LmRemovalOwnershipState InspectRemoval(
                string kktSerial,
                string manifestFingerprint,
                bool cleanupOnly)
            {
                Events.Add("InspectRemoval:" + kktSerial);
                if (!ConfirmationMatches)
                {
                    return LmRemovalOwnershipState.ConfirmationMismatch;
                }
                if (cleanupOnly && ManifestProjectedCleanupPending)
                {
                    return LmRemovalOwnershipState.CleanupOnly;
                }
                return Ownership;
            }

            public void WriteRemovalJournal(
                string kktSerial,
                string operationId,
                LmServiceOperation operation,
                ManagedServiceLifecycleState state,
                string manifestFingerprint)
            {
                Events.Add("Journal:" + state.ToString());
            }

            public void RequestNormalStop(string kktSerial)
            {
                Events.Add("Stop:" + kktSerial);
            }

            public void WaitUntilStopped(string kktSerial)
            {
                Events.Add("WaitStopped:" + kktSerial);
            }

            public LmScmDeletionState DeleteServiceAndConfirmAbsent(string kktSerial)
            {
                Events.Add("DeleteService:" + kktSerial);
                Events.Add("ConfirmScmAbsent:" + kktSerial);
                return DeletionState;
            }

            public void MarkCleanupPending(
                string kktSerial,
                string operationId,
                string errorClass)
            {
                ManifestProjectedCleanupPending = true;
                Events.Add("CleanupPending:" + kktSerial);
            }

            public void CleanupProfile(string kktSerial)
            {
                Events.Add("CleanupProfile:" + kktSerial);
                if (CleanupFails)
                {
                    throw new IOException("profile locked");
                }
            }

            public void CleanupManifest(string kktSerial)
            {
                Events.Add("CleanupManifest:" + kktSerial);
            }

            public void CompleteRemoval(string kktSerial)
            {
                Events.Add("CompleteRemoval:" + kktSerial);
                if (!PreserveOwnershipAcrossSerials)
                {
                    Ownership = LmRemovalOwnershipState.Missing;
                }
                ManifestProjectedCleanupPending = false;
            }
        }

        private sealed class FakeLmProvisioningPlatform : ILmProvisioningPlatform
        {
            private readonly Dictionary<string, LmProvisioningObservedState> _states =
                new Dictionary<string, LmProvisioningObservedState>(StringComparer.Ordinal);
            private bool _reservationHeld;
            private bool _profileSawReservation;
            private bool _scmSawReservation;
            private bool _throwInjected;

            internal FakeLmProvisioningPlatform()
            {
                Events = new List<string>();
                ManagedSerials = new List<string>();
                Readiness = LmReadinessResult.Ready();
                InstallerResult = new LmControllerInstallResult
                {
                    Status = LmServiceProvisioningStatus.Succeeded,
                    Message = "installed"
                };
            }

            internal List<string> Events { get; private set; }
            internal List<string> ManagedSerials { get; private set; }
            internal LmReadinessResult Readiness { get; set; }
            internal LmControllerInstallResult InstallerResult { get; set; }
            internal string FailSerial { get; set; }
            internal string ThrowOnceAtPrefix { get; set; }
            internal bool RejectInstaller { get; set; }
            internal bool RequireReservationReleasedBeforeStart { get; set; }
            internal int CurrentMachineLockDepth { get; private set; }
            internal int CurrentItemLockDepth { get; private set; }
            internal int MaximumMachineLockDepth { get; private set; }
            internal int MaximumItemLockDepth { get; private set; }
            internal int CompletedItems { get; private set; }

            internal bool ReservationHeldDuringProfileAndScm
            {
                get { return _profileSawReservation && _scmSawReservation; }
            }

            internal void SetState(string serial, LmProvisioningObservedState state)
            {
                _states[serial] = state;
            }

            internal bool ContainsEventPrefix(string prefix)
            {
                for (int index = 0; index < Events.Count; index++)
                {
                    if (Events[index].StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                return false;
            }

            public IDisposable AcquireMachineLock()
            {
                Record("MachineLock");
                CurrentMachineLockDepth++;
                MaximumMachineLockDepth = Math.Max(
                    MaximumMachineLockDepth,
                    CurrentMachineLockDepth);
                return new CallbackDisposable(delegate { CurrentMachineLockDepth--; });
            }

            public IDisposable AcquireItemLock(string kktSerial)
            {
                Record("ItemLock:" + kktSerial);
                CurrentItemLockDepth++;
                MaximumItemLockDepth = Math.Max(MaximumItemLockDepth, CurrentItemLockDepth);
                return new CallbackDisposable(delegate { CurrentItemLockDepth--; });
            }

            public void Reconcile(LmServiceProvisioningItemRequest item, string operationId)
            {
                Record("Reconcile:" + item.KktSerial);
            }

            public LmVerifiedController VerifyController()
            {
                Record("VerifyController");
                return new LmVerifiedController
                {
                    Version = "1.6.3.2",
                    BinarySha256 = new string('a', 64),
                    SupervisorSha256 = new string('b', 64)
                };
            }

            public LmProvisioningObservedState Inspect(
                LmServiceProvisioningItemRequest item,
                string initiatingSid)
            {
                Record("Inspect:" + item.KktSerial);
                if (string.Equals(item.KktSerial, FailSerial, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("injected item failure");
                }
                LmProvisioningObservedState state;
                return _states.TryGetValue(item.KktSerial, out state)
                    ? state
                    : LmProvisioningObservedState.Absent;
            }

            public IDisposable ReservePorts(LmServiceProvisioningItemRequest item)
            {
                Record("Reserve:" + item.KktSerial);
                if (_reservationHeld)
                {
                    throw new InvalidOperationException("reservation already held");
                }
                _reservationHeld = true;
                return new CallbackDisposable(delegate
                {
                    if (_reservationHeld)
                    {
                        _reservationHeld = false;
                        Record("ReleasePorts:" + item.KktSerial);
                    }
                });
            }

            public string DeriveServiceSid(string kktSerial)
            {
                return TestServiceSid();
            }

            public void WriteJournal(
                LmServiceProvisioningItemRequest item,
                string operationId,
                LmProvisioningJournalStage stage)
            {
                Record("Journal:" + stage.ToString());
            }

            public void PrepareProfile(LmServiceProvisioningItemRequest item, string serviceSid)
            {
                _profileSawReservation |= _reservationHeld;
                Record("Profile:" + item.KktSerial);
            }

            public void ConfigureService(
                LmServiceProvisioningItemRequest item,
                string serviceSid,
                string initiatingSid)
            {
                _scmSawReservation |= _reservationHeld;
                Record("Configure:" + item.KktSerial);
            }

            public void Start(LmServiceProvisioningItemRequest item)
            {
                if (RequireReservationReleasedBeforeStart && _reservationHeld)
                {
                    throw new InvalidOperationException("ports still reserved at StartService");
                }
                Record("Start:" + item.KktSerial);
            }

            public void RequestStop(LmServiceProvisioningItemRequest item)
            {
                Record("Stop:" + item.KktSerial);
            }

            public LmReadinessResult Probe(LmServiceProvisioningItemRequest item)
            {
                Record("Probe:" + item.KktSerial);
                return Readiness;
            }

            public void WriteManifest(
                LmServiceProvisioningItemRequest item,
                LmVerifiedController controller,
                string serviceSid,
                string operationId)
            {
                Record("Manifest:" + item.KktSerial);
                _states[item.KktSerial] = LmProvisioningObservedState.MatchingReady;
            }

            public void CompleteJournal(
                LmServiceProvisioningItemRequest item,
                string operationId)
            {
                Record("CompleteJournal:" + item.KktSerial);
                CompletedItems++;
            }

            public IList<string> GetManagedSerials()
            {
                return new List<string>(ManagedSerials);
            }

            public void MarkVersionPending(string kktSerial, string operationId)
            {
                Record("VersionPending:" + kktSerial);
            }

            public ILockedControllerInstaller PrepareInstaller(
                LmControllerInstallerSelection selection,
                string operationId)
            {
                Record("PrepareInstaller");
                if (RejectInstaller)
                {
                    throw new InvalidDataException("installer changed");
                }
                return new FakeLockedInstaller(this);
            }

            private void Record(string value)
            {
                Events.Add(value);
                if (!_throwInjected &&
                    !string.IsNullOrEmpty(ThrowOnceAtPrefix) &&
                    value.StartsWith(ThrowOnceAtPrefix, StringComparison.Ordinal))
                {
                    _throwInjected = true;
                    throw new InvalidOperationException("simulated crash at " + value);
                }
            }

            private sealed class FakeLockedInstaller : ILockedControllerInstaller
            {
                private readonly FakeLmProvisioningPlatform _owner;

                internal FakeLockedInstaller(FakeLmProvisioningPlatform owner)
                {
                    _owner = owner;
                }

                public LmControllerInstallResult Run()
                {
                    _owner.Record("LaunchInstaller");
                    return _owner.InstallerResult;
                }

                public void Dispose()
                {
                }
            }
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action _callback;

            internal CallbackDisposable(Action callback)
            {
                _callback = callback;
            }

            public void Dispose()
            {
                Action callback = _callback;
                _callback = null;
                if (callback != null)
                {
                    callback();
                }
            }
        }

        private sealed class FakePathSafety : IPathSafety
        {
            private readonly bool _isSafe;

            internal FakePathSafety(bool isSafe)
            {
                _isSafe = isSafe;
            }

            public ValidationResult Validate(string path, string requiredRoot)
            {
                ValidationResult result = new ValidationResult();
                if (!_isSafe)
                {
                    result.Add("reparse path rejected");
                }
                return result;
            }

            public ValidationResult ValidateProtected(
                string path,
                string requiredRoot,
                string allowedWriterSid)
            {
                return Validate(path, requiredRoot);
            }

            public ValidationResult ValidateProtectedForServices(
                string path,
                string requiredRoot,
                IList<string> allowedWriterSids)
            {
                return Validate(path, requiredRoot);
            }

            public void EnsureProtectedDirectory(
                string path,
                ProtectedDirectoryKind kind,
                string readOnlySid,
                string serviceSid)
            {
                if (!_isSafe)
                {
                    throw new InvalidDataException("reparse path rejected");
                }
                Directory.CreateDirectory(path);
            }

            public void EnsureProtectedDirectoryForServices(
                string path,
                ProtectedDirectoryKind kind,
                string readOnlySid,
                IList<string> serviceSids)
            {
                EnsureProtectedDirectory(path, kind, readOnlySid, null);
            }

            public void EnsureProtectedRuntimeFile(string path)
            {
                if (!_isSafe)
                {
                    throw new InvalidDataException("reparse path rejected");
                }
            }

            public void EnsureProtectedReadOnlyFile(
                string path,
                IList<string> readOnlySids)
            {
                EnsureProtectedRuntimeFile(path);
            }
        }

        private sealed class FakeManagedProvisioningSessionChannel :
            IManagedProvisioningSessionChannel
        {
            private readonly Queue<ManagedProvisioningSessionMessage> _incoming;

            internal FakeManagedProvisioningSessionChannel(
                IEnumerable<ManagedProvisioningSessionMessage> incoming)
            {
                _incoming = new Queue<ManagedProvisioningSessionMessage>(incoming);
                SessionMessages = new List<ManagedProvisioningSessionMessage>();
            }

            internal IList<ManagedProvisioningSessionMessage> SessionMessages
            {
                get;
                private set;
            }

            public ManagedProvisioningSessionMessage ReadSessionMessage()
            {
                if (_incoming.Count == 0)
                {
                    throw new EndOfStreamException(
                        "The fake session has no caller message.");
                }
                return _incoming.Dequeue();
            }

            public void WriteSessionMessage(
                ManagedProvisioningSessionMessage message)
            {
                SessionMessages.Add(message);
            }
        }

        private static void AssertSequence(
            IList<string> expected,
            IList<string> actual,
            string message)
        {
            if (expected == null || actual == null || expected.Count != actual.Count)
            {
                throw new InvalidOperationException(
                    message + " Expected " +
                    (expected == null ? "<null>" : expected.Count.ToString()) +
                    " event(s), actual " +
                    (actual == null ? "<null>" : actual.Count.ToString()) + ".");
            }
            for (int index = 0; index < expected.Count; index++)
            {
                if (!string.Equals(expected[index], actual[index], StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        message + " Difference at index " + index.ToString() +
                        ": expected '" + expected[index] + "', actual '" +
                        actual[index] + "'.");
                }
            }
        }

        private sealed class FakeWindowsServiceApi : IWindowsServiceApi
        {
            private WindowsServiceRecord _record;

            internal WindowsServiceDefinition LastDefinition { get; private set; }

            public WindowsServiceRecord Query(string serviceName)
            {
                if (_record == null ||
                    !string.Equals(_record.ServiceName, serviceName, StringComparison.Ordinal))
                {
                    return null;
                }
                return _record;
            }

            public void Create(WindowsServiceDefinition definition)
            {
                LastDefinition = definition;
                _record = WindowsServiceRecord.FromDefinition(definition);
            }

            public void Update(WindowsServiceDefinition definition)
            {
                LastDefinition = definition;
                _record = WindowsServiceRecord.FromDefinition(definition);
            }

            public void Start(string serviceName)
            {
                EnsureExact(serviceName);
                _record.State = WindowsServiceState.Running;
            }

            public void RequestStop(string serviceName)
            {
                EnsureExact(serviceName);
                _record.State = WindowsServiceState.Stopped;
            }

            public void Delete(string serviceName)
            {
                EnsureExact(serviceName);
                _record = null;
            }

            private void EnsureExact(string serviceName)
            {
                if (_record == null ||
                    !string.Equals(_record.ServiceName, serviceName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("unexpected service identity");
                }
            }
        }

        private sealed class FakeProcessEnvironmentReader : IProcessEnvironmentReader
        {
            private readonly IDictionary<string, string> _environment;

            internal FakeProcessEnvironmentReader(IDictionary<string, string> environment)
            {
                _environment = environment;
            }

            public IDictionary<string, string> ReadCurrent()
            {
                return new Dictionary<string, string>(_environment, StringComparer.OrdinalIgnoreCase);
            }
        }

        private sealed class FakeControllerChildRuntime : IControllerChildRuntime
        {
            internal ControllerChildStartPlan LastPlan { get; private set; }
            internal int GracefulStopProcessId { get; private set; }
            internal int WaitProcessId { get; private set; }
            internal int WaitMilliseconds { get; private set; }

            public int Start(ControllerChildStartPlan plan)
            {
                LastPlan = plan;
                return 4321;
            }

            public bool SendGracefulStop(int processId)
            {
                GracefulStopProcessId = processId;
                return true;
            }

            public bool WaitForExit(int processId, int milliseconds)
            {
                WaitProcessId = processId;
                WaitMilliseconds = milliseconds;
                return true;
            }
        }

        private sealed class FakeFileTrustVerifier : IFileTrustVerifier
        {
            private readonly TrustedFileExpectation _observed;
            private readonly bool _trusted;
            private readonly string _error;

            internal FakeFileTrustVerifier(
                TrustedFileExpectation observed,
                bool trusted,
                string error = null)
            {
                _observed = observed;
                _trusted = trusted;
                _error = error;
            }

            internal TrustedFileExpectation LastExpectation { get; private set; }

            public FileTrustResult Verify(string path, TrustedFileExpectation expectation)
            {
                LastExpectation = expectation;
                return _trusted
                    ? FileTrustResult.Trusted(path, _observed)
                    : FileTrustResult.Rejected(_error ?? "trust rejected");
            }
        }

        private sealed class FakeWindowsServiceCollectionApi : IWindowsServiceApi
        {
            private readonly Dictionary<string, WindowsServiceRecord> _records =
                new Dictionary<string, WindowsServiceRecord>(StringComparer.Ordinal);

            internal FakeWindowsServiceCollectionApi()
                : this(null)
            {
            }

            internal FakeWindowsServiceCollectionApi(IList<string> events)
            {
                Events = events;
                CreatedDefinitions = new List<WindowsServiceDefinition>();
            }

            internal IList<WindowsServiceDefinition> CreatedDefinitions { get; private set; }
            internal IList<string> Events { get; private set; }

            public WindowsServiceRecord Query(string serviceName)
            {
                WindowsServiceRecord record;
                return _records.TryGetValue(serviceName, out record) ? record : null;
            }

            public void Create(WindowsServiceDefinition definition)
            {
                definition.Validate();
                CreatedDefinitions.Add(definition);
                _records.Add(
                    definition.ServiceName,
                    WindowsServiceRecord.FromDefinition(definition));
            }

            public void Update(WindowsServiceDefinition definition)
            {
                definition.Validate();
                _records[definition.ServiceName] =
                    WindowsServiceRecord.FromDefinition(definition);
            }

            public void Start(string serviceName)
            {
                WindowsServiceRecord record = Require(serviceName);
                record.State = WindowsServiceState.Running;
                if (Events != null) Events.Add("start:" + serviceName);
            }

            public void RequestStop(string serviceName)
            {
                WindowsServiceRecord record = Require(serviceName);
                record.State = WindowsServiceState.Stopped;
                if (Events != null) Events.Add("stop:" + serviceName);
            }

            public void Delete(string serviceName)
            {
                Require(serviceName);
                _records.Remove(serviceName);
            }

            private WindowsServiceRecord Require(string serviceName)
            {
                WindowsServiceRecord record;
                if (!_records.TryGetValue(serviceName, out record))
                {
                    throw new InvalidOperationException("unexpected service identity");
                }
                return record;
            }
        }

        private sealed class FakeEpmdCommandRunner : IEpmdCommandRunner
        {
            private readonly Queue<EpmdCommandResult> _results;

            internal FakeEpmdCommandRunner(params EpmdCommandResult[] results)
            {
                _results = new Queue<EpmdCommandResult>(results);
                Plans = new List<EpmdCommandPlan>();
            }

            internal IList<EpmdCommandPlan> Plans { get; private set; }

            public EpmdCommandResult Execute(EpmdCommandPlan plan)
            {
                Plans.Add(plan);
                if (_results.Count == 0)
                {
                    throw new InvalidOperationException("No fake EPMD result is queued.");
                }
                return _results.Dequeue();
            }
        }

        private sealed class FakeTcpListenerOwnerReader : ITcpListenerOwnerReader
        {
            private readonly int _port;
            private readonly int _processId;

            internal FakeTcpListenerOwnerReader(int port, int processId)
            {
                _port = port;
                _processId = processId;
            }

            public IList<int> FindListenerProcessIds(int port)
            {
                return port == _port
                    ? new List<int> { _processId }
                    : new List<int>();
            }
        }

        private sealed class FakeProcessParentReader : IProcessParentReader
        {
            private readonly int _processId;

            internal FakeProcessParentReader(int processId, int parentProcessId)
            {
                _processId = processId;
                ParentProcessId = parentProcessId;
            }

            internal int ParentProcessId { get; set; }

            public int GetParentProcessId(int processId)
            {
                return processId == _processId ? ParentProcessId : 0;
            }
        }

        private sealed class FakeLocalModulePortReservationFactory :
            ILocalModulePortReservationFactory
        {
            public IDisposable Acquire(
                ManagedLocalModuleProvisioningItemRequest item)
            {
                return new CallbackDisposable(delegate { });
            }
        }

        private sealed class FakeLocalModuleServiceReadinessProbe :
            ILocalModuleServiceReadinessProbe
        {
            private readonly IList<string> _events;

            internal FakeLocalModuleServiceReadinessProbe(IList<string> events)
            {
                _events = events;
            }

            public void WaitUntilOwnedListener(
                LocalModuleInstanceManifest manifest,
                LocalModuleProcessRole role)
            {
                _events.Add("ready:" + role.ToString());
            }

            public void WaitUntilStopped(
                LocalModuleInstanceManifest manifest,
                LocalModuleProcessRole role)
            {
                _events.Add("stopped:" + role.ToString());
            }
        }

        private sealed class RecordingEpmdCommandRunner : IEpmdCommandRunner
        {
            private readonly IList<string> _events;
            private readonly Queue<EpmdCommandResult> _results;

            internal RecordingEpmdCommandRunner(
                IList<string> events,
                params EpmdCommandResult[] results)
            {
                _events = events;
                _results = new Queue<EpmdCommandResult>(results);
            }

            public EpmdCommandResult Execute(EpmdCommandPlan plan)
            {
                _events.Add("epmd:" + plan.ArgumentTokens[0]);
                return _results.Dequeue();
            }
        }

        private sealed class FakeManagedLocalModuleProvisioningPlatform :
            IManagedLocalModuleProvisioningPlatform
        {
            private bool _ready;

            internal FakeManagedLocalModuleProvisioningPlatform()
            {
                Events = new List<string>();
                ReconciledInns = new List<string>();
            }

            internal ManagedLocalModuleProvisioningStage? FailureStage { get; set; }
            internal IList<string> Events { get; private set; }
            internal IList<string> ReconciledInns { get; private set; }
            internal int MutationCount { get; private set; }
            internal int ProfilePreparationCount { get; private set; }
            internal int ServicePairConfigurationCount { get; private set; }
            internal int StackReferenceEnsureCount { get; private set; }

            public IDisposable AcquireItemLock(string inn)
            {
                return new CallbackDisposable(delegate { });
            }

            public void Reconcile(
                ManagedLocalModuleProvisioningItemRequest item,
                string operationId)
            {
                ReconciledInns.Add(item.Inn);
            }

            public ManagedLocalModuleObservedState Inspect(
                ManagedLocalModuleProvisioningItemRequest item,
                ManagedLocalModuleProvisioningContext context)
            {
                return _ready
                    ? ManagedLocalModuleObservedState.MatchingReady
                    : ManagedLocalModuleObservedState.Absent;
            }

            public void RecordStage(
                ManagedLocalModuleProvisioningItemRequest item,
                ManagedLocalModuleProvisioningContext context,
                string operationId,
                ManagedLocalModuleProvisioningStage stage)
            {
                Events.Add("stage:" + stage.ToString() + ":" + item.Inn);
                MutationCount++;
                if (FailureStage.HasValue && FailureStage.Value == stage)
                {
                    throw new IOException("simulated stage failure");
                }
            }

            public void PrepareProfile(
                ManagedLocalModuleProvisioningItemRequest item,
                ManagedLocalModuleProvisioningContext context,
                string operationId)
            {
                Events.Add("profile:" + item.Inn);
                ProfilePreparationCount++;
                MutationCount++;
            }

            public void ConfigureServicePair(
                ManagedLocalModuleProvisioningItemRequest item,
                ManagedLocalModuleProvisioningContext context,
                string initiatingSid)
            {
                Events.Add("services:" + item.Inn);
                ServicePairConfigurationCount++;
                MutationCount++;
            }

            public void StartDatabaseAndWait(
                ManagedLocalModuleProvisioningItemRequest item,
                ManagedLocalModuleProvisioningContext context)
            {
                Events.Add("database:" + item.Inn);
                MutationCount++;
            }

            public void StartApiAndWait(
                ManagedLocalModuleProvisioningItemRequest item,
                ManagedLocalModuleProvisioningContext context)
            {
                Events.Add("api:" + item.Inn);
                MutationCount++;
            }

            public void Complete(
                ManagedLocalModuleProvisioningItemRequest item,
                ManagedLocalModuleProvisioningContext context,
                string operationId)
            {
                Events.Add("complete:" + item.Inn);
                MutationCount++;
                _ready = true;
            }

            public void EnsureStackReference(
                ManagedLocalModuleProvisioningItemRequest item,
                ManagedLocalModuleProvisioningContext context,
                string operationId)
            {
                StackReferenceEnsureCount++;
            }

            public void MarkRequiresAttention(
                ManagedLocalModuleProvisioningItemRequest item,
                string operationId,
                string errorClass)
            {
                Events.Add("attention:" + item.Inn);
            }
        }

        private sealed class FakeManagedLocalModuleRemovalPlatform :
            IManagedLocalModuleRemovalPlatform
        {
            private bool _profileFailureThrown;

            internal FakeManagedLocalModuleRemovalPlatform()
            {
                Events = new List<string>();
            }

            internal IList<string> Events { get; private set; }
            internal int InstanceReferencesAfterStackDeletion { get; set; }
            internal int RuntimeReferencesAfterInstanceDeletion { get; set; }
            internal bool FailProfileDeletionOnce { get; set; }
            internal int CleanupPendingCount { get; private set; }
            internal int InstanceDeletionCount { get; private set; }
            internal int RuntimeDeletionCount { get; private set; }

            public IDisposable AcquireMachineLock()
            {
                return new CallbackDisposable(delegate { });
            }

            public ManagedLocalModuleRemovalSnapshot Inspect(
                string kktSerial,
                string operationId)
            {
                return ManagedLocalModuleRemovalSnapshot.CreateForTesting(
                    kktSerial,
                    "kkt-" + kktSerial,
                    "11111111111111111111111111111111",
                    "lmi-0123456789abcdef01234567",
                    "22222222222222222222222222222222",
                    "lmrt-0123456789abcdef01234567",
                    "33333333333333333333333333333333");
            }

            public void RemoveController(ManagedLocalModuleRemovalSnapshot snapshot)
            {
                AddOnce("controller");
            }

            public void DeleteStack(ManagedLocalModuleRemovalSnapshot snapshot)
            {
                AddOnce("stack");
            }

            public int CountInstanceReferences(ManagedLocalModuleRemovalSnapshot snapshot)
            {
                return InstanceReferencesAfterStackDeletion;
            }

            public LocalModuleServicePairStopOutcome StopServicePair(
                ManagedLocalModuleRemovalSnapshot snapshot)
            {
                AddOnce("stop-pair");
                return LocalModuleServicePairStopOutcome.Stopped;
            }

            public void DeleteServicePair(ManagedLocalModuleRemovalSnapshot snapshot)
            {
                AddOnce("services");
            }

            public void DeleteProfile(ManagedLocalModuleRemovalSnapshot snapshot)
            {
                if (FailProfileDeletionOnce && !_profileFailureThrown)
                {
                    _profileFailureThrown = true;
                    throw new IOException("profile is busy");
                }
                AddOnce("profile");
            }

            public void DeleteInstance(ManagedLocalModuleRemovalSnapshot snapshot)
            {
                AddOnce("instance");
                InstanceDeletionCount++;
            }

            public int CountRuntimeReferences(ManagedLocalModuleRemovalSnapshot snapshot)
            {
                return RuntimeReferencesAfterInstanceDeletion;
            }

            public void DeleteRuntime(ManagedLocalModuleRemovalSnapshot snapshot)
            {
                AddOnce("runtime");
                RuntimeDeletionCount++;
            }

            public void MarkCleanupPending(
                ManagedLocalModuleRemovalSnapshot snapshot,
                string operationId,
                string errorClass)
            {
                CleanupPendingCount++;
            }

            public void CompleteRemoval(
                ManagedLocalModuleRemovalSnapshot snapshot)
            {
            }

            private void AddOnce(string value)
            {
                if (!Events.Contains(value)) Events.Add(value);
            }
        }

        private sealed class FakeManagedLocalModuleUpdatePlatform :
            IManagedLocalModuleUpdatePlatform
        {
            internal int BeginMigrationCount { get; private set; }

            public void BeginExactMigration(
                string currentVersion,
                string selectedVersion)
            {
                BeginMigrationCount++;
            }
        }

        private sealed class ThrowOnceLocalModuleMutationBoundary :
            ILocalModuleMutationBoundary
        {
            private readonly LocalModuleRuntimeMutationBoundary _selected;
            private bool _thrown;

            internal ThrowOnceLocalModuleMutationBoundary(
                LocalModuleRuntimeMutationBoundary selected)
            {
                _selected = selected;
            }

            public void Reached(LocalModuleRuntimeMutationBoundary boundary)
            {
                if (!_thrown && boundary == _selected)
                {
                    _thrown = true;
                    throw new IOException("Simulated crash at " + boundary.ToString() + ".");
                }
            }
        }

        private sealed class FakeWindowsInstallerPackageReader : IWindowsInstallerPackageReader
        {
            private readonly WindowsInstallerPackageMetadata _metadata;

            internal FakeWindowsInstallerPackageReader(WindowsInstallerPackageMetadata metadata)
            {
                _metadata = metadata;
            }

            public WindowsInstallerPackageMetadata Read(string path)
            {
                return new WindowsInstallerPackageMetadata
                {
                    ProductName = _metadata.ProductName,
                    ProductVersion = _metadata.ProductVersion,
                    ProductCode = _metadata.ProductCode,
                    UpgradeCode = _metadata.UpgradeCode
                };
            }
        }
    }
}
