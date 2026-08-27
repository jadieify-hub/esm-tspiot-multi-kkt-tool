using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
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
            string masked = SensitiveDataMasker.Mask("password=secret-value&status=ready");

            AssertEqual("password=***&status=ready", masked, "Expected only the password value to be masked.");
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
