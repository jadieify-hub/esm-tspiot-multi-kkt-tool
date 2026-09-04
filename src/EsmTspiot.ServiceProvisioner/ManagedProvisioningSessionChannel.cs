using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IManagedProvisioningSessionChannel
    {
        ManagedProvisioningSessionMessage ReadSessionMessage();

        void WriteSessionMessage(ManagedProvisioningSessionMessage message);
    }
}
