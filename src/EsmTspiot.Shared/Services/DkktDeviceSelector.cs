using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class DkktDeviceSelector
    {
        public static IList<DkktDeviceInfo> FindDevicesWithoutInstances(
            IList<DkktDeviceInfo> devices,
            IList<KktInstanceInfo> instances)
        {
            List<DkktDeviceInfo> candidates = new List<DkktDeviceInfo>();
            if (devices == null)
            {
                return candidates;
            }

            for (int i = 0; i < devices.Count; i++)
            {
                DkktDeviceInfo device = devices[i];
                if (device == null || string.IsNullOrWhiteSpace(device.KktSerial))
                {
                    continue;
                }

                if (!ContainsInstanceId(instances, device.KktSerial))
                {
                    candidates.Add(device);
                }
            }

            return candidates;
        }

        private static bool ContainsInstanceId(IList<KktInstanceInfo> instances, string kktSerial)
        {
            if (instances == null)
            {
                return false;
            }

            for (int i = 0; i < instances.Count; i++)
            {
                if (string.Equals(instances[i].Id, kktSerial, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
