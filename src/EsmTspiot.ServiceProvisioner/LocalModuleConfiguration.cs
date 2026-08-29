namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleTemplateObservation
    {
        internal string RegimeLocalIni { get; set; }
        internal string YeniseiLocalIni { get; set; }
    }

    internal sealed class LocalModuleConfiguration
    {
        internal string ProfileRoot { get; set; }
        internal string ConfigRoot { get; set; }
        internal string DataRoot { get; set; }
        internal string LogsRoot { get; set; }

        internal string RegimeLocalIniPath { get; set; }
        internal string RegimeLocalIni { get; set; }
        internal string YeniseiLocalIniPath { get; set; }
        internal string YeniseiLocalIni { get; set; }

        internal string RegimeVmArgsPath { get; set; }
        internal string RegimeVmArgs { get; set; }
        internal string YeniseiVmArgsPath { get; set; }
        internal string YeniseiVmArgs { get; set; }

        internal string RegimeSysConfigPath { get; set; }
        internal string RegimeSysConfig { get; set; }
        internal string YeniseiSysConfigPath { get; set; }
        internal string YeniseiSysConfig { get; set; }

        internal ErlangChildStartPlan DatabaseStartPlan { get; set; }
        internal ErlangChildStartPlan ApiStartPlan { get; set; }

        internal string AllText
        {
            get
            {
                return (RegimeLocalIni ?? string.Empty) + "\n" +
                    (YeniseiLocalIni ?? string.Empty) + "\n" +
                    (RegimeVmArgs ?? string.Empty) + "\n" +
                    (YeniseiVmArgs ?? string.Empty) + "\n" +
                    (RegimeSysConfig ?? string.Empty) + "\n" +
                    (YeniseiSysConfig ?? string.Empty);
            }
        }
    }
}
