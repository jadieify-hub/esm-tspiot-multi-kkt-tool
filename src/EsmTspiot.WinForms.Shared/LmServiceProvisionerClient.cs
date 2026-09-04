using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LmServiceProvisionerClient : ILmServiceProvisioner
    {
        private const int LegacySchemaVersion = 1;
        private const int DirectSchemaVersion = 2;
        private const int MaximumMessageBytes = 1024 * 1024;
        private readonly ProvisionerProcessLauncher _launcher;

        internal LmServiceProvisionerClient()
            : this(new ProvisionerProcessLauncher())
        {
        }

        internal LmServiceProvisionerClient(ProvisionerProcessLauncher launcher)
        {
            if (launcher == null)
            {
                throw new ArgumentNullException("launcher");
            }
            _launcher = launcher;
        }

        public Task<LmControllerInstallResult> InstallControllerVersionAsync(
            LmControllerInstallerSelection selection,
            string operationId,
            string planHash,
            CancellationToken cancellation)
        {
            LmServiceProvisioningBatchRequest request = CreateRequest(
                LmServiceOperation.InstallControllerVersion,
                operationId,
                planHash);
            request.InstallerSelection = selection;
            return InvokeAsync<LmControllerInstallResult>(request, cancellation);
        }

        internal Task<LmServiceProvisioningBatchResult> EnsureDirectControllersAsync(
            IList<DirectControllerProvisioningItemRequest> items,
            string operationId,
            CancellationToken cancellation)
        {
            LmServiceProvisioningBatchRequest request = CreateRequest(
                LmServiceOperation.EnsureDirectControllers,
                operationId,
                string.Empty);
            if (items != null)
            {
                for (int index = 0; index < items.Count; index++)
                {
                    request.DirectControllers.Add(items[index]);
                }
            }
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            return InvokeAsync<LmServiceProvisioningBatchResult>(
                request,
                cancellation);
        }

        internal Task<LmServiceProvisioningBatchResult> RemoveAllDirectControllersAsync(
            IList<DirectControllerProvisioningItemRequest> items,
            string operationId,
            CancellationToken cancellation)
        {
            LmServiceProvisioningBatchRequest request = CreateRequest(
                LmServiceOperation.RemoveAllDirectControllers,
                operationId,
                string.Empty);
            if (items != null)
            {
                for (int index = 0; index < items.Count; index++)
                {
                    request.DirectControllers.Add(items[index]);
                }
            }
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            return InvokeAsync<LmServiceProvisioningBatchResult>(
                request,
                cancellation);
        }

        public async Task<LmServiceProvisioningItemResult> RemoveAsync(
            LmRemovalConfirmation confirmation,
            string operationId,
            string planHash,
            CancellationToken cancellation)
        {
            LmServiceProvisioningBatchRequest request = CreateRequest(
                LmServiceOperation.RemoveManaged,
                operationId,
                planHash);
            request.RemovalConfirmation = confirmation;
            LmServiceProvisioningBatchResult result =
                await InvokeAsync<LmServiceProvisioningBatchResult>(
                    request,
                    cancellation).ConfigureAwait(false);
            return ReadSingleItem(result, confirmation == null ? null : confirmation.KktSerial);
        }

        public Task<LmServiceProvisioningBatchResult> RemoveAllAsync(
            IList<LmRemovalConfirmation> confirmations,
            string operationId,
            string planHash,
            CancellationToken cancellation)
        {
            LmServiceProvisioningBatchRequest request = CreateRequest(
                LmServiceOperation.RemoveAllManaged,
                operationId,
                planHash);
            if (confirmations != null)
            {
                for (int index = 0; index < confirmations.Count; index++)
                {
                    request.RemovalConfirmations.Add(confirmations[index]);
                }
            }
            return InvokeAsync<LmServiceProvisioningBatchResult>(request, cancellation);
        }

        public async Task<LmServiceProvisioningItemResult> CleanupAsync(
            LmCleanupConfirmation confirmation,
            string operationId,
            string planHash,
            CancellationToken cancellation)
        {
            LmServiceProvisioningBatchRequest request = CreateRequest(
                LmServiceOperation.CleanupManaged,
                operationId,
                planHash);
            request.CleanupConfirmation = confirmation;
            LmServiceProvisioningBatchResult result =
                await InvokeAsync<LmServiceProvisioningBatchResult>(
                    request,
                    cancellation).ConfigureAwait(false);
            return ReadSingleItem(result, confirmation == null ? null : confirmation.KktSerial);
        }

        internal bool IsAvailable(out string reason)
        {
            return _launcher.CanLaunch(out reason);
        }

        internal Task<LmServiceProvisioningBatchResult> ExecuteRawBatchAsync(
            LmServiceProvisioningBatchRequest request,
            CancellationToken cancellation)
        {
            return InvokeAsync<LmServiceProvisioningBatchResult>(
                request,
                cancellation);
        }

        private async Task<T> InvokeAsync<T>(
            LmServiceProvisioningBatchRequest request,
            CancellationToken cancellation)
        {
            ValidateBeforeElevation(request);
            cancellation.ThrowIfCancellationRequested();

            string pipeName = Guid.NewGuid().ToString("N");
            using (NamedPipeServerStream pipe = CreateServer(pipeName))
            using (Process helper = _launcher.Launch(pipeName, request.OperationId))
            {
                try
                {
                    await WaitForConnectionAsync(pipe, cancellation).ConfigureAwait(false);
                    _launcher.AuthenticateConnectedHelper(pipe.SafePipeHandle, helper.Id);

                    object writeGate = new object();
                    WriteMessage(pipe, request, writeGate);
                    long sequence = 0;
                    using (CancellationTokenRegistration registration = cancellation.Register(
                        delegate
                        {
                            try
                            {
                                WriteMessage(
                                    pipe,
                                    new LmProvisioningControlMessage
                                    {
                                        SchemaVersion = request.SchemaVersion,
                                        OperationId = request.OperationId,
                                        Sequence = Interlocked.Increment(ref sequence),
                                        Kind = LmProvisioningControlKind.CancelAfterCurrentItem
                                    },
                                    writeGate);
                            }
                            catch (IOException)
                            {
                            }
                            catch (ObjectDisposedException)
                            {
                            }
                        }))
                    {
                        T result = await Task.Run(delegate { return ReadMessage<T>(pipe); })
                            .ConfigureAwait(false);
                        ValidateResult(request, result);
                        return result;
                    }
                }
                catch (IOException ex)
                {
                    throw CreatePrematureExitException(helper, ex);
                }
            }
        }

        internal static IOException CreatePrematureExitException(
            Process helper,
            IOException inner)
        {
            try
            {
                if (helper != null &&
                    (helper.HasExited || helper.WaitForExit(1500)))
                {
                    return new IOException(DescribePrematureExit(helper.ExitCode), inner);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (SystemException)
            {
            }
            return new IOException(
                "Канал с helper был разорван до получения ответа.",
                inner);
        }

        private static string DescribePrematureExit(int exitCode)
        {
            if (exitCode == 2)
            {
                return "Helper отклонил аутентификацию защищенного канала или запрос (code 2).";
            }
            if (exitCode == 3)
            {
                return "Helper завершил операцию до ответа (code 3).";
            }
            return "Helper завершился до ответа (code " +
                exitCode.ToString() + ").";
        }

        private static LmServiceProvisioningBatchRequest CreateRequest(
            LmServiceOperation operation,
            string operationId,
            string planHash)
        {
            return new LmServiceProvisioningBatchRequest
            {
                SchemaVersion = IsDirectControllerOperation(operation)
                    ? DirectSchemaVersion
                    : LegacySchemaVersion,
                Operation = operation,
                OperationId = operationId,
                InitiatingSid = WindowsIdentity.GetCurrent().User.Value,
                PlanHash = planHash
            };
        }

        private static void ValidateBeforeElevation(LmServiceProvisioningBatchRequest request)
        {
            if (request == null || !IsGuidN(request.OperationId))
            {
                throw new ArgumentException("operationId должен быть GUID в формате N.");
            }
            string actualHash = CanonicalLmPlanHasher.Compute(request);
            if (!CanonicalLmPlanHasher.FixedTimeEqualsHex(request.PlanHash, actualHash))
            {
                throw new InvalidDataException(
                    "Показанный план изменился до запуска операции со службами.");
            }
        }

        internal static NamedPipeServerStream CreateServer(string pipeName)
        {
            PipeSecurity security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            AddPipeRule(security, WindowsIdentity.GetCurrent().User);
            AddPipeRule(
                security,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
            AddPipeRule(
                security,
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
            PipeOptions options = PipeOptions.Asynchronous;
#if NETFRAMEWORK
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                options,
                0,
                0,
                security);
#else
            return NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                options,
                0,
                0,
                security);
#endif
        }

        private static void AddPipeRule(PipeSecurity security, SecurityIdentifier sid)
        {
            security.AddAccessRule(new PipeAccessRule(
                sid,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }

        internal static Task WaitForConnectionAsync(
            NamedPipeServerStream pipe,
            CancellationToken cancellation)
        {
            TaskCompletionSource<object> completion =
                new TaskCompletionSource<object>();
            CancellationTokenRegistration registration = cancellation.Register(
                delegate
                {
                    completion.TrySetCanceled();
                    try
                    {
                        pipe.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                });
            try
            {
                pipe.BeginWaitForConnection(
                    delegate(IAsyncResult result)
                    {
                        try
                        {
                            pipe.EndWaitForConnection(result);
                            completion.TrySetResult(null);
                        }
                        catch (ObjectDisposedException)
                        {
                            completion.TrySetCanceled();
                        }
                        catch (Exception ex)
                        {
                            completion.TrySetException(ex);
                        }
                        finally
                        {
                            registration.Dispose();
                        }
                    },
                    null);
            }
            catch
            {
                registration.Dispose();
                throw;
            }
            return completion.Task;
        }

        internal static void WriteMessage<T>(Stream stream, T message, object writeGate)
        {
            byte[] payload;
            using (MemoryStream memory = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(memory, message);
                if (memory.Length < 1 || memory.Length > MaximumMessageBytes)
                {
                    throw new InvalidDataException("Размер IPC-сообщения недопустим.");
                }
                payload = memory.ToArray();
            }

            lock (writeGate)
            {
                byte[] length = BitConverter.GetBytes(payload.Length);
                stream.Write(length, 0, length.Length);
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }
        }

        internal static T ReadMessage<T>(Stream stream)
        {
            int length = BitConverter.ToInt32(ReadExact(stream, sizeof(int)), 0);
            if (length < 1 || length > MaximumMessageBytes)
            {
                throw new InvalidDataException("Размер IPC-сообщения недопустим.");
            }
            using (MemoryStream memory = new MemoryStream(ReadExact(stream, length), false))
            {
                object value = new DataContractJsonSerializer(typeof(T)).ReadObject(memory);
                if (!(value is T))
                {
                    throw new InvalidDataException("Helper вернул результат неожиданного типа.");
                }
                return (T)value;
            }
        }

        private static byte[] ReadExact(Stream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("Helper закрыл IPC до отправки результата.");
                }
                offset += read;
            }
            return buffer;
        }

        internal static void ValidateResult<T>(
            LmServiceProvisioningBatchRequest request,
            T result)
        {
            string operationId;
            string planHash;
            LmServiceProvisioningBatchResult batch =
                result as LmServiceProvisioningBatchResult;
            if (batch != null)
            {
                if (batch.SchemaVersion != request.SchemaVersion)
                {
                    throw new InvalidDataException("Helper вернул неизвестную версию схемы.");
                }
                operationId = batch.OperationId;
                planHash = batch.PlanHash;
            }
            else
            {
                LmControllerInstallResult install = result as LmControllerInstallResult;
                if (install == null)
                {
                    throw new InvalidDataException("Helper вернул результат неожиданного типа.");
                }
                operationId = install.OperationId;
                planHash = install.PlanHash;
            }

            if (!string.Equals(operationId, request.OperationId, StringComparison.OrdinalIgnoreCase) ||
                !CanonicalLmPlanHasher.FixedTimeEqualsHex(planHash, request.PlanHash))
            {
                throw new InvalidDataException("Ответ helper относится к другой операции.");
            }
        }

        private static LmServiceProvisioningItemResult ReadSingleItem(
            LmServiceProvisioningBatchResult result,
            string expectedSerial)
        {
            if (result == null || result.Items == null || result.Items.Count != 1 ||
                !string.Equals(
                    result.Items[0].KktSerial,
                    expectedSerial,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Helper вернул неоднозначный результат операции.");
            }
            return result.Items[0];
        }

        private static bool IsGuidN(string value)
        {
            Guid parsed;
            return value != null && value.Length == 32 &&
                Guid.TryParseExact(value, "N", out parsed);
        }


        private static bool IsDirectControllerOperation(LmServiceOperation operation)
        {
            return operation == LmServiceOperation.EnsureDirectControllers ||
                operation == LmServiceOperation.RestartDirectController ||
                operation == LmServiceOperation.RemoveDirectController ||
                operation == LmServiceOperation.RemoveAllDirectControllers;
        }
    }
}
