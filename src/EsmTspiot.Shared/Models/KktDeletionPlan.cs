using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class KktDeletionPlan
    {
        public KktDeletionPlan()
        {
            Candidates = new List<KktDeletionCandidate>();
        }

        public IList<KktDeletionCandidate> Candidates { get; private set; }
        public bool HasReliablePrimary { get; set; }
        public string BlockingReason { get; set; }
    }
}
