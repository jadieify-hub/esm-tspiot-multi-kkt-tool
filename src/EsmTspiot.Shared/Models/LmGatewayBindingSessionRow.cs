namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayBindingSessionRow
    {
        public LmGatewayKkt Kkt { get; internal set; }
        public bool IsSelected { get; internal set; }
        public string ControllerAddress { get; internal set; }
        public string ControllerGrpcPort { get; internal set; }
        public LmGatewayBindingStatus? LastBindingStatus { get; internal set; }
        public string LastMessage { get; internal set; }
    }
}
