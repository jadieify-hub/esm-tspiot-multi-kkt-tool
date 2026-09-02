using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal static class LmGatewayCredentialDefaults
    {
        internal static LmGatewayCredentials Create()
        {
            return new LmGatewayCredentials
            {
                Login = SupportedLocalModulePackageIdentity.ApiLogin,
                Password = SupportedLocalModulePackageIdentity.ApiPassword
            };
        }
    }
}
