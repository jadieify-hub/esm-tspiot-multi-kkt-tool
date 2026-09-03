using System;
using System.IO;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleFirewallManager
    {
        private readonly IWindowsFirewallApi _firewall;

        internal LocalModuleFirewallManager(IWindowsFirewallApi firewall)
        {
            if (firewall == null) throw new ArgumentNullException("firewall");
            _firewall = firewall;
        }

        internal LocalModuleFirewallRule EnsureApiRule(
            LocalModuleInstalledLayout layout,
            string ownershipId,
            string remoteAddress)
        {
            if (layout == null) throw new ArgumentNullException("layout");
            if (!layout.ErtsDiscovered)
                throw new InvalidDataException(
                    "Каталог рантайма erts-* локального модуля не найден; " +
                    "правило сети не выписывается вслепую.");
            LocalModuleFirewallRule expected = LocalModuleFirewallRule.Create(
                ownershipId,
                Path.Combine(layout.ErtsBinPath, "erl.exe"),
                layout.ApiPort,
                remoteAddress);
            WindowsFirewallRuleRecord existing =
                _firewall.FindByName(expected.RuleName);
            if (existing != null)
            {
                if (!existing.Matches(expected))
                    throw new InvalidDataException(
                        "A firewall rule with the owned name has foreign fields.");
                return expected;
            }
            _firewall.Add(expected);
            WindowsFirewallRuleRecord created =
                _firewall.FindByName(expected.RuleName);
            if (created == null || !created.Matches(expected))
                throw new InvalidDataException(
                    "The local-module firewall rule failed exact read-back.");
            return expected;
        }

        internal void Remove(LocalModuleFirewallRule expected)
        {
            if (expected == null) throw new ArgumentNullException("expected");
            Remove(expected.RuleName, expected.ExpectedFieldHash);
        }

        internal void Remove(string ruleName, string expectedFieldHash)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
                throw new ArgumentException(
                    "Firewall rule name is required.",
                    "ruleName");
            if (!LocalModuleManagedIdentity.IsLowerHex(
                    expectedFieldHash,
                    64))
                throw new ArgumentException(
                    "Firewall expected-field hash is invalid.",
                    "expectedFieldHash");
            WindowsFirewallRuleRecord existing =
                _firewall.FindByName(ruleName);
            if (existing == null) return;
            if (!string.Equals(
                    existing.ComputeFieldHash(),
                    expectedFieldHash,
                    StringComparison.Ordinal))
                throw new InvalidDataException(
                    "A firewall rule with the owned name has foreign fields.");
            _firewall.Remove(ruleName);
            if (_firewall.FindByName(ruleName) != null)
                throw new InvalidOperationException(
                    "The owned local-module firewall rule was not removed.");
        }
    }
}
