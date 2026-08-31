using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public interface IKktConnectionLease : IDisposable
    {
        KktConnectionIdentity Identity { get; }
    }

    public interface IKktConnectionProvider
    {
        IList<KktConnectionPort> EnumeratePorts();

        Task<IKktConnectionLease> OpenAsync(
            KktConnectionPort port,
            CancellationToken cancellationToken);
    }
}
