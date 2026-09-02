using System;
using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class LocalModuleMsiPlan
    {
        public LocalModuleMsiPlan()
        {
            Assignments = new List<LocalModuleMsiAssignment>();
            ValidationMessages = new List<string>();
        }

        public IList<LocalModuleMsiAssignment> Assignments { get; private set; }
        public IList<string> ValidationMessages { get; private set; }

        public bool IsValid
        {
            get { return ValidationMessages.Count == 0; }
        }

        public LocalModuleMsiAssignment FindByInn(string inn)
        {
            string expected = inn == null ? string.Empty : inn.Trim();
            for (int index = 0; index < Assignments.Count; index++)
            {
                if (Assignments[index] != null && string.Equals(
                        Assignments[index].Inn,
                        expected,
                        StringComparison.Ordinal))
                {
                    return Assignments[index];
                }
            }
            return null;
        }

        public IDictionary<string, int> CreateTargetApiPortMap()
        {
            Dictionary<string, int> result =
                new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < Assignments.Count; index++)
            {
                LocalModuleMsiAssignment assignment = Assignments[index];
                if (assignment != null)
                {
                    result.Add(assignment.Inn, assignment.ApiPort);
                }
            }
            return result;
        }
    }
}
