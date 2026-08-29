using System;
using System.Collections.Generic;
using System.IO;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class CompleteStackProvisioningSession : IDisposable
    {
        private readonly LmServiceProvisioningBatchRequest _request;
        private readonly ManagedLocalModuleProvisioner _provisioner;
        private ManagedLocalModuleProvisioningContext _context;
        private readonly Dictionary<int, LmServiceProvisioningItemResult> _results;
        private int _nextIndex;
        private bool _failed;
        private bool _disposed;

        internal CompleteStackProvisioningSession(
            LmServiceProvisioningBatchRequest request,
            ManagedLocalModuleProvisioner provisioner,
            ManagedLocalModuleProvisioningContext context)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (provisioner == null) throw new ArgumentNullException("provisioner");
            if (context == null) throw new ArgumentNullException("context");
            ValidationResult validation = ProvisioningRequestValidator.Validate(request);
            if (!validation.IsValid ||
                request.Operation != LmServiceOperation.EnsureManagedLocalModules ||
                !CanonicalLmPlanHasher.FixedTimeEqualsHex(
                    request.PlanHash,
                    CanonicalLmPlanHasher.Compute(request)))
            {
                throw new InvalidDataException(
                    "Complete-stack session requires one immutable prehashed plan.");
            }
            _request = request;
            _provisioner = provisioner;
            _context = context;
            _results = new Dictionary<int, LmServiceProvisioningItemResult>();
        }

        internal LmServiceProvisioningItemResult ExecuteItem(int itemIndex)
        {
            ThrowIfDisposed();
            if (itemIndex != _nextIndex ||
                itemIndex < 0 ||
                itemIndex >= _request.ManagedLocalModules.Count)
            {
                throw new InvalidDataException(
                    "Session accepts only the next prehashed plan index.");
            }
            if (_failed)
            {
                throw new InvalidOperationException(
                    "The canary or an earlier group failed; no later mutation is allowed.");
            }

            ManagedLocalModuleProvisioningItemRequest item =
                _request.ManagedLocalModules[itemIndex];
            LmServiceProvisioningItemResult result = _provisioner.Ensure(
                item,
                _context,
                _request.OperationId,
                _request.InitiatingSid);
            _results.Add(itemIndex, result);
            _nextIndex++;
            if (!IsReady(result.Status)) _failed = true;
            return result;
        }

        internal LmServiceProvisioningBatchResult ExecuteAll()
        {
            ThrowIfDisposed();
            while (_nextIndex < _request.ManagedLocalModules.Count && !_failed)
            {
                ExecuteItem(_nextIndex);
            }
            if (_failed)
            {
                for (int index = _nextIndex;
                    index < _request.ManagedLocalModules.Count;
                    index++)
                {
                    ManagedLocalModuleProvisioningItemRequest item =
                        _request.ManagedLocalModules[index];
                    _results.Add(index, new LmServiceProvisioningItemResult
                    {
                        KktSerial = item.KktSerial,
                        Status = LmServiceProvisioningStatus.Cancelled,
                        Message =
                            "Не выполнено после ошибки контрольного управляемого ЛМ."
                    });
                }
                _nextIndex = _request.ManagedLocalModules.Count;
            }
            return BuildResult();
        }

        private LmServiceProvisioningBatchResult BuildResult()
        {
            LmServiceProvisioningBatchResult result =
                new LmServiceProvisioningBatchResult
                {
                    SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                    OperationId = _request.OperationId,
                    PlanHash = _request.PlanHash,
                    Status = LmServiceProvisioningStatus.ReadyToInitialize
                };
            for (int index = 0; index < _request.ManagedLocalModules.Count; index++)
            {
                LmServiceProvisioningItemResult item;
                if (!_results.TryGetValue(index, out item))
                {
                    throw new InvalidOperationException(
                        "Session result is incomplete.");
                }
                result.Items.Add(item);
                if (!IsReady(item.Status) &&
                    item.Status != LmServiceProvisioningStatus.Cancelled &&
                    IsReady(result.Status))
                {
                    result.Status = item.Status;
                }
                else if (item.Status == LmServiceProvisioningStatus.Cancelled &&
                    IsReady(result.Status))
                {
                    result.Status = LmServiceProvisioningStatus.Cancelled;
                }
            }
            return result;
        }

        private static bool IsReady(LmServiceProvisioningStatus status)
        {
            return status == LmServiceProvisioningStatus.Succeeded ||
                status == LmServiceProvisioningStatus.ReadyToInitialize;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    "CompleteStackProvisioningSession");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_context != null)
            {
                _context.Dispose();
                _context = null;
            }
        }
    }
}
