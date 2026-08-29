using System;
using System.Collections.Generic;
using System.Globalization;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.Shared.Services
{
    public static class LmGatewayBindingPlanner
    {
        public static LmGatewayBindingPlan Build(
            LmGatewayDiscovery discovery,
            IList<LmGatewayBindingInput> inputs)
        {
            LmGatewayBindingPlan plan = new LmGatewayBindingPlan();
            if (discovery == null || !discovery.IsSuccessful)
            {
                plan.ErrorMessage = discovery == null || string.IsNullOrWhiteSpace(discovery.ErrorMessage)
                    ? "Нет корректных результатов обнаружения ККТ."
                    : discovery.ErrorMessage;
                return plan;
            }

            Dictionary<string, List<LmGatewayBindingInput>> inputsBySerial =
                BuildInputIndex(inputs);

            for (int index = 0; index < discovery.Items.Count; index++)
            {
                LmGatewayKkt kkt = CopyKkt(discovery.Items[index]);
                string serial = (kkt.KktSerial ?? string.Empty).Trim();
                List<LmGatewayBindingInput> matches;
                if (!inputsBySerial.TryGetValue(serial, out matches) || matches.Count == 0)
                {
                    ValidationResult missingValidation = new ValidationResult();
                    missingValidation.Add("Для ККТ не заданы параметры локального контроллера ЛМ.");
                    plan.Items.Add(new LmGatewayBindingItem
                    {
                        Kkt = kkt,
                        Input = new LmGatewayBindingInput
                        {
                            KktSerial = serial,
                            KktInn = (kkt.KktInn ?? string.Empty).Trim(),
                            ControllerAddress = string.Empty,
                            ControllerGrpcPort = string.Empty
                        },
                        Validation = missingValidation
                    });
                    continue;
                }

                LmGatewayBindingInput input = matches[0];
                ValidationResult validation = LmGatewayInputValidator.ValidateBinding(input);
                if (matches.Count > 1)
                {
                    validation.Add("Для одной ККТ задано несколько наборов параметров контроллера ЛМ.");
                }
                if (!string.Equals(
                    (kkt.KktInn ?? string.Empty).Trim(),
                    input.KktInn,
                    StringComparison.Ordinal))
                {
                    validation.Add("ИНН в параметрах привязки не совпадает с ИНН зарегистрированной ККТ.");
                }

                plan.Items.Add(new LmGatewayBindingItem
                {
                    Kkt = kkt,
                    Input = input,
                    Validation = validation
                });
            }

            MarkDuplicateEndpoints(plan.Items);
            return plan;
        }

        private static Dictionary<string, List<LmGatewayBindingInput>> BuildInputIndex(
            IList<LmGatewayBindingInput> inputs)
        {
            Dictionary<string, List<LmGatewayBindingInput>> result =
                new Dictionary<string, List<LmGatewayBindingInput>>(StringComparer.Ordinal);
            if (inputs == null)
            {
                return result;
            }

            for (int index = 0; index < inputs.Count; index++)
            {
                LmGatewayBindingInput normalized = CopyAndNormalizeInput(inputs[index]);
                string serial = normalized.KktSerial;
                List<LmGatewayBindingInput> matches;
                if (!result.TryGetValue(serial, out matches))
                {
                    matches = new List<LmGatewayBindingInput>();
                    result.Add(serial, matches);
                }

                matches.Add(normalized);
            }

            return result;
        }

        private static LmGatewayBindingInput CopyAndNormalizeInput(LmGatewayBindingInput source)
        {
            string address = source == null ? string.Empty : source.ControllerAddress;
            string normalizedAddress;
            LmGatewayInputValidator.TryNormalizeControllerAddress(address, out normalizedAddress);

            string portText = source == null || source.ControllerGrpcPort == null
                ? string.Empty
                : source.ControllerGrpcPort.Trim();
            int port;
            if (int.TryParse(portText, out port) && port >= 1 && port <= 65535)
            {
                portText = port.ToString(CultureInfo.InvariantCulture);
            }

            return new LmGatewayBindingInput
            {
                KktSerial = source == null || source.KktSerial == null ? string.Empty : source.KktSerial.Trim(),
                KktInn = source == null || source.KktInn == null ? string.Empty : source.KktInn.Trim(),
                ControllerAddress = normalizedAddress,
                ControllerGrpcPort = portText,
                ExpectedLmAddress = source == null ? string.Empty : Trim(source.ExpectedLmAddress),
                ExpectedLmPort = source == null ? string.Empty : Trim(source.ExpectedLmPort)
            };
        }

        private static LmGatewayKkt CopyKkt(LmGatewayKkt source)
        {
            if (source == null)
            {
                return new LmGatewayKkt();
            }

            return new LmGatewayKkt
            {
                InstanceId = Trim(source.InstanceId),
                KktSerial = Trim(source.KktSerial),
                KktInn = Trim(source.KktInn),
                FnSerial = Trim(source.FnSerial),
                Port = Trim(source.Port),
                SoftPort = Trim(source.SoftPort),
                DkktPort = Trim(source.DkktPort),
                ServiceState = Trim(source.ServiceState)
            };
        }

        private static void MarkDuplicateEndpoints(IList<LmGatewayBindingItem> items)
        {
            Dictionary<string, List<LmGatewayBindingItem>> endpointGroups =
                new Dictionary<string, List<LmGatewayBindingItem>>(StringComparer.Ordinal);
            for (int index = 0; index < items.Count; index++)
            {
                LmGatewayBindingItem item = items[index];
                string normalizedAddress;
                int port;
                if (item == null || item.Input == null ||
                    !LmGatewayInputValidator.TryNormalizeControllerAddress(
                        item.Input.ControllerAddress,
                        out normalizedAddress) ||
                    !int.TryParse(item.Input.ControllerGrpcPort, out port) ||
                    port < 1 || port > 65535)
                {
                    continue;
                }

                string endpoint = normalizedAddress + ":" + port.ToString(CultureInfo.InvariantCulture);
                List<LmGatewayBindingItem> group;
                if (!endpointGroups.TryGetValue(endpoint, out group))
                {
                    group = new List<LmGatewayBindingItem>();
                    endpointGroups.Add(endpoint, group);
                }

                group.Add(item);
            }

            foreach (KeyValuePair<string, List<LmGatewayBindingItem>> pair in endpointGroups)
            {
                if (pair.Value.Count < 2)
                {
                    continue;
                }

                for (int index = 0; index < pair.Value.Count; index++)
                {
                    pair.Value[index].Validation.Add(
                        "Локальный endpoint контроллера ЛМ " + pair.Key + " назначен нескольким ККТ.");
                }
            }
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
