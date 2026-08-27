using System;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class ProvisionerCommandLine
    {
        internal string PipeName { get; private set; }
        internal string OperationId { get; private set; }

        internal static bool TryParse(string[] args, out ProvisionerCommandLine result)
        {
            result = null;
            if (args == null || args.Length != 4 ||
                !string.Equals(args[0], "--pipe", StringComparison.Ordinal) ||
                !string.Equals(args[2], "--operation", StringComparison.Ordinal) ||
                !IsGuidN(args[1]) ||
                !IsGuidN(args[3]))
            {
                return false;
            }

            result = new ProvisionerCommandLine
            {
                PipeName = args[1].ToLowerInvariant(),
                OperationId = args[3].ToLowerInvariant()
            };
            return true;
        }

        internal static bool IsGuidN(string value)
        {
            Guid parsed;
            return value != null && value.Length == 32 &&
                Guid.TryParseExact(value, "N", out parsed);
        }
    }
}
