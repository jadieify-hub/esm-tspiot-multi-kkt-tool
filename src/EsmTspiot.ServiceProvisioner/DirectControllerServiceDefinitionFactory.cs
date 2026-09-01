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
            if (!string.Equals(binary.Version, profile.Version, StringComparison.Ordinal) ||
                !string.Equals(binary.Sha256, profile.ControllerBinary.Sha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(binary.SignerThumbprint,
                    profile.ControllerBinary.SignerThumbprint,
                    StringComparison.OrdinalIgnoreCase) ||
                binary.Machine != profile.ControllerBinary.Machine ||
                !string.Equals(Path.GetFileName(fullPath),
                    profile.ControllerBinary.FileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Verified controller binary does not match capability profile 1.6.4.0.");
            }
            binary.FullPath = fullPath;
        }
    }
}
