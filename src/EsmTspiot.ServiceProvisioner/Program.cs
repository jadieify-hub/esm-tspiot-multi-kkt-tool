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

                    LmServiceProvisioningBatchResult result = CreateUnsupportedResult(request);
                    channel.WriteMessage(result);
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

            if (request.Items != null)
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

            return result;
        }
    }
}
