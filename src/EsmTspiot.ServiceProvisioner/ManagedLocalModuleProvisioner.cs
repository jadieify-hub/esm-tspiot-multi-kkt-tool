using System;
using System.IO;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal enum ManagedLocalModuleProvisioningStage
    {
        Created = 1,
        RuntimeReady = 2,
        ProfileReady = 3,
        DbReady = 4,
        ApiReady = 5,
        ControllerReady = 6,
        BindingPending = 7,
        Completed = 8,
        CleanupPending = 9,
        RequiresAttention = 10
    }

    internal enum ManagedLocalModuleObservedState
    {
        Absent = 1,
        MatchingStopped = 2,
        MatchingReady = 3,
        OwnedMismatch = 4,
        VersionVerificationPending = 5,
        CleanupPending = 6,
        Foreign = 7
    }

    internal sealed class ManagedLocalModuleProvisioningContext : IDisposable
    {
        private IDisposable _lockedPackage;

        internal ManagedLocalModuleProvisioningContext(
            string runtimeId,
            string capabilityId,
            string runtimeVersion)
            : this(runtimeId, capabilityId, runtimeVersion, null)
        {
        }

        internal ManagedLocalModuleProvisioningContext(
            string runtimeId,
            string capabilityId,
            string runtimeVersion,
            IDisposable lockedPackage)
        {
            if (!LocalModuleManagedIdentity.IsRuntimeId(runtimeId) ||
                string.IsNullOrWhiteSpace(capabilityId) ||
                string.IsNullOrWhiteSpace(runtimeVersion))
            {
                throw new ArgumentException(
                    "Managed local-module runtime context is invalid.");
            }
            RuntimeId = runtimeId;
            CapabilityId = capabilityId;
            RuntimeVersion = runtimeVersion;
            _lockedPackage = lockedPackage;
        }

        internal string RuntimeId { get; private set; }
        internal string CapabilityId { get; private set; }
        internal string RuntimeVersion { get; private set; }

        public void Dispose()
        {
            if (_lockedPackage != null)
            {
                _lockedPackage.Dispose();
                _lockedPackage = null;
            }
        }
    }

    internal interface IManagedLocalModuleProvisioningPlatform
    {
        IDisposable AcquireItemLock(string inn);

        void Reconcile(
            ManagedLocalModuleProvisioningItemRequest item,
            string operationId);

        ManagedLocalModuleObservedState Inspect(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context);

        void RecordStage(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string operationId,
            ManagedLocalModuleProvisioningStage stage);

        void PrepareProfile(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string operationId);

        void ConfigureServicePair(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string initiatingSid);

        void StartDatabaseAndWait(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context);

        void StartApiAndWait(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context);

        void Complete(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string operationId);

        void EnsureStackReference(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string operationId);

        void MarkRequiresAttention(
            ManagedLocalModuleProvisioningItemRequest item,
            string operationId,
            string errorClass);
    }

    internal sealed class ManagedLocalModuleProvisioner
    {
        private readonly IManagedLocalModuleProvisioningPlatform _platform;

        internal ManagedLocalModuleProvisioner(
            IManagedLocalModuleProvisioningPlatform platform)
        {
            if (platform == null) throw new ArgumentNullException("platform");
            _platform = platform;
        }

        internal LmServiceProvisioningItemResult Ensure(
            ManagedLocalModuleProvisioningItemRequest item,
            ManagedLocalModuleProvisioningContext context,
            string operationId,
            string initiatingSid)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (context == null) throw new ArgumentNullException("context");
            if (!ProvisionerCommandLine.IsGuidN(operationId))
            {
                throw new ArgumentException(
                    "Operation id must contain a 32-character GUID.",
                    "operationId");
            }
            if (!string.Equals(
                    item.RuntimeVersion,
                    context.RuntimeVersion,
                    StringComparison.Ordinal))
            {
                return CreateResult(
                    item,
                    LmServiceProvisioningStatus.VersionVerificationPending,
                    "Версия строки не совпадает с проверенным runtime ЛМ ЧЗ.");
            }

            using (_platform.AcquireItemLock(item.Inn))
            {
                try
                {
                    _platform.Reconcile(item, operationId);
                    ManagedLocalModuleObservedState observed =
                        _platform.Inspect(item, context);
                    if (observed == ManagedLocalModuleObservedState.MatchingReady)
                    {
                        _platform.EnsureStackReference(
                            item,
                            context,
                            operationId);
                        return Ready(item);
                    }
                    if (observed ==
                            ManagedLocalModuleObservedState.VersionVerificationPending)
                    {
                        return CreateResult(
                            item,
                            LmServiceProvisioningStatus.VersionVerificationPending,
                            "Для установленной версии требуется точный capability-профиль обновления.");
                    }
                    if (observed == ManagedLocalModuleObservedState.CleanupPending)
                    {
                        return CreateResult(
                            item,
                            LmServiceProvisioningStatus.CleanupPending,
                            "Сначала требуется завершить ранее начатую очистку.");
                    }
                    if (observed == ManagedLocalModuleObservedState.Foreign)
                    {
                        return CreateResult(
                            item,
                            LmServiceProvisioningStatus.RequiresAttention,
                            "Обнаружены компоненты без подтверждённого владения программы.");
                    }

                    if (observed == ManagedLocalModuleObservedState.Absent ||
                        observed == ManagedLocalModuleObservedState.OwnedMismatch)
                    {
                        _platform.RecordStage(
                            item,
                            context,
                            operationId,
                            ManagedLocalModuleProvisioningStage.Created);
                        _platform.RecordStage(
                            item,
                            context,
                            operationId,
                            ManagedLocalModuleProvisioningStage.RuntimeReady);
                        _platform.PrepareProfile(item, context, operationId);
                        _platform.RecordStage(
                            item,
                            context,
                            operationId,
                            ManagedLocalModuleProvisioningStage.ProfileReady);
                        _platform.ConfigureServicePair(
                            item,
                            context,
                            initiatingSid);
                    }

                    _platform.StartDatabaseAndWait(item, context);
                    _platform.RecordStage(
                        item,
                        context,
                        operationId,
                        ManagedLocalModuleProvisioningStage.DbReady);
                    _platform.StartApiAndWait(item, context);
                    _platform.RecordStage(
                        item,
                        context,
                        operationId,
                        ManagedLocalModuleProvisioningStage.ApiReady);
                    _platform.Complete(item, context, operationId);
                    _platform.RecordStage(
                        item,
                        context,
                        operationId,
                        ManagedLocalModuleProvisioningStage.Completed);
                    return Ready(item);
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException) &&
                        !(ex is UnauthorizedAccessException) &&
                        !(ex is InvalidDataException) &&
                        !(ex is InvalidOperationException))
                    {
                        throw;
                    }
                    _platform.MarkRequiresAttention(
                        item,
                        operationId,
                        ex.GetType().Name);
                    return CreateResult(
                        item,
                        LmServiceProvisioningStatus.Failed,
                        "Не удалось подготовить управляемый ЛМ: " +
                            ex.GetType().Name + ".");
                }
            }
        }

        private static LmServiceProvisioningItemResult Ready(
            ManagedLocalModuleProvisioningItemRequest item)
        {
            return CreateResult(
                item,
                LmServiceProvisioningStatus.ReadyToInitialize,
                "ЛМ запущен и готов к штатной инициализации.");
        }

        private static LmServiceProvisioningItemResult CreateResult(
            ManagedLocalModuleProvisioningItemRequest item,
            LmServiceProvisioningStatus status,
            string message)
        {
            return new LmServiceProvisioningItemResult
            {
                KktSerial = item == null ? string.Empty : item.KktSerial,
                Status = status,
                Message = message
            };
        }
    }
}
