using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
            Run("Provisioning protocol rejects plan hash mismatch", ProvisioningProtocolRejectsPlanHashMismatch);
            Run("Provisioning protocol exposes no credentials paths or commands", ProvisioningProtocolExposesNoCredentialsPathsOrCommands);
            Run("Installer operation accepts only one verified setup selection", InstallerOperationAcceptsOnlyOneVerifiedSetupSelection);
            Run("Installer operation rejects stale or substituted source file", InstallerOperationRejectsStaleOrSubstitutedSourceFile);
            Run("Official controller locator enforces protected allowed root", OfficialControllerLocatorEnforcesProtectedAllowedRoot);
            Run("Official controller locator enforces full product trust", OfficialControllerLocatorEnforcesFullProductTrust);
            Run("Official installer verifier locks verifies and stages atomically", OfficialInstallerVerifierLocksVerifiesAndStagesAtomically);
            Run("Official installer verifier rejects filename signer version or hash mismatch", OfficialInstallerVerifierRejectsFilenameSignerVersionOrHashMismatch);
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

            if (_failures == 0)
            {
                Console.WriteLine("All 33 provisioner tests passed.");
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

        private static void ProvisioningProtocolRejectsPlanHashMismatch()
        {
            LmServiceProvisioningBatchRequest request = CreateEnsureRequest(1);
            request.Items[0].TargetPort++;

            ValidationResult validation = ProvisioningRequestValidator.Validate(request);

            AssertFalse(validation.IsValid, "A changed displayed plan must invalidate its confirmation hash.");
            AssertContains(validation.JoinMessages(), "SHA-256");
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
                state);
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
