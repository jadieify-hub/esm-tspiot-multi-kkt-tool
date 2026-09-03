using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private static int Main(string[] args)
        {
            if (args != null && args.Length == 2 &&
                string.Equals(
                    args[0],
                    "--verify-local-module-msi",
                    StringComparison.Ordinal))
            {
                return VerifyLocalModuleMsiReadOnly(args[1]);
            }
            if (args != null && args.Length == 3 &&
                string.Equals(
                    args[0],
                    "--transform-local-module-msi",
                    StringComparison.Ordinal))
            {
                return TransformLocalModuleMsiOffline(args[1], args[2]);
            }
            if (args != null && args.Length == 2 &&
                string.Equals(
                    args[0],
                    "--inspect-local-module-config-schema",
                    StringComparison.Ordinal))
            {
                return InspectLocalModuleConfigSchema(args[1]);
            }
            if (args != null && args.Length == 1 &&
                string.Equals(
                    args[0],
                    "--direct-controller-sandbox",
                    StringComparison.Ordinal))
            {
                return RunDirectControllerProductionSandbox();
            }
            if (args != null && args.Length == 3 &&
                string.Equals(
                    args[0],
                    "--local-module-msi-sandbox",
                    StringComparison.Ordinal))
            {
                try
                {
                    return RunLocalModuleMsiProductionSandbox(
                        args[1],
                        args[2]);
                }
                catch (Exception exception)
                {
                    try
                    {
                        File.WriteAllText(
                            args[2],
                            "HARNESS CRASH: " + exception + "\r\n");
                    }
                    catch
                    {
                    }
                    return 1;
                }
            }
            if (args != null && args.Length != 0)
            {
                Console.Error.WriteLine("Unknown test mode.");
                return 2;
            }

            Run("DTF dependency closure is exact and vendor free", DtfDependencyClosureIsExactAndVendorFree);
            Run("Provisioning protocol accepts bounded ensure batch", ProvisioningProtocolAcceptsBoundedEnsureBatch);
            Run("Provisioning protocol rejects oversized or duplicate batch", ProvisioningProtocolRejectsOversizedOrDuplicateBatch);
            Run("Provisioning protocol rejects unknown schema or operation", ProvisioningProtocolRejectsUnknownSchemaOrOperation);
            Run("Production helper rejects retired supervisor entry points", ProductionHelperRejectsRetiredSupervisorEntryPoints);
            Run("Provisioning protocol rejects unsafe item", ProvisioningProtocolRejectsUnsafeItem);
            Run("Provisioning pipe authenticates exact peer images from any folder", ProvisioningPipeAuthenticatesExactPeerImages);
            Run("Provisioning pipe accepts main images independently of filename", ProvisioningPipeAcceptsMainImageIndependentlyOfFilename);
            Run("Provisioning protocol rejects plan hash mismatch", ProvisioningProtocolRejectsPlanHashMismatch);
            Run("Direct controller protocol v2 accepts canonical batch", DirectControllerProtocolV2AcceptsCanonicalBatch);
            Run("Direct controller protocol v2 rejects stale schema and duplicates", DirectControllerProtocolV2RejectsStaleSchemaAndDuplicates);
            Run("Direct controller protocol exposes no paths commands or secrets", DirectControllerProtocolExposesNoPathsCommandsOrSecrets);
            Run("Direct controller SCM definition uses only verified vendor binary", DirectControllerScmDefinitionUsesOnlyVerifiedVendorBinary);
            Run("Direct controller SCM definition rejects supervisor rules", DirectControllerScmDefinitionRejectsSupervisorRules);
            Run("Direct controller manifest is credential free and hash guarded", DirectControllerManifestIsCredentialFreeAndHashGuarded);
            Run("Direct controller provisioner continues independent KKT failures", DirectControllerProvisionerContinuesIndependentKktFailures);
            Run("Direct controller readiness requires listeners owned by service PID", DirectControllerReadinessRequiresListenersOwnedByServicePid);
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
            Run("Local module MSI profile accepts exact sanitized snapshot", LocalModuleMsiProfileAcceptsExactSanitizedSnapshot);
            Run("Local module MSI profile reports every structural mismatch safely", LocalModuleMsiProfileReportsEveryStructuralMismatchSafely);
            Run("Local module MSI profile is derived from the selected package", LocalModuleMsiProfileIsDerivedFromSelectedPackage);
            Run("Local module MSI structure is read only after trust", LocalModuleMsiStructureIsReadOnlyAfterTrust);
            Run("Local module installer properties are pinned by the vendor profile", LocalModuleInstallerPropertiesArePinnedByVendorProfile);
            Run("Local module configuration rejects missing API credentials", LocalModuleConfigurationRejectsMissingApiCredentials);
            Run("Local module MSI clone identity is stable except PackageCode", LocalModuleMsiCloneIdentityIsStableExceptPackageCode);
            Run("Local module MSI transformer edits only profiled rows", LocalModuleMsiTransformerEditsOnlyProfiledRows);
            Run("Local module MSI output rejects cabinet and table drift", LocalModuleMsiOutputRejectsCabinetAndTableDrift);
            Run("Local module MSI staging recovers every mutation boundary", LocalModuleMsiStagingRecoversEveryMutationBoundary);
            Run("Windows Installer API keeps credentials out of diagnostics", WindowsInstallerApiKeepsCredentialsOutOfDiagnostics);
            Run("Windows Installer receives a terminated absolute LM directory", WindowsInstallerReceivesTerminatedAbsoluteLmDirectory);
            Run("Installed local module reader scans both registry views", InstalledLocalModuleReaderScansBothRegistryViews);
            Run("Local module MSI manifests guard ownership and content", LocalModuleMsiManifestsGuardOwnershipAndContent);
            Run("Local module start-mode controller adjusts and restores the exact pair", LocalModuleStartModeControllerAdjustsAndRestoresExactPair);
            Run("Installed local module config exposes only a cookie digest", InstalledLocalModuleConfigExposesOnlyCookieDigest);
            Run("Installed local module services start stop and own listeners", InstalledLocalModuleServicesStartStopAndOwnListeners);
            Run("Local module firewall manager owns only an exact API rule", LocalModuleFirewallManagerOwnsOnlyExactApiRule);
            Run("Local module layout discovers the erts runtime", LocalModuleLayoutDiscoversErtsRuntime);
            Run("MSI local module ensure is idempotent and recovers cleanup", MsiLocalModuleEnsureIsIdempotentAndRecoversCleanup);
            Run("MSI local module removal preserves base and compensates EPMD", MsiLocalModuleRemovalPreservesBaseAndCompensatesEpmd);
            Run("MSI local module ensure enables automatic start for an adopted base", MsiLocalModuleEnsureEnablesAutomaticStartForAdoptedBase);
            Run("MSI local module protocol v3 hashes and validates every item", MsiLocalModuleProtocolV3HashesAndValidatesEveryItem);
            Run("MSI local module session continues independent INN failures", MsiLocalModuleSessionContinuesIndependentInnFailures);
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
            Run("Local module inventory keeps operator read only access", LocalModuleInventoryKeepsOperatorReadOnlyAccess);
            Run("Local module inventory repairs legacy operator read only access", LocalModuleInventoryRepairsLegacyOperatorReadOnlyAccess);
            Run("Local module runtime deletion requires zero references", LocalModuleRuntimeDeletionRequiresZeroReferences);
            Run("Existing local module runtime verifies without MSI extraction", ExistingLocalModuleRuntimeVerifiesWithoutMsiExtraction);
            Run("Local module runtime recovers every mutation boundary", LocalModuleRuntimeRecoversEveryMutationBoundary);
            Run("Local module runtime deletion recovers every mutation boundary", LocalModuleRuntimeDeletionRecoversEveryMutationBoundary);
            Run("Official controller locator enforces protected allowed root", OfficialControllerLocatorEnforcesProtectedAllowedRoot);
            Run("Official controller locator enforces full product trust", OfficialControllerLocatorEnforcesFullProductTrust);
            Run("Official controller locator requires installed product registration", OfficialControllerLocatorRequiresInstalledProductRegistration);
            Run("Installed controller product requires exact registry values", InstalledControllerProductRequiresExactRegistryValues);
            Run("Supported controller profile pins the vendor, not the version", SupportedControllerProfilePinsVendorNotVersion);
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
            Run("Legacy terminator rejects PID reuse and foreign identity", LegacyTerminatorRejectsPidReuseAndForeignIdentity);
            Run("Legacy migration retries partial cleanup and honors cancellation", LegacyMigrationRetriesPartialCleanupAndHonorsCancellation);
            Run("ESM instance YAML patches unique structural ldbControl", EsmInstanceYamlPatchesUniqueStructuralLdbControl);
            Run("ESM instance YAML rejects duplicate anchors and aliases", EsmInstanceYamlRejectsDuplicateAnchorsAndAliases);
            Run("ESM instance YAML supports block sequences and rejects ambiguity", EsmInstanceYamlSupportsBlockSequencesAndRejectsAmbiguity);
            Run("ESM instance config transaction backs up restores and defers", EsmInstanceConfigTransactionBacksUpRestoresAndDefers);
            Run("ESM instance service is startable but never mutable", EsmInstanceServiceIsStartableButNeverMutable);
            Run("ESM instance config recovers from an aborted operation", EsmInstanceConfigRecoversFromAbortedOperation);
            Run("SCM handles are disposed on every failure", ScmHandlesAreDisposedOnEveryFailure);
            Run("SCM configures restricted service SID", ScmConfiguresRestrictedServiceSid);
            Run("Supervisor replaces only child ProgramData", SupervisorReplacesOnlyChildProgramData);
            Run("Supervisor rejects caller supplied environment and arguments", SupervisorRejectsCallerSuppliedEnvironmentAndArguments);
            Run("Supervisor stops child gracefully without process kill", SupervisorStopsChildGracefullyWithoutProcessKill);
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
            Run("Windows managed platform persists and repairs exact owned pair", WindowsManagedPlatformPersistsProfileAndExactServicePair);
            Run("Complete stack canary failure stops remaining groups", CompleteStackCanaryFailureStopsRemainingGroups);
            Run("Complete stack provisions every KKT of shared INN", CompleteStackProvisionsEveryKktOfSharedInn);
            Run("Complete stack skips a later unregistered KKT", CompleteStackSkipsLaterUnregisteredKkt);
            Run("Managed local module same INN ensure is idempotent", ManagedLocalModuleSameInnEnsureIsIdempotent);
            Run("Managed removal retains shared same-INN module", ManagedRemovalRetainsSharedSameInnModule);
            Run("Managed removal cleans complete stack in reverse", ManagedRemovalCleansCompleteStackInReverse);
            Run("Managed removal projects cleanup pending and retries", ManagedRemovalProjectsCleanupPendingAndRetries);
            Run("Managed removal journal survives deleted KKT stack", ManagedRemovalJournalSurvivesDeletedKktStack);
            Run("Managed removal journal keeps operator read only access", ManagedRemovalJournalKeepsOperatorReadOnlyAccess);
            Run("Windows managed removal deletes owned stack and retry journal", WindowsManagedRemovalDeletesOwnedStackAndRetryJournal);
            Run("Managed update guard never stops unknown version", ManagedUpdateGuardNeverStopsUnknownVersion);
            Run("LM profile adapter changes only supported fields", LmProfileAdapterChangesOnlySupportedFields);
            Run("LM profile adapter rejects ambiguous schema", LmProfileAdapterRejectsAmbiguousSchema);
            Run("LM profile adapter preserves unknown nonsecret fields", LmProfileAdapterPreservesUnknownNonsecretFields);
            Run("LM profile adapter writes atomically", LmProfileAdapterWritesAtomically);
            Run("LM profile adapter never clones official profile", LmProfileAdapterNeverClonesOfficialProfile);
            Run("Direct controller profile stages only official CA pair", DirectControllerProfileStagesOnlyOfficialCaPair);
            Run("Direct controller profile atomically replaces read only CA", DirectControllerProfileAtomicallyReplacesReadOnlyCa);
            Run("Direct controller profile writes isolated ports without credentials", DirectControllerProfileWritesIsolatedPortsWithoutCredentials);
            Run("Direct controller manifest safely upgrades legacy target port", DirectControllerManifestSafelyUpgradesLegacyTargetPort);
            Run("Windows direct controller platform creates and removes exact clone", WindowsDirectControllerPlatformCreatesAndRemovesExactClone);
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

        private static int VerifyLocalModuleMsiReadOnly(string sourcePath)
        {
            try
            {
                LocalModuleInstallerSelection selection =
                    MsiTestPackageFactory.DescribeSelection(sourcePath);
                using (VerifiedLocalModulePackage package =
                    LocalModulePackageVerifier.Supported()
                        .VerifyAndLock(selection))
                {
                    Console.WriteLine(
                        "LOCAL_MODULE_MSI_PROFILE_OK " +
                        package.Metadata.ProductVersion + " " +
                        package.Metadata.PackageCode);
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static int TransformLocalModuleMsiOffline(
            string sourcePath,
            string outputPath)
        {
            try
            {
                LocalModuleInstallerSelection selection =
                    MsiTestPackageFactory.DescribeSelection(sourcePath);
                using (VerifiedLocalModulePackage package =
                    LocalModulePackageVerifier.Supported()
                        .VerifyAndLock(selection))
                {
                    LocalModuleMsiCloneIdentity identity =
                        new LocalModuleMsiIdentityFactory(delegate {
                            return Guid.NewGuid();
                        }).Create(
                            package.Metadata.ProductVersion,
                            "990000000000",
                            1);
                    LocalModuleMsiTransformPlan plan =
                        LocalModuleMsiTransformPlan.Create(
                            identity,
                            package.Metadata.ProductVersion,
                            6995,
                            7984);
                    using (TransformedLocalModuleMsi transformed =
                        new LocalModuleMsiTransformer().Transform(
                            package,
                            plan,
                            outputPath))
                    {
                        Console.WriteLine(
                            "LOCAL_MODULE_MSI_TRANSFORM_OK files=" +
                            transformed.Verified.TotalFileCount +
                            " modified=" +
                            transformed.Verified.ModifiedFileCount);
                    }
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.ToString());
                return 1;
            }
        }

        private static int InspectLocalModuleConfigSchema(string sourcePath)
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                LocalModuleInstallerSelection selection =
                    MsiTestPackageFactory.DescribeSelection(sourcePath);
                using (VerifiedLocalModulePackage package =
                    LocalModulePackageVerifier.Supported()
                        .VerifyAndLock(selection))
                {
                    LocalModuleCabinetSnapshot snapshot =
                        LocalModuleCabinetTools.Extract(sourcePath, directory);
                    IDictionary<string, string> configNames =
                        LocalModuleConfigBytes.MapConfigurationFiles(
                            package.CapabilityProfile);
                    for (int index = 0; index < snapshot.Files.Count; index++)
                    {
                        MsiFilePayloadSnapshot file = snapshot.Files[index];
                        string configName;
                        if (!configNames.TryGetValue(file.FileId, out configName))
                            continue;
                        string configPath =
                            Path.Combine(directory, file.FileId);
                        if (LocalModuleConfigBytes.Classify(
                                configName,
                                File.ReadAllBytes(configPath)) ==
                            LocalModuleConfigRole.None)
                            continue;
                        Console.WriteLine("CONFIG " + file.FileId);
                        string[] lines = File.ReadAllLines(configPath);
                        for (int line = 0; line < lines.Length; line++)
                        {
                            string trimmed = lines[line].Trim();
                            if (trimmed.StartsWith("[", StringComparison.Ordinal) ||
                                trimmed.StartsWith("-name", StringComparison.Ordinal))
                                Console.WriteLine("  " + trimmed);
                            else
                            {
                                int equals = trimmed.IndexOf('=');
                                if (equals > 0 &&
                                    !trimmed.StartsWith(";", StringComparison.Ordinal) &&
                                    !trimmed.StartsWith("#", StringComparison.Ordinal))
                                    Console.WriteLine(
                                        "  " + trimmed.Substring(0, equals).Trim() +
                                        " = <redacted>");
                            }
                        }
                    }
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.ToString());
                return 1;
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(directory);
            }
        }

        private static void DtfDependencyClosureIsExactAndVendorFree()
        {
            string outputRoot = Path.GetDirectoryName(
                typeof(Program).Assembly.Location);
            string[] expected =
            {
                "WixToolset.Dtf.WindowsInstaller.dll",
                "WixToolset.Dtf.WindowsInstaller.Package.dll",
                "WixToolset.Dtf.Compression.dll",
                "WixToolset.Dtf.Compression.Cab.dll"
            };
            string[] expectedSha256 =
            {
                "CDD7F34DDA1180F21205543C8EE836C5BE66060D94441172366BCE078E1CDB87",
                "3DE9CAB111A102040C6B4E17FE26690AFCDF37E8F9D82055A479CEF0DECA803C",
                "7D1C9C7B18D95A5AD80E250E4B185F47FAE6C921048355AE6D53FAB10BF19525",
                "06971F82041E80DAAC87CCEFFC1F88CC7D663470E9295E487554447A5AE43435"
            };
            for (int index = 0; index < expected.Length; index++)
            {
                string path = Path.Combine(outputRoot, expected[index]);
                AssertTrue(
                    File.Exists(path),
                    "The helper closure is missing " + expected[index] + ".");
                if (File.Exists(path))
                {
                    AssertEqual(
                        "4.0.6.0",
                        FileVersionInfo.GetVersionInfo(path).FileVersion,
                        expected[index] + " must remain pinned to DTF 4.0.6.");
                    AssertTrue(
                        string.Equals(
                            expectedSha256[index],
                            ComputeSha256(File.ReadAllBytes(path)),
                            StringComparison.OrdinalIgnoreCase),
                        expected[index] + " must match the reviewed DTF binary.");
                }
            }

            string[] files = Directory.GetFiles(
                outputRoot,
                "*",
                SearchOption.TopDirectoryOnly);
            List<string> actualDtf = new List<string>();
            for (int index = 0; index < files.Length; index++)
            {
                string name = Path.GetFileName(files[index]);
                if (name.StartsWith(
                    "WixToolset.Dtf.",
                    StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        Path.GetExtension(name),
                        ".dll",
                        StringComparison.OrdinalIgnoreCase))
                {
                    actualDtf.Add(name);
                }
                AssertFalse(
                    string.Equals(
                        Path.GetExtension(name),
                        ".msi",
                        StringComparison.OrdinalIgnoreCase),
                    "The helper closure must not contain an MSI package.");
                AssertFalse(
                    name.IndexOf("regime", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("yenisei", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("nssm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("RollingPin", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The helper closure must not contain a vendor or reference binary.");
            }

            Array.Sort(expected, StringComparer.OrdinalIgnoreCase);
            actualDtf.Sort(StringComparer.OrdinalIgnoreCase);
            AssertEqual(
                string.Join("|", expected),
                string.Join("|", actualDtf.ToArray()),
                "The helper output must contain exactly the reviewed DTF closure.");
        }

        private static int RunDirectControllerProductionSandbox()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                Console.Error.WriteLine(
                    "The production direct-controller sandbox requires elevation.");
                return 1;
            }

            string initiatingSid = identity.User == null
                ? null
                : identity.User.Value;
            if (string.IsNullOrWhiteSpace(initiatingSid))
            {
                Console.Error.WriteLine("The elevated caller SID is unavailable.");
                return 1;
            }

            DirectControllerProvisioningItemRequest[] items =
            {
                new DirectControllerProvisioningItemRequest
                {
                    KktSerial = "99000000000031",
                    Inn = "9900000031",
                    Ordinal = 31,
                    TargetLocalModulePort =
                        DirectControllerIdentity.FutureLmPortForOrdinal(31)
                },
                new DirectControllerProvisioningItemRequest
                {
                    KktSerial = "99000000000032",
                    Inn = "9900000032",
                    Ordinal = 32,
                    TargetLocalModulePort =
                        DirectControllerIdentity.FutureLmPortForOrdinal(32)
                }
            };
            PathSafety pathSafety = new PathSafety();
            DirectControllerManifestStore manifests =
                DirectControllerManifestStore.CreateMachineStore(
                    pathSafety,
                    initiatingSid);
            WindowsServiceApi services = new WindowsServiceApi();
            WindowsDirectControllerPlatform platform = null;
            List<int> processIds = new List<int>();
            string failure = null;

            try
            {
                platform = WindowsDirectControllerPlatform.Create(initiatingSid);
                for (int index = items.Length - 1; index >= 0; index--)
                {
                    RemoveOwnedSandboxAssignmentIfPresent(
                        platform,
                        manifests,
                        items[index],
                        initiatingSid);
                }
                for (int index = 0; index < items.Length; index++)
                {
                    string serviceName = DirectControllerIdentity.ServiceNameForOrdinal(
                        items[index].Ordinal);
                    if (services.Query(serviceName) != null)
                    {
                        throw new InvalidOperationException(
                            "Sandbox service name is occupied without our sandbox manifest: " +
                            serviceName + ".");
                    }

                    LmServiceProvisioningItemResult result = platform.Ensure(
                        items[index],
                        Guid.NewGuid().ToString("N"),
                        initiatingSid);
                    if (result.Status != LmServiceProvisioningStatus.Succeeded)
                    {
                        throw new InvalidOperationException(
                            "Production Ensure did not succeed: " + result.FormatLogLine());
                    }

                    WindowsServiceRecord service = services.Query(serviceName);
                    if (service == null || service.ProcessId <= 0 ||
                        service.State != WindowsServiceState.Running)
                    {
                        throw new InvalidOperationException(
                            "Production Ensure did not leave a running SCM service: " +
                            serviceName + ".");
                    }
                    if (processIds.Contains(service.ProcessId))
                    {
                        throw new InvalidOperationException(
                            "Two controller services share one vendor process.");
                    }
                    processIds.Add(service.ProcessId);

                    string profile = Path.Combine(
                        manifests.GetProfileEnvironmentRoot(items[index].Ordinal),
                        "ESP",
                        "lmcontroller");
                    RequireSandboxFile(Path.Combine(profile, "config.yml"));
                    RequireSandboxFile(Path.Combine(profile, "ca.crt"));
                    RequireSandboxFile(Path.Combine(profile, "ca.pem"));
                    RequireSandboxFile(Path.Combine(profile, "server.crt"));
                    RequireSandboxFile(Path.Combine(profile, "server.pem"));
                }
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
            }
            finally
            {
                if (platform != null)
                {
                    for (int index = items.Length - 1; index >= 0; index--)
                    {
                        try
                        {
                            RemoveOwnedSandboxAssignmentIfPresent(
                                platform,
                                manifests,
                                items[index],
                                initiatingSid);
                        }
                        catch (Exception cleanupException)
                        {
                            failure = AppendFailure(
                                failure,
                                "Cleanup failed for ordinal " +
                                items[index].Ordinal.ToString() + ": " +
                                cleanupException.ToString());
                        }
                    }
                }

                for (int index = 0; index < items.Length; index++)
                {
                    try
                    {
                        string serviceName = DirectControllerIdentity.ServiceNameForOrdinal(
                            items[index].Ordinal);
                        if (services.Query(serviceName) != null)
                        {
                            failure = AppendFailure(
                                failure,
                                "Cleanup left the SCM service " + serviceName + ".");
                        }
                        if (manifests.Read(items[index].KktSerial) != null)
                        {
                            failure = AppendFailure(
                                failure,
                                "Cleanup left the sandbox manifest for " +
                                items[index].KktSerial + ".");
                        }
                        if (Directory.Exists(
                                manifests.GetProfileEnvironmentRoot(items[index].Ordinal)))
                        {
                            failure = AppendFailure(
                                failure,
                                "Cleanup left the sandbox profile for ordinal " +
                                items[index].Ordinal.ToString() + ".");
                        }
                    }
                    catch (Exception verificationException)
                    {
                        failure = AppendFailure(
                            failure,
                            "Cleanup verification failed: " +
                            verificationException.ToString());
                    }
                }
            }

            if (!string.IsNullOrEmpty(failure))
            {
                Console.Error.WriteLine(failure);
                return 1;
            }

            Console.WriteLine("DIRECT_CONTROLLER_PRODUCTION_SANDBOX_OK");
            Console.WriteLine(
                "ProductionPlatform=WindowsDirectControllerPlatform; PIDs=" +
                string.Join(",", processIds.ToArray()) + "; ordinals=31,32");
            return 0;
        }

        private static void RemoveOwnedSandboxAssignmentIfPresent(
            WindowsDirectControllerPlatform platform,
            DirectControllerManifestStore manifests,
            DirectControllerProvisioningItemRequest item,
            string initiatingSid)
        {
            DirectControllerManifest manifest = manifests.Read(item.KktSerial);
            if (manifest == null) return;
            LmServiceProvisioningItemResult ensured = platform.Ensure(
                item,
                Guid.NewGuid().ToString("N"),
                initiatingSid);
            if (ensured.Status != LmServiceProvisioningStatus.Succeeded)
            {
                throw new InvalidOperationException(
                    "Production recovery Ensure did not complete: " +
                    ensured.FormatLogLine());
            }
            manifest = manifests.Read(item.KktSerial);
            if (manifest == null)
            {
                throw new InvalidOperationException(
                    "Production recovery Ensure removed its manifest unexpectedly.");
            }
            item.ExpectedManifestSha256 = manifests.ComputeFingerprint(manifest);
            LmServiceProvisioningItemResult result = platform.Remove(
                item,
                Guid.NewGuid().ToString("N"),
                initiatingSid);
            if (result.Status !=
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained)
            {
                throw new InvalidOperationException(
                    "Production Remove did not complete: " + result.FormatLogLine());
            }
            item.ExpectedManifestSha256 = null;
        }

        private static void RequireSandboxFile(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 0)
            {
                throw new InvalidOperationException(
                    "Production controller profile file is missing: " + path + ".");
            }
        }

        private static string AppendFailure(string current, string next)
        {
            return string.IsNullOrEmpty(current)
                ? next
                : current + Environment.NewLine + next;
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

        private static void ProvisioningPipeAuthenticatesExactPeerImages()
        {
            ProvisioningPeerEvidence server = CreateValidServerEvidence();
            AssertTrue(ProvisioningPipePeerAuthenticator.ValidateServer(server).IsValid,
                "Expected the exact main process to authenticate from a user folder.");

            ProvisioningPeerEvidence wrongServerImage = CloneEvidence(server);
            wrongServerImage.ActualImagePath = @"C:\Temp\MultiKKT.exe";
            AssertFalse(ProvisioningPipePeerAuthenticator.ValidateServer(wrongServerImage).IsValid,
                "Same-SID server with another image must fail.");

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

        private static void ProvisioningPipeAcceptsMainImageIndependentlyOfFilename()
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
                LocalModulePackageVerifier verifier = CreateTestLocalModulePackageVerifier(
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
                    CreateTestLocalModulePackageVerifier(
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
                    CreateTestLocalModulePackageVerifier(
                        new FakeWindowsInstallerPackageReader(wrongProduct),
                        new FakeFileTrustVerifier(CreateLocalModuleTrustExpectation(selection), true),
                        new FakePathSafety(true)).VerifyAndLock(selection).Dispose();
                }, "A different ProductCode must be rejected.");

                WindowsInstallerPackageMetadata wrongVersion = CreateLocalModulePackageMetadata();
                wrongVersion.ProductVersion = "2.6.2";
                AssertThrows<InvalidDataException>(delegate {
                    CreateTestLocalModulePackageVerifier(
                        new FakeWindowsInstallerPackageReader(wrongVersion),
                        new FakeFileTrustVerifier(CreateLocalModuleTrustExpectation(selection), true),
                        new FakePathSafety(true)).VerifyAndLock(selection).Dispose();
                }, "A different ProductVersion must be rejected.");

                TrustedFileExpectation wrongSigner = CreateLocalModuleTrustExpectation(selection);
                wrongSigner.SignerSubject = "CN=Unexpected Signer";
                AssertThrows<InvalidDataException>(delegate {
                    CreateTestLocalModulePackageVerifier(
                        new FakeWindowsInstallerPackageReader(CreateLocalModulePackageMetadata()),
                        new FakeFileTrustVerifier(wrongSigner, true),
                        new FakePathSafety(true)).VerifyAndLock(selection).Dispose();
                }, "A substituted signer must be rejected even if a trust adapter reports success.");

                TrustedFileExpectation substitutedFile =
                    CreateLocalModuleTrustExpectation(selection);
                substitutedFile.Sha256 = new string('c', 64);
                AssertThrows<InvalidDataException>(delegate {
                    CreateTestLocalModulePackageVerifier(
                        new FakeWindowsInstallerPackageReader(CreateLocalModulePackageMetadata()),
                        new FakeFileTrustVerifier(substitutedFile, true),
                        new FakePathSafety(true)).VerifyAndLock(selection).Dispose();
                }, "A source file substituted after confirmation must be rejected.");

                LocalModuleInstallerSelection staleSelection = CloneLocalModuleInstallerSelection(selection);
                staleSelection.Sha256 = new string('b', 64);
                AssertThrows<InvalidDataException>(delegate {
                    CreateTestLocalModulePackageVerifier(
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

        private static int RunLocalModuleMsiProductionSandbox(
            string sourcePath,
            string reportPath)
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator) ||
                identity.User == null)
            {
                File.WriteAllText(reportPath,
                    "FAILED: sandbox requires elevation.\r\n");
                return 1;
            }

            string initiatingSid = identity.User.Value;
            string machineRoot = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "KRS",
                "MultiKKT");
            WindowsServiceApi services = new WindowsServiceApi();
            WindowsServiceRecord baseApiBefore = services.Query("regime");
            WindowsServiceRecord baseDatabaseBefore = services.Query("yenisei");
            LocalModuleMsiManifestStore manifests =
                new LocalModuleMsiManifestStore(
                    machineRoot,
                    new PathSafety(),
                    initiatingSid);
            List<LocalModuleMsiProvisioningItemRequest> items =
                new List<LocalModuleMsiProvisioningItemRequest>();
            items.Add(MsiRequest("9900000031", 0, 5995, 5984, null));
            items.Add(MsiRequest("9900000032", 1, 6995, 7984, null));
            string failure = null;
            File.WriteAllText(reportPath,
                "START local-module production sandbox\r\n");

            try
            {
                if (services.Query("regime1") != null ||
                    services.Query("yenisei1") != null)
                    throw new InvalidOperationException(
                        "Clone service names regime1/yenisei1 are already occupied.");
                for (int index = 0; index < items.Count; index++)
                    if (manifests.Read(items[index].Inn) != null)
                        throw new InvalidOperationException(
                            "Sandbox manifest already exists for INN " +
                            items[index].Inn + ".");

                LocalModuleInstallerSelection installer =
                    MsiTestPackageFactory.DescribeSelection(sourcePath);
                LmServiceProvisioningBatchRequest request =
                    new LmServiceProvisioningBatchRequest
                    {
                        SchemaVersion = ProvisioningRequestValidator
                            .CurrentSchemaVersion,
                        Operation =
                            LmServiceOperation.EnsureMsiLocalModules,
                        OperationId = Guid.NewGuid().ToString("N"),
                        InitiatingSid = initiatingSid,
                        LocalModuleInstallerSelection = installer
                    };
                for (int index = 0; index < items.Count; index++)
                    request.LocalModuleMsiItems.Add(items[index]);
                request.PlanHash = CanonicalLmPlanHasher.Compute(request);

                using (LocalModuleMsiProvisioningSession session =
                    LocalModuleMsiProvisioningSession.CreateWindows(request))
                {
                    for (int index = 0; index < items.Count; index++)
                    {
                        LocalModuleMsiProvisioningItemResult result =
                            session.ExecuteItem(index);
                        File.AppendAllText(reportPath,
                            "ENSURE " + result.Inn + " " + result.Status +
                            ": " + result.Message + "\r\n");
                        if (result.Status !=
                            LmServiceProvisioningStatus.Succeeded)
                            throw new InvalidOperationException(
                                "Ensure failed for INN " + result.Inn +
                                ": " + result.Status + ".");
                        items[index].ExpectedManifestSha256 =
                            result.ManifestSha256;
                    }
                }

                RequireRunningSandboxService(services, "regime");
                RequireRunningSandboxService(services, "yenisei");
                RequireRunningSandboxService(services, "regime1");
                RequireRunningSandboxService(services, "yenisei1");
                RequireOpenSandboxPort(5995);
                RequireOpenSandboxPort(5984);
                RequireOpenSandboxPort(6995);
                RequireOpenSandboxPort(7984);
                File.AppendAllText(reportPath,
                    "PASS simultaneous services and listeners\r\n");

                RequireAutomaticSandboxService(services, "regime");
                RequireAutomaticSandboxService(services, "yenisei");
                RequireAutomaticSandboxService(services, "regime1");
                RequireAutomaticSandboxService(services, "yenisei1");
                RequireSandboxBaseStartModeRecord(
                    manifests,
                    items[0],
                    baseApiBefore,
                    baseDatabaseBefore);
                File.AppendAllText(reportPath,
                    "PASS automatic start on base and clone; " +
                    "baseline start mode recorded\r\n");
                RequireSandboxCloneConfiguration(manifests, items[1]);
                File.AppendAllText(reportPath,
                    "PASS clone configuration matches its plan; " +
                    "API credentials present\r\n");
                RequireNoKrsHelperProcess();
                File.AppendAllText(reportPath,
                    "PASS no persistent KRS helper process\r\n");

                LocalModuleMsiProvisioningContext restartContext =
                    LocalModuleMsiProvisioningContext
                        .CreateWindowsForInstalledProducts(
                            Guid.NewGuid().ToString("N"),
                            machineRoot,
                            initiatingSid);
                for (int index = 0; index < items.Count; index++)
                {
                    LocalModuleMsiProvisioningItemResult restarted =
                        new LocalModuleMsiProvisioner().Restart(
                            items[index],
                            restartContext);
                    File.AppendAllText(reportPath,
                        "RESTART " + restarted.Inn + " " +
                        restarted.Status + ": " + restarted.Message +
                        "\r\n");
                    if (restarted.Status !=
                        LmServiceProvisioningStatus.Succeeded)
                        throw new InvalidOperationException(
                            "Restart failed for INN " + restarted.Inn + ".");
                }
                RequireOpenSandboxPort(5995);
                RequireOpenSandboxPort(6995);
                File.AppendAllText(reportPath, "PASS restart cycle\r\n");
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
                File.AppendAllText(reportPath,
                    "FAILED: " + failure + "\r\n");
            }
            finally
            {
                try
                {
                    List<LocalModuleMsiProvisioningItemRequest> removable =
                        new List<LocalModuleMsiProvisioningItemRequest>();
                    for (int index = 0; index < items.Count; index++)
                    {
                        LocalModuleMsiManifest manifest =
                            manifests.Read(items[index].Inn);
                        if (manifest == null) continue;
                        items[index].ExpectedManifestSha256 =
                            manifest.ManifestSha256;
                        removable.Add(items[index]);
                    }
                    if (removable.Count > 0)
                    {
                        LocalModuleMsiProvisioningContext removeContext =
                            LocalModuleMsiProvisioningContext
                                .CreateWindowsForInstalledProducts(
                                    Guid.NewGuid().ToString("N"),
                                    machineRoot,
                                    initiatingSid);
                        IList<LocalModuleMsiProvisioningItemResult> removed =
                            new LocalModuleMsiRemovalWorkflow().RemoveAll(
                                removable,
                                removeContext);
                        for (int index = 0; index < removed.Count; index++)
                            File.AppendAllText(reportPath,
                                "REMOVE " + removed[index].Inn + " " +
                                removed[index].Status + ": " +
                                removed[index].Message + "\r\n");
                    }
                    RestoreSandboxBaseState(
                        services,
                        baseApiBefore,
                        baseDatabaseBefore);
                    RequireSandboxBaseStartModeRestored(
                        services,
                        baseApiBefore,
                        baseDatabaseBefore);
                    RequireNoKrsHelperProcess();
                    if (services.Query("regime1") != null ||
                        services.Query("yenisei1") != null ||
                        Directory.Exists(@"D:\Program Files\Regime1"))
                        throw new InvalidOperationException(
                            "Cleanup left clone services or install root.");
                    for (int index = 0; index < items.Count; index++)
                        if (manifests.Read(items[index].Inn) != null)
                            throw new InvalidOperationException(
                                "Cleanup left manifest for " +
                                items[index].Inn + ".");
                    File.AppendAllText(reportPath,
                        "PASS cleanup and baseline restoration\r\n");
                }
                catch (Exception cleanupException)
                {
                    failure = AppendFailure(
                        failure,
                        "Cleanup failed: " + cleanupException.ToString());
                    File.AppendAllText(reportPath,
                        "CLEANUP FAILED: " + cleanupException + "\r\n");
                }
            }

            File.AppendAllText(reportPath,
                failure == null ? "SANDBOX_OK\r\n" : "SANDBOX_FAILED\r\n");
            return failure == null ? 0 : 1;
        }

        private static void RequireRunningSandboxService(
            WindowsServiceApi services,
            string serviceName)
        {
            WindowsServiceRecord service = services.Query(serviceName);
            if (service == null ||
                service.State != WindowsServiceState.Running ||
                service.ProcessId <= 0)
                throw new InvalidOperationException(
                    "Service is not running: " + serviceName + ".");
        }

        private static void RequireOpenSandboxPort(int port)
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult pending = client.BeginConnect(
                    IPAddress.Loopback,
                    port,
                    null,
                    null);
                if (!pending.AsyncWaitHandle.WaitOne(5000))
                    throw new InvalidOperationException(
                        "Listener did not open on port " + port + ".");
                client.EndConnect(pending);
            }
        }

        private static void RequireAutomaticSandboxService(
            WindowsServiceApi services,
            string serviceName)
        {
            WindowsServiceRecord service = services.Query(serviceName);
            if (service == null ||
                service.StartMode != WindowsServiceStartMode.AutoStart)
                throw new InvalidOperationException(
                    "Service is not set to automatic start: " +
                    serviceName + ".");
        }

        private static void RequireSandboxCloneConfiguration(
            LocalModuleMsiManifestStore manifests,
            LocalModuleMsiProvisioningItemRequest item)
        {
            LocalModuleMsiManifest manifest = manifests.Read(item.Inn);
            if (manifest == null)
                throw new InvalidOperationException(
                    "Manifest is missing for INN " + item.Inn + ".");
            LocalModuleInstalledLayout layout =
                LocalModuleInstalledLayout.Create(
                    manifest.InstallRoot,
                    manifest.CloneOrdinal,
                    manifest.ApiPort,
                    manifest.DatabasePort);
            // Throws when the installed configuration does not match its
            // plan or the vendor installer left [api] login/password empty.
            // Values are checked in place and never copied into the report.
            new LocalModuleConfigurationInspector().Inspect(layout);
        }

        private static void RequireSandboxBaseStartModeRecord(
            LocalModuleMsiManifestStore manifests,
            LocalModuleMsiProvisioningItemRequest baseItem,
            WindowsServiceRecord apiBefore,
            WindowsServiceRecord databaseBefore)
        {
            LocalModuleMsiManifest manifest = manifests.Read(baseItem.Inn);
            if (manifest == null || !manifest.PreExisting) return;
            if (apiBefore == null || databaseBefore == null) return;
            bool wasAutomatic =
                apiBefore.StartMode == WindowsServiceStartMode.AutoStart &&
                databaseBefore.StartMode == WindowsServiceStartMode.AutoStart;
            if (manifest.StartModeAdjusted == wasAutomatic)
                throw new InvalidOperationException(
                    "Base manifest start-mode record does not match the baseline.");
            if (manifest.StartModeAdjusted &&
                (manifest.PreviousApiStartMode != (int)apiBefore.StartMode ||
                 manifest.PreviousDatabaseStartMode !=
                    (int)databaseBefore.StartMode))
                throw new InvalidOperationException(
                    "Base manifest recorded wrong previous start modes.");
        }

        private static void RequireSandboxBaseStartModeRestored(
            WindowsServiceApi services,
            WindowsServiceRecord apiBefore,
            WindowsServiceRecord databaseBefore)
        {
            RequireSandboxStartModeRestored(services, "regime", apiBefore);
            RequireSandboxStartModeRestored(services, "yenisei", databaseBefore);
        }

        private static void RequireSandboxStartModeRestored(
            WindowsServiceApi services,
            string serviceName,
            WindowsServiceRecord before)
        {
            if (before == null) return;
            WindowsServiceRecord now = services.Query(serviceName);
            if (now == null) return;
            if (now.StartMode != before.StartMode)
                throw new InvalidOperationException(
                    "Cleanup did not restore the start mode of " +
                    serviceName + ": expected " + before.StartMode +
                    ", observed " + now.StartMode + ".");
        }

        private static void RequireNoKrsHelperProcess()
        {
            Process[] helpers = Process.GetProcessesByName(
                "EsmTspiot.ServiceProvisioner");
            try
            {
                if (helpers.Length > 0)
                    throw new InvalidOperationException(
                        "A KRS helper process is still running after the operation.");
            }
            finally
            {
                for (int index = 0; index < helpers.Length; index++)
                    helpers[index].Dispose();
            }
        }

        private static void RestoreSandboxBaseState(
            WindowsServiceApi services,
            WindowsServiceRecord apiBefore,
            WindowsServiceRecord databaseBefore)
        {
            if (apiBefore == null || databaseBefore == null) return;
            WindowsServiceRecord apiNow = services.Query("regime");
            WindowsServiceRecord databaseNow = services.Query("yenisei");
            if (apiBefore.State == WindowsServiceState.Stopped &&
                apiNow != null && apiNow.State == WindowsServiceState.Running)
            {
                services.RequestStop("regime");
                WaitForSandboxServiceState(
                    services, "regime", WindowsServiceState.Stopped);
            }
            if (databaseBefore.State == WindowsServiceState.Stopped &&
                databaseNow != null &&
                databaseNow.State == WindowsServiceState.Running)
            {
                services.RequestStop("yenisei");
                WaitForSandboxServiceState(
                    services, "yenisei", WindowsServiceState.Stopped);
            }
            databaseNow = services.Query("yenisei");
            if (databaseBefore.State == WindowsServiceState.Running &&
                databaseNow != null &&
                databaseNow.State == WindowsServiceState.Stopped)
            {
                services.Start("yenisei");
                WaitForSandboxServiceState(
                    services, "yenisei", WindowsServiceState.Running);
            }
            apiNow = services.Query("regime");
            if (apiBefore.State == WindowsServiceState.Running &&
                apiNow != null && apiNow.State == WindowsServiceState.Stopped)
            {
                services.Start("regime");
                WaitForSandboxServiceState(
                    services, "regime", WindowsServiceState.Running);
            }
        }

        private static void WaitForSandboxServiceState(
            WindowsServiceApi services,
            string serviceName,
            WindowsServiceState expected)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                WindowsServiceRecord observed = services.Query(serviceName);
                if (observed != null && observed.State == expected) return;
                System.Threading.Thread.Sleep(250);
            }
            throw new InvalidOperationException(
                "Service did not reach " + expected + ": " +
                serviceName + ".");
        }

        private static void ProductionHelperRejectsRetiredSupervisorEntryPoints()
        {
            ProvisionerCommandLine ignored;
            AssertFalse(ProvisionerCommandLine.TryParse(
                    new[] { "--supervise", "krs-esm-lm-00105700000001" },
                    out ignored),
                "The production helper must reject the retired controller supervisor mode.");
            AssertFalse(ProvisionerCommandLine.TryParse(
                    new[] {
                        "--supervise-local-module",
                        "krs-lm-db-0123456789abcdef01234567"
                    },
                    out ignored),
                "The production helper must reject the retired Erlang supervisor mode.");

            AssertTrue(typeof(ProvisionerCommandLine).Assembly.GetType(
                    "EsmTspiot.ServiceProvisioner.LegacyManagedStateMigrationWorkflow",
                    false) != null,
                "Exact legacy cleanup must remain available after supervisor entry points are disabled.");
        }

        private static void LocalModuleMsiProfileAcceptsExactSanitizedSnapshot()
        {
            LocalModuleMsiCapabilityProfile profile =
                LoadLocalModuleMsiCapabilityFixture();
            LocalModuleMsiDatabaseSnapshot snapshot =
                CreateLocalModuleMsiSnapshot(profile);

            AssertEqual(0, snapshot.Compare(profile).Count,
                "The exact sanitized MSI profile must match without diagnostics.");
            AssertEqual(5, CountMsiProfileRows(profile, "File"),
                "The profile must pin exactly five generated configuration files.");
            AssertTrue(CountMsiProfileRows(profile, "CustomAction") >= 18,
                "The profile must pin every quoted service action and StopEPMD.");
        }

        private static void LocalModuleMsiProfileReportsEveryStructuralMismatchSafely()
        {
            LocalModuleMsiCapabilityProfile profile =
                LoadLocalModuleMsiCapabilityFixture();
            AssertMsiProfileMismatch(profile, delegate(LocalModuleMsiDatabaseSnapshot value) {
                value.FileRowCount--;
            }, "File:row-count");
            AssertMsiProfileMismatch(profile, delegate(LocalModuleMsiDatabaseSnapshot value) {
                value.MsiFileHashRowCount--;
            }, "MsiFileHash:row-count");
            AssertMsiProfileMismatch(profile, delegate(LocalModuleMsiDatabaseSnapshot value) {
                value.Media[0].Cabinet = "#unexpected.cab";
            }, "Media:#media1.cab");
            AssertMsiProfileMismatch(profile, delegate(LocalModuleMsiDatabaseSnapshot value) {
                value.Media[1].LastSequence--;
            }, "Media:#Disk1.cab");
            AssertMsiProfileRowMismatch(profile, "Directory", "APPLICATIONFOLDER");
            AssertMsiProfileRowMismatch(profile, "Registry", "RegimeInstallDir");
            AssertMsiProfileRowMismatch(
                profile,
                "RegLocator",
                "RegimeInstallDirRegistry");
            AssertMsiProfileRowMismatch(
                profile,
                "AppSearch",
                "APPLICATIONFOLDER|RegimeInstallDirRegistry");
            AssertMsiProfileRowMismatch(
                profile,
                "File",
                "fil4D9BD38000F7BBE9FA37B3949730CD45");
            AssertMsiProfileRowMismatch(
                profile,
                "CustomAction",
                "SetInstallRegimeService");
            AssertMsiProfileRowMismatch(
                profile,
                "InstallExecuteSequence",
                "InstallAutoApdater");
            AssertMsiProfileRowMismatch(
                profile,
                "InstallExecuteSequence",
                "UninstallAutoApdater");
            AssertMsiProfileRowMismatch(
                profile,
                "InstallExecuteSequence",
                "RemoveAll");
            AssertMsiProfileRowMismatch(
                profile,
                "InstallExecuteSequence",
                "StartRegimeService");
            AssertMsiProfileRowMismatch(
                profile,
                "InstallExecuteSequence",
                "NotAutoStartlYeniseiService");
            AssertMsiProfileRowMismatch(
                profile,
                "Property",
                "MsiHiddenProperties");
            AssertMsiProfileRowMismatch(
                profile,
                "CustomAction",
                "SetStopEPMD");
        }

        private static void LocalModuleMsiProfileIsDerivedFromSelectedPackage()
        {
            LocalModuleMsiCapabilityProfile fixture =
                LoadLocalModuleMsiCapabilityFixture();
            LocalModuleMsiDatabaseSnapshot snapshot =
                CreateLocalModuleMsiSnapshot(fixture);
            WindowsInstallerPackageMetadata metadata =
                CreateLocalModulePackageMetadata();
            metadata.PackageCode = fixture.PackageCode;
            TrustedFileExpectation trust =
                CreateLocalModuleResolverTrust(fixture);
            LocalModuleMsiCapabilityResolver resolver =
                new LocalModuleMsiCapabilityResolver();

            LocalModuleMsiCapabilityProfile derived =
                resolver.Resolve(metadata, trust, snapshot);
            AssertEqual(0, snapshot.Compare(derived).Count,
                "Выведенный профиль должен совпадать с самим пакетом.");
            AssertEqual(metadata.ProductCode, derived.ProductCode,
                "Профиль должен фиксировать ProductCode выбранного файла.");
            AssertEqual(fixture.Sha256, derived.Sha256,
                "Профиль должен фиксировать хеш выбранного файла.");

            WindowsInstallerPackageMetadata newer = metadata.Clone();
            newer.ProductVersion = "2.7.0";
            newer.ProductCode = "{11111111-2222-3333-4444-555555555555}";
            newer.PackageCode = "{66666666-7777-8888-9999-AAAAAAAAAAAA}";
            LocalModuleMsiCapabilityProfile upgraded =
                resolver.Resolve(newer, trust, snapshot);
            AssertEqual("2.7.0", upgraded.ProductVersion,
                "Следующая версия ЛМ ЧЗ должна приниматься без правки приложения.");

            TrustedFileExpectation foreignSigner =
                CreateLocalModuleResolverTrust(fixture);
            foreignSigner.SignerSubject = "CN=Другой поставщик";
            AssertThrows<InvalidDataException>(delegate {
                resolver.Resolve(metadata, foreignSigner, snapshot);
            }, "MSI без подписи ЦРПТ должен отвергаться.");

            WindowsInstallerPackageMetadata foreignUpgrade = metadata.Clone();
            foreignUpgrade.UpgradeCode =
                "{00000000-0000-0000-0000-000000000009}";
            AssertThrows<InvalidDataException>(delegate {
                resolver.Resolve(foreignUpgrade, trust, snapshot);
            }, "MSI с чужим UpgradeCode должен отвергаться.");

            LocalModuleMsiDatabaseSnapshot renamed =
                CreateLocalModuleMsiSnapshot(fixture);
            RenameSnapshotService(renamed, "yenisei", "couchdb");
            AssertThrows<InvalidDataException>(delegate {
                resolver.Resolve(metadata, trust, renamed);
            }, "Пакет с другим набором служб должен отвергаться до клонирования.");

            LocalModuleMsiDatabaseSnapshot withoutFolder =
                CreateLocalModuleMsiSnapshot(fixture);
            RemoveSnapshotRow(withoutFolder, "Directory", "APPLICATIONFOLDER");
            AssertThrows<InvalidDataException>(delegate {
                resolver.Resolve(metadata, trust, withoutFolder);
            }, "Пакет без каталога установки должен отвергаться.");
        }

        private static void LocalModuleMsiStructureIsReadOnlyAfterTrust()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "regime-test.msi");
                byte[] content = Encoding.ASCII.GetBytes("locked-msi-profile-order");
                File.WriteAllBytes(path, content);
                LocalModuleInstallerSelection selection =
                    CreateLocalModuleInstallerSelection(path, content);
                LocalModuleMsiCapabilityProfile profile =
                    LoadLocalModuleMsiCapabilityFixture();
                FakeLocalModuleMsiProfileReader structuralReader =
                    new FakeLocalModuleMsiProfileReader(
                        CreateLocalModuleMsiSnapshot(profile));
                LocalModulePackageVerifier verifier = new LocalModulePackageVerifier(
                    new FakeWindowsInstallerPackageReader(
                        CreateLocalModulePackageMetadata()),
                    new FakeFileTrustVerifier(
                        CreateLocalModuleTrustExpectation(selection),
                        false),
                    new FakePathSafety(true),
                    new FakeLocalModuleMsiCapabilityResolver(profile),
                    structuralReader);

                AssertThrows<InvalidDataException>(delegate {
                    verifier.VerifyAndLock(selection).Dispose();
                }, "Untrusted MSI input must be rejected.");
                AssertEqual(0, structuralReader.ReadCount,
                    "Structural DTF reads must not occur before trust succeeds.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void LocalModuleInstallerPropertiesArePinnedByVendorProfile()
        {
            LocalModuleMsiCapabilityProfile profile =
                LoadLocalModuleMsiCapabilityFixture();
            LocalModuleInstallerProperties.RequireSupportedBy(profile);

            string properties = LocalModuleInstallerProperties.Build(
                @"C:\Program Files\Regime1\");
            AssertEqual(
                "ADMINUSER=admin ADMINPASSWORD=admin AUTOSERVICE=1 " +
                    "APPLICATIONFOLDER=\"C:\\Program Files\\Regime1\\\"",
                properties,
                "Installer properties must use the vendor names and request automatic start.");
            AssertTrue(
                properties.IndexOf("ADMINLOGIN", StringComparison.Ordinal) < 0,
                "The retired ADMINLOGIN name must never reach msiexec.");
            IList<string> names = LocalModuleInstallerProperties.Names();
            for (int index = 0; index < names.Count; index++)
            {
                AssertTrue(
                    properties.IndexOf(
                        names[index] + "=",
                        StringComparison.Ordinal) >= 0,
                    "Every declared installer property must be passed: " +
                        names[index]);
            }
            AssertThrows<InvalidDataException>(delegate {
                LocalModuleInstallerProperties.Build("C:\\Program \"Files\"\\Regime1");
            }, "A quoted installation root must be rejected.");

            LocalModuleMsiCapabilityProfile renamed =
                LoadLocalModuleMsiCapabilityFixture();
            MsiProfileRow hidden = FindLocalModuleProfileRow(
                renamed,
                "Property",
                "MsiHiddenProperties");
            hidden.Values[0] = hidden.Values[0].Replace("ADMINUSER", "ADMINLOGIN");
            AssertThrows<InvalidDataException>(delegate {
                LocalModuleInstallerProperties.RequireSupportedBy(renamed);
            }, "A renamed hidden credential property must fail before msiexec.");

            LocalModuleMsiCapabilityProfile ungated =
                LoadLocalModuleMsiCapabilityFixture();
            MsiProfileRow start = FindLocalModuleProfileRow(
                ungated,
                "InstallExecuteSequence",
                "StartRegimeService");
            start.Values[0] = "NOT Installed AND NOT REMOVE";
            AssertThrows<InvalidDataException>(delegate {
                LocalModuleInstallerProperties.RequireSupportedBy(ungated);
            }, "Automatic start must be proven by the AUTOSERVICE condition.");

            LocalModuleMsiCapabilityProfile missing =
                LoadLocalModuleMsiCapabilityFixture();
            // The deserialized fixture exposes a fixed-size array.
            missing.Rows = new List<MsiProfileRow>(missing.Rows);
            missing.Rows.Remove(FindLocalModuleProfileRow(
                missing,
                "InstallExecuteSequence",
                "NotAutoStartlYeniseiService"));
            AssertThrows<InvalidDataException>(delegate {
                LocalModuleInstallerProperties.RequireSupportedBy(missing);
            }, "A profile without the demand-start action cannot prove automatic start.");
        }

        private static MsiProfileRow FindLocalModuleProfileRow(
            LocalModuleMsiCapabilityProfile profile,
            string table,
            string key)
        {
            for (int index = 0; index < profile.Rows.Count; index++)
            {
                MsiProfileRow row = profile.Rows[index];
                if (string.Equals(row.Table, table, StringComparison.Ordinal) &&
                    string.Equals(row.Key, key, StringComparison.Ordinal))
                {
                    return row;
                }
            }
            throw new InvalidOperationException(
                "Profile fixture lacks " + table + ":" + key + ".");
        }

        private static void LocalModuleConfigurationRejectsMissingApiCredentials()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                LocalModuleInstalledLayout clone = LocalModuleInstalledLayout.Create(
                    Path.Combine(directory, "Regime1"),
                    1,
                    6995,
                    7984);
                WriteInstalledLocalModuleConfiguration(clone, new string('a', 32));
                LocalModuleConfigurationInspector inspector =
                    new LocalModuleConfigurationInspector();
                inspector.Inspect(clone);

                File.WriteAllText(
                    clone.ApiLocalIniPath,
                    "[api]\r\nip_address = 0.0.0.0\r\nport = 6995\r\n" +
                    ";login =\r\n;password =\r\n" +
                    "[local]\r\ndb_url = http://127.0.0.1:7984\r\n");
                AssertThrows<InvalidDataException>(delegate {
                    inspector.Inspect(clone);
                }, "An installed module without [api] credentials must be rejected.");

                File.WriteAllText(
                    clone.ApiLocalIniPath,
                    "[api]\r\nip_address = 0.0.0.0\r\nport = 6995\r\n" +
                    "login = \r\npassword = \r\n" +
                    "[local]\r\ndb_url = http://127.0.0.1:7984\r\n");
                AssertThrows<InvalidDataException>(delegate {
                    inspector.Inspect(clone);
                }, "Empty [api] credentials must be rejected like missing ones.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(directory);
            }
        }

        private static LocalModuleMsiCapabilityProfile
            LoadLocalModuleMsiCapabilityFixture()
        {
            return MsiTestPackageFactory.LoadFixtureProfile();
        }

        private static void LocalModuleMsiCloneIdentityIsStableExceptPackageCode()
        {
            Queue<Guid> packageCodes = new Queue<Guid>();
            packageCodes.Enqueue(new Guid("10000000-0000-0000-0000-000000000001"));
            packageCodes.Enqueue(new Guid("10000000-0000-0000-0000-000000000002"));
            packageCodes.Enqueue(new Guid("10000000-0000-0000-0000-000000000003"));
            LocalModuleMsiIdentityFactory factory =
                new LocalModuleMsiIdentityFactory(delegate {
                    return packageCodes.Dequeue();
                });

            LocalModuleMsiCloneIdentity first = factory.Create(
                "2.6.1",
                "7701234567",
                1);
            LocalModuleMsiCloneIdentity repeat = factory.Create(
                "2.6.1",
                "7701234567",
                1);
            LocalModuleMsiCloneIdentity otherInn = factory.Create(
                "2.6.1",
                "7701234568",
                2);

            AssertEqual(first.ProductCode, repeat.ProductCode,
                "ProductCode must be stable for version plus INN.");
            AssertEqual(first.UpgradeCode, repeat.UpgradeCode,
                "UpgradeCode must be stable for the logical INN instance.");
            AssertFalse(first.PackageCode == repeat.PackageCode,
                "Every generated physical MSI must receive a fresh PackageCode.");
            AssertFalse(first.ProductCode == first.UpgradeCode ||
                    first.ProductCode == first.PackageCode ||
                    first.UpgradeCode == first.PackageCode,
                "ProductCode, UpgradeCode and PackageCode must be unique.");
            AssertFalse(first.ProductCode == otherInn.ProductCode,
                "Different INNs must not share ProductCode.");
            AssertFalse(first.UpgradeCode == otherInn.UpgradeCode,
                "Different INNs must not share UpgradeCode.");
            AssertEqual("Regime1", first.InstallDirectoryName,
                "Clone ordinal must determine only the visible instance suffix.");
            AssertEqual("regime1", first.ApiServiceName,
                "The API service name must be instance-scoped.");
            AssertEqual("yenisei1", first.DatabaseServiceName,
                "The database service name must be instance-scoped.");
            AssertEqual(5, GuidVersion(first.ProductCode),
                "Stable clone IDs must carry RFC-4122 version bits.");
            AssertEqual(2, GuidVariant(first.ProductCode),
                "Stable clone IDs must carry the RFC-4122 variant.");
        }

        private static void LocalModuleMsiTransformerEditsOnlyProfiledRows()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                using (MsiTestPackageFixture fixture =
                    MsiTestPackageFactory.Create(directory))
                {
                    LocalModuleMsiCloneIdentity identity =
                        new LocalModuleMsiIdentityFactory(delegate {
                            return new Guid(
                                "20000000-0000-0000-0000-000000000001");
                        }).Create("2.6.1", "7701234567", 1);
                    LocalModuleMsiTransformPlan plan =
                        LocalModuleMsiTransformPlan.Create(
                            identity,
                            "2.6.1",
                            6995,
                            7984);
                    string outputPath = Path.Combine(directory, "clone.msi");

                    using (TransformedLocalModuleMsi transformed =
                        new LocalModuleMsiTransformer().Transform(
                            fixture.Package,
                            plan,
                            outputPath))
                    {
                        AssertSequence(
                            MsiTestPackageFactory.ReadCabinetMembers(
                                fixture.Package.FullPath,
                                "media1.cab"),
                            MsiTestPackageFactory.ReadCabinetMembers(
                                outputPath,
                                "media1.cab"),
                            "The rebuilt first cabinet must preserve exact source member order.");
                        AssertSequence(
                            MsiTestPackageFactory.ReadCabinetMembers(
                                fixture.Package.FullPath,
                                "Disk1.cab"),
                            MsiTestPackageFactory.ReadCabinetMembers(
                                outputPath,
                                "Disk1.cab"),
                            "The rebuilt second cabinet must preserve exact source member order.");
                        AssertEqual(identity.ProductCode.ToString("B").ToUpperInvariant(),
                            MsiTestPackageFactory.ReadProperty(
                                outputPath,
                                "ProductCode"),
                            "ProductCode must be updated exactly once.");
                        AssertEqual(identity.UpgradeCode.ToString("B").ToUpperInvariant(),
                            MsiTestPackageFactory.ReadProperty(
                                outputPath,
                                "UpgradeCode"),
                            "UpgradeCode must be updated exactly once.");
                        AssertEqual(identity.UpgradeCode.ToString("B").ToUpperInvariant(),
                            MsiTestPackageFactory.ReadRowValue(
                                outputPath,
                                "Upgrade",
                                "UpgradeCode",
                                identity.UpgradeCode.ToString("B").ToUpperInvariant(),
                                "UpgradeCode"),
                            "Upgrade table must not detect and remove the base product.");
                        AssertEqual(identity.PackageCode.ToString("B").ToUpperInvariant(),
                            MsiTestPackageFactory.ReadPackageCode(outputPath),
                            "Summary PackageCode must be fresh.");
                        AssertEqual("Regime1", MsiTestPackageFactory.ReadRowValue(
                                outputPath,
                                "Directory",
                                "Directory",
                                "APPLICATIONFOLDER",
                                "DefaultDir"),
                            "Only APPLICATIONFOLDER must receive the clone suffix.");
                        AssertContains(MsiTestPackageFactory.ReadRowValue(
                                outputPath,
                                "Registry",
                                "Registry",
                                "RegimeInstallDir",
                                "Key"),
                            "экземпляр 1");
                        AssertContains(MsiTestPackageFactory.ReadRowValue(
                                outputPath,
                                "RegLocator",
                                "Signature_",
                                "RegimeInstallDirRegistry",
                                "Key"),
                            "экземпляр 1");
                        AssertContains(MsiTestPackageFactory.ReadRowValue(
                                outputPath,
                                "CustomAction",
                                "Action",
                                "SetInstallRegimeService",
                                "Target"),
                            "\"regime1\"");
                        AssertContains(MsiTestPackageFactory.ReadRowValue(
                                outputPath,
                                "CustomAction",
                                "Action",
                                "SetInstallYeniseiService",
                                "Target"),
                            "\"yenisei1\"");
                        AssertEqual("1=0", MsiTestPackageFactory.ReadRowValue(
                                outputPath,
                                "InstallExecuteSequence",
                                "Action",
                                "InstallAutoApdater",
                                "Condition"),
                            "Clone auto-updater must be disabled.");
                        AssertEqual("1=0", MsiTestPackageFactory.ReadRowValue(
                                outputPath,
                                "InstallExecuteSequence",
                                "Action",
                                "UninstallAutoApdater",
                                "Condition"),
                            "Clone auto-updater uninstall must be disabled.");
                        AssertEqual("1=0", MsiTestPackageFactory.ReadRowValue(
                                outputPath,
                                "InstallExecuteSequence",
                                "Action",
                                "RemoveAll",
                                "Condition"),
                            "Clone data cleanup must be owned by the app.");
                        AssertEqual("1=0", MsiTestPackageFactory.ReadRowValue(
                                outputPath,
                                "InstallExecuteSequence",
                                "Action",
                                "StopEPMD",
                                "Condition"),
                            "Clone uninstall must not kill global epmd.");
                        AssertEqual("regime1@127.0.0.1", plan.ApiNodeName,
                            "The transform plan must carry the isolated API node.");
                        AssertEqual("yenisei1@127.0.0.1", plan.DatabaseNodeName,
                            "The transform plan must carry the isolated database node.");
                        AssertEqual(6995, plan.ApiPort,
                            "The planned API port must survive table transformation.");
                        AssertEqual(7984, plan.DatabasePort,
                            "The planned database port must survive table transformation.");
                    }
                }
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(directory);
            }
        }

        private static int GuidVersion(Guid value)
        {
            string text = value.ToString("N");
            return int.Parse(text.Substring(12, 1));
        }

        private static void LocalModuleMsiOutputRejectsCabinetAndTableDrift()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                using (MsiTestPackageFixture fixture =
                    MsiTestPackageFactory.Create(directory))
                {
                    LocalModuleMsiCloneIdentity identity =
                        new LocalModuleMsiIdentityFactory(delegate {
                            return new Guid(
                                "30000000-0000-0000-0000-000000000001");
                        }).Create("2.6.1", "7701234567", 1);
                    LocalModuleMsiTransformPlan plan =
                        LocalModuleMsiTransformPlan.Create(
                            identity,
                            "2.6.1",
                            6995,
                            7984);
                    string outputPath = Path.Combine(directory, "verified-clone.msi");
                    using (TransformedLocalModuleMsi transformed =
                        new LocalModuleMsiTransformer().Transform(
                            fixture.Package,
                            plan,
                            outputPath))
                    {
                        AssertTrue(transformed.Verified != null,
                            "A transformed MSI must be byte-verified before return.");
                        AssertEqual(4, transformed.Verified.ModifiedFileCount,
                            "Exactly four mutable config files may differ.");
                        AssertEqual(6, transformed.Verified.TotalFileCount,
                            "The synthetic second-CAB payload must also be verified.");
                        AssertEqual(0, fixture.Package.DatabaseSnapshot.Files.Count,
                            "Transform must not mutate the locked source profile snapshot.");
                    }

                    string wrongSize = Path.Combine(directory, "wrong-size.msi");
                    File.Copy(outputPath, wrongSize);
                    MsiTestPackageFactory.ChangeFileSize(
                        wrongSize,
                        "fil4D9BD38000F7BBE9FA37B3949730CD45");
                    AssertThrows<InvalidDataException>(delegate {
                        new LocalModuleMsiOutputVerifier().Verify(
                            fixture.Package.DatabaseSnapshot,
                            wrongSize,
                            plan);
                    }, "A wrong FileSize must fail output verification.");

                    string wrongHash = Path.Combine(directory, "wrong-hash.msi");
                    File.Copy(outputPath, wrongHash);
                    MsiTestPackageFactory.ChangeFileHash(
                        wrongHash,
                        "fil4D9BD38000F7BBE9FA37B3949730CD45");
                    AssertThrows<InvalidDataException>(delegate {
                        new LocalModuleMsiOutputVerifier().Verify(
                            fixture.Package.DatabaseSnapshot,
                            wrongHash,
                            plan);
                    }, "A wrong MsiFileHash part must fail output verification.");

                    string wrongMedia = Path.Combine(directory, "wrong-media.msi");
                    File.Copy(outputPath, wrongMedia);
                    MsiTestPackageFactory.ChangeMediaSequence(wrongMedia, 2);
                    AssertThrows<InvalidDataException>(delegate {
                        new LocalModuleMsiOutputVerifier().Verify(
                            fixture.Package.DatabaseSnapshot,
                            wrongMedia,
                            plan);
                    }, "A changed second-CAB sequence must fail output verification.");

                    string wrongFirstMedia = Path.Combine(
                        directory,
                        "wrong-first-media.msi");
                    File.Copy(outputPath, wrongFirstMedia);
                    MsiTestPackageFactory.ChangeMediaSequence(wrongFirstMedia, 1);
                    AssertThrows<InvalidDataException>(delegate {
                        new LocalModuleMsiOutputVerifier().Verify(
                            fixture.Package.DatabaseSnapshot,
                            wrongFirstMedia,
                            plan);
                    }, "A changed first-CAB sequence must fail output verification.");

                    string missingFile = Path.Combine(directory, "missing-file.msi");
                    File.Copy(outputPath, missingFile);
                    MsiTestPackageFactory.RemoveFileRow(
                        missingFile,
                        "filSECOND00000000000000000000000001");
                    AssertThrows<InvalidDataException>(delegate {
                        new LocalModuleMsiOutputVerifier().Verify(
                            fixture.Package.DatabaseSnapshot,
                            missingFile,
                            plan);
                    }, "A missing output file must fail output verification.");

                    string unexpectedFile = Path.Combine(
                        directory,
                        "unexpected-file.msi");
                    File.Copy(outputPath, unexpectedFile);
                    MsiTestPackageFactory.AddUnexpectedCabinetFile(unexpectedFile);
                    AssertThrows<InvalidDataException>(delegate {
                        new LocalModuleMsiOutputVerifier().Verify(
                            fixture.Package.DatabaseSnapshot,
                            unexpectedFile,
                            plan);
                    }, "An unexpected output file must fail output verification.");

                    string wrongBytes = Path.Combine(directory, "wrong-bytes.msi");
                    File.Copy(outputPath, wrongBytes);
                    MsiTestPackageFactory.ChangeCabinetFile(
                        wrongBytes,
                        "fil4D9BD38000F7BBE9FA37B3949730CD45");
                    AssertThrows<InvalidDataException>(delegate {
                        new LocalModuleMsiOutputVerifier().Verify(
                            fixture.Package.DatabaseSnapshot,
                            wrongBytes,
                            plan);
                    }, "Changed generated config bytes must fail output verification.");

                    string wrongUnchangedBytes = Path.Combine(
                        directory,
                        "wrong-unchanged-bytes.msi");
                    File.Copy(outputPath, wrongUnchangedBytes);
                    MsiTestPackageFactory.ChangeCabinetFile(
                        wrongUnchangedBytes,
                        "fil6C2420445C448A9D193F9397AB772B3F");
                    AssertThrows<InvalidDataException>(delegate {
                        new LocalModuleMsiOutputVerifier().Verify(
                            fixture.Package.DatabaseSnapshot,
                            wrongUnchangedBytes,
                            plan);
                    }, "Changed immutable default.ini bytes must fail verification.");
                }
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(directory);
            }
        }

        private static int GuidVariant(Guid value)
        {
            string text = value.ToString("N");
            int high = int.Parse(
                text.Substring(16, 1),
                System.Globalization.NumberStyles.HexNumber);
            return (high & 8) == 8 ? 2 : 0;
        }

        private static void LocalModuleMsiStagingRecoversEveryMutationBoundary()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string machineRoot = Path.Combine(directory, "MultiKKT");
                Directory.CreateDirectory(machineRoot);
                string source = Path.Combine(directory, "source-package.bin");
                File.WriteAllText(source, "synthetic-package");
                LocalModuleMsiStagingStage[] stages =
                {
                    LocalModuleMsiStagingStage.JournalCreated,
                    LocalModuleMsiStagingStage.SourceCopied,
                    LocalModuleMsiStagingStage.Transformed,
                    LocalModuleMsiStagingStage.Verified,
                    LocalModuleMsiStagingStage.InstallerReturned,
                    LocalModuleMsiStagingStage.ManifestPersisted,
                    LocalModuleMsiStagingStage.Installed
                };
                for (int index = 0; index < stages.Length; index++)
                {
                    FakePathSafety safety = new FakePathSafety(true);
                    LocalModuleMsiWorkspace workspace =
                        LocalModuleMsiWorkspace.Create(
                            machineRoot,
                            Guid.NewGuid().ToString("N"),
                            new string((char)('0' + index), 32),
                            safety);
                    if (stages[index] >= LocalModuleMsiStagingStage.SourceCopied)
                        workspace.CopySource(source);
                    if (stages[index] >= LocalModuleMsiStagingStage.Transformed)
                    {
                        File.Copy(source, workspace.OutputMsiPath);
                        workspace.MarkTransformed();
                    }
                    if (stages[index] >= LocalModuleMsiStagingStage.Verified)
                        workspace.MarkVerified();
                    if (stages[index] >= LocalModuleMsiStagingStage.InstallerReturned)
                        workspace.MarkInstallerReturned();
                    if (stages[index] >= LocalModuleMsiStagingStage.ManifestPersisted)
                        workspace.MarkManifestPersisted();
                    if (stages[index] >= LocalModuleMsiStagingStage.Installed)
                        workspace.MarkInstalled(
                            "{10000000-0000-0000-0000-000000000001}",
                            "{20000000-0000-0000-0000-000000000001}");
                    string ownedRoot = workspace.RootPath;
                    int protectionCalls = safety.ProtectionCallCount;
                    LocalModuleMsiWorkspace.RecoverPending(machineRoot, safety);
                    AssertFalse(Directory.Exists(ownedRoot),
                        "Recovery must delete the exact journal-owned staging root.");
                    AssertTrue(safety.ProtectionCallCount > protectionCalls,
                        "Recovery must reconstruct the exact protected ACL before deletion.");
                }

                string prefixSibling = Path.Combine(
                    machineRoot,
                    "Operations",
                    "LocalModuleMsi2");
                Directory.CreateDirectory(prefixSibling);
                string sentinel = Path.Combine(prefixSibling, "keep.txt");
                File.WriteAllText(sentinel, "keep");
                LocalModuleMsiWorkspace.RecoverPending(
                    machineRoot,
                    new FakePathSafety(true));
                AssertTrue(File.Exists(sentinel),
                    "Recovery must never use a staging-root prefix match.");

                FakePathSafety interruptedWriteSafety = new FakePathSafety(true);
                LocalModuleMsiWorkspace interruptedWrite =
                    LocalModuleMsiWorkspace.Create(
                        machineRoot,
                        Guid.NewGuid().ToString("N"),
                        new string('d', 32),
                        interruptedWriteSafety);
                File.WriteAllText(
                    Path.Combine(
                        interruptedWrite.RootPath,
                        LocalModuleMsiStagingJournalStore.JournalTempFileName),
                    "interrupted");
                LocalModuleMsiWorkspace.RecoverPending(
                    machineRoot,
                    interruptedWriteSafety);
                AssertFalse(Directory.Exists(interruptedWrite.RootPath),
                    "Recovery must delete an interrupted journal temp file.");

                FakePathSafety foreignSafety = new FakePathSafety(true);
                LocalModuleMsiWorkspace foreign = LocalModuleMsiWorkspace.Create(
                    machineRoot,
                    Guid.NewGuid().ToString("N"),
                    new string('f', 32),
                    foreignSafety);
                string foreignFile = Path.Combine(foreign.RootPath, "foreign.txt");
                File.WriteAllText(foreignFile, "foreign");
                AssertThrows<InvalidDataException>(delegate {
                    foreign.Cleanup();
                }, "Cleanup must reject a file outside the exact allowlist.");
                AssertTrue(File.Exists(foreignFile),
                    "Rejected foreign data must not be deleted.");
                File.Delete(foreignFile);
                foreign.Cleanup();

                FakePathSafety safe = new FakePathSafety(true);
                LocalModuleMsiWorkspace unsafeWorkspace =
                    LocalModuleMsiWorkspace.Create(
                        machineRoot,
                        Guid.NewGuid().ToString("N"),
                        new string('e', 32),
                        safe);
                AssertThrows<InvalidDataException>(delegate {
                    LocalModuleMsiWorkspace.RecoverPending(
                        machineRoot,
                        new FakePathSafety(false));
                }, "Recovery must reject a reparse or path-escape observation.");
                AssertTrue(Directory.Exists(unsafeWorkspace.RootPath),
                    "Unsafe recovery must not delete the staging root.");
                unsafeWorkspace.Cleanup();
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(directory);
            }
        }

        private static void WindowsInstallerApiKeepsCredentialsOutOfDiagnostics()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string package = Path.Combine(directory, "package.msi");
                File.WriteAllText(package, "synthetic");
                FakeWindowsInstallerNative native =
                    new FakeWindowsInstallerNative();
                WindowsInstallerApi api = new WindowsInstallerApi(native);
                const string hidden =
                    "ADMINUSER=admin ADMINPASSWORD=admin AUTOSERVICE=1";
                AssertEqual((uint)0, api.Install(package, hidden),
                    "Successful MSI result must remain unchanged.");
                AssertEqual(Path.GetFullPath(package), native.PackagePath,
                    "Native MSI install must receive the exact package path.");
                AssertEqual(hidden, native.CommandLine,
                    "Native MSI install must receive the required credentials.");
                AssertEqual(
                    "ADMINUSER=<redacted> ADMINPASSWORD=<redacted> " +
                        "AUTOSERVICE=<redacted>",
                    WindowsInstallerApi.RedactInstallProperties(hidden),
                    "Diagnostics must never expose MSI credentials.");

                const string product =
                    "{10000000-0000-0000-0000-000000000001}";
                AssertEqual((uint)0, api.Repair(product),
                    "Successful MSI repair must remain unchanged.");
                AssertEqual(5, native.InstallState,
                    "Repair must request INSTALLSTATE_DEFAULT.");
                AssertEqual((uint)0, api.Uninstall(product),
                    "Successful MSI uninstall must remain unchanged.");
                AssertEqual(0, native.InstallLevel,
                    "MSI configuration must use INSTALLLEVEL_DEFAULT.");
                AssertEqual(2, native.InstallState,
                    "Uninstall must request INSTALLSTATE_ABSENT.");
                AssertEqual(6, native.UiTransitions.Count,
                    "Every MSI operation must disable and restore UI.");
                AssertEqual(2, native.UiTransitions[0],
                    "MSI operations must run with INSTALLUILEVEL_NONE.");
                AssertEqual(5, native.UiTransitions[5],
                    "MSI operations must restore the previous UI level.");

                native.ReturnCode = 1603;
                try
                {
                    api.Install(package, hidden);
                    throw new InvalidOperationException(
                        "Nonzero MSI result must throw.");
                }
                catch (WindowsInstallerOperationException exception)
                {
                    AssertEqual((uint)1603, exception.MsiErrorCode,
                        "MSI error code must survive unchanged.");
                    AssertFalse(exception.Message.Contains("admin"),
                        "MSI exception must not contain credentials.");
                }
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(directory);
            }
        }

        private static void WindowsInstallerReceivesTerminatedAbsoluteLmDirectory()
        {
            MethodInfo method = typeof(WindowsLocalModuleMsiProvisioningPlatform)
                .GetMethod(
                    "InstallProperties",
                    BindingFlags.Static | BindingFlags.NonPublic);
            string properties = (string)method.Invoke(
                null,
                new object[] { @"D:\Program Files\Regime1" });
            AssertTrue(
                properties.Contains(
                    "APPLICATIONFOLDER=\"D:\\Program Files\\Regime1\\\""),
                "Windows Installer directory properties must end in a separator.");
        }

        private static void InstalledLocalModuleReaderScansBothRegistryViews()
        {
            FakeInstalledLocalModuleRegistry registry =
                new FakeInstalledLocalModuleRegistry();
            registry.Registry64.Add(new InstalledLocalModuleProduct
            {
                ProductCode = "{20000000-0000-0000-0000-000000000001}",
                DisplayName = "Unrelated",
                DisplayVersion = "1.0",
                InstallLocation = @"C:\Unrelated"
            });
            registry.Registry32.Add(new InstalledLocalModuleProduct
            {
                ProductCode = "{10000000-0000-0000-0000-000000000001}",
                DisplayName = "Локальный модуль Честный Знак",
                DisplayVersion = "2.6.1",
                InstallLocation = @"D:\Program Files\Regime"
            });
            InstalledLocalModuleProductReader reader =
                new InstalledLocalModuleProductReader(registry);
            InstalledLocalModuleProduct exact = reader.FindExact(
                "{10000000-0000-0000-0000-000000000001}",
                "Локальный модуль Честный Знак",
                "2.6.1",
                @"D:\Program Files\Regime\");
            AssertTrue(exact != null,
                "Exact product must be found in the 32-bit registry view.");
            AssertTrue(registry.Read32 && registry.Read64,
                "Installed product inventory must scan both HKLM registry views.");
            AssertTrue(reader.FindExact(
                exact.ProductCode,
                exact.DisplayName,
                exact.DisplayVersion,
                @"D:\Other") == null,
                "A different install root must not match.");
        }

        private static void LocalModuleMsiManifestsGuardOwnershipAndContent()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string machineRoot = Path.Combine(directory, "MultiKKT");
                Directory.CreateDirectory(machineRoot);
                LocalModuleMsiManifestStore store =
                    new LocalModuleMsiManifestStore(
                        machineRoot,
                        new FakePathSafety(true),
                        "S-1-5-21-111-222-333-1001");
                LocalModuleMsiManifest preExisting =
                    LocalModuleMsiManifest.Create(
                        "1234567890",
                        0,
                        5995,
                        5984,
                        "{10000000-0000-0000-0000-000000000001}",
                        "{20000000-0000-0000-0000-000000000001}",
                        "1.5.2",
                        @"D:\Program Files\Regime",
                        false,
                        true,
                        new string('a', 32));
                AssertFalse(preExisting.CanRemove,
                    "A pre-existing base product must be preserved.");
                store.Write(preExisting);
                LocalModuleMsiManifest read = store.Read(preExisting.Inn);
                AssertEqual(preExisting.ProductCode, read.ProductCode,
                    "Manifest read-back must preserve exact product identity.");
                string operatorInventoryPath = Path.Combine(
                    machineRoot,
                    "OperatorInventory",
                    "LocalModuleMsi",
                    preExisting.Inn + ".json");
                AssertTrue(File.Exists(operatorInventoryPath),
                    "Every manifest write must publish a nonsecret operator projection.");
                string operatorInventory = File.ReadAllText(
                    operatorInventoryPath,
                    Encoding.UTF8);
                AssertContains(operatorInventory, preExisting.ManifestSha256);
                AssertFalse(operatorInventory.IndexOf(
                        "OwnershipNonce",
                        StringComparison.OrdinalIgnoreCase) >= 0,
                    "Operator inventory must not expose the ownership nonce.");
                LocalModuleMsiLifecycleJournal journal =
                    LocalModuleMsiLifecycleJournal.Create(
                        "10000000000000000000000000000001",
                        MsiRequest(preExisting.Inn, 0, 5995, 5984, null),
                        preExisting.OwnershipNonce,
                        LocalModuleMsiLifecycleStage.Observing,
                        string.Empty,
                        false);
                store.WriteJournal(journal);
                AssertEqual(journal.OperationId,
                    store.ReadJournal(preExisting.Inn).OperationId,
                    "Lifecycle journal read-back must preserve exact ownership.");
                AssertThrows<InvalidDataException>(delegate {
                    store.DeleteJournal(
                        preExisting.Inn,
                        "20000000000000000000000000000002",
                        preExisting.OwnershipNonce);
                }, "A different operation must not delete the lifecycle journal.");
                store.DeleteJournal(
                    preExisting.Inn,
                    journal.OperationId,
                    preExisting.OwnershipNonce);
                AssertEqual(1, store.ReadAll().Count,
                    "Manifest inventory must return each exact INN once.");

                string path = store.GetManifestPath(preExisting.Inn);
                string json = File.ReadAllText(path);
                File.WriteAllText(
                    path,
                    json.Replace(
                        "\"PreExisting\":true",
                        "\"PreExisting\":false"));
                AssertThrows<InvalidDataException>(delegate {
                    store.Read(preExisting.Inn);
                }, "Manifest content changes must fail the hash guard.");

                LocalModuleMsiManifest applicationInstalled =
                    LocalModuleMsiManifest.Create(
                        "123456789012",
                        0,
                        5995,
                        5984,
                        "{30000000-0000-0000-0000-000000000001}",
                        "{40000000-0000-0000-0000-000000000001}",
                        "2.6.1",
                        @"D:\Program Files\Regime",
                        true,
                        false,
                        new string('b', 32));
                AssertTrue(applicationInstalled.CanRemove,
                    "An application-installed base product must be removable.");

                LocalModuleMsiManifest adjusted =
                    LocalModuleMsiManifest.Create(
                        "7707083893",
                        0,
                        5995,
                        5984,
                        "{50000000-0000-0000-0000-000000000001}",
                        "{60000000-0000-0000-0000-000000000001}",
                        "2.6.1",
                        @"D:\Program Files\Regime",
                        false,
                        true,
                        new string('c', 32));
                AssertFalse(adjusted.StartModeAdjusted,
                    "A fresh manifest must not carry a start-mode record.");
                string unadjustedHash = adjusted.ManifestSha256;
                adjusted.RecordStartModeAdjustment(
                    WindowsServiceStartMode.DemandStart,
                    WindowsServiceStartMode.DemandStart);
                AssertTrue(adjusted.StartModeAdjusted &&
                    adjusted.PreviousApiStartMode ==
                        (int)WindowsServiceStartMode.DemandStart &&
                    adjusted.PreviousDatabaseStartMode ==
                        (int)WindowsServiceStartMode.DemandStart,
                    "The start-mode record must keep both previous modes.");
                AssertFalse(string.Equals(
                        unadjustedHash,
                        adjusted.ManifestSha256,
                        StringComparison.Ordinal),
                    "The manifest hash must cover the start-mode record.");
                LocalModuleMsiManifest.Validate(adjusted);
                AssertThrows<InvalidOperationException>(delegate {
                    adjusted.RecordStartModeAdjustment(
                        WindowsServiceStartMode.AutoStart,
                        WindowsServiceStartMode.AutoStart);
                }, "A start-mode record must be written once and never overwritten.");
                store.Write(adjusted);
                LocalModuleMsiManifest adjustedRead = store.Read(adjusted.Inn);
                AssertEqual((int)WindowsServiceStartMode.DemandStart,
                    adjustedRead.PreviousApiStartMode,
                    "Manifest read-back must preserve the start-mode record.");
                string adjustedPath = store.GetManifestPath(adjusted.Inn);
                File.WriteAllText(
                    adjustedPath,
                    File.ReadAllText(adjustedPath).Replace(
                        "\"StartModeAdjusted\":true",
                        "\"StartModeAdjusted\":false"));
                AssertThrows<InvalidDataException>(delegate {
                    store.Read(adjusted.Inn);
                }, "Start-mode record changes must fail the manifest guard.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(directory);
            }
        }

        private static void InstalledLocalModuleConfigExposesOnlyCookieDigest()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                LocalModuleInstalledLayout first =
                    LocalModuleInstalledLayout.Create(
                        Path.Combine(directory, "Regime1"),
                        1,
                        6995,
                        7984);
                LocalModuleInstalledLayout second =
                    LocalModuleInstalledLayout.Create(
                        Path.Combine(directory, "Regime2"),
                        2,
                        7995,
                        8984);
                LocalModuleInstalledLayout vendorBase =
                    LocalModuleInstalledLayout.Create(
                        Path.Combine(directory, "Regime"),
                        0,
                        5995,
                        5984);
                WriteInstalledLocalModuleConfiguration(
                    first,
                    new string('a', 32));
                WriteInstalledLocalModuleConfiguration(
                    second,
                    new string('b', 32));
                WriteInstalledLocalModuleConfiguration(
                    vendorBase,
                    new string('c', 32));
                File.WriteAllText(
                    vendorBase.DatabaseLocalIniPath,
                    "[chttpd]\r\nbind_address = 0.0.0.0\r\nport = 5984\r\n");
                LocalModuleConfigurationInspector inspector =
                    new LocalModuleConfigurationInspector();
                LocalModuleConfigurationObservation observedFirst =
                    inspector.Inspect(first);
                LocalModuleConfigurationObservation observedSecond =
                    inspector.Inspect(second);
                LocalModuleConfigurationObservation observedBase =
                    inspector.Inspect(vendorBase);
                AssertEqual("0.0.0.0", observedFirst.ApiBindAddress,
                    "LM API must remain reachable from the second KKT.");
                AssertEqual("127.0.0.1", observedFirst.DatabaseBindAddress,
                    "LM database must remain loopback-only.");
                AssertEqual(6995, observedFirst.ApiPort,
                    "LM API port must match the clone plan.");
                AssertEqual(7984, observedFirst.DatabasePort,
                    "LM database port must match the clone plan.");
                AssertEqual("regime1@127.0.0.1", observedFirst.ApiNodeName,
                    "LM API node name must be isolated.");
                AssertEqual("yenisei1@127.0.0.1", observedFirst.DatabaseNodeName,
                    "LM database node name must be isolated.");
                AssertFalse(string.Equals(
                        observedFirst.CookieDigest,
                        new string('a', 32),
                        StringComparison.Ordinal),
                    "Raw cookie must never leave the inspector.");
                AssertFalse(string.Equals(
                        observedFirst.CookieDigest,
                        observedSecond.CookieDigest,
                        StringComparison.Ordinal),
                    "Different LM pairs must have different cookie digests.");
                AssertEqual("0.0.0.0", observedBase.DatabaseBindAddress,
                    "The pre-existing vendor base must remain usable without rewriting it.");

                File.AppendAllText(
                    first.ApiLocalIniPath,
                    "[api]\r\nport = 7000\r\n");
                AssertThrows<InvalidDataException>(delegate {
                    inspector.Inspect(first);
                }, "Duplicate characterized INI keys must be rejected.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(directory);
            }
        }

        private static void InstalledLocalModuleServicesStartStopAndOwnListeners()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "krs-lm-layout-" + Guid.NewGuid().ToString("N"));
            LocalModuleInstalledLayout layout =
                LocalModuleInstalledLayout.Create(root, 1, 6995, 7984);
            FakeWindowsServiceApi services = new FakeWindowsServiceApi();
            services.SetRecord(new WindowsServiceRecord
            {
                ServiceName = layout.DatabaseServiceName,
                ImagePath = "\"" + layout.ServiceImagePath + "\"",
                State = WindowsServiceState.Running,
                ProcessId = 5001
            });
            services.SetRecord(new WindowsServiceRecord
            {
                ServiceName = layout.ApiServiceName,
                ImagePath = layout.ServiceImagePath,
                State = WindowsServiceState.Running,
                ProcessId = 5002
            });
            FakeDirectTcpListenerOwnerReader listeners =
                new FakeDirectTcpListenerOwnerReader();
            listeners.SetOwners(layout.DatabasePort, 6001);
            listeners.SetOwners(layout.ApiPort, 6002);
            FakeProcessTreeReader parents = new FakeProcessTreeReader();
            parents.SetParent(6001, 5001);
            parents.SetParent(6002, 5002);
            LocalModuleMsiReadinessProbe probe =
                new LocalModuleMsiReadinessProbe(
                    services,
                    listeners,
                    parents,
                    1,
                    delegate { });
            AssertTrue(probe.ProbeOwnedListener(
                    layout,
                    LocalModuleProcessRole.Database).IsValid,
                "Database listener must belong to the exact service tree.");
            AssertTrue(probe.ProbeOwnedListener(
                    layout,
                    LocalModuleProcessRole.Api).IsValid,
                "API listener must belong to the exact service tree.");
            services.SetRecord(new WindowsServiceRecord
            {
                ServiceName = layout.ApiServiceName,
                ImagePath = Path.Combine(root, "foreign.exe"),
                State = WindowsServiceState.Running,
                ProcessId = 5002
            });
            AssertFalse(probe.ProbeOwnedListener(
                    layout,
                    LocalModuleProcessRole.Api).IsValid,
                "A service outside the exact install root must be rejected.");

            FakeWindowsServiceApi orderedServices = new FakeWindowsServiceApi();
            orderedServices.SetRecord(new WindowsServiceRecord
            {
                ServiceName = layout.DatabaseServiceName,
                ImagePath = layout.ServiceImagePath,
                State = WindowsServiceState.Stopped
            });
            orderedServices.SetRecord(new WindowsServiceRecord
            {
                ServiceName = layout.ApiServiceName,
                ImagePath = layout.ServiceImagePath,
                State = WindowsServiceState.Stopped
            });
            List<string> events = orderedServices.Events;
            LocalModuleServicePairController controller =
                new LocalModuleServicePairController(
                    orderedServices,
                    new FakeLocalModuleMsiReadiness(events));
            controller.StartDatabaseThenApi(layout);
            controller.StopApiThenDatabase(layout);
            AssertSequence(new[]
            {
                "start:yenisei1", "ready:Database",
                "start:regime1", "ready:Api",
                "stop:regime1", "stopped:Api",
                "stop:yenisei1", "stopped:Database"
            }, events, "LM service pair lifecycle order must be exact.");
        }

        private static void LocalModuleFirewallManagerOwnsOnlyExactApiRule()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                LocalModuleFirewallManagerOwnsOnlyExactApiRule(directory);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void LocalModuleFirewallManagerOwnsOnlyExactApiRule(
            string directory)
        {
            string installRoot = Path.Combine(directory, "Regime1");
            Directory.CreateDirectory(
                Path.Combine(installRoot, "erts-13.0.4", "bin"));
            LocalModuleInstalledLayout layout =
                LocalModuleInstalledLayout.Create(
                    installRoot,
                    1,
                    6995,
                    7984);
            AssertTrue(layout.ErtsDiscovered,
                "Каталог рантайма ЛМ должен находиться на диске.");
            AssertEqual(
                Path.Combine(installRoot, "erts-13.0.4", "bin"),
                layout.ErtsBinPath,
                "Правило сети должно указывать на найденный рантайм.");
            FakeWindowsFirewallApi api = new FakeWindowsFirewallApi();
            LocalModuleFirewallManager manager =
                new LocalModuleFirewallManager(api);
            string ownershipId = new string('d', 32);
            LocalModuleFirewallRule rule = manager.EnsureApiRule(
                layout,
                ownershipId,
                null);
            AssertEqual(1, api.AddCount,
                "The exact LM API rule must be created once.");
            AssertEqual(6995, api.Rule.LocalPort,
                "Only the LM API port may be opened.");
            AssertEqual("LocalSubnet", api.Rule.RemoteAddress,
                "The default remote scope must be LocalSubnet.");
            AssertEqual(
                Path.Combine(layout.ErtsBinPath, "erl.exe"),
                api.Rule.ProgramPath,
                "The rule must target the instance Erlang executable.");
            AssertTrue(api.Rule.Enabled && api.Rule.Inbound &&
                api.Rule.Allow && api.Rule.Tcp &&
                api.Rule.Domain && api.Rule.Private && !api.Rule.Public,
                "The rule must be inbound TCP Allow for Domain and Private only.");
            manager.EnsureApiRule(layout, ownershipId, "LocalSubnet");
            AssertEqual(1, api.AddCount,
                "An exact owned rule must be idempotent.");

            WindowsFirewallRuleRecord foreign = api.Rule.Clone();
            foreign.Public = true;
            api.Rule = foreign;
            AssertThrows<InvalidDataException>(delegate {
                manager.EnsureApiRule(layout, ownershipId, "LocalSubnet");
            }, "A foreign conflicting rule must not be adopted or overwritten.");
            AssertThrows<InvalidDataException>(delegate {
                manager.Remove(rule);
            }, "A foreign conflicting rule must not be deleted.");
            AssertEqual(0, api.RemoveCount,
                "Foreign firewall state must remain untouched.");

            api.Rule = WindowsFirewallRuleRecord.FromExpected(rule);
            manager.Remove(rule);
            AssertEqual(1, api.RemoveCount,
                "Only the exact owned firewall rule may be removed.");
            AssertTrue(api.Rule == null,
                "The exact owned firewall rule must disappear.");
            AssertThrows<ArgumentException>(delegate {
                manager.EnsureApiRule(layout, ownershipId, "Any");
            }, "An unbounded remote address must be rejected.");
            manager.EnsureApiRule(
                layout,
                ownershipId,
                "192.168.10.0/24");
            AssertEqual("192.168.10.0/24", api.Rule.RemoteAddress,
                "An explicit validated CIDR must be preserved.");

            LocalModuleMsiManifest manifest = LocalModuleMsiManifest.Create(
                "1234567890",
                1,
                6995,
                7984,
                "{10000000-0000-0000-0000-000000000001}",
                "{20000000-0000-0000-0000-000000000001}",
                "2.6.1",
                layout.InstallRoot,
                true,
                false,
                ownershipId);
            manifest.AttachFirewallRule(rule);
            LocalModuleMsiManifest.Validate(manifest);
            AssertEqual(rule.RuleName, manifest.FirewallRuleName,
                "The manifest must retain the exact firewall rule name.");
            AssertEqual(rule.ExpectedFieldHash, manifest.FirewallRuleHash,
                "The manifest must retain the exact expected-field hash.");
            api.Rule = WindowsFirewallRuleRecord.FromExpected(rule);
            manager.Remove(
                manifest.FirewallRuleName,
                manifest.FirewallRuleHash);
            AssertEqual(2, api.RemoveCount,
                "Manifest identity must authorize exact owned removal.");
        }

        private static void LocalModuleLayoutDiscoversErtsRuntime()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string upgraded = Path.Combine(directory, "Regime");
                Directory.CreateDirectory(
                    Path.Combine(upgraded, "erts-15.2.1", "bin"));
                LocalModuleInstalledLayout layout =
                    LocalModuleInstalledLayout.Create(upgraded, 0, 5995, 5984);
                AssertTrue(layout.ErtsDiscovered,
                    "Рантайм новой версии ЛМ должен опознаваться по каталогу.");
                AssertEqual(
                    Path.Combine(upgraded, "erts-15.2.1", "bin"),
                    layout.ErtsBinPath,
                    "Номер версии рантайма не должен быть зашит в приложение.");

                string missing = Path.Combine(directory, "Regime2");
                Directory.CreateDirectory(missing);
                LocalModuleInstalledLayout blind =
                    LocalModuleInstalledLayout.Create(missing, 2, 7995, 8984);
                AssertFalse(blind.ErtsDiscovered,
                    "Без каталога рантайма разметка должна это признавать.");
                AssertThrows<InvalidDataException>(delegate {
                    new LocalModuleFirewallManager(
                        new FakeWindowsFirewallApi()).EnsureApiRule(
                            blind,
                            new string('e', 32),
                            null);
                }, "Без найденного рантайма правило сети не выписывается.");

                string ambiguous = Path.Combine(directory, "Regime3");
                Directory.CreateDirectory(
                    Path.Combine(ambiguous, "erts-13.0.4", "bin"));
                Directory.CreateDirectory(
                    Path.Combine(ambiguous, "erts-15.2.1", "bin"));
                AssertThrows<InvalidDataException>(delegate {
                    LocalModuleInstalledLayout.Create(ambiguous, 3, 8995, 9984);
                }, "Два рантайма в каталоге ЛМ должны останавливать работу.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void MsiLocalModuleEnsureIsIdempotentAndRecoversCleanup()
        {
            FakeLocalModuleMsiLifecyclePlatform platform =
                new FakeLocalModuleMsiLifecyclePlatform();
            FakeLocalModuleMsiRepository repository =
                new FakeLocalModuleMsiRepository();
            FakeLocalModuleMsiJournalStore journals =
                new FakeLocalModuleMsiJournalStore();
            LocalModuleMsiProvisioningContext context =
                CreateMsiContext(platform, repository, journals);
            LocalModuleMsiProvisioner provisioner =
                new LocalModuleMsiProvisioner();
            LocalModuleMsiProvisioningItemRequest clone = MsiRequest(
                "1234567890", 1, 6995, 7984, null);

            LocalModuleMsiProvisioningItemResult created =
                provisioner.Ensure(clone, context);
            AssertEqual(LmServiceProvisioningStatus.Succeeded, created.Status,
                "A new clone must be installed and started.");
            AssertSequence(new[]
            {
                "install:1234567890", "firewall:1234567890",
                "start:1234567890"
            }, platform.Events, "New clone ensure order must be exact.");
            int mutationCount = platform.Events.Count;
            clone.ExpectedManifestSha256 = created.ManifestSha256;
            LocalModuleMsiProvisioningItemResult repeated =
                provisioner.Ensure(clone, context);
            AssertEqual(LmServiceProvisioningStatus.Succeeded, repeated.Status,
                "A matching ready clone must be a successful no-op.");
            AssertEqual(mutationCount, platform.Events.Count,
                "A second ensure must perform no mutation.");

            platform.State(clone.Inn).Ready = false;
            platform.State(clone.Inn).Running = false;
            provisioner.Ensure(clone, context);
            AssertEqual("start:1234567890",
                platform.Events[platform.Events.Count - 1],
                "A matching stopped clone must only be started.");

            LocalModuleMsiProvisioningItemRequest foreign = MsiRequest(
                "123456789012", 2, 7995, 8984, null);
            platform.State(foreign.Inn).ProductPresent = true;
            platform.State(foreign.Inn).ProductMatches = false;
            int beforeForeign = platform.Events.Count;
            LocalModuleMsiProvisioningItemResult rejected =
                provisioner.Ensure(foreign, context);
            AssertEqual(LmServiceProvisioningStatus.Failed, rejected.Status,
                "A foreign product conflict must fail only this INN.");
            AssertEqual(beforeForeign, platform.Events.Count,
                "A foreign conflict must not be mutated.");

            LocalModuleMsiProvisioningItemRequest failed = MsiRequest(
                "7707083893", 3, 8995, 9984, null);
            platform.FailInstallAfterMutationForInn = failed.Inn;
            platform.FailUninstallForInn = failed.Inn;
            LocalModuleMsiProvisioningItemResult pending =
                provisioner.Ensure(failed, context);
            AssertEqual(LmServiceProvisioningStatus.CleanupPending,
                pending.Status,
                "A failed install with failed rollback must remain cleanup-pending.");
            AssertTrue(journals.Contains(failed.Inn),
                "Cleanup-pending state must survive for retry.");
            platform.FailInstallAfterMutationForInn = null;
            platform.FailUninstallForInn = null;
            LocalModuleMsiProvisioningItemResult recovered =
                provisioner.Ensure(failed, context);
            AssertEqual(LmServiceProvisioningStatus.Succeeded, recovered.Status,
                "The next ensure must clean the partial install and retry.");
            AssertFalse(journals.Contains(failed.Inn),
                "A successful retry must clear its lifecycle journal.");

            LocalModuleMsiProvisioningItemRequest cancelled = MsiRequest(
                "7707083894", 5, 10995, 11984, null);
            platform.FailInstallWithPartialServicesForInn = cancelled.Inn;
            int beforeCancelled = platform.Events.Count;
            LocalModuleMsiProvisioningItemResult cancelledResult =
                provisioner.Ensure(cancelled, context);
            AssertEqual(LmServiceProvisioningStatus.Failed,
                cancelledResult.Status,
                "A cancelled MSI install must report its original failure.");
            AssertTrue(platform.Events.GetRange(
                    beforeCancelled,
                    platform.Events.Count - beforeCancelled).Contains(
                        "cleanup-residue:" + cancelled.Inn),
                "Rollback must remove partial services even when Windows " +
                "Installer did not register the product.");
            AssertFalse(platform.State(cancelled.Inn).ServicesPresent,
                "Cancelled MSI install residue must be removed immediately.");
            platform.FailInstallWithPartialServicesForInn = null;

            LocalModuleMsiProvisioningItemRequest interrupted = MsiRequest(
                "500100732259", 4, 9995, 10984, null);
            string interruptedNonce = new string('f', 32);
            journals.Write(LocalModuleMsiLifecycleJournal.Create(
                "20000000000000000000000000000002",
                interrupted,
                interruptedNonce,
                LocalModuleMsiLifecycleStage.CleanupPending,
                "SimulatedCrash",
                true));
            LocalModuleMsiProvisioningContext resumedContext =
                new LocalModuleMsiProvisioningContext(
                    "30000000000000000000000000000003",
                    platform,
                    repository,
                    journals,
                    delegate { return new string('a', 32); });
            LocalModuleMsiProvisioningItemResult resumed =
                provisioner.Ensure(interrupted, resumedContext);
            AssertEqual(LmServiceProvisioningStatus.Succeeded, resumed.Status,
                "A new operation must recover a manifest-free cleanup journal " +
                "in one ensure call.");
            AssertFalse(journals.Contains(interrupted.Inn),
                "Cross-operation recovery must clear the old journal.");

            LocalModuleMsiProvisioningItemRequest existingBase = MsiRequest(
                "525700335451", 0, 5995, 5984, null);
            FakeLocalModuleMsiState existingBaseState =
                platform.State(existingBase.Inn);
            existingBaseState.ProductPresent = true;
            existingBaseState.ProductMatches = true;
            existingBaseState.ServicesPresent = true;
            existingBaseState.Running = true;
            existingBaseState.Ready = false;
            platform.FailStartForInn = existingBase.Inn;
            platform.Events.Clear();
            LocalModuleMsiProvisioningItemResult failedAdoption =
                provisioner.Ensure(existingBase, context);
            AssertEqual(LmServiceProvisioningStatus.Failed,
                failedAdoption.Status,
                "A failed base adoption must roll back only application state.");
            AssertFalse(platform.Events.Contains("stop:" + existingBase.Inn),
                "Rollback must never stop a pre-existing vendor base.");
            AssertTrue(existingBaseState.Running,
                "A running pre-existing vendor base must survive rollback.");
            platform.FailStartForInn = null;
        }

        private static void MsiLocalModuleRemovalPreservesBaseAndCompensatesEpmd()
        {
            FakeLocalModuleMsiLifecyclePlatform platform =
                new FakeLocalModuleMsiLifecyclePlatform();
            FakeLocalModuleMsiRepository repository =
                new FakeLocalModuleMsiRepository();
            FakeLocalModuleMsiJournalStore journals =
                new FakeLocalModuleMsiJournalStore();
            LocalModuleMsiProvisioningContext context =
                CreateMsiContext(platform, repository, journals);
            LocalModuleMsiProvisioner provisioner =
                new LocalModuleMsiProvisioner();
            LocalModuleMsiRemovalWorkflow removal =
                new LocalModuleMsiRemovalWorkflow();
            LocalModuleMsiProvisioningItemRequest baseRequest = MsiRequest(
                "7707083893", 0, 5995, 5984, null);
            LocalModuleMsiProvisioningItemRequest cloneRequest = MsiRequest(
                "1234567890", 1, 6995, 7984, null);
            LocalModuleMsiProvisioningItemResult baseCreated =
                provisioner.Ensure(baseRequest, context);
            LocalModuleMsiProvisioningItemResult cloneCreated =
                provisioner.Ensure(cloneRequest, context);
            baseRequest.ExpectedManifestSha256 = baseCreated.ManifestSha256;
            cloneRequest.ExpectedManifestSha256 = cloneCreated.ManifestSha256;
            platform.Events.Clear();

            LocalModuleMsiProvisioningItemResult removedBase =
                removal.Remove(baseRequest, context);
            AssertEqual(
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                removedBase.Status,
                "An application-owned base must be removable.");
            AssertSequence(new[]
            {
                "stop:1234567890", "stop:7707083893",
                "remove-firewall:7707083893", "uninstall:7707083893",
                "epmd", "start:1234567890"
            }, platform.Events,
                "Base removal must compensate every running clone around EPMD.");

            FakeLocalModuleMsiLifecyclePlatform stopFailurePlatform =
                new FakeLocalModuleMsiLifecyclePlatform();
            FakeLocalModuleMsiRepository stopFailureRepository =
                new FakeLocalModuleMsiRepository();
            FakeLocalModuleMsiJournalStore stopFailureJournals =
                new FakeLocalModuleMsiJournalStore();
            LocalModuleMsiProvisioningContext stopFailureContext =
                CreateMsiContext(stopFailurePlatform, stopFailureRepository,
                    stopFailureJournals);
            LocalModuleMsiProvisioningItemRequest stopFailureBase = MsiRequest(
                "7707083893", 0, 5995, 5984, null);
            LocalModuleMsiProvisioningItemRequest firstRunningClone = MsiRequest(
                "1234567890", 1, 6995, 7984, null);
            LocalModuleMsiProvisioningItemRequest secondRunningClone = MsiRequest(
                "123456789012", 2, 7995, 8984, null);
            stopFailureBase.ExpectedManifestSha256 = provisioner.Ensure(
                stopFailureBase, stopFailureContext).ManifestSha256;
            provisioner.Ensure(firstRunningClone, stopFailureContext);
            provisioner.Ensure(secondRunningClone, stopFailureContext);
            stopFailurePlatform.FailStopForInn = secondRunningClone.Inn;
            stopFailurePlatform.Events.Clear();
            removal.Remove(stopFailureBase, stopFailureContext);
            AssertTrue(stopFailurePlatform.State(firstRunningClone.Inn).Running,
                "A clone stopped before another clone failure must be restarted.");
            AssertTrue(stopFailurePlatform.State(stopFailureBase.Inn).ProductPresent,
                "The base product must remain untouched when clone stop fails.");
            AssertFalse(stopFailurePlatform.Events.Contains(
                    "uninstall:" + stopFailureBase.Inn),
                "Base uninstall must not begin until every running clone stops.");

            LocalModuleMsiProvisioningItemRequest preserved = MsiRequest(
                "123456789012", 0, 5995, 5984, null);
            platform.State(preserved.Inn).ProductPresent = true;
            platform.State(preserved.Inn).ProductMatches = true;
            LocalModuleMsiProvisioningItemResult adopted =
                provisioner.Ensure(preserved, context);
            preserved.ExpectedManifestSha256 = adopted.ManifestSha256;
            platform.Events.Clear();
            LocalModuleMsiProvisioningItemResult preservedResult =
                removal.Remove(preserved, context);
            AssertEqual(
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                preservedResult.Status,
                "Remove-created must forget but preserve a pre-existing base.");
            AssertFalse(platform.Events.Contains("uninstall:" + preserved.Inn),
                "A pre-existing base must never be uninstalled.");
            AssertTrue(platform.State(preserved.Inn).ProductPresent,
                "A pre-existing base product must survive removal.");

            platform.State(preserved.Inn).Ready = false;
            LocalModuleMsiProvisioningItemResult readopted =
                provisioner.Ensure(preserved, context);
            preserved.ExpectedManifestSha256 = readopted.ManifestSha256;
            platform.Events.Clear();
            LocalModuleMsiProvisioningItemResult restartedBase =
                provisioner.Restart(preserved, context);
            AssertEqual(LmServiceProvisioningStatus.Succeeded,
                restartedBase.Status,
                "A confirmed pre-existing base must support an explicit restart.");
            AssertSequence(new[]
            {
                "stop:" + preserved.Inn,
                "start:" + preserved.Inn
            }, platform.Events,
                "Explicit base restart must restart the confirmed service pair.");

            cloneRequest.ExpectedManifestSha256 =
                repository.Read(cloneRequest.Inn).ManifestSha256;
            platform.Events.Clear();
            removal.Remove(cloneRequest, context);
            AssertFalse(platform.Events.Contains("stop:" + preserved.Inn),
                "Removing one clone must not stop the base.");

            LocalModuleMsiProvisioningItemRequest interruptedRemoval = MsiRequest(
                "500100732259", 4, 9995, 10984, null);
            LocalModuleMsiProvisioningItemResult removalCreated =
                provisioner.Ensure(interruptedRemoval, context);
            interruptedRemoval.ExpectedManifestSha256 =
                removalCreated.ManifestSha256;
            repository.FailDeleteOnceForInn = interruptedRemoval.Inn;
            platform.Events.Clear();
            LocalModuleMsiProvisioningItemResult firstRemoval =
                removal.Remove(interruptedRemoval, context);
            AssertEqual(LmServiceProvisioningStatus.CleanupPending,
                firstRemoval.Status,
                "A crash boundary after MSI uninstall must remain retryable.");
            LocalModuleMsiProvisioningItemResult retriedRemoval =
                removal.Remove(interruptedRemoval, context);
            AssertEqual(
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                retriedRemoval.Status,
                "Removal retry must finish after an already completed uninstall.");
            AssertEqual(1, platform.Events.FindAll(delegate(string value) {
                return string.Equals(value,
                    "uninstall:" + interruptedRemoval.Inn,
                    StringComparison.Ordinal);
            }).Count,
                "Removal retry must not invoke MSI uninstall twice.");

            FakeLocalModuleMsiLifecyclePlatform allPlatform =
                new FakeLocalModuleMsiLifecyclePlatform();
            FakeLocalModuleMsiRepository allRepository =
                new FakeLocalModuleMsiRepository();
            FakeLocalModuleMsiJournalStore allJournals =
                new FakeLocalModuleMsiJournalStore();
            LocalModuleMsiProvisioningContext allContext = CreateMsiContext(
                allPlatform, allRepository, allJournals);
            provisioner.Ensure(MsiRequest(
                "7707083893", 0, 5995, 5984, null), allContext);
            provisioner.Ensure(MsiRequest(
                "1234567890", 1, 6995, 7984, null), allContext);
            provisioner.Ensure(MsiRequest(
                "123456789012", 2, 7995, 8984, null), allContext);
            allPlatform.Events.Clear();
            IList<LocalModuleMsiProvisioningItemResult> allResults =
                removal.RemoveAll(allContext);
            AssertEqual(3, allResults.Count,
                "Remove-all must return one result per owned product.");
            AssertTrue(
                allPlatform.Events.IndexOf("uninstall:123456789012") <
                allPlatform.Events.IndexOf("uninstall:1234567890") &&
                allPlatform.Events.IndexOf("uninstall:1234567890") <
                allPlatform.Events.IndexOf("uninstall:7707083893"),
                "Remove-all must uninstall clones in reverse order before base.");

            FakeLocalModuleMsiLifecyclePlatform selectivePlatform =
                new FakeLocalModuleMsiLifecyclePlatform();
            FakeLocalModuleMsiRepository selectiveRepository =
                new FakeLocalModuleMsiRepository();
            LocalModuleMsiProvisioningContext selectiveContext =
                CreateMsiContext(
                    selectivePlatform,
                    selectiveRepository,
                    new FakeLocalModuleMsiJournalStore());
            LocalModuleMsiProvisioningItemRequest selectiveBase = MsiRequest(
                "7707083893", 0, 5995, 5984, null);
            LocalModuleMsiProvisioningItemRequest selectedClone = MsiRequest(
                "1234567890", 1, 6995, 7984, null);
            LocalModuleMsiProvisioningItemRequest unlistedClone = MsiRequest(
                "123456789012", 2, 7995, 8984, null);
            selectiveBase.ExpectedManifestSha256 = provisioner.Ensure(
                selectiveBase, selectiveContext).ManifestSha256;
            selectedClone.ExpectedManifestSha256 = provisioner.Ensure(
                selectedClone, selectiveContext).ManifestSha256;
            unlistedClone.ExpectedManifestSha256 = provisioner.Ensure(
                unlistedClone, selectiveContext).ManifestSha256;
            IList<LocalModuleMsiProvisioningItemResult> selectiveResults =
                removal.RemoveAll(
                    new[] { selectiveBase, selectedClone },
                    selectiveContext);
            AssertEqual(selectiveBase.Inn, selectiveResults[0].Inn,
                "Remove-all results must retain the confirmed UI order.");
            AssertEqual(selectedClone.Inn, selectiveResults[1].Inn,
                "Remove-all results must retain the confirmed UI order.");
            AssertTrue(selectiveRepository.Read(unlistedClone.Inn) != null,
                "Remove-all must not adopt a manifest absent from the confirmed request.");
            AssertTrue(selectivePlatform.State(unlistedClone.Inn).ProductPresent,
                "An unlisted installed clone must remain untouched.");
        }

        private static void LocalModuleStartModeControllerAdjustsAndRestoresExactPair()
        {
            LocalModuleInstalledLayout layout =
                LocalModuleInstalledLayout.Create(
                    @"D:\Program Files\Regime",
                    0,
                    5995,
                    5984);
            FakeWindowsServiceApi services = new FakeWindowsServiceApi();
            services.SetRecord(new WindowsServiceRecord
            {
                ServiceName = layout.ApiServiceName,
                ImagePath = layout.ServiceImagePath,
                StartMode = WindowsServiceStartMode.DemandStart,
                State = WindowsServiceState.Running
            });
            services.SetRecord(new WindowsServiceRecord
            {
                ServiceName = layout.DatabaseServiceName,
                ImagePath = layout.ServiceImagePath,
                StartMode = WindowsServiceStartMode.DemandStart,
                State = WindowsServiceState.Running
            });
            LocalModuleServiceStartModeController controller =
                new LocalModuleServiceStartModeController(services);
            AssertFalse(LocalModuleServiceStartModeController.IsAutomatic(
                    services.Query(layout.ApiServiceName),
                    services.Query(layout.DatabaseServiceName)),
                "A manual vendor pair must not report automatic start.");

            LocalModuleStartModeAdjustment adjustment =
                controller.EnsureAutomatic(layout);
            AssertTrue(adjustment.Adjusted,
                "Switching a manual pair must report an adjustment.");
            AssertEqual(WindowsServiceStartMode.DemandStart,
                adjustment.PreviousApiStartMode,
                "The previous API start mode must be reported.");
            AssertEqual(WindowsServiceStartMode.DemandStart,
                adjustment.PreviousDatabaseStartMode,
                "The previous database start mode must be reported.");
            AssertSequence(new[]
            {
                "startmode:" + layout.DatabaseServiceName + ":AutoStart",
                "startmode:" + layout.ApiServiceName + ":AutoStart"
            }, services.Events,
                "Only the two start types of the exact pair may change.");
            AssertTrue(LocalModuleServiceStartModeController.IsAutomatic(
                    services.Query(layout.ApiServiceName),
                    services.Query(layout.DatabaseServiceName)),
                "Both services must be on automatic start afterwards.");

            int events = services.Events.Count;
            LocalModuleStartModeAdjustment repeated =
                controller.EnsureAutomatic(layout);
            AssertFalse(repeated.Adjusted,
                "An automatic pair must report no adjustment.");
            AssertEqual(events, services.Events.Count,
                "An automatic pair must not be touched again.");

            controller.Restore(
                layout,
                WindowsServiceStartMode.DemandStart,
                WindowsServiceStartMode.DemandStart);
            AssertEqual(WindowsServiceStartMode.DemandStart,
                services.Query(layout.ApiServiceName).StartMode,
                "Restore must hand the API service its previous start mode back.");
            AssertEqual(WindowsServiceStartMode.DemandStart,
                services.Query(layout.DatabaseServiceName).StartMode,
                "Restore must hand the database service its previous start mode back.");

            services.SetRecord(new WindowsServiceRecord
            {
                ServiceName = layout.ApiServiceName,
                ImagePath = @"D:\Program Files\Regime\foreign.exe",
                StartMode = WindowsServiceStartMode.DemandStart
            });
            AssertThrows<InvalidDataException>(delegate {
                controller.EnsureAutomatic(layout);
            }, "A foreign service under the vendor name must be refused.");
            controller.Restore(
                layout,
                WindowsServiceStartMode.AutoStart,
                WindowsServiceStartMode.AutoStart);
            AssertEqual(WindowsServiceStartMode.DemandStart,
                services.Query(layout.ApiServiceName).StartMode,
                "Restore must never touch a foreign service.");

            services.SetRecord(new WindowsServiceRecord
            {
                ServiceName = layout.ApiServiceName,
                ImagePath = layout.ServiceImagePath,
                StartMode = WindowsServiceStartMode.Disabled
            });
            events = services.Events.Count;
            AssertThrows<InvalidOperationException>(delegate {
                controller.EnsureAutomatic(layout);
            }, "A disabled vendor service must be left to the administrator.");
            AssertEqual(events, services.Events.Count,
                "A disabled pair must not be modified.");

            services.Delete(layout.ApiServiceName);
            services.Delete(layout.DatabaseServiceName);
            controller.Restore(
                layout,
                WindowsServiceStartMode.DemandStart,
                WindowsServiceStartMode.DemandStart);
            AssertEqual(events, services.Events.Count,
                "Restore must ignore services that are already gone.");
        }

        private static void MsiLocalModuleEnsureEnablesAutomaticStartForAdoptedBase()
        {
            FakeLocalModuleMsiLifecyclePlatform platform =
                new FakeLocalModuleMsiLifecyclePlatform();
            FakeLocalModuleMsiRepository repository =
                new FakeLocalModuleMsiRepository();
            FakeLocalModuleMsiJournalStore journals =
                new FakeLocalModuleMsiJournalStore();
            LocalModuleMsiProvisioningContext context =
                CreateMsiContext(platform, repository, journals);
            LocalModuleMsiProvisioner provisioner =
                new LocalModuleMsiProvisioner();
            LocalModuleMsiRemovalWorkflow removal =
                new LocalModuleMsiRemovalWorkflow();
            LocalModuleMsiProvisioningItemRequest vendorBase = MsiRequest(
                "7707083893", 0, 5995, 5984, null);
            FakeLocalModuleMsiState state = platform.State(vendorBase.Inn);
            state.ProductPresent = true;
            state.ProductMatches = true;
            state.ServicesPresent = true;
            state.Running = true;
            state.Ready = true;
            state.AutomaticStart = false;

            LocalModuleMsiProvisioningItemResult adopted =
                provisioner.Ensure(vendorBase, context);
            AssertEqual(LmServiceProvisioningStatus.Succeeded, adopted.Status,
                "A running vendor base on manual start must be adopted.");
            AssertSequence(new[]
            {
                "firewall:7707083893", "startmode:7707083893",
                "start:7707083893"
            }, platform.Events,
                "Automatic start must be enabled after the firewall and before start.");
            AssertTrue(state.AutomaticStart,
                "Adoption must switch the vendor pair to automatic start.");
            LocalModuleMsiManifest manifest = repository.Read(vendorBase.Inn);
            AssertTrue(manifest.StartModeAdjusted,
                "The manifest must journal the start-mode adjustment.");
            AssertEqual((int)WindowsServiceStartMode.DemandStart,
                manifest.PreviousApiStartMode,
                "The previous API start mode must be recorded.");
            AssertEqual((int)WindowsServiceStartMode.DemandStart,
                manifest.PreviousDatabaseStartMode,
                "The previous database start mode must be recorded.");
            AssertEqual(adopted.ManifestSha256, manifest.ManifestSha256,
                "The reported hash must cover the start-mode record.");

            vendorBase.ExpectedManifestSha256 = adopted.ManifestSha256;
            int mutations = platform.Events.Count;
            LocalModuleMsiProvisioningItemResult repeated =
                provisioner.Ensure(vendorBase, context);
            AssertEqual(LmServiceProvisioningStatus.Succeeded, repeated.Status,
                "An adopted base on automatic start must be a no-op.");
            AssertEqual(mutations, platform.Events.Count,
                "A ready base must not be mutated again.");

            state.AutomaticStart = false;
            LocalModuleMsiProvisioningItemResult repaired =
                provisioner.Ensure(vendorBase, context);
            AssertEqual(LmServiceProvisioningStatus.Succeeded, repaired.Status,
                "A base reverted to manual start must be repaired.");
            AssertTrue(platform.Events.LastIndexOf("startmode:7707083893") >= mutations,
                "Repair must re-enable automatic start.");
            AssertTrue(state.AutomaticStart,
                "Repair must restore automatic start.");
            manifest = repository.Read(vendorBase.Inn);
            AssertEqual(adopted.ManifestSha256, manifest.ManifestSha256,
                "Repair must keep the original start-mode record.");

            platform.Events.Clear();
            LocalModuleMsiProvisioningItemResult removed =
                removal.Remove(vendorBase, context);
            AssertEqual(
                LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                removed.Status,
                "Removing an adopted base must succeed without uninstalling it.");
            AssertSequence(new[]
            {
                "remove-firewall:7707083893", "restore-startmode:7707083893"
            }, platform.Events,
                "Removal must hand the previous start mode back after the firewall rule.");
            AssertFalse(state.AutomaticStart,
                "Removal must return the vendor pair to its previous start mode.");
            AssertTrue(state.ProductPresent && state.Running,
                "Removal must leave the vendor base installed and running.");
            AssertContains(removed.Message, "режим запуска");
            AssertTrue(repository.Read(vendorBase.Inn) == null,
                "Removal must delete the base assignment manifest.");

            LocalModuleMsiProvisioningItemRequest clone = MsiRequest(
                "1234567890", 1, 6995, 7984, null);
            platform.Events.Clear();
            provisioner.Ensure(clone, context);
            AssertFalse(platform.Events.Contains("startmode:1234567890"),
                "A clone installed with AUTOSERVICE must not need a start-mode change.");
            AssertFalse(repository.Read(clone.Inn).StartModeAdjusted,
                "A clone manifest must not journal a start-mode adjustment.");

            FakeLocalModuleMsiLifecyclePlatform failing =
                new FakeLocalModuleMsiLifecyclePlatform();
            FakeLocalModuleMsiRepository failingRepository =
                new FakeLocalModuleMsiRepository();
            LocalModuleMsiProvisioningContext failingContext = CreateMsiContext(
                failing, failingRepository, new FakeLocalModuleMsiJournalStore());
            LocalModuleMsiProvisioningItemRequest failingBase = MsiRequest(
                "525700335451", 0, 5995, 5984, null);
            FakeLocalModuleMsiState failingState = failing.State(failingBase.Inn);
            failingState.ProductPresent = true;
            failingState.ProductMatches = true;
            failingState.ServicesPresent = true;
            failingState.Running = true;
            failing.FailStartForInn = failingBase.Inn;
            LocalModuleMsiProvisioningItemResult failed =
                provisioner.Ensure(failingBase, failingContext);
            AssertEqual(LmServiceProvisioningStatus.Failed, failed.Status,
                "A failed base start must fail the adoption.");
            AssertTrue(failing.Events.IndexOf("startmode:525700335451") >= 0 &&
                failing.Events.IndexOf("startmode:525700335451") <
                    failing.Events.IndexOf("restore-startmode:525700335451"),
                "Rollback must restore the previous start mode of the vendor base.");
            AssertFalse(failingState.AutomaticStart,
                "Rollback must leave the vendor base on its previous start mode.");
            AssertTrue(failingRepository.Read(failingBase.Inn) == null,
                "Rollback must delete the base assignment manifest.");

            failing.Events.Clear();
            failing.FailStartForInn = null;
            failing.FailStartModeForInn = failingBase.Inn;
            LocalModuleMsiProvisioningItemResult refused =
                provisioner.Ensure(failingBase, failingContext);
            AssertEqual(LmServiceProvisioningStatus.Failed, refused.Status,
                "A refused start-mode change must fail the adoption.");
            AssertFalse(failing.Events.Contains("restore-startmode:525700335451"),
                "Nothing must be restored when no adjustment was recorded.");
            AssertFalse(failing.Events.Contains("start:525700335451"),
                "Services must not be started after a refused start-mode change.");
            AssertTrue(failingRepository.Read(failingBase.Inn) == null,
                "A refused adoption must leave no base assignment manifest.");
        }

        private static LocalModuleMsiProvisioningContext CreateMsiContext(
            FakeLocalModuleMsiLifecyclePlatform platform,
            FakeLocalModuleMsiRepository repository,
            FakeLocalModuleMsiJournalStore journals)
        {
            return new LocalModuleMsiProvisioningContext(
                "10000000000000000000000000000001",
                platform,
                repository,
                journals,
                delegate { return new string('e', 32); });
        }

        private static void MsiLocalModuleProtocolV3HashesAndValidatesEveryItem()
        {
            LmServiceProvisioningBatchRequest request =
                CreateMsiProtocolRequest();
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            ValidationResult valid = ProvisioningRequestValidator.Validate(request);
            AssertTrue(valid.IsValid, valid.JoinMessages());

            LocalModuleMsiProvisioningItemRequest item =
                request.LocalModuleMsiItems[1];
            AssertMsiHashChanges(request, delegate { item.Inn = "500100732259"; },
                delegate { item.Inn = "123456789012"; }, "INN");
            AssertMsiHashChanges(request, delegate { item.CloneOrdinal = 2; },
                delegate { item.CloneOrdinal = 1; }, "ordinal");
            AssertMsiHashChanges(request, delegate { item.ApiPort = 7995; },
                delegate { item.ApiPort = 6995; }, "API port");
            AssertMsiHashChanges(request, delegate { item.DatabasePort = 8984; },
                delegate { item.DatabasePort = 7984; }, "database port");
            AssertMsiHashChanges(request, delegate { item.InstallVolumeRoot = @"C:\"; },
                delegate { item.InstallVolumeRoot = @"D:\"; }, "volume root");
            AssertMsiHashChanges(request,
                delegate { item.RemoteAddress = "192.168.10.0/24"; },
                delegate { item.RemoteAddress = "LocalSubnet"; }, "remote address");
            AssertMsiHashChanges(request,
                delegate { item.ExpectedManifestSha256 = new string('d', 64); },
                delegate { item.ExpectedManifestSha256 = null; }, "manifest fingerprint");

            string[] exactFields =
            {
                "Inn", "CloneOrdinal", "ApiPort", "DatabasePort",
                "InstallVolumeRoot", "RemoteAddress",
                "ExpectedManifestSha256"
            };
            System.Reflection.PropertyInfo[] properties =
                typeof(LocalModuleMsiProvisioningItemRequest).GetProperties();
            AssertEqual(exactFields.Length, properties.Length,
                "The v3 item must not grow an unreviewed path, command or secret field.");
            for (int index = 0; index < exactFields.Length; index++)
                AssertTrue(Array.Exists(properties, delegate(
                    System.Reflection.PropertyInfo property)
                {
                    return string.Equals(property.Name, exactFields[index],
                        StringComparison.Ordinal);
                }), "Expected reviewed v3 field " + exactFields[index] + ".");

            request.LocalModuleMsiItems[1].Inn =
                request.LocalModuleMsiItems[0].Inn;
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            AssertFalse(ProvisioningRequestValidator.Validate(request).IsValid,
                "Duplicate INN must be rejected before helper mutation.");
            request = CreateMsiProtocolRequest();
            request.LocalModuleMsiItems[1].CloneOrdinal = 0;
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            AssertFalse(ProvisioningRequestValidator.Validate(request).IsValid,
                "Duplicate ordinal must be rejected before helper mutation.");
            request = CreateMsiProtocolRequest();
            request.LocalModuleMsiItems[1].ApiPort =
                request.LocalModuleMsiItems[0].DatabasePort;
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            AssertFalse(ProvisioningRequestValidator.Validate(request).IsValid,
                "A repeated API/database port must be rejected.");
            request = CreateMsiProtocolRequest();
            request.LocalModuleMsiItems[1].InstallVolumeRoot =
                @"D:\Program Files\Regime2";
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            AssertFalse(ProvisioningRequestValidator.Validate(request).IsValid,
                "A caller-supplied full install path must be rejected.");

            request = CreateMsiProtocolRequest();
            request.SchemaVersion = 1;
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            AssertContains(
                ProvisioningRequestValidator.Validate(request).JoinMessages(),
                "Версия схемы");
            request.SchemaVersion = 2;
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            AssertContains(
                ProvisioningRequestValidator.Validate(request).JoinMessages(),
                "Версия схемы");
        }

        private static void AssertMsiHashChanges(
            LmServiceProvisioningBatchRequest request,
            Action mutate,
            Action restore,
            string field)
        {
            string before = CanonicalLmPlanHasher.Compute(request);
            mutate();
            string after = CanonicalLmPlanHasher.Compute(request);
            restore();
            AssertFalse(CanonicalLmPlanHasher.FixedTimeEqualsHex(before, after),
                "MSI local-module " + field + " must participate in the plan hash.");
        }

        private static void MsiLocalModuleSessionContinuesIndependentInnFailures()
        {
            LmServiceProvisioningBatchRequest request =
                CreateMsiProtocolRequest();
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            FakeLocalModuleMsiLifecyclePlatform platform =
                new FakeLocalModuleMsiLifecyclePlatform();
            platform.State(request.LocalModuleMsiItems[0].Inn).ProductPresent =
                true;
            platform.State(request.LocalModuleMsiItems[0].Inn).ProductMatches =
                false;
            LocalModuleMsiProvisioningContext context =
                new LocalModuleMsiProvisioningContext(
                    request.OperationId,
                    platform,
                    new FakeLocalModuleMsiRepository(),
                    new FakeLocalModuleMsiJournalStore(),
                    delegate { return new string('e', 32); });
            using (LocalModuleMsiProvisioningSession session =
                new LocalModuleMsiProvisioningSession(
                    request,
                    new LocalModuleMsiProvisioner(),
                    context,
                    new CallbackDisposable(delegate { })))
            {
                AssertEqual(LmServiceProvisioningStatus.Failed,
                    session.ExecuteItem(0).Status,
                    "A foreign first INN must fail only that item.");
                AssertEqual(LmServiceProvisioningStatus.Succeeded,
                    session.ExecuteItem(1).Status,
                    "A later independent INN must still be installed.");
                AssertEqual(2, session.Finish().LocalModuleMsiItems.Count,
                    "The v3 result must retain one result per INN.");
            }
        }

        private static LmServiceProvisioningBatchRequest
            CreateMsiProtocolRequest()
        {
            LocalModuleInstallerSelection installer =
                MsiTestPackageFactory.SampleSelection(
                    @"D:\regime-2.6.1-7.msi");
            LmServiceProvisioningBatchRequest request =
                new LmServiceProvisioningBatchRequest
                {
                    SchemaVersion = ProvisioningRequestValidator
                        .CurrentSchemaVersion,
                    Operation = LmServiceOperation.EnsureMsiLocalModules,
                    OperationId =
                        "40000000000000000000000000000004",
                    InitiatingSid = "S-1-5-21-1-2-3-1001"
                };
            request.LocalModuleInstallerSelection = installer;
            request.LocalModuleMsiItems.Add(MsiRequest(
                "1234567890", 0, 5995, 5984, null));
            request.LocalModuleMsiItems.Add(MsiRequest(
                "123456789012", 1, 6995, 7984, null));
            return request;
        }

        private static LocalModuleMsiProvisioningItemRequest MsiRequest(
            string inn,
            int ordinal,
            int apiPort,
            int databasePort,
            string fingerprint)
        {
            return new LocalModuleMsiProvisioningItemRequest
            {
                Inn = inn,
                CloneOrdinal = ordinal,
                ApiPort = apiPort,
                DatabasePort = databasePort,
                InstallVolumeRoot = @"D:\",
                RemoteAddress = "LocalSubnet",
                ExpectedManifestSha256 = fingerprint
            };
        }

        private static void WriteInstalledLocalModuleConfiguration(
            LocalModuleInstalledLayout layout,
            string cookie)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(layout.ApiLocalIniPath));
            Directory.CreateDirectory(Path.GetDirectoryName(layout.DatabaseLocalIniPath));
            Directory.CreateDirectory(Path.GetDirectoryName(layout.ErlIniPath));
            File.WriteAllText(layout.ApiLocalIniPath,
                "[api]\r\nip_address = 0.0.0.0\r\nport = " +
                layout.ApiPort.ToString() +
                "\r\nlogin = DMIYC_synthetic-login\r\n" +
                "password = DMIYC_synthetic-password\r\n" +
                "[local]\r\ndb_url = http://127.0.0.1:" +
                layout.DatabasePort.ToString() + "\r\n");
            File.WriteAllText(layout.DatabaseLocalIniPath,
                "[chttpd]\r\nbind_address = 127.0.0.1\r\nport = " +
                layout.DatabasePort.ToString() + "\r\n");
            File.WriteAllText(layout.ApiVmArgsPath,
                "-name " + layout.ApiNodeName +
                "\r\n-setcookie " + cookie + "\r\n");
            File.WriteAllText(layout.DatabaseVmArgsPath,
                "-name " + layout.DatabaseNodeName +
                "\r\n-setcookie " + cookie + "\r\n");
            File.WriteAllText(layout.ErlIniPath,
                "[erlang]\r\nBindir=" +
                layout.ErtsBinPath.Replace("\\", "\\\\") +
                "\r\nProgname=erl\r\nRootdir=" +
                layout.InstallRoot.Replace("\\", "\\\\") + "\\\\\r\n");
        }

        private static LocalModuleMsiDatabaseSnapshot CreateLocalModuleMsiSnapshot(
            LocalModuleMsiCapabilityProfile profile)
        {
            LocalModuleMsiDatabaseSnapshot result =
                new LocalModuleMsiDatabaseSnapshot
                {
                    FileRowCount = profile.FileRowCount,
                    MsiFileHashRowCount = profile.MsiFileHashRowCount
                };
            for (int index = 0; index < profile.Media.Count; index++)
            {
                MsiMediaSnapshot source = profile.Media[index];
                result.Media.Add(new MsiMediaSnapshot
                {
                    DiskId = source.DiskId,
                    LastSequence = source.LastSequence,
                    Cabinet = source.Cabinet
                });
            }
            for (int index = 0; index < profile.Rows.Count; index++)
            {
                MsiProfileRow source = profile.Rows[index];
                result.Rows.Add(new MsiProfileRow
                {
                    Table = source.Table,
                    Key = source.Key,
                    Columns = new List<string>(source.Columns),
                    Values = new List<string>(source.Values)
                });
            }
            return result;
        }

        private static int CountMsiProfileRows(
            LocalModuleMsiCapabilityProfile profile,
            string table)
        {
            int count = 0;
            for (int index = 0; index < profile.Rows.Count; index++)
            {
                if (string.Equals(
                        profile.Rows[index].Table,
                        table,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
        }

        private static void AssertMsiProfileRowMismatch(
            LocalModuleMsiCapabilityProfile profile,
            string table,
            string key)
        {
            AssertMsiProfileMismatch(profile, delegate(LocalModuleMsiDatabaseSnapshot value) {
                for (int index = 0; index < value.Rows.Count; index++)
                {
                    MsiProfileRow row = value.Rows[index];
                    if (string.Equals(row.Table, table, StringComparison.Ordinal) &&
                        string.Equals(row.Key, key, StringComparison.Ordinal))
                    {
                        row.Values[0] =
                            @"C:\Users\secret\ADMINPASSWORD=hunter2";
                        return;
                    }
                }
                throw new InvalidOperationException("Fixture row was not found.");
            }, table + ":" + key);
        }

        private static void AssertMsiProfileMismatch(
            LocalModuleMsiCapabilityProfile profile,
            Action<LocalModuleMsiDatabaseSnapshot> mutate,
            string expectedSafeKey)
        {
            LocalModuleMsiDatabaseSnapshot snapshot =
                CreateLocalModuleMsiSnapshot(profile);
            mutate(snapshot);
            IList<MsiProfileMismatch> mismatches = snapshot.Compare(profile);
            AssertTrue(mismatches.Count > 0,
                "A structural MSI mutation must be rejected.");
            string diagnostic = mismatches[0].ToString();
            AssertContains(diagnostic, expectedSafeKey);
            AssertFalse(
                diagnostic.IndexOf(@"C:\Users", StringComparison.OrdinalIgnoreCase) >= 0 ||
                diagnostic.IndexOf("hunter2", StringComparison.OrdinalIgnoreCase) >= 0 ||
                diagnostic.IndexOf("ADMINPASSWORD", StringComparison.OrdinalIgnoreCase) >= 0,
                "MSI mismatch diagnostics must not echo paths or secret values.");
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
                                SchemaVersion = request.SchemaVersion,
                                OperationId = request.OperationId,
                                Sequence = 2,
                                Kind = ManagedProvisioningSessionKind.ExecuteItem,
                                ItemIndex = 0,
                                Status = LmServiceProvisioningStatus.Pending
                            },
                            new ManagedProvisioningSessionMessage
                            {
                                SchemaVersion = request.SchemaVersion,
                                OperationId = request.OperationId,
                                Sequence = 4,
                                Kind = ManagedProvisioningSessionKind.ExecuteItem,
                                ItemIndex = 1,
                                Status = LmServiceProvisioningStatus.Cancelled,
                                Message = "ККТ не зарегистрирована в ЕСМ."
                            },
                            new ManagedProvisioningSessionMessage
                            {
                                SchemaVersion = request.SchemaVersion,
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
                    MsiTestPackageFactory.SampleSelection(string.Empty);
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
                        MsiTestPackageFactory.SampleSelection(string.Empty),
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
                        MsiTestPackageFactory.SampleSelection(string.Empty),
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
                        MsiTestPackageFactory.SampleSelection(string.Empty);
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

                OfficialControllerLocator locator = new OfficialControllerLocator(
                    profile,
                    trust,
                    safePaths,
                    new FakeInstalledControllerProductVerifier(true));
                VerifiedControllerBinaryResult result = locator.ResolveVerifiedBinary();

                AssertTrue(result.IsSuccess, result.ErrorMessage);
                AssertEqual(Path.GetFullPath(binaryPath), result.Binary.FullPath,
                    "Controller must resolve only under the fixed allowed root.");

                profile.ControllerRelativePath = @"..\outside\lmcontroller.exe";
                VerifiedControllerBinaryResult escaped =
                    new OfficialControllerLocator(
                        profile,
                        trust,
                        safePaths,
                        new FakeInstalledControllerProductVerifier(true)).ResolveVerifiedBinary();
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
                    new FakePathSafety(true),
                    new FakeInstalledControllerProductVerifier(true)).ResolveVerifiedBinary();

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

        private static void DirectControllerScmDefinitionUsesOnlyVerifiedVendorBinary()
        {
            ControllerCapabilityProfile profile = ControllerCapabilityProfile.Supported();
            VerifiedControllerBinary binary = new VerifiedControllerBinary
            {
                FullPath = @"C:\Program Files\ESP\LMController\bin\lmcontroller.exe",
                Version = "1.6.4.0",
                Sha256 = new string('a', 64),
                SignerThumbprint = ControllerSignerThumbprint,
                Machine = PeMachine.Amd64
            };
            DirectControllerServiceDefinitionFactory factory =
                new DirectControllerServiceDefinitionFactory(profile, binary);

            WindowsServiceDefinition definition = factory.Create(
                2,
                @"C:\ProgramData\KRS\MultiKKT\DirectControllers\Profiles\controller-2",
                "S-1-5-21-111-222-333-1001");

            definition.Validate();
            AssertEqual(WindowsServiceDefinitionKind.DirectController, definition.Kind,
                "The direct controller must use its own strict definition contract.");
            AssertEqual("esm-lm-controller-2", definition.ServiceName,
                "Ordinal 2 must map to the exact clone service name.");
            AssertEqual("\"C:\\Program Files\\ESP\\LMController\\bin\\lmcontroller.exe\"", definition.ImagePath,
                "SCM must start the verified vendor binary directly without helper arguments.");
            AssertEqual(WindowsServiceSidType.None, definition.ServiceSidType,
                "Vendor controller clones must copy the official SERVICE_SID_TYPE_NONE setting.");
            AssertEqual(WindowsServiceStartMode.AutoStart, definition.StartMode,
                "Direct clones must start automatically.");
            AssertEqual(WindowsServiceErrorControl.Ignore, definition.ErrorControl,
                "Direct clones must copy the official error-control setting.");
            AssertEqual(0, definition.Dependencies.Count,
                "Direct clones must not acquire application-owned dependencies.");
            AssertEqual(1, definition.EnvironmentVariables.Count,
                "The clone must receive exactly one isolated environment override.");
            AssertEqual(
                @"C:\ProgramData\KRS\MultiKKT\DirectControllers\Profiles\controller-2",
                definition.EnvironmentVariables["ProgramData"],
                "The vendor controller must receive only its derived ProgramData root.");
        }

        private static void DirectControllerScmDefinitionRejectsSupervisorRules()
        {
            ControllerCapabilityProfile profile = ControllerCapabilityProfile.Supported();
            DirectControllerServiceDefinitionFactory factory =
                new DirectControllerServiceDefinitionFactory(
                    profile,
                    new VerifiedControllerBinary
                    {
                        FullPath = @"C:\Program Files\ESP\LMController\bin\lmcontroller.exe",
                        Version = "1.6.4.0",
                        Sha256 = new string('a', 64),
                        SignerThumbprint = ControllerSignerThumbprint,
                        Machine = PeMachine.Amd64
                    });
            WindowsServiceDefinition definition = factory.Create(
                2,
                @"C:\ProgramData\KRS\MultiKKT\DirectControllers\Profiles\controller-2",
                null);

            definition.ServiceSidType = WindowsServiceSidType.Restricted;
            AssertThrows<InvalidOperationException>(delegate { definition.Validate(); },
                "A direct vendor service must reject the legacy supervisor SID policy.");
            definition.ServiceSidType = WindowsServiceSidType.None;
            definition.ImagePath += " --supervise foreign";
            AssertThrows<InvalidOperationException>(delegate { definition.Validate(); },
                "A direct vendor service must reject helper arguments in ImagePath.");
        }

        private static void DirectControllerManifestIsCredentialFreeAndHashGuarded()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                DirectControllerManifestStore store = new DirectControllerManifestStore(
                    root,
                    new FakePathSafety(true),
                    "S-1-5-21-111-222-333-1001");
                DirectControllerManifest manifest = DirectControllerManifest.Create(
                    "00105700000001",
                    "7701234567",
                    2,
                    7595,
                    "1.6.4.0",
                    new string('a', 64),
                    Path.Combine(root, "Profiles", "controller-2"),
                    Guid.NewGuid().ToString("N"),
                    DirectControllerLifecycleState.Ready);

                store.Write(manifest);
                string fingerprint = store.ComputeFingerprint(manifest);
                DirectControllerManifest observed = store.Read("00105700000001");
                AssertEqual(fingerprint, store.ComputeFingerprint(observed),
                    "A persisted direct-controller identity must retain its removal fingerprint.");
                observed.TargetLocalModulePort = 7596;
                AssertFalse(string.Equals(
                        fingerprint,
                        store.ComputeFingerprint(observed),
                        StringComparison.OrdinalIgnoreCase),
                    "The explicit target LM port must be covered by the removal fingerprint.");

                string allJson = string.Join(
                    "\n",
                    Array.ConvertAll(
                        Directory.GetFiles(root, "*.json", SearchOption.AllDirectories),
                        File.ReadAllText));
                AssertFalse(allJson.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    allJson.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    allJson.IndexOf("newPassword", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Direct manifests and inventory must contain no controller credentials.");

                AssertThrows<InvalidDataException>(delegate {
                    store.Delete("00105700000001", new string('f', 64));
                }, "Removal must reject a stale or substituted displayed fingerprint.");
                AssertTrue(store.Read("00105700000001") != null,
                    "A rejected deletion must leave the owned manifest intact.");
                store.Delete("00105700000001", fingerprint);
                AssertTrue(store.Read("00105700000001") == null,
                    "A matching displayed fingerprint must remove owned metadata.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(root);
            }
        }

        private static void DirectControllerProvisionerContinuesIndependentKktFailures()
        {
            LmServiceProvisioningBatchRequest request =
                CreateDirectControllerRequest(3, LmServiceOperation.EnsureDirectControllers);
            FakeDirectControllerPlatform platform = new FakeDirectControllerPlatform
            {
                FailSerial = request.DirectControllers[1].KktSerial
            };
            IDirectControllerProvisioner provisioner =
                new DirectControllerProvisioner(platform);

            LmServiceProvisioningBatchResult result = provisioner.Execute(
                request,
                NeverCancelLmProvisioning.Instance);

            AssertEqual(3, platform.EnsuredSerials.Count,
                "A controller failure must not retain the old canary dependency between KKT rows.");
            AssertEqual(LmServiceProvisioningStatus.Succeeded, result.Items[0].Status,
                "The first independent controller must succeed.");
            AssertEqual(LmServiceProvisioningStatus.Failed, result.Items[1].Status,
                "Only the injected controller must fail.");
            AssertEqual(LmServiceProvisioningStatus.Succeeded, result.Items[2].Status,
                "A later independent controller must still be attempted.");
            AssertEqual(2, result.SchemaVersion,
                "Direct-controller results must retain protocol schema v2.");
        }

        private static void DirectControllerReadinessRequiresListenersOwnedByServicePid()
        {
            FakeWindowsServiceApi services = new FakeWindowsServiceApi();
            services.SetRecord(new WindowsServiceRecord
            {
                ServiceName = "esm-lm-controller-2",
                State = WindowsServiceState.Running,
                ProcessId = 4242
            });
            FakeDirectTcpListenerOwnerReader listeners = new FakeDirectTcpListenerOwnerReader();
            listeners.SetOwners(50064, 4242);
            listeners.SetOwners(5064, 4242);
            DirectControllerReadinessProbe probe = new DirectControllerReadinessProbe(
                services,
                listeners);

            AssertTrue(probe.Probe(2).IsReady,
                "Both direct-controller listeners owned by the SCM service PID are ready.");
            listeners.SetOwners(5064, 9999);
            AssertFalse(probe.Probe(2).IsReady,
                "A listener owned by any foreign PID must fail readiness.");
        }

        private static void DirectControllerProtocolV2AcceptsCanonicalBatch()
        {
            LmServiceProvisioningBatchRequest request =
                CreateDirectControllerRequest(3, LmServiceOperation.EnsureDirectControllers);

            ValidationResult validation = ProvisioningRequestValidator.Validate(request);

            AssertTrue(validation.IsValid, validation.JoinMessages());
            AssertEqual(2, request.SchemaVersion,
                "Direct controller mutations require the new protocol schema.");
            string original = request.PlanHash;
            request.DirectControllers[1].Ordinal = 7;
            AssertFalse(CanonicalLmPlanHasher.FixedTimeEqualsHex(
                    original,
                    CanonicalLmPlanHasher.Compute(request)),
                "Every direct assignment field must participate in the canonical hash.");
        }

        private static void DirectControllerProtocolV2RejectsStaleSchemaAndDuplicates()
        {
            LmServiceProvisioningBatchRequest stale =
                CreateDirectControllerRequest(1, LmServiceOperation.EnsureDirectControllers);
            stale.SchemaVersion = 1;
            stale.PlanHash = CanonicalLmPlanHasher.Compute(stale);
            AssertFalse(ProvisioningRequestValidator.Validate(stale).IsValid,
                "An old helper schema must reject a direct-controller request.");

            LmServiceProvisioningBatchRequest duplicate =
                CreateDirectControllerRequest(2, LmServiceOperation.EnsureDirectControllers);
            duplicate.DirectControllers[1].KktSerial =
                duplicate.DirectControllers[0].KktSerial;
            duplicate.PlanHash = CanonicalLmPlanHasher.Compute(duplicate);
            ValidationResult validation = ProvisioningRequestValidator.Validate(duplicate);
            AssertFalse(validation.IsValid,
                "A KKT or ordinal may appear only once in the direct plan.");
            AssertContains(validation.JoinMessages(), "повтор");
        }

        private static void DirectControllerProtocolExposesNoPathsCommandsOrSecrets()
        {
            Type[] protocolTypes =
            {
                typeof(DirectControllerProvisioningItemRequest),
                typeof(LmServiceProvisioningBatchRequest)
            };
            for (int typeIndex = 0; typeIndex < protocolTypes.Length; typeIndex++)
            {
                PropertyInfo[] properties = protocolTypes[typeIndex].GetProperties();
                for (int propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
                {
                    string name = properties[propertyIndex].Name;
                    AssertFalse(name.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Argument", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Token", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0,
                        "Direct protocol must contain identity and ordinals only.");
                }
            }
        }

        private static void OfficialControllerLocatorRequiresInstalledProductRegistration()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string binaryPath = Path.Combine(root, "bin", "lmcontroller.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(binaryPath));
                File.WriteAllBytes(binaryPath, Encoding.ASCII.GetBytes("trusted-controller"));
                ControllerCapabilityProfile profile = CreateTestCapabilityProfile(root, binaryPath, null);

                VerifiedControllerBinaryResult result = new OfficialControllerLocator(
                    profile,
                    new FakeFileTrustVerifier(profile.ControllerBinary, true),
                    new FakePathSafety(true)).ResolveVerifiedBinary();

                AssertFalse(result.IsSuccess,
                    "A trusted file without an exact installed-product record must fail closed.");
                AssertContains(result.ErrorMessage,
                    "Установленный контроллер ЛМ ЧЗ от ЕСП");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void InstalledControllerProductRequiresExactRegistryValues()
        {
            MethodInfo matches = typeof(WindowsInstalledControllerProductVerifier).GetMethod(
                "MatchesProductValues",
                BindingFlags.Static | BindingFlags.NonPublic);
            AssertTrue(matches != null,
                "Registry value matching must be isolated for deterministic verification.");

            ControllerCapabilityProfile profile =
                ControllerCapabilityProfile.Supported();
            object[][] accepted =
            {
                new object[]
                {
                    profile,
                    "ЕСП Контроллер ЛМ ЧЗ",
                    "1.6.4.0",
                    profile.InstallRoot + Path.DirectorySeparatorChar
                },
                new object[]
                {
                    profile, "ЕСП Контроллер ЛМ ЧЗ", "1.6.4.0.375", profile.InstallRoot
                },
                new object[]
                {
                    profile, "ЕСП Контроллер ЛМ ЧЗ", "2.0.0.1", profile.InstallRoot
                }
            };
            for (int index = 0; index < accepted.Length; index++)
            {
                AssertTrue((bool)matches.Invoke(null, accepted[index]),
                    "Очередная версия контроллера от ЕСП должна приниматься.");
            }

            object[][] mismatches =
            {
                new object[] { profile, "Another product", "1.6.4.0", profile.InstallRoot },
                new object[] { profile, "ЕСП Контроллер ЛМ ЧЗ", string.Empty, profile.InstallRoot },
                new object[] { profile, "ЕСП Контроллер ЛМ ЧЗ", null, profile.InstallRoot },
                new object[] { profile, "ЕСП Контроллер ЛМ ЧЗ", "1.6.4.0", profile.InstallRoot + "-other" }
            };
            for (int index = 0; index < mismatches.Length; index++)
            {
                AssertFalse((bool)matches.Invoke(null, mismatches[index]),
                    "Имя продукта, наличие версии и каталог остаются границей доверия.");
            }
        }

        private static void SupportedControllerProfilePinsVendorNotVersion()
        {
            ControllerCapabilityProfile profile =
                ControllerCapabilityProfile.Supported();

            AssertEqual(string.Empty, profile.Version,
                "Версия контроллера не пинуется: принимается любая сборка ЕСП.");
            AssertEqual(0L, profile.ControllerBinary.ByteLength,
                "Размер бинарника меняется от сборки к сборке.");
            AssertEqual(string.Empty, profile.ControllerBinary.Sha256,
                "Хеш бинарника меняется от сборки к сборке.");
            AssertEqual(string.Empty, profile.ControllerBinary.SignerThumbprint,
                "Отпечаток сертификата меняется при перевыпуске.");
            AssertEqual(PeMachine.Unknown, profile.ControllerBinary.Machine,
                "Разрядность бинарника не является границей доверия.");
            AssertEqual("lmcontroller.exe", profile.ControllerBinary.FileName,
                "Имя исполняемого файла контроллера остаётся закреплённым.");
            AssertTrue(profile.ControllerBinary.RequireCodeSigningEku,
                "Подпись контроллера обязана иметь назначение Code Signing.");
            AssertContains(profile.ControllerBinary.SignerSubject, "JSC ESP");

            AssertEqual(string.Empty, profile.Installer.FileName,
                "Имя установщика содержит версию и потому не пинуется.");
            AssertEqual(0L, profile.Installer.ByteLength,
                "Размер установщика меняется от сборки к сборке.");
            AssertEqual(string.Empty, profile.Installer.Sha256,
                "Хеш установщика меняется от сборки к сборке.");
            AssertEqual(string.Empty, profile.Installer.FileVersion,
                "Версия установщика меняется от сборки к сборке.");
            AssertEqual(string.Empty, profile.Installer.SignerThumbprint,
                "Отпечаток сертификата установщика не пинуется.");
            AssertEqual(PeMachine.Unknown, profile.Installer.Machine,
                "Разрядность установщика не является границей доверия.");
            AssertEqual("ЕСП Контроллер ЛМ ЧЗ", profile.Installer.ProductName,
                "Имя продукта вендор не меняет и оно остаётся закреплённым.");
            AssertEqual("ЕСП", profile.Installer.CompanyName,
                "Издатель остаётся закреплённым.");
            AssertTrue(profile.Installer.RequireCodeSigningEku,
                "Подпись установщика обязана иметь назначение Code Signing.");
            AssertContains(profile.Installer.SignerSubject, "JSC ESP");

            PropertyInfo serviceSidType = typeof(ControllerCapabilityProfile).GetProperty(
                "ServiceSidType",
                BindingFlags.Instance | BindingFlags.NonPublic);
            AssertTrue(serviceSidType != null,
                "The characterized official service SID type must be explicit in the profile.");
            AssertEqual(
                WindowsServiceSidType.None,
                (WindowsServiceSidType)serviceSidType.GetValue(profile, null),
                "У штатной службы контроллера нет значения SERVICE_SID_INFO.");
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
                ControllerCapabilityProfile.Supported();

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
                ControllerCapabilityProfile.Supported(),
                VerifiedProvisionerBinary.CreateForTesting(expectedPath));

            service.EnsureConfigured("00105700000001");

            AssertEqual(
                WindowsCommandLine.QuoteArgument(expectedPath) +
                    " --supervise krs-esm-lm-00105700000001",
                api.LastDefinition.ImagePath,
                "CreateService must receive the exact verified provisioner image.");
            WindowsServiceRecord legacyManaged = api.Query(
                "krs-esm-lm-00105700000001");
            legacyManaged.DisplayName = "KRS: legacy controller";
            legacyManaged.StartMode = WindowsServiceStartMode.DemandStart;
            AssertTrue(service.IsManagedDefinition(
                    "00105700000001",
                    legacyManaged),
                "A controller still targeting the exact KRS supervisor may be reclaimed and updated.");
            legacyManaged.ImagePath = @"C:\Windows\System32\foreign-service.exe";
            AssertFalse(service.IsManagedDefinition(
                    "00105700000001",
                    legacyManaged),
                "A controller redirected outside the exact KRS supervisor must remain blocked.");
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
                if (string.Equals(
                        Path.GetFileName(files[fileIndex]),
                        "LegacyOwnedProcessTerminator.cs",
                        StringComparison.Ordinal))
                {
                    continue;
                }
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
                FakeEpmdCommandRunner epmd = new FakeEpmdCommandRunner(
                    new EpmdCommandResult(
                        0,
                        "epmd: up and running\n",
                        string.Empty),
                    new EpmdCommandResult(0, "Killed\n", string.Empty));
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

                services.Start(instance.DatabaseServiceName);
                services.Start(instance.ApiServiceName);
                services.Query(instance.DatabaseServiceName).DisplayName =
                    "KRS: legacy managed LM database";
                services.Query(instance.ApiServiceName).StartMode =
                    WindowsServiceStartMode.DemandStart;
                File.WriteAllText(
                    Path.Combine(instance.ConfigRoot, "regime-local.ini"),
                    "corrupted owned profile",
                    new UTF8Encoding(false));
                platform.Reconcile(item, operationId);

                AssertEqual(ManagedLocalModuleObservedState.OwnedMismatch,
                    platform.Inspect(item, context),
                    "A running exact manifest-owned pair with an incomplete profile must be repairable.");

                platform.PrepareProfile(item, context, operationId);

                AssertEqual(WindowsServiceState.Stopped,
                    services.Query(instance.DatabaseServiceName).State,
                    "Profile recovery must stop the exact owned database service first.");
                AssertEqual(WindowsServiceState.Stopped,
                    services.Query(instance.ApiServiceName).State,
                    "Profile recovery must stop the exact owned API service first.");

                services.Query(instance.DatabaseServiceName).ImagePath =
                    @"C:\Windows\System32\foreign-service.exe";
                platform.Reconcile(item, operationId);
                AssertEqual(ManagedLocalModuleObservedState.Foreign,
                    platform.Inspect(item, context),
                    "A service redirected outside the verified KRS supervisor must remain blocked.");
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

        private static void LocalModuleInventoryKeepsOperatorReadOnlyAccess()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                const string operatorSid = "S-1-5-21-111-222-333-1001";
                FakePathSafety pathSafety = new FakePathSafety(true);
                LocalModuleManifestStore manifests;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    pathSafety,
                    operatorSid,
                    out manifests,
                    out runtime,
                    out instance);

                AssertTrue(pathSafety.HasReadOnlyDirectoryAccess(
                        manifests.MachineRoot,
                        operatorSid),
                    "The operator must be able to traverse the protected inventory root.");
                AssertTrue(pathSafety.HasReadOnlyFileAccess(
                        manifests.GetInstanceManifestPath(instance.InstanceId),
                        operatorSid),
                    "Writing an LM instance manifest must preserve operator read access.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(root);
            }
        }

        private static void LocalModuleInventoryRepairsLegacyOperatorReadOnlyAccess()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                const string operatorSid = "S-1-5-21-111-222-333-1001";
                LocalModuleManifestStore legacyStore;
                LocalModuleRuntimeManifest runtime;
                LocalModuleInstanceManifest instance;
                CreateTestManagedLocalModuleOwnership(
                    root,
                    new FakePathSafety(true),
                    null,
                    out legacyStore,
                    out runtime,
                    out instance);
                ManagedKktStackManifest stack = ManagedKktStackManifest.Create(
                    CreateManagedLocalModuleRequest(1).ManagedLocalModules[0],
                    instance.InstanceId,
                    string.Empty,
                    "89898989898989898989898989898989",
                    "cccccccccccccccccccccccccccccccc");
                legacyStore.WriteStack(stack);

                FakePathSafety repairedAcl = new FakePathSafety(true);
                LocalModuleManifestStore currentStore =
                    new LocalModuleManifestStore(
                        legacyStore.MachineRoot,
                        legacyStore.RuntimeContainerRoot,
                        repairedAcl,
                        operatorSid);

                currentStore.RepairOperatorInventoryAccess();

                AssertTrue(repairedAcl.HasReadOnlyFileAccess(
                        currentStore.GetInstanceManifestPath(instance.InstanceId),
                        operatorSid),
                    "A validated legacy LM manifest must become readable by the initiating operator.");
                AssertTrue(repairedAcl.HasReadOnlyFileAccess(
                        currentStore.GetStackManifestPath(stack.KktSerial),
                        operatorSid),
                    "A validated legacy KKT stack must become readable by the initiating operator.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(root);
            }
        }

        private static void ManagedRemovalJournalKeepsOperatorReadOnlyAccess()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                const string operatorSid = "S-1-5-21-111-222-333-1001";
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
                    "79797979797979797979797979797979",
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
                manifests.WriteStack(stack);
                ManagedLocalModuleRemovalSnapshot snapshot =
                    ManagedLocalModuleRemovalSnapshot.CreateOwned(
                        stack,
                        instance,
                        runtime);
                FakePathSafety pathSafety = new FakePathSafety(true);
                ManagedLocalModuleRemovalJournalStore journal =
                    new ManagedLocalModuleRemovalJournalStore(
                        manifests.MachineRoot,
                        pathSafety,
                        operatorSid);

                journal.Write(
                    snapshot,
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    "simulated-error");

                string removalRoot = Path.Combine(
                    manifests.MachineRoot,
                    "ManagedLocalModuleRemoval");
                string journalPath = Path.Combine(
                    removalRoot,
                    stack.KktSerial + ".json");
                AssertTrue(pathSafety.HasReadOnlyDirectoryAccess(
                        removalRoot,
                        operatorSid),
                    "The operator must be able to enumerate removal recovery state.");
                AssertTrue(pathSafety.HasReadOnlyFileAccess(
                        journalPath,
                        operatorSid),
                    "A removal journal must remain readable after the elevated helper writes it.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(root);
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

        private static void LegacyTerminatorRejectsPidReuseAndForeignIdentity()
        {
            string root = Path.GetFullPath(@"C:\ProgramData\KRS\MultiKKT\LocalModules\runtime");
            LegacyProcessSnapshot original = new LegacyProcessSnapshot
            {
                ProcessId = 4321,
                CreationTimeUtcTicks = 638900000000000000L,
                ImagePath = Path.Combine(root, "erts", "bin", "erl.exe"),
                ImageSha256 = new string('a', 64),
                CommandLine = "erl.exe -name krs_lm_regime_n01@127.0.0.1"
            };
            VerifiedLegacyProcessIdentity identity =
                VerifiedLegacyProcessIdentity.Create(
                    original,
                    root,
                    original.ImagePath,
                    original.ImageSha256,
                    original.CommandLine,
                    new string('b', 64));
            FakeLegacyProcessSnapshotReader reader =
                new FakeLegacyProcessSnapshotReader(original);
            FakeLegacyNativeTerminator native = new FakeLegacyNativeTerminator();
            native.OnTerminate = delegate { reader.Current = null; };
            LegacyOwnedProcessTerminator terminator =
                new LegacyOwnedProcessTerminator(reader, native);

            reader.Current = new LegacyProcessSnapshot
            {
                ProcessId = original.ProcessId,
                CreationTimeUtcTicks = original.CreationTimeUtcTicks + 1,
                ImagePath = original.ImagePath,
                ImageSha256 = original.ImageSha256,
                CommandLine = original.CommandLine
            };
            AssertThrows<InvalidDataException>(delegate {
                terminator.Terminate(identity, NeverCancelLmProvisioning.Instance);
            }, "A reused PID must never be terminated.");
            AssertEqual(0, native.Terminated.Count,
                "PID reuse must be rejected before the native termination call.");

            reader.Current = original;
            terminator.Terminate(identity, NeverCancelLmProvisioning.Instance);
            AssertEqual(1, native.Terminated.Count,
                "Only the exactly re-observed owned orphan may reach native termination.");

            LegacyProcessSnapshot foreign = new LegacyProcessSnapshot
            {
                ProcessId = 9876,
                CreationTimeUtcTicks = original.CreationTimeUtcTicks,
                ImagePath = @"C:\Windows\System32\notepad.exe",
                ImageSha256 = new string('c', 64),
                CommandLine = "notepad.exe"
            };
            AssertThrows<InvalidDataException>(delegate {
                VerifiedLegacyProcessIdentity.Create(
                    foreign,
                    root,
                    original.ImagePath,
                    original.ImageSha256,
                    original.CommandLine,
                    new string('d', 64));
            }, "A foreign executable must never become a verified legacy identity.");
        }

        private static void LegacyMigrationRetriesPartialCleanupAndHonorsCancellation()
        {
            FakeLegacyManagedStateCleanup cleanup = new FakeLegacyManagedStateCleanup(
                new[] { "00105700000001", "00105700000002" });
            cleanup.CleanupPendingOnceFor = "00105700000001";
            LegacyManagedStateMigrationWorkflow workflow =
                new LegacyManagedStateMigrationWorkflow(cleanup, delegate { });

            LegacyManagedStateMigrationResult result = workflow.Execute(
                Guid.NewGuid().ToString("N"),
                "S-1-5-21-111-222-333-1001",
                NeverCancelLmProvisioning.Instance);

            AssertTrue(result.IsComplete,
                "A retryable partial cleanup must be retried and completed.");
            AssertEqual(2, cleanup.Attempts["00105700000001"],
                "CleanupPending must be retried at a bounded migration boundary.");
            AssertEqual(1, cleanup.Attempts["00105700000002"],
                "An independent legacy stack must still be cleaned.");

            FakeLegacyManagedStateCleanup cancelledCleanup =
                new FakeLegacyManagedStateCleanup(new[] { "00105700000003" });
            LegacyManagedStateMigrationResult cancelled =
                new LegacyManagedStateMigrationWorkflow(
                    cancelledCleanup,
                    delegate { }).Execute(
                        Guid.NewGuid().ToString("N"),
                        "S-1-5-21-111-222-333-1001",
                        new AlwaysCancelledProvisioning());
            AssertTrue(cancelled.IsCancelled,
                "Cancellation must stop migration before the next owned stack.");
            AssertEqual(0, cancelledCleanup.Attempts.Count,
                "Cancelled migration must not mutate any legacy service.");
        }

        private static void EsmInstanceYamlPatchesUniqueStructuralLdbControl()
        {
            string source =
                "defaultconfig:\r\n" +
                "    settings:\r\n" +
                "        ldbControl:\r\n" +
                "            gRPCPort: 50063\r\n" +
                "            RESTPort: 5063\r\n" +
                "            url: 0.0.0.0\r\n" +
                "            login: admin\r\n" +
                "            password: admin # must stay untouched\r\n" +
                "            version: \"\"\r\n" +
                "        dkkt:\r\n" +
                "            port:\r\n" +
                "                - 4042\r\n" +
                "            timeout: 30\r\n" +
                "        gisMT:\r\n" +
                "            params:\r\n" +
                "                config:\r\n" +
                "                    CdnCodesCheckTimeoutGISValue: null\r\n" +
                "                    url: https://rsapi.crpt.ru/api?a=1&b=2\r\n" +
                "        notes: |\r\n" +
                "            free text: not yaml\r\n" +
                "            - not a sequence\r\n" +
                "filepath: C:\\ProgramData\\esp\\esm\\um\r\n";
            string patched = EsmInstanceControllerConfigPatcher.Patch(
                source,
                50064,
                5064);
            AssertContains(patched, "            gRPCPort: 50064\r\n");
            AssertContains(patched, "            RESTPort: 5064\r\n");
            AssertContains(patched, "            url: 127.0.0.1\r\n");
            AssertContains(patched, "password: admin # must stay untouched");
            AssertContains(patched, "            port:\r\n                - 4042\r\n");
            AssertContains(patched, "url: https://rsapi.crpt.ru/api?a=1&b=2");
            AssertContains(patched, "            - not a sequence\r\n");
            AssertContains(patched, "filepath: C:\\ProgramData\\esp\\esm\\um");
            AssertEqual(patched, EsmInstanceControllerConfigPatcher.Patch(
                patched,
                50064,
                5064), "Patching the exact ESM target must be idempotent.");
        }

        private static void EsmInstanceYamlRejectsDuplicateAnchorsAndAliases()
        {
            AssertThrows<InvalidDataException>(delegate
            {
                EsmInstanceControllerConfigPatcher.Patch(
                    "settings:\n  ldbControl:\n    gRPCPort: 1\n    gRPCPort: 2\n    RESTPort: 3\n    url: x\n",
                    50063,
                    5063);
            }, "Duplicate target keys must be rejected.");
            AssertThrows<InvalidDataException>(delegate
            {
                EsmInstanceControllerConfigPatcher.Patch(
                    "settings: &base\n  ldbControl:\n    gRPCPort: 1\n    RESTPort: 2\n    url: x\n",
                    50063,
                    5063);
            }, "YAML anchors must be rejected.");
            AssertThrows<InvalidDataException>(delegate
            {
                EsmInstanceControllerConfigPatcher.Patch(
                    "settings:\n  ldbControl:\n    <<: *defaults\n    gRPCPort: 1\n    RESTPort: 2\n    url: x\n",
                    50063,
                    5063);
            }, "YAML aliases and merge keys must be rejected.");
        }

        private static void EsmInstanceYamlSupportsBlockSequencesAndRejectsAmbiguity()
        {
            string sequences =
                "services:\n" +
                "  - name: regime\n" +
                "    port: 5995\n" +
                "  - name: yenisei\n" +
                "    port: 6984\n" +
                "settings:\n" +
                "  ldbControl:\n" +
                "    gRPCPort: 1\n" +
                "    RESTPort: 2\n" +
                "    url: 0.0.0.0\n";
            string patchedSequences = EsmInstanceControllerConfigPatcher.Patch(
                sequences,
                50063,
                5063);
            AssertContains(patchedSequences, "  - name: regime\n    port: 5995\n");
            AssertContains(patchedSequences, "  - name: yenisei\n    port: 6984\n");
            AssertContains(patchedSequences, "    gRPCPort: 50063\n");
            AssertContains(patchedSequences, "    RESTPort: 5063\n");
            AssertContains(patchedSequences, "    url: 127.0.0.1\n");

            AssertThrows<InvalidDataException>(delegate
            {
                EsmInstanceControllerConfigPatcher.Patch(
                    "first:\n  settings:\n    ldbControl:\n      gRPCPort: 1\n      RESTPort: 2\n      url: x\n" +
                    "second:\n  settings:\n    ldbControl:\n      gRPCPort: 3\n      RESTPort: 4\n      url: y\n",
                    50063,
                    5063);
            }, "Several ldbControl nodes must be rejected.");
            AssertThrows<InvalidDataException>(delegate
            {
                EsmInstanceControllerConfigPatcher.Patch(
                    "settings:\n  other:\n    gRPCPort: 1\n    RESTPort: 2\n    url: x\n",
                    50063,
                    5063);
            }, "A missing ldbControl node must be rejected.");
            AssertThrows<InvalidDataException>(delegate
            {
                EsmInstanceControllerConfigPatcher.Patch(
                    "settings:\n  ldbControl:\n    gRPCPort:\n      value: 1\n    RESTPort: 2\n    url: x\n",
                    50063,
                    5063);
            }, "A nested block instead of a scalar must be rejected.");
            AssertThrows<InvalidDataException>(delegate
            {
                EsmInstanceControllerConfigPatcher.Patch(
                    "settings:\n\tldbControl:\n\t\tgRPCPort: 1\n\t\tRESTPort: 2\n\t\turl: x\n",
                    50063,
                    5063);
            }, "Tab indentation must be rejected.");
            AssertThrows<InvalidDataException>(delegate
            {
                EsmInstanceControllerConfigPatcher.Patch(
                    "---\nsettings:\n  ldbControl:\n    gRPCPort: 1\n    RESTPort: 2\n    url: x\n---\nother: 1\n",
                    50063,
                    5063);
            }, "Multiple YAML documents must be rejected.");
        }

        private static void EsmInstanceServiceIsStartableButNeverMutable()
        {
            const string instance = "esm-cm-00106205280301";
            AssertTrue(
                WindowsServiceApi.IsLifecycleServiceName(instance),
                "Экземпляр ЕСМ обязан останавливаться и запускаться: без этого " +
                "перенаправление на клон контроллера падает в поле.");
            AssertFalse(
                WindowsServiceApi.IsOwnedServiceName(instance),
                "Вендорскую службу ЕСМ нельзя создавать, менять и удалять.");
            AssertTrue(
                WindowsServiceApi.IsOwnedServiceName("esm-lm-controller-2"),
                "Клон контроллера остаётся нашей службой.");

            string[] foreign =
            {
                "esm-cm-",
                "esm-cm-0010620528030",
                "esm-cm-001062052803011",
                "esm-cm-0010620528030X",
                "esm-cm-00106205280301 ",
                "Esm-Cm-00106205280301"
            };
            for (int index = 0; index < foreign.Length; index++)
            {
                AssertFalse(
                    WindowsServiceApi.IsLifecycleServiceName(foreign[index]),
                    "Имя " + foreign[index] + " не должно приниматься.");
            }

            string serial;
            AssertTrue(
                EsmTspiot.Shared.Services.EsmInstanceServiceIdentity.TryParseName(
                    instance,
                    out serial),
                "Имя службы экземпляра ЕСМ должно разбираться.");
            AssertEqual("00106205280301", serial,
                "Серийный номер должен извлекаться.");
            AssertEqual(
                instance,
                EsmTspiot.Shared.Services.EsmInstanceServiceIdentity.CreateName(
                    "00106205280301"),
                "Имя службы должно собираться обратно без изменений.");
        }

        private static void EsmInstanceConfigRecoversFromAbortedOperation()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string esmRoot = Path.Combine(root, "ESP", "ESM", "um");
                Directory.CreateDirectory(esmRoot);
                string serial = "00105700000003";
                string configPath = Path.Combine(esmRoot, "config_" + serial + ".yml");
                string original =
                    "settings:\n  ldbControl:\n    password: admin\n" +
                    "    gRPCPort: 50063\n    RESTPort: 5063\n    url: 127.0.0.1\n" +
                    "dkkt:\n  port: 4042\n";
                File.WriteAllText(configPath, original, new UTF8Encoding(false));
                DirectControllerManifestStore manifests =
                    new DirectControllerManifestStore(
                        Path.Combine(root, "DirectControllers"),
                        new FakePathSafety(true),
                        null,
                        Path.Combine(root, "ProgramData"));
                DirectControllerManifest manifest = DirectControllerManifest.Create(
                    serial,
                    "7701234567",
                    2,
                    "1.6.4.0",
                    new string('c', 64),
                    manifests.GetProfileEnvironmentRoot(2),
                    Guid.NewGuid().ToString("N"),
                    DirectControllerLifecycleState.Preparing);
                manifests.Write(manifest);
                FakeWindowsServiceApi services = new FakeWindowsServiceApi();
                EsmInstanceConfigManager manager = new EsmInstanceConfigManager(
                    esmRoot,
                    manifests,
                    services,
                    new AtomicFileWriter(),
                    delegate { });

                // Служба экземпляра ЕСМ недоступна: остановка падает уже после
                // того, как манифест записал «применено». Полевой отказ.
                bool refused = false;
                try
                {
                    manager.ApplyAndRestart(manifests.Read(serial), false);
                }
                catch (InvalidDataException)
                {
                    refused = true;
                }
                AssertTrue(refused,
                    "Отсутствующая служба экземпляра ЕСМ обязана быть ошибкой.");
                AssertEqual(original, File.ReadAllText(configPath, Encoding.UTF8),
                    "Несостоявшееся применение не должно менять конфигурацию ЕСМ.");
                AssertFalse(File.Exists(manifests.GetEsmConfigBackupPath(serial)),
                    "Резервная копия несостоявшегося применения не должна оставаться.");
                DirectControllerManifest afterFailure = manifests.Read(serial);
                AssertTrue(
                    string.IsNullOrEmpty(afterFailure.EsmConfigAppliedSha256) &&
                    string.IsNullOrEmpty(afterFailure.EsmConfigOriginalSha256),
                    "Признак применения обязан сниматься, иначе следующий прогон " +
                    "упрётся в защиту от перезаписи чужих правок.");

                // Состояние, оставленное прежней сборкой: манифест ссылается на
                // конфигурацию, которой на диске уже нет (ЕСМ пересоздал её при
                // перерегистрации ККТ).
                DirectControllerManifest stale = manifests.Read(serial);
                stale.EsmConfigOriginalSha256 = new string('d', 64);
                stale.EsmConfigAppliedSha256 = new string('e', 64);
                stale.UpdatedUtc = DateTime.UtcNow.ToString("o");
                manifests.Write(stale);
                services.SetRecord(new WindowsServiceRecord
                {
                    ServiceName = "esm-cm-" + serial,
                    State = WindowsServiceState.Running,
                    ProcessId = 909
                });
                bool guarded = false;
                try
                {
                    manager.ApplyAndRestart(manifests.Read(serial), false);
                }
                catch (InvalidDataException)
                {
                    guarded = true;
                }
                AssertTrue(guarded,
                    "Без признака оборванной операции чужую правку трогать нельзя.");
                AssertEqual(original, File.ReadAllText(configPath, Encoding.UTF8),
                    "Отказ обязан оставлять конфигурацию ЕСМ нетронутой.");

                AssertEqual(
                    EsmInstanceConfigApplyState.Applied,
                    manager.ApplyAndRestart(manifests.Read(serial), true),
                    "После оборванной операции настройка обязана перепривязываться " +
                    "к текущей конфигурации ЕСМ.");
                AssertContains(File.ReadAllText(configPath, Encoding.UTF8),
                    "gRPCPort: 50064");
                DirectControllerManifest applied = manifests.Read(serial);
                AssertEqual(
                    Sha256Hex(File.ReadAllBytes(configPath)),
                    applied.EsmConfigAppliedSha256,
                    "Манифест обязан ссылаться на то, что действительно записано.");
                AssertTrue(File.Exists(manifests.GetEsmConfigBackupPath(serial)),
                    "Перепривязка обязана заново сохранить исходный файл.");

                // Теперь конфигурацию правили вручную: перезаписывать её вслепую
                // нельзя, но и снятие комплекта блокировать нечем.
                File.WriteAllText(
                    configPath,
                    File.ReadAllText(configPath, Encoding.UTF8) + "extra: 1\n",
                    new UTF8Encoding(false));
                AssertFalse(
                    manager.RestoreAndRestart(manifests.Read(serial)),
                    "Незнакомую конфигурацию ЕСМ нельзя восстанавливать вслепую.");
                AssertContains(File.ReadAllText(configPath, Encoding.UTF8), "extra: 1");
                AssertTrue(File.Exists(manifests.GetEsmConfigBackupPath(serial)),
                    "Резервная копия остаётся оператору, раз мы её больше не сторожим.");
                DirectControllerManifest disowned = manifests.Read(serial);
                AssertTrue(
                    string.IsNullOrEmpty(disowned.EsmConfigAppliedSha256) &&
                    string.IsNullOrEmpty(disowned.EsmConfigOriginalSha256),
                    "Снятие комплекта не должно упираться в чужую правку.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(root);
            }
        }

        private static string Sha256Hex(byte[] value)
        {
            using (System.Security.Cryptography.SHA256 hash =
                System.Security.Cryptography.SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(value);
                StringBuilder builder = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    builder.Append(digest[index].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static void EsmInstanceConfigTransactionBacksUpRestoresAndDefers()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string esmRoot = Path.Combine(root, "ESP", "ESM", "um");
                Directory.CreateDirectory(esmRoot);
                string serial = "00105700000001";
                string configPath = Path.Combine(esmRoot, "config_" + serial + ".yml");
                string original =
                    "settings:\n  ldbControl:\n    password: admin\n" +
                    "    gRPCPort: 50063\n    RESTPort: 5063\n    url: 127.0.0.1\n" +
                    "dkkt:\n  port: 4042\n";
                File.WriteAllText(configPath, original, new UTF8Encoding(false));
                string orchestrator = Path.Combine(esmRoot, "config-orchestrator.yml");
                File.WriteAllText(orchestrator, "vendor-template", Encoding.UTF8);
                DirectControllerManifestStore manifests =
                    new DirectControllerManifestStore(
                        Path.Combine(root, "DirectControllers"),
                        new FakePathSafety(true),
                        null,
                        Path.Combine(root, "ProgramData"));
                DirectControllerManifest manifest = DirectControllerManifest.Create(
                    serial,
                    "7701234567",
                    2,
                    "1.6.4.0",
                    new string('a', 64),
                    manifests.GetProfileEnvironmentRoot(2),
                    Guid.NewGuid().ToString("N"),
                    DirectControllerLifecycleState.Preparing);
                manifests.Write(manifest);
                FakeWindowsServiceApi services = new FakeWindowsServiceApi();
                services.SetRecord(new WindowsServiceRecord
                {
                    ServiceName = "esm-cm-" + serial,
                    State = WindowsServiceState.Running,
                    ProcessId = 777
                });
                EsmInstanceConfigManager manager = new EsmInstanceConfigManager(
                    esmRoot,
                    manifests,
                    services,
                    new AtomicFileWriter(),
                    delegate { });

                AssertEqual(
                    EsmInstanceConfigApplyState.Applied,
                    manager.ApplyAndRestart(manifest, false),
                    "The instance config must be applied transactionally.");
                AssertContains(File.ReadAllText(configPath), "gRPCPort: 50064");
                AssertTrue(File.Exists(manifests.GetEsmConfigBackupPath(serial)),
                    "The protected original config must exist until removal.");
                AssertEqual("vendor-template", File.ReadAllText(orchestrator, Encoding.UTF8),
                    "The orchestrator template must never be touched.");
                DirectControllerManifest applied = manifests.Read(serial);
                AssertTrue(manager.RestoreAndRestart(applied),
                    "Hash-matching managed config must be restored.");
                AssertEqual(original, File.ReadAllText(configPath, Encoding.UTF8),
                    "Removal must restore the byte-equivalent source text.");
                AssertFalse(File.Exists(manifests.GetEsmConfigBackupPath(serial)),
                    "A completed restore must remove the secret-bearing backup.");

                DirectControllerManifest absent = DirectControllerManifest.Create(
                    "00105700000002",
                    "7701234567",
                    3,
                    "1.6.4.0",
                    new string('b', 64),
                    manifests.GetProfileEnvironmentRoot(3),
                    Guid.NewGuid().ToString("N"),
                    DirectControllerLifecycleState.Preparing);
                manifests.Write(absent);
                AssertEqual(
                    EsmInstanceConfigApplyState.Deferred,
                    manager.ApplyAndRestart(absent, false),
                    "An instance config that is not created yet must defer without mutation.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(root);
            }
        }

        private static void DirectControllerProfileStagesOnlyOfficialCaPair()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string officialRoot = Path.Combine(root, "official");
                string cloneRoot = Path.Combine(root, "clone");
                Directory.CreateDirectory(officialRoot);
                Directory.CreateDirectory(cloneRoot);
                File.WriteAllText(Path.Combine(officialRoot, "ca.crt"),
                    "-----BEGIN CERTIFICATE-----\nfield-ca\n-----END CERTIFICATE-----\n",
                    Encoding.ASCII);
                File.WriteAllText(Path.Combine(officialRoot, "ca.pem"),
                    "-----BEGIN PRIVATE KEY-----\nfield-key\n-----END PRIVATE KEY-----\n",
                    Encoding.ASCII);
                File.WriteAllText(Path.Combine(officialRoot, "server.crt"), "must-not-copy",
                    Encoding.ASCII);
                File.WriteAllText(Path.Combine(officialRoot, "server.pem"), "must-not-copy",
                    Encoding.ASCII);
                File.WriteAllText(Path.Combine(officialRoot, "config.yml"), "must-not-copy",
                    Encoding.ASCII);

                FakePathSafety paths = new FakePathSafety(true);
                new DirectControllerCaStager(
                    ControllerCapabilityProfile.Supported(),
                    new AtomicFileWriter(),
                    paths,
                    officialRoot).Stage(cloneRoot);

                string[] files = Directory.GetFiles(cloneRoot, "*", SearchOption.AllDirectories);
                AssertEqual(2, files.Length,
                    "Only the shared CA certificate and key may be staged before first start.");
                AssertContains(File.ReadAllText(Path.Combine(cloneRoot, "ca.crt"), Encoding.ASCII),
                    "field-ca");
                AssertContains(File.ReadAllText(Path.Combine(cloneRoot, "ca.pem"), Encoding.ASCII),
                    "field-key");
                AssertFalse(File.Exists(Path.Combine(cloneRoot, "server.crt")) ||
                            File.Exists(Path.Combine(cloneRoot, "server.pem")) ||
                            File.Exists(Path.Combine(cloneRoot, "config.yml")),
                    "Per-instance server identity and config must be generated independently.");
                AssertTrue(paths.WasProtectedReadOnly(Path.Combine(cloneRoot, "ca.crt")) &&
                            paths.WasProtectedReadOnly(Path.Combine(cloneRoot, "ca.pem")),
                    "The staged CA pair must be hardened before final validation.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void DirectControllerProfileAtomicallyReplacesReadOnlyCa()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string officialRoot = Path.Combine(root, "official");
                string cloneRoot = Path.Combine(root, "clone");
                Directory.CreateDirectory(officialRoot);
                Directory.CreateDirectory(cloneRoot);
                File.WriteAllText(Path.Combine(officialRoot, "ca.crt"),
                    "-----BEGIN CERTIFICATE-----\nreplacement-ca\n-----END CERTIFICATE-----\n",
                    Encoding.ASCII);
                File.WriteAllText(Path.Combine(officialRoot, "ca.pem"),
                    "-----BEGIN RSA PRIVATE KEY-----\nreplacement-key\n-----END RSA PRIVATE KEY-----\n",
                    Encoding.ASCII);
                string cloneCertificate = Path.Combine(cloneRoot, "ca.crt");
                string cloneKey = Path.Combine(cloneRoot, "ca.pem");
                File.WriteAllText(cloneCertificate,
                    "-----BEGIN CERTIFICATE-----\nold-ca\n-----END CERTIFICATE-----\n",
                    Encoding.ASCII);
                File.WriteAllText(cloneKey,
                    "-----BEGIN PRIVATE KEY-----\nold-key\n-----END PRIVATE KEY-----\n",
                    Encoding.ASCII);
                File.SetAttributes(cloneCertificate, FileAttributes.ReadOnly);
                File.SetAttributes(cloneKey, FileAttributes.ReadOnly);

                new DirectControllerCaStager(
                    ControllerCapabilityProfile.Supported(),
                    new AtomicFileWriter(),
                    new FakePathSafety(true),
                    officialRoot).Stage(cloneRoot);

                AssertContains(File.ReadAllText(cloneCertificate, Encoding.ASCII), "replacement-ca");
                AssertContains(File.ReadAllText(cloneKey, Encoding.ASCII), "replacement-key");
                AssertEqual(0, Directory.GetFiles(cloneRoot, "*.tmp").Length,
                    "Atomic replacement must not leave temporary CA files.");
                AssertFalse((File.GetAttributes(cloneCertificate) & FileAttributes.ReadOnly) != 0 ||
                            (File.GetAttributes(cloneKey) & FileAttributes.ReadOnly) != 0,
                    "Repaired CA files must remain writable by the privileged one-shot helper.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(root);
            }
        }

        private static void DirectControllerProfileWritesIsolatedPortsWithoutCredentials()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string officialRoot = Path.Combine(root, "official");
                Directory.CreateDirectory(officialRoot);
                File.WriteAllText(Path.Combine(officialRoot, "ca.crt"),
                    "-----BEGIN CERTIFICATE-----\nfield-ca\n-----END CERTIFICATE-----\n",
                    Encoding.ASCII);
                File.WriteAllText(Path.Combine(officialRoot, "ca.pem"),
                    "-----BEGIN PRIVATE KEY-----\nfield-key\n-----END PRIVATE KEY-----\n",
                    Encoding.ASCII);
                FakePathSafety paths = new FakePathSafety(true);
                DirectControllerManifestStore manifests = new DirectControllerManifestStore(
                    Path.Combine(root, "DirectControllers"),
                    paths,
                    null);
                string environmentRoot = manifests.GetProfileEnvironmentRoot(2);
                DirectControllerManifest manifest = DirectControllerManifest.Create(
                    "00105700000001",
                    "7701234567",
                    2,
                    7595,
                    "1.6.4.0",
                    new string('a', 64),
                    environmentRoot,
                    Guid.NewGuid().ToString("N"),
                    DirectControllerLifecycleState.Preparing);
                DirectControllerProfileStore store = new DirectControllerProfileStore(
                    ControllerCapabilityProfile.Supported(),
                    manifests,
                    new DirectControllerCaStager(
                        ControllerCapabilityProfile.Supported(),
                        new AtomicFileWriter(),
                        paths,
                        officialRoot),
                    new AtomicFileWriter(),
                    paths);

                string configPath = store.PrepareClone(manifest);
                string yaml = File.ReadAllText(configPath, Encoding.UTF8);

                AssertContains(yaml, "gRPCPort: 50064");
                AssertContains(yaml, "RESTPort: 5064");
                AssertContains(yaml, "port: 7595");
                AssertFalse(yaml.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            yaml.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The local controller profile must not persist ESM binding credentials.");
                string vendorRoot = Path.GetDirectoryName(configPath);
                AssertTrue(File.Exists(Path.Combine(vendorRoot, "ca.crt")) &&
                           File.Exists(Path.Combine(vendorRoot, "ca.pem")),
                    "The isolated profile must receive the trusted CA pair.");
                AssertFalse(File.Exists(Path.Combine(vendorRoot, "server.crt")) ||
                            File.Exists(Path.Combine(vendorRoot, "server.pem")),
                    "The vendor process must generate its own server identity on first start.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(root);
            }
        }

        private static void DirectControllerManifestSafelyUpgradesLegacyTargetPort()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                const string serial = "00105700000001";
                DirectControllerManifestStore store = new DirectControllerManifestStore(
                    Path.Combine(root, "DirectControllers"),
                    new FakePathSafety(true),
                    null,
                    Path.Combine(root, "ProgramData"));
                DirectControllerManifest legacy = new DirectControllerManifest
                {
                    SchemaVersion = 1,
                    OwnershipMarker = DirectControllerManifest.ExpectedOwnershipMarker,
                    KktSerial = serial,
                    KktInn = "7701234567",
                    Ordinal = 2,
                    ServiceName = "esm-lm-controller-2",
                    GrpcPort = 50064,
                    RestPort = 5064,
                    LegacyFutureLocalModulePort = 6995,
                    ControllerVersion = "1.6.4.0",
                    ControllerBinarySha256 = new string('a', 64),
                    ProfileEnvironmentRoot = store.GetProfileEnvironmentRoot(2),
                    OperationId = Guid.NewGuid().ToString("N"),
                    State = DirectControllerLifecycleState.Ready,
                    UpdatedUtc = DateTime.UtcNow.ToString("o")
                };
                string manifestPath = Path.Combine(
                    root,
                    "DirectControllers",
                    "Owned",
                    serial,
                    "manifest.json");
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
                using (FileStream stream = File.Create(manifestPath))
                {
                    new DataContractJsonSerializer(
                        typeof(DirectControllerManifest)).WriteObject(stream, legacy);
                }

                DirectControllerManifest upgraded = store.Read(serial);

                AssertEqual(DirectControllerManifest.CurrentSchemaVersion,
                    upgraded.SchemaVersion,
                    "An owned v1 manifest must be normalized to the current schema.");
                AssertEqual(6995, upgraded.TargetLocalModulePort,
                    "The legacy ordinal-derived target must survive migration.");
                AssertEqual(0, upgraded.LegacyFutureLocalModulePort,
                    "The legacy field must be cleared before the next write.");
                store.Write(upgraded);
                string persisted = File.ReadAllText(manifestPath, Encoding.UTF8);
                AssertContains(persisted, "TargetLocalModulePort");
                AssertFalse(persisted.Contains("FutureLocalModulePort"),
                    "Rewritten manifests must not retain the obsolete field.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(root);
            }
        }

        private static void WindowsDirectControllerPlatformCreatesAndRemovesExactClone()
        {
            string root = CreateTemporaryDirectory();
            try
            {
                string officialRoot = Path.Combine(root, "official");
                Directory.CreateDirectory(officialRoot);
                File.WriteAllText(Path.Combine(officialRoot, "ca.crt"),
                    "-----BEGIN CERTIFICATE-----\nfield-ca\n-----END CERTIFICATE-----\n",
                    Encoding.ASCII);
                File.WriteAllText(Path.Combine(officialRoot, "ca.pem"),
                    "-----BEGIN PRIVATE KEY-----\nfield-key\n-----END PRIVATE KEY-----\n",
                    Encoding.ASCII);
                FakePathSafety paths = new FakePathSafety(true);
                DirectControllerManifestStore manifests = new DirectControllerManifestStore(
                    Path.Combine(root, "DirectControllers"),
                    paths,
                    null,
                    Path.Combine(root, "ProgramData"));
                ControllerCapabilityProfile profile =
                    ControllerCapabilityProfile.Supported();
                DirectControllerProfileStore profiles = new DirectControllerProfileStore(
                    profile,
                    manifests,
                    new DirectControllerCaStager(
                        profile,
                        new AtomicFileWriter(),
                        paths,
                        officialRoot),
                    new AtomicFileWriter(),
                    paths);
                FakeWindowsServiceApi services = new FakeWindowsServiceApi();
                services.SetRecord(new WindowsServiceRecord
                {
                    ServiceName = "esm-lm-controller",
                    ImagePath = "\"C:\\Program Files\\ESP\\LMController\\bin\\lmcontroller.exe\"",
                    AccountName = "LocalSystem",
                    Dependencies = new List<string>(),
                    StartMode = WindowsServiceStartMode.AutoStart,
                    ErrorControl = WindowsServiceErrorControl.Ignore,
                    ServiceSidType = WindowsServiceSidType.None,
                    RecoveryPolicy = new WindowsServiceRecoveryPolicy(0, new int[0]),
                    State = WindowsServiceState.Running,
                    ProcessId = 100
                });
                WindowsDirectControllerPlatform platform = new WindowsDirectControllerPlatform(
                    profile,
                    new VerifiedControllerBinary
                    {
                        FullPath = @"C:\Program Files\ESP\LMController\bin\lmcontroller.exe",
                        Version = "1.6.4.0",
                        Sha256 = new string('a', 64),
                        SignerThumbprint = ControllerSignerThumbprint,
                        Machine = PeMachine.Amd64
                    },
                    manifests,
                    profiles,
                    services,
                    new FakeDirectControllerReadiness(true));
                DirectControllerProvisioningItemRequest item =
                    CreateDirectControllerRequest(
                        2,
                        LmServiceOperation.EnsureDirectControllers).DirectControllers[1];
                string operationId = Guid.NewGuid().ToString("N");

                LmServiceProvisioningItemResult ensured = platform.Ensure(
                    item,
                    operationId,
                    "S-1-5-21-111-222-333-1001");

                AssertEqual(LmServiceProvisioningStatus.Succeeded, ensured.Status,
                    "The exact direct clone must become ready.");
                AssertEqual("esm-lm-controller-2", services.LastDefinition.ServiceName,
                    "SCM must receive the derived clone identity.");
                DirectControllerManifest manifest = manifests.Read(item.KktSerial);
                AssertEqual(DirectControllerLifecycleState.Ready, manifest.State,
                    "Only a ready clone may be projected as ready in inventory.");
                item.ExpectedManifestSha256 = manifests.ComputeFingerprint(manifest);
                services.DeleteVisibilityQueries = 2;

                LmServiceProvisioningItemResult removed = platform.Remove(
                    item,
                    Guid.NewGuid().ToString("N"),
                    "S-1-5-21-111-222-333-1001");

                AssertEqual(LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                    removed.Status,
                    "Controller cleanup must explicitly retain the ESM binding for caller cleanup.");
                AssertTrue(services.Query("esm-lm-controller-2") == null,
                    "The owned clone service must be absent after confirmed removal.");
                AssertTrue(manifests.Read(item.KktSerial) == null,
                    "The owned manifest must be removed only after SCM absence.");
            }
            finally
            {
                DeleteTestTreeWithReadOnlyFiles(root);
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
                    "ProgramFiles");

                AssertThrows<NotSupportedException>(delegate {
                    new OfficialLmProfileAdapter(
                        unsupported,
                        new ManagedServiceManifestStore(
                            root, new FakePathSafety(true), "S-1-5-21-111-222-333-1001"),
                        new AtomicFileWriter(),
                        new FakeWindowsServiceApi());
                }, "Неописанная схема профиля контроллера должна отвергаться до доступа к файлам.");
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
                ControllerCapabilityProfile.Supported(),
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
                ControllerCapabilityProfile.Supported(),
                VerifiedProvisionerBinary.CreateForTesting(
                    @"C:\Program Files\KRS\MultiKKT\Provisioner\EsmTspiot.ServiceProvisioner.exe"));
        }

        private static LmControllerChildProcess CreateTestChildProcess(
            IDictionary<string, string> environment,
            FakeControllerChildRuntime runtime)
        {
            return new LmControllerChildProcess(
                ControllerCapabilityProfile.Supported(),
                new VerifiedControllerBinary
                {
                    FullPath = @"C:\Program Files\ESP\LMController\bin\lmcontroller.exe",
                    Version = "1.6.4.0",
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

        private static LmServiceProvisioningBatchRequest CreateDirectControllerRequest(
            int count,
            LmServiceOperation operation)
        {
            LmServiceProvisioningBatchRequest request = new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 2,
                Operation = operation,
                OperationId = Guid.NewGuid().ToString("N"),
                InitiatingSid = "S-1-5-21-111-222-333-1001"
            };
            for (int index = 0; index < count; index++)
            {
                request.DirectControllers.Add(new DirectControllerProvisioningItemRequest
                {
                    KktSerial = (105700000001L + index).ToString("00000000000000"),
                    Inn = (1000000002L + (index * 1000000001L)).ToString("0000000000"),
                    Ordinal = index + 1,
                    TargetLocalModulePort =
                        DirectControllerIdentity.FutureLmPortForOrdinal(index + 1),
                    ExpectedManifestSha256 = operation ==
                        LmServiceOperation.EnsureDirectControllers
                        ? null
                        : new string((char)('a' + index), 64)
                });
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
                SignerSubject =
                    SupportedLocalModulePackageIdentity.SignerSubject,
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
                UpgradeCode = "{9449123B-61C4-40DE-AA6C-1BB9AA02EB67}",
                PackageCode = "{2308F9F9-DB33-44DA-819D-9A2D553E8309}"
            };
        }

        private static LocalModulePackageVerifier CreateTestLocalModulePackageVerifier(
            IWindowsInstallerPackageReader packageReader,
            IFileTrustVerifier trustVerifier,
            IPathSafety pathSafety)
        {
            LocalModuleMsiCapabilityProfile profile =
                LoadLocalModuleMsiCapabilityFixture();
            return new LocalModulePackageVerifier(
                packageReader,
                trustVerifier,
                pathSafety,
                new FakeLocalModuleMsiCapabilityResolver(profile),
                new FakeLocalModuleMsiProfileReader(
                    CreateLocalModuleMsiSnapshot(profile)));
        }

        private static TrustedFileExpectation CreateLocalModuleResolverTrust(
            LocalModuleMsiCapabilityProfile fixture)
        {
            return new TrustedFileExpectation
            {
                FileName = fixture.FileName,
                ByteLength = fixture.ByteLength,
                Sha256 = fixture.Sha256,
                FileVersion = string.Empty,
                ProductVersion = string.Empty,
                ProductName = string.Empty,
                CompanyName = string.Empty,
                Machine = PeMachine.Unknown,
                SignerSubject =
                    SupportedLocalModulePackageIdentity.SignerSubject,
                SignerThumbprint = fixture.SignerThumbprint,
                RequireCodeSigningEku = true
            };
        }

        private static void RenameSnapshotService(
            LocalModuleMsiDatabaseSnapshot snapshot,
            string source,
            string replacement)
        {
            for (int index = 0; index < snapshot.Rows.Count; index++)
            {
                MsiProfileRow row = snapshot.Rows[index];
                if (!string.Equals(row.Table, "CustomAction",
                        StringComparison.Ordinal))
                    continue;
                for (int column = 0; column < row.Columns.Count; column++)
                {
                    if (!string.Equals(row.Columns[column], "Target",
                            StringComparison.Ordinal))
                        continue;
                    row.Values[column] = (row.Values[column] ?? string.Empty)
                        .Replace("\"" + source + "\"",
                            "\"" + replacement + "\"");
                }
            }
        }

        private static void RemoveSnapshotRow(
            LocalModuleMsiDatabaseSnapshot snapshot,
            string table,
            string key)
        {
            for (int index = snapshot.Rows.Count - 1; index >= 0; index--)
            {
                MsiProfileRow row = snapshot.Rows[index];
                if (string.Equals(row.Table, table, StringComparison.Ordinal) &&
                    string.Equals(row.Key, key, StringComparison.Ordinal))
                    snapshot.Rows.RemoveAt(index);
            }
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
            CreateTestManagedLocalModuleOwnership(
                root,
                new FakePathSafety(true),
                null,
                out store,
                out runtime,
                out instance);
        }

        private static void CreateTestManagedLocalModuleOwnership(
            string root,
            IPathSafety pathSafety,
            string initiatingSid,
            out LocalModuleManifestStore store,
            out LocalModuleRuntimeManifest runtime,
            out LocalModuleInstanceManifest instance)
        {
            string appRoot = Path.Combine(root, "app");
            string runtimeContainer = Path.Combine(root, "program", "LocalModuleRuntime");
            store = new LocalModuleManifestStore(
                appRoot,
                runtimeContainer,
                pathSafety,
                initiatingSid);
            LocalModuleInstallerSelection package =
                MsiTestPackageFactory.SampleSelection(string.Empty);
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
                ExpectedImagePath = @"C:\Users\operator\Downloads\MultiKKT\MultiKKT.exe",
                ActualImagePath = @"C:\Users\operator\Downloads\MultiKKT\MultiKKT.exe",
                ExpectedProcessId = 0,
                ActualProcessId = 3100,
                HasExpectedMetadata = true,
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
                HasExpectedMetadata = source.HasExpectedMetadata,
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
                Console.WriteLine(ex.ToString());
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

        private static TException CaptureException<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException exception)
            {
                return exception;
            }
            throw new InvalidOperationException(
                "Expected exception was not thrown: " + typeof(TException).Name + ".");
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

        private sealed class FakeDirectControllerPlatform : IDirectControllerPlatform
        {
            internal FakeDirectControllerPlatform()
            {
                EnsuredSerials = new List<string>();
            }

            internal IList<string> EnsuredSerials { get; private set; }
            internal string FailSerial { get; set; }

            public LmServiceProvisioningItemResult Ensure(
                DirectControllerProvisioningItemRequest item,
                string operationId,
                string initiatingSid)
            {
                EnsuredSerials.Add(item.KktSerial);
                if (string.Equals(item.KktSerial, FailSerial, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("injected direct controller failure");
                }
                return Success(item, "Контроллер запущен.");
            }

            public LmServiceProvisioningItemResult Restart(
                DirectControllerProvisioningItemRequest item,
                string operationId,
                string initiatingSid)
            {
                return Success(item, "Контроллер перезапущен.");
            }

            public LmServiceProvisioningItemResult Remove(
                DirectControllerProvisioningItemRequest item,
                string operationId,
                string initiatingSid)
            {
                return Success(item, "Контроллер удалён.");
            }

            private static LmServiceProvisioningItemResult Success(
                DirectControllerProvisioningItemRequest item,
                string message)
            {
                return new LmServiceProvisioningItemResult
                {
                    KktSerial = item.KktSerial,
                    Status = LmServiceProvisioningStatus.Succeeded,
                    Message = message
                };
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

        private sealed class FakeWindowsInstallerNative :
            IWindowsInstallerNative
        {
            internal uint ReturnCode { get; set; }
            internal string PackagePath { get; private set; }
            internal string ProductCode { get; private set; }
            internal string CommandLine { get; private set; }
            internal int InstallLevel { get; private set; }
            internal int InstallState { get; private set; }
            internal List<int> UiTransitions { get; private set; }
            private int _currentUiLevel = 5;

            internal FakeWindowsInstallerNative()
            {
                UiTransitions = new List<int>();
            }

            public int SetInternalUi(int uiLevel)
            {
                int previous = _currentUiLevel;
                _currentUiLevel = uiLevel;
                UiTransitions.Add(uiLevel);
                return previous;
            }

            public uint InstallProduct(string packagePath, string commandLine)
            {
                PackagePath = packagePath;
                CommandLine = commandLine;
                return ReturnCode;
            }

            public uint ConfigureProduct(
                string productCode,
                int installLevel,
                int installState,
                string commandLine)
            {
                ProductCode = productCode;
                InstallLevel = installLevel;
                InstallState = installState;
                CommandLine = commandLine;
                return ReturnCode;
            }
        }

        private sealed class FakeInstalledLocalModuleRegistry :
            IInstalledLocalModuleRegistry
        {
            internal FakeInstalledLocalModuleRegistry()
            {
                Registry32 = new List<InstalledLocalModuleProduct>();
                Registry64 = new List<InstalledLocalModuleProduct>();
            }

            internal IList<InstalledLocalModuleProduct> Registry32 { get; private set; }
            internal IList<InstalledLocalModuleProduct> Registry64 { get; private set; }
            internal bool Read32 { get; private set; }
            internal bool Read64 { get; private set; }

            public IList<InstalledLocalModuleProduct> Read(
                Microsoft.Win32.RegistryView view)
            {
                if (view == Microsoft.Win32.RegistryView.Registry32)
                {
                    Read32 = true;
                    return Registry32;
                }
                Read64 = true;
                return Registry64;
            }
        }

        private sealed class FakePathSafety : IPathSafety
        {
            private readonly bool _isSafe;
            private readonly List<DirectoryProtectionCall> _directoryCalls;
            private readonly List<FileProtectionCall> _fileCalls;

            internal FakePathSafety(bool isSafe)
            {
                _isSafe = isSafe;
                _directoryCalls = new List<DirectoryProtectionCall>();
                _fileCalls = new List<FileProtectionCall>();
            }

            internal int ProtectionCallCount
            {
                get { return _directoryCalls.Count + _fileCalls.Count; }
            }

            internal bool HasReadOnlyDirectoryAccess(string path, string sid)
            {
                for (int index = 0; index < _directoryCalls.Count; index++)
                {
                    if (string.Equals(
                            Path.GetFullPath(_directoryCalls[index].Path),
                            Path.GetFullPath(path),
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            _directoryCalls[index].ReadOnlySid,
                            sid,
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                return false;
            }

            internal bool HasReadOnlyFileAccess(string path, string sid)
            {
                for (int index = 0; index < _fileCalls.Count; index++)
                {
                    if (!string.Equals(
                            Path.GetFullPath(_fileCalls[index].Path),
                            Path.GetFullPath(path),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    for (int sidIndex = 0;
                        sidIndex < _fileCalls[index].ReadOnlySids.Count;
                        sidIndex++)
                    {
                        if (string.Equals(
                            _fileCalls[index].ReadOnlySids[sidIndex],
                            sid,
                            StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            internal bool WasProtectedReadOnly(string path)
            {
                for (int index = 0; index < _fileCalls.Count; index++)
                {
                    if (string.Equals(
                            Path.GetFullPath(_fileCalls[index].Path),
                            Path.GetFullPath(path),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
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
                _directoryCalls.Add(new DirectoryProtectionCall
                {
                    Path = path,
                    Kind = kind,
                    ReadOnlySid = readOnlySid
                });
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
                _fileCalls.Add(new FileProtectionCall
                {
                    Path = path,
                    ReadOnlySids = readOnlySids == null
                        ? new List<string>()
                        : new List<string>(readOnlySids)
                });
            }

            private sealed class DirectoryProtectionCall
            {
                internal string Path { get; set; }
                internal ProtectedDirectoryKind Kind { get; set; }
                internal string ReadOnlySid { get; set; }
            }

            private sealed class FileProtectionCall
            {
                internal string Path { get; set; }
                internal IList<string> ReadOnlySids { get; set; }
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

        private const string ControllerSignerThumbprint =
            "1CD26372850FE30F1559821CF5D318591695271A";

        private static void RequireLifecycleServiceName(string serviceName)
        {
            if (!WindowsServiceApi.IsLifecycleServiceName(serviceName))
            {
                throw new ArgumentException(
                    "Managed service name is invalid.",
                    "serviceName");
            }
        }

        private static void RequireOwnedServiceName(string serviceName)
        {
            if (!WindowsServiceApi.IsOwnedServiceName(serviceName))
            {
                throw new ArgumentException(
                    "Managed service name is invalid.",
                    "serviceName");
            }
        }

        private sealed class FakeWindowsServiceApi : IWindowsServiceApi
        {
            private readonly Dictionary<string, WindowsServiceRecord> _records =
                new Dictionary<string, WindowsServiceRecord>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _deleteQueriesRemaining =
                new Dictionary<string, int>(StringComparer.Ordinal);

            internal FakeWindowsServiceApi()
            {
                Events = new List<string>();
            }

            internal WindowsServiceDefinition LastDefinition { get; private set; }
            internal int DeleteVisibilityQueries { get; set; }
            internal List<string> Events { get; private set; }

            internal void SetRecord(WindowsServiceRecord record)
            {
                _records[record.ServiceName] = record;
            }

            public WindowsServiceRecord Query(string serviceName)
            {
                RequireLifecycleServiceName(serviceName);
                WindowsServiceRecord record;
                int remaining;
                if (_deleteQueriesRemaining.TryGetValue(serviceName, out remaining))
                {
                    if (remaining <= 0)
                    {
                        _deleteQueriesRemaining.Remove(serviceName);
                        _records.Remove(serviceName);
                        return null;
                    }
                    _deleteQueriesRemaining[serviceName] = remaining - 1;
                }
                return _records.TryGetValue(serviceName, out record) ? record : null;
            }

            public void Create(WindowsServiceDefinition definition)
            {
                LastDefinition = definition;
                _records[definition.ServiceName] =
                    WindowsServiceRecord.FromDefinition(definition);
            }

            public void Update(WindowsServiceDefinition definition)
            {
                LastDefinition = definition;
                _records[definition.ServiceName] =
                    WindowsServiceRecord.FromDefinition(definition);
            }

            public void Start(string serviceName)
            {
                RequireLifecycleServiceName(serviceName);
                EnsureExact(serviceName);
                Events.Add("start:" + serviceName);
                _records[serviceName].State = WindowsServiceState.Running;
                if (_records[serviceName].ProcessId == 0)
                {
                    _records[serviceName].ProcessId = 1234;
                }
            }

            public void RequestStop(string serviceName)
            {
                RequireLifecycleServiceName(serviceName);
                EnsureExact(serviceName);
                Events.Add("stop:" + serviceName);
                _records[serviceName].State = WindowsServiceState.Stopped;
                _records[serviceName].ProcessId = 0;
            }

            public void SetStartMode(
                string serviceName,
                WindowsServiceStartMode startMode)
            {
                RequireOwnedServiceName(serviceName);
                EnsureExact(serviceName);
                Events.Add("startmode:" + serviceName + ":" + startMode);
                _records[serviceName].StartMode = startMode;
            }

            public void Delete(string serviceName)
            {
                RequireOwnedServiceName(serviceName);
                EnsureExact(serviceName);
                if (DeleteVisibilityQueries > 0)
                {
                    _deleteQueriesRemaining[serviceName] = DeleteVisibilityQueries;
                }
                else
                {
                    _records.Remove(serviceName);
                }
            }

            private void EnsureExact(string serviceName)
            {
                if (!_records.ContainsKey(serviceName))
                {
                    throw new InvalidOperationException("unexpected service identity");
                }
            }
        }

        private sealed class FakeDirectTcpListenerOwnerReader : ITcpListenerOwnerReader
        {
            private readonly Dictionary<int, IList<int>> _owners =
                new Dictionary<int, IList<int>>();

            internal void SetOwners(int port, params int[] processIds)
            {
                _owners[port] = new List<int>(processIds);
            }

            public IList<int> FindListenerProcessIds(int port)
            {
                IList<int> result;
                return _owners.TryGetValue(port, out result)
                    ? new List<int>(result)
                    : new List<int>();
            }
        }

        private sealed class FakeDirectControllerReadiness :
            IDirectControllerReadinessProbe
        {
            private readonly bool _ready;

            internal FakeDirectControllerReadiness(bool ready)
            {
                _ready = ready;
            }

            public LmReadinessResult WaitUntilReady(
                int ordinal,
                int timeoutMilliseconds)
            {
                return _ready
                    ? LmReadinessResult.Ready()
                    : LmReadinessResult.Failed("injected readiness failure");
            }

            public bool WaitUntilStopped(int ordinal, int timeoutMilliseconds)
            {
                return true;
            }
        }

        private sealed class FakeLegacyProcessSnapshotReader :
            ILegacyProcessSnapshotReader
        {
            internal FakeLegacyProcessSnapshotReader(LegacyProcessSnapshot current)
            {
                Current = current;
            }

            internal LegacyProcessSnapshot Current { get; set; }

            public LegacyProcessSnapshot Read(int processId)
            {
                return Current;
            }
        }

        private sealed class FakeLegacyNativeTerminator : ILegacyNativeTerminator
        {
            internal FakeLegacyNativeTerminator()
            {
                Terminated = new List<int>();
            }

            internal IList<int> Terminated { get; private set; }
            internal Action<int> OnTerminate { get; set; }

            public void TerminateAndWait(int processId, int timeoutMilliseconds)
            {
                Terminated.Add(processId);
                if (OnTerminate != null) OnTerminate(processId);
            }
        }

        private sealed class AlwaysCancelledProvisioning :
            ILmProvisioningCancellation
        {
            public bool IsCancellationRequested
            {
                get { return true; }
            }
        }

        private sealed class FakeLegacyManagedStateCleanup :
            ILegacyManagedStateCleanup
        {
            private readonly IList<string> _serials;

            internal FakeLegacyManagedStateCleanup(IList<string> serials)
            {
                _serials = new List<string>(serials);
                Attempts = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            internal IDictionary<string, int> Attempts { get; private set; }
            internal string CleanupPendingOnceFor { get; set; }

            public IList<string> ReadOwnedSerials()
            {
                return new List<string>(_serials);
            }

            public LmServiceProvisioningItemResult Cleanup(
                string kktSerial,
                string operationId,
                string initiatingSid)
            {
                int count;
                Attempts.TryGetValue(kktSerial, out count);
                count++;
                Attempts[kktSerial] = count;
                bool pending = string.Equals(
                        kktSerial,
                        CleanupPendingOnceFor,
                        StringComparison.Ordinal) && count == 1;
                return new LmServiceProvisioningItemResult
                {
                    KktSerial = kktSerial,
                    Status = pending
                        ? LmServiceProvisioningStatus.CleanupPending
                        : LmServiceProvisioningStatus.Succeeded,
                    Message = pending ? "retry" : "removed"
                };
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

        private sealed class FakeInstalledControllerProductVerifier :
            IInstalledControllerProductVerifier
        {
            private readonly bool _matches;

            internal FakeInstalledControllerProductVerifier(bool matches)
            {
                _matches = matches;
            }

            public InstalledControllerProductResult Verify(
                ControllerCapabilityProfile profile)
            {
                ValidationResult result = new ValidationResult();
                if (!_matches)
                {
                    result.Add("Official controller installed product does not match.");
                }
                return new InstalledControllerProductResult
                {
                    Validation = result,
                    DisplayVersion = _matches ? "1.6.4.0.375" : string.Empty
                };
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
                RequireLifecycleServiceName(serviceName);
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
                RequireLifecycleServiceName(serviceName);
                WindowsServiceRecord record = Require(serviceName);
                record.State = WindowsServiceState.Running;
                if (Events != null) Events.Add("start:" + serviceName);
            }

            public void RequestStop(string serviceName)
            {
                RequireLifecycleServiceName(serviceName);
                WindowsServiceRecord record = Require(serviceName);
                record.State = WindowsServiceState.Stopped;
                if (Events != null) Events.Add("stop:" + serviceName);
            }

            public void Delete(string serviceName)
            {
                RequireOwnedServiceName(serviceName);
                Require(serviceName);
                _records.Remove(serviceName);
            }

            public void SetStartMode(
                string serviceName,
                WindowsServiceStartMode startMode)
            {
                RequireOwnedServiceName(serviceName);
                Require(serviceName).StartMode = startMode;
                if (Events != null)
                    Events.Add("startmode:" + serviceName + ":" + startMode);
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
                    UpgradeCode = _metadata.UpgradeCode,
                    PackageCode = _metadata.PackageCode
                };
            }
        }

        private sealed class FakeProcessTreeReader : IProcessParentReader
        {
            private readonly Dictionary<int, int> _parents =
                new Dictionary<int, int>();

            internal void SetParent(int processId, int parentProcessId)
            {
                _parents[processId] = parentProcessId;
            }

            public int GetParentProcessId(int processId)
            {
                int parent;
                return _parents.TryGetValue(processId, out parent) ? parent : 0;
            }
        }

        private sealed class FakeLocalModuleMsiReadiness :
            ILocalModuleMsiReadinessProbe
        {
            private readonly IList<string> _events;

            internal FakeLocalModuleMsiReadiness(IList<string> events)
            {
                _events = events;
            }

            public void WaitUntilReady(
                LocalModuleInstalledLayout layout,
                LocalModuleProcessRole role)
            {
                _events.Add("ready:" + role.ToString());
            }

            public void WaitUntilStopped(
                LocalModuleInstalledLayout layout,
                LocalModuleProcessRole role)
            {
                _events.Add("stopped:" + role.ToString());
            }
        }

        private sealed class FakeWindowsFirewallApi : IWindowsFirewallApi
        {
            internal WindowsFirewallRuleRecord Rule { get; set; }
            internal int AddCount { get; private set; }
            internal int RemoveCount { get; private set; }

            public WindowsFirewallRuleRecord FindByName(string ruleName)
            {
                return Rule != null && string.Equals(
                    Rule.RuleName,
                    ruleName,
                    StringComparison.Ordinal) ? Rule.Clone() : null;
            }

            public void Add(LocalModuleFirewallRule rule)
            {
                AddCount++;
                Rule = WindowsFirewallRuleRecord.FromExpected(rule);
            }

            public void Remove(string ruleName)
            {
                if (Rule == null || !string.Equals(
                        Rule.RuleName,
                        ruleName,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "The fake firewall rule does not exist.");
                RemoveCount++;
                Rule = null;
            }
        }

        private sealed class FakeLocalModuleMsiLifecyclePlatform :
            ILocalModuleMsiProvisioningPlatform
        {
            private readonly Dictionary<string, FakeLocalModuleMsiState> _states =
                new Dictionary<string, FakeLocalModuleMsiState>(
                    StringComparer.Ordinal);

            internal FakeLocalModuleMsiLifecyclePlatform()
            {
                Events = new List<string>();
            }

            internal List<string> Events { get; private set; }
            internal string FailInstallAfterMutationForInn { get; set; }
            internal string FailInstallWithPartialServicesForInn { get; set; }
            internal string FailStartForInn { get; set; }
            internal string FailStopForInn { get; set; }
            internal string FailUninstallForInn { get; set; }
            internal string FailStartModeForInn { get; set; }

            internal FakeLocalModuleMsiState State(string inn)
            {
                FakeLocalModuleMsiState state;
                if (!_states.TryGetValue(inn, out state))
                {
                    state = new FakeLocalModuleMsiState
                    {
                        ProductMatches = true,
                        ConfigurationMatches = true,
                        ServicesMatch = true,
                        FirewallMatches = true
                    };
                    _states.Add(inn, state);
                }
                return state;
            }

            public LocalModuleMsiObservedState Observe(
                LocalModuleMsiProvisioningItemRequest request,
                LocalModuleMsiManifest manifest)
            {
                FakeLocalModuleMsiState state = State(request.Inn);
                string conflict = string.Empty;
                if (state.ProductPresent && !state.ProductMatches)
                    conflict = "Foreign ProductCode conflict.";
                else if (manifest != null && state.ProductPresent &&
                    (!state.ConfigurationMatches ||
                     (state.ServicesPresent && !state.ServicesMatch) ||
                     (state.FirewallPresent && !state.FirewallMatches)))
                    conflict = "Foreign service, port, or firewall conflict.";
                return new LocalModuleMsiObservedState
                {
                    ProductPresent = state.ProductPresent,
                    ProductMatches = state.ProductMatches,
                    ConfigurationMatches = state.ConfigurationMatches,
                    ServicesPresent = state.ServicesPresent,
                    ServicesMatch = state.ServicesMatch,
                    FirewallPresent = state.FirewallPresent,
                    FirewallMatches = state.FirewallMatches,
                    Running = state.Running,
                    Ready = state.Ready,
                    AutomaticStart = state.AutomaticStart,
                    ConflictMessage = conflict
                };
            }

            public LocalModuleMsiManifest PrepareInstall(
                LocalModuleMsiProvisioningItemRequest request,
                string ownershipNonce)
            {
                return CreateManifest(request, ownershipNonce, true, false);
            }

            public void Install(
                LocalModuleMsiProvisioningItemRequest request,
                LocalModuleMsiManifest manifest)
            {
                Events.Add("install:" + request.Inn);
                FakeLocalModuleMsiState state = State(request.Inn);
                state.ProductPresent = true;
                state.ProductMatches = true;
                state.Running = false;
                state.Ready = false;
                state.ServicesPresent = true;
                // AUTOSERVICE=1 leaves the vendor pair on automatic start.
                state.AutomaticStart = true;
                if (string.Equals(
                        FailInstallWithPartialServicesForInn,
                        request.Inn,
                        StringComparison.Ordinal))
                {
                    state.ProductPresent = false;
                    throw new InvalidOperationException(
                        "Simulated cancelled install with partial services.");
                }
                if (string.Equals(
                        FailInstallAfterMutationForInn,
                        request.Inn,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Simulated post-install failure.");
            }

            public LocalModuleMsiManifest AdoptPreExistingBase(
                LocalModuleMsiProvisioningItemRequest request,
                string ownershipNonce)
            {
                State(request.Inn).ServicesPresent = true;
                return CreateManifest(request, ownershipNonce, false, true);
            }

            public LocalModuleFirewallRule EnsureFirewall(
                LocalModuleMsiProvisioningItemRequest request,
                LocalModuleMsiManifest manifest)
            {
                Events.Add("firewall:" + request.Inn);
                State(request.Inn).FirewallPresent = true;
                State(request.Inn).FirewallMatches = true;
                return LocalModuleFirewallRule.Create(
                    manifest.OwnershipNonce,
                    Path.Combine(manifest.InstallRoot,
                        "erts-13.0.4", "bin", "erl.exe"),
                    request.ApiPort,
                    request.RemoteAddress);
            }

            public LocalModuleStartModeAdjustment EnsureAutomaticStart(
                LocalModuleMsiProvisioningItemRequest request,
                LocalModuleMsiManifest manifest)
            {
                Events.Add("startmode:" + request.Inn);
                if (string.Equals(
                        FailStartModeForInn,
                        request.Inn,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Simulated start-mode failure.");
                FakeLocalModuleMsiState state = State(request.Inn);
                WindowsServiceStartMode previous = state.AutomaticStart
                    ? WindowsServiceStartMode.AutoStart
                    : WindowsServiceStartMode.DemandStart;
                state.AutomaticStart = true;
                return new LocalModuleStartModeAdjustment
                {
                    PreviousApiStartMode = previous,
                    PreviousDatabaseStartMode = previous
                };
            }

            public void RestoreStartMode(LocalModuleMsiManifest manifest)
            {
                Events.Add("restore-startmode:" + manifest.Inn);
                State(manifest.Inn).AutomaticStart =
                    manifest.PreviousApiStartMode ==
                        (int)WindowsServiceStartMode.AutoStart &&
                    manifest.PreviousDatabaseStartMode ==
                        (int)WindowsServiceStartMode.AutoStart;
            }

            public void StartAndVerify(
                LocalModuleMsiProvisioningItemRequest request,
                LocalModuleMsiManifest manifest)
            {
                Events.Add("start:" + request.Inn);
                if (string.Equals(
                        FailStartForInn,
                        request.Inn,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("Simulated start failure.");
                State(request.Inn).Running = true;
                State(request.Inn).Ready = true;
            }

            public void StopAndVerify(
                LocalModuleMsiProvisioningItemRequest request,
                LocalModuleMsiManifest manifest)
            {
                Events.Add("stop:" + request.Inn);
                if (string.Equals(
                        FailStopForInn,
                        request.Inn,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("Simulated stop failure.");
                State(request.Inn).Running = false;
                State(request.Inn).Ready = false;
            }

            public void RemoveFirewall(LocalModuleMsiManifest manifest)
            {
                Events.Add("remove-firewall:" + manifest.Inn);
                State(manifest.Inn).FirewallPresent = false;
                State(manifest.Inn).FirewallMatches = false;
            }

            public void Uninstall(LocalModuleMsiManifest manifest)
            {
                FakeLocalModuleMsiState state = State(manifest.Inn);
                Events.Add(
                    (state.ProductPresent ? "uninstall:" : "cleanup-residue:") +
                    manifest.Inn);
                if (string.Equals(
                        FailUninstallForInn,
                        manifest.Inn,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Simulated uninstall failure.");
                state.ProductPresent = false;
                state.ServicesPresent = false;
                state.Running = false;
                state.Ready = false;
                state.AutomaticStart = false;
            }

            public void WaitForEpmdExit()
            {
                Events.Add("epmd");
            }

            private static LocalModuleMsiManifest CreateManifest(
                LocalModuleMsiProvisioningItemRequest request,
                string ownershipNonce,
                bool installedByApplication,
                bool preExisting)
            {
                string root = request.CloneOrdinal == 0
                    ? Path.Combine(
                        request.InstallVolumeRoot,
                        "Program Files",
                        "Regime")
                    : LocalModuleInstallRootPolicy.BuildCloneInstallDirectory(
                        request.InstallVolumeRoot,
                        request.CloneOrdinal);
                return LocalModuleMsiManifest.Create(
                    request.Inn,
                    request.CloneOrdinal,
                    request.ApiPort,
                    request.DatabasePort,
                    "{10000000-0000-0000-0000-" +
                        request.CloneOrdinal.ToString("000000000000") + "}",
                    "{20000000-0000-0000-0000-" +
                        request.CloneOrdinal.ToString("000000000000") + "}",
                    "2.6.1",
                    root,
                    installedByApplication,
                    preExisting,
                    ownershipNonce);
            }
        }

        private sealed class FakeLocalModuleMsiState
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
        }

        private sealed class FakeLocalModuleMsiRepository :
            ILocalModuleMsiManifestRepository
        {
            private readonly Dictionary<string, LocalModuleMsiManifest> _items =
                new Dictionary<string, LocalModuleMsiManifest>(
                    StringComparer.Ordinal);

            internal string FailDeleteOnceForInn { get; set; }

            public LocalModuleMsiManifest Read(string inn)
            {
                LocalModuleMsiManifest value;
                return _items.TryGetValue(inn, out value) ? value : null;
            }

            public IList<LocalModuleMsiManifest> ReadAll()
            {
                return new List<LocalModuleMsiManifest>(_items.Values);
            }

            public void Write(LocalModuleMsiManifest manifest)
            {
                LocalModuleMsiManifest.Validate(manifest);
                _items[manifest.Inn] = manifest;
            }

            public void Delete(string inn, string expectedManifestSha256)
            {
                if (string.Equals(FailDeleteOnceForInn, inn,
                        StringComparison.Ordinal))
                {
                    FailDeleteOnceForInn = null;
                    throw new IOException(
                        "Simulated crash boundary before manifest deletion.");
                }
                LocalModuleMsiManifest value = Read(inn);
                if (value == null || !string.Equals(
                        value.ManifestSha256,
                        expectedManifestSha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Fake manifest fingerprint mismatch.");
                _items.Remove(inn);
            }
        }

        private sealed class FakeLocalModuleMsiJournalStore :
            ILocalModuleMsiLifecycleJournalStore
        {
            private readonly Dictionary<string, LocalModuleMsiLifecycleJournal>
                _items = new Dictionary<string, LocalModuleMsiLifecycleJournal>(
                    StringComparer.Ordinal);

            internal bool Contains(string inn)
            {
                return _items.ContainsKey(inn);
            }

            public LocalModuleMsiLifecycleJournal Read(string inn)
            {
                LocalModuleMsiLifecycleJournal value;
                return _items.TryGetValue(inn, out value) ? value : null;
            }

            public void Write(LocalModuleMsiLifecycleJournal journal)
            {
                _items[journal.Inn] = journal;
            }

            public void Delete(
                string inn,
                string operationId,
                string ownershipNonce)
            {
                LocalModuleMsiLifecycleJournal value = Read(inn);
                if (value != null &&
                    (!string.Equals(value.OperationId, operationId,
                        StringComparison.Ordinal) ||
                     !string.Equals(value.OwnershipNonce, ownershipNonce,
                        StringComparison.Ordinal)))
                    throw new InvalidDataException(
                        "Fake lifecycle journal ownership mismatch.");
                _items.Remove(inn);
            }
        }

        private sealed class FakeLocalModuleMsiCapabilityResolver :
            ILocalModuleMsiCapabilityResolver
        {
            private readonly LocalModuleMsiCapabilityProfile _profile;

            internal FakeLocalModuleMsiCapabilityResolver(
                LocalModuleMsiCapabilityProfile profile)
            {
                _profile = profile;
            }

            public LocalModuleMsiCapabilityProfile Resolve(
                WindowsInstallerPackageMetadata metadata,
                TrustedFileExpectation trust,
                LocalModuleMsiDatabaseSnapshot snapshot)
            {
                return _profile;
            }
        }

        private sealed class FakeLocalModuleMsiProfileReader :
            ILocalModuleMsiProfileReader
        {
            private readonly LocalModuleMsiDatabaseSnapshot _snapshot;

            internal FakeLocalModuleMsiProfileReader(
                LocalModuleMsiDatabaseSnapshot snapshot)
            {
                _snapshot = snapshot;
            }

            internal int ReadCount { get; private set; }

            public LocalModuleMsiDatabaseSnapshot Read(string lockedMsiPath)
            {
                ReadCount++;
                return _snapshot;
            }
        }
    }
}
