using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class ManagedLocalModulePlanItem
    {
        public ManagedLocalModulePlanItem()
        {
            KktAssignments = new List<ManagedKktAssignment>();
            ValidationMessages = new List<string>();
        }

        public ManagedLocalModuleAssignment Module { get; set; }
        public IList<ManagedKktAssignment> KktAssignments { get; private set; }
        public IList<string> ValidationMessages { get; private set; }

        public bool IsValid
        {
            get { return ValidationMessages.Count == 0; }
        }

        public string JoinValidationMessages()
        {
            return string.Join("; ", ValidationMessages);
        }
    }
}
