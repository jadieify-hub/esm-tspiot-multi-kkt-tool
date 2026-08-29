using System;
using System.IO;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IManagedProvisioningSessionChannel
    {
        ManagedProvisioningSessionMessage ReadSessionMessage();

        void WriteSessionMessage(ManagedProvisioningSessionMessage message);
    }

    internal sealed class ManagedProvisioningSessionServer
    {
        private readonly IManagedProvisioningSessionChannel _channel;
        private long _lastSequence;

        internal ManagedProvisioningSessionServer(
            IManagedProvisioningSessionChannel channel)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            _channel = channel;
        }

        internal LmServiceProvisioningBatchResult Run(
            LmServiceProvisioningBatchRequest request,
            CompleteStackProvisioningSession session)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (session == null) throw new ArgumentNullException("session");

            Write(
                request,
                ManagedProvisioningSessionKind.SessionReady,
                -1,
                LmServiceProvisioningStatus.Pending,
                "Оба пакета проверены. Можно регистрировать контрольную ККТ.");

            int nextIndex = 0;
            while (true)
            {
                ManagedProvisioningSessionMessage command =
                    _channel.ReadSessionMessage();
                ValidateIncoming(request, command, nextIndex);
                _lastSequence = command.Sequence;

                if (command.Kind == ManagedProvisioningSessionKind.Finish ||
                    command.Kind ==
                        ManagedProvisioningSessionKind.CancelAfterCurrentItem)
                {
                    return session.Finish();
                }

                LmServiceProvisioningItemResult result;
                if (command.Status == LmServiceProvisioningStatus.Cancelled)
                {
                    result = session.SkipItem(nextIndex, command.Message);
                }
                else
                {
                    try
                    {
                        result = session.ExecuteItem(nextIndex);
                    }
                    catch (Exception ex)
                    {
                        LmServiceProvisioningStatus status =
                            ex is NotSupportedException
                                ? LmServiceProvisioningStatus.UnsupportedController
                                : LmServiceProvisioningStatus.Failed;
                        result = session.FailItem(
                            nextIndex,
                            status,
                            "Не удалось подготовить полный комплект: " +
                                ex.GetType().Name + ".");
                    }
                }

                Write(
                    request,
                    ManagedProvisioningSessionKind.ItemResult,
                    nextIndex,
                    result.Status,
                    result.Message);
                nextIndex++;
            }
        }

        private void ValidateIncoming(
            LmServiceProvisioningBatchRequest request,
            ManagedProvisioningSessionMessage message,
            int nextIndex)
        {
            ValidationResult validation =
                ProvisioningRequestValidator.ValidateSessionMessage(
                    message,
                    request.OperationId,
                    _lastSequence,
                    request.ManagedLocalModules.Count);
            if (!validation.IsValid || message.Sequence != _lastSequence + 1)
            {
                throw new InvalidDataException(
                    "Нарушена целостность последовательного сеанса.");
            }
            if (message.Kind == ManagedProvisioningSessionKind.ExecuteItem)
            {
                if (message.ItemIndex != nextIndex ||
                    (message.Status != LmServiceProvisioningStatus.Pending &&
                     message.Status != LmServiceProvisioningStatus.Cancelled))
                {
                    throw new InvalidDataException(
                        "Сеанс принимает только следующую строку неизменного плана.");
                }
                return;
            }
            if (message.Kind != ManagedProvisioningSessionKind.Finish &&
                message.Kind !=
                    ManagedProvisioningSessionKind.CancelAfterCurrentItem)
            {
                throw new InvalidDataException(
                    "Клиент отправил сообщение неверного направления.");
            }
        }

        private void Write(
            LmServiceProvisioningBatchRequest request,
            ManagedProvisioningSessionKind kind,
            int itemIndex,
            LmServiceProvisioningStatus status,
            string message)
        {
            _lastSequence++;
            _channel.WriteSessionMessage(
                new ManagedProvisioningSessionMessage
                {
                    SchemaVersion =
                        ProvisioningRequestValidator.CurrentSchemaVersion,
                    OperationId = request.OperationId,
                    Sequence = _lastSequence,
                    Kind = kind,
                    ItemIndex = itemIndex,
                    Status = status,
                    Message = message ?? string.Empty
                });
        }
    }
}
