using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class WindowsControllerReset
    {
        internal static LmServiceProvisioningBatchResult Execute(
            LmServiceProvisioningBatchRequest request, ILmProvisioningCancellation cancellation)
        {
            var result = new LmServiceProvisioningBatchResult { SchemaVersion = request.SchemaVersion,
                OperationId = request.OperationId, PlanHash = request.PlanHash,
                Status = LmServiceProvisioningStatus.Succeeded };
            WindowsDirectControllerPlatform direct = null;
            foreach (var item in request.DirectControllers)
            {
                var captured = item;
                RunOne(result, item.KktSerial, cancellation, delegate {
                    if (direct == null) direct = WindowsDirectControllerPlatform.Create(request.InitiatingSid);
                    return direct.RemoveForReset(captured, request.OperationId, request.InitiatingSid);
                });
            }
            foreach (var service in request.ControllerResetServices)
            {
                var captured = service;
                RunOne(result, service.ServiceName, cancellation, delegate {
                    if (!ControllerResetServiceRequest.IsAdditionalController(captured.ServiceName))
                        throw new InvalidDataException("Удаление этой службы запрещено.");
                    int ordinal;
                    if (DirectControllerIdentity.TryParseServiceName(captured.ServiceName, out ordinal))
                    {
                        if (direct == null) direct = WindowsDirectControllerPlatform.Create(request.InitiatingSid);
                        direct.RemoveUntrackedController(captured.ServiceName, captured.ExpectedImagePath, request.InitiatingSid);
                    }
                    else RemoveLegacyController(captured, request.InitiatingSid);
                    return new LmServiceProvisioningItemResult { KktSerial = captured.ServiceName,
                        Status = LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained,
                        Message = "Дополнительный контроллер удалён; ЛМ ЧЗ и регистрации ЕСМ сохранены." };
                });
            }
            return result;
        }

        private static void RunOne(LmServiceProvisioningBatchResult result, string name,
            ILmProvisioningCancellation cancellation, Func<LmServiceProvisioningItemResult> action)
        {
            try
            {
                if (cancellation != null && cancellation.IsCancellationRequested)
                    throw new OperationCanceledException();
                var item = action();
                result.Items.Add(item);
                if (item.Status != LmServiceProvisioningStatus.Succeeded &&
                    item.Status != LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained)
                    result.Status = LmServiceProvisioningStatus.RequiresAttention;
            }
            catch (Exception error)
            {
                if (error is OutOfMemoryException || error is StackOverflowException || error is AccessViolationException) throw;
                result.Status = LmServiceProvisioningStatus.RequiresAttention;
                result.Items.Add(new LmServiceProvisioningItemResult { KktSerial = name,
                    Status = error is OperationCanceledException ? LmServiceProvisioningStatus.Cancelled : LmServiceProvisioningStatus.RequiresAttention,
                    Message = error.Message });
            }
        }

        private static void RemoveLegacyController(ControllerResetServiceRequest item, string sid)
        {
            string serial;
            if (!LmServiceIdentity.TryParseName(item.ServiceName, out serial))
                throw new InvalidDataException("Неизвестная старая служба.");
            var services = new WindowsServiceApi();
            var service = services.Query(item.ServiceName);
            var store = ManagedServiceManifestStore.CreateMachineStore(new PathSafety(), sid);
            ManagedServiceManifest manifest;
            bool hasManifest = store.TryRead(serial, out manifest);
            if (service != null)
            {
                if (!string.Equals(service.ImagePath, item.ExpectedImagePath, StringComparison.Ordinal))
                    throw new InvalidDataException("Служба изменилась после подтверждения плана.");
                RequireLegacyOwnership(service, serial);
                if (hasManifest && !string.Equals(service.ImagePath, manifest.SupervisorImagePath, StringComparison.Ordinal) &&
                    !service.ImagePath.StartsWith(WindowsCommandLine.QuoteArgument(manifest.SupervisorImagePath) + " ", StringComparison.Ordinal))
                    throw new InvalidDataException("Путь старой службы не совпадает с манифестом.");
                StopAndDelete(services, item.ServiceName, delegate { Thread.Sleep(250); });
            }
            // Снимается только запись контроллера; старые профили и данные ЛМ сохраняются.
            if (hasManifest) store.Delete(serial);
        }

        internal static void RequireLegacyOwnership(WindowsServiceRecord service, string serial)
        {
            string suffix = " --supervise " + LmServiceIdentity.CreateName(serial);
            if (service == null || service.ImagePath == null || !service.ImagePath.EndsWith(suffix, StringComparison.Ordinal) ||
                !string.Equals(service.Description, LmGatewaySupervisorService.DescriptionPrefix + serial, StringComparison.Ordinal))
                throw new InvalidDataException("Принадлежность старой службы MultiKKT не подтверждена.");
            string executable = service.ImagePath.Substring(0, service.ImagePath.Length - suffix.Length).Trim('"');
            if (!string.Equals(Path.GetFileName(executable), "EsmTspiot.ServiceProvisioner.exe", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(executable) || FileVersionInfo.GetVersionInfo(executable).CompanyName != "KRS")
                throw new InvalidDataException("Не подтверждён исполняемый файл старого контроллера MultiKKT.");
        }

        internal static void StopAndDelete(IWindowsServiceApi services, string name, Action delay)
        {
            if (!ControllerResetServiceRequest.IsAdditionalController(name))
                throw new InvalidDataException("Сброс не удаляет штатный контроллер или службы ЛМ/ЕСМ.");
            var current = services.Query(name);
            if (current == null) return;
            if (current.State != WindowsServiceState.Stopped || current.ProcessId != 0)
            {
                services.RequestStop(name);
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                do
                {
                    current = services.Query(name);
                    if (current == null) return;
                    if (current.State == WindowsServiceState.Stopped && current.ProcessId == 0) break;
                    delay();
                } while (DateTime.UtcNow < deadline);
                if (current.State != WindowsServiceState.Stopped || current.ProcessId != 0)
                    throw new InvalidOperationException("Служба не остановилась; её запись и данные сохранены: " + name);
            }
            services.Delete(name);
            DateTime deletionDeadline = DateTime.UtcNow.AddSeconds(30);
            do
            {
                if (services.Query(name) == null) return;
                delay();
            } while (DateTime.UtcNow < deletionDeadline);
            throw new InvalidOperationException("Служба помечена на удаление и ещё присутствует в SCM: " + name);
        }
    }
}
