using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal enum ProtectedDirectoryKind
    {
        Inventory = 1,
        Profile = 2,
        Operations = 3,
        InstallerStaging = 4
    }

    internal interface IPathSafety
    {
        ValidationResult Validate(string path, string requiredRoot);
        ValidationResult ValidateProtected(string path, string requiredRoot, string allowedWriterSid);
        void EnsureProtectedDirectory(
            string path,
            ProtectedDirectoryKind kind,
            string readOnlySid,
            string serviceSid);
    }

    internal sealed class PathSafety : IPathSafety
    {
        private static readonly SecurityIdentifier SystemSid =
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier AdministratorsSid =
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier TrustedInstallerSid =
            new SecurityIdentifier(
                "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
        private const FileSystemRights UnsafeRights =
            FileSystemRights.Write |
            FileSystemRights.Modify |
            FileSystemRights.FullControl |
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership |
            FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles;

        public ValidationResult Validate(string path, string requiredRoot)
        {
            ValidationResult result = new ValidationResult();
            string fullRoot;
            string fullPath;
            try
            {
                fullRoot = NormalizeDirectory(requiredRoot);
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                result.Add("Некорректный локальный путь: " + ex.GetType().Name + ".");
                return result;
            }

            if (!IsUnderRoot(fullPath, fullRoot))
            {
                result.Add("Путь выходит за разрешенный корень.");
                return result;
            }
            string relative = fullPath.Substring(fullRoot.TrimEnd(Path.DirectorySeparatorChar).Length);
            if (relative.IndexOf(':') >= 0)
            {
                result.Add("Альтернативные потоки NTFS в защищенных путях запрещены.");
                return result;
            }

            string current = fullPath;
            while (IsUnderRoot(current, fullRoot))
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        result.Add("В защищенном пути обнаружен reparse point.");
                        return result;
                    }
                }

                if (string.Equals(current.TrimEnd(Path.DirectorySeparatorChar),
                    fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(current))
                {
                    break;
                }
            }

            return result;
        }

        public ValidationResult ValidateProtected(
            string path,
            string requiredRoot,
            string allowedWriterSid)
        {
            ValidationResult result = Validate(path, requiredRoot);
            if (!result.IsValid)
            {
                return result;
            }

            string fullRoot = Path.GetFullPath(requiredRoot).TrimEnd(Path.DirectorySeparatorChar);
            string current = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            while (IsUnderRoot(current, fullRoot))
            {
                try
                {
                    FileSystemSecurity security = File.Exists(current)
                        ? (FileSystemSecurity)new FileInfo(current).GetAccessControl()
                        : Directory.Exists(current)
                            ? new DirectoryInfo(current).GetAccessControl()
                            : null;
                    if (security != null && !IsSecurityProtected(security, allowedWriterSid))
                    {
                        result.Add("Защищенный путь допускает изменение непривилегированной учетной записью.");
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is UnauthorizedAccessException) && !(ex is SystemException))
                    {
                        throw;
                    }
                    result.Add("Не удалось проверить DACL защищенного пути: " + ex.GetType().Name + ".");
                    return result;
                }

                if (string.Equals(current, fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(current))
                {
                    break;
                }
            }

            return result;
        }

        public void EnsureProtectedDirectory(
            string path,
            ProtectedDirectoryKind kind,
            string readOnlySid,
            string serviceSid)
        {
            Directory.CreateDirectory(path);
            DirectorySecurity security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(AdministratorsSid);
            AddDirectoryRule(security, SystemSid, FileSystemRights.FullControl);
            AddDirectoryRule(security, AdministratorsSid, FileSystemRights.FullControl);

            if (kind == ProtectedDirectoryKind.Inventory && !string.IsNullOrEmpty(readOnlySid))
            {
                AddDirectoryRule(
                    security,
                    new SecurityIdentifier(readOnlySid),
                    FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory | FileSystemRights.Read);
            }
            if (kind == ProtectedDirectoryKind.Profile)
            {
                if (string.IsNullOrEmpty(serviceSid))
                {
                    throw new InvalidDataException("Profile directory requires an exact service SID.");
                }
                AddDirectoryRule(
                    security,
                    new SecurityIdentifier(serviceSid),
                    FileSystemRights.Modify | FileSystemRights.Synchronize);
            }

            new DirectoryInfo(path).SetAccessControl(security);
        }

        private static void AddDirectoryRule(
            DirectorySecurity security,
            SecurityIdentifier sid,
            FileSystemRights rights)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        private static string NormalizeDirectory(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        internal static bool IsUnderRoot(string path, string root)
        {
            string normalizedPath = Path.GetFullPath(path);
            string normalizedRoot = NormalizeDirectory(root);
            return string.Equals(
                    normalizedPath.TrimEnd(Path.DirectorySeparatorChar),
                    normalizedRoot.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasReparseComponent(string path, string stopRoot)
        {
            try
            {
                string current = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                string root = string.IsNullOrEmpty(stopRoot)
                    ? Path.GetPathRoot(current).TrimEnd(Path.DirectorySeparatorChar)
                    : Path.GetFullPath(stopRoot).TrimEnd(Path.DirectorySeparatorChar);
                while (IsUnderRoot(current, root))
                {
                    if ((File.Exists(current) || Directory.Exists(current)) &&
                        (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                    if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    current = Path.GetDirectoryName(current);
                    if (string.IsNullOrEmpty(current))
                    {
                        break;
                    }
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

        internal static bool IsSecurityProtected(
            FileSystemSecurity security,
            string allowedWriterSid)
        {
            SecurityIdentifier owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null || !IsPrivilegedSid(owner.Value, allowedWriterSid))
            {
                return false;
            }

            AuthorizationRuleCollection rules = security.GetAccessRules(
                true,
                true,
                typeof(SecurityIdentifier));
            for (int index = 0; index < rules.Count; index++)
            {
                FileSystemAccessRule rule = rules[index] as FileSystemAccessRule;
                if (rule == null ||
                    rule.AccessControlType != AccessControlType.Allow ||
                    (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0 ||
                    (rule.FileSystemRights & UnsafeRights) == 0)
                {
                    continue;
                }

                SecurityIdentifier sid = rule.IdentityReference as SecurityIdentifier;
                if (sid == null || !IsPrivilegedSid(sid.Value, allowedWriterSid))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsPrivilegedSid(string sid, string allowedWriterSid)
        {
            return string.Equals(sid, SystemSid.Value, StringComparison.Ordinal) ||
                string.Equals(sid, AdministratorsSid.Value, StringComparison.Ordinal) ||
                string.Equals(sid, TrustedInstallerSid.Value, StringComparison.Ordinal) ||
                (!string.IsNullOrEmpty(allowedWriterSid) &&
                 string.Equals(sid, allowedWriterSid, StringComparison.Ordinal));
        }
    }
}
