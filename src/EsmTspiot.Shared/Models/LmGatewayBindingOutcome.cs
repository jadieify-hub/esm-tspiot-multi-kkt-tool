using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayBindingOutcome
    {
        public LmGatewayBindingOutcome()
        {
            Results = new List<LmGatewayBindingResult>();
        }

        public IList<LmGatewayBindingResult> Results { get; private set; }
        public bool Cancelled { get; set; }
    }
}
