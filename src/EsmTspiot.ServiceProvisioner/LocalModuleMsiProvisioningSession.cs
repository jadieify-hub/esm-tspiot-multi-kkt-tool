using System;
using System.Collections.Generic;
using System.IO;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleMsiProvisioningSession : IDisposable
    {
        private readonly LmServiceProvisioningBatchRequest _request;
        private readonly LocalModuleMsiProvisioner _provisioner;
        private readonly LocalModuleMsiProvisioningContext _context;
        private readonly IDisposable _source;
        private readonly Dictionary<int, LocalModuleMsiProvisioningItemResult>
            _results;
        private int _nextIndex;
        private bool _disposed;

        internal LocalModuleMsiProvisioningSession(
            LmServiceProvisioningBatchRequest request,
            LocalModuleMsiProvisioner provisioner,
            LocalModuleMsiProvisioningContext context,
            IDisposable source)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (provisioner == null) throw new ArgumentNullException("provisioner");
            if (context == null) throw new ArgumentNullException("context");
            if (source == null) throw new ArgumentNullException("source");
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid || request.Operation !=
                    LmServiceOperation.EnsureMsiLocalModules)
                throw new InvalidDataException(
                    "MSI local-module session requires one immutable v3 plan.");
            _request = request;
            _provisioner = provisioner;
            _context = context;
            _source = source;
            _results =
                new Dictionary<int, LocalModuleMsiProvisioningItemResult>();
        }

        internal static LocalModuleMsiProvisioningSession CreateWindows(
            LmServiceProvisioningBatchRequest request)
        {
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid || request.Operation !=
                    LmServiceOperation.EnsureMsiLocalModules)
                throw new InvalidDataException(
                    "A valid MSI local-module v3 request is required.");
            VerifiedLocalModulePackage source = null;
            try
            {
                source = LocalModulePackageVerifier.SupportedVersion2617()
                    .VerifyAndLock(request.LocalModuleInstallerSelection);
                string machineRoot = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonApplicationData),
                    "KRS",
                    "MultiKKT");
                LocalModuleMsiProvisioningContext context =
                    LocalModuleMsiProvisioningContext.CreateWindows(
                        request.OperationId,
                        source,
                        machineRoot);
                LocalModuleMsiProvisioningSession result =
                    new LocalModuleMsiProvisioningSession(
                        request,
                        new LocalModuleMsiProvisioner(),
                        context,
                        source);
                source = null;
                return result;
            }
            finally
            {
                if (source != null) source.Dispose();
            }
        }

        internal LocalModuleMsiProvisioningItemResult ExecuteItem(int itemIndex)
        {
            ThrowIfDisposed();
            RequireNext(itemIndex);
            LocalModuleMsiProvisioningItemResult result = _provisioner.Ensure(
                _request.LocalModuleMsiItems[itemIndex],
                _context);
            _results.Add(itemIndex, result);
            _nextIndex++;
            return result;
        }

        internal LocalModuleMsiProvisioningItemResult SkipItem(
            int itemIndex,
            string message)
        {
            ThrowIfDisposed();
            RequireNext(itemIndex);
            LocalModuleMsiProvisioningItemRequest item =
                _request.LocalModuleMsiItems[itemIndex];
            LocalModuleMsiProvisioningItemResult result =
                LocalModuleMsiProvisioner.Result(
                    item,
                    LmServiceProvisioningStatus.Cancelled,
                    message,
                    item.ExpectedManifestSha256);
            _results.Add(itemIndex, result);
            _nextIndex++;
            return result;
        }

        internal LmServiceProvisioningBatchResult Finish()
        {
            ThrowIfDisposed();
            LmServiceProvisioningBatchResult result =
                new LmServiceProvisioningBatchResult
                {
                    SchemaVersion = ProvisioningRequestValidator
                        .CurrentSchemaVersion,
                    OperationId = _request.OperationId,
                    PlanHash = _request.PlanHash,
                    Status = LmServiceProvisioningStatus.Succeeded
                };
            for (int index = 0;
                index < _request.LocalModuleMsiItems.Count;
                index++)
            {
                LocalModuleMsiProvisioningItemResult item;
                if (!_results.TryGetValue(index, out item))
                    item = LocalModuleMsiProvisioner.Result(
                        _request.LocalModuleMsiItems[index],
                        LmServiceProvisioningStatus.Cancelled,
                        "Операция отменена до обработки этого ИНН.",
                        _request.LocalModuleMsiItems[index]
                            .ExpectedManifestSha256);
                result.LocalModuleMsiItems.Add(item);
                result.Status = Aggregate(result.Status, item.Status);
            }
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _source.Dispose();
        }

        private void RequireNext(int itemIndex)
        {
            if (itemIndex != _nextIndex || itemIndex < 0 ||
                itemIndex >= _request.LocalModuleMsiItems.Count)
                throw new InvalidDataException(
                    "Session accepts only the next MSI local-module item.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(
                "LocalModuleMsiProvisioningSession");
        }

        private static LmServiceProvisioningStatus Aggregate(
            LmServiceProvisioningStatus current,
            LmServiceProvisioningStatus next)
        {
            if (next == LmServiceProvisioningStatus.Failed ||
                next == LmServiceProvisioningStatus.RequiresAttention ||
                next == LmServiceProvisioningStatus.CleanupPending)
                return next;
            if (current == LmServiceProvisioningStatus.Succeeded &&
                next != LmServiceProvisioningStatus.Succeeded)
                return next;
            return current;
        }
    }

    internal sealed class LocalModuleMsiProvisioningSessionServer
    {
        private readonly IManagedProvisioningSessionChannel _channel;
        private long _lastSequence;

        internal LocalModuleMsiProvisioningSessionServer(
            IManagedProvisioningSessionChannel channel)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            _channel = channel;
        }

        internal LmServiceProvisioningBatchResult Run(
            LmServiceProvisioningBatchRequest request,
            LocalModuleMsiProvisioningSession session)
        {
            Write(request, ManagedProvisioningSessionKind.SessionReady,
                -1, LmServiceProvisioningStatus.Pending,
                "MSI ЛМ проверен; начинается последовательная установка.");
            int nextIndex = 0;
            while (true)
            {
                ManagedProvisioningSessionMessage command =
                    _channel.ReadSessionMessage();
                ValidationResult validation =
                    ProvisioningRequestValidator.ValidateSessionMessage(
                        command,
                        request.OperationId,
                        _lastSequence,
                        request.LocalModuleMsiItems.Count);
                if (!validation.IsValid ||
                    command.Sequence != _lastSequence + 1)
                    throw new InvalidDataException(
                        "Нарушен последовательный протокол MSI ЛМ.");
                _lastSequence = command.Sequence;
                if (command.Kind == ManagedProvisioningSessionKind.Finish ||
                    command.Kind ==
                        ManagedProvisioningSessionKind.CancelAfterCurrentItem)
                    return session.Finish();
                if (command.Kind != ManagedProvisioningSessionKind.ExecuteItem ||
                    command.ItemIndex != nextIndex)
                    throw new InvalidDataException(
                        "Клиент изменил порядок строк MSI ЛМ.");
                LocalModuleMsiProvisioningItemResult item = command.Status ==
                        LmServiceProvisioningStatus.Cancelled
                    ? session.SkipItem(nextIndex, command.Message)
                    : session.ExecuteItem(nextIndex);
                Write(request, ManagedProvisioningSessionKind.ItemResult,
                    nextIndex, item.Status, item.Message);
                nextIndex++;
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
                    SchemaVersion = ProvisioningRequestValidator
                        .CurrentSchemaVersion,
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
