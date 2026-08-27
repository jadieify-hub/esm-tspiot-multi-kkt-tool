using System;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.Shared.Models
{
    public sealed class ManagedLmServiceSpec
    {
        public ManagedLmServiceSpec(LmGatewayKkt kkt, LmGatewayPorts ports, LmGatewayTarget target)
        {
            if (kkt == null)
            {
                throw new ArgumentNullException("kkt");
            }
            if (ports == null)
            {
                throw new ArgumentNullException("ports");
            }
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            KktSerial = kkt.KktSerial;
            KktInn = kkt.KktInn;
            ServiceName = LmServiceIdentity.CreateName(KktSerial);
            ProfileName = ServiceName;
            Ports = new LmGatewayPorts(ports.GrpcPort, ports.RestPort);
            Target = new LmGatewayTarget(target.Address, target.Port);
        }

        public ManagedLmServiceSpec(
            string kktSerial,
            LmGatewayPorts ports,
            LmGatewayTarget target)
        {
            if (ports == null)
            {
                throw new ArgumentNullException("ports");
            }
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            KktSerial = kktSerial;
            KktInn = string.Empty;
            ServiceName = LmServiceIdentity.CreateName(KktSerial);
            ProfileName = ServiceName;
            Ports = new LmGatewayPorts(ports.GrpcPort, ports.RestPort);
            Target = new LmGatewayTarget(target.Address, target.Port);
        }

        public string KktSerial { get; private set; }
        public string KktInn { get; private set; }
        public string ServiceName { get; private set; }
        public string ProfileName { get; private set; }
        public LmGatewayPorts Ports { get; private set; }
        public LmGatewayTarget Target { get; private set; }

        public LmServiceRole Role
        {
            get { return LmServiceRole.Managed; }
        }
    }
}
