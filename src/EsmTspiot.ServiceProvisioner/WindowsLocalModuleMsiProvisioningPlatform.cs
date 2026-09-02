using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class WindowsLocalModuleMsiProvisioningPlatform :
        ILocalModuleMsiProvisioningPlatform
    {
        private const string HiddenInstallerProperties =
            "ADMINLOGIN=admin ADMINPASSWORD=admin ";
        private readonly VerifiedLocalModulePackage _source;
        private readonly string _machineRoot;
        private readonly IPathSafety _pathSafety;
        private readonly ILocalModuleMsiTransformer _transformer;
        private readonly IWindowsInstallerApi _installer;
        private readonly InstalledLocalModuleProductReader _products;
        private readonly IWindowsServiceApi _services;
        private readonly LocalModuleServicePairController _servicePairs;
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
            if (source == null) throw new ArgumentNullException("source");
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
            _machineRoot = Path.GetFullPath(machineRoot);
            _pathSafety = pathSafety;
            _transformer = transformer;
            _installer = installer;
            _products = products;
            _services = services;
            _servicePairs = servicePairs;
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
                        _source.Metadata.ProductVersion,
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
                ConflictMessage = conflict
            };
        }

        public LocalModuleMsiManifest PrepareInstall(
            LocalModuleMsiProvisioningItemRequest request,
            string ownershipNonce)
        {
            string installRoot = InstallRoot(request);
            if (request.CloneOrdinal == 0)
                return LocalModuleMsiManifest.Create(
                    request.Inn,
                    request.CloneOrdinal,
                    request.ApiPort,
                    request.DatabasePort,
                    _source.Metadata.ProductCode,
                    _source.Metadata.PackageCode,
                    installRoot,
                    true,
                    false,
                    ownershipNonce);
            LocalModuleMsiCloneIdentity identity =
                new LocalModuleMsiIdentityFactory(_newGuid).Create(
                    _source.Metadata.ProductVersion,
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
            string installRoot = InstallRoot(request);
            if (request.CloneOrdinal == 0)
            {
                if (!string.Equals(manifest.ProductCode,
                        _source.Metadata.ProductCode,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(manifest.PackageCode,
                        _source.Metadata.PackageCode,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Prepared base MSI identity changed before install.");
                _installer.Install(
                    _source.FullPath,
                    InstallProperties(installRoot));
                RequireInstalled(
                    manifest.ProductCode,
                    _source.Metadata.ProductName,
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
                    _source.Metadata.ProductVersion,
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
                    _source.Metadata.ProductVersion,
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
                _source.Metadata.ProductCode,
                _source.Metadata.ProductName,
                installRoot);
            return LocalModuleMsiManifest.Create(
                request.Inn,
                0,
                request.ApiPort,
                request.DatabasePort,
                _source.Metadata.ProductCode,
                _source.Metadata.PackageCode,
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
            _installer.Uninstall(manifest.ProductCode);
            IList<InstalledLocalModuleProduct> remaining = _products.ReadAll();
            for (int index = 0; index < remaining.Count; index++)
                if (string.Equals(remaining[index].ProductCode,
                        manifest.ProductCode,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Windows Installer still reports the removed product.");
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
                        ? _source.Metadata.ProductName
                        : "Локальный модуль Честный Знак экземпляр " +
                            request.CloneOrdinal.ToString()
                };
            if (request.CloneOrdinal == 0)
                return new ProductExpectation
                {
                    ProductCode = _source.Metadata.ProductCode,
                    ProductName = _source.Metadata.ProductName
                };
            LocalModuleMsiCloneIdentity identity =
                new LocalModuleMsiIdentityFactory(delegate {
                    return new Guid("f0000000-0000-4000-8000-000000000001");
                }).Create(
                    _source.Metadata.ProductVersion,
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
                    _source.Metadata.ProductVersion,
                    installRoot) == null)
                throw new InvalidOperationException(
                    "Windows Installer did not confirm the exact LM product.");
        }

        private static string InstallProperties(string installRoot)
        {
            if (installRoot.IndexOf('"') >= 0)
                throw new InvalidDataException(
                    "Local-module installation root contains a quote.");
            return HiddenInstallerProperties + "APPLICATIONFOLDER=\"" +
                installRoot + "\"";
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
