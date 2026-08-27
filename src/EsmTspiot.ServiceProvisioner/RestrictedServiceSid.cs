using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class RestrictedServiceSid
    {
        private const uint TokenQuery = 0x0008;
        private const int TokenRestrictedSids = 11;

        internal static string Resolve(string serviceName)
        {
            string serial;
            if (!EsmTspiot.Shared.Services.LmServiceIdentity.TryParseName(serviceName, out serial))
            {
                throw new ArgumentException("Managed service name is invalid.", "serviceName");
            }
            SecurityIdentifier sid = (SecurityIdentifier)new NTAccount(
                "NT SERVICE",
                serviceName).Translate(typeof(SecurityIdentifier));
            return sid.Value;
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
