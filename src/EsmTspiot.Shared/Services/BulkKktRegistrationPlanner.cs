using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.Shared.Services
{
    public static class BulkKktRegistrationPlanner
    {
        private const int PortBase = 50400;
        private const int SoftPortBase = 51400;
        private const int MaximumPairIndex = 1000;

        public static BulkKktRegistrationPlan Build(
            string baseUrl,
            string dkktPort,
            IList<DkktDeviceInfo> devices,
            IList<KktInstanceInfo> instances)
        {
            BulkKktRegistrationPlan plan = new BulkKktRegistrationPlan();
            HashSet<string> existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<int> occupiedIndexes = new HashSet<int>();
            HashSet<string> seenDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (instances != null)
            {
                for (int i = 0; i < instances.Count; i++)
                {
                    KktInstanceInfo instance = instances[i];
                    if (instance == null)
                    {
                        continue;
                    }

                    string id = (instance.Id ?? string.Empty).Trim();
                    if (id.Length > 0)
                    {
                        existingIds.Add(id);
                    }

                    ReservePairIndex(instance.Port, PortBase, occupiedIndexes);
                    ReservePairIndex(instance.SoftPort, SoftPortBase, occupiedIndexes);
                }
            }

            if (devices == null)
            {
                return plan;
            }

            int nextIndex = 1;
            for (int i = 0; i < devices.Count; i++)
            {
                DkktDeviceInfo device = devices[i];
                if (device == null)
                {
                    continue;
                }

                string serial = (device.KktSerial ?? string.Empty).Trim();
                if (!seenDeviceIds.Add(serial))
                {
                    plan.DuplicateDevices.Add(device);
                    continue;
                }

                if (existingIds.Contains(serial))
                {
                    plan.ExistingDevices.Add(device);
                    continue;
                }

                while (occupiedIndexes.Contains(nextIndex))
                {
                    nextIndex++;
                }

                if (nextIndex > MaximumPairIndex)
                {
                    TspiotFormInput blockedInput = new TspiotFormInput
                    {
                        BaseUrl = baseUrl,
                        KktSerial = serial,
                        FnSerial = (device.FnSerial ?? string.Empty).Trim(),
                        KktInn = (device.KktInn ?? string.Empty).Trim(),
                        Port = string.Empty,
                        SoftPort = string.Empty,
                        DkktPort = dkktPort
                    };
                    ValidationResult blockedValidation = TspiotInputValidator.ValidatePut(blockedInput, true);
                    blockedValidation.Add("Не найдено свободных пар портов ЕСМ в поддерживаемом диапазоне.");
                    plan.Items.Add(new BulkKktRegistrationItem
                    {
                        Device = device,
                        Input = blockedInput,
                        Validation = blockedValidation
                    });
                    continue;
                }

                TspiotFormInput input = new TspiotFormInput
                {
                    BaseUrl = baseUrl,
                    KktSerial = serial,
                    FnSerial = (device.FnSerial ?? string.Empty).Trim(),
                    KktInn = (device.KktInn ?? string.Empty).Trim(),
                    Port = (PortBase + nextIndex).ToString(),
                    SoftPort = (SoftPortBase + nextIndex).ToString(),
                    DkktPort = dkktPort
                };

                plan.Items.Add(new BulkKktRegistrationItem
                {
                    Device = device,
                    Input = input,
                    Validation = TspiotInputValidator.ValidatePut(input, true)
                });

                occupiedIndexes.Add(nextIndex);
                nextIndex++;
            }

            return plan;
        }

        private static void ReservePairIndex(string value, int portBase, ISet<int> occupiedIndexes)
        {
            int port;
            if (!int.TryParse(value, out port))
            {
                return;
            }

            int index = port - portBase;
            if (index >= 1 && index <= MaximumPairIndex)
            {
                occupiedIndexes.Add(index);
            }
        }
    }
}
