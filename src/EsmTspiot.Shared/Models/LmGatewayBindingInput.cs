namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayBindingInput
    {
        public string KktSerial { get; set; }
        public string KktInn { get; set; }
        public string ControllerAddress { get; set; }
        public string ControllerGrpcPort { get; set; }
        public string ExpectedLmAddress { get; set; }
        public string ExpectedLmPort { get; set; }
    }
}
