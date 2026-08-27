namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayTarget
    {
        public LmGatewayTarget(string address, int port)
        {
            Address = address;
            Port = port;
        }

        public string Address { get; private set; }
        public int Port { get; private set; }
    }
}
