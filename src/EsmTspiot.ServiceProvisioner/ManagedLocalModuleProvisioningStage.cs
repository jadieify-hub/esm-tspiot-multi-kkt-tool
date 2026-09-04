
namespace EsmTspiot.ServiceProvisioner
{
    internal enum ManagedLocalModuleProvisioningStage
    {
        Created = 1,
        RuntimeReady = 2,
        ProfileReady = 3,
        DbReady = 4,
        ApiReady = 5,
        ControllerReady = 6,
        BindingPending = 7,
        Completed = 8,
        CleanupPending = 9,
        RequiresAttention = 10
    }
}
