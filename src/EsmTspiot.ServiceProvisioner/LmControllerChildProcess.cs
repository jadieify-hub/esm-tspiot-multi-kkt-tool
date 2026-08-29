using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }
            if (binary == null)
            {
                throw new ArgumentNullException("binary");
            }
            if (environmentReader == null)
            {
                throw new ArgumentNullException("environmentReader");
            }
            if (runtime == null)
            {
                throw new ArgumentNullException("runtime");
            }
            _profile = profile;
            _binary = binary;
            _profileRoot = Path.GetFullPath(profileRoot);
            _environmentReader = environmentReader;
            _runtime = runtime;
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
        private readonly IManagedChildRuntime _runtime;

        internal NativeControllerChildRuntime()
            : this(new NativeManagedChildRuntime())
        {
        }

        internal NativeControllerChildRuntime(IManagedChildRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException("runtime");
            _runtime = runtime;
        }

        public int Start(ControllerChildStartPlan plan)
        {
            ValidatePlan(plan);
            return _runtime.Start(new ManagedChildStartPlan(
                plan.FileName,
                plan.WorkingDirectory,
                new string[0],
                plan.Environment));
        }

        public bool SendGracefulStop(int processId)
        {
            return _runtime.SendGracefulStop(processId);
        }

        public bool WaitForExit(int processId, int milliseconds)
        {
            return _runtime.WaitForExit(processId, milliseconds);
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

    }
}
