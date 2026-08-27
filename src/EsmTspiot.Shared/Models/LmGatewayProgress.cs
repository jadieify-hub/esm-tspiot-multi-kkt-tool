namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string InstanceId { get; set; }
        public string Stage { get; set; }
        public string Message { get; set; }
    }
}
