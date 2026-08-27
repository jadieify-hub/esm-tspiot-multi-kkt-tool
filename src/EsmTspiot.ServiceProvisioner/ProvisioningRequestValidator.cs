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
                request.CleanupConfirmation != null)
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
            if (HasItems(request) || request.RemovalConfirmation != null || request.CleanupConfirmation != null)
            {
                result.Add("InstallControllerVersion принимает ровно один выбранный установщик.");
            }
            ValidateInstallerShape(request.InstallerSelection, result);
        }

        private static void ValidateRemove(
            LmServiceProvisioningBatchRequest request,
            ValidationResult result)
        {
            if (HasItems(request) || request.InstallerSelection != null || request.CleanupConfirmation != null)
            {
                result.Add("RemoveManaged принимает только одно подтверждение удаления.");
            }
            LmRemovalConfirmation confirmation = request.RemovalConfirmation;
            if (confirmation == null)
            {
                result.Add("Не задано подтверждение удаления.");
                return;
            }
            ValidateSerialAndFingerprint(confirmation.KktSerial, confirmation.ManifestFingerprint, result);
            if (confirmation.GrpcPort < 1 || confirmation.GrpcPort > 65535 ||
                confirmation.RestPort < 1 || confirmation.RestPort > 65535 ||
                confirmation.GrpcPort == confirmation.RestPort)
            {
                result.Add("Порты в подтверждении удаления неверны.");
            }
            if (!confirmation.RetainedEsmWarningAccepted)
            {
                result.Add("Не подтверждено, что настройка в ЕСМ может остаться после локального удаления.");
            }
        }

        private static void ValidateCleanup(
            LmServiceProvisioningBatchRequest request,
            ValidationResult result)
        {
            if (HasItems(request) || request.InstallerSelection != null || request.RemovalConfirmation != null)
            {
                result.Add("CleanupManaged принимает только одно подтверждение очистки.");
            }
            LmCleanupConfirmation confirmation = request.CleanupConfirmation;
            if (confirmation == null)
            {
                result.Add("Не задано подтверждение очистки.");
                return;
            }
            ValidateSerialAndFingerprint(confirmation.KktSerial, confirmation.ManifestFingerprint, result);
            if (confirmation.DisplayedState != LmServiceProvisioningStatus.CleanupPending)
            {
                result.Add("Очистка разрешена только из показанного состояния CleanupPending.");
            }
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

        private static bool HasItems(LmServiceProvisioningBatchRequest request)
        {
            return request.Items != null && request.Items.Count > 0;
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
