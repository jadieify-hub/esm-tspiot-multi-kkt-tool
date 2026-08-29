using System;
using System.Threading;
using System.Threading.Tasks;

namespace EsmTspiot.Shared.Services
{
    public sealed class LmAutomaticSetupCoordinator
    {
        public async Task<bool> ExecuteAsync(
            Func<CancellationToken, Task<bool>> install,
            Func<CancellationToken, Task<bool>> configure,
            CancellationToken cancellation)
        {
            if (install == null)
            {
                throw new ArgumentNullException("install");
            }
            if (configure == null)
            {
                throw new ArgumentNullException("configure");
            }

            cancellation.ThrowIfCancellationRequested();
            if (!await install(cancellation))
            {
                return false;
            }

            cancellation.ThrowIfCancellationRequested();
            return await configure(cancellation);
        }
    }
}
