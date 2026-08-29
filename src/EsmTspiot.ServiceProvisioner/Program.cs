using System;
using System.IO;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class Program
    {
        private const int ExitResultSent = 0;
        private const int ExitInvalidRequest = 2;
        private const int ExitOperationFailed = 3;
        private const int ExitUnsupportedController = 4;

        private static int Main(string[] args)
        {
            ProvisionerCommandLine commandLine;
            if (!ProvisionerCommandLine.TryParse(args, out commandLine))
            {
                return ExitInvalidRequest;
            }
            if (commandLine.Mode == ProvisionerMode.Supervisor)
            {
                return LmGatewaySupervisorService.RunServiceMode(commandLine.ServiceName);
            }
            if (commandLine.Mode == ProvisionerMode.LocalModuleSupervisor)
            {
                return ManagedChildServiceHost.Run(commandLine.ServiceName);
            }

            try
            {
                using (NamedPipeProvisioningChannel channel =
                    NamedPipeProvisioningChannel.ConnectAndAuthenticate(commandLine.PipeName, 30000))
                {
                    LmServiceProvisioningBatchRequest request =
                        channel.ReadMessage<LmServiceProvisioningBatchRequest>();
                    if (!string.Equals(
                            request.OperationId,
                            commandLine.OperationId,
                            StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(
                            request.InitiatingSid,
                            channel.AuthenticatedServerSid,
                            StringComparison.Ordinal))
                    {
                        return ExitInvalidRequest;
                    }

                    ValidationResult validation = ProvisioningRequestValidator.Validate(request);
                    if (!validation.IsValid)
                    {
                        return ExitInvalidRequest;
                    }

                    try
                    {
                        if (request.Operation ==
                            LmServiceOperation.EnsureManagedLocalModules)
                        {
                            using (CompleteStackProvisioningSession session =
                                WindowsManagedLocalModulePlatform.CreateSession(
                                    request))
                            {
                                LmServiceProvisioningBatchResult result =
                                    session.ExecuteAll();
                                channel.WriteMessage(result);
                                return ToExitCode(result.Status);
                            }
                        }
                        if (request.Operation == LmServiceOperation.RemoveManaged ||
                            request.Operation == LmServiceOperation.CleanupManaged)
                        {
                            string kktSerial = request.Operation ==
                                LmServiceOperation.RemoveManaged
                                ? request.RemovalConfirmation.KktSerial
                                : request.CleanupConfirmation.KktSerial;
                            if (WindowsManagedLocalModuleRemovalPlatform.HasManagedState(
                                    request.InitiatingSid,
                                    kktSerial))
                            {
                                ManagedLocalModuleRemovalWorkflow workflow =
                                    new ManagedLocalModuleRemovalWorkflow(
                                        WindowsManagedLocalModuleRemovalPlatform.Create(
                                            request));
                                LmServiceProvisioningItemResult item =
                                    workflow.RemoveKkt(
                                        kktSerial,
                                        request.OperationId);
                                LmServiceProvisioningBatchResult fullResult =
                                    CreateSingleItemResult(request, item);
                                channel.WriteMessage(fullResult);
                                return ToExitCode(item.Status);
                            }
                        }
                        WindowsLmProvisioningPlatform platform =
                            WindowsLmProvisioningPlatform.Create(
                                request.InitiatingSid,
                                request.OperationId);
                        LmServiceProvisioner provisioner = new LmServiceProvisioner(platform);
                        if (request.Operation == LmServiceOperation.EnsureBatch)
                        {
                            using (PipeProvisioningCancellation cancellation =
                                new PipeProvisioningCancellation(channel, request.OperationId))
                            {
                                LmServiceProvisioningBatchResult result = provisioner.EnsureBatch(
                                    request,
                                    cancellation);
                                channel.WriteMessage(result);
                                return ToExitCode(result.Status);
                            }
                        }
                        if (request.Operation == LmServiceOperation.InstallControllerVersion)
                        {
                            LmControllerInstallResult result =
                                provisioner.InstallControllerVersion(request);
                            channel.WriteMessage(result);
                            return ToExitCode(result.Status);
                        }
                        if (request.Operation == LmServiceOperation.RemoveManaged)
                        {
                            LmServiceProvisioningItemResult item =
                                provisioner.RemoveManaged(request);
                            LmServiceProvisioningBatchResult result =
                                CreateSingleItemResult(request, item);
                            channel.WriteMessage(result);
                            return ToExitCode(item.Status);
                        }
                        if (request.Operation == LmServiceOperation.CleanupManaged)
                        {
                            LmServiceProvisioningItemResult item =
                                provisioner.CleanupManaged(request);
                            LmServiceProvisioningBatchResult result =
                                CreateSingleItemResult(request, item);
                            channel.WriteMessage(result);
                            return ToExitCode(item.Status);
                        }
                        if (request.Operation == LmServiceOperation.RemoveAllManaged)
                        {
                            using (PipeProvisioningCancellation cancellation =
                                new PipeProvisioningCancellation(channel, request.OperationId))
                            {
                                LmServiceProvisioningBatchResult result =
                                    provisioner.RemoveAllManaged(request, cancellation);
                                channel.WriteMessage(result);
                                return ToExitCode(result.Status);
                            }
                        }
                    }
                    catch (NotSupportedException ex)
                    {
                        WriteOperationFailure(
                            channel,
                            request,
                            LmServiceProvisioningStatus.UnsupportedController,
                            ex.Message);
                        return ExitUnsupportedController;
                    }
                    catch (Exception ex)
                    {
                        WriteOperationFailure(
                            channel,
                            request,
                            LmServiceProvisioningStatus.Failed,
                            "Не удалось подготовить защищенную среду операции: " +
                                ex.GetType().Name + ".");
                        return ExitOperationFailed;
                    }

                    LmServiceProvisioningBatchResult unsupported =
                        CreateUnsupportedResult(request);
                    channel.WriteMessage(unsupported);
                    return ExitUnsupportedController;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return ExitInvalidRequest;
            }
            catch (InvalidDataException)
            {
                return ExitInvalidRequest;
            }
            catch (IOException)
            {
                return ExitOperationFailed;
            }
            catch
            {
                return ExitOperationFailed;
            }
        }

        private static void WriteOperationFailure(
            NamedPipeProvisioningChannel channel,
            LmServiceProvisioningBatchRequest request,
            LmServiceProvisioningStatus status,
            string message)
        {
            if (request.Operation == LmServiceOperation.InstallControllerVersion)
            {
                channel.WriteMessage(new LmControllerInstallResult
                {
                    Status = status,
                    Message = message,
                    OperationId = request.OperationId,
                    PlanHash = request.PlanHash
                });
                return;
            }

            LmServiceProvisioningBatchResult result = new LmServiceProvisioningBatchResult
            {
                SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                OperationId = request.OperationId,
                PlanHash = request.PlanHash,
                Status = status
            };
            if (request.Items != null && request.Items.Count > 0)
            {
                for (int index = 0; index < request.Items.Count; index++)
                {
                    result.Items.Add(new LmServiceProvisioningItemResult
                    {
                        KktSerial = request.Items[index] == null
                            ? string.Empty
                            : request.Items[index].KktSerial,
                        Status = status,
                        Message = message
                    });
                }
            }
            else if (request.ManagedLocalModules != null &&
                request.ManagedLocalModules.Count > 0)
            {
                for (int index = 0;
                    index < request.ManagedLocalModules.Count;
                    index++)
                {
                    ManagedLocalModuleProvisioningItemRequest item =
                        request.ManagedLocalModules[index];
                    result.Items.Add(new LmServiceProvisioningItemResult
                    {
                        KktSerial = item == null
                            ? string.Empty
                            : item.KktSerial,
                        Status = status,
                        Message = message
                    });
                }
            }
            else if (request.RemovalConfirmation != null)
            {
                result.Items.Add(new LmServiceProvisioningItemResult
                {
                    KktSerial = request.RemovalConfirmation.KktSerial,
                    Status = status,
                    Message = message
                });
            }
            else if (request.CleanupConfirmation != null)
            {
                result.Items.Add(new LmServiceProvisioningItemResult
                {
                    KktSerial = request.CleanupConfirmation.KktSerial,
                    Status = status,
                    Message = message
                });
            }
            else if (request.RemovalConfirmations != null)
            {
                for (int index = 0; index < request.RemovalConfirmations.Count; index++)
                {
                    LmRemovalConfirmation confirmation = request.RemovalConfirmations[index];
                    result.Items.Add(new LmServiceProvisioningItemResult
                    {
                        KktSerial = confirmation == null ? string.Empty : confirmation.KktSerial,
                        Status = status,
                        Message = message
                    });
                }
            }
            channel.WriteMessage(result);
        }

        private static int ToExitCode(LmServiceProvisioningStatus status)
        {
            if (status == LmServiceProvisioningStatus.Succeeded ||
                status == LmServiceProvisioningStatus.Cancelled ||
                status == LmServiceProvisioningStatus.RequiresAttention ||
                status == LmServiceProvisioningStatus.CleanupPending ||
                status == LmServiceProvisioningStatus.RemovalBlocked ||
                status == LmServiceProvisioningStatus.MarkedForDelete ||
                status == LmServiceProvisioningStatus.RemovedLocalArtifactsBindingRetained ||
                status == LmServiceProvisioningStatus.ReadyToInitialize ||
                status == LmServiceProvisioningStatus.SharedLocalModuleRetained)
            {
                return ExitResultSent;
            }
            if (status == LmServiceProvisioningStatus.UnsupportedController ||
                status == LmServiceProvisioningStatus.VersionVerificationPending)
            {
                return ExitUnsupportedController;
            }
            return ExitOperationFailed;
        }

        private static LmServiceProvisioningBatchResult CreateSingleItemResult(
            LmServiceProvisioningBatchRequest request,
            LmServiceProvisioningItemResult item)
        {
            LmServiceProvisioningBatchResult result = new LmServiceProvisioningBatchResult
            {
                SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                OperationId = request.OperationId,
                PlanHash = request.PlanHash,
                Status = item.Status
            };
            result.Items.Add(item);
            return result;
        }

        private static LmServiceProvisioningBatchResult CreateUnsupportedResult(
            LmServiceProvisioningBatchRequest request)
        {
            LmServiceProvisioningBatchResult result = new LmServiceProvisioningBatchResult
            {
                SchemaVersion = ProvisioningRequestValidator.CurrentSchemaVersion,
                OperationId = request.OperationId,
                PlanHash = request.PlanHash,
                Status = LmServiceProvisioningStatus.UnsupportedController
            };

            if (request.Items != null && request.Items.Count > 0)
            {
                for (int index = 0; index < request.Items.Count; index++)
                {
                    result.Items.Add(new LmServiceProvisioningItemResult
                    {
                        KktSerial = request.Items[index] == null ? string.Empty : request.Items[index].KktSerial,
                        Status = LmServiceProvisioningStatus.UnsupportedController,
                        Message = "Операция еще не подключена к проверенному capability profile."
                    });
                }
            }
            else if (request.ManagedLocalModules != null)
            {
                for (int index = 0;
                    index < request.ManagedLocalModules.Count;
                    index++)
                {
                    result.Items.Add(new LmServiceProvisioningItemResult
                    {
                        KktSerial = request.ManagedLocalModules[index] == null
                            ? string.Empty
                            : request.ManagedLocalModules[index].KktSerial,
                        Status = LmServiceProvisioningStatus.UnsupportedController,
                        Message =
                            "Операция не имеет точного capability-профиля ЛМ ЧЗ."
                    });
                }
            }

            return result;
        }
    }
}
