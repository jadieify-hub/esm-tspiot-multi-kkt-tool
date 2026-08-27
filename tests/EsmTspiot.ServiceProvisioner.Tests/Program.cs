using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
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

            if (_failures == 0)
            {
                Console.WriteLine("All 9 provisioner tests passed.");
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
    }
}
