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
    public static class LmGatewayInfoParser
    {
        public static bool TryParse(string json, out LmGatewayInfo info)
        {
            info = null;
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

                string kktSerial = ToText(GetValue(root, "kktSerial")).Trim();
                string kktInn = ToText(GetValue(root, "kktInn")).Trim();
                if (string.IsNullOrEmpty(kktSerial) || string.IsNullOrEmpty(kktInn))
                {
                    return false;
                }

                object lmValue;
                bool hasLm = TryGetValue(root, "lm", out lmValue) && lmValue != null;
                Dictionary<string, object> lm = lmValue as Dictionary<string, object>;
                if (hasLm && lm == null)
                {
                    return false;
                }

                info = new LmGatewayInfo
                {
                    KktSerial = kktSerial,
                    KktInn = kktInn,
                    HasLmConfiguration = hasLm,
                    LmAddress = hasLm ? ToText(GetValue(lm, "ip")).Trim() : string.Empty,
                    LmPort = hasLm ? ToText(GetValue(lm, "port")).Trim() : string.Empty,
                    LmStatus = hasLm ? ToText(GetValue(lm, "status")).Trim() : string.Empty,
                    LmVersion = hasLm ? ToText(GetValue(lm, "version")).Trim() : string.Empty
                };
#else
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    string kktSerial = ToText(GetProperty(root, "kktSerial")).Trim();
                    string kktInn = ToText(GetProperty(root, "kktInn")).Trim();
                    if (string.IsNullOrEmpty(kktSerial) || string.IsNullOrEmpty(kktInn))
                    {
                        return false;
                    }

                    JsonElement lm;
                    bool hasLmProperty = TryGetProperty(root, "lm", out lm);
                    bool hasLm = hasLmProperty && lm.ValueKind != JsonValueKind.Null;
                    if (hasLm && lm.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    info = new LmGatewayInfo
                    {
                        KktSerial = kktSerial,
                        KktInn = kktInn,
                        HasLmConfiguration = hasLm,
                        LmAddress = hasLm ? ToText(GetProperty(lm, "ip")).Trim() : string.Empty,
                        LmPort = hasLm ? ToText(GetProperty(lm, "port")).Trim() : string.Empty,
                        LmStatus = hasLm ? ToText(GetProperty(lm, "status")).Trim() : string.Empty,
                        LmVersion = hasLm ? ToText(GetProperty(lm, "version")).Trim() : string.Empty
                    };
                }
#endif
            }
            catch
            {
                info = null;
                return false;
            }

            return true;
        }

#if NETFRAMEWORK
        private static bool TryGetValue(
            Dictionary<string, object> dictionary,
            string key,
            out object value)
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
            return dictionary != null && TryGetValue(dictionary, key, out value) ? value : null;
        }

        private static string ToText(object value)
        {
            return value == null
                ? string.Empty
                : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
#else
        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
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
