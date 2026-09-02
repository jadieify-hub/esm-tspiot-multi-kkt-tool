using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LocalModuleMsiProvisionerClient
    {
        private const int SchemaVersion = 3;
        private readonly ProvisionerProcessLauncher _launcher;

        internal LocalModuleMsiProvisionerClient()
            : this(new ProvisionerProcessLauncher())
        {
        }

        internal LocalModuleMsiProvisionerClient(
            ProvisionerProcessLauncher launcher)
        {
            if (launcher == null) throw new ArgumentNullException("launcher");
            _launcher = launcher;
        }

        internal async Task<LmServiceProvisioningBatchResult> RunEnsureAsync(
            LmServiceProvisioningBatchRequest request,
            CancellationToken cancellation)
        {
            ValidateEnsureRequest(request);
            cancellation.ThrowIfCancellationRequested();
            string pipeName = Guid.NewGuid().ToString("N");
            using (NamedPipeServerStream pipe =
                LmServiceProvisionerClient.CreateServer(pipeName))
            using (Process helper = _launcher.Launch(
                pipeName,
                request.OperationId))
            {
                try
                {
                    await LmServiceProvisionerClient.WaitForConnectionAsync(
                        pipe, cancellation);
                    _launcher.AuthenticateConnectedHelper(
                        pipe.SafePipeHandle, helper.Id);
                    object gate = new object();
                    LmServiceProvisionerClient.WriteMessage(
                        pipe, request, gate);
                    long sequence = 0;
                    ManagedProvisioningSessionMessage ready =
                        await ReadSessionMessageAsync(pipe);
                    ValidateMessage(request, ready, ref sequence,
                        ManagedProvisioningSessionKind.SessionReady, -1);

                    for (int index = 0;
                        index < request.LocalModuleMsiItems.Count;
                        index++)
                    {
                        if (cancellation.IsCancellationRequested) break;
                        LmServiceProvisionerClient.WriteMessage(
                            pipe,
                            CreateMessage(request, ++sequence,
                                ManagedProvisioningSessionKind.ExecuteItem,
                                index,
                                LmServiceProvisioningStatus.Pending),
                            gate);
                        ManagedProvisioningSessionMessage item =
                            await ReadSessionMessageAsync(pipe);
                        ValidateMessage(request, item, ref sequence,
                            ManagedProvisioningSessionKind.ItemResult,
                            index);
                    }

                    LmServiceProvisionerClient.WriteMessage(
                        pipe,
                        CreateMessage(request, ++sequence,
                            cancellation.IsCancellationRequested
                                ? ManagedProvisioningSessionKind
                                    .CancelAfterCurrentItem
                                : ManagedProvisioningSessionKind.Finish,
                            -1,
                            cancellation.IsCancellationRequested
                                ? LmServiceProvisioningStatus.Cancelled
                                : LmServiceProvisioningStatus.Pending),
                        gate);
                    LmServiceProvisioningBatchResult result =
                        await Task.Run(delegate
                        {
                            return LmServiceProvisionerClient.ReadMessage<
                                LmServiceProvisioningBatchResult>(pipe);
                        });
                    ValidateResult(request, result);
                    if (cancellation.IsCancellationRequested)
                        throw new OperationCanceledException(cancellation);
                    return result;
                }
                catch (IOException exception)
                {
                    throw LmServiceProvisionerClient
                        .CreatePrematureExitException(helper, exception);
                }
            }
        }

        internal async Task<LmServiceProvisioningBatchResult> RunAsync(
            LmServiceProvisioningBatchRequest request,
            CancellationToken cancellation)
        {
            if (request == null || request.Operation ==
                LmServiceOperation.EnsureMsiLocalModules)
                return await RunEnsureAsync(request, cancellation);
            ValidateCommon(request);
            return await new LmServiceProvisionerClient(_launcher)
                .ExecuteRawBatchAsync(request, cancellation);
        }

        private static Task<ManagedProvisioningSessionMessage>
            ReadSessionMessageAsync(Stream stream)
        {
            return Task.Run(delegate
            {
                return LmServiceProvisionerClient.ReadMessage<
                    ManagedProvisioningSessionMessage>(stream);
            });
        }

        private static void ValidateEnsureRequest(
            LmServiceProvisioningBatchRequest request)
        {
            ValidateCommon(request);
            if (request.Operation != LmServiceOperation.EnsureMsiLocalModules ||
                request.LocalModuleMsiItems == null ||
                request.LocalModuleMsiItems.Count == 0)
                throw new InvalidDataException(
                    "Требуется непустой план установки MSI ЛМ схемы v3.");
        }

        private static void ValidateCommon(
            LmServiceProvisioningBatchRequest request)
        {
            if (request == null || request.SchemaVersion != SchemaVersion ||
                !CanonicalLmPlanHasher.FixedTimeEqualsHex(
                    request.PlanHash,
                    CanonicalLmPlanHasher.Compute(request)))
                throw new InvalidDataException(
                    "План MSI ЛМ изменился до запуска helper.");
        }

        private static void ValidateMessage(
            LmServiceProvisioningBatchRequest request,
            ManagedProvisioningSessionMessage message,
            ref long sequence,
            ManagedProvisioningSessionKind kind,
            int itemIndex)
        {
            if (message == null || message.SchemaVersion != SchemaVersion ||
                !string.Equals(message.OperationId, request.OperationId,
                    StringComparison.OrdinalIgnoreCase) ||
                message.Sequence != sequence + 1 || message.Kind != kind ||
                message.ItemIndex != itemIndex)
                throw new InvalidDataException(
                    "Helper нарушил последовательный протокол MSI ЛМ.");
            sequence = message.Sequence;
        }

        private static void ValidateResult(
            LmServiceProvisioningBatchRequest request,
            LmServiceProvisioningBatchResult result)
        {
            LmServiceProvisionerClient.ValidateResult(request, result);
            if (result.LocalModuleMsiItems == null ||
                result.LocalModuleMsiItems.Count !=
                    request.LocalModuleMsiItems.Count)
                throw new InvalidDataException(
                    "Helper вернул неполный итог MSI ЛМ.");
            for (int index = 0;
                index < result.LocalModuleMsiItems.Count;
                index++)
            {
                LocalModuleMsiProvisioningItemRequest expected =
                    request.LocalModuleMsiItems[index];
                LocalModuleMsiProvisioningItemResult actual =
                    result.LocalModuleMsiItems[index];
                if (actual == null || !string.Equals(actual.Inn, expected.Inn,
                        StringComparison.Ordinal) ||
                    actual.CloneOrdinal != expected.CloneOrdinal ||
                    actual.ApiPort != expected.ApiPort)
                    throw new InvalidDataException(
                        "Helper изменил порядок или назначение MSI ЛМ.");
            }
        }

        private static ManagedProvisioningSessionMessage CreateMessage(
            LmServiceProvisioningBatchRequest request,
            long sequence,
            ManagedProvisioningSessionKind kind,
            int itemIndex,
            LmServiceProvisioningStatus status)
        {
            return new ManagedProvisioningSessionMessage
            {
                SchemaVersion = SchemaVersion,
                OperationId = request.OperationId,
                Sequence = sequence,
                Kind = kind,
                ItemIndex = itemIndex,
                Status = status,
                Message = string.Empty
            };
        }
    }
}
