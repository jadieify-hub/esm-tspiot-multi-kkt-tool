using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Win32;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class WindowsLocalModuleMsiProvisioningPlatform :
        ILocalModuleMsiProvisioningPlatform
    {
        private readonly VerifiedLocalModulePackage _source;
        private readonly WindowsInstallerPackageMetadata _metadata;
        private readonly string _machineRoot;
        private readonly IPathSafety _pathSafety;
        private readonly ILocalModuleMsiTransformer _transformer;
        private readonly IWindowsInstallerApi _installer;
        private readonly InstalledLocalModuleProductReader _products;
        private readonly IWindowsServiceApi _services;
        private readonly LocalModuleServicePairController _servicePairs;
        private readonly LocalModuleServiceStartModeController _startModes;
        private readonly LocalModuleMsiReadinessProbe _readiness;
        private readonly LocalModuleConfigurationInspector _configurations;
        private readonly IWindowsFirewallApi _firewallApi;
        private readonly LocalModuleFirewallManager _firewall;
        private readonly ITcpListenerOwnerReader _listeners;
        private readonly Func<Guid> _newGuid;
        private readonly Action _delay;

        internal static WindowsLocalModuleMsiProvisioningPlatform Create(
            VerifiedLocalModulePackage source,
            string machineRoot)
        {
            PathSafety pathSafety = new PathSafety();
            WindowsServiceApi services = new WindowsServiceApi();
            TcpListenerOwnerReader listeners = new TcpListenerOwnerReader();
            LocalModuleMsiReadinessProbe readiness =
                new LocalModuleMsiReadinessProbe(
                    services,
                    listeners,
                    new NativeProcessParentReader());
            LocalModuleServicePairController servicePairs =
                new LocalModuleServicePairController(services, readiness);
            WindowsFirewallApi firewallApi = new WindowsFirewallApi();
            return new WindowsLocalModuleMsiProvisioningPlatform(
                source,
                source.Metadata,
                machineRoot,
                pathSafety,
                new LocalModuleMsiTransformer(),
                new WindowsInstallerApi(),
                new InstalledLocalModuleProductReader(),
                services,
                servicePairs,
                readiness,
                new LocalModuleConfigurationInspector(),
                firewallApi,
                new LocalModuleFirewallManager(firewallApi),
                listeners,
                delegate { return Guid.NewGuid(); },
                delegate { Thread.Sleep(250); });
        }

        internal static WindowsLocalModuleMsiProvisioningPlatform
            CreateForInstalledProducts(string machineRoot)
        {
            LocalModuleInstallerSelection identity =
                LocalModulePackageVerifier.CreateSupportedIdentity();
            WindowsInstallerPackageMetadata metadata =
                new WindowsInstallerPackageMetadata
                {
                    ProductName = identity.ProductName,
                    ProductVersion = identity.ProductVersion,
                    ProductCode = identity.ProductCode,
                    UpgradeCode = identity.UpgradeCode,
                    PackageCode = "{00000000-0000-0000-0000-000000000000}"
                };
            PathSafety pathSafety = new PathSafety();
            WindowsServiceApi services = new WindowsServiceApi();
            TcpListenerOwnerReader listeners = new TcpListenerOwnerReader();
            LocalModuleMsiReadinessProbe readiness =
                new LocalModuleMsiReadinessProbe(
                    services, listeners, new NativeProcessParentReader());
            LocalModuleServicePairController servicePairs =
                new LocalModuleServicePairController(services, readiness);
            WindowsFirewallApi firewallApi = new WindowsFirewallApi();
            return new WindowsLocalModuleMsiProvisioningPlatform(
                null,
                metadata,
                machineRoot,
                pathSafety,
                new LocalModuleMsiTransformer(),
                new WindowsInstallerApi(),
                new InstalledLocalModuleProductReader(),
                services,
                servicePairs,
                readiness,
                new LocalModuleConfigurationInspector(),
                firewallApi,
                new LocalModuleFirewallManager(firewallApi),
                listeners,
                delegate { return Guid.NewGuid(); },
                delegate { Thread.Sleep(250); });
        }

        internal WindowsLocalModuleMsiProvisioningPlatform(
            VerifiedLocalModulePackage source,
            WindowsInstallerPackageMetadata metadata,
            string machineRoot,
            IPathSafety pathSafety,
            ILocalModuleMsiTransformer transformer,
            IWindowsInstallerApi installer,
            InstalledLocalModuleProductReader products,
            IWindowsServiceApi services,
            LocalModuleServicePairController servicePairs,
            LocalModuleMsiReadinessProbe readiness,
            LocalModuleConfigurationInspector configurations,
            IWindowsFirewallApi firewallApi,
            LocalModuleFirewallManager firewall,
            ITcpListenerOwnerReader listeners,
            Func<Guid> newGuid,
            Action delay)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (string.IsNullOrWhiteSpace(machineRoot))
                throw new ArgumentException("Machine root is required.",
                    "machineRoot");
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            if (transformer == null) throw new ArgumentNullException("transformer");
            if (installer == null) throw new ArgumentNullException("installer");
            if (products == null) throw new ArgumentNullException("products");
            if (services == null) throw new ArgumentNullException("services");
            if (servicePairs == null) throw new ArgumentNullException("servicePairs");
            if (readiness == null) throw new ArgumentNullException("readiness");
            if (configurations == null)
                throw new ArgumentNullException("configurations");
            if (firewallApi == null) throw new ArgumentNullException("firewallApi");
            if (firewall == null) throw new ArgumentNullException("firewall");
            if (listeners == null) throw new ArgumentNullException("listeners");
            if (newGuid == null) throw new ArgumentNullException("newGuid");
            if (delay == null) throw new ArgumentNullException("delay");
            _source = source;
            _metadata = metadata.Clone();
            _machineRoot = Path.GetFullPath(machineRoot);
            _pathSafety = pathSafety;
            _transformer = transformer;
            _installer = installer;
            _products = products;
            _services = services;
            _servicePairs = servicePairs;
            _startModes = new LocalModuleServiceStartModeController(services);
            _readiness = readiness;
            _configurations = configurations;
            _firewallApi = firewallApi;
            _firewall = firewall;
            _listeners = listeners;
            _newGuid = newGuid;
            _delay = delay;
        }

        public LocalModuleMsiObservedState Observe(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest)
        {
            LocalModuleMsiProvisioningContext.ValidateRequest(request);
            string installRoot = InstallRoot(request);
            ProductExpectation expected = ExpectedProduct(request, manifest);
            IList<InstalledLocalModuleProduct> products = _products.ReadAll();
            bool anyProduct = false;
            bool exactProduct = false;
            for (int index = 0; index < products.Count; index++)
            {
                InstalledLocalModuleProduct product = products[index];
                bool sameCode = string.Equals(product.ProductCode,
                    expected.ProductCode, StringComparison.OrdinalIgnoreCase);
                bool sameRoot = PathsEqual(product.InstallLocation, installRoot);
                if (!sameCode && !sameRoot) continue;
                anyProduct = true;
                exactProduct |= sameCode && sameRoot &&
                    string.Equals(product.DisplayName, expected.ProductName,
                        StringComparison.Ordinal) &&
                    string.Equals(product.DisplayVersion,
                        _metadata.ProductVersion,
                        StringComparison.Ordinal);
            }

            LocalModuleInstalledLayout layout = LocalModuleInstalledLayout.Create(
                installRoot,
                request.CloneOrdinal,
                request.ApiPort,
                request.DatabasePort);
            WindowsServiceRecord api = _services.Query(layout.ApiServiceName);
            WindowsServiceRecord database =
                _services.Query(layout.DatabaseServiceName);
            bool servicesPresent = api != null || database != null;
            bool servicesMatch = api != null && database != null &&
                layout.MatchesService(LocalModuleProcessRole.Api, api) &&
                layout.MatchesService(LocalModuleProcessRole.Database, database);
            bool configMatches = false;
            if (exactProduct && servicesMatch)
            {
                try
                {
                    _configurations.Inspect(layout);
                    configMatches = true;
                }
                catch (InvalidDataException)
                {
                    configMatches = false;
                }
            }

            bool firewallPresent = false;
            bool firewallMatches = false;
            if (manifest != null &&
                !string.IsNullOrEmpty(manifest.FirewallRuleName))
            {
                WindowsFirewallRuleRecord record =
                    _firewallApi.FindByName(manifest.FirewallRuleName);
                firewallPresent = record != null;
                firewallMatches = record != null && string.Equals(
                    record.ComputeFieldHash(),
                    manifest.FirewallRuleHash,
                    StringComparison.Ordinal);
            }

            bool automaticStart =
                LocalModuleServiceStartModeController.IsAutomatic(api, database);
            bool running = (api != null &&
                api.State == WindowsServiceState.Running) ||
                (database != null &&
                 database.State == WindowsServiceState.Running);
            bool ready = false;
            if (servicesMatch && running)
            {
                ready = _readiness.ProbeOwnedListener(
                    layout, LocalModuleProcessRole.Api).IsValid &&
                    _readiness.ProbeOwnedListener(
                        layout, LocalModuleProcessRole.Database).IsValid;
            }
            string conflict = string.Empty;
            if (anyProduct && !exactProduct)
                conflict = "MSI product or installation root is foreign.";
            else if (servicesPresent && !servicesMatch)
                conflict = "Local-module service pair is foreign.";
            else if (exactProduct && servicesMatch && !configMatches)
                conflict = "Local-module configuration or ports are foreign.";
            else if (firewallPresent && !firewallMatches)
                conflict = "Local-module firewall rule is foreign.";

            return new LocalModuleMsiObservedState
            {
                ProductPresent = anyProduct,
                ProductMatches = exactProduct,
                ConfigurationMatches = configMatches,
                ServicesPresent = servicesPresent,
                ServicesMatch = servicesMatch,
                FirewallPresent = firewallPresent,
                FirewallMatches = firewallMatches,
                Running = running,
                Ready = ready,
                AutomaticStart = automaticStart,
                ConflictMessage = conflict
            };
        }

        public LocalModuleMsiManifest PrepareInstall(
            LocalModuleMsiProvisioningItemRequest request,
            string ownershipNonce)
        {
            RequireSource();
            string installRoot = InstallRoot(request);
            if (request.CloneOrdinal == 0)
                return LocalModuleMsiManifest.Create(
                    request.Inn,
                    request.CloneOrdinal,
                    request.ApiPort,
                    request.DatabasePort,
                    _metadata.ProductCode,
                    _metadata.PackageCode,
                    installRoot,
                    true,
                    false,
                    ownershipNonce);
            LocalModuleMsiCloneIdentity identity =
                new LocalModuleMsiIdentityFactory(_newGuid).Create(
                    _metadata.ProductVersion,
                    request.Inn,
                    request.CloneOrdinal);
            return LocalModuleMsiManifest.Create(
                request.Inn,
                request.CloneOrdinal,
                request.ApiPort,
                request.DatabasePort,
                identity.ProductCode.ToString("B").ToUpperInvariant(),
                identity.PackageCode.ToString("B").ToUpperInvariant(),
                installRoot,
                true,
                false,
                ownershipNonce);
        }

        public void Install(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest)
        {
            RequireSource();
            LocalModuleInstallerProperties.RequireSupportedBy(
                _source.CapabilityProfile);
            string installRoot = InstallRoot(request);
            if (request.CloneOrdinal == 0)
            {
                if (!string.Equals(manifest.ProductCode,
                        _metadata.ProductCode,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(manifest.PackageCode,
                        _metadata.PackageCode,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Prepared base MSI identity changed before install.");
                _installer.Install(
                    _source.FullPath,
                    InstallProperties(installRoot));
                RequireInstalled(
                    manifest.ProductCode,
                    _metadata.ProductName,
                    installRoot);
                return;
            }

            Guid packageCode;
            if (!Guid.TryParseExact(manifest.PackageCode, "B", out packageCode))
                throw new InvalidDataException(
                    "Prepared clone PackageCode is invalid.");
            LocalModuleMsiCloneIdentity identity =
                new LocalModuleMsiIdentityFactory(delegate {
                    return packageCode;
                }).Create(
                    _metadata.ProductVersion,
                    request.Inn,
                    request.CloneOrdinal);
            if (!string.Equals(manifest.ProductCode,
                    identity.ProductCode.ToString("B"),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Prepared clone ProductCode changed before install.");
            LocalModuleMsiTransformPlan plan =
                LocalModuleMsiTransformPlan.Create(
                    identity,
                    _metadata.ProductVersion,
                    request.ApiPort,
                    request.DatabasePort);
            string stagingOperation = _newGuid().ToString("N");
            using (LocalModuleMsiWorkspace workspace =
                LocalModuleMsiWorkspace.Create(
                    _machineRoot,
                    stagingOperation,
                    manifest.OwnershipNonce,
                    _pathSafety))
            {
                workspace.CopySource(_source.FullPath);
                using (TransformedLocalModuleMsi transformed =
                    _transformer.Transform(
                        _source,
                        plan,
                        workspace.OutputMsiPath))
                {
                    workspace.MarkTransformed();
                    workspace.MarkVerified();
                    _installer.Install(
                        transformed.FullPath,
                        InstallProperties(installRoot));
                    workspace.MarkInstallerReturned();
                }
            }
            RequireInstalled(
                manifest.ProductCode,
                identity.ProductName,
                installRoot);
        }

        public LocalModuleMsiManifest AdoptPreExistingBase(
            LocalModuleMsiProvisioningItemRequest request,
            string ownershipNonce)
        {
            if (request.CloneOrdinal != 0)
                throw new InvalidOperationException(
                    "Only the exact vendor base may be preserved as pre-existing.");
            string installRoot = InstallRoot(request);
            RequireInstalled(
                _metadata.ProductCode,
                _metadata.ProductName,
                installRoot);
            return LocalModuleMsiManifest.Create(
                request.Inn,
                0,
                request.ApiPort,
                request.DatabasePort,
                _metadata.ProductCode,
                _metadata.PackageCode,
                installRoot,
                false,
                true,
                ownershipNonce);
        }

        public LocalModuleFirewallRule EnsureFirewall(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest)
        {
            return _firewall.EnsureApiRule(
                Layout(request, manifest),
                manifest.OwnershipNonce,
                request.RemoteAddress);
        }

        public LocalModuleStartModeAdjustment EnsureAutomaticStart(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest)
        {
            return _startModes.EnsureAutomatic(Layout(request, manifest));
        }

        public void RestoreStartMode(LocalModuleMsiManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            if (!manifest.StartModeAdjusted) return;
            _startModes.Restore(
                LocalModuleInstalledLayout.Create(
                    manifest.InstallRoot,
                    manifest.CloneOrdinal,
                    manifest.ApiPort,
                    manifest.DatabasePort),
                (WindowsServiceStartMode)manifest.PreviousApiStartMode,
                (WindowsServiceStartMode)manifest.PreviousDatabaseStartMode);
        }

        public void StartAndVerify(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest)
        {
            LocalModuleInstalledLayout layout = Layout(request, manifest);
            _configurations.Inspect(layout);
            _servicePairs.StartDatabaseThenApi(layout);
        }

        public void StopAndVerify(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest)
        {
            _servicePairs.StopApiThenDatabase(Layout(request, manifest));
        }

        public void RemoveFirewall(LocalModuleMsiManifest manifest)
        {
            _firewall.Remove(
                manifest.FirewallRuleName,
                manifest.FirewallRuleHash);
        }

        public void Uninstall(LocalModuleMsiManifest manifest)
        {
            IList<InstalledLocalModuleProduct> installed = _products.ReadAll();
            bool productPresent = false;
            for (int index = 0; index < installed.Count; index++)
                if (string.Equals(installed[index].ProductCode,
                        manifest.ProductCode,
                        StringComparison.OrdinalIgnoreCase))
                {
                    productPresent = true;
                    break;
                }
            if (productPresent)
                _installer.Uninstall(manifest.ProductCode);

            IList<InstalledLocalModuleProduct> remaining = _products.ReadAll();
            for (int index = 0; index < remaining.Count; index++)
                if (string.Equals(remaining[index].ProductCode,
                        manifest.ProductCode,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Windows Installer still reports the removed product.");
            if (manifest.CloneOrdinal > 0 && manifest.CanRemove)
                RemoveCloneResidue(manifest);
        }

        private void RemoveCloneResidue(LocalModuleMsiManifest manifest)
        {
            LocalModuleInstalledLayout layout = LocalModuleInstalledLayout.Create(
                manifest.InstallRoot,
                manifest.CloneOrdinal,
                manifest.ApiPort,
                manifest.DatabasePort);
            RemoveStoppedPartialService(
                layout,
                LocalModuleProcessRole.Api);
            RemoveStoppedPartialService(
                layout,
                LocalModuleProcessRole.Database);

            string expectedName = "Regime" +
                manifest.CloneOrdinal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            string root = Path.GetFullPath(manifest.InstallRoot)
                .TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(
                    Path.GetFileName(root),
                    expectedName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(root)),
                    "Program Files",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Clone installation root is not exact.");
            if (Directory.Exists(root))
            {
                RejectReparseTree(root);
                DeleteCloneDirectory(root);
            }

            string suffix = " экземпляр " +
                manifest.CloneOrdinal.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            RemoveInstallRegistryKey(
                RegistryHive.LocalMachine,
                @"SOFTWARE\Эквирон\Локальный модуль ЧЗ" + suffix,
                manifest.InstallRoot);
            RemoveCrptRegistryKey(
                @"SOFTWARE\ЦРПТ\Локальный модуль ЧЗ" + suffix);
        }

        private void DeleteCloneDirectory(string root)
        {
            IOException lastError = null;
            for (int attempt = 0; attempt < 120; attempt++)
            {
                try
                {
                    if (!Directory.Exists(root)) return;
                    Directory.Delete(root, true);
                    return;
                }
                catch (IOException exception)
                {
                    lastError = exception;
                    _delay();
                }
            }
            throw new IOException(
                "Clone installation directory remained locked after uninstall.",
                lastError);
        }

        private void RemoveStoppedPartialService(
            LocalModuleInstalledLayout layout,
            LocalModuleProcessRole role)
        {
            string serviceName = layout.ServiceName(role);
            WindowsServiceRecord service = _services.Query(serviceName);
            if (service == null) return;
            if (!layout.MatchesService(role, service))
                throw new InvalidOperationException(
                    "Partial clone service is not owned by this installation.");
            if (service.State != WindowsServiceState.Stopped ||
                service.ProcessId != 0)
                throw new InvalidOperationException(
                    "Partial clone service is still running.");

            _services.Delete(serviceName);
            for (int attempt = 0; attempt < 120; attempt++)
            {
                if (_services.Query(serviceName) == null) return;
                _delay();
            }
            throw new InvalidOperationException(
                "Partial clone service was not deleted from SCM.");
        }

        private static void RejectReparseTree(string root)
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Clone installation root is a reparse point.");
            string[] entries = Directory.GetFileSystemEntries(
                root,
                "*",
                SearchOption.AllDirectories);
            for (int index = 0; index < entries.Length; index++)
                if ((File.GetAttributes(entries[index]) &
                        FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "Clone installation tree contains a reparse point.");
        }

        private static void RemoveInstallRegistryKey(
            RegistryHive hive,
            string subKey,
            string expectedRoot)
        {
            RegistryView[] views =
            {
                RegistryView.Registry64,
                RegistryView.Registry32
            };
            for (int index = 0; index < views.Length; index++)
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, views[index]))
            using (RegistryKey key = baseKey.OpenSubKey(subKey, true))
            {
                if (key == null) continue;
                string installDir = key.GetValue("InstallDir") as string;
                if (!PathsEqual(installDir, expectedRoot))
                    throw new InvalidDataException(
                        "Clone registry installation root is foreign.");
                key.Close();
                baseKey.DeleteSubKeyTree(subKey, false);
            }
        }

        private static void RemoveCrptRegistryKey(string subKey)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, true))
            {
                if (key == null) return;
                object installed = key.GetValue("installed");
                if (installed == null || Convert.ToInt32(installed) != 1)
                    throw new InvalidDataException(
                        "Clone CRPT registry marker is foreign.");
                key.Close();
                Registry.CurrentUser.DeleteSubKeyTree(subKey, false);
            }
        }

        public void WaitForEpmdExit()
        {
            for (int attempt = 0; attempt < 120; attempt++)
            {
                IList<int> owners = _listeners.FindListenerProcessIds(4369);
                if (owners != null && owners.Count == 0) return;
                if (attempt + 1 < 120) _delay();
            }
            throw new InvalidOperationException(
                "EPMD listener did not stop after base removal.");
        }

        private LocalModuleInstalledLayout Layout(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest)
        {
            if (!string.Equals(InstallRoot(request), manifest.InstallRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                manifest.ApiPort != request.ApiPort ||
                manifest.DatabasePort != request.DatabasePort)
                throw new InvalidDataException(
                    "Installed local-module layout does not match its manifest.");
            return LocalModuleInstalledLayout.Create(
                manifest.InstallRoot,
                manifest.CloneOrdinal,
                manifest.ApiPort,
                manifest.DatabasePort);
        }

        private string InstallRoot(LocalModuleMsiProvisioningItemRequest request)
        {
            string volume = LocalModuleInstallRootPolicy.ResolveVolumeRoot(
                request.InstallVolumeRoot,
                Path.Combine(request.InstallVolumeRoot,
                    "Program Files", "Regime"));
            return request.CloneOrdinal == 0
                ? Path.Combine(volume, "Program Files", "Regime")
                : LocalModuleInstallRootPolicy.BuildCloneInstallDirectory(
                    volume,
                    request.CloneOrdinal);
        }

        private ProductExpectation ExpectedProduct(
            LocalModuleMsiProvisioningItemRequest request,
            LocalModuleMsiManifest manifest)
        {
            if (manifest != null)
                return new ProductExpectation
                {
                    ProductCode = manifest.ProductCode,
                    ProductName = request.CloneOrdinal == 0
                        ? _metadata.ProductName
                        : "Локальный модуль Честный Знак экземпляр " +
                            request.CloneOrdinal.ToString()
                };
            if (request.CloneOrdinal == 0)
                return new ProductExpectation
                {
                    ProductCode = _metadata.ProductCode,
                    ProductName = _metadata.ProductName
                };
            LocalModuleMsiCloneIdentity identity =
                new LocalModuleMsiIdentityFactory(delegate {
                    return new Guid("f0000000-0000-4000-8000-000000000001");
                }).Create(
                    _metadata.ProductVersion,
                    request.Inn,
                    request.CloneOrdinal);
            return new ProductExpectation
            {
                ProductCode = identity.ProductCode.ToString("B")
                    .ToUpperInvariant(),
                ProductName = identity.ProductName
            };
        }

        private void RequireInstalled(
            string productCode,
            string productName,
            string installRoot)
        {
            if (_products.FindExact(
                    productCode,
                    productName,
                    _metadata.ProductVersion,
                    installRoot) == null)
                throw new InvalidOperationException(
                    "Windows Installer did not confirm the exact LM product.");
        }

        private static string InstallProperties(string installRoot)
        {
            return LocalModuleInstallerProperties.Build(installRoot);
        }

        private void RequireSource()
        {
            if (_source == null)
                throw new InvalidOperationException(
                    "A verified source MSI is required for installation.");
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) ||
                string.IsNullOrWhiteSpace(right)) return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(
                        Path.DirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(
                        Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                if (exception is ArgumentException ||
                    exception is NotSupportedException ||
                    exception is PathTooLongException)
                    return false;
                throw;
            }
        }

        private sealed class ProductExpectation
        {
            internal string ProductCode { get; set; }
            internal string ProductName { get; set; }
        }
    }
}
