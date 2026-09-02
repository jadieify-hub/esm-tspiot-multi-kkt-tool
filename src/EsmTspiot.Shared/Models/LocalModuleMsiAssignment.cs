namespace EsmTspiot.Shared.Models
{
    public sealed class LocalModuleMsiAssignment
    {
        public string Inn { get; set; }
        public int CloneOrdinal { get; set; }
        public int ApiPort { get; set; }
        public int DatabasePort { get; set; }
        public string InstallVolumeRoot { get; set; }
        public bool BaseWasPreExisting { get; set; }
    }
}
