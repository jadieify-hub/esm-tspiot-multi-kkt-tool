using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class EpmdCommandPlan
    {
        internal EpmdCommandPlan(
            string executablePath,
            string workingDirectory,
            IList<string> argumentTokens,
            IDictionary<string, string> environment)
        {
            if (string.IsNullOrWhiteSpace(executablePath) ||
                !Path.IsPathRooted(executablePath))
            {
                throw new ArgumentException("An absolute epmd path is required.", "executablePath");
            }
            if (string.IsNullOrWhiteSpace(workingDirectory) ||
                !Path.IsPathRooted(workingDirectory))
            {
                throw new ArgumentException("An absolute working directory is required.", "workingDirectory");
            }
            if (argumentTokens == null) throw new ArgumentNullException("argumentTokens");
            if (environment == null) throw new ArgumentNullException("environment");
            ExecutablePath = Path.GetFullPath(executablePath);
            WorkingDirectory = Path.GetFullPath(workingDirectory);
            ArgumentTokens = new List<string>(argumentTokens).AsReadOnly();
            Environment = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(
                    environment,
                    StringComparer.OrdinalIgnoreCase));
        }

        internal string ExecutablePath { get; private set; }
        internal string WorkingDirectory { get; private set; }
        internal IList<string> ArgumentTokens { get; private set; }
        internal IDictionary<string, string> Environment { get; private set; }
    }

    internal sealed class EpmdCommandResult
    {
        internal EpmdCommandResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        internal int ExitCode { get; private set; }
        internal string StandardOutput { get; private set; }
        internal string StandardError { get; private set; }
    }

    internal interface IEpmdCommandRunner
    {
        EpmdCommandResult Execute(EpmdCommandPlan plan);
    }

    internal sealed class NativeEpmdCommandRunner : IEpmdCommandRunner
    {
        public EpmdCommandResult Execute(EpmdCommandPlan plan)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = plan.ExecutablePath,
                WorkingDirectory = plan.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = BuildArguments(plan.ArgumentTokens)
            };
            start.EnvironmentVariables.Clear();
            foreach (KeyValuePair<string, string> item in plan.Environment)
            {
                start.EnvironmentVariables.Add(item.Key, item.Value);
            }
            using (Process process = new Process())
            {
                StringBuilder output = new StringBuilder();
                StringBuilder error = new StringBuilder();
                process.StartInfo = start;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args.Data != null)
                    {
                        lock (output) output.AppendLine(args.Data);
                    }
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args.Data != null)
                    {
                        lock (error) error.AppendLine(args.Data);
                    }
                };
                if (!process.Start())
                {
                    throw new InvalidOperationException("EPMD process did not start.");
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(30000))
                {
                    throw new InvalidOperationException(
                        "EPMD command did not finish within the bounded timeout.");
                }
                process.WaitForExit();
                return new EpmdCommandResult(
                    process.ExitCode,
                    output.ToString(),
                    error.ToString());
            }
        }

        private static string BuildArguments(IList<string> tokens)
        {
            StringBuilder result = new StringBuilder();
            for (int index = 0; index < tokens.Count; index++)
            {
                if (index != 0) result.Append(' ');
                result.Append(WindowsCommandLine.QuoteArgument(tokens[index]));
            }
            return result.ToString();
        }
    }

    internal enum EpmdShutdownOutcome
    {
        Stopped = 1,
        AlreadyStopped = 2,
        LiveNodes = 3
    }

    internal sealed class EpmdShutdownResult
    {
        internal EpmdShutdownResult(
            EpmdShutdownOutcome outcome,
            IEnumerable<string> nodeNames)
        {
            Outcome = outcome;
            NodeNames = new List<string>(nodeNames ?? new string[0]).AsReadOnly();
        }

        internal EpmdShutdownOutcome Outcome { get; private set; }
        internal IList<string> NodeNames { get; private set; }
    }

    internal sealed class EpmdInstanceController
    {
        private readonly string _epmdPath;
        private readonly string _runtimeRoot;
        private readonly int _epmdPort;
        private readonly IEpmdCommandRunner _runner;
        private readonly IDictionary<string, string> _environment;

        internal EpmdInstanceController(
            LocalModuleCapabilityProfile capability,
            string runtimeRoot,
            string tempRoot,
            int epmdPort,
            IEpmdCommandRunner runner)
        {
            if (capability == null) throw new ArgumentNullException("capability");
            if (string.IsNullOrWhiteSpace(runtimeRoot) ||
                !Path.IsPathRooted(runtimeRoot))
            {
                throw new ArgumentException("An absolute runtime root is required.", "runtimeRoot");
            }
            if (epmdPort < 1 || epmdPort > 65535)
            {
                throw new ArgumentOutOfRangeException("epmdPort");
            }
            if (runner == null) throw new ArgumentNullException("runner");
            _runtimeRoot = Path.GetFullPath(runtimeRoot);
            _epmdPath = Path.Combine(
                _runtimeRoot,
                capability.EpmdExecutableRelativePath);
            _epmdPort = epmdPort;
            _runner = runner;
            _environment = new ReadOnlyDictionary<string, string>(
                LocalModuleConfigurationWriter.BuildProcessEnvironment(
                    _runtimeRoot,
                    tempRoot,
                    capability,
                    epmdPort));
        }

        internal EpmdShutdownResult StopIfUnused()
        {
            EpmdCommandResult names = _runner.Execute(BuildPlan("-names"));
            if (names.ExitCode != 0)
            {
                if (IsNotRunning(names))
                {
                    return new EpmdShutdownResult(
                        EpmdShutdownOutcome.AlreadyStopped,
                        new string[0]);
                }
                throw new InvalidDataException(
                    "Instance-specific EPMD name query failed.");
            }
            IList<string> nodes = ParseNodeNames(names.StandardOutput);
            if (nodes.Count != 0)
            {
                return new EpmdShutdownResult(
                    EpmdShutdownOutcome.LiveNodes,
                    nodes);
            }

            EpmdCommandResult stop = _runner.Execute(BuildPlan("-kill"));
            if (stop.ExitCode != 0 && !IsNotRunning(stop))
            {
                throw new InvalidDataException(
                    "Instance-specific EPMD graceful shutdown failed.");
            }
            return new EpmdShutdownResult(
                stop.ExitCode == 0
                    ? EpmdShutdownOutcome.Stopped
                    : EpmdShutdownOutcome.AlreadyStopped,
                new string[0]);
        }

        private EpmdCommandPlan BuildPlan(string operation)
        {
            if (!string.Equals(operation, "-names", StringComparison.Ordinal) &&
                !string.Equals(operation, "-kill", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unsupported EPMD operation.");
            }
            return new EpmdCommandPlan(
                _epmdPath,
                _runtimeRoot,
                new[]
                {
                    operation,
                    "-port",
                    _epmdPort.ToString(CultureInfo.InvariantCulture)
                },
                _environment);
        }

        private static IList<string> ParseNodeNames(string output)
        {
            List<string> result = new List<string>();
            string[] lines = (output ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (!line.StartsWith("name ", StringComparison.Ordinal)) continue;
                int separator = line.IndexOf(" at port ", StringComparison.Ordinal);
                if (separator <= 5)
                {
                    throw new InvalidDataException("EPMD returned an ambiguous node line.");
                }
                string name = line.Substring(5, separator - 5);
                if (name.Length == 0 || name.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
                {
                    throw new InvalidDataException("EPMD returned an invalid node name.");
                }
                result.Add(name);
            }
            return result.AsReadOnly();
        }

        private static bool IsNotRunning(EpmdCommandResult result)
        {
            string text = (result.StandardOutput + "\n" + result.StandardError)
                .ToLowerInvariant();
            return text.IndexOf("cannot connect", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("not running", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("no epmd", StringComparison.Ordinal) >= 0;
        }
    }
}
