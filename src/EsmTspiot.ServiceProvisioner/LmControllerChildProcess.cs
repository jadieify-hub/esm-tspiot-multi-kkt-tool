using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class ControllerChildStartPlan
    {
        internal string FileName { get; set; }
        internal string Arguments { get; set; }
        internal string WorkingDirectory { get; set; }
        internal IDictionary<string, string> Environment { get; set; }
    }

    internal interface IProcessEnvironmentReader
    {
        IDictionary<string, string> ReadCurrent();
    }

    internal sealed class ProcessEnvironmentReader : IProcessEnvironmentReader
    {
        public IDictionary<string, string> ReadCurrent()
        {
            Dictionary<string, string> result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            IDictionary environment = System.Environment.GetEnvironmentVariables();
            foreach (DictionaryEntry item in environment)
            {
                string key = item.Key as string;
                string value = item.Value as string;
                if (!string.IsNullOrEmpty(key) && value != null)
                {
                    result[key] = value;
                }
            }
            return result;
        }
    }

    internal interface IControllerChildRuntime
    {
        int Start(ControllerChildStartPlan plan);
        bool SendGracefulStop(int processId);
        bool WaitForExit(int processId, int milliseconds);
    }

    internal sealed class LmControllerChildProcess
    {
        private readonly ControllerCapabilityProfile _profile;
        private readonly VerifiedControllerBinary _binary;
        private readonly string _profileRoot;
        private readonly IProcessEnvironmentReader _environmentReader;
        private readonly IControllerChildRuntime _runtime;
        private int _processId;

        internal LmControllerChildProcess(
            ControllerCapabilityProfile profile,
            VerifiedControllerBinary binary,
            string profileRoot,
            IProcessEnvironmentReader environmentReader,
            IControllerChildRuntime runtime)
        {
            _profile = profile ?? throw new ArgumentNullException("profile");
            _binary = binary ?? throw new ArgumentNullException("binary");
            _profileRoot = Path.GetFullPath(profileRoot);
            _environmentReader = environmentReader ?? throw new ArgumentNullException("environmentReader");
            _runtime = runtime ?? throw new ArgumentNullException("runtime");
            if (!string.Equals(_binary.Version, _profile.Version, StringComparison.Ordinal) ||
                !string.Equals(_profile.TerminalArguments, string.Empty, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Controller binary or terminal contract is unsupported.");
            }
        }

        internal int Start()
        {
            if (_processId != 0)
            {
                throw new InvalidOperationException("Controller child is already started.");
            }

            IDictionary<string, string> environment = _environmentReader.ReadCurrent();
            Dictionary<string, string> childEnvironment = new Dictionary<string, string>(
                environment,
                StringComparer.OrdinalIgnoreCase);
            childEnvironment[_profile.ProfileEnvironmentKey] = _profileRoot;
            ControllerChildStartPlan plan = new ControllerChildStartPlan
            {
                FileName = _binary.FullPath,
                Arguments = _profile.TerminalArguments,
                WorkingDirectory = Path.GetDirectoryName(_binary.FullPath),
                Environment = childEnvironment
            };
            _processId = _runtime.Start(plan);
            if (_processId <= 0)
            {
                _processId = 0;
                throw new InvalidOperationException("Controller child did not return a valid PID.");
            }
            return _processId;
        }

        internal bool StopGracefully()
        {
            if (_processId == 0)
            {
                return true;
            }
            int processId = _processId;
            if (!_runtime.SendGracefulStop(processId))
            {
                return false;
            }
            bool exited = _runtime.WaitForExit(
                processId,
                _profile.GracefulStopTimeoutMilliseconds);
            if (exited)
            {
                _processId = 0;
            }
            return exited;
        }

        internal bool WaitForExit()
        {
            if (_processId == 0)
            {
                return true;
            }
            int processId = _processId;
            bool exited = _runtime.WaitForExit(processId, Timeout.Infinite);
            if (exited)
            {
                _processId = 0;
            }
            return exited;
        }
    }

    internal sealed class NativeControllerChildRuntime : IControllerChildRuntime
    {
        private const uint CreateNewConsole = 0x00000010;
        private const uint CreateUnicodeEnvironment = 0x00000400;
        private const uint CtrlBreakEvent = 1;

        public int Start(ControllerChildStartPlan plan)
        {
            ValidatePlan(plan);
            IntPtr environment = BuildEnvironmentBlock(plan.Environment);
            ProcessInformation processInformation = new ProcessInformation();
            StartupInfo startupInfo = new StartupInfo();
            startupInfo.Size = Marshal.SizeOf(typeof(StartupInfo));
            StringBuilder commandLine = new StringBuilder(
                WindowsCommandLine.QuoteArgument(plan.FileName));
            try
            {
                bool created = CreateProcessW(
                    plan.FileName,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateNewConsole | CreateUnicodeEnvironment,
                    environment,
                    plan.WorkingDirectory,
                    ref startupInfo,
                    out processInformation);
                if (!created)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return unchecked((int)processInformation.ProcessId);
            }
            finally
            {
                if (processInformation.ThreadHandle != IntPtr.Zero)
                {
                    CloseHandle(processInformation.ThreadHandle);
                }
                if (processInformation.ProcessHandle != IntPtr.Zero)
                {
                    CloseHandle(processInformation.ProcessHandle);
                }
                Marshal.FreeHGlobal(environment);
            }
        }

        public bool SendGracefulStop(int processId)
        {
            if (processId <= 0)
            {
                return false;
            }
            FreeConsole();
            if (!AttachConsole(unchecked((uint)processId)))
            {
                return HasExited(processId);
            }
            try
            {
                if (!SetConsoleCtrlHandler(null, true))
                {
                    return false;
                }
                return GenerateConsoleCtrlEvent(CtrlBreakEvent, 0);
            }
            finally
            {
                FreeConsole();
                SetConsoleCtrlHandler(null, false);
            }
        }

        public bool WaitForExit(int processId, int milliseconds)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return process.WaitForExit(milliseconds);
                }
            }
            catch (ArgumentException)
            {
                return true;
            }
        }

        private static bool HasExited(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }
        }

        private static void ValidatePlan(ControllerChildStartPlan plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.FileName) ||
                !Path.IsPathRooted(plan.FileName) ||
                !string.Equals(plan.Arguments, string.Empty, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(plan.WorkingDirectory) ||
                plan.Environment == null)
            {
                throw new InvalidDataException("Controller child start plan is invalid.");
            }
        }

        private static IntPtr BuildEnvironmentBlock(IDictionary<string, string> environment)
        {
            List<string> entries = new List<string>();
            foreach (KeyValuePair<string, string> item in environment)
            {
                if (string.IsNullOrEmpty(item.Key) || item.Key.IndexOf('=') >= 0 ||
                    item.Value == null || item.Value.IndexOf('\0') >= 0)
                {
                    throw new InvalidDataException("Process environment contains an invalid entry.");
                }
                entries.Add(item.Key + "=" + item.Value);
            }
            entries.Sort(StringComparer.OrdinalIgnoreCase);
            string block = string.Join("\0", entries.ToArray()) + "\0\0";
            return Marshal.StringToHGlobalUni(block);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            internal int Size;
            private string Reserved;
            private string Desktop;
            private string Title;
            private int X;
            private int Y;
            private int XSize;
            private int YSize;
            private int XCountChars;
            private int YCountChars;
            private int FillAttribute;
            private int Flags;
            private short ShowWindow;
            private short Reserved2;
            private IntPtr Reserved2Pointer;
            private IntPtr StandardInput;
            private IntPtr StandardOutput;
            private IntPtr StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            internal IntPtr ProcessHandle;
            internal IntPtr ThreadHandle;
            internal uint ProcessId;
            private uint ThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, bool add);

        private delegate bool ConsoleCtrlHandler(uint controlType);
    }
}
