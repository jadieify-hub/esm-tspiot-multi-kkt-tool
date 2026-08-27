using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class RestrictedServiceSid
    {
        private const uint TokenQuery = 0x0008;
        private const int TokenRestrictedSids = 11;

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

        internal static bool CurrentTokenContains(string expectedSid)
        {
            SecurityIdentifier parsed = new SecurityIdentifier(expectedSid);
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            try
            {
                int required;
                GetTokenInformation(token, TokenRestrictedSids, IntPtr.Zero, 0, out required);
                int error = Marshal.GetLastWin32Error();
                if (required <= 0 || error != 122)
                {
                    return false;
                }
                IntPtr buffer = Marshal.AllocHGlobal(required);
                try
                {
                    if (!GetTokenInformation(
                        token,
                        TokenRestrictedSids,
                        buffer,
                        required,
                        out required))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    uint count = unchecked((uint)Marshal.ReadInt32(buffer));
                    int firstEntryOffset = Marshal.OffsetOf(
                        typeof(TokenGroupsOne),
                        "FirstGroup").ToInt32();
                    int entrySize = Marshal.SizeOf(typeof(SidAndAttributes));
                    for (uint index = 0; index < count; index++)
                    {
                        IntPtr entryPointer = IntPtr.Add(
                            buffer,
                            firstEntryOffset + checked((int)index) * entrySize);
                        SidAndAttributes entry = (SidAndAttributes)Marshal.PtrToStructure(
                            entryPointer,
                            typeof(SidAndAttributes));
                        SecurityIdentifier observed = new SecurityIdentifier(entry.Sid);
                        if (parsed.Equals(observed))
                        {
                            return true;
                        }
                    }
                    return false;
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

        private static void ValidateServiceName(string serviceName)
        {
            string serial;
            if (!EsmTspiot.Shared.Services.LmServiceIdentity.TryParseName(serviceName, out serial))
            {
                throw new ArgumentException("Managed service name is invalid.", "serviceName");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SidAndAttributes
        {
            internal IntPtr Sid;
            private uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenGroupsOne
        {
            private uint GroupCount;
            internal SidAndAttributes FirstGroup;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out IntPtr tokenHandle);

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
}
