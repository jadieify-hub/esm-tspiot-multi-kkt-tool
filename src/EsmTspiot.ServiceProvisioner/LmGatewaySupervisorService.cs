using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class VerifiedProvisionerBinary
    {
        private VerifiedProvisionerBinary(string fullPath, string sha256)
        {
            FullPath = fullPath;
            Sha256 = sha256;
        }

        internal string FullPath { get; private set; }
        internal string Sha256 { get; private set; }

        internal static VerifiedProvisionerBinary ResolveCurrent(IPathSafety pathSafety)
        {
            if (pathSafety == null)
            {
                throw new ArgumentNullException("pathSafety");
            }
            string fullPath = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            if (!string.Equals(
                    Path.GetFileName(fullPath),
                    "EsmTspiot.ServiceProvisioner.exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Supervisor executable name is not canonical.");
            }
            string provisionerRoot = Path.GetDirectoryName(fullPath);
            string productRoot = Directory.GetParent(provisionerRoot).FullName;
            ValidationResult pathValidation = pathSafety.ValidateProtected(fullPath, productRoot, null);
            if (!pathValidation.IsValid)
            {
                throw new InvalidDataException(pathValidation.JoinMessages());
            }

            FileVersionInfo version = FileVersionInfo.GetVersionInfo(fullPath);
            if (!string.Equals(version.CompanyName, "KRS", StringComparison.Ordinal) ||
                !string.Equals(version.ProductName, "Управление ККТ в ЕСМ/ТС ПИоТ", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Supervisor product metadata is not trusted.");
            }
            return new VerifiedProvisionerBinary(fullPath, ComputeSha256(fullPath));
        }

        internal static VerifiedProvisionerBinary CreateForTesting(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathRooted(fullPath) ||
                !string.Equals(
                    Path.GetFileName(fullPath),
                    "EsmTspiot.ServiceProvisioner.exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A canonical provisioner path is required.", "fullPath");
            }
            return new VerifiedProvisionerBinary(Path.GetFullPath(fullPath), new string('0', 64));
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                StringBuilder result = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    result.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                return result.ToString();
            }
        }
    }

    internal static class WindowsCommandLine
    {
        internal static string QuoteArgument(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            StringBuilder result = new StringBuilder(value.Length + 2);
            result.Append('"');
            int backslashes = 0;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('"');
                    backslashes = 0;
                    continue;
                }
                result.Append('\\', backslashes);
                backslashes = 0;
                result.Append(character);
            }
            result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }
    }

    internal sealed class LmGatewaySupervisorService
    {
        internal const string DescriptionPrefix = "KRS.MultiKKT.LmGateway.Managed.v1:";

        private readonly IWindowsServiceApi _serviceApi;
        private readonly ControllerCapabilityProfile _profile;
        private readonly VerifiedProvisionerBinary _supervisorBinary;

        internal LmGatewaySupervisorService(
            IWindowsServiceApi serviceApi,
            ControllerCapabilityProfile profile,
            VerifiedProvisionerBinary supervisorBinary)
        {
            if (serviceApi == null) throw new ArgumentNullException("serviceApi");
            if (profile == null) throw new ArgumentNullException("profile");
            if (supervisorBinary == null) throw new ArgumentNullException("supervisorBinary");
            _serviceApi = serviceApi;
            _profile = profile;
            _supervisorBinary = supervisorBinary;
        }

        internal WindowsServiceRecord EnsureConfigured(string kktSerial)
        {
            return EnsureConfigured(kktSerial, null);
        }

        internal WindowsServiceRecord EnsureConfigured(string kktSerial, string operatorSid)
        {
            WindowsServiceDefinition definition = BuildDefinition(kktSerial, operatorSid);
            WindowsServiceRecord existing = _serviceApi.Query(definition.ServiceName);
            if (existing == null)
            {
                _serviceApi.Create(definition);
            }
            else
            {
                _serviceApi.Update(definition);
            }

            WindowsServiceRecord observed = _serviceApi.Query(definition.ServiceName);
            if (!WindowsServiceDefinitionMatcher.Matches(definition, observed))
            {
                throw new InvalidDataException("SCM did not retain the exact managed service definition.");
            }
            return observed;
        }

        internal WindowsServiceDefinition BuildDefinition(string kktSerial)
        {
            return BuildDefinition(kktSerial, null);
        }

        internal WindowsServiceDefinition BuildDefinition(string kktSerial, string operatorSid)
        {
            string serviceName = LmServiceIdentity.CreateName(kktSerial);
            WindowsServiceDefinition definition = new WindowsServiceDefinition
            {
                ServiceName = serviceName,
                DisplayName = "KRS: Контроллер ЛМ ЧЗ (" +
                    kktSerial.Substring(kktSerial.Length - 4) + ")",
                ImagePath = WindowsCommandLine.QuoteArgument(_supervisorBinary.FullPath) +
                    " --supervise " + serviceName,
                Description = DescriptionPrefix + kktSerial,
                AccountName = _profile.ServiceAccountName,
                Dependencies = new List<string>(_profile.ServiceDependencies),
                StartMode = _profile.ServiceStartMode,
                ErrorControl = _profile.ServiceErrorControl,
                ServiceSidType = WindowsServiceSidType.Restricted,
                RecoveryPolicy = _profile.ServiceRecoveryPolicy,
                SecurityDescriptor = ServiceSecurityDescriptor.CreateRestrictive(operatorSid)
            };
            definition.Validate();
            return definition;
        }

        internal bool IsManagedDefinition(
            string kktSerial,
            WindowsServiceRecord observed)
        {
            if (observed == null) return false;
            WindowsServiceDefinition expected = BuildDefinition(kktSerial, null);
            return string.Equals(
                    observed.ServiceName,
                    expected.ServiceName,
                    StringComparison.Ordinal) &&
                string.Equals(
                    observed.ImagePath,
                    expected.ImagePath,
                    StringComparison.Ordinal);
        }

    }
}
