using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public interface ILmGatewayProbe
    {
        Task<LmGatewayProbeResult> ProbeAsync(
            ManagedLmServiceSpec service,
            CancellationToken cancellation);
    }
}
