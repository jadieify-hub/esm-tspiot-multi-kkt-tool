using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayDiscovery
    {
        public LmGatewayDiscovery()
        {
            Items = new List<LmGatewayKkt>();
            Issues = new List<LmGatewayDiscoveryIssue>();
        }

        public IList<LmGatewayKkt> Items { get; private set; }
        public IList<LmGatewayDiscoveryIssue> Issues { get; private set; }
        public string ErrorMessage { get; set; }

        public bool IsSuccessful
        {
            get { return string.IsNullOrWhiteSpace(ErrorMessage); }
        }
    }
}
