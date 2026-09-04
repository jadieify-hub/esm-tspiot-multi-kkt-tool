using System;
using System.Threading;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class GlobalOperationMutex
    {
        internal static IDisposable Acquire(string name)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                !name.StartsWith("Global\\KRS.MultiKKT.", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A canonical global operation mutex is required.",
                    "name");
            }
            Mutex mutex = new Mutex(false, name);
            try
            {
                try
                {
                    mutex.WaitOne();
                }
                catch (AbandonedMutexException)
                {
                }
                return new MutexLease(mutex);
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
        }

        private sealed class MutexLease : IDisposable
        {
            private Mutex _mutex;

            internal MutexLease(Mutex mutex)
            {
                _mutex = mutex;
            }

            public void Dispose()
            {
                Mutex mutex = _mutex;
                _mutex = null;
                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                }
            }
        }
    }
}
