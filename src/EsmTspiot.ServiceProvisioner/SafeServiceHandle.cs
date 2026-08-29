using System;
using Microsoft.Win32.SafeHandles;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly Func<IntPtr, bool> _release;

        private SafeServiceHandle()
            : base(true)
        {
            _release = WindowsServiceApi.CloseNativeServiceHandle;
        }

        internal SafeServiceHandle(IntPtr handle)
            : this(handle, WindowsServiceApi.CloseNativeServiceHandle)
        {
        }

        private SafeServiceHandle(IntPtr handle, Func<IntPtr, bool> release)
            : base(true)
        {
            if (release == null)
            {
                throw new ArgumentNullException("release");
            }
            _release = release;
            SetHandle(handle);
        }

        internal static SafeServiceHandle CreateForTesting(
            IntPtr handle,
            Func<IntPtr, bool> release)
        {
            return new SafeServiceHandle(handle, release);
        }

        protected override bool ReleaseHandle()
        {
            return _release(handle);
        }
    }
}
