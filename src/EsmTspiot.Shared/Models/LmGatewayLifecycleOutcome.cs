using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayLifecycleOutcome
    {
        public LmGatewayLifecycleOutcome()
        {
            Results = new List<LmGatewayLifecycleResult>();
        }

        public string OperationId { get; set; }
        public string PlanHash { get; set; }
        public bool Cancelled { get; set; }
        public bool ReconciliationRequired { get; set; }
        public IList<LmGatewayLifecycleResult> Results { get; private set; }
    }
}
