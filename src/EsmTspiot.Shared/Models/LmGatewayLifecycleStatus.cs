namespace EsmTspiot.Shared.Models
{
    public enum LmGatewayLifecycleStatus
    {
        Pending = 0,
        Blocked = 1,
        Cancelled = 2,
        ServiceFailed = 3,
        ServiceReady = 4,
        BindingAccepted = 5,
        BindingFailed = 6,
        RequiresAttention = 7,
        CleanupPending = 8,
        RemovedLocalArtifactsBindingRetained = 9
    }
}
