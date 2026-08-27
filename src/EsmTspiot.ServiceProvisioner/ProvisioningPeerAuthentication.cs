using System;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class ProvisioningPeerEvidence
    {
        internal string ExpectedSid { get; set; }
        internal string ActualSid { get; set; }
        internal string ExpectedImagePath { get; set; }
        internal string ActualImagePath { get; set; }
        internal int ExpectedProcessId { get; set; }
        internal int ActualProcessId { get; set; }
        internal bool IsImagePathProtected { get; set; }
        internal bool HasExpectedMetadata { get; set; }
        internal bool HasReparseComponent { get; set; }
        internal bool IsHighIntegrity { get; set; }
        internal int MaxServerInstances { get; set; }
        internal bool IsSecondServerAttempt { get; set; }
    }

    internal static class ProvisioningPipePeerAuthenticator
    {
        internal static ValidationResult ValidateServer(ProvisioningPeerEvidence evidence)
        {
            return Validate(evidence, false);
        }

        internal static ValidationResult ValidateClient(ProvisioningPeerEvidence evidence)
        {
            return Validate(evidence, true);
        }

        private static ValidationResult Validate(ProvisioningPeerEvidence evidence, bool requireElevatedClient)
        {
            ValidationResult result = new ValidationResult();
            if (evidence == null)
            {
                result.Add("Нет данных для аутентификации pipe peer.");
                return result;
            }

            if (string.IsNullOrEmpty(evidence.ExpectedSid) ||
                !string.Equals(evidence.ExpectedSid, evidence.ActualSid, StringComparison.Ordinal))
            {
                result.Add("Windows SID pipe peer не совпадает с ожидаемой учетной записью.");
            }
            if (string.IsNullOrEmpty(evidence.ExpectedImagePath) ||
                string.IsNullOrEmpty(evidence.ActualImagePath) ||
                !string.Equals(
                    evidence.ExpectedImagePath,
                    evidence.ActualImagePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add("Образ pipe peer не совпадает с ожидаемым EXE.");
            }
            if (!evidence.IsImagePathProtected || evidence.HasReparseComponent)
            {
                result.Add("Образ pipe peer находится в незащищенном пути или в reparse-цепочке.");
            }
            if (!evidence.HasExpectedMetadata)
            {
                result.Add("Метаданные pipe peer не совпадают с продуктом KRS.");
            }
            if (evidence.MaxServerInstances != 1 || evidence.IsSecondServerAttempt)
            {
                result.Add("Для операции допустим только один одноразовый pipe server.");
            }
            if (evidence.ActualProcessId <= 0)
            {
                result.Add("Не определен PID pipe peer.");
            }

            if (requireElevatedClient)
            {
                if (evidence.ExpectedProcessId <= 0 ||
                    evidence.ActualProcessId != evidence.ExpectedProcessId)
                {
                    result.Add("PID подключившегося helper не совпадает с Process.Start.");
                }
                if (!evidence.IsHighIntegrity)
                {
                    result.Add("Подключившийся helper не имеет high integrity.");
                }
            }

            return result;
        }
    }
}
