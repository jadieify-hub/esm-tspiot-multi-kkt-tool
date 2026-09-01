using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LegacyProcessSnapshot
    {
        internal int ProcessId { get; set; }
        internal long CreationTimeUtcTicks { get; set; }
        internal string ImagePath { get; set; }
        internal string ImageSha256 { get; set; }
        internal string CommandLine { get; set; }
    }

    internal sealed class VerifiedLegacyProcessIdentity
    {
        private VerifiedLegacyProcessIdentity()
        {
        }

        internal int ProcessId { get; private set; }
        internal long CreationTimeUtcTicks { get; private set; }
        internal string ImagePath { get; private set; }
        internal string ImageSha256 { get; private set; }
        internal string CommandLine { get; private set; }
        internal string ManifestFingerprint { get; private set; }

        internal static VerifiedLegacyProcessIdentity Create(
            LegacyProcessSnapshot observed,
            string expectedRoot,
            string expectedImagePath,
            string expectedImageSha256,
            string expectedCommandLine,
            string manifestFingerprint)
        {
            if (observed == null || observed.ProcessId <= 0 ||
                observed.CreationTimeUtcTicks <= 0 ||
                !IsHex(expectedImageSha256, 64) ||
                !IsHex(manifestFingerprint, 64) ||
                string.IsNullOrWhiteSpace(expectedCommandLine))
            {
                throw new InvalidDataException(
                    "Legacy process ownership evidence is incomplete.");
            }
            string root = Path.GetFullPath(expectedRoot);
            string expectedPath = Path.GetFullPath(expectedImagePath);
            string observedPath = Path.GetFullPath(observed.ImagePath);
            if (!PathSafety.IsUnderRoot(expectedPath, root) ||
                !PathSafety.IsUnderRoot(observedPath, root) ||
                !string.Equals(observedPath, expectedPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    observed.ImageSha256,
                    expectedImageSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    observed.CommandLine,
                    expectedCommandLine,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Legacy process does not match the protected manifest identity.");
            }
            return new VerifiedLegacyProcessIdentity
            {
                ProcessId = observed.ProcessId,
                CreationTimeUtcTicks = observed.CreationTimeUtcTicks,
                ImagePath = observedPath,
                ImageSha256 = expectedImageSha256.ToLowerInvariant(),
                CommandLine = expectedCommandLine,
                ManifestFingerprint = manifestFingerprint.ToLowerInvariant()
            };
        }

        internal bool Matches(LegacyProcessSnapshot current)
        {
            return current != null && current.ProcessId == ProcessId &&
                current.CreationTimeUtcTicks == CreationTimeUtcTicks &&
                string.Equals(
                    Path.GetFullPath(current.ImagePath),
                    ImagePath,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    current.ImageSha256,
                    ImageSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.CommandLine, CommandLine, StringComparison.Ordinal);
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char valueChar = value[index];
                if (!((valueChar >= '0' && valueChar <= '9') ||
                      (valueChar >= 'a' && valueChar <= 'f') ||
                      (valueChar >= 'A' && valueChar <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }
    }

    internal interface ILegacyProcessSnapshotReader
    {
        LegacyProcessSnapshot Read(int processId);
    }

    internal interface ILegacyNativeTerminator
    {
        void TerminateAndWait(int processId, int timeoutMilliseconds);
    }

    internal sealed class LegacyOwnedProcessTerminator
    {
        private readonly ILegacyProcessSnapshotReader _reader;
        private readonly ILegacyNativeTerminator _native;

        internal LegacyOwnedProcessTerminator(
            ILegacyProcessSnapshotReader reader,
            ILegacyNativeTerminator native)
        {
            if (reader == null) throw new ArgumentNullException("reader");
            if (native == null) throw new ArgumentNullException("native");
            _reader = reader;
            _native = native;
        }

        internal void Terminate(
            VerifiedLegacyProcessIdentity identity,
            ILmProvisioningCancellation cancellation)
        {
            if (identity == null) throw new ArgumentNullException("identity");
            if (cancellation != null && cancellation.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "Legacy process cleanup was cancelled before termination.");
            }
            LegacyProcessSnapshot current = _reader.Read(identity.ProcessId);
            if (current == null)
            {
                return;
            }
            if (!identity.Matches(current))
            {
                throw new InvalidDataException(
                    "Legacy process identity changed before termination; possible PID reuse.");
            }
            _native.TerminateAndWait(identity.ProcessId, 10000);
            if (_reader.Read(identity.ProcessId) != null)
            {
                throw new IOException(
                    "Verified legacy process did not exit after termination.");
            }
        }
    }

    internal sealed class WindowsLegacyNativeTerminator : ILegacyNativeTerminator
    {
        private const uint ProcessTerminate = 0x0001;
        private const uint Synchronize = 0x00100000;
        private const uint WaitObject0 = 0;

        public void TerminateAndWait(int processId, int timeoutMilliseconds)
        {
            IntPtr process = OpenProcess(
                ProcessTerminate | Synchronize,
                false,
                processId);
            if (process == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try
            {
                if (!TerminateProcess(process, 0x4B52534C))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                uint waited = WaitForSingleObject(
                    process,
                    unchecked((uint)timeoutMilliseconds));
                if (waited != WaitObject0)
                {
                    throw new TimeoutException(
                        "Verified legacy process did not exit within the bounded timeout.");
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint desiredAccess,
            bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
