using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class BulkRegistrationOutcome
    {
        public BulkRegistrationOutcome()
        {
            Results = new List<BulkKktRegistrationResult>();
        }

        public IList<BulkKktRegistrationResult> Results { get; private set; }
        public bool Cancelled { get; set; }
    }
}