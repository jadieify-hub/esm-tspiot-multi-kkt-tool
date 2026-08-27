namespace EsmTspiot.Shared.Models
{
    public enum LmProvisioningStage
    {
        Validating = 1,
        WaitingForElevation = 2,
        RunningHelper = 3,
        Reconciling = 4,
        Completed = 5
    }

    public sealed class LmProvisioningProgress
    {
        public string OperationId { get; set; }
        public string KktSerial { get; set; }
        public LmProvisioningStage Stage { get; set; }
        public LmServiceProvisioningStatus Status { get; set; }
        public string Message { get; set; }
    }
}
