using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class AutomaticRegistrationModeSelector
    {
        public static AutomaticRegistrationMode Select(
            BulkRegistrationDiscovery discovery)
        {
            if (discovery == null)
            {
                throw new ArgumentNullException("discovery");
            }

            if (discovery.ObservedDevices.Count == 0)
            {
                return AutomaticRegistrationMode.SequentialVcom;
            }
            if (discovery.Items.Count == 0)
            {
                return AutomaticRegistrationMode.ExistingSessions;
            }

            HashSet<string> inns = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < discovery.ObservedDevices.Count; index++)
            {
                DkktDeviceInfo device = discovery.ObservedDevices[index];
                string inn = device == null
                    ? string.Empty
                    : (device.KktInn ?? string.Empty).Trim();
                if (inn.Length > 0)
                {
                    inns.Add(inn);
                }
            }

            return inns.Count <= 1
                ? AutomaticRegistrationMode.ExistingSessions
                : AutomaticRegistrationMode.SequentialVcom;
        }
    }
}
