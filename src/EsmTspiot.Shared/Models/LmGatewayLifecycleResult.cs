namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayLifecycleResult
    {
        public string InstanceId { get; set; }
        public string KktSerial { get; set; }
        public string KktInn { get; set; }
        public string ServiceName { get; set; }
        public LmGatewayLifecycleStatus Status { get; set; }
        public LmServiceProvisioningStatus ServiceStatus { get; set; }
        public LmGatewayBindingStatus BindingStatus { get; set; }
        public bool BindingAttempted { get; set; }
        public string Details { get; set; }
    }
}
