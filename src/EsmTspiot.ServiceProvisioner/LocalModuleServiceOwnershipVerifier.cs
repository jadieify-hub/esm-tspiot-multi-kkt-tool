using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleProcessObservation
    {
        private LocalModuleProcessObservation()
        {
        }

        internal WindowsServiceRecord Service { get; private set; }
        internal int ChildProcessId { get; private set; }
        internal int ChildParentProcessId { get; private set; }
        internal string ExecutablePath { get; private set; }
        internal string ExecutableSha256 { get; private set; }
        internal IList<string> ArgumentTokens { get; private set; }
        internal IDictionary<string, string> Environment { get; private set; }
        internal string ManifestOwnershipNonce { get; private set; }
        internal IList<int> ListenerProcessIds { get; private set; }

        internal static LocalModuleProcessObservation CreateForTesting(
            WindowsServiceRecord service,
            int childProcessId,
            int childParentProcessId,
            string executablePath,
            string executableSha256,
            IList<string> argumentTokens,
            IDictionary<string, string> environment,
            string manifestOwnershipNonce,
            IEnumerable<int> listenerProcessIds)
        {
            if (argumentTokens == null) throw new ArgumentNullException("argumentTokens");
            if (environment == null) throw new ArgumentNullException("environment");
            if (listenerProcessIds == null)
            {
                throw new ArgumentNullException("listenerProcessIds");
            }
            return new LocalModuleProcessObservation
            {
                Service = service,
                ChildProcessId = childProcessId,
                ChildParentProcessId = childParentProcessId,
                ExecutablePath = executablePath,
                ExecutableSha256 = executableSha256,
                ArgumentTokens = new List<string>(argumentTokens).AsReadOnly(),
                Environment = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(
                        environment,
                        StringComparer.OrdinalIgnoreCase)),
                ManifestOwnershipNonce = manifestOwnershipNonce,
                ListenerProcessIds =
                    new List<int>(listenerProcessIds).AsReadOnly()
            };
        }
    }

    internal sealed class LocalModuleServiceOwnershipVerifier
    {
        private readonly LocalModuleCapabilityProfile _capability;

        internal LocalModuleServiceOwnershipVerifier(
            LocalModuleCapabilityProfile capability)
        {
            if (capability == null) throw new ArgumentNullException("capability");
            _capability = capability;
        }

        internal ValidationResult Verify(
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role,
            WindowsServiceDefinition expectedService,
            LocalModuleProcessObservation observation)
        {
            ValidationResult result = new ValidationResult();
            if (manifest == null || expectedService == null || observation == null)
            {
                result.Add("Local-module ownership evidence is incomplete.");
                return result;
            }
            ErlangChildStartPlan plan;
            try
            {
                plan = LocalModuleConfigurationWriter.RebuildStartPlan(
                    _capability,
                    manifest,
                    role);
            }
            catch (Exception ex)
            {
                result.Add("Manifest start plan is invalid: " + ex.GetType().Name + ".");
                return result;
            }

            if (!WindowsServiceDefinitionMatcher.Matches(
                    expectedService,
                    observation.Service) ||
                observation.Service.State != WindowsServiceState.Running ||
                observation.Service.ProcessId <= 0)
            {
                result.Add("SCM service identity or state does not match.");
            }
            if (observation.ChildProcessId <= 0 ||
                observation.Service == null ||
                observation.ChildParentProcessId != observation.Service.ProcessId)
            {
                result.Add("Child process does not belong to the exact service supervisor.");
            }
            if (!PathEquals(observation.ExecutablePath, plan.ExecutablePath) ||
                !string.Equals(
                    observation.ExecutableSha256,
                    FindRequiredFileHash(
                        _capability,
                        _capability.ErlExecutableRelativePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add("Child executable path or hash does not match capability.");
            }
            if (!SequenceEquals(
                    plan.ArgumentTokens,
                    observation.ArgumentTokens,
                    StringComparison.Ordinal))
            {
                result.Add("Child command tokens do not select the owned configuration.");
            }
            if (!DictionaryEquals(plan.Environment, observation.Environment))
            {
                result.Add("Child process environment does not select the owned EPMD instance.");
            }
            if (!string.Equals(
                    manifest.OwnershipNonce,
                    observation.ManifestOwnershipNonce,
                    StringComparison.Ordinal))
            {
                result.Add("Manifest ownership nonce does not match process evidence.");
            }
            if (observation.ListenerProcessIds == null ||
                observation.ListenerProcessIds.Count != 1 ||
                observation.ListenerProcessIds[0] != observation.ChildProcessId)
            {
                result.Add("Expected listener is not owned exclusively by the child process.");
            }
            return result;
        }

        internal static void ValidateStartArtifacts(
            LocalModuleCapabilityProfile capability,
            LocalModuleRuntimeManifest runtime,
            LocalModuleInstanceManifest manifest,
            LocalModuleProcessRole role)
        {
            if (capability == null || runtime == null || manifest == null)
            {
                throw new InvalidDataException("Owned start artifacts are incomplete.");
            }
            if (runtime.State != LocalModuleRuntimeLifecycleState.Ready ||
                !string.Equals(
                    runtime.RuntimeId,
                    manifest.RuntimeId,
                    StringComparison.Ordinal) ||
                !PathEquals(runtime.RuntimeRoot, manifest.RuntimeRoot) ||
                !string.Equals(
                    runtime.RequiredFileContractSha256,
                    capability.RuntimeContractSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Owned runtime is not ready for service start.");
            }

            ErlangChildStartPlan plan =
                LocalModuleConfigurationWriter.RebuildStartPlan(
                    capability,
                    manifest,
                    role);
            LocalModuleRuntimeFile executableInventory = null;
            for (int index = 0; index < runtime.Files.Count; index++)
            {
                if (string.Equals(
                        runtime.Files[index].RelativePath,
                        capability.ErlExecutableRelativePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    executableInventory = runtime.Files[index];
                    break;
                }
            }
            string requiredHash = FindRequiredFileHash(
                capability,
                capability.ErlExecutableRelativePath);
            if (executableInventory == null ||
                !string.Equals(
                    executableInventory.Sha256,
                    requiredHash,
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(plan.ExecutablePath) ||
                new FileInfo(plan.ExecutablePath).Length !=
                    executableInventory.ByteLength ||
                !string.Equals(
                    ComputeFileSha256(plan.ExecutablePath),
                    requiredHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Owned Erlang executable does not match runtime inventory.");
            }

            ValidateConfigurationArtifacts(manifest);
        }

        internal static void ValidateConfigurationArtifacts(
            LocalModuleInstanceManifest manifest)
        {
            if (manifest == null)
            {
                throw new InvalidDataException(
                    "Owned local-module manifest is missing.");
            }
            ValidateConfigHash(
                Path.Combine(manifest.ConfigRoot, "regime-local.ini"),
                manifest.RegimeLocalIniSha256);
            ValidateConfigHash(
                Path.Combine(manifest.ConfigRoot, "yenisei-local.ini"),
                manifest.YeniseiLocalIniSha256);
            ValidateConfigHash(
                Path.Combine(manifest.ConfigRoot, "regime-vm.args"),
                manifest.RegimeVmArgsSha256);
            ValidateConfigHash(
                Path.Combine(manifest.ConfigRoot, "yenisei-vm.args"),
                manifest.YeniseiVmArgsSha256);
            ValidateConfigHash(
                Path.Combine(manifest.ConfigRoot, "regime-sys.config"),
                manifest.RegimeSysConfigSha256);
            ValidateConfigHash(
                Path.Combine(manifest.ConfigRoot, "yenisei-sys.config"),
                manifest.YeniseiSysConfigSha256);
        }

        private static void ValidateConfigHash(
            string path,
            string expectedSha256)
        {
            if (!File.Exists(path) ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(
                    ComputeFileSha256(path),
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Owned local-module configuration hash does not match.");
            }
        }

        private static string FindRequiredFileHash(
            LocalModuleCapabilityProfile capability,
            string relativePath)
        {
            for (int index = 0; index < capability.RequiredFiles.Count; index++)
            {
                LocalModuleRequiredFile file = capability.RequiredFiles[index];
                if (string.Equals(
                        file.RelativePath,
                        relativePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return file.Sha256;
                }
            }
            throw new InvalidDataException(
                "Capability does not contain the required executable hash.");
        }

        private static bool SequenceEquals(
            IList<string> expected,
            IList<string> actual,
            StringComparison comparison)
        {
            if (expected == null || actual == null ||
                expected.Count != actual.Count)
            {
                return false;
            }
            for (int index = 0; index < expected.Count; index++)
            {
                if (!string.Equals(expected[index], actual[index], comparison))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool DictionaryEquals(
            IDictionary<string, string> expected,
            IDictionary<string, string> actual)
        {
            if (expected == null || actual == null ||
                expected.Count != actual.Count)
            {
                return false;
            }
            foreach (KeyValuePair<string, string> item in expected)
            {
                string value;
                if (!actual.TryGetValue(item.Key, out value) ||
                    !string.Equals(item.Value, value, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool PathEquals(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) ||
                string.IsNullOrWhiteSpace(right))
            {
                return false;
            }
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeFileSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(stream);
                StringBuilder result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    result.Append(digest[index].ToString(
                        "x2",
                        CultureInfo.InvariantCulture));
                }
                return result.ToString();
            }
        }
    }
}
