using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using EsmTspiot.Shared.Models;
using EsmTspiot.WindowsSecurity;

namespace EsmTspiot.ServiceProvisioner
{
    internal enum ProtectedDirectoryKind
    {
        Inventory = 1,
        Profile = 2,
        Operations = 3,
        InstallerStaging = 4,
        ProfileContainer = 5,
        Runtime = 6,
        ProfileConfiguration = 7,
        RuntimeContainer = 8
    }

    internal interface IPathSafety
    {
        ValidationResult Validate(string path, string requiredRoot);
        ValidationResult ValidateProtected(string path, string requiredRoot, string allowedWriterSid);
        ValidationResult ValidateProtectedForServices(
            string path,
            string requiredRoot,
            IList<string> allowedWriterSids);
        void EnsureProtectedDirectory(
            string path,
            ProtectedDirectoryKind kind,
            string readOnlySid,
            string serviceSid);
        void EnsureProtectedDirectoryForServices(
            string path,
            ProtectedDirectoryKind kind,
            string readOnlySid,
            IList<string> serviceSids);
        void EnsureProtectedRuntimeFile(string path);
        void EnsureProtectedReadOnlyFile(
            string path,
            IList<string> readOnlySids);
    }

    internal sealed class PathSafety : IPathSafety
    {
        private static readonly SecurityIdentifier SystemSid =
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier AdministratorsSid =
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier AllServicesSid =
            new SecurityIdentifier("S-1-5-80-0");

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
            IList<string> allowed = string.IsNullOrEmpty(allowedWriterSid)
                ? null
                : new[] { allowedWriterSid };
            return ValidateProtectedForServices(path, requiredRoot, allowed);
        }

        public ValidationResult ValidateProtectedForServices(
            string path,
            string requiredRoot,
            IList<string> allowedWriterSids)
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
                    if (security != null &&
                        !IsSecurityProtectedForServices(
                            security,
                            allowedWriterSids))
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
            IList<string> serviceSids = string.IsNullOrEmpty(serviceSid)
                ? null
                : new[] { serviceSid };
            EnsureProtectedDirectoryForServices(
                path,
                kind,
                readOnlySid,
                serviceSids);
        }

        public void EnsureProtectedDirectoryForServices(
            string path,
            ProtectedDirectoryKind kind,
            string readOnlySid,
            IList<string> serviceSids)
        {
            Directory.CreateDirectory(path);
            IList<SecurityIdentifier> preservedTraverseSids = ReadSafeTraverseSids(path);
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
                if (serviceSids == null || serviceSids.Count == 0)
                {
                    throw new InvalidDataException(
                        "Profile directory requires exact service SIDs.");
                }
                for (int index = 0; index < serviceSids.Count; index++)
                {
                    AddDirectoryRule(
                        security,
                        ParseExactServiceSid(serviceSids[index]),
                        FileSystemRights.Modify | FileSystemRights.Synchronize);
                }
            }
            if (kind == ProtectedDirectoryKind.ProfileConfiguration)
            {
                if (serviceSids == null || serviceSids.Count == 0)
                {
                    throw new InvalidDataException(
                        "Profile configuration requires exact service SIDs.");
                }
                for (int index = 0; index < serviceSids.Count; index++)
                {
                    AddDirectoryRule(
                        security,
                        ParseExactServiceSid(serviceSids[index]),
                        FileSystemRights.ReadAndExecute |
                            FileSystemRights.ListDirectory |
                            FileSystemRights.Read |
                            FileSystemRights.Synchronize);
                }
            }
            if (kind == ProtectedDirectoryKind.ProfileContainer)
            {
                if (serviceSids == null || serviceSids.Count == 0)
                {
                    throw new InvalidDataException(
                        "Profile container requires exact service SIDs.");
                }
            }
            if (kind == ProtectedDirectoryKind.Runtime)
            {
                AddDirectoryRule(
                    security,
                    AllServicesSid,
                    FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize);
            }
            if (kind == ProtectedDirectoryKind.RuntimeContainer)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    AllServicesSid,
                    FileSystemRights.Traverse | FileSystemRights.Synchronize,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }
            if (serviceSids != null)
            {
                for (int index = 0; index < serviceSids.Count; index++)
                {
                    SecurityIdentifier sid = ParseExactServiceSid(serviceSids[index]);
                    if (!ContainsSid(preservedTraverseSids, sid.Value))
                    {
                        preservedTraverseSids.Add(sid);
                    }
                }
            }
            for (int index = 0; index < preservedTraverseSids.Count; index++)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    preservedTraverseSids[index],
                    FileSystemRights.Traverse | FileSystemRights.Synchronize,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            new DirectoryInfo(path).SetAccessControl(security);
        }

        public void EnsureProtectedRuntimeFile(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Runtime file was not found.", fullPath);
            }
            FileSecurity security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(AdministratorsSid);
            AddFileRule(security, SystemSid, FileSystemRights.FullControl);
            AddFileRule(security, AdministratorsSid, FileSystemRights.FullControl);
            AddFileRule(
                security,
                AllServicesSid,
                FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize);
            new FileInfo(fullPath).SetAccessControl(security);
        }

        public void EnsureProtectedReadOnlyFile(
            string path,
            IList<string> readOnlySids)
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "Protected read-only file was not found.",
                    fullPath);
            }
            FileSecurity security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(AdministratorsSid);
            AddFileRule(security, SystemSid, FileSystemRights.FullControl);
            AddFileRule(security, AdministratorsSid, FileSystemRights.FullControl);
            if (readOnlySids != null)
            {
                for (int index = 0; index < readOnlySids.Count; index++)
                {
                    AddFileRule(
                        security,
                        ParseExactServiceSid(readOnlySids[index]),
                        FileSystemRights.ReadAndExecute |
                            FileSystemRights.Read |
                            FileSystemRights.Synchronize);
                }
            }
            new FileInfo(fullPath).SetAccessControl(security);
        }

        private static IList<SecurityIdentifier> ReadSafeTraverseSids(string path)
        {
            List<SecurityIdentifier> result = new List<SecurityIdentifier>();
            DirectorySecurity existing = new DirectoryInfo(path).GetAccessControl();
            AuthorizationRuleCollection rules = existing.GetAccessRules(
                true,
                false,
                typeof(SecurityIdentifier));
            FileSystemRights allowed = FileSystemRights.Traverse | FileSystemRights.Synchronize;
            for (int index = 0; index < rules.Count; index++)
            {
                FileSystemAccessRule rule = rules[index] as FileSystemAccessRule;
                SecurityIdentifier sid = rule == null
                    ? null
                    : rule.IdentityReference as SecurityIdentifier;
                if (rule != null && sid != null &&
                    rule.AccessControlType == AccessControlType.Allow &&
                    rule.InheritanceFlags == InheritanceFlags.None &&
                    (rule.FileSystemRights & ~allowed) == 0 &&
                    sid.Value.StartsWith("S-1-5-80-", StringComparison.Ordinal) &&
                    !ContainsSid(result, sid.Value))
                {
                    result.Add(sid);
                }
            }
            return result;
        }

        private static bool ContainsSid(IList<SecurityIdentifier> values, string sid)
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index].Value, sid, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
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
            return ProtectedAclPolicy.IsProtected(security, allowedWriterSid);
        }

        internal static bool IsSecurityProtectedForServices(
            FileSystemSecurity security,
            IList<string> allowedWriterSids)
        {
            return ProtectedAclPolicy.IsProtectedForWriters(
                security,
                allowedWriterSids);
        }

        private static SecurityIdentifier ParseExactServiceSid(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                !value.StartsWith("S-1-5-80-", StringComparison.Ordinal))
            {
                throw new InvalidDataException("An exact Windows service SID is required.");
            }
            return new SecurityIdentifier(value);
        }

        private static void AddFileRule(
            FileSecurity security,
            SecurityIdentifier sid,
            FileSystemRights rights)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                rights,
                AccessControlType.Allow));
        }
    }
}
