using System;
using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class DirectControllerServiceInventoryItem
    {
        public string ServiceName { get; set; }
        public string KktSerial { get; set; }
        public bool IsOwned { get; set; }
        public bool IsVerifiedOfficial { get; set; }
    }

    public sealed class DirectControllerPlan
    {
        public DirectControllerPlan()
        {
            Assignments = new List<DirectControllerAssignment>();
            ValidationMessages = new List<string>();
        }

        public IList<DirectControllerAssignment> Assignments { get; private set; }
        public IList<string> ValidationMessages { get; private set; }

        public bool IsValid
        {
            get { return ValidationMessages.Count == 0; }
        }

        public DirectControllerAssignment FindBySerial(string kktSerial)
        {
            string expected = kktSerial == null ? string.Empty : kktSerial.Trim();
            for (int index = 0; index < Assignments.Count; index++)
            {
                DirectControllerAssignment assignment = Assignments[index];
                if (assignment != null && string.Equals(
                        assignment.KktSerial,
                        expected,
                        StringComparison.Ordinal))
                {
                    return assignment;
                }
            }
            return null;
        }
    }
}
