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
            Run("Provisioning protocol exposes no credentials paths or commands", ProvisioningProtocolExposesNoCredentialsPathsOrCommands);
            Run("Installer operation accepts only one verified setup selection", InstallerOperationAcceptsOnlyOneVerifiedSetupSelection);
            Run("Installer operation rejects stale or substituted source file", InstallerOperationRejectsStaleOrSubstitutedSourceFile);
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
                Console.WriteLine("All 65 provisioner tests passed.");
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

            AssertFalse(ProvisionerCommandLine.TryParse(
                    new[] { "--pipe", Guid.NewGuid().ToString("N"), "--operation", "not-a-guid" },
                    out ProvisionerCommandLine ignored),
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

        private static void ProvisioningProtocolExposesNoCredentialsPathsOrCommands()
        {
            Type[] credentialFreeTypes =
            {
                typeof(LmServiceProvisioningItemRequest),
                typeof(LmRemovalConfirmation),
                typeof(LmCleanupConfirmation),
                typeof(LmManifestFingerprint),
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
    }
}
