using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class ManagedLocalModuleProfileStore
    {
        private static readonly string[] ConfigurationFileNames =
        {
            "regime-local.ini",
            "yenisei-local.ini",
            "regime-vm.args",
            "yenisei-vm.args",
            "regime-sys.config",
            "yenisei-sys.config"
        };

        private readonly LocalModuleManifestStore _manifests;
        private readonly IPathSafety _pathSafety;
        private readonly AtomicFileWriter _writer;

        internal ManagedLocalModuleProfileStore(
            LocalModuleManifestStore manifests,
            IPathSafety pathSafety,
            AtomicFileWriter writer)
        {
            if (manifests == null) throw new ArgumentNullException("manifests");
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            if (writer == null) throw new ArgumentNullException("writer");
            _manifests = manifests;
            _pathSafety = pathSafety;
            _writer = writer;
        }

        internal LocalModuleConfiguration WriteConfiguration(
            LocalModuleRuntimeManifest runtime,
            LocalModuleCapabilityProfile capability,
            ManagedLocalModuleProvisioningItemRequest item,
            string instanceId,
            string ownershipNonce)
        {
            ValidateRuntime(runtime, capability);
            if (item == null ||
                !string.Equals(
                    instanceId,
                    LocalModuleManagedIdentity.CreateInstanceId(
                        item.Inn,
                        ownershipNonce),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Profile identity is not derived from the owned INN and nonce.");
            }

            LocalModuleTemplateObservation templates = ReadTemplates(
                runtime,
                capability);
            LocalModuleConfiguration configuration =
                LocalModuleConfigurationWriter.Build(
                    capability,
                    runtime.RuntimeRoot,
                    _manifests.GetInstanceRoot(instanceId),
                    item,
                    ownershipNonce,
                    templates);
            IList<string> serviceSids = DeriveServiceSids(instanceId);
            EnsureProfileDirectories(configuration, serviceSids);
            WriteConfigurationFiles(configuration, serviceSids);
            ValidationResult validation = ValidateGeneratedConfiguration(
                configuration,
                serviceSids);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
            return configuration;
        }

        internal void ProtectOwnedManifests(
            LocalModuleRuntimeManifest runtime,
            LocalModuleInstanceManifest instance)
        {
            if (runtime == null || instance == null ||
                !string.Equals(
                    runtime.RuntimeId,
                    instance.RuntimeId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Runtime and instance manifest ownership do not match.");
            }
            IList<string> serviceSids = DeriveServiceSids(instance.InstanceId);
            _pathSafety.EnsureProtectedDirectoryForServices(
                _manifests.MachineRoot,
                ProtectedDirectoryKind.ProfileContainer,
                null,
                serviceSids);
            _pathSafety.EnsureProtectedDirectoryForServices(
                _manifests.RuntimeInventoryRoot,
                ProtectedDirectoryKind.ProfileConfiguration,
                null,
                serviceSids);
            string runtimeManifest = _manifests.GetRuntimeManifestPath(
                runtime.RuntimeId);
            _pathSafety.EnsureProtectedDirectoryForServices(
                Path.GetDirectoryName(runtimeManifest),
                ProtectedDirectoryKind.ProfileConfiguration,
                null,
                serviceSids);
            _pathSafety.EnsureProtectedRuntimeFile(runtimeManifest);
            _pathSafety.EnsureProtectedReadOnlyFile(
                _manifests.GetInstanceManifestPath(instance.InstanceId),
                serviceSids);
        }

        internal ValidationResult ValidateConfiguration(
            LocalModuleRuntimeManifest runtime,
            LocalModuleCapabilityProfile capability,
            LocalModuleInstanceManifest instance)
        {
            ValidationResult result = new ValidationResult();
            try
            {
                ValidateRuntime(runtime, capability);
                if (instance == null ||
                    !string.Equals(
                        instance.RuntimeId,
                        runtime.RuntimeId,
                        StringComparison.Ordinal))
                {
                    result.Add("Манифест профиля ссылается на другой runtime.");
                    return result;
                }
                IList<string> serviceSids = DeriveServiceSids(instance.InstanceId);
                ValidationResult paths = ValidateProfilePaths(instance, serviceSids);
                if (!paths.IsValid)
                {
                    result.Add(paths.JoinMessages());
                    return result;
                }
                LocalModuleServiceOwnershipVerifier.ValidateConfigurationArtifacts(
                    instance);
                string[] files = Directory.GetFiles(instance.ConfigRoot);
                if (files.Length != ConfigurationFileNames.Length)
                {
                    result.Add("Каталог конфигурации ЛМ содержит лишние или отсутствующие файлы.");
                    return result;
                }
                HashSet<string> expected = new HashSet<string>(
                    ConfigurationFileNames,
                    StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < files.Length; index++)
                {
                    if (!expected.Remove(Path.GetFileName(files[index])))
                    {
                        result.Add("Каталог конфигурации ЛМ содержит неизвестный файл.");
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) &&
                    !(ex is UnauthorizedAccessException) &&
                    !(ex is InvalidDataException) &&
                    !(ex is SystemException))
                {
                    throw;
                }
                result.Add("Профиль ЛМ не прошёл проверку: " +
                    ex.GetType().Name + ".");
            }
            return result;
        }

        internal void DeleteProfileContent(LocalModuleInstanceManifest instance)
        {
            if (instance == null ||
                !string.Equals(
                    instance.ProfileRoot,
                    _manifests.GetInstanceRoot(instance.InstanceId),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Only the manifest-owned profile may be removed.");
            }
            IList<string> serviceSids = DeriveServiceSids(instance.InstanceId);
            ValidationResult paths = ValidateProfilePaths(instance, serviceSids);
            if (!paths.IsValid)
            {
                throw new InvalidDataException(paths.JoinMessages());
            }
            DeleteConfiguration(instance.ConfigRoot);
            DeleteOwnedMutableTree(instance.LogsRoot, serviceSids);
            DeleteOwnedMutableTree(instance.DataRoot, serviceSids);
        }

        private void EnsureProfileDirectories(
            LocalModuleConfiguration configuration,
            IList<string> serviceSids)
        {
            _pathSafety.EnsureProtectedDirectoryForServices(
                _manifests.MachineRoot,
                ProtectedDirectoryKind.ProfileContainer,
                null,
                serviceSids);
            _pathSafety.EnsureProtectedDirectoryForServices(
                _manifests.InstancesRoot,
                ProtectedDirectoryKind.ProfileContainer,
                null,
                serviceSids);
            _pathSafety.EnsureProtectedDirectoryForServices(
                configuration.ProfileRoot,
                ProtectedDirectoryKind.ProfileContainer,
                null,
                serviceSids);
            _pathSafety.EnsureProtectedDirectoryForServices(
                configuration.ConfigRoot,
                ProtectedDirectoryKind.ProfileConfiguration,
                null,
                serviceSids);
            _pathSafety.EnsureProtectedDirectoryForServices(
                Path.Combine(configuration.ConfigRoot, "yenisei-local.d"),
                ProtectedDirectoryKind.ProfileConfiguration,
                null,
                serviceSids);
            EnsureMutableDirectory(configuration.DataRoot, serviceSids);
            EnsureMutableDirectory(
                Path.Combine(configuration.DataRoot, "database"),
                serviceSids);
            EnsureMutableDirectory(
                Path.Combine(configuration.DataRoot, "index"),
                serviceSids);
            EnsureMutableDirectory(
                Path.Combine(configuration.DataRoot, "key-store"),
                serviceSids);
            EnsureMutableDirectory(configuration.LogsRoot, serviceSids);
        }

        private void EnsureMutableDirectory(
            string path,
            IList<string> serviceSids)
        {
            _pathSafety.EnsureProtectedDirectoryForServices(
                path,
                ProtectedDirectoryKind.Profile,
                null,
                serviceSids);
        }

        private void WriteConfigurationFiles(
            LocalModuleConfiguration configuration,
            IList<string> serviceSids)
        {
            WriteOne(configuration.RegimeLocalIniPath, configuration.RegimeLocalIni, serviceSids);
            WriteOne(configuration.YeniseiLocalIniPath, configuration.YeniseiLocalIni, serviceSids);
            WriteOne(configuration.RegimeVmArgsPath, configuration.RegimeVmArgs, serviceSids);
            WriteOne(configuration.YeniseiVmArgsPath, configuration.YeniseiVmArgs, serviceSids);
            WriteOne(configuration.RegimeSysConfigPath, configuration.RegimeSysConfig, serviceSids);
            WriteOne(configuration.YeniseiSysConfigPath, configuration.YeniseiSysConfig, serviceSids);
        }

        private void WriteOne(
            string path,
            string content,
            IList<string> serviceSids)
        {
            _writer.WriteUtf8(path, content);
            _pathSafety.EnsureProtectedReadOnlyFile(path, serviceSids);
        }

        private ValidationResult ValidateGeneratedConfiguration(
            LocalModuleConfiguration configuration,
            IList<string> serviceSids)
        {
            LocalModuleInstanceManifest synthetic = new LocalModuleInstanceManifest
            {
                InstanceId = Path.GetFileName(configuration.ProfileRoot),
                ProfileRoot = configuration.ProfileRoot,
                ConfigRoot = configuration.ConfigRoot,
                DataRoot = configuration.DataRoot,
                LogsRoot = configuration.LogsRoot
            };
            return ValidateProfilePaths(synthetic, serviceSids);
        }

        private ValidationResult ValidateProfilePaths(
            LocalModuleInstanceManifest instance,
            IList<string> serviceSids)
        {
            ValidationResult result = new ValidationResult();
            AddValidation(result, _pathSafety.ValidateProtectedForServices(
                instance.ProfileRoot,
                _manifests.MachineRoot,
                serviceSids));
            AddValidation(result, _pathSafety.ValidateProtectedForServices(
                instance.DataRoot,
                instance.ProfileRoot,
                serviceSids));
            AddValidation(result, _pathSafety.ValidateProtectedForServices(
                instance.LogsRoot,
                instance.ProfileRoot,
                serviceSids));
            AddValidation(result, _pathSafety.ValidateProtectedForServices(
                instance.ConfigRoot,
                instance.ProfileRoot,
                null));
            return result;
        }

        private void DeleteConfiguration(string configRoot)
        {
            if (!Directory.Exists(configRoot))
            {
                return;
            }
            ValidationResult validation = _pathSafety.ValidateProtected(
                configRoot,
                _manifests.MachineRoot,
                null);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
            HashSet<string> expected = new HashSet<string>(
                ConfigurationFileNames,
                StringComparer.OrdinalIgnoreCase);
            string[] files = Directory.GetFiles(configRoot);
            for (int index = 0; index < files.Length; index++)
            {
                if (!expected.Remove(Path.GetFileName(files[index])))
                {
                    throw new InvalidDataException(
                        "Configuration cleanup found an unknown file.");
                }
                File.SetAttributes(files[index], FileAttributes.Normal);
                File.Delete(files[index]);
            }
            string[] directories = Directory.GetDirectories(configRoot);
            for (int index = 0; index < directories.Length; index++)
            {
                if (!string.Equals(
                        Path.GetFileName(directories[index]),
                        "yenisei-local.d",
                        StringComparison.OrdinalIgnoreCase) ||
                    Directory.GetFileSystemEntries(directories[index]).Length != 0)
                {
                    throw new InvalidDataException(
                        "Configuration cleanup found an unknown entry.");
                }
                Directory.Delete(directories[index], false);
            }
            Directory.Delete(configRoot, false);
        }

        private void DeleteOwnedMutableTree(
            string root,
            IList<string> serviceSids)
        {
            if (!Directory.Exists(root))
            {
                return;
            }
            ValidationResult validation = _pathSafety.ValidateProtectedForServices(
                root,
                _manifests.MachineRoot,
                serviceSids);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.JoinMessages());
            }
            List<string> directories = new List<string>();
            Queue<string> pending = new Queue<string>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                string[] children = Directory.GetDirectories(current);
                for (int index = 0; index < children.Length; index++)
                {
                    ValidationResult childValidation = _pathSafety.Validate(
                        children[index],
                        root);
                    if (!childValidation.IsValid)
                    {
                        throw new InvalidDataException(
                            childValidation.JoinMessages());
                    }
                    directories.Add(children[index]);
                    pending.Enqueue(children[index]);
                }
                string[] files = Directory.GetFiles(current);
                for (int index = 0; index < files.Length; index++)
                {
                    ValidationResult fileValidation = _pathSafety.Validate(
                        files[index],
                        root);
                    if (!fileValidation.IsValid)
                    {
                        throw new InvalidDataException(fileValidation.JoinMessages());
                    }
                    File.SetAttributes(files[index], FileAttributes.Normal);
                    File.Delete(files[index]);
                }
            }
            directories.Sort(delegate(string left, string right)
            {
                return right.Length.CompareTo(left.Length);
            });
            for (int index = 0; index < directories.Count; index++)
            {
                Directory.Delete(directories[index], false);
            }
            Directory.Delete(root, false);
        }

        private static LocalModuleTemplateObservation ReadTemplates(
            LocalModuleRuntimeManifest runtime,
            LocalModuleCapabilityProfile capability)
        {
            string regime = Path.Combine(
                runtime.RuntimeRoot,
                @"regime\etc\local.ini.dist");
            string database = Path.Combine(
                runtime.RuntimeRoot,
                @"yenisei\etc\local.ini.dist");
            EnsureRequiredFile(capability, @"regime\etc\local.ini.dist", regime);
            EnsureRequiredFile(capability, @"yenisei\etc\local.ini.dist", database);
            return new LocalModuleTemplateObservation
            {
                RegimeLocalIni = File.ReadAllText(regime, Encoding.UTF8),
                YeniseiLocalIni = File.ReadAllText(database, Encoding.UTF8)
            };
        }

        private static void EnsureRequiredFile(
            LocalModuleCapabilityProfile capability,
            string relativePath,
            string fullPath)
        {
            LocalModuleRequiredFile expected = null;
            for (int index = 0; index < capability.RequiredFiles.Count; index++)
            {
                if (string.Equals(
                        capability.RequiredFiles[index].RelativePath,
                        relativePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    expected = capability.RequiredFiles[index];
                    break;
                }
            }
            if (expected == null || !File.Exists(fullPath))
            {
                throw new InvalidDataException(
                    "Required local-module template is absent.");
            }
        }

        private static void ValidateRuntime(
            LocalModuleRuntimeManifest runtime,
            LocalModuleCapabilityProfile capability)
        {
            if (runtime == null || capability == null ||
                runtime.State != LocalModuleRuntimeLifecycleState.Ready ||
                !string.Equals(
                    runtime.CapabilityId,
                    capability.CapabilityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    runtime.ProductVersion,
                    capability.ProductVersion,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    runtime.RequiredFileContractSha256,
                    capability.RuntimeContractSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Runtime does not match the exact local-module capability.");
            }
        }

        private static IList<string> DeriveServiceSids(string instanceId)
        {
            return new[]
            {
                RestrictedServiceSid.Derive(
                    LocalModuleManagedIdentity.CreateDatabaseServiceName(instanceId)),
                RestrictedServiceSid.Derive(
                    LocalModuleManagedIdentity.CreateApiServiceName(instanceId))
            };
        }

        private static void AddValidation(
            ValidationResult target,
            ValidationResult source)
        {
            if (source != null && !source.IsValid)
            {
                target.Add(source.JoinMessages());
            }
        }
    }
}
