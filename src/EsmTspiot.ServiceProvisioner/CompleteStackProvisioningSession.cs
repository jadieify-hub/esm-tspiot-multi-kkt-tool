using System;
using System.Collections.Generic;
using System.IO;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class CompleteStackProvisioningComponents
    {
        internal CompleteStackProvisioningComponents(
            ManagedLocalModuleProvisioner localModuleProvisioner,
            ManagedLocalModuleProvisioningContext context,
            Func<
                ManagedLocalModuleProvisioningItemRequest,
                LmServiceProvisioningItemResult> controllerProvisioner)
        {
            if (localModuleProvisioner == null)
            {
                throw new ArgumentNullException("localModuleProvisioner");
            }
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            LocalModuleProvisioner = localModuleProvisioner;
            Context = context;
            ControllerProvisioner = controllerProvisioner;
        }

        internal ManagedLocalModuleProvisioner LocalModuleProvisioner { get; private set; }
        internal ManagedLocalModuleProvisioningContext Context { get; private set; }
        internal Func<
            ManagedLocalModuleProvisioningItemRequest,
            LmServiceProvisioningItemResult> ControllerProvisioner { get; private set; }
    }

    internal sealed class CompleteStackProvisioningSession : IDisposable
    {
        private readonly LmServiceProvisioningBatchRequest _request;
        private ManagedLocalModuleProvisioner _provisioner;
        private Func<
            ManagedLocalModuleProvisioningItemRequest,
            LmServiceProvisioningItemResult> _controllerProvisioner;
        private ManagedLocalModuleProvisioningContext _context;
        private Func<CompleteStackProvisioningComponents> _initializer;
        private IDisposable _preparedResources;
        private readonly Dictionary<int, LmServiceProvisioningItemResult> _results;
        private int _nextIndex;
        private bool _stopRemaining;
        private bool _disposed;

        internal CompleteStackProvisioningSession(
            LmServiceProvisioningBatchRequest request,
            ManagedLocalModuleProvisioner provisioner,
            ManagedLocalModuleProvisioningContext context)
            : this(request, provisioner, context, null)
        {
        }

        internal CompleteStackProvisioningSession(
            LmServiceProvisioningBatchRequest request,
            ManagedLocalModuleProvisioner provisioner,
            ManagedLocalModuleProvisioningContext context,
            Func<
                ManagedLocalModuleProvisioningItemRequest,
                LmServiceProvisioningItemResult> controllerProvisioner)
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
            _controllerProvisioner = controllerProvisioner;
            _results = new Dictionary<int, LmServiceProvisioningItemResult>();
        }

        internal CompleteStackProvisioningSession(
            LmServiceProvisioningBatchRequest request,
            Func<CompleteStackProvisioningComponents> initializer,
            IDisposable preparedResources)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (initializer == null) throw new ArgumentNullException("initializer");
            if (preparedResources == null)
            {
                throw new ArgumentNullException("preparedResources");
            }
            ValidateRequest(request);
            _request = request;
            _initializer = initializer;
            _preparedResources = preparedResources;
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
            if (_stopRemaining)
            {
                throw new InvalidOperationException(
                    "The canary or an earlier group failed; no later mutation is allowed.");
            }

            ManagedLocalModuleProvisioningItemRequest item =
                _request.ManagedLocalModules[itemIndex];
            EnsureInitialized();
            LmServiceProvisioningItemResult result = _provisioner.Ensure(
                item,
                _context,
                _request.OperationId,
                _request.InitiatingSid);
            if (IsReady(result.Status) && _controllerProvisioner != null)
            {
                LmServiceProvisioningItemResult controller =
                    _controllerProvisioner(item);
                if (controller == null ||
                    !string.Equals(
                        controller.KktSerial,
                        item.KktSerial,
                        StringComparison.Ordinal))
                {
                    result = new LmServiceProvisioningItemResult
                    {
                        KktSerial = item.KktSerial,
                        Status = LmServiceProvisioningStatus.RequiresAttention,
                        Message = "Контроллер не вернул однозначный результат для ККТ."
                    };
                }
                else if (controller.Status != LmServiceProvisioningStatus.Succeeded)
                {
                    result = controller;
                }
                else
                {
                    result.Message =
                        "ЛМ запущен и готов к инициализации; контроллер ККТ запущен.";
                }
            }
            _results.Add(itemIndex, result);
            _nextIndex++;
            if (!IsReady(result.Status) &&
                (itemIndex == 0 || IsGlobalFailure(result.Status)))
            {
                _stopRemaining = true;
            }
            return result;
        }

        internal LmServiceProvisioningItemResult SkipItem(
            int itemIndex,
            string message)
        {
            ThrowIfDisposed();
            if (itemIndex != _nextIndex ||
                itemIndex < 0 ||
                itemIndex >= _request.ManagedLocalModules.Count)
            {
                throw new InvalidDataException(
                    "Session accepts a skip only for the next prehashed plan index.");
            }
            ManagedLocalModuleProvisioningItemRequest item =
                _request.ManagedLocalModules[itemIndex];
            LmServiceProvisioningItemResult result =
                new LmServiceProvisioningItemResult
                {
                    KktSerial = item.KktSerial,
                    Status = LmServiceProvisioningStatus.Cancelled,
                    Message = string.IsNullOrWhiteSpace(message)
                        ? "Строка пропущена до изменения локальных компонентов."
                        : message
                };
            _results.Add(itemIndex, result);
            _nextIndex++;
            if (itemIndex == 0)
            {
                _stopRemaining = true;
            }
            return result;
        }

        internal LmServiceProvisioningItemResult FailItem(
            int itemIndex,
            LmServiceProvisioningStatus status,
            string message)
        {
            ThrowIfDisposed();
            if (itemIndex != _nextIndex ||
                itemIndex < 0 ||
                itemIndex >= _request.ManagedLocalModules.Count ||
                (status != LmServiceProvisioningStatus.Failed &&
                 status != LmServiceProvisioningStatus.UnsupportedController &&
                 status !=
                    LmServiceProvisioningStatus.VersionVerificationPending &&
                 status != LmServiceProvisioningStatus.RequiresAttention))
            {
                throw new InvalidDataException(
                    "Session failure does not match the next immutable plan index.");
            }
            ManagedLocalModuleProvisioningItemRequest item =
                _request.ManagedLocalModules[itemIndex];
            LmServiceProvisioningItemResult result =
                new LmServiceProvisioningItemResult
                {
                    KktSerial = item.KktSerial,
                    Status = status,
                    Message = string.IsNullOrWhiteSpace(message)
                        ? "Не удалось подготовить полный комплект."
                        : message
                };
            _results.Add(itemIndex, result);
            _nextIndex++;
            if (itemIndex == 0 || IsGlobalFailure(status))
            {
                _stopRemaining = true;
            }
            return result;
        }

        internal LmServiceProvisioningBatchResult ExecuteAll()
        {
            ThrowIfDisposed();
            while (_nextIndex < _request.ManagedLocalModules.Count && !_stopRemaining)
            {
                ExecuteItem(_nextIndex);
            }
            return Finish();
        }

        internal LmServiceProvisioningBatchResult Finish()
        {
            ThrowIfDisposed();
            if (_nextIndex < _request.ManagedLocalModules.Count)
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

        private static bool IsGlobalFailure(LmServiceProvisioningStatus status)
        {
            return status == LmServiceProvisioningStatus.UnsupportedController ||
                status == LmServiceProvisioningStatus.VersionVerificationPending;
        }

        private void EnsureInitialized()
        {
            if (_provisioner != null && _context != null)
            {
                return;
            }
            if (_initializer == null)
            {
                throw new InvalidOperationException(
                    "Complete-stack provisioning components are unavailable.");
            }
            CompleteStackProvisioningComponents components = _initializer();
            if (components == null)
            {
                throw new InvalidOperationException(
                    "Complete-stack provisioning initialization returned no components.");
            }
            _provisioner = components.LocalModuleProvisioner;
            _context = components.Context;
            _controllerProvisioner = components.ControllerProvisioner;
            _initializer = null;
        }

        private static void ValidateRequest(
            LmServiceProvisioningBatchRequest request)
        {
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
            if (_preparedResources != null)
            {
                _preparedResources.Dispose();
                _preparedResources = null;
            }
            _initializer = null;
        }
    }
}
