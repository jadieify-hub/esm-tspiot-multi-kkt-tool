using System;
using System.Globalization;
using System.IO;
using System.Net;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleFirewallRule
    {
        private const string NamePrefix = "KRS MultiKKT LM ";

        internal string RuleName { get; private set; }
        internal string OwnershipId { get; private set; }
        internal string ProgramPath { get; private set; }
        internal int LocalPort { get; private set; }
        internal string RemoteAddress { get; private set; }
        internal string ExpectedFieldHash { get; private set; }

        internal static LocalModuleFirewallRule Create(
            string ownershipId,
            string programPath,
            int localPort,
            string remoteAddress)
        {
            if (!LocalModuleManagedIdentity.IsLowerHex(ownershipId, 32))
                throw new ArgumentException(
                    "Firewall ownership identity is invalid.",
                    "ownershipId");
            if (string.IsNullOrWhiteSpace(programPath) ||
                !Path.IsPathRooted(programPath) ||
                !string.Equals(
                    Path.GetFileName(programPath),
                    "erl.exe",
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "Firewall program path is invalid.",
                    "programPath");
            if (localPort < 1 || localPort > 65535)
                throw new ArgumentOutOfRangeException("localPort");
            string normalizedRemote = NormalizeRemoteAddress(remoteAddress);
            LocalModuleFirewallRule result = new LocalModuleFirewallRule
            {
                RuleName = RuleNameFor(ownershipId),
                OwnershipId = ownershipId,
                ProgramPath = Path.GetFullPath(programPath),
                LocalPort = localPort,
                RemoteAddress = normalizedRemote
            };
            result.ExpectedFieldHash = ComputeExpectedFieldHash(result);
            return result;
        }

        internal static string RuleNameFor(string ownershipId)
        {
            if (!LocalModuleManagedIdentity.IsLowerHex(ownershipId, 32))
                throw new ArgumentException(
                    "Firewall ownership identity is invalid.",
                    "ownershipId");
            return NamePrefix + ownershipId;
        }

        internal static string ComputeExpectedFieldHash(
            LocalModuleFirewallRule rule)
        {
            if (rule == null) throw new ArgumentNullException("rule");
            return WindowsFirewallRuleRecord.FromExpected(rule)
                .ComputeFieldHash();
        }

        private static string NormalizeRemoteAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "LocalSubnet";
            string candidate = value.Trim();
            if (string.Equals(
                    candidate,
                    "LocalSubnet",
                    StringComparison.Ordinal))
                return candidate;
            if (candidate.IndexOf(',') >= 0 || candidate.IndexOf('-') >= 0)
                throw new ArgumentException(
                    "Firewall remote address must be one IP or CIDR.",
                    "remoteAddress");
            string addressText = candidate;
            int? prefix = null;
            int slash = candidate.LastIndexOf('/');
            if (slash >= 0)
            {
                int parsedPrefix;
                addressText = candidate.Substring(0, slash);
                if (!int.TryParse(
                        candidate.Substring(slash + 1),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out parsedPrefix))
                    throw new ArgumentException(
                        "Firewall CIDR prefix is invalid.",
                        "remoteAddress");
                prefix = parsedPrefix;
            }
            IPAddress address;
            if (!IPAddress.TryParse(addressText, out address))
                throw new ArgumentException(
                    "Firewall remote address is invalid.",
                    "remoteAddress");
            int maximumPrefix = address.AddressFamily ==
                System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            if (prefix.HasValue &&
                (prefix.Value < 0 || prefix.Value > maximumPrefix))
                throw new ArgumentException(
                    "Firewall CIDR prefix is invalid.",
                    "remoteAddress");
            return address.ToString() +
                (prefix.HasValue ? "/" + prefix.Value.ToString(
                    CultureInfo.InvariantCulture) : string.Empty);
        }
    }

    internal sealed class WindowsFirewallRuleRecord
    {
        internal string RuleName { get; set; }
        internal string Grouping { get; set; }
        internal string ProgramPath { get; set; }
        internal int LocalPort { get; set; }
        internal string RemoteAddress { get; set; }
        internal bool Enabled { get; set; }
        internal bool Inbound { get; set; }
        internal bool Allow { get; set; }
        internal bool Tcp { get; set; }
        internal bool Domain { get; set; }
        internal bool Private { get; set; }
        internal bool Public { get; set; }

        internal bool Matches(LocalModuleFirewallRule expected)
        {
            return expected != null &&
                string.Equals(RuleName, expected.RuleName,
                    StringComparison.Ordinal) &&
                string.Equals(Grouping, expected.RuleName,
                    StringComparison.Ordinal) &&
                PathsEqual(ProgramPath, expected.ProgramPath) &&
                LocalPort == expected.LocalPort &&
                string.Equals(RemoteAddress, expected.RemoteAddress,
                    StringComparison.Ordinal) &&
                Enabled && Inbound && Allow && Tcp &&
                Domain && Private && !Public;
        }

        internal WindowsFirewallRuleRecord Clone()
        {
            return (WindowsFirewallRuleRecord)MemberwiseClone();
        }

        internal string ComputeFieldHash()
        {
            string canonical = string.Join("\n", new[]
            {
                RuleName ?? string.Empty,
                Grouping ?? string.Empty,
                ProgramPath ?? string.Empty,
                LocalPort.ToString(CultureInfo.InvariantCulture),
                RemoteAddress ?? string.Empty,
                Tcp ? "TCP" : "NotTCP",
                Inbound ? "Inbound" : "NotInbound",
                Allow ? "Allow" : "NotAllow",
                Domain && Private && !Public
                    ? "Domain|Private"
                    : "OtherProfiles",
                Enabled ? "Enabled" : "Disabled"
            });
            byte[] digest;
            using (System.Security.Cryptography.SHA256 algorithm =
                System.Security.Cryptography.SHA256.Create())
                digest = algorithm.ComputeHash(
                    new System.Text.UTF8Encoding(false, true)
                        .GetBytes(canonical));
            System.Text.StringBuilder value =
                new System.Text.StringBuilder(64);
            for (int index = 0; index < digest.Length; index++)
                value.Append(digest[index].ToString("x2"));
            return value.ToString();
        }

        internal static WindowsFirewallRuleRecord FromExpected(
            LocalModuleFirewallRule expected)
        {
            if (expected == null) throw new ArgumentNullException("expected");
            return new WindowsFirewallRuleRecord
            {
                RuleName = expected.RuleName,
                Grouping = expected.RuleName,
                ProgramPath = expected.ProgramPath,
                LocalPort = expected.LocalPort,
                RemoteAddress = expected.RemoteAddress,
                Enabled = true,
                Inbound = true,
                Allow = true,
                Tcp = true,
                Domain = true,
                Private = true,
                Public = false
            };
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(left) &&
                    string.Equals(
                        Path.GetFullPath(left),
                        Path.GetFullPath(right),
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
    }
}
