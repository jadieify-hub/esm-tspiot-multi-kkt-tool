using System;
using System.Collections.Generic;
using System.Threading;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.WinForms.Shared;

namespace EsmTspiot.Operator.Tests
{
    internal static class Program
    {
        private static int _failed;

        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--frontol-rollback-check")
            {
                Run("Installed Firebird applies atomically and rolls test changes back", FrontolTests.CheckLocalTransaction);
                return _failed == 0 ? 0 : 1;
            }
            Run("PE architecture accepts only the current process machine", PeArchitectureAcceptsOnlyCurrentMachine);
            Run("ATOL runtime locator skips incompatible installations", RuntimeLocatorSkipsIncompatibleInstallation);
            Run("ATOL VCOM enumeration keeps MI_00 and numeric order", VcomEnumerationKeepsMi00AndNumericOrder);
            Run("ATOL provider uses exact COM and releases a normal lease", ProviderUsesExactComAndReleasesNormalLease);
            Run("ATOL provider closes and destroys after identity failure", ProviderCleansUpAfterIdentityFailure);
            Run("Frontol reads the cashier INI and COM settings", FrontolTests.ReadsCashierSettings);
            Run("Frontol uses confirmed API v1 client ports", FrontolTests.UsesConfirmedApi1ClientPorts);
            Run("Frontol maps physical serials to software ports", FrontolTests.MapsSoftwarePorts);
            Run("Frontol rejects ambiguous or missing KKT matches", FrontolTests.RejectsUnsafeMatches);

            if (_failed != 0)
            {
                Console.Error.WriteLine("Operator tests failed: " + _failed);
                return 1;
            }

            Console.WriteLine("All operator tests passed.");
            return 0;
        }

        private static void PeArchitectureAcceptsOnlyCurrentMachine()
        {
            byte[] x86 = CreatePe(0x014c);
            byte[] x64 = CreatePe(0x8664);
            AssertTrue(AtolPortableExecutable.IsCompatible(x86, false), "x86 process must accept x86 native DLL.");
            AssertFalse(AtolPortableExecutable.IsCompatible(x64, false), "x86 process must reject x64 native DLL.");
            AssertTrue(AtolPortableExecutable.IsCompatible(x64, true), "x64 process must accept x64 native DLL.");
            AssertFalse(AtolPortableExecutable.IsCompatible(x86, true), "x64 process must reject x86 native DLL.");
        }

        private static void RuntimeLocatorSkipsIncompatibleInstallation()
        {
            FakeAtolDriverEnvironment environment = new FakeAtolDriverEnvironment();
            environment.Add("C:\\ATOL64", false);
            environment.Add("C:\\ATOL32", true);

            AtolDriverRuntime runtime = new AtolDriverRuntimeLocator(environment, false).Resolve();

            AssertEqual("C:\\ATOL32", runtime.InstallationRoot, "The first compatible installed runtime must be selected.");
            AssertEqual("C:\\ATOL32\\langs\\csharp\\Atol.Drivers10.Fptr.dll", runtime.WrapperPath, "Wrapper path must remain below the selected root.");
            AssertEqual("C:\\ATOL32\\bin\\fptr10.dll", runtime.NativeLibraryPath, "Native path must remain below the selected root.");
        }

        private static void VcomEnumerationKeepsMi00AndNumericOrder()
        {
            FakeAtolVcomRegistry registry = new FakeAtolVcomRegistry();
            registry.Add("USB\\VID_2912&PID_0005&MI_00", "COM11");
            registry.Add("USB\\VID_2912&PID_0005&MI_00", "COM7", false);
            registry.Add("USB\\VID_2912&PID_0005&MI_02", "COM8");
            registry.Add("USB\\VID_2912&PID_0005&MI_00", "COM2");
            registry.Add("USB\\VID_2912&PID_0005&MI_00", "com11");
            registry.Add("USB\\VID_1234&PID_0005&MI_00", "COM1");
            registry.Add("USB\\VID_2912&PID_0005&MI_00", "USB:auto");

            IList<KktConnectionPort> ports = new AtolVcomEnumerator(registry).EnumeratePorts();

            AssertEqual(2, ports.Count, "Only unique ATOL MI_00 COM ports are eligible.");
            AssertEqual("COM2", ports[0].PortName, "COM ports must be sorted by their numeric suffix.");
            AssertEqual("COM11", ports[1].PortName, "COM11 must follow COM2, not sort lexicographically.");
        }

        private static void ProviderUsesExactComAndReleasesNormalLease()
        {
            FakeAtolFptrRuntime runtime = new FakeAtolFptrRuntime();
            runtime.Identity = new KktConnectionIdentity
            {
                KktSerial = "00106126505156",
                ModelName = "АТОЛ 30Ф",
                FirmwareVersion = "5.8.1"
            };
            FakeAtolFptrRuntimeFactory factory = new FakeAtolFptrRuntimeFactory(runtime);
            AtolFptrConnectionProvider provider = CreateProvider(factory);

            IKktConnectionLease lease = provider.OpenAsync(
                new KktConnectionPort { PortName = "COM9" }, CancellationToken.None).Result;

            AssertEqual("COM9", runtime.PortName, "The provider must address the exact VCOM.");
            AssertFalse(runtime.AutoReconnect, "AutoReconnect must be disabled for an owned short session.");
            AssertTrue(runtime.OpenCalled, "The driver session must be opened.");
            AssertEqual("COM9", lease.Identity.PortName, "Read-back identity must be tied to the requested port.");
            lease.Dispose();
            lease.Dispose();
            AssertEqual(1, runtime.CloseCount, "A lease must close exactly once.");
            AssertEqual(1, runtime.DestroyCount, "A lease must destroy exactly once.");
        }

        private static void ProviderCleansUpAfterIdentityFailure()
        {
            FakeAtolFptrRuntime runtime = new FakeAtolFptrRuntime();
            runtime.IdentityError = new InvalidOperationException("broken read-back");
            FakeAtolFptrRuntimeFactory factory = new FakeAtolFptrRuntimeFactory(runtime);
            AtolFptrConnectionProvider provider = CreateProvider(factory);

            AssertThrows(delegate
            {
                provider.OpenAsync(
                    new KktConnectionPort { PortName = "COM9" }, CancellationToken.None).GetAwaiter().GetResult();
            });

            AssertEqual(1, runtime.CloseCount, "An opened session must close when identity read-back fails.");
            AssertEqual(1, runtime.DestroyCount, "The driver object must be destroyed when identity read-back fails.");
        }

        private static AtolFptrConnectionProvider CreateProvider(IAtolFptrRuntimeFactory factory)
        {
            FakeAtolDriverEnvironment environment = new FakeAtolDriverEnvironment();
            environment.Add("C:\\ATOL32", true);
            FakeAtolVcomRegistry registry = new FakeAtolVcomRegistry();
            registry.Add("USB\\VID_2912&PID_0005&MI_00", "COM9");
            return new AtolFptrConnectionProvider(
                new AtolDriverRuntimeLocator(environment, false),
                new AtolVcomEnumerator(registry),
                factory);
        }

        private static byte[] CreatePe(ushort machine)
        {
            byte[] bytes = new byte[256];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            bytes[0x3c] = 0x80;
            bytes[0x80] = (byte)'P';
            bytes[0x81] = (byte)'E';
            bytes[0x84] = (byte)(machine & 0xff);
            bytes[0x85] = (byte)(machine >> 8);
            return bytes;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.Error.WriteLine("FAIL " + name + ": " + ex.Message);
            }
        }

        private static void AssertTrue(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static void AssertFalse(bool value, string message)
        {
            if (value) throw new InvalidOperationException(message);
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(message + " Expected=" + expected + "; actual=" + actual + ".");
            }
        }

        private static void AssertThrows(Action action)
        {
            try
            {
                action();
            }
            catch
            {
                return;
            }
            throw new InvalidOperationException("Expected an exception.");
        }

        private sealed class FakeAtolDriverEnvironment : IAtolDriverEnvironment
        {
            private readonly List<AtolDriverRootCandidate> _candidates = new List<AtolDriverRootCandidate>();
            private readonly Dictionary<string, bool> _compatibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            public void Add(string root, bool compatible)
            {
                _candidates.Add(new AtolDriverRootCandidate { InstallationRoot = root, Source = "test" });
                _compatibility[root] = compatible;
            }

            public IList<AtolDriverRootCandidate> GetCandidates()
            {
                return new List<AtolDriverRootCandidate>(_candidates);
            }

            public bool FileExists(string path)
            {
                return path.EndsWith("Atol.Drivers10.Fptr.dll", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("fptr10.dll", StringComparison.OrdinalIgnoreCase);
            }

            public bool IsNativeLibraryCompatible(string path, bool process64Bit)
            {
                foreach (KeyValuePair<string, bool> pair in _compatibility)
                {
                    if (path.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
                }
                return false;
            }
        }

        private sealed class FakeAtolVcomRegistry : IAtolVcomRegistry
        {
            private readonly List<AtolVcomRegistryRecord> _records = new List<AtolVcomRegistryRecord>();

            public void Add(string hardwareId, string portName)
            {
                Add(hardwareId, portName, true);
            }

            public void Add(
                string hardwareId,
                string portName,
                bool isPresent)
            {
                _records.Add(new AtolVcomRegistryRecord
                {
                    HardwareId = hardwareId,
                    PortName = portName,
                    IsPresent = isPresent
                });
            }

            public IList<AtolVcomRegistryRecord> ReadRecords()
            {
                return new List<AtolVcomRegistryRecord>(_records);
            }
        }

        private sealed class FakeAtolFptrRuntimeFactory : IAtolFptrRuntimeFactory
        {
            private readonly IAtolFptrRuntime _runtime;

            public FakeAtolFptrRuntimeFactory(IAtolFptrRuntime runtime)
            {
                _runtime = runtime;
            }

            public IAtolFptrRuntime Create(AtolDriverRuntime driverRuntime)
            {
                return _runtime;
            }
        }

        private sealed class FakeAtolFptrRuntime : IAtolFptrRuntime
        {
            public string PortName { get; private set; }
            public bool AutoReconnect { get; private set; }
            public bool OpenCalled { get; private set; }
            public int CloseCount { get; private set; }
            public int DestroyCount { get; private set; }
            public KktConnectionIdentity Identity { get; set; }
            public Exception IdentityError { get; set; }

            public void Configure(string portName, bool autoReconnect)
            {
                PortName = portName;
                AutoReconnect = autoReconnect;
            }

            public void Open()
            {
                OpenCalled = true;
            }

            public KktConnectionIdentity ReadIdentity()
            {
                if (IdentityError != null) throw IdentityError;
                return Identity;
            }

            public void Close()
            {
                CloseCount++;
            }

            public void Destroy()
            {
                DestroyCount++;
            }
        }
    }
}
