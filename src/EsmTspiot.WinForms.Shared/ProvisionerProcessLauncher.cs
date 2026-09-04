using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class ProvisionerProcessLauncher
    {
        private const int TokenUser = 1;
        private const int TokenElevationType = 18;
        private const int TokenIntegrityLevel = 25;
        private const int TokenElevationTypeFull = 2;
        private const int TokenElevationTypeLimited = 3;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint TokenQuery = 0x0008;
        private readonly string _applicationRoot;
        private readonly string _helperPath;

        internal ProvisionerProcessLauncher()
        {
            _applicationRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar);
            _helperPath = Path.Combine(
                _applicationRoot,
                "Provisioner",
                "EsmTspiot.ServiceProvisioner.exe");
        }

        internal string HelperPath
        {
            get { return _helperPath; }
        }

        internal bool CanLaunch(out string reason)
        {
            try
            {
                string missingFile = FindMissingClosureFile();
                if (missingFile != null)
                {
                    reason = "В папке Provisioner рядом с программой нет файла " +
                        missingFile + ". Распакуйте архив целиком.";
                    return false;
                }
                if (!HasAdministrativeToken())
                {
                    reason = "Текущая учетная запись не имеет административных прав.";
                    return false;
                }
                VerifyHelperIdentity();
                reason = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                reason = "Проверка helper не пройдена: " +
                    (ex is InvalidDataException
                        ? ex.Message
                        : ex.GetType().Name + ".");
                return false;
            }
        }

        internal Process Launch(string pipeName, string operationId)
        {
            string reason;
            if (!CanLaunch(out reason))
            {
                throw new InvalidOperationException(reason);
            }
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = _helperPath,
                Arguments = "--pipe " + pipeName + " --operation " + operationId,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(_helperPath),
                WindowStyle = ProcessWindowStyle.Hidden
            };
            try
            {
                Process process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Не удалось запустить helper.");
                }
                return process;
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                {
                    throw new OperationCanceledException("Запрос UAC отменен пользователем.", ex);
                }
                throw;
            }
        }

        internal void AuthenticateConnectedHelper(
            Microsoft.Win32.SafeHandles.SafePipeHandle pipeHandle,
            int expectedProcessId)
        {
            uint actualProcessId;
            if (!GetNamedPipeClientProcessId(pipeHandle, out actualProcessId) ||
                actualProcessId != unchecked((uint)expectedProcessId))
            {
                throw new UnauthorizedAccessException("К pipe подключился неожиданный процесс.");
            }
            string path = GetProcessPath(expectedProcessId);
            if (!string.Equals(
                Path.GetFullPath(path),
                _helperPath,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Образ подключившегося helper не совпадает.");
            }
            string currentSid = WindowsIdentity.GetCurrent().User.Value;
            if (!string.Equals(GetProcessSid(expectedProcessId), currentSid, StringComparison.Ordinal) ||
                !IsHighIntegrity(expectedProcessId))
            {
                throw new UnauthorizedAccessException(
                    "Helper запущен не от ожидаемого пользователя или без high integrity.");
            }
            VerifyHelperIdentity();
        }

        private void VerifyHelperIdentity()
        {
            string expectedHash = ProvisionerIntegrity.ExpectedSha256;
            if (string.IsNullOrEmpty(expectedHash) || expectedHash.Length != 64 ||
                !FixedTimeEquals(expectedHash, ComputeSha256(_helperPath)))
            {
                throw new InvalidDataException("SHA-256 helper не совпадает со встроенным значением.");
            }
            IList<KeyValuePair<string, string>> closure =
                ParseExpectedClosure(ProvisionerIntegrity.ExpectedClosureSha256);
            if (closure.Count == 0)
            {
                throw new InvalidDataException("Встроенный список файлов helper пуст.");
            }
            string helperDirectory = Path.GetDirectoryName(_helperPath);
            for (int index = 0; index < closure.Count; index++)
            {
                string closurePath = Path.Combine(helperDirectory, closure[index].Key);
                if (!File.Exists(closurePath) ||
                    !FixedTimeEquals(closure[index].Value, ComputeSha256(closurePath)))
                {
                    throw new InvalidDataException(
                        "SHA-256 файла Provisioner\\" + closure[index].Key +
                        " не совпадает со встроенным значением.");
                }
            }
            FileVersionInfo helper = FileVersionInfo.GetVersionInfo(_helperPath);
            FileVersionInfo main = FileVersionInfo.GetVersionInfo(
                Process.GetCurrentProcess().MainModule.FileName);
            if (!string.Equals(helper.CompanyName, "KRS", StringComparison.Ordinal) ||
                !string.Equals(
                    helper.ProductName,
                    "Управление ККТ в ЕСМ/ТС ПИоТ",
                    StringComparison.Ordinal) ||
                !string.Equals(helper.FileVersion, main.FileVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Метаданные helper не совпадают с приложением.");
            }
        }

        // The embedded list has the form "name=SHA256|name=SHA256"; it is
        // generated at build time from the exact helper closure.
        internal static IList<KeyValuePair<string, string>> ParseExpectedClosure(
            string value)
        {
            List<KeyValuePair<string, string>> result =
                new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(value))
            {
                return result;
            }
            string[] entries = value.Split('|');
            for (int index = 0; index < entries.Length; index++)
            {
                string entry = entries[index].Trim();
                if (entry.Length == 0)
                {
                    continue;
                }
                int separator = entry.IndexOf('=');
                if (separator <= 0 || separator == entry.Length - 1)
                {
                    throw new InvalidDataException(
                        "Встроенный список файлов helper повреждён.");
                }
                string name = entry.Substring(0, separator).Trim();
                string hash = entry.Substring(separator + 1).Trim();
                if (name.Length == 0 ||
                    name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    hash.Length != 64)
                {
                    throw new InvalidDataException(
                        "Встроенный список файлов helper повреждён.");
                }
                result.Add(new KeyValuePair<string, string>(name, hash));
            }
            return result;
        }

        private string FindMissingClosureFile()
        {
            if (!File.Exists(_helperPath))
            {
                return Path.GetFileName(_helperPath);
            }
            IList<KeyValuePair<string, string>> closure =
                ParseExpectedClosure(ProvisionerIntegrity.ExpectedClosureSha256);
            string helperDirectory = Path.GetDirectoryName(_helperPath);
            for (int index = 0; index < closure.Count; index++)
            {
                if (!File.Exists(Path.Combine(helperDirectory, closure[index].Key)))
                {
                    return closure[index].Key;
                }
            }
            return null;
        }

        // Помощник поднимается через runas. Если программа уже запущена от
        // администратора, окна UAC не будет вообще, и обещать оператору
        // подтверждение нельзя: он ждёт запрос, которого не появится, и
        // считает, что стадия замерла.
        internal static bool IsAlreadyElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(
                    WindowsBuiltInRole.Administrator);
            }
        }

        private static bool HasAdministrativeToken()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent(
                TokenAccessLevels.Query | TokenAccessLevels.Duplicate))
            {
                int elevationType = ReadTokenInt32(identity.Token, TokenElevationType);
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                bool isAdministrator = principal.IsInRole(
                    WindowsBuiltInRole.Administrator);
                return HasAdministrativeToken(elevationType, isAdministrator);
            }
        }

        private static bool HasAdministrativeToken(
            int elevationType,
            bool isAdministrator)
        {
            return elevationType == TokenElevationTypeLimited ||
                elevationType == TokenElevationTypeFull ||
                isAdministrator;
        }

        private static bool IsHighIntegrity(int processId)
        {
            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (process == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try
            {
                IntPtr token;
                if (!OpenProcessToken(process, TokenQuery, out token))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                try
                {
                    int required;
                    GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out required);
                    IntPtr buffer = Marshal.AllocHGlobal(required);
                    try
                    {
                        if (!GetTokenInformation(
                            token,
                            TokenIntegrityLevel,
                            buffer,
                            required,
                            out required))
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        }
                        SidAndAttributes label = (SidAndAttributes)Marshal.PtrToStructure(
                            buffer,
                            typeof(SidAndAttributes));
                        SecurityIdentifier sid = new SecurityIdentifier(label.Sid);
                        string[] parts = sid.Value.Split('-');
                        int level = int.Parse(parts[parts.Length - 1], CultureInfo.InvariantCulture);
                        return level >= 12288;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    CloseHandle(token);
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }

        private static string GetProcessSid(int processId)
        {
            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (process == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try
            {
                IntPtr token;
                if (!OpenProcessToken(process, TokenQuery, out token))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                try
                {
                    int required;
                    GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out required);
                    IntPtr buffer = Marshal.AllocHGlobal(required);
                    try
                    {
                        if (!GetTokenInformation(token, TokenUser, buffer, required, out required))
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        }
                        TokenUserValue user = (TokenUserValue)Marshal.PtrToStructure(
                            buffer,
                            typeof(TokenUserValue));
                        return new SecurityIdentifier(user.User.Sid).Value;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    CloseHandle(token);
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }

        private static string GetProcessPath(int processId)
        {
            IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (process == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try
            {
                int capacity = 32768;
                StringBuilder path = new StringBuilder(capacity);
                if (!QueryFullProcessImageNameW(process, 0, path, ref capacity))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return path.ToString();
            }
            finally
            {
                CloseHandle(process);
            }
        }

        private static int ReadTokenInt32(IntPtr token, int informationClass)
        {
            int value;
            int returned;
            if (!GetTokenInformation(
                token,
                informationClass,
                out value,
                sizeof(int),
                out returned))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return value;
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(stream);
                StringBuilder result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    result.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }
                return result.ToString();
            }
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= char.ToUpperInvariant(left[index]) ^
                    char.ToUpperInvariant(right[index]);
            }
            return difference == 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SidAndAttributes
        {
            internal IntPtr Sid;
            private uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenUserValue
        {
            internal SidAndAttributes User;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(
            Microsoft.Win32.SafeHandles.SafePipeHandle pipe,
            out uint clientProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(
            IntPtr process,
            uint flags,
            StringBuilder path,
            ref int size);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(
            IntPtr process,
            uint access,
            out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr token,
            int informationClass,
            IntPtr information,
            int informationLength,
            out int returnLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr token,
            int informationClass,
            out int information,
            int informationLength,
            out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
