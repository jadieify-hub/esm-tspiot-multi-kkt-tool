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
    internal sealed class CompleteStackProvisionerClient
    {
        private const int SchemaVersion = 1;
        private readonly ProvisionerProcessLauncher _launcher;

        internal CompleteStackProvisionerClient()
            : this(new ProvisionerProcessLauncher())
        {
        }

        internal CompleteStackProvisionerClient(
            ProvisionerProcessLauncher launcher)
        {
            if (launcher == null) throw new ArgumentNullException("launcher");
            _launcher = launcher;
        }

        internal async Task<LmServiceProvisioningBatchResult> RunAsync(
            LmServiceProvisioningBatchRequest request,
            Func<int, CancellationToken, Task<bool>> prepareItem,
            Func<
                int,
                LmServiceProvisioningItemResult,
                CancellationToken,
                Task<bool>> finalizeItem,
            CancellationToken cancellation)
        {
            ValidateRequest(request);
            if (prepareItem == null) throw new ArgumentNullException("prepareItem");
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
                        pipe,
                        cancellation);
                    _launcher.AuthenticateConnectedHelper(
                        pipe.SafePipeHandle,
                        helper.Id);
                    object writeGate = new object();
                    LmServiceProvisionerClient.WriteMessage(
                        pipe,
                        request,
                        writeGate);

                    long sequence = 0;
                    ManagedProvisioningSessionMessage ready =
                        await ReadSessionMessageAsync(pipe);
                    ValidateSessionMessage(
                        request,
                        ready,
                        ref sequence,
                        ManagedProvisioningSessionKind.SessionReady,
                        -1);

                    bool stop = false;
                    for (int index = 0;
                        index < request.ManagedLocalModules.Count && !stop;
                        index++)
                    {
                        if (cancellation.IsCancellationRequested)
                        {
                            break;
                        }

                        bool execute = await prepareItem(index, cancellation);
                        ManagedProvisioningSessionMessage command =
                            CreateSessionMessage(
                                request,
                                ++sequence,
                                ManagedProvisioningSessionKind.ExecuteItem,
                                index,
                                execute
                                    ? LmServiceProvisioningStatus.Pending
                                    : LmServiceProvisioningStatus.Cancelled,
                                execute
                                    ? string.Empty
                                    : "ККТ не зарегистрирована в ЕСМ; локальные компоненты не изменялись.");
                        LmServiceProvisionerClient.WriteMessage(
                            pipe,
                            command,
                            writeGate);

                        ManagedProvisioningSessionMessage itemResult =
                            await ReadSessionMessageAsync(pipe);
                        ValidateSessionMessage(
                            request,
                            itemResult,
                            ref sequence,
                            ManagedProvisioningSessionKind.ItemResult,
                            index);
                        ValidateItemIdentity(request, index, itemResult);

                        bool readyForFinalization = IsReady(itemResult.Status);
                        bool finalized = true;
                        if (readyForFinalization && finalizeItem != null)
                        {
                            finalized = await finalizeItem(
                                index,
                                ToItemResult(request, index, itemResult),
                                cancellation);
                        }
                        if (index == 0 && (!execute ||
                            !readyForFinalization || !finalized))
                        {
                            stop = true;
                        }
                        if (itemResult.Status ==
                                LmServiceProvisioningStatus.UnsupportedController ||
                            itemResult.Status ==
                                LmServiceProvisioningStatus.VersionVerificationPending)
                        {
                            stop = true;
                        }
                    }

                    ManagedProvisioningSessionKind finishKind =
                        cancellation.IsCancellationRequested
                            ? ManagedProvisioningSessionKind.CancelAfterCurrentItem
                            : ManagedProvisioningSessionKind.Finish;
                    LmServiceProvisionerClient.WriteMessage(
                        pipe,
                        CreateSessionMessage(
                            request,
                            ++sequence,
                            finishKind,
                            -1,
                            cancellation.IsCancellationRequested
                                ? LmServiceProvisioningStatus.Cancelled
                                : LmServiceProvisioningStatus.Pending,
                            string.Empty),
                        writeGate);
                    LmServiceProvisioningBatchResult result =
                        await Task.Run(delegate
                        {
                            return LmServiceProvisionerClient
                                .ReadMessage<LmServiceProvisioningBatchResult>(pipe);
                        });
                    LmServiceProvisionerClient.ValidateResult(request, result);
                    ValidateBatchItems(request, result);
                    if (cancellation.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellation);
                    }
                    return result;
                }
                catch (IOException ex)
                {
                    throw LmServiceProvisionerClient
                        .CreatePrematureExitException(helper, ex);
                }
            }
        }

        private static Task<ManagedProvisioningSessionMessage>
            ReadSessionMessageAsync(Stream pipe)
        {
            return Task.Run(delegate
            {
                return LmServiceProvisionerClient
                    .ReadMessage<ManagedProvisioningSessionMessage>(pipe);
            });
        }

        private static void ValidateRequest(
            LmServiceProvisioningBatchRequest request)
        {
            if (request == null ||
                request.Operation !=
                    LmServiceOperation.EnsureManagedLocalModules ||
                request.ManagedLocalModules == null ||
                request.ManagedLocalModules.Count == 0 ||
                !CanonicalLmPlanHasher.FixedTimeEqualsHex(
                    request.PlanHash,
                    CanonicalLmPlanHasher.Compute(request)))
            {
                throw new InvalidDataException(
                    "Полный план ККТ, контроллеров и ЛМ изменился до запуска.");
            }
        }

        private static void ValidateSessionMessage(
            LmServiceProvisioningBatchRequest request,
            ManagedProvisioningSessionMessage message,
            ref long sequence,
            ManagedProvisioningSessionKind expectedKind,
            int expectedIndex)
        {
            if (message == null ||
                message.SchemaVersion != SchemaVersion ||
                !string.Equals(
                    message.OperationId,
                    request.OperationId,
                    StringComparison.OrdinalIgnoreCase) ||
                message.Sequence != sequence + 1 ||
                message.Kind != expectedKind ||
                message.ItemIndex != expectedIndex)
            {
                throw new InvalidDataException(
                    "Helper нарушил последовательный протокол полной настройки.");
            }
            sequence = message.Sequence;
        }

        private static void ValidateItemIdentity(
            LmServiceProvisioningBatchRequest request,
            int index,
            ManagedProvisioningSessionMessage message)
        {
            if (index < 0 || index >= request.ManagedLocalModules.Count ||
                string.IsNullOrWhiteSpace(
                    request.ManagedLocalModules[index].KktSerial) ||
                message.Status == LmServiceProvisioningStatus.Pending)
            {
                throw new InvalidDataException(
                    "Helper не вернул итог строки полного плана.");
            }
        }

        private static LmServiceProvisioningItemResult ToItemResult(
            LmServiceProvisioningBatchRequest request,
            int index,
            ManagedProvisioningSessionMessage message)
        {
            return new LmServiceProvisioningItemResult
            {
                KktSerial = request.ManagedLocalModules[index].KktSerial,
                Status = message.Status,
                Message = message.Message ?? string.Empty
            };
        }

        private static void ValidateBatchItems(
            LmServiceProvisioningBatchRequest request,
            LmServiceProvisioningBatchResult result)
        {
            if (result == null || result.Items == null ||
                result.Items.Count != request.ManagedLocalModules.Count)
            {
                throw new InvalidDataException(
                    "Helper вернул неполный итог полной настройки.");
            }
            for (int index = 0; index < result.Items.Count; index++)
            {
                if (result.Items[index] == null ||
                    !string.Equals(
                        result.Items[index].KktSerial,
                        request.ManagedLocalModules[index].KktSerial,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Helper изменил порядок строк полного плана.");
                }
            }
        }

        private static ManagedProvisioningSessionMessage CreateSessionMessage(
            LmServiceProvisioningBatchRequest request,
            long sequence,
            ManagedProvisioningSessionKind kind,
            int itemIndex,
            LmServiceProvisioningStatus status,
            string message)
        {
            return new ManagedProvisioningSessionMessage
            {
                SchemaVersion = SchemaVersion,
                OperationId = request.OperationId,
                Sequence = sequence,
                Kind = kind,
                ItemIndex = itemIndex,
                Status = status,
                Message = message ?? string.Empty
            };
        }

        private static bool IsReady(LmServiceProvisioningStatus status)
        {
            return status == LmServiceProvisioningStatus.Succeeded ||
                status == LmServiceProvisioningStatus.ReadyToInitialize;
        }
    }
}
