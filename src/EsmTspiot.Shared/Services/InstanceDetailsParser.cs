using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;

#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace EsmTspiot.Shared.Services
{
    public static class InstanceDetailsParser
    {
        public static bool TryParse(string json, out KktInstanceDetails details)
        {
            details = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
#if NETFRAMEWORK
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null)
                {
                    return false;
                }

                object clientPort;
                object state;
                object regDataValue;
                bool hasClientPort = TryGetValue(root, "clientPort", out clientPort);
                TryGetValue(root, "state", out state);
                bool hasRegData = TryGetValue(root, "regData", out regDataValue);
                if (!hasClientPort && !hasRegData)
                {
                    return false;
                }

                Dictionary<string, object> regData = regDataValue as Dictionary<string, object>;
                if (hasRegData && regDataValue != null && regData == null)
                {
                    return false;
                }
                if (regData != null && !HasCompleteRegistrationData(regData))
                {
                    return false;
                }

                details = new KktInstanceDetails
                {
                    State = ToText(state).Trim(),
                    ClientPort = ToText(clientPort),
                    RegistrationData = ParseRegistrationData(regData)
                };
#else
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    JsonElement clientPort;
                    JsonElement state;
                    JsonElement regData;
                    bool hasClientPort = TryGetProperty(root, "clientPort", out clientPort);
                    TryGetProperty(root, "state", out state);
                    bool hasRegData = TryGetProperty(root, "regData", out regData);
                    if (!hasClientPort && !hasRegData)
                    {
                        return false;
                    }

                    if (hasRegData && regData.ValueKind != JsonValueKind.Null && regData.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }
                    if (hasRegData && regData.ValueKind == JsonValueKind.Object && !HasCompleteRegistrationData(regData))
                    {
                        return false;
                    }

                    details = new KktInstanceDetails
                    {
                        State = ToText(state).Trim(),
                        ClientPort = ToText(clientPort),
                        RegistrationData = hasRegData && regData.ValueKind == JsonValueKind.Object
                            ? ParseRegistrationData(regData)
                            : null
                    };
                }
#endif
            }
            catch
            {
                details = null;
                return false;
            }

            return true;
        }

#if NETFRAMEWORK
        private static KktRegistrationData ParseRegistrationData(Dictionary<string, object> data)
        {
            if (data == null)
            {
                return null;
            }

            return new KktRegistrationData
            {
                KktSerial = ToText(GetValue(data, "kktSerial")).Trim(),
                FnSerial = ToText(GetValue(data, "fnSerial")).Trim(),
                KktInn = ToText(GetValue(data, "kktInn")).Trim()
            };
        }

        private static bool HasCompleteRegistrationData(Dictionary<string, object> data)
        {
            return !string.IsNullOrWhiteSpace(ToText(GetValue(data, "kktSerial"))) &&
                !string.IsNullOrWhiteSpace(ToText(GetValue(data, "fnSerial"))) &&
                !string.IsNullOrWhiteSpace(ToText(GetValue(data, "kktInn")));
        }

        private static bool TryGetValue(Dictionary<string, object> dictionary, string key, out object value)
        {
            foreach (KeyValuePair<string, object> pair in dictionary)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static object GetValue(Dictionary<string, object> dictionary, string key)
        {
            object value;
            return TryGetValue(dictionary, key, out value) ? value : null;
        }

        private static string ToText(object value)
        {
            return value == null ? string.Empty : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
#else
        private static KktRegistrationData ParseRegistrationData(JsonElement data)
        {
            return new KktRegistrationData
            {
                KktSerial = ToText(GetProperty(data, "kktSerial")).Trim(),
                FnSerial = ToText(GetProperty(data, "fnSerial")).Trim(),
                KktInn = ToText(GetProperty(data, "kktInn")).Trim()
            };
        }

        private static bool HasCompleteRegistrationData(JsonElement data)
        {
            return !string.IsNullOrWhiteSpace(ToText(GetProperty(data, "kktSerial"))) &&
                !string.IsNullOrWhiteSpace(ToText(GetProperty(data, "fnSerial"))) &&
                !string.IsNullOrWhiteSpace(ToText(GetProperty(data, "kktInn")));
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default(JsonElement);
            return false;
        }

        private static JsonElement GetProperty(JsonElement element, string name)
        {
            JsonElement value;
            return TryGetProperty(element, name, out value) ? value : default(JsonElement);
        }

        private static string ToText(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
            {
                return string.Empty;
            }

            return element.ValueKind == JsonValueKind.String
                ? (element.GetString() ?? string.Empty)
                : element.ToString();
        }
#endif
    }
}
