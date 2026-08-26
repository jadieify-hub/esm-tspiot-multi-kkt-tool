namespace EsmTspiot.Shared.Models
{
    public sealed class KktDeletionCandidate
    {
        public KktInstanceInfo Instance { get; set; }
        public bool IsPrimary { get; set; }
        public bool CanDelete { get; set; }
        public string ProtectionReason { get; set; }
    }
}
