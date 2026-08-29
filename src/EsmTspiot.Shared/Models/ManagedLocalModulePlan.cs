using System;
using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class ManagedLocalModulePlan
    {
        public ManagedLocalModulePlan()
        {
            Items = new List<ManagedLocalModulePlanItem>();
            ValidationMessages = new List<string>();
        }

        public IList<ManagedLocalModulePlanItem> Items { get; private set; }
        public IList<string> ValidationMessages { get; private set; }

        public bool IsValid
        {
            get
            {
                if (ValidationMessages.Count > 0)
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

        public ManagedLocalModulePlanItem FindByInn(string inn)
        {
            string expected = inn == null ? string.Empty : inn.Trim();
            for (int index = 0; index < Items.Count; index++)
            {
                ManagedLocalModulePlanItem item = Items[index];
                if (item != null && item.Module != null &&
                    string.Equals(item.Module.Inn, expected, StringComparison.Ordinal))
                {
                    return item;
                }
            }
            return null;
        }
    }
}
