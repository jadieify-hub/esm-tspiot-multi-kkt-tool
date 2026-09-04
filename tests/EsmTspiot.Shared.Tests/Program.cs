using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;
#if !NETFRAMEWORK
using EsmTspiot.WinForms.Shared;
#endif

namespace EsmTspiot.Shared.Tests
{
    internal static class Program
    {
        private static int _failures;

        private static int Main()
        {
            Run("Valid input passes validation", ValidInputPassesValidation);
            Run("Serials must be digits", SerialsMustBeDigits);
            Run("KKT serial must have expected length", KktSerialMustHaveExpectedLength);
            Run("FN serial must have expected length", FnSerialMustHaveExpectedLength);
            Run("Inn length must be 10 or 12", InnLengthMustBeTenOrTwelve);
            Run("Inn control digits must be valid", InnControlDigitsMustBeValid);
            Run("Inn 12 control digits must be valid", Inn12ControlDigitsMustBeValid);
            Run("Unexpected serial prefixes produce warnings", UnexpectedSerialPrefixesProduceWarnings);
            Run("Unknown Atol model code produces warning", UnknownAtolModelCodeProducesWarning);
            Run("Optional entered fields are validated before check", OptionalEnteredFieldsAreValidatedBeforeCheck);
            Run("Optional registration fields are validated before post", OptionalRegistrationFieldsAreValidatedBeforePost);
            Run("Ports must be valid and distinct", PortsMustBeValidAndDistinct);
            Run("Service ports must differ from dkktPort", ServicePortsMustDifferFromDkktPort);
            Run("Known errors are decoded", KnownErrorsAreDecoded);
            Run("Error 1012 is decoded as manual service recovery", Error1012IsDecodedAsManualServiceRecovery);
            Run("Error 1013 is decoded as manual service recovery", Error1013IsDecodedAsManualServiceRecovery);
            Run("Error 1026 explains multiple INN limitation", Error1026ExplainsMultipleInnLimitation);
            Run("Unknown HTTP error shows the service response body", UnknownHttpErrorShowsServiceResponseBody);
            Run("LM binding waits until ESM reports the binding", LmBindingWorkflowWaitsUntilEsmReportsTheBinding);
            Run("LM binding diagnostics carry the info response", LmBindingDiagnosticsCarryTheInfoResponse);
            Run("Service recovery command uses KKT serial and ports", ServiceRecoveryCommandUsesKktSerialAndPorts);
            Run("Service recovery command recreates service with wrong ports", ServiceRecoveryCommandRecreatesServiceWithWrongPorts);
            Run("Service recovery command writes diagnostics", ServiceRecoveryCommandWritesDiagnostics);
            Run("Service recovery command escapes PowerShell substitution", ServiceRecoveryCommandEscapesPowerShellSubstitution);
            Run("Service recovery detection ignores codes inside serials", ServiceRecoveryDetectionIgnoresCodesInsideSerials);
            Run("Error code detection requires a numeric boundary", ErrorCodeDetectionRequiresNumericBoundary);
            Run("Service recovery command rejects invalid ports", ServiceRecoveryCommandRejectsInvalidPorts);
            Run("Forbidden is decoded", ForbiddenIsDecoded);
            Run("Instance parser reads nested instances", InstanceParserReadsNestedInstances);
            Run("Instance parser reads root arrays", InstanceParserReadsRootArrays);
            Run("Instance response accepts empty 204 as no instances", InstanceResponseAcceptsEmpty204AsNoInstances);
            Run("Dkkt parser reads kkt array", DkktParserReadsKktArray);
            Run("Dkkt response accepts only an empty 204", DkktResponseAcceptsOnlyEmpty204);
            Run("Automatic registration keeps the one-INN flow", AutomaticRegistrationKeepsOneInnFlow);
            Run("Automatic registration keeps fully registered multi-INN flow", AutomaticRegistrationKeepsRegisteredMultiInnFlow);
            Run("Automatic registration selects VCOM for pending multi-INN flow", AutomaticRegistrationSelectsVcomForPendingMultiInnFlow);
            Run("Automatic registration selects VCOM when no live sessions exist", AutomaticRegistrationSelectsVcomWithoutLiveSessions);
            Run("Dkkt selector excludes already created instances", DkktSelectorExcludesAlreadyCreatedInstances);
            Run("Port allocator selects pair after existing instances", PortAllocatorSelectsPairAfterExistingInstances);
            Run("Port allocator reserves either side and consecutive selections", PortAllocatorReservesEitherSideAndConsecutiveSelections);
            Run("Port allocator starts with the primary KKT pair", PortAllocatorStartsWithPrimaryKktPair);
            Run("Bulk planner assigns first sequential port pairs", BulkPlannerAssignsFirstSequentialPortPairs);
            Run("Bulk planner uses the orchestrator DKKT port by default", BulkPlannerUsesOrchestratorDkktPortByDefault);
            Run("Bulk planner reserves implicit first soft port", BulkPlannerReservesImplicitFirstSoftPort);
            Run("Bulk planner skips occupied pair indexes", BulkPlannerSkipsOccupiedPairIndexes);
            Run("Bulk planner rejects exhausted port pairs", BulkPlannerRejectsExhaustedPortPairs);
            Run("Bulk planner excludes existing devices", BulkPlannerExcludesExistingDevices);
            Run("Bulk planner marks invalid device data", BulkPlannerMarksInvalidDeviceData);
            Run("Bulk planner does not reserve ports for invalid devices", BulkPlannerDoesNotReservePortsForInvalidDevices);
            Run("Bulk planner skips duplicate devices", BulkPlannerSkipsDuplicateDevices);
            Run("Bulk registration summary counts every status", BulkRegistrationSummaryCountsEveryStatus);
            Run("Bulk registration result formats log line", BulkRegistrationResultFormatsLogLine);
            Run("Bulk settings validation ignores single KKT fields", BulkSettingsValidationIgnoresSingleKktFields);
            Run("Bulk parsers distinguish empty data from invalid JSON", BulkParsersDistinguishEmptyDataFromInvalidJson);
            Run("Strict parsers reject unexpected JSON shape", StrictParsersRejectUnexpectedJsonShape);
            Run("Instance details parser reads registration data", InstanceDetailsParserReadsRegistrationData);
            Run("Instance details parser rejects malformed registration data", InstanceDetailsParserRejectsMalformedRegistrationData);
            Run("API client uses documented HTTP contracts", ApiClientUsesDocumentedHttpContracts);
            Run("LM gateway API uses documented PUT contract", LmGatewayApiUsesDocumentedPutContract);
            Run("LM gateway API escapes instance id", LmGatewayApiEscapesInstanceId);
            Run("LM gateway API response stores redacted request", LmGatewayApiResponseStoresRedactedRequest);
            Run("LM info API uses the current documented endpoint", LmInfoApiUsesCurrentDocumentedEndpoint);
            Run("LM info API classifies TLS certificate failure", LmInfoApiClassifiesTlsCertificateFailure);
            Run("LM readback explains a rejected TLS certificate", LmReadbackExplainsRejectedTlsCertificate);
            Run("LM readback labels controller errors without implying health", LmReadbackLabelsControllerErrorsWithoutImplyingHealth);
            Run("LM info parser exposes only safe readback fields", LmInfoParserExposesOnlySafeReadbackFields);
            Run("LM discovery returns registered KKT with INN", LmDiscoveryReturnsRegisteredKktWithInn);
            Run("LM discovery aborts on malformed instance list", LmDiscoveryAbortsOnMalformedInstanceList);
            Run("LM discovery continues after one malformed detail", LmDiscoveryContinuesAfterOneMalformedDetail);
            Run("LM discovery excludes unregistered instance", LmDiscoveryExcludesUnregisteredInstance);
            Run("LM discovery honors cancellation", LmDiscoveryHonorsCancellation);
            Run("LM binding planner matches by KKT identity", LmBindingPlannerMatchesByKktIdentity);
            Run("LM binding planner keeps different INNs separate", LmBindingPlannerKeepsDifferentInnsSeparate);
            Run("LM binding planner requires one input per KKT", LmBindingPlannerRequiresOneInputPerKkt);
            Run("LM binding planner rejects duplicate local endpoints", LmBindingPlannerRejectsDuplicateLocalEndpoints);
            Run("LM binding planner rejects invalid loopback and port", LmBindingPlannerRejectsInvalidLoopbackAndPort);
            Run("LM binding plan cannot contain credentials", LmBindingPlanCannotContainCredentials);
            Run("LM service identity is deterministic and independent", LmServiceIdentityIsDeterministicAndIndependent);
            Run("LM service identity rejects unsafe serial", LmServiceIdentityRejectsUnsafeSerial);
            Run("Direct controller planner assigns official base then clones", DirectControllerPlannerAssignsOfficialBaseThenClones);
            Run("Direct controller planner preserves stable saved ordinal", DirectControllerPlannerPreservesStableSavedOrdinal);
            Run("Direct controller planner skips foreign names and ports", DirectControllerPlannerSkipsForeignNamesAndPorts);
            Run("Direct controller planner reports ordinal exhaustion", DirectControllerPlannerReportsOrdinalExhaustion);
            Run("Direct controller planner tolerates an absent saved KKT", DirectControllerPlannerToleratesAbsentSavedKkt);
            Run("Direct controller planner never shares one LM port between INNs", DirectControllerPlannerNeverSharesLocalModulePort);
            Run("MSI local module planner groups KKT by INN", MsiLocalModulePlannerGroupsKktByInn);
            Run("MSI local module planner uses actual base and clone ports", MsiLocalModulePlannerUsesActualBaseAndClonePorts);
            Run("MSI local module planner preserves saved ordinals", MsiLocalModulePlannerPreservesSavedOrdinals);
            Run("MSI local module root policy accepts only fixed Program Files volumes", MsiLocalModuleRootPolicyAcceptsOnlyFixedProgramFilesVolumes);
            Run("MSI local module planner reserves ordinals and names port owners", MsiLocalModulePlannerReservesOrdinalsAndNamesPortOwners);
            Run("MSI local module package policy pins the vendor, not the version", MsiLocalModulePackagePolicyPinsVendorNotVersion);
            Run("ESM instance service identity matches the vendor naming", EsmInstanceServiceIdentityMatchesVendorNaming);
            Run("MSI local module disk budget reports both volumes", MsiLocalModuleDiskBudgetReportsBothVolumes);
#if !NETFRAMEWORK
            Run("MSI local module operator inventory restores stable assignments", MsiLocalModuleOperatorInventoryRestoresStableAssignments);
#endif
            Run("MSI local module removal wording preserves vendor base", MsiLocalModuleRemovalWordingPreservesVendorBase);
            Run("Direct controller setup defers LM readiness without failing controllers", DirectControllerSetupDefersLmReadinessWithoutFailingControllers);
            Run("Direct controller binding separates controller and LM ports", DirectControllerBindingSeparatesControllerAndLmPorts);
            Run("LM gateway planner never adopts official base service", LmGatewayPlannerNeverAdoptsOfficialBaseService);
            Run("LM gateway planner allocates sequential local ports", LmGatewayPlannerAllocatesSequentialLocalPorts);
            Run("LM gateway planner keeps owned and skips foreign listener", LmGatewayPlannerKeepsOwnedAndSkipsForeignListener);
            Run("LM listener snapshot projects only ready managed owners", LmListenerSnapshotProjectsOnlyReadyManagedOwners);
            Run("LM gateway planner preserves matching managed assignment", LmGatewayPlannerPreservesMatchingManagedAssignment);
            Run("LM gateway planner rejects unsafe target and all port conflicts", LmGatewayPlannerRejectsUnsafeTargetAndAllPortConflicts);
            Run("Managed LM service spec contains no credentials", ManagedLmServiceSpecContainsNoCredentials);
            Run("Managed LM planner groups KKT by INN", ManagedLmPlannerGroupsKktByInn);
            Run("Managed LM planner assigns stable ordinals", ManagedLmPlannerAssignsStableOrdinals);
            Run("Managed LM planner blocks occupied deterministic ports", ManagedLmPlannerBlocksOccupiedPorts);
            Run("Managed LM port preflight ignores owned ports and blocks new conflicts", ManagedLmPortPreflightIgnoresOwnedPortsAndBlocksNewConflicts);
            Run("Managed LM request repeats one endpoint for shared INN", ManagedLmRequestRepeatsOneEndpointForSharedInn);
            Run("LM gateway defaults use the fixed 45000 gRPC pool", LmDefaultsUse45000GrpcPool);
            Run("LM gateway draft defaults follow the KKT ordinal", LmGatewayDraftDefaultsFollowKktOrdinal);
            Run("LM gateway draft defaults do not crash on excess KKT", LmGatewayDraftDefaultsDoNotCrashOnExcessKkt);
            Run("LM gateway draft settings persist only the matching nonsecret endpoint", LmGatewayDraftSettingsPersistOnlyMatchingNonsecretEndpoint);
            Run("Operator package paths persist without package data or secrets", OperatorPackagePathsPersistWithoutPackageDataOrSecrets);
            Run("LM inventory display separates the official controller from KKT services", LmInventoryDisplaySeparatesOfficialControllerFromKktServices);
            Run("LM binding session does not invent readback", LmBindingSessionDoesNotInventReadback);
            Run("LM binding session applies verified readback", LmBindingSessionAppliesVerifiedReadback);
            Run("LM binding fallback preserves newer readback", LmBindingFallbackPreservesNewerReadback);
            Run("LM binding session orders KKT for stable ordinals", LmBindingSessionOrdersKktForStableOrdinals);
            Run("LM binding session preserves current drafts on refresh", LmBindingSessionPreservesCurrentDraftsOnRefresh);
            Run("LM binding session discards a draft after INN changes", LmBindingSessionDiscardsDraftAfterInnChanges);
            Run("LM binding session builds a plan only for selected KKT", LmBindingSessionBuildsPlanOnlyForSelectedKkt);
            Run("LM binding session builds an exact one-KKT plan", LmBindingSessionBuildsExactOneKktPlan);
            Run("LM binding session ignores an unknown exact KKT", LmBindingSessionIgnoresUnknownExactKkt);
            Run("LM binding session can select all KKT for full automatic setup", LmBindingSessionCanSelectAllForAutomaticSetup);
            Run("LM binding session masks outcome details", LmBindingSessionMasksOutcomeDetails);
            Run("LM binding workflow sends items sequentially", LmBindingWorkflowSendsItemsSequentially);
            Run("LM binding workflow verifies accepted settings through info", LmBindingWorkflowVerifiesAcceptedSettingsThroughInfo);
            Run("LM binding workflow keeps accepted when info is unavailable", LmBindingWorkflowKeepsAcceptedWhenInfoIsUnavailable);
            Run("LM binding workflow flags an info mismatch", LmBindingWorkflowFlagsInfoMismatch);
            Run("LM binding workflow skips invalid item and continues", LmBindingWorkflowSkipsInvalidItemAndContinues);
            Run("LM binding workflow marks lost response for attention", LmBindingWorkflowMarksLostResponseForAttention);
            Run("LM binding workflow does not retry permanent HTTP error", LmBindingWorkflowDoesNotRetryPermanentHttpError);
            Run("LM binding workflow preserves partial results on cancellation", LmBindingWorkflowPreservesPartialResultsOnCancellation);
            Run("LM binding workflow progress never contains password", LmBindingWorkflowProgressNeverContainsPassword);
            Run("API client preserves an injected timeout", ApiClientPreservesInjectedTimeout);
            Run("Canonical hasher validates SHA-256 syntax", CanonicalHasherValidatesSha256Syntax);
            Run("Instruction selector uses the newest file time", InstructionSelectorUsesNewestFileTime);
            Run("Instruction selector falls back to the field guide", InstructionSelectorFallsBackToFieldGuide);
            Run("KKT service states are localized for operators", KktServiceStatesAreLocalizedForOperators);
            Run("Unknown KKT service state remains diagnosable", UnknownKktServiceStateRemainsDiagnosable);
            Run("Deletion planner allows primary KKT with stronger confirmation", DeletionPlannerAllowsPrimaryKktWithStrongerConfirmation);
            Run("Deletion planner blocks ambiguous primary KKT", DeletionPlannerBlocksAmbiguousPrimaryKkt);
            Run("Deletion confirmation requires last four digits", DeletionConfirmationRequiresLastFourDigits);
            Run("Deletion confirmation supports exact full serial", DeletionConfirmationSupportsExactFullSerial);
            Run("Deletion workflow requires full serial for primary KKT", DeletionWorkflowRequiresFullSerialForPrimaryKkt);
            Run("Deletion workflow verifies additional KKT removal", DeletionWorkflowVerifiesAdditionalKktRemoval);
            Run("Deletion workflow reports unverified removal", DeletionWorkflowReportsUnverifiedRemoval);
            Run("Remote base URL produces warning", RemoteBaseUrlProducesWarning);
            Run("Base URL rejects credentials and query", BaseUrlRejectsCredentialsAndQuery);
            Run("Unicode digits are rejected", UnicodeDigitsAreRejected);
            Run("Bulk workflow treats empty 204 as no instances", BulkWorkflowTreatsEmpty204AsNoInstances);
            Run("Bulk workflow plans supplied sequential VCOM devices", BulkWorkflowPlansSuppliedSequentialVcomDevices);
            Run("Bulk workflow skips PUT after POST completes registration", BulkWorkflowSkipsPutAfterPostCompletesRegistration);
            Run("Bulk workflow does not skip PUT for an unregistered state", BulkWorkflowDoesNotSkipPutForUnregisteredState);
            Run("Bulk workflow blocks mismatched registration after POST", BulkWorkflowBlocksMismatchedRegistrationAfterPost);
            Run("Bulk workflow resumes incomplete existing instance", BulkWorkflowResumesIncompleteExistingInstance);
            Run("Bulk workflow continues after add failure", BulkWorkflowContinuesAfterAddFailure);
            Run("Bulk workflow retries service not started", BulkWorkflowRetriesServiceNotStarted);
            Run("Bulk workflow retries connection failure during add", BulkWorkflowRetriesConnectionFailureDuringAdd);
            Run("Bulk workflow retries connection failure during registration", BulkWorkflowRetriesConnectionFailureDuringRegistration);
            Run("Bulk workflow waits for registered state after PUT", BulkWorkflowWaitsForRegisteredStateAfterPut);
            Run("Bulk workflow rejects mismatched readback after PUT", BulkWorkflowRejectsMismatchedReadbackAfterPut);
            Run("Bulk workflow times out an unconfirmed PUT", BulkWorkflowTimesOutUnconfirmedPut);
            Run("Bulk workflow executes one selected KKT", BulkWorkflowExecutesOneSelectedKkt);
            Run("Bulk workflow recovers when retry reports existing instance", BulkWorkflowRecoversWhenRetryReportsExistingInstance);
            Run("Bulk workflow skips PUT when readiness is not confirmed", BulkWorkflowSkipsPutWhenReadinessIsNotConfirmed);
            Run("Bulk workflow honors cancellation", BulkWorkflowHonorsCancellation);
            Run("Bulk workflow preserves results when request is cancelled", BulkWorkflowPreservesResultsWhenRequestIsCancelled);
            Run("Sequential discovery rejects externally held KKT sessions", SequentialDiscoveryRejectsExternalSessions);
            Run("Sequential discovery maps each VCOM through the real dkktList", SequentialDiscoveryMapsEachVcom);
            Run("Sequential registration disposes its VCOM before returning", SequentialRegistrationDisposesVcomBeforeReturning);
            Run("Sequential cancellation keeps the completed KKT", SequentialCancellationKeepsCompletedKkt);
            Run("Sequential final verification closes every VCOM on failure", SequentialFinalVerificationClosesEveryVcomOnFailure);
            Run("Diagnostic masker hides fiscal identifiers", DiagnosticMaskerHidesFiscalIdentifiers);
            Run("Diagnostic masker hides local user paths", DiagnosticMaskerHidesLocalUserPaths);
            Run("Diagnostic masker hides common secrets", DiagnosticMaskerHidesCommonSecrets);
            Run("Sensitive masker redacts JSON credentials", SensitiveMaskerRedactsJsonCredentials);
            Run("Sensitive masker redacts derived token fields", SensitiveMaskerRedactsDerivedTokenFields);
            Run("Sensitive masker redacts LM info pass", SensitiveMaskerRedactsLmInfoPass);
            Run("Sensitive masker redacts key value credentials", SensitiveMaskerRedactsKeyValueCredentials);
            Run("Sensitive masker redacts YAML scalar and block credentials", SensitiveMaskerRedactsYamlScalarAndBlockCredentials);
            Run("Log formatter never persists reflected password", LogFormatterNeverPersistsReflectedPassword);
            Run("Sensitive masker preserves ordinary fields", SensitiveMaskerPreservesOrdinaryFields);
            Run("Display log trimmer preserves the newest half", DisplayLogTrimmerPreservesNewestHalf);
            Run("File log sink persists text without blocking", FileLogSinkPersistsTextWithoutBlocking);
#if !NETFRAMEWORK
            Run("Provisioner closure list parses names and hashes", ProvisionerClosureListParsesNamesAndHashes);
            Run("Provisioner launcher names the missing helper file without location rules", ProvisionerLauncherNamesMissingHelperFileWithoutLocationRules);
            Run("Provisioner accepts full administrator without split UAC", ProvisionerAcceptsFullAdministratorWithoutSplitUac);
            Run("Provisioner can inspect the current administrative token", ProvisionerCanInspectCurrentAdministrativeToken);
            Run("Provisioner client creates a supported protected pipe", ProvisionerClientCreatesSupportedProtectedPipe);
            Run("Provisioner client explains premature helper exit", ProvisionerClientExplainsPrematureHelperExit);
            Run("MSI provisioner launch verifies helper before process start", MsiProvisionerLaunchVerifiesHelperBeforeProcessStart);
#endif
            Run("LM provisioner contract exposes no arbitrary command", LmProvisionerContractExposesNoArbitraryCommand);
            Run("LM probe result separates service and listener state", LmProbeResultSeparatesServiceAndListenerState);
            Run("LM provisioning progress contains no credentials", LmProvisioningProgressContainsNoCredentials);
            Run("LM provisioning result formats every item for the operator log", LmProvisioningResultFormatsEveryItemForOperatorLog);
            Run("Automatic mode registers KKT before LM setup", AutomaticModeRegistersKktBeforeLmSetup);
            Run("Automatic mode skips LM setup after registration failure", AutomaticModeSkipsLmSetupAfterRegistrationFailure);
            Run("LM automatic setup installs before configuring KKT", LmAutomaticSetupInstallsBeforeConfiguringKkt);
            Run("LM automatic setup stops after failed installation", LmAutomaticSetupStopsAfterFailedInstallation);
#if !NETFRAMEWORK
            Run("Full automatic setup keeps LM initialization deferred", FullAutomaticSetupKeepsLmInitializationDeferred);
            Run("Full automatic setup does not stop after LM failure", FullAutomaticSetupDoesNotStopAfterLmFailure);
            Run("Full automatic setup reads back ESM after binding", FullAutomaticSetupReadsBackEsmAfterBinding);
            Run("LM contour read-back policy classifies ESM observations", LmContourReadbackPolicyClassifiesEsmObservations);
#endif
            Run("LM automatic setup reports incomplete controller configuration", LmAutomaticSetupReportsIncompleteControllerConfiguration);
            Run("LM lifecycle ensures probes then binds", LmLifecycleEnsuresProbesThenBinds);
            Run("LM lifecycle completion requires verified readback", LmLifecycleCompletionRequiresVerifiedReadback);
            Run("LM lifecycle never binds failed service", LmLifecycleNeverBindsFailedService);
            Run("LM lifecycle continues after one KKT failure", LmLifecycleContinuesAfterOneKktFailure);
            Run("LM lifecycle retries binding without reprovisioning", LmLifecycleRetriesBindingWithoutReprovisioning);
            Run("LM lifecycle preserves partial outcome on cancellation", LmLifecyclePreservesPartialOutcomeOnCancellation);
            Run("LM lifecycle reconciles unknown result before mutation", LmLifecycleReconcilesUnknownResultBeforeMutation);
            Run("LM removal workflow removes one selected managed service", LmRemovalWorkflowRemovesOneSelectedManagedService);
            Run("LM removal workflow accepts managed stack fingerprint", LmRemovalWorkflowAcceptsManagedStackFingerprint);
            Run("LM cleanup workflow locks the displayed managed stack fingerprint", LmCleanupWorkflowLocksDisplayedManagedStackFingerprint);
            Run("LM removal workflow blocks batch and official removal", LmRemovalWorkflowBlocksBatchAndOfficialRemoval);
            Run("LM removal outcome warns that ESM binding remains", LmRemovalOutcomeWarnsThatEsmBindingRemains);

            if (_failures == 0)
            {
                Console.WriteLine("All shared tests passed.");
                return 0;
            }

            Console.WriteLine(_failures.ToString() + " shared test(s) failed.");
            return 1;
        }

        private static void ValidInputPassesValidation()
        {
            ValidationResult result = TspiotInputValidator.ValidatePost(CreateValidInput());
            AssertTrue(result.IsValid, "Expected valid input.");
        }

        private static void SerialsMustBeDigits()
        {
            TspiotFormInput input = CreateValidInput();
            input.KktSerial = "012ABC";

            ValidationResult result = TspiotInputValidator.ValidatePut(input, true);

            AssertFalse(result.IsValid, "Expected invalid serial.");
            AssertContains(result.JoinMessages(), "Серийный номер подключаемой ККТ должен состоять только из цифр");
        }

        private static void KktSerialMustHaveExpectedLength()
        {
            TspiotFormInput input = CreateValidInput();
            input.KktSerial = "1";

            ValidationResult result = TspiotInputValidator.ValidatePost(input);

            AssertFalse(result.IsValid, "Expected short KKT serial to be invalid.");
            AssertContains(result.JoinMessages(), "Серийный номер подключаемой ККТ/ФР должен содержать 14 цифр");
        }

        private static void FnSerialMustHaveExpectedLength()
        {
            TspiotFormInput input = CreateValidInput();
            input.FnSerial = "23";

            ValidationResult result = TspiotInputValidator.ValidatePut(input, true);

            AssertFalse(result.IsValid, "Expected short FN serial to be invalid.");
            AssertContains(result.JoinMessages(), "Номер ФН подключаемой ККТ должен содержать 16 цифр");
        }

        private static void InnLengthMustBeTenOrTwelve()
        {
            TspiotFormInput input = CreateValidInput();
            input.KktInn = "123456789";

            ValidationResult result = TspiotInputValidator.ValidatePut(input, true);

            AssertFalse(result.IsValid, "Expected invalid INN.");
            AssertContains(result.JoinMessages(), "ИНН должен содержать 10 или 12 цифр");
        }

        private static void InnControlDigitsMustBeValid()
        {
            TspiotFormInput input = CreateValidInput();
            input.KktInn = "1234567890";

            ValidationResult result = TspiotInputValidator.ValidatePut(input, true);

            AssertFalse(result.IsValid, "Expected invalid 10-digit INN control digit.");
            AssertContains(result.JoinMessages(), "Контрольные цифры ИНН не сходятся");
        }

        private static void Inn12ControlDigitsMustBeValid()
        {
            TspiotFormInput input = CreateValidInput();
            input.KktInn = "123456789040";

            ValidationResult invalidResult = TspiotInputValidator.ValidatePut(input, true);

            AssertFalse(invalidResult.IsValid, "Expected invalid 12-digit INN control digits.");
            AssertContains(invalidResult.JoinMessages(), "Контрольные цифры ИНН не сходятся");

            input.KktInn = "123456789047";
            ValidationResult validResult = TspiotInputValidator.ValidatePut(input, true);

            AssertTrue(validResult.IsValid, "Expected valid 12-digit INN.");
        }

        private static void UnexpectedSerialPrefixesProduceWarnings()
        {
            TspiotFormInput input = CreateValidInput();
            input.KktSerial = "99906200000000";
            input.FnSerial = "9900000000000002";

            ValidationResult result = TspiotInputValidator.ValidatePut(input, true);

            AssertTrue(result.IsValid, "Unexpected prefixes must not block operation.");
            AssertEqual(2, result.Warnings.Count, "Expected two warnings.");
            AssertContains(result.JoinWarnings(), "Серийный номер ФР/ККТ обычно начинается с 001");
            AssertContains(result.JoinWarnings(), "Серийный номер ФН обычно начинается с 73");

            input.KktSerial = "00106200000000";
            input.FnSerial = "7300000000000000";
            result = TspiotInputValidator.ValidatePut(input, true);

            AssertTrue(result.IsValid, "Expected valid preferred prefixes.");
            AssertEqual(0, result.Warnings.Count, "Expected no warnings.");
        }

        private static void UnknownAtolModelCodeProducesWarning()
        {
            TspiotFormInput input = CreateValidInput();
            input.KktSerial = "00199900000000";

            ValidationResult result = TspiotInputValidator.ValidatePost(input);

            AssertTrue(result.IsValid, "Unknown model code must not block operation.");
            AssertContains(result.JoinWarnings(), "Код модели ККТ 999 не найден");
        }

        private static void OptionalEnteredFieldsAreValidatedBeforeCheck()
        {
            TspiotFormInput input = new TspiotFormInput
            {
                BaseUrl = "http://127.0.0.1:51077",
                KktSerial = string.Empty,
                FnSerial = string.Empty,
                KktInn = string.Empty,
                Port = string.Empty,
                SoftPort = string.Empty,
                DkktPort = string.Empty
            };

            ValidationResult emptyResult = TspiotInputValidator.ValidateForCheck(input);

            AssertTrue(emptyResult.IsValid, "Empty optional fields must not block current KKT check.");

            input.KktSerial = "00106200000000";
            input.FnSerial = "1233";
            input.KktInn = "п4325345";

            ValidationResult invalidResult = TspiotInputValidator.ValidateForCheck(input);

            AssertFalse(invalidResult.IsValid, "Entered invalid optional fields must block current KKT check.");
            AssertContains(invalidResult.JoinMessages(), "Номер ФН подключаемой ККТ должен содержать 16 цифр");
            AssertContains(invalidResult.JoinMessages(), "ИНН должен состоять только из цифр");
        }

        private static void OptionalRegistrationFieldsAreValidatedBeforePost()
        {
            TspiotFormInput input = CreateValidInput();
            input.FnSerial = string.Empty;
            input.KktInn = string.Empty;

            ValidationResult emptyResult = TspiotInputValidator.ValidatePost(input);

            AssertTrue(emptyResult.IsValid, "Empty FN and INN must not block POST.");

            input.FnSerial = "7300000000000000";
            input.KktInn = "1314123";

            ValidationResult invalidResult = TspiotInputValidator.ValidatePost(input);

            AssertFalse(invalidResult.IsValid, "Entered invalid INN must block POST.");
            AssertContains(invalidResult.JoinMessages(), "ИНН должен содержать 10 или 12 цифр");
        }

        private static void PortsMustBeValidAndDistinct()
        {
            TspiotFormInput input = CreateValidInput();
            input.Port = "51402";
            input.SoftPort = "51402";

            ValidationResult result = TspiotInputValidator.ValidatePost(input);

            AssertFalse(result.IsValid, "Expected equal ports to be invalid.");
            AssertContains(result.JoinMessages(), "port и softPort не должны совпадать");
        }

        private static void ServicePortsMustDifferFromDkktPort()
        {
            TspiotFormInput portCollision = CreateValidInput();
            portCollision.Port = portCollision.DkktPort;
            ValidationResult portResult = TspiotInputValidator.ValidatePost(portCollision);

            TspiotFormInput softPortCollision = CreateValidInput();
            softPortCollision.SoftPort = softPortCollision.DkktPort;
            ValidationResult softPortResult = TspiotInputValidator.ValidatePost(softPortCollision);

            AssertFalse(portResult.IsValid, "Expected port and dkktPort collision to be rejected.");
            AssertContains(portResult.JoinMessages(), "port и dkktPort не должны совпадать");
            AssertFalse(softPortResult.IsValid, "Expected softPort and dkktPort collision to be rejected.");
            AssertContains(softPortResult.JoinMessages(), "softPort и dkktPort не должны совпадать");
        }

        private static void KnownErrorsAreDecoded()
        {
            string message = TspiotErrorDecoder.Decode(400, "{\"code\":1015}");
            AssertContains(message, "Не запущен агент-сервис ДККТ");

            message = TspiotErrorDecoder.Decode(400, "{\"errorCode\":2046}");
            AssertContains(message, "Служба ЕСМ не зарегистрирована");
        }

        private static void Error1012IsDecodedAsManualServiceRecovery()
        {
            string body = "{\"error\":{\"code\":1012,\"text\":\"Служба с таким именем не создана\"}}";

            AssertTrue(ServiceRecoveryCommandBuilder.IsManualServiceRecoveryError(body), "Expected 1012 to be detected.");

            string message = TspiotErrorDecoder.Decode(400, body);
            AssertContains(message, "Ошибка 1012");
            AssertContains(message, "службу подключаемой ККТ");
        }

        private static void Error1013IsDecodedAsManualServiceRecovery()
        {
            string body = "{\"error\":{\"code\":1013,\"text\":\"Служба не была запущена\",\"description\":\"служба не запущена\"}}";

            AssertTrue(ServiceRecoveryCommandBuilder.IsManualServiceRecoveryError(body), "Expected 1013 to be detected.");

            string message = TspiotErrorDecoder.Decode(500, body);
            AssertContains(message, "Ошибка 1013");
            AssertContains(message, "служба подключаемой ККТ");
        }

        private static void Error1026ExplainsMultipleInnLimitation()
        {
            string message = TspiotErrorDecoder.Decode(
                403,
                "{\"error\":{\"code\":1026,\"text\":\"Обнаружено несколько ИНН\"}}");

            AssertContains(message, "1026");
            AssertContains(message, "несколько ИНН");
            AssertContains(message, "автоматическую настройку");
            AssertContains(message, "только с кассами одного ИНН");
        }

        /// <summary>
        /// Проверка привязки в поле ждёт ЕСМ десятками секунд. Тестам ждать
        /// реальное время незачем, поэтому бюджет укорочен.
        /// </summary>
        private static LmGatewayBindingWorkflow NewFastBindingWorkflow(
            ITspiotApiClient api)
        {
            return new LmGatewayBindingWorkflow(
                api,
                TimeSpan.FromMilliseconds(600),
                TimeSpan.FromMilliseconds(50));
        }

        private static void UnknownHttpErrorShowsServiceResponseBody()
        {
            // Полевой случай: ЕСМ отвечал HTTP 500 на привязку, а в журнале
            // оставалось только «смотрите в ответе сервера» — самого ответа
            // не было нигде, и причина оставалась неизвестной.
            string message = TspiotErrorDecoder.Decode(
                500,
                "{\"detail\": \"lm controller unreachable\"}");

            AssertContains(message, "HTTP 500");
            AssertContains(message, "lm controller unreachable");

            string empty = TspiotErrorDecoder.Decode(500, string.Empty);
            AssertContains(empty, "Ответ сервиса пустой");

            string masked = TspiotErrorDecoder.Decode(
                500,
                "{\"espToken\": \"abcdefghijklmnopqrstuvwxyz0123456789\"}");
            AssertFalse(
                masked.Contains("abcdefghijklmnopqrstuvwxyz0123456789"),
                "Expected the response body excerpt to be masked.");

            string overflow = TspiotErrorDecoder.Decode(500, new string((char)0x0439, 4000));
            AssertTrue(
                overflow.Length < 1000,
                "Expected a long response body to be trimmed.");
        }

        private static void ServiceRecoveryCommandUsesKktSerialAndPorts()
        {
            string command = ServiceRecoveryCommandBuilder.BuildPowerShellScript(
                "00108000000003",
                "50402",
                "51402",
                "C:\\Program Files\\ESP\\ESM\\bin\\controlModule.exe");

            AssertContains(command, "$kkt = \"00108000000003\"");
            AssertContains(command, "$port = 50402");
            AssertContains(command, "$softPort = 51402");
            AssertContains(command, "New-Service");
            AssertContains(command, "esm-cm-$kkt");
            AssertContains(command, "controlModule.exe");
            AssertContains(command, "$serviceArgs = \"--id $kkt --port $port --soft-port $softPort --pretty-logs=true\"");
            AssertContains(command, "Restart-Service esm-orchestrator");
            AssertContains(command, "Не удалось перезапустить службу esm-orchestrator");
        }

        private static void ServiceRecoveryCommandRecreatesServiceWithWrongPorts()
        {
            string command = ServiceRecoveryCommandBuilder.BuildPowerShellScript(
                "00106300000004",
                "50402",
                "51402",
                "C:\\Program Files\\ESP\\ESM\\bin\\controlModule.exe");

            AssertContains(command, "Get-WmiObject Win32_Service");
            AssertContains(command, "$expectedId = \"--id $kkt\"");
            AssertContains(command, "$expectedPort = \"--port $port\"");
            AssertContains(command, "$expectedSoftPort = \"--soft-port $softPort\"");
            AssertContains(command, "Параметры существующей службы отличаются");
            AssertContains(command, "Stop-Service $serviceName");
            AssertContains(command, "sc.exe delete $serviceName");
            AssertContains(command, "Start-Sleep -Seconds 2");
            AssertContains(command, "New-Service");
        }

        private static void ServiceRecoveryCommandWritesDiagnostics()
        {
            string command = ServiceRecoveryCommandBuilder.BuildPowerShellScript(
                "00106300000004",
                "50402",
                "51402",
                "C:\\Program Files\\ESP\\ESM\\bin\\controlModule.exe");

            AssertContains(command, "Start-Transcript");
            AssertContains(command, "Stop-Transcript");
            AssertContains(command, "Диагностика перед восстановлением службы");
            AssertContains(command, "Get-WmiObject Win32_Service");
            AssertContains(command, "PathName -match");
            AssertContains(command, "netstat -ano");
            AssertContains(command, ":4041 :4042 :4043");
            AssertContains(command, "tasklist");
            AssertContains(command, "Лог сохранён");
        }

        private static void ServiceRecoveryCommandEscapesPowerShellSubstitution()
        {
            string command = ServiceRecoveryCommandBuilder.BuildPowerShellScript(
                "0010$(calc)003",
                "50402",
                "51402",
                "C:\\Program Files\\ESP\\ESM\\bin\\controlModule.exe");

            AssertContains(command, "$kkt = \"0010`$(calc)003\"");
            AssertFalse(
                command.IndexOf("$kkt = \"0010$(calc)003\"", StringComparison.Ordinal) >= 0,
                "PowerShell substitution must not remain executable.");
        }

        private static void ServiceRecoveryDetectionIgnoresCodesInsideSerials()
        {
            string body = "{\"kktSerial\":\"00101300000000\"}";

            AssertFalse(
                ServiceRecoveryCommandBuilder.IsManualServiceRecoveryError(body),
                "A serial containing 1013 must not enable service recovery.");
        }

        private static void ErrorCodeDetectionRequiresNumericBoundary()
        {
            string body = "{\"code\":10131}";

            AssertFalse(
                TspiotErrorDecoder.ContainsErrorCode(body, 1013),
                "Code 10131 must not be treated as 1013.");
            AssertFalse(
                ServiceRecoveryCommandBuilder.IsManualServiceRecoveryError(body),
                "Code 10131 must not enable service recovery.");
        }

        private static void ServiceRecoveryCommandRejectsInvalidPorts()
        {
            bool portRejected = false;
            try
            {
                ServiceRecoveryCommandBuilder.BuildPowerShellScript(
                    "00108000000003",
                    "50402; calc",
                    "51402",
                    string.Empty);
            }
            catch (ArgumentException ex)
            {
                portRejected = ex.Message.IndexOf("port", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            AssertTrue(portRejected, "Expected a non-numeric port to be rejected.");
        }

        private static void ForbiddenIsDecoded()
        {
            string message = TspiotErrorDecoder.Decode(403, "");
            AssertContains(message, "Forbidden");
        }

        private static void InstanceParserReadsNestedInstances()
        {
            string json = "{\"items\":[{\"id\":\"00100000000000\",\"port\":50402,\"softPort\":51402,\"dkktPort\":4041,\"serviceState\":\"Работает\"}]}";
            IList<KktInstanceInfo> instances = InstanceInfoParser.Parse(json);

            AssertEqual(1, instances.Count, "Expected one instance.");
            AssertEqual("00100000000000", instances[0].Id, "Expected id.");
            AssertEqual("50402", instances[0].Port, "Expected port.");
            AssertEqual("51402", instances[0].SoftPort, "Expected softPort.");
            AssertEqual("4041", instances[0].DkktPort, "Expected dkktPort.");
            AssertEqual("Работает", instances[0].ServiceState, "Expected serviceState.");
        }

        private static void InstanceParserReadsRootArrays()
        {
            string json = "[{\"id\":\"A\"},{\"id\":\"B\",\"serviceState\":\"Stopped\"}]";
            IList<KktInstanceInfo> instances = InstanceInfoParser.Parse(json);

            AssertEqual(2, instances.Count, "Expected two instances.");
            AssertTrue(InstanceInfoParser.ContainsId(json, "B"), "Expected id B to be found.");
            AssertFalse(InstanceInfoParser.ContainsId(json, "C"), "Expected id C not to be found.");
        }

        private static void InstanceResponseAcceptsEmpty204AsNoInstances()
        {
            IList<KktInstanceInfo> instances;
            ApiResponse noContent = new ApiResponse
            {
                StatusCode = 204,
                IsSuccess = true,
                ResponseBody = string.Empty
            };
            ApiResponse unexpectedEmptyOk = new ApiResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                ResponseBody = string.Empty
            };
            ApiResponse unexpected204Body = new ApiResponse
            {
                StatusCode = 204,
                IsSuccess = true,
                ResponseBody = "[]"
            };
            ApiResponse failedResponse = new ApiResponse
            {
                StatusCode = 500,
                IsSuccess = false,
                ResponseBody = "[]"
            };

            AssertTrue(
                InstanceInfoParser.TryParse(noContent, out instances),
                "HTTP 204 with an empty body must mean an empty instance list.");
            AssertEqual(0, instances.Count, "Expected no registered instances after HTTP 204.");
            AssertFalse(
                InstanceInfoParser.TryParse(unexpectedEmptyOk, out instances),
                "HTTP 200 with an empty body must remain an invalid contract.");
            AssertFalse(
                InstanceInfoParser.TryParse(unexpected204Body, out instances),
                "HTTP 204 with a non-empty body must be rejected as an anomalous response.");
            AssertFalse(
                InstanceInfoParser.TryParse(failedResponse, out instances),
                "An unsuccessful response must not be parsed as an instance list.");
        }

        private static void DkktParserReadsKktArray()
        {
            string json = "{\"kkt\":[{\"kktSerial\":\"00106200000000\",\"fnSerial\":\"7300000000000000\",\"kktInn\":\"1234567894\",\"modelName\":\"Атол 55Ф\",\"dkktVersion\":\"1.0.1\"}]}";
            IList<DkktDeviceInfo> devices = DkktListParser.Parse(json);

            AssertEqual(1, devices.Count, "Expected one DKKt device.");
            AssertEqual("00106200000000", devices[0].KktSerial, "Expected kktSerial.");
            AssertEqual("7300000000000000", devices[0].FnSerial, "Expected fnSerial.");
            AssertEqual("1234567894", devices[0].KktInn, "Expected kktInn.");
            AssertEqual("Атол 55Ф", devices[0].ModelName, "Expected modelName.");
            AssertEqual("1.0.1", devices[0].DkktVersion, "Expected dkktVersion.");
        }

        private static void DkktResponseAcceptsOnlyEmpty204()
        {
            IList<DkktDeviceInfo> devices;
            AssertTrue(
                DkktListParser.TryParse(new ApiResponse
                {
                    IsSuccess = true,
                    StatusCode = 204,
                    ResponseBody = " \r\n\t"
                }, out devices),
                "An empty 204 must mean that no live driver sessions exist.");
            AssertEqual(0, devices.Count, "An empty 204 must produce an empty device list.");

            AssertFalse(
                DkktListParser.TryParse(new ApiResponse
                {
                    IsSuccess = true,
                    StatusCode = 204,
                    ResponseBody = "{\"kkt\":[]}"
                }, out devices),
                "A 204 response must not carry a body.");
            AssertFalse(
                DkktListParser.TryParse(new ApiResponse
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    ResponseBody = string.Empty
                }, out devices),
                "An empty 200 response must remain a contract failure.");
        }

        private static void AutomaticRegistrationKeepsOneInnFlow()
        {
            BulkRegistrationDiscovery discovery = CreateRegistrationModeDiscovery(
                new[] { "1234567894" },
                true);

            AssertEqual(
                AutomaticRegistrationMode.ExistingSessions,
                AutomaticRegistrationModeSelector.Select(discovery),
                "One visible INN must keep the existing no-dialog flow.");
        }

        private static void AutomaticRegistrationKeepsRegisteredMultiInnFlow()
        {
            BulkRegistrationDiscovery discovery = CreateRegistrationModeDiscovery(
                new[] { "1234567894", "7707083893" },
                false);

            AssertEqual(
                AutomaticRegistrationMode.ExistingSessions,
                AutomaticRegistrationModeSelector.Select(discovery),
                "Already registered KKT must not require VCOM takeover.");
        }

        private static void AutomaticRegistrationSelectsVcomForPendingMultiInnFlow()
        {
            BulkRegistrationDiscovery discovery = CreateRegistrationModeDiscovery(
                new[] { "1234567894", "7707083893" },
                true);

            AssertEqual(
                AutomaticRegistrationMode.SequentialVcom,
                AutomaticRegistrationModeSelector.Select(discovery),
                "Pending KKT of different INNs must be isolated through VCOM.");
        }

        private static void AutomaticRegistrationSelectsVcomWithoutLiveSessions()
        {
            BulkRegistrationDiscovery discovery = CreateRegistrationModeDiscovery(
                new string[0],
                false);

            AssertEqual(
                AutomaticRegistrationMode.SequentialVcom,
                AutomaticRegistrationModeSelector.Select(discovery),
                "An empty dkktList must trigger deterministic VCOM discovery.");
        }

        private static BulkRegistrationDiscovery CreateRegistrationModeDiscovery(
            string[] inns,
            bool hasPendingItem)
        {
            BulkRegistrationDiscovery discovery = new BulkRegistrationDiscovery();
            for (int index = 0; index < inns.Length; index++)
            {
                discovery.ObservedDevices.Add(new DkktDeviceInfo
                {
                    KktSerial = (105700000001L + index).ToString("00000000000000"),
                    KktInn = inns[index]
                });
            }
            if (hasPendingItem)
            {
                discovery.Items.Add(new BulkRegistrationWorkItem());
            }
            return discovery;
        }

        private static void DkktSelectorExcludesAlreadyCreatedInstances()
        {
            IList<DkktDeviceInfo> devices = DkktListParser.Parse(
                "{\"kkt\":[" +
                "{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"123456789047\"}," +
                "{\"kktSerial\":\"00105700000002\",\"fnSerial\":\"7300000000000000\",\"kktInn\":\"1234567894\"}" +
                "]}");
            IList<KktInstanceInfo> instances = InstanceInfoParser.Parse(
                "{\"instances\":[{\"id\":\"00105700000001\",\"port\":50401,\"softPort\":0,\"serviceState\":\"Работает\"}]}");

            IList<DkktDeviceInfo> candidates = DkktDeviceSelector.FindDevicesWithoutInstances(devices, instances);

            AssertEqual(1, candidates.Count, "Expected one uncreated DKKt candidate.");
            AssertEqual("00105700000002", candidates[0].KktSerial, "Expected second KKT candidate.");
        }

        private static void BulkPlannerAssignsFirstSequentialPortPairs()
        {
            IList<DkktDeviceInfo> devices = new List<DkktDeviceInfo>
            {
                CreateDevice("00105700000001"),
                CreateDevice("00105700000002")
            };

            BulkKktRegistrationPlan plan = BulkKktRegistrationPlanner.Build(
                "http://127.0.0.1:51077", "4041", devices, new List<KktInstanceInfo>());

            AssertEqual(2, plan.Items.Count, "Expected two planned devices.");
            AssertEqual("50401", plan.Items[0].Input.Port, "Expected first KKT port.");
            AssertEqual("51401", plan.Items[0].Input.SoftPort, "Expected first KKT soft port.");
            AssertEqual("50402", plan.Items[1].Input.Port, "Expected second KKT port.");
            AssertEqual("51402", plan.Items[1].Input.SoftPort, "Expected second KKT soft port.");
        }

        private static void BulkPlannerUsesOrchestratorDkktPortByDefault()
        {
            BulkKktRegistrationPlan plan = BulkKktRegistrationPlanner.Build(
                TspiotDefaults.BaseUrl,
                TspiotDefaults.DkktPort,
                new List<DkktDeviceInfo> { CreateDevice("00105700000001") },
                new List<KktInstanceInfo>());

            AssertEqual(1, plan.Items.Count, "Expected one planned device.");
            AssertEqual(
                "4042",
                plan.Items[0].Input.DkktPort,
                "The default request must target the ESM orchestrator, not the ATOL KKM service.");
        }

        private static void PortAllocatorSelectsPairAfterExistingInstances()
        {
            List<KktInstanceInfo> instances = new List<KktInstanceInfo>
            {
                new KktInstanceInfo { Id = "1", Port = "50401", SoftPort = "0" },
                new KktInstanceInfo { Id = "2", Port = "50402", SoftPort = "51402" }
            };

            KktPortPair pair = new KktPortPairAllocator(instances).ReserveNext();

            AssertEqual("50403", pair.Port, "Expected the next service port after pairs 1 and 2.");
            AssertEqual("51403", pair.SoftPort, "Expected the matching Frontol port.");
        }

        private static void PortAllocatorReservesEitherSideAndConsecutiveSelections()
        {
            List<KktInstanceInfo> instances = new List<KktInstanceInfo>
            {
                new KktInstanceInfo { Id = "1", Port = "50401", SoftPort = "51403" }
            };
            KktPortPairAllocator allocator = new KktPortPairAllocator(instances);

            KktPortPair first = allocator.ReserveNext();
            KktPortPair second = allocator.ReserveNext();

            AssertEqual("50402", first.Port, "Expected the lowest pair whose both sides are free.");
            AssertEqual("51402", first.SoftPort, "Expected matching softPort for the first selection.");
            AssertEqual("50404", second.Port, "Expected the previous selection to remain reserved.");
            AssertEqual("51404", second.SoftPort, "Expected matching softPort for the second selection.");
        }

        private static void PortAllocatorStartsWithPrimaryKktPair()
        {
            KktPortPair pair = new KktPortPairAllocator(new List<KktInstanceInfo>()).ReserveNext();

            AssertEqual("50401", pair.Port, "A clean ESM must allocate the primary KKT service port first.");
            AssertEqual("51401", pair.SoftPort, "A clean ESM must allocate the primary KKT software port first.");
        }

        private static void BulkPlannerReservesImplicitFirstSoftPort()
        {
            IList<KktInstanceInfo> instances = new List<KktInstanceInfo>
            {
                new KktInstanceInfo { Id = "00105700000001", Port = "50401", SoftPort = "0" }
            };

            BulkKktRegistrationPlan plan = BulkKktRegistrationPlanner.Build(
                "http://127.0.0.1:51077", "4041",
                new List<DkktDeviceInfo> { CreateDevice("00105700000002") }, instances);

            AssertEqual("50402", plan.Items[0].Input.Port, "Expected second pair port.");
            AssertEqual("51402", plan.Items[0].Input.SoftPort, "Expected second pair soft port.");
        }

        private static void BulkPlannerSkipsOccupiedPairIndexes()
        {
            IList<KktInstanceInfo> instances = new List<KktInstanceInfo>
            {
                new KktInstanceInfo { Id = "A", Port = "50401", SoftPort = "0" },
                new KktInstanceInfo { Id = "B", Port = "60000", SoftPort = "51403" }
            };

            BulkKktRegistrationPlan plan = BulkKktRegistrationPlanner.Build(
                "http://127.0.0.1:51077", "4041",
                new List<DkktDeviceInfo>
                {
                    CreateDevice("00105700000001"),
                    CreateDevice("00105700000002")
                }, instances);

            AssertEqual("50402", plan.Items[0].Input.Port, "Expected free pair 2.");
            AssertEqual("50404", plan.Items[1].Input.Port, "Expected free pair 4.");
        }

        private static void BulkPlannerRejectsExhaustedPortPairs()
        {
            IList<KktInstanceInfo> instances = new List<KktInstanceInfo>();
            for (int index = 1; index <= 1000; index++)
            {
                instances.Add(new KktInstanceInfo
                {
                    Id = "existing-" + index.ToString(),
                    Port = (50400 + index).ToString(),
                    SoftPort = (51400 + index).ToString()
                });
            }

            BulkKktRegistrationPlan plan = BulkKktRegistrationPlanner.Build(
                "http://127.0.0.1:51077", "4041",
                new List<DkktDeviceInfo> { CreateDevice("00105700000001") }, instances);

            AssertEqual(1, plan.Items.Count, "Expected one blocked plan row.");
            AssertFalse(plan.Items[0].Validation.IsValid, "Exhausted pairs must block registration.");
            AssertContains(plan.Items[0].Validation.JoinMessages(), "свободных пар портов");
        }

        private static void BulkPlannerExcludesExistingDevices()
        {
            IList<DkktDeviceInfo> devices = new List<DkktDeviceInfo>
            {
                CreateDevice("00105700000001"),
                CreateDevice("00105700000002")
            };
            IList<KktInstanceInfo> instances = new List<KktInstanceInfo>
            {
                new KktInstanceInfo { Id = "00105700000001", Port = "50401", SoftPort = "0" }
            };

            BulkKktRegistrationPlan plan = BulkKktRegistrationPlanner.Build(
                "http://127.0.0.1:51077", "4041", devices, instances);

            AssertEqual(1, plan.ExistingDevices.Count, "Expected one existing device.");
            AssertEqual("00105700000001", plan.ExistingDevices[0].KktSerial, "Expected existing serial.");
            AssertEqual(1, plan.Items.Count, "Expected one new device.");
            AssertEqual("00105700000002", plan.Items[0].Device.KktSerial, "Expected new serial.");
        }

        private static void BulkPlannerMarksInvalidDeviceData()
        {
            DkktDeviceInfo device = CreateDevice("00105700000001");
            device.FnSerial = "1234";

            BulkKktRegistrationPlan plan = BulkKktRegistrationPlanner.Build(
                "http://127.0.0.1:51077", "4041",
                new List<DkktDeviceInfo> { device }, new List<KktInstanceInfo>());

            AssertFalse(plan.Items[0].Validation.IsValid, "Expected invalid FN to block the item.");
            AssertContains(plan.Items[0].Validation.JoinMessages(), "Номер ФН подключаемой ККТ должен содержать 16 цифр");
        }

        private static void BulkPlannerDoesNotReservePortsForInvalidDevices()
        {
            DkktDeviceInfo invalid = CreateDevice("00105700000001");
            invalid.FnSerial = "1234";
            DkktDeviceInfo valid = CreateDevice("00105700000002");

            BulkKktRegistrationPlan plan = BulkKktRegistrationPlanner.Build(
                "http://127.0.0.1:51077", "4041",
                new List<DkktDeviceInfo> { invalid, valid },
                new List<KktInstanceInfo>());

            AssertFalse(plan.Items[0].Validation.IsValid, "Expected the first device to remain invalid.");
            AssertEqual("50401", plan.Items[1].Input.Port, "An invalid device must not consume the first KKT port pair.");
            AssertEqual("51401", plan.Items[1].Input.SoftPort, "An invalid device must not consume the first KKT softPort.");
        }

        private static void BulkPlannerSkipsDuplicateDevices()
        {
            DkktDeviceInfo first = CreateDevice("00105700000001");
            DkktDeviceInfo duplicate = CreateDevice("00105700000001");

            BulkKktRegistrationPlan plan = BulkKktRegistrationPlanner.Build(
                "http://127.0.0.1:51077", "4041",
                new List<DkktDeviceInfo> { first, duplicate },
                new List<KktInstanceInfo>());

            AssertEqual(1, plan.Items.Count, "Expected one planned serial.");
            AssertEqual(1, plan.DuplicateDevices.Count, "Expected duplicate to be recorded.");
        }
        private static DkktDeviceInfo CreateDevice(string serial)
        {
            return new DkktDeviceInfo
            {
                KktSerial = serial,
                FnSerial = "7300000000000001",
                KktInn = "1234567894"
            };
        }

        private static void BulkRegistrationSummaryCountsEveryStatus()
        {
            IList<BulkKktRegistrationResult> results = new List<BulkKktRegistrationResult>
            {
                new BulkKktRegistrationResult { Status = BulkKktRegistrationStatus.Registered },
                new BulkKktRegistrationResult { Status = BulkKktRegistrationStatus.AlreadyExists },
                new BulkKktRegistrationResult { Status = BulkKktRegistrationStatus.InvalidData },
                new BulkKktRegistrationResult { Status = BulkKktRegistrationStatus.AddFailed },
                new BulkKktRegistrationResult { Status = BulkKktRegistrationStatus.RegistrationFailed }
            };

            string summary = BulkKktRegistrationResult.FormatSummary(results);

            AssertContains(summary, "Зарегистрировано: 1");
            AssertContains(summary, "Уже существует: 1");
            AssertContains(summary, "Некорректные данные: 1");
            AssertContains(summary, "Ошибок добавления: 1");
            AssertContains(summary, "Регистрация не завершена (экземпляр существует): 1");
        }

        private static void BulkRegistrationResultFormatsLogLine()
        {
            BulkKktRegistrationResult result = new BulkKktRegistrationResult
            {
                KktSerial = "00105700000001",
                Status = BulkKktRegistrationStatus.RegistrationFailed,
                Details = "HTTP 500"
            };

            string line = result.FormatLogLine();

            AssertContains(line, "00105700000001");
            AssertContains(line, "Экземпляр создан, регистрация не завершена");
            AssertContains(line, "HTTP 500");
        }

        private static void BulkSettingsValidationIgnoresSingleKktFields()
        {
            ValidationResult valid = TspiotInputValidator.ValidateBulkSettings(
                "http://127.0.0.1:51077", "4041");
            ValidationResult invalid = TspiotInputValidator.ValidateBulkSettings(
                "not-a-url", "70000");

            AssertTrue(valid.IsValid, "Expected valid bulk settings.");
            AssertFalse(invalid.IsValid, "Expected invalid bulk settings.");
            AssertContains(invalid.JoinMessages(), "Адрес сервиса ЕСМ/ТС ПИоТ");
            AssertContains(invalid.JoinMessages(), "dkktPort должен быть целым числом в диапазоне 1-65535");
        }

        private static void BulkParsersDistinguishEmptyDataFromInvalidJson()
        {
            IList<KktInstanceInfo> instances;
            IList<DkktDeviceInfo> devices;

            AssertTrue(InstanceInfoParser.TryParse("{\"instances\":[]}", out instances), "Expected valid empty instances JSON.");
            AssertEqual(0, instances.Count, "Expected no instances.");
            AssertFalse(InstanceInfoParser.TryParse("{broken", out instances), "Expected invalid instances JSON.");

            AssertTrue(DkktListParser.TryParse("{\"kkt\":[]}", out devices), "Expected valid empty DKKt JSON.");
            AssertEqual(0, devices.Count, "Expected no devices.");
            AssertFalse(DkktListParser.TryParse("{broken", out devices), "Expected invalid DKKt JSON.");
        }

        private static void StrictParsersRejectUnexpectedJsonShape()
        {
            IList<KktInstanceInfo> instances;
            IList<DkktDeviceInfo> devices;

            AssertFalse(InstanceInfoParser.TryParse("{}", out instances), "Expected missing instances array to be rejected.");
            AssertFalse(InstanceInfoParser.TryParse("{\"error\":{\"code\":1}}", out instances), "Expected error object to be rejected.");
            AssertFalse(InstanceInfoParser.TryParse("{\"instances\":[{\"port\":50401,\"softPort\":51401}]}", out instances),
                "Expected an instance without id to be rejected.");
            AssertFalse(DkktListParser.TryParse("{}", out devices), "Expected missing kkt array to be rejected.");
            AssertFalse(DkktListParser.TryParse("{\"instances\":[]}", out devices), "Expected wrong response contract to be rejected.");
        }

        private static void InstanceDetailsParserReadsRegistrationData()
        {
            string json = "{\"state\":\"Зарегистрирован\",\"clientPort\":51402,\"regData\":{" +
                "\"kktSerial\":\"00105700000001\"," +
                "\"fnSerial\":\"7300000000000001\"," +
                "\"kktInn\":\"1234567894\"}}";
            KktInstanceDetails details;

            AssertTrue(InstanceDetailsParser.TryParse(json, out details), "Expected valid instance details.");
            AssertEqual("51402", details.ClientPort, "Expected client port.");
            AssertEqual("Зарегистрирован", details.State, "Expected registration state.");
            AssertTrue(details.IsRegistered, "Expected the registered state to be explicit.");
            AssertTrue(details.HasCompleteRegistrationData, "Expected complete regData.");
            AssertEqual("00105700000001", details.RegistrationData.KktSerial, "Expected KKT serial.");
            AssertEqual("7300000000000001", details.RegistrationData.FnSerial, "Expected FN serial.");
            AssertEqual("1234567894", details.RegistrationData.KktInn, "Expected INN.");

            AssertFalse(InstanceDetailsParser.TryParse("{}", out details), "Expected unrelated JSON to be rejected.");
        }

        private static void InstanceDetailsParserRejectsMalformedRegistrationData()
        {
            KktInstanceDetails details;

            AssertFalse(InstanceDetailsParser.TryParse("{\"clientPort\":51402,\"regData\":\"unexpected\"}", out details),
                "Expected non-object regData to be rejected.");
            AssertFalse(InstanceDetailsParser.TryParse("{\"clientPort\":51402,\"regData\":{\"kktSerial\":\"00105700000001\"}}", out details),
                "Expected partial regData to be rejected.");
            AssertTrue(InstanceDetailsParser.TryParse("{\"clientPort\":51402,\"regData\":null}", out details),
                "Expected explicit null regData before registration.");
            AssertFalse(details.HasCompleteRegistrationData, "Null regData must remain incomplete.");
        }

        private static void ApiClientUsesDocumentedHttpContracts()
        {
            RecordingHttpHandler handler = new RecordingHttpHandler();
            TspiotApiClient client = new TspiotApiClient(new HttpClient(handler));
            string baseUrl = "http://127.0.0.1:51077";

            client.GetInstancesAsync(baseUrl, CancellationToken.None).Wait();
            client.GetDkktListAsync(baseUrl, CancellationToken.None).Wait();
            client.GetInstanceAsync(baseUrl, "00105700000001", CancellationToken.None).Wait();
            client.GetSettingsAsync(baseUrl, "00105700000001", CancellationToken.None).Wait();
            client.AddInstanceAsync(baseUrl, new AddTspiotRequest
            {
                Id = "00105700000001",
                Port = 50402,
                SoftPort = 51402,
                DkktPort = 4041
            }, CancellationToken.None).Wait();
            client.RegisterInstanceAsync(baseUrl, new RegisterTspiotRequest
            {
                Id = "00105700000001",
                KktSerial = "00105700000001",
                FnSerial = "7300000000000001",
                KktInn = "1234567894"
            }, CancellationToken.None).Wait();
            client.DeleteInstanceAsync(baseUrl, "00105700000001", CancellationToken.None).Wait();
            client.ConfigureLmGatewayAsync(baseUrl, "00105700000001", new LmConnectionRequest
            {
                Address = "127.0.0.1",
                Port = 50063,
                Login = "operator",
                Password = "raw-test-password"
            }, CancellationToken.None).Wait();

            AssertEqual(8, handler.Requests.Count, "Expected eight requests.");
            AssertRequest(handler.Requests[0], "GET", baseUrl + "/api/v1/instances/info");
            AssertRequest(handler.Requests[1], "GET", baseUrl + "/api/v1/dkktList");
            AssertRequest(handler.Requests[2], "GET", baseUrl + "/api/v1/instances/info/00105700000001");
            AssertRequest(handler.Requests[3], "GET", baseUrl + "/api/v1/settings/00105700000001");
            AssertRequest(handler.Requests[4], "POST", baseUrl + "/api/v1/tspiot");
            AssertContains(handler.Requests[4].Body, "\"dkktPort\":4041");
            AssertContains(handler.Requests[4].Body, "\"port\":50402");
            AssertContains(handler.Requests[4].Body, "\"softPort\":51402");
            AssertRequest(handler.Requests[5], "PUT", baseUrl + "/api/v1/tspiot");
            AssertContains(handler.Requests[5].Body, "\"kktSerial\":\"00105700000001\"");
            AssertContains(handler.Requests[5].Body, "\"fnSerial\":\"7300000000000001\"");
            AssertContains(handler.Requests[5].Body, "\"kktInn\":\"1234567894\"");
            AssertRequest(handler.Requests[6], "DELETE", baseUrl + "/api/v1/tspiot/00105700000001");
            AssertRequest(handler.Requests[7], "PUT", baseUrl + "/api/v1/settings/lm/00105700000001");
        }

        private static void LmGatewayApiUsesDocumentedPutContract()
        {
            RecordingHttpHandler handler = new RecordingHttpHandler();
            TspiotApiClient client = new TspiotApiClient(new HttpClient(handler));

            client.ConfigureLmGatewayAsync(
                "http://127.0.0.1:51077",
                "00105700000001",
                new LmConnectionRequest
                {
                    Address = "127.0.0.1",
                    Port = 50063,
                    Login = "operator",
                    Password = "raw-test-password"
                },
                CancellationToken.None).Wait();

            AssertEqual(1, handler.Requests.Count, "Expected one LM settings request.");
            RecordedHttpRequest request = handler.Requests[0];
            AssertRequest(request, "PUT", "http://127.0.0.1:51077/api/v1/settings/lm/00105700000001");
            AssertEqual("application/json; charset=utf-8", request.ContentType, "Expected JSON UTF-8 content type.");
            AssertEqual(
                "{\"address\":\"127.0.0.1\",\"login\":\"operator\",\"password\":\"raw-test-password\",\"port\":50063}",
                request.Body,
                "Expected exactly the documented LM settings fields and a numeric port.");
        }

        private static void LmGatewayApiEscapesInstanceId()
        {
            RecordingHttpHandler handler = new RecordingHttpHandler();
            TspiotApiClient client = new TspiotApiClient(new HttpClient(handler));

            client.ConfigureLmGatewayAsync(
                "http://127.0.0.1:51077",
                "a/b c",
                new LmConnectionRequest(),
                CancellationToken.None).Wait();

            AssertEqual(
                "http://127.0.0.1:51077/api/v1/settings/lm/a%2Fb%20c",
                handler.Requests[0].Url,
                "Expected the instance id to be escaped as one URI segment.");
        }

        private static void LmGatewayApiResponseStoresRedactedRequest()
        {
            const string password = "raw-test-password";
            RecordingHttpHandler handler = new RecordingHttpHandler();
            TspiotApiClient client = new TspiotApiClient(new HttpClient(handler));

            ApiResponse response = client.ConfigureLmGatewayAsync(
                "http://127.0.0.1:51077",
                "00105700000001",
                new LmConnectionRequest
                {
                    Address = "127.0.0.1",
                    Port = 50063,
                    Login = "operator",
                    Password = password
                },
                CancellationToken.None).Result;

            AssertContains(handler.Requests[0].Body, "\"password\":\"" + password + "\"");
            AssertContains(response.RequestBody, "\"password\":\"***\"");
            AssertFalse(response.RequestBody.Contains(password), "ApiResponse must not retain the raw password.");
        }

        private static void LmInfoApiUsesCurrentDocumentedEndpoint()
        {
            const string pass = "lm-info-secret";
            const string token = "lm-info-token";
            RecordingHttpHandler handler = new RecordingHttpHandler
            {
                ResponseBody = "{\"lm\":{\"pass\":\"" + pass + "\",\"token\":\"" + token + "\"}}"
            };
            TspiotApiClient client = new TspiotApiClient(new HttpClient(handler));

            ApiResponse explicitPort = client.GetLmInfoAsync(
                "http://192.0.2.44:51077",
                "50402",
                "51402",
                CancellationToken.None).Result;
            client.GetLmInfoAsync(
                "http://192.0.2.44:51077",
                "50401",
                "0",
                CancellationToken.None).Wait();
            client.GetLmInfoAsync(
                "http://127.0.0.1:51077",
                "50401",
                "51401",
                CancellationToken.None).Wait();

            AssertEqual(3, handler.Requests.Count, "Expected one readback request per KKT.");
            AssertRequest(handler.Requests[0], "GET", "https://192.0.2.44:51402/api/v2/info");
            AssertEqual("application/json", handler.Requests[0].Accept,
                "The documented info request must ask for JSON.");
            AssertRequest(handler.Requests[1], "GET", "https://192.0.2.44:51401/api/v2/info");
            AssertRequest(handler.Requests[2], "GET", "https://localhost:51401/api/v2/info");
            AssertFalse(explicitPort.ResponseBody.Contains(pass), "LM pass must not remain in the API response.");
            AssertFalse(explicitPort.ResponseBody.Contains(token), "LM token must not remain in the API response.");
            AssertContains(explicitPort.ResponseBody, "\"pass\":\"***\"");
            AssertContains(explicitPort.ResponseBody, "\"token\":\"***\"");
        }

        private static void LmInfoApiClassifiesTlsCertificateFailure()
        {
            HttpRequestException transportFailure = new HttpRequestException(
                "Secure connection failed.",
                new AuthenticationException("The remote certificate is invalid."));
            TspiotApiClient client = new TspiotApiClient(
                new HttpClient(new ThrowingHttpHandler(transportFailure)));

            ApiResponse response = client.GetLmInfoAsync(
                "http://127.0.0.1:51077",
                "50401",
                "51401",
                CancellationToken.None).Result;

            AssertTrue(response.IsConnectionFailure,
                "A TLS trust error must remain a transport failure.");
            PropertyInfo tlsProperty = typeof(ApiResponse).GetProperty(
                "IsTlsCertificateFailure",
                BindingFlags.Instance | BindingFlags.Public);
            AssertTrue(tlsProperty != null,
                "ApiResponse must distinguish a rejected TLS certificate from an ordinary connection failure.");
            AssertTrue(Convert.ToBoolean(tlsProperty.GetValue(response, null)),
                "A nested AuthenticationException must be classified as a TLS certificate failure.");
        }

        private static void LmReadbackExplainsRejectedTlsCertificate()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmInfoResponses.Enqueue(new ApiResponse
            {
                IsConnectionFailure = true,
                IsTlsCertificateFailure = true
            });
            LmGatewayReadbackWorkflow workflow = new LmGatewayReadbackWorkflow(api);

            LmGatewayReadbackObservation observation = workflow.ReadAsync(
                "http://127.0.0.1:51077",
                new LmGatewayKkt
                {
                    InstanceId = "00105700000001",
                    KktSerial = "00105700000001",
                    KktInn = "1234567894",
                    Port = "50401",
                    SoftPort = "51401"
                },
                "127.0.0.1",
                "5995",
                CancellationToken.None).Result;

            AssertFalse(observation.IsAvailable,
                "Rejected TLS must not be reported as a successful readback.");
            AssertContains(observation.Details, "сертификат ЕСМ");
            AssertContains(observation.Details, "доверенный корневой сертификат");
            AssertContains(observation.Details, "не отключайте проверку TLS");
        }

        private static void LmReadbackLabelsControllerErrorsWithoutImplyingHealth()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmInfoResponses.Enqueue(Success(
                "{\"kktSerial\":\"00105700000001\",\"kktInn\":\"1234567894\"," +
                "\"lm\":{\"version\":\"\",\"status\":\"error 2055: LM is unavailable\"," +
                "\"ip\":\"127.0.0.1\",\"port\":5995}}"));
            LmGatewayReadbackWorkflow workflow = new LmGatewayReadbackWorkflow(api);

            LmGatewayReadbackObservation observation = workflow.ReadAsync(
                "http://127.0.0.1:51077",
                new LmGatewayKkt
                {
                    InstanceId = "00105700000001",
                    KktSerial = "00105700000001",
                    KktInn = "1234567894",
                    Port = "50401",
                    SoftPort = "51401"
                },
                "127.0.0.1",
                "5995",
                CancellationToken.None).Result;

            AssertTrue(observation.EndpointMatches.HasValue && observation.EndpointMatches.Value,
                "The endpoint itself must remain confirmed.");
            AssertContains(observation.Details, "Адрес привязки");
            AssertContains(observation.Details, "ЛМ сообщает ошибку");
            AssertContains(observation.Details, "error 2055");
            AssertFalse(observation.Details.StartsWith("Подтверждено ЕСМ", StringComparison.Ordinal),
                "A matching address must not make an unhealthy LM look fully confirmed.");
        }

        private static void LmInfoParserExposesOnlySafeReadbackFields()
        {
            LmGatewayInfo info;
            bool parsed = LmGatewayInfoParser.TryParse(
                "{\"kktSerial\":\"00105700000001\",\"kktInn\":\"1234567894\"," +
                "\"lm\":{\"version\":\"2.0\",\"status\":\"ready\",\"ip\":\"127.0.0.1\"," +
                "\"port\":5995,\"login\":\"operator\",\"pass\":\"raw-pass\",\"token\":\"raw-token\"}}",
                out info);

            AssertTrue(parsed, "Expected the documented /api/v2/info shape.");
            AssertTrue(info.HasLmConfiguration, "Expected the LM block to be detected.");
            AssertEqual("00105700000001", info.KktSerial, "Expected KKT identity.");
            AssertEqual("1234567894", info.KktInn, "Expected KKT INN.");
            AssertEqual("127.0.0.1", info.LmAddress, "Expected safe LM address.");
            AssertEqual("5995", info.LmPort, "Expected safe LM port.");
            AssertEqual("ready", info.LmStatus, "Expected safe LM state.");
            AssertEqual("2.0", info.LmVersion, "Expected safe LM version.");
            AssertTrue(typeof(LmGatewayInfo).GetProperty("Login") == null,
                "Readback model must not expose the LM login.");
            AssertTrue(typeof(LmGatewayInfo).GetProperty("Pass") == null,
                "Readback model must not expose the LM pass.");
            AssertTrue(typeof(LmGatewayInfo).GetProperty("Token") == null,
                "Readback model must not expose the LM token.");
        }

        private static void LmDiscoveryReturnsRegisteredKktWithInn()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success(
                "{\"instances\":[" +
                "{\"id\":\"00105700000002\",\"port\":50402,\"softPort\":51402,\"dkktPort\":4041,\"serviceState\":\"RUNNING\"}," +
                "{\"id\":\"00105700000001\",\"port\":50401,\"softPort\":51401,\"dkktPort\":4041,\"serviceState\":\"STOPPED\"}]}");
            api.InstanceResponses.Enqueue(Success(
                "{\"clientPort\":51402,\"regData\":{\"kktSerial\":\"00105700000002\"," +
                "\"fnSerial\":\"7300000000000002\",\"kktInn\":\"1234567894\"}}"));
            api.InstanceResponses.Enqueue(Success(
                "{\"clientPort\":51401,\"regData\":{\"kktSerial\":\"00105700000001\"," +
                "\"fnSerial\":\"7300000000000001\",\"kktInn\":\"7707083893\"}}"));
            LmGatewayDiscoveryWorkflow workflow = new LmGatewayDiscoveryWorkflow(api);

            LmGatewayDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", null, CancellationToken.None).Result;

            AssertTrue(discovery.IsSuccessful, "Expected successful LM discovery.");
            AssertEqual(2, discovery.Items.Count, "Expected two registered KKT.");
            AssertEqual("00105700000002", discovery.Items[0].InstanceId, "Expected source order to be preserved.");
            AssertEqual("1234567894", discovery.Items[0].KktInn, "Expected first KKT INN.");
            AssertEqual("50402", discovery.Items[0].Port, "Expected ESM service port.");
            AssertEqual("51402", discovery.Items[0].SoftPort, "Expected ESM soft port.");
            AssertEqual("4041", discovery.Items[0].DkktPort, "Expected ESM dkkt port.");
            AssertEqual("RUNNING", discovery.Items[0].ServiceState, "Expected ESM service state.");
            AssertEqual("00105700000001", discovery.Items[1].InstanceId, "Expected stable second item.");
            AssertEqual("7707083893", discovery.Items[1].KktInn, "Expected a different second INN.");
            AssertEqual(0, discovery.Issues.Count, "Expected no issues for valid details.");
        }

        private static void LmDiscoveryAbortsOnMalformedInstanceList()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":\"unexpected\"}");
            LmGatewayDiscoveryWorkflow workflow = new LmGatewayDiscoveryWorkflow(api);

            LmGatewayDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", null, CancellationToken.None).Result;

            AssertFalse(discovery.IsSuccessful, "Malformed instance list must abort discovery.");
            AssertContains(discovery.ErrorMessage, "список экземпляров");
            AssertEqual(0, api.InstanceCalls, "Details must not be requested after a malformed list.");
            AssertEqual(0, discovery.Items.Count, "Malformed list must not produce binding candidates.");
        }

        private static void LmDiscoveryContinuesAfterOneMalformedDetail()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success(
                "{\"instances\":[{\"id\":\"bad-detail\"},{\"id\":\"00105700000002\"}]}");
            api.InstanceResponses.Enqueue(Success("{\"unexpected\":true}"));
            api.InstanceResponses.Enqueue(Success(
                "{\"clientPort\":51402,\"regData\":{\"kktSerial\":\"00105700000002\"," +
                "\"fnSerial\":\"7300000000000002\",\"kktInn\":\"1234567894\"}}"));
            LmGatewayDiscoveryWorkflow workflow = new LmGatewayDiscoveryWorkflow(api);

            LmGatewayDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", null, CancellationToken.None).Result;

            AssertTrue(discovery.IsSuccessful, "One malformed detail must remain a nonfatal issue.");
            AssertEqual(1, discovery.Issues.Count, "Expected one issue.");
            AssertEqual("bad-detail", discovery.Issues[0].InstanceId, "Issue must identify only the invalid instance.");
            AssertEqual(1, discovery.Items.Count, "Expected discovery to continue with the next instance.");
            AssertEqual("00105700000002", discovery.Items[0].InstanceId, "Expected the valid second KKT.");
        }

        private static void LmDiscoveryExcludesUnregisteredInstance()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[{\"id\":\"00105700000001\"}]}");
            api.InstanceResponses.Enqueue(Success("{\"clientPort\":51401,\"regData\":null}"));
            LmGatewayDiscoveryWorkflow workflow = new LmGatewayDiscoveryWorkflow(api);

            LmGatewayDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", null, CancellationToken.None).Result;

            AssertTrue(discovery.IsSuccessful, "Unregistered KKT must be a nonfatal issue.");
            AssertEqual(0, discovery.Items.Count, "Unregistered instance must not be eligible for LM binding.");
            AssertEqual(1, discovery.Issues.Count, "Expected an issue for incomplete registration data.");
            AssertContains(discovery.Issues[0].Message, "не зарегистрирован");
        }

        private static void LmDiscoveryHonorsCancellation()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[{\"id\":\"00105700000001\"}]}");
            api.CancelOnInstanceCall = 1;
            LmGatewayDiscoveryWorkflow workflow = new LmGatewayDiscoveryWorkflow(api);
            bool cancellationObserved = false;

            try
            {
                workflow.DiscoverAsync(
                    "http://127.0.0.1:51077", null, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                cancellationObserved = true;
            }

            AssertTrue(cancellationObserved, "Discovery cancellation must propagate to the caller.");
        }

        private static void LmBindingPlannerMatchesByKktIdentity()
        {
            LmGatewayDiscovery discovery = CreateLmDiscovery(
                new LmGatewayKkt { InstanceId = "00105700000001", KktSerial = "00105700000001", KktInn = "1234567894" },
                new LmGatewayKkt { InstanceId = "00105700000002", KktSerial = "00105700000002", KktInn = "1234567894" });
            IList<LmGatewayBindingInput> inputs = new List<LmGatewayBindingInput>
            {
                CreateLmBindingInput(" 00105700000002 ", "1234567894", "localhost", "50064"),
                CreateLmBindingInput("00105700000001", "1234567894", "::1", "50063")
            };

            LmGatewayBindingPlan plan = LmGatewayBindingPlanner.Build(discovery, inputs);
            inputs[1].ControllerGrpcPort = "59999";

            AssertEqual(2, plan.Items.Count, "Expected one plan row per discovered KKT.");
            AssertEqual("00105700000001", plan.Items[0].Kkt.KktSerial, "Expected discovery order.");
            AssertEqual("50063", plan.Items[0].Input.ControllerGrpcPort, "Inputs must match by KKT serial, not list order or INN.");
            AssertFalse(object.ReferenceEquals(inputs[1], plan.Items[0].Input), "Plan must copy mutable UI input.");
            AssertEqual("127.0.0.1", plan.Items[0].Input.ControllerAddress, "IPv6 loopback must normalize for the request.");
            AssertEqual("50064", plan.Items[1].Input.ControllerGrpcPort, "Expected the second serial's input.");
        }

        private static void LmBindingPlannerKeepsDifferentInnsSeparate()
        {
            LmGatewayDiscovery discovery = CreateLmDiscovery(
                new LmGatewayKkt { InstanceId = "00105700000001", KktSerial = "00105700000001", KktInn = "1234567894" },
                new LmGatewayKkt { InstanceId = "00105700000002", KktSerial = "00105700000002", KktInn = "7707083893" });
            IList<LmGatewayBindingInput> inputs = new List<LmGatewayBindingInput>
            {
                CreateLmBindingInput("00105700000001", "1234567894", "localhost", "50063"),
                CreateLmBindingInput("00105700000002", "7707083893", "127.0.0.1", "50063")
            };

            LmGatewayBindingPlan plan = LmGatewayBindingPlanner.Build(discovery, inputs);

            AssertEqual(2, plan.Items.Count, "Different INNs must remain separate rows.");
            AssertEqual("1234567894", plan.Items[0].Kkt.KktInn, "Expected first INN.");
            AssertEqual("7707083893", plan.Items[1].Kkt.KktInn, "Expected second INN.");
            AssertFalse(plan.Items[0].Validation.IsValid, "Shared endpoint must block the first row, not merge it.");
            AssertFalse(plan.Items[1].Validation.IsValid, "Shared endpoint must block the second row, not merge it.");
        }

        private static void LmBindingPlannerRequiresOneInputPerKkt()
        {
            LmGatewayDiscovery discovery = CreateLmDiscovery(
                new LmGatewayKkt { InstanceId = "00105700000001", KktSerial = "00105700000001", KktInn = "1234567894" },
                new LmGatewayKkt { InstanceId = "00105700000002", KktSerial = "00105700000002", KktInn = "7707083893" });
            IList<LmGatewayBindingInput> inputs = new List<LmGatewayBindingInput>
            {
                CreateLmBindingInput("00105700000001", "1234567894", "127.0.0.1", "50063"),
                CreateLmBindingInput("00105700000001", "1234567894", "127.0.0.1", "50064")
            };

            LmGatewayBindingPlan plan = LmGatewayBindingPlanner.Build(discovery, inputs);

            AssertFalse(plan.Items[0].Validation.IsValid, "Duplicate input must block its KKT row.");
            AssertContains(plan.Items[0].Validation.JoinMessages(), "несколько наборов");
            AssertFalse(plan.Items[1].Validation.IsValid, "Missing input must block its KKT row.");
            AssertContains(plan.Items[1].Validation.JoinMessages(), "не заданы");
        }

        private static void LmBindingPlannerRejectsDuplicateLocalEndpoints()
        {
            LmGatewayDiscovery discovery = CreateLmDiscovery(
                new LmGatewayKkt { InstanceId = "00105700000001", KktSerial = "00105700000001", KktInn = "1234567894" },
                new LmGatewayKkt { InstanceId = "00105700000002", KktSerial = "00105700000002", KktInn = "7707083893" });
            IList<LmGatewayBindingInput> inputs = new List<LmGatewayBindingInput>
            {
                CreateLmBindingInput("00105700000001", "1234567894", "localhost", "50063"),
                CreateLmBindingInput("00105700000002", "7707083893", "127.0.0.1", "50063")
            };

            LmGatewayBindingPlan plan = LmGatewayBindingPlanner.Build(discovery, inputs);

            AssertContains(plan.Items[0].Validation.JoinMessages(), "Локальный endpoint");
            AssertContains(plan.Items[1].Validation.JoinMessages(), "Локальный endpoint");
        }

        private static void LmBindingPlannerRejectsInvalidLoopbackAndPort()
        {
            LmGatewayBindingInput input = CreateLmBindingInput(
                "00105700000001", "1234567894", "192.168.1.10", "70000");

            ValidationResult validation = LmGatewayInputValidator.ValidateBinding(input);

            AssertFalse(validation.IsValid, "Remote address and out-of-range port must be rejected.");
            AssertContains(validation.JoinMessages(), "loopback");
            AssertContains(validation.JoinMessages(), "1-65535");
        }

        private static void LmBindingPlanCannotContainCredentials()
        {
            Type[] planTypes =
            {
                typeof(LmGatewayBindingInput),
                typeof(LmGatewayBindingItem),
                typeof(LmGatewayBindingPlan),
                typeof(LmGatewayBindingSessionRow),
                typeof(LmGatewayBindingSession)
            };

            for (int typeIndex = 0; typeIndex < planTypes.Length; typeIndex++)
            {
                System.Reflection.PropertyInfo[] properties = planTypes[typeIndex].GetProperties();
                for (int propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
                {
                    string propertyName = properties[propertyIndex].Name;
                    string propertyTypeName = properties[propertyIndex].PropertyType.FullName ?? string.Empty;
                    AssertFalse(propertyName.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0,
                        "Plan model must not expose a password property.");
                    AssertFalse(propertyName.IndexOf("Token", StringComparison.OrdinalIgnoreCase) >= 0,
                        "Plan model must not expose a token property.");
                    AssertFalse(propertyName.IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0,
                        "Plan model must not expose a secret property.");
                    AssertFalse(propertyTypeName.IndexOf("Credential", StringComparison.OrdinalIgnoreCase) >= 0,
                        "Plan model must not retain a credential object.");
                }
            }
        }

        private static void LmServiceIdentityIsDeterministicAndIndependent()
        {
            string first = LmServiceIdentity.CreateName("00105700000001");
            string repeated = LmServiceIdentity.CreateName("00105700000001");
            string second = LmServiceIdentity.CreateName("00105700000002");
            string parsedSerial;

            AssertEqual("krs-esm-lm-00105700000001", first, "Expected the fixed app-owned service prefix.");
            AssertEqual(first, repeated, "The same KKT must always produce the same service identity.");
            AssertFalse(string.Equals(first, second, StringComparison.Ordinal),
                "Different KKT must never share a service identity.");
            AssertTrue(LmServiceIdentity.TryParseName(first, out parsedSerial),
                "The exact derived identity must round-trip.");
            AssertEqual("00105700000001", parsedSerial,
                "The derived identity must retain only the KKT serial.");
            AssertFalse(LmServiceIdentity.TryParseName("esm-lm-controller", out parsedSerial),
                "The official base service must never parse as app-owned.");
        }

        private static void LmServiceIdentityRejectsUnsafeSerial()
        {
            string[] unsafeValues =
            {
                null,
                string.Empty,
                "0010570000001",
                "001057000000001",
                "0010570000000A",
                "0010570000000\u0661",
                "0010570000000-",
                " 00105700000001 "
            };

            for (int index = 0; index < unsafeValues.Length; index++)
            {
                bool rejected = false;
                try
                {
                    LmServiceIdentity.CreateName(unsafeValues[index]);
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }

                AssertTrue(rejected, "Unsafe KKT serial must be rejected.");
            }
        }

        private static void DirectControllerPlannerAssignsOfficialBaseThenClones()
        {
            DirectControllerPlan plan = DirectControllerPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000002", "7707083893"),
                    CreateLmKkt("00105700000001", "1234567894")
                },
                new List<DirectControllerAssignment>(),
                new List<DirectControllerServiceInventoryItem>
                {
                    new DirectControllerServiceInventoryItem
                    {
                        ServiceName = "esm-lm-controller",
                        IsVerifiedOfficial = true
                    }
                },
                new List<TcpListenerSnapshotItem>());

            AssertTrue(plan.IsValid, string.Join("; ", plan.ValidationMessages));
            AssertEqual(2, plan.Assignments.Count, "Expected one controller per registered KKT.");
            AssertEqual("00105700000001", plan.Assignments[0].KktSerial,
                "The first stable KKT gets the official base service.");
            AssertEqual(DirectControllerRole.OfficialBase, plan.Assignments[0].Role,
                "Ordinal one must be the verified official service.");
            AssertEqual("esm-lm-controller", plan.Assignments[0].ServiceName,
                "The base service name is vendor-owned and fixed.");
            AssertEqual(50063, plan.Assignments[0].GrpcPort, "Unexpected base gRPC port.");
            AssertEqual(5063, plan.Assignments[0].RestPort, "Unexpected base REST port.");
            AssertEqual(5995, plan.Assignments[0].TargetLocalModulePort,
                "The future LM port must remain compatible with field binding.");
            AssertEqual(DirectControllerRole.DirectClone, plan.Assignments[1].Role,
                "Every later KKT uses a direct clone.");
            AssertEqual("esm-lm-controller-2", plan.Assignments[1].ServiceName,
                "Clone identity must be derived from the stable ordinal.");
            AssertEqual(50064, plan.Assignments[1].GrpcPort, "Unexpected clone gRPC port.");
            AssertEqual(5064, plan.Assignments[1].RestPort, "Unexpected clone REST port.");
            AssertEqual(6995, plan.Assignments[1].TargetLocalModulePort,
                "Unexpected future LM port for ordinal two.");
        }

        private static void DirectControllerPlannerPreservesStableSavedOrdinal()
        {
            DirectControllerAssignment saved = new DirectControllerAssignment
            {
                KktSerial = "00105700000002",
                KktInn = "7707083893",
                Ordinal = 4,
                Role = DirectControllerRole.DirectClone,
                ServiceName = "esm-lm-controller-4",
                GrpcPort = 50066,
                RestPort = 5066,
                TargetLocalModulePort = 8995
            };
            DirectControllerPlan plan = DirectControllerPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000002", "7707083893"),
                    CreateLmKkt("00105700000001", "1234567894")
                },
                new List<DirectControllerAssignment> { saved },
                new List<DirectControllerServiceInventoryItem>
                {
                    new DirectControllerServiceInventoryItem
                    {
                        ServiceName = "esm-lm-controller",
                        IsVerifiedOfficial = true
                    }
                },
                new List<TcpListenerSnapshotItem>());

            AssertTrue(plan.IsValid, string.Join("; ", plan.ValidationMessages));
            DirectControllerAssignment preserved = plan.FindBySerial("00105700000002");
            AssertEqual(4, preserved.Ordinal,
                "A matching saved assignment must never be compacted into an ordinal gap.");
            AssertEqual("esm-lm-controller-4", preserved.ServiceName,
                "Stable service identity follows the preserved ordinal.");
            AssertEqual(1, plan.FindBySerial("00105700000001").Ordinal,
                "The free official base remains available to a new KKT.");
        }

        private static void DirectControllerPlannerNeverSharesLocalModulePort()
        {
            // Прошлый прогон присвоил второй ККТ базовый ЛМ, и это осело в
            // сохранённом назначении. Один ЛМ обслуживает один ИНН, поэтому
            // повторно отдавать тот же порт нельзя.
            DirectControllerAssignment saved = new DirectControllerAssignment
            {
                KktSerial = "00105700000002",
                KktInn = "7707083893",
                Ordinal = 2,
                Role = DirectControllerRole.DirectClone,
                ServiceName = "esm-lm-controller-2",
                GrpcPort = 50064,
                RestPort = 5064,
                TargetLocalModulePort = 5995
            };
            DirectControllerPlan plan = DirectControllerPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894"),
                    CreateLmKkt("00105700000002", "7707083893")
                },
                new List<DirectControllerAssignment> { saved },
                new List<DirectControllerServiceInventoryItem>
                {
                    new DirectControllerServiceInventoryItem
                    {
                        ServiceName = "esm-lm-controller",
                        IsVerifiedOfficial = true
                    }
                },
                new List<TcpListenerSnapshotItem>());

            AssertTrue(plan.IsValid, string.Join("; ", plan.ValidationMessages));
            AssertEqual(2, plan.Assignments.Count, "Обе ККТ обязаны попасть в план.");
            int first = plan.FindBySerial("00105700000001").TargetLocalModulePort;
            int second = plan.FindBySerial("00105700000002").TargetLocalModulePort;
            AssertTrue(first != second,
                "Два ИНН не могут делить один ЛМ: получено " +
                first.ToString() + " и " + second.ToString() + ".");
            AssertTrue(first >= 5995 && second >= 5995,
                "Порты ЛМ обязаны оставаться в штатном диапазоне.");
        }

        private static void DirectControllerPlannerToleratesAbsentSavedKkt()
        {
            // Одна ККТ временно не отдаётся ЕСМ (например, её экземпляр
            // перезапускается и отвечает 2003). Это не должно ронять стадию
            // контроллеров целиком, но её номер обязан остаться занятым.
            DirectControllerAssignment absent = new DirectControllerAssignment
            {
                KktSerial = "00105700000009",
                KktInn = "7707083893",
                Ordinal = 2,
                Role = DirectControllerRole.DirectClone,
                ServiceName = "esm-lm-controller-2",
                GrpcPort = 50064,
                RestPort = 5064,
                TargetLocalModulePort = 6995
            };
            DirectControllerPlan plan = DirectControllerPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894"),
                    CreateLmKkt("00105700000002", "7707083893")
                },
                new List<DirectControllerAssignment> { absent },
                new List<DirectControllerServiceInventoryItem>
                {
                    new DirectControllerServiceInventoryItem
                    {
                        ServiceName = "esm-lm-controller",
                        IsVerifiedOfficial = true
                    }
                },
                new List<TcpListenerSnapshotItem>());

            AssertTrue(plan.IsValid, string.Join("; ", plan.ValidationMessages));
            AssertEqual(0, plan.ValidationMessages.Count,
                "Недоступная сейчас ККТ не является ошибкой плана: " +
                string.Join("; ", plan.ValidationMessages));
            AssertEqual(2, plan.Assignments.Count,
                "В план входят только присутствующие ККТ.");
            AssertTrue(plan.FindBySerial("00105700000009") == null,
                "Отсутствующая ККТ в план этого прогона не попадает.");
            AssertEqual(1, plan.FindBySerial("00105700000001").Ordinal,
                "Штатная служба достаётся присутствующей ККТ.");
            AssertEqual(3, plan.FindBySerial("00105700000002").Ordinal,
                "Номер отсутствующей ККТ остаётся занятым за ней.");
        }

        private static void DirectControllerPlannerSkipsForeignNamesAndPorts()
        {
            DirectControllerPlan plan = DirectControllerPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894"),
                    CreateLmKkt("00105700000002", "7707083893")
                },
                new List<DirectControllerAssignment>(),
                new List<DirectControllerServiceInventoryItem>
                {
                    new DirectControllerServiceInventoryItem
                    {
                        ServiceName = "esm-lm-controller",
                        IsVerifiedOfficial = true
                    },
                    new DirectControllerServiceInventoryItem
                    {
                        ServiceName = "esm-lm-controller-2",
                        IsOwned = false,
                        IsVerifiedOfficial = false
                    }
                },
                new List<TcpListenerSnapshotItem>
                {
                    new TcpListenerSnapshotItem(50065, "foreign-controller", false)
                });

            AssertTrue(plan.IsValid, string.Join("; ", plan.ValidationMessages));
            DirectControllerAssignment second = plan.FindBySerial("00105700000002");
            AssertEqual(4, second.Ordinal,
                "Ordinal two is blocked by a foreign name and ordinal three by a foreign port.");
            AssertEqual("esm-lm-controller-4", second.ServiceName,
                "A foreign RollingPin-style service must never be adopted or overwritten.");
        }

        private static void DirectControllerPlannerReportsOrdinalExhaustion()
        {
            List<DirectControllerServiceInventoryItem> services =
                new List<DirectControllerServiceInventoryItem>
                {
                    new DirectControllerServiceInventoryItem
                    {
                        ServiceName = "esm-lm-controller",
                        IsVerifiedOfficial = true
                    }
                };
            for (int ordinal = 2; ordinal <= 32; ordinal++)
            {
                services.Add(new DirectControllerServiceInventoryItem
                {
                    ServiceName = "esm-lm-controller-" + ordinal.ToString(),
                    IsOwned = false
                });
            }

            DirectControllerPlan plan = DirectControllerPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894"),
                    CreateLmKkt("00105700000002", "7707083893")
                },
                new List<DirectControllerAssignment>(),
                services,
                new List<TcpListenerSnapshotItem>());

            AssertFalse(plan.IsValid, "Exhaustion must be explicit rather than dropping a KKT.");
            AssertEqual(1, plan.Assignments.Count,
                "Only the KKT assigned before exhaustion may remain in the plan.");
            AssertContains(string.Join("; ", plan.ValidationMessages), "32");
        }

        private static void MsiLocalModulePlannerGroupsKktByInn()
        {
            string systemVolume = Path.GetPathRoot(Environment.SystemDirectory);
            LocalModuleMsiPlan lmPlan = LocalModuleMsiPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894"),
                    CreateLmKkt("00105700000002", "1234567894")
                },
                new List<LocalModuleMsiAssignment>(),
                new LocalModuleBaseInventory
                {
                    IsInstalled = true,
                    InstallDirectory = Path.Combine(systemVolume, "Program Files", "Regime"),
                    ApiPort = 5995,
                    DatabasePort = 5984,
                    WasInstalledByApplication = false
                },
                null,
                new List<TcpListenerSnapshotItem>());

            AssertTrue(lmPlan.IsValid, string.Join("; ", lmPlan.ValidationMessages));
            AssertEqual(1, lmPlan.Assignments.Count,
                "Two KKT of one INN must share one local module.");
            AssertEqual(0, lmPlan.Assignments[0].CloneOrdinal,
                "The first INN must use the official base local module.");
            AssertTrue(lmPlan.Assignments[0].BaseWasPreExisting,
                "A base product found before this operation must remain pre-existing.");

            DirectControllerPlan controllers = DirectControllerPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894"),
                    CreateLmKkt("00105700000002", "1234567894")
                },
                new List<DirectControllerAssignment>(),
                new List<DirectControllerServiceInventoryItem>
                {
                    new DirectControllerServiceInventoryItem
                    {
                        ServiceName = "esm-lm-controller",
                        IsVerifiedOfficial = true
                    }
                },
                new List<TcpListenerSnapshotItem>(),
                lmPlan.CreateTargetApiPortMap());

            AssertTrue(controllers.IsValid,
                string.Join("; ", controllers.ValidationMessages));
            AssertEqual(5995, controllers.Assignments[0].TargetLocalModulePort,
                "The first controller must target the shared base LM.");
            AssertEqual(5995, controllers.Assignments[1].TargetLocalModulePort,
                "The second controller of the same INN must target the same LM.");
            AssertFalse(
                controllers.Assignments[0].GrpcPort == controllers.Assignments[1].GrpcPort,
                "Each KKT still requires its own direct-controller gRPC listener.");
        }

        private static void MsiLocalModulePlannerUsesActualBaseAndClonePorts()
        {
            string systemVolume = Path.GetPathRoot(Environment.SystemDirectory);
            LocalModuleMsiPlan plan = LocalModuleMsiPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894"),
                    CreateLmKkt("00105700000002", "7707083893")
                },
                new List<LocalModuleMsiAssignment>(),
                new LocalModuleBaseInventory
                {
                    IsInstalled = true,
                    InstallDirectory = Path.Combine(systemVolume, "Program Files", "Regime"),
                    ApiPort = 5995,
                    DatabasePort = 5984,
                    WasInstalledByApplication = false
                },
                null,
                new List<TcpListenerSnapshotItem>());

            AssertTrue(plan.IsValid, string.Join("; ", plan.ValidationMessages));
            AssertEqual(2, plan.Assignments.Count, "Expected one LM per unique INN.");
            AssertEqual(5995, plan.Assignments[0].ApiPort,
                "Base API port must come from actual inventory.");
            AssertEqual(5984, plan.Assignments[0].DatabasePort,
                "Base database port must come from actual inventory.");
            AssertEqual(6995, plan.Assignments[1].ApiPort,
                "The first clone API port must use the characterized rule.");
            AssertEqual(7984, plan.Assignments[1].DatabasePort,
                "The first clone database port has a deliberate 2000 gap from base.");
            AssertEqual(systemVolume, plan.Assignments[1].InstallVolumeRoot,
                "Clone root must default to the actual base-LM volume.");
        }

        private static void MsiLocalModuleRootPolicyAcceptsOnlyFixedProgramFilesVolumes()
        {
            AssertEqual("D:\\", LocalModuleInstallRootPolicy.GetVolumeRoot(
                @"D:\Program Files\Regime"),
                "The default volume must be derived from the base install path.");
            AssertEqual(@"D:\Program Files\Regime1",
                LocalModuleInstallRootPolicy.BuildCloneInstallDirectory(@"D:\", 1),
                "Clone paths must always stay under Program Files.");
            AssertFalse(LocalModuleInstallRootPolicy.IsCanonicalVolumeRoot(@"D:\Temp"),
                "An arbitrary directory is not an install-volume root.");
            AssertFalse(LocalModuleInstallRootPolicy.IsCanonicalVolumeRoot(
                    @"\\server\share"),
                "UNC roots must be rejected.");
            AssertFalse(LocalModuleInstallRootPolicy.IsCanonicalVolumeRoot("relative"),
                "Relative roots must be rejected.");
            AssertFalse(LocalModuleInstallRootPolicy.IsSupportedVolumeCharacteristics(
                    DriveType.Network,
                    FileAttributes.Directory),
                "Mapped and network drives must be rejected.");
            AssertFalse(LocalModuleInstallRootPolicy.IsSupportedVolumeCharacteristics(
                    DriveType.Fixed,
                    FileAttributes.Directory | FileAttributes.ReparsePoint),
                "Reparse-point roots must be rejected.");
            AssertTrue(LocalModuleInstallRootPolicy.IsSupportedVolumeCharacteristics(
                    DriveType.Fixed,
                    FileAttributes.Directory),
                "A plain fixed local volume is supported.");
            AssertEqual(
                Path.GetPathRoot(Environment.SystemDirectory),
                LocalModuleInstallRootPolicy.GetSystemVolumeRoot(),
                "New local modules must always target the system volume.");
        }

        private static void EsmInstanceServiceIdentityMatchesVendorNaming()
        {
            AssertEqual(
                EsmInstanceServiceIdentity.Prefix,
                ServiceRecoveryCommandBuilder.ServiceNamePrefix,
                "Префикс службы экземпляра ЕСМ должен быть один на всю программу.");
            AssertEqual(
                "esm-cm-00106205280301",
                EsmInstanceServiceIdentity.CreateName("00106205280301"),
                "Имя службы экземпляра ЕСМ задаёт вендор.");

            string serial;
            AssertTrue(
                EsmInstanceServiceIdentity.TryParseName(
                    "esm-cm-00106205280301",
                    out serial),
                "Вендорское имя должно разбираться.");
            AssertEqual("00106205280301", serial,
                "Серийный номер должен извлекаться без изменений.");

            string[] foreign =
            {
                null,
                "",
                "esm-cm-",
                "esm-cm-0010620528030",
                "esm-cm-001062052803011",
                "esm-cm-0010620528030X",
                "esm-lm-controller-2",
                "Esm-Cm-00106205280301"
            };
            for (int index = 0; index < foreign.Length; index++)
            {
                string parsed;
                AssertFalse(
                    EsmInstanceServiceIdentity.TryParseName(foreign[index], out parsed),
                    "Имя " + (foreign[index] ?? "<null>") +
                    " не должно приниматься за службу экземпляра ЕСМ.");
            }

            bool rejectedShortSerial = false;
            try
            {
                EsmInstanceServiceIdentity.CreateName("920352376299");
            }
            catch (ArgumentException)
            {
                rejectedShortSerial = true;
            }
            AssertTrue(rejectedShortSerial,
                "Серийный номер ККТ должен состоять ровно из 14 цифр.");
        }

        private static void MsiLocalModulePackagePolicyPinsVendorNotVersion()
        {
            LocalModuleInstallerSelection next = CreateLmPackageSelection();
            next.ProductVersion = "2.7.3";
            next.ProductCode = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
            next.Sha256 = new string('c', 64);
            next.ByteLength = 60000000;
            next.FileName = "regime-2.7.3-1.msi";
            ValidationResult accepted = LocalModulePackagePolicy.Evaluate(next);
            AssertTrue(accepted.IsValid,
                "Следующая версия ЛМ ЧЗ от ЦРПТ должна приниматься.");
            AssertEqual(0, accepted.Warnings.Count,
                "У пакета того же продукта не должно быть замечаний.");

            LocalModuleInstallerSelection subjectWithEmail =
                CreateLmPackageSelection();
            subjectWithEmail.SignerSubject =
                "E=support@example.invalid, " +
                SupportedLocalModulePackageIdentity.SignerSubject + ", C=RU";
            AssertTrue(
                LocalModulePackagePolicy.Evaluate(subjectWithEmail).IsValid,
                "Дополнительные поля субъекта сертификата не должны мешать.");

            LocalModuleInstallerSelection foreignSigner =
                CreateLmPackageSelection();
            foreignSigner.SignerSubject = "CN=Другой поставщик, O=Другой";
            ValidationResult signerRejected =
                LocalModulePackagePolicy.Evaluate(foreignSigner);
            AssertFalse(signerRejected.IsValid,
                "Пакет без подписи ЦРПТ должен отвергаться.");
            AssertContains(signerRejected.JoinMessages(), "подписан не ЦРПТ");

            LocalModuleInstallerSelection foreignUpgrade =
                CreateLmPackageSelection();
            foreignUpgrade.UpgradeCode =
                "{11111111-2222-3333-4444-555555555555}";
            ValidationResult upgradeRejected =
                LocalModulePackagePolicy.Evaluate(foreignUpgrade);
            AssertFalse(upgradeRejected.IsValid,
                "Пакет с чужим UpgradeCode должен отвергаться.");
            AssertContains(upgradeRejected.JoinMessages(), "UpgradeCode");

            LocalModuleInstallerSelection renamed = CreateLmPackageSelection();
            renamed.ProductName = "Локальный модуль ЧЗ (новое имя)";
            ValidationResult renamedResult =
                LocalModulePackagePolicy.Evaluate(renamed);
            AssertTrue(renamedResult.IsValid,
                "Переименование продукта вендором не должно быть отказом.");
            AssertEqual(1, renamedResult.Warnings.Count,
                "Переименование продукта должно оставаться замечанием.");
        }

        private static LocalModuleInstallerSelection CreateLmPackageSelection()
        {
            return new LocalModuleInstallerSelection
            {
                SourcePath = @"D:\packages\regime-2.6.1-7.msi",
                FileName = "regime-2.6.1-7.msi",
                ByteLength = 51007488,
                Sha256 = new string('a', 64),
                ProductName =
                    SupportedLocalModulePackageIdentity.ProductName,
                ProductVersion = "2.6.1",
                ProductCode = "{556FD8AD-43A3-4645-BC54-EBF3043ADF82}",
                UpgradeCode =
                    SupportedLocalModulePackageIdentity.UpgradeCode,
                SignerSubject =
                    SupportedLocalModulePackageIdentity.SignerSubject,
                SignerThumbprint = new string('b', 40),
                LicenseNoticeAccepted = true
            };
        }

        private static void MsiLocalModulePlannerReservesOrdinalsAndNamesPortOwners()
        {
            string systemVolume = Path.GetPathRoot(Environment.SystemDirectory);
            LocalModuleMsiPlan plan = LocalModuleMsiPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894"),
                    CreateLmKkt("00105700000002", "7707083893")
                },
                new List<LocalModuleMsiAssignment>(),
                new LocalModuleBaseInventory
                {
                    IsInstalled = true,
                    InstallDirectory = Path.Combine(systemVolume, "Program Files", "Regime"),
                    ApiPort = 5995,
                    DatabasePort = 5984,
                    WasInstalledByApplication = false
                },
                null,
                new List<TcpListenerSnapshotItem>
                {
                    new TcpListenerSnapshotItem(5995, "postgres", true)
                });

            AssertEqual(1, plan.Assignments.Count,
                "Only the second INN can be planned when the base ports are taken.");
            AssertEqual("7707083893", plan.Assignments[0].Inn,
                "The blocked INN must not consume the free clone ordinal.");
            AssertEqual(1, plan.Assignments[0].CloneOrdinal,
                "A failed base assignment must reserve ordinal 0 for nobody else.");
            AssertEqual(1, plan.ValidationMessages.Count,
                "A single blocked INN must produce exactly one message.");
            AssertContains(plan.ValidationMessages[0], "1234567894");
            AssertContains(plan.ValidationMessages[0], "порт 5995");
            AssertContains(plan.ValidationMessages[0], "служба postgres");

            LocalModuleMsiPlan unknownOwner = LocalModuleMsiPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894")
                },
                new List<LocalModuleMsiAssignment>(),
                new LocalModuleBaseInventory
                {
                    IsInstalled = true,
                    InstallDirectory = Path.Combine(systemVolume, "Program Files", "Regime"),
                    ApiPort = 5995,
                    DatabasePort = 5984,
                    WasInstalledByApplication = false
                },
                null,
                new List<TcpListenerSnapshotItem>
                {
                    new TcpListenerSnapshotItem(5984, string.Empty, false)
                });

            AssertEqual(0, unknownOwner.Assignments.Count,
                "A taken database port must block the base assignment too.");
            AssertEqual(1, unknownOwner.ValidationMessages.Count,
                "A single blocked INN must produce exactly one message.");
            AssertContains(unknownOwner.ValidationMessages[0], "порт 5984");
            AssertContains(unknownOwner.ValidationMessages[0], "владелец не определён");
        }

        private static void MsiLocalModulePlannerPreservesSavedOrdinals()
        {
            string systemVolume = Path.GetPathRoot(Environment.SystemDirectory);
            LocalModuleMsiPlan plan = LocalModuleMsiPlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000002", "7707083893"),
                    CreateLmKkt("00105700000003", "500100732259")
                },
                new List<LocalModuleMsiAssignment>
                {
                    new LocalModuleMsiAssignment
                    {
                        Inn = "1234567894",
                        CloneOrdinal = 0,
                        ApiPort = 5995,
                        DatabasePort = 5984,
                        InstallVolumeRoot = systemVolume,
                        BaseWasPreExisting = true
                    },
                    new LocalModuleMsiAssignment
                    {
                        Inn = "7707083893",
                        CloneOrdinal = 3,
                        ApiPort = LocalModuleMsiIdentity.ApiPortForClone(3),
                        DatabasePort = LocalModuleMsiIdentity.DatabasePortForClone(3),
                        InstallVolumeRoot = systemVolume
                    }
                },
                new LocalModuleBaseInventory
                {
                    IsInstalled = true,
                    AssignedInn = "1234567894",
                    InstallDirectory = Path.Combine(systemVolume, "Program Files", "Regime"),
                    ApiPort = 5995,
                    DatabasePort = 5984
                },
                null,
                new List<TcpListenerSnapshotItem>());

            AssertTrue(plan.IsValid, string.Join("; ", plan.ValidationMessages));
            AssertEqual(3, plan.FindByInn("7707083893").CloneOrdinal,
                "A saved clone ordinal must survive gaps and sorting.");
            AssertEqual(1, plan.FindByInn("500100732259").CloneOrdinal,
                "A new INN must use the first free clone ordinal.");
            AssertTrue(plan.FindByInn("1234567894") == null,
                "A temporarily absent base INN stays reserved without inventing a current row.");
        }

        private static void MsiLocalModuleDiskBudgetReportsBothVolumes()
        {
            long mib = 1024L * 1024L;
            LocalModuleDiskSpaceProjection projection =
                LocalModuleDiskSpacePolicy.Evaluate(
                    2,
                    2,
                    700L * mib,
                    700L * mib);

            AssertEqual(640L * mib, projection.InstallVolumeRequiredBytes,
                "Install volume needs 256 MiB per clone plus 128 MiB headroom.");
            AssertEqual(640L * mib, projection.SystemVolumeRequiredBytes,
                "System volume needs staging, cache entries and headroom.");
            AssertEqual(700L * mib, projection.InstallVolumeObservedFreeBytes,
                "UI must receive the observed install-volume free space.");
            AssertEqual(700L * mib, projection.SystemVolumeObservedFreeBytes,
                "UI must receive the observed system-volume free space.");
            AssertTrue(projection.HasEnoughSpace,
                "Both independently checked volumes have enough free space.");
        }

#if !NETFRAMEWORK
        private static void MsiLocalModuleOperatorInventoryRestoresStableAssignments()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "krs-lm-msi-inventory-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string installRoot = Path.Combine(
                    Path.GetPathRoot(Path.GetFullPath(root)),
                    "Program Files",
                    "Regime");
                LocalModuleMsiInventoryItem item =
                    new LocalModuleMsiInventoryItem
                    {
                        SchemaVersion =
                            LocalModuleMsiInventoryItem.CurrentSchemaVersion,
                        OwnershipMarker =
                            LocalModuleMsiInventoryItem.ExpectedOwnershipMarker,
                        Inn = "1234567894",
                        CloneOrdinal = 0,
                        ApiPort = 5995,
                        DatabasePort = 5984,
                        InstallRoot = installRoot,
                        InstalledByApplication = false,
                        PreExisting = true,
                        ManifestSha256 = new string('a', 64)
                    };
                string path = Path.Combine(root, item.Inn + ".json");
                using (FileStream stream = File.Create(path))
                {
                    new System.Runtime.Serialization.Json
                        .DataContractJsonSerializer(
                            typeof(LocalModuleMsiInventoryItem))
                        .WriteObject(stream, item);
                }

                LocalModuleMsiOperatorInventorySnapshot snapshot =
                    new LocalModuleMsiOperatorInventoryReader(root).Read();

                AssertEqual(1, snapshot.Assignments.Count,
                    "The operator projection must restore one stable assignment.");
                AssertEqual("1234567894", snapshot.Assignments[0].Inn,
                    "The saved INN must survive application restart.");
                AssertEqual(5995, snapshot.BaseInventory.ApiPort,
                    "The base port must come from observed inventory, not a clone formula.");
                AssertTrue(snapshot.Assignments[0].BaseWasPreExisting,
                    "Removal UI must preserve a vendor-owned base product.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
#endif

        private static void MsiLocalModuleRemovalWordingPreservesVendorBase()
        {
            string preserving =
                LocalModuleRemovalMessagePolicy
                    .BuildRemoveEverythingConfirmation(2, 1, true);
            string owned =
                LocalModuleRemovalMessagePolicy
                    .BuildRemoveEverythingConfirmation(2, 1, false);

            AssertContains(preserving, "останется установленным");
            AssertFalse(owned.IndexOf(
                    "останется установленным",
                    StringComparison.Ordinal) >= 0,
                "An application-installed base must not be promised preservation.");
        }

        private static void DirectControllerSetupDefersLmReadinessWithoutFailingControllers()
        {
            AssertTrue(
                DirectControllerSetupPolicy.IsDeferredLocalModuleWarning(
                    "error 2025: ЛМ контроллер не найден"),
                "A missing LM must be a warning after the controller is ready.");
            AssertTrue(
                DirectControllerSetupPolicy.IsDeferredLocalModuleWarning(
                    "error 2055: версия ЛМ ЧЗ пока не определена"),
                "A not-yet-initialized LM must be a warning.");
            AssertFalse(
                DirectControllerSetupPolicy.IsDeferredLocalModuleWarning(
                    "HTTP 500: access denied"),
                "An unrelated binding failure must remain actionable.");
            AssertTrue(
                DirectControllerSetupPolicy.IsComplete(1, 1, 0, false),
                "The one-KKT controller flow must complete without a special branch.");
            AssertFalse(
                DirectControllerSetupPolicy.IsComplete(2, 1, 1, false),
                "One controller failure must not be reported as a complete batch.");
        }

        private static void LmGatewayPlannerNeverAdoptsOfficialBaseService()
        {
            LmGatewayDiscovery discovery = CreateLmDiscovery(CreateLmKkt("00105700000001", "1234567894"));
            IList<LmGatewayDraft> drafts = new List<LmGatewayDraft>
            {
                CreateLmGatewayDraft("00105700000001", "10.20.30.40", "5995", null, null)
            };
            IList<LmServiceInventoryItem> inventory = new List<LmServiceInventoryItem>
            {
                new LmServiceInventoryItem
                {
                    ServiceName = "esm-lm-controller",
                    Role = LmServiceRole.VerifiedOfficial,
                    Ports = new LmGatewayPorts(50063, 5063),
                    IsRunning = true
                }
            };

            LmGatewayPlan plan = LmGatewayPlanner.Build(
                discovery,
                drafts,
                inventory,
                CreateLmPortPolicy(),
                new List<TcpListenerSnapshotItem>());

            AssertEqual(1, plan.Items.Count, "Expected one managed plan row.");
            AssertTrue(plan.Items[0].IsValid, "Official service must only reserve its observed ports.");
            AssertEqual("krs-esm-lm-00105700000001", plan.Items[0].Spec.ServiceName,
                "The official base service must never be adopted.");
            AssertEqual(LmServiceRole.Managed, plan.Items[0].Spec.Role,
                "Every planned per-KKT service must have the managed role.");
            AssertFalse(plan.Items[0].Spec.Ports.GrpcPort == 50063 || plan.Items[0].Spec.Ports.RestPort == 5063,
                "Official ports must remain reserved.");
        }

        private static void LmGatewayPlannerAllocatesSequentialLocalPorts()
        {
            LmGatewayDiscovery discovery = CreateLmDiscovery(
                CreateLmKkt("00105700000003", "500100732259"),
                CreateLmKkt("00105700000001", "1234567894"),
                CreateLmKkt("00105700000002", "7707083893"));
            IList<LmGatewayDraft> drafts = new List<LmGatewayDraft>
            {
                CreateLmGatewayDraft("00105700000003", "lm-three.example", "5995", null, null),
                CreateLmGatewayDraft("00105700000001", "10.20.30.41", "5995", null, null),
                CreateLmGatewayDraft("00105700000002", "2001:db8::2", "5995", null, null)
            };

            LmGatewayPlan plan = LmGatewayPlanner.Build(
                discovery,
                drafts,
                new List<LmServiceInventoryItem>(),
                CreateLmPortPolicy(),
                new List<TcpListenerSnapshotItem>());

            AssertEqual(3, plan.Items.Count, "Expected one plan row per KKT even when INNs repeat.");
            for (int index = 0; index < plan.Items.Count; index++)
            {
                AssertTrue(plan.Items[index].IsValid, "Expected every happy-path row to be valid.");
                AssertEqual(LmServiceRole.Managed, plan.Items[index].Spec.Role,
                    "Each KKT must receive an independent managed service.");
                AssertEqual(55000 + index, plan.Items[index].Spec.Ports.GrpcPort,
                    "Expected deterministic sequential gRPC allocation.");
                AssertEqual(15000 + index, plan.Items[index].Spec.Ports.RestPort,
                    "Expected deterministic sequential REST allocation.");
            }

            AssertEqual("00105700000001", plan.Items[0].Kkt.KktSerial,
                "Allocation order must use ordinal KKT serial order.");
            AssertEqual("00105700000003", plan.Items[2].Kkt.KktSerial,
                "Discovery order must not affect allocation.");

            LmGatewayDiscovery sameInnDiscovery = CreateLmDiscovery(
                CreateLmKkt("00105700000011", "7707083893"),
                CreateLmKkt("00105700000012", "7707083893"));
            IList<LmGatewayDraft> sameInnDrafts = new List<LmGatewayDraft>
            {
                CreateLmGatewayDraft("00105700000011", "10.20.31.11", "5995", null, null),
                CreateLmGatewayDraft("00105700000012", "10.20.31.12", "5995", null, null)
            };
            LmGatewayPlan sameInnPlan = LmGatewayPlanner.Build(
                sameInnDiscovery,
                sameInnDrafts,
                new List<LmServiceInventoryItem>(),
                CreateLmPortPolicy(),
                new List<TcpListenerSnapshotItem>());
            AssertEqual(2, sameInnPlan.Items.Count,
                "Equal INNs must not collapse independent physical KKT rows.");
            AssertFalse(string.Equals(
                    sameInnPlan.Items[0].Spec.ServiceName,
                    sameInnPlan.Items[1].Spec.ServiceName,
                    StringComparison.Ordinal),
                "Service identity must be derived from KKT serial rather than INN.");
        }

        private static void LmGatewayPlannerKeepsOwnedAndSkipsForeignListener()
        {
            const string existingSerial = "00105700000001";
            const string newSerial = "00105700000002";
            string existingService = LmServiceIdentity.CreateName(existingSerial);
            LmGatewayDiscovery discovery = CreateLmDiscovery(
                CreateLmKkt(existingSerial, "1234567894"),
                CreateLmKkt(newSerial, "7707083893"));
            IList<LmGatewayDraft> drafts = new List<LmGatewayDraft>
            {
                CreateLmGatewayDraft(existingSerial, "10.20.30.41", "5995", null, null),
                CreateLmGatewayDraft(newSerial, "10.20.30.42", "5995", null, null)
            };
            IList<LmServiceInventoryItem> inventory = new List<LmServiceInventoryItem>
            {
                new LmServiceInventoryItem
                {
                    KktSerial = existingSerial,
                    ServiceName = existingService,
                    Role = LmServiceRole.Managed,
                    Ports = new LmGatewayPorts(55000, 15000),
                    Target = new LmGatewayTarget("10.20.30.41", 5995),
                    IsRunning = true
                }
            };
            IList<TcpListenerSnapshotItem> listeners = new List<TcpListenerSnapshotItem>
            {
                new TcpListenerSnapshotItem(55000, existingService, true),
                new TcpListenerSnapshotItem(15000, existingService, true),
                new TcpListenerSnapshotItem(55001, "foreign-service", true),
                new TcpListenerSnapshotItem(15001, null, false)
            };

            LmGatewayPlan plan = LmGatewayPlanner.Build(
                discovery, drafts, inventory, CreateLmPortPolicy(), listeners);

            AssertEqual(55000, plan.Items[0].Spec.Ports.GrpcPort,
                "A proven owned listener must preserve the managed assignment.");
            AssertEqual(15000, plan.Items[0].Spec.Ports.RestPort,
                "A proven owned listener must preserve both assigned ports.");
            AssertEqual(LmGatewayPlanAction.NoChange, plan.Items[0].Action,
                "A running matching service with proven listeners needs no service mutation.");
            AssertEqual(55002, plan.Items[1].Spec.Ports.GrpcPort,
                "A foreign listener in either column must reserve the number globally.");
            AssertEqual(15002, plan.Items[1].Spec.Ports.RestPort,
                "An ambiguous listener must be skipped instead of adopted.");
        }

        private static void LmListenerSnapshotProjectsOnlyReadyManagedOwners()
        {
            const string readySerial = "00105700000001";
            const string unreadySerial = "00105700000002";
            string readyService = LmServiceIdentity.CreateName(readySerial);
            IList<LmServiceInventoryItem> inventory = new List<LmServiceInventoryItem>
            {
                new LmServiceInventoryItem
                {
                    KktSerial = readySerial,
                    ServiceName = readyService,
                    Role = LmServiceRole.Managed,
                    Ports = new LmGatewayPorts(55000, 15000),
                    IsRunning = true,
                    IsReady = true
                },
                new LmServiceInventoryItem
                {
                    KktSerial = unreadySerial,
                    ServiceName = LmServiceIdentity.CreateName(unreadySerial),
                    Role = LmServiceRole.Managed,
                    Ports = new LmGatewayPorts(55001, 15001),
                    IsRunning = true,
                    IsReady = false
                }
            };

            IList<TcpListenerSnapshotItem> snapshot = LmTcpListenerSnapshotBuilder.Build(
                new[] { 55000, 15000, 55001, 15001, 55002 },
                inventory);

            AssertEqual(5, snapshot.Count, "Every occupied port must remain in the snapshot.");
            AssertTrue(snapshot[0].IsOwnerVerified && snapshot[0].OwnerServiceName == readyService,
                "A readiness-probed managed gRPC listener must retain its verified owner.");
            AssertTrue(snapshot[1].IsOwnerVerified && snapshot[1].OwnerServiceName == readyService,
                "A readiness-probed managed REST listener must retain its verified owner.");
            AssertFalse(snapshot[2].IsOwnerVerified,
                "An unready managed service must not claim an occupied port.");
            AssertFalse(snapshot[3].IsOwnerVerified,
                "Both ports of an unready managed service must remain unverified.");
            AssertFalse(snapshot[4].IsOwnerVerified,
                "An unrelated occupied port must remain unverified.");
        }

        private static void LmGatewayPlannerPreservesMatchingManagedAssignment()
        {
            const string serial = "00105700000001";
            LmGatewayDiscovery discovery = CreateLmDiscovery(CreateLmKkt(serial, "1234567894"));
            IList<LmGatewayDraft> drafts = new List<LmGatewayDraft>
            {
                CreateLmGatewayDraft(serial, "lm-one.example", "5995", null, null)
            };
            IList<LmServiceInventoryItem> inventory = new List<LmServiceInventoryItem>
            {
                new LmServiceInventoryItem
                {
                    KktSerial = serial,
                    ServiceName = LmServiceIdentity.CreateName(serial),
                    Role = LmServiceRole.Managed,
                    Ports = new LmGatewayPorts(55007, 15007),
                    Target = new LmGatewayTarget("lm-one.example", 5995),
                    IsRunning = false
                }
            };

            LmGatewayPlan plan = LmGatewayPlanner.Build(
                discovery,
                drafts,
                inventory,
                CreateLmPortPolicy(),
                new List<TcpListenerSnapshotItem>());

            AssertTrue(plan.Items[0].IsValid, "A stopped but matching managed assignment must remain valid.");
            AssertEqual(55007, plan.Items[0].Spec.Ports.GrpcPort,
                "A valid existing assignment must not be renumbered.");
            AssertEqual(15007, plan.Items[0].Spec.Ports.RestPort,
                "Both existing ports must be preserved.");
            AssertEqual(LmGatewayPlanAction.StartManagedService, plan.Items[0].Action,
                "A matching stopped service only needs to start.");
        }

        private static void LmGatewayPlannerRejectsUnsafeTargetAndAllPortConflicts()
        {
            LmGatewayDiscovery discovery = CreateLmDiscovery(
                CreateLmKkt("00105700000001", "1234567894"),
                CreateLmKkt("00105700000002", "7707083893"),
                CreateLmKkt("00105700000003", "500100732259"),
                CreateLmKkt("00105700000004", "781122334455"));
            IList<LmGatewayDraft> drafts = new List<LmGatewayDraft>
            {
                CreateLmGatewayDraft("00105700000001", "https://lm.example/path", "5995", null, null),
                CreateLmGatewayDraft("00105700000002", "10.20.30.42", "5995", "54999", "15001"),
                CreateLmGatewayDraft("00105700000003", "localhost", "55000", "55000", "15000"),
                CreateLmGatewayDraft("00105700000004", "10.20.30.44", "5995", null, null)
            };
            IList<TcpListenerSnapshotItem> listeners = new List<TcpListenerSnapshotItem>
            {
                new TcpListenerSnapshotItem(55001, "foreign-service", true),
                new TcpListenerSnapshotItem(15001, null, false)
            };

            LmGatewayPlan plan = LmGatewayPlanner.Build(
                discovery, drafts, new List<LmServiceInventoryItem>(), CreateLmPortPolicy(), listeners);

            AssertEqual(LmGatewayPlanAction.Blocked, plan.Items[0].Action,
                "A URL is not a plain target address and must be blocked.");
            AssertContains(plan.Items[0].ServiceValidation.JoinMessages(), "Адрес");
            AssertEqual(LmGatewayPlanAction.Blocked, plan.Items[1].Action,
                "An explicit port outside its approved pool must be blocked.");
            AssertContains(plan.Items[1].ServiceValidation.JoinMessages(), "диапазон");
            AssertEqual(LmGatewayPlanAction.Blocked, plan.Items[2].Action,
                "A loopback target must not reuse a local controller port.");
            AssertContains(plan.Items[2].ServiceValidation.JoinMessages(), "целевого ЛМ");
            AssertTrue(plan.Items[3].IsValid, "Invalid preceding drafts must not consume automatic ports.");
            AssertEqual(55000, plan.Items[3].Spec.Ports.GrpcPort,
                "The first valid automatic row must retain the first free gRPC number.");
            AssertEqual(15000, plan.Items[3].Spec.Ports.RestPort,
                "The first valid automatic row must retain the first free REST number.");

            LmGatewayDiscovery overlappingDiscovery = CreateLmDiscovery(
                CreateLmKkt("00105700000011", "1234567894"),
                CreateLmKkt("00105700000012", "7707083893"),
                CreateLmKkt("00105700000013", "500100732259"));
            IList<LmGatewayDraft> overlappingDrafts = new List<LmGatewayDraft>
            {
                CreateLmGatewayDraft("00105700000011", "10.20.31.11", "5995", "20000", "20001"),
                CreateLmGatewayDraft("00105700000012", "10.20.31.12", "5995", "20002", "20000"),
                CreateLmGatewayDraft("00105700000013", "10.20.31.13", "5995", null, null)
            };
            LmManagedPortPolicy overlappingPolicy = new LmManagedPortPolicy(
                new TcpPortRange(20000, 20003),
                new TcpPortRange(20000, 20003));

            LmGatewayPlan overlappingPlan = LmGatewayPlanner.Build(
                overlappingDiscovery,
                overlappingDrafts,
                new List<LmServiceInventoryItem>(),
                overlappingPolicy,
                new List<TcpListenerSnapshotItem>());

            AssertTrue(overlappingPlan.Items[0].IsValid, "Expected the first explicit pair to be accepted.");
            AssertEqual(LmGatewayPlanAction.Blocked, overlappingPlan.Items[1].Action,
                "A port used in the other local column must still be treated as occupied.");
            AssertTrue(overlappingPlan.Items[2].IsValid,
                "A duplicate invalid row must not reserve its otherwise unused port.");
            AssertEqual(20002, overlappingPlan.Items[2].Spec.Ports.GrpcPort,
                "Automatic allocation must skip both numbers of the earlier pair.");
            AssertEqual(20003, overlappingPlan.Items[2].Spec.Ports.RestPort,
                "Automatic allocation must keep both local columns globally unique.");

            string normalizedLoopback;
            bool isLoopback;
            AssertTrue(LmGatewayInputValidator.TryNormalizeTargetAddress(
                "::ffff:127.0.0.1", out normalizedLoopback, out isLoopback),
                "IPv4-mapped IPv6 loopback must be accepted as an IP literal.");
            AssertTrue(isLoopback, "All loopback representations must participate in local-port conflicts.");
            AssertEqual("127.0.0.1", normalizedLoopback, "Loopback comparison must use one canonical form.");

            ValidationResult unicodeTarget = LmGatewayInputValidator.ValidateTarget(
                new LmGatewayTarget("lm-\u0430.example", 5995));
            AssertFalse(unicodeTarget.IsValid, "Ambiguous Unicode DNS names must be rejected.");
        }

        private static void ManagedLmServiceSpecContainsNoCredentials()
        {
            Type[] types =
            {
                typeof(ManagedLmServiceSpec),
                typeof(LmGatewayDraft),
                typeof(LmGatewayPlanItem),
                typeof(LmGatewayPlan)
            };

            for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                System.Reflection.PropertyInfo[] properties = types[typeIndex].GetProperties();
                for (int propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
                {
                    string name = properties[propertyIndex].Name;
                    string typeName = properties[propertyIndex].PropertyType.FullName ?? string.Empty;
                    AssertFalse(name.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0,
                        "Managed service plan must not retain a password.");
                    AssertFalse(name.IndexOf("Login", StringComparison.OrdinalIgnoreCase) >= 0,
                        "Managed service plan must not retain a login.");
                    AssertFalse(name.IndexOf("Credential", StringComparison.OrdinalIgnoreCase) >= 0,
                        "Managed service plan must not retain credentials.");
                    AssertFalse(name.IndexOf("Binary", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Argument", StringComparison.OrdinalIgnoreCase) >= 0,
                        "Managed service plan must not accept arbitrary executable values.");
                    AssertFalse(typeName.IndexOf("Credential", StringComparison.OrdinalIgnoreCase) >= 0,
                        "Managed service plan must not reference a credential type.");
                }
            }
        }

        private static LmGatewayKkt CreateLmKkt(string serial, string inn)
        {
            return new LmGatewayKkt
            {
                InstanceId = serial,
                KktSerial = serial,
                KktInn = inn,
                ServiceState = "RUNNING"
            };
        }

        private static LmGatewayDraft CreateLmGatewayDraft(
            string serial,
            string targetAddress,
            string targetPort,
            string grpcPort,
            string restPort)
        {
            return new LmGatewayDraft
            {
                KktSerial = serial,
                TargetAddress = targetAddress,
                TargetPort = targetPort,
                GrpcPort = grpcPort,
                RestPort = restPort
            };
        }

        private static LmManagedPortPolicy CreateLmPortPolicy()
        {
            return new LmManagedPortPolicy(
                new TcpPortRange(55000, 55009),
                new TcpPortRange(15000, 15009));
        }

        private static void ManagedLmPlannerGroupsKktByInn()
        {
            ManagedLocalModulePlan plan = ManagedLocalModulePlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000003", "7707083893"),
                    CreateLmKkt("00105700000002", "1234567894"),
                    CreateLmKkt("00105700000001", "1234567894")
                },
                new List<ManagedKktAssignment>(),
                new List<ManagedLocalModuleAssignment>(),
                new List<TcpListenerSnapshotItem>());

            AssertTrue(plan.IsValid, "A conflict-free local plan must be valid.");
            AssertEqual(2, plan.Items.Count, "Two unique INNs must create two local modules.");
            AssertEqual("1234567894", plan.Items[0].Module.Inn,
                "The first module must belong to the first stable KKT owner.");
            AssertEqual(1, plan.Items[0].Module.ModuleOrdinal,
                "The first INN must inherit the first KKT ordinal.");
            AssertEqual(5995, plan.Items[0].Module.ApiPort,
                "LM number 1 must use API port 5995.");
            AssertEqual(5984, plan.Items[0].Module.DatabasePort,
                "LM number 1 must use database port 5984.");
            AssertEqual(43691, plan.Items[0].Module.EpmdPort,
                "LM number 1 must use its own EPMD port.");
            AssertEqual(2, plan.Items[0].KktAssignments.Count,
                "Both KKT of one INN must point to the same LM.");
            AssertEqual(1, plan.Items[0].KktAssignments[0].KktOrdinal,
                "Serial ordering must define KKT number 1.");
            AssertEqual(45001, plan.Items[0].KktAssignments[0].GrpcPort,
                "KKT number 1 must use gRPC 45001.");
            AssertEqual(2, plan.Items[0].KktAssignments[1].KktOrdinal,
                "The second KKT of the same INN keeps its own number.");
            AssertEqual(45002, plan.Items[0].KktAssignments[1].GrpcPort,
                "KKT number 2 must use gRPC 45002.");
            AssertEqual(3, plan.Items[1].Module.ModuleOrdinal,
                "The next unique INN must inherit its first linked KKT number.");
            AssertEqual(7995, plan.Items[1].Module.ApiPort,
                "LM number 3 must use API port 7995.");
        }

        private static void ManagedLmPlannerAssignsStableOrdinals()
        {
            ManagedKktAssignment savedKkt = new ManagedKktAssignment
            {
                KktSerial = "00105700000002",
                KktInn = "7707083893",
                KktOrdinal = 7,
                LocalModuleInstanceId = "lm-existing",
                GrpcPort = 45007,
                RestPort = 15007
            };
            ManagedLocalModuleAssignment savedModule = new ManagedLocalModuleAssignment
            {
                Inn = "7707083893",
                ModuleOrdinal = 7,
                InstanceId = "lm-existing",
                ApiPort = 11995,
                DatabasePort = 11984,
                EpmdPort = 43697,
                RuntimeVersion = "2.6.1"
            };

            ManagedLocalModulePlan plan = ManagedLocalModulePlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894"),
                    CreateLmKkt("00105700000002", "7707083893")
                },
                new List<ManagedKktAssignment> { savedKkt },
                new List<ManagedLocalModuleAssignment> { savedModule },
                new List<TcpListenerSnapshotItem>());

            ManagedLocalModulePlanItem existing = plan.FindByInn("7707083893");
            AssertTrue(existing != null, "The saved INN group must remain present.");
            AssertEqual(7, existing.Module.ModuleOrdinal,
                "A refresh must not renumber an existing LM.");
            AssertEqual("lm-existing", existing.Module.InstanceId,
                "A refresh must retain the owned instance id.");
            AssertEqual(45007, existing.KktAssignments[0].GrpcPort,
                "A refresh must retain the KKT controller assignment.");
            AssertEqual(1, plan.FindByInn("1234567894").Module.ModuleOrdinal,
                "A new INN must take the lowest available preferred KKT number.");
        }

        private static void ManagedLmPlannerBlocksOccupiedPorts()
        {
            ManagedLocalModulePlan plan = ManagedLocalModulePlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894")
                },
                new List<ManagedKktAssignment>(),
                new List<ManagedLocalModuleAssignment>(),
                new List<TcpListenerSnapshotItem>
                {
                    new TcpListenerSnapshotItem(5995, "foreign-lm", false)
                });

            AssertFalse(plan.IsValid, "An occupied deterministic port must block the plan.");
            AssertFalse(plan.Items[0].IsValid, "The affected INN group must be invalid.");
            AssertContains(plan.Items[0].JoinValidationMessages(), "5995");
            AssertEqual(5995, plan.Items[0].Module.ApiPort,
                "The planner must report the deterministic port instead of shifting it.");
        }

        private static void ManagedLmPortPreflightIgnoresOwnedPortsAndBlocksNewConflicts()
        {
            ManagedKktAssignment savedKkt = new ManagedKktAssignment
            {
                KktSerial = "00105700000001",
                KktInn = "1234567894",
                KktOrdinal = 1,
                GrpcPort = 45001,
                RestPort = 15001
            };
            ManagedLocalModuleAssignment savedModule =
                new ManagedLocalModuleAssignment
                {
                    Inn = "1234567894",
                    ModuleOrdinal = 1,
                    InstanceId = "lm-existing",
                    ApiPort = 5995,
                    DatabasePort = 5984,
                    EpmdPort = 43691,
                    RuntimeVersion = "2.6.1"
                };
            ManagedLocalModulePlan plan = ManagedLocalModulePlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894"),
                    CreateLmKkt("00105700000002", "1234567894")
                },
                new List<ManagedKktAssignment> { savedKkt },
                new List<ManagedLocalModuleAssignment> { savedModule },
                new List<TcpListenerSnapshotItem>());

            IList<int> conflicts = ManagedLocalModulePortPreflight.FindConflicts(
                plan,
                new List<ManagedKktAssignment> { savedKkt },
                new List<ManagedLocalModuleAssignment> { savedModule },
                new List<int> { 5995, 5984, 43691, 45001, 15001, 45002 });

            AssertEqual(1, conflicts.Count,
                "Existing managed listeners must be left to helper ownership checks.");
            AssertEqual(45002, conflicts[0],
                "An occupied port required by the new KKT must fail preflight.");

            plan.Items[0].Module.ApiPort = 6995;
            conflicts = ManagedLocalModulePortPreflight.FindConflicts(
                plan,
                new List<ManagedKktAssignment> { savedKkt },
                new List<ManagedLocalModuleAssignment> { savedModule },
                new List<int> { 6995 });
            AssertEqual(1, conflicts.Count,
                "An edited port is new ownership and must be checked.");
            AssertEqual(6995, conflicts[0],
                "The changed API port must be reported exactly.");
        }

        private static void ManagedLmRequestRepeatsOneEndpointForSharedInn()
        {
            ManagedLocalModulePlan plan = ManagedLocalModulePlanner.Build(
                new List<LmGatewayKkt>
                {
                    CreateLmKkt("00105700000001", "1234567894"),
                    CreateLmKkt("00105700000002", "1234567894"),
                    CreateLmKkt("00105700000003", "7707083893")
                },
                new List<ManagedKktAssignment>(),
                new List<ManagedLocalModuleAssignment>(),
                new List<TcpListenerSnapshotItem>());

            IList<ManagedLocalModuleProvisioningItemRequest> request =
                ManagedLocalModuleRequestBuilder.Build(plan, "2.6.1");

            AssertEqual(3, request.Count,
                "The immutable helper plan must retain one row per KKT.");
            AssertEqual("1234567894", request[0].Inn,
                "The canary must be the first KKT of the first INN group.");
            AssertEqual("1234567894", request[1].Inn,
                "The second KKT of the same INN must follow its canary.");
            AssertEqual(request[0].LocalModuleOrdinal, request[1].LocalModuleOrdinal,
                "KKT of one INN must share the same LM number.");
            AssertEqual(request[0].ApiPort, request[1].ApiPort,
                "KKT of one INN must show the same LM endpoint.");
            AssertFalse(request[0].ControllerGrpcPort == request[1].ControllerGrpcPort,
                "Every KKT must retain its own controller port.");
        }

        private static void LmDefaultsUse45000GrpcPool()
        {
            LmGatewayDraft first = LmGatewayDraftDefaults.Create(
                CreateLmKkt("00105700000001", "1234567894"),
                1);
            LmGatewayDraft last = LmGatewayDraftDefaults.Create(
                CreateLmKkt("00105700000032", "7707083893"),
                32);

            AssertEqual("45001", first.GrpcPort,
                "The default must stay below the Windows dynamic port range.");
            AssertEqual("45032", last.GrpcPort,
                "The supported KKT range must end at gRPC 45032.");
        }

        private static void LmGatewayDraftDefaultsFollowKktOrdinal()
        {
            System.Reflection.MethodInfo create = typeof(LmGatewayDraftDefaults).GetMethod(
                "Create",
                new[] { typeof(LmGatewayKkt), typeof(int) });
            AssertTrue(create != null,
                "Ordinal-aware defaults are required to keep table numbers aligned with ports.");
            if (create == null)
            {
                return;
            }

            LmGatewayKkt kkt = new LmGatewayKkt
            {
                KktSerial = "00105700000001",
                KktInn = "1234567894"
            };
            LmGatewayDraft first = (LmGatewayDraft)create.Invoke(null, new object[] { kkt, 1 });
            LmGatewayDraft second = (LmGatewayDraft)create.Invoke(null, new object[] { kkt, 2 });
            LmGatewayDraft third = (LmGatewayDraft)create.Invoke(null, new object[] { kkt, 3 });

            AssertEqual("00105700000001", first.KktSerial, "The draft must retain the KKT identity.");
            AssertEqual("1234567894", first.KktInn, "The draft must be scoped to the current owner INN.");
            AssertEqual("127.0.0.1", first.TargetAddress, "The normal same-PC scenario must be ready immediately.");
            AssertEqual("5995", first.TargetPort, "KKT number 1 must use the first LM CHZ port.");
            AssertEqual("45001", first.GrpcPort, "KKT number 1 must end its gRPC port in 01.");
            AssertEqual("15001", first.RestPort, "KKT number 1 must end its REST port in 01.");
            AssertEqual("6995", second.TargetPort, "KKT number 2 must use the second LM CHZ port.");
            AssertEqual("45002", second.GrpcPort, "KKT number 2 must end its gRPC port in 02.");
            AssertEqual("15002", second.RestPort, "KKT number 2 must end its REST port in 02.");
            AssertEqual("7995", third.TargetPort, "KKT number 3 must use the third LM CHZ port.");
            AssertEqual("45003", third.GrpcPort, "KKT number 3 must end its gRPC port in 03.");
            AssertEqual("15003", third.RestPort, "KKT number 3 must end its REST port in 03.");
        }

        private static void LmGatewayDraftDefaultsDoNotCrashOnExcessKkt()
        {
            LmGatewayDraft draft = LmGatewayDraftDefaults.Create(
                new LmGatewayKkt { KktSerial = "00105700000033", KktInn = "1234567894" },
                33);

            AssertEqual("45033", draft.GrpcPort,
                "An excess row must remain visible for validation instead of crashing discovery.");
            AssertEqual("15033", draft.RestPort,
                "An excess row must keep its ordinal when reporting a range conflict.");
        }

        private static void LmGatewayDraftSettingsPersistOnlyMatchingNonsecretEndpoint()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "esm_lm_draft_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "drafts.json");
                LmGatewayDraftSettingsStore store = new LmGatewayDraftSettingsStore(path);
                store.Save(new List<LmGatewayDraft>
                {
                    new LmGatewayDraft
                    {
                        KktSerial = "00105700000001",
                        KktInn = "1234567894",
                        TargetAddress = "10.20.30.41",
                        TargetPort = "5995",
                        GrpcPort = "55001",
                        RestPort = "15000"
                    },
                    new LmGatewayDraft
                    {
                        KktSerial = "00105700000002",
                        KktInn = "1111111111",
                        TargetAddress = "10.20.30.42",
                        TargetPort = "5995"
                    }
                });

                IList<LmGatewayDraft> loaded = store.LoadFor(new List<LmGatewayKkt>
                {
                    new LmGatewayKkt { KktSerial = "00105700000001", KktInn = "1234567894" },
                    new LmGatewayKkt { KktSerial = "00105700000002", KktInn = "2222222222" }
                });
                string raw = File.ReadAllText(path);

                AssertEqual(1, loaded.Count, "A draft from a different INN must not be restored.");
                AssertEqual("10.20.30.41", loaded[0].TargetAddress, "The matching nonsecret endpoint must survive restart.");
                AssertEqual("55001", loaded[0].GrpcPort,
                    "An explicit legacy gRPC choice must survive restart without silent migration.");
                AssertFalse(raw.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The settings format must have no password field.");
                AssertFalse(raw.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The settings format must have no login field.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void LmInventoryDisplaySeparatesOfficialControllerFromKktServices()
        {
            LmServiceInventoryItem official = new LmServiceInventoryItem
            {
                Role = LmServiceRole.VerifiedOfficial,
                ServiceName = "esm-lm-controller",
                IsRunning = true
            };
            LmServiceInventoryItem managed = new LmServiceInventoryItem
            {
                Role = LmServiceRole.Managed,
                ServiceName = "krs-esm-lm-00105700000001",
                KktSerial = "00105700000001"
            };

            LmServiceInventoryDisplay display = LmServiceInventoryDisplay.Create(
                new List<LmServiceInventoryItem> { managed, official });

            AssertEqual(1, display.OfficialControllers.Count,
                "The official controller must have its own status presentation.");
            AssertEqual("esm-lm-controller", display.OfficialControllers[0].ServiceName,
                "The official status must identify the actual Windows service.");
            AssertEqual(1, display.InstanceServices.Count,
                "Only managed or foreign instance services may remain in the KKT grid projection.");
            AssertEqual("00105700000001", display.InstanceServices[0].KktSerial,
                "The managed KKT service must remain actionable in the grid.");
        }

        private static void LmBindingSessionDoesNotInventReadback()
        {
            LmGatewayBindingSession session = new LmGatewayBindingSession();
            session.ReplaceDiscovery(CreateLmDiscovery(
                new LmGatewayKkt
                {
                    InstanceId = "00105700000001",
                    KktSerial = "00105700000001",
                    KktInn = "1234567894",
                    ServiceState = "RUNNING"
                }));

            AssertEqual(1, session.Rows.Count, "Expected one row for the registered KKT.");
            AssertEqual("00105700000001", session.Rows[0].Kkt.KktSerial, "Expected discovered KKT identity.");
            AssertEqual("127.0.0.1", session.Rows[0].ControllerAddress, "Expected safe local controller default.");
            AssertEqual(string.Empty, session.Rows[0].ControllerGrpcPort, "A controller port must not be guessed.");
            AssertFalse(session.Rows[0].LastBindingStatus.HasValue,
                "Discovery alone must not claim a binding result before LM info readback.");
            AssertTrue(session.Rows[0].IsSelected,
                "A newly discovered row must be selected for the automatic all-KKT setup.");
        }

        private static void LmBindingSessionAppliesVerifiedReadback()
        {
            LmGatewayBindingSession session = new LmGatewayBindingSession();
            session.ReplaceDiscovery(CreateLmDiscovery(
                new LmGatewayKkt
                {
                    InstanceId = "00105700000001",
                    KktSerial = "00105700000001",
                    KktInn = "1234567894",
                    Port = "50401",
                    SoftPort = "51401"
                }));

            session.ApplyReadback(new List<LmGatewayReadbackObservation>
            {
                new LmGatewayReadbackObservation
                {
                    InstanceId = "00105700000001",
                    KktSerial = "00105700000001",
                    KktInn = "1234567894",
                    IsAvailable = true,
                    IdentityMatches = true,
                    HasLmConfiguration = true,
                    EndpointMatches = true,
                    LmAddress = "127.0.0.1",
                    LmPort = "5995",
                    LmStatus = "ready",
                    LmVersion = "2.0",
                    Details = "Подтверждено ЕСМ: ЛМ 127.0.0.1:5995."
                }
            });

            AssertEqual(LmGatewayBindingStatus.BindingVerified, session.Rows[0].LastBindingStatus.Value,
                "A documented matching readback must be visible as verified.");
            AssertEqual(string.Empty, session.Rows[0].ControllerGrpcPort,
                "The LM target port must never be mistaken for the controller gRPC port.");
            AssertEqual("5995", session.Rows[0].ObservedLmPort,
                "The observed LM target port must be kept separately.");
            AssertContains(session.Rows[0].LastMessage, "127.0.0.1:5995");
        }

        private static void LmBindingFallbackPreservesNewerReadback()
        {
            LmGatewayBindingSession session = new LmGatewayBindingSession();
            session.ReplaceDiscovery(CreateLmDiscovery(
                CreateLmKkt("00105700000001", "1234567894")));
            LmGatewayBindingOutcome accepted = new LmGatewayBindingOutcome();
            accepted.Results.Add(new LmGatewayBindingResult
            {
                KktSerial = "00105700000001",
                KktInn = "1234567894",
                Status = LmGatewayBindingStatus.BindingAccepted,
                Details = "PUT принят; read-back недоступен."
            });

            session.ApplyReadback(new List<LmGatewayReadbackObservation>
            {
                new LmGatewayReadbackObservation
                {
                    KktSerial = "00105700000001",
                    KktInn = "1234567894",
                    IsAvailable = true,
                    IdentityMatches = true,
                    HasLmConfiguration = true,
                    EndpointMatches = true,
                    LmAddress = "127.0.0.1",
                    LmPort = "5995",
                    Details = "Привязка подтверждена ЕСМ."
                }
            });
            session.ApplyOutcomeFallback(accepted);

            AssertEqual(LmGatewayBindingStatus.BindingVerified,
                session.Rows[0].LastBindingStatus.Value,
                "A stale accepted PUT must not overwrite a newer verified readback.");

            session.ApplyReadback(new List<LmGatewayReadbackObservation>
            {
                new LmGatewayReadbackObservation
                {
                    KktSerial = "00105700000001",
                    KktInn = "1234567894",
                    IsAvailable = true,
                    IdentityMatches = true,
                    HasLmConfiguration = true,
                    EndpointMatches = false,
                    Details = "Адрес ЛМ не совпадает."
                }
            });
            session.ApplyOutcomeFallback(accepted);

            AssertEqual(LmGatewayBindingStatus.RequiresAttention,
                session.Rows[0].LastBindingStatus.Value,
                "A stale accepted PUT must not overwrite a newer mismatch.");
            AssertContains(session.Rows[0].LastMessage, "не совпадает");

            session.ApplyReadback(new List<LmGatewayReadbackObservation>
            {
                new LmGatewayReadbackObservation
                {
                    KktSerial = "00105700000001",
                    KktInn = "1234567894",
                    IsAvailable = false,
                    Details = "Read-back недоступен."
                }
            });
            session.ApplyOutcomeFallback(accepted);

            AssertEqual(LmGatewayBindingStatus.BindingAccepted,
                session.Rows[0].LastBindingStatus.Value,
                "Accepted PUT must remain visible when final readback has no status.");
            AssertContains(session.Rows[0].LastMessage, "Read-back недоступен");

            session.ApplyReadback(new List<LmGatewayReadbackObservation>
            {
                new LmGatewayReadbackObservation
                {
                    KktSerial = "00105700000001",
                    KktInn = "1234567894",
                    IsAvailable = true,
                    IdentityMatches = true,
                    HasLmConfiguration = false,
                    Details = "ЕСМ не сообщает настроенную привязку."
                }
            });
            session.ApplyOutcomeFallback(accepted);

            AssertEqual(LmGatewayBindingStatus.RequiresAttention,
                session.Rows[0].LastBindingStatus.Value,
                "An available final readback without LM configuration must fail the accepted fallback.");

            LmGatewayBindingSession identitySession = new LmGatewayBindingSession();
            identitySession.ReplaceDiscovery(CreateLmDiscovery(
                CreateLmKkt("00105700000001", "1234567894")));
            identitySession.ApplyOutcome(accepted);
            identitySession.ApplyReadback(new List<LmGatewayReadbackObservation>
            {
                new LmGatewayReadbackObservation
                {
                    KktSerial = "00105700000001",
                    KktInn = "7707083893",
                    IsAvailable = true,
                    IdentityMatches = false,
                    HasLmConfiguration = true,
                    Details = "Ответ относится к другому ИНН."
                }
            });
            identitySession.ApplyOutcomeFallback(accepted);

            AssertEqual(LmGatewayBindingStatus.RequiresAttention,
                identitySession.Rows[0].LastBindingStatus.Value,
                "A final identity mismatch must override an earlier accepted PUT.");
        }

        private static void LmBindingSessionOrdersKktForStableOrdinals()
        {
            LmGatewayBindingSession session = new LmGatewayBindingSession();
            session.ReplaceDiscovery(CreateLmDiscovery(
                CreateLmKkt("00105700000003", "500100732259"),
                CreateLmKkt("00105700000001", "1234567894"),
                CreateLmKkt("00105700000002", "7707083893")));

            AssertEqual("00105700000001", session.Rows[0].Kkt.KktSerial,
                "Table number 1 must be stable regardless of ESM response order.");
            AssertEqual("00105700000002", session.Rows[1].Kkt.KktSerial,
                "Table number 2 must follow deterministic serial order.");
            AssertEqual("00105700000003", session.Rows[2].Kkt.KktSerial,
                "Table number 3 must follow deterministic serial order.");
        }

        private static void LmBindingSessionPreservesCurrentDraftsOnRefresh()
        {
            LmGatewayBindingSession session = new LmGatewayBindingSession();
            session.ReplaceDiscovery(CreateLmDiscovery(
                new LmGatewayKkt
                {
                    InstanceId = "00105700000001",
                    KktSerial = "00105700000001",
                    KktInn = "1234567894",
                    ServiceState = "STOPPED"
                },
                new LmGatewayKkt
                {
                    InstanceId = "00105700000002",
                    KktSerial = "00105700000002",
                    KktInn = "7707083893"
                }));
            AssertTrue(session.TryUpdateDraft(
                "00105700000001", "localhost", "50064", true), "Expected draft update for a known KKT.");

            session.ReplaceDiscovery(CreateLmDiscovery(
                new LmGatewayKkt
                {
                    InstanceId = "00105700000001",
                    KktSerial = "00105700000001",
                    KktInn = "1234567894",
                    ServiceState = "RUNNING"
                }));

            AssertEqual(1, session.Rows.Count, "A stale KKT must be removed after refresh.");
            AssertEqual("localhost", session.Rows[0].ControllerAddress, "Current-session address draft must survive refresh.");
            AssertEqual("50064", session.Rows[0].ControllerGrpcPort, "Current-session port draft must survive refresh.");
            AssertTrue(session.Rows[0].IsSelected, "Current-session selection must survive refresh.");
            AssertEqual("RUNNING", session.Rows[0].Kkt.ServiceState, "Fresh ESM state must replace the old snapshot.");
        }

        private static void LmBindingSessionBuildsPlanOnlyForSelectedKkt()
        {
            LmGatewayBindingSession session = new LmGatewayBindingSession();
            session.ReplaceDiscovery(CreateLmDiscovery(
                new LmGatewayKkt
                {
                    InstanceId = "00105700000001",
                    KktSerial = "00105700000001",
                    KktInn = "1234567894"
                },
                new LmGatewayKkt
                {
                    InstanceId = "00105700000002",
                    KktSerial = "00105700000002",
                    KktInn = "7707083893"
                }));
            session.TryUpdateDraft("00105700000001", "127.0.0.1", "50063", false);
            session.TryUpdateDraft("00105700000002", "127.0.0.1", "50064", true);

            LmGatewayBindingPlan plan = session.BuildSelectedPlan();
            session.TryUpdateDraft("00105700000002", "127.0.0.1", "59999", true);

            AssertEqual(1, plan.Items.Count, "Only explicitly selected KKT must enter the binding plan.");
            AssertEqual("00105700000002", plan.Items[0].Kkt.KktSerial, "Expected the selected KKT.");
            AssertEqual("50064", plan.Items[0].Input.ControllerGrpcPort, "Plan must retain a stable draft snapshot.");
            AssertTrue(plan.Items[0].IsValid, "Expected selected row to produce a valid plan item.");
        }

        private static void LmBindingSessionBuildsExactOneKktPlan()
        {
            LmGatewayBindingSession session = new LmGatewayBindingSession();
            session.ReplaceDiscovery(CreateLmDiscovery(
                CreateLmKkt("00105700000001", "1234567894"),
                CreateLmKkt("00105700000002", "7707083893")));
            session.TryUpdateDraft("00105700000001", "127.0.0.1", "45001", true);
            session.TryUpdateDraft("00105700000002", "127.0.0.1", "45002", true);

            LmGatewayBindingPlan plan = session.BuildPlanFor("00105700000002");
            session.TryUpdateDraft("00105700000002", "127.0.0.1", "45999", true);

            AssertEqual(1, plan.Items.Count,
                "A row action must never bind another selected KKT.");
            AssertEqual("00105700000002", plan.Items[0].Kkt.KktSerial,
                "Expected the explicitly requested KKT.");
            AssertEqual("45002", plan.Items[0].Input.ControllerGrpcPort,
                "The exact plan must retain a stable controller-port snapshot.");
        }

        private static void LmBindingSessionIgnoresUnknownExactKkt()
        {
            LmGatewayBindingSession session = new LmGatewayBindingSession();
            session.ReplaceDiscovery(CreateLmDiscovery(
                CreateLmKkt("00105700000001", "1234567894")));

            LmGatewayBindingPlan plan = session.BuildPlanFor("00105700000999");

            AssertEqual(0, plan.Items.Count,
                "An orphan or stale grid identity must not create a PUT plan.");
        }

        private static void LmBindingSessionCanSelectAllForAutomaticSetup()
        {
            LmGatewayBindingSession session = new LmGatewayBindingSession();
            session.ReplaceDiscovery(CreateLmDiscovery(
                CreateLmKkt("00105700000001", "1234567894"),
                CreateLmKkt("00105700000002", "7707083893")));
            session.TryUpdateDraft("00105700000001", "127.0.0.1", "55001", false);

            session.SelectAll();
            LmGatewayBindingPlan plan = session.BuildSelectedPlan();

            AssertTrue(session.Rows[0].IsSelected, "Full automatic setup must reselect the first KKT.");
            AssertTrue(session.Rows[1].IsSelected, "Full automatic setup must retain the second KKT.");
            AssertEqual(2, plan.Items.Count, "Full automatic setup must include every registered KKT.");
        }

        private static void LmBindingSessionDiscardsDraftAfterInnChanges()
        {
            const string serial = "00105700000001";
            LmGatewayBindingSession session = new LmGatewayBindingSession();
            session.ReplaceDiscovery(CreateLmDiscovery(
                new LmGatewayKkt
                {
                    InstanceId = serial,
                    KktSerial = serial,
                    KktInn = "1234567894"
                }));
            session.TryUpdateDraft(serial, "127.0.0.1", "50063", true);
            LmGatewayBindingOutcome outcome = new LmGatewayBindingOutcome();
            outcome.Results.Add(new LmGatewayBindingResult
            {
                InstanceId = serial,
                KktSerial = serial,
                KktInn = "1234567894",
                Status = LmGatewayBindingStatus.BindingAccepted,
                Details = "accepted"
            });
            session.ApplyOutcome(outcome);

            session.ReplaceDiscovery(CreateLmDiscovery(
                new LmGatewayKkt
                {
                    InstanceId = serial,
                    KktSerial = serial,
                    KktInn = "7707083893"
                }));

            AssertEqual("127.0.0.1", session.Rows[0].ControllerAddress,
                "A new INN must receive a fresh local-address default.");
            AssertEqual(string.Empty, session.Rows[0].ControllerGrpcPort,
                "A controller port from the previous INN must be discarded.");
            AssertTrue(session.Rows[0].IsSelected,
                "A KKT re-registered to another INN must join the automatic all-KKT setup.");
            AssertFalse(session.Rows[0].LastBindingStatus.HasValue,
                "A binding result from the previous INN must not survive refresh.");
            AssertEqual(1, session.InvalidatedKktSerials.Count,
                "The UI must be told to clear credentials for the replaced identity.");
            AssertEqual(serial, session.InvalidatedKktSerials[0],
                "Expected invalidation for the re-registered KKT serial.");
        }

        private static void LmBindingSessionMasksOutcomeDetails()
        {
            const string password = "session-secret-value";
            LmGatewayBindingSession session = new LmGatewayBindingSession();
            session.ReplaceDiscovery(CreateLmDiscovery(
                new LmGatewayKkt
                {
                    InstanceId = "00105700000001",
                    KktSerial = "00105700000001",
                    KktInn = "1234567894"
                }));
            LmGatewayBindingOutcome outcome = new LmGatewayBindingOutcome();
            outcome.Results.Add(new LmGatewayBindingResult
            {
                InstanceId = "00105700000001",
                KktSerial = "00105700000001",
                KktInn = "1234567894",
                Status = LmGatewayBindingStatus.BindingFailed,
                Details = "password=" + password
            });

            session.ApplyOutcome(outcome);

            AssertEqual(LmGatewayBindingStatus.BindingFailed, session.Rows[0].LastBindingStatus.Value,
                "Expected matching outcome status on the session row.");
            AssertFalse(session.Rows[0].LastMessage.Contains(password),
                "Session state rendered by the UI must not retain a reflected password.");
        }

        private static LmGatewayDiscovery CreateLmDiscovery(params LmGatewayKkt[] items)
        {
            LmGatewayDiscovery discovery = new LmGatewayDiscovery();
            for (int index = 0; index < items.Length; index++)
            {
                discovery.Items.Add(items[index]);
            }

            return discovery;
        }

        private static LmGatewayBindingInput CreateLmBindingInput(
            string kktSerial,
            string kktInn,
            string address,
            string port)
        {
            return new LmGatewayBindingInput
            {
                KktSerial = kktSerial,
                KktInn = kktInn,
                ControllerAddress = address,
                ControllerGrpcPort = port
            };
        }

        private static void LmBindingWorkflowSendsItemsSequentially()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            LmGatewayBindingPlan plan = CreateValidLmBindingPlan(2);
            List<string> events = new List<string>();
            api.LmGatewayCallObserved = delegate(int callNumber)
            {
                events.Add("put:" + api.LmGatewayCalls[callNumber - 1].Id);
            };
            LmGatewayBindingWorkflow workflow = NewFastBindingWorkflow(api);

            LmGatewayBindingOutcome outcome = workflow.ExecuteAsync(
                "http://127.0.0.1:51077",
                plan,
                delegate(string kktSerial)
                {
                    events.Add("credentials:" + kktSerial);
                    return new LmGatewayCredentials { Login = "operator-" + kktSerial, Password = "test-password" };
                },
                null,
                CancellationToken.None).Result;

            AssertEqual(2, api.LmGatewayCalls.Count, "Expected one sequential PUT per valid KKT.");
            AssertEqual("credentials:00105700000001", events[0], "Credentials must be requested immediately before the first row.");
            AssertEqual("put:00105700000001", events[1], "Expected the first PUT before reading the next credentials.");
            AssertEqual("credentials:00105700000002", events[2], "Expected second-row credentials only after the first PUT.");
            AssertEqual("put:00105700000002", events[3], "Expected the second PUT last.");
            AssertEqual("127.0.0.1", api.LmGatewayCalls[0].Request.Address, "Expected normalized local controller address.");
            AssertEqual(50063, api.LmGatewayCalls[0].Request.Port, "Expected the first controller gRPC port.");
            AssertEqual(LmGatewayBindingStatus.BindingAccepted, outcome.Results[0].Status, "Expected accepted first binding.");
            AssertEqual(LmGatewayBindingStatus.BindingAccepted, outcome.Results[1].Status, "Expected accepted second binding.");
        }

        private static void LmBindingWorkflowVerifiesAcceptedSettingsThroughInfo()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmInfoResponses.Enqueue(Success(CreateLmInfoJson(
                "00105700000001", "1234567894", "127.0.0.1", 5995)));
            LmGatewayBindingWorkflow workflow = NewFastBindingWorkflow(api);

            LmGatewayBindingOutcome outcome = workflow.ExecuteAsync(
                "http://127.0.0.1:51077",
                CreateValidLmBindingPlan(1),
                delegate { return new LmGatewayCredentials { Login = "operator", Password = "test-password" }; },
                null,
                CancellationToken.None).Result;

            AssertEqual(1, api.LmInfoCalls, "A successful PUT must be followed by one documented readback.");
            AssertEqual(LmGatewayBindingStatus.BindingVerified, outcome.Results[0].Status,
                "Matching KKT identity and target LM endpoint must be verified.");
            AssertContains(outcome.Results[0].Details, "127.0.0.1:5995");
        }

        private static void LmBindingWorkflowKeepsAcceptedWhenInfoIsUnavailable()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmInfoResponses.Enqueue(ConnectionFailure());
            LmGatewayBindingWorkflow workflow = NewFastBindingWorkflow(api);

            LmGatewayBindingOutcome outcome = workflow.ExecuteAsync(
                "http://127.0.0.1:51077",
                CreateValidLmBindingPlan(1),
                delegate { return new LmGatewayCredentials { Login = "operator", Password = "test-password" }; },
                null,
                CancellationToken.None).Result;

            AssertEqual(LmGatewayBindingStatus.BindingAccepted, outcome.Results[0].Status,
                "An unavailable optional readback must not turn a successful PUT into a failure.");
            AssertContains(outcome.Results[0].Details, "не удалось проверить");
        }

        private static void LmBindingWorkflowFlagsInfoMismatch()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmInfoResponses.Enqueue(Success(CreateLmInfoJson(
                "00105700000001", "1234567894", "127.0.0.1", 6995)));
            LmGatewayBindingWorkflow workflow = NewFastBindingWorkflow(api);

            LmGatewayBindingOutcome outcome = workflow.ExecuteAsync(
                "http://127.0.0.1:51077",
                CreateValidLmBindingPlan(1),
                delegate { return new LmGatewayCredentials { Login = "operator", Password = "test-password" }; },
                null,
                CancellationToken.None).Result;

            AssertEqual(LmGatewayBindingStatus.RequiresAttention, outcome.Results[0].Status,
                "A reachable readback with another endpoint must require attention.");
            AssertContains(outcome.Results[0].Details, "6995");
        }

        private static string CreateLmInfoJson(
            string kktSerial,
            string kktInn,
            string address,
            int port)
        {
            return "{\"kktSerial\":\"" + kktSerial + "\",\"kktInn\":\"" + kktInn + "\"," +
                "\"lm\":{\"version\":\"2.0\",\"status\":\"ready\",\"ip\":\"" + address +
                "\",\"port\":" + port.ToString() + ",\"login\":\"operator\",\"pass\":\"secret\"}}";
        }

        private static void LmBindingWorkflowWaitsUntilEsmReportsTheBinding()
        {
            // Поле: ЕСМ принимает привязку, а сообщает её обратно только
            // через несколько секунд. Одна проверка сразу после запроса
            // объявляла новую привязку неподтверждённой.
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmInfoResponses.Enqueue(Success(CreateLmInfoWithoutLocalModuleJson(
                "00105700000001", "1234567894")));
            api.LmInfoResponses.Enqueue(Success(CreateLmInfoWithoutLocalModuleJson(
                "00105700000001", "1234567894")));
            api.LmInfoResponses.Enqueue(Success(CreateLmInfoJson(
                "00105700000001", "1234567894", "127.0.0.1", 5995)));
            LmGatewayBindingWorkflow workflow = NewFastBindingWorkflow(api);

            LmGatewayBindingOutcome outcome = workflow.ExecuteAsync(
                "http://127.0.0.1:51077",
                CreateValidLmBindingPlan(1),
                delegate { return new LmGatewayCredentials { Login = "operator", Password = "test-password" }; },
                null,
                CancellationToken.None).Result;

            AssertEqual(3, api.LmInfoCalls,
                "Expected the verification to poll until ESM reports the binding.");
            AssertEqual(LmGatewayBindingStatus.BindingVerified, outcome.Results[0].Status,
                "A binding reported a few seconds later must still be verified.");
        }

        private static void LmBindingDiagnosticsCarryTheInfoResponse()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmInfoResponses.Enqueue(Success(CreateLmInfoWithoutLocalModuleJson(
                "00105700000001", "1234567894")));
            LmGatewayBindingWorkflow workflow = NewFastBindingWorkflow(api);

            LmGatewayBindingOutcome outcome = workflow.ExecuteAsync(
                "http://127.0.0.1:51077",
                CreateValidLmBindingPlan(1),
                delegate { return new LmGatewayCredentials { Login = "operator", Password = "test-password" }; },
                null,
                CancellationToken.None).Result;

            AssertEqual(LmGatewayBindingStatus.RequiresAttention, outcome.Results[0].Status,
                "A missing LM configuration must still require attention after the wait.");
            AssertContains(outcome.Results[0].Diagnostics, "/api/v2/info");
            AssertContains(outcome.Results[0].Diagnostics, "00105700000001");
            AssertFalse(
                outcome.Results[0].Details.Contains("kktSerial"),
                "The raw response must stay out of the decision text.");
        }

        private static string CreateLmInfoWithoutLocalModuleJson(
            string kktSerial,
            string kktInn)
        {
            return "{\"kktSerial\":\"" + kktSerial +
                "\",\"kktInn\":\"" + kktInn + "\"}";
        }

        private static void LmBindingWorkflowSkipsInvalidItemAndContinues()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            LmGatewayDiscovery discovery = CreateLmDiscovery(
                new LmGatewayKkt { InstanceId = "00105700000001", KktSerial = "00105700000001", KktInn = "1234567894" },
                new LmGatewayKkt { InstanceId = "00105700000002", KktSerial = "00105700000002", KktInn = "7707083893" });
            IList<LmGatewayBindingInput> inputs = new List<LmGatewayBindingInput>
            {
                CreateLmBindingInput("00105700000002", "7707083893", "127.0.0.1", "50064")
            };
            LmGatewayBindingPlan plan = LmGatewayBindingPlanner.Build(discovery, inputs);
            List<string> credentialCalls = new List<string>();
            LmGatewayBindingWorkflow workflow = NewFastBindingWorkflow(api);

            LmGatewayBindingOutcome outcome = workflow.ExecuteAsync(
                "http://127.0.0.1:51077",
                plan,
                delegate(string serial)
                {
                    credentialCalls.Add(serial);
                    return new LmGatewayCredentials { Login = "operator", Password = "test-password" };
                },
                null,
                CancellationToken.None).Result;

            AssertEqual(LmGatewayBindingStatus.Invalid, outcome.Results[0].Status, "Invalid row must be recorded.");
            AssertEqual(LmGatewayBindingStatus.BindingAccepted, outcome.Results[1].Status, "Valid row after it must still execute.");
            AssertEqual(1, credentialCalls.Count, "Invalid row must not request credentials.");
            AssertEqual("00105700000002", credentialCalls[0], "Expected credentials only for the valid KKT.");
            AssertEqual(1, api.LmGatewayCalls.Count, "Invalid row must not call PUT.");
        }

        private static void LmBindingWorkflowMarksLostResponseForAttention()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmGatewayResponses.Enqueue(ConnectionFailure());
            LmGatewayBindingWorkflow workflow = NewFastBindingWorkflow(api);

            LmGatewayBindingOutcome outcome = workflow.ExecuteAsync(
                "http://127.0.0.1:51077",
                CreateValidLmBindingPlan(1),
                delegate { return new LmGatewayCredentials { Login = "operator", Password = "test-password" }; },
                null,
                CancellationToken.None).Result;

            AssertEqual(1, api.LmGatewayCalls.Count, "Lost response must not trigger an automatic retry.");
            AssertEqual(LmGatewayBindingStatus.RequiresAttention, outcome.Results[0].Status,
                "Unknown remote outcome must require manual attention.");
        }

        private static void LmBindingWorkflowDoesNotRetryPermanentHttpError()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmGatewayResponses.Enqueue(Failure(400, "{\"error\":\"invalid settings\"}"));
            LmGatewayBindingWorkflow workflow = NewFastBindingWorkflow(api);

            LmGatewayBindingOutcome outcome = workflow.ExecuteAsync(
                "http://127.0.0.1:51077",
                CreateValidLmBindingPlan(1),
                delegate { return new LmGatewayCredentials { Login = "operator", Password = "test-password" }; },
                null,
                CancellationToken.None).Result;

            AssertEqual(1, api.LmGatewayCalls.Count, "Permanent HTTP error must be recorded without retry.");
            AssertEqual(LmGatewayBindingStatus.BindingFailed, outcome.Results[0].Status,
                "Expected a permanent binding failure.");
        }

        private static void LmBindingWorkflowPreservesPartialResultsOnCancellation()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            api.LmGatewayCallObserved = delegate(int callNumber)
            {
                if (callNumber == 1)
                {
                    cancellation.Cancel();
                }
            };
            int credentialCalls = 0;
            LmGatewayBindingWorkflow workflow = NewFastBindingWorkflow(api);

            LmGatewayBindingOutcome outcome = workflow.ExecuteAsync(
                "http://127.0.0.1:51077",
                CreateValidLmBindingPlan(3),
                delegate
                {
                    credentialCalls++;
                    return new LmGatewayCredentials { Login = "operator", Password = "test-password" };
                },
                null,
                cancellation.Token).Result;

            AssertTrue(outcome.Cancelled, "Expected a cancelled outcome.");
            AssertEqual(3, outcome.Results.Count, "Completed and untouched rows must all remain visible.");
            AssertEqual(LmGatewayBindingStatus.BindingAccepted, outcome.Results[0].Status, "First completed row must be preserved.");
            AssertEqual(LmGatewayBindingStatus.Cancelled, outcome.Results[1].Status, "Second untouched row must be cancelled.");
            AssertEqual(LmGatewayBindingStatus.Cancelled, outcome.Results[2].Status, "Third untouched row must be cancelled.");
            AssertEqual(1, credentialCalls, "Untouched rows must not request credentials.");
            AssertEqual(1, api.LmGatewayCalls.Count, "Untouched rows must not call PUT.");
        }

        private static void LmBindingWorkflowProgressNeverContainsPassword()
        {
            const string password = "workflow-secret-value";
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmGatewayResponses.Enqueue(new ApiResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                RequestBody = "{\"password\":\"" + password + "\"}",
                ResponseBody = "{\"password\":\"" + password + "\"}",
                DecodedMessage = "password=" + password
            });
            List<LmGatewayBindingProgress> progressItems = new List<LmGatewayBindingProgress>();
            LmGatewayBindingWorkflow workflow = NewFastBindingWorkflow(api);

            LmGatewayBindingOutcome outcome = workflow.ExecuteAsync(
                "http://127.0.0.1:51077",
                CreateValidLmBindingPlan(1),
                delegate { return new LmGatewayCredentials { Login = "operator", Password = password }; },
                delegate(LmGatewayBindingProgress progress) { progressItems.Add(progress); },
                CancellationToken.None).Result;

            AssertTrue(progressItems.Count > 0, "Expected binding progress.");
            for (int index = 0; index < progressItems.Count; index++)
            {
                LmGatewayBindingProgress progress = progressItems[index];
                string progressText = (progress.KktSerial ?? string.Empty) + (progress.Stage ?? string.Empty) +
                    (progress.Message ?? string.Empty);
                if (progress.Response != null)
                {
                    progressText += (progress.Response.RequestBody ?? string.Empty) +
                        (progress.Response.ResponseBody ?? string.Empty) +
                        (progress.Response.DecodedMessage ?? string.Empty);
                }
                AssertFalse(progressText.Contains(password), "Progress object must not retain the password.");
            }
            AssertFalse(outcome.Results[0].Details.Contains(password), "Result details must not retain the password.");
        }

        private static LmGatewayBindingPlan CreateValidLmBindingPlan(int count)
        {
            LmGatewayDiscovery discovery = new LmGatewayDiscovery();
            IList<LmGatewayBindingInput> inputs = new List<LmGatewayBindingInput>();
            for (int index = 1; index <= count; index++)
            {
                string serial = "001057" + index.ToString("00000000");
                string inn = index % 2 == 0 ? "7707083893" : "1234567894";
                discovery.Items.Add(new LmGatewayKkt
                {
                    InstanceId = serial,
                    KktSerial = serial,
                    KktInn = inn,
                    Port = (50400 + index).ToString(),
                    SoftPort = (51400 + index).ToString()
                });
                LmGatewayBindingInput bindingInput = CreateLmBindingInput(
                    serial,
                    inn,
                    "127.0.0.1",
                    (50062 + index).ToString());
                bindingInput.ExpectedLmAddress = "127.0.0.1";
                bindingInput.ExpectedLmPort = ((index + 4) * 1000 + 995).ToString();
                inputs.Add(bindingInput);
            }

            return LmGatewayBindingPlanner.Build(discovery, inputs);
        }

        private static void ApiClientPreservesInjectedTimeout()
        {
            HttpClient httpClient = new HttpClient(new RecordingHttpHandler());
            httpClient.Timeout = TimeSpan.FromSeconds(7);

            new TspiotApiClient(httpClient);

            AssertEqual(TimeSpan.FromSeconds(7), httpClient.Timeout, "An injected HttpClient timeout must remain owned by its caller.");
        }

        private static void InstructionSelectorUsesNewestFileTime()
        {
            string directory = Path.Combine(Path.GetTempPath(), "esm_instruction_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string v9 = Path.Combine(directory, "Instruction-MultiKKT-v9.pdf");
                string v10 = Path.Combine(directory, "Instruction-MultiKKT-v10.pdf");
                File.WriteAllText(v9, "v9");
                File.WriteAllText(v10, "v10");
                File.SetLastWriteTimeUtc(v10, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(v9, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

                string selected = InstructionFileSelector.SelectNewest(new string[] { v9, v10 });

                AssertEqual(v9, selected, "Expected the most recently written instruction file.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void CanonicalHasherValidatesSha256Syntax()
        {
            AssertTrue(CanonicalLmPlanHasher.IsWellFormedSha256(new string('a', 64)),
                "A lowercase 64-character SHA-256 value must be accepted.");
            AssertTrue(CanonicalLmPlanHasher.IsWellFormedSha256(new string('F', 64)),
                "An uppercase 64-character SHA-256 value must be accepted.");
            AssertFalse(CanonicalLmPlanHasher.IsWellFormedSha256(new string('a', 63)),
                "A truncated fingerprint must be rejected.");
            AssertFalse(CanonicalLmPlanHasher.IsWellFormedSha256(new string('z', 64)),
                "A non-hex fingerprint must be rejected.");
            AssertFalse(CanonicalLmPlanHasher.IsWellFormedSha256(null),
                "A missing fingerprint must be rejected.");
        }

        private static void InstructionSelectorFallsBackToFieldGuide()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "esm_instruction_fallback_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string guide = Path.Combine(directory, "FIELD_TEST_1.6.3.2.md");
                File.WriteAllText(guide, "guide");

                AssertEqual(guide, InstructionFileSelector.SelectAvailable(directory),
                    "The compact package field guide must keep the Help command usable.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void KktServiceStatesAreLocalizedForOperators()
        {
            AssertEqual("Запущена", KktServiceStateFormatter.ToDisplayText("Running"),
                "Running must not leak into the Russian operator table.");
            AssertEqual("Остановлена", KktServiceStateFormatter.ToDisplayText("STOPPED"),
                "Stopped must not leak into the Russian operator table.");
            AssertEqual("Запускается", KktServiceStateFormatter.ToDisplayText("StartPending"),
                "A pending start must remain distinguishable.");
        }

        private static void UnknownKktServiceStateRemainsDiagnosable()
        {
            AssertEqual("VendorSpecificState",
                KktServiceStateFormatter.ToDisplayText(" VendorSpecificState "),
                "An unknown vendor state must remain visible instead of being hidden.");
            AssertEqual("—", KktServiceStateFormatter.ToDisplayText(null),
                "An absent state must have an explicit empty marker.");
        }

        private static void DeletionPlannerAllowsPrimaryKktWithStrongerConfirmation()
        {
            KktDeletionPlan plan = KktDeletionPlanner.Build(new List<KktInstanceInfo>
            {
                Instance("00105700000001", "50401", "0"),
                Instance("00105700000002", "50402", "51402")
            });

            AssertTrue(plan.HasReliablePrimary, "Expected a reliable primary KKT.");
            AssertTrue(plan.Candidates[0].CanDelete, "Primary KKT must be removable after stronger confirmation.");
            AssertTrue(plan.Candidates[0].IsPrimary, "Expected the first KKT to be marked as primary.");
            AssertTrue(plan.Candidates[1].CanDelete, "Additional KKT should be deletable.");
        }

        private static void DeletionPlannerBlocksAmbiguousPrimaryKkt()
        {
            KktDeletionPlan missingPrimary = KktDeletionPlanner.Build(new List<KktInstanceInfo>
            {
                Instance("00105700000001", "50402", "51402"),
                Instance("00105700000002", "50403", "51403")
            });
            KktDeletionPlan duplicatePrimary = KktDeletionPlanner.Build(new List<KktInstanceInfo>
            {
                Instance("00105700000001", "50401", "0"),
                Instance("00105700000002", "50401", "0")
            });

            AssertFalse(missingPrimary.HasReliablePrimary, "Missing primary marker must block deletion.");
            AssertFalse(missingPrimary.Candidates[1].CanDelete, "Deletion must fail closed without a primary marker.");
            AssertFalse(duplicatePrimary.HasReliablePrimary, "Duplicate primary markers must be ambiguous.");
            AssertFalse(duplicatePrimary.Candidates[1].CanDelete, "Deletion must fail closed with duplicate primary markers.");
        }

        private static void DeletionConfirmationRequiresLastFourDigits()
        {
            AssertTrue(KktDeletionConfirmation.Matches("00105700000002", "0002"), "Expected matching suffix.");
            AssertFalse(KktDeletionConfirmation.Matches("00105700000002", "00002"), "Only four digits must be accepted.");
            AssertFalse(KktDeletionConfirmation.Matches("00105700000002", "0003"), "Wrong suffix must be rejected.");
            AssertFalse(KktDeletionConfirmation.Matches("00105700000002", "00A2"), "Non-digits must be rejected.");
        }

        private static void DeletionConfirmationSupportsExactFullSerial()
        {
            AssertTrue(KktDeletionConfirmation.MatchesFullSerial("00105700000001", " 00105700000001 "),
                "Expected the exact full serial to match after trimming.");
            AssertFalse(KktDeletionConfirmation.MatchesFullSerial("00105700000001", "0001"),
                "The four-digit suffix must not authorize primary KKT deletion.");
            AssertFalse(KktDeletionConfirmation.MatchesFullSerial("00105700000001", "00105700000002"),
                "A different full serial must be rejected.");
        }

        private static void DeletionWorkflowRequiresFullSerialForPrimaryKkt()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponses.Enqueue(Success(TwoInstancesJson()));
            api.InstancesResponses.Enqueue(Success(TwoInstancesJson()));
            api.DeleteResponses.Enqueue(Success("{}"));
            api.InstancesResponses.Enqueue(Success("{\"instances\":[{\"id\":\"00105700000002\",\"port\":50402,\"softPort\":51402,\"serviceState\":\"Работает\"}]}"));
            KktDeletionWorkflow workflow = CreateDeletionWorkflow(api);

            KktDeletionOutcome suffixOutcome = workflow.DeleteAsync(
                "http://127.0.0.1:51077", "00105700000001", "0001", null, CancellationToken.None).Result;
            KktDeletionOutcome fullOutcome = workflow.DeleteAsync(
                "http://127.0.0.1:51077", "00105700000001", "00105700000001", null, CancellationToken.None).Result;

            AssertTrue(suffixOutcome.IsBlocked, "The short suffix must not authorize primary KKT deletion.");
            AssertFalse(suffixOutcome.IsSuccess, "Blocked deletion cannot succeed.");
            AssertTrue(fullOutcome.IsSuccess, "The exact full serial must authorize verified primary KKT deletion.");
            AssertEqual(1, api.DeleteCalls, "Exactly one DELETE must be sent after full confirmation.");
            AssertEqual("00105700000001", api.LastDeletedId, "Expected the selected primary KKT id.");
        }

        private static void DeletionWorkflowVerifiesAdditionalKktRemoval()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponses.Enqueue(Success(TwoInstancesJson()));
            api.DeleteResponses.Enqueue(Success("{}"));
            api.InstancesResponses.Enqueue(Success("{\"instances\":[{\"id\":\"00105700000001\",\"port\":50401,\"softPort\":0,\"serviceState\":\"Работает\"}]}"));
            KktDeletionWorkflow workflow = CreateDeletionWorkflow(api);
            List<ApiResponse> responses = new List<ApiResponse>();

            KktDeletionOutcome outcome = workflow.DeleteAsync(
                "http://127.0.0.1:51077", "00105700000002", "0002", responses.Add, CancellationToken.None).Result;

            AssertTrue(outcome.IsSuccess, "Expected verified deletion.");
            AssertTrue(outcome.DeleteRequestAccepted, "Expected successful DELETE response.");
            AssertEqual(1, api.DeleteCalls, "Expected one DELETE request.");
            AssertEqual("00105700000002", api.LastDeletedId, "Expected selected KKT id.");
            AssertEqual(3, responses.Count, "Expected initial GET, DELETE and verification GET in the log callback.");
        }

        private static void DeletionWorkflowReportsUnverifiedRemoval()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponses.Enqueue(Success(TwoInstancesJson()));
            api.DeleteResponses.Enqueue(Success("{}"));
            api.InstancesResponses.Enqueue(Success(TwoInstancesJson()));
            api.InstancesResponses.Enqueue(Success(TwoInstancesJson()));
            api.InstancesResponses.Enqueue(Success(TwoInstancesJson()));
            KktDeletionWorkflow workflow = CreateDeletionWorkflow(api);

            KktDeletionOutcome outcome = workflow.DeleteAsync(
                "http://127.0.0.1:51077", "00105700000002", "0002", null, CancellationToken.None).Result;

            AssertFalse(outcome.IsSuccess, "Instance still present must not be reported as success.");
            AssertTrue(outcome.DeleteRequestAccepted, "DELETE itself succeeded.");
            AssertContains(outcome.Message, "не подтверждено");
            AssertEqual(3, outcome.VerificationAttempts, "Expected bounded verification retries.");
        }

        private static KktInstanceInfo Instance(string id, string port, string softPort)
        {
            return new KktInstanceInfo
            {
                Id = id,
                Port = port,
                SoftPort = softPort,
                ServiceState = "Работает"
            };
        }

        private static string TwoInstancesJson()
        {
            return "{\"instances\":[" +
                "{\"id\":\"00105700000001\",\"port\":50401,\"softPort\":0,\"serviceState\":\"Работает\"}," +
                "{\"id\":\"00105700000002\",\"port\":50402,\"softPort\":51402,\"serviceState\":\"Работает\"}]}";
        }

        private static KktDeletionWorkflow CreateDeletionWorkflow(FakeTspiotApiClient api)
        {
            return new KktDeletionWorkflow(
                api,
                delegate(TimeSpan delay, CancellationToken token) { return Task.FromResult(0); });
        }
        private static void RemoteBaseUrlProducesWarning()
        {
            ValidationResult result = TspiotInputValidator.ValidateBulkSettings("http://10.20.30.40:51077", "4041");

            AssertTrue(result.IsValid, "Remote URL must remain available after confirmation.");
            AssertContains(result.JoinWarnings(), "не является локальным адресом");
        }

        private static void BaseUrlRejectsCredentialsAndQuery()
        {
            ValidationResult credentials = TspiotInputValidator.ValidateBulkSettings(
                "https://alice:secret@example.com:51077", "4041");
            ValidationResult query = TspiotInputValidator.ValidateBulkSettings(
                "http://127.0.0.1:51077?token=ABC123", "4041");

            AssertFalse(credentials.IsValid, "Credentials in base URL must be rejected.");
            AssertFalse(query.IsValid, "Query in base URL must be rejected.");
            AssertContains(credentials.JoinMessages(), "учетные данные");
            AssertContains(query.JoinMessages(), "query");
        }

        private static void UnicodeDigitsAreRejected()
        {
            TspiotFormInput input = CreateValidInput();
            input.KktSerial = "0010570000000１";

            ValidationResult result = TspiotInputValidator.ValidatePut(input, true);

            AssertFalse(result.IsValid, "Expected non-ASCII digit to be rejected.");
            AssertContains(result.JoinMessages(), "ASCII-цифр");
        }

        private static void AssertRequest(RecordedHttpRequest request, string method, string url)
        {
            AssertEqual(method, request.Method, "Expected HTTP method.");
            AssertEqual(url, request.Url, "Expected request URL.");
        }

        private static void BulkWorkflowTreatsEmpty204AsNoInstances()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = new ApiResponse
            {
                IsSuccess = true,
                StatusCode = 204,
                ResponseBody = string.Empty
            };
            api.DkktResponse = Success(
                "{\"kkt\":[{\"kktSerial\":\"00105700000001\"," +
                "\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4042", null, CancellationToken.None).Result;

            AssertTrue(discovery.IsValid, "A clean ESM returning HTTP 204 must produce a valid plan.");
            AssertEqual(1, discovery.Items.Count, "The physical KKT must remain available for registration.");
            AssertEqual(
                "00105700000001",
                discovery.Items[0].Item.Input.KktSerial,
                "Expected the discovered KKT to be planned after an empty instance response.");
            AssertEqual(
                "50401",
                discovery.Items[0].Item.Input.Port,
                "The first KKT on a clean ESM must use the first service port.");
            AssertEqual(
                "51401",
                discovery.Items[0].Item.Input.SoftPort,
                "The first KKT on a clean ESM must expose the first cash-software port.");
        }

        private static void BulkWorkflowSkipsPutAfterPostCompletesRegistration()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success(
                "{\"kkt\":[{\"kktSerial\":\"00105700000001\"," +
                "\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.AddResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Success(
                "{\"state\":\"Зарегистрирован\",\"clientPort\":51401,\"regData\":{" +
                "\"kktSerial\":\" 00105700000001 \"," +
                "\"fnSerial\":\" 7300000000000001 \"," +
                "\"kktInn\":\" 1234567894  \"}}"));
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4042", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(1, api.AddCalls, "Expected one POST.");
            AssertEqual(0, api.RegisterCalls,
                "A matching complete regData response after POST must suppress redundant PUT.");
            AssertEqual(BulkKktRegistrationStatus.Registered, outcome.Results[0].Status,
                "POST-completed registration must be reported as registered.");
        }

        private static void BulkWorkflowDoesNotSkipPutForUnregisteredState()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success(
                "{\"kkt\":[{\"kktSerial\":\"00105700000001\"," +
                "\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.AddResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Success(
                "{\"state\":\"Не зарегистрирован\",\"clientPort\":51401,\"regData\":{" +
                "\"kktSerial\":\"00105700000001\"," +
                "\"fnSerial\":\"7300000000000001\"," +
                "\"kktInn\":\"1234567894\"}}"));
            api.RegisterResponses.Enqueue(Success("{\"tspiotId\":\"1\"}"));
            EnqueueRegisteredReadback(
                api, "00105700000001", "7300000000000001", "1234567894");
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4042", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(1, api.RegisterCalls,
                "Matching regData must not suppress PUT while ESM says the instance is unregistered.");
            AssertEqual(BulkKktRegistrationStatus.Registered, outcome.Results[0].Status,
                "Only the successful PUT may complete registration in this state.");
        }

        private static void BulkWorkflowBlocksMismatchedRegistrationAfterPost()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success(
                "{\"kkt\":[{\"kktSerial\":\"00105700000001\"," +
                "\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.AddResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Success(
                "{\"clientPort\":51401,\"regData\":{" +
                "\"kktSerial\":\"00105700000001\"," +
                "\"fnSerial\":\"7300000000000001\"," +
                "\"kktInn\":\"7707083893\"}}"));
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4042", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(0, api.RegisterCalls,
                "A mismatched completed registration must never be overwritten with PUT.");
            AssertEqual(BulkKktRegistrationStatus.InspectionFailed, outcome.Results[0].Status,
                "Mismatched regData must stop the item for operator inspection.");
            AssertContains(outcome.Results[0].Details, "не совпадают");
        }

        private static void BulkWorkflowResumesIncompleteExistingInstance()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[{\"id\":\"00105700000001\",\"port\":50401,\"softPort\":0}]}");
            api.DkktResponse = Success("{\"kkt\":[{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.InstanceResponses.Enqueue(Success("{\"clientPort\":51401}"));
            api.SettingsResponse = Success("[]");
            api.RegisterResponses.Enqueue(Success("{\"tspiotId\":\"1\"}"));
            EnqueueRegisteredReadback(
                api, "00105700000001", "7300000000000001", "1234567894");
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4041", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(1, discovery.Items.Count, "Expected one resumable item.");
            AssertFalse(discovery.Items[0].RequiresAdd, "Existing item must skip POST.");
            AssertEqual(0, api.AddCalls, "Expected no POST.");
            AssertEqual(1, api.RegisterCalls, "Expected one PUT.");
            AssertEqual(BulkKktRegistrationStatus.RecoveredRegistration, outcome.Results[0].Status, "Expected recovered registration.");
        }

        private static void BulkWorkflowContinuesAfterAddFailure()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success("{\"kkt\":[" +
                "{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}," +
                "{\"kktSerial\":\"00105700000002\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.AddResponses.Enqueue(Failure(500, "{\"error\":{\"code\":9999}}"));
            api.AddResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Success("{\"clientPort\":51402}"));
            api.RegisterResponses.Enqueue(Success("{}"));
            EnqueueRegisteredReadback(
                api, "00105700000002", "7300000000000001", "1234567894");
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4041", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(2, api.AddCalls, "Expected both POST attempts.");
            AssertEqual(1, api.RegisterCalls, "Expected second KKT PUT.");
            AssertEqual(BulkKktRegistrationStatus.AddFailed, outcome.Results[0].Status, "Expected first add failure.");
            AssertEqual(BulkKktRegistrationStatus.Registered, outcome.Results[1].Status, "Expected second registration success.");
        }

        private static void BulkWorkflowRetriesServiceNotStarted()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success("{\"kkt\":[{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.AddResponses.Enqueue(Failure(500, "{\"error\":{\"code\":1013}}"));
            api.AddResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Success("{\"clientPort\":51401}"));
            api.RegisterResponses.Enqueue(Success("{}"));
            EnqueueRegisteredReadback(
                api, "00105700000001", "7300000000000001", "1234567894");
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4041", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(2, api.AddCalls, "Expected transient POST retry.");
            AssertEqual(BulkKktRegistrationStatus.Registered, outcome.Results[0].Status, "Expected successful retry.");
        }

        private static void BulkWorkflowRetriesConnectionFailureDuringAdd()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success("{\"kkt\":[{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.AddResponses.Enqueue(ConnectionFailure());
            api.AddResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Success("{\"clientPort\":51402}"));
            api.RegisterResponses.Enqueue(Success("{}"));
            EnqueueRegisteredReadback(
                api, "00105700000001", "7300000000000001", "1234567894");
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4041", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(2, api.AddCalls, "Expected POST retry after a connection failure.");
            AssertEqual(BulkKktRegistrationStatus.Registered, outcome.Results[0].Status, "Expected registration after POST retry.");
        }

        private static void BulkWorkflowRetriesConnectionFailureDuringRegistration()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success("{\"kkt\":[{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.AddResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Success("{\"clientPort\":51402}"));
            api.RegisterResponses.Enqueue(ConnectionFailure());
            api.RegisterResponses.Enqueue(Success("{}"));
            EnqueueRegisteredReadback(
                api, "00105700000001", "7300000000000001", "1234567894");
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4041", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(2, api.RegisterCalls, "Expected PUT retry after a connection failure.");
            AssertEqual(BulkKktRegistrationStatus.Registered, outcome.Results[0].Status, "Expected successful PUT retry.");
        }

        private static void BulkWorkflowPlansSuppliedSequentialVcomDevices()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = NoContent();
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);
            IList<DkktDeviceInfo> devices = new List<DkktDeviceInfo>();
            devices.Add(new DkktDeviceInfo
            {
                KktSerial = "00105700000001",
                FnSerial = "7300000000000001",
                KktInn = "1234567894"
            });
            devices.Add(new DkktDeviceInfo
            {
                KktSerial = "00105700000002",
                FnSerial = "7300000000000002",
                KktInn = "7707083893"
            });

            BulkRegistrationDiscovery discovery = workflow
                .DiscoverFromDevicesAsync(
                    "http://127.0.0.1:51077",
                    "4042",
                    devices,
                    null,
                    CancellationToken.None).Result;

            AssertTrue(discovery.IsValid, "Sequentially observed devices must produce a valid plan.");
            AssertEqual(2, discovery.Items.Count, "Every supplied real KKT must be planned.");
            AssertEqual(0, api.DkktCalls, "Supplied VCOM observations must not be replaced by a shared dkktList read.");
            AssertEqual("50401", discovery.Items[0].Item.Input.Port, "First supplied KKT must keep the first deterministic pair.");
            AssertEqual("51402", discovery.Items[1].Item.Input.SoftPort, "Second supplied KKT must keep the second deterministic pair.");
        }

        private static void BulkWorkflowWaitsForRegisteredStateAfterPut()
        {
            FakeTspiotApiClient api = CreateSinglePendingKktApi();
            api.InstanceResponses.Enqueue(Success(
                "{\"state\":\"Не зарегистрирован\",\"clientPort\":51401}"));
            api.RegisterResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Success(RegistrationDetailsJson(
                "Не зарегистрирован",
                "00105700000001",
                "7300000000000001",
                "1234567894")));
            api.InstanceResponses.Enqueue(Success(RegistrationDetailsJson(
                "Зарегистрирован",
                " 00105700000001 ",
                " 7300000000000001 ",
                " 1234567894  ")));
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4042", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(BulkKktRegistrationStatus.Registered, outcome.Results[0].Status,
                "PUT must succeed only after registered readback.");
            AssertEqual(3, api.InstanceCalls,
                "Expected one post-POST inspection and two post-PUT readbacks.");
        }

        private static void BulkWorkflowRejectsMismatchedReadbackAfterPut()
        {
            FakeTspiotApiClient api = CreateSinglePendingKktApi();
            api.InstanceResponses.Enqueue(Success(
                "{\"state\":\"Не зарегистрирован\",\"clientPort\":51401}"));
            api.RegisterResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Success(RegistrationDetailsJson(
                "Зарегистрирован",
                "00105700000001",
                "7300000000000001",
                "7707083893")));
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4042", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(BulkKktRegistrationStatus.InspectionFailed, outcome.Results[0].Status,
                "A registered response for another identity must be blocked.");
            AssertContains(outcome.Results[0].Details, "не совпадают");
        }

        private static void BulkWorkflowTimesOutUnconfirmedPut()
        {
            FakeTspiotApiClient api = CreateSinglePendingKktApi();
            api.InstanceResponses.Enqueue(Success(
                "{\"state\":\"Не зарегистрирован\",\"clientPort\":51401}"));
            api.RegisterResponses.Enqueue(Success("{}"));
            for (int attempt = 0; attempt < 30; attempt++)
            {
                api.InstanceResponses.Enqueue(Success(RegistrationDetailsJson(
                    "Не зарегистрирован",
                    "00105700000001",
                    "7300000000000001",
                    "1234567894")));
            }
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4042", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(BulkKktRegistrationStatus.RegistrationFailed, outcome.Results[0].Status,
                "An accepted PUT without registered readback must not authorize a local kit.");
            AssertContains(outcome.Results[0].Details, "не подтверждена");
            AssertEqual(31, api.InstanceCalls,
                "The bounded confirmation loop must stop after thirty post-PUT reads.");
        }

        private static FakeTspiotApiClient CreateSinglePendingKktApi()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success(
                "{\"kkt\":[{\"kktSerial\":\"00105700000001\"," +
                "\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.AddResponses.Enqueue(Success("{}"));
            return api;
        }

        private static string RegistrationDetailsJson(
            string state,
            string serial,
            string fnSerial,
            string inn)
        {
            return "{\"state\":\"" + state +
                "\",\"clientPort\":51401,\"regData\":{" +
                "\"kktSerial\":\"" + serial +
                "\",\"fnSerial\":\"" + fnSerial +
                "\",\"kktInn\":\"" + inn + "\"}}";
        }

        private static void EnqueueRegisteredReadback(
            FakeTspiotApiClient api,
            string serial,
            string fnSerial,
            string inn)
        {
            api.InstanceResponses.Enqueue(Success(RegistrationDetailsJson(
                "Зарегистрирован",
                serial,
                fnSerial,
                inn)));
        }

        private static void BulkWorkflowExecutesOneSelectedKkt()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success("{\"kkt\":[" +
                "{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}," +
                "{\"kktSerial\":\"00105700000002\",\"fnSerial\":\"7300000000000002\",\"kktInn\":\"1234567894\"}]}");
            api.AddResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Success("{\"clientPort\":51402}"));
            api.RegisterResponses.Enqueue(Success("{}"));
            EnqueueRegisteredReadback(
                api, "00105700000002", "7300000000000002", "1234567894");
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);
            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4041", null, CancellationToken.None).Result;

            BulkKktRegistrationResult result = workflow.ExecuteItemAsync(
                discovery.Items[1],
                2,
                2,
                null,
                CancellationToken.None).Result;

            AssertEqual("00105700000002", result.KktSerial,
                "Only the selected immutable work item must be returned.");
            AssertEqual(BulkKktRegistrationStatus.Registered, result.Status,
                "The selected KKT must complete POST/readiness/PUT.");
            AssertEqual(1, api.AddCalls,
                "Executing one row must not start the preceding KKT.");
            AssertEqual(1, api.RegisterCalls,
                "Executing one row must issue one registration request.");
        }

        private static void BulkWorkflowRecoversWhenRetryReportsExistingInstance()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success("{\"kkt\":[{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.AddResponses.Enqueue(Failure(500, "{\"error\":{\"code\":1013}}"));
            api.AddResponses.Enqueue(Failure(400, "{\"error\":{\"code\":1010}}"));
            api.InstanceResponses.Enqueue(Success("{\"clientPort\":51401}"));
            api.RegisterResponses.Enqueue(Success("{}"));
            EnqueueRegisteredReadback(
                api, "00105700000001", "7300000000000001", "1234567894");
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4041", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(2, api.AddCalls, "Expected POST retry.");
            AssertEqual(1, api.RegisterCalls, "Existing instance confirmed by GET should continue to PUT.");
            AssertEqual(BulkKktRegistrationStatus.Registered, outcome.Results[0].Status, "Expected recovered registration.");
        }

        private static void BulkWorkflowHonorsCancellation()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success("{\"kkt\":[{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);
            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4041", null, CancellationToken.None).Result;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, cancellation.Token).Result;

            AssertTrue(outcome.Cancelled, "Expected cancelled outcome.");
            AssertEqual(0, api.AddCalls, "Cancellation must prevent POST.");
        }

        private static void BulkWorkflowSkipsPutWhenReadinessIsNotConfirmed()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success("{\"kkt\":[{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.AddResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Failure(404, "{}"));
            api.InstanceResponses.Enqueue(Success("{}"));
            api.InstanceResponses.Enqueue(Failure(500, "{}"));
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4041", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(1, api.AddCalls, "Expected successful POST.");
            AssertEqual(0, api.RegisterCalls, "PUT must be skipped without confirmed readiness.");
            AssertEqual(BulkKktRegistrationStatus.InspectionFailed, outcome.Results[0].Status,
                "Expected inspection failure after POST.");
        }

        private static void BulkWorkflowPreservesResultsWhenRequestIsCancelled()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[]}");
            api.DkktResponse = Success("{\"kkt\":[" +
                "{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}," +
                "{\"kktSerial\":\"00105700000002\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.InstanceResponses.Enqueue(Success("{\"clientPort\":51401}"));
            EnqueueRegisteredReadback(
                api, "00105700000001", "7300000000000001", "1234567894");
            api.CancelOnInstanceCall = 3;
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4041", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertTrue(outcome.Cancelled, "Expected cancelled outcome.");
            AssertEqual(2, api.AddCalls, "Both POST calls should be visible before cancellation.");
            AssertEqual(1, api.RegisterCalls, "First KKT result must be preserved.");
            AssertEqual(2, outcome.Results.Count, "Expected previous and interrupted KKT results.");
            AssertEqual(BulkKktRegistrationStatus.Registered, outcome.Results[0].Status, "Expected preserved first result.");
            AssertEqual(BulkKktRegistrationStatus.Cancelled, outcome.Results[1].Status, "Expected interrupted second result.");
        }

        private static void SequentialDiscoveryRejectsExternalSessions()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            string foreign = DkktListJson(
                "00105700000009", "7300000000000009", "7707083893");
            api.DkktResponses.Enqueue(Success(foreign));
            api.DkktResponses.Enqueue(Success(foreign));
            api.DkktResponses.Enqueue(Success(foreign));
            FakeKktConnectionProvider connections = new FakeKktConnectionProvider();
            connections.Add("COM9", "00105700000001");
            SequentialKktRegistrationCoordinator coordinator =
                CreateSequentialCoordinator(api, connections);

            SequentialKktDiscovery discovery = coordinator.DiscoverAsync(
                "http://127.0.0.1:51077",
                null,
                CancellationToken.None).Result;

            AssertFalse(discovery.IsValid,
                "Foreign live sessions must block deterministic VCOM discovery.");
            AssertTrue(discovery.HasExternalSessions,
                "The operator needs a retryable external-holder diagnosis.");
            AssertEqual(1, discovery.BlockingDevices.Count,
                "The last real dkktList snapshot must be shown to the operator.");
            AssertEqual(0, connections.OpenCount,
                "No application VCOM may open while another program holds KKT sessions.");
            AssertContains(discovery.ErrorMessage, "другой программой");
        }

        private static void SequentialDiscoveryMapsEachVcom()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.DkktResponses.Enqueue(NoContent());
            api.DkktResponses.Enqueue(NoContent());
            api.DkktResponses.Enqueue(NoContent());
            api.DkktResponses.Enqueue(Success(DkktListJson(
                "00105700000001", "7300000000000001", "1234567894")));
            api.DkktResponses.Enqueue(Success(DkktListJson(
                "00105700000002", "7300000000000002", "7707083893")));
            FakeKktConnectionProvider connections = new FakeKktConnectionProvider();
            connections.Add("COM9", "00105700000001");
            connections.Add("COM11", "00105700000002");
            List<BulkRegistrationProgress> progress =
                new List<BulkRegistrationProgress>();
            SequentialKktRegistrationCoordinator coordinator =
                CreateSequentialCoordinator(api, connections);

            SequentialKktDiscovery discovery = coordinator.DiscoverAsync(
                "http://127.0.0.1:51077",
                delegate(BulkRegistrationProgress item) { progress.Add(item); },
                CancellationToken.None).Result;

            AssertTrue(discovery.IsValid, "Both VCOM mappings must be accepted.");
            AssertEqual(2, discovery.Targets.Count, "Expected one target per MI_00 port.");
            AssertEqual("COM9", discovery.Targets[0].Port.PortName,
                "Port order must remain deterministic.");
            AssertEqual("00105700000002", discovery.Targets[1].Device.KktSerial,
                "The second real dkktList row must be attached to COM11.");
            AssertEqual(2, connections.DisposedCount,
                "Discovery must close every port immediately after its snapshot.");
            AssertTrue(ContainsProgressText(progress, "ИНН") &&
                ContainsProgressText(progress, "ККТ: 1"),
                "Every poll must expose visible INNs and KKT count.");
            AssertTrue(progress[0].CountsAttempts,
                "Poll counters must be labelled as attempts, not KKT ordinals.");
        }

        private static void DirectControllerBindingSeparatesControllerAndLmPorts()
        {
            LmGatewayBindingInput input =
                DirectControllerBindingInputFactory.Create(
                    new LmGatewayKkt
                    {
                        KktSerial = "00105700000001",
                        KktInn = "1234567894"
                    },
                    new DirectControllerAssignment
                    {
                        KktSerial = "00105700000001",
                        KktInn = "1234567894",
                        GrpcPort = 50063,
                        TargetLocalModulePort = 5995
                    });

            AssertEqual("50063", input.ControllerGrpcPort,
                "ESM must bind to the controller gRPC listener.");
            AssertEqual("5995", input.ExpectedLmPort,
                "Read-back must compare the controller's LM target, not its gRPC port.");
        }

        private static void SequentialRegistrationDisposesVcomBeforeReturning()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.DkktResponses.Enqueue(Success(DkktListJson(
                "00105700000001", "7300000000000001", "1234567894")));
            FakeKktConnectionProvider connections = new FakeKktConnectionProvider();
            connections.Add("COM9", "00105700000001");
            SequentialKktRegistrationTarget target = CreateSequentialTarget(
                "COM9", "00105700000001", "7300000000000001", "1234567894");
            SequentialKktRegistrationCoordinator coordinator =
                CreateSequentialCoordinator(api, connections);
            bool registrationObservedOpenLease = false;

            BulkKktRegistrationResult result = coordinator.ExecuteWithTargetAsync(
                "http://127.0.0.1:51077",
                target,
                delegate(CancellationToken token)
                {
                    registrationObservedOpenLease =
                        connections.LastLease != null &&
                        !connections.LastLease.IsDisposed;
                    return Task.FromResult(new BulkKktRegistrationResult
                    {
                        KktSerial = target.Device.KktSerial,
                        Status = BulkKktRegistrationStatus.Registered
                    });
                },
                null,
                CancellationToken.None).Result;

            AssertTrue(registrationObservedOpenLease,
                "The physical KKT must remain visible while POST/PUT run.");
            AssertEqual(BulkKktRegistrationStatus.Registered, result.Status,
                "The registration result must pass through unchanged.");
            AssertTrue(connections.LastLease.IsDisposed,
                "The helper/controller phase starts only after the VCOM lease is disposed.");
        }

        private static void SequentialCancellationKeepsCompletedKkt()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.DkktResponses.Enqueue(Success(DkktListJson(
                "00105700000001", "7300000000000001", "1234567894")));
            FakeKktConnectionProvider connections = new FakeKktConnectionProvider();
            connections.Add("COM9", "00105700000001");
            connections.Add("COM11", "00105700000002");
            SequentialKktRegistrationCoordinator coordinator =
                CreateSequentialCoordinator(api, connections);
            SequentialKktRegistrationTarget first = CreateSequentialTarget(
                "COM9", "00105700000001", "7300000000000001", "1234567894");
            SequentialKktRegistrationTarget second = CreateSequentialTarget(
                "COM11", "00105700000002", "7300000000000002", "7707083893");

            BulkKktRegistrationResult completed = coordinator.ExecuteWithTargetAsync(
                "http://127.0.0.1:51077",
                first,
                delegate(CancellationToken token)
                {
                    return Task.FromResult(new BulkKktRegistrationResult
                    {
                        KktSerial = first.Device.KktSerial,
                        Status = BulkKktRegistrationStatus.Registered
                    });
                },
                null,
                CancellationToken.None).Result;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancelled = false;
            try
            {
                coordinator.ExecuteWithTargetAsync(
                    "http://127.0.0.1:51077",
                    second,
                    delegate(CancellationToken token)
                    {
                        throw new InvalidOperationException(
                            "Cancelled target must never run.");
                    },
                    null,
                    cancellation.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            finally
            {
                cancellation.Dispose();
            }

            AssertTrue(cancelled, "Cancellation between targets must be observed.");
            AssertEqual(BulkKktRegistrationStatus.Registered, completed.Status,
                "The completed KKT result must not be rolled back or rewritten.");
            AssertEqual(1, connections.OpenCount,
                "The next VCOM must not open after cancellation.");
            AssertEqual(1, connections.DisposedCount,
                "The completed target lease must already be closed.");
        }

        private static void SequentialFinalVerificationClosesEveryVcomOnFailure()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.DkktResponses.Enqueue(Success(DkktListJson(
                "00105700000001", "7300000000000001", "1234567894")));
            api.DkktResponses.Enqueue(Success(DkktListJson(
                "00105700000001", "7300000000000001", "1234567894")));
            FakeKktConnectionProvider connections = new FakeKktConnectionProvider();
            connections.Add("COM9", "00105700000001");
            connections.Add("COM11", "00105700000002");
            SequentialKktDiscovery discovery = new SequentialKktDiscovery();
            discovery.Targets.Add(CreateSequentialTarget(
                "COM9", "00105700000001", "7300000000000001", "1234567894"));
            discovery.Targets.Add(CreateSequentialTarget(
                "COM11", "00105700000002", "7300000000000002", "7707083893"));
            SequentialKktRegistrationCoordinator coordinator =
                CreateSequentialCoordinator(api, connections);

            bool verified = coordinator.VerifyAllAsync(
                "http://127.0.0.1:51077",
                discovery,
                null,
                CancellationToken.None).Result;

            AssertFalse(verified,
                "A partial final dkktList must not be reported as complete.");
            AssertEqual(2, connections.DisposedCount,
                "Every final-verification lease must close after failure.");
            AssertEqual("COM11", connections.DisposeOrder[0],
                "Final verification must unwind leases in reverse order.");
            AssertEqual("COM9", connections.DisposeOrder[1],
                "The first lease must be the last one released.");
        }

        private static SequentialKktRegistrationCoordinator
            CreateSequentialCoordinator(
                FakeTspiotApiClient api,
                FakeKktConnectionProvider connections)
        {
            return new SequentialKktRegistrationCoordinator(
                api,
                connections,
                delegate(TimeSpan delay, CancellationToken token)
                {
                    return Task.FromResult(0);
                },
                3,
                2);
        }

        private static SequentialKktRegistrationTarget CreateSequentialTarget(
            string port,
            string serial,
            string fnSerial,
            string inn)
        {
            return new SequentialKktRegistrationTarget
            {
                Port = new KktConnectionPort
                {
                    PortName = port,
                    HardwareId = "USB\\VID_2912&PID_0005&MI_00"
                },
                Identity = new KktConnectionIdentity
                {
                    PortName = port,
                    KktSerial = serial,
                    ModelName = "АТОЛ 30Ф",
                    FirmwareVersion = "5.8.1"
                },
                Device = new DkktDeviceInfo
                {
                    KktSerial = serial,
                    FnSerial = fnSerial,
                    KktInn = inn
                }
            };
        }

        private static ApiResponse NoContent()
        {
            return new ApiResponse
            {
                IsSuccess = true,
                StatusCode = 204,
                ResponseBody = string.Empty
            };
        }

        private static string DkktListJson(
            string serial,
            string fnSerial,
            string inn)
        {
            return "{\"kkt\":[{\"kktSerial\":\"" + serial +
                "\",\"fnSerial\":\"" + fnSerial +
                "\",\"kktInn\":\"" + inn + "\"}]}";
        }

        private static bool ContainsProgressText(
            IList<BulkRegistrationProgress> progress,
            string expected)
        {
            for (int index = 0; index < progress.Count; index++)
            {
                if ((progress[index].Message ?? string.Empty).IndexOf(
                    expected,
                    StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static BulkRegistrationWorkflow CreateWorkflow(FakeTspiotApiClient api)
        {
            return new BulkRegistrationWorkflow(
                api,
                delegate(TimeSpan delay, CancellationToken token) { return Task.FromResult(0); });
        }

        private static ApiResponse Success(string body)
        {
            return new ApiResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                ResponseBody = body,
                DecodedMessage = "Успешно"
            };
        }

        private static ApiResponse Failure(int statusCode, string body)
        {
            return new ApiResponse
            {
                IsSuccess = false,
                StatusCode = statusCode,
                ResponseBody = body,
                DecodedMessage = "Ошибка"
            };
        }

        private static ApiResponse ConnectionFailure()
        {
            return new ApiResponse
            {
                IsSuccess = false,
                IsConnectionFailure = true,
                StatusCode = 0,
                ResponseBody = string.Empty,
                DecodedMessage = "Нет соединения"
            };
        }

        private static void DiagnosticMaskerHidesFiscalIdentifiers()
        {
            string source =
                "kktSerial=00105700000001; fnSerial=7300000000000001; kktInn=1234567894\r\n" +
                "{\"id\":\"00105700000001\",\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}";

            string masked = DiagnosticMasker.Mask(source);

            AssertFalse(masked.Contains("00105700000001"), "KKT serial must be masked.");
            AssertFalse(masked.Contains("7300000000000001"), "FN serial must be masked.");
            AssertFalse(masked.Contains("1234567894"), "INN must be masked.");
            AssertContains(masked, "001*********01");
            AssertContains(masked, "730***********01");
        }

        private static void DiagnosticMaskerHidesLocalUserPaths()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string tempPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string source =
                "log=" + Path.Combine(localAppData, "EsmTspiotTool", "Logs", "test.log") + "\r\n" +
                "profile=" + Path.Combine(userProfile, "Desktop", "test.txt") + "\r\n" +
                "temp=" + Path.Combine(tempPath, "esm-test.ps1");

            string masked = DiagnosticMasker.Mask(source);

            AssertFalse(masked.Contains(localAppData), "LocalAppData path must be masked.");
            AssertFalse(masked.Contains(userProfile), "User profile path must be masked.");
            AssertFalse(masked.Contains(tempPath), "Temporary path must be masked.");
            AssertContains(masked, "%LOCALAPPDATA%");
            AssertContains(masked, "%USERPROFILE%");
            AssertContains(masked, "%TEMP%");
        }

        private static void DiagnosticMaskerHidesCommonSecrets()
        {
            string source =
                "{\"password\":\"secret-value\",\"token\":\"ABC123\",\"connectionString\":\"Server=local;Password=qwerty\"}\r\n" +
                "authorization=BearerValue&apiKey=KeyValue";

            string masked = DiagnosticMasker.Mask(source);

            AssertFalse(masked.Contains("secret-value"), "Password value must be masked.");
            AssertFalse(masked.Contains("ABC123"), "Token value must be masked.");
            AssertFalse(masked.Contains("Server=local"), "Connection string must be masked.");
            AssertFalse(masked.Contains("BearerValue"), "Authorization value must be masked.");
            AssertFalse(masked.Contains("KeyValue"), "API key value must be masked.");
        }

        private static void SensitiveMaskerRedactsJsonCredentials()
        {
            string escapedPassword = "alpha\\\"quote\\\\path\\nomega\\u041f";
            string source =
                "{\"password\":\"" + escapedPassword + "\"," +
                "\"NeWpAsSwOrD\":\"new-value\"," +
                "\"token\":\"token-value\"," +
                "\"secret\":\"secret-value\"," +
                "\"authorization\":\"Bearer value\"," +
                "\"apiKey\":\"key-value\"," +
                "\"connectionString\":\"Server=local;Password=qwerty\"}";

            string masked = SensitiveDataMasker.Mask(source);

            AssertContains(masked, "\"password\":\"***\"");
            AssertContains(masked, "\"NeWpAsSwOrD\":\"***\"");
            AssertContains(masked, "\"token\":\"***\"");
            AssertContains(masked, "\"secret\":\"***\"");
            AssertContains(masked, "\"authorization\":\"***\"");
            AssertContains(masked, "\"apiKey\":\"***\"");
            AssertContains(masked, "\"connectionString\":\"***\"");
            AssertFalse(masked.Contains("alpha"), "Escaped password prefix must be masked.");
            AssertFalse(masked.Contains("quote"), "Text after an escaped quote must be masked.");
            AssertFalse(masked.Contains("path"), "Text after an escaped backslash must be masked.");
            AssertFalse(masked.Contains("omega"), "Text after an escaped newline must be masked.");
            AssertFalse(masked.Contains("\\u041f"), "Unicode escape must be masked.");
        }

        private static void SensitiveMaskerRedactsDerivedTokenFields()
        {
            const string responseBody =
                "{\"espToken\":\"esp-secret\",\"accessToken\":\"access-secret\"," +
                "\"refreshToken\":\"refresh-secret\",\"tokenCount\":2}";
            string masked = SensitiveDataMasker.Mask(responseBody);

            AssertFalse(masked.Contains("esp-secret"), "ESM token must be masked.");
            AssertFalse(masked.Contains("access-secret"), "Access token must be masked.");
            AssertFalse(masked.Contains("refresh-secret"), "Refresh token must be masked.");
            AssertContains(masked, "\"espToken\":\"***\"");
            AssertContains(masked, "\"accessToken\":\"***\"");
            AssertContains(masked, "\"refreshToken\":\"***\"");
            AssertContains(masked, "\"tokenCount\":2");

            string formatted = LogFormatter.Format(new ApiResponse
            {
                Method = "GET",
                Url = "http://127.0.0.1:51077/api/v1/instances/info/test",
                StatusCode = 200,
                ReasonPhrase = "OK",
                ResponseBody = responseBody,
                DecodedMessage = "Запрос выполнен успешно."
            });
            AssertFalse(formatted.Contains("esp-secret"),
                "The ESM token must not reach the persisted response log.");
            AssertContains(formatted, "\"espToken\":\"***\"");
        }

        private static void SensitiveMaskerRedactsLmInfoPass()
        {
            const string pass = "lm-controller-pass";
            string masked = SensitiveDataMasker.Mask(
                "{\"lm\":{\"ip\":\"127.0.0.1\",\"port\":5995,\"pass\":\"" + pass + "\"}}");

            AssertFalse(masked.Contains(pass), "The documented LM info pass field must be masked.");
            AssertContains(masked, "\"pass\":\"***\"");
        }

        private static void SensitiveMaskerRedactsKeyValueCredentials()
        {
            string masked = SensitiveDataMasker.Mask(
                "password=secret-value&authorization=Bearer auth-token&status=ready");

            AssertEqual(
                "password=***&authorization=***&status=ready",
                masked,
                "Expected password and the complete authorization value to be masked.");
        }

        private static void SensitiveMaskerRedactsYamlScalarAndBlockCredentials()
        {
            string source =
                "settings:\n" +
                "  ldbControl:\n" +
                "    login: admin\n" +
                "    password: admin # local credential\n" +
                "    newPassword: \"changed\"\n" +
                "    token: |\n" +
                "      first-line\n" +
                "      second-line\n" +
                "    gRPCPort: 50063\n";
            string masked = SensitiveDataMasker.Mask(source);
            AssertContains(masked, "password: *** # local credential");
            AssertContains(masked, "newPassword: ***");
            AssertContains(masked, "token: ***");
            AssertFalse(masked.IndexOf("first-line", StringComparison.Ordinal) >= 0,
                "YAML block secret body must be removed.");
            AssertFalse(masked.IndexOf("second-line", StringComparison.Ordinal) >= 0,
                "Every YAML block secret line must be removed.");
            AssertContains(masked, "login: admin");
            AssertContains(masked, "gRPCPort: 50063");
        }

        private static void LogFormatterNeverPersistsReflectedPassword()
        {
            string escapedPassword = "alpha\\\"quote\\\\path\\nomega\\u041f";
            ApiResponse response = new ApiResponse
            {
                Method = "PUT",
                Url = "http://127.0.0.1:51077/api/v1/settings/lm/test",
                RequestBody = "{\"password\":\"" + escapedPassword + "\"}",
                StatusCode = 400,
                ReasonPhrase = "Bad Request",
                ResponseBody = "{\"error\":{\"password\":\"" + escapedPassword + "\"}}",
                DecodedMessage = "Сервер вернул {\"password\":\"" + escapedPassword + "\"}"
            };

            string formatted = LogFormatter.Format(response);

            AssertFalse(formatted.Contains("alpha"), "Password prefix must not reach the formatted log.");
            AssertFalse(formatted.Contains("quote"), "Reflected text after an escaped quote must be masked.");
            AssertFalse(formatted.Contains("path"), "Reflected text after an escaped backslash must be masked.");
            AssertFalse(formatted.Contains("omega"), "Reflected text after an escaped newline must be masked.");
            AssertFalse(formatted.Contains("\\u041f"), "Reflected Unicode escape must be masked.");
            AssertContains(formatted, "\"password\":\"***\"");
        }

        private static void SensitiveMaskerPreservesOrdinaryFields()
        {
            string source =
                "{\"address\":\"127.0.0.1\",\"port\":50063,\"login\":\"operator\"," +
                "\"kktSerial\":\"test-serial\",\"message\":\"Обычный русский текст\"}";

            string masked = SensitiveDataMasker.Mask(source);

            AssertEqual(source, masked, "Ordinary LM connection fields must stay unchanged.");
        }

        private static void DisplayLogTrimmerPreservesNewestHalf()
        {
            string trimmed = DisplayLogTrimmer.TrimIfNeeded("abcdefghij", 8);

            AssertContains(trimmed, "начало журнала см. в файле");
            AssertTrue(trimmed.EndsWith("fghij", StringComparison.Ordinal), "Expected the newest half of the visible log.");
            AssertFalse(trimmed.Contains("abcde"), "Expected the oldest half to be removed.");
        }

        private static void FileLogSinkPersistsTextWithoutBlocking()
        {
            string directory = Path.Combine(Path.GetTempPath(), "esm_tspiot_tests_" + Guid.NewGuid().ToString("N"));
            FileLogSink sink = new FileLogSink(directory, "test.log");

            AssertTrue(sink.TryAppend("Первая строка\r\n"), "Expected first write.");
            AssertTrue(sink.TryAppend("Вторая строка\r\n"), "Expected second write.");
            string content = File.ReadAllText(sink.LogFilePath, System.Text.Encoding.UTF8);
            AssertContains(content, "Первая строка");
            AssertContains(content, "Вторая строка");

            Directory.Delete(directory, true);
        }

        private static void LmProvisionerContractExposesNoArbitraryCommand()
        {
            Type contract = typeof(ILmServiceProvisioner);
            string[] forbidden =
            {
                "Command", "Argument", "Environment", "BinaryPath", "ProfilePath",
                "ServiceName", "SourcePath", "Password", "Credential", "Secret", "Token"
            };
            MethodInfo[] methods = contract.GetMethods();
            AssertEqual(5, methods.Length, "The helper contract must expose only five typed operations.");
            for (int methodIndex = 0; methodIndex < methods.Length; methodIndex++)
            {
                ParameterInfo[] parameters = methods[methodIndex].GetParameters();
                for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                {
                    string name = parameters[parameterIndex].Name ?? string.Empty;
                    for (int forbiddenIndex = 0; forbiddenIndex < forbidden.Length; forbiddenIndex++)
                    {
                        AssertFalse(
                            name.IndexOf(forbidden[forbiddenIndex], StringComparison.OrdinalIgnoreCase) >= 0,
                            "Provisioner contract contains an unsafe parameter: " + name + ".");
                    }
                }
            }
        }

        private static void LmProbeResultSeparatesServiceAndListenerState()
        {
            LmGatewayProbeResult result = new LmGatewayProbeResult
            {
                ServiceRunning = true,
                GrpcListenerReady = true,
                RestListenerReady = false,
                ListenerOwnersVerified = false,
                Message = "REST listener missing"
            };

            AssertTrue(result.ServiceRunning, "Service state must remain independently visible.");
            AssertTrue(result.GrpcListenerReady, "gRPC readiness must remain independently visible.");
            AssertFalse(result.RestListenerReady, "REST readiness must not be inferred from gRPC.");
            AssertFalse(result.IsReady, "Overall readiness needs service, both listeners and owner proof.");
        }

        private static void LmProvisioningProgressContainsNoCredentials()
        {
            Type type = typeof(LmProvisioningProgress);
            string[] forbidden =
            {
                "Password", "Login", "Credential", "Secret", "Token", "Authorization",
                "Request", "Response"
            };
            PropertyInfo[] properties = type.GetProperties();
            for (int propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
            {
                for (int forbiddenIndex = 0; forbiddenIndex < forbidden.Length; forbiddenIndex++)
                {
                    AssertFalse(
                        properties[propertyIndex].Name.IndexOf(
                            forbidden[forbiddenIndex],
                            StringComparison.OrdinalIgnoreCase) >= 0,
                        "Provisioning progress must be credential-free.");
                }
            }
        }

        private static void OperatorPackagePathsPersistWithoutPackageDataOrSecrets()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "esm_tspiot_package_paths_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string settingsPath = Path.Combine(root, "operator-package-paths.json");
            string controllerPath = Path.Combine(
                root,
                "esm-lm-controller_1.6.3.2-windows-setup.exe");
            string localModulePath = Path.Combine(root, "regime-2.6.1-7.msi");
            try
            {
                OperatorPackagePathStore store =
                    new OperatorPackagePathStore(settingsPath);

                store.Save(controllerPath, localModulePath);
                OperatorPackagePaths loaded = store.Load();

                AssertEqual(Path.GetFullPath(controllerPath), loaded.ControllerInstallerPath,
                    "The last controller package path must survive an application restart.");
                AssertEqual(Path.GetFullPath(localModulePath), loaded.LocalModuleInstallerPath,
                    "The last LM package path must survive an application restart.");
                string json = File.ReadAllText(settingsPath);
                AssertFalse(json.IndexOf("sha", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The path cache must not duplicate package trust data.");
                AssertFalse(json.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The path cache must not contain credentials.");
                AssertFalse(json.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0,
                    "The path cache must not contain credentials.");
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static void LmProvisioningResultFormatsEveryItemForOperatorLog()
        {
            LmServiceProvisioningItemResult failed = new LmServiceProvisioningItemResult
            {
                KktSerial = "00105700000001",
                Status = LmServiceProvisioningStatus.VersionVerificationPending,
                Message = "ЛМ ЧЗ не запущен; контрольная ККТ остановила план."
            };

            string line = failed.FormatLogLine();

            AssertContains(line, "00105700000001");
            AssertContains(line, LmServiceProvisioningStatus.VersionVerificationPending.ToString());
            AssertContains(line, "ЛМ ЧЗ не запущен");
            AssertContains(line, "остановила план");
        }

        private static void LmAutomaticSetupInstallsBeforeConfiguringKkt()
        {
            Type type = typeof(LmGatewayLifecycleWorkflow).Assembly.GetType(
                "EsmTspiot.Shared.Services.LmAutomaticSetupCoordinator");
            AssertTrue(type != null, "Expected an automatic setup coordinator.");
            if (type == null)
            {
                return;
            }

            object coordinator = Activator.CreateInstance(type);
            List<string> order = new List<string>();
            Func<CancellationToken, Task<bool>> install = delegate
            {
                order.Add("install");
                return Task.FromResult(true);
            };
            Func<CancellationToken, Task<bool>> configure = delegate
            {
                order.Add("configure");
                return Task.FromResult(true);
            };
            MethodInfo execute = type.GetMethod("ExecuteAsync");
            AssertTrue(execute != null, "Expected the automatic setup entry point.");
            Task<bool> task = (Task<bool>)execute.Invoke(
                coordinator,
                new object[] { install, configure, CancellationToken.None });

            bool completed = task.GetAwaiter().GetResult();

            AssertTrue(completed, "A successful two-stage setup must report completion.");
            AssertEqual(2, order.Count, "Both automatic setup stages must run exactly once.");
            AssertEqual("install", order[0], "Controller installation must happen first.");
            AssertEqual("configure", order[1], "KKT configuration must start only after installation.");
        }

        private static void AutomaticModeRegistersKktBeforeLmSetup()
        {
            Type type = typeof(LmGatewayLifecycleWorkflow).Assembly.GetType(
                "EsmTspiot.Shared.Services.AutomaticConfigurationCoordinator");
            AssertTrue(type != null, "Expected an end-to-end automatic configuration coordinator.");
            if (type == null)
            {
                return;
            }

            object coordinator = Activator.CreateInstance(type);
            List<string> order = new List<string>();
            Func<CancellationToken, Task<bool>> register = delegate
            {
                order.Add("register");
                return Task.FromResult(true);
            };
            Func<CancellationToken, Task<bool>> configureControllers = delegate
            {
                order.Add("controllers");
                return Task.FromResult(true);
            };
            MethodInfo execute = type.GetMethod("ExecuteAsync");
            AssertTrue(execute != null, "Expected the end-to-end automatic mode entry point.");
            Task<bool> task = (Task<bool>)execute.Invoke(
                coordinator,
                new object[] { register, configureControllers, CancellationToken.None });

            bool completed = task.GetAwaiter().GetResult();

            AssertTrue(completed, "Successful registration and controller setup must complete.");
            AssertEqual(2, order.Count, "Both end-to-end stages must run exactly once.");
            AssertEqual("register", order[0], "KKT registration must happen first.");
            AssertEqual("controllers", order[1],
                "Controller setup must start automatically after KKT registration.");
        }

        private static void AutomaticModeSkipsLmSetupAfterRegistrationFailure()
        {
            AutomaticConfigurationCoordinator coordinator =
                new AutomaticConfigurationCoordinator();
            bool controllersCalled = false;

            bool completed = coordinator.ExecuteAsync(
                delegate { return Task.FromResult(false); },
                delegate
                {
                    controllersCalled = true;
                    return Task.FromResult(true);
                },
                CancellationToken.None).GetAwaiter().GetResult();

            AssertFalse(completed, "A failed registration stage must stop the automatic run.");
            AssertFalse(controllersCalled,
                "Controller services must not be changed after registration preparation fails.");
        }

        private static void LmAutomaticSetupStopsAfterFailedInstallation()
        {
            LmAutomaticSetupCoordinator coordinator = new LmAutomaticSetupCoordinator();
            bool configureCalled = false;

            bool completed = coordinator.ExecuteAsync(
                delegate { return Task.FromResult(false); },
                delegate
                {
                    configureCalled = true;
                    return Task.FromResult(true);
                },
                CancellationToken.None).GetAwaiter().GetResult();

            AssertFalse(completed, "A rejected controller version must stop automatic setup.");
            AssertFalse(configureCalled,
                "KKT services must remain untouched when controller installation is not verified.");
        }

        private static void LmAutomaticSetupReportsIncompleteControllerConfiguration()
        {
            LmAutomaticSetupCoordinator coordinator = new LmAutomaticSetupCoordinator();

            bool completed = coordinator.ExecuteAsync(
                delegate { return Task.FromResult(true); },
                delegate { return Task.FromResult(false); },
                CancellationToken.None).GetAwaiter().GetResult();

            AssertFalse(completed,
                "Automatic setup must report a partial result when a KKT is not fully configured.");
        }

        private static void LmLifecycleEnsuresProbesThenBinds()
        {
            List<string> order = new List<string>();
            FakeLmProvisioner provisioner = new FakeLmProvisioner(order);
            FakeLmProbe probe = new FakeLmProbe(order);
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmGatewayCallObserved = delegate { order.Add("bind"); };
            LmGatewayPlan plan = CreateLifecyclePlan(2, LmGatewayPlanAction.CreateManagedService);
            string operationId = Guid.NewGuid().ToString("N");
            string hash = ComputeEnsureHash(plan, operationId);
            provisioner.EnsureResult = CreateEnsureResult(plan, operationId, hash, -1);

            LmGatewayLifecycleOutcome outcome = new LmGatewayLifecycleWorkflow(
                provisioner,
                probe,
                NewFastBindingWorkflow(api)).ExecuteAsync(
                    "http://127.0.0.1:51077",
                    plan,
                    operationId,
                    hash,
                    CreateCredentials,
                    null,
                    CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(1, provisioner.EnsureCalls, "All services must use one helper batch.");
            AssertEqual(2, api.LmGatewayCalls.Count, "Every ready service must be bound.");
            AssertEqual("ensure", order[0], "Provisioning must happen first.");
            AssertEqual("probe", order[1], "Probe must happen before binding.");
            AssertEqual("bind", order[2], "Binding must follow a successful probe.");
            AssertEqual(LmGatewayLifecycleStatus.BindingAccepted, outcome.Results[0].Status, "Expected accepted binding.");
        }

        private static void LmLifecycleCompletionRequiresVerifiedReadback()
        {
            LmGatewayLifecycleOutcome outcome = new LmGatewayLifecycleOutcome();
            outcome.Results.Add(new LmGatewayLifecycleResult
            {
                Status = LmGatewayLifecycleStatus.BindingAccepted,
                BindingStatus = LmGatewayBindingStatus.BindingAccepted,
                BindingAttempted = true
            });

            AssertFalse(
                LmGatewayLifecycleWorkflow.IsFullyVerified(outcome, 1),
                "A successful PUT without readback must remain a partial automatic result.");

            outcome.Results[0].BindingStatus = LmGatewayBindingStatus.BindingObserved;
            AssertFalse(
                LmGatewayLifecycleWorkflow.IsFullyVerified(outcome, 1),
                "Observed identity without endpoint verification must remain partial.");

            outcome.Results[0].BindingStatus = LmGatewayBindingStatus.BindingVerified;
            AssertTrue(
                LmGatewayLifecycleWorkflow.IsFullyVerified(outcome, 1),
                "Only verified readback may complete the automatic scenario.");
        }

        private static void LmLifecycleNeverBindsFailedService()
        {
            FakeLmProvisioner provisioner = new FakeLmProvisioner(null);
            FakeLmProbe probe = new FakeLmProbe(null);
            probe.DefaultResult = new LmGatewayProbeResult { ServiceRunning = true, Message = "listeners missing" };
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            LmGatewayPlan plan = CreateLifecyclePlan(1, LmGatewayPlanAction.CreateManagedService);
            string operationId = Guid.NewGuid().ToString("N");
            string hash = ComputeEnsureHash(plan, operationId);
            provisioner.EnsureResult = CreateEnsureResult(plan, operationId, hash, -1);

            LmGatewayLifecycleOutcome outcome = CreateLifecycleWorkflow(provisioner, probe, api)
                .ExecuteAsync("http://127.0.0.1:51077", plan, operationId, hash,
                    CreateCredentials, null, CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(0, api.LmGatewayCalls.Count, "A failed readiness probe must block PUT.");
            AssertEqual(LmGatewayLifecycleStatus.ServiceFailed, outcome.Results[0].Status, "Expected service failure.");
        }

        private static void LmLifecycleContinuesAfterOneKktFailure()
        {
            FakeLmProvisioner provisioner = new FakeLmProvisioner(null);
            FakeLmProbe probe = new FakeLmProbe(null);
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            LmGatewayPlan plan = CreateLifecyclePlan(2, LmGatewayPlanAction.UpdateManagedService);
            string operationId = Guid.NewGuid().ToString("N");
            string hash = ComputeEnsureHash(plan, operationId);
            provisioner.EnsureResult = CreateEnsureResult(plan, operationId, hash, 0);

            LmGatewayLifecycleOutcome outcome = CreateLifecycleWorkflow(provisioner, probe, api)
                .ExecuteAsync("http://127.0.0.1:51077", plan, operationId, hash,
                    CreateCredentials, null, CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(2, outcome.Results.Count, "Both KKT outcomes must be retained.");
            AssertEqual(LmGatewayLifecycleStatus.ServiceFailed, outcome.Results[0].Status, "First service must fail.");
            AssertEqual(LmGatewayLifecycleStatus.BindingAccepted, outcome.Results[1].Status, "Second KKT must continue.");
            AssertEqual(1, api.LmGatewayCalls.Count, "Only the ready KKT must be bound.");
        }

        private static void LmLifecycleRetriesBindingWithoutReprovisioning()
        {
            FakeLmProvisioner provisioner = new FakeLmProvisioner(null);
            FakeLmProbe probe = new FakeLmProbe(null);
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            LmGatewayPlanItem item = CreateLifecyclePlan(1, LmGatewayPlanAction.BindReadyService).Items[0];

            LmGatewayLifecycleResult result = CreateLifecycleWorkflow(provisioner, probe, api)
                .RetryBindingAsync("http://127.0.0.1:51077", item, CreateCredentials, null,
                    CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(0, provisioner.EnsureCalls, "Binding retry must not request UAC/helper.");
            AssertEqual(1, probe.Calls, "Binding retry must re-probe readiness.");
            AssertEqual(LmGatewayLifecycleStatus.BindingAccepted, result.Status, "Expected accepted retry.");
        }

        private static void LmLifecyclePreservesPartialOutcomeOnCancellation()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            FakeLmProvisioner provisioner = new FakeLmProvisioner(null);
            FakeLmProbe probe = new FakeLmProbe(null);
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.LmGatewayCallObserved = delegate(int call) { if (call == 1) cancellation.Cancel(); };
            LmGatewayPlan plan = CreateLifecyclePlan(2, LmGatewayPlanAction.CreateManagedService);
            string operationId = Guid.NewGuid().ToString("N");
            string hash = ComputeEnsureHash(plan, operationId);
            provisioner.EnsureResult = CreateEnsureResult(plan, operationId, hash, -1);

            LmGatewayLifecycleOutcome outcome = CreateLifecycleWorkflow(provisioner, probe, api)
                .ExecuteAsync("http://127.0.0.1:51077", plan, operationId, hash,
                    CreateCredentials, null, cancellation.Token).GetAwaiter().GetResult();

            AssertTrue(outcome.Cancelled, "Outcome must retain cancellation state.");
            AssertEqual(LmGatewayLifecycleStatus.BindingAccepted, outcome.Results[0].Status, "Completed KKT must stay completed.");
            AssertEqual(LmGatewayLifecycleStatus.Cancelled, outcome.Results[1].Status, "Untouched KKT must be cancelled.");
        }

        private static void LmLifecycleReconcilesUnknownResultBeforeMutation()
        {
            FakeLmProvisioner provisioner = new FakeLmProvisioner(null);
            provisioner.EnsureException = new IOException("pipe lost");
            FakeLmProbe probe = new FakeLmProbe(null);
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            LmGatewayPlan plan = CreateLifecyclePlan(1, LmGatewayPlanAction.CreateManagedService);
            string operationId = Guid.NewGuid().ToString("N");
            string hash = ComputeEnsureHash(plan, operationId);

            LmGatewayLifecycleOutcome outcome = CreateLifecycleWorkflow(provisioner, probe, api)
                .ExecuteAsync("http://127.0.0.1:51077", plan, operationId, hash,
                    CreateCredentials, null, CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(1, provisioner.EnsureCalls, "Unknown result must not trigger a second mutation.");
            AssertEqual(1, probe.Calls, "Unknown result must be reconciled read-only.");
            AssertEqual(LmGatewayLifecycleStatus.BindingAccepted, outcome.Results[0].Status, "Ready reconciled service may bind.");
        }

        private static void LmRemovalWorkflowRemovesOneSelectedManagedService()
        {
            FakeLmProvisioner provisioner = new FakeLmProvisioner(null);
            LmServiceInventoryItem selected = CreateManagedInventoryItem();
            provisioner.RemoveResult = new LmServiceProvisioningItemResult
            {
                KktSerial = selected.KktSerial,
                Status = LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                Message = "removed"
            };
            int inventoryReads = 0;
            LmGatewayRemovalWorkflow workflow = new LmGatewayRemovalWorkflow(
                provisioner,
                delegate
                {
                    inventoryReads++;
                    return inventoryReads == 1
                        ? new List<LmServiceInventoryItem> { selected }
                        : new List<LmServiceInventoryItem>();
                });
            LmRemovalConfirmation confirmation = CreateRemovalConfirmation(selected);
            string operationId = Guid.NewGuid().ToString("N");
            string hash = ComputeRemovalHash(confirmation, operationId);

            LmGatewayLifecycleResult result = workflow.RemoveAsync(
                new[] { selected }, confirmation, operationId, hash,
                CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(1, provisioner.RemoveCalls, "Exactly one managed service must be removed.");
            AssertEqual(LmGatewayLifecycleStatus.RemovedLocalArtifactsBindingRetained, result.Status, "Expected verified local removal.");
        }

        private static void LmRemovalWorkflowAcceptsManagedStackFingerprint()
        {
            FakeLmProvisioner provisioner = new FakeLmProvisioner(null);
            LmServiceInventoryItem selected = new LmServiceInventoryItem
            {
                KktSerial = "00105700000001",
                ServiceName = LmServiceIdentity.CreateName("00105700000001"),
                Role = LmServiceRole.Managed,
                Ports = new LmGatewayPorts(0, 0),
                Status = LmServiceProvisioningStatus.CleanupPending,
                ManagedStateFingerprint = new LmManifestFingerprint
                {
                    Sha256 = new string('b', 64)
                }
            };
            provisioner.RemoveResult = new LmServiceProvisioningItemResult
            {
                KktSerial = selected.KktSerial,
                Status = LmServiceProvisioningStatus.Succeeded,
                Message = "removed"
            };
            int inventoryReads = 0;
            LmGatewayRemovalWorkflow workflow = new LmGatewayRemovalWorkflow(
                provisioner,
                delegate
                {
                    inventoryReads++;
                    return inventoryReads == 1
                        ? new List<LmServiceInventoryItem> { selected }
                        : new List<LmServiceInventoryItem>();
                });
            LmRemovalConfirmation confirmation = new LmRemovalConfirmation
            {
                KktSerial = selected.KktSerial,
                ManagedStateFingerprint = selected.ManagedStateFingerprint,
                RetainedEsmWarningAccepted = true
            };
            string operationId = Guid.NewGuid().ToString("N");
            string hash = ComputeRemovalHash(confirmation, operationId);

            LmGatewayLifecycleResult result = workflow.RemoveAsync(
                new[] { selected }, confirmation, operationId, hash,
                CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(1, provisioner.RemoveCalls,
                "A displayed managed-stack fingerprint must authorize one exact removal.");
            AssertEqual(
                LmGatewayLifecycleStatus.RemovedLocalArtifactsBindingRetained,
                result.Status,
                "A disappeared managed stack must reconcile as removed.");
        }

        private static void LmRemovalWorkflowBlocksBatchAndOfficialRemoval()
        {
            FakeLmProvisioner provisioner = new FakeLmProvisioner(null);
            LmGatewayRemovalWorkflow workflow = new LmGatewayRemovalWorkflow(
                provisioner,
                delegate { return new List<LmServiceInventoryItem>(); });
            LmServiceInventoryItem managed = CreateManagedInventoryItem();
            LmServiceInventoryItem official = CreateManagedInventoryItem();
            official.Role = LmServiceRole.VerifiedOfficial;
            LmRemovalConfirmation confirmation = CreateRemovalConfirmation(managed);

            LmGatewayLifecycleResult batch = workflow.RemoveAsync(
                new[] { managed, managed }, confirmation, Guid.NewGuid().ToString("N"),
                new string('0', 64), CancellationToken.None).GetAwaiter().GetResult();
            LmGatewayLifecycleResult protectedResult = workflow.RemoveAsync(
                new[] { official }, confirmation, Guid.NewGuid().ToString("N"),
                new string('0', 64), CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(LmGatewayLifecycleStatus.Blocked, batch.Status, "Batch removal must be blocked.");
            AssertEqual(LmGatewayLifecycleStatus.Blocked, protectedResult.Status, "Official service removal must be blocked.");
            AssertEqual(0, provisioner.RemoveCalls, "Blocked removals must never reach helper.");
        }

        private static void LmCleanupWorkflowLocksDisplayedManagedStackFingerprint()
        {
            LmServiceInventoryItem selected = new LmServiceInventoryItem
            {
                KktSerial = "00105700000001",
                ServiceName = LmServiceIdentity.CreateName("00105700000001"),
                Role = LmServiceRole.Managed,
                Ports = new LmGatewayPorts(0, 0),
                Status = LmServiceProvisioningStatus.CleanupPending,
                ManagedStateFingerprint = new LmManifestFingerprint
                {
                    Sha256 = new string('c', 64)
                }
            };
            FakeLmProvisioner provisioner = new FakeLmProvisioner(null)
            {
                CleanupResult = new LmServiceProvisioningItemResult
                {
                    KktSerial = selected.KktSerial,
                    Status = LmServiceProvisioningStatus.Succeeded,
                    Message = "cleaned"
                }
            };
            int inventoryReads = 0;
            LmGatewayRemovalWorkflow workflow = new LmGatewayRemovalWorkflow(
                provisioner,
                delegate
                {
                    inventoryReads++;
                    return inventoryReads == 1
                        ? new List<LmServiceInventoryItem> { selected }
                        : new List<LmServiceInventoryItem>();
                });
            LmCleanupConfirmation confirmation = new LmCleanupConfirmation
            {
                KktSerial = selected.KktSerial,
                ManagedStateFingerprint = selected.ManagedStateFingerprint,
                DisplayedState = LmServiceProvisioningStatus.CleanupPending
            };
            string operationId = Guid.NewGuid().ToString("N");
            string hash = ComputeCleanupHash(confirmation, operationId);

            LmGatewayLifecycleResult result = workflow.CleanupAsync(
                new[] { selected }, confirmation, operationId, hash,
                CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(1, provisioner.CleanupCalls,
                "The exact displayed managed-state fingerprint must reach helper cleanup.");
            AssertEqual(LmGatewayLifecycleStatus.RemovedLocalArtifactsBindingRetained,
                result.Status,
                "A disappeared managed stack must reconcile as cleaned.");

            LmServiceInventoryItem changed = new LmServiceInventoryItem
            {
                KktSerial = selected.KktSerial,
                ServiceName = selected.ServiceName,
                Role = selected.Role,
                Ports = selected.Ports,
                Status = selected.Status,
                ManagedStateFingerprint = new LmManifestFingerprint
                {
                    Sha256 = new string('d', 64)
                }
            };
            FakeLmProvisioner blockedProvisioner = new FakeLmProvisioner(null);
            LmGatewayRemovalWorkflow blockedWorkflow = new LmGatewayRemovalWorkflow(
                blockedProvisioner,
                delegate { return new List<LmServiceInventoryItem> { changed }; });

            LmGatewayLifecycleResult blocked = blockedWorkflow.CleanupAsync(
                new[] { changed }, confirmation, operationId, hash,
                CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(LmGatewayLifecycleStatus.Blocked, blocked.Status,
                "A changed managed cleanup fingerprint must require a fresh confirmation.");
            AssertEqual(0, blockedProvisioner.CleanupCalls,
                "Stale managed cleanup confirmation must never reach helper.");
        }

        private static void LmRemovalOutcomeWarnsThatEsmBindingRemains()
        {
            LmGatewayLifecycleResult result = new LmGatewayLifecycleResult
            {
                Status = LmGatewayLifecycleStatus.RemovedLocalArtifactsBindingRetained,
                Details = LmGatewayRemovalWorkflow.BindingRetainedMessage
            };
            AssertContains(result.Details, "ЕСМ");
            AssertContains(result.Details, "не очищена");
        }

#if !NETFRAMEWORK
        private static void ProvisionerClosureListParsesNamesAndHashes()
        {
            string first = new string('a', 64);
            string second = new string('b', 64);
            IList<KeyValuePair<string, string>> closure =
                ProvisionerProcessLauncher.ParseExpectedClosure(
                    "EsmTspiot.Shared.dll=" + first +
                    "|WixToolset.Dtf.Compression.dll=" + second);
            AssertEqual(2, closure.Count, "Expected two closure entries.");
            AssertEqual("EsmTspiot.Shared.dll", closure[0].Key, "First closure name.");
            AssertEqual(first, closure[0].Value, "First closure hash.");
            AssertEqual("WixToolset.Dtf.Compression.dll", closure[1].Key, "Second closure name.");
            AssertEqual(second, closure[1].Value, "Second closure hash.");
            AssertEqual(0, ProvisionerProcessLauncher.ParseExpectedClosure(string.Empty).Count,
                "An empty list must parse to no entries.");
            bool rejected = false;
            try
            {
                ProvisionerProcessLauncher.ParseExpectedClosure("EsmTspiot.Shared.dll=short");
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            AssertTrue(rejected, "A malformed hash must be rejected.");
            AssertEqual(1, ProvisionerProcessLauncher.ParseExpectedClosure(
                ProvisionerIntegrity.ExpectedClosureSha256).Count,
                "The test stub closure must parse.");
        }

        private static void ProvisionerLauncherNamesMissingHelperFileWithoutLocationRules()
        {
            string reason;
            bool available = new ProvisionerProcessLauncher().CanLaunch(out reason);
            AssertFalse(available, "No helper is present next to the test host.");
            AssertContains(reason, "EsmTspiot.ServiceProvisioner.exe");
            AssertContains(reason, "Распакуйте архив целиком");
            AssertFalse(reason.IndexOf("Program Files", StringComparison.OrdinalIgnoreCase) >= 0,
                "Portable mode must not demand Program Files: " + reason);
        }

        private static void ProvisionerAcceptsFullAdministratorWithoutSplitUac()
        {
            MethodInfo method = typeof(ProvisionerProcessLauncher).GetMethod(
                "HasAdministrativeToken",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(int), typeof(bool) },
                null);
            AssertTrue(method != null,
                "Expected a testable administrative-token decision.");
            bool result = (bool)method.Invoke(null, new object[] { 1, true });
            AssertTrue(result,
                "An already elevated administrator must be allowed when UAC has no split token.");
        }

        private static void ProvisionerCanInspectCurrentAdministrativeToken()
        {
            MethodInfo method = typeof(ProvisionerProcessLauncher).GetMethod(
                "HasAdministrativeToken",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            AssertTrue(method != null,
                "Expected the production administrative-token probe.");
            bool result = (bool)method.Invoke(null, new object[0]);
            AssertTrue(result,
                "The test process must expose its administrative token without a SecurityException.");
        }

        private static void ProvisionerClientCreatesSupportedProtectedPipe()
        {
            MethodInfo method = typeof(LmServiceProvisionerClient).GetMethod(
                "CreateServer",
                BindingFlags.NonPublic | BindingFlags.Static);
            AssertTrue(method != null, "Expected the production pipe factory.");
            using (NamedPipeServerStream pipe = (NamedPipeServerStream)method.Invoke(
                null,
                new object[] { "multikkt-test-" + Guid.NewGuid().ToString("N") }))
            {
                AssertTrue(pipe != null, "Expected a protected named-pipe server.");
            }
        }

        private static void ProvisionerClientExplainsPrematureHelperExit()
        {
            MethodInfo method = typeof(LmServiceProvisionerClient).GetMethod(
                "DescribePrematureExit",
                BindingFlags.NonPublic | BindingFlags.Static);
            AssertTrue(method != null, "Expected a helper-exit diagnostic mapper.");
            string rejected = (string)method.Invoke(null, new object[] { 2 });
            string failed = (string)method.Invoke(null, new object[] { 3 });
            AssertContains(rejected, "аутентификац");
            AssertContains(rejected, "2");
            AssertContains(failed, "3");
        }

        private static void FullAutomaticSetupKeepsLmInitializationDeferred()
        {
            LmAutomaticSetupCoordinator coordinator =
                new LmAutomaticSetupCoordinator();
            List<LmAutomaticSetupStage> stages =
                new List<LmAutomaticSetupStage>();

            LmAutomaticSetupResult result = coordinator.ExecuteFullAsync(
                delegate { return Task.FromResult(true); },
                delegate { return Task.FromResult(true); },
                delegate { return Task.FromResult(true); },
                delegate { return Task.FromResult(true); },
                delegate { return Task.FromResult(true); },
                delegate(LmAutomaticSetupStage stage) { stages.Add(stage); },
                CancellationToken.None).GetAwaiter().GetResult();

            AssertTrue(result.Complete,
                "Registration, controllers, local modules, ESM binding and " +
                "read-back must complete the run.");
            AssertEqual(string.Empty, result.IncompleteReason,
                "A complete contour must not report a missing stage.");
            AssertTrue(result.InitializationDeferred,
                "LM initialization must remain an explicit deferred state.");
            AssertEqual(6, stages.Count,
                "The operator must see every full-automatic stage.");
            AssertEqual(LmAutomaticSetupStage.Registration, stages[0],
                "Registration must remain the first stage.");
            AssertEqual(LmAutomaticSetupStage.ControllerEnsure, stages[1],
                "Controller provisioning must follow registration.");
            AssertEqual(LmAutomaticSetupStage.LocalModuleEnsure, stages[2],
                "Local-module provisioning must follow controllers.");
            AssertEqual(LmAutomaticSetupStage.EsmBinding, stages[3],
                "ESM binding must run after provisioning attempts.");
            AssertEqual(LmAutomaticSetupStage.EsmReadback, stages[4],
                "The ESM read-back must follow binding.");
            AssertEqual(LmAutomaticSetupStage.InitializationDeferred, stages[5],
                "Initialization must be reported without being executed.");
        }

        private static void FullAutomaticSetupDoesNotStopAfterLmFailure()
        {
            LmAutomaticSetupCoordinator coordinator =
                new LmAutomaticSetupCoordinator();
            bool bindingCalled = false;

            LmAutomaticSetupResult result = coordinator.ExecuteFullAsync(
                delegate { return Task.FromResult(true); },
                delegate { return Task.FromResult(true); },
                delegate { return Task.FromResult(false); },
                delegate
                {
                    bindingCalled = true;
                    return Task.FromResult(true);
                },
                delegate { return Task.FromResult(true); },
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            AssertTrue(bindingCalled,
                "A missing or failed LM MSI must not cancel ESM controller binding.");
            AssertTrue(result.ControllerEnsureSucceeded && result.EsmBindingSucceeded,
                "Deferred LM installation must not discard completed KKT/controller work.");
            AssertFalse(result.Complete,
                "A contour without its local modules must not be reported complete.");
            AssertTrue(result.LocalModuleDeferred,
                "The result must preserve that LM installation needs attention.");
            AssertTrue(result.IncompleteReason.IndexOf("ЛМ", StringComparison.Ordinal) >= 0,
                "The incomplete reason must name the missing local modules.");
        }

        private static void FullAutomaticSetupReadsBackEsmAfterBinding()
        {
            LmAutomaticSetupCoordinator coordinator =
                new LmAutomaticSetupCoordinator();
            List<string> order = new List<string>();

            LmAutomaticSetupResult unconfirmed = coordinator.ExecuteFullAsync(
                delegate { order.Add("register"); return Task.FromResult(true); },
                delegate { order.Add("controllers"); return Task.FromResult(true); },
                delegate { order.Add("local-modules"); return Task.FromResult(true); },
                delegate { order.Add("bind"); return Task.FromResult(true); },
                delegate { order.Add("readback"); return Task.FromResult(false); },
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(5, order.Count, "Every contour stage must run exactly once.");
            AssertEqual("bind", order[3], "ESM binding must precede the read-back.");
            AssertEqual("readback", order[4], "The ESM read-back must run after binding.");
            AssertTrue(unconfirmed.EsmBindingSucceeded,
                "The binding result must be preserved when the read-back fails.");
            AssertFalse(unconfirmed.EsmReadbackSucceeded,
                "A failed read-back must be recorded.");
            AssertFalse(unconfirmed.Complete,
                "An unconfirmed binding must leave the contour incomplete.");
            AssertTrue(unconfirmed.IncompleteReason.IndexOf("ЕСМ", StringComparison.Ordinal) >= 0,
                "The incomplete reason must point at the ESM read-back.");

            LmAutomaticSetupResult confirmed = coordinator.ExecuteFullAsync(
                delegate { return Task.FromResult(true); },
                delegate { return Task.FromResult(true); },
                delegate { return Task.FromResult(true); },
                delegate { return Task.FromResult(true); },
                delegate { return Task.FromResult(true); },
                null,
                CancellationToken.None).GetAwaiter().GetResult();
            AssertTrue(confirmed.Complete,
                "A confirmed read-back completes the contour.");
            AssertEqual(string.Empty, confirmed.IncompleteReason,
                "A complete contour has no missing stage.");
        }

        private static void LmContourReadbackPolicyClassifiesEsmObservations()
        {
            LmGatewayReadbackObservation verified = CreateContourObservation("ok");
            AssertEqual(LmContourReadbackState.Verified,
                LmContourReadbackPolicy.Classify(verified),
                "A matching endpoint with a healthy LM status is verified.");
            AssertTrue(LmContourReadbackPolicy.IsAcceptable(
                    LmContourReadbackState.Verified),
                "A verified read-back is acceptable.");
            string verifiedLine = LmContourReadbackPolicy.Describe(verified);
            AssertTrue(verifiedLine.IndexOf("00105700000001", StringComparison.Ordinal) >= 0 &&
                verifiedLine.IndexOf("127.0.0.1:50064", StringComparison.Ordinal) >= 0,
                "The verified line must name the KKT and the confirmed controller endpoint.");

            LmGatewayReadbackObservation uninitialized = CreateContourObservation("error 2025");
            AssertEqual(LmContourReadbackState.LocalModuleNotInitialized,
                LmContourReadbackPolicy.Classify(uninitialized),
                "An LM error code before initialization is not a contour failure.");
            AssertTrue(LmContourReadbackPolicy.IsAcceptable(
                    LmContourReadbackState.LocalModuleNotInitialized),
                "A confirmed binding with an uninitialized LM is acceptable.");
            string pendingLine = LmContourReadbackPolicy.Describe(uninitialized);
            AssertTrue(pendingLine.IndexOf("не инициализирован", StringComparison.Ordinal) >= 0 &&
                pendingLine.IndexOf("error 2025", StringComparison.Ordinal) >= 0,
                "The pending line must explain that the LM awaits initialization and quote ESM.");

            LmGatewayReadbackObservation otherError = CreateContourObservation("error 1234");
            AssertEqual(LmContourReadbackState.LocalModuleNotInitialized,
                LmContourReadbackPolicy.Classify(otherError),
                "Any LM-level error behind a confirmed endpoint is an initialization matter.");

            LmGatewayReadbackObservation mismatch = CreateContourObservation("ok");
            mismatch.EndpointMatches = false;
            mismatch.Details = "ЕСМ сообщает целевой ЛМ 127.0.0.1:5995, ожидалось 127.0.0.1:50064.";
            AssertEqual(LmContourReadbackState.Attention,
                LmContourReadbackPolicy.Classify(mismatch),
                "An endpoint mismatch requires attention.");
            AssertFalse(LmContourReadbackPolicy.IsAcceptable(
                    LmContourReadbackState.Attention),
                "An endpoint mismatch is not acceptable.");
            AssertTrue(LmContourReadbackPolicy.Describe(mismatch).IndexOf(
                    "ожидалось 127.0.0.1:50064", StringComparison.Ordinal) >= 0,
                "The attention line must carry the ESM details.");

            LmGatewayReadbackObservation unconfigured = CreateContourObservation("not_configured");
            unconfigured.HasLmConfiguration = false;
            AssertEqual(LmContourReadbackState.Attention,
                LmContourReadbackPolicy.Classify(unconfigured),
                "A KKT without an LM binding requires attention.");

            LmGatewayReadbackObservation unavailable = CreateContourObservation("ok");
            unavailable.IsAvailable = false;
            AssertEqual(LmContourReadbackState.Unavailable,
                LmContourReadbackPolicy.Classify(unavailable),
                "An unreachable ESM instance is unavailable.");
            AssertEqual(LmContourReadbackState.Unavailable,
                LmContourReadbackPolicy.Classify(null),
                "A missing observation is unavailable.");
            AssertFalse(LmContourReadbackPolicy.IsAcceptable(
                    LmContourReadbackState.Unavailable),
                "An unavailable read-back is not acceptable.");

            // ЕСМ сообщает в /api/v2/info endpoint локального модуля, а не
            // порт контроллера, через который мы его привязали. Полевой
            // журнал показал это прямо: привязка ККТ подтвердилась по адресу
            // 127.0.0.1:5995, а сверка на том же прогоне ждала 127.0.0.1:50063
            // и объявляла «требуется проверка» на исправной ККТ.
            LmGatewayTarget expected = LmContourReadbackPolicy.ExpectedTarget(
                new DirectControllerAssignment
                {
                    GrpcPort = 50064,
                    TargetLocalModulePort = 6995
                });
            AssertEqual("127.0.0.1", expected.Address,
                "The expected binding target is the local controller address.");
            AssertEqual(6995, expected.Port,
                "The read-back must expect the LM endpoint that ESM reports, " +
                "not the controller gRPC port used to bind it.");
            AssertTrue(LmContourReadbackPolicy.ExpectedTarget(null) == null,
                "A KKT without a controller assignment has no expected target.");
        }

        private static LmGatewayReadbackObservation CreateContourObservation(
            string lmStatus)
        {
            return new LmGatewayReadbackObservation
            {
                InstanceId = "00105700000001",
                KktSerial = "00105700000001",
                KktInn = "1234567894",
                IsAvailable = true,
                IdentityMatches = true,
                HasLmConfiguration = true,
                EndpointMatches = true,
                LmAddress = "127.0.0.1",
                LmPort = "50064",
                LmStatus = lmStatus,
                LmVersion = "2.6.1",
                Details = string.Empty
            };
        }

        private static void MsiProvisionerLaunchVerifiesHelperBeforeProcessStart()
        {
            MethodInfo launch = typeof(ProvisionerProcessLauncher).GetMethod(
                "Launch",
                BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo canLaunch = typeof(ProvisionerProcessLauncher).GetMethod(
                "CanLaunch",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo launcher = typeof(LocalModuleMsiProvisionerClient).GetField(
                "_launcher",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertTrue(launch != null && canLaunch != null && launcher != null,
                "The MSI client must use the reviewed provisioner launcher.");
            AssertTrue(MethodBodyCalls(launch, canLaunch),
                "ProvisionerProcessLauncher.Launch must verify helper hash/version before Process.Start.");
        }

        private static bool MethodBodyCalls(MethodInfo caller, MethodInfo target)
        {
            MethodBody body = caller.GetMethodBody();
            byte[] il = body == null ? null : body.GetILAsByteArray();
            if (il == null) return false;
            byte[] token = BitConverter.GetBytes(target.MetadataToken);
            for (int index = 0; index + token.Length < il.Length; index++)
            {
                if (il[index] != 0x28 && il[index] != 0x6f) continue;
                bool equal = true;
                for (int offset = 0; offset < token.Length; offset++)
                    equal &= il[index + 1 + offset] == token[offset];
                if (equal) return true;
            }
            return false;
        }
#endif

        private static LmGatewayLifecycleWorkflow CreateLifecycleWorkflow(
            FakeLmProvisioner provisioner,
            FakeLmProbe probe,
            FakeTspiotApiClient api)
        {
            return new LmGatewayLifecycleWorkflow(
                provisioner,
                probe,
                NewFastBindingWorkflow(api));
        }

        private static LmGatewayPlan CreateLifecyclePlan(int count, LmGatewayPlanAction action)
        {
            LmGatewayPlan plan = new LmGatewayPlan();
            for (int index = 0; index < count; index++)
            {
                string serial = "001057000000" + (index + 1).ToString("00");
                LmGatewayKkt kkt = new LmGatewayKkt
                {
                    InstanceId = "instance-" + index.ToString(),
                    KktSerial = serial,
                    KktInn = index == 0 ? "1234567894" : "500100732259"
                };
                plan.Items.Add(new LmGatewayPlanItem
                {
                    Kkt = kkt,
                    Spec = new ManagedLmServiceSpec(
                        new LmGatewayKkt { KktSerial = serial, KktInn = kkt.KktInn },
                        new LmGatewayPorts(50063 + index * 2, 50064 + index * 2),
                        new LmGatewayTarget("10.0.0." + (index + 10).ToString(), 5000)),
                    Action = action
                });
            }
            return plan;
        }

        private static string ComputeEnsureHash(LmGatewayPlan plan, string operationId)
        {
            LmServiceProvisioningBatchRequest request = new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 1,
                Operation = LmServiceOperation.EnsureBatch,
                OperationId = operationId,
                InitiatingSid = "S-1-5-21-1-2-3-1001"
            };
            for (int index = 0; index < plan.Items.Count; index++)
            {
                LmGatewayPlanItem item = plan.Items[index];
                if (item != null && item.IsValid &&
                    item.Action != LmGatewayPlanAction.BindReadyService &&
                    item.Action != LmGatewayPlanAction.NoChange)
                {
                    request.Items.Add(ToProvisioningRequest(item.Spec));
                }
            }
            return CanonicalLmPlanHasher.Compute(request);
        }

        private static LmServiceProvisioningItemRequest ToProvisioningRequest(ManagedLmServiceSpec spec)
        {
            return new LmServiceProvisioningItemRequest
            {
                KktSerial = spec.KktSerial,
                GrpcPort = spec.Ports.GrpcPort,
                RestPort = spec.Ports.RestPort,
                TargetAddress = spec.Target.Address,
                TargetPort = spec.Target.Port
            };
        }

        private static LmServiceProvisioningBatchResult CreateEnsureResult(
            LmGatewayPlan plan,
            string operationId,
            string hash,
            int failedIndex)
        {
            LmServiceProvisioningBatchResult result = new LmServiceProvisioningBatchResult
            {
                SchemaVersion = 1,
                OperationId = operationId,
                PlanHash = hash,
                Status = failedIndex < 0
                    ? LmServiceProvisioningStatus.Succeeded
                    : LmServiceProvisioningStatus.RequiresAttention
            };
            for (int index = 0; index < plan.Items.Count; index++)
            {
                result.Items.Add(new LmServiceProvisioningItemResult
                {
                    KktSerial = plan.Items[index].Spec.KktSerial,
                    Status = index == failedIndex
                        ? LmServiceProvisioningStatus.Failed
                        : LmServiceProvisioningStatus.Succeeded,
                    Message = index == failedIndex ? "failed" : "ready"
                });
            }
            return result;
        }

        private static LmGatewayCredentials CreateCredentials(string serial)
        {
            return new LmGatewayCredentials { Login = "operator", Password = "temporary" };
        }

        private static LmServiceInventoryItem CreateManagedInventoryItem()
        {
            string serial = "00105700000001";
            return new LmServiceInventoryItem
            {
                KktSerial = serial,
                ServiceName = LmServiceIdentity.CreateName(serial),
                Role = LmServiceRole.Managed,
                Ports = new LmGatewayPorts(50063, 50064),
                Target = new LmGatewayTarget("10.0.0.10", 5000),
                Status = LmServiceProvisioningStatus.Succeeded,
                ManifestFingerprint = new LmManifestFingerprint { Sha256 = new string('a', 64) }
            };
        }

        private static LmRemovalConfirmation CreateRemovalConfirmation(LmServiceInventoryItem item)
        {
            return new LmRemovalConfirmation
            {
                KktSerial = item.KktSerial,
                GrpcPort = item.Ports.GrpcPort,
                RestPort = item.Ports.RestPort,
                ManifestFingerprint = item.ManifestFingerprint,
                RetainedEsmWarningAccepted = true
            };
        }

        private static string ComputeRemovalHash(LmRemovalConfirmation confirmation, string operationId)
        {
            LmServiceProvisioningBatchRequest request = new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 1,
                Operation = LmServiceOperation.RemoveManaged,
                OperationId = operationId,
                InitiatingSid = "S-1-5-21-1-2-3-1001",
                RemovalConfirmation = confirmation
            };
            return CanonicalLmPlanHasher.Compute(request);
        }

        private static string ComputeCleanupHash(LmCleanupConfirmation confirmation, string operationId)
        {
            LmServiceProvisioningBatchRequest request = new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = 1,
                Operation = LmServiceOperation.CleanupManaged,
                OperationId = operationId,
                InitiatingSid = "S-1-5-21-1-2-3-1001",
                CleanupConfirmation = confirmation
            };
            return CanonicalLmPlanHasher.Compute(request);
        }

        private static TspiotFormInput CreateValidInput()
        {
            return new TspiotFormInput
            {
                BaseUrl = "http://127.0.0.1:51077",
                KktSerial = "00106200000000",
                FnSerial = "7300000000000000",
                KktInn = "1234567894",
                Port = "50402",
                SoftPort = "51402",
                DkktPort = "4041"
            };
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
                throw new InvalidOperationException(message + " Expected: " + expected + ". Actual: " + actual + ".");
            }
        }

        private static void AssertContains(string text, string expected)
        {
            if (text == null || text.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Expected text to contain: " + expected + ". Actual: " + text);
            }
        }

        private sealed class FakeLmProvisioner : ILmServiceProvisioner
        {
            private readonly IList<string> _order;

            internal FakeLmProvisioner(IList<string> order)
            {
                _order = order;
            }

            internal int EnsureCalls { get; private set; }
            internal int RemoveCalls { get; private set; }
            internal int CleanupCalls { get; private set; }
            internal Exception EnsureException { get; set; }
            internal LmServiceProvisioningBatchResult EnsureResult { get; set; }
            internal LmServiceProvisioningItemResult RemoveResult { get; set; }
            internal LmServiceProvisioningItemResult CleanupResult { get; set; }

            public Task<LmControllerInstallResult> InstallControllerVersionAsync(
                LmControllerInstallerSelection selection,
                string operationId,
                string planHash,
                CancellationToken cancellation)
            {
                throw new NotSupportedException();
            }

            public Task<LmServiceProvisioningBatchResult> EnsureBatchAsync(
                IList<LmServiceProvisioningItemRequest> items,
                string operationId,
                string planHash,
                CancellationToken cancellation)
            {
                EnsureCalls++;
                if (_order != null)
                {
                    _order.Add("ensure");
                }
                if (EnsureException != null)
                {
                    throw EnsureException;
                }
                return Task.FromResult(EnsureResult);
            }

            public Task<LmServiceProvisioningItemResult> RemoveAsync(
                LmRemovalConfirmation confirmation,
                string operationId,
                string planHash,
                CancellationToken cancellation)
            {
                RemoveCalls++;
                return Task.FromResult(RemoveResult);
            }

            public Task<LmServiceProvisioningBatchResult> RemoveAllAsync(
                IList<LmRemovalConfirmation> confirmations,
                string operationId,
                string planHash,
                CancellationToken cancellation)
            {
                throw new NotSupportedException();
            }

            public Task<LmServiceProvisioningItemResult> CleanupAsync(
                LmCleanupConfirmation confirmation,
                string operationId,
                string planHash,
                CancellationToken cancellation)
            {
                CleanupCalls++;
                return Task.FromResult(CleanupResult);
            }
        }

        private sealed class FakeLmProbe : ILmGatewayProbe
        {
            private readonly IList<string> _order;

            internal FakeLmProbe(IList<string> order)
            {
                _order = order;
                DefaultResult = new LmGatewayProbeResult
                {
                    ServiceRunning = true,
                    GrpcListenerReady = true,
                    RestListenerReady = true,
                    ListenerOwnersVerified = true
                };
            }

            internal int Calls { get; private set; }
            internal LmGatewayProbeResult DefaultResult { get; set; }

            public Task<LmGatewayProbeResult> ProbeAsync(
                ManagedLmServiceSpec service,
                CancellationToken cancellation)
            {
                Calls++;
                if (_order != null)
                {
                    _order.Add("probe");
                }
                return Task.FromResult(DefaultResult);
            }
        }

        private sealed class RecordedHttpRequest
        {
            public string Method { get; set; }
            public string Url { get; set; }
            public string Body { get; set; }
            public string ContentType { get; set; }
            public string Accept { get; set; }
        }

        private sealed class FakeKktConnectionProvider : IKktConnectionProvider
        {
            private readonly List<KktConnectionPort> _ports =
                new List<KktConnectionPort>();
            private readonly Dictionary<string, KktConnectionIdentity> _identities =
                new Dictionary<string, KktConnectionIdentity>(StringComparer.OrdinalIgnoreCase);

            public FakeKktConnectionProvider()
            {
                DisposeOrder = new List<string>();
            }

            public int OpenCount { get; private set; }
            public int DisposedCount { get; private set; }
            public FakeKktConnectionLease LastLease { get; private set; }
            public IList<string> DisposeOrder { get; private set; }

            public void Add(string portName, string serial)
            {
                KktConnectionPort port = new KktConnectionPort
                {
                    PortName = portName,
                    HardwareId = "USB\\VID_2912&PID_0005&MI_00"
                };
                _ports.Add(port);
                _identities.Add(portName, new KktConnectionIdentity
                {
                    PortName = portName,
                    KktSerial = serial,
                    ModelName = "АТОЛ 30Ф",
                    FirmwareVersion = "5.8.1"
                });
            }

            public IList<KktConnectionPort> EnumeratePorts()
            {
                return new List<KktConnectionPort>(_ports);
            }

            public Task<IKktConnectionLease> OpenAsync(
                KktConnectionPort port,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OpenCount++;
                KktConnectionIdentity identity;
                if (port == null ||
                    !_identities.TryGetValue(port.PortName, out identity))
                {
                    throw new InvalidOperationException("Unknown fake port.");
                }
                LastLease = new FakeKktConnectionLease(
                    identity,
                    delegate(string disposedPort)
                    {
                        DisposedCount++;
                        DisposeOrder.Add(disposedPort);
                    });
                return Task.FromResult<IKktConnectionLease>(LastLease);
            }
        }

        private sealed class FakeKktConnectionLease : IKktConnectionLease
        {
            private readonly Action<string> _disposed;

            public FakeKktConnectionLease(
                KktConnectionIdentity identity,
                Action<string> disposed)
            {
                Identity = identity;
                _disposed = disposed;
            }

            public KktConnectionIdentity Identity { get; private set; }
            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                if (IsDisposed)
                {
                    return;
                }
                IsDisposed = true;
                if (_disposed != null)
                {
                    _disposed(Identity.PortName);
                }
            }
        }

        private sealed class FakeTspiotApiClient : ITspiotApiClient
        {
            public FakeTspiotApiClient()
            {
                AddResponses = new Queue<ApiResponse>();
                RegisterResponses = new Queue<ApiResponse>();
                DeleteResponses = new Queue<ApiResponse>();
                InstanceResponses = new Queue<ApiResponse>();
                InstancesResponses = new Queue<ApiResponse>();
                DkktResponses = new Queue<ApiResponse>();
                LmGatewayResponses = new Queue<ApiResponse>();
                LmInfoResponses = new Queue<ApiResponse>();
                LmGatewayCalls = new List<LmGatewayCall>();
                SettingsResponse = Success("[]");
            }

            public ApiResponse InstancesResponse { get; set; }
            public ApiResponse DkktResponse { get; set; }
            public ApiResponse SettingsResponse { get; set; }
            public Queue<ApiResponse> AddResponses { get; private set; }
            public Queue<ApiResponse> RegisterResponses { get; private set; }
            public Queue<ApiResponse> DeleteResponses { get; private set; }
            public Queue<ApiResponse> InstanceResponses { get; private set; }
            public Queue<ApiResponse> InstancesResponses { get; private set; }
            public Queue<ApiResponse> DkktResponses { get; private set; }
            public Queue<ApiResponse> LmGatewayResponses { get; private set; }
            public Queue<ApiResponse> LmInfoResponses { get; private set; }
            public IList<LmGatewayCall> LmGatewayCalls { get; private set; }
            public int AddCalls { get; private set; }
            public int RegisterCalls { get; private set; }
            public int DeleteCalls { get; private set; }
            public int InstanceCalls { get; private set; }
            public int InstancesCalls { get; private set; }
            public int DkktCalls { get; private set; }
            public int LmInfoCalls { get; private set; }

            private ApiResponse _lastLmInfoResponse;
            public int CancelOnInstanceCall { get; set; }
            public Action<int> LmGatewayCallObserved { get; set; }
            public string LastDeletedId { get; private set; }

            public Task<ApiResponse> GetInstancesAsync(string baseUrl, CancellationToken cancellationToken)
            {
                InstancesCalls++;
                return Task.FromResult(InstancesResponses.Count == 0 ? InstancesResponse : InstancesResponses.Dequeue());
            }

            public Task<ApiResponse> GetDkktListAsync(string baseUrl, CancellationToken cancellationToken)
            {
                DkktCalls++;
                return Task.FromResult(DkktResponses.Count == 0
                    ? DkktResponse
                    : DkktResponses.Dequeue());
            }

            public Task<ApiResponse> GetInstanceAsync(string baseUrl, string id, CancellationToken cancellationToken)
            {
                InstanceCalls++;
                if (CancelOnInstanceCall > 0 && InstanceCalls == CancelOnInstanceCall)
                {
                    throw new OperationCanceledException("Simulated cancellation.");
                }
                return Task.FromResult(InstanceResponses.Count == 0
                    ? Failure(404, "{}")
                    : InstanceResponses.Dequeue());
            }

            public Task<ApiResponse> GetSettingsAsync(string baseUrl, string id, CancellationToken cancellationToken)
            {
                return Task.FromResult(SettingsResponse);
            }

            public Task<ApiResponse> AddInstanceAsync(string baseUrl, AddTspiotRequest request, CancellationToken cancellationToken)
            {
                AddCalls++;
                return Task.FromResult(AddResponses.Count == 0 ? Success("{}") : AddResponses.Dequeue());
            }

            public Task<ApiResponse> RegisterInstanceAsync(string baseUrl, RegisterTspiotRequest request, CancellationToken cancellationToken)
            {
                RegisterCalls++;
                return Task.FromResult(RegisterResponses.Count == 0 ? Success("{}") : RegisterResponses.Dequeue());
            }

            public Task<ApiResponse> DeleteInstanceAsync(string baseUrl, string id, CancellationToken cancellationToken)
            {
                DeleteCalls++;
                LastDeletedId = id;
                return Task.FromResult(DeleteResponses.Count == 0 ? Success("{}") : DeleteResponses.Dequeue());
            }

            public Task<ApiResponse> ConfigureLmGatewayAsync(
                string baseUrl,
                string id,
                LmConnectionRequest request,
                CancellationToken cancellationToken)
            {
                LmGatewayCalls.Add(new LmGatewayCall
                {
                    BaseUrl = baseUrl,
                    Id = id,
                    Request = request
                });
                if (LmGatewayCallObserved != null)
                {
                    LmGatewayCallObserved(LmGatewayCalls.Count);
                }
                return Task.FromResult(LmGatewayResponses.Count == 0
                    ? Success("{}")
                    : LmGatewayResponses.Dequeue());
            }

            public Task<ApiResponse> GetLmInfoAsync(
                string baseUrl,
                string instancePort,
                string softPort,
                CancellationToken cancellationToken)
            {
                LmInfoCalls++;
                // Настоящий ЕСМ отвечает одинаково, пока состояние не
                // изменилось. Очередь, иссякая, повторяет последний ответ:
                // иначе повторная проверка получала бы обрыв связи.
                if (LmInfoResponses.Count > 0)
                {
                    _lastLmInfoResponse = LmInfoResponses.Dequeue();
                }

                return Task.FromResult(_lastLmInfoResponse ?? ConnectionFailure());
            }
        }

        private sealed class LmGatewayCall
        {
            public string BaseUrl { get; set; }
            public string Id { get; set; }
            public LmConnectionRequest Request { get; set; }
        }

        private sealed class RecordingHttpHandler : HttpMessageHandler
        {
            public RecordingHttpHandler()
            {
                Requests = new List<RecordedHttpRequest>();
                ResponseBody = "{}";
            }

            public IList<RecordedHttpRequest> Requests { get; private set; }
            public string ResponseBody { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string body = request.Content == null ? string.Empty : request.Content.ReadAsStringAsync().Result;
                Requests.Add(new RecordedHttpRequest
                {
                    Method = request.Method.Method,
                    Url = request.RequestUri.OriginalString,
                    Body = body,
                    ContentType = request.Content == null || request.Content.Headers.ContentType == null
                        ? string.Empty
                        : request.Content.Headers.ContentType.ToString(),
                    Accept = request.Headers.Accept == null
                        ? string.Empty
                        : string.Join(",", request.Headers.Accept)
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ResponseBody ?? string.Empty)
                });
            }
        }

        private sealed class ThrowingHttpHandler : HttpMessageHandler
        {
            private readonly Exception _exception;

            internal ThrowingHttpHandler(Exception exception)
            {
                _exception = exception;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                TaskCompletionSource<HttpResponseMessage> completion =
                    new TaskCompletionSource<HttpResponseMessage>();
                completion.SetException(_exception);
                return completion.Task;
            }
        }
    }
}
