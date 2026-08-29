using System;
using System.Threading;
using System.Threading.Tasks;

namespace EsmTspiot.Shared.Services
{
    public sealed class AutomaticConfigurationCoordinator
    {
        public async Task<bool> ExecuteAsync(
            Func<CancellationToken, Task<bool>> registerKkts,
            Func<CancellationToken, Task<bool>> configureControllers,
            CancellationToken cancellationToken)
        {
            if (registerKkts == null)
            {
                throw new ArgumentNullException("registerKkts");
            }
            if (configureControllers == null)
            {
                throw new ArgumentNullException("configureControllers");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!await registerKkts(cancellationToken))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return await configureControllers(cancellationToken);
        }
    }
}
