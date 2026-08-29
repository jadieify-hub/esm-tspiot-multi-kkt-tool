using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class ProvisioningRequestValidator
    {
        internal const int CurrentSchemaVersion = 1;
        internal const int MaximumBatchSize = 32;

        internal static ValidationResult Validate(LmServiceProvisioningBatchRequest request)
        {
            ValidationResult result = new ValidationResult();
            if (request == null)
            {
                result.Add("Запрос помощника не задан.");
                return result;
            }

            if (request.SchemaVersion != CurrentSchemaVersion)
            {
                result.Add("Версия схемы запроса не поддерживается.");
            }
            if (!ProvisionerCommandLine.IsGuidN(request.OperationId))
            {
                result.Add("operationId должен содержать 32 шестнадцатеричных символа.");
            }
            if (!IsValidSid(request.InitiatingSid))
            {
                result.Add("Неверная Windows SID инициирующего пользователя.");
            }
            if (!IsHex(request.PlanHash, 64))
            {
                result.Add("Неверный SHA-256 показанного плана.");
            }

            if (request.Operation == LmServiceOperation.EnsureBatch)
            {
                ValidateEnsure(request, result);
            }
            else if (request.Operation == LmServiceOperation.InstallControllerVersion)
            {
                ValidateInstall(request, result);
            }
            else if (request.Operation == LmServiceOperation.RemoveManaged)
            {
                ValidateRemove(request, result);
            }
            else if (request.Operation == LmServiceOperation.CleanupManaged)
            {
                ValidateCleanup(request, result);
            }
            else if (request.Operation == LmServiceOperation.RemoveAllManaged)
            {
                ValidateRemoveAll(request, result);
            }
            else if (request.Operation == LmServiceOperation.EnsureManagedLocalModules)
            {
                ValidateManagedLocalModules(request, result);
            }
            else
            {
                result.Add("Операция помощника не поддерживается.");
            }

            if (IsHex(request.PlanHash, 64))
            {
                string expectedHash = CanonicalLmPlanHasher.Compute(request);
                if (!CanonicalLmPlanHasher.FixedTimeEqualsHex(request.PlanHash, expectedHash))
                {
                    result.Add("SHA-256 запроса не совпадает с показанным пользователю планом.");
                }
            }

            return result;
        }

        internal static ValidationResult ValidateInstallerSelection(
            LmControllerInstallerSelection selected,
            LmControllerInstallerSelection observed)
        {
            ValidationResult result = new ValidationResult();
            ValidateInstallerShape(selected, result);
            ValidateInstallerShape(observed, result);
            if (!result.IsValid)
            {
                return result;
            }

            if (!string.Equals(selected.SourcePath, observed.SourcePath, StringComparison.Ordinal) ||
                !string.Equals(selected.FileName, observed.FileName, StringComparison.Ordinal) ||
                selected.ByteLength != observed.ByteLength ||
                !CanonicalLmPlanHasher.FixedTimeEqualsHex(selected.Sha256, observed.Sha256) ||
                !string.Equals(selected.FileVersion, observed.FileVersion, StringComparison.Ordinal) ||
                !string.Equals(selected.ProductVersion, observed.ProductVersion, StringComparison.Ordinal) ||
                !string.Equals(selected.SignerSubject, observed.SignerSubject, StringComparison.Ordinal) ||
                !string.Equals(selected.SignerThumbprint, observed.SignerThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                result.Add("Выбранный файл установщика изменился или был подменен после подтверждения.");
            }

            return result;
        }

        internal static ValidationResult ValidateLocalModuleInstallerSelection(
            LocalModuleInstallerSelection selection)
        {
            ValidationResult result = new ValidationResult();
            ValidateLocalModuleInstallerShape(selection, result);
            return result;
        }

        internal static ValidationResult ValidateLocalModuleInstallerSelection(
            LocalModuleInstallerSelection selected,
            LocalModuleInstallerSelection observed)
        {
            ValidationResult result = new ValidationResult();
            ValidateLocalModuleInstallerShape(selected, result);
            ValidateLocalModuleInstallerShape(observed, result);
            if (!result.IsValid)
            {
                return result;
            }

            if (!string.Equals(selected.SourcePath, observed.SourcePath, StringComparison.Ordinal) ||
                !string.Equals(selected.FileName, observed.FileName, StringComparison.Ordinal) ||
                selected.ByteLength != observed.ByteLength ||
                !CanonicalLmPlanHasher.FixedTimeEqualsHex(selected.Sha256, observed.Sha256) ||
                !string.Equals(selected.ProductName, observed.ProductName, StringComparison.Ordinal) ||
                !string.Equals(selected.ProductVersion, observed.ProductVersion, StringComparison.Ordinal) ||
                !string.Equals(selected.ProductCode, observed.ProductCode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(selected.UpgradeCode, observed.UpgradeCode, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(selected.SignerSubject, observed.SignerSubject, StringComparison.Ordinal) ||
                !string.Equals(
                    selected.SignerThumbprint,
                    observed.SignerThumbprint,
                    StringComparison.OrdinalIgnoreCase) ||
                selected.LicenseNoticeAccepted != observed.LicenseNoticeAccepted)
            {
                result.Add("Выбранный MSI ЛМ изменился или не совпадает с поддерживаемым пакетом.");
            }

            return result;
        }

        internal static ValidationResult ValidateSessionMessage(
            ManagedProvisioningSessionMessage message,
            string expectedOperationId,
            long lastSequence,
            int itemCount)
        {
            ValidationResult result = new ValidationResult();
            if (message == null)
            {
                result.Add("Сообщение сеанса не задано.");
                return result;
            }
            if (message.SchemaVersion != CurrentSchemaVersion)
            {
                result.Add("Версия схемы сообщения сеанса не поддерживается.");
            }
            if (!ProvisionerCommandLine.IsGuidN(expectedOperationId) ||
                !string.Equals(
                    message.OperationId,
                    expectedOperationId,
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add("Сообщение относится к другому сеансу.");
            }
            if (message.Sequence <= 0 || message.Sequence <= lastSequence)
            {
                result.Add("Номер сообщения сеанса должен монотонно увеличиваться.");
            }
            if (!IsKnownSessionKind(message.Kind))
            {
                result.Add("Тип сообщения сеанса не поддерживается.");
                return result;
            }
            bool itemMessage =
                message.Kind == ManagedProvisioningSessionKind.ExecuteItem ||
                message.Kind == ManagedProvisioningSessionKind.ItemResult;
            if (itemMessage)
            {
                if (message.ItemIndex < 0 || message.ItemIndex >= itemCount)
                {
                    result.Add("Индекс строки сообщения сеанса находится вне плана.");
                }
            }
            else if (message.ItemIndex != -1)
            {
                result.Add("Сообщение сеанса этого типа не должно ссылаться на строку.");
            }
            if (message.Message != null && message.Message.Length > 4096)
            {
                result.Add("Текст сообщения сеанса превышает допустимый размер.");
            }
            return result;
        }

        private static void ValidateEnsure(
            LmServiceProvisioningBatchRequest request,
            ValidationResult result)
        {
            int count = request.Items == null ? 0 : request.Items.Count;
            if (count < 1 || count > MaximumBatchSize)
            {
                result.Add("Операция EnsureBatch должна содержать от 1 до 32 ККТ.");
            }
            if (request.InstallerSelection != null ||
                request.RemovalConfirmation != null ||
                request.CleanupConfirmation != null ||
                HasRemovalConfirmations(request) ||
                HasLocalModulePayload(request))
            {
                result.Add("EnsureBatch не принимает payload другой операции.");
            }

            HashSet<string> serials = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> ports = new HashSet<int>();
            for (int index = 0; index < count; index++)
            {
                LmServiceProvisioningItemRequest item = request.Items[index];
                if (item == null)
                {
                    result.Add("Строка EnsureBatch не задана.");
                    continue;
                }

                try
                {
                    LmServiceIdentity.CreateName(item.KktSerial);
                }
                catch (ArgumentException)
                {
                    result.Add("Серийный номер ККТ должен содержать 14 ASCII-цифр.");
                }

                if (!serials.Add(item.KktSerial ?? string.Empty))
                {
                    result.Add("В EnsureBatch повторяется серийный номер ККТ.");
                }
                ValidateLocalPort(item.GrpcPort, "gRPC", ports, result);
                ValidateLocalPort(item.RestPort, "REST", ports, result);

                ValidationResult targetValidation = LmGatewayInputValidator.ValidateTarget(
                    new LmGatewayTarget(item.TargetAddress, item.TargetPort));
                CopyValidation(targetValidation, result);
                if (targetValidation.IsValid)
                {
                    string normalizedAddress;
                    bool isLoopback;
                    LmGatewayInputValidator.TryNormalizeTargetAddress(
                        item.TargetAddress,
                        out normalizedAddress,
                        out isLoopback);
                    if (!string.Equals(item.TargetAddress, normalizedAddress, StringComparison.Ordinal))
                    {
                        result.Add("Адрес целевого ЛМ в EnsureBatch должен быть в канонической форме.");
                    }
                }
            }
        }

        private static void ValidateInstall(
            LmServiceProvisioningBatchRequest request,
            ValidationResult result)
        {
            if (HasItems(request) || request.RemovalConfirmation != null ||
                request.CleanupConfirmation != null || HasRemovalConfirmations(request) ||
                HasLocalModulePayload(request))
            {
                result.Add("InstallControllerVersion принимает ровно один выбранный установщик.");
            }
            ValidateInstallerShape(request.InstallerSelection, result);
        }

        private static void ValidateManagedLocalModules(
            LmServiceProvisioningBatchRequest request,
            ValidationResult result)
        {
            if (HasItems(request) ||
                request.RemovalConfirmation != null || request.CleanupConfirmation != null ||
                HasRemovalConfirmations(request))
            {
                result.Add("EnsureManagedLocalModules принимает два установщика и сгруппированный план ККТ.");
            }
            ValidateInstallerShape(request.InstallerSelection, result);
            ValidateLocalModuleInstallerShape(request.LocalModuleInstallerSelection, result);

            int count = request.ManagedLocalModules == null
                ? 0
                : request.ManagedLocalModules.Count;
            if (count < 1 || count > MaximumBatchSize)
            {
                result.Add("EnsureManagedLocalModules должен содержать от 1 до 32 ККТ.");
                return;
            }

            HashSet<string> serials = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> completedInns = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> kktOrdinals = new HashSet<int>();
            HashSet<int> moduleOrdinals = new HashSet<int>();
            HashSet<int> ports = new HashSet<int>();
            int previousModuleOrdinal = 0;
            int previousKktOrdinal = 0;
            string currentInn = null;
            ManagedLocalModuleProvisioningItemRequest currentModule = null;
            for (int index = 0; index < count; index++)
            {
                ManagedLocalModuleProvisioningItemRequest item =
                    request.ManagedLocalModules[index];
                if (item == null)
                {
                    result.Add("Строка управляемого ЛМ не задана.");
                    continue;
                }

                try
                {
                    LmServiceIdentity.CreateName(item.KktSerial);
                }
                catch (ArgumentException)
                {
                    result.Add("Серийный номер ККТ должен содержать 14 ASCII-цифр.");
                }
                if (!serials.Add(item.KktSerial ?? string.Empty))
                {
                    result.Add("ККТ повторяется в плане управляемых ЛМ.");
                }
                if (!IsAsciiDigits(item.Inn, 10) && !IsAsciiDigits(item.Inn, 12))
                {
                    result.Add("ИНН управляемого ЛМ должен содержать 10 или 12 ASCII-цифр.");
                }
                ValidateOrdinal(item.KktOrdinal, "K", kktOrdinals, result);
                bool beginsGroup = !string.Equals(
                    currentInn,
                    item.Inn,
                    StringComparison.Ordinal);
                if (beginsGroup)
                {
                    if (currentInn != null)
                    {
                        completedInns.Add(currentInn);
                    }
                    if (completedInns.Contains(item.Inn ?? string.Empty))
                    {
                        result.Add("Строки одного ИНН должны идти подряд.");
                    }
                    ValidateOrdinal(
                        item.LocalModuleOrdinal,
                        "N",
                        moduleOrdinals,
                        result);
                    if (item.LocalModuleOrdinal <= previousModuleOrdinal)
                    {
                        result.Add("Группы ЛМ должны быть отсортированы по номеру N.");
                    }
                    previousModuleOrdinal = item.LocalModuleOrdinal;
                    previousKktOrdinal = 0;
                    currentInn = item.Inn;
                    currentModule = item;
                    ValidateLocalPort(item.ApiPort, "API ЛМ", ports, result);
                    ValidateLocalPort(item.DatabasePort, "база ЛМ", ports, result);
                    ValidateLocalPort(item.EpmdPort, "EPMD ЛМ", ports, result);
                }
                else if (currentModule == null ||
                    item.LocalModuleOrdinal != currentModule.LocalModuleOrdinal ||
                    item.ApiPort != currentModule.ApiPort ||
                    item.DatabasePort != currentModule.DatabasePort ||
                    item.EpmdPort != currentModule.EpmdPort ||
                    !string.Equals(
                        item.RuntimeVersion,
                        currentModule.RuntimeVersion,
                        StringComparison.Ordinal))
                {
                    result.Add("Все ККТ одного ИНН должны ссылаться на один неизменный ЛМ.");
                }
                if (item.KktOrdinal <= previousKktOrdinal)
                {
                    result.Add("ККТ внутри группы ИНН должны быть отсортированы по номеру K.");
                }
                previousKktOrdinal = item.KktOrdinal;
                ValidateLocalPort(item.ControllerGrpcPort, "gRPC контроллера", ports, result);
                ValidateLocalPort(item.ControllerRestPort, "REST контроллера", ports, result);
                if (string.IsNullOrWhiteSpace(item.RuntimeVersion) ||
                    request.LocalModuleInstallerSelection == null ||
                    !string.Equals(
                        item.RuntimeVersion,
                        request.LocalModuleInstallerSelection.ProductVersion,
                        StringComparison.Ordinal))
                {
                    result.Add("Версия runtime строки не совпадает с выбранным MSI ЛМ.");
                }
            }
        }

        private static void ValidateRemove(
            LmServiceProvisioningBatchRequest request,
            ValidationResult result)
        {
            if (HasItems(request) || request.InstallerSelection != null ||
                request.CleanupConfirmation != null || HasRemovalConfirmations(request) ||
                HasLocalModulePayload(request))
            {
                result.Add("RemoveManaged принимает только одно подтверждение удаления.");
            }
            LmRemovalConfirmation confirmation = request.RemovalConfirmation;
            if (confirmation == null)
            {
                result.Add("Не задано подтверждение удаления.");
                return;
            }
            ValidateRemovalConfirmation(confirmation, result);
        }

        private static void ValidateCleanup(
            LmServiceProvisioningBatchRequest request,
            ValidationResult result)
        {
            if (HasItems(request) || request.InstallerSelection != null ||
                request.RemovalConfirmation != null || HasRemovalConfirmations(request) ||
                HasLocalModulePayload(request))
            {
                result.Add("CleanupManaged принимает только одно подтверждение очистки.");
            }
            LmCleanupConfirmation confirmation = request.CleanupConfirmation;
            if (confirmation == null)
            {
                result.Add("Не задано подтверждение очистки.");
                return;
            }
            ValidateSerial(confirmation.KktSerial, result);
            bool legacy = IsFingerprint(confirmation.ManifestFingerprint);
            bool managed = IsFingerprint(confirmation.ManagedStateFingerprint);
            if (confirmation.ManifestFingerprint != null && !legacy)
            {
                result.Add("Неверный отпечаток манифеста контроллера при очистке.");
            }
            if (confirmation.ManagedStateFingerprint != null && !managed)
            {
                result.Add("Неверный отпечаток показанного состояния очистки.");
            }
            if (!legacy && !managed)
            {
                result.Add(
                    "Не задан отпечаток показанного состояния очистки.");
            }
            if (confirmation.DisplayedState != LmServiceProvisioningStatus.CleanupPending)
            {
                result.Add("Очистка разрешена только из показанного состояния CleanupPending.");
            }
        }

        private static void ValidateRemoveAll(
            LmServiceProvisioningBatchRequest request,
            ValidationResult result)
        {
            if (HasItems(request) || request.InstallerSelection != null ||
                request.RemovalConfirmation != null || request.CleanupConfirmation != null ||
                HasLocalModulePayload(request))
            {
                result.Add("RemoveAllManaged принимает только список подтверждений удаления.");
            }

            int count = request.RemovalConfirmations == null
                ? 0
                : request.RemovalConfirmations.Count;
            if (count < 1 || count > MaximumBatchSize)
            {
                result.Add("RemoveAllManaged должен содержать от 1 до 32 подтверждений.");
                return;
            }

            HashSet<string> serials = new HashSet<string>(StringComparer.Ordinal);
            string previousSerial = null;
            for (int index = 0; index < count; index++)
            {
                LmRemovalConfirmation confirmation = request.RemovalConfirmations[index];
                if (confirmation == null)
                {
                    result.Add("Подтверждение пакетного удаления не задано.");
                    continue;
                }

                ValidateRemovalConfirmation(confirmation, result);
                string serial = confirmation.KktSerial ?? string.Empty;
                if (!serials.Add(serial))
                {
                    result.Add("В RemoveAllManaged повторяется серийный номер ККТ.");
                }
                if (previousSerial != null &&
                    string.CompareOrdinal(previousSerial, serial) >= 0)
                {
                    result.Add("Подтверждения RemoveAllManaged должны быть отсортированы по серийному номеру ККТ.");
                }
                previousSerial = serial;
            }
        }

        private static void ValidateRemovalConfirmation(
            LmRemovalConfirmation confirmation,
            ValidationResult result)
        {
            ValidateSerial(confirmation.KktSerial, result);
            bool legacy = IsFingerprint(confirmation.ManifestFingerprint);
            bool managed = IsFingerprint(
                confirmation.ManagedStateFingerprint);
            if (confirmation.ManifestFingerprint != null && !legacy)
            {
                result.Add("Неверный отпечаток манифеста контроллера.");
            }
            if (confirmation.ManagedStateFingerprint != null && !managed)
            {
                result.Add("Неверный отпечаток показанного состояния комплекта.");
            }
            if (!legacy && !managed)
            {
                result.Add(
                    "Не задан отпечаток показанного управляемого состояния.");
            }
            if (legacy &&
                (confirmation.GrpcPort < 1 ||
                 confirmation.GrpcPort > 65535 ||
                 confirmation.RestPort < 1 ||
                 confirmation.RestPort > 65535 ||
                 confirmation.GrpcPort == confirmation.RestPort))
            {
                result.Add("Порты в подтверждении удаления неверны.");
            }
            if (!confirmation.RetainedEsmWarningAccepted)
            {
                result.Add("Не подтверждено, что настройка в ЕСМ может остаться после локального удаления.");
            }
        }

        private static void ValidateSerial(
            string serial,
            ValidationResult result)
        {
            if (!IsAsciiDigits(serial, 14))
            {
                result.Add("Серийный номер ККТ в подтверждении неверен.");
            }
        }

        private static bool IsFingerprint(LmManifestFingerprint fingerprint)
        {
            return fingerprint != null && IsHex(fingerprint.Sha256, 64);
        }

        private static void ValidateInstallerShape(
            LmControllerInstallerSelection selection,
            ValidationResult result)
        {
            if (selection == null)
            {
                result.Add("Не выбран установщик контроллера ЛМ.");
                return;
            }
            if (string.IsNullOrEmpty(selection.SourcePath) ||
                !Path.IsPathRooted(selection.SourcePath) ||
                !string.Equals(Path.GetFileName(selection.SourcePath), selection.FileName, StringComparison.Ordinal))
            {
                result.Add("Путь и имя выбранного установщика не совпадают.");
            }
            if (!IsVersionedInstallerFileName(selection.FileName))
            {
                result.Add("Имя установщика должно иметь вид esm-lm-controller_<version>-windows-setup.exe.");
            }
            if (selection.ByteLength <= 0 || !IsHex(selection.Sha256, 64))
            {
                result.Add("Размер и SHA-256 установщика неверны.");
            }
            if (string.IsNullOrWhiteSpace(selection.FileVersion) ||
                selection.ProductVersion == null ||
                string.IsNullOrWhiteSpace(selection.SignerSubject) ||
                !IsHex(selection.SignerThumbprint, 40))
            {
                result.Add("Метаданные версии или подписанта установщика неверны.");
            }
            if (!selection.StopManagedInstancesWarningAccepted)
            {
                result.Add("Не подтверждена остановка всех управляемых экземпляров на время установки.");
            }
        }

        private static void ValidateLocalModuleInstallerShape(
            LocalModuleInstallerSelection selection,
            ValidationResult result)
        {
            if (selection == null)
            {
                result.Add("Не выбран MSI ЛМ ЧЗ.");
                return;
            }
            if (string.IsNullOrEmpty(selection.SourcePath) ||
                !Path.IsPathRooted(selection.SourcePath) ||
                !string.Equals(
                    Path.GetFileName(selection.SourcePath),
                    selection.FileName,
                    StringComparison.Ordinal) ||
                string.IsNullOrEmpty(selection.FileName) ||
                !selection.FileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                result.Add("Путь и имя выбранного MSI ЛМ не совпадают.");
            }
            if (selection.ByteLength <= 0 || !IsHex(selection.Sha256, 64))
            {
                result.Add("Размер и SHA-256 MSI ЛМ неверны.");
            }
            if (string.IsNullOrWhiteSpace(selection.ProductName) ||
                string.IsNullOrWhiteSpace(selection.ProductVersion) ||
                !IsGuid(selection.ProductCode) ||
                !IsGuid(selection.UpgradeCode) ||
                string.IsNullOrWhiteSpace(selection.SignerSubject) ||
                !IsHex(selection.SignerThumbprint, 40))
            {
                result.Add("Метаданные продукта или подписанта MSI ЛМ неверны.");
            }
            if (!selection.LicenseNoticeAccepted)
            {
                result.Add("Не подтверждено использование официального ЛМ для перечисленных ИНН.");
            }
        }

        private static void ValidateSerialAndFingerprint(
            string serial,
            LmManifestFingerprint fingerprint,
            ValidationResult result)
        {
            try
            {
                LmServiceIdentity.CreateName(serial);
            }
            catch (ArgumentException)
            {
                result.Add("Неверный серийный номер ККТ в подтверждении.");
            }
            if (fingerprint == null || !IsHex(fingerprint.Sha256, 64))
            {
                result.Add("Неверный fingerprint манифеста.");
            }
        }

        private static void ValidateLocalPort(
            int port,
            string role,
            HashSet<int> occupied,
            ValidationResult result)
        {
            if (port < 1 || port > 65535)
            {
                result.Add(role + "-порт должен быть в диапазоне 1-65535.");
                return;
            }
            if (!occupied.Add(port))
            {
                result.Add("Локальный порт " + port.ToString() + " повторяется в batch.");
            }
        }

        private static void ValidateOrdinal(
            int ordinal,
            string role,
            HashSet<int> occupied,
            ValidationResult result)
        {
            if (ordinal < 1 || ordinal > MaximumBatchSize)
            {
                result.Add("Номер " + role + " должен быть в диапазоне 1-32.");
                return;
            }
            if (!occupied.Add(ordinal))
            {
                result.Add("Номер " + role + " повторяется в плане.");
            }
        }

        private static bool HasItems(LmServiceProvisioningBatchRequest request)
        {
            return request.Items != null && request.Items.Count > 0;
        }

        private static bool HasRemovalConfirmations(LmServiceProvisioningBatchRequest request)
        {
            return request.RemovalConfirmations != null && request.RemovalConfirmations.Count > 0;
        }

        private static bool HasLocalModulePayload(LmServiceProvisioningBatchRequest request)
        {
            return request.LocalModuleInstallerSelection != null ||
                (request.ManagedLocalModules != null && request.ManagedLocalModules.Count > 0);
        }

        private static bool IsKnownSessionKind(ManagedProvisioningSessionKind kind)
        {
            return kind == ManagedProvisioningSessionKind.SessionReady ||
                kind == ManagedProvisioningSessionKind.ExecuteItem ||
                kind == ManagedProvisioningSessionKind.ItemResult ||
                kind == ManagedProvisioningSessionKind.Finish ||
                kind == ManagedProvisioningSessionKind.CancelAfterCurrentItem;
        }

        private static bool IsAsciiDigits(string value, int length)
        {
            if (value == null || value.Length != length)
            {
                return false;
            }
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsGuid(string value)
        {
            Guid parsed;
            return !string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out parsed);
        }

        private static bool IsVersionedInstallerFileName(string value)
        {
            const string prefix = "esm-lm-controller_";
            const string suffix = "-windows-setup.exe";
            if (string.IsNullOrEmpty(value) ||
                !value.StartsWith(prefix, StringComparison.Ordinal) ||
                !value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string version = value.Substring(prefix.Length, value.Length - prefix.Length - suffix.Length);
            if (version.Length == 0)
            {
                return false;
            }
            for (int index = 0; index < version.Length; index++)
            {
                char character = version[index];
                if ((character < '0' || character > '9') && character != '.')
                {
                    return false;
                }
            }

            return version[0] != '.' && version[version.Length - 1] != '.' &&
                version.IndexOf("..", StringComparison.Ordinal) < 0;
        }

        private static bool IsValidSid(string value)
        {
            try
            {
                SecurityIdentifier ignored = new SecurityIdentifier(value);
                return ignored.BinaryLength > 0;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length)
            {
                return false;
            }
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool isDigit = character >= '0' && character <= '9';
                bool isLower = character >= 'a' && character <= 'f';
                bool isUpper = character >= 'A' && character <= 'F';
                if (!isDigit && !isLower && !isUpper)
                {
                    return false;
                }
            }

            return true;
        }

        private static void CopyValidation(ValidationResult source, ValidationResult destination)
        {
            for (int index = 0; index < source.Messages.Count; index++)
            {
                destination.Add(source.Messages[index]);
            }
        }
    }
}
