using System;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EsmTspiot.WindowsSecurity
{
    internal static class ProtectedAclPolicy
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
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership |
            FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles;

        internal static bool IsProtected(
            FileSystemSecurity security,
            string allowedWriterSid)
        {
            IList<string> allowed = string.IsNullOrEmpty(allowedWriterSid)
                ? null
                : new[] { allowedWriterSid };
            return IsProtectedForWriters(security, allowed);
        }

        internal static bool IsProtectedForWriters(
            FileSystemSecurity security,
            IList<string> allowedWriterSids)
        {
            if (security == null)
            {
                return false;
            }

            SecurityIdentifier owner =
                security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null || !IsPrivilegedSid(owner.Value, allowedWriterSids))
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
                if (sid == null || !IsPrivilegedSid(sid.Value, allowedWriterSids))
                {
                    return false;
                }
            }
            return true;
        }

        // Убирает явные разрешения на изменение, которые не прошли бы
        // IsProtectedForWriters: так бывает после «Продолжить» в Проводнике.
        // Службам (S-1-5-80-*) права выдаёт сама программа — их не трогаем.
        // Только снимает права, ничего не добавляет. Возвращает число снятых.
        internal static int RemoveUnsafeWriters(FileSystemSecurity security)
        {
            AuthorizationRuleCollection rules = security.GetAccessRules(
                true,
                false,
                typeof(SecurityIdentifier));
            int removed = 0;
            for (int index = 0; index < rules.Count; index++)
            {
                FileSystemAccessRule rule = rules[index] as FileSystemAccessRule;
                SecurityIdentifier sid = rule == null
                    ? null
                    : rule.IdentityReference as SecurityIdentifier;
                if (sid == null ||
                    rule.AccessControlType != AccessControlType.Allow ||
                    (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0 ||
                    (rule.FileSystemRights & UnsafeRights) == 0 ||
                    IsPrivilegedSid(sid.Value, null) ||
                    sid.Value.StartsWith("S-1-5-80-", StringComparison.Ordinal))
                {
                    continue;
                }
                security.RemoveAccessRuleSpecific(rule);
                removed++;
            }
            return removed;
        }

        private static bool IsPrivilegedSid(
            string sid,
            IList<string> allowedWriterSids)
        {
            if (string.Equals(sid, SystemSid.Value, StringComparison.Ordinal) ||
                string.Equals(sid, AdministratorsSid.Value, StringComparison.Ordinal) ||
                string.Equals(sid, TrustedInstallerSid.Value, StringComparison.Ordinal))
            {
                return true;
            }
            if (allowedWriterSids == null)
            {
                return false;
            }
            for (int index = 0; index < allowedWriterSids.Count; index++)
            {
                if (!string.IsNullOrEmpty(allowedWriterSids[index]) &&
                    string.Equals(
                        sid,
                        allowedWriterSids[index],
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
