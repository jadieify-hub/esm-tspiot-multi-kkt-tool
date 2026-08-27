namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayProbeResult
    {
        public bool ServiceRunning { get; set; }
        public bool GrpcListenerReady { get; set; }
        public bool RestListenerReady { get; set; }
        public bool ListenerOwnersVerified { get; set; }
        public string Message { get; set; }

        public bool IsReady
        {
            get
            {
                return ServiceRunning &&
                    GrpcListenerReady &&
                    RestListenerReady &&
                    ListenerOwnersVerified;
            }
        }
    }
}
