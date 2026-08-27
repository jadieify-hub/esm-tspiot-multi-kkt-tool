namespace EsmTspiot.Shared.Models
{
    public enum LmGatewayPlanAction
    {
        Blocked = 0,
        CreateManagedService = 1,
        UpdateManagedService = 2,
        StartManagedService = 3,
        BindReadyService = 4,
        NoChange = 5
    }
}
