using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class LmGatewayBindingPlan
    {
        public LmGatewayBindingPlan()
        {
            Items = new List<LmGatewayBindingItem>();
        }

        public IList<LmGatewayBindingItem> Items { get; private set; }
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
