using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class RestrictedServiceSid
    {
        internal static string Resolve(string serviceName)
        {
            ValidateServiceName(serviceName);
            SecurityIdentifier sid = (SecurityIdentifier)new NTAccount(
                "NT SERVICE",
                serviceName).Translate(typeof(SecurityIdentifier));
            string derived = Derive(serviceName);
            if (!string.Equals(sid.Value, derived, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Windows returned an unexpected service SID.");
            }
            return sid.Value;
        }

        internal static string Derive(string serviceName)
        {
            ValidateServiceName(serviceName);
            byte[] name = Encoding.Unicode.GetBytes(serviceName.ToUpperInvariant());
            byte[] digest;
            using (SHA1 algorithm = SHA1.Create())
            {
                digest = algorithm.ComputeHash(name);
            }

            StringBuilder sid = new StringBuilder("S-1-5-80");
            for (int offset = 0; offset < digest.Length; offset += 4)
            {
                uint subAuthority = unchecked(
                    (uint)digest[offset] |
                    ((uint)digest[offset + 1] << 8) |
                    ((uint)digest[offset + 2] << 16) |
                    ((uint)digest[offset + 3] << 24));
                sid.Append('-');
                sid.Append(subAuthority.ToString(CultureInfo.InvariantCulture));
            }
            return sid.ToString();
        }

        private static void ValidateServiceName(string serviceName)
        {
            string serial;
            if (!EsmTspiot.Shared.Services.LmServiceIdentity.TryParseName(
                    serviceName,
                    out serial) &&
                !LocalModuleServiceIdentity.IsManagedName(serviceName))
            {
                throw new ArgumentException("Managed service name is invalid.", "serviceName");
            }
        }
    }
}
