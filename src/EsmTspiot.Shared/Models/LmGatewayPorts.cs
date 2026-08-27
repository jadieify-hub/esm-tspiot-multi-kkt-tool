namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayPorts
    {
        public LmGatewayPorts(int grpcPort, int restPort)
        {
            GrpcPort = grpcPort;
            RestPort = restPort;
        }

        public int GrpcPort { get; private set; }
        public int RestPort { get; private set; }
    }
}
