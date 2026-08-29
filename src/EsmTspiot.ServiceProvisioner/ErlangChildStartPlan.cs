using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EsmTspiot.ServiceProvisioner
{
    internal enum LocalModuleProcessRole
    {
        Database = 1,
        Api = 2
    }

    internal sealed class ErlangChildStartPlan
    {
        internal ErlangChildStartPlan(
            LocalModuleProcessRole role,
            string executablePath,
            string workingDirectory,
            IList<string> argumentTokens,
            IDictionary<string, string> environment)
        {
            if (argumentTokens == null) throw new ArgumentNullException("argumentTokens");
            if (environment == null) throw new ArgumentNullException("environment");
            Role = role;
            ExecutablePath = executablePath;
            WorkingDirectory = workingDirectory;
            ArgumentTokens = new List<string>(argumentTokens).AsReadOnly();
            Environment = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(
                    environment,
                    StringComparer.OrdinalIgnoreCase));
        }

        internal LocalModuleProcessRole Role { get; private set; }
        internal string ExecutablePath { get; private set; }
        internal string WorkingDirectory { get; private set; }
        internal IList<string> ArgumentTokens { get; private set; }
        internal IDictionary<string, string> Environment { get; private set; }
    }
}
