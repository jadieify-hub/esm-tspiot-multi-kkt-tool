using System;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class ServiceSecurityDescriptor
    {
        private const int RequiredManagementMask = 0x000F00BF;
        private const int ReadOnlyOperatorMask = 0x0002008D;
        private const string SystemSid = "S-1-5-18";
        private const string AdministratorsSid = "S-1-5-32-544";

        private ServiceSecurityDescriptor(string sddl)
        {
            if (sddl == null)
            {
                throw new ArgumentNullException("sddl");
            }
            Sddl = sddl;
            string operatorSid;
            IsRestrictive = Validate(sddl, out operatorSid);
            OperatorSid = operatorSid;
        }

        internal string Sddl { get; private set; }
        internal bool IsRestrictive { get; private set; }
        internal string OperatorSid { get; private set; }

        internal static ServiceSecurityDescriptor CreateRestrictive(string operatorSid = null)
        {
            SecurityIdentifier parsedOperator = string.IsNullOrEmpty(operatorSid)
                ? null
                : new SecurityIdentifier(operatorSid);
            if (parsedOperator != null && IsBroadUserSid(parsedOperator.Value))
            {
                throw new ArgumentException("A broad trustee cannot be the service operator.", "operatorSid");
            }
            DiscretionaryAcl dacl = new DiscretionaryAcl(false, false, parsedOperator == null ? 2 : 3);
            dacl.AddAccess(
                AccessControlType.Allow,
                new SecurityIdentifier(SystemSid),
                RequiredManagementMask,
                InheritanceFlags.None,
                PropagationFlags.None);
            dacl.AddAccess(
                AccessControlType.Allow,
                new SecurityIdentifier(AdministratorsSid),
                RequiredManagementMask,
                InheritanceFlags.None,
                PropagationFlags.None);
            if (parsedOperator != null)
            {
                dacl.AddAccess(
                    AccessControlType.Allow,
                    parsedOperator,
                    ReadOnlyOperatorMask,
                    InheritanceFlags.None,
                    PropagationFlags.None);
            }
            CommonSecurityDescriptor descriptor = new CommonSecurityDescriptor(
                false,
                false,
                ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected,
                null,
                null,
                null,
                dacl);
            return new ServiceSecurityDescriptor(descriptor.GetSddlForm(AccessControlSections.Access));
        }

        internal static ServiceSecurityDescriptor Parse(string sddl)
        {
            return new ServiceSecurityDescriptor(sddl);
        }

        internal byte[] ToBinary()
        {
            RawSecurityDescriptor descriptor = new RawSecurityDescriptor(Sddl);
            byte[] binary = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(binary, 0);
            return binary;
        }

        private static bool Validate(string sddl, out string operatorSid)
        {
            operatorSid = null;
            try
            {
                RawSecurityDescriptor descriptor = new RawSecurityDescriptor(sddl);
                if (descriptor.DiscretionaryAcl == null ||
                    (descriptor.DiscretionaryAcl.Count != 2 && descriptor.DiscretionaryAcl.Count != 3))
                {
                    return false;
                }

                bool hasSystem = false;
                bool hasAdministrators = false;
                for (int index = 0; index < descriptor.DiscretionaryAcl.Count; index++)
                {
                    CommonAce ace = descriptor.DiscretionaryAcl[index] as CommonAce;
                    if (ace == null || ace.AceQualifier != AceQualifier.AccessAllowed ||
                        ace.SecurityIdentifier == null)
                    {
                        return false;
                    }
                    if (string.Equals(ace.SecurityIdentifier.Value, SystemSid, StringComparison.Ordinal) &&
                        ace.AccessMask == RequiredManagementMask)
                    {
                        hasSystem = true;
                    }
                    else if (string.Equals(
                        ace.SecurityIdentifier.Value,
                        AdministratorsSid,
                        StringComparison.Ordinal) &&
                        ace.AccessMask == RequiredManagementMask)
                    {
                        hasAdministrators = true;
                    }
                    else if (ace.AccessMask == ReadOnlyOperatorMask &&
                        operatorSid == null &&
                        !IsBroadUserSid(ace.SecurityIdentifier.Value))
                    {
                        operatorSid = ace.SecurityIdentifier.Value;
                    }
                    else
                    {
                        return false;
                    }
                }
                return hasSystem && hasAdministrators &&
                    ((descriptor.DiscretionaryAcl.Count == 2 && operatorSid == null) ||
                     (descriptor.DiscretionaryAcl.Count == 3 && operatorSid != null));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool IsBroadUserSid(string sid)
        {
            return string.Equals(sid, "S-1-1-0", StringComparison.Ordinal) ||
                string.Equals(sid, SystemSid, StringComparison.Ordinal) ||
                string.Equals(sid, AdministratorsSid, StringComparison.Ordinal) ||
                string.Equals(sid, "S-1-5-4", StringComparison.Ordinal) ||
                string.Equals(sid, "S-1-5-11", StringComparison.Ordinal) ||
                string.Equals(sid, "S-1-5-32-545", StringComparison.Ordinal);
        }
    }
}
