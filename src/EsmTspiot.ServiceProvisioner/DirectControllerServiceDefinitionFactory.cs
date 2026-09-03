using System;
using System.Collections.Generic;
using System.IO;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class DirectControllerServiceDefinitionFactory
    {
        private readonly ControllerCapabilityProfile _profile;
        private readonly VerifiedControllerBinary _binary;

        internal DirectControllerServiceDefinitionFactory(
            ControllerCapabilityProfile profile,
            VerifiedControllerBinary binary)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (binary == null) throw new ArgumentNullException("binary");
            ValidateVerifiedBinary(profile, binary);
            _profile = profile;
            _binary = binary;
        }

        internal WindowsServiceDefinition Create(
            int ordinal,
            string profileEnvironmentRoot,
            string operatorSid)
        {
            string serviceName = DirectControllerIdentity.ServiceNameForOrdinal(ordinal);
            string environmentRoot = Path.GetFullPath(profileEnvironmentRoot);
            WindowsServiceDefinition definition = new WindowsServiceDefinition
            {
                Kind = WindowsServiceDefinitionKind.DirectController,
                VerifiedDirectImagePath = _binary.FullPath,
                ServiceName = serviceName,
                DisplayName = ordinal == 1
                    ? "ESM: Local Module Controller"
                    : "ESM: Local Module Controller " + ordinal.ToString(),
                ImagePath = WindowsCommandLine.QuoteArgument(_binary.FullPath),
                Description = "ESM Local Module Controller for MultiKKT slot " +
                    ordinal.ToString() + ".",
                AccountName = _profile.ServiceAccountName,
                Dependencies = new List<string>(_profile.ServiceDependencies),
                EnvironmentVariables = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { _profile.ProfileEnvironmentKey, environmentRoot }
                },
                StartMode = _profile.ServiceStartMode,
                ErrorControl = _profile.ServiceErrorControl,
                ServiceSidType = _profile.ServiceSidType,
                RecoveryPolicy = _profile.ServiceRecoveryPolicy,
                SecurityDescriptor = ServiceSecurityDescriptor.CreateRestrictive(operatorSid)
            };
            definition.Validate();
            return definition;
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool digit = character >= '0' && character <= '9';
                bool lower = character >= 'a' && character <= 'f';
                bool upper = character >= 'A' && character <= 'F';
                if (!digit && !lower && !upper) return false;
            }
            return true;
        }

        private static void ValidateVerifiedBinary(
            ControllerCapabilityProfile profile,
            VerifiedControllerBinary binary)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(binary.FullPath);
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    "Verified controller path is invalid: " + ex.GetType().Name + ".",
                    "binary");
            }
            // Версию и хеш задаёт конкретная сборка вендора, поэтому здесь
            // проверяется не их совпадение с профилем, а то, что бинарник
            // действительно опознан: имя файла, непустая версия установленного
            // продукта и вычисленный отпечаток содержимого.
            if (!string.Equals(Path.GetFileName(fullPath),
                    profile.ControllerBinary.FileName,
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(binary.Version) ||
                !IsHex(binary.Sha256, 64) ||
                string.IsNullOrWhiteSpace(binary.SignerThumbprint))
            {
                throw new InvalidOperationException(
                    "Проверенный бинарник контроллера не опознан.");
            }
            binary.FullPath = fullPath;
        }
    }
}
