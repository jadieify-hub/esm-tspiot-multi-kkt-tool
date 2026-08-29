namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayReadbackObservation
    {
        public string InstanceId { get; set; }
        public string KktSerial { get; set; }
        public string KktInn { get; set; }
        public bool IsAvailable { get; set; }
        public bool IdentityMatches { get; set; }
        public bool HasLmConfiguration { get; set; }
        public bool? EndpointMatches { get; set; }
        public string LmAddress { get; set; }
        public string LmPort { get; set; }
        public string LmStatus { get; set; }
        public string LmVersion { get; set; }
        public string Details { get; set; }

        public bool IsVerified
        {
            get
            {
                return IsAvailable && IdentityMatches && HasLmConfiguration &&
                    EndpointMatches.HasValue && EndpointMatches.Value;
            }
        }
    }
}
