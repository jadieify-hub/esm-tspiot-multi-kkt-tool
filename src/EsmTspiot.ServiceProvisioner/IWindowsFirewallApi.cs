namespace EsmTspiot.ServiceProvisioner
{
    internal interface IWindowsFirewallApi
    {
        WindowsFirewallRuleRecord FindByName(string ruleName);
        void Add(LocalModuleFirewallRule rule);
        void Remove(string ruleName);
    }
}
