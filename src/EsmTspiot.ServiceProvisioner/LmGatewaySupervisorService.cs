using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Threading;
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
            _serviceApi = serviceApi ?? throw new ArgumentNullException("serviceApi");
            _profile = profile ?? throw new ArgumentNullException("profile");
            _supervisorBinary = supervisorBinary ?? throw new ArgumentNullException("supervisorBinary");
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
            if (!Matches(definition, observed))
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

        internal bool IsExactDefinition(
            string kktSerial,
            string operatorSid,
            WindowsServiceRecord observed)
        {
            return Matches(BuildDefinition(kktSerial, operatorSid), observed);
        }

        internal static int RunServiceMode(string serviceName)
        {
            string serial;
            if (!LmServiceIdentity.TryParseName(serviceName, out serial))
            {
                return 2;
            }

            ServiceBase.Run(new SupervisorServiceHost(serviceName));
            return 0;
        }

        private static LmControllerChildProcess CreateVerifiedChild(string serviceName)
        {
            ControllerCapabilityProfile profile = ControllerCapabilityProfile.SupportedVersion1632();
            PathSafety pathSafety = new PathSafety();
            VerifiedProvisionerBinary.ResolveCurrent(pathSafety);
            string serviceSid = RestrictedServiceSid.Resolve(serviceName);
            if (!RestrictedServiceSid.CurrentTokenContains(serviceSid))
            {
                throw new InvalidOperationException("Restricted service SID is absent from the service token.");
            }

            OfficialControllerLocator locator = new OfficialControllerLocator(
                profile,
                new WinTrustVerifier(),
                pathSafety);
            VerifiedControllerBinaryResult controller = locator.ResolveVerifiedBinary();
            if (!controller.IsSuccess)
            {
                throw new InvalidDataException(controller.ErrorMessage);
            }

            string appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "KRS",
                "MultiKKT");
            string profileRoot = Path.Combine(appDataRoot, "Profiles", serviceName);
            ValidationResult profileValidation = pathSafety.ValidateProtected(
                profileRoot,
                appDataRoot,
                serviceSid);
            if (!profileValidation.IsValid)
            {
                throw new InvalidDataException(profileValidation.JoinMessages());
            }

            return new LmControllerChildProcess(
                profile,
                controller.Binary,
                profileRoot,
                new ProcessEnvironmentReader(),
                new NativeControllerChildRuntime());
        }

        private static bool Matches(
            WindowsServiceDefinition expected,
            WindowsServiceRecord actual)
        {
            if (actual == null ||
                !string.Equals(expected.ServiceName, actual.ServiceName, StringComparison.Ordinal) ||
                !string.Equals(expected.DisplayName, actual.DisplayName, StringComparison.Ordinal) ||
                !string.Equals(expected.ImagePath, actual.ImagePath, StringComparison.Ordinal) ||
                !string.Equals(expected.Description, actual.Description, StringComparison.Ordinal) ||
                !string.Equals(expected.AccountName, actual.AccountName, StringComparison.Ordinal) ||
                expected.StartMode != actual.StartMode ||
                expected.ErrorControl != actual.ErrorControl ||
                expected.ServiceSidType != actual.ServiceSidType ||
                actual.SecurityDescriptor == null || !actual.SecurityDescriptor.IsRestrictive ||
                !string.Equals(
                    expected.SecurityDescriptor.OperatorSid,
                    actual.SecurityDescriptor.OperatorSid,
                    StringComparison.Ordinal))
            {
                return false;
            }
            IList<string> dependencies = actual.Dependencies ?? new List<string>();
            if (dependencies.Count != expected.Dependencies.Count)
            {
                return false;
            }
            for (int index = 0; index < dependencies.Count; index++)
            {
                if (!string.Equals(dependencies[index], expected.Dependencies[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            if (actual.RecoveryPolicy == null || expected.RecoveryPolicy == null ||
                actual.RecoveryPolicy.ResetPeriodSeconds != expected.RecoveryPolicy.ResetPeriodSeconds ||
                actual.RecoveryPolicy.RestartDelaysMilliseconds.Count !=
                    expected.RecoveryPolicy.RestartDelaysMilliseconds.Count)
            {
                return false;
            }
            for (int index = 0;
                index < expected.RecoveryPolicy.RestartDelaysMilliseconds.Count;
                index++)
            {
                if (actual.RecoveryPolicy.RestartDelaysMilliseconds[index] !=
                    expected.RecoveryPolicy.RestartDelaysMilliseconds[index])
                {
                    return false;
                }
            }
            return true;
        }

        private sealed class SupervisorServiceHost : ServiceBase
        {
            private LmControllerChildProcess _child;
            private volatile bool _stopping;

            internal SupervisorServiceHost(string serviceName)
            {
                ServiceName = serviceName;
                CanStop = true;
                CanPauseAndContinue = false;
                AutoLog = true;
            }

            protected override void OnStart(string[] args)
            {
                if (args != null && args.Length != 0)
                {
                    throw new InvalidOperationException("Supervisor start arguments are forbidden.");
                }
                _child = CreateVerifiedChild(ServiceName);
                _child.Start();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    bool exited = _child.WaitForExit();
                    if (exited && !_stopping)
                    {
                        Environment.Exit(1);
                    }
                });
            }

            protected override void OnStop()
            {
                _stopping = true;
                if (_child != null && !_child.StopGracefully())
                {
                    throw new InvalidOperationException("Controller did not stop within the graceful timeout.");
                }
            }
        }
    }
}
