using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;

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
            Run("Dkkt parser reads kkt array", DkktParserReadsKktArray);
            Run("Dkkt selector excludes already created instances", DkktSelectorExcludesAlreadyCreatedInstances);
            Run("Port allocator selects pair after existing instances", PortAllocatorSelectsPairAfterExistingInstances);
            Run("Port allocator reserves either side and consecutive selections", PortAllocatorReservesEitherSideAndConsecutiveSelections);
            Run("Port allocator reserves the primary KKT pair", PortAllocatorReservesPrimaryKktPair);
            Run("Bulk planner assigns first sequential port pairs", BulkPlannerAssignsFirstSequentialPortPairs);
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
            Run("LM gateway planner never adopts official base service", LmGatewayPlannerNeverAdoptsOfficialBaseService);
            Run("LM gateway planner allocates sequential local ports", LmGatewayPlannerAllocatesSequentialLocalPorts);
            Run("LM gateway planner keeps owned and skips foreign listener", LmGatewayPlannerKeepsOwnedAndSkipsForeignListener);
            Run("LM listener snapshot projects only ready managed owners", LmListenerSnapshotProjectsOnlyReadyManagedOwners);
            Run("LM gateway planner preserves matching managed assignment", LmGatewayPlannerPreservesMatchingManagedAssignment);
            Run("LM gateway planner rejects unsafe target and all port conflicts", LmGatewayPlannerRejectsUnsafeTargetAndAllPortConflicts);
            Run("Managed LM service spec contains no credentials", ManagedLmServiceSpecContainsNoCredentials);
            Run("LM binding session does not invent readback", LmBindingSessionDoesNotInventReadback);
            Run("LM binding session preserves current drafts on refresh", LmBindingSessionPreservesCurrentDraftsOnRefresh);
            Run("LM binding session discards a draft after INN changes", LmBindingSessionDiscardsDraftAfterInnChanges);
            Run("LM binding session builds a plan only for selected KKT", LmBindingSessionBuildsPlanOnlyForSelectedKkt);
            Run("LM binding session masks outcome details", LmBindingSessionMasksOutcomeDetails);
            Run("LM binding workflow sends items sequentially", LmBindingWorkflowSendsItemsSequentially);
            Run("LM binding workflow skips invalid item and continues", LmBindingWorkflowSkipsInvalidItemAndContinues);
            Run("LM binding workflow marks lost response for attention", LmBindingWorkflowMarksLostResponseForAttention);
            Run("LM binding workflow does not retry permanent HTTP error", LmBindingWorkflowDoesNotRetryPermanentHttpError);
            Run("LM binding workflow preserves partial results on cancellation", LmBindingWorkflowPreservesPartialResultsOnCancellation);
            Run("LM binding workflow progress never contains password", LmBindingWorkflowProgressNeverContainsPassword);
            Run("API client preserves an injected timeout", ApiClientPreservesInjectedTimeout);
            Run("Instruction selector uses the newest file time", InstructionSelectorUsesNewestFileTime);
            Run("Deletion planner protects primary KKT", DeletionPlannerProtectsPrimaryKkt);
            Run("Deletion planner blocks ambiguous primary KKT", DeletionPlannerBlocksAmbiguousPrimaryKkt);
            Run("Deletion confirmation requires last four digits", DeletionConfirmationRequiresLastFourDigits);
            Run("Deletion workflow refuses primary KKT", DeletionWorkflowRefusesPrimaryKkt);
            Run("Deletion workflow verifies additional KKT removal", DeletionWorkflowVerifiesAdditionalKktRemoval);
            Run("Deletion workflow reports unverified removal", DeletionWorkflowReportsUnverifiedRemoval);
            Run("Remote base URL produces warning", RemoteBaseUrlProducesWarning);
            Run("Base URL rejects credentials and query", BaseUrlRejectsCredentialsAndQuery);
            Run("Unicode digits are rejected", UnicodeDigitsAreRejected);
            Run("Bulk workflow resumes incomplete existing instance", BulkWorkflowResumesIncompleteExistingInstance);
            Run("Bulk workflow continues after add failure", BulkWorkflowContinuesAfterAddFailure);
            Run("Bulk workflow retries service not started", BulkWorkflowRetriesServiceNotStarted);
            Run("Bulk workflow retries connection failure during add", BulkWorkflowRetriesConnectionFailureDuringAdd);
            Run("Bulk workflow retries connection failure during registration", BulkWorkflowRetriesConnectionFailureDuringRegistration);
            Run("Bulk workflow recovers when retry reports existing instance", BulkWorkflowRecoversWhenRetryReportsExistingInstance);
            Run("Bulk workflow skips PUT when readiness is not confirmed", BulkWorkflowSkipsPutWhenReadinessIsNotConfirmed);
            Run("Bulk workflow honors cancellation", BulkWorkflowHonorsCancellation);
            Run("Bulk workflow preserves results when request is cancelled", BulkWorkflowPreservesResultsWhenRequestIsCancelled);
            Run("Diagnostic masker hides fiscal identifiers", DiagnosticMaskerHidesFiscalIdentifiers);
            Run("Diagnostic masker hides local user paths", DiagnosticMaskerHidesLocalUserPaths);
            Run("Diagnostic masker hides common secrets", DiagnosticMaskerHidesCommonSecrets);
            Run("Sensitive masker redacts JSON credentials", SensitiveMaskerRedactsJsonCredentials);
            Run("Sensitive masker redacts key value credentials", SensitiveMaskerRedactsKeyValueCredentials);
            Run("Log formatter never persists reflected password", LogFormatterNeverPersistsReflectedPassword);
            Run("Sensitive masker preserves ordinary fields", SensitiveMaskerPreservesOrdinaryFields);
            Run("Display log trimmer preserves the newest half", DisplayLogTrimmerPreservesNewestHalf);
            Run("File log sink persists text without blocking", FileLogSinkPersistsTextWithoutBlocking);
            Run("LM provisioner contract exposes no arbitrary command", LmProvisionerContractExposesNoArbitraryCommand);
            Run("LM probe result separates service and listener state", LmProbeResultSeparatesServiceAndListenerState);
            Run("LM provisioning progress contains no credentials", LmProvisioningProgressContainsNoCredentials);
            Run("LM lifecycle ensures probes then binds", LmLifecycleEnsuresProbesThenBinds);
            Run("LM lifecycle never binds failed service", LmLifecycleNeverBindsFailedService);
            Run("LM lifecycle continues after one KKT failure", LmLifecycleContinuesAfterOneKktFailure);
            Run("LM lifecycle retries binding without reprovisioning", LmLifecycleRetriesBindingWithoutReprovisioning);
            Run("LM lifecycle preserves partial outcome on cancellation", LmLifecyclePreservesPartialOutcomeOnCancellation);
            Run("LM lifecycle reconciles unknown result before mutation", LmLifecycleReconcilesUnknownResultBeforeMutation);
            Run("LM removal workflow removes one selected managed service", LmRemovalWorkflowRemovesOneSelectedManagedService);
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
            AssertEqual("50402", plan.Items[0].Input.Port, "Expected first additional KKT port.");
            AssertEqual("51402", plan.Items[0].Input.SoftPort, "Expected first additional KKT soft port.");
            AssertEqual("50403", plan.Items[1].Input.Port, "Expected second additional KKT port.");
            AssertEqual("51403", plan.Items[1].Input.SoftPort, "Expected second additional KKT soft port.");
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

        private static void PortAllocatorReservesPrimaryKktPair()
        {
            KktPortPair pair = new KktPortPairAllocator(new List<KktInstanceInfo>()).ReserveNext();

            AssertEqual("50402", pair.Port, "The primary KKT port must never be allocated to an additional KKT.");
            AssertEqual("51402", pair.SoftPort, "The primary KKT softPort must never be allocated to an additional KKT.");
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
            AssertEqual("50402", plan.Items[1].Input.Port, "An invalid device must not consume the first additional port pair.");
            AssertEqual("51402", plan.Items[1].Input.SoftPort, "An invalid device must not consume the first additional softPort.");
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
            AssertContains(summary, "Создано без регистрации: 1");
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
            string json = "{\"clientPort\":51402,\"regData\":{" +
                "\"kktSerial\":\"00105700000001\"," +
                "\"fnSerial\":\"7300000000000001\"," +
                "\"kktInn\":\"1234567894\"}}";
            KktInstanceDetails details;

            AssertTrue(InstanceDetailsParser.TryParse(json, out details), "Expected valid instance details.");
            AssertEqual("51402", details.ClientPort, "Expected client port.");
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
                "Discovery has no documented LM readback and must not claim a binding result.");
            AssertFalse(session.Rows[0].IsSelected, "A newly discovered row must require explicit selection.");
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
            AssertFalse(session.Rows[0].IsSelected,
                "A KKT re-registered to another INN must require explicit selection again.");
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
            LmGatewayBindingWorkflow workflow = new LmGatewayBindingWorkflow(api);

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
            LmGatewayBindingWorkflow workflow = new LmGatewayBindingWorkflow(api);

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
            LmGatewayBindingWorkflow workflow = new LmGatewayBindingWorkflow(api);

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
            LmGatewayBindingWorkflow workflow = new LmGatewayBindingWorkflow(api);

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
            LmGatewayBindingWorkflow workflow = new LmGatewayBindingWorkflow(api);

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
            LmGatewayBindingWorkflow workflow = new LmGatewayBindingWorkflow(api);

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
                    KktInn = inn
                });
                inputs.Add(CreateLmBindingInput(
                    serial,
                    inn,
                    "127.0.0.1",
                    (50062 + index).ToString()));
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

        private static void DeletionPlannerProtectsPrimaryKkt()
        {
            KktDeletionPlan plan = KktDeletionPlanner.Build(new List<KktInstanceInfo>
            {
                Instance("00105700000001", "50401", "0"),
                Instance("00105700000002", "50402", "51402")
            });

            AssertTrue(plan.HasReliablePrimary, "Expected a reliable primary KKT.");
            AssertFalse(plan.Candidates[0].CanDelete, "Primary KKT must be protected.");
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

        private static void DeletionWorkflowRefusesPrimaryKkt()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponses.Enqueue(Success(TwoInstancesJson()));
            KktDeletionWorkflow workflow = CreateDeletionWorkflow(api);

            KktDeletionOutcome outcome = workflow.DeleteAsync(
                "http://127.0.0.1:51077", "00105700000001", "0001", null, CancellationToken.None).Result;

            AssertTrue(outcome.IsBlocked, "Primary KKT deletion must be blocked.");
            AssertFalse(outcome.IsSuccess, "Blocked deletion cannot succeed.");
            AssertEqual(0, api.DeleteCalls, "DELETE must not be sent for the primary KKT.");
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

        private static void BulkWorkflowResumesIncompleteExistingInstance()
        {
            FakeTspiotApiClient api = new FakeTspiotApiClient();
            api.InstancesResponse = Success("{\"instances\":[{\"id\":\"00105700000001\",\"port\":50401,\"softPort\":0}]}");
            api.DkktResponse = Success("{\"kkt\":[{\"kktSerial\":\"00105700000001\",\"fnSerial\":\"7300000000000001\",\"kktInn\":\"1234567894\"}]}");
            api.InstanceResponses.Enqueue(Success("{\"clientPort\":51401}"));
            api.SettingsResponse = Success("[]");
            api.RegisterResponses.Enqueue(Success("{\"tspiotId\":\"1\"}"));
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
            BulkRegistrationWorkflow workflow = CreateWorkflow(api);

            BulkRegistrationDiscovery discovery = workflow.DiscoverAsync(
                "http://127.0.0.1:51077", "4041", null, CancellationToken.None).Result;
            BulkRegistrationOutcome outcome = workflow.ExecuteAsync(
                discovery, null, CancellationToken.None).Result;

            AssertEqual(2, api.RegisterCalls, "Expected PUT retry after a connection failure.");
            AssertEqual(BulkKktRegistrationStatus.Registered, outcome.Results[0].Status, "Expected successful PUT retry.");
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
            api.CancelOnInstanceCall = 2;
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

        private static void SensitiveMaskerRedactsKeyValueCredentials()
        {
            string masked = SensitiveDataMasker.Mask(
                "password=secret-value&authorization=Bearer auth-token&status=ready");

            AssertEqual(
                "password=***&authorization=***&status=ready",
                masked,
                "Expected password and the complete authorization value to be masked.");
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
            AssertEqual(4, methods.Length, "The helper contract must expose only four typed operations.");
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
                new LmGatewayBindingWorkflow(api)).ExecuteAsync(
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

        private static LmGatewayLifecycleWorkflow CreateLifecycleWorkflow(
            FakeLmProvisioner provisioner,
            FakeLmProbe probe,
            FakeTspiotApiClient api)
        {
            return new LmGatewayLifecycleWorkflow(
                provisioner,
                probe,
                new LmGatewayBindingWorkflow(api));
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
                LmGatewayResponses = new Queue<ApiResponse>();
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
            public Queue<ApiResponse> LmGatewayResponses { get; private set; }
            public IList<LmGatewayCall> LmGatewayCalls { get; private set; }
            public int AddCalls { get; private set; }
            public int RegisterCalls { get; private set; }
            public int DeleteCalls { get; private set; }
            public int InstanceCalls { get; private set; }
            public int InstancesCalls { get; private set; }
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
                return Task.FromResult(DkktResponse);
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
            }

            public IList<RecordedHttpRequest> Requests { get; private set; }

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
                        : request.Content.Headers.ContentType.ToString()
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            }
        }
    }
}
