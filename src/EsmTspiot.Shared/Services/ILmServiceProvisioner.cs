using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public interface ILmServiceProvisioner
    {
        Task<LmServiceProvisioningItemResult> RemoveAsync(
            LmRemovalConfirmation confirmation,
            string operationId,
            string planHash,
            CancellationToken cancellation);

        Task<LmServiceProvisioningBatchResult> RemoveAllAsync(
            IList<LmRemovalConfirmation> confirmations,
            string operationId,
            string planHash,
            CancellationToken cancellation);

        Task<LmServiceProvisioningItemResult> CleanupAsync(
            LmCleanupConfirmation confirmation,
            string operationId,
            string planHash,
            CancellationToken cancellation);
    }
}
