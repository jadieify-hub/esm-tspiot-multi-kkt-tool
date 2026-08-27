using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class NamedPipeProvisioningChannel : IDisposable
    {
        internal const int MaximumMessageBytes = 1024 * 1024;

        private readonly NamedPipeClientStream _stream;
        private long _lastControlSequence;

        private NamedPipeProvisioningChannel(NamedPipeClientStream stream, string authenticatedServerSid)
        {
            _stream = stream;
            AuthenticatedServerSid = authenticatedServerSid;
        }

        internal string AuthenticatedServerSid { get; private set; }

        internal static NamedPipeProvisioningChannel ConnectAndAuthenticate(string pipeName, int timeoutMilliseconds)
        {
            if (!ProvisionerCommandLine.IsGuidN(pipeName))
            {
                throw new UnauthorizedAccessException("Invalid one-shot pipe name.");
            }

            NamedPipeClientStream stream = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.None);
            try
            {
                stream.Connect(timeoutMilliseconds);
                ProvisioningPeerEvidence evidence = NativePeerEvidenceReader.ReadServer(stream);
                ValidationResult authentication = ProvisioningPipePeerAuthenticator.ValidateServer(evidence);
                if (!authentication.IsValid)
                {
                    throw new UnauthorizedAccessException(authentication.JoinMessages());
                }

                return new NamedPipeProvisioningChannel(stream, evidence.ActualSid);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        internal T ReadMessage<T>()
        {
            byte[] lengthBytes = ReadExact(sizeof(int));
            int length = BitConverter.ToInt32(lengthBytes, 0);
            if (length < 1 || length > MaximumMessageBytes)
            {
                throw new InvalidDataException("Provisioning message length is outside the allowed limit.");
            }

            byte[] payload = ReadExact(length);
            using (MemoryStream memory = new MemoryStream(payload, false))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                object value = serializer.ReadObject(memory);
                if (!(value is T))
                {
                    throw new InvalidDataException("Provisioning message has an unexpected type.");
                }

                return (T)value;
            }
        }

        internal void WriteMessage<T>(T message)
        {
            byte[] payload;
            using (MemoryStream memory = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(memory, message);
                if (memory.Length < 1 || memory.Length > MaximumMessageBytes)
                {
                    throw new InvalidDataException("Provisioning message length is outside the allowed limit.");
                }

                payload = memory.ToArray();
            }

            byte[] length = BitConverter.GetBytes(payload.Length);
            _stream.Write(length, 0, length.Length);
            _stream.Write(payload, 0, payload.Length);
            _stream.Flush();
        }

        internal bool ValidateControlMessage(
            LmProvisioningControlMessage message,
            string operationId)
        {
            if (message == null ||
                message.SchemaVersion != ProvisioningRequestValidator.CurrentSchemaVersion ||
                message.Kind != LmProvisioningControlKind.CancelAfterCurrentItem ||
                !string.Equals(message.OperationId, operationId, StringComparison.OrdinalIgnoreCase) ||
                message.Sequence <= _lastControlSequence)
            {
                return false;
            }

            _lastControlSequence = message.Sequence;
            return true;
        }

        public void Dispose()
        {
            _stream.Dispose();
        }

        private byte[] ReadExact(int length)
        {
            byte[] result = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = _stream.Read(result, offset, length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("Provisioning pipe was closed before the message completed.");
                }

                offset += read;
            }

            return result;
        }
    }

    internal static class NativePeerEvidenceReader
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint TokenQuery = 0x0008;
        private const int TokenUser = 1;
        private const string ExpectedCompany = "KRS";
        private const string ExpectedProduct = "Управление ККТ в ЕСМ/ТС ПИоТ";

        internal static ProvisioningPeerEvidence ReadServer(NamedPipeClientStream stream)
        {
            uint serverPid;
            if (!GetNamedPipeServerProcessId(stream.SafePipeHandle, out serverPid) || serverPid == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot resolve named-pipe server PID.");
            }

            uint flags;
            uint outBufferSize;
            uint inBufferSize;
            uint maxInstances;
            if (!GetNamedPipeInfo(
                stream.SafePipeHandle,
                out flags,
                out outBufferSize,
                out inBufferSize,
                out maxInstances))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read named-pipe server limits.");
            }

            string actualPath = GetProcessImagePath((int)serverPid);
            string actualSid = GetProcessSid((int)serverPid);
            string helperPath = Process.GetCurrentProcess().MainModule.FileName;
            string helperDirectory = Path.GetDirectoryName(helperPath);
            string appDirectory = Directory.GetParent(helperDirectory).FullName;
            bool allowedPath = IsAllowedMainImage(actualPath, appDirectory);
            FileVersionInfo mainVersion = FileVersionInfo.GetVersionInfo(actualPath);
            FileVersionInfo helperVersion = FileVersionInfo.GetVersionInfo(helperPath);
            bool expectedMetadata =
                string.Equals(mainVersion.CompanyName, ExpectedCompany, StringComparison.Ordinal) &&
                string.Equals(mainVersion.ProductName, ExpectedProduct, StringComparison.Ordinal) &&
                string.Equals(mainVersion.FileVersion, helperVersion.FileVersion, StringComparison.Ordinal);

            string currentSid = WindowsIdentity.GetCurrent().User.Value;
            return new ProvisioningPeerEvidence
            {
                ExpectedSid = currentSid,
                ActualSid = actualSid,
                ExpectedImagePath = allowedPath ? Path.GetFullPath(actualPath) : string.Empty,
                ActualImagePath = Path.GetFullPath(actualPath),
                ActualProcessId = (int)serverPid,
                ExpectedProcessId = 0,
                IsImagePathProtected = PeerImagePathSafety.IsProtected(actualPath),
                HasExpectedMetadata = expectedMetadata,
                HasReparseComponent = PeerImagePathSafety.HasReparseComponent(actualPath),
                IsHighIntegrity = false,
                MaxServerInstances = maxInstances > int.MaxValue ? -1 : (int)maxInstances,
                IsSecondServerAttempt = false
            };
        }

        private static bool IsAllowedMainImage(string imagePath, string appDirectory)
        {
            string fullPath = Path.GetFullPath(imagePath);
            if (!string.Equals(
                Path.GetDirectoryName(fullPath),
                Path.GetFullPath(appDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fileName = Path.GetFileName(fullPath);
            return string.Equals(fileName, "EsmTspiot.Legacy.WinForms.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "EsmTspiot.Modern.WinForms.exe", StringComparison.OrdinalIgnoreCase) ||
                (fileName.StartsWith("MultiKKT-ESM-TSPioT-v", StringComparison.OrdinalIgnoreCase) &&
                 fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        }

        private static string GetProcessImagePath(int processId)
        {
            using (Process process = Process.GetProcessById(processId))
            {
                return process.MainModule.FileName;
            }
        }

        private static string GetProcessSid(int processId)
        {
            IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (processHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open named-pipe peer process.");
            }

            try
            {
                IntPtr tokenHandle;
                if (!OpenProcessToken(processHandle, TokenQuery, out tokenHandle))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open named-pipe peer token.");
                }
                try
                {
                    int required;
                    GetTokenInformation(tokenHandle, TokenUser, IntPtr.Zero, 0, out required);
                    IntPtr buffer = Marshal.AllocHGlobal(required);
                    try
                    {
                        if (!GetTokenInformation(tokenHandle, TokenUser, buffer, required, out required))
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read named-pipe peer SID.");
                        }

                        TokenUserValue tokenUser = (TokenUserValue)Marshal.PtrToStructure(
                            buffer,
                            typeof(TokenUserValue));
                        return new SecurityIdentifier(tokenUser.User.Sid).Value;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    CloseHandle(tokenHandle);
                }
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SidAndAttributes
        {
            internal IntPtr Sid;
            internal uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenUserValue
        {
            internal SidAndAttributes User;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeInfo(
            SafePipeHandle pipe,
            out uint flags,
            out uint outBufferSize,
            out uint inBufferSize,
            out uint maxInstances);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            int tokenInformationClass,
            IntPtr tokenInformation,
            int tokenInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }

    internal static class PeerImagePathSafety
    {
        private const FileSystemRights UnsafeRights =
            FileSystemRights.Write |
            FileSystemRights.Modify |
            FileSystemRights.FullControl |
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership;

        internal static bool IsProtected(string imagePath)
        {
            try
            {
                string fullPath = Path.GetFullPath(imagePath);
                string directory = Path.GetDirectoryName(fullPath);
                return IsAclProtected(new FileInfo(fullPath).GetAccessControl()) &&
                    IsAclProtected(new DirectoryInfo(directory).GetAccessControl());
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException || ex is SystemException)
                {
                    return false;
                }
                throw;
            }
        }

        internal static bool HasReparseComponent(string path)
        {
            try
            {
                string current = Path.GetFullPath(path);
                while (!string.IsNullOrEmpty(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }

                    string parent = Path.GetDirectoryName(current);
                    if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    current = parent;
                }

                return false;
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException || ex is SystemException)
                {
                    return true;
                }
                throw;
            }
        }

        private static bool IsAclProtected(FileSystemSecurity security)
        {
            AuthorizationRuleCollection rules = security.GetAccessRules(
                true,
                true,
                typeof(SecurityIdentifier));
            for (int index = 0; index < rules.Count; index++)
            {
                FileSystemAccessRule rule = rules[index] as FileSystemAccessRule;
                if (rule == null || rule.AccessControlType != AccessControlType.Allow ||
                    (rule.FileSystemRights & UnsafeRights) == 0)
                {
                    continue;
                }

                SecurityIdentifier sid = rule.IdentityReference as SecurityIdentifier;
                if (sid == null || !IsPrivilegedSid(sid.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPrivilegedSid(string sid)
        {
            return string.Equals(sid, "S-1-5-18", StringComparison.Ordinal) ||
                string.Equals(sid, "S-1-5-32-544", StringComparison.Ordinal) ||
                string.Equals(
                    sid,
                    "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",
                    StringComparison.Ordinal);
        }
    }
}
