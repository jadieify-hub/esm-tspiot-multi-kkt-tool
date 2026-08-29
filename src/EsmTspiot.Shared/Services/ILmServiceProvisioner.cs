using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public interface ILmServiceProvisioner
    {
        Task<LmControllerInstallResult> InstallControllerVersionAsync(
            LmControllerInstallerSelection selection,
            string operationId,
            string planHash,
            CancellationToken cancellation);

        Task<LmServiceProvisioningBatchResult> EnsureBatchAsync(
            IList<LmServiceProvisioningItemRequest> items,
            string operationId,
            string planHash,
            CancellationToken cancellation);

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
