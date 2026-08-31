using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class SequentialKktDiscovery
    {
        public SequentialKktDiscovery()
        {
            Targets = new List<SequentialKktRegistrationTarget>();
            BlockingDevices = new List<DkktDeviceInfo>();
        }

        public IList<SequentialKktRegistrationTarget> Targets { get; private set; }
        public IList<DkktDeviceInfo> BlockingDevices { get; private set; }
        public bool HasExternalSessions { get; set; }
        public string ErrorMessage { get; set; }

        public bool IsValid
        {
            get
            {
                return !HasExternalSessions &&
                    string.IsNullOrWhiteSpace(ErrorMessage) &&
                    Targets.Count > 0;
            }
        }

        public SequentialKktRegistrationTarget FindBySerial(string serial)
        {
            string expected = (serial ?? string.Empty).Trim();
            for (int index = 0; index < Targets.Count; index++)
            {
                SequentialKktRegistrationTarget target = Targets[index];
                string actual = target == null || target.Device == null
                    ? string.Empty
                    : (target.Device.KktSerial ?? string.Empty).Trim();
                if (string.Equals(actual, expected, System.StringComparison.Ordinal))
                {
                    return target;
                }
            }
            return null;
        }
    }
}
