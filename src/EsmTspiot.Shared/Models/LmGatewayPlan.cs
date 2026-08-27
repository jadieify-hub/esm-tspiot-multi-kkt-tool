using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayPlan
    {
        public LmGatewayPlan()
        {
            Items = new List<LmGatewayPlanItem>();
        }

        public IList<LmGatewayPlanItem> Items { get; private set; }
        public string ErrorMessage { get; set; }

        public bool IsValid
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ErrorMessage))
                {
                    return false;
                }

                for (int index = 0; index < Items.Count; index++)
                {
                    if (Items[index] == null || !Items[index].IsValid)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
