using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
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
                if (!File.Exists(_helperPath))
                {
                    reason = "Защищенный helper не найден в папке Provisioner.";
                    return false;
                }
                if (!IsUnderProgramFiles(_applicationRoot) ||
                    HasReparseComponent(_applicationRoot) ||
                    HasReparseComponent(_helperPath) ||
                    !HasProtectedAcl(_applicationRoot) ||
                    !HasProtectedAcl(Path.GetDirectoryName(_helperPath)) ||
                    !HasProtectedAcl(_helperPath))
                {
                    reason = "Для операций со службами распакуйте проверенный пакет в " +
                        @"C:\Program Files\KRS\MultiKKT от имени администратора.";
                    return false;
                }
                if (!IsSplitAdministratorToken())
                {
                    reason = "Текущая учетная запись не имеет административного UAC-токена.";
                    return false;
                }
                VerifyHelperIdentity();
                reason = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                reason = "Проверка helper не пройдена: " + ex.GetType().Name + ".";
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

        private static bool IsUnderProgramFiles(string path)
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetEnvironmentVariable("ProgramW6432")
            };
            for (int index = 0; index < roots.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(roots[index]))
                {
                    string root = Path.GetFullPath(roots[index])
                        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HasReparseComponent(string path)
        {
            string current = Path.GetFullPath(path);
            string root = Path.GetPathRoot(current).TrimEnd(Path.DirectorySeparatorChar);
            while (!string.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
                if (string.Equals(
                    current.TrimEnd(Path.DirectorySeparatorChar),
                    root,
                    StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = Path.GetDirectoryName(current);
            }
            return false;
        }

        private static bool HasProtectedAcl(string path)
        {
            FileSystemSecurity security = File.Exists(path)
                ? (FileSystemSecurity)new FileInfo(path).GetAccessControl()
                : new DirectoryInfo(path).GetAccessControl();
            SecurityIdentifier owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null ||
                (!owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) &&
                 !owner.IsWellKnown(WellKnownSidType.LocalSystemSid) &&
                 !string.Equals(
                    owner.Value,
                    "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",
                    StringComparison.Ordinal)))
            {
                return false;
            }
            AuthorizationRuleCollection rules = security.GetAccessRules(
                true,
                true,
                typeof(SecurityIdentifier));
            FileSystemRights unsafeRights = FileSystemRights.Write |
                FileSystemRights.Modify |
                FileSystemRights.FullControl |
                FileSystemRights.ChangePermissions |
                FileSystemRights.TakeOwnership;
            for (int index = 0; index < rules.Count; index++)
            {
                FileSystemAccessRule rule = rules[index] as FileSystemAccessRule;
                SecurityIdentifier sid = rule == null
                    ? null
                    : rule.IdentityReference as SecurityIdentifier;
                if (rule != null && sid != null &&
                    rule.AccessControlType == AccessControlType.Allow &&
                    (rule.FileSystemRights & unsafeRights) != 0 &&
                    !sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) &&
                    !sid.IsWellKnown(WellKnownSidType.LocalSystemSid) &&
                    !string.Equals(
                        sid.Value,
                        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsSplitAdministratorToken()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query))
            {
                int elevationType = ReadTokenInt32(identity.Token, TokenElevationType);
                return elevationType == TokenElevationTypeLimited ||
                    elevationType == TokenElevationTypeFull;
            }
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
