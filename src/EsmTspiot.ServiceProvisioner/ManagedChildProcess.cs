using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class ManagedChildStartPlan
    {
        internal ManagedChildStartPlan(
            string executablePath,
            string workingDirectory,
            IList<string> argumentTokens,
            IDictionary<string, string> environment)
        {
            if (string.IsNullOrWhiteSpace(executablePath) ||
                !Path.IsPathRooted(executablePath))
            {
                throw new ArgumentException("An absolute executable path is required.", "executablePath");
            }
            if (string.IsNullOrWhiteSpace(workingDirectory) ||
                !Path.IsPathRooted(workingDirectory))
            {
                throw new ArgumentException("An absolute working directory is required.", "workingDirectory");
            }
            if (argumentTokens == null) throw new ArgumentNullException("argumentTokens");
            if (environment == null) throw new ArgumentNullException("environment");

            List<string> tokens = new List<string>(argumentTokens.Count);
            for (int index = 0; index < argumentTokens.Count; index++)
            {
                string argumentValue = argumentTokens[index];
                if (argumentValue == null || argumentValue.IndexOf('\0') >= 0 ||
                    argumentValue.IndexOf('\r') >= 0 ||
                    argumentValue.IndexOf('\n') >= 0)
                {
                    throw new InvalidDataException("Process argument value is invalid.");
                }
                tokens.Add(argumentValue);
            }

            Dictionary<string, string> variables =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> item in environment)
            {
                if (string.IsNullOrEmpty(item.Key) || item.Key.IndexOf('=') >= 0 ||
                    item.Key.IndexOf('\0') >= 0 || item.Value == null ||
                    item.Value.IndexOf('\0') >= 0)
                {
                    throw new InvalidDataException("Process environment contains an invalid entry.");
                }
                variables.Add(item.Key, item.Value);
            }

            ExecutablePath = Path.GetFullPath(executablePath);
            WorkingDirectory = Path.GetFullPath(workingDirectory);
            ArgumentTokens = tokens.AsReadOnly();
            Environment = new ReadOnlyDictionary<string, string>(variables);
        }

        internal string ExecutablePath { get; private set; }
        internal string WorkingDirectory { get; private set; }
        internal IList<string> ArgumentTokens { get; private set; }
        internal IDictionary<string, string> Environment { get; private set; }
    }

    internal interface IManagedChildRuntime
    {
        int Start(ManagedChildStartPlan plan);
        bool SendGracefulStop(int processId);
        bool WaitForExit(int processId, int milliseconds);
    }

    internal sealed class ManagedChildProcess
    {
        private readonly IManagedChildRuntime _runtime;
        private readonly ManagedChildStartPlan _plan;
        private readonly int _gracefulStopTimeoutMilliseconds;
        private int _processId;

        internal ManagedChildProcess(
            IManagedChildRuntime runtime,
            ManagedChildStartPlan plan,
            int gracefulStopTimeoutMilliseconds)
        {
            if (runtime == null) throw new ArgumentNullException("runtime");
            if (plan == null) throw new ArgumentNullException("plan");
            if (gracefulStopTimeoutMilliseconds < 1 ||
                gracefulStopTimeoutMilliseconds > 60000)
            {
                throw new ArgumentOutOfRangeException("gracefulStopTimeoutMilliseconds");
            }
            _runtime = runtime;
            _plan = plan;
            _gracefulStopTimeoutMilliseconds = gracefulStopTimeoutMilliseconds;
        }

        internal int ProcessId { get { return _processId; } }

        internal int Start()
        {
            if (_processId != 0)
            {
                throw new InvalidOperationException("Managed child is already started.");
            }
            int processId = _runtime.Start(_plan);
            if (processId <= 0)
            {
                throw new InvalidOperationException("Managed child did not return a valid PID.");
            }
            _processId = processId;
            return processId;
        }

        internal bool StopGracefully()
        {
            if (_processId == 0) return true;
            int processId = _processId;
            if (!_runtime.SendGracefulStop(processId)) return false;
            bool exited = _runtime.WaitForExit(
                processId,
                _gracefulStopTimeoutMilliseconds);
            if (exited) _processId = 0;
            return exited;
        }

        internal bool WaitForExit()
        {
            if (_processId == 0) return true;
            int processId = _processId;
            bool exited = _runtime.WaitForExit(processId, Timeout.Infinite);
            if (exited) _processId = 0;
            return exited;
        }
    }

    internal sealed class NativeManagedChildRuntime : IManagedChildRuntime
    {
        private const uint CreateNewConsole = 0x00000010;
        private const uint CreateNewProcessGroup = 0x00000200;
        private const uint CreateUnicodeEnvironment = 0x00000400;
        private const uint CtrlBreakEvent = 1;
        private const uint WaitObject0 = 0;
        private const uint WaitTimeout = 258;
        private const uint Infinite = 0xFFFFFFFF;

        private readonly object _sync = new object();
        private IntPtr _processHandle;
        private int _processId;

        ~NativeManagedChildRuntime()
        {
            IntPtr handle;
            lock (_sync)
            {
                handle = _processHandle;
                _processHandle = IntPtr.Zero;
                _processId = 0;
            }
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }

        public int Start(ManagedChildStartPlan plan)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            IntPtr environment = BuildEnvironmentBlock(plan.Environment);
            ProcessInformation processInformation = new ProcessInformation();
            StartupInfo startupInfo = new StartupInfo();
            startupInfo.Size = Marshal.SizeOf(typeof(StartupInfo));
            StringBuilder commandLine = new StringBuilder(
                WindowsCommandLine.QuoteArgument(plan.ExecutablePath));
            for (int index = 0; index < plan.ArgumentTokens.Count; index++)
            {
                commandLine.Append(' ');
                commandLine.Append(WindowsCommandLine.QuoteArgument(
                    plan.ArgumentTokens[index]));
            }

            bool retainedProcessHandle = false;
            try
            {
                bool created = CreateProcessW(
                    plan.ExecutablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateNewConsole | CreateNewProcessGroup |
                        CreateUnicodeEnvironment,
                    environment,
                    plan.WorkingDirectory,
                    ref startupInfo,
                    out processInformation);
                if (!created)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                int processId = unchecked((int)processInformation.ProcessId);
                lock (_sync)
                {
                    if (_processHandle != IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            "Native child runtime already owns a process handle.");
                    }
                    _processHandle = processInformation.ProcessHandle;
                    _processId = processId;
                    retainedProcessHandle = true;
                }
                return processId;
            }
            finally
            {
                if (processInformation.ThreadHandle != IntPtr.Zero)
                {
                    CloseHandle(processInformation.ThreadHandle);
                }
                if (!retainedProcessHandle &&
                    processInformation.ProcessHandle != IntPtr.Zero)
                {
                    CloseHandle(processInformation.ProcessHandle);
                }
                Marshal.FreeHGlobal(environment);
            }
        }

        public bool SendGracefulStop(int processId)
        {
            if (processId <= 0 || !OwnsProcess(processId)) return false;
            FreeConsole();
            if (!AttachConsole(unchecked((uint)processId)))
            {
                return HasExited(processId);
            }
            try
            {
                if (!SetConsoleCtrlHandler(null, true)) return false;
                return GenerateConsoleCtrlEvent(
                    CtrlBreakEvent,
                    unchecked((uint)processId));
            }
            finally
            {
                FreeConsole();
                SetConsoleCtrlHandler(null, false);
            }
        }

        public bool WaitForExit(int processId, int milliseconds)
        {
            if (milliseconds < Timeout.Infinite)
            {
                throw new ArgumentOutOfRangeException("milliseconds");
            }
            IntPtr handle = GetOwnedProcessHandle(processId);
            if (handle == IntPtr.Zero)
            {
                return true;
            }
            uint timeout = milliseconds == Timeout.Infinite
                ? Infinite
                : unchecked((uint)milliseconds);
            uint result = WaitForSingleObject(handle, timeout);
            if (result == WaitObject0) return true;
            if (result == WaitTimeout) return false;
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private bool HasExited(int processId)
        {
            IntPtr handle = GetOwnedProcessHandle(processId);
            return handle == IntPtr.Zero ||
                WaitForSingleObject(handle, 0) == WaitObject0;
        }

        private bool OwnsProcess(int processId)
        {
            lock (_sync)
            {
                return _processHandle != IntPtr.Zero &&
                    _processId == processId;
            }
        }

        private IntPtr GetOwnedProcessHandle(int processId)
        {
            lock (_sync)
            {
                return _processId == processId
                    ? _processHandle
                    : IntPtr.Zero;
            }
        }

        private static IntPtr BuildEnvironmentBlock(
            IDictionary<string, string> environment)
        {
            List<string> entries = new List<string>();
            foreach (KeyValuePair<string, string> item in environment)
            {
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
        private static extern uint WaitForSingleObject(
            IntPtr handle,
            uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GenerateConsoleCtrlEvent(
            uint controlEvent,
            uint processGroupId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(
            ConsoleCtrlHandler handler,
            bool add);

        private delegate bool ConsoleCtrlHandler(uint controlType);
    }
}
