using EsmTspiot.Shared.Models;

namespace EsmTspiot.WinForms.Shared
{
    internal static class LmGatewayCredentialDefaults
    {
        internal static LmGatewayCredentials Create()
        {
            return new LmGatewayCredentials
            {
                Login = "admin",
                Password = "admin"
            };
        }
    }
}
