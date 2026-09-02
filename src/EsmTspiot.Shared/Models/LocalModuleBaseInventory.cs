namespace EsmTspiot.Shared.Models
{
    public sealed class LocalModuleBaseInventory
    {
        public bool IsInstalled { get; set; }
        public string AssignedInn { get; set; }
        public string InstallDirectory { get; set; }
        public int ApiPort { get; set; }
        public int DatabasePort { get; set; }
        public bool WasInstalledByApplication { get; set; }
    }
}
