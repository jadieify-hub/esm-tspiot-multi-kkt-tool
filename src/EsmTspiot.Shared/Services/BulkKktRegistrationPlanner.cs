using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.Shared.Services
{
    public static class BulkKktRegistrationPlanner
    {
        public static BulkKktRegistrationPlan Build(
            string baseUrl,
            string dkktPort,
            IList<DkktDeviceInfo> devices,
            IList<KktInstanceInfo> instances)
        {
            BulkKktRegistrationPlan plan = new BulkKktRegistrationPlan();
            HashSet<string> existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            KktPortPairAllocator portAllocator = new KktPortPairAllocator(instances);

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
                }
            }

            if (devices == null)
            {
                return plan;
            }

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

                KktPortPair portPair = portAllocator.PeekNext();
                if (portPair == null)
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
                    Port = portPair.Port,
                    SoftPort = portPair.SoftPort,
                    DkktPort = dkktPort
                };

                ValidationResult validation = TspiotInputValidator.ValidatePut(input, true);
                if (validation.IsValid)
                {
                    portAllocator.ReserveNext();
                }

                plan.Items.Add(new BulkKktRegistrationItem
                {
                    Device = device,
                    Input = input,
                    Validation = validation
                });
            }

            return plan;
        }
    }
}
