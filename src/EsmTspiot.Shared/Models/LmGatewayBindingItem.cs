namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayBindingItem
    {
        public LmGatewayKkt Kkt { get; set; }
        public LmGatewayBindingInput Input { get; set; }
        public ValidationResult Validation { get; set; }

        public bool IsValid
        {
            get { return Validation != null && Validation.IsValid; }
        }
    }
}
