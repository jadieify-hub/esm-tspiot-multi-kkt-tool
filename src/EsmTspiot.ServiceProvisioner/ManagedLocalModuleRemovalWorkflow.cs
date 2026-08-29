using System;
using System.IO;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class ManagedLocalModuleRemovalSnapshot
    {
        private ManagedLocalModuleRemovalSnapshot()
        {
        }

        internal string KktSerial { get; private set; }
        internal string StackId { get; private set; }
        internal string StackOwnershipNonce { get; private set; }
        internal string InstanceId { get; private set; }
        internal string InstanceOwnershipNonce { get; private set; }
        internal string RuntimeId { get; private set; }
        internal string RuntimeOwnershipNonce { get; private set; }

        internal static ManagedLocalModuleRemovalSnapshot CreateForTesting(
            string kktSerial,
            string stackId,
            string stackOwnershipNonce,
            string instanceId,
            string instanceOwnershipNonce,
            string runtimeId,
            string runtimeOwnershipNonce)
        {
            if (!LocalModuleManagedIdentity.IsStackId(stackId) ||
                !string.Equals(
                    stackId,
                    LocalModuleManagedIdentity.CreateStackId(kktSerial),
                    StringComparison.Ordinal) ||
                !LocalModuleManagedIdentity.IsInstanceId(instanceId) ||
                !LocalModuleManagedIdentity.IsRuntimeId(runtimeId) ||
                !LocalModuleManagedIdentity.IsLowerHex(stackOwnershipNonce, 32) ||
                !LocalModuleManagedIdentity.IsLowerHex(instanceOwnershipNonce, 32) ||
                !LocalModuleManagedIdentity.IsLowerHex(runtimeOwnershipNonce, 32))
            {
                throw new InvalidDataException(
                    "Managed local-module removal snapshot is invalid.");
            }
            return new ManagedLocalModuleRemovalSnapshot
            {
                KktSerial = kktSerial,
                StackId = stackId,
                StackOwnershipNonce = stackOwnershipNonce,
                InstanceId = instanceId,
                InstanceOwnershipNonce = instanceOwnershipNonce,
                RuntimeId = runtimeId,
                RuntimeOwnershipNonce = runtimeOwnershipNonce
            };
        }

        internal static ManagedLocalModuleRemovalSnapshot CreateOwned(
            ManagedKktStackManifest stack,
            LocalModuleInstanceManifest instance,
            LocalModuleRuntimeManifest runtime)
        {
            if (stack == null || instance == null || runtime == null ||
                !string.Equals(
                    stack.LocalModuleInstanceId,
                    instance.InstanceId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    instance.RuntimeId,
                    runtime.RuntimeId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Removal ownership manifests do not form one exact stack.");
            }
            return CreateForTesting(
                stack.KktSerial,
                stack.StackId,
                stack.OwnershipNonce,
                instance.InstanceId,
                instance.OwnershipNonce,
                runtime.RuntimeId,
                runtime.OwnershipNonce);
        }
    }

    internal interface IManagedLocalModuleRemovalPlatform
    {
        IDisposable AcquireMachineLock();
        ManagedLocalModuleRemovalSnapshot Inspect(
            string kktSerial,
            string operationId);
        void RemoveController(ManagedLocalModuleRemovalSnapshot snapshot);
        void DeleteStack(ManagedLocalModuleRemovalSnapshot snapshot);
        int CountInstanceReferences(ManagedLocalModuleRemovalSnapshot snapshot);
        LocalModuleServicePairStopOutcome StopServicePair(
            ManagedLocalModuleRemovalSnapshot snapshot);
        void DeleteServicePair(ManagedLocalModuleRemovalSnapshot snapshot);
        void DeleteProfile(ManagedLocalModuleRemovalSnapshot snapshot);
        void DeleteInstance(ManagedLocalModuleRemovalSnapshot snapshot);
        int CountRuntimeReferences(ManagedLocalModuleRemovalSnapshot snapshot);
        void DeleteRuntime(ManagedLocalModuleRemovalSnapshot snapshot);
        void MarkCleanupPending(
            ManagedLocalModuleRemovalSnapshot snapshot,
            string operationId,
            string errorClass);
        void CompleteRemoval(ManagedLocalModuleRemovalSnapshot snapshot);
    }

    internal sealed class ManagedLocalModuleRemovalWorkflow
    {
        private readonly IManagedLocalModuleRemovalPlatform _platform;

        internal ManagedLocalModuleRemovalWorkflow(
            IManagedLocalModuleRemovalPlatform platform)
        {
            if (platform == null) throw new ArgumentNullException("platform");
            _platform = platform;
        }

        internal LmServiceProvisioningItemResult RemoveKkt(
            string kktSerial,
            string operationId)
        {
            if (!ProvisionerCommandLine.IsGuidN(operationId))
            {
                throw new ArgumentException(
                    "Operation id must contain a 32-character GUID.",
                    "operationId");
            }
            using (_platform.AcquireMachineLock())
            {
                ManagedLocalModuleRemovalSnapshot snapshot =
                    _platform.Inspect(kktSerial, operationId);
                try
                {
                    _platform.RemoveController(snapshot);
                    _platform.DeleteStack(snapshot);
                    if (_platform.CountInstanceReferences(snapshot) > 0)
                    {
                        _platform.CompleteRemoval(snapshot);
                        return Result(
                            kktSerial,
                            LmServiceProvisioningStatus.SharedLocalModuleRetained,
                            "ККТ удалена; общий ЛМ этого ИНН сохранён для других ККТ.");
                    }

                    LocalModuleServicePairStopOutcome stopped =
                        _platform.StopServicePair(snapshot);
                    if (stopped == LocalModuleServicePairStopOutcome.CleanupBlocked)
                    {
                        _platform.MarkCleanupPending(
                            snapshot,
                            operationId,
                            "LiveErlangNodes");
                        return Result(
                            kktSerial,
                            LmServiceProvisioningStatus.CleanupPending,
                            "Очистка ожидает штатного завершения узлов ЛМ.");
                    }
                    _platform.DeleteServicePair(snapshot);
                    _platform.DeleteProfile(snapshot);
                    _platform.DeleteInstance(snapshot);
                    if (_platform.CountRuntimeReferences(snapshot) == 0)
                    {
                        _platform.DeleteRuntime(snapshot);
                    }
                    _platform.CompleteRemoval(snapshot);
                    return Result(
                        kktSerial,
                        LmServiceProvisioningStatus.Succeeded,
                        "Принадлежащие программе компоненты ККТ и ЛМ удалены.");
                }
                catch (Exception ex)
                {
                    if (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        _platform.MarkCleanupPending(
                            snapshot,
                            operationId,
                            ex.GetType().Name);
                        return Result(
                            kktSerial,
                            LmServiceProvisioningStatus.CleanupPending,
                            "Очистка не завершена; после освобождения файлов доступен повтор.");
                    }
                    if (ex is InvalidDataException ||
                        ex is InvalidOperationException)
                    {
                        return Result(
                            kktSerial,
                            LmServiceProvisioningStatus.RequiresAttention,
                            "Очистка остановлена из-за несовпадения фактического владения.");
                    }
                    throw;
                }
            }
        }

        private static LmServiceProvisioningItemResult Result(
            string kktSerial,
            LmServiceProvisioningStatus status,
            string message)
        {
            return new LmServiceProvisioningItemResult
            {
                KktSerial = kktSerial,
                Status = status,
                Message = message
            };
        }
    }
}
