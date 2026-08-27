using System;

namespace EsmTspiot.Shared.Models
{
    public sealed class LmManagedPortPolicy
    {
        public LmManagedPortPolicy(TcpPortRange grpcPorts, TcpPortRange restPorts)
        {
            if (grpcPorts == null)
            {
                throw new ArgumentNullException("grpcPorts");
            }
            if (restPorts == null)
            {
                throw new ArgumentNullException("restPorts");
            }

            GrpcPorts = grpcPorts;
            RestPorts = restPorts;
        }

        public TcpPortRange GrpcPorts { get; private set; }
        public TcpPortRange RestPorts { get; private set; }
    }
}
