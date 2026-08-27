namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayPlanItem
    {
        public LmGatewayPlanItem()
        {
            ServiceValidation = new ValidationResult();
            BindingValidation = new ValidationResult();
            Action = LmGatewayPlanAction.Blocked;
        }

        public LmGatewayKkt Kkt { get; set; }
        public ManagedLmServiceSpec Spec { get; set; }
        public LmGatewayPlanAction Action { get; set; }
        public ValidationResult ServiceValidation { get; private set; }
        public ValidationResult BindingValidation { get; private set; }

        public bool IsValid
        {
            get
            {
                return Spec != null &&
                    Action != LmGatewayPlanAction.Blocked &&
                    ServiceValidation.IsValid &&
                    BindingValidation.IsValid;
            }
        }
    }
}
